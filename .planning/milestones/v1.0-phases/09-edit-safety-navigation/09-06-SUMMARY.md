---
phase: 09-edit-safety-navigation
plan: 06
subsystem: case-guide
tags: [w1-dependency-notice, case-guide, banner, render]
requires:
  - "09-01: CaseGuideUiText.DependencyNotice, BannerVariant.PlanDependency"
  - "09-05: save-funnel approval withdrawal (keeps the notice current after an edit)"
provides:
  - "Case Guide header row 2 dependency line (muted Info / Warning Changed), wraps beside Resume Guide"
  - "Lowest-priority PlanDependency banner with a Default Review Plan button that opens Review Plan"
affects: [UserControls/StepCaseGuide.cs]
tech-stack:
  added: []
  patterns: ["wrap width for a header label measured from the page width less the header button flow (same as SizeBanner)"]
key-files:
  created: []
  modified:
    - UserControls/StepCaseGuide.cs
    - ReelDiscovery.Tests/Render/CaseGuideRenderTests.cs
decisions:
  - "The Changed dependency line shows only while StaleCore / StaleDownstream / NeedsReview holds the banner row; otherwise the PlanDependency banner carries it"
  - "Review Facts / Mark All Reviewed now show only for the NeedsReview variant (previously any non-stale variant)"
metrics:
  duration: "~6 min"
  completed: 2026-10-11
  tasks: 2
  files: 2
---

# Phase 09 Plan 06: Case Guide Plan Dependency Notice Summary

The Case Guide step now warns when a corpus plan or generated emails depend on the guide. Before an edit it shows a muted header line. After an edit it shows a lowest-priority Warning banner with a Default `Review Plan` button. If a stale or needs-review banner already holds the row, it shows a Warning header line instead. Staleness comes from `CorpusApproval.IsGuideStale`, and email counts come from the cached `GenerationState`.

## Tasks

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Header dependency line and PlanDependency banner | 6c00f3a | UserControls/StepCaseGuide.cs |
| 2 | Render tests for the three notice states (Large + WizardMin) | 81781d6 | ReelDiscovery.Tests/Render/CaseGuideRenderTests.cs |

## What was built

- `headerLayout` has `RowCount = 3` with the row comment `// 2: Dependency line`. `_lblDependency` uses SmallFont, AutoSize, no ellipsis, a 4 px top margin and is hidden when empty. `SetRowSpan(headerButtons, 3)`.
- `SizeDependencyLine()` runs from `SizeBanner()` on every refresh and resize. It sets `MaximumSize.Width` to the page width minus the Resume Guide flow and its margin, so the line wraps before the button.
- `_btnBannerReviewPlan` (Default) is the last button in the banner flow. It calls `NavigateToStage(CaseStage.ReviewPlan)`, never takes focus, and is disabled while busy (`SetBusy` and `RefreshBanner`).
- `RefreshBanner()` maps the decision table: `PlanDependency` is used only when no other variant is showing. The line is empty when the banner carries the Changed notice. The line is `Theme.Warning` for Changed and `Theme.TextMuted` for Info.
- The notice is current when the step opens: `OnEnterStepAsync` calls `RefreshAll` -> `RefreshShell` -> `RefreshBanner`.

## Verification

- `dotnet build ReelDiscovery.Tests.csproj -warnaserror`: green after each task.
- `CaseGuideUiLogicTests|CaseGuideStepLogicTests`: 126 passed.
- `Category=Render&FullyQualifiedName~CaseGuideRenderTests`: 14 passed (the 11 existing tests unchanged).
- `FullyQualifiedName~CaseGuide`: 538 passed.
- Acceptance greps: DependencyNotice( 1, `// 2: Dependency line` 1, `SetRowSpan(headerButtons, 3)` 1, `NavigateToStage(CaseStage.ReviewPlan)` 1, `BannerVariant.PlanDependency` 2, the Review Plan CreateButton 1, three test names 3.

### PNG inspection (rules 43-44)

The six PNGs were rendered at **120 DPI** (this machine's display) and checked by eye.
- `case-guide-plan-dependency-{large,wizardmin}`: Warning border and heading. At WizardMin the body wraps to 3 lines beside `Review Plan` (Default, vertically centered), and the last line is not clipped. There is no header line.
- `case-guide-stale-core-dependency-{large,wizardmin}`: the StaleCore banner (Update Guide Primary, Not Now) is shown, with a one-line Warning-tone header line under the subtitle. The header grows and the banner moves down with no overlap.
- `case-guide-dependency-info-{large,wizardmin}`: the muted line sits beside a visible `Resume Guide`. It fits on one line at Large and wraps to 2 lines at WizardMin, ending left of the button with no ellipsis. There is no banner.
- Warning tone appears only on the banner and the Changed line.

**Human check still needed:** the same six states at 150% (144 DPI), per the phase DPI rule.

## Deviations from Plan

- The PNG file names carry a size suffix (`-large` / `-wizardmin`) so both sizes are kept. Otherwise the harness overwrites the first PNG with the second.
- The Info render test clears Canonical Values (Pending) so that `Resume Guide` is visible. This lets the "never overlaps Resume Guide" assertion actually exercise the button.
- TDD ordering: the plan puts the implementation (Task 1) before the render tests (Task 2), so the render tests passed on their first run. There was no RED commit. The tests assert the new visible behavior, which did not exist before 6c00f3a.

## Known Stubs

None.

## TDD Gate Compliance

Task 2 is `tdd="true"`, but it is ordered after its implementation task in the plan. The `test(09-06)` commit follows the `feat(09-06)` commit, so there is no RED-first gate.

## Self-Check: PASSED
