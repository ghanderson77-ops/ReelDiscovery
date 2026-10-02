---
phase: 03-case-guide
plan: 19
subsystem: case-guide-ui
tags: [winforms, wizard, reopen, recent-cases, render, dpi, theme]
requires:
  - "03-04: CaseStore (GetRecentCases, Load, CasesRoot, CanSave, SaveNow), RecentCase, CaseLoadResult"
  - "03-06: CaseGuideUiText (RecentItemSecondLine, OpenedCaseText, NewCaseText, C8OpenOverUnsaved, SaveFailedText)"
  - "03-18: WizardForm.NavigateToStage / StepTypeForStage, PendingNotice handling on the landing steps"
provides:
  - "Describe the Case: Recent cases card (max 8, owner-drawn, hover, open-case selection, path tooltip)"
  - "Open Case... (OpenFileDialog) and single click / Enter on a recent item -> OpenCase(path, title)"
  - "New Case (visible only when the current case has a folder), no prompt"
  - "StepCaseScenario.NeedsOpenConfirmation, RecentEmptyText, LoadInputs, LoadForRender, OpenCase, StartNewCase"
  - "RenderCollection: all render test classes run serially"
affects: [03-20]
tech-stack:
  added: []
  patterns:
    - "Open flow: C8 (or flush + SaveNow of a saved case) -> CaseStore.Load -> replace draft -> LoadInputs -> PendingNotice -> NavigateToStage"
    - "A spanned card never spans an AutoSize row: group the cells it sits beside into one nested TLP"
key-files:
  created:
    - ReelDiscovery.Tests/Render/CaseScenarioRenderTests.cs
    - ReelDiscovery.Tests/Render/RenderCollection.cs
  modified:
    - UserControls/StepCaseScenario.cs
    - ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs
    - ReelDiscovery.Tests/Render/BaselineRenderTests.cs
    - "ReelDiscovery.Tests/Render/*RenderTests.cs (Collection attribute only)"
decisions:
  - "[03-19]: The Recent cases card sits beside the scenario card only (top-aligned with it, ending with the hint row). The scenario card and hint share one nested TLP cell, so the card spans no AutoSize row. This replaces the planned RowSpan 3 from the caption row"
  - "[03-19]: Clicking the case that is already open does not reload it; it continues at its stage, so edits on this step are not dropped"
  - "[03-19]: Opening another case or starting a new one first flushes this step's inputs into a saved current case and calls SaveNow; a failed save shows SaveFailedText and cancels the switch. C8 only applies to an unsaved typed scenario"
  - "[03-19]: OpenCase always goes through NavigateToStage when hosted in the WizardForm; a Described stage maps back to this step, which shows the Opened notice on re-entry. Without a WizardForm (render tests) the notice is shown in place"
  - "[03-19]: Render test classes join one non-parallel xUnit collection. Parallel STA renders produced intermittent one-line clipping findings on several screens (Issues tab, Case Guide, Summary, Shell). The render suite takes about 2m40s instead of about 36s but is stable"
metrics:
  duration: 35min
  completed: 2026-10-01
  tasks: 2
  files: 16
---

# Phase 3 Plan 19: Describe the Case Reopen Support Summary

Describe the Case can now reopen saved cases (D-11). A Recent cases card beside the scenario lists up to 8 cases, newest first. Each row shows the title and "{stage} · opened {date}", or "open now" for the current case. A single click or Enter opens a case, and **Open Case...** opens any case.json from disk. Loading refreshes this step's inputs before it jumps to the furthest stage reached, which avoids Pitfall 5. Load errors show in red (Danger) and the list refreshes. **New Case** starts a fresh draft without a prompt. Every color and font on this step is now a Theme token.

## What was built

- **Layout:**
  - The root TLP has two columns: Percent 100 | `LogicalToDeviceUnits(296)`.
  - Row 1 holds a nested scenario column (the scenario card, then the hint and counter) next to the Recent cases card, which has a left margin of 16.
  - The complexity, document/court and status rows span both columns.
