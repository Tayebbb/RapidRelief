# RapidRelief — Final Production & Demonstration Audit

**Date:** 2026-09-04
**Auditor:** Autonomous final engineering pass
**Verdict:** **READY TO DEMONSTRATE**, with the credential rotation in §6 outstanding.

---

## 1. Build result

| Gate | Result |
|---|---|
| `dotnet build RapidRelief.sln` | **Succeeded — 0 errors, 0 warnings** |
| `dotnet test RapidRelief.sln` | **802 passed, 0 failed, 2 skipped** (790 API + 12 architecture) |
| Application start (`Development`, Neon Postgres) | **Succeeded** — `GET /health` → `{"status":"ok","dbConnected":true}` |
| Migrations | All 9 DbContexts migrate cleanly on an existing database |
| Live API walkthrough (`artifacts/audit-walkthrough.ps1`) | **56 / 56 checks passed** |
| Browser route sweep (22 routes, 2 roles) | **0 console errors, 0 page errors, 0 broken routes** |

Test count rose from 776 to 802 during this audit: 26 new tests, all pinning behaviour that was fixed here.

---

## 2. What was verified

### Citizen
Registration → login → dashboard → SOS → disaster report → GPS → media → AI → report tracking → shelter → relief → notifications → offline workflow.

All exercised end to end. GPS capture sets the pin and the "you are here" dot; media upload is size- and extension-capped; AI triage scores every report; the tracker shows the live status timeline; the shelter finder ranks by proximity and free capacity; relief requests move through the government state machine; notifications arrive by push.

### Rescue team
Login → dashboard → incident → assignment → navigation → En Route → On Site → Completed.

The full mission lifecycle was driven live. Illegal transitions are refused with 409 (verified: On Scene → En Route rejected). Completing a mission releases the team back to Available and resolves the incident for the citizen.

### Government
Login → command centre → incidents → map → analytics → rescue management → shelters → resources → audit.

Every command surface returns data. The operational map renders four populated layers (30 incidents, 6 teams, 8 shelters, 7 relief drop-offs = 44 markers) with layer toggles, critical-only, free-text search and a concentration heatmap.

### Cross-role propagation
Verified as one continuous chain against the live server and database:

> Citizen files SOS → row in `incidents_reports` → `IncidentCreated` event → AI triage assigns priority + summary → pushed to Rescuer and Government role groups → Government verifies → incident enters the dispatch queue → Rescuer takes the call → mission `Assigned` → `EnRoute` → `OnScene` → `Completed` → incident `Resolved` → citizen receives notifications and sees the resolved timeline.

Real-time propagation is push-driven: the government map moved from 34 to 35 markers on a `incidents.report.created` push with **no polling timer running**.

### Failure handling

| Scenario | Behaviour | Verdict |
|---|---|---|
| AI provider unavailable | Circuit breaker + transient retry + permanent deterministic rule-based fallback; assessment still produced | PASS |
| Map tiles unavailable | Basemap greys out, **all 8 markers still render**, explicit notice: *"Map imagery is unavailable — every marker below is still live and positioned correctly."* | PASS |
| Map config endpoint down | Falls back to OpenStreetMap defaults; map still initialises, page fully usable | PASS |
| Network disconnected | `OFFLINE` → report saved locally → `SAVED LOCALLY` → reconnect → auto-sync → `SYNCED`, queue drained | PASS |
| Invalid input | 400 with field-level ProblemDetails; out-of-range coordinates and negative counts rejected | PASS |
| Missing media | Report submits without attachments; per-file failures are reported but non-blocking | PASS |
| Duplicate report | Idempotency key collapses repeat submissions to one incident (verified twice, live) | PASS |
| Unauthorized request | 401 anonymous / 403 wrong role across 12 probes | PASS |
| Database error | **Fixed in this audit** — now a 503 in degraded mode with automatic recovery (§3) | PASS |
| Empty dataset | Empty states render on every citizen surface; shelter list empty state added here | PASS |
| Slow response | Oversized page sizes clamped, negative pages handled, no 500s | PASS |
| Error bodies | No stack traces, no provider messages, no host names leaked | PASS |

---

## 3. What was fixed

### Critical / high

