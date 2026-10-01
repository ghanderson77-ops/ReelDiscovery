---
phase: 02-case-scenario-core-document
plan: 09
subsystem: ui
tags: [winforms, wizard, workflow, navigation]
requires:
  - phase: 02-01
    provides: WizardFlow enum, WizardState.Flow
  - phase: 02-07
    provides: StepCaseScenario
  - phase: 02-08
    provides: StepCoreDocument (ValidateStepAsync blocks on citation errors)
provides:
  - WizardForm.StepTypesFor(flow) step table and BuildSteps rebuild after the shared API step
  - Finish path that validates the last step before closing, plus "Close Case" confirm in the case-driven flow
  - Workflow radio selector on the API Configuration step (case-driven default)
affects: [phase-03-case-guide, phase-07-legacy-removal]
tech-stack:
  added: []
  patterns: ["Flow-dependent step list built from a Type table via Activator.CreateInstance", "Source-scan test guards Finish ordering"]
key-files:
  created:
    - ReelDiscovery.Tests/Case/WizardFlowTests.cs
  modified:
    - Forms/WizardForm.cs
    - UserControls/StepApiConfiguration.cs
key-decisions:
  - "[02-09] Wizard steps come from one StepTypesFor(flow) table; index 0 (API step) is shared and never rebuilt; later steps are removed+disposed and recreated only when the flow changes on leaving step 0"
  - "[02-09] Finish always runs the last step's ValidateStepAsync first; legacy StepComplete returns true so legacy behavior is unchanged"
  - "[02-09] Case-driven Finish shows the C6 'Close Case' confirm (default No) since Phase 2 has no case persistence"
patterns-established:
  - "Workflow radios respect _suppressEvents and write WizardState.Flow both on CheckedChanged and in ValidateStepAsync"
requirements-completed: [CASE-01, CASE-07]
duration: 6min
completed: 2026-10-01
---

# Phase 2 Plan 09: Wizard Flow Switch Summary

**Flow-dependent wizard step list (case-driven default: API -> Describe the Case -> Review Core Document; legacy 7 steps intact), a Finish path that runs the last step's validation before closing, a "Close Case" discard confirm, and a Workflow selector on the API step.**

## Performance

- **Duration:** ~6 min
- **Started:** 2026-10-01T16:02:58Z
- **Completed:** 2026-10-01T16:09Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- `WizardForm.StepTypesFor(WizardFlow)` defines both flows in one table; `LoadSteps` creates the shared API step and calls `BuildSteps(_state.Flow)`; `WireStep` holds the former bind/dock/add block.
- `BtnNext_Click` validates the current step first (so `StepCoreDocument` citation errors block Finish), then on the last step shows the C6 confirm in the case-driven flow before `this.Close()`. Leaving step 0 rebuilds the list if the chosen flow differs from `_builtFlow`. The header's "Step X of N" uses `_steps.Count` and follows the active flow.
- API Configuration step: rows 13-14 add a `Workflow` header and the `Case-driven (new)` (checked) / `Legacy topic wizard` radios, with MinimumSize raised to 580.

## Task Commits

1. **Task 1 (RED): failing WizardFlow tests** - `599ec12` (test)
2. **Task 1 (GREEN): flow-dependent steps + validated Finish + Close Case** - `76e99c5` (feat)
3. **Task 2: Workflow selector rows** - `fe950b3` (feat)

## Files Created/Modified
- `Forms/WizardForm.cs` - StepTypesFor, WireStep, BuildSteps, `_builtFlow`, reordered Finish path with C6 confirm
- `UserControls/StepApiConfiguration.cs` - Workflow header + radio rows, Flow sync in OnEnterStepAsync/ValidateStepAsync
- `ReelDiscovery.Tests/Case/WizardFlowTests.cs` - 4 facts: both step tables, step-type contract, BtnNext_Click source-scan (ValidateStepAsync before this.Close(), "Close Case" present)

## Verification
- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors
- `dotnet test ReelDiscovery.slnx`: 447/447 passed (4 new)
- Smoke test (not committed): constructed `WizardForm` on an STA thread, step count 3; `BuildSteps(Legacy)` -> 7; `BuildSteps(CaseDriven)` -> 3; Show/Close without exceptions.

## Decisions Made
See key-decisions in frontmatter.

## Deviations from Plan

None - plan executed exactly as written. (The radio CheckedChanged handler is a named method `WorkflowRadio_CheckedChanged` that also honors the existing `_suppressEvents` guard, as allowed by the plan's step 4.)

## TDD Gate Compliance
RED `599ec12` (test, compile failure on missing StepTypesFor) precedes GREEN `76e99c5` (feat). No refactor needed.

## Issues Encountered
None.

## Known Stubs
None. The C6 confirm text intentionally states the case is not saved yet; persistence is GUIDE-10 in Phase 3.

## User Setup Required
None.

## Next Phase Readiness
- The case-driven flow is reachable end to end; manual UAT (02-VALIDATION.md manual-only items) remains for /gsd-verify-work.
- Phase 3 can append case-guide steps to `StepTypesFor(WizardFlow.CaseDriven)`.

## Self-Check: PASSED
- FOUND: Forms/WizardForm.cs, UserControls/StepApiConfiguration.cs, ReelDiscovery.Tests/Case/WizardFlowTests.cs
- FOUND commits: 599ec12, 76e99c5, fe950b3
