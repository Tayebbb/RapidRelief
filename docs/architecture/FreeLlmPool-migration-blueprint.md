# FreeLlmPool Migration Blueprint (F8 + F16 transport swap)

**Scope**: replace the OpenRouter transport inside `Features/Ai/` with **freellmpool** (https://github.com/0xzr/freellmpool) — a self-hosted, OpenAI-compatible LLM gateway that pools 22 free-tier providers behind one endpoint, run as a local HTTP proxy (Python package, not embeddable in .NET). Everything provider-agnostic (breaker, rule-based, canned, sanitizers, server-side validation, worker, endpoints, DbContexts) survives byte-identical in behavior. Module isolation preserved — no file outside `Features/Ai/`, its tests, config, client assistant page/panel/API doc-comment const, and docs is touched, with two narrow, documented exceptions (see D-114's rename map footnote and D-116).

**Note on decision numbering**: the original task brief for this migration proposed D-108/D-109/D-110 for the three decisions below. By the time this migration landed, those IDs were already in use in [PROJECT-CONTEXT.md](../PROJECT-CONTEXT.md) §7 (D-108 = transient-failure retry, D-109 = assistant role-scoped context, D-110 = shelter nearest-neighbor fallback — all from the 2026-09-03 decision-support pass). This document and the actual code comments use **D-113/D-114/D-115/D-116**, appended after the highest existing ID (D-112) at the time of writing, to avoid a duplicate/colliding decision log entry.

**Evidence base**: [FreeLlmPoolClient.cs](../../src/RapidRelief.Api/Features/Ai/FreeLlmPool/FreeLlmPoolClient.cs), [FreeLlmPoolPromptBuilder.cs](../../src/RapidRelief.Api/Features/Ai/FreeLlmPool/FreeLlmPoolPromptBuilder.cs), [FreeLlmPoolResponseParser.cs](../../src/RapidRelief.Api/Features/Ai/FreeLlmPool/FreeLlmPoolResponseParser.cs), [FreeLlmPoolAiAnalysisService.cs](../../src/RapidRelief.Api/Features/Ai/FreeLlmPoolAiAnalysisService.cs), [FreeLlmPoolAssistantService.cs](../../src/RapidRelief.Api/Features/Ai/Assistant/FreeLlmPoolAssistantService.cs), [AssistantPromptBuilder.cs](../../src/RapidRelief.Api/Features/Ai/Assistant/AssistantPromptBuilder.cs), [AiModule.cs](../../src/RapidRelief.Api/Features/Ai/AiModule.cs), [appsettings.json](../../src/RapidRelief.Api/appsettings.json), [docker-compose.yml](../../docker-compose.yml), this repo's own [OpenRouter-migration-blueprint.md](OpenRouter-migration-blueprint.md) (the style/rigor precedent), freellmpool's own README (verified 2026-09-24).

---

## DECISIONS

| ID        | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | Supersedes/Amends                                                                                                                                                                                                       | Rationale                                                                                                                                                     |
| --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **D-113** | **Transport = freellmpool OpenAI-compatible proxy.** Named HttpClient `"freellmpool"`, `BaseAddress` read from config `Ai:FreeLlmPool:BaseUrl` (deployment topology varies — never hardcoded), default `http://localhost:8080/`, `Timeout = Infinite` (D-026 per-request linked CTS survives: 10 s text / 20 s vision, configurable). POST relative path `v1/chat/completions` (no `api/` prefix — that was OpenRouter's path). `Authorization: Bearer {apiKey}` per request; a blank key sends `"Bearer unused"` (freellmpool's own docs: on loopback, any placeholder key works unless a proxy key is configured). The OpenRouter-specific `X-Title` attribution header is dropped entirely. **Request body becomes plain OpenAI-compatible**: `models: [...]` → single `model: "<string>"`; `provider.require_parameters` dropped (OpenRouter-only); `reasoning.enabled` dropped (OpenRouter/GLM-only extension); `response_format` is now **always** `{"type":"json_object"}` on F8, on both the text and vision paths (previously photo used `json_object`, text used strict `json_schema`) — freellmpool pools many providers and can't reliably honor strict-schema mode, so `FreeLlmPoolResponseParser`'s in-code validation is the real enforcement layer, unchanged. The now-unused `ResponseJsonSchema` constant is kept in `FreeLlmPoolPromptBuilder` as a doc-comment reference for a future strict-mode revisit, but is never sent. F16's `AssistantPromptBuilder` drops `models[]`→`model` and drops `reasoning` the same way; it already sent no `response_format` (prose mode), unaffected otherwise. **The "no key → skip to rule-based" gate becomes "blank BaseUrl → skip"**: freellmpool can legitimately answer with zero API key via its keyless providers (Pollinations, OVHcloud, Kilo Gateway, LLM7), so gating on the key no longer makes sense — a blank `Ai:FreeLlmPool:BaseUrl` is now the ops-level kill switch, with the exact same "never counts against the breaker" semantics. D-060's retry mechanism (`MaxAttempts`/`RetryBaseDelayMs`, exponential backoff with jitter) is unchanged. D-063's three-way client-side error classification (403→Blocked without reading body; other non-2xx→Unavailable without reading body; 2xx with top-level `error` and no `choices`→Unavailable reading ONLY `error.code`+sanitized `error.metadata.error_type`; `finish_reason=="error"`→Unavailable) is unchanged in shape, provider-agnostic OpenAI-envelope defensiveness. D-064's blocked-mapping semantics (403 / `content_filter` → Blocked, no breaker count, `AbandonProbe()`) unchanged. | Supersedes D-060/D-061/D-062's OpenRouter-specific wire shape; amends the D-028 gate condition (key→BaseUrl); D-063/D-064 semantics carried over unchanged. | freellmpool is a plain OpenAI-compatible proxy with its own multi-provider failover — RapidRelief's job shrinks to "call the proxy, parse the response, fall back on any failure," exactly as today, just simpler on the wire. |
| **D-114** | **Full provider rename NOW (same precedent as D-065: rename, no aliases).** Folder `Features/Ai/OpenRouter/` → `Features/Ai/FreeLlmPool/`; `IOpenRouterClient`/`OpenRouterClient` → `IFreeLlmPoolClient`/`FreeLlmPoolClient` (signature unchanged: `SendAsync(string requestBody, bool isVision, ct)`); `OpenRouterPromptBuilder` → `FreeLlmPoolPromptBuilder` (`AiPhoto` keeps its name); `OpenRouterResponseParser` → `FreeLlmPoolResponseParser` (`AiParseResult`/`AiParseStatus`/`ParsedAssessment` keep their names); `OpenRouterAiAnalysisService` → `FreeLlmPoolAiAnalysisService`; `OpenRouterAssistantService` → `FreeLlmPoolAssistantService`; named HttpClient `"openrouter"` → `"freellmpool"`; config section `Ai:OpenRouter:*` → `Ai:FreeLlmPool:*` (new `BaseUrl` key added; `TextFallbackModel`/`VisionFallbackModel` **removed** — freellmpool already does automatic multi-provider failover internally, so RapidRelief no longer needs its own primary/fallback model pair; `TextModel`/`VisionModel` become single strings, default `"quality"`, a freellmpool routing alias meaning "route to the best available free model for this request"). Provider string literal in DTOs: `"OpenRouter"` → `"FreeLlmPool"` (`AiAssessmentDto.Provider`, `AssistantAnswer.Provider` — comment-only edits to the frozen contract shape, no field added/removed). Client: `Assistant.razor`'s `LiveProvider` const and `AiInsightPanel.razor`'s provider-label switch arm updated to `"FreeLlmPool"`; `AssistantApi.cs` doc-comment mentions updated. Tests renamed 1:1 (client/builder/parser/service/live-fact/live-smoke files, golden JSON files) with config keys, model literals (`models[]`→`model`), named-client string, URL, and provider-literal assertions updated to match; `X-Title`/`provider.require_parameters`/`reasoning.enabled` assertions dropped; text-path `response_format` assertions changed from `json_schema`/strict to `json_object`. | Supersedes naming in D-060…D-066 (semantics of each survive under the new names, mechanically re-pointed per D-113). | A half-renamed codebase permanently taxes every future reader; this repo already has a working precedent (D-065) for doing the rename as a clean, dedicated pass. |
| **D-115** | **docker-compose sidecar, keyless start only.** `docker-compose.yml` gains a `freellmpool` service (`ghcr.io/0xzr/freellmpool:0.13.0`, port `${FREELLMPOOL_PORT:-8080}` bound to loopback, a named volume `freellmpool-data` for its config, a `/healthz` healthcheck) alongside the existing `postgres` service. **Deliberately no provider API keys configured** — freellmpool's keyless providers (Pollinations, OVHcloud, Kilo Gateway, LLM7) need no credentials per its own README, so the sidecar is usable out of the box; a comment above the service documents that provider keys (`GROQ_API_KEY` etc.) can be added later as `environment:` entries to unlock more capacity. `appsettings.json`'s `Ai` section is replaced per D-113/D-114's config shape, with `BaseUrl: "http://localhost:8080/"` matching this sidecar's default port. | New — no prior compose service existed for the AI transport (OpenRouter was a pure remote API, no local process). | A contributor should be able to `docker compose up` and get a working, if capacity-limited, AI path with zero account setup. |
| **D-116** | **Testing environment defaults `Ai:FreeLlmPool:BaseUrl` to blank.** `TestingWebAppFactory.ConfigureWebHost` now sets `Ai:FreeLlmPool:BaseUrl` to `""` alongside its other test-only settings. Under the old OpenRouter gate, `appsettings.json`'s default `Ai:OpenRouter:ApiKey` was already blank, so every test that didn't explicitly wire an AI client got a free, deterministic, network-free rule-based fallback. Under D-113's new gate, `appsettings.json`'s default `Ai:FreeLlmPool:BaseUrl` is a real, non-blank local address (`http://localhost:8080/` — needed for a working default in dev/prod, D-115), so an unmodified boot in `Testing` would otherwise attempt a real outbound call to a likely-unbound port on every test run, adding latency/flakiness (observed: two `AiDecisionSupportTests` end-to-end pipeline tests timed out waiting for an assessment that was still stuck in a network attempt). Tests that need the real wiring exercised (`AssistantRoleScopeTests`) already re-set a non-blank `BaseUrl` via `WithWebHostBuilder`, which runs after the root factory's `ConfigureWebHost` and wins. | New — mechanical fallout of D-113's gate-condition change, not present in the original task brief. | The gate gained a real, live-reachable default value it never had before; the shared test harness has to compensate the same way `appsettings.json`'s blank `ApiKey` used to, or previously-deterministic tests become flaky/slow through no fault of their own logic. |

**Rename map (D-114)** — folder `Features/Ai/OpenRouter/` → `Features/Ai/FreeLlmPool/`:

| Old                                          | New                                            | Notes                                                                                                      |
| --------------------------------------------- | ----------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `IOpenRouterClient` / `OpenRouterClient`       | `IFreeLlmPoolClient` / `FreeLlmPoolClient`       | signature `SendAsync(string requestBody, bool isVision, ct)` unchanged; client reads `Ai:FreeLlmPool:ApiKey`/`BaseUrl`/timeouts, never a model                                                                     |
| `OpenRouterPromptBuilder` / `AiPhoto`          | `FreeLlmPoolPromptBuilder` / `AiPhoto`           | body rewritten (single `model`, always `json_object`); `AiPhoto` name unchanged (already provider-neutral)                                                                                                          |
| `OpenRouterResponseParser`                    | `FreeLlmPoolResponseParser`                     | parsing/validation logic unchanged — only the namespace/type name moved                                                                                                                                             |
| `OpenRouterAiAnalysisService`                 | `FreeLlmPoolAiAnalysisService`                  | worker type-check in [AiAnalysisWorker.cs](../../src/RapidRelief.Api/Features/Ai/Pipeline/AiAnalysisWorker.cs) follows                                                                                              |
| `OpenRouterAssistantService`                  | `FreeLlmPoolAssistantService`                   | `AssistantPromptBuilder`/`AssistantResponseReader` keep their names, `AssistantPromptBuilder`'s model param changes shape (below)                                                                                   |
| named client `"openrouter"`                   | `"freellmpool"`                                 | [AiModule.cs](../../src/RapidRelief.Api/Features/Ai/AiModule.cs)                                                                                                                                                    |
| Provider `"OpenRouter"`                       | `"FreeLlmPool"`                                 | `AiAssessmentDto`/`AssistantAnswer` emissions and doc-comments, [Assistant.razor](../../src/RapidRelief.Client/Features/Assistant/Pages/Assistant.razor) `LiveProvider` const, [AiInsightPanel.razor](../../src/RapidRelief.Client/Common/Ai/AiInsightPanel.razor) switch arm |
| `LiveOpenRouterFactAttribute`                 | `LiveFreeLlmPoolFactAttribute`                  | keyed to `FREELLMPOOL_API_KEY` **OR** `FREELLMPOOL_BASE_URL` (either opts in — freellmpool is keyless-capable)                                                                                                       |
| `LiveOpenRouterSmokeTests`                    | `LiveFreeLlmPoolSmokeTests`                     | points at `FREELLMPOOL_BASE_URL`, default `http://localhost:8080/`                                                                                                                                                   |

Also touched outside `Features/Ai/**` (comment-only, no shape/behavior change, mirroring what D-065 itself touched): [Program.cs](../../src/RapidRelief.Api/Program.cs) (one rate-limit-policy comment), [AiAssessmentDto.cs](../../src/RapidRelief.Shared/Contracts/ReadModels/AiAssessmentDto.cs) (one doc-comment), [TestingWebAppFactory.cs](../../tests/RapidRelief.Api.Tests/TestingWebAppFactory.cs) (one new `UseSetting` line, D-116).

---

## BLUEPRINT

### File tree delta

```
src/RapidRelief.Api/Features/Ai/
  OpenRouter/                          → FreeLlmPool/                (folder rename)
    IOpenRouterClient.cs               → FreeLlmPool/IFreeLlmPoolClient.cs
    OpenRouterClient.cs                → FreeLlmPool/FreeLlmPoolClient.cs
    OpenRouterPromptBuilder.cs         → FreeLlmPool/FreeLlmPoolPromptBuilder.cs
    OpenRouterResponseParser.cs        → FreeLlmPool/FreeLlmPoolResponseParser.cs
  OpenRouterAiAnalysisService.cs       → FreeLlmPoolAiAnalysisService.cs
  Assistant/OpenRouterAssistantService.cs → Assistant/FreeLlmPoolAssistantService.cs
  (edits, no rename): AiModule.cs, Pipeline/AiAnalysisWorker.cs (type-check line),
    Assistant/AssistantPromptBuilder.cs (models[]→model, drop reasoning),
    AiProviderBlockedException.cs / Assistant/AssistantResponseReader.cs /
    Assistant/IAssistantService.cs / Domain/AiAssessment.cs / Domain/AssistantMessage.cs /
    RuleBasedAiAnalysisService.cs (doc-comment mentions only)
src/RapidRelief.Client/Features/Assistant/Pages/Assistant.razor        (const only)
src/RapidRelief.Client/Common/Ai/AiInsightPanel.razor                  (switch arm only)
src/RapidRelief.Client/Features/Assistant/AssistantApi.cs              (doc-comment only)
src/RapidRelief.Api/appsettings.json                                   (Ai:OpenRouter → Ai:FreeLlmPool)
src/RapidRelief.Api/Program.cs                                         (rate-limit comment only)
src/RapidRelief.Shared/Contracts/ReadModels/AiAssessmentDto.cs         (doc-comment only)
docker-compose.yml                                                    (new freellmpool service + volume)

tests/RapidRelief.Api.Tests/Ai/
  OpenRouterClientTests.cs                 → FreeLlmPoolClientTests.cs
  OpenRouterPromptBuilderTests.cs          → FreeLlmPoolPromptBuilderTests.cs
  OpenRouterResponseParserTests.cs         → FreeLlmPoolResponseParserTests.cs
  OpenRouterAiAnalysisServiceTests.cs      → FreeLlmPoolAiAnalysisServiceTests.cs
  Assistant/OpenRouterAssistantServiceTests.cs → Assistant/FreeLlmPoolAssistantServiceTests.cs
  LiveOpenRouterFactAttribute.cs           → LiveFreeLlmPoolFactAttribute.cs
  LiveOpenRouterSmokeTests.cs              → LiveFreeLlmPoolSmokeTests.cs
  Goldens/openrouter-request-text-only.json                        → freellmpool-request-text-only.json
  Goldens/openrouter-request-with-photo.json                       → freellmpool-request-with-photo.json
  Goldens/openrouter-request-assistant-first-turn.json             → freellmpool-request-assistant-first-turn.json
  Goldens/openrouter-request-assistant-multi-turn-with-shelters.json → freellmpool-request-assistant-multi-turn-with-shelters.json
  (edits, no rename): Assistant/AssistantPromptBuilderTests.cs (models[]→model, golden regen),
    Assistant/AssistantRoleScopeTests.cs, AiDecisionSupportTests.cs,
    Smoke/DiResolutionSmokeTests.cs, Assistant/AssistantApiTests.cs,
    Assistant/AssistantEndpointTests.cs, Assistant/AssistantResponseReaderTests.cs,
    Assistant/AssistantRateLimitTests.cs, Assistant/LiveAssistantSmokeTests.cs
  (new, D-116): ../TestingWebAppFactory.cs (one UseSetting line)
docs/architecture/FreeLlmPool-migration-blueprint.md  (this file)
docs/PROJECT-CONTEXT.md                               (status rows, D-113…D-116, changelog)
```

Deleted: nothing. [OpenRouter-migration-blueprint.md](OpenRouter-migration-blueprint.md) stays untouched as historical record (same precedent as it left the Gemini blueprint untouched).

### `FreeLlmPoolClient` spec

Constructor deps unchanged (`IHttpClientFactory`, `IConfiguration`). Per call:

1. Read `Ai:FreeLlmPool:ApiKey` (blank → `"unused"` on the wire), timeout by `isVision` (`TimeoutSecondsText` 10 / `TimeoutSecondsVision` 20). Does not read model config — the model rides in the body.
2. `CreateClient("freellmpool")` (`BaseAddress` from `Ai:FreeLlmPool:BaseUrl`, falling back to `http://localhost:8080/` if blank — set once at DI registration time in `AiModule`); linked CTS exactly as before; POST `v1/chat/completions`; header `Authorization: Bearer {key-or-"unused"}` (TryAddWithoutValidation). **No** `X-Title` header.
3. Exception mapping — identical shape to the OpenRouter client for timeout/network/cancellation, then:
   - `403` → `AiProviderBlockedException("FreeLlmPool flagged the input (HTTP 403)")` — body not read.
   - other non-2xx → `AiProviderUnavailableException("FreeLlmPool returned HTTP {n}")` — body not read; `429`/`5xx` marked transient (one retry, D-060 unchanged).
   - 2xx: read body; top-level `error` + no `choices` → Unavailable with `"FreeLlmPool 200-level error: code {code}, type {sanitized error_type}"` (`error.message` never read); `choices[0].finish_reason=="error"` → Unavailable `"FreeLlmPool provider mid-generation error"`.
   - else return body string verbatim (parsers reject anything malformed; counts as before).

### Request builders (exact golden layout — key order is insertion order, pinned)

**F8 — `FreeLlmPoolPromptBuilder.Build(AiAnalysisRequest request, AiPhoto? photo, string model)`**:

```json
{ "model": "quality",
  "messages": [
    { "role": "system", "content": "<SystemInstruction — VERBATIM, unchanged>" },
    { "role": "user", "content": "Reported disaster type: Flood\nSOS flag: True\n<incident_description>\n…\n</incident_description>" } ],
  "response_format": { "type": "json_object" },
  "temperature": 0, "max_tokens": 512 }
```

Photo variant: user `content` becomes `[{"type":"text","text":"…"},{"type":"image_url","image_url":{"url":"data:image/jpeg;base64,…"}}]` (unchanged from the OpenRouter shape) — the **only** difference from the text variant is which `model` string is injected; `response_format` is now identically `json_object` on both paths (previously the text path alone used strict `json_schema`). No `provider` key on either path (dropped entirely — was OpenRouter-only). Fencing, closing-tag escaping, 4000-char cap, `UnsafeRelaxedJsonEscaping`, no-PII rule: all unchanged.

**F16 — `AssistantPromptBuilder.Build(ask, options, string model)`**:

```json
{ "model": "quality",
  "messages": [
    { "role": "system", "content": "<SystemInstruction — VERBATIM, unchanged>" },
    { "role": "user", "content": "<user_message>…fenced…</user_message>" },
    { "role": "assistant", "content": "…our sanitized answer…" },
    { "role": "user", "content": "<context>…</context>\n<user_message>…</user_message>" } ],
  "temperature": 0, "max_tokens": 512 }
```

No `response_format`, no `provider`, no `reasoning`. Window logic, fencing regex, context block: byte-identical to before.

### Response parsing

Unchanged from the OpenRouter era: both extract `choices[0].message.content` (string-only stance), capture `usage.total_tokens` → `TotalTokenCount`, `response.model` → `ModelName`. `FreeLlmPoolResponseParser` keeps the tri-state `{Ok, Blocked, Invalid}` result and the same finish-reason policy (`stop` validates, `length`/`error`/missing → Invalid, `content_filter` → Blocked). `AssistantResponseReader` keeps its D-050 semantics unchanged.

### Config (`appsettings.json` replacement)

```json
"Ai": {
  "FreeLlmPool": {
    "BaseUrl": "http://localhost:8080/",
    "ApiKey": "",
    "TextModel": "quality",
    "VisionModel": "quality",
    "TimeoutSecondsText": 10,
    "TimeoutSecondsVision": 20,
    "BreakerFailures": 3,
    "BreakerOpenMinutes": 2
  },
  "Pipeline": { "ChannelCapacity": 100 },
  "Assistant": { "…unchanged…": true }
}
```

`TextFallbackModel`/`VisionFallbackModel` are gone — no fallback-pair concept; freellmpool already does automatic multi-provider failover internally. A blank `BaseUrl` is the new "skip to rule-based/canned, never counts" gate (D-113), replacing the blank-`ApiKey` gate.

### docker-compose sidecar (D-115)

```yaml
  freellmpool:
    image: ghcr.io/0xzr/freellmpool:0.13.0
    ports: ["127.0.0.1:${FREELLMPOOL_PORT:-8080}:8080"]
    volumes: [freellmpool-data:/home/freellmpool/.config/freellmpool]
    healthcheck: { test: ["CMD", "wget", "-qO-", "http://127.0.0.1:8080/healthz"], interval: 10s, timeout: 5s, retries: 10 }
```

Keyless start only; provider keys are optional `environment:` additions per freellmpool's README.

### What does NOT change (enumerate)

Composite chain order (gate check → breaker gate → provider → fallback); `AiCircuitBreaker` internals incl. `AbandonProbe`/half-open semantics; `RuleBasedAiAnalysisService`; `CannedSafetyResponses`; `AssistantSanitizer` + its contract; F8 closed-enum revalidation; `PriorityFormula`/`GeoMath`/`DuplicateDetector`/`IncidentPriorityEngine`; all endpoints + rate policies; `AiDbContext`/migrations/domain shapes; `AiAnalysisWorker` flow (one type-check identifier only); `AssistantRetentionWorker`; client UI markup/flow (const/switch-arm only); all DTO shapes (comment text only); photo policy D-024; timeouts D-026; D-060 retry mechanism; D-063/D-064 error classification and blocked-mapping semantics; metadata-only logging discipline; no PII in request bodies ever.

### Tests migration

- **Fakes**: `FakeOpenRouterClient` → `FakeFreeLlmPoolClient : IFreeLlmPoolClient` in both service test files; canned response bodies keep the same `{"model":"…","choices":[…],"usage":{…}}` shape (already provider-agnostic).
- **Gate tests re-pointed**: "missing API key short-circuits" → "blank BaseUrl short-circuits" (`AiDecisionSupportTests`, `FreeLlmPoolAiAnalysisServiceTests`, `FreeLlmPoolAssistantServiceTests`) — same assertions (no breaker count, zero client calls), keyed off the new config field.
- **Builder tests**: `models[]` assertions → single `model` string assertions; `provider.require_parameters`/`reasoning.enabled` assertions removed; text-path `response_format` assertion changed from `json_schema`/strict to `json_object`; goldens regenerated via `UPDATE_GOLDENS=1` (D-031 mechanism unchanged) then reverified in a clean run.
- **Client tests**: URL assertion `https://openrouter.ai/api/v1/chat/completions` → `http://localhost:8080/v1/chat/completions`; `X-Title` header assertion replaced with an explicit "header is absent" assertion; new test for the blank-key → `Bearer unused` behavior.
- **Provider-literal assertions**: `Assert.Equal("OpenRouter", …)` → `Assert.Equal("FreeLlmPool", …)` across `AiDecisionSupportTests`, `AssistantApiTests`, `FreeLlmPoolAiAnalysisServiceTests`, `FreeLlmPoolAssistantServiceTests`.
- **DI smoke**: `DiResolutionSmokeTests` type assertion re-pointed to `FreeLlmPoolAiAnalysisService`.
- **Role-scope test**: `AssistantRoleScopeTests`' harness now sets a non-blank `Ai:FreeLlmPool:BaseUrl` (was: a non-blank `Ai:OpenRouter:ApiKey`) to make the composite take the provider path before substituting the recording fake.
- **Live smokes**: `LiveFreeLlmPoolFactAttribute` gates on `FREELLMPOOL_API_KEY` **or** `FREELLMPOOL_BASE_URL` (documented reasoning in the class doc-comment: freellmpool is keyless-capable, so gating on the key alone would never let a keyless local sidecar run the smoke). `LiveFreeLlmPoolSmokeTests`/`LiveAssistantSmokeTests` point at `FREELLMPOOL_BASE_URL` (default `http://localhost:8080/`) and assert Provider `"FreeLlmPool"`.
- **New harness default (D-116)**: `TestingWebAppFactory` now blanks `Ai:FreeLlmPool:BaseUrl` by default, so integration tests that don't wire their own AI client keep getting the deterministic, network-free rule-based path they always did — this is what `appsettings.json`'s blank `ApiKey` used to provide for free under the old gate.

### Docs

- **PROJECT-CONTEXT.md**: F8/F16 status-row notes (freellmpool transport, D-113…D-116); decisions appended; changelog entry (newest-first); Contracts v1 Registry (§6) checked and left untouched — `AiAssessmentDto`/`AssistantAnswer` shapes are unchanged, only the `Provider` string *value* changed, which the registry does not pin.
- **PROJECT-AUDIT.md / FINAL-AUDIT.md / RapidRelief-Development-Plan.md / STACK.md**: living-state mentions of OpenRouter as the current transport updated to freellmpool (these describe current state, not historical decisions, so they are edited in place rather than appended to).

---

## VERIFY (actual outcomes, this pass)

- `dotnet build RapidRelief.sln` → 0 warnings, 0 errors.
- `dotnet test` (full solution: `RapidRelief.Api.Tests` + `RapidRelief.Architecture.Tests`) → **911 passed, 0 failed, 2 skipped** (the two opt-in live smokes, no `FREELLMPOOL_API_KEY`/`FREELLMPOOL_BASE_URL` set in this environment).
- Goldens regenerated via `UPDATE_GOLDENS=1` (4 files: text-only, with-photo, assistant first-turn, assistant multi-turn-with-shelters), then reverified green in a clean rerun.
- `grep -rn "OpenRouter" src tests --include=*.cs --include=*.razor` outside `bin`/`obj` → zero identifier/config-key hits; the only remaining mentions are deliberate historical/comparison prose (e.g. "not OpenRouter-compatible", "the OpenRouter-era text path sent…") inside `FreeLlmPoolClient.cs`/`FreeLlmPoolPromptBuilder.cs` doc comments, plus the OpenRouter blueprint itself and PROJECT-CONTEXT.md's changelog history, which are intentionally left as-is.
- Docker live round-trip: attempted `docker compose up -d freellmpool` from the repo root — see PROJECT-CONTEXT.md's changelog entry for this migration for the actual result in this sandbox (network reachability is environment-dependent and is called out plainly there rather than assumed).

## RISKS (implementer traps, carried over + new)

1. **Golden regeneration hiding regressions** — regenerate only after the shape assertions (single `model`, `json_object` on both paths, no `provider`/`reasoning`) pass independently; never `UPDATE_GOLDENS=1` to fix a red assertion you haven't understood.
2. **The gate's default value is no longer inert.** OpenRouter's blank `ApiKey` default meant "off" everywhere by accident; freellmpool's default `BaseUrl` is a real address. Any new test harness that boots the real DI container must consciously decide whether it wants the live path (non-blank `BaseUrl` + a fake/real client) or the rule-based path (blank `BaseUrl`) — it is no longer automatically the latter.
3. **Decision-number collisions.** This migration's own brief proposed IDs that were already taken (see the note under DECISIONS) — always grep the existing decisions log for the next free ID before writing new D-NNN comments, rather than trusting a number handed down from outside the repo.
4. **`error.message` leakage** — unchanged rule: only `code` + sanitized `error_type` may enter exception messages/logs.
5. **Stale `OPENROUTER_API_KEY`/`OPENROUTER_TEXT_MODEL` env vars** — these no longer gate or configure anything; only `FREELLMPOOL_API_KEY`/`FREELLMPOOL_BASE_URL`/`FREELLMPOOL_TEXT_MODEL` do now.

**Open items (non-blocking)**: the live docker round-trip and a real photo request against a keyless freellmpool instance should be re-verified whenever the sidecar image is upgraded past `0.13.0`, the same way D-066 asked for a pre-demo pin check under OpenRouter.
