---
phase: 01-foundation-provider-gateway
plan: 04
subsystem: ai-gateway
tags: [llm, anthropic, claude, structured-outputs, error-classification, key-test]
requires:
  - 01-03 (ILlmProvider, LlmProviderRequest/Response, LlmFailureKind, JsonSchemaRegistry)
provides:
  - AnthropicTextProvider (ILlmProvider for Claude, strict output_config json_schema)
  - AnthropicSpendLimitHandler (spend-cap 429s marked non-retryable for the SDK)
  - Free Anthropic key test (Models.Retrieve)
affects:
  - 01-06/01-07 (gateway wiring and keys screen construct AnthropicTextProvider(apiKey))
tech-stack:
  added: []
  patterns:
    - "DelegatingHandler in front of the SDK transport rewrites retry hints (x-should-retry: false) for non-recoverable 429s"
    - "Provider translators classify by raw stop_reason string and by status + body, never by position or exception message"
key-files:
  created:
    - Services/Ai/AnthropicTextProvider.cs
    - Services/Ai/AnthropicSpendLimitHandler.cs
    - ReelDiscovery.Tests/Ai/Anthropic/AnthropicWireTests.cs
    - ReelDiscovery.Tests/Ai/Anthropic/AnthropicOutcomeClassificationTests.cs
    - ReelDiscovery.Tests/Ai/Anthropic/AnthropicKeyTestTests.cs
  modified:
    - Services/Ai/JsonSchemaRegistry.cs
    - ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs
decisions:
  - "Anthropic spend-cap guard uses x-should-retry: false; the 402 status-rewrite fallback was not needed (Anthropic 12.51.0 honours the header)"
  - "MaxNonStreamingTokens stays 21_333: Anthropic 12.51.0 has no client-side long-request guard (a 128K non-streaming request was sent), so the cap is driven only by the 10-minute timeout budget"
  - "402/billing is detected by status 402 or 'billing_error' in the body instead of AnthropicServiceException.ErrorType (same semantics, no dependency on the ErrorType enum location)"
metrics:
  duration: 6min
  completed: 2026-09-30
  tasks: 2
  files: 7
---

# Phase 01 Plan 04: Anthropic Claude translator Summary

Claude now works as an `ILlmProvider` on the official Anthropic 12.51.0 SDK:
- Requests use strict `output_config.format` json_schema with a list-form system block, and never send sampling or thinking parameters.
- `max_tokens` is clamped to 21_333 for non-streaming calls.
- Responses are classified from the raw `stop_reason` over text blocks only.
- API errors map by status and body to Auth, SpendLimit, RateLimited, BadRequest, Transient or Network.
- A spend-cap 429 is never retried by the SDK.
- The key test is free (a model retrieve).

## What was built

- **`AnthropicTextProvider`** (`Services/Ai/AnthropicTextProvider.cs`):
  - **Constructors:** the production constructor takes the key, uses the SDK default `MaxRetries = 2` and a 10-minute timeout, and wraps the transport in the spend-limit handler. An internal constructor takes `ClientOptions` so tests can inject `FakeHttpHandler`.
  - **`BuildParams`:**
    - Parses and caches the schema dictionary for each schema string.
    - Maps `Effort` to the SDK enum case-insensitively. `OutputConfig.Effort` is init-only, so a separate initializer keeps `effort` off the wire when it is unset.
    - Sets `MaxTokens = Math.Min(r.MaxTokens, MaxNonStreamingTokens)`.
  - **`Classify`:** concatenates `TryPickText` blocks and classifies the raw stop reason:
    - end_turn or stop_sequence: Succeeded
    - refusal: Refused, with the StopDetails category and explanation
    - max_tokens: Truncated
    - anything else: Failed / Unknown
    - Token usage is clamped to int.
  - **`SendAsync`:** `OperationCanceledException` is rethrown. `AnthropicApiException` goes through `MapException` and `SafeMessage`, which only uses `error.message` from the body. `AnthropicIOException` and `HttpRequestException` become Network. There is no app retry loop and no `Task.Delay`.
  - **`MapException`** (checked in this order):
    1. 401/403: Auth
    2. 429 with `enforced_spend_limit_reached`: SpendLimit
    3. Other 429: RateLimited
    4. 402 or `billing_error`: SpendLimit
    5. 400 with "usage limits" or "credit balance": SpendLimit
    6. Other 400, 404, 422: BadRequest
    7. 5xx: Transient
    8. Anything else: Unknown
  - **`TestKeyAsync`:** calls `Models.Retrieve(modelId, new ModelRetrieveParams(), ct)`, which is a GET to `/v1/models/{id}`. Results are classified as "Key rejected (authentication failed)", "Model 'X' is not available to this key", the spend-limit text, or "Could not reach api.anthropic.com".
