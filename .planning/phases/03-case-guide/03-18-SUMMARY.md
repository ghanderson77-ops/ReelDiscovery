---
phase: 03-case-guide
plan: 18
subsystem: case-guide-ui
tags: [winforms, wizard, autosave, close-behavior, render, dpi]
requires:
  - "03-04: CaseStore (CanSave, SaveAsync, SaveNow, LastSaveError, LastSavedUtc), WizardState.CaseStore / PendingNotice"
  - "03-06: CaseGuideUiText (C9RegenerateDocumentWithGuide, C10CloseAfterFailedSave, C11CloseUnsavedScenario, C12CloseDuringRun, SaveFailedText)"
  - "03-02: GridStyle.Apply, Theme.DrawStatusDot, RenderHarness, ClippingDetector"
  - "03-16: StepCaseGuide (IsGenerating); 03-17: StepCaseSummary"
provides:
  - "5-step case flow: API Configuration, Describe the Case, Review Core Document, Case Guide, Case Summary"
  - "WizardForm.StepTypeForStage / NavigateToStage (by step type, unknown stage -> scenario)"
  - "WizardForm.DecideClose + save-on-close (C10/C11/C12), Cancel defers to OnFormClosing in the case flow"
  - "Default window 1150x900 logical client, minimum 1000x700, both clamped to the working area"
  - "WizardForm.CreateForRender / ShowStepForRender (test-only)"
  - "StepCoreDocument: Next >, guide notice, C9 copy, autosave, AdvanceStage, PendingNotice, per-case reset, Content x, Theme colors, IsGenerating"
affects: [03-19, 03-20]
tech-stack:
  added: []
  patterns:
    - "Close path: flush current step (sync part of OnLeaveStepAsync) -> CaseStore.SaveNow -> DecideClose -> prompt only for run / unsaved scenario / failed save"
    - "Stage navigation maps CaseStage to a step Type and finds its index at runtime"
key-files:
  created:
    - ReelDiscovery.Tests/Render/CoreDocumentRenderTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
  modified:
    - Forms/WizardForm.cs
    - UserControls/StepCoreDocument.cs
    - ReelDiscovery.Tests/Case/WizardFlowTests.cs
    - ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs
decisions:
  - "[03-18]: The wizard opens at a 1150x900 logical client (not 1100x750): the shell's header band, nav bar and footer take about 48x184 logical, and at a 1100x750 window the Case Guide folded its Checks list. 1150x900 gives the step about 1100x716. Minimum client is 1000x700; both are clamped to the screen's working area"
  - "[03-18]: DecideClose: legacy -> close; active run -> C12 first; no core document and no folder -> close when the scenario is blank, else C11; otherwise close when the save succeeded, else C10"
  - "[03-18]: After C12 Yes the running step is disposed (cancels its token), the case is saved again and C10 applies if that save fails"
  - "[03-18]: Close prompts run only for CloseReason.UserClosing; other close reasons (shutdown) still flush and save, without prompting"
  - "[03-18]: StepCoreDocument.SaveAsync advances the stage to CoreDocument before saving, so the first save after the first generation records the stage"
  - "[03-18]: Core-document autosave during a run fires when the section count or ready count changes (outline written, each section finished), not on every progress message"
  - "[03-18]: CreateForRender sets _closeConfirmed so a programmatic Close in render tests never shows a dialog"
metrics:
  duration: 35min
  completed: 2026-10-01
  tasks: 3
  files: 6
---

# Phase 3 Plan 18: Wizard Shell Case Flow and Review Core Document Summary

The case flow now runs all five steps, and the app saves on close. Every close path (Cancel, the window X, Finish) brings the current step's edits into the case and calls `CaseStore.SaveNow`. A prompt appears only in three situations: a run is still going (C12), a typed scenario has no folder yet (C11), or the save failed (C10). The Phase 2 discard prompt is gone. Review Core Document now continues with Next >, saves automatically after every generation step and every applied edit, warns that its edits make an existing guide out of date, and uses the same left edge (Content x) as the new screens.

