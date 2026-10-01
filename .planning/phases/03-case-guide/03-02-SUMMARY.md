---
phase: 03-case-guide
plan: 02
subsystem: ui-infrastructure
tags: [render-harness, dpi, clipping, grid, text-field, status-dot, tests]
requires: []
provides:
  - "ReelDiscovery.Tests/Render/RenderHarness.cs (RunSta, RenderControl, RenderForm, RequiredDpi 144, Large 1100x750, Small 900x600, OutputFolder <bin>/renders)"
  - "ReelDiscovery.Tests/Render/ClippingDetector.cs (Find(root, ClippingOptions) -> findings; writes .clip.txt via harness)"
  - "Helpers/GridStyle.cs (Apply(grid, owner), AddDotColumn, DotColumnName)"
  - "Helpers/TextFieldHost.cs (Create(owner, textBox, lines), SetInvalid)"
  - "Theme.DrawStatusDot(g, cell, color, owner)"
affects: [03-10, 03-11, 03-12, 03-13, 03-14, 03-15, 03-16, 03-17, 03-18, 03-19, 03-20]
tech-stack:
  added: []
  patterns:
    - "Render tests: [Trait(\"Category\",\"Render\")], RenderHarness.RunSta + RenderControl, build children in the arrange callback so LogicalToDeviceUnits uses the hosted DPI"
    - "Render harness throws (not skips) on a non-144-DPI display"
    - "TextFieldHost zeroes EM_SETMARGINS so single-line and multiline text share one left edge"
key-files:
  created:
    - ReelDiscovery.Tests/Render/RenderHarness.cs
    - ReelDiscovery.Tests/Render/ClippingDetector.cs
    - ReelDiscovery.Tests/Render/BaselineRenderTests.cs
    - ReelDiscovery.Tests/Render/SharedHelperRenderTests.cs
    - ReelDiscovery.Tests/Case/SharedUiHelperTests.cs
    - Helpers/GridStyle.cs
    - Helpers/TextFieldHost.cs
  modified:
    - Helpers/Theme.cs
decisions:
  - "[03-02]: Render PNGs go to ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/{name}-{w}x{h}.png with a matching .clip.txt; names: step-case-scenario, step-core-document, shared-helpers"
  - "[03-02]: TextFieldHost sets EM_SETMARGINS to 0 on the TextBox handle: multiline edit controls otherwise inset text ~5 device px more than single-line ones at 144 DPI"
  - "[03-02]: ClippingDetector measures non-AutoSize labels inside their Padding (ClientSize minus Padding) to avoid false negatives on padded labels"
metrics:
  duration: 15min
  completed: 2026-10-01
---

# Phase 3 Plan 02: Render Harness and Shared UI Helpers Summary

This plan adds a 144-DPI render harness and an automated clipping detector to the test project, plus the shared helpers every Phase 3 screen will use: `GridStyle`, `TextFieldHost` and `Theme.DrawStatusDot`. `dotnet test --filter "Category=Render"` now renders real hosted controls at 150% to PNG files and writes a clipping report next to each one. If the display is not 144 DPI, the run fails with a clear message instead of passing.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | Render harness, clipping detector, baseline renders | 651a323 | RenderHarness.cs, ClippingDetector.cs, BaselineRenderTests.cs |
| 2 | GridStyle, TextFieldHost, Theme.DrawStatusDot + helper render | 2a7dbb1 (RED), e24ec90 (GREEN) | GridStyle.cs, TextFieldHost.cs, Theme.cs, SharedUiHelperTests.cs, SharedHelperRenderTests.cs |

## Render outputs (in ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/)

- `step-case-scenario-1100x750.png` / `-900x600.png` (+ `.clip.txt`)
- `step-core-document-1100x750.png` / `-900x600.png` (+ `.clip.txt`)
- `shared-helpers-1100x750.png` (+ `.clip.txt`: no findings)

I opened the PNGs and checked them. They show the real screens at 150% (a 1100x750 logical render is 1650x1125 px). In `shared-helpers`, the grid header text and cell text share the 15-logical inset, and the status dots sit at Content x. The single-line field is the same height as the 34 px Save button. Single-line and multiline text now start at the same x (75 px).

## Baseline clipping findings (Phase 2 screens; fix in 03-18/03-19)

StepCaseScenario (identical at 1100x750 and 900x600):
```
Form("")/content/StepCaseScenario("")/TableLayoutPanel("")/FlowLayoutPanel("")/ComboBox("Auto-detect"): extends past its parent (bounds 180x36, needs 283x40 inside 1572x37)
Form("")/content/StepCaseScenario("")/TableLayoutPanel("")/FlowLayoutPanel("")/ComboBox("Auto"): extends past its parent (bounds 120x36, needs 483x40 inside 1572x37)
Form("")/content/StepCaseScenario("")/TableLayoutPanel("")/FlowLayoutPanel("")/ComboBox("Auto"): extends past its parent (bounds 160x36, needs 718x40 inside 1572x37)
```
(Shown from the 1100x750 report; the 900x600 report is the same with parent width 1272.) In the PNG you can see the bottom 3 px of the Document, Court and State combo boxes cut off: the FlowLayoutPanel row is 37 px tall but holds 36 px combos placed at y=4.

StepCoreDocument: no findings at either size. Things seen in the PNG that the detector does not flag: the Sections list cuts its last item in half ("PRAYER FOR RELIEF") at 900x600, which is normal list scrolling. StepCoreDocument still uses raw `Color.DarkOrange`/`Color.Red` for the issue rows and status text (lines 592, 674, 755) instead of Theme.Warning/Theme.Danger.

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors; the test project also has 0 warnings
- `dotnet test --filter "Category=Render"`: 3 passed (DPI 144)
- `dotnet test --filter "Category!=Render"`: 520 passed
- Acceptance greps: SetProcessDpiAwarenessContext x2, `RequiredDpi = 144` x1, the Render trait, the `Apply`/`Create` signatures, `LogicalToDeviceUnits(15)` x2 in GridStyle, `DrawStatusDot` x1, no raw Color/SystemColors in the helpers

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Multiline TextFieldHost text was misaligned with single-line text**
- **Found during:** Task 2 (PNG inspection)
- **Issue:** The multiline borderless TextBox started its text about 5 device px to the right of the single-line one, which broke the one-left-edge rule.
- **Fix:** `TextFieldHost.Create` sends `EM_SETMARGINS` (left and right = 0) on handle creation, so both kinds start exactly at the host's 10 px padding.
- **Files modified:** Helpers/TextFieldHost.cs
- **Commit:** e24ec90

**2. [Rule 3 - Blocking] CS0649 warning on `ClippingOptions.AllowGridHorizontalScroll`**
- The field now has an explicit `= false` initializer so the build stays at 0 warnings. Commit 651a323.

Other notes: the baseline tests call `OnEnterStepAsync` inside the harness `arrange` callback, so the step is hosted first. The StepCoreDocument fixture's SourceHash is current, so no generation runs and no MessageBox appears. GUIDE-08 was not marked complete because this plan only provides infrastructure for it; the edit UI comes in later plans.

## TDD Gate Compliance

Task 2 has a RED commit (`test(03-02)` 2a7dbb1, a compile failure with 18 errors for the missing helpers) followed by the GREEN commit `feat(03-02)` e24ec90. Task 1 is test infrastructure and was committed as `test(03-02)`.

## Known Stubs

None.

## Self-Check: PASSED
