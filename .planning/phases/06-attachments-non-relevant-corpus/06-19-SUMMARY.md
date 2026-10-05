---
phase: 06-attachments-non-relevant-corpus
plan: 19
subsystem: generation-runner
tags: [runner, attachments, filler, side-world, voices, chains, invites, d-05, d-06, d-13, d-16, d-18, d-20, d-21, d-25, d-28, d-29, noise-01, noise-02, noise-03, noise-04, noise-05, run-02, out-01, att-04]
requires:
  - "06-12 SideWorldBuilder.BuildAsync / FromRecord / CoveredOrganizations, CastVoiceBuilder.BuildAsync / FromRecord"
  - "06-14 GenerationRunState.AllAssignments, AttachmentSeeds, BuildOverlay(plan, assignments, chains), EstimateRemaining(... volume, prices, mediaAvailable, guide), InviteSlotsByMeeting"
  - "06-17 AttachmentUnitOfWork (CastVoices), MediaRunGate"
  - "06-18 ThreadUnitOfWork(…, AttachmentUnitOfWork), RunAsync(ctx, profile, run, attachments), RenderProfile, ChainScheduler, ThreadOutcome.AttachmentDeltas / Unjournaled / NotRun*"
provides:
  - "GenerationRunInput.Media (IMediaGenerator?) and MediaPrices"
  - "GenerationRunner.FillerChunkSize = 50"
  - "One run: relevant waves with attachments, then the side world and every non-relevant thread with attachments"
  - "internal test seams GenerationRunner.BeforeRecordAppendHook (CastVoices / SideWorld appends) and BeforeThreadAppendHook (passed to ThreadUnitOfWork.BeforeAppendHook)"
affects: [06-20 attachment repair in the wave loop, 06-23 step wiring (Media, MediaPrices, ShowRunResult stopgap)]
tech-stack:
  added: []
  patterns:
    - "Choices that Continue must repeat (voices, side projects) are journaled before first use; a failed append ends the run as InternalError"
    - "One ThreadAttachmentContext per run step sharing the run-wide ChainScheduler; invite slots copied in at step start and kept after the step"
    - "Unjournaled and NotRun spend accounted once, in RunThreadAsync, so cancel-free waves and filler chunks share one path"
key-files:
  created: []
  modified:
    - Services/Evidence/GenerationRunner.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
decisions:
  - "Unjournaled / NotRun spend is added in RunThreadAsync (one helper, AddUnjournaled) rather than in the post-WhenAll loop, so waves and filler chunks use one path and a thread's spend is counted even if a later thread of the step throws"
  - "ThreadAttachmentContext.InviteSlots is get-only, so the runner keeps a run-wide ConcurrentDictionary (seeded from InviteSlotsByMeeting), copies it into each step's context and copies the step's committed slots back after Task.WhenAll; first slot per meeting key wins"
  - "SideProjectsReady is raised only when the side world is created in this run; a replayed side world goes straight to NonRelevantStarted"
  - "The voice and side-world calls are added to the ticker with snapshot.AddCost whether or not their record is saved; when the record is not saved (or the call ended fatal) the spend goes to RunEnded via AddUnjournaledSpend"
  - "The Non-relevant stage row (Index waves.Count + 1, RowKind NonRelevant) is part of snapshot.Reset from Task 1 on; wave and filler rows get Attachments / AttachmentsDone / Warnings / Failed / Skipped from the seeds"
  - "Phase 5 runner tests keep their meaning on a relevant-only, attachment-free copy of ApprovedCase; the mixed corpus is covered by the new GenerationRunnerMixedTests class in the same file"
metrics:
  duration: 30min
  completed: 2026-10-05
---

# Phase 6 Plan 19: Runner with Attachments, Voices and the Non-relevant Stage Summary

One run now writes the whole mixed corpus in the D-25 order. Relevant threads and their attachments go first, in chronological waves. Only committed thread and attachment details reach later waves. Then the side world is built once, or replayed on Continue, and journaled. After that, every non-relevant thread and its attachments are written in chunks of 50, with filler assignments and category profiles and without the ledger. Voicemail voices are chosen once and journaled before the first voicemail is spoken. Every unjournaled token and dollar reaches RunEnded exactly once.

## What was built