**Degraded mode was a one-way startup decision.**
`DatabaseHealth.PostgresAvailable` was set once by the migration runner and never revisited. Every endpoint guarded on it, so a database that died *after* startup produced raw `500`s with stack traces while all the 503 guards still believed the database was up. This was found by accident during the audit and reproduced deliberately.
Fixed with [DatabaseFailureExceptionHandler.cs](src/RapidRelief.Api/Infrastructure/DatabaseFailureExceptionHandler.cs) (classifies any database fault, flips the health flag, returns the same honest 503 without leaking the provider message) and [DatabaseHealthProbe.cs](src/RapidRelief.Api/Infrastructure/Persistence/DatabaseHealthProbe.cs) (only runs while degraded, restores service within 20 s of the database returning — no restart needed).

**Anonymous feed of every live SOS location.**
`GET /api/foundation/demo-incidents` was `AllowAnonymous` and resolved the *real* incident service, publishing precise coordinates and descriptions of up to 100 live reports — a targeting feed for vulnerable people during a disaster. Now `RequireResponder`.

**Cross-team mission hijack.**
`POST /api/rescue/missions` accepted any `teamId` in the body from any responder. A rescuer could flip a rival team to `Dispatched`, block it from real work and push a false mission to its members. `ResolveTeamAsync` now rejects a team the caller does not belong to unless the caller is Government.

**Phone-number harvesting.**
`GET /api/incidents` returned `includeContact: true` to *any* responder, and Rescuer was self-assignable at registration. Anyone could self-register as a Rescuer and page the whole feed for every reporter's phone number and coordinates. Contact details are now Government-only on the feed; the assigned team still gets them from the incident it is working.

**Self-service privilege escalation.**
Public registration honoured `role: "Rescuer"` from the request body. Now Citizen everywhere except Development/Testing, where the demo needs self-registered responders. Promotion is an administrator action.

**AI decision-support IDOR.**
`/api/ai/assessments/{id}`, `/api/ai/insights/{id}` and `/api/ai/recommendations/*` were keyed by incident id with only `[Authorize]`. Any citizen holding another citizen's incident id could read the AI summary derived from their free text. Now responder-only; a citizen still sees their own AI estimate through the owner-scoped incident DTO.

**Open redirect in the OAuth init route.**
`callbackUrl` was forwarded to the identity provider verbatim, so an attacker-chosen value would receive the victim's sign-in. Now validated same-origin via `AuthEndpoints.SameOriginCallback`.

**Admin self-destruct.**
`DELETE /api/auth/users/{id}` had no self-guard and no last-administrator guard — an admin could delete their own account, or the last Government account, stranding the platform. Both now refused.

**Committed live database credential.**
The Neon Postgres password sat in `appsettings.Development.json`. Moved to a gitignored `appsettings.Development.Local.json`, loaded last by `Program.cs`; the committed file now holds an empty placeholder and a pointer. **The credential remains in git history and must still be rotated — see §6.**

### Medium / polish

- Brand tokens: replaced the remaining raw hex (`#23996a`, `#fb8c00`, `#2f3431` in app.css; `#38d39f`, `#ff6b6b`, `#ff5252`, duplicated `#1e7a5a` in landing.css) with `--rr-*` tokens; added the missing `--rr-warning` and `--rr-success-hover` tokens in both light and dark blocks. **No blue remains anywhere in first-party CSS.**
- `RrCard` acted as a button but was mouse-only. Now `role="button"`, `tabindex="0"` and Enter/Space handling.
- Shelter finder: bare "Loading shelters…" text replaced with a skeleton; added an empty state for the shelter list; the hard-coded `height: 600px` map container now caps against the viewport so it no longer overflows short phones.
- Page-title convention applied to the two pages that were missing it.
- Added a `wifi` icon that the connectivity badge referenced but `AppIcon` did not define.

### Demonstration data

Rescue teams were **never seeded** — the operational map's team layer, the government team registry and the dispatch suitability ranking all rendered empty. Added [RescueTeamSeeder.cs](src/RapidRelief.Api/Features/Rescue/Services/RescueTeamSeeder.cs): six deterministic units (FSCD Mirpur, FSCD Tejgaon, Savar USAR, BDRCS Alpha, Coast Guard River Unit, Army Engineer Detachment) with fixed GUIDs, fixed positions, realistic specialisations and fictional `+880 1555 …` demo contact numbers.

