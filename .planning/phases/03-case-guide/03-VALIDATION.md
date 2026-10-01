---
phase: 3
slug: case-guide
status: planned
nyquist_compliant: true
wave_0_complete: false
created: 2026-10-01
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 (Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4) |
| **Config file** | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Case&Category!=Render"` |
| **Full suite command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| **Render check (UI tasks)** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` → PNGs under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/` |
| **Estimated runtime** | ~5 seconds (non-render) |
| **Precondition** | ReelDiscovery.exe must not be running (MSB3027 file lock) |

---

## Sampling Rate

- **After every task commit:** quick run
- **After every plan wave:** full suite (minus Render)
- **UI tasks:** Render tests + inspect the PNGs at 144 DPI before hand-off (D-04)
- **Before `/gsd-verify-work`:** full suite green, 0 build warnings, render PNGs inspected
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

Filled in by the planner. Unit filters run as `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~<filter>"`; render filters as written (`--filter "Category=Render&..."`). Every UI task's acceptance also requires opening the PNGs and checking UI-SPEC DPI rule 10.

| Task ID | Plan | Wave | Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|---------|------|------|-------------|----------|-----------|----------------------------|-------------|--------|
| 03-01-T1 | 03-01 | 1 | GUIDE-01/02/05/06/10 | Model types, CaseDraft persistence fields, preset numbers | unit | `ComplexityPresetTests\|CaseDraftSerializationTests\|CaseDraftTests` | ❌ W1 (created by task) | ⬜ pending |
| 03-01-T2 | 03-01 | 1 | GUIDE-10 | SampleGuidedDraft fixture, round trip, Normalize null-safety, no secrets | unit | `CaseGuideModelTests` | ❌ W1 (created by task) | ⬜ pending |
| 03-02-T1 | 03-02 | 1 | D-04 (Wave 0) | Render harness (STA, PerMonitorV2, 144 DPI), clipping detector, baseline renders | render | `Category=Render&FullyQualifiedName~BaselineRenderTests` | ❌ W1 (created by task) | ⬜ pending |
| 03-02-T2 | 03-02 | 1 | GUIDE-08 (UI infra) | GridStyle, TextFieldHost, DrawStatusDot | unit + render | `SharedUiHelperTests\|SharedHelperRenderTests` | ❌ W1 (created by task) | ⬜ pending |
| 03-03-T1 | 03-03 | 2 | GUIDE-01 | IdAllocator never reuses; IssueExtractor keeps IDs | unit | `IdAllocatorTests\|IssueExtractorTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-03-T2 | 03-03 | 2 | GUIDE-02/03 | Email identities (.com/.gov/personal), signatures, time zones | unit | `CastIdentityTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-04-T1 | 03-04 | 2 | GUIDE-10 | Folder naming: sanitize, reserved names, uniqueness, traversal | unit | `CaseFolderNamingTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-04-T2 | 03-04 | 2 | GUIDE-10 | Atomic autosave, coalescing, SaveNow under the writer lock, load errors, recent list, move | unit | `CaseStoreTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-05-T1 | 03-05 | 2 | GUIDE-11 | Core document .docx, zero OpenXmlValidator errors, control chars | unit | `CaseGuideDocxExporterTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-05-T2 | 03-05 | 2 | GUIDE-11 | Cast and Key Facts .docx, zero validator errors | unit | `CaseGuideDocxExporterTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-06-T1 | 03-06 | 2 | GUIDE-08/09 | All UI copy helpers (Plural, tabs, state cards, banner, Next gate, confirmations) | unit | `CaseGuideUiLogicTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-06-T2 | 03-06 | 2 | GUIDE-08 | D1/D2 dialogs at 144 DPI | render | `Category=Render&FullyQualifiedName~DialogRenderTests` | ❌ W2 (created by task) | ⬜ pending |
| 03-07-T1 | 03-07 | 3 | GUIDE-09 | Hash staleness per part; hand edits absorbed; StaleReason names the changed basis | unit | `GuideStalenessTests` | ❌ W3 (created by task) | ⬜ pending |
| 03-07-T2 | 03-07 | 3 | GUIDE-08/09 | Edit/add/delete semantics, NeedsReview (facts and linked values), rename replacement | unit | `CaseGuideEditingTests` | ❌ W3 (created by task) | ⬜ pending |
| 03-08-T1 | 03-08 | 4 | GUIDE-06 | Amount/identifier scanner with timeouts | unit | `CanonicalValueScannerTests` | ❌ W4 (created by task) | ⬜ pending |
| 03-08-T2 | 03-08 | 4 | GUIDE-07/03/06/09 | Every validator rule fires (incl. party-without-organization, org/party name mismatch, individual-party counsel, value review); never throws; fixture error-free | unit | `CaseGuideValidatorTests` | ❌ W4 (created by task) | ⬜ pending |
| 03-09-T1 | 03-09 | 4 | GUIDE-01, D-01/D-02 | System prompt keeps names + firm position; issue enrichment; strict schemas | unit | `CaseGuideGeneratorTests\|CaseSchemaTests` | ❌ W4 (created by task) | ⬜ pending |
| 03-09-T2 | 03-09 | 4 | GUIDE-02/03/04 | SyncOrganizations, roster, counsel top-up, batched details, background per org (below preset), name rules in every cast prompt, outcomes | unit | `CaseGuideGeneratorTests` | ❌ W4 (created by task) | ⬜ pending |
| 03-10-T1 | 03-10 | 5 | GUIDE-05 | Facts per section, link checks, date-ordered IDs, resume | unit | `CaseGuideGeneratorTests` | ❌ W5 (created by task) | ⬜ pending |
| 03-10-T2 | 03-10 | 5 | GUIDE-06 | Values + GenerateAsync orchestration | unit | `CaseGuideGeneratorTests\|CaseSchemaTests` | ❌ W5 (created by task) | ⬜ pending |
| 03-11-T1 | 03-11 | 6 | GUIDE-08 | Tab regeneration keeps IDs and hand edits; Regenerate Cast re-syncs parties, adds law firms, rewrites non-edited background staff | unit | `CaseGuideRegenerationTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-11-T2 | 03-11 | 6 | GUIDE-08/09, D-01 | Entry regeneration; Update Guide (stale parts only, party rename/add); prompt invariant theory over every request path | unit | `CaseGuideRegenerationTests\|CaseGuidePromptInvariantTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-12-T1 | 03-12 | 5 | GUIDE-08 | IGuideHost, GuideTabPanel, LinkPicker compile | build | (dotnet build ReelDiscovery.csproj) | ❌ W5 (created by task) | ⬜ pending |
| 03-12-T2 | 03-12 | 5 | GUIDE-01 | Issues tab + LinkPicker renders | render | `Category=Render&FullyQualifiedName~GuidePanelRenderTests` | ❌ W5 (created by task) | ⬜ pending |
| 03-13-T1 | 03-13 | 6 | GUIDE-02/08 | Cast panel helpers | unit | `CastPanelLogicTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-13-T2 | 03-13 | 6 | GUIDE-08 | Cast actions + renders | render | `Category=Render&FullyQualifiedName~CastPanelRenderTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-14-T1 | 03-14 | 6 | GUIDE-05/08 | Facts panel helpers | unit | `FactsPanelLogicTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-14-T2 | 03-14 | 6 | GUIDE-08/09 | Facts actions + renders | render | `Category=Render&FullyQualifiedName~FactsPanelRenderTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-15-T1 | 03-15 | 6 | GUIDE-06/08 | Values panel helpers | unit | `ValuesPanelLogicTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-15-T2 | 03-15 | 6 | GUIDE-06 | Values renders (incl. needs-review notice) | render | `Category=Render&FullyQualifiedName~ValuesPanelRenderTests` | ❌ W6 (created by task) | ⬜ pending |
| 03-16-T1 | 03-16 | 7 | GUIDE-01..09 | Tab state logic, unfinished parts | unit | `CaseGuideStepLogicTests` | ❌ W7 (created by task) | ⬜ pending |
| 03-16-T2 | 03-16 | 7 | GUIDE-08/09/10 | Runs, autosave, Next gate in StepCaseGuide.Run.cs (build + full suite) | build + unit | `Category!=Render` | ❌ W7 (created by task) | ⬜ pending |
| 03-16-T3 | 03-16 | 7 | D-04 | All Case Guide states at 144 DPI | render | `Category=Render&FullyQualifiedName~CaseGuideRenderTests` | ❌ W7 (created by task) | ⬜ pending |
| 03-17-T1 | 03-17 | 5 | GUIDE-10/11 | Summary line helpers | unit | `CaseSummaryLogicTests` | ❌ W5 (created by task) | ⬜ pending |
| 03-17-T2 | 03-17 | 5 | GUIDE-11 | Export/open/move + renders | render | `Category=Render&FullyQualifiedName~CaseSummaryRenderTests` | ❌ W5 (created by task) | ⬜ pending |
| 03-18-T1 | 03-18 | 8 | GUIDE-10 | 5-step flow, stage navigation, close decisions | unit | `WizardFlowTests` | ❌ W8 (created by task) | ⬜ pending |
| 03-18-T2 | 03-18 | 8 | GUIDE-09/10 | Core document autosave/notice/alignment renders | render | `Category=Render&FullyQualifiedName~CoreDocumentRenderTests` | ❌ W8 (created by task) | ⬜ pending |
| 03-18-T3 | 03-18 | 8 | D-04, GUIDE-10 | Wizard shell 5-step flow at 144 DPI ("Step 4 of 5: Case Guide", "Next >") | render | `Category=Render&FullyQualifiedName~WizardShellRenderTests` | ❌ W8 (created by task) | ⬜ pending |
| 03-19-T1 | 03-19 | 9 | GUIDE-10 | Reopen logic, theme colors | unit | `CaseScenarioUiLogicTests\|WizardFlowTests` | ❌ W9 (created by task) | ⬜ pending |
| 03-19-T2 | 03-19 | 9 | GUIDE-10 | Describe the Case renders (0 / 8 recent) | render | `Category=Render&FullyQualifiedName~CaseScenarioRenderTests` | ❌ W9 (created by task) | ⬜ pending |
| 03-20-T1 | 03-20 | 10 | all | Full suites green before UAT | unit + render | `Category!=Render (then Category=Render)` | ❌ W10 (created by task) | ⬜ pending |
| 03-20-T2 | 03-20 | 10 | GUIDE-02/10/11, D-04 | Human check at 150% (manual-only items) | manual | — | ❌ W10 (created by task) | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CaseTestData.SampleGuidedDraft()` (03-01-T2); canned DTO JSON lives in `CaseGuideGeneratorTests.cs` / `CaseGuideRegenerationTests.cs` (03-09..03-11)
- [ ] `ReelDiscovery.Tests/Render/RenderHarness.cs` + `ClippingDetector.cs` (03-02-T1; throws instead of skipping when DPI != 144)
- [ ] New test files listed above; `ComplexityPresetTests` (03-01-T1), `CaseSchemaTests` (03-09..03-11), `WizardFlowTests` (03-18-T1). Email identity tests live in `CastIdentityTests`; prompt invariants (byte-stable system prompt, name rules on every request path) in `CaseGuidePromptInvariantTests` (03-11-T2)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Live guide on "The Office"-inspired scenario (Claude + OpenAI): names kept, `michael.scott@dundermifflin.com` | GUIDE-02, D-01, D-14 | Live model output | Run scenario end to end; inspect Cast tab |
| Exports open in Word | GUIDE-11 | Needs Word | Export all three from Case Summary; open each |
| Reopen lands on furthest step | GUIDE-10 | UI + filesystem | Close mid-guide; Open Case...; confirm step |
| Visual quality at 150% | D-04 | Human judgment | Review Case Guide / Case Summary / Describe the Case |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 60s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** planner sign-off 2026-10-01 (execution pending)
