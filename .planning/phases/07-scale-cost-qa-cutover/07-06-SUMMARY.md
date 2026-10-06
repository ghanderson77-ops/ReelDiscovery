---
phase: 07-scale-cost-qa-cutover
plan: 06
subsystem: ai-cost
tags: [cost, estimate, prompt-cache, batch, PROV-07, RUN-05, RUN-03, D-17]
requires:
  - "07-01 AIModelConfig.CalculateCost(TokenUsage), MinCacheablePrefixTokens, BatchDiscountPercent"
provides:
  - "GenerationCostEstimator.StablePrefixTokens = 600 (assumed; re-measure in 07-22)"
  - "CostEstimate.BatchStoryCost, BatchVolumeCost, BatchTotal from the same PriceWork call as Total"
  - "Live text prices include the prompt-cache read term when StablePrefixTokens >= model.MinCacheablePrefixTokens (> 0)"
  - "GenerationRunState.EstimateRemaining(..., bool batch = false) selects BatchTotal; the 3-arg overload prices through PriceWork"
affects: [07-ready-card, 07-batch, 07-ticker]
tech-stack:
  added: []
  patterns:
    - "One pricing path: every estimate line goes through private Price(model, in, out, batch) -> AIModelConfig.CalculateCost"
    - "Honest caching: cache reads only when the prefix meets the model minimum; cache writes amortized away; batch assumes no cache reads"
key-files:
  created: []
  modified:
    - Services/Corpus/GenerationCostEstimator.cs
    - Services/Evidence/GenerationRunState.cs
    - ReelDiscovery.Tests/Corpus/GenerationCostEstimatorTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunStateTests.cs
decisions:
  - "07-06: MinCacheablePrefixTokens == 0 means no caching, so legacy/custom models without cache data price exactly as before"
  - "07-06: With the 600-token prefix only Opus/Sonnet (min 512) get a live cache term; Haiku (4096) and GPT-6 models (1024) price as before"
  - "07-06: The Phase 5 Estimate(plan, storyModel) overload also prices through Price(..., batch: false) so no CalculateTotalCost remains in the estimator"
metrics:
  duration: "~6 min"
  completed: 2026-10-06
  tasks: 1
  files: 4
---

# Phase 7 Plan 06: Live and Batch Totals from One PriceWork Summary

`PriceWork` now returns both a live total and a batch total from one call. The live total prices the 600-token stable prompt prefix as cache reads, but only on models whose minimum cacheable prefix is 600 tokens or less. The batch total uses batch prices with no cache reads, and both totals include the same live media cost.

## What Was Built

- **`GenerationCostEstimator`**
  - Adds `StablePrefixTokens = 600`, marked `[ASSUMED]`.
  - Adds a private helper, `Price(model, in, out, batch)`, which builds a `TokenUsage` and calls `AIModelConfig.CalculateCost`.
  - Live calls put the prefix into `CacheReadTokens` only when `MinCacheablePrefixTokens > 0`, the prefix is at least the model minimum, and the call's input is larger than the prefix.
  - Batch calls set `IsBatch` and get no cache reads.
- **`PriceWork`**
  - Fills the existing live fields as before.
  - Adds `BatchStoryCost` and `BatchVolumeCost`. `BatchVolumeCost` covers volume messages, small attachments, the side world and the voice cast.
  - Adds `BatchTotal = BatchStoryCost + BatchVolumeCost + MediaCost`.
- **`GenerationRunState.EstimateRemaining`**
  - The 3-argument overload builds a `PricedWork` from the relevant messages still to write and returns `PriceWork(...).Total`.
  - The full overload gains a trailing `bool batch = false`. When it is true, the method returns `BatchTotal`. Existing callers compile unchanged.

## Commits

| Gate | Hash | Message |
|------|------|---------|
| RED | 340c561 | test(07-06): add failing live/batch cache-aware estimate tests |
| GREEN | 2fb1649 | feat(07-06): live and batch totals from one PriceWork with the cache term |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- Filtered tests (estimator, run state, generate corpus without render tests): 93/93 passed.
- Full test suite without render tests: 3427/3427 passed.
- Acceptance greps:
  - `public decimal BatchTotal`: 1
  - `CalculateTotalCost` in both source files: 0
  - `StablePrefixTokens = 600`: 1

## Deviations from Plan

**1. [Rule 3 - Blocking] The Phase 5 `Estimate(plan, storyModel)` overload now prices through `Price`.** The acceptance check requires zero `CalculateTotalCost` calls in `GenerationCostEstimator.cs`, and that overload still had some. Every catalog model with a minimum of 512 or more, and every legacy model with a minimum of 0, prices the same as before, so the existing tests are unchanged. Commit 2fb1649.

**2. Extra test added in `GenerationRunStateTests.cs`.** It covers the batch flag on `EstimateRemaining`, which is a planned behavior. The plan's file list did not include this file.

## TDD Gate Compliance

The RED commit (340c561, which failed to compile because the new members did not exist yet) comes before the GREEN commit (2fb1649). No refactor was needed.

## Known Stubs

None. `StablePrefixTokens` is a documented `[ASSUMED]` value; plan 07-22 is due to re-measure it from live usage.

## Self-Check: PASSED
