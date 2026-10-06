---
phase: 07-scale-cost-qa-cutover
plan: 01
subsystem: ai-cost
tags: [cost, tokens, prompt-cache, batch, gateway, PROV-07]
requires: []
provides:
  - "TokenUsage and CostBreakdown (Services/Ai/TokenUsage.cs)"
  - "AIModelConfig.CalculateCost(TokenUsage) single cost function with cache/batch prices"
  - "LlmResult cache tokens + Cost/CostUsd; LlmRequest/LlmProviderRequest AllowBatch"
  - "TokenUsageTracker per-category totals for the D-16 tooltip"
affects: [07-runtime-cost, 07-estimates, 07-ticker, 07-case-summary, 07-batch, 07-prompt-cache]
tech-stack:
  added: []
  patterns:
    - "Every billed attempt priced once through AIModelConfig.CalculateCost (pre-discount components + BatchSavingsUsd)"
    - "AllowBatch only on attempt 1; retries are live (D-10)"
key-files:
  created:
    - Services/Ai/TokenUsage.cs
    - ReelDiscovery.Tests/Ai/TokenUsageTests.cs
  modified:
    - Models/AIModelConfig.cs
    - Models/TokenUsageTracker.cs
    - Services/Ai/LlmProviderRequest.cs
    - Services/Ai/LlmRequest.cs
    - Services/Ai/LlmResult.cs
    - Services/Ai/LlmGateway.cs
    - UserControls/ModelConfigurationDialog.cs
    - ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs
    - ReelDiscovery.Tests/Ai/LlmGatewayTests.cs
    - ReelDiscovery.Tests/Ai/ModelCatalogTests.cs
decisions:
  - "07-01: LlmResult.InputTokens and tracker TotalInputTokens count all billed input (uncached + cache write + cache read); cache parts are also reported separately"
  - "07-01: Legacy RecordUsage(op, model, in, out) delegates to the TokenUsage overload via CalculateCost (same dollars for non-negative counts)"
  - "07-01: ModelConfigurationDialog working copy carries cache/batch prices and MinCacheablePrefixTokens so OK never zeroes them (grid columns still come later)"
metrics:
  duration: 7min
  completed: 2026-10-06
  tasks: 2
  files: 12
---

# Phase 7 Plan 01: Normalized Usage and Single Cost Function Summary

TokenUsage/CostBreakdown types plus one `AIModelConfig.CalculateCost` that prices uncached input, cache writes, cache reads and output with a clamped batch discount; the gateway bills every attempt through it, returns usage and cost on `LlmResult`, and passes `AllowBatch` only on attempt 1.

## What Was Built

- `Services/Ai/TokenUsage.cs`: mutable `TokenUsage` (Input/CacheWrite/CacheRead/Output, IsBatch, TotalInputTokens, Add/Clone/Normalize) and `CostBreakdown` (four pre-discount components, BatchSavingsUsd, TotalUsd, Add/Clone).
- `Models/AIModelConfig.cs`: `CacheWriteTokenPricePerMillion`, `CacheReadTokenPricePerMillion`, `BatchDiscountPercent`, hidden `MinCacheablePrefixTokens`, and `CalculateCost(TokenUsage)`. All six catalog models carry the verified 2026-10-06 values (no model was outside the table, so no `[ASSUMED]` entries). `CalculateTotalCost` and friends are unchanged.
- `LlmProviderRequest.AllowBatch`; `LlmProviderResponse` gains CacheWriteTokens, CacheReadTokens, IsBatch and `ToUsage()`.
- `LlmRequest.AllowBatch` (default false).
- `LlmResult`: CacheWriteTokens, CacheReadTokens, `Cost`, `CostUsd`; InputTokens = summed TotalInputTokens.
- `LlmGateway`: per-attempt `usage.Normalize()` -> `CalculateCost` -> tracker `RecordUsage(op, model, usage, attemptCost)`; summed usage and cost go to every result path. `providerRequest.AllowBatch = request.AllowBatch && attempts == 1`.
- `TokenUsageTracker`: new overload plus TotalCacheWriteTokens/TotalCacheReadTokens, TotalInputCost/TotalCacheWriteCost/TotalCacheReadCost/TotalOutputCost, TotalBatchSavings, TotalMediaCost (all reset by `Reset()`).
- `FakeLlmProvider` snapshots AllowBatch.

## Tests

- `TokenUsageTests` (new, 13): Opus 1M x4 = 29.20, batch = 14.60 savings/total, Luna input, discount clamp (150 and negative), negative-token clamp (caller object untouched), null usage, Add/Clone for both types, CalculateTotalCost regression.
- `ModelCatalogTests`: verified table theory over the six models; every entry has cache/batch defaults.
- `LlmGatewayTests`: cache tokens flow to result/cost/tracker, two-attempt sums with two tracker entries, AllowBatch true only on attempt 1, false never sent, batch response priced with discount, tracker breakdown + media, legacy RecordUsage.
- Verification: `dotnet build ReelDiscovery.csproj -warnaserror` 0 warnings; Ai tests 132/132; full suite `Category!=Render&Category!=Scale` 3357/3357.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 7e621d7 | test(07-01): add failing tests for TokenUsage, CostBreakdown and catalog cache prices |
| 1 GREEN | ffaab53 | feat(07-01): add TokenUsage, CostBreakdown and catalog cache/batch prices |
| 2 RED | f9a95e4 | test(07-01): add failing gateway billing, AllowBatch and tracker breakdown tests |
| 2 GREEN | 350dcde | feat(07-01): bill every gateway attempt through CalculateCost with cache/batch breakdown |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Model dialog copy dropped the new prices**
- **Found during:** Task 1
- **Issue:** `ModelConfigurationDialog` clones each `AIModelConfig` field by field; without the new fields, clicking OK would set cache prices, batch discount and min prefix to 0, so cached tokens would be priced at $0 (Pitfall 2).
- **Fix:** Copy the four new properties in the working copy. Grid columns for them are still planned for a later 07 plan.
- **Files modified:** UserControls/ModelConfigurationDialog.cs
- **Commit:** ffaab53

**2. [Test fixture] LlmGatewayTests StoryModel helper** now sets cache prices and a 50% batch discount so the batch test can assert savings > 0. Existing tests do not assert dollar values, so they are unaffected.

## TDD Gate Compliance

RED (`test(...)`) and GREEN (`feat(...)`) commits exist for both tasks, in order. No refactor commits were needed.

## Known Stubs

None.

## Self-Check: PASSED
