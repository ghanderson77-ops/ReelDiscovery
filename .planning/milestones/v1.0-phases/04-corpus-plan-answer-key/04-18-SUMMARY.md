---
phase: 04-corpus-plan-answer-key
plan: 18
subsystem: ui-review-plan
tags: [winforms, review-plan, approval, answer-key, dpi, render-tests]
requires:
  - phase: 04-08
    provides: CorpusPlanStore, PlanSummary, GenerationCostEstimator
  - phase: 04-11
    provides: CorpusUiText (entry decision, banner, tiles, stage text, outcomes, Next gate, C-P1)
  - phase: 04-12
    provides: StatTile, MonthHistogram, ReplanDialog
  - phase: 04-16
    provides: CorpusPlanner.PlanAsync with progress and cancel
  - phase: 04-17
    provides: WizardState.SuppressAutoPlanOnce, corner-inset card pattern
  - phase: 04-22
    provides: CorpusApproval (ApproveAsync, ApplyNewPlanAsync, staleness and keep-plan rules)
provides:
  - "StepReviewPlan : UserControl, IWizardStep (Review Plan, UI-SPEC S2) with IsPlanning and CancelAndWaitForRun(TimeSpan)"
  - "internal LoadForRender(state, ReviewPlanRenderState, selectedTable, stage, done, total, findings, reason)"
  - "CorpusApproval.RecordApproval keeps a kept stale guide dismissed after approval"
affects: [04-19 wizard registration and C-P2, 04-20 Case Summary corpus rows]
tech-stack:
  added: []
  patterns:
    - "Full-bleed card: Padding (1, R, 1, R) with header margins reduced by R, so grids and the histogram never cover the rounded corners"
    - "Count grids: int cell values with N0 format, SortCompare on int, sort glyph hidden so right-aligned headers stay on the 16 px line"
    - "Render-test state checks run inside the arrange callback (Visible and grid rows are lost once the render form is disposed)"
key-files:
  created:
    - UserControls/StepReviewPlan.cs
    - UserControls/StepReviewPlan.Run.cs
    - ReelDiscovery.Tests/Render/CorpusReviewPlanRenderTests.cs
  modified:
    - Services/Corpus/CorpusApproval.cs
    - ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs
key-decisions:
  - "Approving a plan kept against a changed guide dismisses the current guide hash (RecordApproval), so Keep This Plan then Approve Plan stays approved on the next entry"
  - "A failed or cancelled run over an existing plan re-shows that plan (with its banner) and reports in the status line; with no plan it shows the Failed / No plan card"
  - "Checks card height is computed for exactly two whole findings (about the UI-SPEC's 152 px) and folds to its caption line when the body would drop below 160 px"
  - "Count-grid sort glyphs are hidden: they push right-aligned numeric headers off the Content x line (rule 15)"
  - "Not relevant columns are 108 logical (UI-SPEC 96) with one-line headers; at 96 the header wrapped and drew 30 px off the numbers"
metrics:
  duration: 13min
  completed: 2026-10-02
---

# Phase 4 Plan 18: Review Plan Step Summary

**Review Plan step that runs planning with a stage checklist and cancel, shows the plan (7 tiles, stacked month histogram, Issue / Key fact / Custodian counts, Checks), handles the stale-guide and settings banners, and on Approve writes answer-key.csv, answer-key.xlsx and the review protocol through CorpusApproval. Verified at 144 DPI in 8 states at Large and WizardMin.**

## What was built

- **Layout (`StepReviewPlan.cs`)**: a root TLP with six commented rows: header, banner (Absolute, sized in `SizeBanner`), tiles, body, Checks and status strip.
  - Header: `Re-plan...`, `Approve Plan` and a painted approval badge (dot plus `Approved {MMM d, h:mm tt}`). Their visibility follows the UI-SPEC header table.
  - Tiles: seven `StatTile`s in equal columns, 4 + 4 apart. The outer tiles sit flush with the cards below.
  - Body: the histogram card (caption plus a Relevant / Not relevant legend) and the counts card (three compact toggles over three grids, with the default sorts from the spec), or a full-width state card. The state card has a 5-row stage checklist that paints its dots and `6 of 17`.
  - Checks card: Where / Check / How to fix. Double-click or Enter on a fact, issue or custodian finding jumps to that table row.
  - Status strip: status label, progress bar and `Cancel Planning`.
- **Run side (`StepReviewPlan.Run.cs`)**: UI glue over `CorpusApproval`.
  - Entry follows `CorpusUiText.ReviewPlanEntryDecision`. `StaleBanner` withdraws approval through `WithdrawApprovalIfStale` and saves. `SuppressAutoPlanOnce` is never reset on entry; `Plan Corpus` / `Retry Planning` clear it.
  - A planning run calls `CorpusPlanner.PlanAsync` with progress and the cancel token. A successful plan goes through `CorpusApproval.ApplyNewPlanAsync` (plan file before case.json, stage set to ReviewPlan), then the case saves.
  - Outcomes: fatal, refused, failed, save-failed, cancelled and an unexpected exception each have their own status or card. `OperationCanceledException` is caught before `Exception`.
  - Re-plan opens `ReplanDialog`. Keep This Plan calls `KeepStalePlan` / `KeepSettingsChange`.
  - Approve calls `CorpusApproval.ApproveAsync`. A file open elsewhere and other write failures get different lines, and nothing is approved on failure.
  - Next gate: M-P1, and M-P2 (default Yes, which approves and then continues).
  - `CancelAndWaitForRun` and `Dispose` cancel planning that is still running.

