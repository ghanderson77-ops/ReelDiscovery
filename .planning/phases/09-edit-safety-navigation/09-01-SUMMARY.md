---
phase: 09-edit-safety-navigation
plan: 01
subsystem: corpus-approval / wizard-ui-text
tags: [W1, stale-guide, approval, ui-copy, tdd]
requires: []
provides:
  - CorpusApproval.WithdrawIfGuideChanged
  - GenerationEntry.GuideChangedCard / GenerationEntryInput.GuideStale
  - GenerationStateCard.GuideChanged + StateCard(emailsWritten, hasPlan)
  - GenerationUiText.RunRefusedGuideChangedText / RunRefusedNotApprovedText
  - CaseGuideUiText.DependencyNotice / DependencyNoticeContent / DependencyNoticeKind
  - BannerVariant.PlanDependency
  - StateCardContent.SecondaryText
affects:
  - Later 09 plans wiring the Case Guide save funnel, reopen, Generate Corpus entry/run and Review Plan skip button
tech-stack:
  added: []
  patterns: [pure UI-text decision functions with verbatim-copy unit tests (04-22 precedent)]
key-files:
  created: []
  modified:
    - Services/Corpus/CorpusApproval.cs
    - UserControls/GenerationUiText.cs
    - UserControls/CaseGuideUiText.cs
    - ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideUiLogicTests.cs
decisions:
  - "WithdrawIfGuideChanged wraps WithdrawApprovalIfStale and bumps draft.Revision only when it withdrew; kept guide hashes never withdraw (D-25)"
  - "EntryDecision checks HasPlan && GuideStale right after Running, so a stale plan never reaches Ready/NotApproved"
  - "StateCard gains trailing optional emailsWritten/hasPlan parameters so existing callers compile unchanged"
metrics:
  duration: 4min
  completed: 2026-10-11
  tasks: 2
  files: 6
---

# Phase 9 Plan 01: Stale-guide approval transition and W1 copy Summary

Pure, tested W1 layer: `CorpusApproval.WithdrawIfGuideChanged` (withdraw + revision bump), the Generate Corpus `GuideChangedCard` entry rule and S1 card/refusal copy, the split NotApproved card (U10), and the Case Guide `DependencyNotice` S2 decision table with a lowest-priority `PlanDependency` banner variant.

## Tasks

| Task | Name | Commits |
| ---- | ---- | ------- |
| 1 | WithdrawIfGuideChanged + Generate Corpus entry/card/refusal copy | 57ded57 (RED), f8848ed (GREEN) |
| 2 | DependencyNotice, PlanDependency variant, StateCardContent.SecondaryText | bbb20d3 (RED), 6b025bf (GREEN) |

## Verification

- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors
- Filtered tests `CorpusApprovalTests|GenerationUiTextTests|CaseGuideUiLogicTests`: 396 passed, 0 failed
- A1 regression (`WidenActiveRanges_AfterPlanning_DoesNotStaleGuide`) passes: visiting Case Guide right after planning does not change the guide hash
- All acceptance-criteria grep counts match the plan

## TDD Gate Compliance

Both tasks have a `test(09-01)` RED commit (confirmed failing by compile errors on the missing members) followed by a `feat(09-01)` GREEN commit. No refactor needed.

## Deviations from Plan

None - plan executed exactly as written. (Minor addition: tests also cover negative `emailsWritten`, which reads as 0 in both the GuideChanged card and DependencyNotice.)

## Known Stubs

None. `GenerationEntry.GuideChangedCard` and `BannerVariant.PlanDependency` are not yet consumed by the step controls; later 09 plans wire them in, as planned.

## Self-Check: PASSED