**`Services/Evidence/GenerationRunner.cs`**

- **Inputs.** `GenerationRunInput.Media` (`IMediaGenerator?`) and `MediaPrices`. One `AttachmentUnitOfWork(_llm, snapshot, input.Media, gate, story, volume)` is shared by every thread unit, with one `MediaRunGate` per run. `SetRemaining` is set to the image and voicemail count of threads not yet written.
- **Run context.**
  - `Assignments = GenerationRunState.AllAssignments(plan)`: relevant assignments plus filler assignments.
  - `Chains = AttachmentChains.Compute(plan, guide)`.
  - `ChainScheduler(chains, committedTexts, ThreadOfAttachment(plan), RankThreads(plan))`. The committed texts are the FlattenedText of every committed attachment record in the journal.
  - A run-wide `InviteSlots` map, seeded from `state.InviteSlotsByMeeting`.
  - `VolumeModel`.
- **Snapshot reset.**
  - Counts come from `ToCounts`, so RelevantTotal, AttachmentsTotal and MediaPlanned are included.
  - Wave rows plus the Non-relevant row carry thread and attachment counters.
  - Seeds come from `state.AttachmentSeeds(plan, waveOf, fillerRow = waves+1, repairRow = waves+2, mediaAvailable)`.
- **Voice set (D-28).** This runs after the RunStarted record, when media is available and a voicemail has no committed outcome:
  - `CastVoiceBuilder.FromRecord(replay.LastCastVoices)` is tried first. Only when it returns null is there one `BuildAsync` call.
  - A fatal provider error on that call → `Fatal`, and no stage runs.
  - A built set is journaled as a `CastVoices` record with the Volume cost. A replayed set is never re-journaled.
  - If that append fails → `InternalError` "Could not save the voicemail voices: …", the spend goes to RunEnded, and no stage runs.
  - Otherwise `attachmentsUow.CastVoices = voices`.
- **Relevant waves.**
  - Each wave starts with `BeginStep(pending)` and a step `ThreadAttachmentContext { Chains, RowIndex = wave }` that holds the known invite slots.
  - Each thread runs with `RenderProfile.Relevant`.
  - After the wave, only Written/Warnings results with a record merge, as `Record.LedgerDelta` followed by `AttachmentDeltas` (D-18, review concern 1). Committed invite slots are kept for later steps.
- **RunThreadAsync.**
  - A thread the run never starts (fatal, or cancelled before start) still calls `ChainScheduler.Complete(id, empty)`. A catch-and-rethrow does the same if anything throws before the unit's own finally runs.
  - NotRun keeps the existing `AddUnjournaledSpend(outcome…)` path.
  - Unjournaled outcomes add their full totals. Committed outcomes add `NotRunInputTokens/NotRunOutputTokens/NotRunCostUsd`. Both use the three-argument form, and each amount is counted once.
- **Non-relevant stage (`RunNonRelevantAsync`).** It runs only when the relevant waves finished cleanly and filler is pending. Pending filler is ordered by first SentAt, then Id.
  1. The side world comes from `SideWorldBuilder.FromRecord(replay.LastSideWorld)`. If there is none, the stage switches to `SideProjects` and makes one `BuildAsync` call:
     - fatal → `Fatal`
     - the `SideWorld` record cannot be saved → `InternalError` "Could not save the side projects: …"
     - otherwise the record is saved and a `SideProjectsReady` event follows
  2. Stage `NonRelevant`, the filler row marked running, and a `NonRelevantStarted` event.
  3. Chunks of `FillerChunkSize = 50`. Each chunk calls `BeginStep(chunk ids)`, and each thread uses `ThreadRenderContext.Create(…, Assignments[id], People, no ledger, fillerRow)` with `RenderProfile.For(thread, _llm, sideWorld)`. A fatal stop ends the stage, and a checkpoint follows every chunk.
  4. A `NonRelevantFinished` event with the written, warnings and failed counts.
- **Checkpoints.** They use `BuildOverlay(plan, Assignments, Chains)` and `EstimateRemaining(plan, state, story, volume, MediaPrices, mediaAvailable, guide)`. The old 2-argument overlay and 3-argument estimate are no longer called. `RunCancelled` now carries the attachment progress.
- `run.SetFatal` is never called for media. A media stop only marks the remaining media items Skipped.

