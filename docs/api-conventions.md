# API Conventions (F0 — plan §8.9)

The Sample slice (`Features/Sample`) is the living reference for everything below — copy it.

## Routes

- Pattern: `/api/{feature}/{resource}` — plural resources, kebab-case for multiword (`/api/foundation/demo-incidents`).
- Each feature maps its own group in its module: `endpoints.MapGroup("/api/sample")`.
- Every endpoint declares auth explicitly: `.RequireAuthorization(AuthPolicies.RequireAdmin)` or `.AllowAnonymous()` — never rely on defaults.

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

- Query params: `page` (1-based, default 1) and `pageSize` (default 50, server-clamped to 1–200).
- `totalCount` is always the full filtered count, independent of the page slice.

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

Conventions per context: table prefix `{feature}_` (e.g. `sample_pings`), history table
`__efmigrationshistory_{feature}`, migrations under `Features/{Feature}/Data/Migrations`.
Modules migrate **their own context only** in `MigrateAsync` (never someone else's).

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

`SampleDbContext` shows the pattern (SQLite-gated `DateTimeOffset`→ticks conversion for ordering).
