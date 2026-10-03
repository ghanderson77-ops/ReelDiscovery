---
phase: 5
slug: relevant-evidence-generation-narrative-ledger
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-10-03
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 (net10.0-windows) |
| **Config file** | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (no runsettings) |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"` |
| **Full suite command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` (PNGs under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`, inspect at 144 DPI) |
| **Estimated runtime** | ~60 seconds (full, non-render) |

---

## Sampling Rate

- **After every task commit:** Run quick run command
- **After every plan wave:** Run full suite command; UI waves also run the render command
- **Before `/gsd-verify-work`:** Full + render suites green, PNGs inspected at 144 DPI, one live smoke run reviewed
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

Requirement → test map (task IDs filled in by the planner/executor; see 05-RESEARCH.md "Validation Architecture").

| Task ID | Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|---------|-------------|----------|-----------|----------------------------|-------------|--------|
| 05-01-T1 | CORP-08, CORP-11, RUN-01 | Ledger/journal/state models, Normalize, MessageTime | unit | `FullyQualifiedName~GenerationStateTests` | ❌ W0 | ⬜ pending |
| 05-01-T2 | CORP-11 | CaseStage.Generation, format v3, CorpusState.Generation round-trip, fixture | unit | `FullyQualifiedName~ReelDiscovery.Tests.Evidence\|FullyQualifiedName~CaseGuideModelTests\|FullyQualifiedName~CaseDraftSerializationTests` | ❌ W0 | ⬜ pending |
| 05-02-T1 | RUN-07 | Token totals on LlmResult / PlanCallOutcome | unit | `FullyQualifiedName~LlmGatewayTests\|FullyQualifiedName~PlanCallRunnerTests` | ✅ (extend) | ⬜ pending |
| 05-02-T2 | OUT-02, D-05 | Folder map, amount parser, key overlay, legend | unit | `FullyQualifiedName~AnswerKeyOverlayTests\|FullyQualifiedName~PlanNumberingTests` | ❌ W0 | ⬜ pending |
| 05-03-T1 | CORP-10 | Deterministic chronological waves | unit | `FullyQualifiedName~WavePlannerTests` | ❌ W0 | ⬜ pending |
| 05-03-T2 | CORP-03/04/05/07 | Exact subtlety/length/privilege mixes; hot always Explicit (incl. privileged) | unit | `FullyQualifiedName~RenderAssignmentsTests` | ❌ W0 | ⬜ pending |
| 05-04-T1 | CORP-01 | Knowledge gate (facts/values by date and participant) | unit | `FullyQualifiedName~KnowledgeGateTests` | ❌ W0 | ⬜ pending |
| 05-04-T2 | CORP-08/09 | Ledger select (earlier waves, known, ranked, capped) and merge | unit | `FullyQualifiedName~LedgerSelectorTests\|FullyQualifiedName~LedgerMergerTests` | ❌ W0 | ⬜ pending |
| 05-05-T1 | CORP-06/07 | People directory, deterministic org themes | unit | `FullyQualifiedName~PeopleDirectoryTests` | ❌ W0 | ⬜ pending |
| 05-05-T2 | D-16 | Allowlist-only ComposeHtml, legacy unchanged | unit | `FullyQualifiedName~ComposeHtmlTests` | ❌ W0 | ⬜ pending |
| 05-06-T1/T2 | RUN-02/06/07 | GenerationUiText copy, plurals, entry decision | unit | `FullyQualifiedName~GenerationUiTextTests` | ❌ W0 | ⬜ pending |
| 05-07-T1/T2 | CORP-05/06/07 | Message-ID, threading, subjects, signatures, privilege markers | unit | `FullyQualifiedName~EmailComposerTests` | ❌ W0 | ⬜ pending |
| 05-08-T1 | CORP-01/03/04/07/09 | Gated prompt, hot key-document suffix, counsel-scoped system prompt | unit | `FullyQualifiedName~EvidencePromptsTests` | ❌ W0 | ⬜ pending |
| 05-08-T2 | D-01 | ThreadRenderDto strict schema | unit | `FullyQualifiedName~CaseSchemaTests` | ✅ (add types) | ⬜ pending |
| 05-08-T3 | CORP-02 | RenderRepair (incl. word-boundary emphasis negatives) | unit | `FullyQualifiedName~RenderRepairTests` | ❌ W0 | ⬜ pending |
| 05-09-T1/T2 | CORP-01/02/03/10 | V1-V8 checks incl. variants, future knowledge, ledger values and body dates | unit | `FullyQualifiedName~RenderValidatorTests` | ❌ W0 | ⬜ pending |
| 05-10-T1 | OUT-01/02, CORP-06 | Per-custodian EML copies, headers, safe paths | unit | `FullyQualifiedName~MailboxWriterTests` | ❌ W0 | ⬜ pending |
| 05-10-T2 | RUN-01/06 | Journal append/replay (torn line, foreign plan, unsafe paths) | unit | `FullyQualifiedName~GenerationJournalTests` | ❌ W0 | ⬜ pending |
| 05-11-T1/T3 | D-05, RUN-01 | Run state, key overlay, draft applier, output writers | unit | `FullyQualifiedName~GenerationRunStateTests` | ❌ W0 | ⬜ pending |
| 05-11-T2 | CORP-11 | Story-so-far docx valid, grouped by month | unit | `FullyQualifiedName~StorySoFarDocxTests` | ❌ W0 | ⬜ pending |
| 05-12-T1 | RUN-07 | Snapshot thread-safety, bounded events | unit | `FullyQualifiedName~GenerationSnapshotTests` | ❌ W0 | ⬜ pending |
| 05-12-T2 | CORP-02/10, RUN-02 | Retry with feedback, keep best, failures (incl. unexpected exception) isolated | integration | `FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 | ⬜ pending |
| 05-13-T1 | CORP-08-11, OUT-02, RUN-01/02/06/07 | Waves, ledger visibility, per-thread key projection, cancel/continue, fatal, guaranteed final | integration | `FullyQualifiedName~GenerationRunnerTests` | ❌ W0 | ⬜ pending |
| 05-13-T2 | RUN-06 | Start Over deletes only validated generated files | integration | `FullyQualifiedName~GenerationResetTests` | ❌ W0 | ⬜ pending |
| 05-14-T1 | UI | Step layout compiles, no new warnings | build | `dotnet build ReelDiscovery.csproj` | ❌ W0 | ⬜ pending |
| 05-14-T2 | RUN-06 | Journal-rebuilt history, entry decision | unit | `FullyQualifiedName~GenerationHistoryTests` | ❌ W0 | ⬜ pending |
| 05-14-T3 | UI, D-04 | Render states 1-6 at 144 DPI | render | `Category=Render&FullyQualifiedName~GenerateCorpusRenderTests` | ❌ W0 | ⬜ pending |
| 05-15-T1/T2 | RUN-01/02/06/07, CORP-11 | Run, persist, cancel, Start Over, reopen, leave retries, open EML | integration (STA) | `FullyQualifiedName~GenerateCorpusStepTests` | ❌ W0 | ⬜ pending |
| 05-15-T3 | UI | 8-step flow, stage mapping, shell headers | unit + render | `FullyQualifiedName~WizardFlowTests\|FullyQualifiedName~WizardShellRenderTests` | ✅ (update) | ⬜ pending |
| 05-16-T1 | CORP-11, OUT-01 | Case Summary rows (render state 7) + export/open | render + unit | `FullyQualifiedName~CaseSummaryRenderTests\|FullyQualifiedName~CaseSummaryGenerationTests` | ✅/❌ | ⬜ pending |
| 05-16-T2 | UI | Re-plan sentence (render state 8) | unit + render | `FullyQualifiedName~CorpusUiTextTests\|FullyQualifiedName~DialogRenderTests` | ✅ (extend) | ⬜ pending |
| 05-17-T1 | all | Full + render suites | suite | `Category!=Render` then `Category=Render` | ✅ | ⬜ pending |
| 05-17-T2 | CORP-03/04/06/07, RUN-07 | Human: 144-DPI, live run, mail client, responsiveness | manual | n/a (checkpoint) | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Evidence/EvidenceTestData.cs` (05-01-T2 fixture, 05-08-T2 responder) — approved-plan fixture (hot, privileged, forward, external-sender, multi-custodian threads) and a `FakeLlmProvider` responder that answers `ThreadRenderDto` JSON by thread ID with injectable violations
- [ ] New test files listed in the map above
- [ ] `ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs` with snapshot-driven state seeding on the step

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Reading quality (voices, subtlety, tone) | CORP-03/04/07 | Subjective prose quality | Live provider run on a test case; read a sample of relevant threads |
| EML rendering in a mail client | CORP-06 | Visual client behavior | Open several EMLs (reply, forward, privileged) in Outlook/Thunderbird |
| UI responsiveness during live run | RUN-07 | Real latency/threading | Run generation, drag/resize window, watch tiles + cost update |
| 144-DPI inspection | UI | Human visual check | Inspect render PNGs at 150% scaling |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 60s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
