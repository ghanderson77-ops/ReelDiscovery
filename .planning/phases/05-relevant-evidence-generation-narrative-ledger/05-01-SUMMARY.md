---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 01
subsystem: evidence-models
tags: [models, ledger, journal, case-format, fixtures]
requires: []
provides:
  - Models/Evidence (LedgerEntry, LedgerDelta, NarrativeLedger, GenerationState, JournalRecord, RenderAssignment, RenderedThread, RenderViolation, GenerationWave, GenerationProgress read models)
  - Services/Evidence/MessageTime
  - CaseStage.Generation, case.json format version 3, CorpusState.Generation, CorpusFileKind.StorySoFar
  - EvidenceTestData.ApprovedCase fixture
affects: [05-02..05-17]
tech-stack:
  added: []
  patterns: [untrusted-JSON Normalize, safe-path filtering on replay]
key-files:
  created:
    - Models/Evidence/LedgerEntry.cs
    - Models/Evidence/LedgerDelta.cs
    - Models/Evidence/NarrativeLedger.cs
    - Models/Evidence/GenerationState.cs
    - Models/Evidence/JournalRecord.cs
    - Models/Evidence/RenderAssignment.cs
    - Models/Evidence/RenderedThread.cs
    - Models/Evidence/RenderViolation.cs
    - Models/Evidence/GenerationWave.cs
    - Models/Evidence/GenerationProgress.cs
    - Services/Evidence/MessageTime.cs
    - ReelDiscovery.Tests/Evidence/GenerationStateTests.cs
    - ReelDiscovery.Tests/Evidence/EvidenceTestData.cs
  modified:
    - Models/Case/CaseStage.cs
    - Models/Case/CaseDraft.cs
    - Models/Corpus/CorpusState.cs
    - UserControls/CaseGuideUiText.cs
    - ReelDiscovery.Tests/Case/CaseGuideModelTests.cs
    - ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideUiLogicTests.cs
decisions:
  - "JournalRecord.Normalize leaves an undefined record Type unchanged so the replayer can skip it instead of misreading it; undefined Status/FailureKind/Outcome map to Warnings/Provider/None"
  - "NarrativeLedger.Normalize clamps negative WaveIndex to 0 and caps at 20,000 entries after dropping malformed/duplicate ones"
metrics:
  duration: 15min
  completed: 2026-10-03
  tasks: 2
  files: 20
---

# Phase 5 Plan 01: Evidence Models, Case-Format Hooks and Fixture Summary

Shared Phase 5 contracts: narrative ledger and journal models with untrusted-JSON repair (safe .eml paths only, 20k ledger cap), the generation UI read model, a sender-local MessageTime helper, CaseStage.Generation plus case.json format version 3 carrying Corpus.Generation, and an approved-plan fixture covering multi-message, forward, external-sender, privileged-counsel, hot and multi-custodian threads.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | Evidence models, Normalize rules and MessageTime | 3964c82 |
| 2 | Case-format hooks and the approved-plan fixture | 2c94af6 |

## What Was Built

- **Ledger:** `LedgerEntry` (code-owned Id, yyyy-MM-dd Date, `LedgerKind`, About/Statement/Value, KnownBy/Fact/Issue ids, source doc/thread, WaveIndex, `[JsonIgnore] ParsedDate`), `LedgerDelta`, `NarrativeLedger.Normalize` (drops null/empty-Id/empty-Statement/bad-date entries, first-wins dedupe, undefined Kind -> Other, cap `MaxEntries`).
- **State/journal:** `GenerationState` (`JournalFileName = "generation-journal.jsonl"`, ledger, outcome, cached counts, Key/Story errors, clamping Normalize); `JournalRecord` + `JournalMessage` + enums, Normalize filters `Files` through `CorpusPlan.IsSafeEmlPath` (T-05-01) and clamps tokens/cost.
- **Render/UI contracts:** `Subtlety`, `LengthBand`, `PrivilegeMarking`, `LengthBandSpec`, `MessageAssignment`, `ThreadAssignment.For(seq)`, `RenderedThread/RenderedMessage`, `RenderViolation`, `GenerationWave`, `GenerationCounts`/`WaveProgress` (with `Clone()`), `GenerationEvent`, `GenerationView`, `GenerationStage`, `GenerationEventKind`.
- **MessageTime:** `Local`, `LocalDate`, `FirstUtc`, `FirstLocalDate`, `LastLocalDate` (mirrors RelevantThreadFiller helpers).
- **Case format:** `CaseStage.Generation` (label "Generating emails"), `CurrentFormatVersion = 3` (T-05-03), `CorpusState.Generation` serialized in case.json and normalized, `CorpusFileKind.StorySoFar`. `WizardForm.StepTypeForStage` intentionally unchanged (default arm).
- **Fixture:** `EvidenceTestData.ApprovedCase()`; F-003 hot thread New/Reply/ReplyAll/Forward (ReplyToSeq 0..3), F-004 rooted at vendor EXT-002, F-001 privileged with CH-005 (in-house) and CH-007 (outside counsel) and 3 custodians on its root; renumbered via `PlanNumbering.Assign` and approved via `CorpusApproval.RecordApproval`. Plus `TempCaseFolder()` / `DeleteFolder()`.

## Verification

- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence|...CaseGuideModelTests|...CaseDraftSerializationTests|...CaseGuideUiLogicTests|...WizardFlowTests"`: 151 passed.
- `dotnet test --filter "Category!=Render"`: 2030 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 CS warnings.
- No `record` / `#region` in Models/Evidence.

## Deviations from Plan

None in behavior. Additions beyond the spec, all within the planned files:
- Extra tests: JournalRecord JSON-line round trip, ledger cap, MessageTime thread bounds, `ThreadAssignment.For`, `CorpusState.Normalize` repairs Generation, and a fixture check that Seq order equals time order.
- `EvidenceTestData.VendorId` constant for the EXT-002 id.

## TDD Gate Compliance

Both tasks were marked `tdd="true"`, but tests and implementation went into one `feat` commit per task. There are no separate `test(...)` RED commits. The work is interface-first model code, so the new tests could not compile before the types existed.

## Known Stubs

None. The view/progress models are contracts that later plans (05-02..05-17) fill in.

## Self-Check: PASSED
- All 13 created files exist; commits 3964c82 and 2c94af6 are present in git log.
