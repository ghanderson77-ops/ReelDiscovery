---
phase: 06-attachments-non-relevant-corpus
plan: 18
subsystem: thread-unit-of-work
tags: [attachments, filler, decoys, commit-boundary, chains, invites, noise-01, noise-02, noise-03, noise-04, noise-05, att-04, run-02, out-01, d-12, d-13, d-14, d-16, d-20, d-26, d-29]
requires:
  - "06-05 AttachmentNaming, AttachmentChainLink, InviteScheduler.Resolve / ForNonRelevant, InviteSlot.MeetingKey"
  - "06-06 DecoyScanner.Scan"
  - "06-08 RenderValidator.CheckFiller, LedgerMerger two-pass Build"
  - "06-09 EmailComposer.Compose with attachment parts"
  - "06-10 MailboxWriter attachment parts"
  - "06-16 FillerRenderer.BuildRequest / ToRendered"
  - "06-17 AttachmentUnitOfWork, AttachmentOutcome.ToRecord, MediaRunGate"
provides:
  - "RenderProfile (Relevant / Decoy / Filler) with RenderAsync, Check, Tier, UsesLedger, Model, SideWorld; RenderProfile.For(thread, llm, sideWorld)"
  - "RenderAttempt"
  - "ChainScheduler(links, committedText, threadOfAttachment, threadRank): RankThreads, ThreadOfAttachment, BeginStep, PriorTextAsync, WaitForPredecessorsAsync, Complete, Links; internal WaitEdges / Compare"
  - "ThreadAttachmentContext { Chains, InviteSlots, RowIndex, LateRepair, LaterEstablished }"
  - "ThreadUnitOfWork(llm, journal, mailbox, snapshot, storyModel, AttachmentUnitOfWork?) and RunAsync(ctx, profile, run, attachments)"
  - "ThreadUnitOfWork.CreateAttachmentContext (internal static, for 06-20)"
  - "ThreadOutcome.AttachmentRecords / AttachmentDeltas / Unjournaled / NotRunInputTokens / NotRunOutputTokens / NotRunCostUsd"
  - "internal ThreadUnitOfWork.BeforeAppendHook test seam"
affects: [06-19 runner wiring, 06-20 attachment repair, 06-21 answer key]
tech-stack:
  added: []
  patterns:
    - "Strategy object per thread kind (RenderProfile) over one unit of work"
    - "Single durable commit line: the ThreadDone record nests every attachment outcome; status, events, chain text and invite slots are applied only after the append"
    - "Deadlock-free waits by total rank: a thread waits only on strictly lower-ranked in-step threads; higher-ranked predecessors give the step-start text"
    - "Chain completion in finally with committed texts only after a successful append"
key-files:
  created:
    - Services/Evidence/RenderProfile.cs
    - Services/Evidence/ChainScheduler.cs
    - ReelDiscovery.Tests/Evidence/ChainSchedulerTests.cs
  modified:
    - Services/Evidence/ThreadUnitOfWork.cs
    - ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs
decisions:
  - "ThreadDone.CostUsd stays the thread's own text spend; each nested attachment record carries its own CostUsd/MediaCostUsd (replay already sums nested spend), so nothing is counted twice"
  - "ThreadFailed (copy-write or build failure after the attachment step) carries thread + every attachment text cost in CostUsd, media in MediaCostUsd, and all tokens; the Failed ThreadOutcome reports the same totals for information only (the line already journals them)"
  - "The Unjournaled path (ThreadDone append failed) adds the spend with snapshot.AddCost only and returns all tokens and text + media spend on the outcome; no second journal attempt, no counters, no events"
  - "OwnDeltas for invites = the render's deltas plus the deltas of attachments already committed earlier in the same unit (all thread deltas known so far, unfiltered)"
  - "A same-thread chain predecessor is answered from the unit's own Written/Warnings texts; a failed or skipped same-thread predecessor gives no prior text (earlier draft not available)"
  - "Nested attachment records get the ThreadDone Utc and the parent email's copy paths as Files (Written/Warnings only; Failed/Skipped keep an empty list)"
  - "Cancel before the next attachment starts (or while waiting on chain text) commits the finished parts and then throws OperationCanceledException; the in-flight call becomes NotRun with reason 'the run was cancelled'"
  - "06-08 absorbed-entry edge is documented, not changed: when Build absorbs the registering thread's meeting entry into an earlier-ranked thread's entry, a later invite sees the other key and gets no KnownSlots hit, but InviteScheduler's content seed gives the identical Start/End (tested); InviteMeetingKey differs between the two invites"
metrics:
  duration: 45min
  completed: 2026-10-05
---

# Phase 6 Plan 18: Thread Unit of Work with Profiles, Attachments and a Durable Commit Boundary Summary

Relevant mail, decoys and filler now share one unit of work. The render, check, attachments, compose, write and journal steps are the same for all three; a `RenderProfile` picks the tier, the request, the checks and whether the ledger is used. Each attachment runs inside its thread after the thread is accepted. The thread and all its attachment outcomes commit as one ThreadDone line, and nothing is reported before that line is on disk. Chain predecessors are scheduled deterministically and the waits cannot deadlock.

