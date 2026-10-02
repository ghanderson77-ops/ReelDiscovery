---
phase: 04-corpus-plan-answer-key
plan: 09
subsystem: corpus
tags: [planner, skeleton, scheduling, privilege, attachments, repair, tdd]
requires:
  - phase: 04-01
    provides: PlannedThread/PlannedMessage/PlannedAttachment, MessageKind, RelevanceBasis, CorpusSettings overrides
  - phase: 04-02
    provides: BusinessCalendar (NextBusinessDay, IsBusinessDay, LocalDate), CorpusTestData.RichDraft
  - phase: 04-03
    provides: SkeletonContext, ThreadShapes, ThreadScheduler, BudgetAllocator
provides:
  - ThreadSkeletonBuilder.BuildRelevant(SkeletonContext, RelevantBudget) -> List<PlannedThread>
  - ThreadSkeletonBuilder.MultiIssueShare / MultiIssueWindowBusinessDays / DefaultPrivilegedPercent
affects: [04-planner-orchestration, 04-relevant-thread-filler, 04-plan-numbering, 04-validator]
tech-stack:
  added: []
  patterns: [spec-level decisions before message building, global swap-from-index repair, exact-share attachment selection]
key-files:
  created:
    - Services/Corpus/ThreadSkeletonBuilder.cs
    - ReelDiscovery.Tests/Corpus/ThreadSkeletonTests.cs
  modified: []
key-decisions:
  - "Multi-issue pairs: facts with different issue sets, fact dates within 10 business days and at least 2 shared active-cast knowers; merges keep one single-fact thread per fact and spill overflow above depth 12 into another thread of the dropped fact, then the kept fact, then a new thread"
  - "Merges, privilege and participant pools are decided on thread specs before messages are built (same result as structure -> merge -> privilege, without rebuilding To/CC)"
  - "Privileged side mirrors validator rule 12: in-house = own org, outside = firm + RepresentsOrgIds, plus any law firm representing those clients; candidates need counsel + one other active client-side knower; target round(threads x pct), min 1 when any candidate"
  - "Attachments use an exact share: round(messages x AttachmentPercent) messages picked at random among those that can carry an enabled type (calendar only on Seq 1, voicemail only on single-message threads)"
  - "Repair swaps an inactive person from that message onward with a random active pool member not on the rest of the thread (counsel only for counsel on privileged threads); a sender swap reschedules the thread (max 3 passes); otherwise the thread is truncated, noted, and the removed messages go to a replacement thread among people active from the anchor through +30 days"
  - "Hot picks Own messages carrying the Hot fact ordered by distinct participant count desc, then SentAt, then id"
metrics:
  duration: 35min
  completed: 2026-10-02
  tasks: 2
  files: 2
---

# Phase 4 Plan 09: Relevant Thread Skeleton Builder Summary

`ThreadSkeletonBuilder.BuildRelevant` turns the relevant budget into fully structured, scheduled relevant threads. Code decides everything except the text: knowledge-safe participants who are active on the date, Seq/ReplyToSeq/Kind, To/CC from `ThreadShapes`, business-hour timestamps from `ThreadScheduler`, about 12% two-fact cross-issue threads, about 8% privileged threads (counsel and client side only), Hot flags, exact-share attachment specs, and D-19 repair with plan notes.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | Participants, structure, multi-issue threads and scheduling | 10f592a (RED), 0586d67 (GREEN) |
| 2 | Hot, privileged, attachments and repair | ed7385e (RED), 9fb5771 (GREEN) |

## What was built

