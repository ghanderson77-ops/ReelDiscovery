---
phase: 08-ancillary-discovery-documents
plan: 02
subsystem: discovery-counsel
tags: [discovery, counsel, fixtures, docx, ANC-01, ANC-02]
requires: []
provides:
  - "CounselDirectory: IsPlaintiffSide, PartyOf, PartyOrganizationIds, FirmFor, Partner, Associate, FirstPlaintiffSide, FirstDefenseSide (null-safe)"
  - "DiscoveryTestData: LitigationDraft, StateCourtDraft, MemoCriminalDraft, MemoRegulatoryDraft, MemoCivilDraft, NoGuideDraft, MissingDefenseFirmDraft, WitnessIds"
  - "DocxTestHelper: OpenAndValidate (errors + paragraph texts), ParagraphText, ErrorsText"
affects: [08-planners, 08-builders, 08-validator, 08-generator]
tech-stack:
  added: []
  patterns:
    - "Firm lookup mirrors CaseGuideGenerator.CounselGaps: LawFirm org whose RepresentsOrgIds include the party's own Party/Individual organizations; Partner/Associate by OutsideCounsel title (OrdinalIgnoreCase)"
    - "Phase 8 tests build drafts from DiscoveryTestData (fresh graph per call) and validate .docx through DocxTestHelper"
key-files:
  created:
    - Services/Discovery/CounselDirectory.cs
    - ReelDiscovery.Tests/Discovery/CounselDirectoryTests.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryTestData.cs
    - ReelDiscovery.Tests/Discovery/DocxTestHelper.cs
  modified: []
decisions:
  - "08-02: CounselDirectory.PartyOf returns null when the draft has no core document (parties live there), even if the guide resolves an organization"
  - "08-02: LitigationDraft adds defense witness CH-013 Michael Scott rather than renaming an existing character; five key non-counsel witnesses with distinct KnownByIds fact counts (CH-003 7, CH-004 6, CH-001 5, CH-002 4, CH-013 3)"
  - "08-02: LitigationDraft adds counts 2 and 3 (Unjust Enrichment, Prompt Payment Act) to the core document so its three issues each map to a count"
  - "08-02: MemoCriminalDraft adds party-3 United States of America (Complainant) and a defense firm ORG-04 Calloway & Marsh LLP representing Desert Bloom; Lane is an individual party placed at Desert Bloom (PartyId party-1), so FirmFor(party-1) is null"
metrics:
  duration: 12min
  completed: 2026-10-08
  tasks: 2
  files: 4
---

# Phase 8 Plan 02: Counsel Directory and Shared Fixtures Summary

`CounselDirectory` resolves a character's party, the outside firm for a party, and that firm's partner and associate using the CounselGaps rule. It returns null instead of throwing. The plan also adds shared Phase 8 drafts (federal, state, three memo kinds, no guide, missing defense firm) and an OpenXmlValidator helper for .docx tests.

## What Was Built

- **Services/Discovery/CounselDirectory.cs**: a static class.
  - `IsPlaintiffSide` re-implements the private caption grouping in CoreDocumentAssembler.
  - `PartyOf` checks the member's PartyId first, then the organization's PartyId.
  - `PartyOrganizationIds` returns the party's Party/Individual orgs.
  - `FirmFor` returns the first LawFirm whose RepresentsOrgIds intersect the party's orgs.
  - `Partner` and `Associate` find OutsideCounsel at that firm by title word.
  - `FirstPlaintiffSide` and `FirstDefenseSide` give the D-16 defaults (Defendant/Subject first, then ThirdParty).
- **DiscoveryTestData**:
  - `LitigationDraft` (from SampleCounselDraft) has:
    - Standard complexity and a federal Complaint filed 2025-02-14.
    - 3 issues and 10 facts dated 2024, with Hot, Key and Supporting facts.
    - Firms ORG-03 and ORG-04, each with a partner and an associate.
    - Values for Amount ($184,250.00 and $487,250.00), ContractNumber SC-LT-2024-031, CO-007 and INV-2024-118.
    - A single substitution, Acme Corporation -> Apex Fabrication Co.
  - Derived drafts: `StateCourtDraft` (Texas, Harris County, Petition), `NoGuideDraft` and `MissingDefenseFirmDraft` (ORG-04, CH-011 and CH-012 removed).
  - `MemoCriminalDraft` has 2 issues, 5 facts, 4 key witnesses, an Amount value and a defense firm; `MemoRegulatoryDraft` and `MemoCivilDraft` derive from it.
- **DocxTestHelper**: `OpenAndValidate` returns the validator errors and the paragraph texts. It also provides `ParagraphText` and `ErrorsText`.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | CounselDirectory (TDD) | 90f50a3 (RED), b3db9d2 (GREEN) |
| 2 | Shared fixtures and docx helper | b8e93d4 |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and `dotnet build ReelDiscovery.Tests -warnaserror`: 0 warnings, 0 errors
- `--filter "FullyQualifiedName~Tests.Discovery."`: 47 passed (11 CounselDirectoryTests plus the 4 fixture tests among them)
- Acceptance greps: 7 `public static CaseDraft`, 1 `OpenXmlValidator`, 0 "fictional", "Michael"/"Scott" present

## Deviations from Plan

None in behavior. Small additions:
- The plan asked for 3 new tests. Added a fourth, `Fixtures_CoverStateCourtMemosAndNoGuide`, which pins the state, memo and no-guide fixture contracts.
- The plan's "four witnesses" became five, because Michael Scott was added as a new defense witness. All five have distinct fact counts.

## TDD Gate Compliance

RED `test(08-02)` 90f50a3 came before GREEN `feat(08-02)` b3db9d2. No refactor was needed.

## Self-Check: PASSED
