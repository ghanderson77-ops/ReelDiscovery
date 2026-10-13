---
phase: 06-attachments-non-relevant-corpus
plan: 20
subsystem: attachment-repair
tags: [attachments, repair, retry-failed, continue, chains, late-repair, phase5-upgrade, d-26, run-02, att-03, out-01]
requires:
  - "06-10 AttachmentHarvester.HarvestMessage, MailboxWriter.CopyPathsFor / WriteAsync (deterministic multipart)"
  - "06-09 EmailComposer.Compose(..., voicemailNotice)"
  - "06-14 GenerationRunState.RepairGroups / AttachmentSeeds / AttachmentRecords / Records"
  - "06-17 AttachmentUnitOfWork.RunAsync, AttachmentOutcome.For / ToRecord"
  - "06-18 ThreadUnitOfWork.CreateAttachmentContext, ThreadAttachmentContext, ChainScheduler.WaitForPredecessorsAsync / PriorTextAsync / Complete"
  - "06-19 wave loop, Stage B, RunContext.AddUnjournaledSpend(int, int, decimal)"
provides:
  - "AttachmentRepair(journal, mailbox, attachments, snapshot, caseFolder, custodianFolders).RepairThreadAsync(ctx, profile, threadDone, attachmentRecords, group, actx, run)"
  - "AttachmentRepairOutcome { Added, Failed, Skipped, FilesRewritten, Records, Deltas, InputTokens, OutputTokens, CostUsd, MediaCostUsd, Unjournaled, Reason, NotRunInputTokens, NotRunOutputTokens, NotRunCostUsd }"
  - "AttachmentRepair.KeptExistingPrefix; internal AttachmentRepair.BeforeAppendHook (test seam)"
  - "GenerationRunner.MaxConcurrentRepairs = 4; internal test seams RepairOrderHook, RepairSlotsOverride, RepairSlotHook, BeforeRepairAppendHook"
  - "Attachments for written threads stage row (Index waves.Count + 2) when the run starts with repairs pending"
affects: [06-21 answer key, 06-23 step wiring (Retry Failed / Continue labels, Media input)]
tech-stack:
  added: []
  patterns:
    - "Repair = pure recomposition from the journal text plus harvested or newly generated parts; never a text render"
    - "One AttachmentsRepaired line per repaired thread; snapshot, events, chain text and invite slots only after the append"
    - "No-loss rule: a message whose committed part could not be harvested or recreated is not rewritten"
    - "Predecessors awaited before a bounded repair slot (DAG by thread rank)"
key-files:
  created:
    - Services/Evidence/AttachmentRepair.cs
    - ReelDiscovery.Tests/Evidence/AttachmentRepairTests.cs
  modified:
    - Services/Evidence/GenerationRunner.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
decisions:
  - "Late repair = a later relevant wave holds a thread finished before this run (text and attachments); a written thread that still has its own repair pending does not make earlier repairs late, because its repair runs after them in story order. Resolves the conflict between Continue_RepairsInWaveOrder_SeeEarlierRepairs and the strict 'any later thread done' rule"
  - "A repair rewrites the copies only when at least one part was added; Failed/Skipped-only outcomes are journaled with their spend and no file is touched"
  - "The AttachmentsRepaired line lists only fully rewritten emails; a copy rewritten before a later copy's write failed is already listed by its ThreadDone line"
  - "No-loss rule marks every new outcome of the excluded message Failed 'kept the existing email: {file} could not be reused or created again', including the failed regeneration itself, as the plan specifies; the next repair converges"
  - "Excluded messages are composed with identity-only placeholder parts so later replies keep the same '[image: ...]' quotes as the copies on disk"
  - "AttachmentRegenerated is raised only after the commit and only for a regenerated part that was committed"
  - "AttachmentsAdded is raised only when at least one part was added"
  - "WaveStarted / WaveFinished are raised only for steps with new threads; a repair-only step sets the Attachments stage instead"
metrics:
  duration: 35min
  completed: 2026-10-05
---

# Phase 6 Plan 20: Attachment Repair for Written Threads Summary

Continue and Retry Failed now complete the attachments of threads that are already written. The repair rebuilds each thread from its journaled text and reuses every part it can read back from any custodian copy. It generates only what is missing, rewrites every copy and commits one AttachmentsRepaired line. Repairs run in story order inside the wave loop, so details from an earlier-wave repair reach later waves. Late repairs never extend the story. Finished email text never changes, including Phase 5 voicemail threads.

## What was built

**`Services/Evidence/AttachmentRepair.cs`**

`RepairThreadAsync` works in six steps:

1. **Accepted thread from the journal.** Each planned message is matched by DocId to the ThreadDone text (BodyPlain, SignOff, AttachmentTitles). If a message has no saved text, the method returns an Unjournaled outcome and makes no call.
2. **Per-message harvest.** `HarvestMessage` reads each message's committed DocIds from its copy paths. A harvested part is rebuilt from its record and keeps its own Content-Id. A committed part that cannot be harvested goes on the regenerate list.
3. **Generation.** Group DocIds and regenerated DocIds run in seq order, then plan order, through `ThreadUnitOfWork.CreateAttachmentContext` and `AttachmentUnitOfWork.RunAsync`. Prior text comes from:
   - a same-thread predecessor created earlier in this repair (its new text)
   - a reused same-thread predecessor (its committed record text)
   - otherwise `Chains.PriorTextAsync`

   Nothing new starts after a fatal stop or Cancel. An in-flight cancel becomes NotRun.
