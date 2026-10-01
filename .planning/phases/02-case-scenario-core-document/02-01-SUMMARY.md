---
phase: 02-case-scenario-core-document
plan: 01
subsystem: case-domain-model
tags: [models, case-draft, complexity-presets, json, tests]
requires: []
provides:
  - "Models/Case/* (CaseDraft, CoreDocument, CoreDocumentSection, Jurisdiction, EntitySubstitution, ComplexityPreset)"
  - "WizardFlow enum; WizardState.Flow / WizardState.Case"
  - "ReelDiscovery.Tests/Case/CaseTestData.cs shared scaffold"
affects: [02-02, 02-03, 02-04, 02-05, 02-06, 02-07, 02-08, 02-09]
tech-stack:
  added: []
  patterns: ["Static preset table via switch expression", "CaseDraft.JsonOptions with JsonStringEnumConverter", "yyyy-MM-dd string dates parsed only by CaseDraft.TryParseDate"]
key-files:
  created:
    - Models/Case/ComplexityPreset.cs
    - Models/Case/Jurisdiction.cs
    - Models/Case/EntitySubstitution.cs
    - Models/Case/CoreDocumentSection.cs
    - Models/Case/CoreDocument.cs
    - Models/Case/CaseDraft.cs
    - Models/WizardFlow.cs
    - ReelDiscovery.Tests/Case/CaseTestData.cs
    - ReelDiscovery.Tests/Case/ComplexityPresetTests.cs
    - ReelDiscovery.Tests/Case/CaseDraftTests.cs
    - ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs
  modified:
    - Models/WizardState.cs
decisions:
  - "[02-01]: CaseCount.Summary is serialized data, so the 'no Summary key' check asserts only count summaries appear and the preset summary text is absent"
  - "[02-01]: ComplexityPreset.Get throws ArgumentOutOfRangeException for undefined enum values"
metrics:
  duration: 9min
  completed: 2026-10-01
---

# Phase 2 Plan 01: Case Domain Model and Presets Summary

This plan adds the case domain model in `Models/Case/*`. `CaseDraft` holds the scenario, preferences, real-name substitutions and a structured `CoreDocument`, and it serializes to JSON with enums written as strings. `ComplexityPreset` is one table of Simple/Standard/Complex values that produces the UI-SPEC summary strings. `WizardState` gains `Flow` (default `CaseDriven`) and `Case`. Test helpers for Phase 2 live in `CaseTestData`, which provides an Anthropic fake gateway and sample complaint and memo drafts.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | Case domain model, preset table, WizardFlow, WizardState properties | e58322a (RED), 5603582 (GREEN) | Models/Case/*.cs, Models/WizardFlow.cs, Models/WizardState.cs, ComplexityPresetTests.cs, CaseDraftTests.cs |
| 2 | CaseTestData scaffold and JSON round-trip tests | 469bbe4 (RED), 035d012 (GREEN) | CaseTestData.cs, CaseDraftSerializationTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors
- `dotnet test ReelDiscovery.slnx`: 248 passed, 0 failed (37 new Case tests)
- The acceptance greps pass: `Get(ComplexityLevel level)`, `MaxScenarioLength = 8_000`, `JsonStringEnumConverter`, a single `TryParseExact` and no culture-dependent `DateTime.TryParse(`, both WizardState property lines, and no `record` in Models/Case. 15 contract section ids are present in CaseTestData.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The plan's Computed_NotSerialized check conflicted with the model**
- **Found during:** Task 2
- **Issue:** The plan asked that the JSON not contain `"Summary"`. `CaseCount.Summary`, which the plan also specifies, is a real serialized property, so that assertion can never pass.
- **Fix:** The test now asserts that `"Preset"` and `"MemoParagraphsMin"` are absent, that the number of `"Summary"` keys equals the number of counts, and that the preset summary text does not appear in the JSON.
- **Files modified:** ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs
- **Commit:** 035d012

**2. [Note] The "Standard" enum string is checked on a default CaseDraft**
- The sample complaint is Simple (per the plan), so `Enums_SerializedAsStrings` checks `"Simple"` on the sample and `"Standard"` on a default `new CaseDraft()`.

### Additions (non-breaking)
- `FromJson_NullLiteral_Throws` covers the T-02-02 mitigation.
- `SampleDrafts_HaveCurrentSourceHash` checks that the sample drafts' SourceHash is not stale.
- `FormatRange_CollapsesEqualBounds` is a new test.
- `SampleMemoDraft` has a Title ("Investigation Memorandum: Desert Bloom Laundromats"). The plan did not specify one.

## TDD Gate Compliance

Both tasks have a `test(02-01)` RED commit (a compile failure) followed by a `feat(02-01)` GREEN commit.

## Known Stubs

None.

## Self-Check: PASSED
