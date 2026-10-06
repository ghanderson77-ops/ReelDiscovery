---
phase: 07-scale-cost-qa-cutover
plan: 11
subsystem: generate-corpus-ui
tags: [batching, ui, estimate, dpi, RUN-03, RUN-04, PROV-07, D-08, D-09, D-17]
requires:
  - phase: 07-06
    provides: "CostEstimate.BatchTotal/BatchStoryCost/BatchVolumeCost; EstimateRemaining(batch)"
  - phase: 07-10
    provides: "WizardState.CreateRunGateway, GenerationRunInput.Batch, WaveProgress batch fields, five batch GenerationEventKinds"
  - phase: 07-08
    provides: "BatchingLlmProvider.CancelPendingAsync"
  - phase: 07-07
    provides: "BatchLedger.PendingBatchCount, FakeBatchTransport"
provides:
  - "GenerationState.UseBatch (per-case batch choice, survives Start Over)"
  - "GenerationUiText: EstimateLine, EstimateTooltip, BatchHint, BatchCheckboxText, BatchRecommendThreshold, BatchWaitText/Tooltip, BatchWaitCellText, SecondsUntil, ProgressText(view, nowUtc), ProgressBatchText, CollectingAfterRestartText, BatchCancelFailedText, batch activity texts, banner and C-G1..C-G3 batch clauses, cost tile batch clauses"
  - "StepGenerateCorpus: Ready-card batch checkbox + hint, RunGatewayFactory and BatchTransportFactory seams, PendingProviderBatches, Start Over batch cancel"
  - "GenerationCounts.BatchOn / BatchSavingsUsd"
affects: [07-21, 07-22]
tech-stack:
  added: []
  patterns: ["state-card last-line margin rule extended to checkbox and hint", "ledger-read pending count on entry and run end, snapshot count during a run"]
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/StepGenerateCorpus.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - Models/Evidence/GenerationState.cs
    - Models/Evidence/GenerationProgress.cs
    - Services/Evidence/GenerationRunner.cs
    - Forms/WizardForm.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
decisions:
  - "[07-11]: Status strip priority is Stopping, then Collecting after restart, then the lowest waiting stage row with its countdown, then the normal stage text"
  - "[07-11]: The batch hint uses the Ready card's planned email count (GenerationReadyInput.Emails); no separate PlannedEmails field"
  - "[07-11]: Start Over keeps the case's UseBatch choice (it belongs to the case, not the cleared run); a failed provider cancel is best effort and shows a Warning status clause instead of an Activity row, because Start Over returns to the Ready card where Activity is hidden"
  - "[07-11]: Cost tile batch savings = journal SpentDetail.BatchSavingsUsd on entry plus this session's TokenUsageTracker.TotalBatchSavings delta during and after a run (no extra journal replay)"
  - "[07-11]: The runner's live remaining estimate uses batch prices when input.Batch is set, so the tile's 'at batch prices' clause matches its number"
metrics:
  duration: ~20min
  completed: 2026-10-06
  tasks: 3
  files: 10
---

# Phase 7 Plan 11: Generate Corpus Batch Mode and Live/Batch Estimate Summary

Generate Corpus now shows and uses batch mode. The Ready card has a live/batch estimate line, with the per-model breakdown in a tooltip. Below it sit a saved "Use batch pricing (slower, about half the cost)" checkbox and a Muted or Warning hint. Runs start through `CreateRunGateway(useBatch)`. While a stage waits on a provider batch, its row shows the wait with an Accent dot, the status strip counts down to the next check, and five batch events appear in Activity. The partial-run banner and the C-G1/C-G2/C-G3 confirmations get batch clauses. Start Over cancels pending provider batches before it deletes anything.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | Batch and estimate copy (TDD) | c99e832 (RED), e279ece (GREEN) | GenerationUiText.cs, GenerationProgress.cs, GenerationUiTextTests.cs |
| 2 | Checkbox, saved choice, run wiring, stages/activity/status, Start Over, confirmations | eaad356 | GenerationState.cs, StepGenerateCorpus.cs, StepGenerateCorpus.Run.cs, WizardForm.cs, GenerationRunner.cs, GenerationUiText.cs, GenerateCorpusStepTests.cs |
| 3 | 144-DPI renders of the four batch states | ac8cd77 | GenerateCorpusRenderTests.cs, StepGenerateCorpus.cs (LoadForRender reads the ledger) |

## What Was Built

