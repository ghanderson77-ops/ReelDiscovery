---
phase: 07-scale-cost-qa-cutover
plan: 23
subsystem: generation-runner
tags: [batch, RUN-03, RUN-04, D-05, D-07-amended, D-09, D-10, gap-closure]
requires:
  - phase: 07-10
    provides: "BatchingLlmProvider, BatchStage labels, batch-mode runner"
  - phase: 07-21
    provides: "5K live + batch scale tests"
provides:
  - "BatchStage.UseLiveGate / LiveGate: ambient gate every live send of BatchingLlmProvider waits on"
  - "BatchingLlmProvider live scope: serves an already-collected batch result, otherwise goes live at once (never waits on an in-flight batch)"
  - "BatchingLlmProvider.CancelPendingAsync(include, closeOnCancelFailure) and CancelPendingStagesAsync(stageMatch)"
  - "GenerationRunner: relevant waves live at live width; Stage B on its own BatchMaxParallel run with a _maxParallel live gate; stale 'wave N' batches closed when a batch run finishes"
affects: [07-22, 07-24]
tech-stack:
  added: []
  patterns:
    - "Ambient AsyncLocal live gate: the semaphore is held only during the live HTTP call, never while waiting on a batch"
    - "Stage-only PlanCallRun: batch fan-out width applies only to the stage that is batched"
key-files:
  created: []
  modified:
    - Services/Ai/BatchStage.cs
    - Services/Ai/BatchingLlmProvider.cs
    - Services/Evidence/GenerationRunner.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs
    - ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs
    - ReelDiscovery.Tests/Evidence/BatchParityTests.cs
    - ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs
decisions:
  - "Amended D-07 implemented: in batch mode relevant waves (renders, attachments, chained attachments, repairs) run live at MaxParallelThreads; only non-relevant mail and its attachments are batched, after the last relevant wave commits"
  - "Stage B live attempts (D-10 fallbacks, AllowBatch=false retries) are gated at _maxParallel via BatchStage.UseLiveGate, so a failed provider batch never exceeds live parallelism"
  - "A finished batch run best-effort cancels pending 'wave N' batches from earlier builds and records them Cancelled even when the provider cancel fails, so PendingBatchCount returns to 0"
  - "Closed batches stop their poll loop (TrackedBatch.Closed) so a cancelled stale batch is never collected and billed as an orphan in the same run"
metrics:
  duration: ~45 min
  completed: 2026-10-08
---

# Phase 7 Plan 23: Relevant Waves Live in Batch Mode Summary

With batch pricing on, relevant waves now run live at live width (3), and only non-relevant mail and its attachments go to the provider Batch API. They are submitted as "non-relevant mail" after the last relevant wave commits. Stage B live fallbacks are capped at 3 concurrent calls. When a run finishes, it closes any per-wave batches an earlier build left pending, so Continue never waits on them.

This closes UAT gap 1 (BLOCKER). Before this change, each relevant wave was one provider batch, so the run waited through 24 batch cycles (about 28 min each observed, 24 h SLA) before non-relevant mail could start.

## Tasks

| # | Task | Commits | Files |
|---|------|---------|-------|
| 1 | Decorator: live scope reuses collected results, ambient live gate, filtered cancel of pending stages | fe493e0 (RED), 2049250 (GREEN), 84c06b5 (refactor) | Services/Ai/BatchStage.cs, Services/Ai/BatchingLlmProvider.cs, UserControls/StepGenerateCorpus.Run.cs, ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs, ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs |
| 2 | Runner: relevant waves live at live width, Stage B live gate, stale wave batches closed at run end | 4a7109c (RED), 1c9d7b1 (GREEN) | Services/Evidence/GenerationRunner.cs, ReelDiscovery.Tests/Evidence/BatchParityTests.cs |
| 3 | 5K batch scale variant: only the non-relevant stage is batched | eb3bea6 | ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs |

## What changed

- **BatchStage**: new AsyncLocal `LiveGate` and `UseLiveGate(SemaphoreSlim)` scope.
- **BatchingLlmProvider.SendAsync**:
  - `AllowBatch = false` requests, and any request after Detach, go live through `SendLiveAsync`. This is now the only `_live.SendAsync` call site, and it honors the gate.
  - Inside `EnterLive`, the provider first uses a result an earlier session already collected (logged as Consumed, billed at batch price). Otherwise the request goes live at once. A live-scope request never attaches to an in-flight batch and never enqueues.
- **CancelPendingAsync**:
  - New optional `include` filter. With a filter, both the cancel and the file deletion are limited to the cancelled records.
  - New optional `closeOnCancelFailure`: provider cancel errors are swallowed and the batch is still recorded Cancelled.
  - It now returns the ids it closed.
  - Start Over keeps the old behavior: a failing cancel still propagates.
- **CancelPendingStagesAsync(stageMatch)**: closes this plan and provider's matching pending batches. It marks them Closed so their poll loop stops before collecting, and releases any waiters to live (D-10). It returns the count.
- **GenerationRunner**:
  - The run-level `PlanCallRun` is always `_maxParallel`.
  - Relevant wave units are created under `BatchStage.Enter("wave N")` plus `BatchStage.EnterLive()`.
  - In batch mode, Stage B chunk units run on their own `PlanCallRun(run.UserToken, BatchMaxParallel)` inside `UseLiveGate(new SemaphoreSlim(_maxParallel))`, and its fatal state is checked after each chunk.
  - When a batch run ends Finished, `CloseStaleWaveBatchesAsync` runs before orphan billing.

