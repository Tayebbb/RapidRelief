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

| Event              | Published by                                                             | Handled by                                                                 |
| ------------------ | ------------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| `PingCreated`      | `Features/Sample` — `POST /api/sample/pings`                             | `PingCreatedLoggingHandler` (Sample)                                        |
| `AuthEvent`        | `Features/Auth` — register, login, failed login, logout, lock/unlock, role change, token reuse | `AuthEventDisconnectHandler` (Realtime): `Lock`/`RoleChange`/`TokenReuse` abort live hub sockets; all other actions are ignored, and **no** notification row is written (D-036) |
| `IncidentAssessed` | `Features/Ai` — `AiAnalysisWorker` after a successful assessment          | `IncidentAssessedNotificationHandler` (Realtime) → topic `ai.incident.assessed` to roles Rescue + Admin |
| `IncidentCreated`  | **F2, not yet built** — today only tests publish it                       | `IncidentCreatedHandler` (Ai) → enqueues to the bounded AI channel (D-021)   |
| `AlertPublished`   | **F10, not yet built** — today only tests publish it                      | `AlertPublishedNotificationHandler` (Realtime) → topic `alerts.published` to all |

The last two rows are the zero-blocking model working as designed: the subscriber ships before the
publisher exists, and publishing the event is the only integration step F2/F10 have to take.
The remaining Contracts v1 events (`IncidentVerified`, `MissionAssigned`, `MissionStatusChanged`,
`ReliefRequested`, `ReliefStatusChanged`) have neither a publisher nor a handler yet.

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
