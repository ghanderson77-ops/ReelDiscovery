---
phase: 09-edit-safety-navigation
verified: 2026-10-12T00:00:00Z
status: passed
score: 4/4 roadmap success criteria verified (39/40 plan truths; the 1 open truth is the 150% DPI walkthrough, which needs a person)
overrides_applied: 0
human_verification:
  - test: "150% DPI walkthrough in the running app (09-10 Task 2, deferred by the user on 2026-10-12)"
    expected: "At 150% Windows scaling: (1) edit the guide after approval, then open Generate Corpus. It shows 'The case guide changed after this plan was built' with a Review Plan button and does not start a run. The same happens through Case Summary's Generate Corpus button and after reopening the case. (2) Back from Generate Corpus to Review Plan does not start planning. (3) Skip to Discovery Documents works on Review Plan and Corpus Settings with no plan. (4) After generation, Case Summary's Mailboxes row shows 'Generate Corpus'. All buttons, banners and cards are unclipped and not ellipsized."
    why_human: "Render PNGs were produced at 120 DPI only. No 144-DPI PNG was produced and no one ran the walkthrough in the app. Layout at 150% and the end-to-end flow feel cannot be checked with grep or unit tests."
  - test: "False Out of date when the journal is missing for a reason other than Start Over (review WR-02)"
    expected: "Decide whether a journal missing because of a lock or a manual move is acceptable to treat as 'every cited email deleted'. Today both CorpusDetector.Detect and StepCaseSummary.LoadQaWithDiscovery treat any !replay.Exists as known-empty."
    why_human: "This is a design choice about false positives. It does not break SC3, which concerns Start Over and Regenerate Thread."
---

# Phase 9: Edit Safety & Navigation Verification Report

**Phase Goal:** Editing earlier steps can never silently corrupt later output, and every optional step is reachable without paid side effects.
**Verified:** 2026-10-12
**Status:** passed (human verification items reported complete by the user, 2026-10-13)
**Re-verification:** No. This is the initial verification.

## Goal Achievement

