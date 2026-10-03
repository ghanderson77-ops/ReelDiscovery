---
phase: 04-corpus-plan-answer-key
verified: 2026-10-03T00:00:00Z
status: human_needed
score: 5/5 roadmap success criteria verified (13/13 requirements satisfied)
overrides_applied: 0
human_verification:
  - test: "CR-02: Reopen a saved case at Review Plan (plan exists, not approved, corpus settings changed since planning). Also try with the guide edited (stale) and with a dismissed settings banner."
    expected: "No automatic re-plan on open. Stale guide shows the 'out of date' banner; otherwise the settings-changed banner shows unapproved wording (does not claim the plan was approved)."
    why_human: "Entry-state decision is unit-tested (ReviewPlanEntryDecision) but the fix landed after the 04-20 human walk (13dc884 at 12:38 vs approval at 12:22) and the reopen path runs through WizardForm stage resume."
  - test: "WR-09: Start planning, click Cancel exactly as planning finishes (and once after the plan has appeared)."
    expected: "Cancel is disabled once the planner returns. A cancel confirmed during the finish discards the new plan (D-18). Clicking after the plan applied shows CancelTooLateText, not a fake cancel."
    why_human: "WinForms click/timing race; no automated test covers the click path."
  - test: "WR-06: Plan a corpus with a live provider and read the decoy subjects in answer-key.csv (Non-relevant category = decoy)."
    expected: "Decoys share project names and keywords with the case but none paraphrases a key fact (alleged conduct); too few seeds should fall back to templates."
    why_human: "Paraphrase rejection uses heuristic thresholds (>=3 distinctive words and >=60%); quality judgment needs a human reading real model output."
---

# Phase 4: Corpus Plan & Answer Key Verification Report

**Phase Goal:** Before any prose is written, code plans every thread and message against the facts timeline. The user reviews the plan, and the answer key and review protocol come straight from it.
**Verified:** 2026-10-03
**Status:** human_needed
**Re-verification:** No (initial verification)

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User sets corpus size (up to ~5,000) and relevant/non-relevant ratio; the plan fixes every message's date/time, sender, To/CC, reply/forward structure, linked facts/issues, relevance label and attachments | VERIFIED | `UserControls/StepCorpusSettings.cs` (250/1,000/2,500/5,000 toggles, custom box, `PercentSlider`, media checkboxes, Advanced card); `CorpusSettings.Normalize` clamps 50-5,000. `CorpusPlanner.PlanAsync` builds relevant + non-relevant skeletons in code (`ThreadSkeletonBuilder`, `NonRelevantSkeletonBuilder`, `ThreadScheduler`), numbers them (`PlanNumbering.Assign`), then fills only text via LLM. Called from `StepReviewPlan.Run.cs:269`. |
| 2 | Relevant threads follow the key-facts timeline, overlapping issues concurrent among shared characters; timestamps respect business hours, time zones, weekends/holidays, reply latency; realistic depth/CC/attachment distributions | VERIFIED | `BudgetAllocator.AllocateRelevant` weights by significance; `ThreadSkeletonBuilder.MergeMultiIssue` (D-15 gap fix: 20/30 business-day windows, 10-15% share) with tests `BuildRelevant_ConcurrentFacts_MultiIssueThreads` and `..._MultiIssueShareTenToFifteenPercent`. `ThreadScheduler` uses `ctx.ZoneFor(sender)`, `BusinessCalendar` (weekdays, 9-5, observed US federal holidays), `ThreadShapes.SampleLatencyMinutes`, monotonic Seq. `RelevantThreadFiller.BuildChunks` chunks by time window across issues. |
| 3 | Plan validator reports counts, ratio, dates outside each character's active range, and any key fact with no planned evidence | VERIFIED | `CorpusPlanValidator.Validate` runs 19 isolated rules incl. `CheckEmailCount`, `CheckRelevantRatio`, `CheckActiveRanges`, `CheckFactEvidence`, plus window, business time, order, ids, labels, knowledge, privilege, attachments, DocIds, mix, depth, fallbacks, leakage, stale. Wired at end of `PlanAsync` and on Review Plan entry (`StepReviewPlan.Run.cs:185`). |
| 4 | User sees plan summary (counts by issue, fact, relevance, custodian + date histogram) and can approve or re-plan | VERIFIED | `StepReviewPlan.cs`: `StatTile`s, `MonthHistogram`, `_gridIssues` / `_gridFacts` / `_gridCustodians` fed by `PlanSummary.From`, Checks list, cost from `GenerationCostEstimator`; Approve -> `CorpusApproval.ApproveAsync`; `ReplanDialog` with guidance; stale banner disables Approve (line 1174). Human walk at 150% approved 2026-10-03 (04-20). |
| 5 | From the plan alone: CSV + Excel answer key (one row per email and attachment, required columns, family relevance propagated to attachments and quoted replies, statistics sheet) plus a review protocol | VERIFIED | `CorpusApproval.BuildFiles` -> `AnswerKeyBuilder.Build(plan, guide)` (guide only for titles) -> `AnswerKeyCsvWriter` / `AnswerKeyXlsxWriter` (sheets Answer Key with frozen pane + AutoFilter, Statistics: By Custodian/Issue/Relevance/Month, Legend). `AnswerKeyRow` has all D-32 columns plus DocType/FileName/ParentDocId (A15). Attachment rows copy parent relevance/flags; quoted messages get "Quotes {parent}, which is relevant". `ReviewProtocolBuilder.Build` writes 9 sections; prose prompt built from guide only (D-34). Case Summary lists/opens the files. |

