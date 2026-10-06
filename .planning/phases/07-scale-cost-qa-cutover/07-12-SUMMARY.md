---
phase: 07-scale-cost-qa-cutover
plan: 12
subsystem: ui-cost
tags: [cost, pricing, prompt-cache, batch, dpi, PROV-07]
requires:
  - "07-01: AIModelConfig cache/batch prices; TokenUsageTracker per-category totals"
provides:
  - "GenerationUiText.CostBreakdown (header ticker tooltip copy, D-16)"
  - "GenerationUiText.CostRow (Case Summary Generation cost row, S2 R1; consumed by 07-18)"
  - "ModelConfigurationDialog cache write / cache read / batch discount columns (D-18)"
  - "WizardForm cost ticker tooltip"
affects: [07-18-case-summary]
tech-stack:
  added: []
  patterns:
    - "Grid numeric bounds enforced in CellParsing (revert to the current cell value); DataError silently cancels the edit"
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/ModelConfigurationDialog.cs
    - Forms/WizardForm.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/MediaPricesDialogTests.cs
    - ReelDiscovery.Tests/Render/DialogRenderTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
decisions:
  - "07-12: Price-cell bounds are enforced in CellParsing (parsed value or the cell's current value), not CellValidating: CancelEdit inside CellValidating still committed the typed value"
  - "07-12: Input $/M and Output $/M get the same 0-1000 bound as the cache prices"
  - "07-12: CostRow tooltip first line is 'Spent so far: $x.xx' (2 decimals) followed by the S6 item lines, plus 'Not itemized' when non-zero"
metrics:
  duration: 25min
  completed: 2026-10-06
  tasks: 2
  files: 7
---

# Phase 7 Plan 12: Cache/Batch Pricing UI and Cost Breakdown Summary

The model dialog now edits Cache Write $/M, Cache Read $/M and Batch Discount % per model (1180 x 480, wrapping headers, frozen Provider/Model ID, silent revert on bad input). The header cost ticker has a tooltip with the session breakdown. `GenerationUiText.CostRow` provides the Case Summary "Generation cost" copy, including the "not itemized" clause, ready for 07-18.

## What Was Built

- `GenerationUiText.CostBreakdown(total, input, cacheWrite, cacheRead, output, batchSavings, media)`: "Spent this session: $x.xxxx" followed by the non-zero lines (Input, Cached input cache write, Cached input cache read, Output, Batch discount with a U+2212 minus sign, Images and voicemail). Returns "Nothing spent this session yet." when every value is zero.
- `GenerationUiText.CostRow(anySpend, ..., unsplit)`: Line2 "What this case has cost so far, across all sessions."; Line3 `$t spent · input · cache write · cache read · output · batch discount −$b · images and voicemail · not itemized` with zero clauses dropped (Muted tone, Success dot). With nothing spent it shows "Nothing spent yet" with a Muted dot (BorderStrong). The tooltip lists the same items one per line.
- `ModelConfigurationDialog`: three new columns after Output $/M (formats `0.00#`, `0.00#`, `0`). Widths narrowed per S5, Display Name fills the remaining width (min 100), headers wrap, Provider and Model ID are frozen, `LogicalSize` is 1180 x 480 and the header copy is extended. `GridModels_DataError` no longer shows a MessageBox. It sets `ThrowException = false`, calls `CancelEdit()` and sets `Cancel = false`. The new `CellParsing` handler checks bounds (prices 0-1000, discount 0-100) with `decimal.TryParse(NumberStyles.Number, CurrentCulture)` and keeps the old value on a bad entry. The clone and Reset Defaults already carried the four new properties (07-01).
- `WizardForm`: `_costToolTip` (disposed in `OnFormClosing`) and `_lastCostTip`. `UpdateCostDisplay` sets the tooltip only when the text changes. The ticker text is unchanged. The test accessor `CostTooltipForTest` was added.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- GenerationUiTextTests + MediaPricesDialogTests + ModelCatalog: 270/270.
- Render (DialogRenderTests + WizardShellRenderTests): 23/23, `Assert.Empty(result.Clipping)`.
- Full non-render suite (`Category!=Render&Category!=Scale`): 3470/3470.
- PNG inspection at 144 DPI (the dialog renders 1770 px wide = 1180 x 1.5):
  - `dialog-model-config-cache-default.png`: all 11 columns are visible with no horizontal scrollbar. "Cache Write $/M" wraps to 3 lines, and Input/Output/Cache Read/Batch Discount/Max Output to 2. All six model rows, the media prices row and OK/Cancel are fully visible.
  - `dialog-model-config-cache-min.png` (820 x 360 logical): the grid scrolls sideways to Max Output while Provider and Model ID stay frozen, 4 full rows show, and the header label wraps to 3 lines. The media prices row and buttons are unclipped.
  - Note: "claude-haiku-4-5-20251..." is ellipsized in the 150-px Model ID column, which is the width S5 specifies. This was pre-existing (the column was already 150) and is not clipping.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 3de8309 | test(07-12): add failing cost breakdown and cost row copy tests |
| 1 GREEN | a92b0f1 | feat(07-12): cost breakdown tooltip and Case Summary cost row copy |
| 2 | a8f811d | feat(07-12): cache/batch price columns in model dialog and cost ticker tooltip |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CellValidating + CancelEdit did not revert out-of-range values**
- **Found during:** Task 2
- **Issue:** Calling `_gridModels.CancelEdit()` inside `CellValidating` still committed the typed value (1001, -1 and 101 were stored).
- **Fix:** Bounds now go through a `CellParsing` handler that sets `e.Value` to the parsed value when it is valid and to the cell's current value otherwise (`ParsingApplied = true`). `DataError` stays as a silent fallback for other columns.
- **Files modified:** UserControls/ModelConfigurationDialog.cs
- **Commit:** a8f811d

**2. [Rule 2 - Missing validation] Input/Output price bounds**
- The 0-1000 bound also applies to Input $/M and Output $/M, so all price cells follow the same rule (T-07-37).

## TDD Gate Compliance

Task 1 followed RED (3de8309) and then GREEN (a92b0f1). Task 2's dialog and shell tests were written together with the implementation and committed in a8f811d, so there is no separate RED commit for Task 2. The tests caught the CellValidating bug above before the commit.

## Known Stubs

None. `CostRow` has no caller yet; 07-18 wires it into StepCaseSummary as planned.

## Self-Check: PASSED
