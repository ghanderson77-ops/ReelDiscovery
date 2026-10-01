---
phase: 02-case-scenario-core-document
plan: 05
subsystem: case-real-name-check
tags: [llm, gateway, real-names, substitution, tests]
requires:
  - "02-01 Models/Case/EntitySubstitution, CaseTestData"
provides:
  - "Services/Case/RealEntityDetector (DetectAsync -> List<EntitySubstitution>)"
affects: [02-07]
tech-stack:
  added: []
  patterns: ["Story-tier gateway call with constant system prompt and strict nested DTOs (StorylineGenerator analog)"]
key-files:
  created:
    - Services/Case/RealEntityDetector.cs
    - ReelDiscovery.Tests/Case/RealEntityDetectorTests.cs
  modified: []
decisions:
  - "[02-05]: RealEntityDetector drops blank/duplicate names (first wins, case-insensitive) and cleans aliases in code; LlmCallException is not caught (UI boundary handles it)"
metrics:
  duration: 5min
  completed: 2026-10-01
---

# Phase 2 Plan 05: RealEntityDetector Summary

`RealEntityDetector.DetectAsync` makes a single Story-tier call ("Real Name Check", MaxOutputTokens 4,000). It returns the real people, companies and brands named in the scenario, plus characters and organizations from fictional works, as `EntitySubstitution` entries. Each entry has aliases, a reason and a suggested fictional substitute, and `Apply` defaults to true. The system prompt is a constant: it excludes places, agencies, courts and statutes (A3) and never mentions the output format. The scenario appears only in the user prompt (T-02-17). A refusal or failure throws `LlmCallException` through `GetValueOrThrow` (T-02-21).

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | RealEntityDetector service with strict DTOs | 988ba9e (RED), 9f79b57 (GREEN) | Services/Case/RealEntityDetector.cs, ReelDiscovery.Tests/Case/RealEntityDetectorTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx --no-incremental`: 0 warnings, 0 errors
- `dotnet test ReelDiscovery.slnx`: 337 passed, 0 failed (11 new RealEntityDetector tests: 10 facts, one of them a 2-case theory)
- Acceptance greps: `LlmTier.Story` = 1, `OperationName = "Real Name Check"` = 1, `SchemaTypes` = 1, no JSON wording outside serializer attributes = 0, `string?` = 0

## Deviations from Plan

### Additions (non-breaking)
- `SchemaTypes_AreStrict` validates `EntityResponseDto` with `StrictSchemaValidator`.
- The blank-scenario test is a theory that covers both the empty string and whitespace.

Otherwise the plan was executed exactly as written.

## TDD Gate Compliance

The `test(02-05)` RED commit (a compile failure) is followed by the `feat(02-05)` GREEN commit. No refactor was needed.

## Known Stubs

None.

## Self-Check: PASSED
