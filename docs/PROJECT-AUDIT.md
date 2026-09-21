# RapidRelief — Repository Audit & Master Execution Plan

**Audit date:** 2026-09-03 · **Auditor:** autonomous architecture/QA pass · **Commit:** `0f161c9` (branch `main`, clean tree at audit start)

> This document is an **evidence-based** audit. Every status below was verified by building the
> solution, running the full test suite, running the application against its real database, and
> probing the live HTTP surface — not by reading documentation. Where this audit contradicts
> [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) or the [development plan](RapidRelief-Development-Plan.md),
> **this document is the accurate one** and PROJECT-CONTEXT §3 has been corrected accordingly.

---

## 0. Status after implementation (2026-09-03)

The P0 critical path, the core P1 items and the whole citizen journey have been **implemented and
verified live against Postgres**. Sections 1-18 below are the original audit; resolved items are
marked in place.

### 0a. Core loop

```
citizen report (SOS) ──► IncidentCreated ──► AI worker ──► priority 100/100 projected onto the incident
   ──► government verifies ──► rescue queue (SOS first) ──► rescuer accepts → mission assigned
   ──► EnRoute → OnScene → Completed ──► incident Resolved ──► citizen notified at every actionable step
```

| Step | Live result |
| --- | --- |
| `POST /api/incidents` (Citizen, SOS) | `201`, persisted with a timestamped receipt entry |
| AI pipeline | `priorityScore = 100`, `aiSummary = "Flood assessed at severity 3/5 with SOS flag…"` |
| `GET /api/rescue/queue` (Rescuer) | SOS first, ahead of the 92/82-priority demo incidents |
| Verify → assign → EnRoute → OnScene → Completed | `200` at every step; personal unit auto-provisioned |
| Incident after completion | `Resolved`, `resolvedAtUtc` set, six-entry timeline in order |
| Authorization | Citizen → rescue/relief triage `403`; Rescuer → user admin `403`; anonymous `401` |

**Delivered:** F2 Incidents (create/media/list/mine/detail/verify + real `IncidentReadService`),
F5 Rescue (queue/missions/teams + guarded state machine), event projections, `ShelterSeeder` +
`IncidentSeeder`, client rewired off mock data, CI fidelity for all nine contexts.

**Defects fixed while building:** SQLite `DateTimeOffset` ordering, EF child-row `Modified` state,
dead `js/shelters-geo.js`.

### 0b. Citizen workflow

| Stage | Delivered | Live evidence |
| --- | --- | --- |
| **Understand the situation** | `/c` rebuilt around six priorities (SOS, report, my active incident, shelter, relief, notifications) with a plain-language situation line and the active-alert banner | Reads live incidents + relief counts |
| **SOS** | Two-step arm → confirm (no accidental firing), automatic GPS, `Catastrophic` severity, real incident + timestamped receipt; offline SOS is queued with a "call 999 now" instruction | `201` → AI priority 100/100 |
| **Report** | Four short steps: what → where (GPS + **tap-the-map pin adjust**) → photo/video → send; live character count, validation, progress and error states | `POST /api/incidents/media` + create with `photoPaths` |
| **Status** | Six-stage timeline **Submitted → Verified → Assigned → En route → On site → Resolved**, per-stage timestamps, citizen-language wording (`MissionStage` + a timeline row per mission stage) | All six rows in order in the live run |
| **Shelter** | Suitability ranking (distance + free capacity + facilities), occupancy bar, free-space count, facilities, reasons and directions links; full/closed shelters are never recommended | `GET /api/shelters/recommendations` → "4.6 km away · 280 spaces left · has water, medical, food" |
| **Relief** | Full F4 vertical: create / mine / detail / cancel + Government triage; stages **Requested → Accepted → Preparing → Dispatched → Delivered** behind a guarded state machine | Live run walked all four transitions |
| **Notifications** | Only actionable events reach the citizen — the AI-triage message was removed as noise. 5 per rescue, 4 per relief request | Live inbox contained exactly those |
| **Offline** | IndexedDB outbox: stored **before** the first network attempt; explicit Online / Offline / Saved on this device / Waiting to sync / Synced / Sync failed states; auto-sync on reconnect; idempotency key stops duplicates; failed items listed with a discard action — **never silently lost** | Replay with the same key returned the original incident; one row in the database |

**Tests:** `680 passed, 0 failed, 2 skipped` (668 API incl. 12 new lifecycle/citizen tests + 12 architecture). **Build:** 0 warnings.

### 0c. Rescue operations

The rescue vertical was completed as a full operational console: **priority incident → assignment →
navigation → live mission → resolution**, with every guard enforced server-side.

| Stage | Delivered | Live evidence |
| --- | --- | --- |
| **See what matters** | `GET /api/rescue/dashboard` returns severity **bands** (Critical/High/Medium/Low), the critical list, nearby calls by distance, mission counts and the caller's team in one round-trip; `/r` renders clickable band cards that filter the queue | `bands {Critical:2, High:4, Medium:4, Low:3}`, `critical:2 nearby:5` |
| **Prioritised queue** | `GET /api/rescue/queue?band=&lat=&lng=` — SOS and priority score first, distance from the responder, band filter | Critical SOS surfaced at the top: `band=Critical dist=0.11km priority=100` |
| **Right team, not any team** | `TeamSuitabilityScorer` = `0.45×availability + 0.30×proximity + 0.15×load + 0.10×speciality`; off-duty teams are excluded and every candidate carries human-readable reasons | `Unit rescuer1@rr.dev [Available] free now · position unknown` |
| **Assignment** | `POST /api/rescue/missions` (Responder) with conflict guards; `POST /{id}/reassign` (Government only) moves the incident to the new mission | assign `200`; duplicate assign `409`; rescuer reassign `403` |
| **Accept / reject** | `POST /{id}/accept` stamps `AcceptedAtUtc`; `POST /{id}/reject` cancels the mission with a reason, **requeues the incident** and frees the team | reject → mission `Cancelled`, incident back to `Verified`, `assignedTeamId` cleared, team `Available` |
| **Navigation** | Incident detail `/r/incidents/{id}` shows situation, callback number, RapidMap pin and directions links; responders (and only responders/owner) see `contactPhone` | rescuer read `+8801799887766`; citizen feed hides it |
| **Live mission** | Four-stage HUD (Assigned → En route → On site → Completed) with per-stage timestamps, one-tap advance, team status selector and position sharing (`POST /teams/mine/position`, `/teams/mine/status`) | `accepted/started/onScene/completed` stamped in order |
| **Guarded state machine** | Forward-only transitions; deployed teams cannot take a second mission; a team on a mission cannot go off duty; unknown status strings are rejected | backwards transition `409`; off-duty-mid-mission `409`; `"Napping"` `400` |
| **Resolution & notification** | Completion resolves the incident, stamps `ResolvedAtUtc`, clears the mission stage and pushes the update to the citizen | incident `Resolved`, `missionStage=Completed`; citizen inbox `incidents.report.status` ×4; team inbox `rescue.mission.assigned` |

**Tests:** `688 passed, 0 failed, 2 skipped` (676 API incl. 8 new `RescueOperationsTests` + 12 architecture). **Build:** 0 warnings.

### 0d. Government command centre

`/g` was a mock: hard-coded KPIs (142 incidents, 48/60 units, 4,850 evacuees, 45,000 L), three
invented shelter cards and a broadcast modal that set a local boolean. It is now a real EOC whose
every figure originates from application data.

