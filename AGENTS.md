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

## After any merged change

Update PROJECT-CONTEXT.md in the same PR: status board row, changelog line, and any new decision (D-NNN). A change without a context update is incomplete.
