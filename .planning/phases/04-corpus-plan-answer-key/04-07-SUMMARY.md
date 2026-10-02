---
phase: 04-corpus-plan-answer-key
plan: 07
subsystem: corpus-protocol
tags: [corpus, review-protocol, prompts, docx, openxml]
requires:
  - 04-01 (ReviewProtocolProse / IssueProse models)
  - 04-02 (corpus fixtures, FakeLlmProvider.Respond)
provides:
  - CorpusPlanPrompts.SystemPrompt (shared byte-stable planning system prompt)
  - CorpusPlanPrompts.AppendCaseSynopsis / AppendCast / AppendValues / BuildProtocolPrompt / IsCounsel / RoleText
  - ReviewProtocolBuilder (GenerateProseAsync, FallbackProse, Build, FileNameFor, SchemaTypes)
  - CaseGuideDocxExporter.ParaFormat / WordBuilder / Cell now internal (reusable)
affects: [04-13, 04-14, 04-15, 04-16, 04-18, 04-20, 04-22]
tech-stack:
  added: []
  patterns: [structure-in-code plus one prose call, fatal-rethrow / non-fatal fallback, DocID token stripping on model prose]
key-files:
  created:
    - Services/Corpus/CorpusPlanPrompts.cs
    - Services/Corpus/ReviewProtocolBuilder.cs
    - ReelDiscovery.Tests/Corpus/CorpusPromptInvariantTests.cs
    - ReelDiscovery.Tests/Corpus/ReviewProtocolTests.cs
  modified:
    - Services/Case/CaseGuideDocxExporter.cs
decisions:
  - "[04-07]: Protocol prose with any blank top-level section (purpose, background, hot, privilege) is replaced entirely by FallbackProse; an issue missing from the response (or with no relevant examples) gets the criteria-only fallback example while the rest of the model prose is kept (IsFallback false)"
  - "[04-07]: Protocol issue ids match guide ids by trim + case-insensitive compare; unknown ids are dropped; first entry per issue wins"
  - "[04-07]: DocID tokens ([A-Z]{2,6}-\\d{6} with optional .0000 attachment suffix) are stripped from all model prose before it reaches the protocol"
  - "[04-07]: CorpusPlanPrompts.AppendCast role text is Party / In-house counsel / Outside counsel / Staff, with ', background' for Background-tier members"
metrics:
  duration: 14min
  completed: 2026-10-02
  tasks: 2
  files: 5
---

# Phase 4 Plan 07: Review Protocol and Shared Planning Prompt Summary

This plan adds the shared corpus-planning system prompt and the review protocol (coding manual) .docx. Code builds the protocol's structure from the case guide, and one Story-tier call writes the prose. If that call fails, criteria-only fallback prose is used and flagged. The prompt and the document carry no plan data, DocIDs, counts or answer-key fields.

## What Was Built

- **CaseGuideDocxExporter**: `ParaFormat`, `WordBuilder` and `Cell` changed from `private` to `internal sealed` (still nested). Nothing else changed, and the exporter tests stay green.
- **CorpusPlanPrompts** (`Services/Corpus/CorpusPlanPrompts.cs`):
  - `SystemPrompt` const, worded exactly as the plan gives it. It keeps names as given (including names from films, TV, books or games), takes a firm position ("Do not hedge"), asks for plain business language with no legal terms, and has no "fictional" and no JSON wording.
  - `AppendCaseSynopsis`: the case title, the core document type and title, and one line per issue: `ISS-01 Title: RelevanceCriteria`.
  - `AppendCast(sb, guide, onlyIds?)`: one line per member, e.g. `CH-001 Maria Delgado, President, Brightline Glazing, LLC (Party)`.
  - `AppendValues`: one line per value, `{Label} = {Value}`.
  - `BuildProtocolPrompt(draft)`: `AppendNameRules`, the synopsis, parties, key people (no counsel), counsel, law firms with the clients they represent, then the fixed task text. It never takes plan data.
