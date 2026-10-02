---
phase: 04-corpus-plan-answer-key
plan: 04
subsystem: corpus-plan-validation
tags: [validator, warnings, leakage, privilege, docids, business-time]
requires:
  - Models/Corpus/CorpusPlan.cs
  - Models/Corpus/CorpusSettings.cs
  - Services/Corpus/BusinessCalendar.cs
  - Services/Corpus/ThreadShapes.cs
  - Models/Case/CaseGuide.cs (ComputeHash)
provides:
  - PlanArea, PlanFinding (Severity always Warning, Where, FixHint)
  - CorpusPlanValidator.Validate(CaseDraft?, CorpusPlan?, bool mediaAvailable)
  - PlanFixtures.ValidPlan(CaseDraft) / PlanFixtures.Clone(CorpusPlan) test fixtures
affects:
  - Review Plan step Checks list, planner repair pass, later planner/answer-key tests (fixture reuse)
tech-stack:
  added: []
  patterns:
    - CaseGuideValidator-shaped static validator (Context + per-rule Run isolation + CouldNotRun)
    - Mix check against the planner's own Apportion.Largest split (rounding never warns)
    - Leakage metrics skipped on a zero value in either group (no divide-by-zero, no single-attachment noise)
key-files:
  created:
    - Services/Corpus/CorpusPlanValidator.cs
    - ReelDiscovery.Tests/Corpus/PlanFixtures.cs
    - ReelDiscovery.Tests/Corpus/CorpusPlanValidatorTests.cs
  modified: []
decisions:
  - "[04-04]: Rule 15 compares each non-relevant category with Apportion.Largest(actual non-relevant count, settings mix) and warns when the drift exceeds 3 points; the message still shows the actual share vs the settings percent"
  - "[04-04]: Rule 12 privileged side = the first counsel on the thread (from/to/cc in Seq order): in-house -> own org, outside -> firm + RepresentsOrgIds; any firm representing a client is also allowed; anyone else (incl. externals) is 'from the other side'"
  - "[04-04]: Rule 14 checks DocID set equality with {prefix}-000001..n (not strict chronological order) plus attachment {parent}.{k:0000}; first problem only"
  - "[04-04]: Rule 18 metrics = mean thread depth per thread, share of messages with attachments, mean To+CC, mean sender-local hour; relevant vs DailyBusiness+Decoy"
  - "[04-04]: Validator reads plan.Settings/ToIds/CcIds as stored (no Normalize); null Settings skips settings rules; instants at DateTime edges skip zone conversion (rule 5 still flags them)"
metrics:
  duration: 15min
  completed: 2026-10-02
  tasks: 2
  files: 3
---

# Phase 4 Plan 04: Corpus Plan Validator Summary

`CorpusPlanValidator` checks a corpus plan against its case guide and settings with 19 rules. Every finding is a Warning with a fix hint, so nothing blocks Approve. Each rule runs inside its own try/catch, so Validate never throws and one broken rule cannot hide the others. A hand-built 20-email `PlanFixtures.ValidPlan` passes every rule.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | PlanFixtures.ValidPlan | 60ae51b | ReelDiscovery.Tests/Corpus/PlanFixtures.cs |
| 2 | CorpusPlanValidator (19 rules, warnings only) | ce00597 (RED), e655dc8 (GREEN) | Services/Corpus/CorpusPlanValidator.cs, ReelDiscovery.Tests/Corpus/CorpusPlanValidatorTests.cs |

## What Was Built

- **PlanFixtures.ValidPlan(RichDraft)**: 8 one-message relevant fact threads (F-001 privileged with in-house counsel CH-005; F-003 hot with "Change Order 7.docx"; F-007 on the Monday after the Saturday fact; F-008 as a note to self the day after the holiday). It also has 6 daily-business threads (one with "Site schedule.docx" inside CH-009's narrow range), 3 ancillary, 2 newsletter threads from EXT-001, and 1 decoy. Every message is at 10:00 sender-local. DocIDs run RD-000001.. in UTC order, and the guide and settings hashes are stamped. The fixture is never passed through Normalize. `Clone` makes a JSON round-trip copy.
- **CorpusPlanValidator rules** (exact messages as in the plan):
  1. Email count
  2. Relevant ratio, within max(1, 1%)
  3. Participants inside their active range on the sender-local date
  4. Every dated fact has evidence
  5. Date window
  6. Business time and holidays in the sender zone (externals use UsTimeZones.Default)
  7. Monotonic Seq/ReplyToSeq
  8. Id resolution
  9. Label consistency
  10. Facts mentioned before their date
  11. Knowledge (note-to-self skipped)
  12. Privilege plausibility
  13. Attachment kinds and file names (`..` and invalid characters flagged)
  14. DocID uniqueness and contiguity
  15. Non-relevant mix
  16. Depth cap 12
  17. Fallback threads and plan Notes
  18. Leakage ratios
  19. Staleness against `Guide.ComputeHash()` and `DismissedStaleGuideHash`

## Verification

- `dotnet test --filter CorpusPlanValidatorTests`: 40 passed (38 [Fact]/[Theory] attributes).
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 174 passed.
- `dotnet build ReelDiscovery.csproj`: succeeded, no warnings.
- Acceptance greps: Validate signature = 1, non-comment `IssueSeverity.Error` = 0, "No planned email is evidence of" = 1, "A check could not run" = 1, BusinessCalendar.IsBusinessTime/Resolve present.

## Deviations from Plan

None. The plan executed as written. These choices fill in details the plan left open (recorded as decisions above):
- Rule 15 compares against the apportioned whole-email split. A raw percentage comparison would have flagged the plan's own fixture: 3/12 ancillary is 25% against 20%.
- Rule 9 joins several label problems for a thread into one finding, separated by "; ".
- Rule 8 skips blank ids. Blank DocIDs and message ids are reported by rule 14.

## TDD Gate Compliance

Task 2: `test(04-04)` ce00597 (RED: compile failure, types missing) came before `feat(04-04)` e655dc8 (GREEN). Task 1 is a fixture-only task with a `test(04-04)` commit. No refactor was needed.

## Requirements

- PLAN-06: complete. The validator covers counts, ratio, active ranges and fact evidence, plus the extended rule set.
- PLAT-03: advanced (validator tests), not complete. The answer-key writer tests come in later plans.

## Known Stubs

None.

## Threat Flags

None. T-04-10 is mitigated: per-rule try/catch, TryAdd dictionaries, null-safe iteration and guarded zone conversion. T-04-11 is mitigated by rule 13 (`..` and invalid file-name characters). T-04-12 is mitigated by rule 18.

## Self-Check: PASSED
