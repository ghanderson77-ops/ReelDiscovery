---
phase: 06-attachments-non-relevant-corpus
plan: 23
subsystem: generate-corpus-run-wiring
tags: [winforms, generate-corpus, run-lifecycle, media, banners, ready-card, progress, stages, d-25, d-26, d-29, run-02, att-03, out-01, prov-06]
requires:
  - "06-04 WizardState.CreateMediaGenerator / MediaPrices / IsMediaAvailable / VolumeModelConfig"
  - "06-13 GenerationEntryInput.AttachmentsTotal/AttachmentsFinal/LatestAttemptEnded, MediaBanner, ReadyCard(GenerationReadyInput), CG1(threads, attachments)"
  - "06-14 GenerationRunState whole-corpus counts, RepairGroups, AttachmentSeeds, EstimateRemaining(story, volume, prices, mediaAvailable, guide)"
  - "06-15 GenerationCostEstimator.Estimate(plan, story, volume, prices, mediaAvailable, guide)"
  - "06-19/06-20 GenerationRunInput.Media/MediaPrices, stage rows waves+1 (NonRelevant) and waves+2 (Attachments)"
  - "06-22 ShowDashboard(bool runEnded, ...), ShowPartialBanner, ShowMediaBanner, Ready card estimate/notice labels"
provides:
  - "Run input with Media = _state.CreateMediaGenerator() and a MediaPrices clone; internal RunInputCreated test seam"
  - "One banner rule for entry, after-run and after-error dashboards (partial banner > media banner > Retry Failed Items), over threads and attachments"
  - "StepGenerateCorpus.MostFrequentSkippedReason(state), CancelConfirmation(counts), ProgressUnits(counts)"
  - "ClickBannerButtonAsync / ClickRetryFailedAsync (same StartRunAsync)"
  - "Ready card with the D-29 Story / Volume / media estimate and media notice"
  - "GenerationHistory.Waves(plan, state, mediaAvailable = false) with Non-relevant mail and Attachments for written threads rows"
  - "GenerateCorpusRenderTests.StepFixture(approved, mixed) loads AttachmentTestData.MixedCase"
affects: [06-25 human render inspection]
tech-stack:
  added: []
  patterns:
    - "Reopened Stages rows use the runner's row indexes and AttachmentSeeds, with the mediaAvailable the run used (or the next run will use)"
    - "Banner and Retry button visibility decided logically (returned flag), never read back from Control.Visible"
key-files:
  created: []
  modified:
    - UserControls/StepGenerateCorpus.Run.cs
    - UserControls/StepGenerateCorpus.cs
    - Services/Evidence/GenerationHistory.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationHistoryTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
decisions:
  - "Entry heading keeps the 06-13 rule: LatestAttemptEnded = state.LatestAttemptEnded && LastRunEnded.Outcome == Finished; after a run, runEnded = result.Outcome == Finished"
  - "Banner and remaining-cost estimate after a run use the session's media availability and prices (what the next run does); the Stages rows use the run's own input.Media != null"
  - "ShowDashboardFromDraft (after an unexpected exception) computes whole-corpus counts from the cached GenerationState (relevant + non-relevant + attachments); its skipped reason is empty because the cache has no reasons"
  - "HistoryView (entry) is the second GenerationHistory.Waves caller and gets the session's mediaAvailable; the plan named ShowDashboardFromDraft, which has no run state to pass"
  - "RunInputCreated test seam runs after the input is built, so the step tests check the real CreateMediaGenerator result and then swap in FakeMediaGenerator (no OpenAI call from a test)"
metrics:
  duration: 22min
  completed: 2026-10-05
  tasks: 2
  files: 6
---

# Phase 6 Plan 23: Generate Corpus Run Wiring for the Mixed Corpus Summary

