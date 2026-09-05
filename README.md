# RapidRelief

**AI Smart Disaster Response & Emergency Management System** — semester project by Tayeb, Shehab, Tanjim & Mugdho.

Citizens report disasters (GPS, photos, offline-capable SOS) → AI classifies, scores priority, and detects duplicates → rescue teams run missions from a live priority queue → a government command center monitors, verifies, and dispatches → relief resources are requested, allocated, and tracked to delivery.

**Stack:** ASP.NET Core 8 (modular monolith) · Blazor WebAssembly PWA · PostgreSQL + EF Core · SignalR · Leaflet/OpenStreetMap · OpenRouter free models (with rule-based fallback).

## Start here

| Doc                                                                | Purpose                                                                                                                             |
| ------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------- |
| [PROJECT-CONTEXT.md](docs/PROJECT-CONTEXT.md)                      | **Single source of truth** — what's implemented, what's next, architecture rules. Humans and AI agents read this before any change. |
| [PROJECT-AUDIT.md](docs/PROJECT-AUDIT.md)                          | **Evidence-based state of the repo (2026-09-03)** — capability matrix, defects, security findings, P0–P3 backlog, effort estimates.  |
| [RapidRelief-Development-Plan.md](docs/RapidRelief-Development-Plan.md) | Full development plan — features, ownership, phases, zero-blocking parallel model, demo script.                                |
| [AGENTS.md](docs/AGENTS.md)                                        | Instructions for AI coding agents (Copilot, Antigravity, etc.).                                                                     |
| [design.md](docs/design.md)                                        | **Mandatory before any UI work** — design tokens, components, dark/light + accessibility gate.                                     |
| [frontend-uiux.md](docs/frontend-uiux.md)                          | The long-form UI/UX engineering guide: component library, typography, map UX, DoD checklist.                                        |
| [api-conventions.md](docs/api-conventions.md)                      | Routes, response envelope, ProblemDetails, paging, rate-limit policies, per-context EF commands.                                    |
| [event-bus.md](docs/event-bus.md)                                  | Declaring, publishing and handling cross-module events (what is on the bus today).                                                  |
| [STACK.md](docs/STACK.md)                                          | Technology stack and architecture mentoring guide.                                                                                  |
| [RapidRelief-Website-Theme.md](docs/RapidRelief-Website-Theme.md)  | Brand palette and public-site theme (source of truth for the colour hexes).                                                        |

> Status: **F0 Foundation, F1 Auth, F3 Shelters, F5 Rescue Operations, F7 Command Centre, F8 AI Engine, F9 Realtime, F10 Broadcast Alerts, F14 Audit Trail and F16 AI Assistant are DONE; F2 Disaster Reporting, F4 Relief Requests and F12 Analytics are MVP DONE** — the full loop (citizen report → AI priority → government verification → rescue assignment → live mission → resolution → citizen notification) plus the government command centre run end-to-end against Postgres. Remaining gaps are advanced: F13 registry, F11 dispatch/delivery records, a map heatmap layer and AI human override. See the status board in PROJECT-CONTEXT.md §3 and the verified state in [PROJECT-AUDIT.md](docs/PROJECT-AUDIT.md).

## What works today

