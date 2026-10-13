---
phase: 04-corpus-plan-answer-key
plan: 12
subsystem: ui-controls
tags: [winforms, custom-paint, dpi, render-tests, tdd]
requires:
  - phase: 04-08
    provides: MonthBucket (PlanSummary)
  - phase: 04-11
    provides: TileContent, CorpusUiText.Replan* copy
  - phase: 03
    provides: Theme, CardPanel, ThemedDialog, TextFieldHost, ButtonHelper, RenderHarness
provides:
  - PercentSlider (themed 5-60 slider; ValueChanged live, ValueCommitted on mouse-up/key-up)
  - MonthHistogram (stacked Relevant/Not relevant per month; NiceAxisMax, HistogramLabelStep, hover tooltip)
  - StatTile (caption/value/sub-line card with full-text tooltip; SetContent(TileContent))
  - ReplanDialog (D-RP; Guidance property)
affects: [04-17 Corpus Settings step, 04-18 Review Plan step]
tech-stack:
  added: []
  patterns:
    - "Custom Control painting with every length from LogicalToDeviceUnits at paint time, repaint on DpiChangedAfterParent"
    - "Histogram bucket edges from Math.Round(i * plotWidth / count) and one constant bar width (no 1-px jitter)"
    - "Ellipsizing label rows: fixed one-line Absolute rows, TopLeft text, heights measured before docking"
key-files:
  created:
    - Helpers/PercentSlider.cs
    - UserControls/MonthHistogram.cs
    - UserControls/StatTile.cs
    - Forms/ReplanDialog.cs
    - ReelDiscovery.Tests/Corpus/CorpusControlsTests.cs
    - ReelDiscovery.Tests/Render/CorpusControlsRenderTests.cs
    - .planning/phases/04-corpus-plan-answer-key/deferred-items.md
  modified: []
key-decisions:
  - "PercentSlider exposes internal ValueFromX(deviceX), HandleKey(Keys) and Commit() for tests; Up/Down step like Right/Left"
  - "MonthHistogram: odd axis maxima (1, 5) keep the middle gridline but drop its label (no fractional N0 label)"
  - "MonthHistogram x labels: after first/last clamping, a label that would come within 4 px of the previous one is skipped, so clamping can never cause overlap"
  - "HistogramLabelStep's 8 px pad is logical; paint folds the device-scaled pad into the measured label width"
  - "StatTile tooltip = '{caption}: {value} ({subline})' plus a new line with the content Tooltip when present; set on the tile, card, layout and all three labels"
  - "StatTile rows are fixed one-line (font height) with TopLeft text: MiddleLeft centres an ellipsized label as if wrapped and lifts it half a line"
metrics:
  duration: 35min
  completed: 2026-10-02
  tasks: 2
  files: 6
---

# Phase 4 Plan 12: Corpus UI Building Blocks Summary

This plan adds four custom-painted, theme-only pieces for Corpus Settings and Review Plan: a 5-60 `PercentSlider`, a stacked `MonthHistogram` with a nice y-axis and labels that never overlap, a `StatTile` and the D-RP `ReplanDialog`. All four render with no clipping at 144 DPI, and I opened and checked every PNG.

## What was built