It seeds **by id, not "only when the table is empty"** — a database that already holds real or ad-hoc teams would otherwise never receive the demo set. It only inserts missing demo ids and never modifies a row it did not create.

Demo data is separated from real data by construction:

| Dataset | Separator |
|---|---|
| 28 incidents | GUID prefix `a0000000-…`, `AddressOrArea = "Dhaka demo dataset"`, synthetic reporter `dddddddd-…` so they never appear in a real citizen's list |
| 8 shelters | GUID prefix `b0000000-…` |
| 6 rescue teams | GUID prefix `f0000000-…`, synthetic team-lead id so they never join a real rescuer's "my team" |
| 6 demo users | Opt-in via `Auth:SeedDemoUsers`, `@rr.dev` domain |

All seeders are deterministic — fixed anchor time, no `Random`, no `DateTime.Now` — and each is individually disableable (`Incidents:SeedDemoData`, `Shelters:SeedDemoData`, `Rescue:SeedDemoData`). **No fabricated performance statistics anywhere**; the only numbers shown are counted from real rows.

---

## 4. Technical-debt sweep

| Marker | Result |
|---|---|
| `TODO` / `FIXME` / `HACK` / `XXX` | **None** in first-party source (one match is the word "catch" inside vendored Leaflet CSS) |
| `console.log` / `debugger` / `Console.WriteLine` | **None** |
| Mock / fake / placeholder data | **None reachable.** The only `mock` identifiers are landing-page CSS classes for the deliberately stylised marketing preview panels (`assistant-dialog-mock`, `map-preview-mock`), which are intentional illustration, not data |
| Hardcoded credentials | **One** — the dev-only JWT signing key in `appsettings.Development.json`, explicitly labelled and rejected at startup outside Development/Testing. The real credential was removed (§3) |
| Dead code | `Features/Stubs/*` are live fallbacks registered with `TryAdd`, displaced by real services — intentional, retained |
| Unused imports | Build is clean at 0 warnings |

---

## 5. Security findings

A full OWASP-oriented audit was performed. Categories confirmed **clean**: SQL injection (no raw SQL anywhere in `src/`), path traversal (`LocalDiskFileStorage.ResolveSafe` rejects rooted and `..` paths and re-verifies the resolved prefix; uploads are GUID-named), notification IDOR, relief IDOR, `CanDriveAsync` team scoping, profile/assistant scoping, password hashing (PBKDF2, 210 000 iterations, constant-cost dummy verify defeats email enumeration), refresh-token rotation with family revocation, cookie flags (`HttpOnly`, `SameSite=Strict`, `Secure` outside Development), XSS (no `MarkupString`/`innerHTML` — enforced by an architecture test), page-size caps on every paged route, and `FakeAuth` being inert outside Development/Testing.

Eight issues were found and **all eight were fixed** (§3), each with a regression test in [AccessControlRegressionTests.cs](tests/RapidRelief.Api.Tests/Security/AccessControlRegressionTests.cs).

### Remaining security items

| Severity | Item | Why it is not fixed here |
|---|---|---|
| **CRITICAL** | The Neon Postgres password is in git history and is still live. `Trust Server Certificate=true` also disables TLS validation. | Rotation is an operational action on the Neon console that only you can perform. **Do this before any public deployment.** After rotating, drop `Trust Server Certificate=true` in favour of `SSL Mode=VerifyFull`. |
| **HIGH** | `POST /api/auth/oauth/google-session` mints a session from an unverified request body. | Refused outside Development/Testing, and it no longer grants a role from the body. A real fix requires verifying the Neon Auth session server-side against their API — that contract is not available to verify autonomously without risking breaking the working Google sign-in demo. Treat `ASPNETCORE_ENVIRONMENT` as a security control. |
| MEDIUM | Incident `PhotoPaths` are accepted as caller-supplied strings rather than signed upload tokens. | Path traversal is already blocked by storage; this is a latent sink, not an exploitable one today. |
| MEDIUM | Rate limiting partitions per-IP; behind a CDN without `Proxy:Enabled` every client shares one partition. | Deployment configuration. Set `Proxy:Enabled` + `Proxy:KnownProxies`. |
| LOW | No Content-Security-Policy header. | Defence in depth; XSS is already structurally prevented and enforced by test. |
| LOW | `AllowedHosts: "*"`. | Needs the real production hostname. |
| — | Dependency vulnerability scan (`dotnet list package --vulnerable`) not run. | This OWASP category is **unassessed**, not clean. Run before release. |

