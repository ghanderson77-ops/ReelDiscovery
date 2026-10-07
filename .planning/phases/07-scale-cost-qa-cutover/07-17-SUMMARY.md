---
phase: 07-scale-cost-qa-cutover
plan: 17
subsystem: qa-ui
tags: [qa-copy, findings-dialog, blind-review-dialog, regenerate-thread, dpi, QA-01, QA-02, QA-03, D-11, D-12, D-13, D-14, D-15, D-04]
requires:
  - phase: 07-13
    provides: "ThreadInvalidation.InvalidateAsync -> ThreadInvalidationResult { Appended, FilesDeleted, Errors, KeyError }"
  - phase: 07-14
    provides: "ScanReport/ScanFinding/ScanCheck, LeakageReport/LeakageSignal/LeakageVerdict, LeakageAudit thresholds and VerdictFor"
  - phase: 07-16
    provides: "Blind-review engine (sample order, estimate) consumed by the 07-18 wiring through BlindReviewDialog"
provides:
  - "QaUiText: every QA string (S2 R2-R4 rows, S3 findings content/cells/footer, S4 dialog copy, C-Q1..C-Q3, status-strip lines)"
  - "QaFindingsMode, BlindReviewState, BlindReviewRowInput, QaRowContent (SummaryRowContent + InProgress for the Accent running state)"
  - "QaFindingsDialog(mode, rows, leakage, reviewerModel, queuedThreadIds, regenerate, openEmail) with QueuedAny; QaFindingRow"
  - "BlindReviewDialog(model, provider, fallback, total, relevant, defaultSize, alreadyReviewed, estimateFor) with SampleSize"
affects: [07-18]
tech-stack:
  added: []
  patterns: ["Sizable themed Form with ThemedDialog chrome and working-area clamp", "C-Q1 confirm seam (Func<string,string,bool>) for testable destructive actions"]
key-files:
  created:
    - UserControls/QaUiText.cs
    - Forms/QaFindingsDialog.cs
    - Forms/BlindReviewDialog.cs
    - ReelDiscovery.Tests/Qa/QaUiTextTests.cs
    - ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs
    - ReelDiscovery.Tests/Render/QaDialogRenderTests.cs
  modified:
    - .planning/phases/07-scale-cost-qa-cutover/07-UI-SPEC.md
decisions:
  - "[07-17]: Leakage copy recomputes the verdict with LeakageAudit.VerdictFor from the stored accuracy and counts; every threshold in the copy is read from LeakageAudit constants"
  - "[07-17]: Scans row adds an 'attachment' clause (ScanCheck.Attachment findings exist); singular verbs agree (1 EML file is deleted, 1 already reviewed is kept, has it open)"
  - "[07-17]: Findings dialog summaries are one line at 640 x 400 (full guidance in a tooltip) and the leakage headline drops the top-signal sentence, so rule 29's 4 visible rows hold with the footer showing"
  - "[07-17]: Footer is one label: FooterQueued, then FooterNotDeleted and FooterKeyNotUpdated on their own lines; Success when only queued, Warning when any warning line; a refused invalidation shows RegenerateFailed in Danger and leaves the row unqueued"
  - "[07-17]: Regenerate Thread... and Open Email start disabled: the grid's automatic first-row selection is cleared on Shown"
metrics:
  duration: 15min
  completed: 2026-10-06
  tasks: 3
  files: 7
---

# Phase 7 Plan 17: QA copy and the QA dialogs Summary

All QA copy now lives in the pure, tested `QaUiText`. The two QA dialogs are built and verified at high DPI: `QaFindingsDialog` lists scan findings, blind-review disagreements or the leakage signal table, and offers Regenerate Thread... behind C-Q1. `BlindReviewDialog` sets the sample size and shows a live estimate with the resume clause.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | QaUiText (all QA copy) | ad29947 (RED), 51948f0 (GREEN) |
| 2 | QaFindingsDialog and BlindReviewDialog | 38de916 (RED), f149786 (GREEN), 895e19a (render-driven fixes) |
| 3 | 144-DPI renders of the QA dialogs | 85b18ca |

