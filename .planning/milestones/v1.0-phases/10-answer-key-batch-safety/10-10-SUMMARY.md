---
phase: 10-answer-key-batch-safety
plan: 10
subsystem: ui/answer-key
tags: [answer-key, relabel, D-06, D-07, D-08, QA-01]
requires:
  - "10-03 (AnswerKeyFiles.PeekRelabels)"
  - "10-08 (latest StepGenerateCorpus Start Over edits)"
  - "10-09 (relabelsKept / relabels copy parameters)"
provides:
  - "QaSnapshot.RelabeledDocIds and QaFindingRow.RelabelsKept (D-06)"
  - "Start Over confirm says relabels are kept when the current plan has any (D-07)"
  - "ReplanDialog(bool approved, int generatedThreads = 0, int relabels = 0) and the Corpus Settings notice count (D-08)"
  - "StepCorpusSettings.RelabelCount(CaseDraft)"
affects:
  - "Phase 10 verification: ROADMAP SC1 warning half"
tech-stack:
  added: []
  patterns:
    - "Relabel counts come from PeekRelabels inside Task.Run; the UI only receives the count or DocID set"
key-files:
  created: []
  modified:
    - UserControls/StepCaseSummary.cs
    - Forms/QaFindingsDialog.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - Forms/ReplanDialog.cs
    - UserControls/StepReviewPlan.Run.cs
    - UserControls/StepCorpusSettings.cs
    - ReelDiscovery.Tests/Qa/CaseSummaryQaTests.cs
    - ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
    - ReelDiscovery.Tests/Render/DialogRenderTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusSettingsStepLogicTests.cs
decisions:
  - "[Phase 10] Corpus Settings counts relabels after EnterCore and refreshes only the notice label (plus SizeNoticeRow), so user edits in other controls are not overwritten"
  - "[Phase 10] Re-plan reads the relabel count before opening its dialog, with an _isOpeningReplan re-entry guard, and re-checks the planning, approving and plan-identity guards after the await"
metrics:
  duration: "~20 min"
  completed: 2026-10-12
  tasks: 2
  files: 11
---

# Phase 10 Plan 10: Relabel counts feed the keep/drop copy Summary

Regenerate Thread, Start Over, Re-plan and the Corpus Settings notice now use real relabel counts from `AnswerKeyFiles.PeekRelabels`. These counts are read off the UI thread. Regenerate Thread and Start Over say relabels are kept, and a re-plan warns how many will be dropped.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | Regenerate Thread and Start Over "relabels kept" from real counts (D-06, D-07) | c1e95cd |
| 2 | Re-plan dialog and settings notice warn with the relabel count (D-08) | fcf0cde |

## What was built

- **Case Summary QA (D-06)**
  - `LoadQaWithDiscovery` is already off the UI thread. It now fills `QaSnapshot.RelabeledDocIds` from `PeekRelabels` when there is a folder and a plan.
  - `DescribeThread` sets `QaFindingRow.RelabelsKept` when any email or attachment DocID of the thread is relabeled.
  - `QaFindingsDialog` passes `relabelsKept: selected.RelabelsKept` to `CQ1RegenerateThread`.
- **Start Over (D-07)**
  - The relabel count is part of the existing `Task.Run` tuple. It is 0 when there is no plan.
  - The confirm passes `relabelsKept: relabels > 0`.
  - The `_isResolvingBatches` and other guards are unchanged.
- **Re-plan (D-08)**
  - `ReplanAsync` counts relabels in `Task.Run` and passes the count to `ReplanDialog`.
  - After the await it re-checks `IsDisposed`, `_isPlanning`, `_isApproving` and whether the plan is still the same.
- **Corpus Settings (D-08)**
  - `RelabelCount(CaseDraft)` is a static helper. On entry it runs off the UI thread.
  - `_relabelCount` resets to 0 on each entry. When the count is above 0, only `_lblNotice.Text` and `SizeNoticeRow()` are refreshed.
  - `NoticeText()` is now the single place that calls `SettingsNotice(..., relabels: _relabelCount)`.

## Verification

- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror` passed with 0 warnings and 0 errors before each commit.
- Task 1 filter (CaseSummaryQa, QaFindingsDialog, GenerateCorpusStep): 92 passed.
- Task 2 filter (DialogRender, CorpusSettingsStepLogic, ReviewPlanLogic, CorpusSettingsRender): 150 passed.
- Plan verification filter: 218 passed.
- All grep acceptance counts match.
- Line endings are preserved: CRLF files stayed CRLF and LF files stayed LF.

## DPI note

Render PNGs on this machine are **120 DPI**. `replan-dialog-relabels.png` and `replan-dialog-relabels-unapproved.png` were inspected:
- the body wraps cleanly;
- the "N manual relabels in the answer key will be dropped." sentence is fully visible;
- buttons are not clipped.

**A 150% check is still needed** for:
- the Re-plan dialog body;
- the Corpus Settings notice label with the relabels clause;
- the Start Over and Regenerate Thread message boxes. These are stock MessageBox, so the risk is low.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Re-entry guard on Re-plan during the relabel count**
- **Found during:** Task 2
- **Issue:** The new `await` happens before the dialog opens. A second Re-plan click during it could open a second dialog.
- **Fix:** Added an `_isOpeningReplan` flag, set around the count. The plan identity is also re-checked after the await.
- **Files modified:** UserControls/StepReviewPlan.Run.cs
- **Commit:** fcf0cde

**2. [Rule 3 - Blocking] Nullable warning after moving the notice text into `NoticeText()`**
- **Found during:** Task 2
- **Issue:** `Draft.Corpus!` used to sit on the line that moved into the helper. This left a CS8602 warning on the next line in `FillControls`.
- **Fix:** Added `!` on `ShowsSkipToDiscovery(Draft.Corpus!.HasPlan, ...)`.
- **Commit:** fcf0cde

## TDD Gate Compliance

Each task is one `feat` commit containing both tests and code. The plan requires every commit to build with -warnaserror, and the new tests do not compile until the new members exist (the same precedent as 10-09). There is no separate RED commit.

## Known Stubs

None.

## Self-Check: PASSED
