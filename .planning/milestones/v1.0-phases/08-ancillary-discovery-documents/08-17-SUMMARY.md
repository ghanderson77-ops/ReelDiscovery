---
phase: 08-ancillary-discovery-documents
plan: 17
subsystem: discovery-step
tags: [discovery, winforms, run, regenerate, save-again, asvs-v12, ANC-01, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-14
    provides: "StepDiscoveryDocuments layout, seams (Confirm, KeyReady, FileExists, SaveCase), EnterRunState/ExitRunState, RefreshModel, PaintDocuments, SetStatus, SelectDocument"
  - phase: 08-15
    provides: "DiscoveryGenerator.RunAsync / SaveAgainAsync, DiscoveryProgress, DiscoveryRunResult"
  - phase: 08-16
    provides: "Step 8 of 9 in the wizard, IConfirmLeave, close-during-run safety"
provides:
  - "StepDiscoveryDocuments.Run.cs: Generate Documents / Cancel Generation, progress, outcome lines, Regenerate..., Save Again, Open in Word, Open Discovery Folder"
  - "Seams: GatewayFactory, GeneratorFactory, ShowDialogOverride, OpenFile, OpenFolder; internal RunClickAsync, RegenerateSelectedAsync, SaveAgainSelectedAsync, OpenSelectedInWord, OpenDiscoveryFolder"
  - "FileLockDetector.IsOpenElsewhere(path)"
  - "DiscoveryProgress.Done: the per-document finish report"
  - "DiscoveryUiText.OpenFolderFailed, RegenerateUnplannable"
affects: [08-18, 08-19]
tech-stack:
  added: []
  patterns:
    - "Run progress posted with BeginInvoke from a synchronous IProgress and dropped when the run version moved or the run ended"
    - "Writing rows are an overlay (key -> part) painted over the rebuilt step model; each finished document reloads the sidecar"
key-files:
  created:
    - UserControls/StepDiscoveryDocuments.Run.cs
    - ReelDiscovery.Tests/Discovery/StepDiscoveryDocumentsRunTests.cs
  modified:
    - Helpers/FileLockDetector.cs
    - Services/Discovery/DiscoveryGenerator.cs
    - UserControls/DiscoveryUiText.cs
key-decisions:
  - "08-17: DiscoveryProgress gained Done (set on the finish report) because start and finish reports were otherwise identical (Part 0 of 0)"
  - "08-17: The running status line uses the grid's short title (Deposition of {name}); the work item's ShortTitle is the document caption"
  - "08-17: Row actions are internal Task/void methods wired to Click, so tests drive them without showing the step"
requirements-completed: []
duration: 25min
completed: 2026-10-09
---

# Phase 8 Plan 17: Discovery Run Wiring Summary

The Discovery Documents step can now run. Generate Documents writes the Not generated and Failed rows through `DiscoveryGenerator`. Rows show `Writing...` while a document is written and take their outcome as each one is saved. Cancel Generation asks C-D1 first. The outcome line shows fatal, then cancelled, then problems, then clean. Regenerate... always goes through its dialog, Save Again makes no model call, and the open actions only open validated paths.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | Generate / Cancel / progress / outcomes + FileLockDetector probe | 4e10bc1 (RED), e7fd3e0 (GREEN) |
| 2 | Regenerate..., Save Again, Open in Word, Open Discovery Folder | 1ae4233 (RED), fbf57f6 (GREEN) |

## What was built

- **Run skeleton** (`RunDocumentsAsync`): the gateway is created first, and a missing model gives a Danger line before anything starts. Then `_ = Handle`, `EnterRunState(cts)`, and `Task.Run(generator.RunAsync(...))`. `catch (OperationCanceledException)` is followed by a UI-boundary `catch (Exception)`. In `finally` the CTS is disposed, the Writing overlay is cleared and `ExitRunState()` runs. After that the sidecar is reloaded, `RefreshModel()` runs and the outcome line is set.
- **Progress:** reports go through `BeginInvoke` to `ApplyProgress`. That method ignores reports from an earlier run or after the run ends. It updates the progress bar (finished/total), marks the row `Writing...` with its part, and sets the line `Writing i of t documents: {short title}, part p of q...` (Accent). After a cancel the line stays on Stopping. A `Done` report reloads the sidecar so the row shows its outcome.
- **Generate:** first commits pending input, then checks `CanGenerate`, then runs `_model.ToWrite` with no confirmation, because the cost is already on screen.
- **Cancel:** shows C-D1 through `Confirm`. Yes calls `StopRun()`, which shows the Stopping line. No leaves the run going.
- **Regenerate...:** builds the item with `DiscoveryWorklist.ForDocument`. The dialog gets the row's short title, the file name, the deposition or written settings line, the `FileLockDetector.IsOpenElsewhere` result and the estimate from `GenerationCostEstimator`. OK runs that one item with `guidance[key]`. Cancel starts nothing.
- **Save Again:** calls `DiscoveryGenerator.SaveAgainAsync`, reloads the set and the model, then shows `Saved {file}.` (Success), `StillOpen` (Warning), or the error (Danger).
- **Open in Word:** opens only when `DiscoveryStore.DocxPath` gives a path and the file exists. Errors (`Win32Exception`, `InvalidOperationException`, `IOException`) show `OpenFailed` (Danger). Double-click and Enter on a row call it too.
- **Open Discovery Folder:** opens `explorer.exe "<full Discovery path>"`, only when the folder has a .docx.

## Verification

- `StepDiscoveryDocumentsRunTests`: 12 tests. They cover clean generate (Writing row, Accent line, IsGenerating and CanMoveNext during the run), warnings, refused, fatal Auth, cancel No then Yes, the lock probe, the regenerate dialog contents (Cancel, file-open warning, OK with guidance), regenerate with no key, Save Again (still open, then saved), Open in Word (traversal name, valid path, failure), and Open Discovery Folder. They passed three runs in a row.
- `FullyQualifiedName~Tests.Discovery.|DiscoveryDocumentsRenderTests|WizardFlowTests|CorpusApprovalTests`: 544 passed.
- `ReelDiscovery.csproj` and the test project both build with `-warnaserror` (0 warnings).
- No static render state changed, so no new PNG review was needed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] DiscoveryProgress could not mark a finished document**
- **Found during:** Task 1
- **Issue:** The start report and the finish report were identical (Part 0, PartCount 0). The step could not tell when a row should move from Writing to its outcome.
- **Fix:** Added `DiscoveryProgress.Done`, set only on `Finish`'s report.
- **Files modified:** Services/Discovery/DiscoveryGenerator.cs
- **Commit:** e7fd3e0

