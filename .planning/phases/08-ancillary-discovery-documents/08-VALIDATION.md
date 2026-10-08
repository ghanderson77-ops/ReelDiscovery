---
phase: 8
slug: ancillary-discovery-documents
status: planned
nyquist_compliant: true
wave_0_complete: false
created: 2026-10-08
---

# Phase 8 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 (net10.0-windows) |
| **Config file** | none — `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~ReelDiscovery.Tests.Discovery"` |
| **Full suite command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category!=Render&Category!=Scale"` |
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render"` |
| **Estimated runtime** | ~30 s (quick), ~90 s (full) |

---

## Sampling Rate

- **After every task commit:** quick run filtered to the touched test classes
- **After every plan wave:** `FullyQualifiedName~Discovery|FullyQualifiedName~WizardFlowTests|FullyQualifiedName~CaseSchemaTests|FullyQualifiedName~CaseDraftSerializationTests`, plus Render when UI changed
- **Before `/gsd-verify-work`:** full suite + Render green, PNGs inspected at 144 DPI, manual Word check of a deposition .docx
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

Derived from 08-RESEARCH.md "Phase Requirements → Test Map". Every task has an automated command; test files are created inside the task that needs them (TDD), so there is no separate Wave 0 plan.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 08-01-T1 | 08-01 | 1 | ANC-05 | T-08-01 | Normalize clamps untrusted settings; v3 file loads | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseDraftSerializationTests"` | created in task | ⬜ pending |
| 08-01-T2 | 08-01 | 1 | ANC-05 | T-08-02..04 | Docx paths stay inside Discovery folder; sidecar capped/versioned; staleness | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryStoreTests|FullyQualifiedName~DiscoveryStalenessTests"` | created in task | ⬜ pending |
| 08-02-T1 | 08-02 | 1 | ANC-01, ANC-02 | T-08-06/07 | Counsel lookups null-safe | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CounselDirectoryTests"` | created in task | ⬜ pending |
| 08-02-T2 | 08-02 | 1 | ANC-01, ANC-02 | — | Fixtures keep names from fictional works | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CounselDirectoryTests"` | created in task | ⬜ pending |
| 08-03-T1 | 08-03 | 2 | ANC-01, ANC-05 | T-08-08/10 | Deterministic witnesses/roles/dates/case number | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseNumberTests|FullyQualifiedName~DepositionPlannerTests"` | created in task | ⬜ pending |
| 08-03-T2 | 08-03 | 2 | ANC-01 | T-08-09 | Chunks, knowledge slice, exhibit numbering | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DepositionPlannerTests"` | created in task | ⬜ pending |
| 08-04-T1 | 08-04 | 2 | ANC-02..04 | T-08-11 | Availability, parties, count clamps (ROG <= 25) | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~WrittenDiscoveryPlannerTests"` | created in task | ⬜ pending |
| 08-04-T2 | 08-04 | 2 | ANC-02..04 | T-08-12 | Slots keyed by code; RFA prefix | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~WrittenDiscoveryPlannerTests"` | created in task | ⬜ pending |
| 08-05-T1 | 08-05 | 2 | ANC-01, ANC-03 | T-08-14..16 | Read-only corpus probe never throws | build | `dotnet build ReelDiscovery.csproj -warnaserror --artifacts-path obj/gsd-artifacts` | created in task | ⬜ pending |
| 08-05-T2 | 08-05 | 2 | ANC-01, ANC-03 | T-08-15 | Only written DocIDs become candidates | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CorpusDetectorTests"` | created in task | ⬜ pending |
| 08-06-T1 | 08-06 | 2 | ANC-01 | T-08-18/19 | 25 lines/page, <= 62 chars, page:line map | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~TranscriptLayoutTests"` | created in task | ⬜ pending |
| 08-06-T2 | 08-06 | 2 | ANC-01, ANC-05 | T-08-17 | Transcript .docx validator-clean | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~Transcript"` | created in task | ⬜ pending |
| 08-07-T1 | 08-07 | 2 | ANC-02..05 | T-08-21/22 | Code boilerplate; no invented rule numbers | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryBoilerplateTests|FullyQualifiedName~CoreDocumentAssemblerTests|FullyQualifiedName~CaseGuideDocxExporterTests"` | created in task | ⬜ pending |
| 08-07-T2 | 08-07 | 2 | ANC-02..05 | T-08-20 | Written .docx validator-clean | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryDocxBuilderTests"` | created in task | ⬜ pending |
| 08-08-T1 | 08-08 | 2 | ANC-05 | T-08-23 | Regex move; Phase 5 behavior unchanged | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~RenderValidatorTests|FullyQualifiedName~RenderRepairTests|FullyQualifiedName~ThreadUnitOfWorkTests"` | created in task | ⬜ pending |
| 08-08-T2 | 08-08 | 2 | ANC-03..05 | T-08-24/25 | D1-D8 checks, keep-best, feedback | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryValidatorTests"` | created in task | ⬜ pending |
| 08-09-T1 | 08-09 | 2 | ANC-01..05 | T-08-26 | All copy pure/tested; never says fictional | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryUiTextTests"` | created in task | ⬜ pending |
| 08-09-T2 | 08-09 | 2 | ANC-01..05 | T-08-27 | Dialog at 144 DPI; guidance <= 1,000 chars | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&FullyQualifiedName~DialogRenderTests"` | created in task | ⬜ pending |
| 08-10-T1 | 08-10 | 3 | ANC-01..05 | T-08-30 | Single pricing path | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationCostEstimatorTests"` | created in task | ⬜ pending |
| 08-10-T2 | 08-10 | 3 | ANC-01..05 | T-08-28 | Ordered worklist; valid file names | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryWorklistTests"` | created in task | ⬜ pending |
| 08-10-T3 | 08-10 | 3 | ANC-01..05 | T-08-28/29 | Rows/states/ToWrite/defaults/validation | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryStepModelTests"` | created in task | ⬜ pending |
| 08-11-T1 | 08-11 | 3 | ANC-01..05 | T-08-31/32 | Grounded, cacheable, name-safe prompts | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryPromptInvariantTests"` | created in task | ⬜ pending |
| 08-11-T2 | 08-11 | 3 | ANC-01..05 | — | Strict DTOs | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseSchemaTests"` | created in task | ⬜ pending |
| 08-12-T1 | 08-12 | 4 | ANC-01, ANC-05 | T-08-34..36 | Chunk loop, retries, keep-best, fatal/cancel | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DepositionWriterTests"` | created in task | ⬜ pending |
| 08-13-T1 | 08-13 | 4 | ANC-02..05 | T-08-37..39 | Batches, retries, renumbering, prefixes | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~WrittenSetWriterTests"` | created in task | ⬜ pending |
| 08-14-T1 | 08-14 | 4 | ANC-01..04 | — | Layout per UI-SPEC | build | `dotnet build ReelDiscovery.csproj -warnaserror --artifacts-path obj/gsd-artifacts` | created in task | ⬜ pending |
| 08-14-T2 | 08-14 | 4 | ANC-01..04 | T-08-40/41 | Binding, validation, never blocks Next | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~StepDiscoveryDocumentsTests"` | created in task | ⬜ pending |
| 08-14-T3 | 08-14 | 4 | ANC-01..04 | — | Render checks 1-7 at 144 DPI | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&FullyQualifiedName~DiscoveryDocumentsRenderTests"` | created in task | ⬜ pending |
| 08-15-T1 | 08-15 | 5 | ANC-01..05 | T-08-43..47 | Isolation, fatal, cancel, lock, Save Again, regenerate | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryGeneratorTests"` | created in task | ⬜ pending |
| 08-15-T2 | 08-15 | 5 | ANC-05 | — | Start Over keeps Discovery folder; full non-render suite | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category!=Render&Category!=Scale"` | created in task | ⬜ pending |
| 08-16-T1 | 08-16 | 5 | ANC-01..04 | T-08-48/49 | 9 steps, NavigateToStep(Type), leave/close safety | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~WizardFlowTests"` | created in task | ⬜ pending |
| 08-16-T2 | 08-16 | 5 | ANC-01..04 | — | Shell 'of 9' at 144 DPI | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&FullyQualifiedName~WizardShellRenderTests"` | created in task | ⬜ pending |
| 08-17-T1 | 08-17 | 6 | ANC-01..05 | T-08-53 | Generate/Cancel/fatal outcomes | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~StepDiscoveryDocumentsRunTests"` | created in task | ⬜ pending |
| 08-17-T2 | 08-17 | 6 | ANC-01..05 | T-08-50..52 | Regenerate dialog, Save Again, validated open | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~StepDiscoveryDocuments"` | created in task | ⬜ pending |
| 08-18-T1 | 08-18 | 6 | ANC-05 | T-08-55/56 | Cost clause; cost row without a corpus plan | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationUiTextTests|FullyQualifiedName~CaseSummaryQaTests"` | created in task | ⬜ pending |
| 08-18-T2 | 08-18 | 6 | ANC-01..05 | T-08-54 | Summary row + navigation + renders | unit+render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseSummary"` | created in task | ⬜ pending |
| 08-19-T1 | 08-19 | 7 | ANC-01..05 | — | Full suite + render + sample docs | unit+render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category!=Render&Category!=Scale"` then `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render"` | created in task | ⬜ pending |
| 08-19-T2 | 08-19 | 7 | ANC-01..05 | T-08-57 | Word pagination, 150% DPI, live providers | manual | checkpoint:human-verify | created in task | ⬜ pending |
| 08-19-T3 | 08-19 | 7 | ANC-01..05 | — | Requirements marked only after approval | grep | `grep -c` on ANC checkboxes in REQUIREMENTS.md = 5 | created in task | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Covered inside the plans (no separate Wave 0 plan):
- [x] `ReelDiscovery.Tests/Discovery/DiscoveryTestData.cs` + `DocxTestHelper.cs` — Plan 08-02 (wave 1)
- [x] `TranscriptTestData.cs` — Plan 08-06; `DiscoveryCorpusFixture.cs` — Plan 08-05
- [x] Discovery unit test files — created by the owning task in Plans 08-01..08-18
- [x] `ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs` — Plan 08-14
- [x] Updates: CaseDraftSerializationTests (08-01), CaseSchemaTests (08-11), GenerationCostEstimatorTests (08-10), GenerationResetTests (08-15), WizardFlowTests + WizardShellRenderTests (08-16), DialogRenderTests (08-09), GenerationUiTextTests + CaseSummaryQaTests + CaseSummaryLogicTests + CaseSummaryRenderTests (08-18)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Deposition .docx pagination in Word (25 lines/page, Courier New, page count = code count) | ANC-01 | Word layout engine | Open a generated transcript in Word, compare page count and line 25 placement |
| Live generation with a real provider | ANC-01..05 | Needs API key | Generate one deposition and one set of each written type on a small case |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 90s (filtered per-task commands; full suite only at 08-15 and 08-19)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** planned 2026-10-08 (gsd-planner)