## What was built

- **WizardForm (Task 1):**
  - `StepTypesFor(CaseDriven)` now ends with `StepCaseGuide, StepCaseSummary`.
  - `StepTypeForStage` maps the four stages to step types. Any unknown value falls back to `StepCaseScenario` (T-03-60).
  - `NavigateToStage` looks up the step's index by type and does nothing when that type is not in the current flow.
  - `DecideClose` (a pure function) and `OnFormClosing`:
    - First the step is flushed, then `SaveNow` runs, then the decision.
    - C12 is a Yes/No Question with No as the default. Yes disposes the running step (its token is cancelled) and saves again.
    - C11 ("Close Case") and C10 (with `LastSaveError` and the last saved local time) are Yes/No Warnings with No as the default.
  - In the case flow, Cancel just calls `Close()`. The legacy flow keeps its "Are you sure" prompt.
  - The window now opens at a 1150x900 logical client, with a 1000x700 minimum, both clamped to the working area and centered.
- **StepCoreDocument (Task 2):**
  - Navigation and copy: `NextButtonText => "Next >"`, "before continuing", and "click Next again" (twice).
  - Header row `// 2: Guide notice` (SmallFont, TextMuted, top margin 2). It shows only when `Case.Guide != null`.
  - When a guide exists, Regenerate All and the Scenario Changed prompt both use the C9 copy (Warning, No as the default).
  - Autosave goes through `SaveAsync()` (the `CanSave` guard, then `CaseStore.SaveAsync`, an IsDisposed check, and `SaveFailedText` in Danger on failure). It runs from:
    - the progress callback, when the outline appears and as each section finishes
    - `RunGenerationAsync`'s finally
    - a successful section regenerate
    - `ApplyPendingEdit` when `ApplyEdit` returned true
  - `OnEnterStepAsync`:
    - On a case change, it resets the declined stale hash, the section list and selection, the editor, the guidance and the preview mode, and clears the status.
    - It shows and then clears `PendingNotice` in Success.
    - It calls `AdvanceStage(CoreDocument)` once a document exists.
  - Content x:
    - The issues grid now uses `GridStyle.Apply(_gridIssues, this)` and stays ReadOnly.
    - The Sections header padding is 15 on both sides.
    - The issues caption and the no-issues label are inset 15.
    - The section-list dot is drawn with `Theme.DrawStatusDot`, and its text starts at 15 + 8 + 10.
  - Colors: all status, suffix and row colors are Theme tokens. There are no Color.Red, DarkOrange, Green or DimGray literals and no SystemColors. `CoreDocumentUiLogicTests` was updated to match.
  - `IsGenerating` is public. It was added in the Task 1 commit because `OnFormClosing` needs it.
- **Shell render (Task 3):**
  - `CreateForRender(state)` builds the case-flow steps without entering the API step, so no saved keys are loaded. It loads no icon, and closing it never prompts.
  - `ShowStepForRender(index)` is the test entry point.
  - The header title and the nav buttons are now named controls, so tests can find them.

## Verification

- `WizardFlowTests`: 16/16 pass. They cover the flow table, stage mapping (including an unknown value), every DecideClose branch, Finish validating before close, the OnFormClosing source checks and the C11 title.
- The full suite passes, render tests included: 1152/1152. App and test builds have 0 warnings.
- Plan grep checks all pass:
  - WizardForm.cs: flow types 1, StepTypeForStage 1, NavigateToStage 1, discard text 0, `CaseStore.SaveNow` 2, `"Close Case"` 1, off-palette 0, CreateForRender 1.
  - StepCoreDocument.cs: Next > 1, "click Finish again" 0, notice 1, `CaseStore.SaveAsync` 1, `SaveAsync()` 5, `GridStyle.Apply(_gridIssues, this)` 1, `Theme.DrawStatusDot` 1, off-palette 0, `C9RegenerateDocumentWithGuide` 2.

## Render inspection (7 PNGs, opened one by one; display at 120 DPI = 125%, DeviceDpi >= MinimumDpi)

