# API Conventions (F0 — plan §8.9)

The Sample slice (`Features/Sample`) is the living reference for everything below — copy it.

## Routes

- Pattern: `/api/{feature}/{resource}` — plural resources, kebab-case for multiword (`/api/foundation/demo-incidents`).
- Each feature maps its own group in its module: `endpoints.MapGroup("/api/sample")`.
- Every endpoint declares auth explicitly: `.RequireAuthorization(AuthPolicies.RequireAdmin)` or `.AllowAnonymous()` — never rely on defaults.

Groups in force today:

| Group                         | Owner      | Auth                                                                          |
| ----------------------------- | ---------- | ----------------------------------------------------------------------------- |
| `/api/foundation`             | Foundation | mixed (`/whoami` authorized, `/demo-incidents` anonymous)                     |
| `/api/sample`                 | Sample     | mixed (POST = Admin policy, GET = anonymous)                                  |
| `/api/auth`                   | Auth       | mixed (register/login/refresh anonymous, rest authorized, user admin = Admin) |
| `/api/ai`                     | Ai         | any authenticated role                                                        |
| `/api/ai/assistant`           | Ai (F16)   | any authenticated role (D-047, D-054)                                         |
| `/api/realtime/notifications` | Realtime   | any authenticated role                                                        |

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
| `reports`   | per-IP                                    | 30 / 60 s      | F2 report endpoints — **mandatory** when F2 lands (D-011) |
| `ai`        | per-IP                                    | 30 / 60 s      | `/api/ai/*` and the assistant `GET`/`DELETE`              |
| `assistant` | per-user (`RateLimitPartitions.UserOrIp`) | 12 / 300 s     | `POST /api/ai/assistant/messages` only (D-054)            |
| `realtime`  | per-user (`RateLimitPartitions.UserOrIp`) | 120 / 60 s     | `/api/realtime/notifications/*`                           |

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

Four contexts are live today — substitute your own row:

| Context                  | Owner    | Tables            | History table                         | `--output-dir`                      |
| ------------------------ | -------- | ----------------- | ------------------------------------- | ----------------------------------- |
| `SampleDbContext`        | Sample   | `sample_*`        | `__efmigrationshistory_sample`        | `Features/Sample/Data/Migrations`   |
| `AuthDbContext`          | Auth     | `auth_*`          | `__efmigrationshistory_auth`          | `Features/Auth/Data/Migrations`     |
| `AiDbContext`            | Ai       | `ai_*`            | `__efmigrationshistory_ai`            | `Features/Ai/Data/Migrations`       |
| `NotificationsDbContext` | Realtime | `notifications_*` | `__efmigrationshistory_notifications` | `Features/Realtime/Data/Migrations` |

(The `notifications_` prefix on a `Features/Realtime` folder is deliberate — D-042.)

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
