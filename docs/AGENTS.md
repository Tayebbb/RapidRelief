# RapidRelief — Agent Instructions

**STOP — before implementing, planning, or reviewing anything in this repo:**

1. Read [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) — the single source of truth for what is implemented, what is next, and the architecture rules.
2. Read [PROJECT-AUDIT.md](PROJECT-AUDIT.md) — the verified state of the repository (capability matrix, known defects, P0–P3 backlog). If a status row and the audit disagree, the audit wins.
3. For feature work, read that feature's section in [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md).
4. **Before creating or changing ANY page, component or style, read [design.md](design.md)** — the mandatory design system (tokens, components, dark/light rules, a11y gate). Colour hexes come from [RapidRelief-Website-Theme.md](RapidRelief-Website-Theme.md); the long-form UI guide is [frontend-uiux.md](frontend-uiux.md). UI work that ignores them must not be merged.
5. **Before adding or changing an endpoint, read [api-conventions.md](api-conventions.md)** (routes, envelope, ProblemDetails, paging, rate-limit policies, per-context EF commands) and, for cross-module work, [event-bus.md](event-bus.md).

## Facts agents get wrong (check these first)

- **Roles are `Citizen`, `Rescuer`, `Government`.** `Roles.Admin`/`Roles.Ngo`/`Roles.Rescue` are aliases of those — there is no separate NGO role.
- **The palette has no blue** (D-074). Action = Forest Green `#1e7a5a`; red is emergency/destructive only. Use `--rr-*` tokens, never raw hex.
- **Ten DbContexts** exist (Sample, Auth, Ai, Notifications, Ops, Alerts, Incidents, Relief, Rescue, Audit) — always pass `--context` and the feature's `--output-dir`.
- **Administrative actions must be audited.** Inject `IAuditTrail` (frozen contract) and record who/what/entity/result; never reference `Features/Audit` directly. Writes never throw, so an audit line can never fail the action (D-097).
- **AI output is decision support, never fact.** Read `AiInsightDto`, render its `Disclaimer`, and never drop the confidence or the priority factors when you surface a score (D-102). Deterministic text evidence is unioned with the model, never replaced by it (D-104). Duplicate flags are advisory — nothing in the AI slice may close, merge or delete a report (D-107).
- **Every feature maps endpoints now** — D-079 is superseded. F2 `/api/incidents`, F4 `/api/relief/requests` and F5 `/api/rescue` are all live as of 2026-09-03 (D-083, D-084, D-087, D-092…D-096). Use them; do not re-mock them.
- **Rescue conflicts are refused, not merged**: assigning an already-assigned incident, a deployed team or an off-duty team is `409`; mission transitions are forward-only; reassignment is Government-only (D-094, D-095).
- **Never call `navigator.geolocation` from a page** — inject `GeolocationService` (D-075).

## Hard rules (summary — full versions in PROJECT-CONTEXT.md §4)

- Vertical slice ownership: code lives in `Features/<Feature>/`; never reference another feature's folder — cross-module only via `Shared/Contracts` interfaces + events.
- No cross-module foreign keys or navigation properties; reference by `Guid` ID.
- Per-owner DbContexts; never add tables to a context you don't own; never edit merged migrations.
- Contract changes are additive-only without team sign-off.
- Keep all stubs/fallbacks (FakeAuth, rule-based AI, polling) working — they are permanent resilience, not scaffolding.
- Only touch feature folders owned by the developer you are working for, unless the task explicitly says otherwise.

## After EVERY implementation (mandatory)

An implementation is **not finished** until PROJECT-CONTEXT.md is updated in the same change/PR. Always:

1. Update the feature's **status board row** (§3): status + one-line note of what now works.
2. Add a **changelog entry** (§8, newest first): date — what was implemented/changed.
3. Append a **decision** (§7, D-NNN) if you made any choice not covered by the plan, or deviated from it.
4. Update the **Contracts v1 Registry** (§6) if any contract was added or extended.
5. Update the **Repository State** table (§2) if scaffold/CI/contracts state changed.

No exceptions — code without the PROJECT-CONTEXT.md update is incomplete work and must not be merged.
