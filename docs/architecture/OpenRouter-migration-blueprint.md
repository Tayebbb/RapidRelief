# OpenRouter Migration Blueprint (F8 + F16 transport swap)

**Scope**: replace the Gemini transport inside `Features/Ai/` with OpenRouter free models. Everything provider-agnostic (breaker, rule-based, canned, sanitizers, server-side validation, worker, endpoints, DbContexts) survives byte-identical in behavior. Module isolation preserved — no file outside `Features/Ai/`, its tests, config, client assistant page const, and docs is touched.

**Evidence base**: [GeminiClient.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiClient.cs), [GeminiAiAnalysisService.cs](src/RapidRelief.Api/Features/Ai/GeminiAiAnalysisService.cs), [GeminiAssistantService.cs](src/RapidRelief.Api/Features/Ai/Assistant/GeminiAssistantService.cs), [AssistantResponseReader.cs](src/RapidRelief.Api/Features/Ai/Assistant/AssistantResponseReader.cs), [AssistantPromptBuilder.cs](src/RapidRelief.Api/Features/Ai/Assistant/AssistantPromptBuilder.cs), [GeminiPromptBuilder.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiPromptBuilder.cs), [GeminiResponseParser.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiResponseParser.cs), [AiModule.cs](src/RapidRelief.Api/Features/Ai/AiModule.cs), [appsettings.json](src/RapidRelief.Api/appsettings.json#L28-L45), [Assistant.razor](src/RapidRelief.Client/Features/Assistant/Pages/Assistant.razor#L106), OpenRouter research (verified 2026-09-02).

---

## DECISIONS

| ID        | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              | Supersedes/Amends                                                                                                                                                                                                                              | Rationale                                                                                                                       |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| **D-060** | **Transport = OpenRouter chat completions.** Named client `"openrouter"`, `BaseAddress https://openrouter.ai/`, `Timeout Infinite`; POST relative `api/v1/chat/completions`; `Authorization: Bearer {key}` per request (never URL/logs); attribution header `X-Title: RapidRelief` (no `HTTP-Referer` — we have no public URL and won't invent one); no `response-healing` plugin; no streaming; zero retries, `Retry-After` ignored (breaker's 2-min window is our pacing); 402/429/5xx are plain breaker-counted failures. D-026 timeout mechanism + values unchanged (10 s text / 20 s vision, linked CTS).                                                                                                                        | Supersedes D-023 (transport), amends D-026 (values/mechanism kept, provider swapped). D-055 stands (one client seam, one shared breaker, no streaming).                                                                                        | Zero new packages; smallest delta from the working design                                                                       |
| **D-061** | **Model pins + OpenRouter-level fallback.** Body carries `models: [primary, fallback]` (fallback omitted if config empty → single-element array; `model` key not sent — `models` is the single source of truth). Text pair: `z-ai/glm-5.2:free` → `nvidia/nemotron-3-super-120b-a12b:free`. Vision pair: `google/gemma-4-31b-it:free` → `minimax/minimax-m3:free`. F16 uses the text pair. **Every request sends `reasoning: {"enabled": false}`** (GLM-5.2 defaults reasoning on → would burn the 256-token budget and the 10 s timeout; harmless on non-reasoning models). Expiring `dots-3-note` and reasoning-mandatory models excluded. `ModelName` telemetry now = `response.model` (actual routed model), not the config echo. | Supersedes D-023 (pin), extends D-028 chain with an availability-only (not quota) tier before rule-based.                                                                                                                                      | Research rec 2/8, risk 5                                                                                                        |
| **D-062** | **F8 routing split by photo (Q1: split).** No-photo → text pair with `response_format: json_schema, strict:true` + `provider: {"require_parameters": true}` (routes only to schema-conforming endpoints; unsupported = hard error per research rec 4). Photo → vision pair with `response_format: {"type":"json_object"}`, **no** `require_parameters` (would shrink the free vision pool; our parser is the enforcement anyway). D-024 survives whole: first photo only, ≤10 MiB, base64 — now as `image_url` data-URL part appended **after** the text part.                                                                                                                                                                        | Amends D-023/D-024 wire shape; D-024 policy intact.                                                                                                                                                                                            | No free model does strict-schema+vision (research risk 4); server-side validation (§4.5) carries correctness on the vision path |
| **D-063** | **Three-way error classification in the client; bounded body read.** (1) non-2xx except 403 → `AiProviderUnavailableException("OpenRouter returned HTTP {n}")`, **body never read**; (2) 2xx body with top-level `error` and no `choices` → Unavailable, reading **only** `error.code` + `error.metadata.error_type` (sanitized `[A-Za-z0-9_]`≤32, same helper pattern as [GeminiResponseParser.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiResponseParser.cs#L188-L193)) — `error.message` is never read into any string; (3) `choices[0].finish_reason == "error"` → Unavailable ("provider mid-generation error"). All three count against the breaker in both services (existing catch blocks).                              | Amends D-026's "never read the body" (the 200-with-error case forces it — research delta a/risk 2); amends the candidate-less-200 classification note (that check stays in parsers as backstop: no-choices without `error` = Invalid, counts). | A status-only client would record provider failures as successes and poison the breaker                                         |
| **D-064** | **Blocked mapping, extended to F8.** HTTP **403** (OpenRouter = input moderation, status alone suffices) → client throws new `AiProviderBlockedException`; both services catch it before the failure catch-all → canned/rule-based + `AbandonProbe()`, **no breaker count**. `finish_reason == "content_filter"` on 200 → F16 reader `Blocked` (existing D-050 path), **F8 parser now also returns a tri-state** `{Ok, Blocked, Invalid}` and the composite maps Blocked → fallback + AbandonProbe, no count.                                                                                                                                                                                                                         | Extends D-050 to F8; supersedes D-050's Gemini-specific signal list (`promptFeedback.blockReason` → 403 / `content_filter`). D-050's DoS rationale unchanged.                                                                                  | Three hostile reports must not disable AI globally for 2 min                                                                    |
| **D-065** | **Full provider rename NOW (Q3: rename, no aliases).** Exact map below. Provider string emitted in DTOs becomes `"OpenRouter"`; DTO _shapes_ frozen (comment-only contract edit); client badge const updated (one line). Env: `OPENROUTER_API_KEY` (+ optional `OPENROUTER_TEXT_MODEL` override in live smokes). Config: `Ai:Gemini:*` → `Ai:OpenRouter:*`.                                                                                                                                                                                                                                                                                                                                                                           | Supersedes naming in D-023/D-025/D-026/D-028/D-030/D-055 (semantics of each survive under the new names).                                                                                                                                      | A half-renamed codebase permanently taxes every future reader; tests exist to catch the mechanical diff                         |
| **D-066** | **Quota posture (Q2): change nothing in the worker.** Free tier = 20 rpm / 50 rpd (account-level); after cap, 429s open the breaker → rule-based, which is the designed §4.5 degradation. No quota-aware routing, no priority reservation. **Pre-demo checklist replaces the D-023 Wk-10 item**: (a) buy $10 lifetime credits → 1000 rpd, or explicitly accept 50 rpd; (b) 5-min live pin check — both pinned pairs still in the catalog (`GET /api/v1/models`), latency sane with reasoning off; swap pins via config only.                                                                                                                                                                                                          | Supersedes D-023's Wk-10 task.                                                                                                                                                                                                                 | Speculative quota logic guards a demo-day number that $10 fixes                                                                 |

**Rename map (D-065)** — folder `Features/Ai/Gemini/` → `Features/Ai/OpenRouter/`:

| Old                                   | New                                      | Notes                                                                                                                                                                                                              |
| ------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `IGeminiClient` / `GeminiClient`      | `IOpenRouterClient` / `OpenRouterClient` | signature `SendAsync(string requestBody, bool isVision, ct)` — D-030 seam confirmed, client stays transport-only; model now rides **in the body**, injected by builders, client no longer reads `Model` config     |
| `GeminiUnavailableException`          | `AiProviderUnavailableException`         | moves to `Features/Ai/` root (provider-neutral, used by both services)                                                                                                                                             |
| — (new)                               | `AiProviderBlockedException`             | `Features/Ai/` root; D-064                                                                                                                                                                                         |
| `GeminiCircuitBreaker`                | `AiCircuitBreaker`                       | moves to `Features/Ai/` root; internals byte-identical                                                                                                                                                             |
| `GeminiPromptBuilder` / `GeminiPhoto` | `OpenRouterPromptBuilder` / `AiPhoto`    | body rewritten (below)                                                                                                                                                                                             |
| `GeminiResponseParser`                | `OpenRouterResponseParser`               | tri-state result (D-064)                                                                                                                                                                                           |
| `GeminiAiAnalysisService`             | `OpenRouterAiAnalysisService`            | worker type-check at [AiAnalysisWorker.cs](src/RapidRelief.Api/Features/Ai/Pipeline/AiAnalysisWorker.cs#L149) follows                                                                                              |
| `GeminiAssistantService`              | `OpenRouterAssistantService`             | `AssistantPromptBuilder`/`AssistantResponseReader` keep their names, internals rewritten                                                                                                                           |
| named client `"gemini"`               | `"openrouter"`                           | [AiModule.cs](src/RapidRelief.Api/Features/Ai/AiModule.cs#L38-L43)                                                                                                                                                 |
| Provider `"Gemini"`                   | `"OpenRouter"`                           | `AiAssessmentDto`/`AssistantAnswer` emissions, domain-comment docs, [Assistant.razor](src/RapidRelief.Client/Features/Assistant/Pages/Assistant.razor#L106) `GeminiProvider` const → `LiveProvider = "OpenRouter"` |
| `LiveGeminiFactAttribute`             | `LiveOpenRouterFactAttribute`            | keyed to `OPENROUTER_API_KEY`                                                                                                                                                                                      |

---

## BLUEPRINT

### File tree delta

```
src/RapidRelief.Api/Features/Ai/
  Gemini/                              → OpenRouter/          (folder rename)
    IGeminiClient.cs                   → OpenRouter/IOpenRouterClient.cs
    GeminiClient.cs                    → OpenRouter/OpenRouterClient.cs
    GeminiPromptBuilder.cs             → OpenRouter/OpenRouterPromptBuilder.cs
    GeminiResponseParser.cs            → OpenRouter/OpenRouterResponseParser.cs
    GeminiCircuitBreaker.cs            → AiCircuitBreaker.cs               (to Ai/ root)
    GeminiUnavailableException.cs      → AiProviderUnavailableException.cs (to Ai/ root)
  (new) AiProviderBlockedException.cs
  GeminiAiAnalysisService.cs           → OpenRouterAiAnalysisService.cs
  Assistant/GeminiAssistantService.cs  → Assistant/OpenRouterAssistantService.cs
  (edits, no rename): AiModule.cs, Pipeline/AiAnalysisWorker.cs (type-check line),
    Assistant/{AssistantPromptBuilder,AssistantResponseReader}.cs, Domain comments
src/RapidRelief.Client/Features/Assistant/Pages/Assistant.razor   (const only)
src/RapidRelief.Api/appsettings.json                              (Ai:Gemini → Ai:OpenRouter)
src/RapidRelief.Api/Program.cs                                    (comment L106 only)

tests/RapidRelief.Api.Tests/Ai/
  GeminiClientTests.cs                 → OpenRouterClientTests.cs
  GeminiPromptBuilderTests.cs          → OpenRouterPromptBuilderTests.cs
  GeminiResponseParserTests.cs         → OpenRouterResponseParserTests.cs
  GeminiCircuitBreakerTests.cs         → AiCircuitBreakerTests.cs           (rename-only)
  GeminiAiAnalysisServiceTests.cs      → OpenRouterAiAnalysisServiceTests.cs
  Assistant/GeminiAssistantServiceTests.cs → Assistant/OpenRouterAssistantServiceTests.cs
  LiveGeminiFactAttribute.cs           → LiveOpenRouterFactAttribute.cs
  LiveGeminiSmokeTests.cs              → LiveOpenRouterSmokeTests.cs
  Goldens/gemini-request-text-only.json                        → openrouter-request-text-only.json
  Goldens/gemini-request-with-photo.json                       → openrouter-request-with-photo.json
  Goldens/gemini-request-assistant-first-turn.json             → openrouter-request-assistant-first-turn.json
  Goldens/gemini-request-assistant-multi-turn-with-shelters.json → openrouter-request-assistant-multi-turn-with-shelters.json
README.md, PROJECT-CONTEXT.md                                   (docs section below)
```

Deleted: nothing else. `docs/architecture/F8-blueprint.md`/F16 blueprint stay untouched as Gemini-era historical records (superseded by D-060+; one-line banner optional).

### `OpenRouterClient` spec

Constructor deps unchanged (`IHttpClientFactory`, `IConfiguration`). Per call:

1. Read `Ai:OpenRouter:ApiKey`, timeout by `isVision` (`TimeoutSecondsText` 10 / `TimeoutSecondsVision` 20). **Does not read model config** (D-061 — model is in the body).
2. `CreateClient("openrouter")`; linked CTS exactly as today ([GeminiClient.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiClient.cs#L35-L37)); POST `api/v1/chat/completions`; headers `Authorization: Bearer {key}` (TryAddWithoutValidation), `X-Title: RapidRelief`.
3. Exception mapping — same shape as today for timeout/network (caller-cancellation passthrough preserved verbatim), then:
   - `403` → `throw new AiProviderBlockedException("OpenRouter flagged the input (HTTP 403)")` — body not read.
   - other non-2xx → `AiProviderUnavailableException("OpenRouter returned HTTP {n}")` — body not read.
   - 2xx: read body; `JsonDocument.Parse`; if top-level `error` present and no `choices` → Unavailable with `"OpenRouter 200-level error: code {error.code}, type {sanitized error_type}"` (D-063 — only these two fields, `error.message` never touched); if `choices[0].finish_reason == "error"` → Unavailable `"OpenRouter provider mid-generation error"`. Unparseable 2xx body → return verbatim (parsers reject it; counts — unchanged posture).
   - else return body string.

### Request builders (exact golden layout — key order is insertion order, pinned)

**F8 — `OpenRouterPromptBuilder.Build(AiAnalysisRequest request, AiPhoto? photo, IReadOnlyList<string> models)`** (composite picks text pair vs vision pair by `photo is null`):

```json
{ "models": ["z-ai/glm-5.2:free", "nvidia/nemotron-3-super-120b-a12b:free"],
  "messages": [
    { "role": "system", "content": "<SystemInstruction — VERBATIM, unchanged from today>" },
    { "role": "user", "content": "Reported disaster type: Flood\nSOS flag: True\n<incident_description>\n…\n</incident_description>" } ],
  "response_format": { "type": "json_schema", "json_schema": {
      "name": "incident_assessment", "strict": true, "schema": { …ResponseJsonSchema verbatim… } } },
  "provider": { "require_parameters": true },
  "temperature": 0, "max_tokens": 256, "reasoning": { "enabled": false } }
```

Photo variant (vision pair): user `content` becomes a **parts array** — `[{"type":"text","text":"…same text…"},{"type":"image_url","image_url":{"url":"data:image/jpeg;base64,…"}}]` (text before image per docs); `response_format` = `{"type":"json_object"}`; **no `provider` key**. Fencing, closing-tag escaping, 4000-char cap, `UnsafeRelaxedJsonEscaping`, no-PII rule: all unchanged from [GeminiPromptBuilder.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiPromptBuilder.cs#L60-L76).

**F16 — `AssistantPromptBuilder.Build(ask, options, IReadOnlyList<string> models)`** (always text pair):

```json
{ "models": [ …text pair… ],
  "messages": [
    { "role": "system", "content": "<SystemInstruction — VERBATIM, unchanged>" },
    { "role": "user", "content": "<user_message>…fenced…</user_message>" },
    { "role": "assistant", "content": "…our sanitized answer…" },
    { "role": "user", "content": "<context>…</context>\n<user_message>…</user_message>" } ],
  "temperature": 0, "max_tokens": 512, "reasoning": { "enabled": false } }
```

`role:"model"` → `"assistant"` (kills the old extrapolated-literal risk noted in PROJECT-CONTEXT). Window logic, fencing regex, context block: byte-identical. No `response_format`, no `provider`.

### Response parsing

Both extract `choices[0].message.content` — **string-only stance**: docs guarantee string for non-streaming; a non-string/missing content → Invalid (counts). No content-parts array handling (speculative complexity; breaker + fallback absorb exotic providers). Capture `usage.total_tokens` → `TotalTokenCount`, `response.model` → `ModelName` (D-061).

**F8 `OpenRouterResponseParser`** returns `{Ok, Blocked, Invalid}` + reject reason:

- `finish_reason`: `"stop"` → proceed to inner-JSON validation (unchanged: closed enum case-sensitive, severity 1–5, summary clamp 200 + control-strip, confidence clamp — [GeminiResponseParser.cs](src/RapidRelief.Api/Features/Ai/Gemini/GeminiResponseParser.cs#L58-L100) logic verbatim); `"length"` → Invalid (truncated JSON is useless — counts); `"content_filter"` → **Blocked**; `"error"` → Invalid (client backstop); missing/other → Invalid. No `choices` without top-level `error` → Invalid (candidate-less fix preserved).
- Composite mapping: Ok → success path; Blocked → rule-based + `AbandonProbe()`, no count, log `"blocked"` metadata; Invalid → `throw AiProviderUnavailableException($"Response rejected: {reason}")` (counts — today's pattern).

**F16 `AssistantResponseReader`** (D-050 semantics, new signals): `"stop"` → Ok; `"length"` → Ok + `Truncated:true`; `"content_filter"` → Blocked; `"error"` → **Invalid** (counts — backstop); missing/other → Blocked (today's posture); no-choices without `error` → Invalid (fix preserved, [AssistantResponseReader.cs](src/RapidRelief.Api/Features/Ai/Assistant/AssistantResponseReader.cs#L62-L74)). Sanitizer, `EmptyAfterSanitize`, canned taxonomy: untouched.

**Both services** add one catch before the failure catch-all: `catch (AiProviderBlockedException)` → canned/rule-based + `AbandonProbe()` + metadata-only log, no count.

### Config ([appsettings.json](src/RapidRelief.Api/appsettings.json#L28-L37) replacement)

```json
"Ai": {
  "OpenRouter": {
    "ApiKey": "",
    "TextModel": "z-ai/glm-5.2:free",
    "TextFallbackModel": "nvidia/nemotron-3-super-120b-a12b:free",
    "VisionModel": "google/gemma-4-31b-it:free",
    "VisionFallbackModel": "minimax/minimax-m3:free",
    "TimeoutSecondsText": 10, "TimeoutSecondsVision": 20,
    "BreakerFailures": 3, "BreakerOpenMinutes": 2 },
  "Pipeline": { "ChannelCapacity": 100 },
  "Assistant": { …unchanged… } }
```

Services compose `models` arrays from these keys (empty fallback → single-element). Empty `ApiKey` → straight to rule-based/canned, never counts (D-028 rule verbatim).

### What does NOT change (enumerate)

Composite chain order (key check → breaker gate → provider → fallback); `AiCircuitBreaker` internals incl. `AbandonProbe`/half-open semantics (D-025); `RuleBasedAiAnalysisService`; `CannedSafetyResponses`; `AssistantSanitizer` + D-051 contract; F8 closed-enum revalidation; `PriorityFormula`/`GeoMath`/`DuplicateDetector`; all endpoints + rate policies (`Ai` 30/60s, `Assistant` 12/300s, D-054); `AiDbContext`/migrations/domain shapes; `AiAnalysisWorker` flow (one type-check identifier only); `AssistantRetentionWorker`; client UI markup/flow (one const); all DTO shapes (comment text only); photo policy D-024; timeouts D-026; zero retries; metadata-only logging discipline.

### Tests migration

- **Fakes**: `FakeGeminiClient` → `FakeOpenRouterClient : IOpenRouterClient` in both service test files; canned response bodies rewritten to `{"model":"…","choices":[{"message":{"content":"…"},"finish_reason":"stop"}],"usage":{"total_tokens":N}}`.
- **Fallback matrices** ([GeminiAiAnalysisServiceTests.cs](tests/RapidRelief.Api.Tests/Ai/GeminiAiAnalysisServiceTests.cs), [GeminiAssistantServiceTests.cs](tests/RapidRelief.Api.Tests/Ai/Assistant/GeminiAssistantServiceTests.cs)): same cases re-pointed; finish literals `STOP/MAX_TOKENS/SAFETY` → `stop/length/content_filter`; provider asserts `"Gemini"` → `"OpenRouter"`.
- **NEW client tests** (in `OpenRouterClientTests`): 200 + top-level error/no choices → Unavailable, message contains code+error_type and **not** the error message text; `finish_reason:"error"` → Unavailable; 403 → `AiProviderBlockedException`; header assert `Authorization: Bearer` + `X-Title`; named-client assert `"openrouter"`; body-never-in-exception assert for non-2xx.
- **NEW service tests**: F8 `content_filter` → rule-based + no breaker count + probe released; F16 403-Blocked → canned + no count.
- **Goldens**: regenerate via `UPDATE_GOLDENS=1` (D-031 mechanism unchanged); vision golden keeps `<BASE64_PHOTO>` normalization, now inside the data-URL; independent verbatim asserts re-pointed (`messages[0].content` for systemInstruction — text itself unchanged; schema verbatim; `temperature/max_tokens/reasoning/models/response_format/provider` block asserts); no-PII assert unchanged.
- **Live smokes**: `LiveOpenRouterFactAttribute` gates on `OPENROUTER_API_KEY`; optional `OPENROUTER_TEXT_MODEL` override; assert Provider `"OpenRouter"`, valid closed enum, severity 1–5, `ModelName` non-null (actual routed model), `FinishReason == "stop"` — completing within the 10 s timeout is the reasoning-disabled latency sanity check. Assistant smoke likewise.
- Everything else (endpoints, pipeline, retention, rate-limit, wire-contract, architecture tests) unchanged — [AssistantApiTests.cs](tests/RapidRelief.Api.Tests/Ai/Assistant/AssistantApiTests.cs#L67) fake-provider literals updated mechanically.

### Docs

- **README**: stack line + env-var table (`Ai:OpenRouter:*`, `OPENROUTER_API_KEY`); consent section rewritten with the research's approved wording: _"AI features route via OpenRouter to third-party free model providers, which may log and train on submitted content (incident descriptions, photos, assistant chats) per their own policies. Do not include personal or sensitive data. Routing to training providers can be disabled in the OpenRouter account privacy settings, which may reduce free-model availability."_
- **PROJECT-CONTEXT.md**: F8/F16 status-row notes (OpenRouter transport, models, D-060+); D-060–D-066 appended; changelog entry; pre-demo checklist item per D-066 replacing the D-023 Wk-10 check.

---

## IMPLEMENTATION CHUNK (one)

Ordered internal sequence:

1. **Rename mechanics** (git mv folder + files, identifiers, namespaces, `"gemini"`→`"openrouter"`, worker type-check, razor const, test renames incl. breaker tests) — compile green, most tests still red is expected only after step 2 begins; do renames as a pure no-behavior commit first (all tests green with old wire shapes still in place).
2. **Transport**: `OpenRouterClient` (URL, headers, D-063 classification, `AiProviderBlockedException`).
3. **Builders**: `OpenRouterPromptBuilder` + `AssistantPromptBuilder` body rewrite + `models` injection from services.
4. **Parsers/readers**: tri-state F8 parser, F16 reader signal remap; service Blocked catches; `ModelName` from `response.model`.
5. **Tests**: fakes + matrices + new classification tests; `UPDATE_GOLDENS=1` regeneration; verbatim asserts.
6. **Config + docs**: appsettings, README, PROJECT-CONTEXT (§3 rows, §7 D-060+, §8 changelog).

**Verify** (expected outcomes):

- `dotnet build RapidRelief.sln -c Release` → 0 warnings (TreatWarningsAsErrors).
- `dotnet test` offline → all green, live smokes **skipped** (no `OPENROUTER_API_KEY`).
- `grep -ri "gemini\|GEMINI_API_KEY" src tests README.md --exclude-dir obj,bin` → zero hits (PROJECT-CONTEXT/blueprints historical hits allowed).
- With `OPENROUTER_API_KEY` set: `dotnet test --filter LiveOpenRouter` → 2 smokes green.

## TEST PLAN (delta only)

New: 3-way client classification (3 tests) + 403-Blocked (2, client+service) + F8 content_filter no-count (1) + header/attribution (1). Rewritten: builders' golden + verbatim tests, parser/reader matrices, service matrices (literals only), live smokes. Rename-only: breaker tests, everything else. Removed: none.

## DOD

Offline suite green with fakes; goldens byte-stable across two consecutive runs; live smokes green with a real key; zero `Gemini` identifiers in `src/`+`tests/`; breaker/blocked matrix proves: 3 transport failures open, 403/content_filter never count, probe released on block+cancel; PROJECT-CONTEXT §3/§7/§8 updated in the same PR.

## RISKS (implementer traps)

1. **Golden regeneration hiding regressions** — regenerate only after the verbatim asserts (system text, schema, `reasoning`, `models`, `response_format`) pass independently; never `UPDATE_GOLDENS=1` to fix a red verbatim assert.
2. **Breaker classification gaps** — the 200-with-error and `finish_reason:"error"` paths must throw _Unavailable_ (count), while 403/`content_filter` must _not_; a missed catch-order (`AiProviderBlockedException` after the generic catch) silently reverts D-064.
3. **Stale `GEMINI_API_KEY`** — CI/local env vars keep live smokes silently skipped; grep-gate in verify list.
4. **`error.message` leakage** — it can echo user content; only `code` + sanitized `error_type` may enter exception messages/logs (D-063).
5. **Formatter/analyzer interference** — folder+identifier rename in one commit with zero behavior change, or reviewers can't see the wire-format diff; keep step 1 separate.
6. **Legacy `"Gemini"` provider rows** — old assistant rows (≤7-day retention) will badge as offline guidance in the client; self-heals within a week, do not add compat code.

**Open items (non-blocking)**: 10 MiB photo fit per free vision provider is medium-confidence — the pre-demo live check (D-066) should include one photo request; if OpenRouter rejects single-element `models` arrays (undocumented edge), fall back to emitting `model` instead — client/builder seam isolates the change to one line.