The Generate Corpus step now runs the whole mixed corpus with media. Every Generate, Continue, Retry and Create Skipped Media button starts the same run, which gets the media generator only when an OpenAI key was tested this session and keeps its own copy of the media prices. Entry, after-run and after-error dashboards follow one banner rule over threads and attachments. The Ready card shows the Story / Volume / media estimate. The progress bar counts every work unit. Reopening a case rebuilds the Non-relevant mail and Attachments for written threads rows from the journal, using the same rules as the live run.

## What Was Built

**Task 1: run start, entry decisions, banners, Ready card and progress (37ebbaa)**
- **Run input:** `Media = _state.CreateMediaGenerator()` and `MediaPrices = _state.MediaPrices.Clone()`. `_runMediaAvailable = input.Media != null` is set once the input is built. The internal `RunInputCreated` hook sees each input before the runner starts.
- **Entry:** `GenerationEntryInput.AttachmentsTotal` and `AttachmentsFinal` (Written + Warnings + Failed + Skipped) come from the replayed state. `LatestAttemptEnded` uses `state.LatestAttemptEnded` with the Finished filter; there is no `LastRunEnded != null` check.
- **One banner rule (`ShowResultDashboard`)**, used on entry, after a run and after an unexpected exception:
  - Items left: the partial-run banner via `ShowPartialBanner`, with item-based failed counts and the attachment progress.
  - Otherwise, skipped media: `ShowMediaBanner(GenerationUiText.MediaBanner(skipped, failedItems, mediaAvailable, MostFrequentSkippedReason(state)))`.
  - Otherwise, failed items: header Retry Failed Items.
  - It returns whether Retry Failed Items shows, so the outcome line names that button only when it is visible.
- **06-14 stopgap removed:** `ShowRunResult` decides partial/finished from whole-corpus counts (`IsPartial`: pending threads or attachments without a final record). It also uses the full `EstimateRemaining(plan, state, story, volume, prices, mediaAvailable, guide)`. `HistoryView` uses the same overload.
- **Buttons:** `_btnContinue` (Continue Generating, Create Skipped Media, Retry Failed and Skipped Items) goes through `ClickBannerButtonAsync`, and `_btnRetryFailed` through `ClickRetryFailedAsync`. Both call `StartRunAsync`, which is guarded by `_isGenerating`.
- **Ready card:** `GenerationCostEstimator.Estimate(plan, Story, Volume, MediaPrices, IsMediaAvailable, Draft.Guide)` feeds `GenerationReadyInput`. It covers relevant and non-relevant threads, all emails, waves, attachments, Story / Volume / media cost, model names, the media item count and media availability.
- **Progress bar:** `ProgressUnits(counts)` sets the maximum to threads + attachments. Done, Failed and Skipped items count as finished, and the value is clamped to the maximum.
- **Cancel:** `CancelConfirmation(_lastCounts)` passes the attachments written to CG1.

**Task 2: Stages rows on reopen (1adde3d)**
- `GenerationHistory.Waves(plan, state, mediaAvailable = false)` builds the wave rows first. When the plan has non-relevant threads it adds the Non-relevant mail row (`waves.Count + 1`) with the runner's thread, email, date and status counts. When `state.RepairGroups(plan, mediaAvailable)` has journaled groups it adds the Attachments for written threads row (`waves.Count + 2`). Attachment counters are tallied from `state.AttachmentSeeds(...)` with the same mediaAvailable.
- Callers: `ShowRunResult` passes `_runMediaAvailable`; the entry `HistoryView` passes `_state.IsMediaAvailable`.

## Tests

- **GenerateCorpusStepTests (+15):**
  - Run input: Media and a price clone with a tested key; no Media without one; one run per click and no confirmation for each button.
  - Entry headings: "Part of the corpus is not written yet." when the last run finished, plus the rebuilt Stages rows; "The last run did not finish." with no RunEnded, and also for RunStarted, RunEnded, RunStarted.
  - Media banner: Create Skipped Media; Retry Failed and Skipped Items; no button when media is unavailable; the most frequent skipped reason.
  - Ready card on MixedCase: both model names, the media clause and the notice.
  - Progress: `ProgressUnits` values, and a retried Failed attachment that is never counted twice (bar value unchanged).
  - CG1 attachment clause.