**Score:** 5/5 truths verified

### Required Artifacts

All 22 plans' key files exist and are substantive (13,148 lines across Services/Corpus, Models/Corpus, new controls). No TODO/FIXME/NotImplemented markers. Key ones: `Services/Corpus/CorpusPlanner.cs` (382), `ThreadSkeletonBuilder.cs` (848), `NonRelevantSkeletonBuilder.cs` (918), `CorpusPlanValidator.cs` (839), `RelevantThreadFiller.cs` (644), `SubjectSeeder.cs` (714), `AnswerKeyBuilder.cs`, `AnswerKeyXlsxWriter.cs`, `ReviewProtocolBuilder.cs`, `CorpusApproval.cs`, `CorpusPlanStore.cs`, `UserControls/StepCorpusSettings.cs` (1,600), `StepReviewPlan.cs` + `.Run.cs`.

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| WizardForm | StepCorpusSettings / StepReviewPlan | `StepTypesFor` (line 287), `CaseStage.CorpusSettings/ReviewPlan` mapping (302-303) | WIRED |
| StepReviewPlan.Run | CorpusPlanner | `planner.PlanAsync(...)` line 269, result applied via `CorpusApproval.ApplyNewPlanAsync` | WIRED |
| CorpusPlanner | CorpusPlanValidator | `Validate(draft, plan, mediaAvailable)` line 172 | WIRED |
| StepReviewPlan | PlanSummary / GenerationCostEstimator | lines 891-892 | WIRED |
| Approve | answer key + protocol | `CorpusApproval.ApproveAsync` -> `BuildFiles` -> writers | WIRED |
| StepCaseSummary | written files | file-kind name resolution + `Process.Start` (lines 716-721, 1069, 1101) | WIRED |
| Plan id / hash | guide | `GuideHash = guide.ComputeHash()`; `CorpusApproval.IsGuideStale` | WIRED |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Build clean | `dotnet build ReelDiscovery.csproj` | 0 warnings, 0 errors | PASS |
| Unit suite | `dotnet test ... --filter "Category!=Render"` | 2013/2013 passed | PASS |

