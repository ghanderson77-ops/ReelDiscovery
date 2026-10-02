---
phase: 04-corpus-plan-answer-key
plan: 11
subsystem: ui-copy
tags: [ui-text, wizard, review-plan, corpus-settings, case-summary, tdd]
requires:
  - phase: 04-01
    provides: CorpusSettings (RelevantCount, DefaultTotalFor, ranges), ComplexityLevel
  - phase: 04-02
    provides: PlanStage
  - phase: 04-08
    provides: PlanSummary, CostEstimate
  - phase: 03-06
    provides: CaseGuideUiText.Plural, StateCardContent
provides:
  - CorpusUiText static helpers for Corpus Settings, Re-plan dialog, Review Plan and the Case Summary corpus rows
  - ReviewPlanEntryDecision(ReviewPlanEntryInput) and WithdrawsApproval(input)
  - PlanNextGate(running, hasPlan, approved) -> PlanNextGateResult (M-P1 / M-P2)
  - PlanBannerText, Tile(kind, summary, cost), StageName/StageProgress/ProgressText, StateCard, outcome lines
  - Types WarningLine, CorpusFileStatus, PlanBannerVariant/PlanBannerContent, TileKind/TileContent, ReviewPlanEntry/ReviewPlanEntryInput, PlanStateCard, PlanNextGateKind/PlanNextGateResult
affects: [04-12 Corpus Settings step, 04-15 Review Plan step, 04-17 Re-plan dialog, 04-18 Case Summary corpus rows]
tech-stack:
  added: []
  patterns: [pure UI-text helper class with public-field POCOs (CaseGuideUiText precedent)]
key-files:
  created:
    - UserControls/CorpusUiText.cs
    - ReelDiscovery.Tests/Corpus/CorpusUiTextTests.cs
    - ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs
  modified: []
key-decisions:
  - "Review Plan entry order: running, unreadable plan, no plan (AutoPlan or NoPlanCard on reopen), unapproved + settings changed (AutoReplan), stale guide not dismissed (StaleBanner), approved + settings changed not dismissed (SettingsBanner), else ShowPlan; WithdrawsApproval = approved and StaleBanner"
  - "MinPercentFor searches the slider range 5..60 (not from 1%), so the warning never suggests a value the field would clamp"
  - "HotHint uses {f}-{2f} hot emails; PrivilegedHint names at most two counsel then 'and {n} more'; auto rates quoted as constants (8% privileged, 20% attachments)"
  - "PercentText: whole values without decimals, else one decimal rounded away from zero; Est. cost money is N0 from $10 up, else 0.00 (tile value and tooltip parts)"
  - "Attachments tooltip pluralizes each type (1 voicemail) and folds Word/Excel/PowerPoint into documents; 'No attachments planned' when none"
  - "Reasons and messages are trimmed of trailing periods before templates add punctuation; an empty reason drops its clause"
metrics:
  duration: 20min
  completed: 2026-10-02
  tasks: 2
  files: 3
---

# Phase 4 Plan 11: Corpus UI Text Summary

`CorpusUiText` now holds all Phase 4 copy and the pure UI decisions, matching 04-UI-SPEC word for word: the Corpus Settings notices, hints and clamp lines, the D-07 relevant warning, the Re-plan dialog, the Review Plan entry rules, banners, tiles, stages, state cards, outcomes and Next gate, and the Case Summary corpus rows. Every count goes through `CaseGuideUiText.Plural`. 115 unit tests cover it without building any controls.

## What Was Built

- **Task 1 (Corpus Settings, dialogs, Case Summary):** `LiveCounts`, `RelevantWarning` and `MinPercentFor` (the same `CorpusSettings.RelevantCount` the allocator uses), `SettingsNotice`, `SizeHint`/`SizeClampNotice`, `WindowHint`/`StartClampedText`/`EndClampedText`, `MixTotalLine`, `MixHint`, `AdvancedSummary`, override hints, M-S1/M-S2/C-S1, Re-plan dialog constants and `ReplanBody`, plus `CorpusPlanLine`, `PlanStatusLine`, `AnswerKeyLine`, `ProtocolLine`, `ProtocolFileName`, `FileStatusLine`, `MissingFileText` and `PercentText`.
- **Task 2 (Review Plan):** `ReviewPlanEntryDecision` and `WithdrawsApproval` (D-12, D-25, D-26, T-04-34), `PlanBannerText` (the stale-guide banner wins), `Tile` for all seven tiles, `StageName`/`StageProgress`/`ProgressText`, `StateCard` (Planning, NoPlan, Failed, Unreadable), `ChecksCaption`, `NoChecksText`, `ChecksHint`, the outcome status lines, `PlanNextGate`, C-P1/C-P2 and `ApprovalBadgeText`.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | 38345eb | test(04-11): add failing tests for corpus settings, dialog and Case Summary copy |
| 1 | GREEN | b3251de | feat(04-11): add CorpusUiText settings, dialog and Case Summary copy |
| 2 | RED | 40d7a33 | test(04-11): add failing tests for Review Plan copy and entry decisions |
| 2 | GREEN | 002c2a0 | feat(04-11): add Review Plan copy, entry rules and Next gate to CorpusUiText |

## Verification

- `dotnet test --filter "FullyQualifiedName~ReviewPlanLogicTests|FullyQualifiedName~CorpusUiTextTests"`: 115 passed
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 472 passed
- Acceptance greps: `CaseGuideUiText.Plural` appears 20 times; the media notice, "The four shares must add up to 100%.", the `ReviewPlanEntryDecision` signature, the stale heading and "Warnings never block Approve Plan." are each present. Test attribute counts are 70 (CorpusUiTextTests) and 64 (ReviewPlanLogicTests).
- `CaseGuideUiText.cs` was not changed (04-19 owns its StageLabel change)

## Deviations from Plan

### Additions (Rule 2, needed by the UI plans and by the spec)

- Added `ReplanGuidanceCaption` ("Guidance (optional)") and `ProtocolFileName(folderName)`. The D-RP caption and the "{folder name} - Review Protocol.docx" name appear in the spec but the plan's helper list did not include them.
- Made `PercentText(decimal)` public. Tiles and the Case Summary row share it, and it is tested on its own.
- `ReviewPlanEntryDecision` checks `UnreadableReason` before `HasPlan`, so an unreadable plan always shows its card, even on reopen.

Otherwise the plan was executed as written.

## TDD Gate Compliance

Both tasks have a `test(...)` RED commit (the build failed because the types were missing) followed by a `feat(...)` GREEN commit. No refactor commits were needed.

## Known Stubs

None. The helpers return final copy; the UI plans 04-12, 04-15, 04-17 and 04-18 will wire them in.

## Requirements

PLAN-01 was already Complete. PLAN-07 needs the Review Plan screen (04-15) and PLAT-03 needs the remaining planner tests, so neither is marked complete here; this plan only adds to them.

## Self-Check: PASSED

- FOUND: UserControls/CorpusUiText.cs
- FOUND: ReelDiscovery.Tests/Corpus/CorpusUiTextTests.cs
- FOUND: ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs
- FOUND commits: 38345eb, b3251de, 40d7a33, 002c2a0
