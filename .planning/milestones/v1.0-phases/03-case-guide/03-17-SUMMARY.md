---
phase: 03-case-guide
plan: 17
subsystem: case-guide-ui
tags: [winforms, ui, export, docx, case-folder, render, dpi]
requires:
  - "03-04: CaseStore (EnsureFolder, SaveAsync, SaveNow, LastSavedUtc, LastSaveError, MoveFolder, WriteAtomicAsync), CaseFolderNaming.CaseTitle"
  - "03-05: CaseExportKind, CaseGuideDocxExporter (Build, FileNameFor, DocumentNoun)"
  - "03-06: CaseGuideUiText (DisplayPath, SaveLine, LastSavedLine, ExportLine, Summary*Line, Exporting/Exported/ExportFileOpen/ExportFailed/MoveFailed/Moved/SaveFailed text, C7MoveFolder)"
  - "03-08: CaseGuideValidator (Validate, CountErrors, CountWarnings)"
  - "03-02: Theme.DrawStatusDot, CardPanel, ButtonHelper, RenderHarness, ClippingDetector"
provides:
  - "StepCaseSummary (IWizardStep, title 'Case Summary', NextButtonText 'Finish')"
  - "Static line helpers: DocumentTypeLabel, CoreDocumentLine, CastLine, FactsLine, ValuesLine, ChecksLine, ExportedAt"
  - "internal LoadForRender(WizardState) for render tests"
  - "CaseSummaryLogicTests, CaseSummaryRenderTests (4 PNGs: case-summary-*)"
affects: [03-18]
tech-stack:
  added: []
  patterns:
    - "Contents rows: TLP AutoSize | Percent 100 | AutoSize with row padding (15, 12, 15, 12) inside a Padding = 1 card; the dot is an owner-painted 18 px panel that calls Theme.DrawStatusDot with the cell shifted left by LDU(15) so the dot sits at Content x"
    - "Rows live in an AutoScroll panel (Dock Top AutoSize TLP) so the 900x600 window scrolls instead of clipping"
    - "Data-bearing labels use UseMnemonic = false (case titles, paths and 'Key Facts & Events' contain '&')"
key-files:
  created:
    - UserControls/StepCaseSummary.cs
    - ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
  modified: []
decisions:
  - "[03-17]: CanMoveNext is !_busy (not always true): Finish is disabled while an export or folder move runs, matching Back"
  - "[03-17]: Export All stops at the first failure (reported in Danger, row dot Danger) and keeps the documents already written"
  - "[03-17]: An open-in-Word export is detected by sharing/lock-violation HResults, or by UnauthorizedAccessException when a FileShare.None probe of the existing target fails with IOException"
  - "[03-17]: Last saved falls back to case.json's write time when this session has not saved yet (opened case, render tests)"
  - "[03-17]: Move Case Folder runs CaseStore.MoveFolder on the pool (cross-volume copy) with every action disabled, then saves and reports MovedText"
  - "[03-17]: FactsLine uses the min/max parseable fact dates, so undated facts do not break the range"
metrics:
  duration: "~35 min"
  completed: 2026-10-01
  tasks: 2
  files: 3
---

# Phase 3 Plan 17: Case Summary Step Summary

The Case Summary step shows the case folder (display path with full-path tooltip, last saved time) and a Contents card with six status-dot rows. From it the user can export the core document, cast and key facts to .docx, one at a time or all together. Exports are written atomically into the case folder. The step also offers Open in Word, Open Case Folder (explorer.exe on a verified folder) and Move Case Folder with C7 confirmation. Finish saves the case and closes with no prompt.

## What was built

