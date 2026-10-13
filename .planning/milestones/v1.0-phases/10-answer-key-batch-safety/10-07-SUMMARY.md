---
phase: 10-answer-key-batch-safety
plan: 07
subsystem: ui/generate-corpus
tags: [batch, provider, run-start, W5, D-13, D-14, D-22, D-24]
requires:
  - "10-02 (BatchingLlmProvider.ResolveOtherProviderPendingAsync, OtherProviderResolution)"
  - "10-06 (TransportFor, HasBatchTransport, SetOtherProviderBatches, OtherProviderKeyMissingText)"
provides:
  - "Run-start resolution of other-provider pending batches before the run's gateway exists"
  - "GenerationEventKind.OtherProviderBatchesCollected / OtherProviderBatchesCancelled (appended)"
  - "GenerationRunInput.OtherProviderResolution (Activity only)"
  - "GenerationUiText.RunRefusedOtherProviderKeyText / RunRefusedOtherProviderFailedText"
  - "StepGenerateCorpus.IsResolvingBatches (busy state; WizardForm close asks C-G3)"
affects:
  - "Generate, Continue, Retry Failed Items and Create Skipped Media all pass through the resolver"
tech-stack:
  added: []
  patterns:
    - "Resolver awaited off the UI thread only when provider-batches.jsonl exists; otherwise the run starts synchronously as before"
key-files:
  created: []
  modified:
    - Models/Evidence/GenerationProgress.cs
    - Services/Evidence/GenerationRunner.cs
    - UserControls/GenerationUiText.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - UserControls/StepGenerateCorpus.cs
    - Forms/WizardForm.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
decisions:
  - "The run-start resolver runs only when the batch ledger file exists, so runs without a ledger keep their synchronous start (IsGenerating true right after the click)"
  - "Closing during a resolution lets it finish (CancellationToken.None, never interrupted mid-ledger), then no run starts (_stopAfterResolve)"
  - "A resolver exception counts as Failed (D-22); the status shows fixed copy, never the exception message (T-07-52)"
metrics:
  duration: "~35 min"
  completed: 2026-10-12
  tasks: 2
  files: 9
---

# Phase 10 Plan 07: Run-Start Other-Provider Batch Resolution Summary

W5 is now closed at run start. Before any run builds its gateway, the app collects or cancels this plan's pending batches from the other provider. A batch that has ended is collected and later billed as an orphan at Finished (CR-A04). A batch still running is cancelled, so its items run once under the current provider. If the other provider has no key, or it could not be reached, the run refuses to start with fixed copy (D-13, D-14, D-22).

## What Was Built

### Task 1: Event kinds, runner Activity lines, copy (commits 341e56c RED, c9911b6 GREEN)
- `GenerationEventKind` gains `OtherProviderBatchesCollected` and `OtherProviderBatchesCancelled`, appended after `BatchCollectedAfterRestart`.
- `GenerationRunInput.OtherProviderResolution`: right after RunStarted/RunContinued, the runner adds one Collected line and/or one Cancelled line when their counts are > 0.
- Activity copy:
  - "Collected {N} {Provider} batch(es) from an earlier session. Its/Their results are kept as batch results not used." (Success)
  - "Cancelled {N} {Provider} batch(es) from an earlier session. Its/Their requests run once in this run." (Warning)
- `RunRefusedOtherProviderKeyText(provider)` = "Generation did not start: " + `OtherProviderKeyMissingText(provider)`. `RunRefusedOtherProviderFailedText(provider)` is the verbatim D-22 copy. The existing `Activity_EveryKindHasText_NoBannedWords` already covers the new kinds through `Enum.GetValues`.