- **Specs**: one spec per budget thread, in fact-date order. The anchor is the fact date plus 0..2 business days (60%) or 3..15 (40%). It is clamped so it never falls before the fact date and otherwise stays inside the window, then moved to `NextBusinessDay`. So F-007 (Saturday) threads start on or after 2024-06-03, and F-008 (July 4) threads start on or after 2024-07-05.
- **Participants**: everyone who knew all of the thread's facts (`ActorIds ∪ KnownByIds`, cast only), filtered to people active on the anchor. The sender is an actor, Key tier preferred. Root To/CC counts come from `SampleRootToCount`/`SampleRootCcCount`. Later senders are recipients of the parent message (To, or CC 25% of the time). The kind comes from `SampleKind`/`ResolveKind` and recipients from `RecipientsFor`. A Forward goes to a pool member who is not yet on the thread. Own vs Quoted is 60/40.
- **Single knower**: the thread becomes notes to self, and the plan gets the exact note once per fact. If nobody in the pool is active, the actors are used and a note is added. If a fact has no cast knowers at all, the first active Key character writes notes to self and a note is added (T-04-27).
- **Scheduling**: `ThreadScheduler.Schedule(thread, anchor, ctx, notBefore: latest fact date)`.
- **Ids**: threads get `T-{n:0000}` in creation order; messages get `{T}-M{seq:00}`.
- **Determinism**: same seed gives identical JSON. The builder only uses `ctx.Random`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] RichDraft cannot produce a multi-issue thread under the planned rule**
- **Found during:** Task 1
- **Issue:** The multi-issue test was meant to run on RichDraft. But RichDraft has no pair of facts with different issue sets whose dates are within 10 business days and who share knowers. The closest pairs are F-004/F-005 (14 business days apart) and F-006/F-007 (13). So under the planned rule (from RESEARCH Pattern 4) no thread can be merged.
- **Fix:** I kept the rule as planned and tested it on `ConcurrentDraft()`, a copy of RichDraft made only in the test file: F-004 moved to 2024-05-03 and CH-002 added to F-005's knowers. A pair must also share at least 2 knowers, so a two-issue thread is never a note to self. All other Task 1 tests still use RichDraft.
- **Files modified:** ReelDiscovery.Tests/Corpus/ThreadSkeletonTests.cs
- **Commit:** 10f592a

**2. [Rule 1 - Bug] The planned repair test would never trigger truncation**
- **Found during:** Task 2
- **Issue:** With CH-002 inactive after 2024-05-01 and seed 42, every inactive participant could be swapped, so truncation never happened. The swap path is tested, but the truncation path is not.
- **Fix:** I turned the repair test into a Theory. The 2024-05-01 case checks swaps and totals. A second case uses 2024-04-23, the day after F-004, whose only knowers are CH-002 and CH-004. That forces a truncation, and the test checks the note "Thread T-xxxx was shortened because Tom Becker was not active on 2024-04-..", a replacement thread, and exact totals.
- **Commit:** ed7385e / 9fb5771

**3. Design adjustments (same behavior as the plan)**
- `ThreadShapes.SampleHasAttachment`, listed in the plan's interfaces, does not exist. Instead, attachments are an exact share, picked at random among messages that can carry an enabled type. This keeps the share within ±5 points for any seed instead of only on average.
- Truncated messages go into a new replacement thread for the same facts, made of people active from the anchor through +30 days. The plan said "another thread of the same fact". Appending to an existing thread would mean rescheduling it, which can cause new active-range violations. Totals are still exact.
- Merging, privilege and pools are decided on thread specs before the messages are built, instead of rebuilding To/CC afterwards.

## Requirements

PLAN-02, PLAN-03, PLAN-05 and PLAT-03 are moved forward but **not** marked complete in REQUIREMENTS.md:
- PLAN-02 also needs non-relevant thread skeletons and relevance labels for every thread.
- PLAN-03 and PLAN-05 are only reachable once the planner orchestration is built.
- PLAT-03 also covers EML tests and the rest of the planner tests.

## Known Stubs

- `PlannedAttachment.Title` is intentionally "" and the file names are code defaults ("Document 1.docx"). The LLM title is filled in 04-13, as the plan specifies.
- Thread Subject, Purpose and Rationale, and message Intent and MustConvey, are empty by design. The LLM fill comes in a later plan.

## Verification

- `dotnet test ... --filter "FullyQualifiedName~ThreadSkeletonTests|...ThreadSchedulerTests|...BudgetAllocatorTests"`: passed
- `dotnet test ... --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 313 passed, 0 failed
- Build is warning-free. ThreadSkeletonBuilder.cs has 0 uses of `new Random` / `Random.Shared`. The test file has 17 `[Fact]`/`[Theory]` attributes.

## Self-Check: PASSED