## What was built

- **RenderProfile** (`Services/Evidence/RenderProfile.cs`):
  - **Relevant**: `ThreadRenderer.BuildRequest` + `RenderRepair.Apply` + `RenderValidator.Check`, Story tier, ledger on.
  - **Decoy**: the filler request on the Story tier, `FillerRenderer.ToRendered`, then `CheckFiller` ∪ `DecoyScanner.Scan`.
  - **Filler**: the filler request on the Volume tier with `CheckFiller` only. This is D-12 strict: V1/V5 only, so filler never retries for length.
  - `Model` is the tier's model, so filler is priced at Volume rates (D-29).
- **ChainScheduler** (`Services/Evidence/ChainScheduler.cs`):
  - `RankThreads` orders threads by first SentAt (`MessageTime.FirstUtc`), then Id ordinal. A strict `Compare` puts threads missing from the plan after every planned one.
  - There is one committed store for the whole run. `Complete` merges into it, and `BeginStep` only refreshes the step-start snapshot and the per-thread `TaskCompletionSource`s.
  - `PriorTextAsync` follows four rules:
    - same-thread predecessor → committed lookup, never waits
    - lower-ranked in-step predecessor → waits for its `Complete`
    - higher-ranked in-step predecessor → step-start text, never waits
    - predecessor not in the step → committed lookup
  - `WaitForPredecessorsAsync` waits on exactly the threads rule 2 would wait on. The internal `WaitEdges` exposes those edges for tests.
- **ThreadUnitOfWork** (`Services/Evidence/ThreadUnitOfWork.cs`):
  - New: `ThreadAttachmentContext`, a constructor overload that takes an `AttachmentUnitOfWork`, and `RunAsync(ctx, profile, run, attachments)`. The old `RunAsync(ctx, run)` delegates to it with the relevant profile.
  - The ThreadDone record:
    - `LedgerDelta` is empty unless `profile.UsesLedger` (D-13).
    - Each `JournalMessage` carries the display `From` and the filler `AttachmentTitles`.
    - `RecordThread` gets `relevant:`.
  - **Attachment step**: runs after the V1/V5 check and before compose, in seq order then plan order. It does not start another attachment after a fatal stop or Cancel.
    - Chain prior text comes from the unit's own texts for a same-thread predecessor, and from `Chains.PriorTextAsync` otherwise. That call is made between provider calls, never inside a `PlanCallRun` slot.
    - `CreateAttachmentContext` builds each attachment's context:
      - names from `AttachmentNaming`
      - the accepted parent body and the accepted thread
      - visible deltas from the parent seq and its ancestors only (empty for filler)
      - the chain link and prior text
      - profile tier and side world
      - the invite slot: `InviteScheduler.Resolve` with `OwnDeltas = threadDeltas` and `KnownSlots = InviteSlots` for relevant mail, `ForNonRelevant` otherwise
  - **Commit boundary**: all writes use `CancellationToken.None`.
    1. Every custodian copy is written. An IO failure here journals ThreadFailed with all attachment spend.
    2. The ThreadDone line is built, with every attachment record nested in it, and appended in its own try/catch. If the append fails, the outcome is `Unjournaled` and the spend goes to `AddCost`.
    3. Only after a successful append: `RecordThread`, `RecordAttachment` per record, KeptWithWarnings / Failed / Skipped events, `AddCost` for NotRun spend, `InviteSlots.TryAdd(slot.MeetingKey, slot)` for committed relevant invites with a key, and the `ThreadWritten` event with `Attachments` = parts written.
  - `Chains.Complete` runs in a `finally`. Its committed texts are non-empty only after a successful append.
  - **NotRun spend**: the committed path returns `NotRunInputTokens` / `NotRunOutputTokens` / `NotRunCostUsd` (text + media) for the runner's `AddUnjournaledSpend`. The ThreadFailed and Unjournaled paths fold that spend into their totals and leave all three at 0.

## Tests

- **ChainSchedulerTests (16)**:
  - seeded lookup; wait-until-Complete across 50 randomized orderings; Complete with no text; no text anywhere; cancelled wait; idempotent Complete
  - `CrossStep_CompleteInStep1_VisibleInStep2`, `Repair_InStepPredecessorWithOldText_WaitsThenNewest`, `SameThread_PredecessorNeverWaits`, `WaitForPredecessors_WaitsOnlyForOtherInStepThreads`
  - `RankThreads_OrderIsFirstSentThenId`, `Rank_HigherRankedInStepPredecessor_NeverWaits_UsesStepStartText`
  - `Chain_InterleavedAcrossTwoThreads_SchedulerNoDeadlock` and `Chain_CrossingChainsTwoThreads_SchedulerNoDeadlock`: 52 repetitions each, both launch orders, 10 s guards
  - `Rank_WaitGraphIsAcyclic`: 5 seeds, 8 threads, random chains plus a deliberate cross-thread cycle
