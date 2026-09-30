---
phase: 01-foundation-provider-gateway
plan: 08
subsystem: generation
tags: [llm-gateway, structured-outputs, strict-schema, storylines, characters, themes]
requires:
  - phase: 01-03
    provides: ILlmGateway, LlmRequest/LlmResult, JsonSchemaRegistry, FakeLlmProvider, StrictSchemaValidator
  - phase: 01-07
    provides: WizardState.CreateGateway()
provides:
  - Gateway-based StorylineGenerator / CharacterGenerator (Story tier) and ThemeGenerator (Volume tier)
  - Strict-schema DTOs with [Description] field semantics and per-generator SchemaTypes
  - LegacyGeneratorPortTests (schema strictness + round-trip on an Anthropic fake)
affects: [01-10, 01-11]
tech-stack:
  added: []
  patterns:
    - "Generators take ILlmGateway; call _llm.GenerateAsync<TDto>(new LlmRequest { Tier, OperationName, ... }) then GetValueOrThrow()"
    - "Field semantics live in [Description] on internal nested DTOs, not in prompt JSON templates"
    - "internal static SchemaTypes per generator, verified by a strict-schema theory test"
key-files:
  created:
    - ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs
  modified:
    - Services/StorylineGenerator.cs
    - Services/CharacterGenerator.cs
    - Services/ThemeGenerator.cs
    - UserControls/StepStorylines.cs
    - UserControls/StepCharacters.cs
key-decisions:
  - "SuggestedDateRange is non-null in the schema; empty startDate means no range was requested"
  - "SuggestedEmailCount range 5..20 enforced with Math.Clamp in code, not in the schema"
  - "ThemeGenerator rethrows fatal (Auth/SpendLimit) failures and falls back to default themes on any other failure"
patterns-established:
  - "Legacy generator port: DTO hardening + gateway call + step call-site swap to CreateGateway()"
requirements-completed: []
duration: 5min
completed: 2026-09-30
---

# Phase 01 Plan 08: Legacy Generator Port Summary

**Storyline, character and theme generation now run through ILlmGateway with strict-schema DTOs: Story tier for storylines/characters, Volume tier for themes, on either OpenAI or Claude.**

## Performance

- **Duration:** about 5 min
- **Completed:** 2026-09-30
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- All nested response DTOs in the three generators are `internal`, carry `[Description]` text taken from the old prompt JSON templates, and pass the strict-schema validator (16 or fewer union types).
- The generators take `ILlmGateway`. The embedded JSON templates and "respond with valid JSON" lines are gone from the prompts. Every behavioural instruction is kept, and the date-range instruction is now plain text.
- The model output is still checked in code: `SuggestedEmailCount` is clamped to 5..20, storyline indices are bounds-checked, and theme colors/fonts still go through `SanitizeHexColor` and the font allowlist.
- `StepStorylines` and `StepCharacters` now build these generators from `_state.CreateGateway()`. One gateway instance is shared between characters and themes.
- There are 11 LegacyGeneratorPortTests, and the round-trip tests run on an Anthropic `FakeLlmProvider`. The full suite passes (146 tests).

## Task Commits

1. **Task 1: Harden the three generators' DTOs and write schema tests** - `621f430` (feat)
2. **Task 2: Switch the three generators and their steps to ILlmGateway** - `973b710` (feat)

## Files Created/Modified

- `Services/StorylineGenerator.cs` - gateway call (Story tier, 32K), hardened DTOs, clamp, non-null date range
- `Services/CharacterGenerator.cs` - gateway call (Story tier, 32K), hardened DTOs, simplified null handling
- `Services/ThemeGenerator.cs` - gateway call (Volume tier, 8K), fatal rethrow + default-theme fallback
- `UserControls/StepStorylines.cs`, `UserControls/StepCharacters.cs` - `CreateGateway()` call sites
- `ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs` - schema strictness, round-trip, refusal, fallback and fatal tests

## Decisions Made

- An empty `suggestedDateRange.startDate` means "no date range". The gateway schema needs every property, so the date range can no longer be optional.
- Added a `Storyline_EmptyDateRange_LeavesDatesUnset` test beyond the plan to pin that behaviour.

## Deviations from Plan

None. The plan was executed as written. The only addition is the extra empty-date-range test.

## Issues Encountered

None.

## TDD Gate Compliance

Both tasks were marked `tdd="true"`, but the tests and implementation were committed together in each task. The schema tests in Task 1 need the `SchemaTypes` members, and the round-trip tests in Task 2 need the new constructors to compile. No separate `test(...)` RED commits exist.

## Known Stubs

None.

## Next Phase Readiness

- Three of the four legacy generators are on the gateway. EmailGenerator follows in 01-10. `OpenAIService` / `CreateOpenAIService()` remain only for EmailGenerator until 01-11.

## Self-Check: PASSED

- FOUND: Services/StorylineGenerator.cs, Services/CharacterGenerator.cs, Services/ThemeGenerator.cs, ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs
- FOUND: 621f430, 973b710
