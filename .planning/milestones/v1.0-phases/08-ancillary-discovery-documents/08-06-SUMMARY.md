---
phase: 08-ancillary-discovery-documents
plan: 06
subsystem: discovery-transcript
tags: [discovery, deposition, transcript, docx, openxml, ANC-01, ANC-05]
requires:
  - "08-01: DepositionContent, DepositionTurn, DepositionSpeaker, DepositionExhibit, TurnKind, SpeakerRole"
  - "08-02: DocxTestHelper.OpenAndValidate / ErrorsText"
provides:
  - "TranscriptLayout: LinesPerPage (25), MaxCharsPerLine (62), Wrap, SpeakerLabel, FormatTurn, ComputeEndTime, CountTestimonyLines, Build"
  - "TranscriptPage, TranscriptLocation, TranscriptDocument (Pages, First/LastTestimonyPage, TestimonyPageCount, ExhibitMarked, Examinations)"
  - "TranscriptDocxBuilder.Build(DepositionContent) and Build(TranscriptDocument, title)"
  - "Test fixture TranscriptTestData.Sample(turnCount, memo)"
affects: [08-deposition-writer, 08-validator, 08-generator]
tech-stack:
  added: []
  patterns:
    - "Everything is a line: turns -> <=62-char lines -> 25-line pages, page:line map computed in code, never by Word"
    - "Index chicken-and-egg solved with fixed-width right-aligned page numbers: placeholder index has the same line count as the final index"
    - "Sibling .docx builder (not WordBuilder): Courier New 12 pt, exact 480 spacing, pageBreakBefore per page heading, no lnNumType or PAGE fields"
key-files:
  created:
    - Services/Discovery/TranscriptLayout.cs
    - Services/Discovery/TranscriptDocxBuilder.cs
    - ReelDiscovery.Tests/Discovery/TranscriptTestData.cs
    - ReelDiscovery.Tests/Discovery/TranscriptLayoutTests.cs
    - ReelDiscovery.Tests/Discovery/TranscriptDocxBuilderTests.cs
  modified: []
decisions:
  - "08-06: Wrap keeps first-line leading spaces (federal indentation) and maps each whitespace char to a space instead of collapsing; turn text itself is collapsed with the generated \\s+ regex before the prefix is added"
  - "08-06: Content.EndTime is used for the closing line when set; otherwise ComputeEndTime(start, testimony lines so far)"
  - "08-06: Memo variant: caption 'EXAMINATION OF', marker '(Exhibit N was marked...)', closing '(Whereupon the interview concluded at ...)', signature/certificate say 'examination'"
  - "08-06: An exhibit is marked before the first turn that references it (any ExhibitNumber > 0); index lists content.Exhibits with '--' for an exhibit never introduced"
  - "08-06: Unknown speaker ids get the colloquy label 'COUNSEL'"
metrics:
  duration: 8min
  completed: 2026-10-08
  tasks: 2
  files: 5
---

# Phase 8 Plan 06: Transcript Layout and Transcript .docx Builder Summary

This plan adds a deterministic transcript layout engine and a sibling .docx builder. The engine turns `DepositionContent` into 25-line pages of at most 62 characters each, and it records page:line citations for every exhibit marking and for the examination start. The builder renders those pages as a validator-clean Word document whose pagination matches the engine.

## What Was Built

- **`TranscriptLayout`** (pure, in `Services/Discovery/TranscriptLayout.cs`):
  - Turn formatting follows the federal format. Q/A turns are `"Q.   text"` and `"A.   text"`. Colloquy and objections are 9 spaces + `LABEL:` + 2 spaces + text. Parentheticals are 4 spaces + `(text)`, with continuation lines at column 6.
  - Speaker labels are `MS. QUIST` from the honorific and last name, `THE WITNESS`, `THE REPORTER` or `THE VIDEOGRAPHER`, or the upper-cased full name.
  - `Wrap` uses greedy wrapping, hard-splits oversized tokens and strips invalid XML characters. Its regexes are source-generated with a 1000 ms timeout.
  - Sections, each starting on a new page and padded to 25 lines:
    - Caption, truncated so it never overflows page 1. It shows the case number, DEPOSITION OF NAME, EXCERPT, "Pages 1 - N", long date, time, location and the reporter with CSR.
    - Appearances
    - Index: the examination line with dot leaders and its page, and exhibits with wrapped descriptions and the page where each was marked.
    - Testimony: videographer opening, oath, centered EXAMINATION, `BY MS. QUIST:`, turns with code-inserted exhibit markers, and the closing parenthetical.
    - Signature page
    - Errata sheet
    - Reporter's certificate
  - `ComputeEndTime` assumes 45 s per line, rounds up to 5 minutes and caps the session at 7 h (FRCP 30(d)(1)). `CountTestimonyLines` is there for the DepositionWriter overflow trim.
- **`TranscriptDocxBuilder`**:
  - Styles: DocDefaults and the `TranscriptLine` / `TranscriptHeading` styles use Courier New for all four font slots, at 24 half-points with exact 480 spacing.
  - Each page is one heading paragraph (heading, tab, page number, with `pageBreakBefore` from page 2 on) plus 25 line paragraphs (tab, line number, tab, text).
  - Section properties: Letter page; margins 1080/540/1080/1440 (top/right/bottom/left). Header and footer 540.
  - There is no `LineNumberType` and no field codes. Every text run goes through `StripInvalidXmlChars`.

## Verification

- `FullyQualifiedName~Transcript`: 33/33 passed (26 layout, 7 docx).
- `FullyQualifiedName~Tests.Discovery.`: 167/167 passed.
- The app and the test project both build cleanly with `-warnaserror`.
- Acceptance greps all passed:
  - Constants: `LinesPerPage = 25`, `MaxCharsPerLine = 62`.
  - Code: the marker text, `GeneratedRegex` with no `new Regex(`, `"Courier New"`, `LineSpacingRuleValues.Exact`, `PageBreakBefore`, `StripInvalidXmlChars`, `TranscriptLayout.Build`.
  - `LineNumberType`: 0 matches.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | 8e404a5 | test(08-06): add failing transcript layout tests and sample content |
| 1 | GREEN | 5429516 | feat(08-06): add TranscriptLayout 25-line page engine |
| 2 | RED | b28338b | test(08-06): add failing transcript docx builder tests |
| 2 | GREEN | b0aa824 | feat(08-06): add TranscriptDocxBuilder for deposition transcripts |

## TDD Gate Compliance

Both tasks have a `test(...)` commit (it fails to compile because the type is missing) followed by a `feat(...)` commit. No refactor commit was needed.

## Deviations from Plan

**1. [Minor] Style element subset.** The transcript paragraph styles emit name, basedOn (heading only), next, qFormat, pPr and rPr in CT_Style order. `uiPriority` is omitted. The validator is clean.

**2. [Minor] Additional exhibit-index wording.** If an exhibit in `content.Exhibits` is never introduced by a turn, its index page shows `--`. The plan does not define that case.

No other deviations. The plan ran as written.

## Known Stubs

None.

## Self-Check: PASSED

- All five created files exist.
- Commits 8e404a5, 5429516, b28338b and b0aa824 are present in `git log`.
