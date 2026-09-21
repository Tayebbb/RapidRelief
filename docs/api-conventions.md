# API Conventions (F0 — plan §8.9)

The Sample slice (`Features/Sample`) is the living reference for everything below — copy it.

## Routes

- Pattern: `/api/{feature}/{resource}` — plural resources, kebab-case for multiword (`/api/foundation/demo-incidents`).
- Each feature maps its own group in its module: `endpoints.MapGroup("/api/sample")`.
- Every endpoint declares auth explicitly: `.RequireAuthorization(AuthPolicies.RequireAdmin)` or `.AllowAnonymous()` — never rely on defaults.

Groups in force today:

| Group                         | Owner       | Auth                                                                                                        |
| ----------------------------- | ----------- | ----------------------------------------------------------------------------------------------------------- |
| `/health`, `/api/hero-images` | Foundation  | anonymous (health reports DB state; hero-images lists the landing slideshow files)                          |
| `/api/foundation`             | Foundation  | mixed (`/whoami` authorized, `/demo-incidents` anonymous)                                                   |
| `/api/sample`                 | Sample      | mixed (POST `/pings` = Admin policy, GET `/pings`, `/pings/{id}` = anonymous)                              |
| `/api/auth`                   | Auth        | mixed (register/login/refresh/`oauth/google-init`/`oauth/google-session` anonymous, rest authorized, `/users*` = Admin) |
| `/api/ai`                     | Ai          | any authenticated role                                                                                      |
| `/api/ai/assistant`           | Ai (F16)    | any authenticated role (D-047, D-054)                                                                       |
| `/api/realtime/notifications` | Realtime    | any authenticated role                                                                                      |
| `/api/shelters`               | Shelters    | mixed (GET list/`{id}`/`recommend` anonymous, POST/PUT/PATCH = Admin policy)                                |
| `/api/alerts`                 | Alerts (F10)| mixed (GET list/`active`/`{id}` anonymous, POST + `{id}/revoke` = Government policy)                        |
| `/api/incidents`              | Incidents (F2) | authenticated (create/list/mine/detail); `POST /{id}/verify` = Government. Citizens are scoped to their own reports |
| `/api/rescue`                 | Rescue (F5) | `RequireResponder` (Rescuer **or** Government); `POST /teams` + `POST /missions/{id}/reassign` = Government, `POST /teams/mine/position` + `/teams/mine/status` = Rescuer |
| `/api/relief/requests`        | Relief (F4) | authenticated (create/mine/detail/cancel — citizens scoped to their own); triage list + `POST /{id}/status` = Government |
| `/api/relief/resources`       | Relief (F11)| **Government** — warehouse inventory (list/create/update) |
| `/api/incidents/ops`          | Incidents (F12) | `RequireResponder` — command-centre analytics; deliberately **outside** the `reports` budget |
| `/api/audit`                  | Audit (F14) | **Government** — append-only trail, filterable |

Every feature now maps endpoints; the `/api/shelters/recommendations` route returns shelters ranked by
suitability (distance + free capacity + facilities) with the reasons, not by distance alone.

### Rescue operations surface (F5)

| Route | Who | Purpose |
| --- | --- | --- |
| `GET /api/rescue/dashboard?lat=&lng=` | Responder | One-call console feed: `queueByBand` (Critical/High/Medium/Low), `critical`, `nearby`, mission counts, the caller's team |
| `GET /api/rescue/queue?band=&lat=&lng=&take=` | Responder | Unassigned queue, SOS + AI priority first, distance from the caller, optional band filter |
| `GET /api/rescue/teams/suitable?incidentId=` | Responder | Teams ranked by `TeamSuitabilityScorer` with plain-language reasons; off-duty excluded |
| `POST /api/rescue/missions` | Responder | Assign a team. `409` if the incident already has a live mission, or the team is deployed or off duty |
| `POST /api/rescue/missions/{id}/accept` | Rescuer (team member) | Stamps `acceptedAtUtc` |
| `POST /api/rescue/missions/{id}/reject` | Rescuer (team member) | Cancels with a reason, requeues the incident, frees the team |
| `POST /api/rescue/missions/{id}/status` | Rescuer (team member) | Forward-only `Assigned→EnRoute→OnScene→Completed`; `Cancelled` from any live state; anything else `409` |
| `POST /api/rescue/missions/{id}/reassign` | **Government** | Cancels the current mission and opens a new one on another team; the incident follows |
| `GET /api/rescue/missions?mine=&activeOnly=&incidentId=` | Responder | Mission ledger |
| `POST /api/rescue/teams/mine/position` · `/mine/status` | Rescuer | Position sharing and duty status (`Available`/`Dispatched`/`OffDuty`; refused mid-mission) |