- **GenerationHistoryTests (+4):** mixed-case wave, non-relevant and repair rows; no repair row when nothing is to repair; `Waves_MediaAvailableMatchesSeeds`; no Non-relevant row for a relevant-only plan.
- **Filtered:** `GenerateCorpus|GenerationHistoryTests`, 61 passed.
- **Full suite (wave-8 post-wave gate):** `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`: **3397 passed, 0 failed, 0 skipped** (baseline 3379), in 8 m 14 s. The `GenerationResetTests` flake did not occur.
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- **Render:** I inspected the 144-DPI Ready PNG (1000x540). It now shows the non-relevant threads and attachments in the body, and a Story / Volume split estimate line. Nothing is clipped.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] The second `GenerationHistory.Waves` caller is `HistoryView`, not `ShowDashboardFromDraft`**
- `ShowDashboardFromDraft` has no run state, so it builds its banners from the cached GenerationState counts. `HistoryView` (the entry path) gets the session's mediaAvailable, models and prices through a small `EntryPricing` bag passed to `ReadEntryAsync`.
- Commits: 37ebbaa, 1adde3d

**2. [Rule 3 - Blocking] Existing tests assumed wave-only Stages rows**
- `GenerationHistoryTests.Waves_ProjectStatePerWave` now filters to `StageRowKind.Wave`. ApprovedCase has non-relevant threads, so a Non-relevant mail row now follows the waves.
- `GenerateCorpusEntryTests.JournalWithPendingThreads_ShowsDashboardAndBanner` expects waves + 1 rows and sums Done over the wave rows only.
- These are intended behavior changes (reopen rebuilds the stage rows); no assertion was weakened.
- Commit: 1adde3d

**3. [Rule 2 - Test safety] RunInputCreated test seam**
- The step fixture has a tested OpenAI key, so the runner would otherwise get a real `OpenAIMediaGenerator`.
- The harness swaps in `FakeMediaGenerator` after the step builds the real input, so the tests check the true wiring without calling OpenAI.
- Commit: 37ebbaa

**4. [Small additions]**
- `StepFixture(approved, mixed)` loads MixedCase.
- `ProgressUnits` lives in StepGenerateCorpus.cs, which owns `ApplyView` (that file was not listed in files_modified).

## TDD Gate Compliance

Both tasks are `tdd="true"`. Each task's tests were written before its production code, but tests and implementation went into one `feat` commit per task. The new members (`RunInputCreated`, `ProgressUnits`, `CancelConfirmation`, `MostFrequentSkippedReason`, the `Waves` mediaAvailable overload) did not exist yet, so the RED state would only have been a compile failure. There are no separate `test(...)` RED commits.

## Known Stubs

None. The 06-13 / 06-22 stubs are closed: attachment counts in the banner body, the cancel confirmation, the entry decision and the Ready card are wired, and the bar covers all work units.

## Threat Flags

None. No new endpoints or file access.
- T-06-68: the media generator is created only by `CreateMediaGenerator`, which returns null without a tested key.
- T-06-69: every button routes to `StartRunAsync`, which is guarded by `_isGenerating`.
- T-06-70: banner reasons are the runner's plain-words strings.

## Self-Check: PASSED
- FOUND: UserControls/StepGenerateCorpus.Run.cs, Services/Evidence/GenerationHistory.cs, ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs, ReelDiscovery.Tests/Evidence/GenerationHistoryTests.cs
- FOUND commits: 37ebbaa, 1adde3d
- All acceptance greps of both tasks match. `LastRunEnded != null` (non-comment) = 0, `GenerationHistory.Waves(plan, state, ` = 2.
