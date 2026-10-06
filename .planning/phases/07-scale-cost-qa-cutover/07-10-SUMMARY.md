---
phase: 07-scale-cost-qa-cutover
plan: 10
subsystem: evidence-generation
tags: [batching, runner, snapshot, start-over, RUN-03, RUN-04, D-05, D-07, D-09, D-10]
requires:
  - phase: 07-08
    provides: "BatchingLlmProvider, BatchStage, BatchStatusEvent, ResumePending/Detach/BillOrphansAsync"
  - phase: 07-09
    provides: "LlmProviderFactory.CreateBatchTransport"
  - phase: 07-07
    provides: "BatchLedger (FileName, ResultsFolderName), FakeBatchTransport"
  - phase: 07-05
    provides: "RunContext.AddUnjournaledSpend itemized"
  - phase: 07-14
    provides: "QaReportStore file-name constants"
provides:
  - "WizardState.CreateRunGateway(useBatch, caseFolder, planId, transport?, timings?) -> (Gateway, Batch)"
  - "GenerationRunInput.Batch; batch-mode parallelism (BatchMaxParallel) and Stage B chunk (BatchFillerChunkSize = 2_000)"
  - "Stage labels per wave / non-relevant chunk; attachment repairs inside BatchStage.EnterLive()"
  - "GenerationSnapshot.MapBatchStage / RecordBatchStatus / SetCollectingAfterRestart / SetPendingProviderBatches / CurrentBatchWait"
  - "WaveProgress batch wait fields; GenerationView CollectingAfterRestart and PendingProviderBatches; five batch GenerationEventKind members and GenerationEvent batch fields"
  - "Orphan spend added to RunEnded once on a finished batch run"
  - "Start Over deletes provider-batches.jsonl, batch-results/*.jsonl and the three qa-* reports"
affects: [07-11, 07-19, 07-21]
tech-stack:
  added: []
  patterns: ["AsyncLocal stage scope around synchronous task creation", "status events applied under the snapshot lock"]
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/BatchParityTests.cs
  modified:
    - Models/Evidence/GenerationProgress.cs
    - Services/Evidence/GenerationSnapshot.cs
    - Models/WizardState.cs
    - Services/Evidence/GenerationRunner.cs
    - Services/Evidence/GenerationReset.cs
    - ReelDiscovery.Tests/Evidence/GenerationSnapshotTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationResetTests.cs
    - ReelDiscovery.Tests/WizardStateProviderTests.cs
decisions:
  - "[07-10]: Batch stage rows aggregate only batches still waiting; a terminal event (Finished, FinishedWithFailures, FailedAtProvider, CollectedAfterRestart) removes the batch, decrements PendingProviderBatches and clears CollectingAfterRestart"
  - "[07-10]: A resumed batch's first Progress event creates its row track, because resumed batches never raise Submitted"
  - "[07-10]: CurrentBatchWait returns the lowest-index waiting stage row (the status-strip countdown source)"
  - "[07-10]: Orphan spend is added to the snapshot cost as well as RunEnded, so the tile matches the journal; a BillOrphansAsync failure is swallowed (RunAsync never throws) and the next finished run bills it"
  - "[07-10]: D-05 parity compares EML bytes exactly except inside Office parts, where it compares the content XML with relationship ids masked, because OpenXml packages are not byte-deterministic even between two live runs"
metrics:
  duration: ~16min
  completed: 2026-10-06
  tasks: 2
  files: 9
---

# Phase 7 Plan 10: Batch Mode Wired Into Generation Summary

The runner can now run through the batching decorator. `WizardState.CreateRunGateway` builds a gateway that is either live or batch-capable. In batch mode every unit of a wave starts at once under the stage label `wave {i}`. Non-relevant mail goes out in 2,000-thread chunks under `non-relevant mail`, and attachment repairs always run live. Batch status reaches the stage rows and the Activity feed. Pending batches from an earlier session are resumed. Spend on results that were collected but never used is billed in RunEnded once. Start Over now also removes the batch and QA files. Tests show that live and batch runs write the same journal, EMLs and answer key, and that a restart resumes pending batches without resubmitting anything.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | Snapshot batch status, progress fields, events and the run-gateway factory | cd34266 (RED), 686a2f9 (GREEN) | GenerationProgress.cs, GenerationSnapshot.cs, WizardState.cs, GenerationSnapshotTests.cs, WizardStateProviderTests.cs |
| 2 | Runner integration, orphan billing, Start Over cleanup, parity/restart proofs | 3e30d8c (RED), 4da2684 (GREEN) | GenerationRunner.cs, GenerationReset.cs, BatchParityTests.cs, GenerationResetTests.cs |

## What Was Built

