---
phase: 03-case-guide
plan: 10
subsystem: case-guide-generation
tags: [llm, story-tier, strict-schema, facts, canonical-values, orchestration, parallel, tdd]
requires:
  - "03-01: CoreDocument.FactPlan, KeyFact, CanonicalValue, ComplexityPreset.FactsMin/FactsMax, GuidePartState.CompletedUnits"
  - "03-07: GuideStaleness.Order/Stamp"
  - "03-08: CanonicalValueScanner.Scan"
  - "03-09: CaseGuideGenerator (issues, cast, RunUnitAsync, catch ladder), CaseGuidePrompts"
provides:
  - "CaseGuideGenerator.GenerateFactsAsync, PlanFactCalls, GenerateValuesAsync, GenerateAsync"
  - "CaseGuideGenerationResult"
  - "CaseGuidePrompts.BuildFactsPrompt, BuildValuesPrompt (existing + keep-exactly lists ready for 03-11)"
affects: [03-11, 03-13, 03-14]
tech-stack:
  added: []
  patterns:
    - "Fact units = fact sections (split into equal date slices above 30 facts); CompletedUnits keys are the section id or '{id}#{k}'"
    - "GenerateAsync awaits partCompleted in a finally block: after each finished or failed part, and after a cancelled part only when draft.Revision changed"
key-files:
  created:
    - .planning/phases/03-case-guide/03-10-SUMMARY.md
  modified:
    - Services/Case/CaseGuideGenerator.cs
    - Services/Case/CaseGuidePrompts.cs
    - ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs
decisions:
  - "[03-10]: Fact target = midpoint of FactsMin..FactsMax spread by TargetParagraphs (largest remainder, min 2); sections above 30 split into ceil(n/30) equal date slices; unparseable dates -> one call capped at 30"
  - "[03-10]: Fact actors/knownBy resolve by CH id or exact full name; unknown refs dropped with fact MappingNotes; KnownByIds = ActorIds union knownBy; bad dates kept with a note and sorted last"
  - "[03-10]: Facts failure message: first unit error, prefixed '{n} periods were not written: ' when n > 1; retry labels 'Key Facts' and 'Canonical Values'"
  - "[03-10]: GenerateFactsAsync/GenerateValuesAsync are first-generation/resume paths (no existing entries sent); regeneration with existingId is 03-11"
  - "[03-10]: GenerateAsync does not set Generating itself (part methods do); a fatal Issues details failure still records Issues as written, then stops"
  - "[03-10]: Values duplicates (label + value, case-insensitive) merge FactIds; hand-edited values are never changed"
metrics:
  duration: 8min
  completed: 2026-10-01
---

# Phase 3 Plan 10: Key Facts, Canonical Values and GenerateAsync Summary

The generator now writes the dated key-facts timeline with one Story call per core-document fact section, at most 3 at a time. It writes the canonical values registry in a single call seeded with the amounts and identifiers the scanner finds. A single `GenerateAsync` call runs the whole guide in order (issues, cast, facts, values) and gives the caller an autosave hook after each part.

## What was built

- **PlanFactCalls**: The total fact target is the midpoint of the preset's range (Simple 16, Standard 40, Complex 90). It is split across the fact sections by `TargetParagraphs` using largest remainder, with at least 2 facts per section. A section that needs more than `MaxFactsPerCall = 30` facts is split into `ceil(n/30)` calls over equal date slices; for example, a 60-fact section spanning 2024 becomes 2024-01-01..07-01 and 07-02..12-31. A section with unparseable dates gets one call capped at 30.
- **BuildFactsPrompt**: The prompt contains the counts or findings summaries, the section heading with its date range, focus and paragraphs, and every issue. The cast is listed one line per person as `CH-001 | name | title | org | active from to to`. It asks for "Write about {n} facts dated {from} to {to}", includes the D-02 line "State each fact as what happened...", and ends with the name rules and guidance. Existing facts appear under "Existing facts (revise by existingId...)" and hand-edited ones under "Keep exactly; do not return these:".
- **GenerateFactsAsync**: Requires a Ready cast; otherwise it returns "Write the cast before the key facts." without sending a request. Units run through the shared `RunUnitAsync` (semaphore plus linked CTS). The merge happens once, on the calling thread, after `Task.WhenAll` inside a `finally`. It normalizes issue and cast IDs, checks they exist, falls back to an exact full-name match for cast, unions knownBy with the actors, and records notes for dropped references and bad dates. It sets `SourceSectionId` and allocates F-ids in date order (then unit, then response order), keeps `guide.Facts` sorted, and records finished units in CompletedUnits.
  - When every unit is finished, the part is Ready: it is stamped and CompletedUnits is cleared.
  - When some units failed, the part is Failed with the first error.
  - On a fatal error the part goes back to Pending and the facts already merged are kept.
  - On a user cancel, finished units are merged, the part is set to Pending and the cancel is rethrown.
