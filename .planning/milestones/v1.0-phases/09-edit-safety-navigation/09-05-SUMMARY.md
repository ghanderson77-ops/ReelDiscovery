---
phase: 09-edit-safety-navigation
plan: 05
subsystem: case-guide / case-summary
tags: [w1-withdrawal, w6-navigation, approval, case-summary, render]
requires:
  - "09-01: CorpusApproval.WithdrawIfGuideChanged"
  - "09-03: StepCaseScenario.MarkOpenedFromDisk"
provides:
  - "Approval withdrawal in the StepCaseGuide save funnel (synchronous, before the first await)"
  - "Reopen backstop: MarkOpenedFromDisk withdraws a stale approval"
  - "Case Summary entry withdrawal before the first RefreshAll; persisted by the entry save"
  - "SummaryRowContent.SecondaryText; Mailboxes row always shows Generate Emails / Generate Corpus"
affects: [UserControls/StepCaseGuide.Run.cs, UserControls/StepCaseScenario.cs, UserControls/StepCaseSummary.cs, UserControls/GenerationUiText.cs]
tech-stack:
  added: []
  patterns: ["withdraw-before-first-await so the close path's synchronous prefix persists it", "caption change followed by FitToText + PerformLayout on the row"]
key-files:
  created: []
  modified:
    - UserControls/StepCaseGuide.Run.cs
    - UserControls/StepCaseScenario.cs
    - UserControls/StepCaseSummary.cs
    - UserControls/GenerationUiText.cs
    - ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideStepLogicTests.cs
    - ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/CaseSummaryGenerationTests.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
decisions:
  - "The guide-staleness withdrawal lives in the step save funnels (Case Guide SaveAsync, Case Summary entry, reopen helper), never in CaseStore.SaveAsync, so Keep This Plan -> Approve on Review Plan is not disturbed"
  - "Case Summary Mailboxes secondary button is always visible: Generate Emails before generation, Generate Corpus after; the Generate Corpus entry gate (09-04) decides what the user sees"
metrics:
  duration: "~8 min"
  completed: 2026-10-12
  tasks: 2
  files: 10
---

# Phase 9 Plan 05: Withdrawal call sites and Case Summary way back Summary

A stale approval now cannot survive a Case Guide save (including the close flush), a case reopen or a visit to Case Summary, and Case Summary's Mailboxes row always offers a button to Generate Corpus ("Generate Emails" before anything is generated, "Generate Corpus" after) without clipping.

## Tasks

| Task | Name | Commit | Files |
| ---- | ---- | ------ | ----- |
| 1 | Withdrawal in the Case Guide save funnel, on reopen and on Case Summary entry | f45c929 | StepCaseGuide.Run.cs, StepCaseScenario.cs, StepCaseSummary.cs, 3 test files |
| 2 | Case Summary Mailboxes row always links to Generate Corpus (W6) | 9bd8268 | GenerationUiText.cs, StepCaseSummary.cs, GenerationUiTextTests.cs, CaseSummaryGenerationTests.cs, CaseSummaryRenderTests.cs |

## What changed

- `StepCaseGuide.SaveAsync`: `var withdrew = CorpusApproval.WithdrawIfGuideChanged(Draft);` after the `CanSave` guard and before `await _state.CaseStore.SaveAsync(Draft)`; `RefreshBanner()` after `RefreshHeader()` when it withdrew. Because the close path (`FlushCurrentCaseStep`) runs only the synchronous prefix of `OnLeaveStepAsync` before `SaveNow`, the withdrawal is in the draft before the flush.
- `StepCaseScenario.MarkOpenedFromDisk` now clears `PlanOnEntryOnce` and withdraws a stale approval on the loaded case. The next step's entry save persists it.
- `StepCaseSummary.OnEnterStepAsync` withdraws after `AdvanceStage(Summary)` and before the first `RefreshAll()`, so the plan row already reads out of date and the existing entry save persists it.
- `SummaryRowContent.SecondaryText` (default `""`). `MailboxesRow` sets `ShowSecondary = true` and `SecondaryText = input.Generated ? "Generate Corpus" : "Generate Emails"`. `RefreshGenerationRows` applies the caption, calls `FitToText(0, LogicalToDeviceUnits(34))`, and calls `PerformLayout()` on the button flow panel and the row.

## Tests

- New tests: `MarkOpenedFromDisk_GuideEditedAfterApproval_WithdrawsApproval`, `_UneditedGuide_KeepsApproval` and `_CaseWithoutCorpus_DoesNotThrow`. `SaveAsync_WithdrawsStaleApprovalBeforeFirstAwait` and `Source_EntryWithdrawsStaleApprovalBeforeFirstRefresh` are source-order tests. `Entry_GuideEditedAfterApproval_WithdrawsAndSaves` reloads case.json and checks `Approved == false`.
- `GenerationUiTextTests` checks the SecondaryText caption in the not-generated, complete, partial and EarlierPlan states, and that it defaults to empty.
- `CaseSummaryRenderTests.Summary_GeneratedCorpusRows` now expects the button to be visible in every state, captioned Generate Emails or Generate Corpus. `MailboxesRow_CompleteClean` and `MailboxesRow_PartialAllClauses` now assert that neither button ellipsizes, that the two buttons don't overlap, and that the button column starts at or right of line 3. Both checks run at Large and WizardMin.
- Plan verification filter (CaseScenarioUiLogic, CaseGuideStepLogic, CaseSummary*, GenerationUiText, WizardFlow, CorpusApproval, GenerateCorpusEntry): 470 passed. CaseSummary render filter: passed. `-warnaserror` build: 0 warnings.

## DPI inspection (rule 47)

This machine renders at 120 DPI (125%). I inspected `case-summary-mailboxes-partial-1000x540.png` and `-1100x750.png`. "Open Mailboxes Folder" and "Generate Corpus" sit side by side, fully legible, and clear of the warning line 3 ("34 of 40 threads written · ... · 2 skipped items"). The WizardMin render has no horizontal scroll. **A human check at 150% (144 DPI) is still needed.** The render assertions are DPI-relative and should hold there too, but they were not run at 144 here.

## Deviations from Plan

**1. [Rule 1 - Test update] CaseSummaryGenerationTests.OpenMailboxesFolder_OpensOnlyTheCaseMailboxesFolder**
- **Found during:** Task 2 verification (the `CaseSummaryGenerationTests` filter in the plan)
- **Issue:** The test asserted that `GenerateEmailsButton` is hidden once generation exists, which is the pre-W6 behavior.
- **Fix:** The test now asserts the button is visible with the caption "Generate Corpus".
- **Files modified:** ReelDiscovery.Tests/Evidence/CaseSummaryGenerationTests.cs
- **Commit:** 9bd8268

**2. Minor:** `MailboxesLine2` has no test accessor, so the overlap assertion uses line 3, which sits in the same text column. The caption refit runs only when the caption actually changes, so a refresh with the same caption doesn't relayout.

## TDD note

Each task's tests and implementation went in one commit (plan type `execute`), not as separate RED and GREEN commits. Before commit 9bd8268, the `CaseSummaryGenerationTests` assertion on the old behavior failed against the new code, which confirms the tests detect the change.

## Self-Check: PASSED
- FOUND: f45c929, 9bd8268
- FOUND: all modified files listed above
