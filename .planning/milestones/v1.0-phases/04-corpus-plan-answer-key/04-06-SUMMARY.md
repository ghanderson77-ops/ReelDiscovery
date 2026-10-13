---
phase: 04-corpus-plan-answer-key
plan: 06
subsystem: corpus-answer-key
tags: [answer-key, csv, xlsx, openxml, csv-injection, statistics]
requires:
  - Models/Corpus/AnswerKeyRow.cs (AnswerKeyRow.ToValues, AnswerKeyColumns.Headers) (04-01)
  - Services/Corpus/AnswerKeyBuilder.cs (RelevantLabel, NotRelevantLabel, CategoryLabel) (04-05)
  - Services/Case/CaseGuideDocxExporter.StripInvalidXmlChars
provides:
  - AnswerKeyCsvWriter.Build(IReadOnlyList<AnswerKeyRow>) -> byte[], Field(string?), FileName ("answer-key.csv")
  - AnswerKeyXlsxWriter.Build(IReadOnlyList<AnswerKeyRow>, CaseGuide) -> byte[], FileName ("answer-key.xlsx"), SheetNames, MaxCellLength, ColumnName(int), NoCustodian, Undated
affects:
  - 04-18 Approve flow (writes these bytes via CaseStore.WriteAtomicAsync)
tech-stack:
  added: []
  patterns:
    - Pure byte writers (no I/O) tested against OpenXmlValidator and RFC 4180/OWASP rules
    - Inline-string-only spreadsheet cells (no CellFormula), sanitized and capped
key-files:
  created:
    - Services/Corpus/AnswerKeyCsvWriter.cs
    - Services/Corpus/AnswerKeyXlsxWriter.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyCsvWriterTests.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyXlsxWriterTests.cs
  modified: []
decisions:
  - "[04-06]: Statistics sections are title row + header row + data rows, separated by one skipped row index; By Custodian ends with a Total row; blank custodian groups as '(No custodian)' and unparseable Date Sent as '(Undated)', both sorted last"
  - "[04-06]: Statistics by issue counts rows whose '; '-split Issue Codes contain the issue id; by relevance lists Relevant, Not Relevant, the four non-relevant category labels (from AnswerKeyBuilder.CategoryLabel), Hot, Privileged, Near-miss"
  - "[04-06]: Not Relevant = every row whose Relevance is not 'Relevant', so Relevant + Not Relevant always equals the row count per custodian and month"
  - "[04-06]: Cell cap at 32,767 backs off one char if it would split a surrogate pair"
metrics:
  duration: 10min
  completed: 2026-10-02
  tasks: 2
  files: 4
---

# Phase 4 Plan 06: Answer Key CSV and XLSX Writers Summary

The answer key now has two pure byte writers. One produces an RFC 4180 CSV (UTF-8 with BOM) that guards against formula injection. The other produces a three-sheet OpenXml workbook (Answer Key, Statistics, Legend) that passes OpenXmlValidator with 0 errors.

## What was built

- **AnswerKeyCsvWriter**: `Field` applies the OWASP guard. A field that starts with `= + - @`, tab, CR, LF or a full-width `＝＋－＠` gets a `'` prefix and is quoted. Fields containing a comma, quote, CR or LF are quoted, with embedded quotes doubled. `Build` writes the 23 headers and then one line per row, each ending in CRLF. The output is the UTF-8 BOM followed by the UTF-8 body.
- **AnswerKeyXlsxWriter**:
  - Stylesheet styles: 0 default, 1 bold header on fill FFE8F2FC, 2 wrapped and top-aligned body, 3 bold section title.
  - Answer Key sheet: frozen header pane (VerticalSplit 1, TopLeftCell A2), widths for all 23 columns, an autofilter over `A1:W{n+1}`, and a hidden workbook DefinedName `_xlnm._FilterDatabase` with LocalSheetId 0.
  - Statistics sheet: By Custodian, By Issue, By Relevance / Category and By Month. Every count is a `CellValues.Number` cell.
  - Legend sheet: Issue Codes (id, title, relevance criteria), Key Facts (id, date, title), Column Definitions (one per header) and Category Definitions.
  - Every text cell is an InlineString passed through `CaseGuideDocxExporter.StripInvalidXmlChars` and capped at 32,767 characters. No `CellFormula` is ever built.

## Verification

- `dotnet test --filter "FullyQualifiedName~AnswerKeyXlsxWriterTests|FullyQualifiedName~AnswerKeyCsvWriterTests|FullyQualifiedName~DocumentRegressionTests"`: 45 passed.
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 250 passed.
- No-incremental build: 0 warnings.
- Acceptance greps all pass: no CellFormula, `_xlnm._FilterDatabase` = 1, `PaneStateValues.Frozen` = 1, StripInvalidXmlChars used, full-width leads present, and `Services/OfficeDocumentService.cs` unchanged.

## TDD Gate Compliance

- Task 1: RED `911054c` (test), GREEN `a54537b` (feat)
- Task 2: RED `8ba6415` (test), GREEN `6621c87` (feat)
- No refactor commits were needed.

## Deviations from Plan

**1. [Rule 3 - Blocking] Font type alias**
- **Found during:** Task 2
- **Issue:** `Font` was ambiguous between `System.Drawing.Font` (from the WinForms implicit usings) and `DocumentFormat.OpenXml.Spreadsheet.Font`.
- **Fix:** Added `using Font = DocumentFormat.OpenXml.Spreadsheet.Font;`, the same alias approach `OfficeDocumentService` uses.
- **Commit:** `6621c87`

**2. [Additions] A few extra public members on AnswerKeyXlsxWriter**
- `ColumnName` is public so it can be tested directly, and `NoCustodian` and `Undated` are public constants for the Statistics labels.
- The workbook also gets a `BookViews`/`WorkbookView` so that `SheetView.WorkbookViewId = 0` points at a real view.

## Requirements

- KEY-04 (statistics sheet) is delivered in full.
- KEY-01 stays Pending. The files are written to disk only by the Approve flow in 04-18.
- PLAT-03 stays Partial. The answer-key writer tests exist; EML-related coverage continues in later phases.

## Self-Check: PASSED
