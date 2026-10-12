---
phase: 10
slug: answer-key-batch-safety
status: draft
nyquist_compliant: false
wave_0_complete: false
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

The planner/executor fills this from the requirement → test map in `10-RESEARCH.md` § Validation Architecture.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 10-xx-xx | xx | x | QA-01 / KEY-03 | — | User relabels never silently discarded; damaged file backed up | unit/integration | quick run | ❌ W0 | ⬜ pending |
| 10-xx-xx | xx | x | RUN-03 / RUN-04 / PROV-01 / PROV-07 | — | No double pay across providers; run refused when key missing | integration | quick run | ✅ | ⬜ pending |
| 10-xx-xx | xx | x | PLAT-05 | — | N/A | unit | quick run | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs`: CSV/XLSX read-back variants, including a shared-string workbook built in the test
- [ ] `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs`: harvest/vote/apply/snapshot/backup lifecycle
- No new fakes are needed. `FakeBatchTransport` already supports Kind, CancelFailure, NeverEndIds and PollsUntilEnded.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Relabel in real Excel (open, edit Relevance, save as xlsx and as csv in a `;` locale) survives Continue and Start Over | QA-01 | Real Excel save behavior is only partly documented | Edit the key in Excel, click Continue, and check that the relabel is still there; repeat after Start Over |
| Cross-provider resolution against the live provider APIs | PROV-01, RUN-03 | Live batch cancellation timing | Optional live UAT with both keys |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
