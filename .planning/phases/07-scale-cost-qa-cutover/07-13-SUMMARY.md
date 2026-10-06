---
phase: 07-scale-cost-qa-cutover
plan: 13
subsystem: evidence-generation
tags: [regenerate-thread, journal, replay, ledger, answer-key, QA-01, QA-03, D-02, D-05, D-13, D-15, OQ3]
requires:
  - phase: 07-05
    provides: "JournalRecord.CostDetail and itemized spend replay (kept on invalidation)"
  - phase: 07-08
    provides: "BatchStage-aware runner wave loop"
  - phase: 07-10
    provides: "GenerationRunner wave loop and Start Over file rules (GenerationReset.TryDelete)"
provides:
  - "JournalRecordType.ThreadInvalidated (appended last; older builds skip the line)"
  - "JournalReplay.InvalidatedThreadIds (queued state) and JournalReplay.InvalidatedDeltas"
  - "GenerationRunState.InvalidatedDeltas; DeltasByThread keeps an invalidated relevant thread's deltas"
  - "ThreadInvalidation.InvalidateAsync(caseFolder, plan, guide, threadId, ct) -> ThreadInvalidationResult { Appended, FilesDeleted, Errors, KeyError }"
  - "ThreadRenderContext.PriorDeltas; EvidencePrompts.PriorDetailsHeader section in the user prompt"
  - "ThreadDone of a regenerated relevant thread: LedgerDelta = prior deltas, LateRepair = true"
affects: [07-15, 07-16, 07-17]
tech-stack:
  added: []
  patterns: ["invalidation as an append-only journal line; key re-projected right after it"]
key-files:
  created:
    - Services/Evidence/ThreadInvalidation.cs
    - ReelDiscovery.Tests/Evidence/ThreadInvalidationTests.cs
  modified:
    - Models/Evidence/JournalRecord.cs
    - Services/Evidence/GenerationJournal.cs
    - Services/Evidence/GenerationRunState.cs
    - Services/Evidence/ThreadRenderContext.cs
    - Services/Evidence/EvidencePrompts.cs
    - Services/Evidence/ThreadUnitOfWork.cs
    - Services/Evidence/GenerationRunner.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
decisions:
  - "[07-13]: The ThreadInvalidated line carries the thread's full DeltasByThread entry (its own deltas plus committed attachment deltas), so the ledger after invalidation and after the re-render equals the ledger before"
  - "[07-13]: A ThreadDone with LateRepair = true contributes only its LedgerDelta to DeltasByThread; deltas of attachments nested in it stay out of the ledger"
  - "[07-13]: A regenerated relevant thread's attachments see the prior thread deltas, not the discarded new ones; its attachment step is not otherwise switched to late-repair mode"
  - "[07-13]: InvalidatedDeltas are never removed by a later ThreadDone; InvalidatedThreadIds is cleared by a later ThreadDone or ThreadFailed"
  - "[07-13]: InvalidateAsync refuses (Appended false) when the journal is unreadable or the thread's last record is not ThreadDone; locked copies are reported as '{relative}: {reason}' and the line is still appended"
metrics:
  duration: "~35 min"
  completed: "2026-10-06"
  tasks: 2
  files: 10
---

# Phase 7 Plan 13: Regenerate Thread (Invalidation, Replay, Consistent Re-render) Summary

"Regenerate thread" is a durable `ThreadInvalidated` journal line. `ThreadInvalidation.InvalidateAsync` deletes only the validated EML copies, appends the line and re-projects the answer key so the thread shows Planned again. On Continue, a relevant thread is re-rendered with its earlier details pinned in the prompt, and its earlier ledger deltas are kept with `LateRepair = true`. Later waves are never contradicted. A non-relevant thread re-renders as normal filler.

## What was built

