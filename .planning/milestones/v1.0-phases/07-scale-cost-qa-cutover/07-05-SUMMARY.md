---
phase: 07-scale-cost-qa-cutover
plan: 05
subsystem: ai-cost
tags: [cost, journal, prompt-cache, batch, replay, PROV-07]
requires:
  - "07-01 TokenUsage/CostBreakdown, LlmResult.Cost"
  - "07-03 runner/repair paths"
provides:
  - "JournalRecord.CostDetail, CacheWriteTokens, CacheReadTokens, Provider (backward compatible)"
  - "JournalReplay.SpentDetail, UnsplitSpentUsd, WriterProvider"
  - "Gateway cost carried through PlanCallOutcome -> RenderAttempt -> units/builders -> journal, snapshot and RunEnded"
  - "RunContext.AddUnjournaledSpend(CostBreakdown text, decimal media, int in, int out); RunEnded fully itemized"
affects: [07-ticker, 07-case-summary, 07-batch, 07-reviewer-choice]
tech-stack:
  added: []
  patterns:
    - "One cost function: every runtime cost is LlmResult.Cost copied, never recomputed from tokens"
    - "Replay safety net: max(0, CostUsd - CostDetail.TotalUsd) (whole CostUsd when no detail) goes to UnsplitSpentUsd, so SpentDetail.TotalUsd + MediaSpentCostUsd + UnsplitSpentUsd == SpentCostUsd"
key-files:
  created: []
  modified:
    - Models/Evidence/JournalRecord.cs
    - Services/Evidence/GenerationJournal.cs
    - Services/Corpus/PlanCallRunner.cs
    - Services/Evidence/RenderProfile.cs
    - Services/Evidence/ThreadUnitOfWork.cs
    - Services/Evidence/AttachmentUnitOfWork.cs
    - Services/Evidence/AttachmentRepair.cs
    - Services/Evidence/CastVoiceBuilder.cs
    - Services/Evidence/SideWorldBuilder.cs
    - Services/Evidence/GenerationRunner.cs
    - ReelDiscovery.Tests/Evidence/GenerationJournalTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
    - ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs
    - ReelDiscovery.Tests/Evidence/AttachmentUnitOfWorkTests.cs
decisions:
  - "07-05: RunEnded now writes CostUsd = unjournaled text (CostDetail.TotalUsd) and MediaCostUsd = unjournaled media; replay counts CostUsd + MediaCostUsd for RunEnded (old lines have MediaCostUsd 0, so totals are unchanged)"
  - "07-05: ThreadDone, SideWorld, CastVoices and top-level AttachmentsRepaired spend replays text only (withMedia: false) exactly as before; ThreadFailed, RunEnded and attachment records add their media"
  - "07-05: ThreadOutcome/AttachmentRepairOutcome NotRunCostUsd keeps its text + media meaning; the new NotRunCost (detail) + NotRunMediaCostUsd split it for RunEnded"
  - "07-05: ThreadUnitOfWork/AttachmentUnitOfWork constructors keep their model parameters for callers but no longer price anything"
metrics:
  duration: 16min
  completed: 2026-10-06
  tasks: 2
  files: 14
---

# Phase 7 Plan 05: Gateway Cost Breakdown Into the Journal Summary

Every runtime cost now comes from the gateway's `LlmResult.Cost`. It travels through PlanCallRunner, RenderProfile, the thread and attachment units, the voice-cast and side-world builders and the runner, and is written into each journal line as a `CostDetail` with cache-token counts. Replay sums it into a cross-session `SpentDetail`, with an `UnsplitSpentUsd` safety net and the writer provider.

## What Was Built

**Task 1: journal schema and replay**
- `JournalRecord` gained `CacheWriteTokens`, `CacheReadTokens`, `CostBreakdown? CostDetail` (null on pre-Phase-7 lines) and `Provider` (RunStarted). `Normalize()` clamps the token counts and every CostDetail field to 0 or more, and sets a null `Provider` to empty (T-07-14).
- `JournalReplay` gained `SpentDetail`, `UnsplitSpentUsd` and `WriterProvider`. Every spend site goes through one `AddSpend(replay, record, withMedia)` helper (9 call sites), and the catch-path reset clears all three.
- The RunEnded replay counts `CostUsd + MediaCostUsd`.

**Task 2: cost plumbing**
- `PlanCallOutcome<T>` and `RenderAttempt` carry `Cost`, `CacheWriteTokens` and `CacheReadTokens`, copied (`Cost = result.Cost.Clone()` / `ex.Result.Cost.Clone()`) on every return path.
- `ThreadUnitOfWork`:
  - A `RenderUsage` sums the cost of every attempt. ThreadDone and ThreadFailed records carry `CostDetail` and the cache tokens, and the snapshot gets `detail.TotalUsd`.
  - `ThreadOutcome` gained `UnjournaledCost`/`UnjournaledMediaCostUsd` (thread NotRun or Unjournaled) and `NotRunCost`/`NotRunMediaCostUsd` (NotRun attachments).
  - The token-based `Cost(...)` helper is deleted.
