---
phase: 05-relevant-evidence-generation-narrative-ledger
fixed_at: 2026-10-03T00:00:00Z
review_path: .planning/phases/05-relevant-evidence-generation-narrative-ledger/05-REVIEW.md
iteration: 1
findings_in_scope: 9
fixed: 9
skipped: 0
status: all_fixed
---

# Phase 5: Code Review Fix Report

**Fixed at:** 2026-10-03T00:00:00Z
**Source review:** .planning/phases/05-relevant-evidence-generation-narrative-ledger/05-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 9 (2 Critical, 7 Warning; Info findings were out of scope)
- Fixed: 9
- Skipped: 0

**Verification:**
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"`: 2572 passed, 0 failed.
- Every finding has a regression test. The CR-01, CR-02, WR-03 and WR-07 tests were confirmed to fail before the fix and pass after it.

## Fixed Issues

### CR-01: Best-attempt selection ignores structural violations

**Files modified:** `Services/Evidence/ThreadUnitOfWork.cs`, `ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs`
**Commit:** fcdce2a
**Applied fix:** Attempts are now ranked in two steps. An attempt without V1 or V5 always beats one with them. After that, the fewer violations wins, and a tie still goes to the later attempt. The new `IsStructural` helper does the V1/V5 check.
**Test:** `ComposableAttempt_BeatsStructurallyBrokenAttempts_WithFewerViolations`. Attempts 1 and 3 each drop a message; attempt 2 is complete but names six people who are not in the cast. The thread is now kept with warnings from attempt 2. Before the fix it was failed.
**Status:** fixed: requires human verification (logic change in the selection rule)

### CR-02: A checkpoint from an unreadable journal overwrites the key and case.json

**Files modified:** `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs`
**Commit:** eaa67b8
**Applied fix:**
- `CheckpointCoreAsync` now checks the replay before projecting. If the journal is not readable (missing, IO error or over the size cap) or belongs to another plan, nothing is projected.
- The same happens if the projection itself throws. That catch used to fall back to an empty `GenerationRunState`.
- In all of these cases the checkpoint returns the last good state and ledger, held in `RunContext.LastGoodState` and `LastGoodLedger`. These are seeded from the start-of-run replay and updated only after a projection succeeds.
- An unusable checkpoint does not write the answer key, does not write the story file and does not call Persist, so `ProjectedUtc` is not stamped. Its `KeyResult` carries the reason.
- On the final checkpoint, RunEnded is still appended.
**Test:** `UnreadableJournalMidRun_KeepsLastGoodKeyAndState`. From wave 2 on, the test holds the journal open with an exclusive lock. Afterwards the key still shows wave 1 as Generated, every persisted checkpoint keeps the wave 1 counts and ledger, and `result.State.Written` matches wave 1.
**Status:** fixed: requires human verification (state-handling logic)

### WR-01: The live dashboard double-counts failures during Retry / Continue

**Files modified:** `Services/Evidence/GenerationSnapshot.cs`, `Services/Evidence/ThreadRenderContext.cs`, `Services/Evidence/ThreadUnitOfWork.cs`, `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Evidence/GenerationSnapshotTests.cs`, `ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs`
**Commit:** e6f7ba0
**Applied fix:**
- `RecordThread` takes a new optional `previous` status. When it is `Failed`, the run-wide and wave Failed counts are decremented first, floored at 0.
- `ThreadRenderContext` gets a `PreviousStatus` property. The runner sets it from the replayed state, and the unit of work passes it on.
**Tests:** `RecordThread_RetriedFailedThread_LeavesFailedCountOnce`. The existing retry test now also checks that the live snapshot ends with 0 failed and all threads done.

### WR-02: Spend on replaced failures and fatal-stop calls is dropped

**Files modified:** `Services/Evidence/GenerationJournal.cs`, `Services/Evidence/GenerationRunState.cs`, `Services/Evidence/ThreadUnitOfWork.cs`, `Services/Evidence/GenerationRunner.cs`, `Services/Evidence/GenerationSnapshot.cs`, plus tests in `GenerationJournalTests`, `ThreadUnitOfWorkTests` and `GenerationRunnerTests`
**Commit:** a61e23f
**Applied fix:**
- `JournalReplay.SpentCostUsd` adds up the cost of every ThreadDone, ThreadFailed and RunEnded record for the current plan. `GenerationRunState.From` uses that total.
- A `NotRun` outcome now carries its tokens and cost and adds the cost to the live snapshot. The runner collects this unjournaled spend and writes it on the RunEnded record.
- On the final checkpoint, RunEnded is now appended before the replay, so the final projection includes it.
- The stale comment in `FailAsync` is corrected.
**Tests:** `Replay_SpentCost_CountsReplacedFailuresAndRunEnded` and `FatalAfterAnAttempt_NotRunCarriesTheSpend`. The auth-failure runner test now also checks the RunEnded cost.

