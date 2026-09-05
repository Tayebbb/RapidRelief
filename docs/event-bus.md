# Event Bus (F0 — plan §8.6, D-006)

Hand-rolled in-process pub/sub — **never** add MediatR (commercial from v13, D-006). The contracts
live in `Shared/Contracts/Eventing`; the implementation is `Infrastructure/Eventing/InProcessEventBus`
(registered **scoped** in Program step 6).

## The pieces

- `IEvent` — `EventId` + `OccurredAtUtc`.
- `EventBase` — abstract record supplying both; every event derives from it.
- `IEventHandler<TEvent>` — `Task HandleAsync(TEvent evt, CancellationToken ct)`.
- `IEventBus` — `Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct)`.

Events are sealed records in `Shared/Contracts/Events` (architecture-tested — they may live nowhere
else) and are part of Contracts v1: additive-only changes (§4.6).

## What is on the bus today

| Event              | Published by                                                                                   | Handled by                                                                                                                                                                      |
| ------------------ | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `PingCreated`      | `Features/Sample` — `POST /api/sample/pings`                                                   | `PingCreatedLoggingHandler` (Sample)                                                                                                                                            |
| `AuthEvent`        | `Features/Auth` — register, login, failed login, logout, lock/unlock, role change, token reuse | `AuthEventDisconnectHandler` (Realtime): `Lock`/`RoleChange`/`TokenReuse` abort live hub sockets; all other actions are ignored, and **no** notification row is written (D-036) |
| `IncidentAssessed` | `Features/Ai` — `AiAnalysisWorker` after a successful assessment                               | `IncidentAssessedNotificationHandler` (Realtime) → topic `ai.incident.assessed` to roles Rescuer + Government                                                                   |
| `IncidentCreated`  | `Features/Incidents` — `POST /api/incidents` after the report is committed (F2, D-083)        | `IncidentCreatedHandler` (Ai) → enqueues to the bounded AI channel (D-021) · `IncidentCreatedNotificationHandler` (Incidents) → topic `incidents.report.created` to Rescuer + Government                                                    || `AlertPublished`   | `Features/Alerts` — `POST /api/alerts` after the alert row is committed (F10, D-073)           | `AlertPublishedNotificationHandler` (Realtime) → topic `alerts.published` to all                                                                                                |
| `IncidentVerified` | `Features/Incidents` — `POST /api/incidents/{id}/verify` (F2)                                  | `IncidentVerifiedAuditHandler` (Audit) → `Incident.Verify` / `Incident.Reject` on the trail                                                                                      |
| `MissionAssigned`  | `Features/Rescue` — `POST /api/rescue/missions`, `/{id}/reassign` (F5, D-084, D-094)          | `MissionAssignedProjectionHandler` (Incidents) → incident → `Assigned`, notifies the reporter                                                                                   |
| `MissionStatusChanged` | `Features/Rescue` — `/{id}/accept`, `/{id}/reject`, `/{id}/status` (F5, D-084, D-095)      | `MissionStatusProjectionHandler` (Incidents) → incident → `InProgress`/`Resolved`/back to `Verified` on cancel or reject, writes a timeline row per mission stage (D-088) and notifies the reporter |

The loop closes without a single cross-feature reference: Rescue never touches `incidents_*`, and
Incidents never reads `ai_assessments` or `rescue_*` — each slice projects what it needs from the
events it subscribes to (D-083).
`ReliefRequested` and `ReliefStatusChanged` are published by `Features/Relief` (F4) on create and on
each triage transition; the requester's notification is sent by Relief itself through the frozen
`IRealtimeNotifier`, so no subscriber is required yet.

## Notification topics

Events are the *internal* contract; topics are what a signed-in user actually receives.