**2. [Rule 1 - Bug] Running line showed the document caption, not the grid title**
- **Found during:** Task 1 (GREEN)
- **Issue:** `DiscoveryWorkItem.ShortTitle` holds the caption ("DEPOSITION OF ..."). UI-SPEC wants the row's short title.
- **Fix:** Titles now come from `_model.Rows`, falling back to the item title.
- **Commit:** e7fd3e0

**3. [Rule 2 - Missing copy] Two error lines had no UI-SPEC text**
- **Issue:** A failed Explorer launch and a Regenerate... on a document the current guide can no longer plan had no copy.
- **Fix:** Added `DiscoveryUiText.OpenFolderFailed(message)` and `RegenerateUnplannable(shortTitle)`, following the existing "Could not open ..." pattern and tone.
- **Commit:** fbf57f6

**4. Seams moved earlier:** `OpenFile`, `OpenFolder` and `ShowDialogOverride` were added in Task 1 (the plan's Task 1 action lists them) because the shared test fixture sets them.

## Threat Flags

None. Every surface is covered by T-08-50..53: open paths come only from `DocxPath`, the folder opens through explorer.exe with a quoted constant path, regenerate only runs through the dialog, and progress is marshalled with a disposed/version guard.

## For later plans

- 08-18 (Case Summary) can rely on the sidecar the run writes. `DiscoveryStepModel.SavedRows` gives the same row states.
- Visibility of the Regenerate.../Save Again slot and the header Open Discovery Folder button is still painted by 08-14's `PaintSelection` and `PaintHeaderButton`. The run code calls them through `RefreshModel` and `ExitRunState`.

## Self-Check: PASSED

- FOUND: UserControls/StepDiscoveryDocuments.Run.cs
- FOUND: ReelDiscovery.Tests/Discovery/StepDiscoveryDocumentsRunTests.cs
- FOUND commits: 4e10bc1, e7fd3e0, 1ae4233, fbf57f6