- **Copy (pure, tested):**
  - `EstimateLine(live, batch)`, plus `EstimateTooltip`, which uses one model name when Story = Volume and drops the media clause when no media is planned.
  - `BatchHint`, with `BatchRecommendThreshold = 2_000`.
  - `BatchWaitText` (single batch and multiple batches; today vs `MMM d` submission time), `BatchWaitTooltip` and `SecondsUntil`.
  - `ProgressText(view, nowUtc)` for wave, non-relevant and collecting.
  - The five batch activity rows with their UI-SPEC tones; durations show as `{m} min` or `{h} h {m} min`.
  - Banner clauses (pending batches take precedence), C-G1..C-G3 batch clauses, and the cost tile clauses ` at batch prices` / ` Batch pricing saved $b.`.
  - The Ready card now uses the new estimate line. The old single-total line is gone.
- **Ready card:**
  - New `_chkUseBatch` (BodyFont, FlatStyle.System, UseMnemonic false) and `_lblBatchHint` (SmallFont, max width 520), placed between the estimate and the media notice.
  - Both appear on the Ready card only.
  - The last-line 20 px margin moves to whichever of estimate / checkbox / hint / notice is last; earlier lines get 4.
  - `CheckedChanged` writes `Draft.Corpus.Generation.UseBatch`, saves the case and redraws the card.
- **Run:**
  - `RunGatewayFactory(useBatch)` seam, then `GatewayFactory`, then `_state.CreateRunGateway(useBatch, folder, plan.PlanId)`; `input.Batch = batch`.
  - Continue Generating, Retry Failed Items and Create Skipped Media all reuse the saved choice, because they share one run path.
  - Remaining estimates pass `batch`.
- **Waiting and status:**
  - A waiting stage row shows the wait text in Dates, the dates plus countdown in its tooltip, and an Accent dot.
  - The status strip refreshes on the existing 250 ms snapshot timer.
  - `PendingProviderBatches` comes from the ledger on entry and after a run, and from the snapshot while a run is going.
- **Start Over:**
  - C-G2 adds its clause when the ledger has pending batches.
  - `BatchingLlmProvider.CancelPendingAsync` runs before `GenerationReset.StartOverAsync`. Transports come from `BatchTransportFactory` or from `LlmProviderFactory.CreateBatchTransport` with the session key; a provider with no key is skipped.
  - `OperationCanceledException` is rethrown. Other failures add a Warning clause and Start Over continues.
- **Shell:** `StepGenerateCorpus s => GenerationUiText.CG3CloseDuringRun(s.PendingProviderBatches > 0)`.

## Verification

- `GenerationUiTextTests`: 261 passed, including the 1,999 / 2,000 boundary, plurals at 0/1/2/1,000 and the banned-word scan.
- New step tests:
  - The checkbox shows on Ready only, is saved, and is read back on re-entry.
  - It is hidden on the Not-approved card.
  - A run uses the saved choice.
  - Start Over cancels the pending ledger batch and keeps UseBatch.
  - When the cancel fails, Start Over still clears and shows a Warning.
- Plan filter (GenerateCorpus non-render, GenerationUiText, WizardShell, BatchParity, GenerationRunner): 368 passed.
- Full non-render suite: 3646 passed.
- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- Render: 21/21 GenerateCorpusRenderTests passed. Each render had DPI >= 144, no clipping, no horizontal grid scrollbar, and the expected Primary count.
- Acceptance greps:
  - UseBatch = 1
  - `_chkUseBatch` = 8
  - `CreateRunGateway(` = 1
  - `CancelPendingAsync(` = 1
  - WizardForm CG3 = 1
  - New `new Size(` without LogicalToDeviceUnits = 0
  - Batch render tests = 4

### 144-DPI inspection (8 PNGs, `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`)

