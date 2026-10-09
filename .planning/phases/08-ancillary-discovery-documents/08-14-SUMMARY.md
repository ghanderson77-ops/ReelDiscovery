---
phase: 08-ancillary-discovery-documents
plan: 14
subsystem: discovery-step-ui
tags: [discovery, winforms, wizard-step, dpi, render, ANC-01, ANC-02, ANC-03, ANC-04]
requires:
  - phase: 08-05
    provides: "CorpusDetector.Detect (usable-corpus probe, never throws)"
  - phase: 08-09
    provides: "DiscoveryUiText (all S1 copy), DiscoveryRowInfo"
  - phase: 08-10
    provides: "DiscoveryStepModel.Build / ApplyDefaults / TryParsePages / TryParseCount / CountHint / Condition / CanGenerate"
provides:
  - "StepDiscoveryDocuments: three-card layout (or the no-guide state card), binding to CaseDraft.Discovery, IWizardStep lifecycle"
  - "Seams for 08-17: partial void WireRunHandlers(), Confirm, SaveCase, KeyReady, FileExists, EnterRunState(cts)/ExitRunState(), RefreshModel(), PaintDocuments(), PaintRunChrome(), SetStatus(), SelectDocument(key)"
  - "Shell hooks: IsGenerating, ConfirmLeave() (C-D1 copy), CancelAndWaitForRun(TimeSpan)"
  - "LoadForRender(state, DiscoveryRenderState, model, status, tone) + internal accessors for render tests"
affects: [08-15, 08-16, 08-17, 08-18]
tech-stack:
  added: []
  patterns:
    - "Paint first, probe later: OnEnterStepAsync paints from the draft/sidecar, then awaits Task.Run(CorpusDetector.Detect) and drops the result when _enterVersion moved (leave/re-enter), the case changed or a run started"
    - "Input values are populated once on enter; RefreshModel repaints hints, rows and gates without touching what the user typed, so an invalid field keeps its text while settings keep the last valid value"
    - "Full-bleed cards for all three columns (grids span the card, other rows inset 15) so captions and cell text share Content x = 16"
key-files:
  created:
    - UserControls/StepDiscoveryDocuments.cs
    - ReelDiscovery.Tests/Discovery/StepDiscoveryDocumentsTests.cs
    - ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs
  modified:
    - .planning/phases/08-ancillary-discovery-documents/08-UI-SPEC.md
key-decisions:
  - "08-14: While a run is active the status line wraps to two lines and the estimate line is hidden, so the part counter is visible; outcome lines stay one line with ellipsis + tooltip (UI-SPEC S1 Card 3 amended)"
  - "08-14: EnterRunState(cts)/ExitRunState() own the run chrome (inputs locked, Cancel Generation in the run slot, Back/Next off); 08-17's run skeleton calls them"
  - "08-14: Input changes write CaseDraft.Discovery, set UserTouched and queue a background case save; ValidateStepAsync commits pending fields, saves and always returns true"
  - "08-14: The witness grid moves the 15 px text inset from the grid default onto its text columns, because a column Padding.Empty inherits it and hid the 28-wide check boxes"
  - "08-14: Pages validity is required only when eligible witnesses exist; a count field blocks Generate only while its set is visible and checked"
patterns-established:
  - "Render seam for invalid states applies the field values itself (pages 5, RFP 99, same party, refused 7th witness) with _renderOnly suppressing saves and timers"
requirements-completed: []
duration: 20min
completed: 2026-10-08
---

# Phase 8 Plan 14: Discovery Documents step layout, binding and render checks Summary

The Discovery Documents step now exists as a three-card WinForms step (Depositions, Written discovery, Documents) or a no-guide state card. It binds to `CaseDraft.Discovery` and the pure `DiscoveryStepModel`, validates its fields, never blocks Next, and gives the shell and the 08-17 run code their hooks. Every static state renders cleanly at 144 DPI at Large and WizardMin.

## Performance

- **Duration:** about 20 min
- **Completed:** 2026-10-08
- **Tasks:** 3
- **Files:** 3 created, 1 modified

## Accomplishments

- **Layout (Task 1).** Built in code from `Theme`, `CardPanel`, `GridStyle`, `TextFieldHost` and `ButtonHelper`. All sizes go through `LogicalToDeviceUnits`, there is no `new Font(...)`, and only Theme colors are used.
  - Root rows: header, notice line, body.
  - Card 1: witness grid (check, Witness Fill min 96, Side, Facts) with cell tooltips, a no-witness text, and the pages field (64 wide) with its hint.
  - Card 2: RFP/ROG/RFA rows (checkbox plus a 64-wide count), counts hint, memo note, and Served by / Served on pickers in a private copy of `HostCombo` that widens the drop-down to the longest item. Below them, the party hint.
  - Card 3: documents grid with status dots, empty text, the detail line, Open in Word / Regenerate... / Save Again (one shared slot), a divider, the estimate, the status line, the progress bar, and the run button (Primary Generate Documents or Danger Cancel Generation).
  - `partial void WireRunHandlers()` is declared and called after `WireHandlers()`.
