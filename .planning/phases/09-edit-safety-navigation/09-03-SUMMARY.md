---
phase: 09-edit-safety-navigation
plan: 03
subsystem: corpus-wizard-navigation
tags: [cost-safety, review-plan, wizard, navigation]
requires: []
provides:
  - "WizardState.PlanOnEntryOnce one-shot planning intent"
  - "ReviewPlanEntryInput.PlanRequested entry rule"
  - "PlanNextGateKind.AskSkip (Continue Without a Plan) Next gate"
affects:
  - UserControls/StepReviewPlan.Run.cs
  - UserControls/StepCorpusSettings.cs
  - UserControls/StepCaseScenario.cs
tech-stack:
  added: []
  patterns: ["one-shot read-and-clear session intent flag", "source-order regression test"]
key-files:
  created: []
  modified:
    - Models/WizardState.cs
    - UserControls/CorpusUiText.cs
    - UserControls/StepReviewPlan.Run.cs
    - UserControls/StepCaseScenario.cs
    - UserControls/StepCorpusSettings.cs
    - ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs
    - ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusSettingsStepLogicTests.cs
    - ReelDiscovery.Tests/Render/WizardShellRenderTests.cs
decisions:
  - "Review Plan auto-plans / auto re-plans only when the one-shot PlanOnEntryOnce intent (set only by a successful Corpus Settings Next) is present (09 W2, D-12 kept)"
  - "PlanOnEntryOnce is read and cleared at the very top of Review Plan entry (before the active-run early return), so it is consumed on every entry"
  - "Next on Review Plan with no plan is a Yes/No 'Continue Without a Plan' confirm (default Yes), not a block"
metrics:
  duration: "~10 min"
  completed: 2026-10-11
  tasks: 2
  files: 9
---

# Phase 9 Plan 03: Plan-on-Entry Intent and Continue-Without-Plan Gate Summary

Review Plan now starts a paid planning run only on the one-shot `PlanOnEntryOnce` intent that a successful Corpus Settings Next sets (D-12 kept); Back from Generate Corpus, Case Summary's Review Plan button and reopened cases show the No corpus plan yet card, and Next with no plan asks "Continue Without a Plan" (Yes proceeds) instead of blocking.

## Tasks

| # | Task | Commit |
|---|------|--------|
| 1 | CorpusUiText PlanRequested entry rule and AskSkip Next gate | b3c9f60 |
| 2 | PlanOnEntryOnce flag wired through Corpus Settings, Review Plan and reopen | 637e3f7 |

## What Changed

- `ReviewPlanEntryInput.SuppressAutoPlan` became `PlanRequested`; `ReviewPlanEntryDecision` returns AutoPlan / AutoReplan only when requested, otherwise NoPlanCard / SettingsBanner. Approved plans are still never re-planned silently (T-04-34 test now passes `requested: true`).
- `PlanNextGateKind.NoPlan` became `AskSkip` with the verbatim UI-SPEC S3 title/message; `StepReviewPlan.ValidateStepAsync` shows a YesNo Question box (default Yes) and returns `answer == Yes`.
- `WizardState.SuppressAutoPlanOnce` became `PlanOnEntryOnce`; `StepCorpusSettings.ClearsAutoPlanSuppression` became `RequestsPlanning` and sets the flag; `MarkOpenedFromDisk` clears it; `PlanFromStateCardAsync` no longer touches the flag.
- Old-behavior tests were updated/renamed in place (none deleted); added `ReviewPlanEntry_ClearsPlanOnEntryOnceBeforeDeciding` source-order test. The shell render test for Review Plan no longer needs a flag to avoid planning.

## Verification

- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror` green (0 warnings) at both commits.
- Task 1 filter (ReviewPlanLogicTests|CorpusUiTextTests): 125 passed.
- Task 2 filter (ReviewPlanLogicTests|CaseScenarioUiLogicTests|CorpusSettingsStepLogicTests|WizardFlowTests|Render WizardShellRenderTests): 176 passed.
- `grep -rn "SuppressAutoPlan|ClearsAutoPlanSuppression" --include=*.cs` (excluding obj/bin): no matches.

## Deviations from Plan

**1. [Rule 1 - Correctness] Read-and-clear placed before the active-run early return**
- **Found during:** Task 2
- **Issue:** The plan placed the read-and-clear immediately before building `ReviewPlanEntryInput`, but `OnEnterStepAsync` returns early while a planning run is active. A flag set during an active run would then survive and could start an unrequested run on a later entry (Pitfall 5).
- **Fix:** The read-and-clear moved to right after the `_state == null` check, so every entry consumes it. It still comes before `ReviewPlanEntryDecision(` (the source-order test checks this).
- **Files:** UserControls/StepReviewPlan.Run.cs
- **Commit:** 637e3f7

**2. TDD note:** Each task's tests and implementation went into a single commit, not separate RED/GREEN commits. Renaming the members breaks compilation, and every commit had to build with -warnaserror.

**3. Test fixups beyond the listed ones:** In ReviewPlanLogicTests, the reopen-scenario tests that passed `suppress: true` now use the default (not requested). They express the same "no request" condition. The unreadable test now passes `requested: true`, which shows the unreadable card still wins over a request.

## Threat Flags

None. No new surface. T-09-08/T-09-09 are covered by the requested-only AutoPlan/AutoReplan rules and the read-and-clear on entry. T-09-10 is accepted: the flag is in-memory only.

## Self-Check: PASSED
