---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 13
subsystem: evidence-generation
tags: [run-orchestration, waves, ledger, checkpoints, cancel-continue, start-over]
requires:
  - phase: 05-03
    provides: WavePlanner, RenderAssignments
  - phase: 05-04
    provides: LedgerMerger
  - phase: 05-10
    provides: GenerationJournal, MailboxWriter
  - phase: 05-11
    provides: GenerationRunState, GenerationOutputs, StorySoFarDocxBuilder
  - phase: 05-12
    provides: ThreadUnitOfWork, GenerationSnapshot
provides:
  - GenerationRunInput, GenerationCheckpoint, GenerationPersist, GenerationRunResult
  - GenerationRunner (MaxParallelThreads = 3, RetryHint, KeyProjectionInterval = 2 s, RunAsync, internal BeforeWaveHook)
  - StartOverResult, GenerationReset.StartOverAsync
affects: [Generate Corpus step (05-15), Case Summary]
tech-stack:
  added: []
  patterns:
    - One run method for Generate, Continue and Retry Failed: it writes every thread the journal does not list as done
    - try / catch(OCE when user) / catch(Exception) / finally, where the finally always runs the final checkpoint and appends RunEnded
    - Debounced per-thread key projection behind a SemaphoreSlim(1,1) gate; wave end and run end wait on the same gate
key-files:
  created:
    - Services/Evidence/GenerationRunner.cs
    - Services/Evidence/GenerationReset.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationResetTests.cs
  modified: []
decisions:
  - "BeforeWaveHook receives the 0-based wave position (the plan's 'wave index 1 = second wave'); GenerationWave.Index stays 1-based everywhere else"
  - "Run messages: Finished and Cancelled carry an empty message; Fatal carries the provider message; InternalError carries the exception message"
  - "A unit that ends NotRun (fatal stop) does not trigger a per-thread projection; the final checkpoint covers it"
  - "Start Over counts only deleted EML copies in FilesDeleted; the journal and story file are deleted but not counted"
  - "OUT-01 stays Pending: relevant emails now land in every custodian mailbox, but non-relevant mail is still to come"
metrics:
  duration: ~25 min
  completed: 2026-10-03
  tasks: 2
  files: 4
---

# Phase 5 Plan 13: Generation Runner and Start Over Summary

GenerationRunner runs the whole relevant-evidence generation. It processes chronological waves and runs up to 3 threads of a wave in parallel. A wave's ledger deltas are merged only after the wave completes. The answer key is re-projected from the journal as threads finish (at most every 2 s), after every wave and at run end. Cancel, a fatal provider error and an unexpected exception all end the same clean way: final checkpoint, RunEnded record, snapshot finished. GenerationReset.StartOverAsync deletes only validated generated EMLs, the journal and the story file.

## What was built

**Task 1: GenerationRunner** (`Services/Evidence/GenerationRunner.cs`)
- `RunAsync(input, snapshot, persist, ct)`:
  - It replays the journal first. An unreadable journal or a changed plan returns `Outcome None` with the reason and sends no requests.
  - It builds waves from the whole plan (`WavePlanner.Plan(input.Plan)`), plus assignments, people, folders, journal, mailbox writer and the unit of work.
  - It seeds the snapshot from the replayed state, adds a RunStarted or RunContinued event, and appends a RunStarted record.
- Wave loop:
  - Waves with no pending thread are skipped.
  - For each other wave: set the stage to Writing and mark the wave running, then add a WaveStarted event.
  - Build the ledger from the deltas so far and run one `ThreadRenderContext.Create(...)` per pending thread through `Task.WhenAll`.
  - After the wave, merge its deltas, set the Merging stage, add a WaveFinished event with counts, and write a non-final checkpoint.
  - A fatal provider error breaks out of the loop with `Fatal`.
- Per-thread projection: `RunThreadAsync` runs the unit, then `MaybeProjectAsync`.
  - It is debounced by `KeyProjectionInterval` (2 s, measured from the run start) and skips when another projection holds the gate.
  - It replays the journal, rewrites the key and calls persist with a non-final checkpoint. Any failure is swallowed, so the thread stays finished.