| Question the operator asks | Answered by | Live evidence |
| --- | --- | --- |
| **What is happening?** | `GET /api/incidents/ops/summary` — active / critical / SOS / unassigned / awaiting-team / in-progress / resolved-24 h / new-24 h, computed from `incidents_reports` | `active=22 critical=14 sos=4 unassigned=22 resolved24h=3 new24h=4 total=32` |
| **Where is it happening?** | `/g/map` — one map with **incidents, rescue teams, shelters and relief drop-offs** as toggleable layers plus an incident status/critical filter | Layer counts render from the same feeds the other pages use |
| **How serious is it?** | Severity banding and distribution (`bySeverity`, `byType`, `byStatus`) | `Flood=15 Fire=4 BuildingCollapse=3 Cyclone=3 Other=3 Earthquake=2 Landslide=2` |
| **Who needs help?** | "Needs a decision now" panel — unassigned incidents, SOS and AI priority first, with inline Verify | Fed by `GET /api/incidents?unassigned=true` |
| **Which teams are available / deployed?** | `GET /api/rescue/teams` with live mission counts, grouped Available → Dispatched → Off duty | Counts match the registry |
| **Which shelters have capacity?** | `GET /api/shelters` sorted fullest-first with occupancy bars and total free spaces | Occupancy edits are audited and immediately visible |
| **What areas are becoming critical?** | Hotspots: open incidents grouped by area with a last-6 h vs previous-6 h trend (`Escalating`/`Steady`/`Easing`/`Quiet`) | `Sector 3[Escalating] 2/2crit · Block D[Escalating] 1/1crit · Dhaka demo dataset[Quiet] 21/13crit` |
| **Average response time** | `CreatedAtUtc → AssignedAtUtc` across all dispatched incidents; resolution is `CreatedAtUtc → ResolvedAtUtc` | `avgResponse=812.8min rate=25%` after live dispatches |

**Incident management** (`/g/incidents`) — debounced full-text search (`q=`), status / disaster-type /
severity / SOS / unassigned filters, four sort orders, a map view of the *filtered* set, inline
verify and reject, dispatch hand-off to `/r/incidents/{id}`, and a new Government close-out
`POST /api/incidents/{id}/resolve` that **refuses with `409` while a mission is live** so the
incident and its mission can never disagree.

**Analytics** (`/g/analytics`) — incidents-over-time (reported vs resolved as an inline SVG, no chart
dependency), disaster distribution, severity distribution, pipeline state (widest bar = bottleneck)
and geographic concentration with the escalation trend. Every chart is labelled with the operational
question it answers.

**Management** — users (`/g/users`: search, role filter, role editing, lock/unlock, self-action
guards), rescue teams (`/g/teams`: register, rename, re-speciality, duty status — refused mid-mission),
shelters (existing `/admin/shelters`), and **relief resources** (`/g/relief`: new
`GET|POST|PUT /api/relief/resources` warehouse inventory showing free stock against the **open demand
computed from citizen relief requests**, and naming supply types with no stock at all).

**Audit trail** — new `Features/Audit` slice: `AuditDbContext` → `audit_entries`, the new frozen
contract `IAuditTrail` (with a `NoOpAuditTrail` stub-yield fallback), six event projections and
`GET /api/audit` (Government-only, filterable by action / record type / actor / window / free text).
Records **who, what, when, which entity, and the result**. `audit_logs` inside `AiDbContext` (audit
finding T2) is superseded.

**Live trail from one command session:** `Team.Create`, `Team.Update`, `Resource.Create`,
`Incident.Verify`, `Mission.Assign`, `Shelter.Occupancy`, `Alert.Publish`, `Alert.Revoke`,
`User.Lock`, `User.Unlock` — 10 entries, each with actor, entity id and outcome.

**Connected, not isolated:** verifying in `/g` moves the incident to `Verified` in the rescuer's
queue; a team registered in `/g/teams` appears in the dispatch suitability ranking immediately;
locking an account in `/g/users` is reflected in the user list and the trail; a citizen's relief
request raises the open demand shown against warehouse stock; closing an incident notifies the
reporter. All of that is asserted by
`Command_decisions_actually_move_the_citizen_and_rescue_workflows`.

**Role boundaries (live):** `audit` → Rescuer `403`, Citizen `403`, anonymous `401`;
`relief/resources` → Rescuer `403`; `incidents/ops/summary` → Citizen `403`, Rescuer `200`;
`PUT /api/rescue/teams/{id}` → Rescuer `403`. `DELETE /api/auth/users/all` (audit finding B8) is now
refused outside Development/Testing and audited.

**Tests:** `697 passed, 0 failed, 2 skipped` (685 API incl. 9 new `CommandCentreTests` + 12 architecture). **Build:** 0 warnings.

### 0e. AI decision support

The AI slice already had a real OpenRouter transport, a circuit breaker and a rule-based fallback,
but its *output* was four fields (`predictedType`, `severity`, `summary`, `confidence`), its priority
was `20×severity + 25×SOS + recency`, and nothing it produced was explainable or labelled. It is now
a decision-support layer.

| Requirement | Delivered | Live evidence |
| --- | --- | --- |
| **Structured analysis** | Schema extended to `damageIndicators`, `estimatedPeopleAffected`, `medicalUrgency` and `reasoning` alongside the original four; the parser treats the new fields as **optional** so a terse model answer degrades instead of being rejected | `indicators: People trapped ("trapped") \| Injuries reported ("bleeding") \| Structural collapse ("rubble") \| Vulnerable people present ("children")` |
| **Evidence that survives an outage** | `IncidentSignalReader` extracts damage indicators, medical wording, head counts and escalation phrases deterministically; model output is **unioned** with it, and a reported head count is never lowered by a model guess | The live run above was produced entirely by the offline engine — no key configured |
| **Priority engine** | `IncidentPriorityEngine`: severity (confidence-damped), SOS, people affected (logarithmic), medical urgency, waiting time, location risk (open incidents within 2 km), and rescue capacity via the new `IResponderAvailabilityService` contract | `+50.1 Assessed severity · +20 SOS raised · +5.3 People affected · +12 Medical urgency` = `87.4 Critical` |
| **Explainability** | Every factor carries a `Code`, `Label`, `Points` and the **evidence** that earned it, plus a one-line explanation built from the top factors | "Scored 87/100 (Critical) because of catastrophic (5/5) at 45% confidence + the reporter pressed the emergency button + injuries" |
| **Labelled as decision support** | `AiInsightDto.Disclaimer` + `IsDecisionSupport`; the client `AiInsightPanel` renders a dashed, visually distinct block with an "AI · decision support" badge and the disclaimer, and `/reports/my` says "AI estimate (decision support, not confirmed)" | `decisionSupport=True` |
| **Duplicate detection** | Geographic proximity + time proximity + disaster type (hard gates) then a **confidence score** blending proximity, recency and Jaccard description overlap; a normalised description fingerprint is stored per assessment | `confidence=0.922 — 15 m apart; reported within the same minute; same disaster type; descriptions share 56% of their words` |
| **Never auto-delete** | Flags are advisory. `GET /api/ai/duplicates` is the review queue; Government-only confirm/dismiss records a verdict and an audit line but **does not close either report** | After `Duplicate.Confirmed`, both incidents remained `status=0, resolved=False` |
| **AI failure resilience** | Per-attempt timeout (D-026) **plus retry with exponential backoff and jitter** on transient failures only (timeout, network, 429, 5xx — never 403 or 4xx), the existing 3-fail circuit breaker, and a structured rule-based fallback that always fills the same shape. Every fallback names its `DegradedReason` | `429` → 2 attempts then fallback; `400` → 1 attempt; `500` that recovers → body returned |
| **Emergency reporting never blocked** | The analyser runs on a bounded background channel; the report is already committed and answered `201` before analysis starts | Reports kept succeeding with no provider configured throughout the live run |
| **Authorized assistant** | Role-scoped context built **server-side from the validated token**: citizens keep shelters + alerts; responders additionally get open/critical incidents near them and rescue capacity; the command centre also gets the disaster-type breakdown. The block is fenced untrusted data and the system rule now forbids naming an incident, team, count or capacity that is not in it | Citizen prompt contains no `Operational picture`, no `Rescue capacity:`, no incident detail — even when the question demands it |

**New surface:** `GET /api/ai/insights/{incidentId}` (any authenticated role),
`GET /api/ai/duplicates` (Responder), `POST /api/ai/duplicates/{id}/confirm|dismiss` (Government),
and the `/g/duplicates` review page. Migration `AiDecisionSupport` adds the confidence, urgency,
band, indicators, reasoning, factor and duplicate-review columns.

**Role boundaries (live):** `/api/ai/duplicates` → Citizen `403`; confirm → Rescuer `403`, Government
`200`, repeat `409`; `/api/ai/insights/{id}` → anonymous `401`.