- **Recent cases card** (`CardPanel`, Padding 1). It has four rows:
  - a "Recent cases" header in LabelFont with 15/14/15/10 padding
  - an owner-drawn `ListBox` (`OwnerDrawFixed`, ItemHeight 52, no border, `IntegralHeight = false`, hand cursor) or the empty-state label
  - a 1 px divider
  - a footer TLP (15/12/15/12 padding) with New Case and Open Case..., right-aligned, 34 high, 8 apart
- **Drawing each row:**
  - Line 1 is the title in BodyFont: Text color, or Accent for the open case, with end-ellipsis.
  - Line 2 is `RecentItemSecondLine` in SmallFont, TextMuted.
  - Both lines are inset 15 and centered vertically as a pair.
  - Hover and keyboard selection use SurfaceMuted. The open case uses AccentSoft with a 3 px Accent bar.
  - The tooltip shows the case folder path.
- **Open flow (`OpenCase(path, title)`):**
  - If a typed scenario has never been saved, C8 asks first (Yes/No, Warning, No as the default). If the current case is saved, its inputs are flushed and `SaveNow` runs instead.
  - Then `CaseStore.Load` runs:
    - On failure, `result.Message` shows in Danger and the list refreshes.
    - On success, the draft is replaced, `LoadInputs` refreshes the inputs, and `PendingNotice = OpenedCaseText(title)` is set before `NavigateToStage(Stage)`.
- **Open Case...** uses an `OpenFileDialog` with the exact title and filter from the spec. It starts in the Cases root when that folder exists.
- **New Case** flushes and saves the current case, replaces the draft with `new CaseDraft()`, reloads the inputs and shows `NewCaseText(previous title)` in Success.
- **`LoadInputs`** is the former `OnEnterStepAsync` body. Enum indexes are clamped, because a hand-edited case.json could hold out-of-range values. `OnEnterStepAsync` now calls `LoadInputs`, refreshes the list, and shows and then clears `PendingNotice`.
- **Restyle:**
  - Colors: `Color.DimGray`, `Green`, `DarkOrange` and `Red` and `Color.FromArgb(0,120,215)` became `Theme.TextMuted`, `Success`, `Warning`, `Danger` and `Accent`.
  - Fonts: every `new Font(...)` became `Theme.SmallFont/BodyFont/LabelFont`. The scenario box stays on `EditorFont`.
  - Link colors now use Accent.
  - All handlers are wired after every control exists.

## Verification

- `CaseScenarioUiLogicTests` + `WizardFlowTests`: 45/45 pass (NeedsOpenConfirmation in 5 cases, RecentEmptyText, and CounterColor checked against the Theme tokens).
- Full suite without renders: 1108/1108 pass. The render suite (`Category=Render`): 53/53, 6 of 6 serial runs green.
- App and test builds have 0 warnings.
- Plan grep checks:
  - `"Recent cases"` 1, the empty text 1, the filter 1, `NavigateToStage` 1, `LogicalToDeviceUnits(296)` 1, off-palette/`new Font(` 0.
  - `LoadInputs(_state.Case)` comes before `NavigateToStage` inside `OpenCase`.

## Render inspection (display at 120 DPI = 125%, DeviceDpi >= MinimumDpi; every PNG opened)

- `case-scenario-eight-recent-1100x750`:
  - The card's top edge matches the scenario card's (y 64) and its bottom matches the hint row's.
  - The list and header text sit on the card's Content x.
  - All 8 two-line rows are centered vertically, and long titles end in an ellipsis.
  - The open case (Ironside v. Marlowe) shows AccentSoft, a 3 px Accent bar, an Accent title and "Complete · open now".
  - The footer buttons are right-aligned at the 15 inset, 34 logical px high, 8 apart.
  - The combo and status rows span the full width, and nothing is off the palette.
