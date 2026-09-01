# RapidRelief — Agent Instructions

**STOP — before implementing, planning, or reviewing anything in this repo:**

1. Read [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) — the single source of truth for what is implemented, what is next, and the architecture rules.
2. For feature work, read that feature's section in [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md).

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