Conflicts are always refused server-side with `409` and a ProblemDetails `detail` the client shows
verbatim — the API never resolves a double-assignment by last-write-wins.

### Government command surface (F7 / F11 / F12 / F14)

| Route | Who | Purpose |
| --- | --- | --- |
| `GET /api/incidents/ops/summary?days=` | Responder | KPIs (active / critical / SOS / unassigned / awaiting-team / in-progress / resolved-24 h / new-24 h), `byStatus`/`byType`/`bySeverity`, daily reported-vs-resolved buckets, average response and resolution, resolution rate, area hotspots with a 6-hour trend |
| `GET /api/incidents?q=&status=&type=&severity=&sos=&unassigned=` | Responder | Incident board search. Citizens are still redirected to their own reports |
| `POST /api/incidents/{id}/resolve` | **Government** | Close-out with a reason; `409` while a mission is live, `400` without notes |
| `PUT /api/rescue/teams/{id}` | **Government** | Team registry edit; duty status refused mid-mission (`409`), unknown status `400` |
| `GET|POST|PUT /api/relief/resources` | **Government** | Warehouse stock, committed and free quantities, open demand per supply type, and supply types with no stock |
| `GET /api/audit?action=&entityType=&entityId=&actorId=&q=&hours=` | **Government** | Append-only trail: who, what, when, entity, result, source |
| `GET /api/audit/actions` | **Government** | Distinct actions and record types, for the filter dropdowns |

Administrative writes record through the frozen `IAuditTrail` contract — never by referencing
`Features/Audit`. Audit writes never throw, so a failed trail line cannot roll back the action
(D-097).

### AI decision support (F8)

| Route | Who | Purpose |
| --- | --- | --- |
| `GET /api/ai/insights/{incidentId}` | any authenticated role | Full structured view: classification, severity, confidence, urgency band, damage indicators, estimated people affected, medical urgency, summary, reasoning, priority score/band and the scored factors behind it, plus any duplicate flag |
| `GET /api/ai/assessments/{incidentId}` | any authenticated role | The original narrow assessment shape (unchanged) |
| `GET /api/ai/duplicates?pendingOnly=` | Responder | Review queue of flagged possible duplicates with confidence and evidence |
| `POST /api/ai/duplicates/{id}/confirm` · `/dismiss` | **Government** | Records the verdict and an audit line. **Neither report is closed** — that stays in the incident board |

Every AI payload is decision support. `AiInsightDto.IsDecisionSupport` is always true and
`AiInsightDto.Disclaimer` must be rendered wherever an insight is shown (D-102). A response whose
`provider` is `RuleBased` was produced without the model — the reasoning line names why.

The SignalR hub at `/hubs/notifications` is the one non-`/api` surface (push-only, D-032/D-043).
Unknown `/api/**` routes return a ProblemDetails 404 — never the SPA shell.

## Response envelope (success)

Success responses wrap payloads in `ApiEnvelope<T>` (from `Shared/Contracts/Common`):

```json
{
  "data": {
    "id": "…",
    "message": "hello",
    "createdAtUtc": "2026-09-01T12:00:00+00:00"
  }
}
```

Collections use `ApiEnvelope<PagedResult<T>>`:

```json
{ "data": { "items": [ … ], "page": 1, "pageSize": 50, "totalCount": 128 } }
```

## Paging

- Query params: `page` (1-based, default 1) and `pageSize` (default 50).
- **Clamp convention (mandatory, BEFORE any math):** `page` is server-clamped to 1–1,000,000 and
  `pageSize` to 1–200. Out-of-range values are silently clamped, never a 400 and never a 500 —
  an unclamped `page=2147483647` overflows `(page-1)*pageSize` into a crash.