## Tests

`ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs`:

- **GenerationRunnerTests (13, unchanged assertions).** These now run on a relevant-only, attachment-free ApprovedCase.
- **GenerationRunnerMixedTests (31 new, on MixedCase with priced models).** Every run has a 60 s guard; the chain tests have a 10 s guard.
  - **Task 1:**
    - ThreadDone nests the attachments; no standalone attachment lines are written; EML parts are present.
    - A wave-1 attachment delta is seen only by later-wave prompts.
    - An unjournaled thread contributes no deltas, gives its successor "earlier draft not available", and its spend (1000+3×800 / 400+3×300 at Story rates) is in RunEnded. The ticker equals the journaled spend plus RunEnded.
    - Reset rows and counts: attachments per wave, the NonRelevant row and MediaPlanned. After a Continue, the counters match the journal.
    - Key statuses Generated / Failed / Skipped and the `_v2` chain name.
    - A media stop gives exactly one MediaStopped and Finished.
    - With no media, every media item is Skipped and no voice request is made.
    - Voice set: one call before the first render; replayed on Continue with no second record; refused → FromFallback with fallback voices; auth → Fatal with no thread; not saved → InternalError with no thread call; no voicemail planned → no call.
    - `Invite_OwnDeltaInWave1_LedgerMatchInWave2_SameSchedule`: same key and Start/End, the same DTSTART/DTEND in both .ics parts, and the same slot again on a Retry Failed where only the wave-2 thread is pending. A persisted, hand-moved slot is reused.
    - `Chain_FreshRun_V2InLaterWave_GetsV1Text`.
    - `Chain_InterleavedAcrossThreadsInOneWave_RunFinishes`: a delayed async provider, the run finishes within 10 s, and prompts are identical across two fresh runs.
    - `Chain_PredecessorStoppedBeforeCommit_SuccessorDoesNotHang`.
    - `NotRunSpend_FatalAfterPaidAttachmentCall_InRunEnded`: exactly 800 / 300 and the Story cost, with ticker = journal spend.
  - **Task 2:**
    - Every filler call comes after every relevant render (D-25).
    - One side-world call before the filler, journaled, followed by the SideProjectsReady → NonRelevantStarted → NonRelevantFinished events, with the NonRelevant row Done = filler count.
    - Daily-business prompts name the journaled projects.
    - Every non-relevant copy is on disk, every custodian gets one, and external senders are inbox-only (OUT-01).
    - A second run on the finished folder makes 0 requests.
    - Cancel in Stage B, then Continue: the side world is replayed and only the remaining filler threads are written.
    - Filler auth failure → Fatal, with no further filler calls.
    - Side-world fatal stops before any filler, and so does an unsaved side world (InternalError, with the spend in RunEnded).
    - Filler writes no ledger deltas and its prompts never carry the ledger. The final ledger is identical to a relevant-only run (D-13).
    - Key rows for non-relevant messages and attachments are all Generated, and none has a "Document N" style name.
- **Filtered runs.** `FullyQualifiedName~GenerationRunner`: 44 passed. RED was checked for both tasks: Task 1 tests did not compile against the old runner, and 11 of the Task 2 tests failed against the Task 1 runner.
- **Full suite (wave-6 post-wave gate).** `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`: 3331 passed, 0 failed (baseline 3300 + 31), in 7 m 43 s. The app build has 0 warnings; the test build has only the 2 known xUnit1031 warnings in GenerateCorpusStepTests.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Phase 5 runner fixture narrowed to relevant-only, no attachments**
- **Found during:** Task 1
- **Issue:** ApprovedCase contains non-relevant threads and planned attachments. With this plan the runner writes those too, which changes the request counts and key statuses that the 13 Phase 5 tests assert.
- **Fix:** The test constructor removes non-relevant threads and clears attachments. This matches the plan's "existing cases (relevant-only plans, no media)". The mixed corpus is tested in a new class in the same file.
- **Commit:** 7e1842f

