---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 15
subsystem: generation-ui
tags: [winforms, wizard-step, run-lifecycle, ui-thread-marshalling, crash-recovery, start-over]
requires:
  - phase: 05-13
    provides: GenerationRunner, GenerationCheckpoint/GenerationPersist, GenerationReset
  - phase: 05-14
    provides: StepGenerateCorpus layout, ApplyView, entry decision, _refreshTimer
  - phase: 05-11
    provides: GenerationStateApplier, GenerationOutputs, GenerationRunState
provides:
  - StepGenerateCorpus run lifecycle (start/continue/retry, snapshot drain, UI-thread checkpoints, cancel, dispose)
  - Start Over, reopen materialization, leave-step key/story retries, OpenEml
  - WizardForm 8-step case flow with Generate Corpus, CaseStage.Generation mapping, C-G3 close during run
affects: [05-16, 05-17, Case Summary rows]
tech-stack:
  added: []
  patterns:
    - "Runner on the pool via Task.Run; persist callback BeginInvokes onto the UI thread and returns a TaskCompletionSource; pending TCSs released on dispose so the runner never hangs"
    - "Step-logic tests on an STA thread with WindowsFormsSynchronizationContext.AutoInstall = false (DoEvents otherwise swaps the context out and awaits resume on the pool)"
    - "Test seams as internal fields: Confirm, GatewayFactory, CheckpointApplied, OpenFile"
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
  modified:
    - UserControls/StepGenerateCorpus.Run.cs
    - UserControls/StepGenerateCorpus.cs
    - Forms/WizardForm.cs
    - ReelDiscovery.Tests/Case/WizardFlowTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
decisions:
  - "[Phase 05] A key/story write failure outranks the Finished/Cancelled outcome line at run end (it is the actionable item; the banner still offers Continue); Fatal/InternalError lines always win"
  - "[Phase 05] Reopen materialization applies the journal with final:false: counts, ledger and key are rebuilt, but LastOutcome and the stage are only set by a real run end"
  - "[Phase 05] Leave-step retry skips when the journal is unreadable or belongs to another plan, so it can never reset the key to Planned"
  - "[Phase 05] ShowStateCard takes the card kind so the single state-card button dispatches: Ready = run, NotApproved = NavigateToStage(ReviewPlan), PlanChanged/Unreadable = Start Over"
metrics:
  duration: ~50min
  completed: 2026-10-03
  tasks: 3
  files: 6
---

# Phase 5 Plan 15: Generate Corpus Run Wiring and Shell Integration Summary

Generate Corpus is now fully wired into the wizard. Generate Emails, Continue Generating and Retry Failed Threads all start the same `GenerationRunner` run, on the thread pool (`Task.Run`). A 250 ms timer paints the snapshot, and every checkpoint is applied to the `CaseDraft` on the UI thread and saved. The user can cancel, dispose or close mid-run and every finished thread stays saved. Start Over, crash recovery on reopen, write retries when leaving the step, and opening an EML are in place. The case flow now has 8 steps.

## What Was Built

**Task 1: Run lifecycle (`StepGenerateCorpus.Run.cs`)**
- `RunCoreAsync` checks guards, then builds the folder and gateway (a failure shows Danger status and nothing starts). It forces a window handle so `BeginInvoke` works, shows the run chrome and starts the timer. The run itself is `await Task.Run(() => runner.RunAsync(input, snapshot, PersistOnUiThreadAsync, token))`.
- The `finally` block stops the timer, applies a last drain, clears the generating flag and hides the bar and Cancel. Each of these steps is skipped once the step is disposed. Afterwards `ShowRunResult` rebuilds counts from `result.State` and shows the dashboard: the banner when threads are left, or Retry Failed Threads when the run finished with failures. It then shows the outcome line.
- `PersistOnUiThreadAsync` does a `BeginInvoke` plus a `TaskCompletionSource`. If the handle is gone, it completes immediately. Any pending checkpoints are released in `Dispose`.
- `ApplyCheckpointAsync` calls `GenerationStateApplier.Apply` with the draft and the checkpoint, sets `KeyError`/`StoryError`, shows the locked text (Warning) or failed text (Danger), then calls `SaveAsync()`.
- `RequestCancel` asks C-G1 through `Confirm(..., Question)`. It re-checks that the run is still active (WR-09), then sets `StoppingText` and cancels.
- `CancelAndWaitForRun(TimeSpan)` is copied from Review Plan. `Dispose` cancels the token and stops the timer.
- Copied from Review Plan: `QueueSave` / `SaveAsync`.

**Task 2: Start Over, reopen, leave-step, open EML**
- **Start Over:** asks C-G2 (Warning icon, default No) with the journal's file count. On Yes it runs `GenerationReset.StartOverAsync` and `GenerationStateApplier.Clear`. For an approved plan it rewrites the key with an empty overlay (all rows Planned) and calls `RecordPartialWrites`. It then saves and re-enters the step (Ready card). The status line shows `StartedOverText` (Success), or the first delete error or key error.
- **Entry:** `ReadEntryAsync` re-projects whenever case.json is behind the journal: the cached plan id differs, `ProjectedUtc` is missing, or `ProjectedUtc` is older than `replay.LastWriteUtc`. It builds the ledger and writes the key off the UI thread, then applies (`final: false`) and saves on the UI thread. No provider is ever built.
- **`OnLeaveStepAsync`:** retries a failed key write and/or story write once, off the UI thread, using the overlay from the replay. It clears the error on success and saves. It never throws.
- **`OpenEml`:** responds only to ThreadWritten and KeptWithWarnings rows. The path must pass `MailboxWriter.ResolveUnderCase` (which includes `IsSafeEmlPath`) and end in `.eml`. A missing file shows `MissingEmlText` (Danger). Otherwise the file opens through the shell (`OpenFile` seam), and Win32, InvalidOperation and IO failures show in the status line.
- **Activity grid:** wired to `CellDoubleClick` and Enter on `KeyDown`.