- **`AnthropicSpendLimitHandler`:** a `DelegatingHandler`. On a 429 whose buffered body contains `enforced_spend_limit_reached`, it sets `x-should-retry: false` and removes `retry-after` and `retry-after-ms`. All other responses pass through untouched.

## Verification

- `dotnet test` full suite: **80 passed**, 0 failed, run 3 times in a row. The 30 Anthropic tests include 9 wire tests, 16 outcome-classification tests (11 theory rows) and 5 key tests.
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- All acceptance greps for both tasks pass:
  - no sampling or thinking setters
  - no `Content[0]`
  - no bare catch
  - no `Task.Delay`
  - handler class present and wired
- **Spend-cap guard:** with `MaxRetries = 2`, a [429 spend-cap, 200] sequence produced exactly 1 HTTP call and SpendLimit. The header mechanism was enough. A plain 429 with `retry-after: 0` is still retried (2 calls, then Succeeded).
- **Non-streaming probe** (a temporary test, now removed): with the clamp bypassed, `Messages.Create` sent 128_000, 21_334 and 21_333 without any client-side exception. Anthropic 12.51.0 has no long-request guard, so the constant stays at 21_333 because of the timeout budget. It was not lowered.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] JsonSchemaRegistry race under concurrent schema builds**
- **Found during:** Task 2. The new Anthropic test classes run in parallel and build schemas at the same moment.
- **Issue:** `AIJsonUtilities.CreateJsonSchema` lazily assigns `TypeInfoResolver` on the shared `JsonSerializerOptions`. When two types were built concurrently, one call threw "This JsonSerializerOptions instance is read-only". This would also hit production generators that run in parallel.
- **Fix:** The options are now created with `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` and frozen with `MakeReadOnly()`.
- **Files modified:** Services/Ai/JsonSchemaRegistry.cs
- **Commit:** 3e360f4

**2. [Rule 3 - Blocking] FakeHttpHandler needed response headers and request method**
- **Found during:** Task 2
- **Issue:** The plain-429 retry test needs a `retry-after: 0` header, and the NoTokensSpent test asserts GET. The fake supported neither.
- **Fix:** Added `ResponseHeaders`, applied to every response, and `LastRequestMethod`.
- **Files modified:** ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs
- **Commit:** 4d399e5

**3. [Adjustment] 402 detection without `ErrorType.BillingError`**
- `MapException` detects billing by status 402 or `billing_error` in the body instead of `AnthropicServiceException.ErrorType`. The plan allowed status-based mapping with the same semantics, and the tests lock the behaviour.

**4. [Adjustment] Wire tests use the unguarded client**
- `AnthropicWireTests.CreateProvider` uses `handler.CreateClient()`, because `CreateHttpClient` was introduced in Task 2. The Task 2 test classes all go through `CreateHttpClient(handler)`, so the guard is in the loop.

## TDD Gate Compliance

- **Task 1:** RED aa2af4f (test), then GREEN 53ae9c5 (feat).
- **Task 2:** RED 4d399e5 (test), then GREEN a60c543 (feat), after fix 3e360f4.
- No refactor commits were needed.

## Commits

- aa2af4f test(01-04): add failing Anthropic wire-format and classification tests
- 53ae9c5 feat(01-04): implement AnthropicTextProvider request building and classification
- 4d399e5 test(01-04): add failing Anthropic outcome classification and key test tests
- 3e360f4 fix(01-04): make JsonSchemaRegistry thread-safe for concurrent schema builds
- a60c543 feat(01-04): Anthropic exception mapping, spend-cap retry guard and free key test

## Self-Check: PASSED
