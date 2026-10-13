---
phase: 04-corpus-plan-answer-key
plan: 19
subsystem: ui-wizard-shell
tags: [winforms, wizard, stage-mapping, close-confirm, dpi, render-tests]
requires:
  - phase: 04-17
    provides: StepCorpusSettings, WizardState.SuppressAutoPlanOnce
  - phase: 04-18
    provides: StepReviewPlan (IsPlanning, CancelAndWaitForRun)
  - phase: 04-11
    provides: CorpusUiText.CP2ClosePlanning
provides:
  - "7-step case flow: API Configuration, Describe the Case, Review Core Document, Case Guide, Corpus Settings, Review Plan, Case Summary"
  - "StepTypeForStage maps CorpusSettings and ReviewPlan (unknown values still open the scenario step)"
  - "StepCaseScenario.MarkOpenedFromDisk(WizardState) sets SuppressAutoPlanOnce on open"
  - "Close during a planning run asks C-P2, cancels and awaits the run, then saves"
  - "MonthHistogram.NiceAxisMax rounds to 1/2/2.5/5 x 10^n"
affects: [04-20 Case Summary corpus rows, phase 04 verification]
tech-stack:
  added: []
  patterns:
    - "WizardForm.WaitWhileStopping(Action): disable form + wait cursor while a step's run is cancelled and awaited"
key-files:
  created: []
  modified:
    - Forms/WizardForm.cs
    - UserControls/CaseGuideUiText.cs
    - UserControls/StepCaseScenario.cs
    - UserControls/MonthHistogram.cs
    - ReelDiscovery.Tests/Case/WizardFlowTests.cs
    - ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideUiLogicTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusControlsTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
key-decisions:
  - "The close-time flush now includes Corpus Settings and Review Plan, so pending corpus edits are committed before the synchronous save"
  - "Closing during planning uses C-P2 (Stop Planning?) only when the current step is Review Plan; core document and guide runs keep C12"
  - "Histogram axis max is the smallest 1, 2, 2.5 or 5 x 10^n at or above the largest month; 2.5 only where it is whole (25, 250, ...)"
metrics:
  duration: 15min
  completed: 2026-10-02
---

# Phase 4 Plan 19: Wizard Registration of the Corpus Steps Summary

**Corpus Settings and Review Plan are now steps 5 and 6 of the 7-step case flow. Case stages map to them by type, recent cases show "Corpus settings" / "Plan review", a case opened from disk never starts planning by itself, and closing during a planning run asks C-P2 and then cancels and awaits the run before saving.**

## What was built

- **Flow table** (`WizardForm.StepTypesFor`): `StepCorpusSettings` and `StepReviewPlan` sit between `StepCaseGuide` and `StepCaseSummary`. The header reads "Step 5 of 7: Corpus Settings" and "Step 6 of 7: Review Plan".
- **Stage mapping** (`StepTypeForStage`): `CorpusSettings` and `ReviewPlan` map to their step types. Unknown values (for example `(CaseStage)99`) still open the scenario step (T-04-60). `NavigateToStage` already maps by type, so reopening a case lands on the furthest corpus step reached.
- **Stage labels** (`CaseGuideUiText.StageLabel`): "Corpus settings" and "Plan review".
- **Reopen flag**: `StepCaseScenario.MarkOpenedFromDisk(state)` sets `SuppressAutoPlanOnce = true`. It is called right after `_state.Case = result.Draft!`, so Review Plan shows "No corpus plan yet" instead of starting a billed run (T-04-62).
- **Run detection and close**: `IsRunActive()` includes `StepReviewPlan { IsPlanning: true }`. `StopActiveRun()` cancels and awaits the planning run through `CancelAndWaitForRun(TimeSpan.FromSeconds(10))` before it disposes the step. The disable/wait-cursor block is now shared in `WaitWhileStopping`. `OnFormClosing` shows `CorpusUiText.CP2ClosePlanning()` while Review Plan is planning (T-04-61).
- **Shell renders**: the existing tests now expect "of 7". New tests cover Corpus Settings (step 5) and Review Plan (step 6). The Review Plan test sets the reopen flag, so the test makes no API calls.

## Verification

- `dotnet test --filter "Category!=Render"`: 1968 passed.
- `WizardShellRenderTests`: 5 passed at 144 DPI with no clipping. I opened and checked `wizard-shell-corpus-settings.png` and `wizard-shell-review-plan.png`. The headers read correctly, nothing is cut off, and the Review Plan step shows the "No corpus plan yet" card with Plan Corpus.
- I re-rendered `review-plan-stale-1100x750.png`. The histogram axis now reads 0 / 125 / 250 (it used to be 500).
- `dotnet build`: 0 warnings, 0 errors.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] The close-time flush skipped the corpus steps**
- **Found during:** Task 2
- **Issue:** `FlushCurrentCaseStep` only called `OnLeaveStepAsync` for the scenario, core document, guide and summary steps. If the window was closed on Corpus Settings, edits that had not been committed yet were lost before the synchronous save.
- **Fix:** Added `StepCorpusSettings` and `StepReviewPlan` to the flush pattern. Both commit synchronously before their first await, the same as the other steps.
- **Files modified:** Forms/WizardForm.cs
- **Commit:** d974d56

**2. [Requested polish] Histogram axis rounded too far**
- **Issue:** A tallest month of about 210 gave a 500 axis.
- **Fix:** `NiceAxisMax` now also tries 2.5 x 10^n where it is a whole number, so 210 gives 250. InlineData rows were added for 21, 201, 210, 250, 251 and 2100.
- **Files modified:** UserControls/MonthHistogram.cs, ReelDiscovery.Tests/Corpus/CorpusControlsTests.cs
- **Commit:** 9218a56

**3. [Test coverage] Recent-item labels**
- Added `CorpusSettings` / `ReviewPlan` rows to `CaseGuideUiLogicTests.RecentItemSecondLine_StageAndDate`.

## TDD Gate Compliance

Task 1 was marked `tdd="true"`. The test and implementation changes were committed together in cbb5119, so there is no separate `test(...)` RED commit. The new rows (stage mapping, the 7-entry flow, labels, MarkOpenedFromDisk) could not compile or pass before the production change.

## Requirements

- **PLAN-07: Complete.** The Review Plan step can now be reached in the wizard. It shows counts by issue, fact, relevance and custodian plus the date histogram, and offers Re-plan and Approve (04-18, render-tested in 8 states).
- **KEY-01 / KEY-05: Complete.** Approve Plan, now reachable, writes answer-key.csv, answer-key.xlsx and the Review Protocol .docx (`CorpusApprovalTests.Approve_DefaultWriter_WritesFilesAndRecordsApproval`).
- PLAN-01 was already Complete. PLAT-03 stays open; it is shared across the phase.

## Commits

- cbb5119 feat(04-19): add corpus steps to the case flow, stage mapping, labels and reopen flag
- d974d56 feat(04-19): detect and stop planning runs on close with C-P2; 7-step shell renders
- 9218a56 fix(04-19): round the month histogram axis to 1/2/2.5/5 x 10^n

## Known Stubs

None.

## Self-Check: PASSED
