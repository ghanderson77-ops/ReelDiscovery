---
phase: 4
slug: corpus-plan-answer-key
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-10-02
---

# Phase 4 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Source: 04-RESEARCH.md §Validation Architecture.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 17.14.1 (net10.0-windows) |
| **Config file** | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (render tests in non-parallel `RenderCollection`) |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"` |
| **Full suite command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` (PNGs + `.clip.txt` under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`, inspect at 144 DPI) |
| **Estimated runtime** | ~30 s quick, ~120 s full |

---

## Sampling Rate

- **After every task commit:** Run quick run command
- **After every plan wave:** Run full suite command; UI waves also run the render command
- **Before `/gsd-verify-work`:** Full suite + render suite green, PNGs inspected at 144 DPI
- **Max feedback latency:** 30 seconds (quick)

---

## Per-Task Verification Map

Task IDs are filled in by the planner/executor; the requirement → test map is fixed here.

| Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|-------------|----------|-----------|----------------------------|-------------|--------|
| PLAN-01 | Size presets/clamp, ratio split math, low-relevant warning, window default/clamp, media gating, settings round-trip + Normalize | unit | `FullyQualifiedName~CorpusSettingsTests` | ✅ `Corpus/CorpusSettingsTests.cs`, `Corpus/CorpusSettingsStepLogicTests.cs` | ✅ green |
| PLAN-02 | Every message has SentAt/From/To/Kind/ReplyToSeq; LLM fills text only; unknown ids dropped; re-ask once then template; cancel discards; fatal/refusal handling | unit (FakeLlmProvider) | `FullyQualifiedName~CorpusPlannerTests` | ✅ `Corpus/CorpusPlannerTests.cs`, `Corpus/RelevantThreadFillerTests.cs`, `Corpus/PlanCallRunnerTests.cs`, `Corpus/SubjectSeederTests.cs`, `Corpus/CorpusPromptInvariantTests.cs` | ✅ green |
| PLAN-02 | corpus-plan.json round-trip, PlanId mismatch, size cap, Normalize garbage, format version 2 | unit | `FullyQualifiedName~CorpusPlanStoreTests` | ✅ `Corpus/CorpusPlanStoreTests.cs`, `Corpus/CorpusModelTests.cs` | ✅ green |
| PLAN-03 | Message sum = relevant budget; every fact ≥1 thread; Hot ≥ Key ≥ Supporting; multi-issue threads; chunk windows span issues | unit | `FullyQualifiedName~BudgetAllocatorTests\|FullyQualifiedName~ThreadSkeletonTests` | ✅ `Corpus/BudgetAllocatorTests.cs`, `Corpus/ThreadSkeletonTests.cs`, `Corpus/NonRelevantSkeletonTests.cs`, `Corpus/PlanNumberingTests.cs` | ✅ green |
| PLAN-04 | Holiday vectors, no weekend/holiday/after-hours, DST offsets, Phoenix, monotonic latency, bad TZ fallback | unit | `FullyQualifiedName~BusinessCalendarTests` | ✅ `Corpus/BusinessCalendarTests.cs`, `Corpus/ThreadSchedulerTests.cs` | ✅ green |
| PLAN-05 | Depth/kind/CC/attachment frequencies within tolerance; reply-all/forward recipient rules; only enabled attachment types | unit | `FullyQualifiedName~ThreadShapesTests` | ✅ `Corpus/ThreadShapesTests.cs` | ✅ green |
| PLAN-06 | Each validator rule fires on bad plan, silent on good; never throws; warnings only with fix hints | unit | `FullyQualifiedName~CorpusPlanValidatorTests` | ✅ `Corpus/CorpusPlanValidatorTests.cs` | ✅ green |
| PLAN-07 | Summary counts, month buckets, cost estimate, stale detection/dismiss, approval withdrawn | unit | `FullyQualifiedName~PlanSummaryTests\|FullyQualifiedName~ReviewPlanLogicTests` | ✅ `Corpus/PlanSummaryTests.cs`, `Corpus/ReviewPlanLogicTests.cs`, `Corpus/CorpusApprovalTests.cs`, `Corpus/CorpusUiTextTests.cs`, `Corpus/CorpusControlsTests.cs` | ✅ green |
| PLAN-07 | Corpus Settings, Review Plan (planned/progress/empty/stale), Re-plan dialog — no clipping at 144 DPI | render | `Category=Render&(FullyQualifiedName~CorpusControlsRenderTests\|CorpusSettingsRenderTests\|CorpusReviewPlanRenderTests\|CaseSummaryRenderTests)` | ✅ `Render/CorpusControlsRenderTests.cs`, `Render/CorpusSettingsRenderTests.cs`, `Render/CorpusReviewPlanRenderTests.cs`, `Render/CaseSummaryRenderTests.cs` | ✅ green (86/86 render; 39 Phase 4 PNGs inspected at 144 DPI, 0 clip findings) |
| KEY-01/02/03 | Row count, column order, chronological DocIDs, custodian rules, opaque paths, family relevance propagation | unit | `FullyQualifiedName~AnswerKeyBuilderTests` | ✅ `Corpus/AnswerKeyBuilderTests.cs` | ✅ green |
| KEY-01 | CSV BOM, CRLF, RFC 4180 escaping, formula-injection prefix | unit | `FullyQualifiedName~AnswerKeyCsvWriterTests` | ✅ `Corpus/AnswerKeyCsvWriterTests.cs` | ✅ green |
| KEY-04 | XLSX 3 sheets, OpenXmlValidator 0 errors, frozen pane, autofilter, stats totals, text-only cells | unit | `FullyQualifiedName~AnswerKeyXlsxWriterTests` | ✅ `Corpus/AnswerKeyXlsxWriterTests.cs` | ✅ green |
| KEY-05 | Protocol docx validates; issue codes + criteria + counsel; no DocIDs/labels; fallback prose | unit | `FullyQualifiedName~ReviewProtocolTests` | ✅ `Corpus/ReviewProtocolTests.cs` | ✅ green |
| cross | Strict DTO schemas ≤16 unions; wizard order/stage mapping; content-only guide hash | unit | `FullyQualifiedName~CaseSchemaTests\|FullyQualifiedName~WizardFlowTests\|FullyQualifiedName~CaseGuideModelTests` | ✅ `Case/CaseSchemaTests.cs`, `Case/WizardFlowTests.cs`, `Case/CaseGuideModelTests.cs` (extended) | ✅ green |
| PLAT-03 | All of the above exist and pass | suite | full suite command | ✅ | ✅ green (1968/1968 non-render, 86/86 render) |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `ReelDiscovery.Tests/Corpus/CorpusTestData.cs` — rich fixture (≥2 issues, two time zones incl. America/Phoenix, Saturday + holiday facts, narrow active range, single-knower fact, counsel both sides, Hot/Key/Supporting mix)
- [x] `FakeLlmProvider` (+ `Corpus/PlanFixtures.cs`) — thread-safe (lock) + request-based responder
- [x] Helper building valid thread-plan batch JSON echoing ids found in the prompt
- [x] Test files in the map above under `ReelDiscovery.Tests/Corpus/`
- [x] Render tests in `RenderCollection`: `Render/CorpusControlsRenderTests.cs`, `Render/CorpusSettingsRenderTests.cs`, `Render/CorpusReviewPlanRenderTests.cs`, `Render/CaseSummaryRenderTests.cs`

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Visual quality of Corpus Settings / Review Plan at 150% scaling | PLAN-07 | Aesthetic judgment beyond clipping detector | Open render PNGs at 144 DPI; then run app at 150% and walk Case Guide → Corpus Settings → Review Plan → Approve |
| Plan realism with a live provider | PLAN-03/05 | Needs real LLM output | Plan the "inspired by The Office" case at 1,000 emails; read thread subjects/intents; check histogram and tables |
| Answer key opens cleanly in Excel | KEY-01/04 | Excel UI behavior | Open answer-key.xlsx and .csv by double-click; check frozen header, filters, names render (UTF-8) |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 30s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** approved 2026-10-03 (automated gate + human check at 150%)