---

## 6. Before you deploy publicly

1. **Rotate the Neon database password.** It is in git history.
2. Set `Jwt__SigningKey` (≥32 bytes), `ConnectionStrings__Postgres` and `Ai__OpenRouter__ApiKey` as environment variables. Startup fails fast without a real signing key outside Development.
3. Set `ASPNETCORE_ENVIRONMENT=Production`. Three mitigations depend on it.
4. Set `AllowedHosts` and, if proxied, `Proxy:Enabled` + `Proxy:KnownProxies`.
5. Run `dotnet list package --vulnerable`.

---

## 7. UX findings

**Good:** the design system is real and consistently applied — tokens drive both themes, `prefers-reduced-motion` is respected, skeletons and empty states exist on every citizen surface, form labels are associated, heading structure is mostly sound, and the connectivity badge means a citizen is never left guessing whether their report left the device.

**Known limitations (accepted, not fixed):**

- **Five button class systems coexist** (`rr-btn`, `btn-dash-*`, `btn-auth-*`, landing `btn-*`, Bootstrap `btn btn-*`). Each is internally consistent within its surface, and consolidating them is a wide cosmetic refactor with real regression risk immediately before a demonstration. Deliberately deferred.
- **Six different responsive breakpoints** across the CSS files with no shared token. Layouts work; the inconsistency is a maintenance cost, not a defect.
- A few `h1 → h3` heading jumps remain on dashboard cards.
- Command-centre tabs are links styled as tabs rather than semantic `role="tab"` elements.

---

## 8. Recommended demonstration sequence

Start the app: `dotnet run --project src/RapidRelief.Api` (add `Auth__SeedDemoUsers=true` for the demo accounts).
Demo accounts — `citizen1@rr.dev`, `rescuer1@rr.dev`, `government1@rr.dev`, all password `Demo!123`.

1. **Landing page** — set the scene, then sign in as `citizen1@rr.dev`.
2. **File an emergency** (`/reports/new`) — tap *Use my location*, watch the pin and accuracy halo land on the shared map, add a description, submit. Show the ticket confirmation.
3. **The offline story — the strongest moment.** Open DevTools → Network → Offline. The badge flips to `OFFLINE`. File a second report. It is accepted, the banner reads *"Saved on this device"*, and the badge shows `SAVED LOCALLY 1`. Go back online without touching anything: the badge moves to `SYNCED` on its own and the report appears in *Track my reports*.
4. **AI triage** — in *Track my reports*, show the AI priority score and summary the system produced with no human input.
5. **Switch to `government1@rr.dev` → Operational map** (`/g/map`) — four live layers, the `LIVE` indicator, layer toggles, *Critical and SOS only*, and the concentration heatmap.
6. **Real-time, with no polling** — keep the map open, file a report from a second browser as the citizen, and watch the marker count increase on its own.
7. **Verify and dispatch** (`/g/incidents`) — verify the report; it enters the rescue queue.
8. **Switch to `rescuer1@rr.dev`** (`/r`) — the queue is sorted by SOS then AI priority with distance from the unit. *Take call* → *En route* → *On site* → *Completed*.
9. **Close the loop** — back as the citizen, the report reads **Resolved** with the full step-by-step timeline and the notifications that arrived at each stage.
10. **Optional resilience** — block `tile.openstreetmap.org` in DevTools and reload the map: the basemap greys out, every marker stays live and the app says so plainly.

---

## 9. Artefacts

- [artifacts/audit-walkthrough.ps1](artifacts/audit-walkthrough.ps1) — the repeatable 56-check live walkthrough used above.
- [artifacts/audit-result.txt](artifacts/audit-result.txt) — its most recent output (56 passed, 0 failed).
- [tests/RapidRelief.Api.Tests/Security/AccessControlRegressionTests.cs](tests/RapidRelief.Api.Tests/Security/AccessControlRegressionTests.cs) — regression cover for every access-control fix.
- [tests/RapidRelief.Api.Tests/Foundation/DatabaseFailureClassificationTests.cs](tests/RapidRelief.Api.Tests/Foundation/DatabaseFailureClassificationTests.cs) — degraded-mode classification.
