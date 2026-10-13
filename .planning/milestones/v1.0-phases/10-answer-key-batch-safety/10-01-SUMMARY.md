---
phase: 10-answer-key-batch-safety
plan: 01
subsystem: corpus/answer-key
tags: [answer-key, relabel, csv, xlsx, openxml, sidecar, legacy-cleanup]
requires: []
provides:
  - "AnswerKeyReader: ReadShared, ParseCsv, ParseXlsx, NormalizeRelevance, StripCsvGuard"
  - "AnswerKeyCsvWriter.IsGuarded"
  - "AnswerKeyRelabelStore (answer-key-relabels.json)"
  - "AnswerKeyRelabel, AnswerKeyFileSnapshot, AnswerKeyRelabelSet DTOs"
  - "Models/AttachmentType.cs (moved verbatim)"
affects:
  - "Plan 10-03 relabel funnel (AnswerKeyFiles) builds on the reader and the store"
tech-stack:
  added: []
  patterns:
    - "Hand-written RFC 4180 CSV state machine (no TextFieldParser, no CodePages package)"
    - "Shared read: FileStream with FileShare.ReadWrite | FileShare.Delete copied to memory"
    - "QaReportStore sidecar pattern (constant name, tolerant read, atomic temp + File.Move)"
key-files:
  created:
    - Services/Corpus/AnswerKeyReader.cs
    - Services/Corpus/AnswerKeyRelabelStore.cs
    - Models/Corpus/AnswerKeyRelabels.cs
    - Models/AttachmentType.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyRelabelStoreTests.cs
  modified:
    - Services/Corpus/AnswerKeyCsvWriter.cs
    - ReelDiscovery.Tests/PlatformTests.cs
  deleted:
    - Models/Attachment.cs
decisions:
  - "CSV read-back uses a hand-written RFC 4180 state machine so whitespace inside quoted Rationale is never trimmed"
  - "XLSX stores CRLF inside a cell as LF (XML end-of-line handling); Plan 03 must compare Rationale with line endings normalized"
  - "The relabel sidecar normalizes hand-edited JSON on load: null collections become empty, null entries and blank DocIds are dropped"
metrics:
  duration: "~7 min"
  completed: 2026-10-12
  tasks: 3
  files: 9
---

# Phase 10 Plan 01: Answer-Key Reader, Relabel Sidecar and Legacy Attachment Removal Summary

This plan adds a tolerant answer-key reader for CSV and XLSX and the `answer-key-relabels.json` sidecar store with its DTOs. It also deletes the legacy `Attachment` and `AttachmentComplexity` types (D-18). The reader handles every format Excel saves (BOM or ANSI, `,` `;` or tab, a `sep=` line, quoted multi-line fields, inline, shared and formula strings) and still reads a key file that Excel holds open.

## What Was Built

### Task 1: AnswerKeyReader (commits 7c2e76b RED, e48a197 GREEN)
- `AnswerKeyCsvWriter.IsGuarded(string?)` exposes the guard check. `FormulaLeads` stays private and is not duplicated anywhere.
- `AnswerKeyReader.ReadShared` opens the file with `FileShare.ReadWrite | FileShare.Delete` and copies it into memory.
  - A missing file, or one deleted during the read, returns `Exists = false`.
  - An I/O or access failure returns `ReadError`.
  - A file over 100 MB returns `TooLarge` and is not read.
- `ParseCsv` decoding:
  - Detects a UTF-8 or UTF-16 BOM.
  - Otherwise tries strict UTF-8 and falls back to Latin1.
- `ParseCsv` separator:
  - Uses the `sep=` first line when there is one.
  - Otherwise picks the first of `,` `;` tab that gives a header with both DocID and Relevance.
- `ParseCsv` maps columns by header name. Rows with no DocID are skipped and Rationale is capped at `MaxCellLength`. It never throws.
- `ParseXlsx` opens a read-only MemoryStream, never a path.
  - It picks the "Answer Key" sheet first, then any sheet whose header has DocID and Relevance.
  - It reads shared, inline and formula/cached cell values and places cells by their `CellReference`.
  - Text inside a `PhoneticRun` is excluded.
