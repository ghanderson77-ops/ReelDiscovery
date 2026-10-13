---
phase: 03-case-guide
plan: 01
subsystem: case-guide-model
tags: [models, case-guide, case-json, complexity-presets, fixture, tests]
requires: []
provides:
  - "Models/Case/CaseGuide.cs (lists, IdCounters, part states, State(GuidePart), Find*, AllIds, ComputeHash, Normalize)"
  - "Models/Case/{CaseIssue,CaseOrganization,CastMember,KeyFact,CanonicalValue,GuidePartState,CaseStage}.cs"
  - "CaseDraft FormatVersion/CaseId/CreatedUtc/UpdatedUtc/Stage/Guide, [JsonIgnore] FolderPath, Normalize(), AdvanceStage()"
  - "ComplexityPreset BackgroundPerOrg, FactsMin/FactsMax"
  - "CaseTestData.SampleGuidedDraft() fixture"
affects: [03-03, 03-04, 03-05, 03-07, 03-08, 03-09, 03-10, 03-11, 03-12, 03-13, 03-14, 03-15, 03-16, 03-17, 03-18, 03-19, 03-20]
tech-stack:
  added: []
  patterns: ["Normalize() repairs untrusted JSON (null lists/strings/sub-objects -> empty) without throwing", "Stage only moves forward via AdvanceStage", "CaseGuide.CleanList/CleanStrings internal helpers shared with CaseDraft.Normalize"]
key-files:
  created:
    - Models/Case/CaseStage.cs
    - Models/Case/GuidePartState.cs
    - Models/Case/CaseGuide.cs
    - Models/Case/CaseIssue.cs
    - Models/Case/CaseOrganization.cs
    - Models/Case/CastMember.cs
    - Models/Case/KeyFact.cs
    - Models/Case/CanonicalValue.cs
    - ReelDiscovery.Tests/Case/CaseGuideModelTests.cs
  modified:
    - Models/Case/CaseDraft.cs
    - Models/Case/ComplexityPreset.cs
    - ReelDiscovery.Tests/Case/CaseTestData.cs
    - ReelDiscovery.Tests/Case/ComplexityPresetTests.cs
decisions:
  - "[03-01]: SampleGuidedDraft has a single issue (ISS-01) because SampleComplaintDraft has one count; all six facts reference ISS-01 and IdCounters[ISS] = 1"
  - "[03-01]: Fixture law-firm domain is derived from Counsel.FirmName (first two letter-only tokens, LLP/LLC/PC skipped) -> ellispark.com; CH-006 is Morgan Ellis"
  - "[03-01]: CaseDraft.Normalize also cleans EntitySubstitution strings/aliases and null CoreDocument scalar strings; GuidePartState null BasisComponents values become empty"
metrics:
  duration: 5min
  completed: 2026-10-01
---

# Phase 3 Plan 01: Case Guide Data Model Summary

This plan adds the case guide data model that later Phase 3 plans build on. `CaseGuide` holds issues, organizations, cast, key facts and canonical values, each with a stable string Id. It also carries per-prefix ID counters and five `GuidePartState`s, and provides ordinal lookups, `AllIds()`, `ComputeHash()` and a null-safe `Normalize()`. `CaseDraft` gains the case.json persistence fields (FormatVersion, CaseId, timestamps, Stage, Guide). `FolderPath` is `[JsonIgnore]`, and `Normalize()` / `AdvanceStage()` were added. `ComplexityPreset` now has BackgroundPerOrg (3/6/10) and Facts ranges (12-20 / 30-50 / 70-110). A complete `SampleGuidedDraft()` fixture is available for every later test.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | Guide model types, CaseDraft persistence fields, preset numbers | e70646e (RED), 9ec59be (GREEN) | 10 model files, ComplexityPresetTests.cs |
| 2 | SampleGuidedDraft fixture and CaseGuideModelTests | decc1e6 (RED), cff5267 (GREEN) | CaseTestData.cs, CaseGuideModelTests.cs |

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors (the test project also builds with 0 warnings)
- `dotnet test --filter "Category!=Render"`: 515 passed, 0 failed
- Acceptance greps: CaseStage enum, `State(GuidePart part)`, both `Normalize()` methods, `[JsonIgnore]` on FolderPath, `BackgroundPerOrg = 10`, `FactsMin = 70, FactsMax = 110`, `"Facts"` in preset tests, no apikey/ModelId/Provider in CaseDraft.cs, `"CH-009"` literal in the fixture, 11 `[Fact]`s in CaseGuideModelTests

## Deviations from Plan

### Auto-fixed Issues

None. Two small additions beyond the listed behaviors:
- I added two extra tests: `State_MapsEachPart` and `SampleGuidedDraft_IsInternallyConsistent`. The second checks the fixture rule that later validator tests rely on: every issue has a fact, every key party/staff character acts in a fact, KnownByIds includes the ActorIds, and every referenced id resolves.
- `CaseDraft.Normalize` also repairs `EntitySubstitution` strings and aliases, CoreDocument scalar strings and `SourceHash`, plus null values inside the `BasisComponents` dictionary. This hardens the T-03-01 mitigation and does not change the contract.

## TDD Gate Compliance

Both tasks have a `test(03-01)` RED commit (a compile failure that proved the members were missing) followed by a `feat(03-01)` GREEN commit. No refactor commits were needed.

## Known Stubs

None.

## Self-Check: PASSED