- **Progress model:** six batch fields on `WaveProgress` (BatchWaiting, BatchCount, BatchFirstSubmittedUtc, BatchDone, BatchTotal, BatchNextCheckUtc). `GenerationView` gains `CollectingAfterRestart` and `PendingProviderBatches`. Five `GenerationEventKind` members are appended after `NotSaved`, and `GenerationEvent` gains the batch fields (requests, succeeded, failed, provider, stage, reason, duration, submitted).
- **Snapshot:**
  - A label-to-row map and a per-batch track, all under `_lock` (T-07-33).
  - Each row aggregates its waiting batches: count, earliest submission time, summed done/total, and the soonest next check.
  - `Progress` updates the row without adding an Activity row; the other five kinds each add their event.
  - `Reset` clears all batch state.
- **WizardState.CreateRunGateway:** uses the same model null checks as `CreateGateway`. Live mode returns `Batch = null`. Batch mode wraps the live provider in `BatchingLlmProvider` over `new BatchLedger(caseFolder)`, and prices orphans with this provider's catalog models.
- **Runner:**
  - **Startup:** maps wave rows and the non-relevant row, subscribes `RecordBatchStatus`, then calls `ResumePending()`, which sets `SetCollectingAfterRestart` and `SetPendingProviderBatches`.
  - **Parallelism:** `PlanCallRun` gets `BatchMaxParallel` (10,000) in batch mode and `_maxParallel` (3) in live mode.
  - **Stage labels:** wave and Stage B task creation are wrapped in `BatchStage.Enter(...)`; both repair sites are wrapped in `BatchStage.EnterLive()`.
  - **End of run:** on a finished run, orphan spend goes to `ctx.AddUnjournaledSpend` before the final checkpoint. The `finally` block unsubscribes and calls `Detach()`.
- **Reset:** deletes the ledger, the `*.jsonl` files directly inside `batch-results` (the folder too when it ends up empty), and `qa-scans.json`, `qa-leakage.json` and `qa-blind-review.jsonl`. It deletes nothing except those exact names (T-07-34).

## Verification

- `BatchParityTests` (8 tests):
  - Live vs batch parity: journal with Utc/cost fields removed, EMLs and answer key, with cheaper batch spend.
  - Batches by wave: one submission per wave, waves never mixed, and wave N+1 starts only after earlier waves are committed.
  - Repair goes live and is never submitted.
  - An errored item renders live and the journal still matches.
  - Restart: cancel while wave 1 waits, then Continue. Nothing is resubmitted, CollectedAfterRestart appears, and the journal equals an uninterrupted run.
  - Orphan billed once, and a second Continue adds $0.
  - Live constants unchanged; a live run sets no batch fields.
- Parity and reset tests passed 3 consecutive runs (25/25 each).
- Snapshot tests: 8 new batch tests. Gateway factory tests: 3 new tests.
- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- Plan filter (Evidence, Corpus, Ai, not Render/Scale): 2220 passed. Full non-render suite: 3611 passed.
- Acceptance greps:
  - `BatchStage.Enter(` = 2 and `BatchStage.EnterLive()` = 2
  - `BillOrphansAsync(` = 1
  - `MaxParallelThreads = 3`, `FillerChunkSize = 50` and `BatchFillerChunkSize = 2_000` each = 1
  - Reset constant references = 7

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Test correctness] EML parity inside Office attachments**
- **Found during:** Task 2 (GREEN)
- **Issue:** The plan asked for byte-identical EMLs. The OpenXml SDK writes zip timestamps, a random core-properties part name and random relationship ids into every .docx/.xlsx/.pptx, so the bytes differ even between two live runs. This nondeterminism already existed and has nothing to do with batching.
- **Fix:** `AssertSameEml` checks for whole-file byte equality first. If that fails, it requires identical message headers, part headers and decoded bytes for every non-Office part. For Office parts it requires identical content XML under `word/`, `xl/` and `ppt/`, with `R`+16-hex ids masked. Making the packages deterministic is logged in deferred-items.md.
- **Commit:** 4da2684

**2. [Additive] Small extra surface**
- `GenerationSnapshot.CurrentBatchWait()` came from the plan's behavior list but was not in the action steps.
- The snapshot cost includes orphan spend, so the tile matches RunEnded.
- Test placement: the snapshot tests went in `GenerationSnapshotTests.cs` and the factory tests in `WizardStateProviderTests.cs` (the plan's verify filter covers both), not `GenerationRunnerTests.cs`.

## Deferred Issues

- Office packages are not byte-deterministic (see deferred-items.md). Out of scope.

## Known Stubs

None. The UI wiring (batch toggle, stage-row text, status strip, banner, Start Over cancel) is 07-11.

## TDD Gate Compliance

Both tasks have a `test(...)` RED commit followed by a `feat(...)` GREEN commit (cd34266 → 686a2f9, 3e30d8c → 4da2684).

## Self-Check: PASSED