**Tests:** `731 passed, 0 failed, 2 skipped` (719 API incl. 10 `IncidentPriorityEngineTests`, 18
`AiDecisionSupportTests` and 4 `AssistantRoleScopeTests` + 12 architecture). **Build:** 0 warnings.

### Still open

- **P0-1 (security):** the Neon Auth session exchange remains unverified and is refused outside Development/Testing; the committed Neon credential still needs rotating. **Top risk, unchanged.**
- **P2-4 Registry** (hospitals/volunteers/NGOs still stub-backed), **P2-5 dispatch records** (`relief_dispatches` has inventory but no dispatch/delivery rows), **P2-6 AI classification override** (explanation and duplicate review shipped §0e), **P2-7 permission matrix**, **P2-8 team layer on the shared citizen map** — untouched; §14 still applies.
- **Heatmap** rendering (as opposed to the hotspot table) is still absent; the concentration data now exists to drive it.

---

## How the audit was performed

| Check | Command / method | Result |
| --- | --- | --- |
| Build | `dotnet build RapidRelief.sln` | ✅ **0 errors, 0 warnings** (`TreatWarningsAsErrors=true`) |
| Tests | `dotnet test RapidRelief.sln` | ✅ **668 passed, 0 failed, 2 skipped** (656 API + 2 live-OpenRouter skips + 12 architecture) |
| Run | `dotnet run --project src/RapidRelief.Api` | ✅ Boots, applies **9** migration sets to Neon Postgres, listens on `http://localhost:5179`, `/health` → `{"status":"ok","dbConnected":true}` |
| Live API probe | `curl` against 20+ routes, anonymous + `X-Dev-Role` + minted JWT | See §5 and the matrix — 2 defects found and fixed, 1 critical vulnerability found |
| Static scan | `TODO/FIXME/mock/placeholder/hardcoded/NotImplemented`, secrets, blue-hex, dead JS | 1 real TODO, 3 dead files, 1 committed credential |
| Architecture guards | `RapidRelief.Architecture.Tests` (5 classes) | ✅ Slice isolation, contracts purity, DbContext ownership, render safety, log-leak guards all green |

---

## 1. Current architecture

**Shape:** a single ASP.NET Core 8 host (`RapidRelief.Api`) that serves both the REST API and a
hosted **Blazor WebAssembly PWA** (`RapidRelief.Client`), with a shared contracts kernel
(`RapidRelief.Shared`). It is a **modular monolith with vertical slices**, not layered MVC.

```
RapidRelief.sln
├── src/RapidRelief.Api             ASP.NET Core 8 · Minimal APIs · Serilog · FluentValidation
│   ├── Features/<Feature>/         self-registering IFeatureModule: Endpoints + Domain + Data(+Migrations) + Services
│   │   Ai · Alerts · Auth · Foundation · Incidents · Realtime · Relief · Rescue · Sample · Shelters · Stubs
│   └── Infrastructure/             Auth (MultiAuth/JWT/FakeAuth/policies) · Eventing (in-process bus)
│                                   Modules (discovery) · Persistence (MigrationRunner, DatabaseHealth)
│                                   RateLimiting · Storage (LocalDiskFileStorage)
├── src/RapidRelief.Client          Blazor WASM · Features/* pages · Common/{Auth,Geo,Map,Realtime,Ui} · wwwroot
├── src/RapidRelief.Shared          Contracts v1: Common · Enums · Events · ReadModels · Services (interfaces)
└── tests/                          RapidRelief.Api.Tests (65 files) · RapidRelief.Architecture.Tests (5 guards)
```

**Mechanisms that are genuinely in place and working**

- **Module discovery** — every feature implements `IFeatureModule` (`AddModule` / `MapEndpoints` / `MigrateAsync`); the host discovers and orders them deterministically. Adding a feature touches no shared file.
- **Per-owner DbContext + migration history** — 9 contexts, each with `__efmigrationshistory_<owner>` and its own migrations folder. No cross-module FKs; cross-module references are plain `Guid`s.
- **Degraded startup (D-005)** — migrations retry, then the app keeps serving with `DatabaseHealth.PostgresAvailable=false`; DB-backed endpoints answer 503 ProblemDetails.
- **In-process event bus (D-006)** — scoped `IEventBus`, per-handler try/catch, "missing subscriber breaks nothing".
- **MultiAuth** — JWT bearer (30 min) + rotating refresh cookie (7 d absolute, reuse ⇒ family revoke) + `FakeAuthHandler` (`X-Dev-Role`, Development only — verified by the startup warning and by 403 responses under a Citizen dev role).
- **Cross-cutting policy** — global + 6 named rate-limit policies, ProblemDetails everywhere, `no-store` on sensitive groups, `nosniff` globally, HSTS/HTTPS outside Development.
- **Client** — token design system (`app.css`), `AuthorizeRouteView` routing, Bearer handler chain with single-flight refresh, SignalR client with permanent polling fallback, vendored Leaflet + `RapidMap`, vendored Lexend, CSP meta with no `unsafe-inline` script.

**Dependency direction is correct**: `Features/*` → `Shared/Contracts` → (nothing). `Infrastructure` is feature-agnostic. Both facts are enforced at build time by `ModuleIsolationTests` and `ContractsPurityTests`.

**The structural problem is not the architecture — it is that three slices stop at the schema.**
`Incidents`, `Relief` and `Rescue` register a DbContext and migrate tables, but their
`MapEndpoints` bodies are empty comments. The citizen and rescuer pages that appear to use them
render hardcoded C# lists.

---

## 2. Completed functionality (verified end-to-end)

| Area | Evidence |
| --- | --- |
| **Foundation** | `GET /health` → 200 with real DB state; `GET /api/foundation/whoami` → 401 anonymous / 200 with role claims per `X-Dev-Role`; `GET /api/foundation/demo-incidents` → 200 with 28 seeded Dhaka incidents; unknown `/api/**` → ProblemDetails 404 (never the SPA shell) |
| **Contracts v1 + stubs** | `Shared/Contracts` compiles with zero framework dependencies (guard test); `Features/Stubs` resolves every contract interface via `TryAdd*`; DI smoke test pins the graph |
| **Authentication (password)** | Register/login/refresh/logout/profile + photo upload/read, 11 endpoints, `auth` rate limit, hashed refresh tokens with rotation + reuse detection, security-stamp checks; ~12 dedicated test files |
| **Role authorization** | Live probe: Citizen dev-role → `POST /api/sample/pings` **403**, `POST /api/alerts` **403**; Government → 200. Policies: `RequireGovernment` (aliases `RequireAdmin`/`RequireNgo`), `RequireRescuer`, `RequireCitizen` |
| **Admin user management (API)** | `GET /api/auth/users` returns real paged users with roles; lock, role-assign, delete endpoints exist and are Government-gated |
| **AI analysis engine** | `IncidentCreated` → bounded channel → `AiAnalysisWorker` → classification, severity, priority score, duplicate detection, persistence to `ai_assessments`, `IncidentAssessed` published. OpenRouter transport with model-pair fallback, circuit breaker, timeouts, blocked/unavailable classification, and a **permanent rule-based fallback** — ~18 test files incl. golden-pinned request bodies |
| **AI assistant (F16)** | `/api/ai/assistant` POST/GET/DELETE, server-owned history (`ai_assistant_messages`), sanitizer (control chars + URL stripping + clamp), canned safety taxonomy, per-user 12/300 s budget, `/assistant` page that never dead-ends |
| **Realtime + notifications** | `/hubs/notifications` SignalR hub (server-derived role groups), `notifications_*` store, cursor-paged inbox, unread count, read/read-all, retention sweep, bell + `/notifications` inbox + toasts, dedupe between push and 5 s/60 s polling, tri-state `Realtime:Mode` |
| **Broadcast alerts (F10)** | `POST /api/alerts` (Government) → persisted → `AlertPublished` → existing F9 delivery; `GET /api/alerts/active` verified 200; `/alerts/compose` UI + citizen banner |
| **Shelter CRUD API + admin UI** | `POST/PUT/PATCH /api/shelters*` Government-gated, `GET` anonymous, `/admin/shelters` page — **the code path works; the table is empty (see §5)** |
| **Design system & shell** | Token CSS (light/dark), pre-paint theme switch, `AppIcon`, `Rr*` primitives, responsive drawer shell, `/ui-showcase`; render-safety guard test forbids `MarkupString`/`innerHTML` |
| **Shared geolocation** | `GeolocationService` (never throws, typed failure + user-ready message) + map "you are here" layer, consumed by 3 pages |