4. **No-loss rule.** If a message lost a committed part (not harvested and not recreated), that message is not rewritten. Its new outcomes become Failed "kept the existing email: …".
5. **Recompose and write.** `EmailComposer.Compose(..., voicemailNotice: !legacy)` runs, where legacy means every journaled From is empty. Every non-excluded email is written with `CancellationToken.None`. An IO failure stops the writes and turns every new Written/Warnings outcome into Failed "Could not rewrite {docId}.eml: …".
6. **Commit.** One AttachmentsRepaired line is appended with the nested records, `Files` = the rewritten copies, line `CostUsd` 0 and the `LateRepair` flag.
   - If the append fails, the outcome is Unjournaled: all spend goes to `AddCost` and nothing is reported.
   - After a successful append, these follow: `RecordAttachment` on `actx.RowIndex`, the Failed / Skipped / KeptWithWarnings events, `AttachmentRegenerated`, `AttachmentsAdded`, the invite slots, Deltas (only when not late), the NotRun fields and `AddCost` for NotRun spend.
   - `finally` always calls `Chains.Complete` with the committed texts.
   - A cancel is rethrown only after the commit.

**`Services/Evidence/GenerationRunner.cs`**

- **Run start.**
  - `state.RepairGroups(plan, mediaAvailable: input.Media != null)` is split into relevant repairs by wave and filler repairs.
  - Repair media items are added to the media gate's remaining count.
  - One `AttachmentRepair` is built on the run's journal, mailbox and attachment unit.
  - `SemaphoreSlim(RepairSlotsOverride ?? MaxConcurrentRepairs)` bounds the repairs.
  - The Attachments stage row (`waves.Count + 2`) is added only when repairs are pending. Its counters come from the seeds that already point there.
- **Late mode.**
  - `LastFinishedWave` is the highest wave holding a relevant thread that was done at run start and has no repair pending. A repair is late when that wave is later than the repair's own wave.
  - `LaterEstablished` is the start-ledger entries from later waves, plus entries of other same-wave threads that were written earlier.
- **Wave loop.**
  - `if (pending.Count == 0 && !relevantRepairs[wave.Index].Any()) continue;`
  - `BeginStep(pending ∪ repairs)`. The stage is Writing when new threads exist, otherwise Attachments.
  - `AttachmentRepairStarted` is raised once per run.
  - Repairs use the same earlier-waves ledger view, run in `RepairOrderHook` order, and go through `Task.WhenAll` together with the new threads.
  - Each repair awaits `WaitForPredecessorsAsync` before it takes a repair slot. A repair that never starts calls `Complete(empty)`.
  - Committed repair deltas merge into `deltas[threadId]` at wave end.
  - Spend: the Unjournaled path adds the full totals; otherwise `AddUnjournaledSpend(outcome.NotRunInputTokens, outcome.NotRunOutputTokens, outcome.NotRunCostUsd)`.
- **Stage B.**
  - The side world is built or replayed when filler or filler repairs are pending.
  - The NonRelevant events are raised only when there is filler to write.
  - Filler repairs run after the filler chunks, in chunks of 50, on the same slots, with no ledger and on the repair row. Each chunk is followed by a checkpoint.
  - A fatal stop ends the stage.

## Tests

- **AttachmentRepairTests (18, new).** They cover:
  - one Excel call with the Word reused byte for byte, every copy, one line and the AttachmentsAdded event
  - `Repair_MultiMessage_ReusesPartsOnEveryMessage` and `Repair_IncompleteFirstCopy_UsesOtherCopy`
  - unchanged emails stay byte-identical and the text parts never change
  - a Word missing from every copy is regenerated, with an event
  - `Repair_RegenerationFails_KeepsExistingCopies` (locked copies) and `Repair_SecondCopyWriteFails_ConvergesNextTime`
  - a skipped inline image: the cid is added and the reply quotes "[image: Site photo]"
  - a Phase-5-style thread: all three parts with AttachmentNaming names
  - `Repair_Phase5VoicemailThread_KeepsTextAddsAudio`, and the Phase 6 voicemail notice kept as written
  - inline Content-Id: the harvested one is kept, a regenerated one is computed
  - late repair: the record is flagged, no deltas are returned and the V6 warning is kept
  - a same-thread chain: v2 gets the new v1 text with no self-wait
  - NotRun spend after a fatal stop; Unjournaled with the NotRun fields at 0
  - cancel during the second attachment: the first is committed, then the cancel is thrown
