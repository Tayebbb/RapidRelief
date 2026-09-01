# RapidRelief — PROJECT CONTEXT (Single Source of Truth)

> **MANDATORY for every AI agent (Copilot, Claude, Antigravity, etc.) and every human:**
> 1. **READ this file completely BEFORE implementing anything.**
> 2. Read the feature's section in [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md) before touching that feature.
> 3. **UPDATE this file AFTER every merged change** (status board row + changelog entry).
> 4. Never violate the Architecture Rules below — they are what keep 4 developers unblocked.

Last updated: 2026-09-01 · Current phase: **P0 in progress — F0 chunk 1/3 implemented (skeleton, contracts, bus, auth, CI)**

---

## 1. Project Snapshot

- **Product:** RapidRelief — AI Smart Disaster Response & Emergency Management System (semester project, 4 devs).
- **Core loop:** Citizen reports disaster → AI classifies/scores → Rescue teams run missions → Admin command center monitors/verifies → Relief requested, allocated, delivered.
- **Stack (frozen, see D-001):** ASP.NET Core 8 Web API (modular monolith, vertical slices) · Blazor WASM PWA (hosted) · PostgreSQL + EF Core 8 · SignalR · Leaflet/OpenStreetMap · Gemini free tier behind `IAiAnalysisService` with rule-based fallback · xUnit · GitHub Actions.
- **Team & lanes:** Tayeb (foundation/auth/AI/realtime) · Shehab (incident lifecycle: reporting/rescue/offline) · Tanjim (shelters/command center/analytics/audit) · Mugdho (relief/resources/registry/alerts).
- **Full plan:** [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md) — feature details, effort, phases, demo script.

## 2. Repository State

| Area | State |
|---|---|
| Planning | ✅ Complete (plan + this context file + [docs/architecture/F0-blueprint.md](docs/architecture/F0-blueprint.md)) |
| Solution scaffold (`RapidRelief.sln`, 3 src + 2 test projects) | ✅ Created (F0 chunk 1) — builds green from fresh clone with .NET 8 SDK only |
| Contracts v1 (`RapidRelief.Shared/Contracts`) | ✅ Authored frozen per F0-blueprint B2 — pending team workshop ratification |
| Stubs & seed data | ❌ Not created (F0 chunk 3) |
| CI pipeline | ✅ build-test job (`.github/workflows/ci.yml`); postgres-fidelity job arrives chunk 2 |
| Any feature code | Foundation module only (`/api/foundation/whoami`, `/health`); Sample slice arrives chunk 2 |

**Next action for the team:** F0 chunk 2 — EF/Postgres per-context pattern, Sample slice end-to-end, SQLite test factory, degraded startup (D-005). Then chunk 3 (stubs + Dhaka seed + RapidMap). Hold the contract workshop to ratify Contracts v1 (§6).

## 3. Feature Status Board

Statuses: `NOT STARTED` → `IN PROGRESS` → `MVP DONE` → `DONE` (· `BLOCKED` should never appear — if it does, fix the process, see plan §1.5).

| ID | Feature | Owner | Status | Notes |
|----|---------|-------|--------|-------|
| F0 | Platform Foundation & Shared Kernel | Tayeb | IN PROGRESS | Chunk 1/3 done: solution+projects, Contracts v1, event bus, module discovery, MultiAuth (JwtBearer+FakeAuth), middleware pipeline, whoami/health, hosted Blazor shell, CI build-test — 17/17 tests green. Chunk 2 next: EF pattern + Sample slice |
| F1 | Authentication, Profiles & RBAC | Tayeb | NOT STARTED | FakeAuth dev bypass ships in F0 |
| F2 | Disaster Reporting & SOS | Shehab | NOT STARTED | Owns Incident aggregate + state machine |
| F3 | Shelter Management & Finder | Tanjim | NOT STARTED | |
| F4 | Relief Requests & Tracking | Mugdho | NOT STARTED | |
| F5 | Rescue Team Operations | Shehab | NOT STARTED | Queue must work without AI scores (fallback sort) |
| F6 | Mission Assignment & Team Registry | Shehab | NOT STARTED | Manual assign = MVP; AI recommend = advanced |
| F7 | Admin Command Center & Verification | Tanjim | NOT STARTED | Build against fake read services first |
| F8 | AI Analysis Engine | Tayeb | NOT STARTED | Rule-based v1 → Gemini v2 |
| F9 | Real-Time Hub & Notification Center | Tayeb | NOT STARTED | Consumers keep polling fallback |
| F10 | Emergency Broadcast Alerts | Mugdho | NOT STARTED | Works pre-F9 via polling |
| F11 | Resource Inventory, Allocation & Delivery | Mugdho | NOT STARTED | Consumes own F4 |
| F12 | Analytics, Heatmaps & Response Metrics | Tanjim | NOT STARTED | Read-only via contracts |
| F13 | Hospital, Volunteer & NGO Registry | Mugdho | NOT STARTED | |
| F14 | Audit Trail | Tanjim | NOT STARTED | Pure event subscriber |
| F15 | Offline Reporting & Auto-Sync (PWA) | Shehab | NOT STARTED | Wow feature; needs F2 idempotency key |
| F16 | AI Emergency Assistant | Tayeb | NOT STARTED | Reuses F8 provider chain |
| F17 | Safety Zones & Road Closures | Tanjim | NOT STARTED | Stretch only |

