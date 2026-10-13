---
phase: 07-scale-cost-qa-cutover
plan: 03
subsystem: corpus-planner, evidence-generation
tags: [privilege-parity, invites, time-zones, start-over, retry]
requires: []
provides:
  - "Daily-business privilege target = max(2% rule, relevant privileged thread share), min 1 whenever relevant mail is privileged (D-23)"
  - "InviteRequest.CanonicalTimeZoneId: new meeting slots derived in one case zone (D-24)"
  - "GenerationReset.TryDelete retry on transient locks (D-25)"
affects: [07-14 leakage audit]
tech-stack:
  added: []
  patterns: ["bounded retry with 100 ms x attempt waits (CaseStore.WriteAtomic analog)"]
key-files:
  created: []
  modified:
    - Services/Corpus/NonRelevantSkeletonBuilder.cs
    - Services/Evidence/InviteScheduler.cs
    - Services/Evidence/ThreadUnitOfWork.cs
    - Services/Evidence/GenerationReset.cs
    - ReelDiscovery.Tests/Corpus/NonRelevantSkeletonTests.cs
    - ReelDiscovery.Tests/Evidence/InviteSchedulerTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationResetTests.cs
decisions:
  - "[07-03]: Relevant privileged share is computed in BuildDaily (the method that receives the relevant threads), not Build"
  - "[07-03]: Canonical invite zone = first guide organization (list order) with a non-empty TimeZoneId; KnownSlots still win unchanged; not-before compares instants against the sender's parent time"
  - "[07-03]: GenerationReset.TryDelete is internal so the retry is tested directly"
metrics:
  duration: 9min
  completed: 2026-10-06
  tasks: 3
  files: 7
---

# Phase 7 Plan 03: Privilege parity, invite time zones and Start Over retry Summary

Daily-business mail is now privileged at the relevant privileged rate (at least one thread whenever relevant mail is privileged). Invites for one meeting share a start instant across sender time zones. Start Over retries files that are locked for a moment.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | D-23 privilege incidence parity | d03d25c (RED), 558e807 (GREEN) |
| 2 | D-24 one meeting time across sender time zones | 1d85907 (RED), 150668b (GREEN) |
| 3 | D-25 Start Over delete retry | 1f464e4 (RED), 55b4d85 (GREEN) |

## What changed

- **D-23:** `BuildDaily` computes `relevantPrivilegedShare` and passes it to `AssignDailyPrivilege`. The target is `max(round(n x 2%), round(n x share))`, and it becomes 1 when it rounds to 0 and either the share is above 0 or there are at least 50 daily threads. The candidate rules (counsel inside the sender organization, no cross-organization threads) did not change, so validator rule 12 still holds. New tests run the real `CorpusPlanner` at 50, 120, 250 and 1,000 emails, plus a no-privileged-relevant case that keeps the old 2% rule.
- **D-24:** `InviteRequest.CanonicalTimeZoneId` is new. `Resolve` derives new slots in that zone (falling back to the sender's zone when it is empty or unknown). `ThreadUnitOfWork` sets it from the first guide organization that has a time zone. Tests: `Invites_DifferentTimeZones_SameInstant`, `Invites_NoCanonicalZone_UseSenderZone` and `Invites_CanonicalZone_NotBeforeComparesInstants`.
- **D-25:** `TryDelete` makes up to `MaxDeleteAttempts = 5` attempts with `Thread.Sleep(100 * attempt)` between them. It runs inside the existing `Task.Run`. A missing file still returns false right away.

## Deviations from Plan

- **[Rule 1 - Test update] D-23 cap in `Daily_SomeThreadsArePrivilegedInsideCounselOrganization`:** this test had a flat 5% upper bound, which the relevant 8% default now exceeds. The bound now follows the D-23 target, and a comment cites D-23. Commit 558e807.
- **Share computed in `BuildDaily` instead of `Build`:** `BuildDaily` is the method that receives the relevant threads, so computing it there needs no extra parameter. The acceptance grep still finds it 3 or more times.
- **`TryDelete` changed from private to internal (Rule 3):** this lets the retry behavior be tested directly; the project already has `InternalsVisibleTo` for the test project.

- **Requirements not marked complete:** QA-02 (leakage audit, 07-14) and OUT-03 (cutover, 07-17+) are delivered by later plans. This plan only contributes to them, so REQUIREMENTS.md was left unchanged.

## Verification

- Filtered suites: NonRelevantSkeleton, DistributionParity, CorpusPlanValidator and CorpusPlanner (127 tests); InviteScheduler, ThreadUnitOfWork and AttachmentUnitOfWork (104); GenerationReset (15). All green.
- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings.
- Full suite `Category!=Render&Category!=Scale`: 3,369 passed, 0 failed.

## TDD Gate Compliance

Each task has a `test(...)` RED commit followed by a `feat`/`fix` GREEN commit.

## Self-Check: PASSED
