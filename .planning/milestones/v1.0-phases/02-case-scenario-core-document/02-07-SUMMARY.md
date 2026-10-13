---
phase: 02-case-scenario-core-document
plan: 07
subsystem: case-scenario-ui
tags: [winforms, wizard-step, dialog, real-names, ui-spec]
requires:
  - "02-01 CaseDraft, ComplexityPreset, Jurisdiction.UsStates, EntitySubstitution"
  - "02-02 NameSubstitution.Apply"
  - "02-05 RealEntityDetector"
provides:
  - "UserControls/StepCaseScenario (Describe the Case step; CounterText/CounterColor static helpers)"
  - "UserControls/EntityReviewDialog (Real Names Found modal; KindLabel/NameWithAliases/FindBlankSubstitute static helpers)"
  - "Helpers/LoadingOverlay.LoadingType.EntityCheck"
affects: [02-09]
tech-stack:
  added: []
  patterns:
    - "Real-name pre-flight gated by scenario SHA-256 hash (no repeat LLM call when unchanged)"
    - "Overlay hidden before ShowDialog, and again in finally"
    - "Owner-drawn DataGridView cell (name default color, aliases DimGray)"
key-files:
  created:
    - UserControls/StepCaseScenario.cs
    - UserControls/EntityReviewDialog.cs
    - ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs
  modified:
    - Helpers/LoadingOverlay.cs
decisions:
  - "[02-07]: StatePreference is stored only when Court = State and a state is chosen; otherwise empty (a stale state from a disabled combo is not written)"
  - "[02-07]: Scenario TextBox is made ReadOnly (not Disabled) while the name check runs so the text stays readable under the overlay"
  - "[02-07]: The status line is cleared after the dialog closes (Apply or Back); Green 'No real names found.' only when the detector returns nothing"
metrics:
  duration: 9min
  completed: 2026-10-01
---

# Phase 2 Plan 07: Describe the Case step and Real Names Found dialog Summary

`StepCaseScenario` ("Describe the Case", UI-SPEC S1) collects a free-text scenario in a multiline TextBox with `MaxLength = 0`, so long pastes are not silently cut off at 32,767 characters. A live counter shows `{n:N0} / 8,000 characters` in DimGray, DarkOrange above 7,200 and Red above 8,000. Simple / Standard / Complex radios update the preset summary from `ComplexityPreset.Get(level).Summary` immediately. Optional Document (Auto-detect / Complaint / Petition / Investigation Memo), Court (Auto / Federal / State) and State (`Auto` + `Jurisdiction.UsStates`, enabled only for State court) combos complete the inputs. On Next, the scenario is length-validated, the draft fields are written, and `RealEntityDetector` runs under the new `EntityCheck` loading overlay only if the scenario hash changed. Found entities open `EntityReviewDialog` ("Real Names Found", UI-SPEC S2). It has an amber warning banner and a grid with "Use fictional name" checked by default, aliases drawn in DimGray, the detector reason as a tooltip and an editable substitute (greyed and read-only when the row is unchecked). Apply Substitutes rejects blank substitutes and asks "Keep Real Names?" (default No) before keeping any real names. `CaseDraft` then holds the substitutions, `EntityCheckHash` and `EffectiveScenario = NameSubstitution.Apply(...)`. The step is not in the wizard list yet; 02-09 wires it in.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | LoadingOverlay EntityCheck + EntityReviewDialog + helper tests | f4b38e2 (RED), 38e46da (GREEN) | Helpers/LoadingOverlay.cs, UserControls/EntityReviewDialog.cs, ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs |
| 2 | StepCaseScenario + counter helper tests | b4b2773 | UserControls/StepCaseScenario.cs, ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors
- `dotnet test --filter ReelDiscovery.Tests.Case.CaseScenarioUiLogicTests`: 15 passed (9 dialog-helper cases, 6 counter cases)
- `dotnet test --filter ReelDiscovery.Tests.Case`: 216 passed
- All grep acceptance criteria for both tasks met (doc comments were reworded so the quoted-title greps count exactly 1)
- One-off STA smoke test (not committed) constructed both controls, bound a WizardState with Complex/State/Texas preferences and ran OnEnterStepAsync without exceptions

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] NullReferenceException when the step was constructed**
- **Found during:** Task 2 (throwaway STA smoke test)
- **Issue:** Setting `_rbStandard.Checked = true` in `InitializeUI` raised CheckedChanged. That handler reads `_lblPresetSummary` and `SelectedLevel()`, which reads `_rbComplex`, and neither existed yet.
- **Fix:** The preset summary label is now created before the radios, and the default radio is checked only after all three radios exist.
- **Files modified:** UserControls/StepCaseScenario.cs
- **Commit:** b4b2773

**2. [Rule 2 - Correctness] Combo default selections**
- The Document, Court and State combos get `SelectedIndex = 0` at construction. `ValidateStepAsync` clamps the indexes with `Math.Max(0, ...)` so a missing selection cannot produce an invalid enum value.

## TDD Gate Compliance

Task 1: a RED `test(02-07)` commit (f4b38e2, compile failure) comes before the GREEN `feat(02-07)` commit (38e46da). Task 2 is not TDD (`type="auto"`). Its two counter facts were added in the same commit as the step.

## Known Stubs

None. The step is complete but not registered in `WizardForm` yet; plan 02-09 does that.

## Self-Check: PASSED

- FOUND: UserControls/StepCaseScenario.cs, UserControls/EntityReviewDialog.cs, ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs, Helpers/LoadingOverlay.cs
- FOUND commits: f4b38e2, 38e46da, b4b2773
