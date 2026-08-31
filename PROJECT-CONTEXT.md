# RapidRelief — PROJECT CONTEXT (Single Source of Truth)

> **MANDATORY for every AI agent (Copilot, Claude, Antigravity, etc.) and every human:**
> 1. **READ this file completely BEFORE implementing anything.**
> 2. Read the feature's section in [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md) before touching that feature.
> 3. **UPDATE this file AFTER every merged change** (status board row + changelog entry).
> 4. Never violate the Architecture Rules below — they are what keep 4 developers unblocked.

Last updated: 2026-09-01 · Current phase: **P0 not started — planning complete**

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
| Planning | ✅ Complete (plan + this context file) |
| Solution scaffold (`RapidRelief.sln`, 3 projects) | ❌ Not created |
| Contracts v1 (`RapidRelief.Shared/Contracts`) | ❌ Not created — requires team contract workshop |
| Stubs & seed data | ❌ Not created |
| CI pipeline | ❌ Not created |
| Any feature code | ❌ None |

**Next action for the team:** Week 1 / F0 — scaffold solution, hold contract workshop, freeze Contracts v1, ship stubs + seed data + CI. See plan §8.

## 3. Feature Status Board

Statuses: `NOT STARTED` → `IN PROGRESS` → `MVP DONE` → `DONE` (· `BLOCKED` should never appear — if it does, fix the process, see plan §1.5).

| ID | Feature | Owner | Status | Notes |
|----|---------|-------|--------|-------|
| F0 | Platform Foundation & Shared Kernel | Tayeb | NOT STARTED | Gate for everything; Week 1 |
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

> Status: **PLANNED — not yet frozen.** To be authored by the whole team in the Week-1 contract workshop, then listed here with final signatures. Until frozen, the lists below are the agreed scope.

- **Enums:** `DisasterType`, `Severity`, `IncidentStatus`, `MissionStatus`, `ReliefStatus`, `ResourceType`, `Roles`
- **Events:** `IncidentCreated`, `IncidentAssessed`, `IncidentVerified`, `MissionAssigned`, `MissionStatusChanged`, `ReliefRequested`, `ReliefStatusChanged`, `AlertPublished`, `AuthEvent`
- **Interfaces:** `IIncidentReadService`, `IShelterReadService`, `IRegistryReadService`, `IUserAdminService`, `IAiAnalysisService`, `IRealtimeNotifier`, `IFileStorage`

## 7. Decisions Log (append-only)

| ID | Date | Decision | Why |
|----|------|----------|-----|
| D-001 | 2026-09-01 | Stack: ASP.NET Core 8 modular monolith + Blazor WASM PWA + PostgreSQL + SignalR + Leaflet/OSM + Gemini-with-fallback | One language for 4 devs, $0 cost, offline + realtime supported, demo cannot fail on quota (plan §1.1) |
| D-002 | 2026-09-01 | Zero-blocking model: contracts-first + stubs + fake read services + per-owner DbContexts + no cross-module FKs | Hard requirement: no developer ever waits on another (plan §1.5) |
| D-003 | 2026-09-01 | This file is the single source of truth for implementation state; all agents read before / update after any change | Keeps multi-agent, multi-dev work consistent |

## 8. Changelog (append-only, newest first)

- **2026-09-01** — Repo created. Development plan + project context authored. No code yet. Next: F0 (Week 1).

## 9. How to Update This File

- After your PR merges to `main`: update your feature's **status row** (+ one-line note), add a **changelog line**, and append any new **decision** (D-NNN) — never rewrite history.
- Keep entries terse. Details belong in the plan, PRs, or code — this file is the *index of truth*, not the archive.
- If reality diverges from the plan (scope cut, owner swap, contract change), record it here **in the same PR** that makes the change.
