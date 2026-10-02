---
phase: 04-corpus-plan-answer-key
plan: 17
subsystem: wizard-ui
tags: [winforms, corpus-settings, dpi, autosave, render-tests]
requires:
  - phase: 04-01
    provides: CorpusSettings (RelevantCount, DefaultWindow, ClampWindow, CreateDefault, SanitizePrefix), CorpusState
  - phase: 04-05
    provides: DocIdPrefix.Derive / Effective
  - phase: 04-11
    provides: CorpusUiText copy helpers
  - phase: 04-12
    provides: PercentSlider
  - phase: 04-21
    provides: CaseStage.CorpusSettings
provides:
  - "StepCorpusSettings : UserControl, IWizardStep (Corpus Settings step, UI-SPEC S1) with internal LoadForRender(state, expandAdvanced)"
  - "WizardState.SuppressAutoPlanOnce (set by 04-19 on reopen, cleared by a successful Next here)"
  - "Pure helpers ParseCount, FormatCount, MatchingPreset, ClampInt, SanitizePrefix, ClearsAutoPlanSuppression"
affects: [04-18, 04-19, 04-22]
tech-stack:
  added: []
  patterns:
    - "Wrapping labels in AutoSize table rows: AutoSize label with MinimumSize = MaximumSize pinned to (width, measured text height), so TLPs never keep a stale wrap height"
    - "Rows sized by code (notice line) set an Absolute RowStyle and call the root's PerformLayout; the step's OnLayout runs UpdateWidths before base.OnLayout"
    - "Padding = 1 card with full-bleed content: card padding (1, R, 1, R) and header/body paddings reduced by R, so content never covers the rounded corners"
key-files:
  created:
    - UserControls/StepCorpusSettings.cs
    - ReelDiscovery.Tests/Corpus/CorpusSettingsStepLogicTests.cs
    - ReelDiscovery.Tests/Render/CorpusSettingsRenderTests.cs
  modified:
    - Models/WizardState.cs
key-decisions:
  - "Fields commit on Leave/Enter only (slider on mouse-up/key-up/Leave); a commit saves only when a value actually changed, and bumps Draft.Revision"
  - "Date window commits only when both dates parse and end >= start after clamping; an empty window is valid only when the guide has no dated facts"
  - "Disabled Images/Voicemail boxes never overwrite the saved choice; the effective Checked is saved && IsMediaAvailable, re-evaluated on every enter"
  - "Relevant % field clamps reuse OverrideClampNotice in the warning line; mix shares clamp silently to 0-100 (the total line shows the result)"
  - "Advanced card uses top/bottom card padding equal to the corner radius instead of Padding = 1 everywhere (fixes the 04-12 square-corner item locally)"
patterns-established:
  - "Pinned-preferred-size wrapping labels (FitWrap) for code-built steps with AutoSize table rows"
requirements-completed: []
metrics:
  duration: 25min
  completed: 2026-10-02
  tasks: 2
  files: 4
---

# Phase 4 Plan 17: Corpus Settings Step Summary

The Corpus Settings wizard step (UI-SPEC S1) covers size presets and a size field, a themed % relevant slider with live counts from `CorpusSettings.RelevantCount`, a date window clamped around the key facts, attachment types gated on a tested OpenAI key, and a collapsible Advanced card for the mix, overrides and DocID prefix. Every change autosaves. Next is blocked only by a mix total other than 100 or an invalid date, and it was verified at 144 DPI in four render states at Large and WizardMin.

## What Was Built