(Note: project targets net10.0-windows since 01-01 retarget, not net8 as CLAUDE.md states; pre-existing, not a Phase 4 issue.)

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| PLAN-01 | SATISFIED | StepCorpusSettings size + % relevant slider with live counts |
| PLAN-02 | SATISFIED | Code skeletons own date, people, structure, labels, attachments; LLM writes text only, unknown ids stripped (`RelevantThreadFiller.ApplyBatch`) |
| PLAN-03 | SATISFIED | Time-window chunks across issues + D-15 multi-issue merge (gap fix) |
| PLAN-04 | SATISFIED | BusinessCalendar + per-sender zone + latency |
| PLAN-05 | SATISFIED | ThreadShapes distributions (depth, reply/reply-all/forward, CC, attachments) |
| PLAN-06 | SATISFIED | CorpusPlanValidator rules 1-4 plus 15 more |
| PLAN-07 | SATISFIED | Review Plan summary, Approve, Re-plan |
| KEY-01..04 | SATISFIED | AnswerKeyBuilder + CSV/XLSX writers, Statistics sheet |
| KEY-05 | SATISFIED | ReviewProtocolBuilder .docx |
| PLAT-03 | SATISFIED | ReelDiscovery.Tests/Corpus/* (planner, validator, answer-key writers) |

All 13 IDs claimed by plans; no orphaned Phase 4 requirements in REQUIREMENTS.md.

### Locked Decisions D-01..D-34

Spot-checked and honored: D-01 (Story tier, strict DTOs, `CaseSchemaTests` registers `CorpusPlanner.SchemaTypes`), D-02 (`CorpusPlanPrompts.SystemPrompt` keeps fictional names, firm position), D-05/D-25 (guide hash, stale banner withdraws approval), D-12 + "Resolved during planning" (planning starts on Review Plan entry), D-13 (code owns ids; unknown tokens stripped), D-14/WR-06 (decoy must-not-mention, scenario excluded), D-15 (gap fix verified in code and tests), D-16, D-18 (failed/cancelled plan discarded: `Plan = null`), D-20 (`Random.Shared`, no seed stored), D-22/D-23/D-24 (warnings only), D-26 (three files on approve, approval recorded only on full success), D-27/D-28/D-30/D-32/D-33, D-34 (protocol prompt from guide only). Plan storage in sibling `corpus-plan.json` via `CorpusPlanStore`.

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| ThreadSkeletonBuilder (merge) | In RichDraft every qualifying pair has only one shared knower, so merged multi-issue threads become notes to self | Info | Real conversations happen when pairs share 2+ knowers (ConcurrentDraft test); realism depends on the guide |
| deferred-items.md | `Padding = 1` cards show square inner corners | Info | Cosmetic, pre-existing Phase 3 pattern; mitigated in 04-17/04-18 by full-bleed padding |
| WR-01 / WR-02 / WR-08 | Smallest-safe mitigations, not full redesigns (e.g. old protocol file left after folder rename) | Info | Documented known gaps, not goal-blocking |

### Human Verification Required

1. **CR-02 reopen without auto re-plan.** Reopen a case saved at Review Plan with an unapproved plan and changed settings (plus stale and dismissed variants). Expected: no automatic re-plan, the correct banner, and wording for an unapproved plan. Why human: the fix landed after the 04-20 human walk, and it depends on the reopen path.
2. **WR-09 Cancel timing.** Click Cancel just as planning finishes, then again after the plan appears. Expected: the button is disabled after return, a late confirmed cancel discards the new plan, and a click after the plan is applied shows the "too late" text. Why human: this is a UI race with no automated click test.
3. **WR-06 decoy quality.** With a live provider, read the decoy rows in answer-key.csv. Expected: they share keywords but never paraphrase a key fact. Why human: the check uses heuristic thresholds.

### Gaps Summary

No blocking gaps. All five roadmap criteria and all 13 requirements are implemented, wired and covered by passing tests (2013/2013). The status is human_needed only because three review fixes were applied after the user's 150% walk-through, and the fix report itself flags them for a human check.

---

_Verified: 2026-10-03_
_Verifier: Claude (gsd-verifier)_
