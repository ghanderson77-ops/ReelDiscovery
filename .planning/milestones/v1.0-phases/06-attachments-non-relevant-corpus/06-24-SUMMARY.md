---
phase: 06-attachments-non-relevant-corpus
plan: 24
subsystem: case-summary-and-model-dialog-ui
tags: [winforms, case-summary, model-dialog, media-prices, render-tests, dpi]
requires:
  - "06-04 MediaPrices (Clone / Normalize / Defaults) and WizardState.MediaPrices"
  - "06-13 MailboxesRowInput Phase 6 fields, SummaryRowContent.Line3Tooltip, AnswerKeyRunTooltip"
  - "06-14 GenerationState NonRelevant* / Attachments* cached counts"
provides:
  - "Answer key run clause counts email rows of every thread plus attachment rows; Skipped rows count as neither and appear in the line-3 tooltip ('{s} skipped')"
  - "HasGenerated also true for a run that only wrote non-relevant threads"
  - "StepCaseSummary test accessors MailboxesLine3Tooltip / AnswerKeyLine3Tooltip"
  - "ModelConfigurationDialog(models, prices) overload, MediaPrices property, Media prices row (Image $ per image, Voicemail audio $ per 1M characters); Reset Defaults resets 0.006 / 15.00; OK commits a normalized copy"
  - "StepApiConfiguration.ApplyDialogResult(DialogResult, current, edited): OK -> normalized clone, otherwise current"
  - "Dialog test accessors ImagePriceInput, TtsPriceInput, ModelsGrid, ResetDefaultsForTest, CommitMediaPricesForTest"
affects: [06-25 human render inspection]
tech-stack:
  added: []
  patterns:
    - "Modal dialog results applied through an internal static function so Cancel/OK semantics are testable without ShowDialog"
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/MediaPricesDialogTests.cs
  modified:
    - UserControls/StepCaseSummary.cs
    - UserControls/ModelConfigurationDialog.cs
    - UserControls/StepApiConfiguration.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
    - ReelDiscovery.Tests/Render/DialogRenderTests.cs
decisions:
  - "Answer key planned = plan rows - generated - failed - skipped, where generated = EmailsWritten (all threads) + attachments written/with warnings and failed = EmailsFailed + AttachmentsFailed; computed from the cached GenerationState counts (no separate key projection exists in the step)"
  - "ModelConfigurationDialog was not DPI-aware (fixed 30/45 px rows, fixed 110 px button column, unscaled column widths); fixed minimally with AutoSize header/button rows, an AutoSize grid-button column and LogicalToDeviceUnits widths/margins. Fonts and the legacy 10 px padding unchanged"
  - "The 8-column grid scrolls sideways at the 820 px minimum by design; the render check allows the grid horizontal scrollbar only"
  - "Media prices row uses the grid panel's default cell margin on the left so the outer left edges match (the spec's 'no extra left inset')"
metrics:
  duration: 17min
  completed: 2026-10-04
  tasks: 3
  files: 6
---

# Phase 6 Plan 24: Case Summary Full-Corpus Counts and Media Prices Dialog Summary

The Case Summary answer key row now counts non-relevant and attachment rows (skipped rows go to a tooltip). ModelConfigurationDialog gained an editable Media prices row (image $0.006 per image, voicemail audio $15.00 per 1M characters), and the dialog now scales cleanly at 144 DPI. OK, Cancel, Reset Defaults and clone behavior are covered by tests.

## What was built

- **Task 1 (ea15ea0):** The Mailboxes row wiring (MailboxesRowInput from GenerationState plus the line-3 tooltip) was already in place from 06-13. This task added the Answer key part: the generated and planned counts now cover the full corpus, and the tooltip shows `{s} skipped` when there are skipped rows. `HasGenerated` also counts non-relevant threads. The dialog got its ctor overload, a `MediaPrices` property, the 4-row layout (`// 0: Header` .. `// 3: Buttons`), the media FlowLayoutPanel, the extra header sentence, and Reset Defaults and OK handling for the prices. StepApiConfiguration now opens the dialog with `_state.MediaPrices.Clone()` and stores the result through `ApplyDialogResult`.
- **Task 2 (e7a938d fix, 15768ec test):** Added `MailboxesRow_CompleteClean` and `MailboxesRow_PartialAllClauses`, run at Large and WizardMin, which check the line 3 text, tone, tooltip, answer key clause and answer key tooltip. Added `ModelDialog_MediaPricesRow_MinimumSize` at 820 x 360. It checks that both NumericUpDowns and both labels sit inside the client area, that at least 3 grid rows are displayed, and that the left edges line up within 1 px.
- **Task 3 (4a9070e):** Four behavior facts: Cancel returns the same instance unchanged; OK returns a new clamped copy (10 / 0) and leaves its inputs alone; the dialog edits a clone; Reset Defaults followed by OK stores 0.006 / 15.00.

## Visual inspection (144 DPI PNGs read)

- `dialog-model-config-media-prices-min.png`: header wraps to two lines, all three grid buttons are fully visible, the media row sits on one line under the grid with its left edge aligned to the grid, and OK / Cancel are fully visible. Three grid rows show above the horizontal scrollbar. The first render, before the layout fix, showed a clipped header, clipped Add Model / Reset Defaults buttons, a clipped Cancel and truncated column headers. All of these are fixed.
- `case-summary-mailboxes-partial-1000x540.png` / `-complete-1100x750.png`: line 3 shows in full with the right tone and nothing is clipped.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] ModelConfigurationDialog clipped at 144 DPI**
- **Found during:** Task 2 (first render)
- **Issue:** The legacy layout used fixed pixel sizes. At 144 DPI that clipped the header label, the grid button column (110 px), the Cancel button (45 px row) and the column header text.
- **Fix:** Header and button rows are now AutoSize, with a Percent 100 column so the header wraps. The grid button FlowLayoutPanel is now AutoSize. Column widths and margins are scaled with LogicalToDeviceUnits.
- **Files modified:** UserControls/ModelConfigurationDialog.cs
- **Commit:** e7a938d

**2. [Rule 1 - Bug] CS8603 on the net10 test build**
- **Issue:** `ToolTip.GetToolTip` is annotated nullable on net10, so the new accessors warned.
- **Fix:** Added `?? string.Empty`.
- **Commit:** e54bf70

**3. Mailboxes row wiring already existed** (06-13 commit 7681de5), so Task 1 only added the Answer key and HasGenerated parts to StepCaseSummary.

## TDD Gate Compliance

Task 3 is `tdd="true"`, but Task 1 had to create `ApplyDialogResult` and the dialog accessors first. Its tests therefore passed on the first run, and there is no separate RED commit. The behaviors are still pinned by `test(06-24)` commit 4a9070e.

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- Render filter (CaseSummaryRenderTests | DialogRenderTests): 25/25 passed
- `MediaPricesDialogTests`: 4/4 passed
- Full suite (wave-5 gate): **3300 passed, 0 failed** (baseline 3293 + 7 new)

## Known Stubs

None.

## Self-Check: PASSED