## 4. Architecture Rules (NEVER violate)

1. **Vertical slices:** all code for a feature lives in `Features/<Feature>/` (Api + Client). A feature folder may reference `Shared/Contracts` and itself — **never another feature's folder**.
2. **Contracts are the only cross-module surface.** Cross-module reads go through contract interfaces (`IIncidentReadService`, `IShelterReadService`, …); cross-module writes happen only via events (`IncidentCreated`, `MissionStatusChanged`, …).
3. **No cross-module foreign keys or EF navigation properties.** Reference other modules by plain `Guid` ID.
4. **Per-owner DbContexts** with separate migration histories: `AuthDbContext`, `IncidentsDbContext`, `OpsDbContext`, `ReliefDbContext`, `AiDbContext`. Never add your tables to someone else's context. Never edit a merged migration — add a new one.
5. **Stubs stay alive.** Real implementations replace fakes via DI only; `FakeAuthHandler` (Development-only, header `X-Dev-Role`), rule-based AI, no-op notifier + polling fallbacks must keep working for the whole semester — they are demo resilience, not scaffolding.
6. **Contract changes are additive.** New optional fields OK; renames/removals/breaking changes require a `contracts`-labeled PR with 2 approvals. Never break Contracts v1 silently.
7. **Lane ownership:** only edit `Features/X` folders you own (or the task explicitly assigns). Shared hot spots: each feature self-registers via its own `{Feature}Module.cs` — never edit another feature's module file.
8. **Every external service sits behind an interface** (`IAiAnalysisService`, `IFileStorage`, `IRealtimeNotifier`) with a working fallback. The demo must never depend on network/quota.
9. **Security non-negotiables:** validate all input (FluentValidation), authorize by role policy on every endpoint, validate uploads (type/size), rate-limit auth + report endpoints, user text is data — never AI instructions.

## 5. Conventions Quick Reference

- **Routes:** `/api/{feature}/...` · standard response envelope + ProblemDetails errors (see plan §8.9).
- **Branches:** `feat/f{NN}-{short-name}`, `fix/f{NN}-{short-name}` — short-lived (≤3 days), PR into `main`, CI green + 1 approval (2 for contracts).
- **Commits:** Conventional Commits — `feat(reporting): ...`, `fix(shelters): ...`, `test(ai): ...`.
- **Tables:** prefixed `feature_tablename`.
- **Seeded users (dev):** `citizen1@rr.dev` / `rescue1@rr.dev` / `admin1@rr.dev` / `ngo1@rr.dev` — password `Demo!123` (Development only; never in production config).

## 6. Contracts v1 Registry

> Status: **FROZEN per [docs/architecture/F0-blueprint.md](docs/architecture/F0-blueprint.md) §B2 — pending workshop ratification.** Authored in `RapidRelief.Shared/Contracts` (F0 chunk 1); exact signatures live in code and blueprint B2. Changes are additive-only (§4.6) — `contracts`-labeled PR + 2 approvals.

- **Common:** `GeoPoint`, `PagedResult<T>`, `ApiEnvelope<T>`
- **Enums:** `DisasterType`, `Severity`, `IncidentStatus`, `MissionStatus`, `ReliefStatus`, `ResourceType`, `Roles`
- **Eventing:** `IEvent`, `EventBase`, `IEventHandler<T>`, `IEventBus`
- **Events:** `IncidentCreated`, `IncidentAssessed`, `IncidentVerified`, `MissionAssigned`, `MissionStatusChanged`, `ReliefRequested`, `ReliefStatusChanged`, `AlertPublished`, `AuthEvent`, `PingCreated`
- **Read models:** `IncidentSummaryDto`, `IncidentQuery`, `ShelterSummaryDto`, `HospitalSummaryDto`, `VolunteerSummaryDto`, `NgoSummaryDto`, `UserSummaryDto`, `AiAnalysisRequest`, `AiAssessmentDto`, `StoredFile`
- **Interfaces:** `IIncidentReadService`, `IShelterReadService`, `IRegistryReadService`, `IUserAdminService`, `IAiAnalysisService`, `IRealtimeNotifier`, `IFileStorage`

## 7. Decisions Log (append-only)