- **ThreadUnitOfWorkTests (+24)**:
  - **Profiles**:
    - filler on Volume: 400 words in a Short band and a stranger's name are accepted on attempt 1; From and titles are journaled; NonRelevantDone counted
    - a missing filler message is V1 and retried
    - decoy D1 retry with feedback, and Warnings after 3 hits
    - newsletter HTML; the relevant profile model
  - **Attachment step**:
    - three parts in one ThreadDone line with byte-identical copies and deltas
    - snapshot and events stay untouched until the blocked append is released
    - a refused Excel is left out of the EML and the bodies are unchanged (D-26)
    - inline image cid; no media key → Skipped
    - same-step chain prompt holds v1's text in 20 randomized runs, and "earlier draft not available" when v1 fails
    - `Chain_InterleavedAcrossTwoThreads_NoDeadlock` and `Chain_CrossingChainsTwoThreads_NoDeadlock` with real units, one shared provider slot, both launch orders, 10 s guards and identical prompts every run
    - a same-thread chain does not wait on itself
    - `Invite_OwnDeltaThenLedger_ReusesRegisteredSlot`, plus the 06-08 absorbed-entry edge
    - the filler invite slot; ancestor-only visible deltas; filler attachments with no deltas and filler-title names
  - **Failure injection**: `Commit_CancelAfterFirstAttachment_KeepsThreadAndPart`, `Commit_SecondCopyWriteFails_NothingReportedWritten`, `Commit_ThreadDoneAppendFails_SpendReturnedUnjournaled`, `Commit_KillBetweenWritesAndAppend_ReplayIsConsistent`, `Commit_FatalDuringAttachments_CommitsFinishedParts`.
- **Totals**: the full suite passed 3293, failed 0 (baseline 3253 + 40), in 7 m 20 s. The app build has 0 warnings; the test build has only the 2 known xUnit1031 warnings in GenerateCorpusStepTests.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Task 2 implementation landed in the Task 1 GREEN commit**
- **Found during:** Task 1
- **Issue:** `ThreadUnitOfWork.RunAsync(ctx, profile, run, attachments)` has a single body that holds both the profile-driven render and the attachment/commit step. Splitting it across two commits would have meant writing and then rewriting the same method.
- **Fix:** The whole unit was written once. The Task 1 commit (c66c1d2) contains it. Task 2 (c03068e) and Task 3 (2314cc5) add the tests for the attachment step and the failure injection, and those pass against that code.
- **Commits:** fdc8632 (RED), c66c1d2 (GREEN), c03068e, 2314cc5

**2. [Rule 2 - Correctness] ThreadOutcome totals on the Failed path**
- **Issue:** The plan asserts NotRun fields are 0 on ThreadFailed "because the spend is inside InputTokens/OutputTokens/CostUsd". FailAsync used to return no totals.
- **Fix:** The Failed outcome now reports the ThreadFailed record's tokens and its text + media spend. They are informational only: the runner must not add them again, because the line already journals them.

**3. [Rule 3 - Blocking] Test fixture switched to AttachmentTestData.MixedCase**
- **Fix:** The fixture moved from ApprovedCase to MixedCase, which is ApprovedCase plus T-0901..T-0915. Models are now priced (Story 3/15, Volume 1/5 per million) so Story and Volume spend can be told apart. All 12 existing tests pass unchanged.

**4. [Rule 2] `ChainScheduler.ThreadOfAttachment(plan)` helper**
- **Fix:** Added a public static helper that builds the `threadOfAttachment` constructor argument from the plan, for the runner (06-19) and the tests.

## Known edge (06-08, documented)

`LedgerMerger.Build` places thread entries first, ordered by wave, then first SentAt, then Id. When an earlier-ranked thread C in the same wave states the same meeting as thread A, Build absorbs A's entry into C's. A registered its slot under `mtg:{A}:...`. A later invite B then sees `mtg:{C}:...` and gets no `KnownSlots` hit. The schedule is still identical: InviteScheduler derives the slot from the content seed `mtg~{detail}|{date}`. `Invite_MeetingAbsorbedIntoEarlierThreadEntry_SameSchedule` asserts equal Start/End and documents the differing keys.

There is a residual risk. If B's sender is in a different time zone from A's, the seeded slot is derived in B's zone and could differ. Fixing that belongs in InviteScheduler (06-05 scope) and is not changed here.

## Known Stubs

None.

## Threat Flags

None. No new endpoints or file locations: writes reuse MailboxWriter and GenerationJournal, and the `BeforeAppendHook` seam is internal and used only by tests.

## TDD Gate Compliance

- RED: fdc8632 (`test(06-18)`) contained only the profile and scheduler tests, which did not compile without the new types.
- GREEN: c66c1d2 (`feat(06-18)`).
- The Task 2 and Task 3 test commits (c03068e, 2314cc5) came after GREEN (see deviation 1). No refactor commit was needed.

## Self-Check: PASSED

- Files exist: Services/Evidence/RenderProfile.cs, Services/Evidence/ChainScheduler.cs, ReelDiscovery.Tests/Evidence/ChainSchedulerTests.cs, Services/Evidence/ThreadUnitOfWork.cs.
- Commits fdc8632, c66c1d2, c03068e and 2314cc5 are in the log.
- Every acceptance grep passed for all three tasks.