- **BuildValuesPrompt / GenerateValuesAsync**: The prompt lists every fact (`F-001 | date | title | description`), the text of the fact sections, and the scanner hits in a "register each one exactly as written: ..." line. It also includes the registration instruction, the list of kinds, and the existing and keep-exactly lists. The call is "Case Guide - Canonical values" with 12,000 max tokens. Mapping trims values, normalizes fact IDs and checks they exist (dropped IDs become MappingNotes), honours `existingId` only for a valid value that is not hand-edited and not already used in this response, merges duplicates (same label and value, case-insensitive), and allocates V-ids. The part requires facts; otherwise it returns "Write the key facts before the canonical values.". It uses the same catch ladder.
- **GenerateAsync**: Goes through `GuideStaleness.Order` and skips parts that are Ready (Issues counts as Ready even when its details failed). It stops when an earlier part is not Ready. Notes from every part are collected. After each part, `partCompleted` is awaited, including after a user cancel that left partial results. The run stops at the first failed part, recording PartsFailed, FailureMessage, and RefusedPart/RefusalMessage for refusals. It also stops on a fatal error. In `finally` it bumps `draft.Revision` when any part was written.
- `CastRun` was renamed `UnitRun` and now carries the retry label, so timeout and outcome messages name the right part: "Click Retry Key Facts." for facts.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 96c0baa | test(03-10): add failing key facts generation tests |
| 1 GREEN | 40151bc | feat(03-10): generate key facts per fact section with code-checked links |
| 2 RED | 5758e67 | test(03-10): add failing canonical values and GenerateAsync tests |
| 2 GREEN | 359a21a | feat(03-10): add canonical values and GenerateAsync orchestration |

## Verification

- `dotnet test --filter "FullyQualifiedName~CaseGuideGeneratorTests|FullyQualifiedName~CaseSchemaTests"`: 66 passed. FactBatchDto and ValuesDto are strict and have no more than 16 union types.
- Full suite `--filter "Category!=Render"`: 946 passed, 0 failed.
- `dotnet build`: 0 warnings, 0 errors.
- Acceptance greps:
  - `MaxFactsPerCall = 30`: 1
  - `typeof(FactBatchDto)`: 1
  - `typeof(ValuesDto)`: 1
  - `GenerateAsync(` signature: 1
  - `CanonicalValueScanner.Scan`: 1
  - `partCompleted`: 4
  - "State each fact as what happened": 1
  - "Keep exactly; do not return these": 1
- Facts_ParallelBound uses the existing `SlowProvider`, because the synchronous FakeLlmProvider cannot overlap calls. Six sections give a peak in-flight count between 2 and 3.

## Deviations from Plan

### Auto-fixed / adjusted

**1. [Rule 3 - Blocking] Generalized the 03-09 unit runner**
- **Issue:** The `RunUnitAsync` failure text was hard-coded to "Click Retry Cast."
- **Fix:** `CastRun` became `UnitRun` with a `Label` ("Cast", "Key Facts"), which is used by the timeout message and by `DescribeOutcome`. The cast behaviour is unchanged and all cast tests still pass.
- **Commit:** 40151bc

**2. [Plan detail] GenerateAsync does not set the Generating state itself**
- The part methods set Generating themselves. A precondition failure (no cast or no facts) returns before sending anything and leaves the state untouched, so it cannot get stuck at Generating.

**3. [Plan detail] BuildValuesPrompt landed in the Task 1 commit**
- It was written alongside BuildFactsPrompt because both share the existing/keep-exactly helper (`AppendExisting`). Its tests are in Task 2.

**4. [Plan detail] Facts_ParallelBound uses SlowProvider rather than an OnSend counter**
- FakeLlmProvider completes synchronously, so an OnSend counter would never see concurrent calls.

## Requirements

GUIDE-05 and GUIDE-06 are implemented at the service level only. They are not marked complete because no wizard step calls `GenerateAsync` yet (that is 03-13/03-14), and regeneration with existingId and hand-edit preservation is 03-11.

## Known Stubs

None.

## Threat Mitigations

- **T-03-34**: Every issue, cast and fact reference is normalized and checked against the guide. Unknown references are dropped with MappingNotes on the fact or value.
- **T-03-35**: Parallel fact tasks return DTOs only. `guide.Facts`, CompletedUnits and the ID counters change only on the calling thread after `Task.WhenAll`.
- **T-03-36**: Each call writes at most 30 facts, with at most 3 calls in flight and a 16,000-token output cap. A fatal error cancels the remaining calls through the linked CTS, and finished units are skipped on Retry.

## TDD Gate Compliance

Each task has a `test(03-10)` RED commit that does not compile because the members are missing, followed by a `feat(03-10)` GREEN commit. No refactor commits were needed.

## Self-Check: PASSED