- `NormalizeRelevance` accepts only "Relevant" and "Not Relevant": trimmed, case-insensitive, one leading `'` stripped.
- `StripCsvGuard` removes the `'` only when the writer added it.

### Task 2: Relabel sidecar (commits 9df1c67 RED, ff85797 GREEN)
- `Models/Corpus/AnswerKeyRelabels.cs` contains three mutable classes:
  - `AnswerKeyRelabel`
  - `AnswerKeyFileSnapshot` (PlanId, Sha256, WrittenUtc, `Applied` keyed by DocId)
  - `AnswerKeyRelabelSet` (Version 1, `Relabels`, `Files` keyed by constant file name)
- `AnswerKeyRelabelStore` loading:
  - The file is the case folder plus the constant `answer-key-relabels.json`, serialized with `CaseDraft.JsonOptions`.
  - A missing, damaged or over-64-MB sidecar reads as an empty set.
  - Null entries are normalized away.
- `AnswerKeyRelabelStore` saving:
  - Writes atomically: temp file with CreateNew + WriteThrough, then `File.Move(overwrite: true)` with retries, and the temp file is cleaned up on failure.
  - Saving into a missing folder throws `IOException`, so the caller decides whether to tolerate it.

### Task 3: D-18 legacy types (commit 05f8b0c)
- `enum AttachmentType` moved verbatim (same members and order, CRLF kept) to `Models/AttachmentType.cs`.
- `Models/Attachment.cs` deleted with `git rm`. This was an intentional deletion of `class Attachment` and `enum AttachmentComplexity`.
- `PlatformTests.LegacyType_IsDeleted` now also guards `ReelDiscovery.Models.Attachment` and `ReelDiscovery.Models.AttachmentComplexity`.

## Verification
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors.
- Filtered tests for AnswerKeyReaderTests, AnswerKeyRelabelStoreTests, AnswerKeyCsvWriterTests, AnswerKeyXlsxWriterTests and PlatformTests: 103 passed, 0 failed.
- Every grep acceptance criterion in the plan was checked and met.

## Deviations from Plan

### Test adjustment (no production change)

**1. [Rule 1 - Test expectation] XLSX read-back of a CRLF Rationale returns LF**
- **Found during:** Task 1 GREEN
- **Issue:** The inline-string round-trip test expected `"\r\n"` inside Rationale. XML end-of-line handling stores a CR that is not escaped as LF, so `AnswerKeyXlsxWriter` output reads back with `"\n"`. This is how the format works; the reader is correct. Trailing spaces are kept (`xml:space="preserve"`).
- **Fix:** The XLSX round-trip assertion compares with `\r\n` replaced by `\n`. The CSV round-trip still asserts the exact CRLF text.
- **Files modified:** ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs
- **Commit:** e48a197
- **Note for Plan 03:** When the funnel compares a Rationale read from XLSX with the projected rationale (D-23), it must normalize line endings. Otherwise every multi-line rationale counts as a user edit.

### Additions within scope
- `AnswerKeyRelabelStore.Load` also normalizes snapshot `Applied` entries that have blank keys or null values, and a null `Sha256`. It is extra hardening of the tolerant read (Rule 2).
- Additional tests cover CSV rows with no DocID, random CSV bytes, and a sidecar with null collections.

## TDD Gate Compliance
- Task 1: RED `test(10-01)` 7c2e76b, then GREEN `feat(10-01)` e48a197.
- Task 2: RED `test(10-01)` 9df1c67, then GREEN `feat(10-01)` ff85797.
- Each RED commit added stubs that throw `NotImplementedException` so the commit still builds with `-warnaserror`. The tests failed at runtime (23 of 23 and 8 of 9).

## Known Stubs
None. The RED stubs were replaced in the GREEN commits.

## Threat Flags
None. The new surfaces (reading user-edited key files and the sidecar) are covered by T-10-01 to T-10-04: the 100 MB and 64 MB caps, parsers that never throw, the canonical-label allowlist, the constant sidecar path, and Rationale truncation.

## Self-Check: PASSED
