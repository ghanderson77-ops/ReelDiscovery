---
phase: 01-foundation-provider-gateway
plan: 05
subsystem: ai-gateway
tags: [llm, openai, structured-outputs, error-classification, key-test, gpt-image, tts]
requires:
  - 01-03 (ILlmProvider, LlmProviderRequest/Response, LlmFailureKind, model catalog)
provides:
  - OpenAITextProvider (ILlmProvider for OpenAI, strict response_format json_schema)
  - QuotaAwareRetryPolicy (SDK transport retries skip 429 insufficient_quota)
  - OpenAIMediaClient (gpt-image images, tts-1 speech)
  - GenerationConfig.ImageModel / ImageQuality
affects:
  - 01-06/01-07 (gateway wiring and keys screen construct OpenAITextProvider(apiKey))
  - 01-11 (EmailGenerator media calls move to OpenAIMediaClient; OpenAIService deleted)
tech-stack:
  added: []
  patterns:
    - "ClientRetryPolicy subclass overrides ShouldRetry/ShouldRetryAsync to veto retries on a non-recoverable 429 body"
    - "OPENAI001 experimental diagnostics suppressed only around the single statement that needs them"
key-files:
  created:
    - Services/Ai/OpenAITextProvider.cs
    - Services/Ai/QuotaAwareRetryPolicy.cs
    - Services/Ai/OpenAIMediaClient.cs
    - ReelDiscovery.Tests/Ai/OpenAI/OpenAIWireTests.cs
    - ReelDiscovery.Tests/Ai/OpenAI/OpenAIOutcomeClassificationTests.cs
    - ReelDiscovery.Tests/Ai/OpenAI/OpenAIKeyTestTests.cs
    - ReelDiscovery.Tests/Ai/OpenAI/OpenAIMediaClientTests.cs
  modified:
    - Models/GenerationConfig.cs
decisions:
  - "D-15 confirmed against the official model page: gpt-6.1-sol supports Chat Completions, strict Structured Outputs and reasoning_effort low; catalog values (128K output, $2/$10) unchanged"
  - "OpenAI status 0 (transport wraps HttpRequestException in ClientResultException) maps to Network"
  - "ReasoningEffortLevel and ImageGenerationOptions.OutputFileFormat are OPENAI001-experimental in 2.14.0; suppressed per statement with a comment"
metrics:
  duration: 8min
  completed: 2026-09-30
  tasks: 2
  files: 8
---

# Phase 01 Plan 05: OpenAI translator and media client Summary

OpenAI now works as an `ILlmProvider` on OpenAI 2.14.0:
- Requests use strict `response_format` json_schema Chat Completions.
- `reasoning_effort` is sent only when the catalog sets it, and no sampling parameters are sent.
- Responses are classified as refusal, truncation or content filter.
- API errors map by status and body to a failure kind.
- The SDK transport never retries a 429 `insufficient_quota`.
- The key test is free (a model retrieve).

Images now use gpt-image-2.5-flare (low, 1024x1024, png) through a new media client, and speech stays on tts-1.

## D-15 verification

- **Source:** https://platform.openai.com/docs/models/gpt-6.1-sol, which redirects to https://developers.openai.com/api/docs/models/gpt-6.1-sol. Fetched 2026-09-30.
- **(a) Chat Completions:** supported. The endpoints list includes `v1/chat/completions`, with the note "Chat Completions is supported without tool calling". That is fine here, because we use no tools.
- **(b) Structured outputs:** "Supported" in the Features list, so `json_schema` with `strict: true` works.
- **(c) reasoning_effort:** supports `low`, `medium` (default), `high`, `xhigh` and `max`. **`none` and `minimal` are NOT supported.** The catalog row uses `low`, so this is OK.
- **(d) Limits and pricing:** 128,000 max output tokens and a 1,050,000 context window. Prices are $2.00 input and $10.00 output per 1M tokens. These match the 01-03 catalog row, so no correction was needed.
- **Other catalog rows:** checked on the same site for the downstream notes.
  - gpt-6-sol and gpt-6-luna both accept `none`, which matches their catalog `Effort = "none"`.
  - Both have 128K output.
  - gpt-6-sol is $2/$10 and gpt-6-luna is $0.10/$0.50, which matches the catalog.

## What was built

