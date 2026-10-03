---
phase: 5
slug: relevant-evidence-generation-narrative-ledger
status: draft
nyquist_compliant: false
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

| Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|-------------|----------|-----------|----------------------------|-------------|--------|
| CORP-01 | Prompt excludes facts dated after the thread or unknown to participants | unit | `FullyQualifiedName~KnowledgeGateTests` | ❌ W0 | ⬜ pending |
| CORP-01/02 | Validator flags future/unknown/variant values; retry then keep-best with Warnings | unit + integration | `FullyQualifiedName~RenderValidatorTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 | ⬜ pending |
| CORP-03/04 | Subtlety mix exact and stable; hot messages Explicit; legal-term scan | unit | `FullyQualifiedName~RenderAssignmentsTests\|FullyQualifiedName~RenderValidatorTests` | ❌ W0 | ⬜ pending |
| CORP-05 | Privileged labeling split; counsel signature verbatim | unit | `FullyQualifiedName~EmailComposerTests` | ❌ W0 | ⬜ pending |
| CORP-06 | Message-ID / In-Reply-To / References / RE:FW: / Date offset in parsed EML | unit | `FullyQualifiedName~EmailComposerTests\|FullyQualifiedName~MailboxWriterTests` | ❌ W0 | ⬜ pending |
| CORP-07 | Signatures + length bands | unit | `FullyQualifiedName~EmailComposerTests\|FullyQualifiedName~RenderAssignmentsTests` | ❌ W0 | ⬜ pending |
| CORP-08 | Ledger merge (IDs, dates, knownBy, dedupe) | unit | `FullyQualifiedName~LedgerMergerTests` | ❌ W0 | ⬜ pending |
| CORP-09 | Ledger selection (earlier-wave, known, ranked, capped) | unit | `FullyQualifiedName~LedgerSelectorTests` | ❌ W0 | ⬜ pending |
| CORP-10 | Deterministic chronological waves; ledger contradiction flagged | unit | `FullyQualifiedName~WavePlannerTests\|FullyQualifiedName~RenderValidatorTests` | ❌ W0 | ⬜ pending |
| CORP-11 | Ledger round-trip in case.json; story-so-far docx valid | unit | `FullyQualifiedName~StorySoFarDocxTests\|FullyQualifiedName~GenerationStateTests` | ❌ W0 | ⬜ pending |
| OUT-01 | Custodian Sent/Inbox copies, safe paths only | unit | `FullyQualifiedName~MailboxWriterTests` | ❌ W0 | ⬜ pending |
| OUT-02 | X-ReelDiscovery-DocID matches plan + key | integration | `FullyQualifiedName~GenerationRunnerTests` | ❌ W0 | ⬜ pending |
| RUN-01/06 | Cancel/kill keeps completed threads; Continue writes only the rest | integration | `FullyQualifiedName~GenerationRunnerTests\|FullyQualifiedName~GenerationJournalTests` | ❌ W0 | ⬜ pending |
| RUN-02 | Single failure logged, others written; fatal stops cleanly | integration | `FullyQualifiedName~GenerationRunnerTests` | ❌ W0 | ⬜ pending |
| RUN-07 | Snapshot updates without UI access; runner off UI thread | unit + manual | `FullyQualifiedName~GenerationSnapshotTests` | ❌ W0 | ⬜ pending |
| UI | GenerationUiText copy + entry decisions; 8 render states | unit + render | `FullyQualifiedName~GenerationUiTextTests` / `Category=Render&FullyQualifiedName~GenerateCorpusRenderTests` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Evidence/EvidenceTestData.cs` — approved-plan fixture (hot, privileged, forward, external-sender, multi-custodian threads) and a `FakeLlmProvider` responder that answers `ThreadRenderDto` JSON by thread ID with injectable violations
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

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
