---
phase: 07-scale-cost-qa-cutover
plan: 24
subsystem: cost-estimate-and-copy
tags: [batch, RUN-03, D-07-amended, D-08, D-17, gap-closure, ui-copy]
requires:
  - phase: 07-23
    provides: "Relevant waves live in batch mode; only non-relevant mail batched"
  - phase: 07-11
    provides: "Ready-card batch checkbox, hint and live/batch estimate"
provides:
  - "PricedWork.LiveDocIds: attachment doc ids that run live in batch mode (repairs), priced live in the batch total"
  - "PriceWork batch total: relevant threads and their attachments, repairs, side world and voice cast live; non-relevant mail and its attachments at batch prices"
  - "GenerationRunState.EstimateRemaining(batch: true) follows the same rule (LiveDocIds = repairs)"
  - "Amended Ready-card batch checkbox, hints and estimate tooltip copy; 07-UI-SPEC S1 aligned"
  - "API Configuration step media notice: 'Enter and test one above to enable them.'"
affects: [07-22]
tech-stack:
  added: []
  patterns:
    - "Per-bucket live/batch split: count x live price + batchCount x batch price, each x QaOverhead"
key-files:
  created: []
  modified:
    - Services/Corpus/GenerationCostEstimator.cs
    - Services/Evidence/GenerationRunState.cs
    - UserControls/GenerationUiText.cs
    - Models/WizardState.cs
    - .planning/phases/07-scale-cost-qa-cutover/07-UI-SPEC.md
    - ReelDiscovery.Tests/Corpus/GenerationCostEstimatorTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunStateTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
    - ReelDiscovery.Tests/MediaGatingTests.cs
decisions:
  - "Batch total = relevant work (live, with cache term) + non-relevant work (batch, no cache reads) + live side world, voice cast, repairs and media (amended D-07, D-17); live fields unchanged"
  - "Batch checkbox reads 'Use batch pricing for non-relevant mail (slower, lower cost)': 'about half the cost' is no longer true once relevant threads are priced live (D-08 discretion)"
  - "The wave forms of the batch wait/progress/activity copy stay for wave batches submitted by an earlier build; UI-SPEC marks them as such"
  - "API-step media notice no longer points at the API Configuration step; later steps keep their own pointer text"
metrics:
  duration: ~35 min
  completed: 2026-10-08
---

# Phase 7 Plan 24: Batch estimate and copy for live relevant waves Summary

The Ready-card batch total now prices relevant threads, repairs, side world and voice cast live and only non-relevant mail at batch prices. The batch checkbox, hint and tooltip say relevant threads run live, and the API step's media line no longer sends the user to the step they are on.

## What was built

- **Estimator (Task 1):** `PriceWork` keeps every live field byte-for-byte. Each text bucket (story messages, story documents, story small, volume messages, volume small) now counts a batch-eligible subset. A message is batch-eligible when its thread is not relevant. An attachment is batch-eligible when its thread is not relevant and its doc id is not in `PricedWork.LiveDocIds`. `BatchStoryCost` and `BatchVolumeCost` add the live and batch parts. The side-world and voice-cast calls are added at their live cost because those requests never allow batching. `EstimateRemaining` passes its repair set as `LiveDocIds`, so after the relevant waves finish, the batch remaining line prices only non-relevant work at batch prices.
- **Copy (Task 2):** checkbox `Use batch pricing for non-relevant mail (slower, lower cost)`. Recommend hint: `... Relevant threads still run live; non-relevant mail then waits on the provider, which can take several hours.` Checked hint: `Relevant threads run live first. Non-relevant mail then waits on the provider in large batches, ...`. The tooltip's fixed sentence now says relevant threads and their attachments, images and voicemail are always priced live, and the batch total assumes no cache savings on batched mail. 07-UI-SPEC S1 has the checkbox row, hint rows, tooltip, D-17 pricing note, the Batch submitted `{stage}` row, the status-strip wave form and the Copywriting row amended and tagged.
- **Media notice (Task 3):** `WizardState.MediaUnavailableNotice` = `Images and voicemail need a tested OpenAI key. Enter and test one above to enable them.` Later steps keep their pointer to the API Configuration step.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and the test project build with `-warnaserror`: 0 warnings, 0 errors (artifacts under `obj/gsd-artifacts`, because the running app locks `bin\Debug`).
- Non-render, non-scale suite: 3737 passed, 0 failed.
- GenerateCorpusRenderTests and WizardShellRenderTests: 31 passed. I checked the PNGs at 144 DPI: `generate-corpus-ready-batch-checked-1000x540`, `generate-corpus-ready-batch-recommended-1000x540` and `step-api-configuration-1000x540`. The longer checkbox text fits at WizardMin, the hints wrap within 520 px, and the new media line fits row 3. Nothing clips.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Render assertion collided with the new tooltip sentence**
- **Found during:** Task 2 (GenerateCorpusRenderTests.ReadyBatchChecked)
- **Issue:** The test asserted that the no-media tooltip does not contain `images and voicemail`, which was meant to catch the media clause. The new fixed sentence (`Relevant threads and their attachments, images and voicemail are always priced live`) contains that phrase in lower case.
- **Fix:** The assertion now targets the media clause itself (`· images and voicemail ≈`).
- **Files modified:** ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
- **Commit:** 7a7b47b

**2. [Rule 1 - Bug] EstimateTooltip_NoMedia_DropsMediaClause prefix**
- **Found during:** Task 2 RED
- **Issue:** Its StartsWith prefix ended in `Images and voicemail`, the old start of the fixed sentence.
- **Fix:** Changed the prefix to the new sentence start in the RED commit.
- **Commit:** 2b0adae

## TDD Gate Compliance

| Task | RED | GREEN |
|------|-----|-------|
| 1 | d404911 test(07-24) (compile-fail RED: `LiveDocIds` missing) | 45b3416 feat(07-24) |
| 2 | 2b0adae test(07-24) batch copy literals (10 failing) | 7a7b47b feat(07-24) |
| 3 | 70a73e6 test(07-24) media notice (2 failing) | ddcc25d fix(07-24) |

Note: `EstimateRemaining_BatchAfterRelevantDone_PricesOnlyNonRelevantAtBatch` compares against `PriceWork` itself. It works as a regression guard, but the RED gate for this behavior comes from the estimator tests and `EstimateRemaining_BatchRepairs_PricedLive`.

## Known Stubs

None.

## Self-Check: PASSED
