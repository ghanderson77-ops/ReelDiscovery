---
phase: 10-answer-key-batch-safety
plan: 03
subsystem: corpus/answer-key
tags: [answer-key, relabel, sidecar, sha256, csv, xlsx, concurrency]
requires:
  - "10-01: AnswerKeyReader (ReadShared, ParseCsv, ParseXlsx, NormalizeRelevance, StripCsvGuard)"
  - "10-01: AnswerKeyRelabelStore and the AnswerKeyRelabel / AnswerKeyFileSnapshot / AnswerKeyRelabelSet DTOs"
provides:
  - "AnswerKeyFiles.WriteAsync: the single answer-key funnel (harvest, apply, build, write, snapshot, save sidecar)"
  - "AnswerKeyFiles.PeekRelabels: dry harvest for the D-06/D-07/D-08 warnings"
  - "AnswerKeyFiles.BuildBytes and AnswerKeyFiles.Apply"
  - "AnswerKeyWriteResult and AnswerKeyBackup"
affects:
  - "Plan 10-05 routes GenerationOutputs.WriteAnswerKeyAsync and CorpusApproval.BuildFiles through the funnel"
tech-stack:
  added: []
  patterns:
    - "Per-file baseline: a SHA-256 snapshot plus the relabels applied at our last successful write of each key file"
    - "Per-case-folder SemaphoreSlim held across harvest, both writes and the sidecar save"
key-files:
  created:
    - Services/Corpus/AnswerKeyFiles.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs
  modified: []
decisions:
  - "Rationale text is compared and stored with line endings normalized to LF, because XLSX reads CRLF back as LF"
  - "A stored relabel whose Relevance equals the plan label, or is not a canonical label, is dropped on load"
  - "Votes from the two files that agree in both Relevance and Rationale are not a conflict; the CSV vote is kept"
metrics:
  duration: "~20 min"
  completed: 2026-10-12
  tasks: 2
  files: 2
---

# Phase 10 Plan 03: Answer-Key Relabel Funnel Summary

`AnswerKeyFiles.WriteAsync` is now the one place that writes answer-key.csv and answer-key.xlsx. Before each write it harvests hand relabels from both files, keeps them in `answer-key-relabels.json` and applies them to the plan projection. It stores a SHA-256 baseline for each file, so our own writes and stale files are never read as user edits. Nothing calls it in production yet; Plan 05 routes the existing writers through it.

## What Was Built

### Task 1: Public surface and lifecycle tests (RED, commit b120d85)
- `AnswerKeyFiles` stubs with the exact surface from the plan, plus `AnswerKeyWriteResult` and `AnswerKeyBackup`.
- `AnswerKeyRelabelTests` has 20 `[Fact]`s. Every behavior bullet in the plan has a test, plus:
  - `MultiLineRationale_LineEndings_NotAnEdit` (requested by the orchestrator);
  - `Apply_RelabelsOnlyTheDocId`;
  - `BuildBytes_NoRelabels_EqualsPlanProjection` (KEY-03).
- Test edit helpers rebuild the rows the file holds now (the projection, with Relevance and Rationale as they are on disk). They save them as CSV, or as a shared-string workbook the way Excel does.
- All 20 tests failed at runtime against the stubs, and the build was green with `-warnaserror`.

### Task 2: Funnel implementation (GREEN, commit ad0304b)
- **Gate:** a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by the normalized full path of the case folder. It is held across harvest, the CSV write, the XLSX write and the sidecar save. `PeekRelabels` does not take it.
- **Harvest (Pattern 1):** for each file the funnel:
  - reads it with `ReadShared`;
  - skips it when its hash equals our snapshot;
  - parses it and finds the owning plan: the snapshot's PlanId, or for a legacy file an exact DocID and Thread ID match with the plan;
  - collects votes for rows that differ from that file's own baseline (`snapshot.Applied`, otherwise the plan label).
- **Voting rules:**
  - Duplicate rows that disagree are ignored.
  - Only canonical Relevance values count.
  - Unknown, added and deleted rows are ignored.
  - When both files vote and the votes conflict, the later LastWriteTimeUtc wins. On equal times the CSV wins.
