---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 12
subsystem: evidence-generation
tags: [unit-of-work, retry-with-feedback, journal, progress-snapshot, thread-safety]
requires:
  - phase: 05-02
    provides: answer-key status vocabulary used by journal projections
  - phase: 05-07
    provides: EmailComposer.Compose / SubjectFor, PeopleDirectory
  - phase: 05-08
    provides: ThreadRenderContext, ThreadRenderer.BuildRequest, RenderRepair.Apply
  - phase: 05-09
    provides: RenderValidator.Check (V1-V8)
  - phase: 05-10
    provides: MailboxWriter.WriteAsync, GenerationJournal.AppendAsync
provides:
  - GenerationSnapshot (Reset, SetStage, SetWaveRunning, RecordThread, SetLedgerEntries, SetRemainingCost, SetFinished, AddEvent, Drain, Peek; MaxQueuedEvents = 500)
  - ThreadOutcomeKind, ThreadOutcome
  - ThreadUnitOfWork (MaxAttempts = 3, internal RunAsync(ThreadRenderContext, PlanCallRun))
affects: [05 run controller (wave fan-out), Generate Corpus step timer]
tech-stack:
  added: []
  patterns:
    - Lock-protected progress snapshot drained by the UI timer; Drain/Peek return cloned views
    - Per-thread unit of work that never throws except on user cancel; every failure becomes a ThreadFailed journal record carrying its spend
key-files:
  created:
    - Services/Evidence/GenerationSnapshot.cs
    - Services/Evidence/ThreadUnitOfWork.cs
    - ReelDiscovery.Tests/Evidence/GenerationSnapshotTests.cs
    - ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs
  modified: []
decisions:
  - "ThreadUnitOfWork returns NotRun as soon as run.IsFatal is set after a call, even if an earlier attempt succeeded: the thread stays pending and Continue rewrites it (D-06)"
  - "Attempts in the journal = number of model calls made for the thread, including a trailing failed call when an earlier attempt is kept"
  - "IO/UnauthorizedAccess during an EML write fails as Write with the DocId; any other exception (MimeKit ParseException, composer bug) fails as Provider 'Could not build thread'"
  - "A failed ThreadFailed journal append is swallowed: the thread stays pending for Continue and the run goes on"
metrics:
  duration: ~20 min
  completed: 2026-10-03
  tasks: 2
  files: 4
---

# Phase 5 Plan 12: Thread Unit of Work and Generation Snapshot Summary

Per-thread render -> repair -> validate -> retry-with-feedback (max 3, fewest violations wins, tie to latest) -> compose -> write every custodian copy -> journal, plus a lock-protected, bounded (500-event) GenerationSnapshot that workers write and the UI timer drains.

## What was built

**Task 1: GenerationSnapshot** (`Services/Evidence/GenerationSnapshot.cs`)
- All 10 members operate under `lock (_lock)`; counts, wave rows and stage fields are private; no WinForms references.
- `RecordThread`: Written/Warnings add to DoneThreads, EmailsWritten, FilesWritten and wave Done (Warnings also WarningThreads and wave Warnings); Failed adds FailedThreads and wave Failed only; cost always accumulates.
- `AddEvent` drops the oldest above 500; `Drain` clones counts/waves and dequeues events, `Peek` does the same without dequeuing.

**Task 2: ThreadUnitOfWork** (`Services/Evidence/ThreadUnitOfWork.cs`)
- Attempts 1..3 through `PlanCallRunner.RunAsync<ThreadRenderer.ThreadRenderDto>`; retries resend the same prompt with the violations appended (D-13) and add a Retry event.
- No value: fatal -> NotRun (no journal); existing best -> keep it; otherwise ThreadFailed Refused ("The model declined: ..."), Timeout or Provider, with no re-ask.
- Best attempt with V1/V5 -> ThreadFailed Provider "The model did not return a usable thread: ...".
- Writes and the ThreadDone append use `CancellationToken.None`; ThreadDone carries Files, Messages (DocId, MessageId, Subject, BodyPlain, SignOff), LedgerDelta, tokens and cost; the snapshot gets RecordThread and a ThreadWritten/KeptWithWarnings event (root subject, last message FilePath).
- Every failure path goes through `FailAsync`, which journals tokens + `CalculateTotalCost`, records Failed in the snapshot and adds a ThreadFailed event; it never throws.

## Verification

- `GenerationSnapshotTests`: 8 passed (including 8 x 1,000 parallel RecordThread == 8,000).
- `ThreadUnitOfWorkTests`: 10 passed (clean, retry, keep-best warnings, refusal, auth fatal, keep attempt 1 after transport failure, missing message x3, blocked target path, user cancel, invalid external address).
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 485 passed.
- All acceptance greps met; no new build warnings in the two new files.

## TDD Gate Compliance

- Task 1: `410da31` test (RED, compile failure) -> `d7e1348` feat (GREEN).
- Task 2: `9ca27dc` test (RED, compile failure) -> `f75ed13` feat (GREEN). No refactor commits needed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 410da31 | test(05-12): add failing tests for GenerationSnapshot |
| 1 GREEN | d7e1348 | feat(05-12): implement GenerationSnapshot progress feed |
| 2 RED | 9ca27dc | test(05-12): add failing tests for ThreadUnitOfWork |
| 2 GREEN | f75ed13 | feat(05-12): implement ThreadUnitOfWork render-check-retry-write loop |

## Deviations from Plan

None that change behavior. Small additions within the spec:
- The plan step 6 outer IO/UnauthorizedAccess catch is kept; in addition, the per-email write is wrapped so the Write reason names the exact `{DocId}.eml` as the plan requires.
- Test names for unknown people use invented names (Harlan Voss, Mirela Tansey, Corbin Ashdale), not names from fictional works.

## Known Stubs

None.

## Requirements

The plan lists CORP-02, CORP-10, RUN-01, RUN-02, RUN-07, OUT-01, OUT-02. These services are building blocks; the end-to-end behavior arrives when the run controller and Generate Corpus step are wired (later Phase 5 plans), so none are marked complete here.

## Self-Check: PASSED
