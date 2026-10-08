---
phase: 08-ancillary-discovery-documents
plan: 07
subsystem: discovery-written-docx
tags: [discovery, boilerplate, caption, docx, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - "08-01: WrittenDiscoveryContent, DiscoveryKind, DiscoveryVariant, DiscoveryVariants.Of"
  - "08-02: CounselDirectory, DiscoveryTestData, DocxTestHelper"
  - "08-03: CaseNumber.Label"
  - "08-04: WrittenDiscoveryPlanner.SetTitle (tests)"
provides:
  - "CoreDocumentAssembler.BuildCaption(doc, caseNumber, title) overload; the old BuildCaption(doc) delegates with the blank number"
  - "CaseGuideDocxExporter.WriteCaption / WriteSignatureBlock / BodyFormat / Compact are internal static"
  - "DiscoveryBoilerplate: CaptionText, TranscriptCaptionLines, ServiceDate, PreliminaryStatement, Definitions, Instructions, RequestLabel, SignatureText, CertificateText"
  - "DiscoveryDocxBuilder.Build(WrittenDiscoveryContent) -> .docx bytes"
affects: [08-generator, 08-12-deposition-content, 08-13]
tech-stack:
  added: []
  patterns:
    - "Written discovery reuses the case guide WordBuilder through using static CaseGuideDocxExporter (StorySoFarDocxBuilder precedent)"
    - "Federal text cites FRCP rule numbers; state text says '{State} Rules of Civil Procedure' with no numbers; memo variants cite nothing (D-20)"
key-files:
  created:
    - Services/Discovery/DiscoveryBoilerplate.cs
    - Services/Discovery/DiscoveryDocxBuilder.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryBoilerplateTests.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryDocxBuilderTests.cs
  modified:
    - Services/Case/CoreDocumentAssembler.cs
    - Services/Case/CaseGuideDocxExporter.cs
decisions:
  - "08-07: DiscoveryDocxBuilder numbers requests by position (1..n), not DiscoveryRequest.Number, so printed numbers are always consecutive"
  - "08-07: SignatureText prints a bar number only when the propounding partner is the core document's own counsel; a side with no firm and no core counsel signs 'By: /s/ Counsel of record' (no invented names)"
  - "08-07: Memo variants sign with Memo.From / Memo.Organization when the propounding party is the plaintiff side or has no outside partner"
  - "08-07: Petition terminology carries into the boilerplate (Petitioner/Respondent), matching WrittenDiscoveryPlanner.SetTitle"
  - "08-07: ServiceDate returns empty when neither FilingDate nor TimelineEnd parses"
metrics:
  duration: 25min
  completed: 2026-10-08
  tasks: 2
  files: 6
---

# Phase 8 Plan 07: Discovery Boilerplate and Written Discovery .docx Summary

Captions, definitions, instructions, signature blocks and certificates of service for RFP, ROG and RFA sets are now code text. Federal sets cite FRCP 26/33/34/36, state sets cite the "{State} Rules of Civil Procedure" with no rule numbers, and memo variants use subpoena-rider, CID or pre-suit wording. A `DiscoveryDocxBuilder` renders the stored content as a .docx that passes OpenXmlValidator.

## What Was Built

- **CoreDocumentAssembler.BuildCaption(doc, caseNumber, title):** this new overload holds the old caption body. `BuildCaption(doc)` now works out the core title (with the jury suffix) and delegates with `____________`, so the core document still shows the blank number (D-24). The existing assembler and exporter tests pass unchanged.
- **CaseGuideDocxExporter:** `WriteCaption`, `WriteSignatureBlock`, `BodyFormat` and `Compact` are now `internal static`. Their behavior is unchanged.
- **DiscoveryBoilerplate:**
  - `CaptionText`
    - Pleadings get the assembler overload.
    - Memos get a matter header: the organization (or the court name), `IN THE MATTER OF {TITLE}` and `{CaseNumber.Label} {number}`, followed by the title.
  - `TranscriptCaptionLines` returns the pleading caption without its title, or the memo matter header.
  - `ServiceDate` is the first business day on or after FilingDate + 45 days. Without a filing date it counts from TimelineEnd.
  - `PreliminaryStatement` covers federal, state and the three memo variants.
  - `Definitions`
    - Covers You/Your (responding party), Document (with FRCP 34(a)(1)(A) when federal), Communication, Concerning, Person, Identify and Relevant Period.
    - Adds one term for each ProjectName or ContractNumber value, using the value's label.
  - `Instructions`
    - Covers the relevant period, the 30-day response, native ESI with metadata, a privilege log and the duty to supplement.
    - RFA instructions say unanswered matters are deemed admitted.
    - ROG instructions say "Each interrogatory is a single question".
  - `RequestLabel` maps each kind and variant to its label.
  - `SignatureText` signs with the propounding firm's partner. It falls back to `CoreDocument.Counsel` for the plaintiff side, and memo variants sign with the memo author.
  - `CertificateText` names the responding party and its partner's email, or says "counsel of record" when there is no partner.
- **DiscoveryDocxBuilder.Build** writes, in order:
  - The caption. Civil sets use `WriteCaption`; memo variants use centered bold header lines plus a Heading1 title.
  - The preliminary statement, then DEFINITIONS and INSTRUCTIONS.
  - The requests heading and the numbered requests, as `{Label} {n}: ` plus the prefix and text.
  - `WriteSignatureBlock`, then CERTIFICATE OF SERVICE and the certificate text.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and `dotnet build ReelDiscovery.Tests -warnaserror`: 0 warnings, 0 errors.
- DiscoveryBoilerplateTests, DiscoveryDocxBuilderTests, CoreDocumentAssemblerTests and CaseGuideDocxExporterTests: 75/75 passed.
- `FullyQualifiedName~Tests.Discovery.`: 198/198 passed.
- Acceptance greps:
  - The overload signature appears once.
  - The two `internal static void Write...` signatures appear.
  - "deemed admitted" and "thirty (30) days" appear in DiscoveryBoilerplate.
  - `WriteCaption(word`, `WriteSignatureBlock(word` and `CERTIFICATE OF SERVICE` each appear once in the builder.
- The state-court test asserts `DoesNotMatch(@"Rule \d")` on the preliminary statement plus the instructions, for all three kinds. The memo tests also assert that no `U.S.C.` citation appears.

## Deviations from Plan

None in behavior. Small additions:
- `RequestsHeading` in the builder chooses the memo headings: DOCUMENTS TO BE PRODUCED, QUESTIONS, REQUESTS TO ADMIT and DOCUMENTS REQUESTED.
- The builder test for the RED commit had a wrong caption assertion (`"Case No.\t"`; WriteCaption puts the tab after `v.`). It was corrected in the GREEN commit.

## TDD Gate Compliance

- Task 1: RED afa200c (test) → GREEN 3331222 (feat)
- Task 2: RED 9887907 (test) → GREEN 24794ac (feat)
- No refactor commits.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | afa200c | test(08-07): add failing discovery boilerplate and caption tests |
| 1 GREEN | 3331222 | feat(08-07): add discovery caption overload and code-owned boilerplate |
| 2 RED | 9887907 | test(08-07): add failing written discovery docx builder tests |
| 2 GREEN | 24794ac | feat(08-07): add written discovery docx builder |

## Known Stubs

None. The builder renders only stored content; the generator that fills the request texts is a later plan.

## Self-Check: PASSED
