---
phase: 01-foundation-provider-gateway
plan: 03
subsystem: ai-gateway
tags: [llm, gateway, structured-outputs, json-schema, model-catalog, retry-policy]
requires:
  - 01-01 (net10 retarget, test project, MEAI/Anthropic/OpenAI packages)
provides:
  - ReelDiscovery.Services.Ai contracts (LlmTier, LlmProviderKind, LlmRequest, LlmResult/LlmResult<T>, LlmOutcome, LlmFailureKind, LlmCallException, LlmKeyTestResult, ILlmGateway, ILlmProvider, LlmProviderRequest/Response)
  - JsonSchemaRegistry (cached strict schema per Type)
  - LlmGateway (tier resolution, retry policy, usage recording)
  - Tiered model catalog (AIModelConfig.Provider/DefaultForTier/Effort/MaxOutputTokens, GetTierDefault)
  - Test helpers FakeLlmProvider and StrictSchemaValidator
affects:
  - 01-04 / 01-05 provider translators implement ILlmProvider
  - 01-07 keys screen (IsDefault still present, gpt-6-luna preselected)
  - generator ports (constructor takes ILlmGateway)
tech-stack:
  added: []
  patterns:
    - "Strict schemas from Microsoft.Extensions.AI AIJsonUtilities.CreateJsonSchema with DisallowAdditionalProperties/RequireAllProperties"
    - "Outcome taxonomy drives retries: only Invalid (<=3 attempts) and Truncated (1 retry at 2x, capped) are retried by the app"
key-files:
  created:
    - Services/Ai/LlmProviderKind.cs
    - Services/Ai/LlmRequest.cs
    - Services/Ai/LlmResult.cs
    - Services/Ai/ILlmGateway.cs
    - Services/Ai/ILlmProvider.cs
    - Services/Ai/LlmProviderRequest.cs
    - Services/Ai/JsonSchemaRegistry.cs
    - Services/Ai/LlmGateway.cs
    - ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs
    - ReelDiscovery.Tests/Fakes/StrictSchemaValidator.cs
    - ReelDiscovery.Tests/Ai/ModelCatalogTests.cs
    - ReelDiscovery.Tests/Ai/JsonSchemaRegistryTests.cs
    - ReelDiscovery.Tests/Ai/LlmGatewayTests.cs
  modified:
    - Models/AIModelConfig.cs
    - Models/TokenUsageTracker.cs
decisions:
  - "OpenAI Story default is gpt-6.1-sol with Effort low (D-15); gpt-6-sol kept as non-default alternative"
  - "LlmGateway treats a provider-reported Outcome=Invalid the same as an unparseable Succeeded response (counts toward the 3-attempt cap)"
  - "MEAI inlines repeated nested DTO types (no $ref/$defs), so emitted schemas need no ref support from providers"
metrics:
  duration: 14min
  completed: 2026-09-30
  tasks: 3
  files: 15
---

# Phase 01 Plan 03: Provider-agnostic LLM gateway core Summary

Strict-schema `LlmGateway` over an `ILlmProvider` seam: tier to model resolution for one provider per run, MEAI-generated cached strict JSON schemas, classified outcomes (Succeeded/Refused/Truncated/Invalid/Failed) with a bounded app-level retry policy, per-attempt token usage, and the new Claude Opus 5.5 / Haiku 4.5 + GPT-6.1 Sol / GPT-6 Luna catalog.

## What was built

