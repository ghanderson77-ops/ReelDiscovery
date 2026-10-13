---
phase: 04-corpus-plan-answer-key
plan: 15
subsystem: ui
tags: [case-summary, answer-key, review-protocol, winforms, render, dpi]
requires:
  - phase: 04-01
    provides: CorpusState, WrittenCorpusFile, CorpusFileKind
  - phase: 04-06
    provides: AnswerKeyCsvWriter.FileName / AnswerKeyXlsxWriter.FileName
  - phase: 04-07
    provides: ReviewProtocolBuilder.FileNameFor
  - phase: 04-08
    provides: CorpusPlanStore.EnsureLoaded, PlanSummary.From
  - phase: 04-11
    provides: CorpusUiText Case Summary lines (CorpusPlanLine, PlanStatusLine, AnswerKeyLine, ProtocolLine, FileStatusLine, MissingFileText)
  - phase: 04-22
    provides: CorpusApproval.IsGuideStale
provides:
  - Case Summary Corpus group (Corpus plan, Answer key, Review protocol rows) above a Case guide group
  - StepCaseSummary pure helpers FileStatusFor, CombinedStatus, CorpusDotState, PlanDotState, PlanRowStale, WrittenLocal
  - CreateRow optional buttonCaptions + ContentRow.Buttons list
  - CorpusUiText.AnswerKeyFilesLine
affects: [04-19]
tech-stack:
  added: []
  patterns: [group headings inside the Contents rows TLP, left-margin button spacing so the right edge stays aligned when buttons hide]
key-files:
  created: []
  modified:
    - UserControls/StepCaseSummary.cs
    - UserControls/CorpusUiText.cs
    - ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
key-decisions:
  - "Answer key row is Written only when both answer-key.csv and answer-key.xlsx exist with entries for the current plan (CombinedStatus: weakest state wins)"
  - "An unreadable plan (EnsureLoaded reason) shows the reason in Danger with a Danger dot and Review Plan (which shows the unreadable card)"
  - "A divider sits above both group headings (separating Case file from the Corpus group); a heading joins its first row without a divider"
  - "Row buttons get an 8 px left margin after the first and none on the right, so the last visible button always ends at the row inset"
requirements-completed: []
duration: 15min
completed: 2026-10-02
---

# Phase 4 Plan 15: Case Summary Corpus Group Summary

**Case Summary now leads its Contents card with a Corpus group (Corpus plan, Answer key, Review protocol) showing approved/stale/written states from CorpusApproval.IsGuideStale and WrittenFiles, with Review Plan / Plan Corpus navigation and Open CSV / Open in Excel / Open in Word from the verified case folder.**

## Performance

- **Duration:** ~15 min
- **Completed:** 2026-10-02
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Rows order per UI-SPEC S3: Case file, `Corpus` heading, Corpus plan / Answer key / Review protocol, `Case guide` heading, then the Phase 3 rows unchanged.
- Corpus plan row: plan line from PlanSummary (emails, threads, relevant %, first/last month), status line Approved (Success) / Not approved yet (muted) / Out of date with the case guide (Warning) via `CorpusApproval.IsGuideStale` (a kept plan counts as current). No plan: `Not planned yet` + `Plan Corpus`.
- Answer key and Review protocol rows: counts lines, status by `FileStatusFor` (Written / Written for an earlier plan / Not written yet), buttons visible only when the file exists.
- Open handlers build paths only from `Draft.FolderPath` (verified to exist) plus constant names; a missing file refreshes and reports `MissingFileText` in Danger (T-04-45). All new labels `UseMnemonic = false`.
- Rows host `ClientSizeChanged` keeps the rows TLP at client width; renders confirm no horizontal scrollbar at Large and WizardMin.
- Existing Case Summary renders moved from Small to WizardMin; new `Summary_NoCorpusPlan` and `Summary_CorpusApproved` renders (both sizes).

## Task Commits

1. **Task 1: Corpus group rows, status logic and file opening** - `9297ba5` (feat)
2. **Task 2: Case Summary renders at 144 DPI (no plan; approved with files)** - `f1dba7f` (test)

## Files Created/Modified
- `UserControls/StepCaseSummary.cs` - Corpus group rows, group headings, pure status helpers, RefreshCorpusRows, OpenCorpusFile, navigation buttons
- `UserControls/CorpusUiText.cs` - `AnswerKeyFilesLine` constant (answer key line 2 before there is a plan)
- `ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs` - FileStatusFor (5 cases), CombinedStatus, dot states, WrittenLocal, PlanRowStale_DismissedHashCountsAsCurrent
- `ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs` - WizardMin sizes, no-plan and approved renders with HorizontalScroll.Visible assertions

## Render Inspection (144 DPI)
Opened case-summary-corpus-approved (1100x750, 1000x540), case-summary-no-corpus-plan (1000x540) and case-summary-after-export (1100x750): group headings align at Content x with the row dots, row buttons share one right edge (single and paired buttons), no ellipsized captions, no grey stock surfaces, protocol file name fits at Large. Card chrome is unchanged from the pre-change render.

## Deviations from Plan

### Auto-fixed / adjusted

**1. [Rule 2 - Missing functionality] Answer key line 2 with no plan**
- **Issue:** `AnswerKeyLine` needs plan counts; with no plan the row would read "0 rows (0 emails, 0 attachments)".
- **Fix:** Added `CorpusUiText.AnswerKeyFilesLine` ("answer-key.csv and answer-key.xlsx") used when no plan is loaded (keeps all text in CorpusUiText).
- **Files modified:** UserControls/CorpusUiText.cs
- **Commit:** 9297ba5

**2. [Rule 2 - T-04-46] Unreadable plan state**
- **Issue:** Plan only covered no plan / plan; a case.json PlanId whose file cannot be read needed a visible state.
- **Fix:** Shows `PlanLoadResult.Reason` in Danger with a Danger dot and the Review Plan button.
- **Commit:** 9297ba5

**3. Divider above the first group heading (UI-SPEC says none)**
- **Issue:** The first heading is not at the top of the list (Case file comes first); without a divider the Case file row visually merges into the Corpus group, and the two headings would be treated inconsistently.
- **Fix:** A divider sits above both headings; no divider between a heading and its first row. Easy to revert (one condition in InitializeUI) if the literal spec is preferred.
- **Commit:** 9297ba5

**4. Helper shapes**
- `CorpusDotState(CorpusFileStatus)` and `PlanDotState(hasPlan, approved, stale)` return Theme member names as planned; added `CombinedStatus` and `WrittenLocal` for the two-file answer key row and its timestamp.

## Known Stubs
None. The Review Plan / Plan Corpus buttons call `WizardForm.NavigateToStage`; the stage-to-step mapping for CorpusSettings/ReviewPlan lands in 04-19 (as the plan states), so until then they are no-ops in the running wizard.

## Requirements
KEY-01, KEY-05, PLAN-07 and PLAT-03 are not marked complete: this plan only surfaces the files on Case Summary; the user-facing approve flow (Review Plan in the wizard, 04-19) is still pending.

## Self-Check: PASSED
- FOUND: UserControls/StepCaseSummary.cs, ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs, ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
- FOUND: 9297ba5, f1dba7f
- Non-render suite: 1881 passed; CaseSummaryRenderTests: 5 passed