- `case-scenario-eight-recent-900x600`: the list scrolls vertically inside the card, with no horizontal scrollbar. The hint wraps to two lines, and the card still ends with the hint row.
- `case-scenario-no-recent-1100x750` / `-900x600`: the empty text wraps in TextMuted at the header's x. Only Open Case... shows, because a draft with no folder hides New Case.
- `case-scenario-load-error-1100x750` / `-900x600`:
  - The missing-folder message shows in Danger and wraps onto two lines at 900x600.
  - The pruned list shows the one remaining case.
- Clipping: none in any of the six. I added the Small load-error render beyond the plan to prove the long path wraps.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Layout] Recent cases card placement and the spanned AutoSize row**
- **Found during:** Task 2 (renders)
- **Issue 1:** With `SetRowSpan(card, 3)` starting at row 0, the card started at the caption row, above the scenario card. The plan's render check requires it to top-align with the scenario card.
- **Issue 2:** Spanning the card into the AutoSize hint row sometimes left that row one line tall after the hint wrapped (a clipping finding at 900x600).
- **Fix:** The scenario card and hint share one nested TLP in root row 1, and the card sits in row 1, column 1, with no span. The root now has 8 rows, and the row-index comments are updated.
- **Commit:** 39c1316

**2. [Rule 1 - Layout] Footer buttons not at the right inset**
- **Issue:** A right-to-left `FlowLayoutPanel` left the buttons about 20 px short of the 15 inset.
- **Fix:** I replaced it with a 3-column TLP (spacer | New Case | Open Case...) with the buttons anchored right.
- **Commit:** 39c1316

**3. [Rule 2 - Data safety] Flush and save the current case before switching**
- Opening another case or starting a new one now stores this step's inputs into a saved current case and calls `SaveNow` first. A failed save shows `SaveFailedText` and cancels the switch. Clicking the case that is already open does not reload it from disk.
- **Commit:** f6eac94

**4. [Rule 2 - Test isolation] The baseline scenario render read the user's recent list**
- `OnEnterStepAsync` now reads the recent list. `BaselineRenderTests.StepCaseScenario_Baseline` used a default `WizardState` (the real `%LOCALAPPDATA%` store), so it now injects a temp `CaseStore`.
- **Commit:** f6eac94

**5. [Rule 3 - Blocking] The render suite failed intermittently**
- **Issue:** On about half of the full `Category=Render` runs, one random test (Issues tab, Case Guide, Summary, Shell, or this step) reported a one-line clipping finding. The same test passed when rerun.
- **Fix:** `RenderCollection` (`DisableParallelization = true`) now covers all 12 render classes.
- **Result:** Serial runs are stable but take about 2m40s instead of about 36s.
- **Commit:** 39c1316

## Known Stubs

None.

## Deferred Issues

- The document, court and state combo widths (180/120/160) are still unscaled device pixels from Phase 2. They look fine at 125% and nothing clips, but they do not grow with DPI.

## Threat Flags

None. The plan's register covers all three threats:
- T-03-62: Load has a size cap and a version check and never throws. Errors show in Danger.
- T-03-63: Recent and dialog paths are only passed to `CaseStore.Load`. They are never executed or opened with the shell.
- T-03-64: C8 asks first, with No as the default. A saved case is flushed with SaveNow before a switch.

## TDD Gate Compliance

- RED: 64648c9 `test(03-19)`. It failed to compile because `NeedsOpenConfirmation` and `RecentEmptyText` did not exist yet, and the CounterColor tests expected Theme tokens.
- GREEN: f6eac94 `feat(03-19)`. 45/45 targeted tests pass.

## Self-Check: PASSED

- Files: StepCaseScenario.cs, CaseScenarioRenderTests.cs, RenderCollection.cs, CaseScenarioUiLogicTests.cs, this SUMMARY: all present
- Commits: 64648c9, f6eac94, 39c1316: all present
- Renders: 6 case-scenario-*.png present (5 planned + the Small load-error render)