- **Contracts** (`Services/Ai/`): the shapes from the plan's interfaces block, all classes (no records). `LlmTier` shares `LlmRequest.cs`; the result taxonomy, `LlmCallException` (`"{Op}: {Outcome}[ ({Kind})] - {Message}"`) and `LlmKeyTestResult` live in `LlmResult.cs`.
- **Catalog**: six models (claude-opus-5-5, claude-sonnet-5-5, claude-haiku-4-5-20251001, gpt-6-sol, gpt-6.1-sol, gpt-6-luna) with prices, tier defaults, effort and output ceilings. The legacy gpt-4/3.5/o1 entries are removed. `IsDefault` is set only on gpt-6-luna so the old keys screen still works during the transition.
- **TokenUsageEntry.Provider** is recorded from `model.Provider`.
- **JsonSchemaRegistry**: `GetSchemaJson(Type)`/`<T>()` returns one cached string instance per type (`$schema` stripped). `GetSchemaName` is sanitized with a `[GeneratedRegex]`. For the sample DTO the schema is fully strict and inlines nested types.
- **LlmGateway**:
  - The constructor rejects mixed providers (D-11).
  - `cap = min(model.MaxOutputTokens, provider.MaxTokensCeiling)`, and the first request is clamped to that cap.
  - Invalid or empty responses get at most 3 attempts.
  - Truncated responses get one retry at 2x tokens, capped. There is no retry when the request is already at the cap.
  - Refused and Failed responses (including RateLimited and Transient) return immediately.
  - Usage is recorded on every attempt that has token counts.
  - There is no Polly or delay, and `OperationCanceledException` propagates.
- **Test helpers**:
  - `FakeLlmProvider` has scripted responses, request snapshots, a settable `MaxTokensCeiling` and an `OnSend` hook.
  - `StrictSchemaValidator` does a schema-aware walk that checks additionalProperties/required, forbidden keywords, minItems > 1, empty `{}` and the root type. It also has `CountUnionTypes`.

## Verification

- `dotnet test` full suite: 50 passed, 0 failed. This includes ModelCatalogTests (11 cases), JsonSchemaRegistryTests (4) and LlmGatewayTests (15 methods, with theories expanding them).
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- Acceptance greps: no `record` in Services/Ai, no gpt-4o/gpt-3.5/o1-preview, no Task.Delay/Polly, and no OperationCanceledException catch in the gateway.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] FakeLlmProvider snapshots each request**
- **Found during:** Task 3
- **Issue:** The gateway reuses and mutates one `LlmProviderRequest` (it doubles MaxTokens on a truncation retry). Storing the reference would make `Requests[0].MaxTokens` show the doubled value, so the truncation tests could not observe the first call.
- **Fix:** The fake records a copy of each request.
- **Commit:** 979af82

**2. [Rule 2 - Correctness] Provider-reported Invalid handled**
- **Found during:** Task 3
- **Issue:** The plan's retry table covers Succeeded/Truncated/Refused/Failed, but `LlmOutcome.Invalid` is also a legal provider outcome.
- **Fix:** It is treated like an unparseable Succeeded response and counts toward the 3-attempt cap, so it is never returned unbounded or treated as final without retry.
- **Commit:** ada1b6f

## TDD Gate Compliance

- Task 2: RED 435fd44 (test), then GREEN 984a3a0 (feat).
- Task 3: RED 979af82 (test), then GREEN ada1b6f (feat).
- No refactor commits were needed.

## Notes for downstream plans

- `Models/WizardState.cs` (`SelectedModel = "gpt-4o-mini"`, the legacy model list) and `UserControls/StepApiConfiguration.cs` fallbacks still reference gpt-4o-mini. Plan 01-07 rebuilds these. They are out of scope here and left untouched.
- `ModelConfigurationDialog` clones models without the new Provider/DefaultForTier/Effort/MaxOutputTokens fields. That is fine until plan 01-07 replaces the dialog.

## Commits

- e812205 feat(01-03): gateway contracts and new tiered model catalog
- 435fd44 test(01-03): add failing tests for JsonSchemaRegistry and StrictSchemaValidator
- 984a3a0 feat(01-03): implement JsonSchemaRegistry with cached strict MEAI schemas
- 979af82 test(01-03): add failing LlmGateway tests and FakeLlmProvider
- ada1b6f feat(01-03): implement LlmGateway with tier resolution and outcome-driven retry

## Self-Check: PASSED