- **Binding and lifecycle (Task 2).**
  - `OnEnterStepAsync` runs `ApplyDefaults`, loads the sidecar, paints at once, then runs `CorpusDetector.Detect` with `Task.Run` and discards the result if the step was left (`_enterVersion`), the case changed or a run started.
  - Witness checks are capped at 6. A refused 7th check shows the Warning cap hint for 3 s.
  - Text fields use a 250 ms debounce. Only `TryParsePages` / `TryParseCount` values are written (T-08-40), and invalid fields turn their host and hint Danger.
  - Checkbox and party changes write the settings, set `UserTouched`, refresh the model and queue a case save.
  - `ValidateStepAsync` commits, saves through the `SaveCase` seam (shows `SaveFailedText` in Danger on failure) and returns true.
  - Shell hooks: `ConfirmLeave()` asks C-D1 copy through `Confirm` and cancels on Yes. `CancelAndWaitForRun` cancels and pumps until the run unwinds.
- **Render checks (Task 3).** `DiscoveryDocumentsRenderTests` has 7 facts (render checks 1-7, with check 4 rendered twice: Warnings selected and Not saved selected). Each runs at Large and WizardMin. They assert:
  - the clipping detector finds nothing
  - no horizontal grid scrollbar appears
  - at most one Primary button is visible, and the expected count per state
  - no ellipsized button or checkbox text
  - at least 3 visible grid rows
  - Side and Status texts fit
  - the Witness and Document minimum widths hold

## Task Commits

1. **Task 1:** `de7057b` feat(08-14): Discovery Documents step layout with three cards and render seam
2. **Task 2 RED:** `aa6c8d0` test(08-14): add failing Discovery Documents step binding and lifecycle tests
3. **Task 2 GREEN:** `e47a6d4` feat(08-14): bind Discovery Documents step to settings with run-state hooks
4. **Task 3:** `3ee24cd` test(08-14): 144-DPI render checks 1-7 for the Discovery Documents step (plus two render-found fixes and the UI-SPEC amendment)

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors.
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors.
- `FullyQualifiedName~StepDiscoveryDocumentsTests`: 18 pass.
- `Category=Render&FullyQualifiedName~DiscoveryDocumentsRenderTests`: 7 pass.
- `FullyQualifiedName~Tests.Discovery.`: 467 pass.
- Acceptance greps:
  - `partial void WireRunHandlers();` 1
  - `new Font(` 0
  - forbidden colors 0
  - `// 0: Header` 1
  - `// 2: Body` 1
  - `GridStyle.AddDotColumn` 1
  - `internal void LoadForRender` 1
  - `public bool CanMoveNext => !_isGenerating;` 1
  - `UserTouched = true` 4
  - `DiscoveryStepModel.Build` 1
  - `Task.Run(() => CorpusDetector.Detect` 1
  - `public void CancelAndWaitForRun(TimeSpan` 1
  - `public bool ConfirmLeave()` 1

## 144-DPI PNG Inspection (D-03)

I inspected these files in `obj/gsd-artifacts/bin/ReelDiscovery.Tests/debug/renders/` by eye at 144 DPI:

- `discovery-ready-1100x750.png`, `discovery-ready-1000x540.png`
  - The three cards sit side by side with no horizontal scrollbar.
  - The two default witnesses are checked, and the check boxes are centered after the fix.
  - At WizardMin the Witness column ellipsizes "Raymond Har..." (rule 35 allows it; the tooltip has the full name).
  - At WizardMin the Documents grid shows 4 rows plus a vertical scrollbar, and "Deposition of Raymon..." ellipsizes, which rule 36 allows.
  - The combo text lines up with the fields' inner edge.
  - Generate Documents is the only Primary button and has focus.
- `discovery-running-1100x750.png`, `discovery-running-1000x540.png`
  - Two rows show "Writing..." with Accent dots.
  - The Accent status line now reads in full over two lines ("... Deposition of Raymond Harmon, part 2 of 3...").
  - The progress bar and the Danger Cancel Generation button are visible.
  - Inputs are disabled, and the pickers show their read-only text.
  - The estimate is hidden.
- `discovery-results-1100x750.png`, `discovery-results-1000x540.png`
  - Dots are Generated green, Warnings and Out of date amber, Failed red, Not saved amber.
  - The selected Warnings row has an amber detail line, `$4.37 spent` shows, and the outcome line is amber.
  - Open in Word and Regenerate... are enabled.
  - At WizardMin the grid shows 3 full rows.
