---
phase: 2
slug: case-scenario-core-document
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-09-30
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 (xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1) |
| **Config file** | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| **Quick run command** | `dotnet test ReelDiscovery.slnx --filter "FullyQualifiedName~ReelDiscovery.Tests.Case"` |
| **Full suite command** | `dotnet test ReelDiscovery.slnx` |
| **Estimated runtime** | ~30 seconds |
| **Precondition** | ReelDiscovery.exe must not be running (MSB3027 file lock) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test ReelDiscovery.slnx --filter "FullyQualifiedName~ReelDiscovery.Tests.Case"`
- **After every plan wave:** Run `dotnet test ReelDiscovery.slnx`
- **Before `/gsd-verify-work`:** Full suite must be green and `dotnet build ReelDiscovery.slnx` must have 0 warnings
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

Filled in by the planner/executor as task IDs are assigned. Requirement → test map (from 02-RESEARCH.md):

| Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|-------------|----------|-----------|----------------------------|-------------|--------|
| CASE-01 | Scenario stored on CaseDraft; empty/over-length rejected | unit | `CaseDraftTests` | ❌ W0 | ⬜ pending |
| CASE-02 | Presets strictly increase; outline prompt carries preset numbers | unit | `ComplexityPresetTests\|CoreDocumentValidatorTests` | ❌ W0 | ⬜ pending |
| CASE-03 | Entity detection parse; safe substitution; leak scan | unit | `NameSubstitutionTests\|RealEntityDetectorTests` | ❌ W0 | ⬜ pending |
| CASE-04 | Doc-type resolver (Complaint / Petition / Memo) | unit | `DocumentTypeResolverTests` | ❌ W0 | ⬜ pending |
| CASE-05 | Assembler numbering, incorporation, caption, required sections, jurisdiction consistency | unit | `CoreDocumentAssemblerTests\|CoreDocumentValidatorTests` | ❌ W0 | ⬜ pending |
| CASE-06 | Citation scanner corpus; one auto-regenerate on error citation | unit | `CitationScannerTests\|CoreDocumentGeneratorTests` | ❌ W0 | ⬜ pending |
| CASE-07 | Section regenerate/edit semantics, revision counter | unit | `CoreDocumentGeneratorTests\|CoreDocumentEditingTests` | ❌ W0 | ⬜ pending |
| cross | Strict schemas (≤16 union types); CaseDraft JSON round-trip; outcome handling | unit | `CaseSchemaTests\|CaseDraftSerializationTests` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Case/` folder with the test files listed above
- [ ] `CaseTestData` helper (sample outline JSON, section JSON with/without citations, scenario with real names)
- [ ] Reuse `FakeLlmProvider` + `LlmGateway` construction pattern from `LegacyGeneratorPortTests.CreateGateway()`

No framework install needed.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Scenario step + preset summary visibly changes | CASE-01, CASE-02 | WinForms UI needs a desktop session | Launch app, enter scenario, switch presets, confirm summary numbers change |
| Real-name warning dialog, default substitution, override | CASE-03 | UI + live model | Enter a scenario naming a real company; confirm dialog, default ticked, untick one and confirm original kept |
| Civil → Complaint and criminal → Memo with full structure | CASE-04, CASE-05 | Live LLM output quality | Run one civil and one criminal scenario against Claude and OpenAI; inspect structure |
| Edit / regenerate section / regenerate all; Next blocked on citation errors | CASE-06, CASE-07 | UI interaction | Edit a section, regenerate another, confirm edits preserved; inject a fake citation and confirm Next is blocked |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