- `core-document-no-guide-1100x750` / `-900x600`:
  - Shared left edge: the Sections caption, the section-list dots and text, the Issues caption, the grid header ("Severity") and the first cell ("Warning") all start at the same x. That is card edge + 20 px (16 logical); the text glyphs sit at +23 because of the label's leading.
  - No guide notice. The subtitle sits directly under the title.
  - At 900x600 the section list scrolls vertically, with no horizontal scrollbar.
  - The 03-02 baseline clipping findings are gone, and the detector reports nothing.
- `core-document-with-guide-1100x750` / `-900x600`: the notice "The case guide was built from this document. Edits will mark it out of date." sits under the subtitle in small muted text, and the cards move down by one line. Clipping: none.
- `wizard-shell-case-guide` (1150x900 logical client):
  - The header reads "Step 4 of 5: Case Guide".
  - The Checks list shows (not folded), with the detail card and the grid side by side.
  - The page title's left edge matches the step's title. Cards and Cancel sit on the 24 margin, and Next > right-aligns with the cards' right edge.
  - The buttons are 34 logical px high with their full text.
- `wizard-shell-case-summary`: the header reads "Step 5 of 5: Case Summary" and the right button reads "Finish". The closing hint sits above the nav bar.
- `wizard-shell-core-document`: the header reads "Step 3 of 5: Review Core Document", the right button reads "Next >", and the guide notice is visible.
- In all three shell renders the header band text is not clipped, and the clipping detector reports nothing.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Visual] The default window was still too small for the Case Guide**
- **Found during:** Task 3 (shell render)
- **Issue:** I first opened the window at a 1100x750 logical client, as the orchestrator suggested. The shell's header band (64), nav bar (66), footer (30) and top margin (24) leave the step only about 1052x566 logical. At that size the Case Guide folded its Checks list ("Make the window taller to see the list."), which is the exact cramped state 03-16 deferred.
- **Fix:** The window now opens at a 1150x900 logical client (the step gets about 1100x716). The minimum client stays 1000x700. Both are clamped to the screen's working area. The shell render test asserts the default size and that Checks are not folded whenever the working area is large enough.
- **Files modified:** Forms/WizardForm.cs, ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
- **Commit:** 9af32f0

**2. [Rule 3 - Blocking] `StepCoreDocument.IsGenerating` was needed in Task 1**
- `OnFormClosing` checks `StepCoreDocument { IsGenerating: true }`, so I added the property in the Task 1 commit (4fea5a5), not in Task 2.

**3. [Rule 2] Stage recorded on the first save**
- `SaveAsync` calls `AdvanceStage(CoreDocument)` before every save. Without it, a case whose document was generated on its first entry would be saved with `Stage = Described` until the user left the step and came back.

**4. [Rule 1] Color tokens beyond the three listed**
- `SectionSuffixColor` also used `Color.DimGray` and `SystemColors.ControlText`. They are now `Theme.TextMuted` and `Theme.Text` (the plan's grep counts DimGray). `CoreDocumentUiLogicTests` asserts the new tokens.

## Known Stubs

None.

## Deferred Issues

- No caller uses `NavigateToStage` yet. Opening a recent case at its stage belongs to the plan that wires case opening (03-19/03-20).
- The 03-16 deferred item "Wizard window too small for the Case Guide" is resolved here (see Deviation 1).

## Threat Flags

None. The plan's threat register covers all three (T-03-59 flush + SaveNow + C10/C12, T-03-60 unknown stage -> scenario and type lookup, T-03-61 C12 + dispose cancels the run). No new endpoints or file paths: saves go through the existing `CaseStore`.

## TDD Gate Compliance

- RED: 8aca1c8 `test(03-18)`. It failed to compile because `StepTypeForStage`, `CloseDecision` and `DecideClose` did not exist yet.
- GREEN: 4fea5a5 `feat(03-18)`, with 16/16 WizardFlowTests passing.

## Self-Check: PASSED