## Tasks / Commits

| Task | Name | Commit |
|------|------|--------|
| 1 | Review Plan layout and display | 35c3523 |
| 2 | Lifecycle, planning run, staleness, re-plan, approve, Next gate | 5c065c6 |
| 2 (Rule 1) | Keep a kept stale plan current after approval | 51b3f45 |
| 3 (render fixes) | One-line count headers, unclipped stage progress | e101031 |
| 3 | 144-DPI renders of states 5-11 | ac83633 |

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- `dotnet test --filter "FullyQualifiedName~CorpusApprovalTests|FullyQualifiedName~ReviewPlanLogicTests"`: 74 passed (this includes the new `Approve_AfterKeepingStalePlan_StaysCurrent`).
- `dotnet test --filter "Category!=Render"`: 1955 passed.
- `dotnet test --filter "Category=Render&FullyQualifiedName~CorpusReviewPlanRenderTests"`: 8 passed at both sizes. Each render asserts the PNG exists, DPI >= 120, empty clipping, one tile height, and that the first tile's left edge and last tile's right edge match the cards. The rightmost header control (Approve or badge) must end at the cards' right edge. The selected grid must be at least as tall as its header, and at Large no count grid may show a horizontal scrollbar. The stale state asserts Approve is disabled.
- Acceptance greps:
  - `StepTitle => "Review Plan"`: 1
  - `GridStyle.Apply`: 5
  - `new StatTile` / `new MonthHistogram`: 1 / 1
  - `ChecksHint`: present
  - Row comments: 22
  - `Color.Red|SystemColors|new Font(`: 0
  - `ApplyNewPlanAsync` / `ApproveAsync`: 1 / 1
  - Direct `Plan =` / `Approved =` / `WriteAtomicAsync` in the step: 0
  - `ReviewPlanEntryDecision`: 1
  - `SuppressAutoPlanOnce = false`: 1

## Render inspection (144 DPI, every review-plan-* PNG opened)

- Approve Plan and the badge end exactly at the cards' right edge. The tiles line up with the histogram card's left edge and the counts card's right edge. Every card corner is round: the histogram, counts and Checks cards use the corner inset, so the deferred-items 04-12 square-corner item does not appear on this screen.
- Numeric headers and cells end on the same line, 16 px from the card edge. Before the fix, `Not relevant` wrapped and drew about 30 px off the numbers.
- The table toggles read as one segmented control: equal width and height, 4 px apart. No button text is ellipsized and there are no grey stock surfaces.
- Histogram labels never overlap or clip. The 51-bucket version labels every 6 months.
- At WizardMin with a banner and findings, Checks folds to `Make the window taller to see the list.`, the card headers stay whole, and the grid shows one row and scrolls. Only tile sub-lines and the long temp-path subtitle ellipsize, which the UI-SPEC allows.
- Before the fix, the planning checklist showed `6...`. It now reads `6 of 17`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Keep This Plan followed by Approve Plan withdrew approval again on the next entry**
- **Found during:** Task 2 (wiring Keep This Plan and Approve)
- **Issue:** `CorpusApproval.RecordApproval` cleared `DismissedStaleGuideHash`, so after the user kept a stale plan and approved it, `IsGuideStale` turned true again. The next Review Plan entry then withdrew the new approval, and Case Summary showed the plan as out of date.
- **Fix:** Approval now stores the current guide hash as dismissed when the plan was built from a different guide, and leaves it empty otherwise. Added a regression test.
- **Files modified:** Services/Corpus/CorpusApproval.cs, ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs
- **Commit:** 51b3f45

**2. [Rule 1 - Bug] `Not relevant` header wrapped and was misaligned; stage progress was ellipsized**
- **Found during:** Task 3 render inspection
- **Fix:** Column width is now 108 logical with one-line headers. StageRow now measures the progress text without `EndEllipsis`.
- **Files modified:** UserControls/StepReviewPlan.cs
- **Commit:** e101031

**3. [Rule 2 - Card corners] Full-bleed cards use the 04-17 corner inset** (as the orchestrator asked). The card padding is `(1, 8, 1, 8)` logical, and the header and caption margins are reduced so the text stays at 15/14 from the border.

**4. Minor spec adjustments**
- The Checks row is sized for two whole findings, not a fixed 152 px, so no half rows show.
- Sort glyphs are hidden on the count grids, because they push right-aligned headers off the Content x line.
- A plain planning failure over an existing plan reports `The new plan was not built: {reason}. The previous plan is still in place.` (Danger). The UI-SPEC has no line for this case.
- Render-test state assertions run while the form is shown, as in 04-17.
- Task 1 committed a stub `StepReviewPlan.Run.cs` so the commit builds; Task 2 replaced it.

## Requirements

PLAN-07, KEY-01 and KEY-05 are not marked complete. The step is not registered in the wizard until 04-19, so the user cannot reach the summary or approval yet. PLAT-03 is shared across plans and also stays open.

## Known Stubs

None. Every tile, grid and banner is filled from the live plan, guide and settings.

## Threat Flags

None. The only file writes go through `CorpusApproval` (constant names in the verified case folder, T-04-55/56/57). Planning is cancellable and is cancelled on dispose (T-04-58). A stale guide never re-plans by itself (T-04-59).

## Self-Check: PASSED
