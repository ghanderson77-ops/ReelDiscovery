---
phase: 9
slug: edit-safety-navigation
status: draft
nyquist_compliant: false
wave_0_complete: true
created: 2026-10-11
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 (net10.0-windows test project) |
| **Config file** | none — `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~<TouchedTestClass>"` |
| **Full suite command** | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` |
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&(FullyQualifiedName~GenerateCorpusRenderTests\|FullyQualifiedName~CaseSummaryRenderTests\|FullyQualifiedName~CorpusReviewPlanRenderTests\|FullyQualifiedName~CaseGuideRenderTests\|FullyQualifiedName~DiscoveryDocumentsRenderTests)"` |
| **Estimated runtime** | quick ~30-90 s; full ~11 min |

---

## Sampling Rate

- **After every task commit:** Run the quick command filtered to the touched test classes
- **After every plan wave:** Quick run plus `FullyQualifiedName~WizardFlowTests|FullyQualifiedName~CaseSummary|FullyQualifiedName~Discovery`, plus Render when UI changed
- **Before `/gsd-verify-work`:** Full suite and Render must be green; PNGs inspected at 144 DPI
- **Max feedback latency:** 90 seconds (quick run)

---

## Per-Task Verification Map

Filled by the planner/executor from the requirement → test map in `09-RESEARCH.md` (§ Validation Architecture).

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 9-xx-xx | xx | x | GUIDE-09 / PLAN-06 / PLAN-07 / KEY-03 | — | Stale guide never feeds a generation run | unit/step | quick run | ✅ | ⬜ pending |
| 9-xx-xx | xx | x | ANC-01 | — | No paid planning run without explicit request | unit | quick run | ✅ | ⬜ pending |
| 9-xx-xx | xx | x | ANC-01 / ANC-03 / ANC-05 | — | N/A | unit/integration | quick run | ✅ | ⬜ pending |
| 9-xx-xx | xx | x | QA-01 / QA-03 / RUN-06 | — | N/A | unit/render | quick + render | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements (FakeLlmProvider, DiscoveryCorpusFixture, render harness, ClippingDetector).

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| New banners/buttons readable at 150% DPI in the running app | QA-01, GUIDE-09 | Render tests cover clipping, not visual judgment | Run app at 150% scaling; edit guide after approval; visit Generate Corpus, Case Summary, Review Plan (no plan), Case Guide |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