- `discovery-results-not-saved-1100x750.png`, `discovery-results-not-saved-1000x540.png`: Save Again replaces Regenerate... in the same slot, and Open in Word is disabled.
- `discovery-memo-criminal-1100x750.png`, `discovery-memo-criminal-1000x540.png`:
  - The ROG and RFA rows are hidden, and the memo note shows.
  - The caption reads "Sworn witness interviews", the pickers read "Requested by" / "Directed to", and the notice says "interview exhibits".
- `discovery-invalid-1100x750.png`, `discovery-invalid-1000x540.png`:
  - Pages 5 and RFP 99 have red borders and Danger hints, and "Choose two different parties." shows in Danger.
  - The amber "Up to 6 witnesses per run" cap hint shows.
  - Generate Documents is disabled.
  - The red border does not shift any row.
- `discovery-no-witnesses-1100x750.png`, `discovery-no-witnesses-1000x540.png`: the no-witness text replaces the grid, the pages row is disabled, and the grid lists 3 written-set rows.
- `discovery-no-guide-1100x750.png`, `discovery-no-guide-1000x540.png`: the state card is centered and the notice line is hidden.

**Verdict:** clean in every state after the two fixes below. No text or button is clipped. The ellipses that remain are the AutoEllipsis cells and lines the UI-SPEC allows, and each has a tooltip.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Witness check boxes were invisible**
- **Found during:** Task 3 PNG inspection
- **Issue:** a column `Padding.Empty` means "inherit", so the 28-wide check cell took the grid's 15 px text inset, and the box was clipped away.
- **Fix:** the grid default padding is now empty, and the Witness and Side columns carry the 15 px inset. A render assertion checks the check cell's inherited padding.
- **Files modified:** UserControls/StepDiscoveryDocuments.cs, ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs
- **Commit:** 3ee24cd

**2. [UI-SPEC amendment] Running status line cut its part counter**
- **Found during:** Task 3 PNG inspection
- **Issue:** a one-line AutoEllipsis status in a third of the page lost ", part 2 of 3..." at both Large and WizardMin, so render check 3's part counter was never on screen.
- **Fix:** while running, the status line wraps to two lines and the estimate line is hidden (it is not actionable during a run). Outcome lines stay one line so the Documents grid keeps 3 rows at WizardMin (I tried two-line outcomes first, and the grid fell to 2 rows). I amended 08-UI-SPEC S1 Card 3 rows 5-6 and the status-line notes.
- **Commit:** 3ee24cd

**3. [Rule 2 - Missing functionality] Run-state hooks and test accessors**
- `EnterRunState(cts)` / `ExitRunState()` hold the run chrome, so ConfirmLeave/CancelAndWaitForRun can be tested now and 08-17 has one place to switch chrome.
- `SelectDocument(key)`, `Model`, `KindRowsShown` and the other internal accessors exist for tests.
- `Dispose` releases the timers and the tooltip. Plan 08-17 must not add a second `Dispose` override.

**4. [Rule 2] Background case save on input change**
- The UI-SPEC says every input change writes `CaseDraft.Discovery` and the case is saved through the existing path. The plan's handler list did not name the save, so input changes queue a non-blocking `SaveCase()`. A failure shows `SaveFailedText` in Danger. Render mode never saves.

## TDD Gate Compliance

Task 2 is `tdd="true"`. Task 1's layout commit (de7057b) already contained most of the lifecycle code, because the plan's Task 1 seam and accessors depend on it. The RED commit (aa6c8d0) was confirmed failing to compile without the Task 2 additions (`EnterRunState`, `ExitRunState`, `KindRowsShown`). The GREEN commit (e47a6d4) followed. So RED preceded GREEN for the Task 2 delta. Most of the binding behavior, however, was written before its tests.

## Known Stubs

- **Run buttons are not wired:** `Generate Documents`, `Cancel Generation`, `Regenerate...`, `Save Again`, `Open in Word` and `Open Discovery Folder` have no Click handlers yet. This is intentional: Plan 08-17 implements `WireRunHandlers()` in `StepDiscoveryDocuments.Run.cs`.
- **The step is not in the wizard yet:** it is not registered in `WizardForm.StepTypes`. That is a later plan (shell wiring, S3 C-D2).

## Threat Flags

None. T-08-40 is mitigated (only parsed values are written; the cap is enforced in the UI and in Normalize), T-08-41 too (`UseMnemonic = false` on every label, checkbox and combo-host label that shows names or titles), and T-08-42 too (the corpus probe runs on `Task.Run` after the first paint; a stale result is discarded).

## Self-Check: PASSED
- FOUND: UserControls/StepDiscoveryDocuments.cs
- FOUND: ReelDiscovery.Tests/Discovery/StepDiscoveryDocumentsTests.cs
- FOUND: ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs
- FOUND: de7057b, aa6c8d0, e47a6d4, 3ee24cd