| PNG | Inspected |
|-----|-----------|
| generate-corpus-ready-batch-recommended-1100x750.png | Estimate wraps to 2 lines. Checkbox box is vertically centered on its text and the text is not ellipsized. Muted hint, then Warning notice. One Primary. |
| generate-corpus-ready-batch-recommended-1000x540.png | Same, all lines fit inside the card. No scrollbar. |
| generate-corpus-ready-batch-checked-1100x750.png | Checked box aligned with its text, Warning hint, no media notice. One Primary. |
| generate-corpus-ready-batch-checked-1000x540.png | Same, fits. |
| generate-corpus-running-batch-wait-1100x750.png | Wave 3 row has an Accent dot and "Batch: 6 of 14 done", not cut off (after the rule-27 fix). Activity shows Batch submitted (Accent), Batch failed at Anthropic and finished-with-failures (Warning), and Batch finished (Success). The status strip shows the full wait text with "Next check in 25 s." Cancel Generation is Danger. |
| generate-corpus-running-batch-wait-1000x540.png | Same. The status strip wraps to 2 lines without clipping. |
| generate-corpus-partial-pending-batches-1100x750.png | Banner ends "1 provider batch submitted earlier will be collected first, without paying again." Continue Generating is the only Primary. |
| generate-corpus-partial-pending-batches-1000x540.png | Same. The banner body wraps to 2 lines, fits. |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Runner's live remaining estimate in batch mode**
- **Found during:** Task 2
- **Issue:** During a run, `GenerationRunner.EstimateRemaining` priced the remaining work live. The tile would have said "at batch prices" next to a live number.
- **Fix:** It now passes `batch: ctx.Input.Batch != null`. The file was not in the plan's list.
- **Commit:** eaad356

**2. [Rule 2 - Missing wiring] Cost tile batch fields**
- **Issue:** Task 1's cost tile clauses had no input on `GenerationCounts`.
- **Fix:** Added `GenerationCounts.BatchOn` and `BatchSavingsUsd` (`Models/Evidence/GenerationProgress.cs`, not in the plan's list). They are filled from the case choice, the journal's `SpentDetail.BatchSavingsUsd` and this session's tracker delta.
- **Commits:** e279ece, eaad356

**3. [Rule 3 - Testability] Seams and accessors**
- `BatchTransportFactory` seam for Start Over's cancel.
- `UseBatchCheckBox`, `BatchHintLabel` and `EstimateTooltipText` accessors.
- `LoadForRender` reads the ledger's pending count so render 4 can come from a real ledger record.
- **Commits:** eaad356, ac8cd77

**4. [Plan detail] Cancel-failure surface**
- **Issue:** The plan asked for a Warning row in Activity when Start Over cannot cancel. Start Over ends on the Ready card, where Activity is hidden.
- **Fix:** The failure shows as a Warning status clause (`BatchCancelFailedText`) after "Cleared the earlier run...".

**5. [Plan detail] Render 3 uses a seeded snapshot**
- **Issue:** The plan described render 3 as a real run through `RunGatewayFactory` + `FakeBatchTransport`.
- **Fix:** Render 3 uses a seeded `GenerationView`, like every other Generate Corpus render. The batch rows and events are built exactly as the snapshot produces them, which keeps the screenshot deterministic. The live batch path is covered by `BatchParityTests` and the new `Run_UsesSavedBatchChoice` step test.

**6. [Plan detail] Existing tests updated to the new copy**
- Existing Ready-card copy tests now expect the D-17 two-total line.
- `ReadyCard_MixedCase_TieredEstimateAndMediaNotice` now checks model names in the tooltip.
- `ReadyWithMediaEstimate` now expects the estimate margin 4 and the hint as the last line.

## DPI rule 27 resolution (follow-up fix)

- **Before:** with the full S1 sentence, the batch-wait text in the `Dates` cell was cut off at `Large` / 144 DPI, which breaks rule 27.
- **Fix:** the cell now shows a compact form, `Batch: {d} of {t} done` (or `{b} batches: ...`), from the new `GenerationUiText.BatchWaitCellText`. The full sentence with the submission time stays in the row tooltip and the status strip.
- **Why there is no time in the cell:** the coordinator's suggested `Batch: 6 of 14 done · sent 4:09 PM` measured 244 px against the 208 px `Dates` column at `Large`.
- **New checks:**
  - A copy test covers the single, multiple, thousands and empty variants.
  - The render test asserts that the shown cell and the widest single-batch case (`Batch: 9,999 of 10,000 done`) fit at `Large`.
- **Re-inspected:** generate-corpus-running-batch-wait-1100x750.png. The wave 3 row reads "Batch: 6 of 14 done" in full.
- **Spec:** 07-UI-SPEC S1 Row 4 and rule 27 are marked [amended in execution 07-11].

## Requirements

RUN-03, RUN-04 and PROV-07 are not marked complete. 07-12, 07-18, 07-21 and 07-22 still carry parts of them (S5 pricing columns, the S2/S6 breakdowns, the phase UAT).

## Known Stubs

None.

## TDD Gate Compliance

Task 1 has RED `test(07-11)` c99e832 followed by GREEN `feat(07-11)` e279ece.

## Self-Check: PASSED