- **Layout**: root TLP (header / notice / scroll host / status, with row-index comments). Two 50/50 cards (`Size and relevance`, `Dates and attachments`) are `Dock = Fill` in one AutoSize row, so both are always the same height. The `Advanced` card sits below. The scroll host's inner TLP width follows `ClientSize.Width`, so the page never scrolls sideways.
- **Size**: four Toggle presets of equal width, 4 px apart, plus a 96 px right-aligned field. Typing selects the matching preset. Values clamp to 50-5,000 and show `SizeClampNotice`. An empty or non-numeric entry restores the previous value.
- **Relevance**: `PercentSlider` (5-60) with a 72 px field and a `%` suffix on one vertical center. `ValueChanged` updates the field and live counts without committing; `ValueCommitted`/Leave commits. `RelevantWarning` shows in Warning or muted colour.
- **Dates**: Start/End fields (`YYYY-MM-DD`) and `Reset Window`. Commits go through `CorpusSettings.ClampWindow`: clamping shows Warning notices, while invalid dates or an end before the start show Danger messages and the value is not committed.
- **Media**: four `FlatStyle.System` checkboxes on `Theme.Surface`. Images and Voicemail are disabled with the media notice when `!IsMediaAvailable`.
- **Advanced**: collapsed by default on every entry. The summary uses `AdvancedSummary` with `DocIdPrefix.Effective`. The mix grid (64 px fields, Success/Danger total line, decoy hint, `Use Default Mix`) and the overrides grid share one field column. Overrides: hot 1-5, privileged 0-30 (disabled when there is no counsel), attachment 0-60 (empty = automatic, clamp lines `Set to {v}. Use {min} to {max}.`), and a DocID prefix (A-Z only, upper-case, placeholder = derived prefix, 1 letter = Danger + not committed).
- **Lifecycle**: `OnEnterStepAsync` creates `Draft.Corpus` with defaults when missing, fills the controls, shows `SettingsNotice`, collapses Advanced, calls `AdvanceStage(CaseStage.CorpusSettings)` and saves. `OnLeaveStepAsync` flushes and saves. `ValidateStepAsync` flushes, then M-S1 (mix) or M-S2 (date) blocks; otherwise it sets `SuppressAutoPlanOnce = false` and returns true. `Reset to Defaults` asks C-S1 (default No).
- **Tests**: 42 logic tests (pure helpers plus STA commit behaviour: defaults by complexity, clamps, invalid date not committed, media choice kept without a key). The 4 render tests × 2 sizes assert no clipping, no horizontal scrollbar, equal card heights, slider/% centre within 1 px, and the state-specific labels and enabled flags.

## Render Inspection (144 DPI)

I opened all eight `corpus-settings-*` PNGs and checked them:
- Captions and field hosts share one Content x per card.
- Size toggles read as one segmented control (equal widths and heights, 4 px gaps).
- The slider thumb is round and crisp at both 20% and 5%.
- Checkboxes sit on white, with disabled Images/Voicemail greyed.
- The Advanced card corners are round both collapsed and expanded.
- No ellipsized button text, and no horizontal scrollbar.
- At WizardMin the page scrolls vertically and the hints wrap cleanly.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Stale AutoSize row heights left a 150 px gap under the notice**
- **Found during:** Task 2 render inspection
- **Issue:** The notice row kept a height measured at the 150 px default control width. The table layout was not re-run after the width changed, and AutoSize labels with `MaximumSize` did not re-measure.
- **Fix:** Wrapping labels pin `MinimumSize = MaximumSize = (width, measured height)` (`FitWrap`). The notice row is an Absolute `RowStyle` sized in code, followed by `_root.PerformLayout()`. `OnLayout` runs `UpdateWidths` first.
- **Files modified:** UserControls/StepCorpusSettings.cs
- **Commit:** 62a038b

**2. [Rule 1 - Bug] Advanced card showed square corners (deferred-items 04-12 pattern)**
- **Found during:** Task 2 render inspection
- **Issue:** I first tried clipping the content with a rounded `Region`. `DrawToBitmap` ignores window regions, so this could not be verified in the renders.
- **Fix:** Card padding is `(1, 8, 1, 8)` (logical radius). The header and body paddings subtract that inset, so spacing still matches 15/14/15/10 and 16 from the border.
- **Files modified:** UserControls/StepCorpusSettings.cs
- **Commit:** 62a038b

**3. [Rule 3 - Blocking] Render assertions on `Visible` after the form closed**
- **Issue:** `Control.Visible` is false once the render form is disposed.
- **Fix:** The test collects visible label texts inside the arrange callback.
- **Commit:** 62a038b

## Known Stubs

None. The step is complete but not yet registered in the wizard; 04-19 adds it to the case flow and the stage mapping, as the plan intends.

## Notes

- `PlaceholderText` (`Auto`, the derived DocID prefix, `YYYY-MM-DD`) does not appear in the renders, because `DrawToBitmap` uses WM_PRINT and skips the TextBox placeholder paint. It still shows in the live UI.
- In state 3 (250 emails, 5% = 13 relevant), the RichDraft fact count produces the muted "fuller threads" line rather than the Warning variant. The test asserts whichever line `RelevantWarning` returns.
- Requirements: PLAN-01 was already checked in REQUIREMENTS.md. PLAT-03 stays partial (it spans the whole phase), so I marked nothing.

## Self-Check: PASSED

- FOUND: UserControls/StepCorpusSettings.cs
- FOUND: ReelDiscovery.Tests/Corpus/CorpusSettingsStepLogicTests.cs
- FOUND: ReelDiscovery.Tests/Render/CorpusSettingsRenderTests.cs
- FOUND: 9779eb6, 62a038b