- **Rationale (D-23):**
  - A Rationale is custom only when it is non-empty after the guard is stripped and the line endings are normalized, and it does not start with the base projected rationale.
  - On a row that is already relabeled, a Rationale-only edit is captured when it differs from the snapshot's applied rationale.
- **Apply:** sets Relevance on the relabeled DocID only, and replaces the Rationale when a custom one exists.
- **Write loop:**
  - A file that could not be read is reported (`FileOpenElsewhere`) and not overwritten.
  - A damaged or oversized file is backed up as `answer-key (unreadable yyyyMMdd-HHmmss[-n]).ext` before the write, using the writer, or `File.Copy` when it is too large.
  - If the backup fails, the file is not overwritten.
  - Each successful write records a new snapshot. A failed write keeps the old one.
- **Sidecar save:** only this PlanId's relabels and snapshots are kept. Save failures are tolerated, with a comment explaining why.

## Verification
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors.
- `dotnet test ... --filter "FullyQualifiedName~AnswerKey"`: 148 passed, 0 failed. This covers AnswerKeyRelabelTests, AnswerKeyReaderTests, AnswerKeyBuilderTests, AnswerKeyCsvWriterTests, AnswerKeyXlsxWriterTests, AnswerKeyRelabelStoreTests and AnswerKeyOverlayTests.
- Mutation check: with line-ending normalization removed from the funnel, `MultiLineRationale_LineEndings_NotAnEdit` fails. The code was then restored.
- Acceptance greps:
  - `NotImplementedException` = 0
  - `SHA256.HashData` = 1
  - `ConcurrentDictionary<string, SemaphoreSlim>` = 1
  - `unreadable ` = 2
  - `yyyyMMdd-HHmmss` = 1
  - writer `Build(` calls = 2
  - WriteAsync signature = 1
  - `Services.Evidence` using = 0
  - `[Fact]` = 20
- `AnswerKeyBuilder.cs` is unchanged (D-11).

## Deviations from Plan

### Auto-fixed / additions within scope

**1. [Rule 2 - Correctness] Line endings normalized in Rationale comparison and storage**
- **Found during:** Task 2, from the hand-off note in 10-01-SUMMARY.
- **Issue:** XLSX reads CRLF back as LF. A multi-line projected rationale would then fail the StartsWith test and be captured as a custom rationale, and a stored CRLF rationale would cause spurious Rationale-only votes.
- **Fix:** `Normalize` (CRLF or CR to LF) is applied to the cell and to the base rationale before comparing. Custom rationales are stored with LF only. The test `MultiLineRationale_LineEndings_NotAnEdit` covers it.
- **Files:** Services/Corpus/AnswerKeyFiles.cs, ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs
- **Commits:** b120d85, ad0304b

**2. [Rule 2 - Correctness] Empty base rationale**
- The custom test is `baseText.Length > 0 && StartsWith`. With an empty projected rationale, `StartsWith("")` would otherwise discard every user rationale.

**3. [Rule 2 - Hardening] Stored relabels normalized on load**
- A relabel whose Relevance is not canonical, or equals the plan label, is dropped when the sidecar is read. A hand-edited sidecar cannot inject other values.

**Plan text note:** the plan's `bool forWrite` harvest parameter was not needed, because the harvest has no side effects. `PeekRelabels` and `WriteAsync` share one private `Harvest`.

## TDD Gate Compliance
- RED: `test(10-03)` b120d85. 20 of 20 tests failed at runtime (NotImplementedException), and the build was green.
- GREEN: `feat(10-03)` ad0304b. 20 of 20 pass.
- No refactor commit was needed.

## Known Stubs
None.

## Threat Flags
None. The surfaces (user-edited key files, the backup and sidecar paths, and concurrent writers) are covered by T-10-10 to T-10-15:
- Backup names use a constant stem, an invariant timestamp and a counter.
- Files that could not be read are never overwritten.
- The 100 MB cap means oversized files are copied aside, not parsed.
- The per-folder gate serializes writers.

## Self-Check: PASSED
- FOUND: Services/Corpus/AnswerKeyFiles.cs
- FOUND: ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs
- FOUND: b120d85
- FOUND: ad0304b
