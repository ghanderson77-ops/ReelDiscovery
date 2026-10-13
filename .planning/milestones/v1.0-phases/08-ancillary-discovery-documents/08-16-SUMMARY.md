---
phase: 08-ancillary-discovery-documents
plan: 16
subsystem: wizard-shell
tags: [discovery, winforms, wizard, navigation, run-safety, render, ANC-01, ANC-02, ANC-03, ANC-04]
requires:
  - phase: 08-09
    provides: "DiscoveryUiText.CD2CloseDuringRun()"
  - phase: 08-14
    provides: "StepDiscoveryDocuments with IsGenerating, ConfirmLeave(), CancelAndWaitForRun(TimeSpan)"
provides:
  - "IConfirmLeave contract (bool ConfirmLeave()) implemented by StepCaseSummary and StepDiscoveryDocuments"
  - "9-step wizard: Discovery Documents is step 8 (index 7), Case Summary step 9 (index 8)"
  - "WizardForm.NavigateToStep(Type) for type-based navigation (Case Summary -> Discovery Documents, D-08)"
  - "Run-safety: IsRunActive, StopActiveRun (cancel + 10 s wait) and C-D2 close prompt for the discovery step"
affects: [08-17, 08-18, 08-19]
tech-stack:
  added: []
  patterns:
    - "Leave confirmation through an interface (IConfirmLeave) instead of per-step `is StepX` checks in ShowStep"
key-files:
  created:
    - UserControls/IConfirmLeave.cs
  modified:
    - Forms/WizardForm.cs
    - UserControls/StepCaseSummary.cs
    - UserControls/StepDiscoveryDocuments.cs
    - ReelDiscovery.Tests/Case/WizardFlowTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
key-decisions:
  - "08-16: Leaving a step is guarded through IConfirmLeave; ShowStep no longer names StepCaseSummary"
  - "08-16: No CaseStage for the discovery step (D-09); StepTypeForStage unchanged, so reopening never lands on it"
  - "08-16: StepCaseSummary.ConfirmLeave became public to implement the public interface"
requirements-completed: []
duration: 15min
completed: 2026-10-09
---

# Phase 8 Plan 16: Wizard Shell Integration Summary

Discovery Documents inserted as step 8 of 9 with `NavigateToStep(Type)`, an `IConfirmLeave` leave contract and close-during-run safety (C-D2, cancel and wait 10 s).

## Tasks

| Task | Name | Commits |
| ---- | ---- | ------- |
| 1 | IConfirmLeave + WizardForm integration (TDD) | b9050fa (RED), b9932e4 (GREEN) |
| 2 | Shell render tests "of 9" + step 8 header | 6a6a6ce |

## What Was Built

- `UserControls/IConfirmLeave.cs`: `bool ConfirmLeave()`; implemented by Case Summary (blind review, C-Q3) and Discovery Documents (run, C-D1).
- `Forms/WizardForm.cs`: `StepTypes` has 9 entries with `StepDiscoveryDocuments` between Generate Corpus and Case Summary; `StepTypeForStage` unchanged; new `NavigateToStep(Type)`; ShowStep leave check is `is IConfirmLeave leaver && !leaver.ConfirmLeave()`; `FlushCurrentCaseStep`, `IsRunActive`, `StopActiveRun` and the `OnFormClosing` run-ask switch cover the discovery step.
- Tests: `WizardFlowTests` now asserts the 9-step order, no new CaseStage, the IConfirmLeave generalization, the run-safety source wiring, and an STA test that `NavigateToStep(typeof(StepDiscoveryDocuments))` shows "Step 8 of 9: Discovery Documents".

## Render Inspection (144 DPI)

- `wizard-shell-discovery-documents.png`: header "Step 8 of 9: Discovery Documents" in full, no ellipsis; the title ends near 330 logical px, well before the cost box, so it also fits at WizardMin (1000 logical wide). Next button reads "Next >". Clipping report: no findings.
- `wizard-shell-case-summary.png`: header "Step 9 of 9: Case Summary", Finish button; clipping report: no findings.
- All 11 WizardShellRenderTests pass. The shell render runs at the default window size (the harness has no size parameter for whole-form renders); the WizardMin fit is measured from the PNG, not asserted.

## Verification

- `-warnaserror` builds of the app and the test project: green.
- WizardFlowTests, CaseSummaryLogicTests, StepDiscoveryDocumentsTests: 76 passed.
- Non-render `Tests.Discovery.` + `Tests.Case.`: 1650 passed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] StepCaseSummary.ConfirmLeave was internal**
- **Found during:** Task 1
- **Issue:** CS0737: an internal method cannot implement the public `IConfirmLeave` member.
- **Fix:** Changed it to `public` (one-word change beyond the plan's "class declaration only" note).
- **Files modified:** UserControls/StepCaseSummary.cs
- **Commit:** b9932e4

**2. [Acceptance grep] `typeof(StepDiscoveryDocuments)` >= 2 in WizardForm.cs**
- The only code use is `StepTypes`; the D-09 comment above `StepTypeForStage` names `typeof(StepDiscoveryDocuments)` so the count is 2. Case Summary's call to `NavigateToStep(Type)` belongs to a later plan.

## TDD Gate Compliance

RED `test(08-16)` b9050fa (compile failure: missing IConfirmLeave and NavigateToStep(Type)) precedes GREEN `feat(08-16)` b9932e4.

## Known Stubs

None.

## Self-Check: PASSED
