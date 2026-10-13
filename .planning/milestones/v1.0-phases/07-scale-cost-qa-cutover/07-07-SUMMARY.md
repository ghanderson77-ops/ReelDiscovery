---
phase: 07-scale-cost-qa-cutover
plan: 07
subsystem: ai-batch
tags: [batch, ledger, durability, request-identity, RUN-03, RUN-04, D-05, D-09]
requires:
  - "07-01 LlmProviderRequest/LlmProviderResponse (cache token fields, IsBatch), LlmProviderKind, LlmFailureKind"
provides:
  - "IBatchTransport + BatchItemRequest/BatchSubmitResult/BatchState/BatchStatusResult/BatchItemOutcome/BatchItemResult/BatchResultsResult"
  - "RequestIdentity.CustomIdFor(kind, request) = 'q' + 40 hex of SHA-256 over the canonical request"
  - "BatchLedger (provider-batches.jsonl): AppendAsync, Replay, SaveResults/ReadResults (batch-results/{SafeFileId}.jsonl), SafeFileId, ResultsPathFor, PendingBatchCount"
  - "BatchLedgerRecordType/BatchLedgerItem/BatchLedgerRecord/BatchLedgerState (Pending(planId), BatchIdByCustomId, ConsumedCustomIds, OrphansBilledIds)"
  - "ReelDiscovery.Tests/Fakes/FakeBatchTransport (scripted submit/poll/results/failures)"
affects: [07-08-batching-decorator, 07-09-transports, 07-10-runner]
tech-stack:
  added: []
  patterns:
    - "Same append + newline-repair + Flush(flushToDisk: true) technique as GenerationJournal, in a separate file (D-05)"
    - "Provider ids never become raw file names: allowlist regex else SHA-256 hex"
key-files:
  created:
    - Services/Ai/RequestIdentity.cs
    - Services/Ai/IBatchTransport.cs
    - Services/Ai/BatchLedger.cs
    - ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs
    - ReelDiscovery.Tests/Ai/RequestIdentityTests.cs
    - ReelDiscovery.Tests/Ai/BatchLedgerTests.cs
  modified: []
decisions:
  - "07-07: AllowBatch is excluded from the request identity; null and empty Effort encode the same"
  - "07-07: SafeFileId anchors with \\z instead of $ so an id with a trailing newline is hashed, not kept"
  - "07-07: Replay keeps the first Submitted record per batch id; Pending orders by SubmittedUtc then batch id"
  - "07-07: ReadResults also skips lines whose Outcome/Response enums are undefined"
metrics:
  duration: "~15 min"
  completed: 2026-10-06
  tasks: 2
  files: 6
---

# Phase 7 Plan 07: Batch Contracts, Request Identity and Ledger Summary

Provider-neutral `IBatchTransport` contracts, SHA-256 deterministic `custom_id`s, and a crash-safe `provider-batches.jsonl` ledger with atomic cached results under `batch-results/`, plus a scripted `FakeBatchTransport` for the decorator/runner plans.

## What was built

- **RequestIdentity** — canonical string (provider, model, effort, schema name/JSON, system, user, max tokens joined by U+001F) hashed with SHA-256; id is `q` + 40 lowercase hex (41 chars, matches `^[a-zA-Z0-9_-]{1,64}$`). Same plan + journal gives the same id after a crash, so Continue can attach to an existing batch instead of paying twice.
- **IBatchTransport** and result types exactly as named in the plan; XML docs state the never-throw rule (only OCE from `ct` escapes).
- **BatchLedger** — SemaphoreSlim-gated append, torn-line repair, `Flush(flushToDisk: true)`; `Replay()` never throws (missing/unreadable file gives empty state; garbage, torn, `null` and undefined-enum lines skipped). Results are written as JSON lines via temp file + `File.Move(overwrite)` with 5 retries; `ReadResults` never throws. `ResultsPathFor` always stays in `{caseFolder}/batch-results`.
- **FakeBatchTransport** — `fb_N` ids, `PollsUntilEnded`, `ErroredIds`/`ExpiredIds`, `SubmitFailure`, `FailWholeBatch`, `SubmitDelay`, and records submissions, cancels, status calls and file deletes under a lock; responses are tagged `IsBatch = true`.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | ea3e092 | test(07-07): add failing deterministic request identity tests |
| 1 | GREEN | f7ee514 | feat(07-07): deterministic request identity and IBatchTransport contracts |
| 2 | RED | aee3ce4 | test(07-07): add failing batch ledger and fake transport tests |
| 2 | GREEN | 59b3a7e | feat(07-07): batch ledger with replay, cached results and scripted fake transport |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors
- `dotnet test --filter "BatchLedgerTests|RequestIdentityTests"`: 18/18 passed
- Acceptance greps: `Flush(flushToDisk: true)` = 1, `GeneratedRegex` = 1, `File.Move(` = 1, `generation-journal` = 0 in BatchLedger.cs; no `GetHashCode|Random|Guid.NewGuid` in RequestIdentity.cs

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Security] End-of-string anchor in SafeFileId**
- **Found during:** Task 2
- **Issue:** The .NET `$` anchor also matches before a trailing `\n`, so `"batch_1\n"` would have been kept as a file name.
- **Fix:** The regex uses `\z`, and a test checks that a trailing-newline id gets hashed.
- **Files modified:** Services/Ai/BatchLedger.cs, ReelDiscovery.Tests/Ai/BatchLedgerTests.cs
- **Commit:** 59b3a7e

## TDD Gate Compliance

Both tasks have a RED `test(...)` commit followed by a GREEN `feat(...)` commit. No refactor commits were needed.

## Known Stubs

None. Nothing calls these contracts yet; 07-08, 07-09 and 07-10 will consume them.

## Self-Check: PASSED
