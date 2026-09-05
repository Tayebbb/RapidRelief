# RapidRelief — Claude Instructions

Before doing ANY work in this repo, read in order:

1. [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) — single source of truth: implementation status, next steps, architecture rules (§4 is non-negotiable).
2. [PROJECT-AUDIT.md](PROJECT-AUDIT.md) — verified repository state, defects and prioritised backlog (wins over status rows when they disagree).
3. [AGENTS.md](AGENTS.md) — hard rules summary.
4. For feature work: the feature's section in [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md).
5. **For ANY UI/page/component work: [design.md](design.md)** — the mandatory design system (tokens, components, dark/light + accessibility gate); colour hexes live in [RapidRelief-Website-Theme.md](RapidRelief-Website-Theme.md).
6. For endpoints: [api-conventions.md](api-conventions.md); for cross-module events: [event-bus.md](event-bus.md).

Quick facts that are easy to get wrong: roles are `Citizen`/`Rescuer`/`Government` (Admin/Ngo/Rescue are aliases) · the palette has **no blue** (D-074) · ten DbContexts, always pass `--context` · **every slice maps live endpoints now** — F2 `/api/incidents`, F4 `/api/relief/requests`, F5 `/api/rescue`, F7/F12 `/api/incidents/ops/summary`, F14 `/api/audit` (D-083, D-084, D-087, D-092…D-101); D-079 is superseded, don't re-mock them · rescue assignment conflicts are `409`, mission transitions are forward-only (D-094) · administrative actions record through the `IAuditTrail` contract, never by referencing `Features/Audit` (D-097) · AI output is decision support: read `AiInsightDto`, always render its disclaimer, never let the AI slice close or merge a report (D-102/D-107) · geolocation goes through `GeolocationService` (D-075).

## After EVERY implementation (mandatory)

Do not consider any task done until PROJECT-CONTEXT.md is updated in the same change/PR:

- **Status board row** (§3) for the feature you touched
- **Changelog entry** (§8, newest first)
- **Decision D-NNN** (§7) for any choice not covered by the plan
- **Contracts registry** (§6) and **Repository State** (§2) if they changed

An implementation without this update is incomplete work — never skip it.