| Area                                | You get                                                                                                                                                                                   |
| ----------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Foundation** (F0)                 | Modular-monolith host, self-registering feature modules, in-process event bus, per-feature EF contexts, degraded-mode startup, hosted Blazor WASM PWA, vendored Leaflet map component     |
| **Contracts v1**                    | `RapidRelief.Shared/Contracts` — enums, read models, events and the 7 service interfaces every feature integrates through (frozen, additive-only)                                         |
| **Stubs + seed data** (F0)          | Deterministic Dhaka dataset (28 incidents, 8 shelters, 6 hospitals, 10 volunteers, 5 NGOs, 6 teams) behind every contract interface, so a feature can be built before its producer exists |
| **Auth & RBAC** (F1)                | Real accounts: `/register`, `/login`, `/profile` pages; password login **and Google sign-in via Neon Auth** (`/auth/callback`); JWT + rotating refresh cookie; 3-Role system (`Citizen`, `Rescuer`, `Government`) with a seeded permission matrix and automated role redirection (`AuthRouteHelper`). `X-Dev-Role` fake auth still works when signed out |
| **Citizen Portal & SOS** (F2/F4)    | `/reports/new` files a **real** incident in four steps (GPS + tap-the-map pin, photo/video upload, 1-tap confirmed SOS → `POST /api/incidents`), `/reports/my` tracks a six-stage timeline with real timestamps, `/relief/request` runs the live F4 vertical with a five-stage tracker, and an IndexedDB outbox stores reports **before** the first network attempt so nothing is lost offline |
| **Rescue operations** (F5)          | `/r` operations console: severity-band KPI cards that filter a distance-aware priority queue, suitability-ranked team selection with reasons, assignment with `409` conflict guards, accept/reject (reject requeues the call), a four-stage live-mission HUD with server-stamped timestamps, team status and position sharing, plus `/r/incidents/{id}` with map, directions, media, timeline and Government reassignment; every transition notifies the reporting citizen |
| **Shelters** (F3)                   | Live shelter finder on Leaflet map (`/shelters/finder`) with a "you are here" dot, capacity meters, nearest-shelter calculations with AI recommendation fallback, and administrative shelter management (`/admin/shelters`) |
| **Broadcast alerts** (F10)          | Government compose/revoke at `/alerts/compose` over `/api/alerts`, public active-alert query, and the citizen dashboard banner — delivered to inboxes through the existing F9 `AlertPublished` subscriber |
| **Command centre** (F7/F12/F14)     | `/g` Emergency Operations Centre: live KPIs from `GET /api/incidents/ops/summary` (active, critical, SOS, unassigned, teams available/deployed, shelters near capacity, pending relief, average response), escalating-area hotspots, `/g/incidents` search and triage board, `/g/map` layered operational map, `/g/analytics` response metrics, `/g/relief` triage + warehouse inventory vs open demand, `/g/teams` registry, `/g/users` role and lock management, and `/g/audit` — an append-only trail of who did what, when, to which record, with what result |
| **Dashboards & HUDs** (F5/F7)       | Role-tailored dashboards: `/c` (citizen — SOS, report, my active incident, shelter, relief, notifications), `/r` (rescuer operations console) and `/g` (Government EOC) — all on live data, no mock figures anywhere |
| **Public Landing Page**             | Modern emergency response landing page at `/` with cinematic slideshow hero, quick actions, how-it-works flow, AI assistant simulator, and interactive navbar with glassmorphic profile menu |
| **AI engine** (F8)                  | `IncidentCreated` → background worker → structured decision support: classification, severity, confidence, damage indicators quoted from the report, estimated people affected, medical urgency, and an **explainable** priority score whose every point traces to a named factor with its evidence. Duplicates are scored (proximity + time + type + wording overlap) and queued for Government review — never merged or deleted. OpenRouter free models when a key is configured, with retry-on-transient, a circuit breaker and a rule-based fallback that fills the same structured shape; every degraded run names why. Surfaced as a labelled "AI · decision support" panel, never as fact |
| **Realtime** (F9)                   | `/hubs/notifications` SignalR push + notification inbox at `/notifications` + bell + toasts, with permanent 5 s polling fallback                                                          |
| **AI assistant** (F16)              | `/assistant` chat page with server-owned history, canned safety answers when OpenRouter is off, opt-in location sharing                                                                   |

