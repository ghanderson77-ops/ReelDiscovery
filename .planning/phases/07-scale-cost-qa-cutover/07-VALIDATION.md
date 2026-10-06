---
phase: 7
slug: scale-cost-qa-cutover
status: draft
nyquist_compliant: false
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

See 07-RESEARCH.md "Phase Requirements → Test Map". Planner fills task IDs.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | — | — | RUN-03/04/05, PROV-07, QA-01/02/03, OUT-03, PLAT-05 | — | — | unit/wire/integration/render | per research map | ❌ W0 | ⬜ pending |

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

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