- **`OpenAITextProvider`** (`Services/Ai/OpenAITextProvider.cs`):
  - **Constructors:** the production constructor takes the key and uses `CreateDefaultOptions()`, which sets `NetworkTimeout` to 10 minutes and `RetryPolicy` to `new QuotaAwareRetryPolicy()`. An internal constructor takes `OpenAIClientOptions` so tests can inject a fake transport.
  - **`ChatClient` cache:** one client per model id.
  - **`BuildOptions`:** `CreateJsonSchemaFormat(..., jsonSchemaIsStrict: true)` and `MaxOutputTokenCount`, which is sent as `max_completion_tokens`. `ReasoningEffortLevel` is set only when `Effort != null`.
  - **`BuildMessages`:** a system message then a user message, with no "Respond ONLY with valid JSON" suffix.
  - **`Classify`** (refusal is checked first):
    - non-empty refusal: Refused
    - Length: Truncated
    - ContentFilter: Refused
    - Stop: Succeeded, with `Content[0].Text`
    - anything else: Failed / Unknown
    - Usage is read null-safely.
  - **`MapStatus`:**
    - 401/403: Auth
    - 429 with `insufficient_quota`: SpendLimit
    - other 429: RateLimited
    - 400/404/422: BadRequest
    - 5xx: Transient
    - 0: Network
    - anything else: Unknown
  - **`SendAsync` / `TestKeyAsync`:** `OperationCanceledException` is rethrown. `ClientResultException` becomes a message built from the failure-kind prefix plus `error.message` from the body only, so it never contains the key. `HttpRequestException` becomes Network. There is no app retry loop and no `Task.Delay`.
  - **Key test messages:** "OpenAI key OK (id)", "Key rejected (authentication failed)", "Model 'id' is not available to this key", "Could not reach api.openai.com".
- **`QuotaAwareRetryPolicy`:** subclasses `ClientRetryPolicy`. `ShouldRetry` and `ShouldRetryAsync` return false for a 429 whose `BufferContent()` contains `insufficient_quota`. Everything else defers to the base policy.
- **`OpenAIMediaClient`:**
  - **`GenerateImageAsync`:** uses the configured model and quality at 1024x1024 png, and never sets `response_format` or `style`. A 400 returns null.
  - **`GenerateSpeechAsync`:** tts-1, with the voice switch copied verbatim from `OpenAIService`. A 400 returns null.
  - There are no app-level 429/503 loops, because the SDK transport retries.
- **`GenerationConfig`:** `ImageModel = "gpt-image-2.5-flare"`, `ImageQuality = "low"`.

## Verification

- **OpenAI tests:** 29 in total, all passing.
  - OpenAIWireTests: 5
  - OpenAIOutcomeClassificationTests: 15, including 8 theory rows and an extra `PlainRateLimit_StillRetriedByTransport`
  - OpenAIKeyTestTests: 4
  - OpenAIMediaClientTests: 5
- **Full suite:** `dotnet test`, 109 passed and 0 failed.
- **Build:** `dotnet build ReelDiscovery.csproj`, 0 warnings and 0 errors.
- **Acceptance greps:** all pass for both tasks (strict flag, no JSON-object mode, retry policy wired, `GetModelAsync`, no bare catch, no `Task.Delay`/`MaxRetries`, no dall-e/Style/ResponseFormat, `"tts-1"` present, config defaults).
- **Quota guard:** with `QuotaAwareRetryPolicy(3)`, a [429 insufficient_quota, 200] sequence produced exactly 1 HTTP call and SpendLimit. A plain `rate_limit_exceeded` 429 is still retried (2 calls, then Succeeded).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] OPENAI001 on OutputFileFormat**
- **Found during:** Task 2
- **Issue:** `ImageGenerationOptions.OutputFileFormat` and `GeneratedImageFileFormat` raise the OPENAI001 experimental error in 2.14.0. The plan only anticipated this for `ReasoningEffortLevel`.
- **Fix:** A per-statement `#pragma warning disable/restore OPENAI001` with an explanatory comment, the same approach used for `ReasoningEffortLevel` in Task 1.
- **Files modified:** Services/Ai/OpenAIMediaClient.cs
- **Commit:** 5b85693

**2. [Addition] Plain rate-limit retry test**
- `PlainRateLimit_StillRetriedByTransport` was added. It proves that the quota guard does not disable ordinary SDK retries.

**3. [Adjustment] Wire and media assertions parse JSON**
- Tests parse the request body with `JsonNode` instead of matching raw substrings. The key and value assertions are the same, but they are robust to serializer spacing. Absence checks (`response_format`, `style`, `reasoning_effort`, `temperature`) are key-based.

## Notes for downstream plans

- gpt-6.1-sol rejects `reasoning_effort: "none"` or `"minimal"`. If a user or the model-config dialog ever changes the Story row's effort, restrict it to low, medium, high, xhigh or max.
- OpenAI tool calling for gpt-6.1-sol requires the Responses API. The gateway uses no tools, so this does not affect Phase 1.
- Media usage (images and TTS) is not token-tracked. `operationName` is accepted but unused.

## TDD Gate Compliance

- **Task 1:** RED f2d1d5e (test), then GREEN db868a9 (feat).
- **Task 2:** RED 8d4505e (test), then GREEN 5b85693 (feat).
- No refactor commits were needed.

## Commits

- f2d1d5e test(01-05): add failing OpenAI wire, classification and key test tests
- db868a9 feat(01-05): implement OpenAITextProvider with quota-aware retry and free key test
- 8d4505e test(01-05): add failing OpenAIMediaClient tests for gpt-image and tts-1
- 5b85693 feat(01-05): add OpenAIMediaClient (gpt-image + tts-1) and configurable image settings

## Self-Check: PASSED