**Task 3: Shell (`Forms/WizardForm.cs`)**
- `StepTypesFor(CaseDriven)` now has 8 entries, with Generate Corpus at index 6.
- Added `CaseStage.Generation => typeof(StepGenerateCorpus)`.
- `StepGenerateCorpus` is added to the flush list, to `IsRunActive` (`{ IsGenerating: true }`) and to `StopActiveRun` (a 10 s `CancelAndWaitForRun` inside `WaitWhileStopping`).
- The `OnFormClosing` run prompt is now a switch: C-P2, C-G3 or C12.
- Tests: `WizardFlowTests` (8 steps plus the Generation stage) and `WizardShellRenderTests` (now "of 8", plus the new `wizard-shell-generate-corpus` render).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `return` inside `finally` does not compile**
- **Issue:** The plan's `finally { if (IsDisposed) return; ... }` is not valid C#.
- **Fix:** The `finally` body is wrapped in `if (!IsDisposed)`, and the post-run display runs after the try with its own `IsDisposed` check.
- **Commit:** 9a22646

**2. [Rule 2 - Robustness] Runner could hang on a disposed step**
- **Issue:** A checkpoint posted with `BeginInvoke` never runs once the handle is destroyed. The runner awaits Persist, so its run task would never complete.
- **Fix:** Pending TaskCompletionSources are tracked and released in `Dispose`. A `BeginInvoke` that throws `ObjectDisposedException` or `InvalidOperationException` completes immediately (the state is already on disk).
- **Commit:** 9a22646

**3. [Rule 3 - Blocking] The test harness lost its WinForms context**
- **Issue:** `Application.DoEvents()` swapped the manually installed `WindowsFormsSynchronizationContext` for a plain one. The run's `await` then resumed on the pool, ShowDashboard created the Start Over button handle on a pool thread, and `Dispose` hung.
- **Fix:** `GenerateCorpusStepTests.RunSta` sets `WindowsFormsSynchronizationContext.AutoInstall = false` and fails fast on `Application.ThreadException`. Production code runs under `Application.Run`, so it is not affected.
- **Commit:** 9a22646

### Other adjustments
- I added a `GatewayFactory` test seam (an internal field) because `WizardState` has no gateway injection. The plan's "injected through WizardState the way existing step tests do" does not exist for gateways.
- Test seams `CheckpointApplied` and `OpenFile` were added. `ClickStateActionAsync`, `ClickContinueAsync`, `ClickStartOverAsync` and `RequestCancel` are internal because `PerformClick` does nothing on a hidden control.
- `CanMoveNext`/`CanMoveBack` also stay false while Start Over is deleting files (`_isResetting`).
- `ShowStateCard` gained a `GenerationStateCard kind` parameter (in `StepGenerateCorpus.cs`) so the single action button knows what to do.
- I used `CorpusTestData.CreateGateway()` with `EvidenceTestData.RenderResponder` as the fake provider.

## Inspected PNGs (144 DPI)

- `wizard-shell-generate-corpus`: header "Step 7 of 8: Generate Corpus", the Not approved card centered with its Review Plan button, Next > / Back / Cancel. Nothing is clipped.
- `wizard-shell-case-summary`: header "Step 8 of 8: Case Summary" and the Finish button. Nothing is clipped.

## Verification

- `dotnet test --filter "FullyQualifiedName~GenerateCorpus"`: 23 passed (11 new step-logic tests, 8 render tests, 4 entry tests).
- `dotnet test --filter "FullyQualifiedName~WizardFlowTests|FullyQualifiedName~WizardShellRenderTests"`: 26 passed.
- `dotnet test --filter "Category!=Render"`: 2550 passed, 0 failed.
- `dotnet build`: 0 warnings.
- Acceptance greps all met:
  - `Task.Run(() => runner.RunAsync` = 1
  - `GenerationStateApplier.Apply` = 2
  - `CancelAndWaitForRun(TimeSpan timeout)` = 1
  - `MessageBoxDefaultButton.Button2` = 1
  - `if (IsDisposed)` = 4
  - `GenerationReset.StartOverAsync` = 1
  - `MailboxWriter.ResolveUnderCase` = 1
  - `MessageBoxIcon.Warning` = 1
  - WizardForm: `typeof(StepGenerateCorpus)` = 2, Generation mapping = 1, `IsGenerating: true` = 1, `CG3CloseDuringRun()` = 1
  - "of 7" in the shell tests = 0

## TDD Gate Compliance

- Task 1: RED d196eae, then GREEN 9a22646.
- Task 2: RED d9ee7bc, then GREEN e72a1b2.
- Task 3: RED 594c9b7, then GREEN 2a497c5.

## Requirements

RUN-01, RUN-02, RUN-06, RUN-07 and CORP-11 were already marked Complete in REQUIREMENTS.md. This plan delivers their in-app end-to-end behavior, so nothing in REQUIREMENTS.md changed.

## Known Stubs

None. Every button 05-14 left unwired (state-card action, Continue Generating, Retry Failed Threads, Start Over..., Cancel Generation) and the refresh timer are now live.

## Self-Check: PASSED