- **GenerationRunnerMixedTests (+15).** They cover:
  - Retry Failed makes 1 attachment call and the key shows Generated
  - `Continue_EarlierWaveRepair_FeedsLaterWave`, `Continue_RepairsInWaveOrder_SeeEarlierRepairs` and `RetryFailed_LateRepair_KeepsFinishedText`
  - media: still unavailable → no calls; available later → generated and rewritten
  - Phase 5 upgrade: attachments in wave order plus the side world and filler; voicemail with audio, unchanged text, and the key's From/Subject equal to the EML's
  - the repair row, the Attachments stage and the started event
  - `Repairs_AtMostFourAtOnce` (10 groups)
  - `Repair_FiveChainLinksSameWave_NoSlotDeadlock` (reversed order, 4 slots)
  - `Repair_InterleavedChainsTwoThreads_NoDeadlock` and `Repair_CrossingChainsTwoThreads_NoDeadlock` (4 or 1 slots × natural or reversed order, 10 s guards, identical prompts in all four runs)
  - a fatal stop after a paid repair call: its spend is in RunEnded exactly once
  - non-relevant repairs in Stage B; a fatal stop skips the rest; a cancel keeps the committed repair
- **Filtered runs.** `AttachmentRepairTests`: 18/18. `GenerationRunnerTests|GenerationRunnerMixedTests|AttachmentRepairTests`: 77/77. The deadlock and bounding tests were run 3 more times and passed every time.
- **Full suite (wave-7 post-wave gate):** `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`: 3364 passed, 0 failed (baseline 3331 + 33), in 8 m 15 s. The app build has 0 warnings; the test build has only the 2 known xUnit1031 warnings in GenerateCorpusStepTests.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Conflict in plan behaviors] Definition of "late"**
- **Found during:** Task 2
- **Issue:** The action defines laterDone as "any relevant thread in a later wave done at run start". Under that rule the wave-1 repair in `Continue_RepairsInWaveOrder_SeeEarlierRepairs` is late, because the wave-2 thread is written. Its deltas would then never enter the ledger, so the wave-2 repair could not see them, which is exactly what that test requires.
- **Fix:** A written thread that still has a repair pending in this run does not count toward late. It is completed after the earlier repair, in story order. `RetryFailed_LateRepair_KeepsFinishedText` (waves 2+ fully finished) still gives LateRepair = true.
- **Files:** Services/Evidence/GenerationRunner.cs
- **Commit:** 70fde8e

**2. [Rule 3] Test seams**
- Added `AttachmentRepair.BeforeAppendHook`, plus the runner's `RepairSlotHook` (concurrency and stage capture) and `BeforeRepairAppendHook`, alongside the plan's `RepairOrderHook` and `RepairSlotsOverride`. All are internal and used only by tests.

**3. [Rule 2] Line Files after a partial rewrite**
- **Issue:** `MailboxWriter.WriteAsync` throws on the failing copy without returning the copies it already wrote.
- **Fix:** The AttachmentsRepaired line lists only fully rewritten emails. The partly rewritten copy is already listed in the thread's ThreadDone Files, so the Start Over sweep still finds it.

**4. [Rule 1 - Test realism] Late repairs in runner tests**
- **Issue:** The default responder always returns a new detail. That makes every late repair retry three times (V6).
- **Fix:** The Retry Failed and chain-matrix tests answer their repaired DocIds with a document that has no new details (`NoNewDetails`), the way a model that follows the late-repair prompt would. They still assert exactly 1 call and one prompt per DocId.

## Known edges

- A committed part that is regenerated, because it could not be harvested, is moved by `RecordAttachment` from its wave row to the repair row. Only the row placement is off; the totals stay correct. This happens only when copies were edited or deleted by hand.
- A cancel during a repair loses the NotRun spend of the in-flight call for RunEnded, the same as in ThreadUnitOfWork. A cancelled call normally has no token usage.

## Known Stubs

None.

## Threat Flags

None. Repairs write only `CopyPathsFor` paths through `MailboxWriter.ResolveUnderCase` (T-06-63) and read only expected DocIds through the bounded harvester (T-06-62). The plan's other mitigations are in place:
- T-06-64: only missing, failed or skipped-with-media items are generated, with at most 4 repairs at once.
- T-06-64c: predecessors are awaited before the slot; the five-link, interleaved and crossing tests pass in both launch orders, with 4 slots and 1 slot.
- T-06-64b: late mode forbids new details and keeps late deltas out of the ledger.

## TDD Gate Compliance

- Task 1: RED 00a2a9c (`test(06-20)`, which does not compile without AttachmentRepair). GREEN 327ed8c (`feat(06-20)`).
- Task 2: RED d29cf90 (`test(06-20)`, which does not compile against the 06-19 runner: no MaxConcurrentRepairs or repair seams). GREEN 70fde8e (`feat(06-20)`).
- No refactor commit was needed.

## Self-Check: PASSED

- Files exist: Services/Evidence/AttachmentRepair.cs, ReelDiscovery.Tests/Evidence/AttachmentRepairTests.cs, Services/Evidence/GenerationRunner.cs, ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs, this SUMMARY.
- Commits 00a2a9c, 327ed8c, d29cf90 and 70fde8e are in the log.
- Every acceptance grep of both tasks passed, including AddUnjournaledSpend(outcome.Unjournaled ? ...) = 0 and the never-re-render check = 0.
