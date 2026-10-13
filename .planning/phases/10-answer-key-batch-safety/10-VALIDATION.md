---
phase: 10
slug: answer-key-batch-safety
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-10-12
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 (net10.0-windows test project) |
| **Config file** | none — `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~<TouchedTestClass>"` |
| **Full suite command** | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` |
| **Scale command (once, D-19)** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Scale"` |
| **Estimated runtime** | quick ~30-90 s; full ~11 min |

---

## Sampling Rate

- **After every task commit:** quick command filtered to the touched test classes.
- **After every plan wave:** `FullyQualifiedName~AnswerKey|FullyQualifiedName~Batch|FullyQualifiedName~GenerateCorpusStepTests|FullyQualifiedName~CorpusApprovalTests|FullyQualifiedName~PlatformTests`, plus the Render command from 09-VALIDATION.md when banner copy changes.
- **Before `/gsd-verify-work`:** the full suite plus one Scale run must be green.
- **Max feedback latency:** 90 seconds (quick run).

---

## Per-Task Verification Map

Filled at the phase gate (10-11) from the tasks of plans 10-01..10-10. Gate run on 2026-10-12: full suite 4683 passed, Scale 2 passed, render suites 61 passed (renders at 120 DPI on this machine, not 144).

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | QA-01 | T-10-01, T-10-02, T-10-04 | Answer key read back with a size cap and canonical labels only | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~AnswerKeyReaderTests|FullyQualifiedName~AnswerKeyCsvWriterTests|FullyQualifiedName~AnswerKeyXlsxWriterTests"` | ✅ | ✅ green |
| 10-01-02 | 01 | 1 | QA-01 | T-10-03, T-10-05 | Relabel sidecar path is constant; tolerant capped read; atomic write | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~AnswerKeyRelabelStoreTests"` | ✅ | ✅ green |
| 10-01-03 | 01 | 1 | PLAT-05 | — | Legacy Attachment/AttachmentComplexity removed | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~PlatformTests"` | ✅ | ✅ green |
| 10-02-01 | 02 | 1 | RUN-04 / PROV-07 | T-10-09 | Ledger counts by provider; unused results priced, never $0 | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~BatchLedgerTests|FullyQualifiedName~BatchingLlmProviderTests"` | ✅ | ✅ green |
| 10-02-02 | 02 | 1 | RUN-03 / PROV-01 | T-10-06, T-10-07, T-10-08 | Other-provider batches resolved or Blocked; foreign ids never delivered | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~BatchingLlmProviderTests|FullyQualifiedName~BatchLedgerTests"` | ✅ | ✅ green |
| 10-03-01 | 03 | 2 | QA-01 / KEY-03 | T-10-10, T-10-11 | Wave 0 relabel lifecycle tests (RED) and funnel surface | build | `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts` | ✅ | ✅ green |
| 10-03-02 | 03 | 2 | QA-01 / KEY-03 | T-10-10 to T-10-15 | Harvest/vote/apply/snapshot/backup; locked file never overwritten; per-folder gate | unit/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~AnswerKeyRelabel|FullyQualifiedName~AnswerKeyReaderTests|FullyQualifiedName~AnswerKeyBuilderTests|FullyQualifiedName~AnswerKeyXlsxWriterTests|FullyQualifiedName~AnswerKeyCsvWriterTests"` | ✅ | ✅ green |
| 10-04-01 | 04 | 2 | RUN-04 / PROV-07 | T-10-16, T-10-17 | Stale stages drained without a Cancelled ledger line; bounded wait | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~BatchingLlmProviderTests"` | ✅ | ✅ green |
| 10-04-02 | 04 | 2 | RUN-03 / PLAT-05 | T-10-18 | Runner drains before wave 0; end-of-run close path deleted | integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~BatchParityTests|FullyQualifiedName~BatchingLlmProviderTests|FullyQualifiedName~GenerationRunnerTests"` | ✅ | ✅ green |
| 10-05-01 | 05 | 3 | QA-01 / KEY-03 | T-10-21 | Generation outputs and thread invalidation write the key through the funnel | integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~AnswerKeyRelabel|FullyQualifiedName~ThreadInvalidationTests|FullyQualifiedName~AnswerKeyOverlayTests|FullyQualifiedName~GenerationRunnerTests|FullyQualifiedName~GenerationResetTests"` | ✅ | ✅ green |
| 10-05-02 | 05 | 3 | QA-01 / KEY-03 | T-10-19, T-10-20 | Approval through the funnel; source-scan guard blocks writer bypass | unit/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CorpusApprovalTests|FullyQualifiedName~PlatformTests|FullyQualifiedName~ReviewPlanLogicTests"` | ✅ | ✅ green |
| 10-06-01 | 06 | 2 | PROV-01 / PROV-07 | T-10-24 | Banner other-provider clause and missing-key notice use fixed copy | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationUiTextTests"` | ✅ | ✅ green |
| 10-06-02 | 06 | 2 | RUN-04 / PROV-01 | T-10-22 | Step counts by provider at all call sites; keys never shown | integration/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerateCorpusStepTests|FullyQualifiedName~GenerateCorpusRenderTests"` | ✅ | ✅ green |
| 10-06-03 | 06 | 2 | RUN-04 / PROV-07 | T-10-23 | Start Over records unused collected batch spend once | integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerateCorpusStepTests|FullyQualifiedName~BatchParityTests|FullyQualifiedName~WizardStateProviderTests"` | ✅ | ✅ green |
| 10-07-01 | 07 | 3 | RUN-03 / PROV-07 | T-10-26 | Activity and refusal copy fixed; FailureMessage never displayed | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationUiTextTests|FullyQualifiedName~GenerationRunnerTests|FullyQualifiedName~GenerationSnapshotTests"` | ✅ | ✅ green |
| 10-07-02 | 07 | 3 | RUN-03 / RUN-04 / PROV-01 | T-10-25, T-10-27, T-10-28 | Run refused before any gateway call when resolution is Blocked; re-entry guarded | integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerateCorpusStepTests"` | ✅ | ✅ green |
| 10-08-01 | 08 | 4 | QA-01 / KEY-03 | T-10-30 | Damaged-key backup surfaced on Generate Corpus and in the QA footer | unit/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationUiTextTests|FullyQualifiedName~QaFindingsDialogTests|FullyQualifiedName~GenerateCorpusStepTests"` | ✅ | ✅ green |
| 10-08-02 | 08 | 4 | QA-01 / KEY-03 | T-10-29 | Re-approval passes the run-state overlay only from a readable same-plan journal | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~ReviewPlanLogicTests|FullyQualifiedName~CorpusApprovalTests"` | ✅ | ✅ green |
| 10-09-01 | 09 | 5 | QA-01 | T-10-31 | Relabel kept/dropped clauses in dialog copy | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~QaUiTextTests|FullyQualifiedName~GenerationUiTextTests|FullyQualifiedName~CorpusUiTextTests"` | ✅ | ✅ green |
| 10-09-02 | 09 | 5 | QA-01 | T-10-32 | Relevance-column guidance in tooltip and XLSX Legend (static inline string) | unit/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~QaUiTextTests|FullyQualifiedName~AnswerKeyXlsxWriterTests|FullyQualifiedName~QaFindingsDialogTests|FullyQualifiedName~QaDialogRenderTests"` | ✅ | ✅ green |
| 10-10-01 | 10 | 6 | QA-01 | T-10-33 | Regenerate Thread and Start Over say relabels are kept, from real counts | unit/integration | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseSummaryQaTests|FullyQualifiedName~QaFindingsDialogTests|FullyQualifiedName~GenerateCorpusStepTests"` | ✅ | ✅ green |
| 10-10-02 | 10 | 6 | QA-01 | T-10-33, T-10-34 | Re-plan dialog and settings notice show the relabel count before the Primary is pressed | unit/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DialogRenderTests|FullyQualifiedName~CorpusSettingsStepLogicTests|FullyQualifiedName~ReviewPlanLogicTests|FullyQualifiedName~CorpusSettingsRenderTests"` | ✅ | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs`: CSV/XLSX read-back variants, including a shared-string workbook built in the test
- [x] `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs`: harvest/vote/apply/snapshot/backup lifecycle
- No new fakes are needed. `FakeBatchTransport` already supports Kind, CancelFailure, NeverEndIds and PollsUntilEnded.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions | Outcome |
|----------|-------------|------------|-------------------|---------|
| Relabel in real Excel (open, edit Relevance, save as xlsx and as csv in a `;` locale) survives Continue and Start Over | QA-01 | Real Excel save behavior is only partly documented | Edit the key in Excel, click Continue, and check that the relabel is still there; repeat after Start Over | pending — deferred (user, 2026-10-13); 150% DPI checks also deferred |
| Cross-provider resolution against the live provider APIs | PROV-01, RUN-03 | Live batch cancellation timing | Optional live UAT with both keys | pending — deferred (user, 2026-10-13) |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 90s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** automated gate passed 2026-10-12; real-Excel round trip and 150% DPI check deferred by user 2026-10-13 — outstanding
