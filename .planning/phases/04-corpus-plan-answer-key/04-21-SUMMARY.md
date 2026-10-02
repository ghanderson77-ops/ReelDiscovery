---
phase: 04-corpus-plan-answer-key
plan: 21
subsystem: case-model
tags: [case-stage, staleness, hash, wizard]
requires: []
provides:
  - "CaseStage { Described, CoreDocument, CaseGuide, CorpusSettings, ReviewPlan, Summary }"
  - "Content-only CaseGuide.ComputeHash() for corpus plan basis staleness"
affects: [04-04, 04-15, 04-16, 04-17, 04-18, 04-19, 04-22]
tech-stack:
  added: []
  patterns: ["Anonymous content projection hashed via CaseDraft.ComputeHash(JsonSerializer.Serialize(new {...}, CaseDraft.JsonOptions)) (GuideStaleness style)"]
key-files:
  created: []
  modified:
    - Models/Case/CaseStage.cs
    - Models/Case/CaseGuide.cs
    - ReelDiscovery.Tests/Case/CaseGuideModelTests.cs
decisions:
  - "[04-21]: CaseGuide.ComputeHash excludes EmailIsUserEdited and KeyFact.SourceSectionId along with part states, review flags, IsUserEdited, DetailsPending, MappingNotes, IdCounters and DismissedStaleKey"
metrics:
  duration: 6min
  completed: 2026-10-02
  tasks: 1
  files: 3
---

# Phase 4 Plan 21: Corpus Stages and Content-Only Guide Hash Summary

Adds the CorpusSettings and ReviewPlan wizard stages (stored by name, forward-only) and makes `CaseGuide.ComputeHash()` hash only guide content, so review/UI actions never make a corpus plan stale but real content edits always do.

## What Was Built

- **CaseStage**: `CorpusSettings, ReviewPlan` inserted between `CaseGuide` and `Summary`. JSON stores names (JsonStringEnumConverter), so existing `"Stage": "Summary"` files still load; `AdvanceStage` stays forward-only by ordinal. WizardForm/CaseGuideUiText `_ =>` fallbacks compile unchanged (mapping lands in 04-19).
- **CaseGuide.ComputeHash()**: same name and signature; now serializes an anonymous projection of Issues, Organizations, Cast, Facts and Values content fields (per the plan's field list), null-safe (null lists become empty, null entries skipped). Comment `// Content only (RESEARCH Pitfall 1): ...` marks the intent.
- **Tests** (CaseGuideModelTests): `ComputeHash_IgnoresUiState` (20 UI/review mutations), `ComputeHash_ChangesOnContent` (21 content mutations), `ComputeHash_NullListsAndEntries_DoNotThrow`, `CaseStage_OrderAndAdvance`, `CaseStage_StoredByName_SummaryStillLoads`.

## Verification

- Focused filter (CaseGuideModelTests, GuideStalenessTests, WizardFlowTests, CaseDraftTests): 55/55 passed.
- Wave gate `--filter "Category!=Render"`: 1393/1393 passed, no build warnings.

## TDD Gate Compliance

- RED: `14025c0` test(04-21) (failed to compile: missing stages)
- GREEN: `4be8823` feat(04-21)
- No refactor needed.

## Deviations from Plan

None - plan executed exactly as written. (`CastMember.EmailIsUserEdited` and `KeyFact.SourceSectionId` were not in the plan's include list, so they are excluded from the hash. Tests pin that.)

## Requirements

PLAN-07 and PLAT-03 are contributed to but not fully delivered by this plan; left Pending in REQUIREMENTS.md.

## Self-Check: PASSED

- FOUND: Models/Case/CaseStage.cs, Models/Case/CaseGuide.cs, ReelDiscovery.Tests/Case/CaseGuideModelTests.cs
- FOUND: 14025c0, 4be8823