---

## 3. Partial functionality

| Area | What exists | What is missing |
| --- | --- | --- |
| **Citizen dashboard `/c`** | Full HUD layout, SOS panel, safety toggle, alert banner (real, from F10) | Every incident/report number is a hardcoded `List<IncidentItem>` in the `.razor` file |
| **Rescuer HUD `/r`** | Layout, duty toggle, mission stepper, dispatch queue, equipment checks | No API at all — the queue is local mock data; no assignment, no status transitions |
| **Government dashboard `/g`** | Layout, triage table, telemetry grid, alert compose modal | Incident data is mock; verification, metrics and resource panels have no backend |
| **Shelter finder `/shelters/finder`** | Geolocation, map, distance sort, capacity meters, AI-recommendation call with graceful "no recommendation" | `ops_shelters` is **empty** in the dev database and there is no shelter seeder ⇒ the flagship citizen feature renders an empty map |
| **Incident map** | `RapidMap` + `/sample` page renders the 28 stub incidents | No real incident source; no rescue-team layer, no heatmap, no directions |
| **Realtime coverage** | Transport is production-grade | Only two topics are ever published (`ai.incident.assessed`, `alerts.published`). Assignment/mission/relief updates have no publisher |
| **Offline/PWA** | `manifest.json`, `service-worker.js` + `service-worker.published.js` (framework template), install-ability | No offline report capture, no IndexedDB queue, no sync engine, no sync status UI |
| **Audit trail** | `audit_logs` table exists (inside `AiDbContext`) | No writer, no reader, no UI, wrong owner |
| **Permission matrix** | `auth_permissions` + `auth_role_permissions` seeded with 23 rows incl. page routes | Never enforced anywhere (only role policies are), and it advertises routes that do not exist (`/admin/analytics`) |

---

## 4. Missing functionality

Not present in any form (no endpoint, no service, no UI wiring):

- **Incident ingestion** — `POST /api/incidents`, media upload, status machine, verification, timeline. The `incidents_*` tables are empty scaffolding. This blocks the product's core loop.
- **Rescue operations** — priority queue, assignment/reassignment, mission acceptance and `En route → On site → Completed`, team registry, team location/status, navigation links.
- **Relief pipeline** — request submission, triage/approval, inventory, allocation, dispatch, delivery status.
- **Analytics** — heatmaps, response-time metrics, KPI dashboards, resource monitoring.
- **Registry** — hospitals, volunteers, NGOs (stub read service only).
- **Offline sync** — creation, local persistence, queue, reconnection sync, idempotency keys.
- **AI human override** — the assessment is now fully explained and duplicate flags are reviewable (§0e), but there is still no endpoint or UI to accept, reject or adjust the classification itself.
- **Directions/navigation** — no external maps hand-off from any page.
- **Admin UI for user management** — the API is complete; there is no page that calls it.

---

## 5. Broken functionality (found at runtime)

| # | Defect | Evidence | Status |
| --- | --- | --- | --- |
| B1 | **Authentication bypass — `POST /api/auth/oauth/google-session`** accepts a caller-supplied e-mail with **no provider-token verification** and mints a valid JWT + refresh cookie for that account. Proven live: an unauthenticated POST returned a 30-minute token with `"role":"Government"` for an existing Government account, and an unknown e-mail silently creates a new account. | Live request/response captured during this audit; code at `Features/Auth/Endpoints/AuthEndpoints.cs` → `GoogleSessionAsync` | 🟠 **Mitigated, not fixed** — now refused outside Development/Testing and rate-limited. A real fix (verify the Neon Auth session server-side) is **P0-1** |
| B2 | Any minimal-API **binding failure returned HTTP 500** with an unhandled-exception log, instead of 400. `GET /api/shelters/recommend?lat=..&lon=..` (a plausible client typo) produced a 500. | Serilog stack: `BadHttpRequestException: Required parameter "double lng" was not provided` | ✅ **Fixed** — `BindingFailureExceptionHandler` maps it to a 400 ProblemDetails; re-probed: 400 |
| B3 | **Test suite could not run at all on a machine without the .NET 10 x64 runtime** — `RollForward=LatestMajor` pushed the test host to a runtime that was not installed. | `dotnet test` aborted with "You must install or update .NET" | ✅ **Fixed** — `Directory.Build.props` now uses `RollForward=LatestMinor`; suite runs with no environment workaround |
| B4 | **Shelter data is empty** — `ops_shelters` has 0 rows, so `/api/shelters` returns an empty page and `/api/shelters/recommend` legitimately 404s. `SheltersModule` registers the real `ShelterReadService`, which **displaces** the 8-shelter stub, so the assistant's shelter context and the citizen finder are both empty. | `SELECT ... FROM ops_shelters` returned 0 rows; `GET /api/shelters` → `totalCount: 0` | ✅ **Fixed** — `ShelterSeeder` seeds the 8 Dhaka shelters into an empty table |
| B5 | **Citizen report + SOS submit do nothing.** `SubmitIncidentReport()` sets `_submitted = true`; `TriggerInstantSos()` sets a flag and captures GPS locally. No HTTP call, no persistence, no event. The UI then shows a success/ticket state. | `Features/Reports/Pages/ReportNew.razor` lines ~236-244 | ✅ **Fixed** — both call `POST /api/incidents`; the ticket id is the real incident id and failures surface as errors |
| B6 | **Coordinates were formatted with the browser's culture** in `SheltersClient` (`lat=23,81` under e.g. `de-DE`) — a latent 400/500 for non-English users. | `$"?lat={lat.Value}&lng={lng.Value}"` | ✅ **Fixed** — invariant formatting |
| B7 | `GET /api/hero-images` returns a **bare JSON array** (envelope violation) and resolves its folder by walking `..\RapidRelief.Client\wwwroot` — a source-tree path that only exists in a dev checkout. | Live response `["hero images/..."]`; `FoundationModule.cs` | ❌ Open — P3 |
| B8 | `DELETE /api/auth/users/all` deletes **every** user account behind a single Government-role check — no confirmation token, no soft delete, no audit record. | `UserAdminEndpoints.cs` | ❌ Open — P1 (see §7 S8) |
| B9 | *(found while implementing the core loop)* SQLite cannot `ORDER BY` a `DateTimeOffset` column, and EF marked audit rows added through a tracked parent's collection as `Modified` — both would have 500'd the new endpoints. | Test-run stack traces | ✅ **Fixed** — see §0 |

---

## 6. Implementation matrix (required capabilities vs. reality)

Legend — **COMPLETE** = works end-to-end · **PARTIAL** = real code, incomplete path · **PLACEHOLDER** = UI/schema only, no behaviour · **BROKEN** = present but defective · **MISSING** = absent.

### Authentication & authorization

| Feature | Status | Evidence | Quality | Problems | Priority |
| --- | --- | --- | --- | --- | --- |
| Registration | COMPLETE | `POST /api/auth/register` + `/register`; Citizen hard-assigned | High | — | — |
| Login / logout | COMPLETE | `POST /api/auth/login`, `/logout`; uniform 401s; timing-equalised dummy hash | High | — | — |
| Google sign-in | BROKEN | `POST /api/auth/oauth/google-session` | **Critical** | Unverified identity ⇒ full bypass (B1) | **P0** |
| Role-based authorization | COMPLETE | Live 403/200 matrix across 3 roles | High | Policy aliases (`RequireAdmin` = Government) are confusing | P3 |
| Citizen / Rescue / Government roles | COMPLETE | `Roles.cs`, `AuthSeeder` fixed GUIDs, permission matrix seeded | High | Matrix is seeded but never enforced | P2 |
| Secure sessions | COMPLETE | 30 min JWT + 7 d rotating refresh cookie, reuse ⇒ family revoke, stamp checks | High | Lock takes ≤31 min to affect live tokens (documented D-020) | P3 |
| Protected routes | COMPLETE | `AuthorizeRouteView`, `AuthorizeView`, redirect-to-login | High | — | — |

