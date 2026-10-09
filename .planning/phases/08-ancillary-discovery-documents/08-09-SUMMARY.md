---
phase: 08-ancillary-discovery-documents
plan: 09
subsystem: discovery-ui-copy
tags: [discovery, ui-copy, dialog, dpi, render, ANC-01, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-01
    provides: "DiscoveryKind, DiscoveryStatus, DiscoveryRowState, DiscoveryVariant, StaleSource, WitnessSide, DiscoveryDocument, DiscoveryViolation/DiscoveryRule, DiscoverySettings range constants"
provides:
  - "DiscoveryUiText: every Phase 8 string (UI-SPEC S1-S4), ResolveState precedence, RowStatusCell/Full/Tooltip, ViolationText, EstimateCondition + EstimateLine, status lines, CD1/CD2, S2 dialog copy, SummaryRow"
  - "DiscoveryRowInfo: row view model for the Documents grid and the Case Summary row"
  - "RegenerateDiscoveryDialog(shortTitle, fileName, isDeposition, settingsLine, fileOpen, estimateUsd) + Guidance (<= 1,000 chars)"
affects: [08-10, 08-14, 08-16, 08-17, 08-18]
tech-stack:
  added: []
  patterns:
    - "Pure static copy class with nested enum (DiscoveryUiText.EstimateCondition), same shape as QaUiText"
    - "Reflection guard test: no public const/static string or sampled method output contains banned words"
key-files:
  created:
    - UserControls/DiscoveryUiText.cs
    - UserControls/DiscoveryRowInfo.cs
    - Forms/RegenerateDiscoveryDialog.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryUiTextTests.cs
  modified:
    - ReelDiscovery.Tests/Render/DialogRenderTests.cs
    - .planning/phases/08-ancillary-discovery-documents/08-UI-SPEC.md
key-decisions:
  - "08-09: ResolveState treats a non-failed document without content as Not generated (nothing to Save Again from)"
  - "08-09: Case Summary row with only failed documents reads 'Not generated · {f} failed' in Danger (no saved version, so no date)"
  - "08-09: Writing full status drops ', part p of q' for single-part documents ('Writing...'), matching the status line"
  - "08-09: Hidden-kind sets (saved before the variant changed) keep their civil label in ShortTitle"
  - "08-09: Settings lines never end with a double period when a party name ends in 'Inc.'"
  - "08-09: UI-SPEC S2 amended: the guidance placeholder is hidden while the focused field is empty (WinForms cue text), shows once focus leaves"
patterns-established:
  - "KindLabel / KindLabelLower / KindSentenceLabel give the three casings of each written-set label per variant"
requirements-completed: []
duration: 12min
completed: 2026-10-08
---

# Phase 8 Plan 09: DiscoveryUiText, DiscoveryRowInfo and RegenerateDiscoveryDialog Summary

All Phase 8 copy now lives in one pure, tested static class, `DiscoveryUiText`. A shared `DiscoveryRowInfo` row model feeds both the Documents grid and the Case Summary row. The Regenerate Document dialog fits at 144 DPI with every optional line shown.

## Performance

- **Duration:** about 12 min
- **Completed:** 2026-10-08
- **Tasks:** 2
- **Files:** 4 created, 2 modified

## Accomplishments

- **`DiscoveryUiText`** covers the full UI-SPEC copy:
  - S1: the header, the notice for each memo/corpus combination, the no-guide card and the card captions.
  - Selection and cap hints, side labels, the witness tooltip and the pages copy.
  - Kind labels for every variant, the counts hint, and the invalid, raised and lowered (2 and 3 argument) count sentences.
  - The memo note, party labels and hints, the spent hint and short titles.
  - `ResolveState`, with precedence Writing > Failed > Not saved > File missing > Out of date > Warnings > Generated.
  - The status cell, full status and tooltip for every row state, plus `ViolationText` for all 11 `DiscoveryRule` members and `StaleWhat`.
  - `EstimateCondition` with `EstimateLine` and the tooltip line, the six status-line states, the file messages and the button captions.
  - S3: `CD1CancelGeneration` and `CD2CloseDuringRun`.
  - S2: all dialog copy.
  - S4: `SummaryRow`, with every problem clause and tone.
- **`DiscoveryRowInfo`** uses public fields, like `BlindReviewRowInput`.
- **`RegenerateDiscoveryDialog : ThemedDialog`** follows the `RegenerateTabDialog` construction. It shows a headline, body, settings line (Small, TextMuted, bottom 12), caption, a 3-line `TextFieldHost` (`ScrollBars.None`, `MaxLength = 1_000`), the file-open warning (Small, `Theme.Warning`, top 12, only when the file is open), and the estimate (top 12, bottom 16). Its buttons are Cancel (Default) and Regenerate Document (Primary). Initial focus goes to the guidance field. `Guidance` trims the text and cuts it to 1,000 characters. All labels come from `CreateWrappingLabel`, which sets `UseMnemonic = false` (T-08-26). The dialog creates no `new Font(...)`.

## Task Commits

1. **Task 1 RED:** `98b0b98`, test(08-09): failing DiscoveryUiText tests plus the DiscoveryRowInfo view model
2. **Task 1 GREEN:** `6dd0bb3`, feat(08-09): DiscoveryUiText with all Phase 8 copy
3. **Render-found fix:** `210891e`, fix(08-09): no double period when a party name ends the settings line
4. **Task 2:** `f97f720`, feat(08-09): RegenerateDiscoveryDialog with 144-DPI render tests (plus the UI-SPEC S2 focus note)

## Verification

- Both `-warnaserror` builds are clean (app and test project, `--artifacts-path obj/gsd-artifacts`): 0 warnings, 0 errors.
- `DiscoveryUiTextTests`: 142 tests and theory rows pass. They cover plurals for n = 0/1/2/1000 across all 8 templates, precedence, every row status, all violation rules, the four estimate conditions, status-line clause joins, the dialog copy, every Summary row clause and tone, and the reflection guard for "fictional" and "evidence".
- `Category=Render&FullyQualifiedName~DialogRenderTests`: 28 of 28 pass. That includes the 2 new render cases and the truncation fact (a 1,500-character input comes back as 1,000).
- The acceptance greps all match.

## 144-DPI PNG Inspection (D-03)

I inspected `dialog-regenerate-deposition.png` and `dialog-regenerate-written.png` (in `obj/gsd-artifacts/bin/ReelDiscovery.Tests/debug/renders/`) by eye:
- **Deposition, with every optional line:** "Regenerate Deposition of Michael Scott?" fits. The long file name wraps inside the body and inside the amber file-open warning. The settings line reads "25 testimony pages, deposed June 10, 2025." Nothing is clipped, and the dialog is about 695 px tall, well inside the working area.
- **Written set:** "Regenerate Requests for Production (25)?" fits. The settings line wraps cleanly over two lines.
- **Both:** Cancel and Regenerate Document sit on one row, right-aligned, with exactly one Primary (blue) button and no ellipsized caption. The automated clipping detector reports nothing. A test asserts that the buttons share a row, read left to right, and that every label has `UseMnemonic = false`.
- **Found in the first render:** "Harmon Construction Group, Inc.." had a double period. Fixed in `210891e`, re-rendered, and now clean.
- **Placeholder:** the guidance placeholder does not show because the field has focus, and WinForms hides cue text in a focused TextBox. The UI-SPEC asks for both the placeholder and initial focus, and both cannot be visible at once. I kept the focus rule (it is the focal point) and amended UI-SPEC S2 to say the placeholder appears once focus leaves the field.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Double period in the written-set settings line**
- **Found during:** the Task 2 PNG inspection
- **Issue:** `SettingsLineWritten` always added "." after the "served on" party, so a party ending in "Inc." produced "Inc..".
- **Fix:** an `EndSentence` helper adds the period only when one is missing. Added a test row.
- **Files modified:** UserControls/DiscoveryUiText.cs, ReelDiscovery.Tests/Discovery/DiscoveryUiTextTests.cs
- **Commit:** 210891e

**2. [UI-SPEC amendment] Guidance placeholder vs initial focus**
- **Issue:** a focused, empty WinForms TextBox does not paint its placeholder, so S2's placeholder and initial focus cannot both show on open.
- **Fix:** kept the focus rule and documented the behavior in 08-UI-SPEC S2.
- **Commit:** f97f720

### Additions beyond the plan's list (all copy the later plans name)

- `StepTitle`, `OpenFolderButton` and `CapHint` (named by 08-14), `LoadingText` (UI-SPEC [DEFAULT] `Loading...`) and `GuidanceCaption`.
- `KindVisible`, `KindLabelLower` and `KindSentenceLabel`, so the step can build the invalid, raised and lowered hints from one source.
- A public `Plural` wrapper.

## Known Stubs

None.

## Threat Flags

None. T-08-26 is mitigated: every dialog label uses `UseMnemonic = false`, and a test asserts it. T-08-27 is mitigated: `MaxLength` plus code truncation to 1,000 characters.

## Self-Check: PASSED
- All 4 created files exist.
- Commits 98b0b98, 6dd0bb3, 210891e and f97f720 are in `git log`.