### Observable Truths (Roadmap Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Editing the guide after approval withdraws approval wherever the edit happens. Generate Corpus refuses to start, with a clear message, when the guide changed, including via the Case Summary button and on reopen. Case Guide warns when an approved plan or corpus depends on it. | VERIFIED | Withdrawal happens in `CorpusApproval.WithdrawIfGuideChanged` (`Services/Corpus/CorpusApproval.cs:227`). It is called from the Case Guide save funnel, synchronously before the first await (`UserControls/StepCaseGuide.Run.cs:41`), on reopen (`StepCaseScenario.MarkOpenedFromDisk`, l.899, called from `OpenCase` l.868), on Case Summary entry (`StepCaseSummary.cs:1099`), and on Generate Corpus entry and run (`StepGenerateCorpus.Run.cs:225,421`). The entry decision checks `HasPlan && GuideStale` before the approval check (`GenerationUiText.cs:749`), so a stale plan never reaches the Ready card. `RunCoreAsync` refuses a stale guide or an unapproved plan before any gateway is created (`StepGenerateCorpus.Run.cs:418-437`) and shows `RunRefusedGuideChangedText` / `RunRefusedNotApprovedText`. `GenerationRunner` is constructed only in `RunCoreAsync` (grep), so there is no other start path. Case Summary's button goes to `NavigateToStage(Generation)`, which re-runs the entry gate. Case Guide shows a dependency line and a PlanDependency banner (`StepCaseGuide.cs:814-843`, `CaseGuideUiText.DependencyNotice` l.165). CR-01 (approving a stale plan from Review Plan Next) is fixed: `PlanNextGate(..., guideStale)` returns AskSkip "Continue Without Approving" (`CorpusUiText.cs:636`), and `ApproveAsync` refuses a stale guide (`StepReviewPlan.Run.cs:518`). |
| 2 | Discovery Documents is reachable without an approved corpus plan, and passing through Review Plan never starts a paid planning run unless the user asks. | VERIFIED | `PlanOnEntryOnce` is set in exactly one place, a successful Corpus Settings Next (`StepCorpusSettings.cs:1082`). Review Plan reads and clears it on every entry before the `_isPlanning` return (`StepReviewPlan.Run.cs:64-65`). `ReviewPlanEntryDecision` auto-plans only when `PlanRequested` is set (`CorpusUiText.cs:388-392`). Reopen clears the flag. Review Plan Next with no plan asks "Continue Without a Plan" (default Yes) and does not block. Skip to Discovery Documents is on the Review Plan state cards (`StepReviewPlan.cs:440`, handler `StepReviewPlan.Run.cs:422`) and on Corpus Settings (`StepCorpusSettings.cs:1115`, validates without setting the flag). Generate Corpus Next is `!_isGenerating` only. `StepDiscoveryDocuments` has no plan or approval gate. `SuppressAutoPlan` grep returns 0. |
| 3 | Start Over and Regenerate Thread mark discovery documents whose cited exhibits changed or were deleted as Out of date. | VERIFIED | `DiscoveryStaleness.Stamp` records `ExhibitStamps` (ThreadDone Utc) for cited DocIds (`DiscoveryStaleness.cs`, called at `DiscoveryGenerator.cs:443`). `Check` returns `StaleSource.Exhibits` when a cited id is missing from the current stamps or its stamp changed. Start Over deletes the journal, so `!replay.Exists` gives known-empty stamps (`CorpusDetector.cs:34-40`, `StepCaseSummary.cs:1817-1818`). Regenerate Thread journals `ThreadInvalidated`, and replay removes the thread from `LastByThread` (`GenerationJournal.cs:253-258`), so its DocIds drop out of `EmailStamps`. A later rewrite gets a new ThreadDone Utc and still reads as changed. Both consumers are wired: `DiscoveryStepModel` passes `corpus?.WrittenStamps`, and Case Summary passes `EmailStamps(qa.Replay)`. The status text names the email. Both confirmation dialogs add an Out-of-date clause (`GenerationUiText.cs:991`, `QaFindingsDialog.cs:408` with `CitedByDiscovery` from `StepCaseSummary.cs:2333`). |
| 4 | Case Summary always offers a direct way back to Generate Corpus once anything has been generated. | VERIFIED | `MailboxesRow` sets `ShowSecondary = true` unconditionally, with `SecondaryText` "Generate Corpus" after generation and "Generate Emails" before (`GenerationUiText.cs:1008-1009`). `StepCaseSummary.cs:941-950` binds Visible and Text and refits the button. The click goes to `NavigateToStage(CaseStage.Generation)` (l.554). `RefreshGenerationRows` is called unconditionally from `RefreshCorpusRows`. |

