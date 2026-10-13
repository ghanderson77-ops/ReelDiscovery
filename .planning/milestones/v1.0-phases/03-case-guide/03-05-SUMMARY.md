---
phase: 03-case-guide
plan: 05
subsystem: case-exports
tags: [docx, openxml, exports, case-guide, tests]
requires:
  - "03-01 (CaseDraft.Guide, CaseGuide Find*, CastMember/KeyFact/CaseOrganization, SampleGuidedDraft)"
  - "Phase 2 CoreDocumentAssembler (Number, BuildCaption, BuildMemoHeader, BuildSignatureBlock, StripLeadingNumber, PleadingPartyLabel)"
provides:
  - "Services/Case/CaseGuideDocxExporter.cs (CaseExportKind, Build, BuildCoreDocument, BuildCast, BuildKeyFacts, FileNameFor, DocumentNoun, internal StripInvalidXmlChars, internal LongDate)"
affects: [03-12, 03-13, 03-14]
tech-stack:
  added: []
  patterns:
    - "Private WordBuilder: StyleDefinitionsPart (Normal, Title, Subtitle, Heading1, Heading2, TableText, GuideTable) + schema-ordered pPr/rPr/tblPr/tcPr/sectPr"
    - "Paragraph-level sectPr ends the section it sits in: title + wide tables on landscape pages, prose (profiles, index) on portrait"
    - "Every text run goes through StripInvalidXmlChars; \\n -> Break, \\t -> TabChar"
    - "Unknown ids render as raw ids; empty lists render (none) instead of a table"
key-files:
  created:
    - Services/Case/CaseGuideDocxExporter.cs
    - ReelDiscovery.Tests/Case/CaseGuideDocxExporterTests.cs
  modified: []
decisions:
  - "[03-05]: Cast and Key Facts exports put the title block on the first landscape page with the wide tables (no near-empty portrait title page); profiles, background staff and the issue index stay portrait"
  - "[03-05]: Pleading caption reuses BuildCaption text; the padded 'v. ... Case No.' line becomes a right tab stop and indented party labels become a 720-twip indent"
  - "[03-05]: Memo header TO/FROM/CC/DATE/RE render as bold label + tab-aligned value (text 'RE:\\tvalue'); the plain-text dash rule becomes a paragraph bottom border"
  - "[03-05]: FACTUAL ALLEGATIONS / CAUSES OF ACTION group headers are Heading1 and their subsections Heading2 so Word's navigation pane nests them"
  - "[03-05]: No page-number footer: PAGE fields would contradict the threat model's 'no fields emitted' acceptance (T-03-18)"
metrics:
  duration: 22min
  completed: 2026-10-01
---

# Phase 3 Plan 05: Case Guide Word Exports Summary

Validator-clean .docx exports built with a small schema-ordered OpenXml builder: the core document (caption or memo header, nested headings, assembler numbering, prayer letters, signature block), the Cast of Key Characters (landscape organization and key-cast tables, per-character profiles, background staff by organization) and Key Facts & Events (landscape chronology with names, then an index by issue).

## What Was Built

- **WordBuilder** (private, in `CaseGuideDocxExporter`): styles part with docDefaults (Calibri 11pt), Title/Subtitle/Heading1/Heading2/TableText paragraph styles and the `GuideTable` table style. Paragraph options cover style, bold lead-in, alignment, indents, spacing, right tab, keep-next/keep-lines and bottom border. Tables follow the RESEARCH excerpt (TableStyle, pct width, horizontal-only borders, fixed layout, cell margins). The header row repeats on each page (`TableHeader`) and rows don't split (`CantSplit`). Portrait pages are 12240x15840 and landscape pages 15840x12240, both with 0.75" margins.
- **BuildCoreDocument**: mirrors `RenderPleading`/`RenderMemo` and reuses `CoreDocumentAssembler.Number`, `BuildCaption`, `BuildMemoHeader` and `BuildSignatureBlock`. It does not reimplement numbering. Paragraphs are justified with a first-line indent. Prayer items get a hanging indent. The signature block sits on the right half of the page.
- **BuildCast** / **BuildKeyFacts**: as specified. Facts are sorted by date, then id, with undated facts last. "Who did it" and "Who knew" show names. Each fact cell has a bold title lead-in.
- `Build(draft, kind)`, `FileNameFor`, `DocumentNoun`, `LongDate`, `StripInvalidXmlChars`. These keep valid surrogate pairs and drop lone surrogates, C0 controls and U+FFFE/FFFF.
- 15 test methods / 23 test cases. Every export asserts zero `OpenXmlValidator` errors, including the control-character, orphan-id and empty-guide cases.

## Visual Check

I rendered all four samples (complaint, memo, cast, key facts) to PDF through Word COM and inspected them. Word was closed afterwards, and the output stayed in the scratchpad. Headings, tables, caption tab stop, memo label alignment and signature block all look clean. Two issues came out of this check and were fixed (see deviations): a near-empty title page, and a profile splitting across pages.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Visual quality] Removed the near-empty portrait title page in Cast and Key Facts**
- **Found during:** Task 2 visual render
- **Issue:** The plan's "portrait intro then landscape section" left page 1 holding only three lines.
- **Fix:** The title, subtitle and intro now open the landscape section above the tables. A landscape section is still present (tested).
- **Commit:** 806d0ee

**2. [Rule 1 - Visual quality] Profiles kept together and column widths tuned**
- **Found during:** Task 2 visual render
- **Issue:** One signature block split across pages. "In-house counsel" and "February 12, 2024" wrapped awkwardly.
- **Fix:** Profile lines now use KeepNext and the signature uses KeepLines. Role column 1000->1300 and Date column 1500->1700, with widths still summing to 13680.
- **Commit:** 806d0ee

**3. [Test copy] Memo RE line asserted as "RE:\t{value}"**
- The memo header renders as a bold label plus a tab-aligned value, so the test checks the tab form instead of "RE: ".

## Known Stubs

None.

## TDD Gate Compliance

RED and GREEN commits exist for both tasks: test 7cca62b, feat 84ff11f, test e51fd45, feat 806d0ee.

## Requirement Status

GUIDE-11 is **not** marked complete. The service side is done, but the user-facing export buttons (StepCaseSummary + `CaseStore.WriteAtomicAsync`) come in a later plan.

## Self-Check: PASSED

- FOUND: Services/Case/CaseGuideDocxExporter.cs
- FOUND: ReelDiscovery.Tests/Case/CaseGuideDocxExporterTests.cs
- FOUND commits: 7cca62b, 84ff11f, e51fd45, 806d0ee
- Full suite: 676/676 passed; build 0 warnings
