---
phase: 08-ancillary-discovery-documents
plan: 10
subsystem: discovery-worklist-step-model
tags: [discovery, pricing, worklist, step-model, ANC-01, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-01
    provides: "DiscoverySettings (+ ForPreset, UserTouched), DiscoveryDocumentSet, DiscoveryDocument, DiscoveryStore (FileNameFor, DocxPath, IsDiscoveryFileName), DiscoveryStaleness.Check/Stamp, DiscoveryCorpus"
  - phase: 08-03
    provides: "DepositionPlanner.Eligible / DefaultWitnessIds / Plan"
  - phase: 08-04
    provides: "WrittenDiscoveryPlanner.Availability / ResolveParties / EffectiveCount / Plan, MaxSlotsPerCall"
  - phase: 08-09
    provides: "DiscoveryUiText (ResolveState, ShortTitle, EstimateCondition, Raised/LoweredSentence), DiscoveryRowInfo"
provides:
  - "GenerationCostEstimator.PriceDiscoveryDeposition(chunks, pages, model) / PriceDiscoveryWrittenSet(calls, requests, model): Story tier, live, QaOverhead, single Price path"
  - "DiscoveryWorkItem + DiscoveryWorklist.Build / ForDocument / DepositionKey / WrittenKey / CaseFolderPart / DepositionDocumentPart"
  - "DiscoveryStepModel.Defaults / ApplyDefaults / Build / SavedRows / CountHint / Condition / CanGenerate / TryParsePages / TryParseCount"
affects: [08-11, 08-14, 08-16, 08-17, 08-18]
tech-stack:
  added: []
  patterns:
    - "Pure step model (no WinForms types) built from draft + sidecar + corpus; the UI only binds to it"
    - "Rows = union of saved sidecar documents and selected work items, keyed by stable sidecar key"
key-files:
  created:
    - Services/Discovery/DiscoveryWorklist.cs
    - UserControls/DiscoveryStepModel.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryWorklistTests.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryStepModelTests.cs
  modified:
    - Services/Corpus/GenerationCostEstimator.cs
    - ReelDiscovery.Tests/Corpus/GenerationCostEstimatorTests.cs
key-decisions:
  - "08-10: Written work item RequestCount = planned slot count and CallCount = ceil(slots / 25), so the estimate follows the lowered RFA count"
  - "08-10: Memo deposition file names keep the (Excerpt) suffix: 'Interview of {name} (Excerpt)' / 'Examination Under Oath of {name} (Excerpt)'"
  - "08-10: ForDocument force-enables the document's own written kind on a settings copy so a saved set whose box is now cleared can still be regenerated"
  - "08-10: Build uses draft.Discovery or (when null) the non-mutating Defaults(draft); only ApplyDefaults writes the draft"
  - "08-10: Saved deposition rows sort by their content Date, unsaved ones by plan date; undated rows last"
  - "08-10: CanGenerate blocks same parties only when a visible written set is checked (AnyWrittenChecked)"
patterns-established:
  - "DiscoveryWorklist.DepositionKey / WrittenKey are the single source of sidecar keys (DEP-{id}, RFP, ROG, RFA)"
requirements-completed: []
duration: 6min
completed: 2026-10-08
---

# Phase 8 Plan 10: Discovery pricing, worklist and step model Summary

Discovery work is now priced through the single `GenerationCostEstimator.Price` path (Story tier, live calls, 1.15 QA overhead). `DiscoveryWorklist` turns the deposition and written-discovery planners into one ordered list: depositions by date, then RFP, ROG, RFA, each with a stable key and a safe file name. `DiscoveryStepModel` is pure and builds the Documents rows, the defaults, the field validation, the documents to write and the estimate. The step UI, the generator and Case Summary all read from it.

## What was built

- **Pricing (Task 1):** four `[ASSUMED]` constants: `DepositionChunkInput` 7,000, `DepositionOutputPerPage` 560, `WrittenSetInput` 6,000 and `OutputPerRequest` 150. `PriceDiscoveryDeposition` and `PriceDiscoveryWrittenSet` return 0 when there is no model or no work.
- **Worklist (Task 2):** `Build` combines `DepositionPlanner.Plan` with `WrittenDiscoveryPlanner.Plan` for each kind. A saved file name from the sidecar is reused only when `IsDiscoveryFileName` accepts it. Otherwise the name comes from `FileNameFor(case folder name or DefaultFolderName, document part)`. `ForDocument` plans the single item that Regenerate... needs.
- **Step model (Task 3):** row states come from `DiscoveryUiText.ResolveState` with `DiscoveryStaleness.Check`. File checks go only through `DiscoveryStore.DocxPath` (T-08-28). `ToWrite` holds only Not generated and Failed rows (D-11, D-27). `SavedRows` serves Case Summary. Pages and counts are parsed as digits only with the D-15/D-17..D-19 ranges (T-08-29).

## Verification

- `GenerationCostEstimatorTests`: 30 pass (4 new)
- `DiscoveryWorklistTests`: 8 pass
- `DiscoveryStepModelTests`: 30 pass, counting theory cases (14 test methods)
- `FullyQualifiedName~Tests.Discovery.|FullyQualifiedName~GenerationCostEstimatorTests`: 439 pass
- App and test builds pass with `-warnaserror`

## TDD Gate Compliance

Each task has a RED `test(08-10)` commit followed by a GREEN `feat(08-10)` commit (2463e55 -> b8b3308, a5129a4 -> a4bbd91, b8e67ab -> 9460b61). The worklist and step-model RED commits were confirmed failing to compile without the implementation before they were committed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 2463e55 | test(08-10): add failing discovery pricing tests |
| 1 GREEN | b8b3308 | feat(08-10): price discovery depositions and written sets on the single estimator path |
| 2 RED | a5129a4 | test(08-10): add failing DiscoveryWorklist tests |
| 2 GREEN | a4bbd91 | feat(08-10): compose discovery planners into an ordered worklist |
| 3 RED | b8e67ab | test(08-10): add failing DiscoveryStepModel tests |
| 3 GREEN | 9460b61 | feat(08-10): add pure DiscoveryStepModel for rows, defaults, validation and estimate |

## Deviations from Plan

**1. [Rule 2 - Missing functionality] Additions to the work item and step model beyond the listed fields**
- `DiscoveryWorkItem` also carries `Variant` and `WitnessName`, so the UI can format `DiscoveryUiText.ShortTitle` without looking up the cast again.
- `DiscoveryStepModel` also exposes `Defaults(draft)` (non-mutating) and `AnyWrittenChecked`. `DiscoveryWorklist` also exposes `DepositionKey`, `WrittenKey`, `CaseFolderPart` and `DepositionDocumentPart`, so the generator uses the same keys and names.
- Files: Services/Discovery/DiscoveryWorklist.cs, UserControls/DiscoveryStepModel.cs

**2. [Rule 2 - Robustness] Planner calls wrapped in `catch (Exception)`**
- The plan requires "never throws". The guide and the sidecar are hand-editable, so Build, ForDocument and SavedRows return empty or null on a malformed graph instead of crashing the wizard. Each catch has a comment explaining why.

## Known Stubs

None.

## Self-Check: PASSED