### WR-03: UI-thread marshalling falls back to the worker thread; the runner can wait forever

**Files modified:** `UserControls/StepGenerateCorpus.Run.cs`, `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs`
**Commit:** c98213e
**Applied fix:**
- `PersistOnUiThreadAsync` applies a checkpoint inline only on the thread that created the step (`_uiThreadId`), not when `InvokeRequired` is false. Every other caller goes through `BeginInvoke`, which already completes the task when no handle exists.
- It also returns at once when the step is `Disposing`.
- `ReleasePendingCheckpoints` sets a `_closing` flag under `_pendingLock`. A checkpoint that arrives after the release completes immediately instead of posting a message that could be dropped.
- The method is now `internal` so tests can call it.
**Test:** `PersistFromWorker_WithoutWindowHandle_NeverAppliesOffTheUiThread`. Before the fix the checkpoint was applied on the pool thread; now it never is. After Dispose, a late checkpoint completes immediately.

### WR-04: Relevant threads with no messages count toward the total but are never scheduled

**Files modified:** `Services/Evidence/WavePlanner.cs`, `Services/Evidence/GenerationRunState.cs`, `Services/Evidence/RenderAssignments.cs`, `Services/Evidence/GenerationHistory.cs`, `UserControls/StepGenerateCorpus.Run.cs`, `ReelDiscovery.Tests/Evidence/GenerationRunStateTests.cs`
**Commit:** 432ada5
**Applied fix:** Added one shared predicate, `WavePlanner.IsGeneratable`: relevant, non-empty id, and at least one non-null message. The wave planner, the run state, the render assignments, the step's relevant-thread list and the run-ended total in the history all use it.
**Test:** `RelevantThreadWithoutMessages_IsNeitherScheduledNorPending`

### WR-05: Start Over deletes the journal even when it could not list its files

**Files modified:** `Services/Evidence/GenerationJournal.cs`, `Services/Evidence/GenerationReset.cs`, `ReelDiscovery.Tests/Evidence/GenerationResetTests.cs`
**Commit:** 396ad56
**Applied fix:**
- New `GenerationJournal.TryListAllFiles(caseFolder, out files)` returns false when the journal could not be read in full (over the size cap or an IO error). `ListAllFiles` now calls it and keeps its signature.
- When the list is incomplete, Start Over keeps the journal and adds `GenerationReset.JournalKeptMessage` to `Errors`.
**Test:** `StartOver_JournalNotReadable_KeepsJournalAndReportsIt`. A handle that blocks reads but allows delete keeps the journal and reports it. A second Start Over, once the journal is readable, removes the EML from the earlier plan and then the journal.

### WR-06: Duplicate thread ids in a plan run the same thread twice

**Files modified:** `Services/Evidence/WavePlanner.cs`, `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Evidence/WavePlannerTests.cs`, `ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs`
**Commit:** 29bd0fc
**Applied fix:** `WavePlanner.Plan` removes duplicate ids and keeps the first copy, the same rule the runner already used. The runner's per-wave `pending` list is also made distinct (ordinal).
**Tests:** `Plan_DuplicateThreadId_ScheduledOnce` and `DuplicateThreadIdInPlan_IsRenderedOnce`

### WR-07: RunAsync can throw and leave the snapshot "running" if Persist raises an OCE

**Files modified:** `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs`
**Commit:** 5b2891f
**Applied fix:** The Persist catch in `CheckpointCoreAsync` now catches every exception, including cancellation types. In the `finally` block, the final `CheckpointAsync` is wrapped so that `FinishSnapshot` always runs. If the checkpoint throws, it falls back to the last good state.
**Test:** `PersistThrowingCancellation_RunStillEndsCleanly`. It failed before the fix: the exception escaped `RunAsync`.

## Notes

- The worktree could not attach to `main` because `main` was already checked out in the primary working tree. The fixes were committed on a temporary branch, `gsd/05-reviewfix`, in an isolated worktree. `main` was then fast-forwarded to it; the primary tree was clean. The worktree and the temporary branch were removed afterwards.
- On a cold build the test project shows two existing `xUnit1031` analyzer warnings in `GenerateCorpusStepTests.cs`, from code these fixes did not touch. The app project builds with 0 warnings.

---

_Fixed: 2026-10-03T00:00:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