### Task 2: Resolve or refuse in RunCoreAsync (commits 744a603 RED, bdfa98f GREEN)
- Order of checks: stale guide → not approved → `EnsureFolder` → resolution → gateway (`RunGatewayFactory` / `GatewayFactory` / `CreateRunGateway`). Nothing is billed before the resolution, and a refused run writes no journal.
- `ResolveOtherProviderPendingAsync` runs in `Task.Run` with `TransportFor()` and `CancellationToken.None`. An exception becomes `Failed`.
- When blocked: `SetOtherProviderBatches(Found)`, then the status shows the key text or the failed text in Warning.
- `_isResolvingBatches` keeps the step busy (`RaiseStateChanged` when it is set and when it is reset):
  - `CanMoveNext` and `CanMoveBack` are false.
  - The `RunCoreAsync` entry guard refuses a second run.
  - Both Start Over guards refuse (no confirmation).
  - The `OnLeaveStepAsync` guard refuses.
  - `WizardForm.IsRunActive` includes `StepGenerateCorpus { IsResolvingBatches: true }`, so closing asks C-G3.
- The run input carries `OtherProviderResolution` when `Found > 0`.
- Seven step tests were added:
  - `Run_OtherProviderEnded_CollectedBeforeGateway_BilledAtFinished`
  - `Run_OtherProviderRunning_CancelledThenRunsOnce`
  - `Run_LiveMode_StillResolves`
  - `Run_OtherProviderKeyMissing_Refused`
  - `Run_OtherProviderUnreachable_Refused`
  - `Run_OtherProviderResolverThrows_Refused`
  - `Resolving_BlocksNavigationAndStartOver`

## Verification
- `-warnaserror` build is green at all four commits. The RED commits used compiling stubs.
- Filter `GenerationUiTextTests|GenerationRunnerTests|GenerationSnapshotTests`: 337 passed.
- Plan verification filter `GenerateCorpusStepTests|GenerationRunnerTests|GenerationUiTextTests|Batch` (plus `GenerateCorpusEntryTests|WizardForm`): 551 passed, 0 failed.
- Every acceptance grep matches. `_isResolvingBatches` appears 7 times in Run.cs, and the resolver is at l.496, before `RunGatewayFactory` at l.526.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The resolver is awaited only when the batch ledger file exists**
- **Found during:** Task 2
- **Issue:** If the run always awaited `Task.Run` before setting `_isGenerating`, it would no longer start synchronously. Existing tests assert `IsGenerating`, `CancelButton.Visible` and similar state right after the click (for example `GenerateEmails_RunsToCompletion_PersistsOnUiThreadAndSaves`). The plan requires those tests to stay unchanged.
- **Fix:** The resolver runs only when `File.Exists(BatchLedger.PathFor(folder))`. Without a ledger, `Found` would be 0 anyway.
- **Files modified:** UserControls/StepGenerateCorpus.Run.cs
- **Commit:** bdfa98f

**2. [Rule 2 - Missing critical] No run starts after a close during the resolution**
- **Found during:** Task 2
- **Issue:** While the resolver runs, `CancelAndWaitForRun` (called on close) cancels only the previous run's token. After the resolution, `RunCoreAsync` would have built a gateway and started a billed run while the window was closing.
- **Fix:** `CancelAndWaitForRun` sets `_stopAfterResolve` while `_isResolvingBatches` is set. `RunCoreAsync` returns after the resolution when that flag is set or the control is disposed.
- **Files modified:** UserControls/StepGenerateCorpus.Run.cs, UserControls/StepGenerateCorpus.cs
- **Commit:** bdfa98f

**3. [Rule 2] Extra test `Run_OtherProviderResolverThrows_Refused`**
- It checks the catch path: an exception refuses the run, and the status never shows the exception message (T-10-26).

## Render / DPI
- New on-screen text includes the status-line refusal copy (both texts) and two Activity rows. They use existing labels and the Activity grid, with no layout change.
- **This machine renders at 120 DPI.** A human still needs to check at 150% scaling that the longer key-missing refusal text wraps correctly in the status line.

## Threat Flags
None. The plan's threat register covers every surface added here (T-10-25..28).

## TDD Gate Compliance
RED and GREEN commits exist for both tasks: 341e56c then c9911b6, and 744a603 then bdfa98f.

## Self-Check: PASSED
