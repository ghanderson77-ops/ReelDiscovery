---
phase: 02-case-scenario-core-document
plan: 06
subsystem: case-core-document-generation
tags: [llm, gateway, core-document, outline, sections, citations, tests]
requires:
  - "02-01 Models/Case (CaseDraft, CoreDocument, ComplexityPreset), CaseTestData"
  - "02-02 CitationScanner"
  - "02-03 DocumentTypeResolver, CoreDocumentTemplates, CoreDocumentAssembler"
  - "02-05 RealEntityDetector.SchemaTypes"
provides:
  - "Services/Case/CoreDocumentGenerator (GenerateOutlineAsync, GenerateAsync, GenerateSectionsAsync, RegenerateSectionAsync)"
  - "Services/Case/CoreDocumentGenerationResult (SectionsWritten, SectionsFailed, FatalError, IsFatal)"
  - "ReelDiscovery.Tests/Case/CaseSchemaTests (strict-schema theory over all Phase 2 DTOs)"
affects: [02-08, 02-09, 03]
tech-stack:
  added: []
  patterns:
    - "Outline-first sectioned generation: one Story-tier outline call, then one call per template section"
    - "Content-level citation retry (exactly one) on top of gateway transport policy"
    - "EmailGenerator-style catch order (user cancel, fatal stop, timeout, fatal LlmCallException, non-fatal LlmCallException, Exception)"
key-files:
  created:
    - Services/Case/CoreDocumentGenerator.cs
    - ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs
    - ReelDiscovery.Tests/Case/CaseSchemaTests.cs
  modified: []
decisions:
  - "[02-06]: One linked runCts covers all phases of GenerateSectionsAsync (not only the parallel phase), so a fatal Auth/SpendLimit in any section cancels in-flight count calls and stops later sections uniformly"
  - "[02-06]: GenerateSectionsAsync does not pass the section's old text when retrying a Failed/Pending section; only RegenerateSectionAsync sends currentText and guidance"
  - "[02-06]: Section prompts include the fact-plan period/focus for fact sections and the count's title/parties/basis for counts and findings, beyond the plan's minimum instruction lines"
metrics:
  duration: 12min
  completed: 2026-10-01
---

# Phase 2 Plan 06: CoreDocumentGenerator Summary

`CoreDocumentGenerator` writes the core document outline-first. A single Story-tier call ("Core Document - Outline", 16,000 max tokens) fixes the matter kind, court, parties, counts or issues, timeline, fact subsections and counsel or memo header. The prompt uses the complexity preset's ranges, the user's document, court and state preferences, and the accepted fictional substitutes. The outline is then mapped to a `CoreDocument`: the type comes from `DocumentTypeResolver`, the sections come from `CoreDocumentTemplates` (all Pending) and the counsel phone is drawn from the fictional (555) 555-01xx range. After that, each section is written by its own call with a 12,000-token limit. Lead sections run in order, counts and findings run up to 3 at a time, then the tail sections, and the memo executive summary comes last. If a section cites a court decision, it is retried exactly once. Refused or truncated sections are marked Failed while the others finish. A fatal Auth or SpendLimit failure stops all further calls and keeps the sections already written. `RegenerateSectionAsync` rewrites one section in place, using the other sections' current (edited) text plus optional guidance.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | Outline call, DTOs, prompts, mapping, CaseSchemaTests | ec99ff2 (RED), b241fce (GREEN) | Services/Case/CoreDocumentGenerator.cs, ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs, ReelDiscovery.Tests/Case/CaseSchemaTests.cs |
| 2 | Section generation, citation retry, outcome handling, RegenerateSectionAsync | cbb4d49 (RED), 3a025c6 (GREEN) | Services/Case/CoreDocumentGenerator.cs, ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx --no-incremental`: 0 warnings, 0 errors
- `dotnet test ReelDiscovery.slnx`: 415 passed, 0 failed (28 CoreDocumentGenerator facts and a 3-case CaseSchemaTests theory are new)
- Acceptance greps all satisfied: `OperationName = "Core Document - Outline"` = 1, `DocumentTypeResolver.Resolve` = 1, `555-01` >= 1, no JSON wording outside serializer attributes = 0, `string?` = 0, `SectionMaxOutputTokens = 12_000` = 1, `new SemaphoreSlim(MaxParallelSections, MaxParallelSections)` = 1, `CitationScanner.HasErrors` = 1, both catch filters present, `new Random(` = 0, `named individuals` = 3, `MemoParagraphsMin` = 1, `SectionsWritten > 0` = 1

## Deviations from Plan

### Additions (non-breaking)
- Added tests beyond the behavior list: `Outline_PromptPrintsEqualBounds`, `Generate_ReportsProgress` and `GenerateSections_NoDocument_Throws`.
- The `InvestigationMemo` document preference is shown as "Investigation Memo" in the outline prompt.
- `GenerateSectionsAsync` throws `InvalidOperationException("Generate the outline before writing sections.")` when the draft has no core document.
- Section prompts add each fact section's period and focus, and each count's or finding's title, opposing parties and legal basis, so section calls stay anchored to the outline.

Otherwise the plan was executed exactly as written.

## TDD Gate Compliance

Each task has a `test(02-06)` RED commit (a compile failure) followed by a `feat(02-06)` GREEN commit. No refactor commits were needed.

## Known Stubs

None.

## Threat Flags

None. All new surface is covered by T-02-22..T-02-27.

## Self-Check: PASSED