**Task 1: journal record, replay and service** (commits c471ff1 RED, 54c3c45 GREEN)
- `ThreadInvalidated` is the last value of `JournalRecordType`. Older builds skip the line through the existing unknown-enum rule.
- When `Replay` reads a ThreadInvalidated line, it removes the thread from `LastByThread` and removes the thread's attachments from `LastByAttachment`. It also adds the thread to `InvalidatedThreadIds` and sets `InvalidatedDeltas[thread]`. Spend is never subtracted (T-07-50). A later ThreadDone or ThreadFailed clears the queued state.
- `GenerationRunState.From` treats the thread as pending. `DeltasByThread` still holds the established deltas for later waves (OQ3/D-02).
- `ThreadInvalidation.InvalidateAsync` works in this order:
  1. Replays the journal and requires that the thread's last record is a ThreadDone.
  2. Deletes the planned copies through `MailboxWriter.DeleteCopies` and the journal-listed files through `ResolveUnderCase` (T-07-48). It retries locked files once more with `GenerationReset.TryDelete` so each failure line includes the reason.
  3. Appends the line with `CancellationToken.None`.
  4. Rewrites the CSV and XLSX answer keys through `GenerationOutputs.WriteAnswerKeyAsync(..., state.BuildFullOverlay(plan, guide))`. If the key write fails, the error goes to `KeyError` and nothing is thrown.

**Task 2: re-render keeps the established details** (commits c3aba2c RED, 8f5c9f7 GREEN)
- Added `ThreadRenderContext.PriorDeltas`. When it is non-empty, `EvidencePrompts.BuildThreadPrompt` adds the section "Details this thread already established (keep every one exactly; add no new details):" after the ledger section, in the user prompt only. Prompts without prior deltas are byte-identical to before, and EvidencePromptsTests is unchanged and green.
- The runner wave loop sets `render.PriorDeltas = prior` from `state.InvalidatedDeltas`.
- For a regenerated relevant thread, `ThreadUnitOfWork` writes `LedgerDelta = ctx.PriorDeltas` with `LateRepair = true` and returns no new attachment deltas. Its attachments are rendered against the prior thread deltas.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- Full non-render suite (Evidence, Corpus and Ai, excluding Render and Scale): 2265 passed, 0 failed.
- New tests:
  - ThreadInvalidationTests: 6 tests. They cover deletion, the journal line, replay, spend, deltas, the CSV/XLSX key, the locked copy, the locked key, unknown and already-invalidated threads, and an older build skipping the line.
  - Runner tests: a relevant re-render (single request, established section in the prompt, prior deltas kept with LateRepair, ledger unchanged, key Planned then Generated). A non-relevant re-render (one filler request, no established section). A check that no ordinary prompt carries the section.
- All acceptance grep checks pass.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] The invalidation line also keeps the thread's attachment deltas**
- **Found during:** Task 1
- **Issue:** The plan writes only the previous ThreadDone's `LedgerDelta`. That would drop the committed attachment deltas from the ledger while the thread is invalidated, so the ledger would no longer equal the pre-invalidation ledger.
- **Fix:** The line carries `GenerationRunState.DeltasByThread[thread]`, which holds the thread's own deltas plus its committed attachment deltas. A ThreadDone with LateRepair adds only its LedgerDelta, without nested attachment deltas, so nothing is counted twice and the regenerated attachments add no new details.
- **Files:** Services/Evidence/ThreadInvalidation.cs, Services/Evidence/GenerationRunState.cs, Services/Evidence/ThreadUnitOfWork.cs
- **Commits:** 54c3c45, 8f5c9f7

**2. [Rule 2 - Correctness] Attachments of a regenerated thread use the prior deltas**
- **Found during:** Task 2
- **Issue:** Attachment contexts were built from the model's new (discarded) details.
- **Fix:** For a regenerated relevant thread, `threadDeltas` comes from `PriorDeltas` (thread-level only).
- **Commit:** 8f5c9f7

**3. Failure reasons for locked copies**
- `MailboxWriter.DeleteCopies` returns only the leftover paths. Locked copies are retried through `GenerationReset.TryDelete`, which produces "{relative}: {reason}" and applies the D-25 retry rule.

### TDD note
The implementation for each task was drafted before its tests. To keep the gate order, I pulled the implementation out of the working tree, confirmed that the test project failed to compile against the new API (RED), committed the tests, and then restored and committed the implementation (GREEN). The gate commits are in order: test c471ff1 -> feat 54c3c45, and test c3aba2c -> feat 8f5c9f7.

## Known Stubs

None.

## Self-Check: PASSED
- FOUND: Services/Evidence/ThreadInvalidation.cs
- FOUND: ReelDiscovery.Tests/Evidence/ThreadInvalidationTests.cs
- FOUND commits: c471ff1, 54c3c45, c3aba2c, 8f5c9f7
