---
phase: 7
slug: scale-cost-qa-cutover
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-10-06
---

# Phase 7 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 17.14.1 (net10.0-windows) |
| **Config file** | none — `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~<touched area>&Category!=Render&Category!=Scale"` |
| **Full suite command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render&Category!=Scale"` |
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` |
| **Scale command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Scale"` |
| **Estimated runtime** | ~60-90 seconds (full); Scale: minutes |

---

## Sampling Rate

- **After every task commit:** Run the quick filter for the touched area
- **After every plan wave:** Run full suite; UI waves also Render; batch/scale waves also Scale
- **Before `/gsd-verify-work`:** Full + Render + Scale green, render PNGs inspected at 144 DPI
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

Task IDs are `{plan}-T{n}`. Each task runs its filtered command after commit; the full suite (`Category!=Render&Category!=Scale`) runs at each wave end, Render after UI waves (2, 5, 6, 7) and Scale after waves 4 and 8. "❌ W0" means the task creates the test file it verifies with (tdd tasks write the test first).

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 07-01-T1 | 07-01 | 1 | PROV-07 | T-07-01..T-07-04 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~TokenUsageTests\|FullyQualifiedName~ModelCatalogTests"` | ❌ W0 | ⬜ pending |
| 07-01-T2 | 07-01 | 1 | PROV-07 | T-07-01..T-07-04 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Ai&Category!=Render&Category!=Scale"` | ✅ | ⬜ pending |
| 07-02-T1 | 07-02 | 2 | RUN-05, PROV-07 | T-07-05..T-07-08 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Ai.Anthropic\|FullyQualifiedName~EvidencePromptsTests"` | ✅ | ⬜ pending |
| 07-02-T2 | 07-02 | 2 | RUN-05, PROV-07 | T-07-05..T-07-08 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Ai&Category!=Render&Category!=Scale"` | ✅ | ⬜ pending |
| 07-03-T1 | 07-03 | 1 | QA-02, OUT-03 | T-07-09..T-07-11 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~NonRelevantSkeleton\|FullyQualifiedName~DistributionParity\|FullyQualifiedName~CorpusPlanValidator\|FullyQualifiedName~CorpusPlanner"` | ✅ | ⬜ pending |
| 07-03-T2 | 07-03 | 1 | QA-02, OUT-03 | T-07-09..T-07-11 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~InviteScheduler\|FullyQualifiedName~ThreadUnitOfWork\|FullyQualifiedName~AttachmentUnitOfWork"` | ✅ | ⬜ pending |
| 07-03-T3 | 07-03 | 1 | QA-02, OUT-03 | T-07-09..T-07-11 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationReset"` | ✅ | ⬜ pending |
| 07-04-T1 | 07-04 | 1 | PLAT-05, OUT-03 | T-07-12..T-07-13 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ThemeSanitizerTests"` | ❌ W0 | ⬜ pending |
| 07-04-T2 | 07-04 | 1 | PLAT-05, OUT-03 | T-07-12..T-07-13 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ComposeHtml\|FullyQualifiedName~EmailComposer\|FullyQualifiedName~MailboxWriter\|FullyQualifiedName~BulkMail\|FullyQualifiedName~HtmlEmailFormatter\|FullyQualifiedName~PeopleDirectory\|FullyQualifiedName~OrganizationTheme\|FullyQualifiedName~ThemeSanitizer"` | ✅ | ⬜ pending |
| 07-05-T1 | 07-05 | 2 | PROV-07 | T-07-14..T-07-16 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationJournal"` | ✅ | ⬜ pending |
| 07-05-T2 | 07-05 | 2 | PROV-07 | T-07-14..T-07-16 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "(FullyQualifiedName~ReelDiscovery.Tests.Evidence\|FullyQualifiedName~ReelDiscovery.Tests.Corpus\|FullyQualifiedName~ReelDiscovery.Tests.Ai)&Category!=Render&Category!=Scale"` | ✅ | ⬜ pending |
| 07-06-T1 | 07-06 | 2 | PROV-07, RUN-05, RUN-03 | T-07-17..T-07-18 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationCostEstimator\|FullyQualifiedName~GenerationRunState\|FullyQualifiedName~GenerateCorpus&Category!=Render"` | ✅ | ⬜ pending |
| 07-07-T1 | 07-07 | 2 | RUN-03, RUN-04 | T-07-19..T-07-22 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~RequestIdentityTests"` | ❌ W0 | ⬜ pending |
| 07-07-T2 | 07-07 | 2 | RUN-03, RUN-04 | T-07-19..T-07-22 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~BatchLedgerTests\|FullyQualifiedName~RequestIdentityTests"` | ❌ W0 | ⬜ pending |
| 07-08-T1 | 07-08 | 3 | RUN-03, RUN-04 | T-07-23..T-07-28 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~BatchingLlmProviderTests"` | ❌ W0 | ⬜ pending |
| 07-08-T2 | 07-08 | 3 | RUN-03, RUN-04 | T-07-23..T-07-28 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~BatchingLlmProviderTests\|FullyQualifiedName~BatchLedgerTests"` | ❌ W0 | ⬜ pending |
| 07-08-T3 | 07-08 | 3 | RUN-03, RUN-04 | T-07-23..T-07-28 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ThreadRenderer\|FullyQualifiedName~FillerRenderer\|FullyQualifiedName~AttachmentRenderer\|FullyQualifiedName~EvidencePrompts\|FullyQualifiedName~ReelDiscovery.Tests.Ai"` | ✅ | ⬜ pending |
| 07-09-T1 | 07-09 | 3 | RUN-03 | T-07-29..T-07-32 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~AnthropicBatchWireTests"` | ❌ W0 | ⬜ pending |
| 07-09-T2 | 07-09 | 3 | RUN-03 | T-07-29..T-07-32 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~OpenAIBatchWireTests\|FullyQualifiedName~AnthropicBatchWireTests\|FullyQualifiedName~ReelDiscovery.Tests.Ai.OpenAI"` | ❌ W0 | ⬜ pending |
| 07-10-T1 | 07-10 | 4 | RUN-03, RUN-04 | T-07-33..T-07-36 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationSnapshot\|FullyQualifiedName~GenerationRunnerTests\|FullyQualifiedName~WizardStateProvider"` | ✅ | ⬜ pending |
| 07-10-T2 | 07-10 | 4 | RUN-03, RUN-04 | T-07-33..T-07-36 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "(FullyQualifiedName~ReelDiscovery.Tests.Evidence\|FullyQualifiedName~ReelDiscovery.Tests.Corpus\|FullyQualifiedName~ReelDiscovery.Tests.Ai)&Category!=Render&Category!=Scale"` | ❌ W0 | ⬜ pending |
| 07-11-T1 | 07-11 | 5 | RUN-03, RUN-04, PROV-07 | T-07-52..T-07-54 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationUiTextTests"` | ✅ | ⬜ pending |
| 07-11-T2 | 07-11 | 5 | RUN-03, RUN-04, PROV-07 | T-07-52..T-07-54 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerateCorpus&Category!=Render\|FullyQualifiedName~GenerationUiText\|FullyQualifiedName~WizardShell&Category!=Render"` | ✅ | ⬜ pending |
| 07-11-T3 | 07-11 | 5 | RUN-03, RUN-04, PROV-07 | T-07-52..T-07-54 | see plan threat model | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render&FullyQualifiedName~GenerateCorpusRenderTests"` | ✅ | ⬜ pending |
| 07-12-T1 | 07-12 | 2 | PROV-07 | T-07-37..T-07-39 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationUiTextTests"` | ✅ | ⬜ pending |
| 07-12-T2 | 07-12 | 2 | PROV-07 | T-07-37..T-07-39 | see plan threat model | render | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~MediaPricesDialogTests\|FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~ModelCatalog" && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render&(FullyQualifiedName~DialogRenderTests\|FullyQualifiedName~WizardShellRenderTests)"` | ✅ | ⬜ pending |
| 07-13-T1 | 07-13 | 5 | QA-01, QA-03 | T-07-48..T-07-51 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ThreadInvalidation\|FullyQualifiedName~GenerationJournal\|FullyQualifiedName~GenerationRunState"` | ❌ W0 | ⬜ pending |
| 07-13-T2 | 07-13 | 5 | QA-01, QA-03 | T-07-48..T-07-51 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "(FullyQualifiedName~ReelDiscovery.Tests.Evidence\|FullyQualifiedName~ReelDiscovery.Tests.Corpus\|FullyQualifiedName~ReelDiscovery.Tests.Ai)&Category!=Render&Category!=Scale"` | ✅ | ⬜ pending |
| 07-14-T1 | 07-14 | 2 | QA-02 | T-07-40..T-07-42 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~QaReportStoreTests\|FullyQualifiedName~LogisticRegressionTests"` | ❌ W0 | ⬜ pending |
| 07-14-T2 | 07-14 | 2 | QA-02 | T-07-40..T-07-42 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Qa"` | ❌ W0 | ⬜ pending |
| 07-15-T1 | 07-15 | 6 | QA-03, QA-02 | T-07-55..T-07-57 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~CorpusScansTests\|FullyQualifiedName~GenerationRunnerTests"` | ❌ W0 | ⬜ pending |
| 07-15-T2 | 07-15 | 6 | QA-03, QA-02 | T-07-55..T-07-57 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Qa\|FullyQualifiedName~GenerateCorpus&Category!=Render"` | ❌ W0 | ⬜ pending |
| 07-16-T1 | 07-16 | 3 | QA-01, RUN-05 | T-07-43..T-07-47 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~BlindReviewTests\|FullyQualifiedName~CaseSchemaTests\|FullyQualifiedName~WizardStateProvider"` | ❌ W0 | ⬜ pending |
| 07-16-T2 | 07-16 | 3 | QA-01, RUN-05 | T-07-43..T-07-47 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Qa\|FullyQualifiedName~CaseSchemaTests"` | ❌ W0 | ⬜ pending |
| 07-17-T1 | 07-17 | 6 | QA-01, QA-02, QA-03 | T-07-58..T-07-60 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~QaUiTextTests"` | ❌ W0 | ⬜ pending |
| 07-17-T2 | 07-17 | 6 | QA-01, QA-02, QA-03 | T-07-58..T-07-60 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~QaFindingsDialogTests\|FullyQualifiedName~QaUiTextTests"` | ❌ W0 | ⬜ pending |
| 07-17-T3 | 07-17 | 6 | QA-01, QA-02, QA-03 | T-07-58..T-07-60 | see plan threat model | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render&FullyQualifiedName~QaDialogRenderTests"` | ❌ W0 | ⬜ pending |
| 07-18-T1 | 07-18 | 7 | QA-01, QA-02, QA-03, PROV-07 | T-07-61..T-07-64 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~CaseSummary&Category!=Render\|FullyQualifiedName~ReelDiscovery.Tests.Qa"` | ❌ W0 | ⬜ pending |
| 07-18-T2 | 07-18 | 7 | QA-01, QA-02, QA-03, PROV-07 | T-07-61..T-07-64 | see plan threat model | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render&FullyQualifiedName~CaseSummaryRenderTests"` | ✅ | ⬜ pending |
| 07-19-T1 | 07-19 | 6 | OUT-03, PLAT-05 | T-07-65..T-07-67 | see plan threat model | render | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~WizardFlow\|FullyQualifiedName~CaseDraft\|FullyQualifiedName~WizardShell\|FullyQualifiedName~KeyTestGuard\|FullyQualifiedName~WizardStateProvider" && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render&FullyQualifiedName~WizardShellRenderTests"` | ✅ | ⬜ pending |
| 07-19-T2 | 07-19 | 6 | OUT-03, PLAT-05 | T-07-65..T-07-67 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ApiKeyStore\|FullyQualifiedName~PlatformTests"` | ✅ | ⬜ pending |
| 07-20-T1 | 07-20 | 7 | PLAT-05, OUT-03 | T-07-68..T-07-70 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render&Category!=Scale"` | ✅ | ⬜ pending |
| 07-20-T2 | 07-20 | 7 | PLAT-05, OUT-03 | T-07-68..T-07-70 | see plan threat model | unit/wire/integration | `dotnet clean ReelDiscovery.csproj && dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render&Category!=Scale" && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` | ✅ | ⬜ pending |
| 07-21-T1 | 07-21 | 8 | OUT-03, RUN-03 | T-07-71..T-07-72 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~GenerationRunnerTests"` | ✅ | ⬜ pending |
| 07-21-T2 | 07-21 | 8 | OUT-03, RUN-03 | T-07-71..T-07-72 | see plan threat model | scale | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Scale" && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render&Category!=Scale"` | ❌ W0 | ⬜ pending |
| 07-22-T1 | 07-22 | 9 | RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05 | T-07-73..T-07-75 | see plan threat model | unit/wire/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=LiveSpike"` | ❌ W0 | ⬜ pending |
| 07-22-T2 | 07-22 | 9 | RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05 | T-07-73..T-07-75 | see plan threat model | manual | `manual checkpoint` | ✅ | ⬜ pending |
| 07-22-T3 | 07-22 | 9 | RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05 | T-07-73..T-07-75 | see plan threat model | unit/wire/integration | `dotnet build ReelDiscovery.csproj -warnaserror && dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Ai\|FullyQualifiedName~GenerationCostEstimator"` | ✅ | ⬜ pending |
| 07-22-T4 | 07-22 | 9 | RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05 | T-07-73..T-07-75 | see plan threat model | manual | `manual checkpoint` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs` — scripted submit/poll/results, errored/expired items, batch-level failure
- [ ] `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`, `BatchParityTests.cs`, `AnthropicBatchWireTests.cs`, `OpenAIBatchWireTests.cs`, `BatchLedgerTests.cs`, `TokenUsageTests.cs`
- [ ] `ReelDiscovery.Tests/Qa/BlindReviewTests.cs`, `LeakageAuditTests.cs`, `CorpusScansTests.cs`
- [ ] `ReelDiscovery.Tests/Evidence/ThreadInvalidationTests.cs`
- [ ] `ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs` with `[Trait("Category","Scale")]`

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| OpenAI Chat Completions implicit cache-write billing/reporting on GPT-6 | RUN-05 / PROV-07 | Needs live key; docs ambiguous | Live spike: two identical >1,024-token prompts, record `usage` JSON |
| Batch round-trip on each real provider | RUN-03 / RUN-04 | Needs live keys and real batch latency | Small corpus run with batch mode on, restart mid-run, confirm collection |
| Wizard UI at 150% DPI | PLAT-05 / PROV-07 | Visual | Inspect Render PNGs at 144 DPI |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [ ] Feedback latency < 90s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