| ID | Date | Decision | Why |
|----|------|----------|-----|
| D-001 | 2026-09-01 | Stack: ASP.NET Core 8 modular monolith + Blazor WASM PWA + PostgreSQL + SignalR + Leaflet/OSM + Gemini-with-fallback | One language for 4 devs, $0 cost, offline + realtime supported, demo cannot fail on quota (plan §1.1) |
| D-002 | 2026-09-01 | Zero-blocking model: contracts-first + stubs + fake read services + per-owner DbContexts + no cross-module FKs | Hard requirement: no developer ever waits on another (plan §1.5) |
| D-003 | 2026-09-01 | This file is the single source of truth for implementation state; all agents read before / update after any change | Keeps multi-agent, multi-dev work consistent |
| D-004 | 2026-09-01 | Stay on .NET 8 | D-001 froze the stack; all package pins are validated for 8.x; EOL (2026-11-10) is irrelevant for a graded local demo ending ~Week 13, while migrating means 4 SDK installs + redoing all research under deadline |
| D-005 | 2026-09-01 | docker-compose is the documented primary DB path; Neon/Supabase free tier is the documented fallback (override via `ConnectionStrings__Postgres` env var or user-secrets); startup is warn-and-continue-degraded — `MigrateAsync` retries 3× then logs a prominent warning, sets `DatabaseHealth.PostgresAvailable=false`, app keeps serving (stub-backed pages work; DB-backed endpoints return 503 ProblemDetails; `/health` reports it) | Consistent with rule §4.8 ("demo must never depend on network"); a dev with no Docker/Postgres still runs the app against stubs, and all F0 tests are provable via the SQLite factory alone |
| D-006 | 2026-09-01 | Hand-rolled in-process event bus (~50 lines) instead of MediatR notifications (deviation from plan §8.6) | MediatR is commercial from v13 (accidental `dotnet add package` = license risk for students); we need only pub/sub notifications, and a zero-dependency bus with per-handler try/catch gives exactly the plan's "missing subscriber breaks nothing" semantics |
| D-007 | 2026-09-01 | F0 ships the per-context infrastructure pattern + exactly ONE concrete context (`SampleDbContext`); `AuthDbContext`/`IncidentsDbContext`/`OpsDbContext`/`ReliefDbContext`/`AiDbContext` arrive with their owning features copying the proven pattern; consequently ASP.NET Identity + seeded Identity users defer to F1's first PR (FakeAuth covers all 4 roles until then) | Empty contexts are ceremony that forces Tayeb to scaffold inside teammates' lanes (violates §4.7); one real context proves history-table naming, `feature_` prefix, `--context`/`--output-dir` usage, and startup migration orchestration — everything owners need to copy |
| D-008 | 2026-09-01 | Sample slice = `Features/Sample` "Ping": `POST /api/sample/pings` (Admin policy, FluentValidation) persists `Ping` to `sample_pings` via `SampleDbContext`, publishes `PingCreated` contract event consumed by a logging handler in the same slice; `GET /api/sample/pings` (anonymous, paged envelope); Blazor page `/sample` posts+lists via the dev-role header; full integration test via SQLite factory | One tiny slice exercises every foundation mechanism (module self-registration, per-context migrations, envelope, validation, auth policy + FakeAuth, event bus, client page, test factory) — the literal copy-me template plan §8.1 demands |

## 8. Changelog (append-only, newest first)

- **2026-09-01** — F0 chunk 1 implemented (TDD, 17/17 tests green): blueprint committed to docs/architecture/F0-blueprint.md; global.json + Directory.Build.props + solution with 3 src / 2 test projects; full Contracts v1 in `RapidRelief.Shared/Contracts` (B2, incl. `IEventBus` + `PingCreated`); scoped `InProcessEventBus` (D-006); `IFeatureModule` + reflection `ModuleDiscovery` (deterministic Order-then-Name sort); MultiAuth policy scheme (JwtBearer + SignalR access_token hook + FakeAuth for Dev/Testing with fixed seed GUIDs); role policies; Serilog + ProblemDetails + status-code pages + rate limiter (named auth/reports policies, skipped in Testing) + FluentValidation registration; `FoundationModule` (`/api/foundation/whoami` [Authorize], `/health`); hosted Blazor WASM PWA shell served by Api; `.config/dotnet-tools.json` (dotnet-ef 8.0.30); CI build-test workflow. Package pins exactly per blueprint B8. Next: chunk 2 (EF/Postgres pattern + Sample slice + SQLite test factory).
- **2026-09-01** — Repo created. Development plan + project context authored. No code yet. Next: F0 (Week 1).

## 9. How to Update This File

- After your PR merges to `main`: update your feature's **status row** (+ one-line note), add a **changelog line**, and append any new **decision** (D-NNN) — never rewrite history.
- Keep entries terse. Details belong in the plan, PRs, or code — this file is the *index of truth*, not the archive.
- If reality diverges from the plan (scope cut, owner swap, contract change), record it here **in the same PR** that makes the change.
