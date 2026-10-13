---
phase: 9
slug: edit-safety-navigation
status: complete
nyquist_compliant: true
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
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&(FullyQualifiedName~GenerateCorpusRenderTests\|FullyQualifiedName~CaseSummaryRenderTests\|FullyQualifiedName~CorpusReviewPlanRenderTests\|FullyQualifiedName~CaseGuideRenderTests\|FullyQualifiedName~DiscoveryDocumentsRenderTests\|FullyQualifiedName~CorpusSettingsRenderTests\|FullyQualifiedName~WizardShellRenderTests)"` |
| **Estimated runtime** | quick ~30-90 s; full ~11 min |

---

## Sampling Rate

- **After every task commit:** Run the quick command filtered to the touched test classes
- **After every plan wave:** Quick run plus `FullyQualifiedName~WizardFlowTests|FullyQualifiedName~CaseSummary|FullyQualifiedName~Discovery`, plus Render when UI changed
- **Before `/gsd-verify-work`:** Full suite and Render must be green; PNGs inspected at 144 DPI (this machine renders at 120 DPI; the 150% check is the manual walkthrough in 09-10)
- **Max feedback latency:** 90 seconds (quick run)

---

## Per-Task Verification Map

Filled by the planner/executor from the requirement → test map in `09-RESEARCH.md` (§ Validation Architecture).

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 9-01-01 | 01 | 1 | GUIDE-09 / PLAN-06 / PLAN-07 / KEY-03 | T-09-01, T-09-02 | Staleness recomputed from live guide hash; GuideChanged card decided before Ready | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CorpusApprovalTests\|FullyQualifiedName~GenerationUiTextTests"` | ✅ | ✅ green |
| 9-01-02 | 01 | 1 | GUIDE-09 / PLAN-07 | T-09-03 | Copy shows counts only | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseGuideUiLogicTests"` | ✅ | ✅ green |
| 9-02-01 | 02 | 1 | ANC-05 / ANC-01 / ANC-03 | T-09-04, T-09-05 | Exhibit stamps normalized; DocIds never build paths; size cap before deserialize | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryStalenessTests\|FullyQualifiedName~DiscoveryStoreTests\|FullyQualifiedName~DiscoveryGeneratorTests"` | ✅ | ✅ green |
| 9-02-02 | 02 | 1 | ANC-05 / ANC-01 / ANC-03 | T-09-06, T-09-07 | Stamps reuse existing off-UI-thread replay | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CorpusDetectorTests\|FullyQualifiedName~DiscoveryStalenessTests"` | ✅ | ✅ green |
| 9-03-01 | 03 | 1 | ANC-01 / PLAN-07 | T-09-08 | No paid planning run without explicit request | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~ReviewPlanLogicTests\|FullyQualifiedName~CorpusUiTextTests"` | ✅ | ✅ green |
| 9-03-02 | 03 | 1 | ANC-01 / PLAN-07 | T-09-09, T-09-10 | One-shot PlanOnEntryOnce, in-memory only; approved plan never re-planned silently | unit/step/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~ReviewPlanLogicTests\|FullyQualifiedName~CaseScenarioUiLogicTests\|FullyQualifiedName~CorpusSettingsStepLogicTests\|FullyQualifiedName~WizardFlowTests\|(Category=Render&FullyQualifiedName~WizardShellRenderTests)"` | ✅ | ✅ green |
| 9-04-01 | 04 | 2 | GUIDE-09 / PLAN-06 / PLAN-07 / KEY-03 | T-09-11, T-09-12, T-09-13 | Stale guide never feeds a run; refusal before EnsureFolder; default card branch is a no-op | unit/step | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerateCorpusStepTests\|FullyQualifiedName~GenerationUiTextTests"` | ✅ | ✅ green |
| 9-04-02 | 04 | 2 | GUIDE-09 / QA-01 | — | N/A | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&FullyQualifiedName~GenerateCorpusRenderTests"` | ✅ | ✅ green |
| 9-05-01 | 05 | 2 | GUIDE-09 / PLAN-07 | T-09-14, T-09-15 | Withdrawal on save funnel, reopen and Case Summary entry | unit/step | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseScenarioUiLogicTests\|FullyQualifiedName~CaseGuideStepLogicTests\|FullyQualifiedName~CaseSummaryLogicTests\|FullyQualifiedName~CorpusApprovalTests"` | ✅ | ✅ green |
| 9-05-02 | 05 | 2 | QA-01 / QA-03 / RUN-06 | T-09-16 | Generate Corpus button only navigates; entry gate decides | unit/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~CaseSummaryLogicTests\|FullyQualifiedName~CaseSummaryGenerationTests\|(Category=Render&FullyQualifiedName~CaseSummaryRenderTests)"` | ✅ | ✅ green |
| 9-06-01 | 06 | 2 | GUIDE-09 | T-09-17, T-09-18, T-09-19 | Notice driven by recomputed staleness and cached counts only | unit/step | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CaseGuideUiLogicTests\|FullyQualifiedName~CaseGuideStepLogicTests"` | ✅ | ✅ green |
| 9-06-02 | 06 | 2 | GUIDE-09 / QA-01 | — | N/A | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&FullyQualifiedName~CaseGuideRenderTests"` | ✅ | ✅ green |
| 9-07-01 | 07 | 2 | ANC-01 / PLAN-07 | T-09-20, T-09-22 | Skip never sets PlanOnEntryOnce; guarded while planning/approving | unit/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~ReviewPlanLogicTests\|(Category=Render&FullyQualifiedName~CorpusReviewPlanRenderTests)"` | ✅ | ✅ green |
| 9-07-02 | 07 | 2 | ANC-01 / PLAN-07 | T-09-20, T-09-21 | Skip runs ValidateFields before navigating | unit/step/render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CorpusUiTextTests\|FullyQualifiedName~CorpusSettingsStepLogicTests\|FullyQualifiedName~WizardFlowTests\|(Category=Render&FullyQualifiedName~CorpusSettingsRenderTests)"` | ✅ | ✅ green |
| 9-08-01 | 08 | 3 | ANC-01 / ANC-03 / ANC-05 | T-09-23, T-09-24, T-09-25 | Stamps from existing background replay; no sidecar writes on reset/invalidation | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~DiscoveryStepModelTests\|FullyQualifiedName~DiscoveryUiTextTests\|FullyQualifiedName~DiscoveryStalenessTests\|FullyQualifiedName~CaseSummaryQaTests\|FullyQualifiedName~CaseSummaryLogicTests"` | ✅ | ✅ green |
| 9-08-02 | 08 | 3 | ANC-05 / QA-01 | — | N/A | render | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&FullyQualifiedName~DiscoveryDocumentsRenderTests"` | ✅ | ✅ green |
| 9-09-01 | 09 | 4 | ANC-05 / QA-01 / QA-03 | T-09-27, T-09-28 | Destructive confirms warn about cited discovery documents; default No | unit | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~QaUiTextTests"` | ✅ | ✅ green |
| 9-09-02 | 09 | 4 | ANC-05 / QA-01 / QA-03 | T-09-26, T-09-27 | Sidecar read inside existing Task.Run, size-capped, never throws | unit/step | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~QaFindingsDialogTests\|FullyQualifiedName~CaseSummaryQaTests\|FullyQualifiedName~GenerateCorpusStepTests\|FullyQualifiedName~QaUiTextTests"` | ✅ | ✅ green |
| 9-10-01 | 10 | 5 | GUIDE-09 / PLAN-06 / PLAN-07 / KEY-03 / ANC-01 / ANC-03 / ANC-05 / QA-01 / QA-03 / RUN-06 | T-09-29, T-09-30 | Grep gates re-assert cost-safety invariants on the final tree | full + render + grep | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` | ✅ | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements (FakeLlmProvider, DiscoveryCorpusFixture, render harness, ClippingDetector).

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| New banners/buttons readable at 150% DPI in the running app | QA-01, GUIDE-09 | Render tests cover clipping, not visual judgment | Run app at 150% scaling; edit guide after approval; visit Generate Corpus, Case Summary, Review Plan (no plan), Case Guide. **Status: ⬜ pending — not performed; deferred by user 2026-10-12 (steps 1-10 in 09-10-SUMMARY.md)** |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 90s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** automated gate passed 2026-10-11; 150% DPI walkthrough deferred by user 2026-10-12 — outstanding (see 09-10-SUMMARY.md)