**Score:** 4/4 roadmap truths verified. Of the plan must-have truths, all are verified in code and tests except 09-10 truth 3 (the 150% DPI walkthrough in the app), which is routed to human verification.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Services/Corpus/CorpusApproval.cs` | WithdrawIfGuideChanged | VERIFIED | Wraps WithdrawApprovalIfStale and bumps Revision; called from 5 sites |
| `UserControls/GenerationUiText.cs` | GuideChangedCard entry, cards, refusal text, Mailboxes secondary | VERIFIED | All present; rule order correct |
| `UserControls/CaseGuideUiText.cs` | DependencyNotice, PlanDependency variant | VERIFIED | Decision table at l.165 |
| `Models/Discovery/DiscoveryDocument.cs` / `DiscoveryCorpus.cs` | ExhibitStamps / WrittenStamps | VERIFIED | Used by Stamp/Check and CorpusDetector |
| `Services/Discovery/DiscoveryStaleness.cs` | EmailStamps, CitedDocIds, AnyCitesEmails, Check | VERIFIED | Substantive and wired |
| `Models/WizardState.cs` | PlanOnEntryOnce | VERIFIED | One setter, read-and-clear on entry |
| `UserControls/StepGenerateCorpus.Run.cs` | GuideChanged branch, explicit card actions, run refusal | VERIFIED | `default:` does nothing, and StartOverAsync is reached only from PlanChanged and Unreadable |
| `UserControls/StepCaseGuide.cs` / `.Run.cs` | Dependency line, banner, save-funnel withdrawal | VERIFIED | |
| `UserControls/StepReviewPlan*.cs`, `StepCorpusSettings.cs` | Skip buttons, AskSkip gate | VERIFIED | |
| `UserControls/DiscoveryStepModel.cs`, `DiscoveryUiText.cs` | Stamps threaded into Check; Exhibits copy | VERIFIED | |
| `UserControls/QaUiText.cs`, `Forms/QaFindingsDialog.cs` | CQ1 clause | VERIFIED | |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| StepCaseGuide.Run SaveAsync | CorpusApproval.WithdrawIfGuideChanged | before the first await | WIRED |
| StepCaseScenario.OpenCase | MarkOpenedFromDisk → WithdrawIfGuideChanged | after load | WIRED |
| StepCaseSummary entry | WithdrawIfGuideChanged + save | OnEnterStepAsync | WIRED |
| StepGenerateCorpus entry/RunCoreAsync | IsGuideStale / WithdrawIfGuideChanged | GuideStale input + guard before gateway | WIRED |
| GuideChanged card | NavigateToStage(ReviewPlan) | ClickStateActionAsync | WIRED |
| StepCorpusSettings Next | PlanOnEntryOnce = true | ValidateStepAsync | WIRED |
| StepReviewPlan entry | PlanOnEntryOnce = false | read-and-clear | WIRED |
| Skip buttons | NavigateToStep(typeof(StepDiscoveryDocuments)) | click handlers | WIRED |
| CorpusDetector | DiscoveryStaleness.EmailStamps | WrittenStamps | WIRED |
| DiscoveryGenerator | DiscoveryStaleness.Stamp(record, draft, corpus) | l.443 | WIRED |
| DiscoveryStepModel.Row | DiscoveryStaleness.Check(doc, draft, currentStamps) | l.272 | WIRED |
| Case Summary _btnGenerateEmails | NavigateToStage(Generation) | always visible | WIRED |

### Data-Flow Trace (Level 4)

| Artifact | Data | Source | Real Data | Status |
|----------|------|--------|-----------|--------|
| Discovery rows (Out of date) | currentStamps | Journal replay `LastByThread` ThreadDone Utc | Yes | FLOWING |
| Case Guide dependency line | EmailsWritten, IsGuideStale | `Draft.Corpus.Generation` cached count, live guide hash | Yes (count can lag after a crash, WR-03) | FLOWING |
| GuideChanged card body | EmailsWritten | cached `Generation.EmailsWritten` | Yes (same lag caveat) | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full non-scale suite after the CR-01 fix | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` | 4548 passed, 0 failed (10 m 52 s); includes `NextGate_NotApproved_StaleGuide_AsksToSkipWithoutApproving` | PASS |
| Grep gate: no SuppressAutoPlan | grep | 0 | PASS |
| Grep gate: no withdrawal in CaseStore | grep `Services/Case/CaseStore.cs` | 0 | PASS |
| Render suite at 144 DPI | not re-run; earlier run was at 120 DPI | n/a | SKIP (human) |

### Requirements Coverage

Phase 9 is a hardening phase. REQUIREMENTS.md maps every ID to an earlier phase (Phase 3, 4, 5, 7 or 8) and all are already `[x]`. No ID is orphaned: all 10 IDs appear in plan frontmatter (09-10 lists all of them).

| Requirement | Source Plans | Status | Evidence |
|-------------|--------------|--------|----------|
| GUIDE-09 | 01, 04, 05, 06, 10 | SATISFIED | SC1 (audit W1 closed) |
| PLAN-06 | 01, 04, 10 | SATISFIED | Generation only runs from an approved, non-stale plan |
| PLAN-07 | 01, 03, 04, 05, 07, 10 | SATISFIED | Approval withdrawn on guide edits; CR-01 fixed; no unrequested planning |
| KEY-03 | 01, 04, 10 | SATISFIED | Labels come only from a current approved plan (run guard) |
| ANC-01 | 02, 03, 07, 08, 10 | SATISFIED | Discovery reachable without a plan (W2); exhibit staleness (W3) |
| ANC-03 | 02, 08, 10 | SATISFIED | RFA requests included in CitedDocIds |
| ANC-05 | 02, 08, 09, 10 | SATISFIED | Exhibits staleness plus confirmation clauses |
| QA-01 | 05, 09, 10 | SATISFIED | Regenerate Thread clause; Case Summary path back (W6) |
| QA-03 | 05, 09, 10 | SATISFIED | Same as QA-01 |
| RUN-06 | 05, 10 | SATISFIED | Generate Corpus button always on Case Summary (W6) |

