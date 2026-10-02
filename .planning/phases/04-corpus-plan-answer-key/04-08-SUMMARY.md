---
phase: 04-corpus-plan-answer-key
plan: 08
subsystem: corpus
tags: [persistence, summary, cost-estimate, tdd]
requires:
  - phase: 04-01
    provides: CorpusPlan, CorpusState, PlannedThread/Message/Attachment models
  - phase: 04-02
    provides: BusinessCalendar.ResolveId / LocalDate
provides:
  - CorpusPlanStore (corpus-plan.json save/load/EnsureLoaded, PlanLoadResult)
  - PlanSummary.From(plan, guide) with MonthBucket, IssueCountRow, FactCountRow, CustodianCountRow
  - GenerationCostEstimator.Estimate(plan, model) -> CostEstimate
affects: [04-planner-orchestration, 04-review-plan-ui, case-open-flow]
tech-stack:
  added: []
  patterns: [atomic write before case.json, never-throw load with user-facing reason, pure summary projection]
key-files:
  created:
    - Services/Corpus/CorpusPlanStore.cs
    - Services/Corpus/PlanSummary.cs
    - Services/Corpus/GenerationCostEstimator.cs
    - ReelDiscovery.Tests/Corpus/CorpusPlanStoreTests.cs
    - ReelDiscovery.Tests/Corpus/PlanSummaryTests.cs
  modified: []
key-decisions:
  - "CorpusPlanStore.EnsureLoaded with no corpus/no PlanId returns an empty-Reason result (Plan null); callers distinguish failure by a non-empty Reason, not Succeeded"
  - "Plan JSON 'null' or a blank case folder is reported as unreadable/missing rather than throwing; InvalidOperationException added to the filtered catch to match CaseStore.Load"
  - "PlanSummary month buckets span the settings window widened to every planned message (bucket sums always equal Emails); windows over 600 months are ignored as typos"
  - "Fact rows: Threads = relevant threads listing the fact, Emails = relevant messages whose own FactIds carry it, Hot = IsHot among those; HotEmails counts IsHot only in relevant threads"
  - "Custodians tile counts distinct CustodianIds that are cast members; custodian rows tie-break by name (case-insensitive) then cast id"
metrics:
  duration: 12min
  completed: 2026-10-02
  tasks: 2
  files: 5
---

# Phase 4 Plan 08: Plan Persistence, Summary and Cost Estimate Summary

Compact `corpus-plan.json` persisted atomically via `CaseStore.WriteAtomicAsync` with a never-throw, PlanId-checked, 100 MB-capped loader, plus a pure `PlanSummary` projection (tiles, whole-window sender-local month histogram, issue/fact/custodian tables) and a heuristic Story-model generation cost estimate with a relevant/other/attachment breakdown.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | CorpusPlanStore | 81d1ae8 (RED), 94e74b2 (GREEN) | Services/Corpus/CorpusPlanStore.cs, ReelDiscovery.Tests/Corpus/CorpusPlanStoreTests.cs |
| 2 | PlanSummary + GenerationCostEstimator | 74eff61 (RED), 579d994 (GREEN) | Services/Corpus/PlanSummary.cs, Services/Corpus/GenerationCostEstimator.cs, ReelDiscovery.Tests/Corpus/PlanSummaryTests.cs |

## What Was Built

- **CorpusPlanStore** (`public static class`): `MaxPlanFileBytes = 100L * 1024 * 1024`, internal `SizeCapOverride` (tests), `PlanPath(folder)` from the folder + constant name only (T-04-26), `Serialize` with `CaseDraft.JsonOptions` copy and `WriteIndented = false`, `SaveAsync` (atomic temp+move), `Load(folder, expectedPlanId)` with FileInfo size check before reading, FormatVersion peek, Normalize and PlanId compare; all JSON/IO errors become `"The plan file could not be read: {reason}."`. `EnsureLoaded(draft)` attaches the plan to `draft.Corpus.Plan`. XML doc states the plan-first-then-case.json ordering (Pitfall 11).
- **PlanSummary**: Emails, Threads, Custodians, Relevant, NonRelevant, RelevantPercent (1 dp, AwayFromZero), HotEmails, HotFacts, PrivilegedThreads/Emails, Attachments, EmailsWithAttachments, AttachmentPercent, AttachmentsByType, Months, ByIssue (decoys count under NotRelevant), ByFact (every dated fact incl. zero rows, date order), ByCustodian (sender if cast else first cast recipient; Total desc then name). Null-safe throughout.
- **GenerationCostEstimator**: RESEARCH constants (4000/450, 1500/300, 3000/1500, 800/200, `QaOverhead = 1.15m`) marked `[ASSUMED]`; documents = Word/Excel/PowerPoint, small = CalendarInvite/Image/Voicemail; media billing excluded; null model -> zero estimate.

## Verification

- `dotnet test ... --filter "FullyQualifiedName~PlanSummaryTests|FullyQualifiedName~CorpusPlanStoreTests"`: 22 passed
- `dotnet test ... --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 292 passed
- Acceptance greps: WriteAtomicAsync 1, `WriteIndented = false` 1, mismatch message 1, `From(CorpusPlan plan, CaseGuide guide)` 1, `QaOverhead = 1.15m` 1, CalculateTotalCost 5
- No new build warnings in the new files

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Robustness] Extra never-throw cases in CorpusPlanStore.Load**
- **Found during:** Task 1
- **Issue:** A plan file containing JSON `null` deserializes to null, and a blank case folder would resolve the plan path relative to the working directory.
- **Fix:** `null` -> "The plan file could not be read: the file is empty."; blank folder -> the missing-file reason; `InvalidOperationException` added to the filtered catch (same set as `CaseStore.Load`).
- **Files modified:** Services/Corpus/CorpusPlanStore.cs
- **Commit:** 94e74b2

Requirements PLAN-02, PLAN-07 and PLAT-03 are only partly delivered by this plan (persistence and summary data; the planner pipeline, Review Plan UI and approve/re-plan come in later 04 plans), so they were not marked complete.

## TDD Gate Compliance

RED (`test(04-08)`) commits 81d1ae8 and 74eff61 precede GREEN (`feat(04-08)`) commits 94e74b2 and 579d994. No refactor commits were needed.

## Known Stubs

None.

## Self-Check: PASSED