## Measurements (5K scale, Debug, net10.0 test host)

| Mode | Threads / emails | Run | Submissions | Peak working set (sampled / process) | Managed after GC | Journal |
|------|------------------|-----|-------------|--------------------------------------|------------------|---------|
| Live | 2299 / 5000 | 164.3 s | n/a | 360 MB / 362 MB | 49 MB | 6.8 MB |
| Batch | 2299 / 5000 | 97.9 s | 9 (all "non-relevant mail") | 423 MB / 433 MB | 74 MB | 6.8 MB |

- In the 5K plan, no non-relevant thread uses the render schema, so the informational non-relevant render start-up spread showed 0 units. Filler uses its own schema.
- The answer key is byte-identical to live, the EML counts match, and every key status is Generated or Generated - with warnings (6065 / 11).

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and the test project build: 0 warnings, 0 errors. Both were built with `--artifacts-path` in the session scratchpad, because the user's running ReelDiscovery.exe locks `bin\Debug`.
- BatchingLlmProviderTests, GenerationResetTests and BatchLedgerTests: green, and BatchingLlmProviderTests passed 3 runs in a row.
- BatchParityTests, GenerationRunnerTests, GenerationSnapshotTests and DistributionParityTests: 78/78 on each of 3 consecutive runs.
- Scale category: 2/2 passed.
- Full non-render, non-scale suite: 3720/3731 with artifacts in the scratchpad. The 11 failures were source-scanning tests (PlatformTests, TelemetryRemovalTests, WizardFlowTests, CaseScenarioUiLogicTests, ConcurrencyTests) that find the repo root from the test assembly path. Re-run with artifacts under the repo (obj/, gitignored), those 86 tests pass. No real failures.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Start Over caller passes ct by name**
- **Found during:** Task 1
- **Issue:** `CancelPendingAsync` gained the optional `include` and `closeOnCancelFailure` parameters before `ct`. The Start Over call in `UserControls/StepGenerateCorpus.Run.cs` passed `CancellationToken.None` positionally, so it no longer compiled.
- **Fix:** It now passes `ct: CancellationToken.None`. Behavior is unchanged.
- **Commit:** 2049250

**2. [Rule 1 - Bug] A closed stale batch could be collected and billed as an orphan in the same run**
- **Found during:** Task 1 design
- **Issue:** After the provider cancel, the next poll sees Cancelled and collects whatever finished into `_available`. The orphan billing that runs right after would then bill it, which the "RunEnded spend does not include it" rule forbids.
- **Fix:** Added `TrackedBatch.Closed`. The poll loop checks it after its delay, after the status call and before collect, and returns without collecting.
- **Commit:** 2049250

**3. [Test fixture] Parity test no longer expects render-schema items in submissions**
- **Found during:** Task 2 GREEN
- **Issue:** In MixedCase, every non-relevant thread uses the filler schema. Once relevant renders went live, no submitted item had the render schema.
- **Fix:** The assertion now checks that at least one submitted item names a non-relevant thread, plus the new check that no submitted item names a relevant thread.
- **Commit:** 1c9d7b1

**4. [Acceptance grep] Single `_live.SendAsync` call site**
- The first GREEN version of `SendLiveAsync` had two call sites (gated and ungated). It was refactored into one try/finally with `gate?.Release()`.
- **Commit:** 84c06b5

### Additional tests beyond the plan
- `EnterLive_AllowBatchFalse_NeverConsumesCollectedResult`: an `AllowBatch = false` request in a live scope stays live per D-10.
- `CancelPending_DefaultArguments_PropagateCancelFailure`: Start Over still surfaces a failing cancel.
- `FinishedRun_ClosesStaleWaveBatch_PendingCountZero` is a Theory covering both a successful cancel and a failing one (`CancelFailure`). RunEnded spend is compared with a clean batch run.

## TDD Gate Compliance

- Task 1: RED fe493e0 (compile failure: no `UseLiveGate` / `CancelPendingStagesAsync`), then GREEN 2049250, then REFACTOR 84c06b5.
- Task 2: RED 4a7109c, checked against the unchanged runner: 7 of 12 BatchParityTests failed, and the stale-wave and restart tests hung until RunGuard. Then GREEN 1c9d7b1.
- The changed `Batch_ErroredItem_RendersLive_JournalMatchesLive` already passed on the old runner, because non-relevant items were batched before too. It is a re-targeted test, not a new behavior.

## Known limitations

- **Spend note:** closing a stale relevant-wave batch at run end is best effort. The provider may still bill items it completed before the cancel took effect; that spend is not itemized in the journal or RunEnded, because those results are never collected.
- **Checker I1 (cosmetic, accepted):** relevant-wave calls keep their wave labels, and wave rows stay mapped to batch status. A wave batch submitted by an earlier build can therefore still show "Batch: d of t done" on its wave row, and a "for wave {i}" activity line, until that batch ends or is closed at run end.

## Threat Flags

None. No new endpoints or trust-boundary surface; the threat register items T-07-76..79 are mitigated as planned.

## Self-Check

## Self-Check: PASSED

- All 6 commits found (fe493e0, 2049250, 84c06b5, 4a7109c, 1c9d7b1, eb3bea6)
- All modified files present; acceptance greps match (EnterLive x3, Enter x2, UseLiveGate x1, CancelPendingStagesAsync x1, single _live.SendAsync)
