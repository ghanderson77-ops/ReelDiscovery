---
phase: 02-case-scenario-core-document
plan: 04
subsystem: case-validation-editing
tags: [validation, citations, real-names, jurisdiction, editing, tests]
requires:
  - "02-01 CaseDraft, CoreDocument, ComplexityPreset, Jurisdiction.UsStates, CaseTestData"
  - "02-02 CitationScanner.Scan, NameSubstitution.FindLeaks"
  - "02-03 CoreDocumentTemplates.GetSectionSpecs, CoreDocumentAssembler.CountNumberedParagraphs/StripLeadingNumber"
provides:
  - "Services/Case/CoreDocumentValidator (Validate, ValidateOutline, CountErrors, CountWarnings, CountCitationErrors; IssueSeverity, IssueCategory, CoreDocumentIssue)"
  - "Services/Case/CoreDocumentEditing (SplitParagraphs, JoinParagraphs, ApplyEdit)"
affects: [02-06, 02-08, 02-09]
tech-stack:
  added: []
  patterns: ["Pure static validator returning an issue list; bad data becomes issues, never exceptions"]
key-files:
  created:
    - Services/Case/CoreDocumentValidator.cs
    - Services/Case/CoreDocumentEditing.cs
    - ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs
    - ReelDiscovery.Tests/Case/CoreDocumentEditingTests.cs
  modified: []
decisions:
  - "[02-04]: Memo date earlier than timeline end warns 'The timeline ends after the memo date.' (pleadings use 'filing date')"
  - "[02-04]: Blank timeline dates are reported as the same yyyy-MM-dd errors as unparseable ones"
  - "[02-04]: ApplyEdit on a Failed section with non-empty text sets Status Ready and clears FailureMessage"
metrics:
  duration: 12min
  completed: 2026-10-01
---

# Phase 2 Plan 04: CoreDocumentValidator and CoreDocumentEditing Summary

This plan adds two pure services. `CoreDocumentValidator.Validate(CaseDraft)` checks preset ranges for parties, counts/findings, timeline span, numbered paragraphs and memo length. It also checks timeline dates and their order, fact-plan dates against the timeline, the filing or memo date, and required, failed or unwritten sections. On the jurisdiction side it flags federal and state court contradictions (state-name mismatch only warns, with whole-word matching), a missing court name, a missing 1331/1332 basis and a state that is never mentioned. It runs `CitationScanner` per section, and `NameSubstitution.FindLeaks` per section and over the outline names (multi-word names are errors, single tokens are warnings). Finally it warns on paragraph-number references and on a stale SourceHash. `CoreDocumentEditing` splits editor text on blank lines, strips typed numbers, and applies edits. Revision goes up only when the text actually changed.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | CoreDocumentValidator | 8ef81d8 (RED), 458b0a9 (GREEN) | Services/Case/CoreDocumentValidator.cs, ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs |
| 2 | CoreDocumentEditing | 86270f0 (RED), 4c9b7bc (GREEN) | Services/Case/CoreDocumentEditing.cs, ReelDiscovery.Tests/Case/CoreDocumentEditingTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx --no-incremental`: 0 warnings, 0 errors
- `dotnet test --filter ReelDiscovery.Tests.Case`: 170 passed; full suite 384 passed, 0 failed
- CoreDocumentValidatorTests: 38 test cases (34 facts + one 4-case theory); CoreDocumentEditingTests: 9 facts
- Acceptance greps all satisfied (Validate signature, CitationScanner.Scan, NameSubstitution.FindLeaks, stale copy x1, CountCitationErrors x1, no MessageBox, federal-court copy x1, Jurisdiction.UsStates, StartsWith("United States" x1, " Parish", MemoParagraphsMin, "Possible real name" x1; ApplyEdit signature, draft.Revision++ x1, one [GeneratedRegex] in editing)

## Deviations from Plan

**1. [Rule 1 - Bug in plan spec] The sample memo has 20 paragraphs, not 18**
- **Found during:** Task 1
- **Issue:** The plan's expected message said "The memo has 18 paragraphs". The `SampleMemoDraft()` fixture actually has 20 paragraphs (7 sections x 2 + 2 fact sections x 3).
- **Fix:** The test asserts "The memo has 20 paragraphs; the Simple preset expects 36-60." The validator logic is unchanged.
- **Commit:** 8ef81d8

**2. [Rule 2 - Robustness] Extra checks added**
- `GetSectionSpecs` throws on an unknown document type. The validator catches that and reports it as a Structure error, so it never throws (T-02-15).
- Added tests: Virginia case vs. a West Virginia court (longest name wins), memo findings noun, timeline span warning, outline leak, malformed dates do not throw, and a Failed section that becomes Ready after a hand edit.

## Known Stubs

None.

## TDD Gate Compliance

RED commits (test) precede GREEN commits (feat) for both tasks.

## Self-Check: PASSED
