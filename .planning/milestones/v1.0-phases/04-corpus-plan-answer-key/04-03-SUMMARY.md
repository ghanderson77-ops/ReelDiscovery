---
phase: 04-corpus-plan-answer-key
plan: 03
subsystem: corpus-planning-core
tags: [thread-shapes, scheduling, business-time, budget-allocation, largest-remainder]
requires:
  - Services/Corpus/BusinessCalendar.cs
  - Models/Corpus/CorpusSettings.cs
  - Models/Corpus/PlannedThread.cs
  - Models/Corpus/PlannedMessage.cs
  - Helpers/Apportion.cs
provides:
  - ThreadShapeClass + ThreadShapes (depth/kind/recipient/latency/root-time/attachment tables and samplers, ResolveKind, RecipientsFor, Chance)
  - SkeletonContext (cast, organizations, sender zones, active ranges, window, enabled media, Random, Notes)
  - ThreadScheduler.Schedule / CheckInvariants
  - BudgetAllocator (AllocateRelevant -> RelevantBudget/FactAllocation, AllocateNonRelevant, SplitIntoThreads, SignificanceWeight)
affects:
  - Relevant and non-relevant skeleton builders, CorpusPlanValidator (rules 3, 6, 7, 16), planner orchestration
tech-stack:
  added: []
  patterns:
    - Pure static samplers taking an injected Random last (Random.Shared in production, seeded in tests)
    - One Business depth table shared by relevant, decoy and daily threads (label-leakage defense)
    - Hard iteration bound in budget adjustment (T-04-08)
key-files:
  created:
    - Services/Corpus/ThreadShapes.cs
    - Services/Corpus/SkeletonContext.cs
    - Services/Corpus/ThreadScheduler.cs
    - Services/Corpus/BudgetAllocator.cs
    - ReelDiscovery.Tests/Corpus/ThreadShapesTests.cs
    - ReelDiscovery.Tests/Corpus/ThreadSchedulerTests.cs
    - ReelDiscovery.Tests/Corpus/BudgetAllocatorTests.cs
  modified: []
decisions:
  - "[04-03]: SampleKind's parentRecipients = parent To + CC count; ThreadShapes.ResolveKind exposes the degraded kind (ReplyAll without others -> Reply, Forward without target -> Reply, New on non-root -> Reply) so builders can set Kind consistently with RecipientsFor"
  - "[04-03]: A Reply from the parent's own sender (follow-up) addresses the parent's To; if nobody is left it is a note to self"
  - "[04-03]: SampleLatencyMinutes draws identically for internal/external; external = round(internal x 1.5)"
  - "[04-03]: SkeletonContext window falls back settings -> DefaultWindow -> last three months (with a Note); ids outside the cast are always active and use UsTimeZones.Default"
  - "[04-03]: ThreadScheduler bump rule: if the sampled reply time is < parent + 1 min or <= previous + 1 min, AddBusinessMinutes(later of parent/previous, 2..20) in the sender zone"
  - "[04-03]: BudgetAllocator adjusts to the exact target by adding to the shortest thread below 12 and trimming the longest above 1 (fact-then-thread order); full threads add a thread to the fact with the most weight per thread; all-singleton overshoot drops Supporting threads first"
metrics:
  duration: 22min
  completed: 2026-10-02
  tasks: 3
  files: 7
---

# Phase 4 Plan 03: Thread Shapes, Scheduler and Budget Allocator Summary

Enron/Avocado-calibrated thread-shape tables and samplers, a business-time thread scheduler with an invariant checker, a significance-weighted relevant budget allocator with exact totals and shortfall reporting, and the shared SkeletonContext. Every function is pure with an injected Random.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | SkeletonContext + ThreadShapes | f91e234 (RED), f832d81 (GREEN) | Services/Corpus/ThreadShapes.cs, Services/Corpus/SkeletonContext.cs, ReelDiscovery.Tests/Corpus/ThreadShapesTests.cs |
| 2 | ThreadScheduler | 90bd357 (RED), f2a61cd (GREEN) | Services/Corpus/ThreadScheduler.cs, ReelDiscovery.Tests/Corpus/ThreadSchedulerTests.cs |
| 3 | BudgetAllocator | 6dd2ed2 (RED), f1f419c (GREEN) | Services/Corpus/BudgetAllocator.cs, ReelDiscovery.Tests/Corpus/BudgetAllocatorTests.cs |

## Verification

- `dotnet test --filter ThreadShapesTests|ThreadSchedulerTests|BudgetAllocatorTests`: 47 passed (including the 1,000-thread invariant test with median parent->reply gap in 30-240 business minutes)
- `dotnet test --filter "Category!=Render"`: 1440 passed, 0 failed
- No build warnings in the new files; `new Random()` count in ThreadShapes/SkeletonContext = 0
- Acceptance greps: MaxThreadDepth const = 1, SkeletonContext.Create signature = 1, Schedule signature = 1, BusinessCalendar.AddBusinessMinutes >= 1, Apportion.Largest = 2, Shortfall >= 2, BudgetAllocatorTests has 12 [Fact]/[Theory]

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Completeness] ThreadShapes.ResolveKind and Chance added**
- **Found during:** Task 1
- **Issue:** `RecipientsFor` returns only recipients, so a builder could not know that Reply-All/Forward degraded to Reply and would store the wrong `Kind`.
- **Fix:** Public `ResolveKind(kind, parent, senderId, forwardTarget)` used by `RecipientsFor`; small `Chance(percent, random)` helper for attachment rates.
- **Commit:** f832d81

**2. [Rule 2 - Robustness] SkeletonContext helpers**
- Added `OrganizationFor(personId)` and static `FactDate(fact)`; window fallback for guides without dates (last three months, recorded in Notes) and swapped inverted windows.
- **Commit:** f832d81

None of the behavior vectors were changed.

## Requirements

PLAN-03, PLAN-04, PLAN-05 and PLAT-03 are advanced (allocation, scheduling, shape tables and their tests) but not marked complete: anchor dates, participants and applying these tables to real planned threads land in the skeleton-builder plans.

## Known Stubs

None.

## Threat Flags

None. T-04-08 mitigated (depth cap 12, Adjust loop bound 4 x (target + sum) + 100, SplitIntoThreads bounded by messages); T-04-09 mitigated (single Business table for relevant, decoy and daily threads).

## TDD Gate Compliance

Each task has a `test(04-03)` commit preceding its `feat(04-03)` commit; RED for all three was a compile failure (types did not exist).

## Self-Check: PASSED
