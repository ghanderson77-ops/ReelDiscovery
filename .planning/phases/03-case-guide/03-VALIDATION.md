---
phase: 3
slug: case-guide
status: draft
nyquist_compliant: false
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

Filled in by the planner as task IDs are assigned. Requirement → test map (from 03-RESEARCH.md):

| Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|-------------|----------|-----------|----------------------------|-------------|--------|
| GUIDE-01 | Counts/findings → `ISS-nn`, IDs kept on re-extract | unit | `IssueExtractorTests` | ❌ W0 | ⬜ pending |
| GUIDE-02 | Cast with required fields, `CH-nnn`, derived emails | unit | `CaseGuideGeneratorTests\|EmailIdentityTests\|IdAllocatorTests` | ❌ W0 | ⬜ pending |
| GUIDE-03 | In-house + outside counsel; signing attorney seeded | unit | `CaseGuideGeneratorTests\|CaseGuideValidatorTests` | ❌ W0 | ⬜ pending |
| GUIDE-04 | One background call per org, preset count | unit | `CaseGuideGeneratorTests` | ❌ W0 | ⬜ pending |
| GUIDE-05 | One facts call per FactPlan section; links validated; KnownBy ⊇ actors | unit | `CaseGuideGeneratorTests` | ❌ W0 | ⬜ pending |
| GUIDE-06 | Canonical values `V-nnn`; scanner; duplicate-label Error | unit | `CanonicalValueScannerTests\|CaseGuideValidatorTests` | ❌ W0 | ⬜ pending |
| GUIDE-07 | Every validator rule fires; never throws | unit | `CaseGuideValidatorTests` | ❌ W0 | ⬜ pending |
| GUIDE-08 | Edit/regenerate/delete semantics, hand edits kept | unit | `CaseGuideEditingTests\|CaseGuideGeneratorTests` | ❌ W0 | ⬜ pending |
| GUIDE-09 | Hash-based staleness; cast rename → facts NeedsReview | unit | `GuideStalenessTests` | ❌ W0 | ⬜ pending |
| GUIDE-10 | Save/load, atomic write, versioning, folder naming, recent list, stage → step | unit | `CaseStoreTests\|CaseFolderNamingTests\|WizardFlowTests` | ❌ W0 | ⬜ pending |
| GUIDE-11 | Three .docx exports open, zero OpenXmlValidator errors | unit | `CaseGuideDocxExporterTests` | ❌ W0 | ⬜ pending |
| cross | Strict schemas; prompts keep scenario names (D-01) and firm position (D-02) | unit | `CaseSchemaTests\|CaseGuideGeneratorTests` | ✅ / ❌ W0 | ⬜ pending |
| UI | Pure UI helpers; 144-DPI renders with clipping detector | unit + render | `CaseGuideUiLogicTests\|Category=Render` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CaseTestData.SampleGuidedDraft()` + canned DTO JSON for roster / details / background / facts / values
- [ ] `ReelDiscovery.Tests/Render/RenderHarness.cs` (STA, PerMonitorV2, DrawToBitmap, ClippingDetector, `Category=Render`)
- [ ] New test files listed above; update `WizardFlowTests`, `CaseSchemaTests`, `ComplexityPresetTests`

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

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