- `AttachmentUnitOfWork`: `AttachmentOutcome.Cost` adds up the calls, and `CostUsd = Cost.TotalUsd`. `ToRecord` writes `CostDetail` and the cache tokens. The token-based `Cost(...)` is deleted.
- `AttachmentRepairOutcome` gained `Cost`, `NotRunCost` and `NotRunMediaCostUsd`, filled in `AddSpend`, `AddNotRun` and `Broken`.
- `CastVoiceOutcome` and `SideWorldOutcome` carry `Cost`. The runner journals them with `CostDetail`, and `ModelCost` is deleted.
- `RunContext.AddUnjournaledSpend(CostBreakdown, decimal media, int, int)` replaces the 3-argument overload, and every caller was updated. `AppendRunEndedAsync` writes `CostUsd = detail.TotalUsd`, `MediaCostUsd = media` and `CostDetail = detail`.
- RunStarted writes `Provider = _llm.Provider.ToString()`.

## Tests

- `GenerationJournalTests`: 6 new tests:
  - CostDetail round trip into SpentDetail (with batch savings)
  - A Phase 6 raw-JSON line counted as unsplit, with an empty WriterProvider
  - Nested and repaired attachment CostDetail
  - Normalize clamps
  - `Replay_MixedRunEnded_TotalEqualsItems` (new and old RunEnded lines, a SideWorld line with a 0.10 gap, ThreadFailed with media, CastVoices)
  - WriterProvider: the latest run wins and an empty value is ignored

  The locked-file reset test also checks that SpentDetail and Unsplit are 0.
- `ThreadUnitOfWorkTests.Retry_CostDetailIsTheGatewaySumOfBothAttempts`: two attempts with cache read and write tokens. Each category equals the price times the summed tokens, CostUsd == CostDetail.TotalUsd, and the snapshot matches.
- `GenerationRunnerMixedTests` checks:
  - `CacheReads_ReachThreadDoneCostDetail_SnapshotEqualsJournal`: ThreadDone, nested attachments, SideWorld, CastVoices and RunEnded all carry CostDetail == CostUsd; the run has no unsplit spend; the items add up; the snapshot equals replay; RunStarted.Provider is set.
  - `NotRunThreadAndVoiceCast_RunEndedFullyItemized`: RunEnded CostDetail.TotalUsd == CostUsd == the story price of the paid NotRun render.
- `GenerationRunnerTests.RunStarted_RecordsTheWritingProvider`: an OpenAI gateway writes "OpenAI", and replay's WriterProvider is "OpenAI".
- Verification:
  - `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings.
  - Full suite (`Category!=Render&Category!=Scale`): 3417/3417 passed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 5feb56e | test(07-05): add failing journal cost detail, unsplit and writer provider tests |
| 1 GREEN | d1063aa | feat(07-05): journal cost detail, cache tokens, writer provider and replay breakdown |
| 2 | a7cb131 | feat(07-05): carry gateway cost breakdown through units, builders and runner into the journal |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] AttachmentUnitOfWorkTests priced models the gateway never saw**
- **Found during:** Task 2
- **Issue:** The fixture passed priced `_story`/`_volume` to the unit, but its gateway used unpriced models. Once cost came from the gateway, 4 cost assertions read 0.
- **Fix:** The fixture now builds `LlmGateway(_fake, _story, _volume)` from priced copies of `CaseTestData.StoryModel()`/`VolumeModel()`. The assertions are unchanged.
- **Files modified:** ReelDiscovery.Tests/Evidence/AttachmentUnitOfWorkTests.cs
- **Commit:** a7cb131

**2. [Rule 1 - Bug] Journal stop-line probe sized lines without the new fields**
- **Found during:** Task 2
- **Issue:** `JournalAtStopLine_StopsRunFatal_JournalStaysReadable` sized its cap from RunStarted/RunEnded probe lines that had no Provider and no CostDetail or media. The real lines are now longer, so RunEnded no longer fit.
- **Fix:** The probe lines now carry Provider, MediaCostUsd and a full CostDetail. This growth was accepted in the plan (T-07-16).
- **Files modified:** ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
- **Commit:** a7cb131

**3. Interpretation: `NotRunCostUsd` keeps text + media**
- The plan says to keep `NotRunCostUsd = NotRunCost.TotalUsd` "for existing readers". The existing readers (the runner's NotRun check and the repair snapshot guard) rely on the combined text + media value. It was left combined, and the split is carried by the new `NotRunCost` + `NotRunMediaCostUsd`.

**4. Test placement**
- The retry-sum behavior was tested in `ThreadUnitOfWorkTests`, which already has the V-check retry fixture, instead of `GenerationRunnerTests`.

## TDD Gate Compliance

- Task 1: RED (5feb56e, compile failure on the missing members) then GREEN (d1063aa).
- Task 2: there is no separate RED commit. Implementation and tests landed together in a7cb131 because the new tests depend on members that the plumbing introduces. They were run and passed after the implementation.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Models/Evidence/JournalRecord.cs (`public CostBreakdown? CostDetail`, `public string Provider`)
- FOUND: Services/Evidence/GenerationJournal.cs (`UnsplitSpentUsd`, `AddSpend(` x9)
- FOUND commits: 5feb56e, d1063aa, a7cb131
- Acceptance greps: `ModelCost(` = 0; `CalculateTotalCost` = 0 in ThreadUnitOfWork/AttachmentUnitOfWork/GenerationRunner; `CostDetail =` 2 / 1; `Provider = _llm.Provider` = 1; 3-argument `AddUnjournaledSpend` = 0
