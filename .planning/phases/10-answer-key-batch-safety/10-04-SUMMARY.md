---
phase: 10-answer-key-batch-safety
plan: 04
subsystem: ai/batch-ledger
tags: [batch, drain, wave, orphans, cancel, WR-A08]
requires:
  - "10-02 (orphan pricing paths, ledger counts)"
provides:
  - "BatchingLlmProvider.DrainStaleStagesAsync(stageMatch, ct)"
  - "TrackedBatch.Ended signal completed in UntrackLocked"
  - "GenerationRunner drains 'wave N' batches before the voice cast and waves"
affects:
  - "Finished path now only bills orphans; no end-of-run batch close"
tech-stack:
  added: []
  patterns:
    - "Cancel-but-track: provider cancel without a Cancelled ledger line, so the batch stays pending until Collected (D-21, Pitfall 10)"
    - "Bounded drain: Task.WhenAll(Ended).WaitAsync(DrainTimeout, ct)"
key-files:
  created: []
  modified:
    - Services/Ai/BatchingLlmProvider.cs
    - Services/Evidence/GenerationRunner.cs
    - ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs
    - ReelDiscovery.Tests/Evidence/BatchParityTests.cs
decisions:
  - "Stale wave batches are drained right after RunStarted, before the voice cast, so live waves use their results free (D-17)"
  - "A failed provider cancel during the drain is swallowed; the DrainTimeout bound still applies and the batch keeps polling, so late results become billed orphans"
  - "CancelPendingStagesAsync, Reopen, ClosedReason, IsClosed and TrackedBatch.Closed were deleted (no production caller left, PLAT-05)"
metrics:
  duration: "~25 min"
  completed: 2026-10-12
  tasks: 2
  files: 4
---

# Phase 10 Plan 04: Drain Stale Wave Batches Before the Waves Summary

WR-A08 is closed. A "wave N" provider batch that an earlier build left pending is now cancelled at the provider but still tracked, and the run waits for it (up to `DrainTimeout`) before the first live wave. Its partial results are then used free instead of being paid for a second time.

## What Was Built

### Task 1: DrainStaleStagesAsync (commits 2bb391a RED, 77bd040 GREEN)
- `TrackedBatch.Ended` is a signal that `UntrackLocked` completes. It fires when a batch is collected, fails, or is released.
- `DrainStaleStagesAsync(Func<string,bool> stageMatch, CancellationToken ct)`:
  - Selects only resumed batches whose polling `ResumePending` started and whose label matches `stageMatch`.
  - Calls `_transport.CancelAsync` on each one. A cancel failure is ignored (comment explains why); `OperationCanceledException` is rethrown.
  - Waits on every `Ended` signal, bounded by `DrainTimeout` and `ct`. On timeout it returns, and the batch keeps polling.
  - Never writes a ledger line, so there is no Cancelled line and the batch stays pending until it is Collected (D-21).
- Five new tests:
  - `DrainStaleStages_CancelsCollectsAndServesFree_NoCancelledLine`
  - `DrainStaleStages_IgnoresOtherLabels`
  - `DrainStaleStages_CancelFails_BoundExpires_StillPending`
  - `DrainStaleStages_UserCancel_Throws`
  - `DrainStaleStages_NotPolling_NotAwaited`
- They share a `SeedAbandonedBatchesAsync` helper: session A submits, its callers stop waiting, then it detaches.

### Task 2: Runner wiring and dead-code removal (commit ce99833)
- **Runner:** `GenerationRunner.RunAsync` calls `batch.DrainStaleStagesAsync(label => label.StartsWith("wave "), ct)` inside the `try`, right after the RunStarted append. It then refreshes `SetPendingProviderBatches`. If the user cancels during the drain, the run ends Cancelled through the existing OCE path.
- **Finished block:** it no longer calls `CloseStaleWaveBatchesAsync`, and that method is deleted. The block now only calls `AddOrphanSpend`.
- **Deleted from `BatchingLlmProvider`:** `CancelPendingStagesAsync`, `Reopen`, `ClosedReason`, `IsClosed`, the three `IsClosed` checks in `PollLoopAsync`, and `TrackedBatch.Closed`.
- **Tests:**
  - The two `CancelPendingStagesAsync_*` tests are deleted.
  - `FinishedRun_ClosesStaleWaveBatch_PendingCountZero` is rewritten as `StaleWaveBatch_DrainedBeforeWaves_NotPaidTwice(bool cancelFails)`.
  - `BatchMode` gained an optional `BatchTimings? timings = null` parameter.

## Verification
- `-warnaserror` build: 0 warnings, 0 errors at every commit.
- `--filter "FullyQualifiedName~BatchingLlmProviderTests"`: 69 passed.
- `--filter "BatchParityTests|BatchingLlmProviderTests|GenerationRunnerTests"`: 102 passed.
- Plan verification `--filter "FullyQualifiedName~Batch|FullyQualifiedName~GenerationRunnerTests"`: 240 passed.
- Every acceptance grep is met: zero `CloseStaleWaveBatchesAsync`, zero `CancelPendingStagesAsync`/`IsClosed(`/`ClosedReason`, zero `public bool Closed`, one `batch.DrainStaleStagesAsync(` call, one `Ended.TrySetResult();`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Test assertion] Run spend asserted on the whole journal, not on RunEnded.CostUsd**
- **Found during:** Task 2
- **Issue:** The plan said to assert `ended.CostUsd > 0`. `RunEnded.CostUsd` only carries spend that no other journal line records, which is 0 in this scenario, so the assertion failed for both cases.
- **Fix:** The test now asserts that a RunEnded record exists and that the journal's total `CostUsd` is greater than 0.
- **Files modified:** ReelDiscovery.Tests/Evidence/BatchParityTests.cs
- **Commit:** ce99833

### Notes
- `CancelPendingAsync(..., closeOnCancelFailure)` keeps its parameter. The plan keeps `CancelPendingAsync` for Start Over and the resolver, and the parameter is outside this plan's scope.

## TDD Gate Compliance
- Task 1: RED `test(10-04)` 2bb391a used a `NotImplementedException` stub (5 of 5 failed), then GREEN `feat(10-04)` 77bd040.
- Task 2: the test rewrite and the runner change went into one `feat` commit (ce99833). The rewritten theory depends on the runner calling the drain, and deleting the old close path in the same change removed the tests' only old target. There is no separate RED commit for Task 2.

## Known Stubs
None.

## Threat Flags
None. T-10-16 to T-10-18 are mitigated as planned:
- No Cancelled line is written, so the batch stays pending until it is Collected.
- The wait is bounded and the user can cancel it, and only polling batches are awaited.
- Leftover results land in `_available` and are billed through PriceOrphans/MarkOrphansBilled.

## Self-Check: PASSED