### Review Findings vs Success Criteria

| Finding | Breaks an SC? | Classification |
|---------|---------------|----------------|
| CR-01 (Next approves a stale plan) | Would have broken SC1. Fixed in 906263a and verified in code (`PlanNextGate` guideStale branch, `ApproveAsync` guard) and by the full suite | Closed |
| WR-01 (Case Guide banner's Review Plan skips the guide's Next validation gate) | No. Approval cannot be granted while the guide is stale (CR-01 fix), and the run guard is unchanged. The gap is that a guide with validation errors can reach Review Plan, where a paid Re-plan is user-initiated. | WARNING |
| WR-02 (any missing journal treated as known-empty) | No. SC3 asks that changed or deleted exhibits are marked; the risk is false positives (a document marked Out of date when it is not) | WARNING, also listed for human decision |
| WR-03 (Guide-changed entry skips crash recovery, so the email count can be stale) | No. The run is still refused with a clear message; only the "N emails already written" number can lag | WARNING |
| IN-01..06 | No | Info |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `UserControls/StepCorpusSettings.cs` | 1081 | Always-true `if (RequestsPlanning(true, datesOk: true))` (IN-02) | Info | None |
| `UserControls/StepReviewPlan.Run.cs` | 116-118 | Hand-rolled withdraw + Revision++ instead of WithdrawIfGuideChanged (IN-01) | Info | Same behavior |

No TODO/placeholder stubs were found in the phase-9 paths that were checked.

### Human Verification Required

#### 1. 150% DPI walkthrough in the running app

**Test:** Set Windows scaling to 150%. (a) Approve a plan, edit the guide, then open Generate Corpus directly, through Case Summary's button, and after reopening the case. (b) Go Back from Generate Corpus to Review Plan. (c) With no plan, use Skip to Discovery Documents on Corpus Settings and on Review Plan. (d) After generation, look at Case Summary's Mailboxes row.
**Expected:** (a) The Guide-changed card shows in all three cases and no run starts. (b) No planning starts. (c) Discovery Documents opens with no spend. (d) "Generate Corpus" shows. No button, banner or card is clipped, ellipsized or overlapping.
**Why human:** Render tests ran at 120 DPI, not 144. The user deferred the walkthrough ("finish it", 2026-10-12), so it was never performed. It is NOT counted as passed.

#### 2. WR-02 design decision

**Test:** Decide whether a journal missing for a reason other than Start Over should mark citing documents Out of date.
**Expected:** Either accept the current behavior, or add a Start Over marker so that an unexplained missing journal reads as unknown.
**Why human:** This trades false positives against complexity. It is not a correctness failure of SC3.

### Gaps Summary

There are no blocking gaps. All four roadmap success criteria are implemented and wired, and the code supports each one directly. The full non-scale suite passes after the CR-01 fix (4548/4548). The audit gaps W1, W2, W3 and W6 are closed in code. The phase stays at human_needed for one reason: the 150% DPI walkthrough in the app was deferred and never performed, and the render evidence exists only at 120 DPI. WR-01, WR-02 and WR-03 remain open as warnings. None of them defeats a success criterion, and no later phase (Phase 10 covers W4, W5 and W8) addresses them, so they should be scheduled or explicitly accepted.

---

_Verified: 2026-10-12_
_Verifier: Claude (gsd-verifier)_


---

**Human verification closed 2026-10-13:** the user reported completing live testing of the items above; see the phase HUMAN-UAT file.
