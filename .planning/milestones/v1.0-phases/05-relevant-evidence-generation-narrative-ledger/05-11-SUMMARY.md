---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 11
subsystem: evidence-projections
tags: [journal-replay, answer-key, openxml, docx, case-json, lock-tolerant-io]
requires:
  - phase: 05-02
    provides: AnswerKeyBuilder overlay + status constants
  - phase: 05-03
    provides: RenderAssignments.Compute (privilege marking per thread)
  - phase: 05-04
    provides: LedgerMerger (ledger input = DeltasByThread)
  - phase: 05-07
    provides: EmailComposer.SubjectFor, PeopleDirectory
  - phase: 05-10
    provides: JournalReplay
provides:
  - GenerationRunState (From, IsDone, BuildOverlay, ToCounts, EstimateRemaining)
  - GenerationStateApplier (Apply, Clear)
  - StorySoFarDocxBuilder (FileNameSuffix, FileNameFor, IsStoryFileName, Build)
  - GenerationOutputs (WriteAnswerKeyAsync, WriteStorySoFarAsync) + GenerationFileResult
affects: [05 run controller, Generate Corpus step, Case Summary, Start Over]
tech-stack:
  added: []
  patterns:
    - Run state as a pure projection of plan + journal replay (last record per thread wins)
    - Per-file independent writes; the first failure is reported and the rest still run
key-files:
  created:
    - Services/Evidence/GenerationRunState.cs
    - Services/Evidence/StorySoFarDocxBuilder.cs
    - Services/Evidence/GenerationOutputs.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunStateTests.cs
    - ReelDiscovery.Tests/Evidence/StorySoFarDocxTests.cs
  modified: []
decisions:
  - "Run state covers relevant threads only; journal records for non-relevant threads (Phase 6) are ignored here"
  - "EmailsWritten counts the journal record's Messages; EmailsFailed counts the planned messages of Failed threads"
  - "EstimateRemaining prices every thread that is not done (Pending + Failed), because Continue retries failed threads"
  - "GenerationStateApplier re-stamps files through CorpusApproval.RecordPartialWrites (same Kind replaced) and leaves LastOutcome/KeyError/StoryError alone on non-final checkpoints"
  - "Story-so-far intro counts generated threads as Written + Warnings and dates the range from the earliest and latest ledger entries"
metrics:
  duration: 15min
  completed: 2026-10-03
---

# Phase 5 Plan 11: Run State, Answer-Key Overlay and Story So Far Summary

The answer key, the ledger in case.json and the "story so far" document are now pure projections of plan + journal, so they can be regenerated after a finished, cancelled or crashed run. GenerationRunState replays the journal into per-thread Written / Warnings / Failed / Pending status, counts and cost. The answer-key overlay takes its subjects from `EmailComposer.SubjectFor`, so key and EML subjects always match. StorySoFarDocxBuilder exports the ledger as a month-grouped Word document that passes the OpenXml validator. GenerationOutputs rewrites the key and story files one at a time, so a locked file does not stop the other writes.

## What Was Built

- **GenerationRunState** (`Services/Evidence/GenerationRunState.cs`)
  - `From(plan, replay)` gives per-thread status for every relevant thread, Records, DeltasByThread (ThreadDone only), counts, `FilesWritten` (distinct files), `Mailboxes` (distinct custodian folder segments), `CostUsd` (ThreadDone + ThreadFailed), HasRun, PlanChanged and UnreadableReason.
  - `BuildOverlay(plan, assignments)` maps each relevant email DocID to Generated / Generated - with warnings / Failed / Planned, with the subject from `EmailComposer.SubjectFor` using the thread's privilege marking. Attachment rows stay Planned and non-relevant rows are not changed.
  - `ToCounts(...)` fills GenerationCounts. `EstimateRemaining(plan, state, model)` returns remaining messages × per-message cost × QaOverhead, or 0 when there is no model.
- **GenerationStateApplier** (same file)
  - `Apply(...)` creates Corpus.Generation if needed and stores RunPlanId, Ledger, the cached counts, CostUsd and ProjectedUtc. It re-stamps files of the same Kind. `final: true` also records the outcome, message and end time, and advances the stage to Generation (it never moves the stage back).
  - `Clear(draft)` (Start Over) drops Corpus.Generation and the StorySoFar file record.
- **StorySoFarDocxBuilder** (`Services/Evidence/StorySoFarDocxBuilder.cs`)
  - The document has a Heading1 title and an intro line, then Heading2 month groups using en-US month names. Each entry has a bold `MMM d, yyyy · Kind` lead, the statement and an optional ` (value)`. Below it is a smaller detail line `Known to … · Facts … · Issues … · Source …` with empty clauses left out.
  - A "Threads kept with warnings" section is added only when at least one thread ended with warnings. An empty ledger produces "No story details yet."
  - `IsStoryFileName` uses the same guard as the review protocol file name check.
- **GenerationOutputs** (`Services/Evidence/GenerationOutputs.cs`)
  - `WriteAnswerKeyAsync` writes the CSV and the XLSX in separate try blocks. `WriteStorySoFarAsync` names the file from the case folder name.
  - IOException and UnauthorizedAccessException are recorded as the first failure (with FileOpenElsewhere from FileLockDetector), and the remaining writes continue. OperationCanceledException is rethrown.

## Verification

- `dotnet test --filter "FullyQualifiedName~GenerationRunStateTests|FullyQualifiedName~AnswerKeyOverlayTests"`: passed.
- `dotnet test --filter "FullyQualifiedName~StorySoFarDocxTests"`: 14 passed. This includes the OpenXmlValidator check, the de-DE culture check and invalid-XML stripping.
- `dotnet test --filter "Category!=Render"`: 2490 passed, 0 failed.
- Every acceptance grep for the three tasks matched.

## TDD Gate Compliance

- Task 1: RED `31171ae`, then GREEN `dd40abe`.
- Task 2: RED `d9390d6`, then GREEN `d162b72`.
- Task 3: RED `50ba9c6`, then GREEN `91d8c22`.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 31171ae | test(05-11): add failing tests for GenerationRunState and draft applier |
| 1 GREEN | dd40abe | feat(05-11): implement GenerationRunState projection and draft applier |
| 2 RED | d9390d6 | test(05-11): add failing tests for StorySoFarDocxBuilder |
| 2 GREEN | d162b72 | feat(05-11): implement StorySoFarDocxBuilder month-grouped ledger document |
| 3 RED | 50ba9c6 | test(05-11): add failing tests for GenerationOutputs key and story writes |
| 3 GREEN | 91d8c22 | feat(05-11): implement GenerationOutputs answer-key and story file writes |

## Deviations from Plan

None. The plan was executed as written. Some choices the plan left open are listed under decisions in the frontmatter: relevant-only scope, how EmailsWritten is computed, and remaining cost including Failed threads.

## Known Stubs

None.

## Self-Check: PASSED