- `totalCount` is always the full filtered count, independent of the page slice.
- **Incremental feeds use cursor paging instead** (`GET /api/realtime/notifications?since=&limit=`,
  D-038): opaque base64url cursor, `limit` clamped 1–100 (default 50), an undecodable cursor is the
  one paging input that IS a 400. Use offset paging for browsable lists, cursors for polling.

## Rate limiting

Policies live in `Program.cs`, read their budgets from the `RateLimiting` config section, and are
skipped entirely in the `Testing` environment. `UseRateLimiter` runs **after** `UseAuthentication`
so per-user partitions see the real caller (and before `UseAuthorization`, so anonymous floods
still consume permits).

| Policy      | Partition                                 | Default budget | Applied to                                                |
| ----------- | ----------------------------------------- | -------------- | --------------------------------------------------------- |
| _global_    | per-IP                                    | 100 / 10 s     | every request                                             |
| `auth`      | per-IP                                    | 10 / 60 s      | `/api/auth` register, login, refresh (D-011)              |
| `reports`   | per-IP                                    | 30 / 60 s      | the whole `/api/incidents` and `/api/relief` groups — ingestion is the abuse surface (D-011) |
| `ai`        | per-IP                                    | 30 / 60 s      | `/api/ai/*` and the assistant `GET`/`DELETE`              |
| `assistant` | per-user (`RateLimitPartitions.UserOrIp`) | 12 / 300 s     | `POST /api/ai/assistant/messages` only (D-054)             |
| `alerts`    | per-user (`RateLimitPartitions.UserOrIp`) | 20 / 60 s      | the whole `/api/alerts` group, reads included (D-073)      |
| `realtime`  | per-user (`RateLimitPartitions.UserOrIp`) | 120 / 60 s     | `/api/realtime/notifications/*`                            |

Apply one with `.RequireRateLimiting("<policy>")` on the group (or on the single endpoint when the
budget differs per verb, as the assistant does). Per-IP partitioning behind a reverse proxy
REQUIRES forwarded headers (`Proxy:Enabled` + `Proxy:KnownProxies`) or all clients share one
partition (D-011).

## Cache-Control on sensitive groups

Any group serving user-scoped, auth or AI data adds an endpoint filter setting
`Cache-Control: no-store, private` on every response — `/api/auth`, `/api/ai`, `/api/ai/assistant`
(reuses `AiEndpoints.CacheControlNoStoreFilter`, D-047) and `/api/realtime/notifications`.
`X-Content-Type-Options: nosniff` is applied globally in `Program.cs` and needs no per-group work.

## Errors — RFC 7807 ProblemDetails, always

Never invent error shapes. Non-2xx responses are ProblemDetails (`application/problem+json`):

```json
{ "type": "…", "title": "Database unavailable", "status": 503, "detail": "…" }
```

Validation failures return 400 via `Results.ValidationProblem(validation.ToDictionary())`:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Message": ["'Message' must not be empty."] }
}
```

Validation is **explicit** FluentValidation in the endpoint (inject `IValidator<T>`, call
`ValidateAsync`) — never auto-MVC integration, never `FluentValidation.AspNetCore`.

DB-backed endpoints gate on `DatabaseHealth.PostgresAvailable` and return a **503 ProblemDetails**
while degraded (D-005) — never a 500, never an empty 200. The one documented exception is the
assistant `POST`, which must never return 5xx at all: it degrades to a stateless answer with
`degraded: true` (§4.8).

## DTO naming

- `{Thing}Dto` — response read models (`PingDto`).
- `{Verb}{Thing}Request` — request bodies (`CreatePingRequest`), validated by `{Verb}{Thing}Validator`.
- `{Thing}SummaryDto` — cross-module read models in `Shared/Contracts/ReadModels`.
- Contract DTOs live in `Shared/Contracts` and are additive-only (§4.6); slice-local DTOs live beside their endpoints.

## EF Core with multiple contexts — exact commands

Every feature owns its context and migration folder. **Always pass `--context` and `--output-dir`** —
a bare `dotnet ef` invocation corrupts folder ownership the moment a second context exists:

```powershell
# add a migration (design-time, no live DB needed)
dotnet ef migrations add Initial --project src/RapidRelief.Api --context SampleDbContext --output-dir Features/Sample/Data/Migrations