The foundation, contracts, stubs and AI fallback all run with **no database and no API key** — see
[degraded mode](#3-no-database-at-all--degraded-mode-d-005) for exactly what is and isn't reachable.

## Run guide

Prerequisite: **.NET 8 SDK** only. Everything below works from a fresh clone.

> **No Docker, no Postgres, no API key?** Skip to step 3 — `dotnet run` alone gives you a working
> app: the SPA, the map, stub-backed data and the dev-role switcher. You can build and test a whole
> vertical slice this way; only sign-in and real persistence need a database.

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

> **Ops note:** never run the `Development` environment against a production database. Roles and
> permissions are seeded in every environment; **demo users are opt-in** — they are created only in
> `Testing` or when `Auth:SeedDemoUsers=true` (`citizen1@rr.dev` / `rescuer1@rr.dev` /
> `government1@rr.dev`, plus the legacy `admin1@rr.dev` / `rescue1@rr.dev` / `ngo1@rr.dev` aliases,
> all with `Demo!123`). Deployed instances run with `ASPNETCORE_ENVIRONMENT=Production` and the
> flag off.
>
> ⚠️ **`appsettings.Development.json` currently commits a live cloud Postgres connection string
> (including the password).** Treat that credential as compromised: rotate it, move it to
> `dotnet user-secrets` or `ConnectionStrings__Postgres`, and keep secrets out of the JSON
> (PROJECT-CONTEXT §2 “Security carry-out”, D-078).

### 3. No database at all — degraded mode (D-005)

`dotnet run --project src/RapidRelief.Api` with nothing listening on 5432 still works: startup retries migrations 3× (2s backoff), logs a prominent warning, and keeps serving. Stub-backed pages keep working; DB-backed endpoints (e.g. `POST`/`GET /api/sample/pings`) return **503 ProblemDetails**; `GET /health` reports `"status": "degraded", "dbConnected": false`. The demo never depends on a network.

What still works with no DB: the SPA and every stub-backed read (`/api/foundation/demo-incidents`, shelter/registry lookups), the map, dev-role switching, and AI analysis (it runs and publishes `IncidentAssessed`, it just skips persistence). What does not: anything needing a real row — registration and **login return 503, so no authenticated page (profile, `/notifications`, `/assistant`) is reachable in the browser**. The assistant API itself still answers, statelessly with `degraded: true`, for a caller that already holds a token.

### AI data flow & consent (F8, F16)

By default (`Ai:OpenRouter:ApiKey` empty), incident analysis is **fully local** — the permanent rule-based fallback makes **zero external calls**. When a key is configured (`dotnet user-secrets set Ai:OpenRouter:ApiKey <key>` in `src/RapidRelief.Api`, or the `Ai__OpenRouter__ApiKey` env var), the incident **description text and the first photo** are sent through OpenRouter to the configured free models (D-061 pins: `z-ai/glm-5.2:free` → `nvidia/nemotron-3-super-120b-a12b:free` for text, `google/gemma-4-31b-it:free` → `minimax/minimax-m3:free` for photos) for assessment. Nothing else leaves the machine: no names, emails, phones, GPS coordinates, incident IDs, or timestamps are in the request, and extra photos are never uploaded. Logs record metadata only (provider, routed model, latency, tokens, status codes) — never the description, photo, or model response, and never the key. Kill the key mid-demo and analysis continues rule-based with no errors.

The **emergency assistant** at `/assistant` (signed-in users, `/api/ai/assistant`) follows the same rule. With no key it answers from a deterministic canned safety taxonomy — **zero external calls**. With a key configured, what goes through OpenRouter is your **chat text** plus, only if you press "Use my location", the **names, distances and free capacity of up to 3 nearby open shelters** picked server-side. Your coordinates themselves are never sent to the models, are rounded to ~11 m in the browser before they leave it, and are only attached to a message while sharing is on (it defaults to off, and the page states exactly what is being shared).

Conversation history is **server-owned** (`ai_assistant_messages`): the client sends only a session id and one message, never past turns, so nobody can forge an assistant turn to rewrite the safety guardrails. History is scoped to its owner, capped at 50 messages per session, deleted by "New chat", and swept after **7 days**. Answers are sanitized server-side (control characters and any URL-shaped token stripped, clamped to 1500 characters) and rendered with plain Blazor interpolation inside a `white-space: pre-wrap` element — never `MarkupString`, never a Markdown renderer. Kill the key or the database mid-chat and the assistant keeps answering: canned guidance, HTTP 200, no error UI.

> **Demo consent note:** AI features route via OpenRouter to third-party free model providers, which may log and train on submitted content (incident descriptions, photos, assistant chats) per their own policies. Do not include personal or sensitive data. Routing to training providers can be disabled in the OpenRouter account privacy settings, which may reduce free-model availability.

### Realtime notifications (F9)

Signed-in users get a bell (unread badge, `99+` above 99), an inbox at `/notifications`, and toasts for live arrivals. Two delivery paths run side by side and are deduped by notification id:

- **Push** — SignalR hub at `/hubs/notifications` (push-only; role groups are derived from the server's own claims, never from a client argument). Reconnects on the 0 s / 2 s / 10 s / 30 s schedule.
- **Poll** — `GET /api/realtime/notifications?since=&limit=` every **5 s while the hub is down** and every **60 s while it's up**. The hub is an optimization; polling is what guarantees delivery.

`Realtime:Mode` (appsettings / `Realtime__Mode`) is a **tri-state** operational switch (D-032):

| Mode          | Hub route        | Notifications persisted | Client behaviour                                        |
| ------------- | ---------------- | ----------------------- | ------------------------------------------------------- |
| `Hub`         | mapped           | yes                     | live push + slow poll                                   |
| `PollingOnly` | 404 (clean JSON) | yes                     | connect fails silently, 5 s polling delivers everything |
| `Off`         | 404 (clean JSON) | no (no-op notifier)     | inbox stays empty, no errors                            |

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

Every context copies this pattern: **always** pass `--context` and its own feature-owned
`--output-dir` (PROJECT-CONTEXT §4.4). Ten contexts are live today — `SampleDbContext`,
`AuthDbContext`, `AiDbContext`, `NotificationsDbContext`, `OpsDbContext` (Shelters),
`AlertsDbContext`, `IncidentsDbContext`, `ReliefDbContext`, `RescueDbContext`, `AuditDbContext`; the
per-context table/history/folder names are listed in [docs/api-conventions.md](docs/api-conventions.md).
A new context is one line in your module, one in `TestingWebAppFactory`, and one step in the CI
`postgres-fidelity` job — which now applies all ten.

## Configuration reference

All keys live in `src/RapidRelief.Api/appsettings.json` and can be overridden by environment
variable (`__` replaces `:`) or `dotnet user-secrets`. Secrets belong in user-secrets/env vars —
never in the JSON.

| Key                                                    | Default                                                        | Purpose                                                                                                                 |
| ------------------------------------------------------ | -------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings:Postgres`                           | _empty_                                                        | Postgres connection string; empty ⇒ degraded mode                                                                       |
| `Jwt:Issuer` / `Jwt:Audience`                          | `RapidRelief`                                                  | Token issuer/audience                                                                                                   |
| `Jwt:SigningKey`                                       | _empty_                                                        | HS256 key; **required outside Development/Testing** (startup fails without it)                                          |
| `Jwt:AccessTokenMinutes`                               | `30`                                                           | Access-token TTL (D-013)                                                                                                |
| `Jwt:RefreshTokenDays`                                 | `7`                                                            | Absolute refresh-token lifetime (D-013)                                                                                 |
| `Auth:PasswordHasherIterations`                        | `210000`                                                       | PBKDF2 iterations (D-018)                                                                                               |
| `Auth:SeedDemoUsers`                                   | `false`                                                        | Opt-in demo accounts (`Demo!123`); always on in `Testing` (D-077)                                                       |
| `FileStorage:Root`                                     | `App_Data/uploads`                                             | Upload root, relative to the content root unless absolute                                                               |
| `FileStorage:MaxSizeBytes`                             | `10485760`                                                     | Per-file size cap (not in the JSON; code default)                                                                       |
| `Ai:OpenRouter:ApiKey`                                 | _empty_                                                        | Empty ⇒ rule-based/canned only, zero external calls (`OPENROUTER_API_KEY` gates the live smokes)                        |
| `Ai:OpenRouter:TextModel` / `TextFallbackModel`        | `z-ai/glm-5.2:free` / `nvidia/nemotron-3-super-120b-a12b:free` | D-061 text pair — sent as the `models` array, OpenRouter falls back in order                                            |
| `Ai:OpenRouter:VisionModel` / `VisionFallbackModel`    | `google/gemma-4-31b-it:free` / `minimax/minimax-m3:free`       | D-061/D-062 vision pair for photo requests                                                                              |
| `Ai:OpenRouter:TimeoutSecondsText` / `…Vision`         | `10` / `20`                                                    | Per-request timeouts (D-026/D-060)                                                                                      |
| `Ai:OpenRouter:MaxAttempts` / `…RetryBaseDelayMs`     | `2` / `250`                                                    | One retry with exponential backoff + jitter, transient failures only — timeout, network, 429, 5xx (D-108)               |
| `Ai:OpenRouter:BreakerFailures` / `BreakerOpenMinutes` | `3` / `2`                                                      | Shared circuit breaker (D-025)                                                                                          |
| `Ai:Pipeline:ChannelCapacity`                          | `100`                                                          | Bounded analysis queue; full ⇒ drop + log (D-021)                                                                       |
| `Ai:Assistant:MaxOutputTokens`                         | `512`                                                          | Assistant answer budget                                                                                                 |
| `Ai:Assistant:HistoryTurns`                            | `10`                                                           | Turns sent to the model (D-048)                                                                                         |
| `Ai:Assistant:MaxSessionMessages`                      | `50`                                                           | Hard cap per session; over ⇒ 400                                                                                        |
| `Ai:Assistant:MaxMessageLength`                        | `1000`                                                         | Inbound message cap                                                                                                     |
| `Ai:Assistant:MaxAnswerLength`                         | `1500`                                                         | Sanitizer clamp (D-051)                                                                                                 |
| `Ai:Assistant:ShelterCount`                            | `3`                                                            | Shelters injected as context (D-052)                                                                                    |
| `Ai:Assistant:RetentionDays` / `RetentionSweepHours`   | `7` / `6`                                                      | Chat retention sweep (D-048)                                                                                            |
| `Realtime:Mode`                                        | `Hub`                                                          | `Hub` · `PollingOnly` · `Off` (D-032)                                                                                   |
| `Realtime:RetentionDays` / `RetentionSweepHours`       | `30` / `6`                                                     | Notification retention sweep (D-034)                                                                                    |
| `Realtime:PollSecondsConnected` / `…Disconnected`      | `60` / `5`                                                     | Documented poll cadence; the WASM client hard-codes the same values (D-044), so changing these does not move the client |
| `RateLimiting:Global`                                  | `100` / `10 s`                                                 | Per-IP global limiter                                                                                                   |
| `RateLimiting:Auth`                                    | `10` / `60 s`                                                  | Per-IP, on register/login/refresh                                                                                       |
| `RateLimiting:Reports`                                 | `30` / `60 s`                                                  | Per-IP, reserved for F2 report endpoints                                                                                |
| `RateLimiting:Ai`                                      | `30` / `60 s`                                                  | Per-IP, `/api/ai/*` + assistant reads                                                                                   |
| `RateLimiting:Assistant`                               | `12` / `300 s`                                                 | **Per-user**, assistant POST only (D-054)                                                                               |
| `RateLimiting:Alerts`                                  | `20` / `60 s`                                                  | **Per-user**, the whole `/api/alerts` group (D-073)                                                                     |
| `RateLimiting:Realtime`                                | `120` / `60 s`                                                 | **Per-user**, notification inbox                                                                                        |
| `Proxy:Enabled`                                        | `false` (absent)                                               | Opt-in forwarded headers; required behind a reverse proxy (D-011)                                                       |
| `Proxy:KnownProxies`                                   | _absent_                                                       | Explicit proxy IPs; only then is default trust cleared                                                                  |

Rate limiting is disabled entirely in the `Testing` environment. Every `RateLimiting:*` entry is a
`PermitLimit` / `WindowSeconds` pair. `appsettings.Development.json` overrides two of the defaults
above: a **cloud Postgres connection string** and a DEV-ONLY `Jwt:SigningKey`. The signing key is
worthless outside your machine, but the connection string is a real credential that must not live
in the repo — see the ops warning above. In Development the API also serves static files with
`Cache-Control: no-cache` (D-076) so edited `wwwroot` CSS/JS is never served stale.

## Adding your own feature (new-developer onboarding)

1. **Read [PROJECT-CONTEXT.md](docs/PROJECT-CONTEXT.md) first** — §3 says what exists, §4 lists the nine
   rules that are never violated, §7 records every decision that binds you.
2. **Copy the Sample slice.** `src/RapidRelief.Api/Features/Sample` is the deliberate template: a
   module that self-registers, an EF context + migration, a validated endpoint pair with the
   response envelope, an auth policy, a published event with its handler, a Blazor page, and an
   integration test. Copy it into `Features/<YourFeature>`, rename, delete what you don't need
   (D-008).
3. **Integrate through contracts, never through folders.** Read other modules via the interfaces in
   `RapidRelief.Shared/Contracts`; write across modules only by publishing an event. Referencing
   another `Features/*` folder fails `ModuleIsolationTests` at build time (§4.1–§4.3).
4. **The stubs answer until you replace them.** Every contract interface already resolves to a
   deterministic fake registered with `TryAdd*` (`Features/Stubs`). Register a real implementation
   in your own module and it wins automatically — the fake yields with no coordination and no
   deletion, so it can never be "the thing someone forgot to remove" (§4.5).
5. **Conventions and mechanics:** [docs/api-conventions.md](docs/api-conventions.md) (routes,
   envelope, ProblemDetails, paging, rate-limit policies, `no-store`, EF commands) and
   [docs/event-bus.md](docs/event-bus.md) (declaring, publishing, handling, failure isolation).
6. **Decisions you inherit:** D-005 degraded mode · D-006 no MediatR · D-007 per-owner contexts ·
   D-008 the Sample template · D-011 rate limiting behind proxies · D-013/D-020 token lifetimes and
   the revocation window · D-019 feature-local wire DTOs · D-021 slow work goes to a background
   worker, never an event handler · D-036 topic naming for notifications.
7. **Finish the job:** update PROJECT-CONTEXT.md (status row + changelog + any new decision) in the
   same PR — see [AGENTS.md](docs/AGENTS.md). Code without it is incomplete work.