### Citizen

| Feature | Status | Evidence | Quality | Problems | Priority |
| --- | --- | --- | --- | --- | --- |
| Citizen dashboard | PARTIAL | `/c` renders; alert banner is real | Medium | All incident/report figures are hardcoded lists | P1 |
| Disaster reporting (type, description) | PLACEHOLDER | `/reports/new` form exists | Low | Submit writes nothing (B5) | **P0** |
| GPS location | COMPLETE | `GeolocationService` + `js/geolocation.js`, retry + friendly failures | High | Result is only rendered as text | P2 |
| Manual location adjustment | PARTIAL | Free-text address field | Low | No map pin-drop, although `RapidMap.OnMapClick` exists | P1 |
| Photo / video upload | MISSING | Only profile-photo upload exists | — | AI vision path has no photo source | P1 |
| SOS | PLACEHOLDER | Button + confirmation UI | Low | Broadcasts nothing (B5) | **P0** |
| My reports | PLACEHOLDER | `/reports/my` with 4-stage stepper | Low | Hardcoded `List<UserReport>` | P1 |
| Report timeline / status | PLACEHOLDER | Stepper UI only | Low | No status source | P1 |
| Shelter finder | PARTIAL | Real API + map + distance | Medium | Zero shelter rows (B4) | **P0** |
| Shelter information | PARTIAL | Capacity/facilities modelled | Medium | No data | P1 |
| Relief requests | PLACEHOLDER | `/relief/request` multi-item form | Low | No API call; `ReliefModule` maps nothing | P1 |
| Notifications | COMPLETE | Hub + inbox + bell + toasts + polling fallback | High | — | — |
| Offline reporting | MISSING | Service worker is the stock template | — | No queue, no sync | P2 |

### Rescue

| Feature | Status | Evidence | Quality | Problems | Priority |
| --- | --- | --- | --- | --- | --- |
| Rescue dashboard | ✅ RESOLVED §0c | `GET /api/rescue/dashboard` + `/r` console: severity bands, critical list, nearby, mission counts, live refresh | High | — | — |
| Priority incident queue | ✅ RESOLVED §0c | `GET /api/rescue/queue?band=&lat=&lng=` — SOS + AI priority first, distance, band filter | High | — | — |
| Incident details | ✅ RESOLVED §0c | `/r/incidents/{id}`: situation, AI summary, media, timeline, callback number, suitable teams | High | — | — |
| Assignment | ✅ RESOLVED §0c | `POST /missions` + accept/reject/reassign, conflict-guarded (`409`) | High | Reassign is Government-only by design | — |
| Mission acceptance / En route / On site / Completed | ✅ RESOLVED §0c | Forward-only state machine with `AcceptedAtUtc`/`StartedAtUtc`/`OnSceneAtUtc`/`CompletedAtUtc`; publishes `MissionAssigned`/`MissionStatusChanged` | High | — | — |
| Victim location | ✅ RESOLVED §0c | Incident coordinates + map pin + distance on queue and detail | High | — | — |
| Rescue-team location / status | ✅ RESOLVED §0c | `POST /teams/mine/position`, `POST /teams/mine/status`; status guarded against mid-mission off-duty | High | Team layer on the shared map still pending | P2 |
| Navigation | ✅ RESOLVED §0c | Directions hand-off from the incident detail page | High | — | — |
| Real-time updates | ✅ RESOLVED §0c | `rescue.mission.assigned` + `rescue.operations.updated` topics; dashboard subscribes and refreshes | High | — | — |

### Government / Admin

| Feature | Status | Evidence | Quality | Problems | Priority |
| --- | --- | --- | --- | --- | --- |
| Command dashboard | ✅ RESOLVED §0d | `/g` on `GET /api/incidents/ops/summary` + teams + shelters + relief — every KPI computed from rows | High | — | — |
| Incident monitoring | ✅ RESOLVED §0d | `/g/incidents`: search, status/type/severity/SOS/unassigned filters, 4 sorts, map view of the filtered set | High | — | — |
| Incident verification | ✅ RESOLVED §0d | Inline verify/reject on `/g` and `/g/incidents`; `POST /{id}/resolve` closes calls with no live mission (`409` if one is running) | High | — | — |
| User management | ✅ RESOLVED §0d | `/g/users`: search, role filter, role editing, lock/unlock, self-action guards; `DELETE /users/all` now Development/Testing-only and audited | High | — | — |
| Rescue team management | ✅ RESOLVED §0d | `/g/teams` over `POST`/`PUT /api/rescue/teams`; duty status refused mid-mission | High | — | — |
| Shelter management | COMPLETE (API+UI) | `/admin/shelters` + CRUD, seeded, occupancy edits audited | High | — | — |
| Analytics / heatmaps / response metrics | ✅ MOSTLY RESOLVED §0d | `/g/analytics`: incidents-over-time, disaster/severity distribution, pipeline state, geographic concentration, response and resolution times | High | Map **heatmap layer** still to render (data now exists) | P2 |
| Resource monitoring | ✅ RESOLVED §0d | `/g/relief` inventory: stock, committed, free, and open demand computed from citizen requests; supply types with zero stock are named | High | Dispatch/delivery records (F11) outstanding | P2 |
| Audit trail | ✅ RESOLVED §0d | `Features/Audit` slice: `audit_entries`, `IAuditTrail` contract, six event projections, Government-only `GET /api/audit` with filters | High | — | — |

### AI

| Feature | Status | Evidence | Quality | Problems | Priority |
| --- | --- | --- | --- | --- | --- |
| Disaster classification | ✅ RESOLVED §0e | Rule-based + OpenRouter, golden-tested, triggered by `IncidentCreated` on every real report | High | — | — |
| Severity estimation | ✅ RESOLVED §0e | Same pipeline, with the model's severity confidence-damped in the priority engine | High | — | — |
| Damage / image analysis | COMPLETE | Vision model pair, first photo, data-URL part, text-only degradation | High | The report UI uploads photos (§0b) but the vision path needs a live key to exercise | P2 |
| AI incident summary | ✅ RESOLVED §0e | Persisted and rendered in the labelled `AiInsightPanel` on the responder detail page | High | — | — |
| Priority recommendation | ✅ RESOLVED §0e | `IncidentPriorityEngine`: severity, SOS, people, medical urgency, waiting time, location risk, rescue capacity — each with evidence | High | — | — |
| Duplicate detection | ✅ RESOLVED §0e | Proximity + time + type gates, then a confidence score including description overlap; Government review queue, never auto-deleted | High | Media similarity not attempted (no perceptual hashing) | P3 |
| AI fallback | ✅ RESOLVED §0e | Key-less ⇒ zero external calls; retry-with-backoff on transient failures, breaker, structured rule-based fallback that names its degraded reason | Excellent | — | — |
| AI confidence / explanation | ✅ RESOLVED §0e | Confidence, urgency band, damage indicators, reasoning and scored priority factors — rendered with the decision-support disclaimer | High | — | — |
| Human override | MISSING | — | — | No endpoint/UI; duplicate flags *are* reviewable, but the classification itself is not overridable | P2 |

### Maps · Real-time · Offline · Relief

| Feature | Status | Notes | Priority |
| --- | --- | --- | --- |
| Citizen location | COMPLETE | Shared service + user dot with accuracy halo | — |
| Incident locations | PARTIAL | Stub incidents on `/sample` only | P1 |
| Rescue-team locations | PARTIAL | Position reporting live (`/teams/mine/position`); no team layer on the shared map yet | P2 |
| Shelter locations | PARTIAL | Component ready, table empty | **P0** |
| Nearby shelter search / distance | COMPLETE (logic) | Haversine, top-N, open-capacity filter | — |
| Directions | MISSING | No maps hand-off | P2 |
| Heatmap | MISSING | — | P2 |
| Incident / assignment / rescue-status realtime | ✅ RESOLVED §0c | Incident, mission-assigned, mission-status and relief-status topics all published and consumed | — |
| Notification realtime | COMPLETE | Push + polling dedupe | — |
| Admin dashboard realtime | MISSING | Dashboards do not subscribe | P2 |
| Offline creation / persistence / queue / sync / status | MISSING | PWA shell only | P2 |
| Relief food / water / medicine / shelter requests | PLACEHOLDER | Form only | P1 |
| Resource inventory / allocation / dispatch / delivery | MISSING | Tables only | P2 |

