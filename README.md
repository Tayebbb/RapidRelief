# RapidRelief

**AI Smart Disaster Response & Emergency Management System** — semester project by Tayeb, Shehab, Tanjim & Mugdho.

Citizens report disasters (GPS, photos, offline-capable SOS) → AI classifies, scores priority, and detects duplicates → rescue teams run missions from a live priority queue → a government command center monitors, verifies, and dispatches → relief resources are requested, allocated, and tracked to delivery.

**Stack:** ASP.NET Core 8 (modular monolith) · Blazor WebAssembly PWA · PostgreSQL + EF Core · SignalR · Leaflet/OpenStreetMap · Gemini (with rule-based fallback).

## Start here

| Doc                                                                | Purpose                                                                                                                             |
| ------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md)                           | **Single source of truth** — what's implemented, what's next, architecture rules. Humans and AI agents read this before any change. |
| [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md) | Full development plan — features, ownership, phases, zero-blocking parallel model, demo script.                                     |
| [AGENTS.md](AGENTS.md)                                             | Instructions for AI coding agents (Copilot, Antigravity, etc.).                                                                     |

> Status: planning complete — implementation starts with F0 (Week 1 foundation). See the status board in PROJECT-CONTEXT.md.

## Run guide

Prerequisite: **.NET 8 SDK** only. Everything below works from a fresh clone.

### 1. Primary path — Docker Postgres

```bash
cp .env.example .env          # optional; compose has the same defaults built in
docker compose up -d          # postgres:16 on localhost:5432
dotnet run --project src/RapidRelief.Api
```

Open http://localhost:5179 — migrations apply automatically at startup (per-module, see D-005/D-007). Try the `/sample` page: it lists pings and posts one as `Admin` via the `X-Dev-Role` dev header.

### 2. Fallback — cloud Postgres (Neon/Supabase free tier)

No Docker? Point the app at any Postgres with an env var (or `dotnet user-secrets`):

```powershell
$env:ConnectionStrings__Postgres = "Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require"
dotnet run --project src/RapidRelief.Api
```

> **Ops note:** never run the `Development` environment against a production database — the startup seeder would create the demo users (`citizen1@rr.dev` … `Demo!123`) in it. Deployed instances must run with `ASPNETCORE_ENVIRONMENT=Production` (roles are seeded everywhere; demo users only in Development/Testing).

### 3. No database at all — degraded mode (D-005)

`dotnet run --project src/RapidRelief.Api` with nothing listening on 5432 still works: startup retries migrations 3× (2s backoff), logs a prominent warning, and keeps serving. Stub-backed pages keep working; DB-backed endpoints (e.g. `POST`/`GET /api/sample/pings`) return **503 ProblemDetails**; `GET /health` reports `"status": "degraded", "dbConnected": false`. The demo never depends on a network.

### AI data flow & consent (F8)

By default (`Ai:Gemini:ApiKey` empty), incident analysis is **fully local** — the permanent rule-based fallback makes **zero external calls**. When a key is configured (`dotnet user-secrets set Ai:Gemini:ApiKey <key>` in `src/RapidRelief.Api`, or the `Ai__Gemini__ApiKey` env var), the incident **description text and the first photo** are sent to Google Gemini for assessment. Nothing else leaves the machine: no names, emails, phones, GPS coordinates, incident IDs, or timestamps are in the request, and extra photos are never uploaded. Logs record metadata only (provider, model, latency, tokens, status codes) — never the description, photo, or model response, and never the key. Kill the key mid-demo and analysis continues rule-based with no errors.

> **Demo consent note:** while a key is configured, submitted reports may be processed by Google Gemini.

### Realtime notifications (F9)

Signed-in users get a bell (unread badge, `99+` above 99), an inbox at `/notifications`, and toasts for live arrivals. Two delivery paths run side by side and are deduped by notification id:

- **Push** — SignalR hub at `/hubs/notifications` (push-only; role groups are derived from the server's own claims, never from a client argument). Reconnects on the 0 s / 2 s / 10 s / 30 s schedule.
- **Poll** — `GET /api/realtime/notifications?since=&limit=` every **5 s while the hub is down** and every **60 s while it's up**. The hub is an optimization; polling is what guarantees delivery.

`Realtime:Mode` (appsettings / `Realtime__Mode`) is a **tri-state** operational switch (D-032):

| Mode          | Hub route            | Notifications persisted | Client behaviour                                        |
| ------------- | -------------------- | ----------------------- | ------------------------------------------------------- |
| `Hub`         | mapped               | yes                     | live push + slow poll                                    |
| `PollingOnly` | 404 (clean JSON)     | yes                     | connect fails silently, 5 s polling delivers everything   |
| `Off`         | 404 (clean JSON)     | no (no-op notifier)     | inbox stays empty, no errors                             |

Notification text is rendered with plain Blazor interpolation (auto HTML-encoded) — never `MarkupString`, because payloads can carry AI output and user-submitted content. In degraded mode (no DB) the inbox endpoints return 503 and the client simply shows nothing new; no error banner, no retry storm.

Dev tip: with no login but a **dev role** selected in the top-right picker, the client connects over long polling and sends `X-Dev-Role` (D-035) — browser WebSockets cannot carry custom headers.

### Tests — no Docker/Postgres needed

```bash
dotnet test
```

Integration tests boot the real app under env `Testing` with per-context SQLite `:memory:` databases (see `tests/RapidRelief.Api.Tests/TestingWebAppFactory.cs`). Postgres SQL fidelity is proven separately by the CI `postgres-fidelity` job, which applies all migrations against a real `postgres:16` service.

### EF migrations (per-context, always)

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/RapidRelief.Api --context SampleDbContext --output-dir Features/Sample/Data/Migrations
dotnet ef migrations list --project src/RapidRelief.Api --context SampleDbContext
```

Every future context (`AuthDbContext`, `IncidentsDbContext`, …) copies this pattern: **always** pass `--context` and its own feature-owned `--output-dir` (PROJECT-CONTEXT §4.4).
