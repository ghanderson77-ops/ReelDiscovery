---
phase: 07-scale-cost-qa-cutover
plan: 19
subsystem: shell-cutover
tags: [cutover, wizard-shell, api-step, key-store, dpi, OUT-03, PLAT-05, D-20, D-21, D-22, D-04]
requires:
  - phase: 07-11
    provides: "CG3CloseDuringRun(pendingBatches) close prompt used by OnFormClosing"
  - phase: 07-12
    provides: "Generate Corpus step state consumed by the shell close path"
provides:
  - "WizardForm.StepTypes: the single 8-step case-driven list (API Configuration -> Case Scenario -> ... -> Case Summary)"
  - "WizardForm.DecideClose(draft, runActive, saveSucceeded) without a flow parameter"
  - "StepApiConfiguration with 13 rows, disclaimer last, no Workflow radios"
  - "ApiKeyStore without legacy migration; Program.Main = disclaimer + Application.Run(new WizardForm())"
affects: [07-20]
tech-stack:
  added: []
  patterns: ["Render tests seed dummy keys in WizardState so the API step never reads the user's DPAPI key store"]
key-files:
  created: []
  modified:
    - Forms/WizardForm.cs
    - UserControls/StepApiConfiguration.cs
    - Models/WizardState.cs
    - Program.cs
    - Services/ApiKeyStore.cs
    - ReelDiscovery.Tests/Case/WizardFlowTests.cs
    - ReelDiscovery.Tests/Case/CaseDraftTests.cs
    - ReelDiscovery.Tests/ApiKeyStoreTests.cs
    - ReelDiscovery.Tests/PlatformTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
  deleted:
    - Models/WizardFlow.cs
decisions:
  - "[07-19]: WizardForm builds every step from the static StepTypes list in LoadSteps; BuildSteps, the flow rebuild and the legacy 'Are you sure you want to cancel?' prompt are gone (Cancel just closes; OnFormClosing decides)"
  - "[07-19]: StepApiConfiguration MinimumSize back to 520 (the +60 only existed for the removed Workflow rows)"
  - "[07-19]: OUT-03 marked complete here (the case flow is now the only flow); PLAT-05 stays pending until 07-20 deletes the legacy step and generator code"
metrics:
  duration: 9min
  completed: 2026-10-06
---

# Phase 7 Plan 19: Shell and Startup Cutover Summary

The app now has a single case-driven wizard. It opens on API Configuration, then Case Scenario. The flow choice and the `WizardFlow` enum are gone, the API step no longer has the Workflow radios, and startup no longer migrates the old Base64 key file.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | Single step list, no flow switch, API step rows removed | d3e5a27 (RED), 15bb0d3 (GREEN) | Forms/WizardForm.cs, UserControls/StepApiConfiguration.cs, Models/WizardState.cs, Models/WizardFlow.cs (deleted), WizardFlowTests, CaseDraftTests, WizardShellRenderTests |
| 2 | Drop the legacy key migration (D-22) | 6690b24 (RED), 2549253 (GREEN) | Program.cs, Services/ApiKeyStore.cs, ApiKeyStoreTests, PlatformTests |

## What changed

- `WizardForm`: `internal static IReadOnlyList<Type> StepTypes` replaces `StepTypesFor(WizardFlow)`. `LoadSteps` creates all eight steps. `_builtFlow`, `BuildSteps`, the rebuild in `BtnNext_Click`, the legacy cancel MessageBox, the flow parameter of `DecideClose` and all `Flow` checks are removed. The form no longer references any legacy step type.
- `StepApiConfiguration`: the Workflow header, `_rbCaseDriven`/`_rbLegacy`, their panel, handler, bind and validate lines are removed. `RowCount = 13`, and the row comments run 0-12 with `// 12: Disclaimer (last row)`. The disclaimer row keeps its 44 px height and margin.
- `Models/WizardFlow.cs` and `WizardState.Flow` are deleted.
- `Program.cs`: the migration try/catch and the `using ReelDiscovery.Services;` it needed are removed.
- `ApiKeyStore`: `LegacySettingsFileName`, `LegacySettingsPath` and `MigrateLegacyKey()` are deleted.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings
- WizardFlow, CaseDraft, WizardShell, KeyTestGuard and WizardStateProvider tests: 77 passed
- `Category=Render & WizardShellRenderTests`: 10 passed, no clipping findings
- ApiKeyStore and PlatformTests: 11 passed
- Full non-render suite: 3748 passed

### Renders inspected (scaled display, 144-DPI reference)

All files are in `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`:
- `wizard-shell-api-configuration.png`: Step 1 of 8: API Configuration. No Workflow section. The disclaimer sits directly above the nav bar with no blank gap below it. Nothing clips.
- `wizard-shell-case-scenario.png`: Step 2 of 8: Describe the Case, with recent cases. Nothing clips.
- `step-api-configuration-1100x750.png` and `step-api-configuration-1000x540.png` (WizardMin): every row is visible, instructions wrap and the disclaimer is the last row. The no-gap assertion holds (gap within 4 logical px).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Commit 15bb0d3 initially held only the WizardFlow.cs deletion**
- **Found during:** Task 1 commit
- **Issue:** `git add` aborted on the already-removed path, so the first commit held only the staged deletion.
- **Fix:** Staged the three modified files and amended the same unpublished commit.
- **Commit:** 15bb0d3

**2. [Rule 1 - Bug] Case Scenario shell title**
- **Found during:** Task 1 RED
- **Issue:** The step title is "Describe the Case", not "Case Scenario".
- **Fix:** The render test expects "Step 2 of 8: Describe the Case". Titles were not changed.

The `ApiKeyStore` legacy-file test passed during RED: the store never read the legacy file itself, because only `MigrateLegacyKey` did. The RED gate for Task 2 came from the PlatformTests reflection and Program.cs assertions.

## TDD Gate Compliance

Both tasks have a `test(07-19)` commit followed by a `feat(07-19)` commit.

## Known Stubs

None.

## Self-Check: PASSED