# list migrations
dotnet ef migrations list --project src/RapidRelief.Api --context SampleDbContext

# apply to the database (used by CI postgres-fidelity job)
dotnet ef database update --project src/RapidRelief.Api --context SampleDbContext
```

Nine contexts are live today — substitute your own row:

| Context                  | Owner     | Tables                                                                     | History table                         | `--output-dir`                       |
| ------------------------ | --------- | -------------------------------------------------------------------------- | ------------------------------------- | ------------------------------------ |
| `SampleDbContext`        | Sample    | `sample_pings`                                                             | `__efmigrationshistory_sample`        | `Features/Sample/Data/Migrations`    |
| `AuthDbContext`          | Auth      | `auth_*` (Identity + `auth_permissions`, `auth_role_permissions`, `auth_refresh_tokens`) | `__efmigrationshistory_auth`          | `Features/Auth/Data/Migrations`      |
| `AiDbContext`            | Ai        | `ai_assessments`, `ai_assistant_messages`                                  | `__efmigrationshistory_ai`            | `Features/Ai/Data/Migrations`        |
| `NotificationsDbContext` | Realtime  | `notifications_*`                                                          | `__efmigrationshistory_notifications` | `Features/Realtime/Data/Migrations`  |
| `OpsDbContext`           | Shelters  | `ops_shelters`, `ops_shelter_supplies`, `ops_safety_zones`                 | `__efmigrationshistory_ops`           | `Features/Shelters/Data/Migrations`  |
| `AlertsDbContext`        | Alerts    | `alerts_alerts`                                                            | `__efmigrationshistory_alerts`        | `Features/Alerts/Data/Migrations`    |
| `IncidentsDbContext`     | Incidents | `incidents_reports`, `incidents_media`, `incidents_status_history`         | `__efmigrationshistory_incidents`     | `Features/Incidents/Data/Migrations` |
| `ReliefDbContext`        | Relief    | `relief_requests`, `relief_resources`, `relief_dispatches`                 | `__efmigrationshistory_relief`        | `Features/Relief/Data/Migrations`    |
| `RescueDbContext`        | Rescue    | `rescue_teams`, `rescue_team_members`, `rescue_missions`, `rescue_mission_logs` | `__efmigrationshistory_rescue`   | `Features/Rescue/Data/Migrations`    |
| `AuditDbContext`         | Audit     | `audit_entries`                                                            | `__efmigrationshistory_audit`         | `Features/Audit/Data/Migrations`     |

(The `notifications_` prefix on a `Features/Realtime` folder is deliberate — D-042. The Shelters
slice owns `OpsDbContext`/`ops_*` for the same reason: the prefix names the data, not the folder.)

> The CI `postgres-fidelity` job applies **all ten** contexts. Add one
> `dotnet ef database update --context <X>` step whenever you add an eleventh.

Adding a context is a one-line scale-out in three places: the module's `AddModule`/`MigrateAsync`,
`TestingWebAppFactory`, and one `dotnet ef database update` step in the CI `postgres-fidelity` job.
Modules migrate **their own context only** in `MigrateAsync` (never someone else's), and a merged
migration is never edited — add a new one (§4.4).

## Provider portability (Npgsql vs SQLite tests)

Integration tests run each context on SQLite `:memory:` (`TestingWebAppFactory`), production runs
Npgsql. Keep entity models to portable types (Guid/string/int/`DateTimeOffset`). Any
provider-specific model configuration must be gated so both providers work:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    if (Database.IsNpgsql())
    {
        // Npgsql-only config (e.g. jsonb columns) goes here
    }
}
```

`SampleDbContext` shows the pattern (SQLite-gated `DateTimeOffset`→ticks conversion for ordering);
`AuthDbContext`, `AiDbContext` and `NotificationsDbContext` all repeat it. SQLite proves behaviour,
the CI `postgres-fidelity` job proves the generated SQL is valid Npgsql — neither replaces the other.
