## What & why

<!-- One or two sentences: what does this PR do, which feature (F-number), link the plan section. -->

## Definition of Done (plan §1.2 — all boxes required)

- [ ] `dotnet build -c Release` — 0 warnings, 0 errors
- [ ] `dotnet test` — fully green locally (no Docker/Postgres required)
- [ ] **PROJECT-CONTEXT.md updated in this PR** (status row §3 + changelog §8 + decisions §7 if any) — code without it is incomplete work
- [ ] No cross-feature references: my code only touches `Features/<mine>` + `Shared/Contracts` (arch tests pass)
- [ ] Contract changes (if any) are **additive-only**, PR is labeled `contracts`, and 2 approvals are requested
- [ ] Every new endpoint: explicit auth policy, explicit FluentValidation, envelope + ProblemDetails shapes (docs/api-conventions.md)
- [ ] New DbContext work used `--context`/`--output-dir` and touched only my migration folder
- [ ] Stubs/fallbacks still work (never deleted or bypassed — §4.5)

## How to verify

<!-- Exact commands/URLs a reviewer runs to see it working, e.g. role header curl, page to open. -->

## Deviations / decisions

<!-- Anything not covered by the plan → also appended as D-NNN in PROJECT-CONTEXT §7. Write "none" otherwise. -->