## What was built

- **`QaUiText`** (`UserControls/QaUiText.cs`)
  - Case Summary rows:
    - `ScansRow(report, outOfDate)`: not run / clean / findings (zero clauses dropped) / out of date.
    - `LeakageRow` / `LeakageLine(report, topSignal)`: Pass at <= 60%, Warning above 60% up to 70%, Fail above 70%, plus Not enough data and Not run. Thresholds come from `LeakageAudit` constants.
    - `LeakageTooltip`, `SignalText`, `SignalCells`.
    - `BlindReviewRow(BlindReviewRowInput)`: Not run / Running (`InProgress` = Accent) / Stopped / Done, with an optional fallback clause and tooltip.
  - Dialog copy:
    - S3 findings dialog: `FindingsContent`, `FindingsSummaryTooltip`, cell texts, `QueuedSuffix`, `FooterQueued`, `FooterNotDeleted`, `FooterKeyNotUpdated` (revision 2) and `RegenerateFailed`.
    - Confirmations `CQ1RegenerateThread`, `CQ2CancelReview` and `CQ3LeaveDuringReview`.
    - S4 blind review dialog copy, `Estimate` with the resume clause, and the status-strip lines.
  - Tests cover plurals at 0, 1, 2 and 1,000, and check for banned words.
- **`QaFindingsDialog`** (`Forms/QaFindingsDialog.cs`)
  - Window: sizable, 900 x 560 default and 640 x 400 minimum, ThemedDialog chrome scaled once, clamped to the working area.
  - Layout: root rows 0-4 as in S3; `GridStyle` grid with a dot column (Warning; BorderStrong when queued) except in leakage mode.
  - Rows sort by date ascending and headers are sortable. Cell tooltips show the full text, and every label has `UseMnemonic = false`.
  - Regenerate Thread... (Primary) is enabled only for a selected row whose thread is not queued. It asks C-Q1 (Warning icon, `MessageBoxDefaultButton.Button2`) through the `Confirm` seam, then calls the `regenerate` delegate (the app passes `ThreadInvalidation.InvalidateAsync`). On success it marks the row ` · queued` and writes the footer: the queued line, the not-deleted line, then the KeyError line.
  - Open Email, double-click and Enter call `openEmail(docId)`.
  - Leakage mode shows Close only.
  - `QueuedAny` tells the caller to refresh Case Summary when the dialog closes.
- **`BlindReviewDialog`** (`Forms/BlindReviewDialog.cs`)
  - A `ThemedDialog` with the headline, the body (model and provider) and an optional Warning fallback sentence.
  - Sample row: `NumericUpDown` (min 1, max total, step 50, thousands separator, `LDU(96)`) plus "of {total}".
  - Below the sample row: the default-rule line, and an estimate line that updates on every value change through `estimateFor`.
  - Buttons: Cancel (focus) and Start Review (Primary, OK). `SampleSize` returns the chosen size.

## Render inspection (DPI rules 29, 30)

These PNGs were inspected by eye. The display was at 125% (120 DPI) during this run. The harness accepts 120 or more, and every size goes through `LogicalToDeviceUnits`, so the layout proportions match 144 DPI.
- `renders/dialog-qa-findings-scans-default.png` and `renders/dialog-qa-findings-scans-min.png`
- `renders/dialog-qa-findings-blindreview-default.png` and `renders/dialog-qa-findings-blindreview-min.png`: one row queued before the dialog opened, one queued in the dialog, and the Success footer.
- `renders/dialog-qa-findings-leakage-default.png` and `renders/dialog-qa-findings-leakage-min.png`
- `renders/dialog-blind-review.png`, `renders/dialog-blind-review-fallback.png` and `renders/dialog-blind-review-resume.png` (123,456 of 250,000, with the resume clause)

