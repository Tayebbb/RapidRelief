# RapidRelief — Claude Instructions

Before doing ANY work in this repo, read in order:

1. [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) — single source of truth: implementation status, next steps, architecture rules (§4 is non-negotiable).
2. [AGENTS.md](AGENTS.md) — hard rules summary.
3. For feature work: the feature's section in [RapidRelief-Development-Plan.md](RapidRelief-Development-Plan.md).
4. **For ANY UI/page/component work: [design.md](design.md)** — the mandatory design system (tokens, components, dark/light + accessibility gate).

## After EVERY implementation (mandatory)

Do not consider any task done until PROJECT-CONTEXT.md is updated in the same change/PR:

- **Status board row** (§3) for the feature you touched
- **Changelog entry** (§8, newest first)
- **Decision D-NNN** (§7) for any choice not covered by the plan
- **Contracts registry** (§6) and **Repository State** (§2) if they changed

An implementation without this update is incomplete work — never skip it.
