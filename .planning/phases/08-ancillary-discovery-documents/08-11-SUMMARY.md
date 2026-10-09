---
phase: 08-ancillary-discovery-documents
plan: 11
subsystem: discovery-prompts
tags: [discovery, prompts, strict-schema, impeachment, ANC-01, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-03
    provides: "DepositionPlan / DepositionChunk / WitnessCandidate (Side) / KnownFacts, KnownValues, KnownLedger / Exhibits (IsImpeachment)"
  - phase: 08-04
    provides: "WrittenSetPlan / RequestSlot (IssueIds, FactId, DocSubject, IsGenuineness, IsPersonsWithKnowledge, CustodianIds, ValueIds, FromDate, ToDate, Prefix)"
  - phase: 08-08
    provides: "DiscoveryValidator.FeedbackText / FeedbackHeader"
provides:
  - "DiscoveryPrompts.BuildSystemPrompt(draft): byte-stable, structured-data-only system prompt (no facts, no scenario, no complaint text)"
  - "DiscoveryPrompts.BuildDepositionPrompt(draft, plan, chunk, previousTurns, coveredTopics, guidance, feedback)"
  - "DiscoveryPrompts.BuildWrittenPrompt(draft, plan, slots, guidance, feedback)"
  - "DiscoveryPrompts.ImpeachmentRule const; OperationDeposition / OperationWritten / MaxOutputTokens 16,000 / MaxGuidanceLength 1,000 / PreviousTurnsInPrompt 6"
  - "Strict DTOs DepositionChunkDto (SpeakerDto, HonorificKind, TurnDto) and RequestSetDto (RequestDto); DiscoveryPrompts.SchemaTypes registered in CaseSchemaTests"
affects: [08-12, 08-13, 08-14]
tech-stack:
  added: []
  patterns:
    - "Labeled pipe-separated data sections sorted by id for a cacheable system prompt; per-witness facts only in the user prompt"
    - "Impeachment rule gated on adverse (Plaintiff/Defense) witness + assigned exhibits, impeachment exhibit listed first"
key-files:
  created:
    - Services/Discovery/DiscoveryPrompts.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryPromptInvariantTests.cs
  modified:
    - ReelDiscovery.Tests/Case/CaseSchemaTests.cs
key-decisions:
  - "08-11: Honorific instruction is given on every topic's first part (Part <= 1); the opening chunk (Index 0) also says to start with the witness's background and role"
  - "08-11: Previous turns (last 6) and covered topics are emitted whenever supplied, so a new topic's first part can also continue the transcript"
  - "08-11: Written slots render as 'Slot n | fact F-x or issues ... | prefix or (none) | anchors' with custodian names, values, long-form date range, fact date/title, Amount values linked to the fact and actor names"
  - "08-11: Replacement names section lists only applied substitutes ('Use the name X'); originals never appear in any prompt"
duration: 15min
completed: 2026-10-08
---

# Phase 8 Plan 11: Discovery Prompts and Strict DTOs Summary

`DiscoveryPrompts` now builds a byte-stable system prompt from structured guide data, a knowledge-gated deposition chunk prompt with an impeachment rule for adverse witnesses, and a slot-anchored written-set prompt. It also defines strict `DepositionChunkDto` / `RequestSetDto` schemas, which pass the strict validator for both providers.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | DiscoveryPrompts: system prompt, user prompts and DTOs (TDD) | 70f472f (RED), 76ed79e (GREEN) | Services/Discovery/DiscoveryPrompts.cs, ReelDiscovery.Tests/Discovery/DiscoveryPromptInvariantTests.cs |
| 2 | Register discovery DTOs in CaseSchemaTests | cf76eff | ReelDiscovery.Tests/Case/CaseSchemaTests.cs |

## Verification

- `DiscoveryPromptInvariantTests`: 17/17 pass. These cover a byte-stable system prompt, no "fictional" or "json" text in any prompt across the civil case and the three memo variants, no core-document paragraph over 60 characters and no scenario text in any prompt, substitute names present and originals absent, only facts the witness knows, date/part/turn target/exhibits/speakers, honorifics, the previous-turn window, impeachment present and absent, the RFA/ROG/RFP slot anchors, guidance truncation and feedback after the header.
- `CaseSchemaTests` plus the invariant tests: 47/47 pass, including the new rows `DiscoveryPrompts`/`DepositionChunkDto` and `DiscoveryPrompts`/`RequestSetDto`.
- App and test project `-warnaserror` builds are green.
- Acceptance greps: fictional 0, json outside attributes/usings 0, AppendNameRules 0, Scenario/Sections 0, the films/TV clause 1, `public const string ImpeachmentRule` 1.

## Deviations from Plan

None. The plan was executed as written.

## TDD Gate Compliance

The RED commit `test(08-11)` 70f472f fails to compile because `DiscoveryPrompts` does not exist yet. The GREEN commit `feat(08-11)` 76ed79e followed it. No refactor commit was needed.

## Self-Check: PASSED