**Score:** of the ~60 required capabilities, **19 are COMPLETE**, **13 PARTIAL**, **8 PLACEHOLDER**, **1 BROKEN-critical**, **~19 MISSING**. The completed set is disproportionately *infrastructure*; the missing set is disproportionately *the product*.

---

## 7. Technical debt & architectural problems

| # | Problem | Impact | Priority |
| --- | --- | --- | --- |
| T1 | Three slices (`Incidents`, `Relief`, `Rescue`) ship schema with **no endpoints**; their pages fabricate data inline instead of consuming the stub services the architecture provides | The "zero-blocking stub" model was bypassed — swapping in a real API later means rewriting every page, not flipping a DI registration | P0/P1 |
| T2 | `audit_logs` lives in **`AiDbContext`** | Violates per-owner context ownership; F14's owner cannot add columns without editing another lane's context | P2 |
| T3 | `notifications_broadcasts` + `BroadcastAlert` are unused F9 scaffolding superseded by F10 | Dead schema and dead code in a shared table family | P2 |
| T4 | Client dashboards use **ad-hoc status/severity strings** instead of `Shared/Contracts` enums | Guarantees inconsistent status vocabularies once the real API lands | P1 |
| T5 | Permission matrix (`auth_permissions`) is seeded but **never enforced**, and references routes that do not exist | Dead security feature that looks like a control | P2 |
| T6 | `Incidents/Relief/Rescue` modules skip DbContext registration under `Testing` | Their schema is never exercised by any test | P1 |
| T7 | CI `postgres-fidelity` covers **4 of 9** contexts | Ops/Alerts/Incidents/Relief/Rescue migrations are only proven on SQLite | P1 |
| T8 | `GoogleInitAsync` swallows every exception and returns a hardcoded provider URL | Silent failure; hardcoded infrastructure identity in source | P1 |
| T9 | `ShelterReadService.GetNearestAsync` loads the **entire** `ops_shelters` table and sorts in memory | Acceptable at demo scale (documented), but there is no geo index and no cap | P3 |
| T10 | Dead assets: `wwwroot/js/shelters-geo.js`, `wwwroot/js/auth-helper.js`, plus the `Sample` slice and `/ui-showcase` exposed in the citizen nav | Confusion and demo noise | P3 |
| T11 | `GET /api/hero-images` breaks the response-envelope convention and depends on a source-tree relative path | Inconsistent API + broken in published output (B7) | P3 |
| T12 | `docs/architecture/*` blueprints describe the pre-OpenRouter, pre-3-role design | Already labelled historical, but they are the first thing a new dev opens | P3 |

**Not problems** (checked and cleared): dependency direction, business logic leaking into a "controller" layer (endpoints are thin, services hold logic), N+1 queries (role lookups are batched with `ANY(@ids)`, notification counts are indexed), async usage (no sync-over-async, `CancellationToken` threaded consistently), exception handling in the AI/realtime paths (never throws at publishers), logging (Serilog with query strings excluded — guard-tested).

---

## 8. Security issues

| # | Severity | Issue | Action |
| --- | --- | --- | --- |
| **S1** | 🔴 **Critical** | **Authentication bypass** via `POST /api/auth/oauth/google-session` (B1). Anyone could obtain a Government-role token for any account, or create arbitrary accounts. | Mitigated this pass (dev-only + rate-limited). **Must** be fixed properly: verify the Neon Auth session/JWT server-side (signature, issuer, audience, expiry, e-mail-verified claim) before minting a session. Until then, treat every account in the dev database as compromised. |
| **S2** | 🔴 **Critical** | **Live database credential committed** in `src/RapidRelief.Api/appsettings.Development.json` (Neon host, user, password) and present in git history. | Rotate the Neon password now; move to `dotnet user-secrets` / `ConnectionStrings__Postgres`; consider history scrubbing; add a pre-commit secret check. |
| **S3** | 🟠 High | Hardcoded third-party identity: the Neon Auth project URL appears in `AuthEndpoints.cs` (twice) and in the client CSP. | Move to configuration; fail closed if unset. |
| **S4** | 🟠 High | `DELETE /api/auth/users/all` wipes every account behind one role check (B8). | Remove it, or gate behind Development + explicit confirmation phrase + audit entry. |
| **S5** | 🟡 Medium | No `X-Frame-Options` / `frame-ancestors`; CSP is a `<meta>` tag only (cannot express frame-ancestors) and is absent from API responses. | Add response-header CSP + `frame-ancestors 'none'` in the host. |
| **S6** | 🟡 Medium | `Auth:SeedDemoUsers` creates known-password accounts; the same configuration pattern points at a **shared cloud database**. | Keep default `false`; never enable against a shared/deployed DB; document in the run guide (done). |
| **S7** | 🟢 Low | `reports` rate-limit policy is defined but unused (no endpoints yet). | Apply it when F2 ingestion lands (D-011 requires it). |
| **S8** | 🟢 Low | Uploads accept `.jpg/.jpeg/.png/.webp` by extension + size cap, content type derived server-side, traversal-safe storage — **this one is done well**. | Extend the same discipline to incident media when it lands (magic-byte check recommended). |

Verified-good: no token in `localStorage`/`sessionStorage`; no `MarkupString`/`innerHTML`/markdown rendering (guard-tested); no secrets in logs; timing-equalised login; refresh-token reuse detection; `X-Dev-Role` gated to Development.

---

## 9. UX problems

| # | Problem | Why it matters |
| --- | --- | --- |
| U1 | **The report and SOS flows confirm success while doing nothing** (B5) | In an emergency product this is the worst possible failure mode: a user believes help was dispatched. Fix or clearly label as a demo mock. |
| U2 | Shelter finder shows an empty map and "no recommendation" | The flagship citizen journey looks broken to a grader (B4). |
| U3 | Dashboards display fabricated counts, ETAs and unit names | Any click-through during a demo exposes inconsistency (a mission that exists on `/r` but nowhere else). |
| U4 | Developer surfaces (`/sample`, `/ui-showcase`) are reachable from citizen navigation | Confuses the audience; `/sample` exposes a raw ping console. |
| U5 | Seeded permission rows advertise pages that 404 (`/admin/analytics`) | Navigating from a "permission" lands on NotFound. |
| U6 | Mock pages have no loading/empty/error states (they cannot fail) | The moment they are wired to an API, all three states must be added — plan for it. |
| U7 | Manual location adjustment is a text box, not a map pin | The map component already supports `OnMapClick`; the report page just doesn't use it. |

Positives worth protecting: consistent token-based visual language in light and dark, WCAG-first focus/contrast rules, never-dead-end assistant, honest degraded-mode wording, and a geolocation failure path that always offers a retry.

---

## 10. Database problems

1. **No shelter seed data** — the single most visible data gap (B4). Incidents/hospitals/volunteers/NGOs are seeded only in the *stub* layer, which the real Shelters service displaces.
2. **Dead schema** — `incidents_*`, `relief_*`, `rescue_*` (10 tables) migrate on every startup and are never read or written.
3. **Misplaced ownership** — `audit_logs` in `AiDbContext` (T2); `notifications_broadcasts` orphaned (T3).
4. **CI proves only 4 of 9 contexts** against real Postgres (T7).
5. **Shared cloud dev database** — every developer runs `Development` against the same Neon instance with a committed password: no isolation, no reset story, and any destructive endpoint (S4) affects everyone. The documented docker-compose path is not actually being used.
6. **No geo index** on `ops_shelters` / future incident tables; distance work is in-memory (fine at seed scale, a wall at real scale).
7. Positives: no cross-module foreign keys, per-context history tables, provider-portable model configuration (SQLite in tests / Npgsql in production), and `DateTimeOffset` handling proven by the fidelity job for the contexts it covers.

---

## 11. AI integration status

**Engineering quality: the strongest part of the repository.** Provider abstraction (`IOpenRouterClient`), shared circuit breaker, per-request timeouts with linked CTS, retry-with-backoff on transient failures only (§0e, D-108), three-way error classification, blocked-vs-failed distinction, golden-pinned request bodies, prompt-injection fencing, no PII in payloads or logs, and a permanent rule-based fallback that makes the demo network-independent.

