---
phase: 09-edit-safety-navigation
plan: 07
subsystem: corpus-navigation
tags: [w2-skip-to-discovery, review-plan, corpus-settings, render]
requires:
  - "09-01: StateCardContent.SecondaryText"
  - "09-03: WizardState.PlanOnEntryOnce, StepCorpusSettings.RequestsPlanning"
provides:
  - "Review Plan NoPlan / Failed / Unreadable cards: Primary + Default 'Skip to Discovery Documents' action row"
  - "Corpus Settings header 'Skip to Discovery Documents' (no approved plan only), validated like Next, never requests planning"
  - "UI-SPEC S3/S4 skip copy in CorpusUiText.StateCard and SettingsNotice"
affects: [UserControls/StepReviewPlan.cs, UserControls/StepCorpusSettings.cs, UserControls/CorpusUiText.cs]
tech-stack:
  added: []
  patterns: ["shared ValidateFields() between Next and a side navigation button so the skip path cannot bypass validation"]
key-files:
  created: []
  modified:
    - UserControls/CorpusUiText.cs
    - UserControls/StepReviewPlan.cs
    - UserControls/StepReviewPlan.Run.cs
    - UserControls/StepCorpusSettings.cs
    - ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusUiTextTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusSettingsStepLogicTests.cs
    - ReelDiscovery.Tests/Render/CorpusReviewPlanRenderTests.cs
    - ReelDiscovery.Tests/Render/CorpusSettingsRenderTests.cs
decisions:
  - "Review Plan action row stays left-aligned under the body text inside the centered content block (CenterStateContent). The render test asserts that the content block is centered in the card (2 px), not that the pair alone is centered."
  - "Corpus Settings skip relies on WizardForm.ShowStep -> OnLeaveStepAsync (FlushCommits + SaveAsync) to save the settings; no extra save call"
metrics:
  duration: "~10 min"
  completed: 2026-10-11
  tasks: 2
  files: 9
---

# Phase 09 Plan 07: Skip to Discovery Documents Summary

Discovery Documents can now be reached from two places without an approved corpus plan and without any model call. The first is the Review Plan NoPlan, Failed and Unreadable cards, where a Default `Skip to Discovery Documents` button sits next to `Plan Corpus` / `Retry Planning`. The second is the Corpus Settings header. Its skip button runs the same mix and date validation as Next but never sets `PlanOnEntryOnce`. Card bodies and the Corpus Settings notice use the UI-SPEC S3/S4 copy verbatim.

## Tasks

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Review Plan state cards with Skip to Discovery Documents | a32c752 | CorpusUiText.cs, StepReviewPlan.cs, StepReviewPlan.Run.cs, ReviewPlanLogicTests.cs, CorpusReviewPlanRenderTests.cs |
| 2 | Corpus Settings header skip button and notice copy | 438a04c | CorpusUiText.cs, StepCorpusSettings.cs, CorpusUiTextTests.cs, CorpusSettingsStepLogicTests.cs, CorpusSettingsRenderTests.cs |

## What changed

- `CorpusUiText.StateCard`: NoPlan, Failed and Unreadable now carry `SecondaryText = "Skip to Discovery Documents"` and the S3 bodies. Planning is unchanged (`SecondaryText` is empty).
- `CorpusUiText.SettingsNotice`: the no-plan and unapproved variants now include the S4 sentences. The approved variant is unchanged.
- `StepReviewPlan`: `_stateActions` is a LeftToRight, non-wrapping FlowLayoutPanel holding `_btnStateAction` (Primary) and then `_btnStateSkip` (Default, 8 px left margin). It replaces the bare Primary in `_stateContent`. `ShowStateCard` sets the skip button's text and visibility from `SecondaryText` and disables it while planning or approving. `SkipToDiscovery()` in `.Run.cs` has the same guard and calls `NavigateToStep(typeof(StepDiscoveryDocuments))`. Render accessors added: `StateActionButton` and `StateSkipButton`.
- `StepCorpusSettings`:
  - The header's column 1 is now a `headerButtons` flow: Skip (Default, right margin 8), then Reset to Defaults.
  - The skip button has the tooltip `Go straight to Discovery Documents without planning the corpus. No model calls are made.`
  - `ShowsSkipToDiscovery(hasPlan, approved)` controls visibility in `FillControls`.
  - `ValidateStepAsync` calls the extracted `ValidateFields()` (FlushCommits, MS1, MS2). Only Next sets `PlanOnEntryOnce`. `SkipToDiscovery()` validates and then navigates.
  - Render accessors added: `SkipToDiscoveryButton`, `ResetDefaultsButton`, `NoticeLabel` and `TitleLabel`.
- Focus: the Review Plan card sets no focus, and the skip button comes after the Primary in tab order. Corpus Settings still focuses the selected size toggle on entry (l.~1037), so the skip button never takes default focus.

## Verification

- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror`: 0 warnings, 0 errors (both tasks)
- Task 1 filter (`ReviewPlanLogicTests | Render CorpusReviewPlanRenderTests`): 69/69 passed
- Task 2 filter (`CorpusUiTextTests | CorpusSettingsStepLogicTests | WizardFlowTests | Render CorpusSettingsRenderTests`): 145/145 passed
- All acceptance greps match. The one difference is that `To write discovery documents without emails, click Skip to Discovery Documents.` appears 2 times in CorpusUiText.cs after Task 2 (StateCard NoPlan plus SettingsNotice no-plan, both verbatim UI-SPEC copy). It appeared once after Task 1, as the plan expected.

## Render inspection (rules 45-46)

This machine renders at **120 DPI**, not 144. I inspected these PNGs from `obj/gsd-artifacts/bin/ReelDiscovery.Tests/debug/renders/`:
- `review-plan-noplan-*`, `review-plan-failed-*` (Large + WizardMin): `Plan Corpus` / `Retry Planning` and `Skip to Discovery Documents` sit on one row with the same Top, and neither is ellipsized. The content block is centered in the card and nothing is clipped. Exactly one Primary is visible.
- `corpus-settings-skip-*`: Skip, then Reset to Defaults, on one row at the right of the header. The subtitle path ellipsizes before either button. The long no-plan notice wraps to two lines without clipping.
- `corpus-settings-approved-*`: the skip button is hidden and only Reset to Defaults shows.

**Human check still needed at 150% (144 DPI)** for these four renders. The assertions are DPI-independent (same Top, width >= measured text, centered content, title ends before the buttons, no clipping), but the 144-DPI PNGs have not been looked at.

## Deviations from Plan

**1. [Interpretation] Centering assertion for the Review Plan pair.** The plan's behavior line says the pair is "horizontally centered in the card (left/right gaps within 2 px)". The existing card content is a left-aligned block centered by `CenterStateContent`, and UI-SPEC S3 says "the card content stays centered (CenterStateContent)". The pair is therefore left-aligned under the body text, and the test asserts that the content block containing it is centered within 2 px. Centering only the buttons under left-aligned text would look misaligned.

**2. [Rule 3 - Blocking] Render harness hook.** `CorpusSettingsRenderTests.RenderBoth` ran `verify` after the form closed, when Visible flags no longer hold. I added an optional `shown` callback that runs while the form is displayed. Existing tests are unchanged.

## Known Stubs

None.

## Threat Flags

None. T-09-20 is mitigated: only Next sets `PlanOnEntryOnce` (grep = 1). T-09-21 is mitigated: the skip calls `ValidateFields()`. T-09-22 is mitigated: the Review Plan skip is guarded and disabled while planning or approving.

## Self-Check: PASSED
