---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 10
subsystem: evidence-io
tags: [mimekit, eml, jsonl, journal, atomic-write, path-guard]
requires:
  - phase: 05-01
    provides: JournalRecord + Normalize, GenerationState.JournalFileName
  - phase: 05-02
    provides: PlanNumbering.CustodianFolders and folder constants
  - phase: 05-07
    provides: ComposedEmail / EmailAddressPart
provides:
  - MailboxWriter (CopyPathsFor, BuildMime, ResolveUnderCase, WriteAsync, DocIdHeader)
  - GenerationJournal (AppendAsync, Replay, ListAllFiles, Exists, Delete, PathFor, MaxJournalBytes)
  - JournalReplay result
affects: [05-11 unit of work, 05 run controller, Start Over sweep, Phase 6 non-relevant rendering]
tech-stack:
  added: []
  patterns:
    - Deterministic multipart boundary (SHA-256 of DocId|MessageId) so rewrites are byte-identical
    - Replay-as-source-of-truth with never-throw result object (Reason string)
key-files:
  created:
    - Services/Evidence/MailboxWriter.cs
    - Services/Evidence/GenerationJournal.cs
    - ReelDiscovery.Tests/Evidence/MailboxWriterTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationJournalTests.cs
  modified: []
decisions:
  - "MailboxWriter sets a deterministic multipart boundary and calls Prepare(SevenBit) so rewrites are byte-identical and no line exceeds RFC 5322 limits"
  - "Journal replay accepts records for any thread in the plan (not only relevant threads) so Phase 6 can reuse the same journal"
  - "JournalReplay.JournalPlanId is the last foreign PlanId seen; an IO error mid-read clears LastByThread and reports a Reason"
  - "ListAllFiles never throws: missing/oversized/unreadable journals return what could be read"
metrics:
  duration: 4min
  completed: 2026-10-03
---

# Phase 5 Plan 10: Mailbox Writer and Generation Journal Summary

The two I/O pieces of a thread's unit of work. MailboxWriter builds a multipart/alternative EML with MimeKit (plain text first, then HTML). The EML carries the DocID header, the plan's Message-ID and threading headers, and a Date that keeps the sender's offset. It writes the EML atomically into every custodian's Sent Items or Inbox, and no copy can land outside the case folder. GenerationJournal adds one flushed JSONL line per record, and its replay never throws: it skips torn lines and drops tampered records.

## What Was Built

- **MailboxWriter** (`Services/Evidence/MailboxWriter.cs`)
  - `CopyPathsFor` lists the sender's Sent Items copy first, then an Inbox copy for each cast recipient in CustodianIds order, then the planned FilePath. Every path is filtered through `CorpusPlan.IsSafeEmlPath` and duplicates are removed.
  - `BuildMime` creates the EML with the trimmed Message-ID, `InReplyTo` (only when present), `References`, `X-ReelDiscovery-DocID`, `Date = email.Date` and a multipart/alternative body from `BodyBuilder`.
  - `ResolveUnderCase` returns a path only if it passes `IsSafeEmlPath` and its full path stays under the case folder (prefix check).
  - `WriteAsync` serializes the email once. For each copy it then runs `Directory.CreateDirectory` and `CaseStore.WriteAtomicAsync`, and returns the relative paths written. IO errors propagate.
- **GenerationJournal** (`Services/Evidence/GenerationJournal.cs`)
  - `AppendAsync` builds the whole line first. It then takes a per-instance `SemaphoreSlim`, opens the file in `FileMode.Append`, writes once and calls `Flush(flushToDisk: true)`.
  - `Replay` checks the 200 MB cap, reads line by line with `FileShare.ReadWrite`, and counts blank, torn and corrupt lines in SkippedLines. It drops undefined Type values, calls `Normalize()` on each record (unsafe Files are removed), and treats a foreign PlanId as PlanChanged (ForeignThreadCount = distinct foreign ThreadDone thread ids). Records for threads not in the plan are dropped, and the last record per thread wins. It also sets `HasRunStarted`, `LastRunEnded` and `LastWriteUtc`.
  - `ListAllFiles`, `Exists`, `Delete` and `PathFor` (case folder plus a constant file name only).

## Verification

- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 431 passed, 0 failed.
- Every acceptance grep for both tasks matched.

## TDD Gate Compliance

- Task 1: RED `e13591c`, then GREEN `67b22c2`.
- Task 2: RED `bd13eab`, then GREEN `a6630c0`.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | e13591c | test(05-10): add failing tests for MailboxWriter |
| 1 GREEN | 67b22c2 | feat(05-10): implement MailboxWriter custodian copies |
| 2 RED | bd13eab | test(05-10): add failing tests for GenerationJournal |
| 2 GREEN | a6630c0 | feat(05-10): implement GenerationJournal append and replay |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Idempotent rewrite needs a deterministic MIME boundary**
- **Found during:** Task 1
- **Issue:** `BodyBuilder.ToMessageBody()` picks a random multipart boundary, so writing the same email twice would not give byte-identical files (a behavior the plan requires).
- **Fix:** The boundary is set to `=-rd-` plus a SHA-256 prefix of DocId and Message-ID.
- **Commit:** 67b22c2

**2. [Rule 2 - Correctness] RFC 5322 line length**
- **Found during:** Task 1
- **Issue:** Plain-text paragraphs can be longer than 998 characters, and the EML would then be invalid.
- **Fix:** `message.Prepare(EncodingConstraint.SevenBit)` applies a transfer encoding where it is needed. It is deterministic, and the round-trip test still passes.
- **Commit:** 67b22c2

**3. [Rule 2 - Robustness] Per-line catch for non-JSON serializer exceptions**
- **Found during:** Task 2
- **Issue:** Besides `JsonException`, the serializer can throw `NotSupportedException`, `InvalidOperationException` or `ArgumentException` for odd values. These would have ended the whole replay instead of skipping one line.
- **Fix:** A second per-line catch skips those lines too.
- **Commit:** a6630c0

## Known Stubs

None.

## Requirements

OUT-01, OUT-02, CORP-06, RUN-01 and RUN-06 are not marked complete. This plan delivers only the I/O primitives; the unit of work and run controller that use them end-to-end come in later Phase 5 plans.

## Self-Check: PASSED
