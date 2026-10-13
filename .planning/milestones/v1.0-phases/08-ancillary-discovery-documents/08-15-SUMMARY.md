---
phase: 08-ancillary-discovery-documents
plan: 15
subsystem: discovery-generation
tags: [discovery, orchestration, persistence, parallel, ANC-01, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-01
    provides: "DiscoveryDocumentSet / DiscoveryDocument, DiscoveryStore (Load, SaveAsync, FolderPath, DocxPath), DiscoveryStaleness.Stamp"
  - phase: 08-10
    provides: "DiscoveryWorkItem / DiscoveryWorklist (Build, ForDocument, DepositionKey)"
  - phase: 08-12
    provides: "DepositionWriter.WriteAsync -> DepositionWriteOutcome"
  - phase: 08-13
    provides: "WrittenSetWriter.WriteAsync -> WrittenSetWriteOutcome"
provides:
  - "DiscoveryGenerator.RunAsync(draft, corpus, items, guidance, progress, ct) -> DiscoveryRunResult (never throws)"
  - "DiscoveryGenerator.SaveAgainAsync(draft, key, ct) -> SaveAgainResult (no model call)"
  - "DiscoveryProgress (Index, Total, Key, Part, PartCount, Finished, Stopping) for the step's status line"
  - "Plain-word reasons ReasonNoContent / ReasonTimeout / ReasonRefused / ReasonWriteFailed(message)"
  - "MaxParallelCalls = 3, internal MaxParallel (tests 1), internal SchemaTypes"
affects: [08-16, 08-17, 08-18, 08-19]
tech-stack:
  added: []
  patterns:
    - "One task per work item under a single PlanCallRun; Task.WhenAll waits for all so finished documents are on disk before a cancel surfaces (BlindReviewer precedent)"
    - "Write order .docx then sidecar; every sidecar mutation runs under SemaphoreSlim(1,1) with CancellationToken.None"
    - "Generator tests route fake responses by schema name (deposition vs written) and chunk/slot parsed from the prompt; MaxParallel = 1 keeps the order deterministic"
key-files:
  created:
    - Services/Discovery/DiscoveryGenerator.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryGeneratorTests.cs
  modified:
    - ReelDiscovery.Tests/Evidence/GenerationResetTests.cs
key-decisions:
  - "08-15: A .docx write failure keeps the paid content in the sidecar with FileSaved = false; an open-in-Word lock leaves FailureReason empty (Not saved), any other write error sets ReasonWriteFailed(...); both count as NotSaved, not Failed"
  - "08-15: A NotRun outcome (fatal stop) writes nothing unless spend was billed; billed spend goes to the existing record or UnattributedCostUsd"
  - "08-15: A failed document with no prior content becomes a Failed record carrying the cost of every attempt (plus any earlier record cost); with prior content only FailureReason and cost change (U54)"
  - "08-15: A deposition success replaces that witness's ExhibitAssignments with the plan's Assignments (the planner reuses saved numbers on regenerate)"
  - "08-15: RunAsync takes DiscoveryCorpus? (nullable): DiscoveryStaleness.Stamp already treats null as no corpus plan id"
patterns-established:
  - "Synchronous IProgress in tests (SyncProgress) to cancel at an exact point; Progress<T> posts asynchronously"
requirements-completed: []
duration: 25min
completed: 2026-10-08
---

# Phase 8 Plan 15: DiscoveryGenerator, persistence and Start Over regression Summary

**Never-throws discovery run: bounded parallel per-document units under PlanCallRun, atomic .docx then single-writer sidecar upsert, fatal/cancel/lock/regenerate semantics, and Save Again from stored content.**

## Performance

- **Duration:** ~25 min
- **Tasks:** 2
- **Files:** 2 created, 1 modified

## Accomplishments

- `DiscoveryGenerator.RunAsync` loads the sidecar (aborting with the load reason on a newer-version/other-case/corrupt file, writing nothing), assigns the case number once, builds the shared system prompt once and fans out one unit per work item with `MaxParallelCalls = 3`.
- Each success builds the .docx (`TranscriptDocxBuilder` / `DiscoveryDocxBuilder`), writes it atomically to the validated `DiscoveryStore.DocxPath`, then upserts a record stamped by `DiscoveryStaleness.Stamp` with cost (old + new), warnings, planner notes, testimony pages / request count.
- Failure isolation: refused/timeout/no-content documents become Failed records (or keep their previous content with a FailureReason); the other documents still save. Fatal Auth/SpendLimit stops queued calls and keeps finished documents; user cancel keeps finished documents.
- `SaveAgainAsync` rewrites a Not-saved document from sidecar content without a provider call; still-locked files report `StillOpen`.
- Regression test: `GenerationReset.StartOverAsync` leaves `Discovery\*.docx` and `discovery-documents.json` intact (no production change needed).

## Task Commits

1. **Task 1 (RED): failing generator tests** - `8e0a3f9` (test)
2. **Task 1 (GREEN): DiscoveryGenerator** - `260bfa8` (feat)
3. **Task 2: Start Over regression** - `a1c1bb2` (test)

## Verification

- `DiscoveryGeneratorTests`: 11/11 pass (3 consecutive runs, deterministic)
- `GenerationResetTests`: 19/19 pass
- Wave gate `Category!=Render&Category!=Scale`: 4258/4258 pass
- App and test project `-warnaserror` builds: 0 warnings, 0 errors

## Deviations from Plan

None that change behavior. Notes:
- The Save Again test checks the rewritten file is a real .docx package (PK header) rather than byte equality, because OpenXml output is not byte-deterministic between builds.
- Task 2's regression test passed on first run, as the plan expected (no production change to GenerationReset).

## TDD Gate Compliance

- Task 1: `test(08-15)` commit `8e0a3f9` precedes `feat(08-15)` commit `260bfa8`.
- Task 2 is a regression test for existing behavior; it passed immediately, so it has no GREEN commit (plan stated no production change was expected).

## Known Stubs

None.

## Next Phase Readiness

- 08-17 can drive `RunAsync` from the step's run skeleton (EnterRunState/ExitRunState), map `DiscoveryProgress` to the status line and `DiscoveryRunResult` counts to the outcome line, and wire Save Again / Regenerate (guidance keyed by document key).

## Self-Check: PASSED

- FOUND: Services/Discovery/DiscoveryGenerator.cs
- FOUND: ReelDiscovery.Tests/Discovery/DiscoveryGeneratorTests.cs
- FOUND: 8e0a3f9, 260bfa8, a1c1bb2