**Product reality: the engine is disconnected.** The pipeline entry point is the `IncidentCreated`
event, and **no production code publishes it** — only tests do. Consequently:

- no incident is ever classified in a real run;
- `ai_assessments` stays empty, so `GET /api/ai/assessments/{id}` has nothing to return;
- the vision path can never fire, because no UI uploads incident media;
- AI shelter recommendations return nothing (no shelter rows);
- AI summaries/priority/duplicates are never rendered anywhere.

The assistant (F16) **is** reachable and works, including the canned/offline mode.

**One line of code in F2's ingestion endpoint (`await eventBus.PublishAsync(new IncidentCreated(...)))`) converts a dormant engine into the product's centrepiece.** That is the highest value-per-effort item in this audit.

---

## 12. Real-time status

- **Transport: COMPLETE and hardened.** Push-only hub, server-derived role groups, bounded connection registry, auth-event driven disconnects, tri-state mode (`Hub`/`PollingOnly`/`Off`), reconnect schedule, dev long-polling with `X-Dev-Role`, and a permanent 5 s/60 s polling fallback deduped by id. Degradation was verified in a real browser previously and is covered by ~15 test files.
- **Coverage: thin.** Exactly two topics exist (`ai.incident.assessed`, `alerts.published`). Nothing publishes incident lifecycle, assignment, mission status or relief status, so no dashboard updates live.
- **Dashboards do not subscribe** to anything — even the topics that do exist.

Effort to close: publishers are one line per event once the owning endpoints exist; the client needs a small per-topic subscription hook.

---

## 13. Offline status

**MISSING in substance.** `manifest.json` and both service workers exist (stock Blazor template:
the dev worker is a no-op, the published one does asset precaching), so the app is installable and
loads offline — but there is **no** offline report capture, **no** IndexedDB/local persistence of
domain data, **no** sync queue, **no** reconnection-triggered synchronisation and **no** sync-status
UI. The only reconnection logic in the codebase belongs to the SignalR client.

Prerequisites before this can be built: F2 ingestion with a **client-supplied idempotency key**
(otherwise a replayed queue duplicates emergencies), plus a defined conflict/duplicate policy —
the AI duplicate detector helps but is not a substitute.

---

## 14. Prioritised backlog

### P0 — Critical (system does not function / unsafe)

| ID | Item | Depends on | Est. |
| --- | --- | --- | --- |
| P0-1 | **Fix the auth bypass properly** — verify the Neon Auth session server-side (signature/issuer/audience/expiry/verified-email) before minting a session; keep the dev-only gate until it is done. **Rotate the committed Neon credential** and move it to user-secrets. | — | 1–1.5 d |
| P0-2 | **F2 incident ingestion API** — `POST /api/incidents` (validated, `reports` rate limit, idempotency key), media upload via `IFileStorage`, `GET /api/incidents` (paged/filtered), `GET /api/incidents/{id}`, status transitions, and **publish `IncidentCreated`**. Replace `FakeIncidentReadService` with the real `IIncidentReadService`. | P0-1 (auth trust) | 3–4 d |
| P0-3 | **Shelter seed data** — a `SheltersSeeder` (Development/demo-gated) writing the 8 Dhaka shelters, so finder, recommendations and assistant context stop being empty. | — | 0.5 d |
| P0-4 | **Wire `/reports/new` + SOS to the API** — real submit, real errors, real ticket id; remove the fake success state. | P0-2 | 1 d |

### P1 — Core (required for the demonstration)

| ID | Item | Depends on | Est. |
| --- | --- | --- | --- |
| P1-1 | `/reports/my` + timeline from the API (status, assigned unit, ETA) | P0-2 | 1 d |
| P1-2 | Citizen dashboard on real data (my incidents, nearby incidents, active alerts) | P0-2 | 1 d |
| P1-3 | ~~**Rescue API**: priority queue (AI score with fallback sort), incident detail, assignment, mission state machine `Assigned → EnRoute → OnSite → Completed`, publish `MissionAssigned`/`MissionStatusChanged`~~ **✅ DONE §0c** — plus dashboard bands, suitability scoring, accept/reject/reassign | P0-2 | 3–4 d |
| P1-4 | ~~Rescuer HUD on real data + navigation hand-off + team status~~ **✅ DONE §0c** | P1-3 | 1.5 d |
| P1-5 | ~~**Admin verification + monitoring**: verify/reject with reason (`IncidentVerified`), live incident table/map, user-management UI over the existing API~~ **✅ DONE §0d** | P0-2 | 2–3 d |
| P1-6 | **Relief API**: request submit/list/status, triage approve/reject, `ReliefRequested`/`ReliefStatusChanged`; wire `/relief/request` | P0-2 | 2–3 d |
| P1-7 | Realtime publishers + client subscriptions for incident/mission/relief topics; dashboards update live | P1-3, P1-6 | 1 d |
| P1-8 | Incident **photo upload** end-to-end (client → `IFileStorage` → AI vision path) | P0-2 | 1 d |
| P1-9 | Replace ad-hoc client status strings with `Shared/Contracts` enums (T4) | P0-2 | 0.5 d |
| P1-10 | CI: add the 5 missing `postgres-fidelity` contexts; register Incidents/Relief/Rescue contexts in `TestingWebAppFactory`; integration tests per new endpoint group | P0-2 | 1 d |
| P1-11 | ~~Remove/gate `DELETE /api/auth/users/all`; add audit entry for destructive admin actions~~ **✅ DONE §0d** | — | 0.25 d |

### P2 — Advanced

| ID | Item | Depends on | Est. |
| --- | --- | --- | --- |
| P2-1 | **Offline reporting**: IndexedDB queue, reconnection sync, sync-status UI, idempotency de-dup | P0-2 (+ idempotency key) | 3 d |
| P2-2 | ~~Analytics: response-time metrics, incident heatmap layer, KPI panels~~ **✅ MOSTLY DONE §0d** — metrics, distributions, pipeline and concentration shipped; the map **heatmap layer** is the remainder | P1-3, P1-5 | 0.5 d |
| P2-3 | ~~Audit trail (F14): move `audit_logs` to its own context, subscribe to the event bus, admin viewer~~ **✅ DONE §0d** — `Features/Audit` + `IAuditTrail` + `/g/audit` | T2 | 1.5 d |
| P2-4 | Registry (F13): hospitals/volunteers/NGOs CRUD replacing the stub read service | — | 2 d |
| P2-5 | ~~Resource inventory~~ **✅ DONE §0d** (`/api/relief/resources` + `/g/relief`); allocation, dispatch and delivery tracking outstanding | P1-6 | 1.5 d |
| P2-6 | ~~AI human override + confidence/explanation surfaced in the UI~~ **✅ PARTLY DONE §0e** — confidence, urgency, damage indicators, reasoning and scored priority factors are rendered as labelled decision support, and duplicate flags are reviewable; overriding the classification itself is the remainder | P1-5 | 0.5 d |
| P2-7 | Enforce or delete the permission matrix (T5) | — | 0.5–1 d |
| P2-8 | Rescue-team live location layer — **position reporting done §0c**; map layer outstanding | P1-3 | 0.5 d |

### P3 — Polish

Directions hand-off · hero-images envelope + publish-safe path (B7) · delete dead JS and hide `/sample`, `/ui-showcase` behind a dev flag (T10) · geo index + query caps (T9) · CSP/`frame-ancestors` response headers (S5) · policy-name cleanup (`RequireAdmin` → `RequireGovernment`) · blueprint doc banners (T12) · NBomber/Postman collections if the brief still requires them.

---

## 15. Dependency analysis & recommended implementation order

```
P0-1 auth trust ──► P0-2 F2 ingestion ──┬─► P0-4 report/SOS wiring ──► P1-1 my reports ──► P1-2 citizen dash
                                        ├─► P1-3 rescue API ──► P1-4 rescuer HUD ──► P2-8 team locations
                                        ├─► P1-5 admin verify/monitor ──► P2-2 analytics ──┐
                                        ├─► P1-6 relief API ──► P2-5 inventory/dispatch    ├─► P2-6 AI override
                                        ├─► P1-8 photo upload (unlocks AI vision)          │
                                        └─► P2-1 offline sync (needs idempotency key)      │
P0-3 shelter seed (independent, do first — 4 hours, unblocks the demo) ────────────────────┘
P1-10 CI/test coverage: runs alongside every step, never after
T2/T3/T5 cleanups: independent, any time
```

