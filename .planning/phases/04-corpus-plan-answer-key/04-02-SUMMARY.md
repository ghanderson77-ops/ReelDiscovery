---
phase: 04-corpus-plan-answer-key
plan: 02
subsystem: corpus-planning-infrastructure
tags: [business-calendar, time-zones, holidays, llm-fan-out, test-fixtures]
requires:
  - Services/Case/UsTimeZones.cs
  - Services/Ai/ILlmGateway.cs
  - ReelDiscovery.Tests/Case/CaseTestData.cs
provides:
  - BusinessCalendar (observed US federal holidays, sender-local business instants, business-minute arithmetic, zone resolution)
  - PlanCallRun / PlanCallOutcome<T> / PlanCallRunner.RunAsync / DescribeOutcome
  - PlanStage / PlanProgress / FillResult
  - CorpusTestData.RichDraft, CreateGateway, ThreadIdsIn, ThreadSeqsIn, SlotIdsIn, DatedFactCount
  - Thread-safe FakeLlmProvider with Respond hook
  - RenderHarness.WizardMin (1000x540)
affects:
  - All later Phase 4 planner, filler, validator and UI plans
tech-stack:
  added: []
  patterns:
    - ConcurrentDictionary caches for holidays per year and resolved zones per id
    - Semaphore acquired on the run token with an acquired flag so fatal stops release waiters without over-releasing
key-files:
  created:
    - Services/Corpus/BusinessCalendar.cs
    - Services/Corpus/PlanCallRunner.cs
    - Services/Corpus/PlanProgress.cs
    - ReelDiscovery.Tests/Corpus/BusinessCalendarTests.cs
    - ReelDiscovery.Tests/Corpus/PlanCallRunnerTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusTestData.cs
  modified:
    - ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs
    - ReelDiscovery.Tests/Render/RenderHarness.cs
decisions:
  - "[04-02]: AddBusinessMinutes landing exactly on 17:00 rolls to 09:00 of the next business day (results are always business time); Tue 10:00 + 480 = Wed 10:00, + 600 = Wed 12:00"
  - "[04-02]: BusinessCalendar.ResolveId returns null for blank/unknown ids (failures not cached); the filtered catch also covers ArgumentException; Resolve falls back TimeZoneId -> Location zone -> America/New_York -> UTC"
  - "[04-02]: PlanCallRunner waits on run.Cts.Token and releases the semaphore only when acquired; PlanCallOutcome carries RefusalMessage for FillResult"
  - "[04-02]: FakeLlmProvider invokes OnSend outside its lock (tests may block in it); Respond is called with the recorded snapshot and overrides the queue"
metrics:
  duration: 14min
  completed: 2026-10-02
  tasks: 3
  files: 8
---

# Phase 4 Plan 02: Planning Infrastructure Summary

BusinessCalendar (observed US federal holidays, sender-local 09:00-17:00 business time via IANA zones, business-minute arithmetic), a shared bounded LLM-call runner with the CaseGuideGenerator cancel/fatal/timeout/refusal ladder, PlanProgress/FillResult types, a rich two-issue Chicago/Phoenix test fixture and a thread-safe fake provider.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | BusinessCalendar | ed925a0 (RED), feec3bb (GREEN) | Services/Corpus/BusinessCalendar.cs, ReelDiscovery.Tests/Corpus/BusinessCalendarTests.cs |
| 2 | PlanCallRunner + PlanProgress | eaf6ef5 (RED), 41b5941 (GREEN) | Services/Corpus/PlanCallRunner.cs, Services/Corpus/PlanProgress.cs, ReelDiscovery.Tests/Corpus/PlanCallRunnerTests.cs, ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs |
| 3 | Rich fixture, thread-safe fake, WizardMin | 3a821d5 | ReelDiscovery.Tests/Corpus/CorpusTestData.cs, ReelDiscovery.Tests/Render/RenderHarness.cs, ReelDiscovery.Tests/Corpus/PlanCallRunnerTests.cs |

## Verification

- `dotnet test --filter BusinessCalendarTests`: 27 passed
- `dotnet test --filter PlanCallRunnerTests|CaseGuideGeneratorTests|CoreDocumentGeneratorTests|LlmGateway`: 132 passed
- `dotnet test --filter "Category!=Render"`: 1388 passed, 0 failed
- No build warnings in new/modified files; Services/Case/CaseGuideGenerator.cs unchanged

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug in plan] AddBusinessMinutes vector corrected**
- **Found during:** Task 1
- **Issue:** The behavior bullet said Tue 10:00 + 600 business minutes = Wed 10:00, but with 480 business minutes per day that is +480; +600 is Wed 12:00.
- **Fix:** Tests assert Tue 10:00 + 480 = Wed 10:00 and + 600 = Wed 12:00. Also pinned that landing exactly on 17:00 rolls to 09:00 next business day so results are always business time.
- **Commit:** ed925a0

**2. [Rule 3 - Blocking] FakeLlmProvider thread-safety moved into Task 2**
- **Found during:** Task 2
- **Issue:** The semaphore test (2 parallel calls) would race on the fake's unsynchronized `Requests` list; the lock was scheduled for Task 3.
- **Fix:** Lock + `Respond` hook added in the Task 2 GREEN commit; Task 3 added the Respond test.
- **Commit:** 41b5941

**3. [Rule 2 - Robustness] Semaphore over-release guard and ArgumentException in zone lookup**
- Waiting on `run.Cts.Token` inside the try (as planned) means a cancelled waiter would hit `finally` without holding a slot; an `acquired` flag guards the release (test `FatalStop_ReleasesQueuedWaiters`).
- `ResolveId` also filters `ArgumentException` so crafted ids can never throw (T-04-05).
- **Commits:** 41b5941, feec3bb

**4. [Convention] CorpusTestData uses [GeneratedRegex]** per CLAUDE.md instead of `new Regex(...)` (class made `partial`).

## Requirements

- PLAN-04: business-time, time-zone and holiday infrastructure delivered; the latency mixture and scheduling of actual planned messages land in later plans, so PLAN-04 is not marked complete here.
- PLAT-03: planner tests are carried across Phase 4; not complete from this plan alone.

## Known Stubs

None.

## TDD Gate Compliance

Task 1: test commit ed925a0 precedes feat commit feec3bb. Task 2: test commit eaf6ef5 precedes feat commit 41b5941. RED for both was a compile failure (types did not exist).

## Self-Check: PASSED