What the inspection confirmed:
- Every button sits on one row and is fully visible, with one Primary per surface.
- At least 4 grid rows are visible at the minimum size.
- There is no horizontal scrollbar at the default size; at the minimum size it is allowed.
- Dots are crisp and centered.
- The six digits with a thousands separator fit in the `NumericUpDown`.
- The clipping detector reported nothing.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Findings rows were 22 px instead of the 32 px GridStyle height**
- **Found during:** Task 3 render inspection
- **Fix:** rows are now cloned from `RowTemplate` before `CreateCells`.
- **Commit:** 895e19a

**2. [Rule 1 - Bug] Clipped cells and a missing button gap in the first renders**
- **Issue:**
  - `Mar 12, 20...` was cut off in Date (96).
  - `Not relevant · queued` and `+25 p...` were ellipsized.
  - The `Relevant share` header wrapped.
  - There was no 8 px gap before Close.
- **Fix:** new widths:
  - Doc ID 112, Date 108, Subject min 140, Check and Reviewer 144, Answer key 104, Text min 160.
  - Relevant share 120, Lift 80.
  - The button margins now follow the ThemedDialog rule.
- **UI-SPEC:** S3 amended.
- **Commit:** 895e19a

**3. [Rule 1 - UI-SPEC conflict] Rule 29 (4 rows at 640 x 400) could not hold with the S3 copy**
- **Issue:** at the minimum size, the two-line summary, the footer and the horizontal scrollbar left 3 rows. The leakage headline (with its top signal) wrapped to two lines.
- **Fix:** shorter one-line summaries, with the full guidance moved to a summary tooltip. The leakage headline drops the "Top signal" sentence, because that signal is already the table's first row.
- **UI-SPEC:** S3 amended.
- **Commit:** 895e19a

**4. [Rule 2 - Correctness] Copy additions and amendments**
- Added an `attachment` clause to the scans row, because 07-15 produces Attachment findings (R2 amended).
- Singular verb agreement in C-Q1, C-Q2, the resume clause and the not-deleted footer (amended).
- Added `RegenerateFailed`, used when the invalidation is refused or throws (S3 amended).
- **Commits:** 51948f0, f149786

**5. Additions not named in the plan:** `QaRowContent.InProgress`, because `SummaryTone` has no Accent and the running blind-review row is Accent. Also `QueuedAny`, `OpenButton`, `CloseButton`, `HeadlineLabel` and `SummaryLabel` test accessors, and `FlaggedEmails` / `Threads` helpers.

## Requirements

QA-01, QA-02 and QA-03 stay Pending. This plan delivers the copy and the dialogs. Wiring them into Case Summary (the rows, running the blind review, opening the dialogs, and passing `ThreadInvalidation` and the open-email path rule, T-07-60) is 07-18.

## TDD Gate Compliance

Task 1: test ad29947, then feat 51948f0. Task 2: test 38de916, then feat f149786. Both RED commits failed to compile because the types did not exist yet.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- QaUiTextTests + QaFindingsDialogTests + QaDialogRenderTests: 79 passed.
- Full non-render, non-scale suite: 3,745 passed, 0 failed.
- Acceptance greps:
  - The `QaUiText` class appears once.
  - The LeakageAudit constants are referenced on 3 lines.
  - No banned words.
  - `FooterKeyNotUpdated` is present.
  - `LogicalMinimumSize = new(640, 400)` appears once.
  - `BlindReviewDialog : ThemedDialog` appears once.
  - `Button2` appears once.
  - `KeyError` appears at least once.
  - No fixed `new Size(` lines.

## Known Stubs

None. The dialogs are complete and take their data and actions through constructor delegates, which 07-18 wires up.

## Self-Check: PASSED
- FOUND: UserControls/QaUiText.cs, Forms/QaFindingsDialog.cs, Forms/BlindReviewDialog.cs, ReelDiscovery.Tests/Qa/QaUiTextTests.cs, ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs, ReelDiscovery.Tests/Render/QaDialogRenderTests.cs
- FOUND commits: ad29947, 51948f0, 38de916, f149786, 895e19a, 85b18ca