- **PercentSlider** (`Helpers/PercentSlider.cs`): 28 px tall. It has a 4 px rounded track (Border, with the filled part in Accent or TextDisabled when disabled) and a 16 px Surface thumb with a 2 px border that changes color for normal, hover, pressed and disabled. When focused it shows a 24 px AccentSoft halo instead of a dotted focus rectangle. The thumb center moves between 8 and Width - 8. Input works by clicking the track to jump, dragging, Left/Right ±1, PageUp/PageDown ±5, and Home/End. `ValueChanged` fires only when the value actually changes. `ValueCommitted` fires on mouse-up and key-up.
- **MonthHistogram** (`UserControls/MonthHistogram.cs`): Insets are top 8, right 15 and bottom = font height + 8. The y-gutter starts at Content x 15 and is as wide as the widest measured label + 8. There are three Border gridlines. Relevant (Accent) is stacked under Not relevant (TextDisabled). X labels use "MMM yy" in InvariantCulture, appear every k-th month and are clamped inside the plot. Hovering a month gives it a SurfaceMuted column and a tooltip such as "March 2024: 46 relevant, 120 not relevant (166 emails)". With no buckets, only the axis is drawn. The ToolTip is disposed with the control.
- **StatTile** (`UserControls/StatTile.cs`): a CardPanel with 16/12 padding holding three labels that ellipsize: caption (SmallFont, TextMuted), value (TitleFont, Text, top 4) and sub-line (SmallFont, TextMuted, top 2). `SetContent(TileContent)` fills them and sets the tooltip. `GetPreferredSize` returns the height of the content.
- **ReplanDialog** (`Forms/ReplanDialog.cs`): built from the RegenerateTabDialog template using CorpusUiText Replan* copy. The guidance box is 3 lines with `ScrollBars.None` and gets initial focus (`this.ActiveControl = _txtGuidance`, U16). Buttons are Cancel (Default) and Re-plan Corpus (Primary). `Guidance` returns the trimmed text.

## Verification

- `dotnet test --filter "FullyQualifiedName~CorpusControlsTests"`: 25 passed.
- `dotnet test --filter "Category=Render&FullyQualifiedName~CorpusControlsRenderTests"`: 5 passed, no clipping (both dialog variants, controls at Large and WizardMin, 9-bucket histogram).
- Full non-render suite: 1819 passed. Main project builds with 0 warnings.
- **PNGs checked by eye** (renders/corpus-controls-1100x750, -1000x540, -histogram-9-1100x750, replan-dialog-approved/unapproved):
  - The slider thumb is round and crisp, with a visible halo.
  - Histogram bars are evenly spaced. The 51-bucket chart labels every 3rd month and the 9-bucket chart labels every month, with no overlaps.
  - The y labels start at Content x.
  - At Large no tile value is ellipsized, and the test also asserts this. At WizardMin only the sub-lines ellipsize ("on 20% of em...", "to generate, ±5..."), which the UI-SPEC allows.
  - Both dialog variants fit their content and the guidance box has focus.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] StatTile was too tall and ellipsized sub-lines sat out of line**
- **Found during:** Task 2, looking at the render PNGs
- **Issue:** Adding the labels to the TableLayoutPanel resized them to their cells before `TileHeight()` read their heights. That made the tile 150 px instead of 133 px at 144 DPI, with extra white space under the sub-line. Separately, labels that were both MiddleLeft and ellipsized drew half a line higher than their neighbours.
- **Fix:** Row heights are now measured before the labels are docked and stored. Rows are a fixed one line (font height) and the text is TopLeft.
- **Files modified:** UserControls/StatTile.cs
- **Commit:** ddee826

### TDD note
For the RED gate, the test file was committed first (13e7908). Before that commit I confirmed it failed to compile without the implementation files. GREEN followed in 42f74a4.

## Deferred Issues

- Cards with `Padding = 1` show square inner corners because a full-bleed child covers the rounded border corners. This pattern comes from Phase 3 and was used here in the test host's histogram card. It is logged in `deferred-items.md` for 04-18.

## Known Stubs

None.

## Requirements

PLAN-07, PLAN-01 and PLAT-03 are only advanced by this plan, so REQUIREMENTS.md was not changed. PLAN-07 needs the Review Plan step (04-18), PLAN-01 was already complete, and PLAT-03 is still partial.

## Commits

- 13e7908 test(04-12): add failing tests for PercentSlider, MonthHistogram and StatTile
- 42f74a4 feat(04-12): add PercentSlider, MonthHistogram and StatTile controls
- ddee826 fix(04-12): keep StatTile lines aligned and the tile one content-height tall
- de43698 feat(04-12): add ReplanDialog and 144-DPI renders of the corpus controls

## Self-Check: PASSED