**2. [Rule 3 - Blocking] Test seams for append failures**
- **Issue:** The plan calls for a "test journal hook" for the CastVoices and SideWorld append failures and the unjournaled ThreadDone. The runner creates its own units, so tests could not reach `ThreadUnitOfWork.BeforeAppendHook`.
- **Fix:** Added the internal `BeforeRecordAppendHook` (runner-owned records) and `BeforeThreadAppendHook` (passed to the unit). Both are test-only.
- **Commit:** 752e7c5

**3. [Rule 2 - Correctness] Unjournaled spend accounted in RunThreadAsync**
- **Issue:** The plan puts the Unjournaled/NotRun accounting in the post-`Task.WhenAll` loop. A cancel during the step makes `WhenAll` throw, so that loop would lose spend from threads that had already finished.
- **Fix:** One `AddUnjournaled(ctx, result)` helper is called per thread from RunThreadAsync, for waves and filler chunks alike. It keeps the exact `AddUnjournaledSpend(result.NotRunInputTokens, result.NotRunOutputTokens, result.NotRunCostUsd)` call and the Unjournaled-first rule, so nothing is counted twice.
- **Commit:** 752e7c5

**4. [Rule 2] RunCancelled carries attachment progress**
- `FinishSnapshot` now sets AttachmentsDone and AttachmentsTotal, so the UI-SPEC "… and d of t attachments are saved" clause has data. This is additive.

**5. [Rule 1 - Bug caused by this plan] GenerateCorpusStepTests assumed a relevant-only run**
- **Found during:** the full-suite gate
- **Issue:** Two step tests failed once the runner wrote filler:
  - `GenerateEmails_RunsToCompletion_PersistsOnUiThreadAndSaves` compared all-thread state counts to the relevant-only `ThreadsWritten`, and built its expected outcome text from relevant counts.
  - `Cancel_KeepsFinishedThreads_BannerOffersContinue_ContinueFinishes` expected Continue to journal only the relevant threads.
  - In both, the harness's thread-only responder made the attachments fail.
- **Fix:** The harness now uses `AttachmentResponder.Create`. Relevant counts are compared to ThreadsWritten and non-relevant counts to NonRelevantWritten. Continue is expected to journal every generation thread. The expected outcome text is built from `state.ToCounts`, and the unused `ToCounts(Harness, GenerationState)` helper was removed. No product code changed.
- **Files modified:** ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
- **Commit:** 184a69f

## Notes for later plans

- **06-23 stopgap:** `UserControls/StepGenerateCorpus.Run.cs` `ShowRunResult` still decides partial/finished from relevant counts only (the 06-14 "Interim until 06-19 writes filler" block). The runner now writes filler, but that file is not in this plan's files_modified, so the stopgap was left for 06-23 to remove.
- **06-23 wiring:** the step builds `new GenerationRunInput { Draft, Plan, CaseFolder }` without `Media` or `MediaPrices`. In the app, every image and voicemail is Skipped ("no tested OpenAI key this session") until the step passes the tested media generator and the shared prices.
- **06-20:** the repair row index (`waves.Count + 2`) is already used for seeds, but no row is created yet. Seeds that point to it are ignored by the snapshot until 06-20 adds the Attachments row.

## Known Stubs

None.

## Threat Flags

None. No new endpoints or file locations. CastVoices and SideWorld records go through the existing GenerationJournal, and the two hooks are internal test seams. The plan's mitigations are in place:
- T-06-58: chunks of 50 with a checkpoint each.
- T-06-59: only text-provider fatal ends the run.
- T-06-60: voices and side world are durable before use, and the final checkpoint runs in finally.
- T-06-61: filler uses `UsesLedger = false`, and only committed outcomes merge. A test compares the ledger with and without filler.

## TDD Gate Compliance

- Task 1: RED 7e1842f (`test(06-19)`) did not compile against the previous runner. GREEN 752e7c5 (`feat(06-19)`).
- Task 2: RED d2855e7 (`test(06-19)`) had 11 failing tests against the Task 1 runner. GREEN 209ab16 (`feat(06-19)`).
- No refactor commit was needed.

## Self-Check: PASSED

- Files exist: Services/Evidence/GenerationRunner.cs, ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs, ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs, this SUMMARY.
- Commits 7e1842f, 752e7c5, d2855e7, 209ab16 and 184a69f are in the log.
- Every acceptance grep of both tasks passed (the one-argument AddUnjournaledSpend check = 0).
