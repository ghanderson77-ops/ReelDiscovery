---
phase: 8
slug: ancillary-discovery-documents
status: draft
nyquist_compliant: false
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

See 08-RESEARCH.md "Phase Requirements → Test Map". Planner fills task IDs.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | — | — | ANC-01..ANC-05 | — | — | unit/render | per research map | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Discovery/DiscoveryTestData.cs` — litigation drafts with counsel cast, memo-criminal and memo-regulatory drafts, small corpus fixture
- [ ] Discovery unit test files listed in 08-RESEARCH.md Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs`
- [ ] Updates to WizardFlowTests (9 steps), WizardShellRenderTests, CaseDraftSerializationTests (v4), CaseSchemaTests, CaseSummary tests, GenerationResetTests, GenerationCostEstimatorTests

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Deposition .docx pagination in Word (25 lines/page, Courier New, page count = code count) | ANC-01 | Word layout engine | Open a generated transcript in Word, compare page count and line 25 placement |
| Live generation with a real provider | ANC-01..05 | Needs API key | Generate one deposition and one set of each written type on a small case |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
