---
phase: 1
slug: foundation-provider-gateway
status: planned
nyquist_compliant: true
wave_0_complete: false
created: 2026-09-29
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + xunit.runner.visualstudio 3.1.4 + Microsoft.NET.Test.Sdk 17.14.1 |
| **Config file** | none (Wave 0 creates `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`, `ReelDiscovery.slnx`, `global.json`) |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~<Area>"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30 seconds (everything in-process with fake HTTP) |

---

## Sampling Rate

- **After every task commit:** Run the quick command filtered to the area touched
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd-verify-work`:** The full suite must be green, and `dotnet publish ReelDiscovery.csproj -c Release` must produce `bin/Release/net10.0-windows/win-x64/publish/ReelDiscovery.exe`
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

Task IDs are `{plan}-T{n}` (plan 01-04 Task 2 = `01-04-T2`). Requirement-level map (from RESEARCH.md §Validation Architecture) with the tasks that deliver and verify each row:

| Requirement | Plan / Tasks | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|-------------|--------------|------------|-----------------|-----------|-------------------|-------------|--------|
| PLAT-01 | 01-01-T1, 01-01-T2, 01-11-T2 (phase gate) | — | N/A | build/smoke | `dotnet publish ReelDiscovery.csproj -c Release` + exe exists; `PlatformTests` | ❌ W0 | ⬜ pending |
| PLAT-02 | 01-06-T1, 01-06-T2, 01-07-T1, 01-07-T2 | T-key-disclosure | keys.dat contains no plaintext key; legacy Base64 file deleted | unit | `--filter FullyQualifiedName~ApiKeyStoreTests` | ❌ W0 | ⬜ pending |
| PLAT-03 (partial in Phase 1; planner/validator/answer-key tests carry to Phase 4) | 01-01-T2, 01-11-T2 | — | N/A | meta | `dotnet test` exit 0 | ❌ W0 | ⬜ pending |
| PLAT-04 | 01-02-T1, 01-02-T2, 01-02-T3, 01-11-T1 | — | N/A | unit | `--filter FullyQualifiedName~ConcurrencyTests` | ❌ W0 | ⬜ pending |
| D-02 fixes | 01-02-T2, 01-02-T3, 01-11-T1 | T-hostile-output | Clamp hour/minute; per-item isolation; fatal stop saves work | unit | `--filter FullyQualifiedName~EmailGeneratorFixTests\|FullyQualifiedName~EmailGeneratorOutcomeTests` | ❌ W0 | ⬜ pending |
| PROV-01 | 01-03-T1, 01-03-T3, 01-07-T1, 01-07-T3, 01-08-T2 | — | N/A | unit | `--filter FullyQualifiedName~ModelCatalogTests\|FullyQualifiedName~LlmGatewayTests\|FullyQualifiedName~WizardStateProviderTests` | ❌ W0 | ⬜ pending |
| D-15 (OpenAI Story = gpt-6.1-sol, effort low) | 01-03-T1, 01-05-T1 (model capability verification, escalate if unsupported), 01-07-T1 | — | N/A | unit | `--filter FullyQualifiedName~ModelCatalogTests\|FullyQualifiedName~WizardStateProviderTests` | ❌ W0 | ⬜ pending |
| PROV-02 | 01-04-T2, 01-05-T1, 01-07-T2 | T-key-in-logs | Key-test errors never include the key | unit | `--filter FullyQualifiedName~KeyTestTests` | ❌ W0 | ⬜ pending |
| PROV-03 | 01-03-T2, 01-04-T1, 01-05-T1, 01-08-T1, 01-08-T2, 01-10-T1, 01-10-T2 | T-hostile-output | Strict schema; typed DTO deserialization only | unit | `--filter FullyQualifiedName~Ai.\|FullyQualifiedName~PortTests` | ❌ W0 | ⬜ pending |
| PROV-04 | 01-03-T3, 01-04-T1, 01-04-T2, 01-05-T1, 01-11-T1, 01-11-T2 | T-denial-of-wallet | SpendLimit/Auth/Refused get 0 app retries; spend-cap 429 not retried by either SDK (Anthropic: Calls==1 at MaxRetries=2); Anthropic max_tokens clamped for non-streaming | unit | `--filter FullyQualifiedName~OutcomeClassificationTests\|FullyQualifiedName~AnthropicWireTests` | ❌ W0 | ⬜ pending |
| PROV-05 | 01-05-T2, 01-09-T2, 01-10-T2 | — | N/A | unit | `--filter FullyQualifiedName~OpenAIMediaClientTests` | ❌ W0 | ⬜ pending |
| PROV-06 | 01-07-T1, 01-09-T1, 01-09-T2, 01-11-T1 | — | N/A | unit | `--filter FullyQualifiedName~MediaGatingTests\|FullyQualifiedName~MediaAvailabilityPolicyTests` | ❌ W0 | ⬜ pending |
| D-16 telemetry removal | 01-09-T3, 01-11-T2 (gate re-check) | T-01-45 | No hooks.slack.com in app source; TelemetryService/TelemetryOptInDialog deleted | unit (source scan) | `--filter FullyQualifiedName~TelemetryRemovalTests` | ❌ W0 | ⬜ pending |
| Regression | 01-01-T2 | — | N/A | unit | `--filter FullyQualifiedName~DocumentRegressionTests` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `global.json`, `ReelDiscovery.slnx`, app csproj retarget plus `Compile Remove="ReelDiscovery.Tests/**"` plus `InternalsVisibleTo`
- [ ] `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`
- [ ] `ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs`, `Fakes/FakeLlmProvider.cs`, `Fakes/StrictSchemaValidator.cs`
- [ ] Optional opt-in live smoke test, skipped when its env key is absent. Not required (D-03).

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Keys screen layout, media notice, Clear buttons | PROV-02, PROV-06 (D-04..D-08) | WinForms UI is not unit-tested; the gating logic is extracted and unit-tested | Launch the app. Enter the keys and test them. Select Claude with no OpenAI key and confirm the media notice appears and the image/voicemail options are disabled. Clear a key, relaunch, and confirm it is gone. |
| Published exe launches and runs the legacy flow | PLAT-01 | Needs a desktop session | Run the published exe and click through to the Topic step. |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies (26 tasks, 26 automated commands)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (01-01 creates the project + FakeHttpHandler; 01-03 creates FakeLlmProvider + StrictSchemaValidator)
- [x] No watch-mode flags
- [x] Feedback latency < 30s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** planned (execution fills Status)