- Final checkpoint, which always runs in `finally`. Each step is guarded on its own:
  1. Replay the journal into state and ledger.
  2. Rewrite the answer key.
  3. Write the story-so-far .docx when the ledger has entries.
  4. Append RunEnded {Outcome, Message}.
  5. Call persist inside `catch (Exception ex) when (ex is not OperationCanceledException)`.
  6. Update the snapshot: ledger entries, remaining cost, a RunFinished, RunCancelled or RunStopped event, stage Idle, and `SetFinished`.
- Every await uses `.ConfigureAwait(false)` (15 occurrences). The draft is never mutated.

**Task 2: GenerationReset** (`Services/Evidence/GenerationReset.cs`)
- Candidates are `GenerationJournal.ListAllFiles` (any plan) plus, when both plan and guide are given, every relevant message's `MailboxWriter.CopyPathsFor`, de-duplicated with ordinal comparison.
- Each candidate goes through `MailboxWriter.ResolveUnderCase` (unsafe paths are dropped) and is deleted only if it exists.
- After that it deletes the journal and `{folder} - Story So Far.docx`.
- An IO or UnauthorizedAccess error becomes an Errors entry `"{relPath}: {message}"`, and the remaining files are still deleted.
- All the work runs inside `Task.Run`. There are no directory deletes.

## Verification

- `GenerationRunnerTests` (10 tests): full run with per-copy DocID headers and key statuses; ledger visibility across and within waves; cancel then continue (identical bytes for kept files); refusal then retry of only that thread; auth fatal; kill recovery (torn line plus orphan EMLs); plan changed; per-thread key projection during a wave; exception escaping a wave; off-UI execution. Passed in 3 repeated runs, about 7 s each.
- `GenerationResetTests` (6 tests): full-run reset, traversal paths, orphan planned copies, foreign-plan journal, locked file, empty folder.
- `dotnet test --filter "Category!=Render"`: 2524 passed, 0 failed.
- All acceptance greps met. There are no new build warnings in the new files; the xUnit1031 warning in the off-UI test was fixed by making it async.

## TDD Gate Compliance

- Task 1: RED `6edb66d` (compile failure confirmed with the runner moved aside), then GREEN `aeec0db`, then test cleanup `a011617`.
- Task 2: RED `f68c5b6` (compile failure), then GREEN `ff0c6d7`.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 6edb66d | test(05-13): add failing tests for GenerationRunner |
| 1 GREEN | aeec0db | feat(05-13): implement GenerationRunner wave orchestration |
| 1 test | a011617 | test(05-13): make off-UI runner test async |
| 2 RED | f68c5b6 | test(05-13): add failing tests for GenerationReset Start Over |
| 2 GREEN | ff0c6d7 | feat(05-13): implement GenerationReset Start Over |

## Deviations from Plan

- **Test fixture:** `EvidenceTestData.ApprovedCase()` puts every relevant thread in its own wave. The same-wave ledger test and the per-thread projection test therefore shift T-0002 and T-0011 by whole days (a test-local `MergeWaves`), which gives two 2-thread waves. Production code is unchanged.
- **Per-thread projection test:** to make the order deterministic, the first call is delayed 2.5 s and the second 1.5 s, instead of 2.5 s on every call. The first thread then projects past the debounce while the second is still running.

## Notes for later plans

- `GenerationJournal.AppendAsync` does not start a new line when the file ends in a torn line, so the first record appended after a kill merges with the torn fragment and is skipped on replay. Today that first record is always the runner's RunStarted, which is harmless, and the kill-recovery test covers it. A one-byte newline guard in the journal would make this explicit (out of scope here).
- The persist callback runs on a pool thread. Plan 05-15 must marshal it to the UI thread before calling `GenerationStateApplier.Apply` (T-05-42).

## Known Stubs

None.

## Self-Check: PASSED