- **Layout (S2):** header (case title, subtitle, `Export All to Word`, which is the only Primary button); case folder card; Contents card with Case file, Core document, Cast of Key Characters, Key Facts & Events, Canonical values and Guide checks rows; a status strip; the closing hint. Root rows carry `// 0:`..`// 4:` comments.
- **Line helpers:** pure static functions built on CaseGuideUiText. `ExportedAt` reads the file's last write time.
- **Actions:** `ExportAsync(kind)` calls EnsureFolder, then Build (on the UI thread, so the snapshot is consistent), then WriteAtomicAsync, and refreshes the row. An open file produces `ExportFileOpenText`. Any other failure produces `ExportFailedText` and sets the row dot to Danger. Open in Word builds the path from the verified folder plus a fixed file name. Open Case Folder starts quoted `explorer.exe` only after `Directory.Exists`. Move uses FolderBrowserDialog, then C7 (default No), then MoveFolder, then a save, then MovedText.
- **Lifecycle:** entering the step shows and clears `PendingNotice`, calls `AdvanceStage(CaseStage.Summary)`, saves, and reports any save failure through SaveLine and `SaveFailedText`. Finish (ValidateStepAsync) flushes the save and returns true. The step does not prompt (D-10).

## Render review (opened every PNG)

- `case-summary-before-export-1100x750`: `Export All to Word` is the only Primary button. The dots, the `Contents` caption and the `Case folder` caption all sit on Content x. Row buttons and folder-card buttons share the same right edge (card edge minus 16). All buttons are 34 high and show their full text. Dots are grey (not exported), green (saved/values) and amber (8 fixture warnings).
- `case-summary-before-export-900x600`: the long temp folder path ends in an ellipsis rather than wrapping. The contents rows scroll vertically with no horizontal scrollbar. The clipping detector reports nothing.
- `case-summary-after-export-1100x750`: all three rows show green `Exported {time} · {file}`, `Open in Word` and `Export Again`.
- `case-summary-with-errors-1100x750`: Guide checks shows a red `1 error, 8 warnings` line and a red dot.
- Fixed during review: the `&` in "Key Facts & Events" was being treated as a mnemonic and rendered as "Key Facts  Events". UseMnemonic is now off on every data label.
- Noted, not changed: the square inner corners of the Padding = 1 card come from the child surface painting over the radius. The accepted StepCoreDocument reference has the same corners, so this step stays consistent with it.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Ampersand mnemonic in row titles and paths**
- **Found during:** Task 2 render review
- **Issue:** WinForms Label treated `&` as a mnemonic prefix, so the title rendered as "Key Facts  Events". Case titles and paths with `&` were affected the same way.
- **Fix:** set `UseMnemonic = false` on every data label.
- **Commit:** 9867788

**2. [Rule 2 - Correctness] Finish disabled while busy**
- The plan said `CanMoveNext => true`. The step uses `!_busy` so the user cannot click Finish mid-export or mid-move, and `ValidateStepAsync` also returns false while busy.

**3. [Rule 2 - Robustness] Rows scroll when the window is short**
- The six rows do not fit at 900x600. The rows are hosted in an AutoScroll panel; the card header stays fixed.

## TDD Gate Compliance

- RED: `1a71e55 test(03-17): add failing tests for Case Summary line helpers` (failed to compile: StepCaseSummary missing)
- GREEN: `3c39e62 feat(03-17): add Case Summary layout, contents rows and line helpers` (13/13 pass)

## Verification

- `dotnet test --filter FullyQualifiedName~CaseSummaryLogicTests`: 13 passed
- `dotnet test --filter "Category=Render&FullyQualifiedName~CaseSummaryRenderTests"`: 3 passed, 4 PNGs, clipping empty
- `dotnet test --filter "Category!=Render"`: 959 passed
- `dotnet build`: 0 warnings, 0 errors
- No off-theme colors or `new Font(` in StepCaseSummary.cs

## Known Stubs

None. WizardForm wiring of the step belongs to 03-18 by design.

## Commits

- 1a71e55 test(03-17): add failing tests for Case Summary line helpers
- 3c39e62 feat(03-17): add Case Summary layout, contents rows and line helpers
- 9867788 feat(03-17): add Case Summary export, open, move actions and 144-DPI renders

## Self-Check: PASSED
