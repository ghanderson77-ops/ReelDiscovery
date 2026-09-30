---
phase: 1
slug: foundation-provider-gateway
status: draft
nyquist_compliant: false
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

The planner fills in task IDs. Requirement-level map (from RESEARCH.md §Validation Architecture):

| Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| PLAT-01 | — | N/A | build/smoke | `dotnet publish ReelDiscovery.csproj -c Release` + exe exists; `PlatformTests` | ❌ W0 | ⬜ pending |
| PLAT-02 | T-key-disclosure | keys.dat contains no plaintext key; legacy Base64 file deleted | unit | `--filter FullyQualifiedName~ApiKeyStoreTests` | ❌ W0 | ⬜ pending |
| PLAT-03 | — | N/A | meta | `dotnet test` exit 0 | ❌ W0 | ⬜ pending |
| PLAT-04 | — | N/A | unit | `--filter FullyQualifiedName~ConcurrencyTests` | ❌ W0 | ⬜ pending |
| D-02 fixes | T-hostile-output | Clamp hour/minute; per-item isolation | unit | `--filter FullyQualifiedName~EmailGeneratorFixTests` | ❌ W0 | ⬜ pending |
| PROV-01 | — | N/A | unit | `--filter FullyQualifiedName~ModelCatalogTests\|FullyQualifiedName~LlmGatewayTests` | ❌ W0 | ⬜ pending |
| PROV-02 | T-key-in-logs | Key-test errors never include the key | unit | `--filter FullyQualifiedName~KeyTestTests` | ❌ W0 | ⬜ pending |
| PROV-03 | T-hostile-output | Strict schema; typed DTO deserialization only | unit | `--filter FullyQualifiedName~Ai.` | ❌ W0 | ⬜ pending |
| PROV-04 | T-denial-of-wallet | SpendLimit/Auth/Refused get 0 app retries | unit | `--filter FullyQualifiedName~OutcomeClassificationTests` | ❌ W0 | ⬜ pending |
| PROV-05 | — | N/A | unit | `--filter FullyQualifiedName~OpenAIMediaClientTests` | ❌ W0 | ⬜ pending |
| PROV-06 | — | N/A | unit | `--filter FullyQualifiedName~MediaGatingTests` | ❌ W0 | ⬜ pending |
| Regression | — | N/A | unit | `--filter FullyQualifiedName~DocumentRegressionTests` | ❌ W0 | ⬜ pending |

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

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
