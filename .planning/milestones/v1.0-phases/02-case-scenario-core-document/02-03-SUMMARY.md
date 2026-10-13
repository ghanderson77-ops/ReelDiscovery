---
phase: 02-case-scenario-core-document
plan: 03
subsystem: case-legal-skeleton
tags: [case, pleading, memo, numbering, caption, tdd]
requires:
  - "02-01 Models/Case (CoreDocument, CoreDocumentSection, Jurisdiction, CaseDraft.TryParseDate)"
provides:
  - "DocumentTypeResolver.Resolve (CASE-04 doc-type decision)"
  - "CoreDocumentTemplates.GetSectionSpecs / SectionSpec / IsPleading / MaxParagraphsPerSection"
  - "CoreDocumentAssembler (Number, IncorporationText, BuildCaption, BuildMemoHeader, BuildSignatureBlock, RenderPlainText, StripLeadingNumber, ToRoman)"
affects: [02-04, 02-06, 02-08]
tech-stack:
  added: []
  patterns: ["Pure static services; numbering computed on every render, never stored", "[GeneratedRegex] leading-number strip"]
key-files:
  created:
    - Services/Case/DocumentTypeResolver.cs
    - Services/Case/CoreDocumentTemplates.cs
    - Services/Case/CoreDocumentAssembler.cs
    - ReelDiscovery.Tests/Case/DocumentTypeResolverTests.cs
    - ReelDiscovery.Tests/Case/CoreDocumentTemplatesTests.cs
    - ReelDiscovery.Tests/Case/CoreDocumentAssemblerTests.cs
  modified: []
decisions:
  - "[02-03]: Number() strips LLM leading numbers and drops blank paragraphs before numbering, so stray '12.' prefixes never double-number"
  - "[02-03]: Every count incorporates through the last factual paragraph (not the previous count's paragraphs)"
metrics:
  duration: 10min
  completed: 2026-10-01
---

# Phase 2 Plan 03: Document Type, Templates and Assembler Summary

Code now owns the legal skeleton. `DocumentTypeResolver` chooses Complaint, Petition or Investigation Memo, and a user preference always wins. `CoreDocumentTemplates` sets the required section list for each type. `CoreDocumentAssembler` handles paragraph numbering, the per-count incorporation paragraphs, the caption (which names no judge), the lettered prayer, the jury demand, the signature block, the memo header and the full plain-text render. All of these are pure static functions over `CoreDocument`.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | DocumentTypeResolver and CoreDocumentTemplates (+ ToRoman) | 6ea8cd2 (RED), 0d8e43e (GREEN) | DocumentTypeResolver.cs, CoreDocumentTemplates.cs, CoreDocumentAssembler.cs, 3 test files |
| 2 | CoreDocumentAssembler numbering, caption, prayer, signature, memo header | c170ed0 (RED), 1978deb (GREEN) | CoreDocumentAssembler.cs, CoreDocumentAssemblerTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx` finished with 0 warnings and 0 errors.
- `dotnet test ReelDiscovery.slnx` passed all 326 tests. The `ReelDiscovery.Tests.Case` filter alone ran 112 tests and all passed.
- Acceptance greps: the Resolve signature, the GetSectionSpecs signature, `MaxParagraphsPerSection = 40` and the four memo/petition ids each match as specified. `repeats and realleges paragraphs 1 through` appears once, `LAW ENFORCEMENT SENSITIVE` appears once, "judge" appears 0 times outside comments and `record ` appears 0 times.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Nullable warnings in the tests**
- **Found during:** Task 1
- **Issue:** `CaseDraft.CoreDocument` is nullable, so the template tests raised CS8604 and CS8602 warnings.
- **Fix:** The tests now use `CoreDocument!`.
- **Commit:** 0d8e43e

### Additions (non-breaking)
- `Number()` runs `StripLeadingNumber` on each paragraph and skips blank ones (threat T-02-09).
- New tests: more ToRoman cases (40, 1994), a multi-count incorporation test, a state Petition caption test (Cause No. and a county line), a check that the memo has no CC line when Cc is blank, and an IsPleading test.
- `ToRoman` throws `ArgumentOutOfRangeException` for values outside 1..3999.

## TDD Gate Compliance

Both tasks have a `test(02-03)` RED commit (a compile failure) followed by a `feat(02-03)` GREEN commit.

## Known Stubs

None.

## Self-Check: PASSED