- **ReviewProtocolBuilder** (`Services/Corpus/ReviewProtocolBuilder.cs`):
  - `GenerateProseAsync` makes one call: `LlmTier.Story`, `"Corpus Plan - Review protocol"`, 8,000 max output tokens, shared system prompt. On error:
    - OperationCanceledException propagates.
    - A fatal LlmCallException (Auth or SpendLimit) is rethrown.
    - Any other LlmCallException, or blank sections, returns `FallbackProse`.
  - `FallbackProse(draft)`: purpose, background, hot guidance and privilege guidance are built from the case title, issue titles, parties, counsel and law firms. Each issue gets one relevant example taken from its RelevanceCriteria and an empty not-relevant list.
  - `Build(draft, prose)`: portrait .docx titled "Document Review Protocol". It has the case-title subtitle and a "Privileged & Confidential — Attorney Work Product" line, then nine numbered sections:
    1. Purpose and Scope
    2. Matter Background
    3. Parties and Key People (table of Key-tier cast, counsel excluded)
    4. Review Fields and Decision Rules (Relevance, Issue Codes, Hot, Privileged)
    5. Issue Codes (RelevanceCriteria verbatim, example bullets)
    6. Hot Documents
    7. Privilege (counsel table plus guidance)
    8. Families and Threads
    9. Questions and Escalation
  - `FileNameFor(folder)` returns `{folder} - Review Protocol.docx`.
  - `SchemaTypes` returns `[ReviewProtocolProseDto]`. The DTO is strict, has no nullable members and stays within the 16-union limit.

## Verification

- `dotnet test --filter "ReviewProtocolTests|CorpusPromptInvariantTests"`: 20 passed.
- Full suite excluding Render tests (`Category!=Render`): 1,576 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- Acceptance greps: `internal sealed class` WordBuilder, ParaFormat and Cell = 1 each; "fictional" in CorpusPlanPrompts.cs = 0; the films/TV phrase = 1; the Build signature = 1; `LlmTier.Story` = 1; the operation name = 1; "near-miss"/"decoy" = 0; GeneratedRegex = 2; `CaseGuideDocxExporter.WordBuilder` key link present.

## TDD Gate Compliance

- Task 1: RED `53672a8` (test), GREEN `99614fd` (feat).
- Task 2: RED `3e007ea` (test), GREEN `3cab6e4` (feat).
- No refactor commits were needed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] "Family" field name was missing from the protocol text**
- **Found during:** Task 2 (GREEN)
- **Issue:** The plan's fixed family text uses lowercase "family" and the heading says "Families", so the required field name "Family" was not in the document.
- **Fix:** The family paragraph now opens with a bold "Family rule: " lead.
- **Files modified:** Services/Corpus/ReviewProtocolBuilder.cs
- **Commit:** 3cab6e4

### Clarifications (no behavior beyond the plan)

- The plan only says "empty fields -> FallbackProse". Here, a blank top-level section replaces everything with the fallback. If a single issue is missing from the response, only that issue gets the criteria-only example, so good model prose for the other issues is not thrown away.
- Section 9 (Questions and Escalation) uses fixed text written here, because the plan named the section but gave no wording.

## Requirements

- KEY-05 is not marked complete. This plan delivers the protocol bytes; writing the file at Approve and the pipeline wiring come in 04-15, 04-18, 04-20 and 04-22.
- PLAT-03 stays Partial. This plan adds protocol and prompt tests; other Phase 4 plans still have to deliver theirs.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Corpus/CorpusPlanPrompts.cs
- FOUND: Services/Corpus/ReviewProtocolBuilder.cs
- FOUND: ReelDiscovery.Tests/Corpus/CorpusPromptInvariantTests.cs
- FOUND: ReelDiscovery.Tests/Corpus/ReviewProtocolTests.cs
- FOUND commits: 53672a8, 99614fd, 3e007ea, 3cab6e4