| Topic | Audience | Sent when |
| --- | --- | --- |
| `alerts.published` | everyone | Government broadcasts an alert (F10) |
| `incidents.report.created` | Rescuer + Government | a report is filed (F2) |
| `ai.incident.assessed` | Rescuer + Government | the AI worker finishes triage (F8) — deliberately **not** sent to the citizen (D-089) |
| `incidents.report.status` | the reporter | verified · assigned · en route · on site · resolved (five per rescue, D-089) |
| `relief.request.status` | the requester | accepted · preparing · dispatched · delivered (four per request, D-089) |
| `rescue.mission.assigned` | the assigned team's members | a mission is assigned or reassigned to their team (F5, D-092) |
| `rescue.operations.updated` | Rescuer + Government | queue-affecting changes (assignment, rejection, completion) so open consoles refresh without waiting for the 15 s poll |

## Audit projections (F14)

`Features/Audit` is a pure subscriber plus a contract. It handles `IncidentVerified`,
`MissionAssigned`, `MissionStatusChanged`, `AlertPublished`, `ReliefStatusChanged` and the
security-only slice of `AuthEvent` (`TokenReuse`, `LoginFailed` — lock, unlock and role changes are
recorded by the admin endpoints with richer wording, so recording them here too would duplicate).

Everything a human decides directly — team create/update, shelter create/update/occupancy, resource
create/update, incident close-out, alert revoke, user lock/unlock/roles/delete — is written at the
endpoint through the frozen `IAuditTrail` contract, which carries the caller's identity from the
`HttpContext`. `IncidentCreated` is deliberately **not** audited: filing a report is not an
administrative action (D-097).

## Declaring an event

```csharp
// Shared/Contracts/Events/PingCreated.cs
public sealed record PingCreated(Guid PingId, string Message) : EventBase;
```

## Publishing (cross-module writes happen ONLY this way)

```csharp
// inside an endpoint/service — inject IEventBus
await eventBus.PublishAsync(new PingCreated(ping.Id, ping.Message), ct);
```

## Handling — register in YOUR module, in YOUR feature folder

```csharp
// Features/Sample/Handlers/PingCreatedLoggingHandler.cs
public sealed class PingCreatedLoggingHandler : IEventHandler<PingCreated>
{
    public Task HandleAsync(PingCreated evt, CancellationToken ct = default) { … }
}

// Features/Sample/SampleModule.cs → AddModule
services.AddScoped<IEventHandler<PingCreated>, PingCreatedLoggingHandler>();
```

Handlers are resolved from the **current scope**, so they can take scoped dependencies
(DbContexts included). Register scoped unless stateless.

## Failure isolation semantics (what "fire-and-forget" means here)

- Handlers run **sequentially, awaited**, each wrapped in try/catch.
- A throwing handler is logged (`LogError` with handler + event id) and **skipped — the next
  handler still runs and the publisher never sees the exception**.
- **Cancellation is the one exception:** if the publisher's `CancellationToken` fires,
  `OperationCanceledException` propagates (publishing stops) — shutdown is not a handler failure.
- Zero registered handlers = silent success. A missing subscriber module breaks nothing (§1.5).
- No `Task.Run`/threads: isolation is at module level, not thread level — this keeps scoped
  services (DbContexts) safe from disposed-scope bugs.
- **Handlers run inline inside the publisher's request**, so a slow handler is the publisher's
  latency. Anything slow (network, AI) must hand off to a background worker instead — that is
  exactly why `IncidentCreatedHandler` only enqueues to a bounded channel and the AI call happens
  in `AiAnalysisWorker` (D-021).
- A handler that talks to an unreliable dependency swallows its own failures too: the realtime
  notifier never throws at publishers, so an unavailable hub or DB cannot fail an incident.
- Log hygiene: user text is data — log IDs/lengths, never free text (and never a payload,
  a token, or an API key).

## Testing

`Eventing/InProcessEventBusTests` covers ordering, isolation, and scoping; `SamplePingTests` proves
the publisher→handler flow end-to-end with a probe handler registered in the test factory.
