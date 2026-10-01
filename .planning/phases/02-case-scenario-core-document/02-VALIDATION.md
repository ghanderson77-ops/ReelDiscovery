---
phase: 2
slug: case-scenario-core-document
status: draft
nyquist_compliant: true
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

Filter prefix: `dotnet test ReelDiscovery.slnx --filter "FullyQualifiedName~ReelDiscovery.Tests.Case.<Class>"`.

| Task ID | Plan | Wave | Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|---------|------|------|-------------|----------|-----------|----------------------------|-------------|--------|
| 02-01-T1 | 01 | 1 | CASE-01, CASE-02 | Preset table strictly increases, summary copy, memo paragraph range; UsStates list; scenario empty/over-length rejected; source hash; TryParseExact dates; WizardState Flow/Case defaults | unit | `ComplexityPresetTests\|CaseDraftTests` | ❌ W0 (created by task) | ⬜ pending |
| 02-01-T2 | 01 | 1 | cross | CaseDraft JSON round-trip with string enums (Phase 3 case.json readiness); CaseTestData fixtures | unit | `CaseDraftSerializationTests` | ❌ W0 (created by task) | ⬜ pending |
| 02-02-T1 | 02 | 2 | CASE-06 | Citation scanner positive/negative corpus, own caption excluded | unit | `CitationScannerTests` | ❌ W0 | ⬜ pending |
| 02-02-T2 | 02 | 2 | CASE-03 | Single-pass substitution, word boundary, possessive, surname case, no chains, leak scan | unit | `NameSubstitutionTests` | ❌ W0 | ⬜ pending |
| 02-03-T1 | 03 | 2 | CASE-04, CASE-05 | Doc-type resolver matrix; required sections per type; ToRoman (assembler class created here) | unit | `DocumentTypeResolverTests\|CoreDocumentTemplatesTests\|CoreDocumentAssemblerTests` | ❌ W0 | ⬜ pending |
| 02-03-T2 | 03 | 2 | CASE-05 | Numbering, incorporation 1..lastFact updates after edit, caption (no judge), memo header, signature, prayer letters | unit | `CoreDocumentAssemblerTests` | ❌ W0 | ⬜ pending |
| 02-04-T1 | 04 | 3 | CASE-02, CASE-05, CASE-06 | Preset range warnings (parties, counts, paragraphs, memo length), missing/failed sections, timeline, jurisdiction consistency + court/system contradiction errors for pleadings (any "United States ..." court accepted; memos skip rule 1) and a different-state warning (trailing segment, whole words, County/Parish ignored), citations, leaks (full name Error, lone alias Warning), paragraph refs, stale hash | unit | `CoreDocumentValidatorTests` | ❌ W0 | ⬜ pending |
| 02-04-T2 | 04 | 3 | CASE-07 | Paragraph split/join, ApplyEdit sets IsUserEdited and bumps Revision only on change | unit | `CoreDocumentEditingTests` | ❌ W0 | ⬜ pending |
| 02-05-T1 | 05 | 2 | CASE-03 | Detector parse/dedupe, Story tier, scenario only in user prompt, refused/fatal propagate | unit | `RealEntityDetectorTests` | ❌ W0 | ⬜ pending |
| 02-06-T1 | 06 | 3 | CASE-02, CASE-04 | Strict schemas (<=16 unions); outline prompt carries preset numbers incl. memo length, required preferences, substitutions; outline mapping; refused outline | unit | `CaseSchemaTests\|CoreDocumentGeneratorTests` | ❌ W0 | ⬜ pending |
| 02-06-T2 | 06 | 3 | CASE-05, CASE-06, CASE-07 | Sections Ready; leading numbers stripped; one citation retry; refused/truncated -> Failed; fatal stops and keeps; memo exec summary last; cancel; RegenerateSection replaces only target, keeps Id, uses edited text; Revision bump when sections written; sections name CharactersMin-Max individuals | unit | `CoreDocumentGeneratorTests` | ❌ W0 | ⬜ pending |
| 02-07-T1 | 07 | 3 | CASE-03 | Entity dialog helpers (kind labels, aliases text, blank substitute); LoadingOverlay EntityCheck builds | unit + build | `CaseScenarioUiLogicTests` | ❌ W0 | ⬜ pending |
| 02-07-T2 | 07 | 3 | CASE-01, CASE-02 | Scenario counter text/colors; step compiles with UI-SPEC copy (grep gates) | unit + build | `CaseScenarioUiLogicTests` | ❌ W0 | ⬜ pending |
| 02-08-T1 | 08 | 4 | CASE-04, CASE-05, CASE-07 | Section suffix/color, document title, ready status, issues caption helpers | unit + build | `CoreDocumentUiLogicTests` | ❌ W0 | ⬜ pending |
| 02-08-T2 | 08 | 4 | CASE-06, CASE-07 | Editing/regenerate/Finish gate wiring (grep gates) | build + suite | `ReelDiscovery.Tests.Case` (all) | n/a | ⬜ pending |
| 02-09-T1 | 09 | 5 | CASE-01, CASE-07 | Flow step tables; Finish validates before close + C6 (source scan) | unit | `WizardFlowTests` | ❌ W0 | ⬜ pending |
| 02-09-T2 | 09 | 5 | CASE-01 | Workflow radios on API step (grep gates); full suite green | build + suite | full `dotnet test ReelDiscovery.slnx` | n/a | ⬜ pending |

Requirement coverage: CASE-01 (02-01, 02-07, 02-09), CASE-02 (02-01, 02-04, 02-06, 02-07), CASE-03 (02-02, 02-05, 02-07), CASE-04 (02-03, 02-06, 02-08), CASE-05 (02-03, 02-04, 02-06, 02-08), CASE-06 (02-02, 02-04, 02-06, 02-08), CASE-07 (02-04, 02-06, 02-08, 02-09).

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

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (each test file is created by the task that needs it; CaseTestData by 02-01-T2)
- [x] No watch-mode flags
- [x] Feedback latency < 60s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