**Already complete — do not rebuild:** foundation, contracts, auth (password), realtime transport,
notifications, alerts, AI engine + assistant, shelter CRUD, design system, geolocation.

**Blocked right now:** rescue operations, admin verification, analytics, offline sync, AI in the
real flow, incident maps — all blocked by the *same* dependency, **F2 ingestion (P0-2)**.

**Independent work streams that can start immediately** (four developers, no collisions):

| Developer | Immediate work | Touches |
| --- | --- | --- |
| Tayeb | P0-1 auth fix + credential rotation, then P1-7 realtime publishers/subscriptions | `Features/Auth`, `Features/Realtime` |
| Shehab | **P0-2 F2 ingestion** (critical path — start today), then P1-3 rescue API | `Features/Incidents`, `Features/Rescue` |
| Tanjim | P0-3 shelter seeder (half a day), then P1-5 admin verification/monitoring + user-management UI | `Features/Shelters`, admin pages |
| Mugdho | P1-6 relief API + wiring (alerts are done), then P2-5 inventory | `Features/Relief` |

**Sequencing rule:** nothing in P1 should start on a page that still holds mock data without also
deleting that mock data in the same PR. The mocks are the debt; each PR must pay some down.

---

## 16. Technology review

**.NET remains mandatory and remains correct.** The recommendation is to **change nothing structural.**

| Area | Current | Verdict |
| --- | --- | --- |
| Runtime | **.NET 8** (LTS) — the original brief says .NET 10 | **Keep.** All package pins are validated for 8.x, the team is mid-project, and the graded demo ends well before 8.0 EOL. Migrating costs SDK churn and re-validation for zero demo benefit (D-004). *If the course rubric hard-requires .NET 10, this is a compliance decision to raise with the instructor now, not a technical one.* |
| Web UI | **Blazor WASM PWA** — the brief says ASP.NET Core MVC | **Keep.** One language end-to-end, offline/PWA support (a required capability) is native, and the entire client already exists. Same compliance caveat as above. |
| Database | **PostgreSQL + EF Core 8** — the brief says SQL Server | **Keep.** Free cloud tier, docker-compose parity, and 9 working migration sets. Switching now would invalidate every migration for no capability gain. |
| Dev database | Shared **Neon** cloud instance with a committed password | **Change.** Use the documented docker-compose Postgres locally; keep Neon for a single deployed demo instance with a rotated secret. |
| Identity | ASP.NET Identity + JWT + refresh cookie | **Keep** — well implemented. |
| OAuth | **Neon Auth**, unverified | **Change the implementation, not the vendor** (verify the session server-side), or drop Google sign-in for the demo — password login already works. |
| AI | **OpenRouter** free models behind `IAiAnalysisService` + rule-based fallback — the brief says Gemini | **Keep.** Provider-agnostic seam, model-pair fallback, and a fallback that guarantees the demo never depends on quota. Gemini can be re-added as one more transport if required. |
| Maps | **Leaflet + OpenStreetMap** (vendored) — the brief says Google Maps | **Keep.** No API key, no billing, works offline-ish, already integrated. Directions can hand off to an external maps URL. |
| Realtime | SignalR + polling fallback | **Keep** — exemplary. |
| Tests | xUnit + `WebApplicationFactory` + SQLite in-memory + NetArchTest | **Keep**, and extend to the new endpoints. Consider Testcontainers **only** if SQLite/Npgsql divergence starts biting; the CI fidelity job is the cheaper fix (P1-10). |
| Load/API tooling | NBomber/Postman named in the brief, absent here | **Add only if the rubric requires it** (P3). A Postman/`.http` collection is ~2 hours; NBomber is a half-day and demos well against `/api/incidents`. |

**Net technology recommendation:** zero framework changes; two *configuration* changes (local
Postgres, secrets out of the repo) and one *implementation* change (verify the OAuth session).

---

## 17. Estimated remaining effort

| Tier | Scope | Effort |
| --- | --- | --- |
| P0 | Security fix + F2 ingestion + shelter seed + report/SOS wiring | **5.5–7 developer-days** |
| P1 | Rescue, admin, relief, realtime publishers, photo upload, CI/test coverage | **13–17 developer-days** |
| P2 | Offline sync, analytics, audit, registry, inventory, AI override | **12–15 developer-days** |
| P3 | Polish, cleanup, tooling | **3–4 developer-days** |
| | **Total to a fully demonstrable system** | **≈ 34–43 developer-days** |

With four developers working their existing lanes in parallel and the dependency order above,
that is roughly **2 weeks to a credible end-to-end demo** (P0+P1) and **3.5–4 weeks to feature
completeness** (P2), assuming the P0 critical path (F2 ingestion) starts immediately and is not
blocked behind design discussion.

---

## 18. Highest-risk items

| Rank | Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- | --- |
| 1 | **The auth bypass (S1) plus the committed credential (S2)** — the dev database must be assumed compromised | Certain (already exploitable) | Catastrophic for any deployment; serious mark deduction in a security-graded project | P0-1 today: rotate, verify tokens, keep the dev-only gate |
| 2 | **F2 ingestion is the single blocker for six other features** — if it slips, rescue, admin, AI-in-flow, analytics and offline all slip with it | High | Demo has no core loop | Start P0-2 first, timebox the design to the existing `IncidentSummaryDto`/`IncidentCreated` contracts (do not redesign contracts) |
| 3 | **Mock-data pages create a false sense of completion** — the status board read "IN PROGRESS" for verticals with zero backend | High | Planning error compounds; a demo click-through exposes fabricated data | Rule: no new mock data; every PR that touches a mock page must replace some of it |
| 4 | **SOS that does nothing (B5)** | Certain today | Reputational/ethical in an emergency product; obvious to any evaluator | P0-4, or an explicit "demo mode" banner until then |
| 5 | **Shared cloud dev database** — one destructive call (S4) wipes everyone's data mid-demo | Medium | Total demo loss | Local docker-compose per developer; remove `DELETE /users/all` |
| 6 | **Contracts v1 still not ratified** — F2/F4/F5 endpoints are about to be built against unratified read models | Medium | Rework across four lanes | Hold the 30-minute workshop before P0-2 merges |
| 7 | **Migration fidelity gap (T7)** — 5 contexts unproven against Postgres | Medium | A migration that works on SQLite fails on demo day | P1-10, one CI line per context |
| 8 | **Stack divergence from the written brief** (.NET 8 vs 10, Blazor vs MVC, Postgres vs SQL Server, OpenRouter vs Gemini, Leaflet vs Google Maps) | Certain (already diverged) | Rubric compliance, not technical | Confirm with the instructor in writing this week; every divergence is defensible and documented in PROJECT-CONTEXT §7 |

---

## Appendix A — changes made during this audit

Only defects that were safe, small and verifiable were fixed; no features were implemented.

| Change | File | Verification |
| --- | --- | --- |
| Unauthenticated Google session exchange refused outside Development/Testing + `auth` rate limit applied to both OAuth endpoints | `Features/Auth/Endpoints/AuthEndpoints.cs` | Build + 668 tests green; dev flow unchanged |
| Minimal-API binding failures return **400 ProblemDetails** instead of 500 | `Infrastructure/BindingFailureExceptionHandler.cs` (new), `Program.cs` | Live re-probe: `?lon=` and missing-parameter cases now 400 |
| Coordinates serialised with `InvariantCulture` | `Features/Shelters/SheltersClient.cs` | Build green |
| `RollForward` `LatestMajor` → `LatestMinor` so the test host runs on the pinned .NET 8 | `Directory.Build.props` | `dotnet test` now runs with no environment workaround |

**Not changed on purpose:** the Neon credential (rotation is the owner's call), the mock-data pages,
`DELETE /users/all`, the hero-images endpoint, and every P0-P3 backlog item above.
