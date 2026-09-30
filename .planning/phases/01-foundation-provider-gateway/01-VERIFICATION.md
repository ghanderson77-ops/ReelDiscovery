---
phase: 01-foundation-provider-gateway
verified: 2026-09-30T16:40:00Z
status: gaps_found
score: 4/5 roadmap success criteria verified (plan must-haves 2 failed: 01-11 "nothing is retried blindly / failures reported", 01-07 "Clear removes a key")
overrides_applied: 0
gaps:
  - truth: "Refusals, truncations and spend-limit errors surface as classified outcomes (SC3 / PROV-04); provider failures never end a run silently (01-11 must-have: failures are reported clearly, completed work saved)"
    status: failed
    reason: >
      A non-user OperationCanceledException (OpenAI NetworkTimeout = 10 min -> TaskCanceledException;
      Anthropic HttpClient.Timeout = 10 min) is rethrown unclassified by both providers and by EmailGenerator.
      It escapes the ILlmProvider "never throws for API/HTTP failures" contract, bypasses the gateway's
      outcome classification (LlmFailureKind.Network exists but is never produced for timeouts), lands in
      EmailGenerator's outer catch (OperationCanceledException) with ct not cancelled and FatalError null,
      so WasCancelled=false, no error is recorded, every remaining enrichment pass is skipped, and
      StepGeneration logs "GENERATION COMPLETE!". One slow request silently truncates the whole run and
      reports success. Confirmed by code reading (REVIEW CR-01).
    artifacts:
      - path: "Services/Ai/OpenAITextProvider.cs"
        issue: "lines 54-57 (and 78-81 in TestKeyAsync): catch (OperationCanceledException) { throw; } with no ct.IsCancellationRequested filter"
      - path: "Services/Ai/AnthropicTextProvider.cs"
        issue: "lines 62-65 (and 89-92): same unfiltered rethrow"
      - path: "Services/Ai/OpenAIMediaClient.cs"
        issue: "media timeout escapes as OCE and aborts the run instead of skipping the item"
      - path: "Services/EmailGenerator.cs"
        issue: "storyline lambda (~178) and RunIsolatedAsync (~466) rethrow any OCE; outer catch (~428) records nothing when ct not cancelled and FatalError null"
      - path: "UserControls/StepGeneration.cs"
        issue: "~204-254: result with FatalError==null, WasCancelled==false, Errors.Count==0 is reported as GENERATION COMPLETE"
    missing:
      - "In both providers: catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } then map any other OCE to Failed(LlmFailureKind.Network, \"request timed out\")"
      - "In OpenAIMediaClient: non-ct OCE returns null (skip item)"
      - "In EmailGenerator storyline lambda and RunIsolatedAsync: rethrow OCE only when runCts.IsCancellationRequested; otherwise fall through to the recorded-skip path"
      - "In EmailGenerator outer OCE catch: if !ct.IsCancellationRequested && FatalError == null, AddError(\"Run stopped unexpectedly (request timed out)\")"
      - "Regression test: fake provider/media throwing TaskCanceledException with an un-cancelled token -> run continues, item recorded as skipped, no GENERATION COMPLETE with zero errors"
  - truth: "Tested keys are saved to the DPAPI store; Clear removes a key from the store (01-07 must-have; supports SC2)"
    status: partial
    reason: >
      TestKeyAsync captures the key before awaiting and disables only the Test button. If the user clicks
      Clear (or edits the key) while the test is in flight, the completing test calls SetKeyState(provider, key, tested:true)
      and _keyStore.SetKey(provider, key), writing the removed/stale key back to keys.dat and marking it as the
      tested active credential. Clear works in isolation, but can be silently undone (REVIEW CR-02).
    artifacts:
      - path: "UserControls/StepApiConfiguration.cs"
        issue: "TestKeyAsync (314-364) does not disable key box/Clear and does not re-check the key after the await; ClearKey (366-391)"
    missing:
      - "Disable the key TextBox and Clear button while a test runs"
      - "After the await, discard the result if GetKeyBox(provider).Text.Trim() != key (do not SetKeyState / SetKey)"
deferred:
  - truth: "PLAT-03: tests for planner, validators and answer-key writer"
    addressed_in: "Phase 4"
    evidence: "ROADMAP Phase 4 (Corpus Plan & Answer Key) Requirements list includes PLAT-03; REQUIREMENTS.md traceability: 'Partial (Phase 1: harness + EML tests; planner/validator/answer-key tests in Phase 4)'"
human_verification:
  - test: "Launch the published single-file ReelDiscovery.exe on a clean Windows x64 machine and walk the full wizard with OpenAI selected"
    expected: "App starts (no .NET install needed), disclaimer shown, no telemetry dialog, a small run completes and writes .eml files"
    why_human: "WinForms UI launch and end-to-end run need a desktop session and a real API key"
  - test: "Enter a real Anthropic key and a real OpenAI key, press Test on each, restart the app"
    expected: "Both show green OK; keys reload from keys.dat after restart; keys.dat does not contain plaintext keys"
    why_human: "Requires real provider keys and network"
  - test: "Select Claude + Story/Volume models, generate storylines, characters and a small email run"
    expected: "Valid typed results from Claude; token/cost display updates"
    why_human: "Live Anthropic structured-output call"
  - test: "With a legacy %LOCALAPPDATA%\\ReelDiscovery\\reeldiscovery.settings file present, launch the app"
    expected: "Key appears as the OpenAI key; legacy file deleted"
    why_human: "Unit-tested, but real-profile startup migration should be confirmed once"
  - test: "Select Claude, clear/omit the OpenAI key, open Topic and Generation Config steps"
    expected: "Image and voicemail options unchecked, disabled, with orange notice; with a tested OpenAI key, a gpt-image image is generated in a run"
    why_human: "Visual gating and live gpt-image call"
---

# Phase 1: Foundation & Provider Gateway Verification Report

**Phase Goal:** The app runs on a supported runtime with tests and safe concurrency, and all text generation goes through one provider-agnostic gateway, so the user can choose OpenAI or Claude.
**Verified:** 2026-09-30T16:40:00Z
**Status:** gaps_found
**Re-verification:** No (initial verification)

## Goal Achievement

The structural work is real and well built. The runtime moved to net10.0-windows, the gateway and both providers exist, all four generators use the gateway, the DPAPI store and migration are wired, media gating works, and the 167 tests pass. The failure is in the outcome contract. A transport timeout is not classified as an outcome. Instead the run ends early, and the user is told it succeeded.

### Observable Truths (Roadmap Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Builds on net10.0-windows with pinned SDK; publishes single-file self-contained exe that launches and runs the existing flow | VERIFIED (launch: human) | `global.json` pins 10.0.401 (latestFeature); `dotnet --version` = 10.0.401; csproj `net10.0-windows`, `PublishSingleFile`/`SelfContained`/`win-x64`. `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors. `dotnet publish -c Release` produced a single `ReelDiscovery.exe` (64 MB) plus a pdb. Launch needs a human check. |
| 2 | User can enter/test both OpenAI and Anthropic keys; stored DPAPI-encrypted; legacy Base64 key file migrated automatically | VERIFIED (with CR-02 partial on Clear) | `Services/ApiKeyStore.cs` uses `ProtectedData.Protect/Unprotect` (CurrentUser) into `keys.dat`, an atomic replace, and treats corrupt entries as absent. `Program.cs:23` calls `new ApiKeyStore().MigrateLegacyKey()`. `StepApiConfiguration` has Test buttons for both providers (`LlmProviderFactory.Create(provider,key).TestKeyAsync`) and uses `_keyStore.GetKey/SetKey/ClearKey`. Covered by ApiKeyStoreTests and Anthropic/OpenAI KeyTest tests. The Clear race (CR-02) is recorded as a separate gap. |
| 3 | Pick Claude or OpenAI plus a model; a structured (JSON-schema) request returns a valid typed result from either provider; refusals, truncations and spend-limit errors surface as classified outcomes and are not retried blindly | FAILED (BLOCKER) | Most of this holds. `LlmGateway` resolves the tier, uses `JsonSchemaRegistry` strict schemas, retries a truncation once (capped), makes at most 3 invalid attempts, and does not retry Refused/Failed. The wire and classification tests pass for both SDKs, including the spend-limit non-retry handler and `QuotaAwareRetryPolicy`. **However**, both providers rethrow every `OperationCanceledException` without checking `ct`, so a 10-minute transport timeout escapes classification. `EmailGenerator` then drops into its outer OCE catch and records nothing (`WasCancelled=false`, `FatalError=null`, no error), skips all later passes, and `StepGeneration` prints "GENERATION COMPLETE!". A provider failure therefore produces a false success rather than a classified outcome. This goes against PROV-04 ("Provider responses are classified ... failed") and the 01-11 must-have that failures are reported clearly. |
| 4 | Image generation works on a gpt-image model; with Claude selected and no OpenAI key, images and voicemail are disabled with a clear notice | VERIFIED (live call: human) | `OpenAIMediaClient` defaults to `gpt-image-2.5-flare`, quality low, 1024x1024, and sets no DALL-E params. `GenerationConfig.ImageModel` matches. No "dall" strings remain in UI or Services. `WizardState.IsMediaAvailable` requires a tested OpenAI key, and `CreateMediaClient()` returns null otherwise. StepTopicInput and StepGenerationConfig gate on `_state.IsMediaAvailable`, show `MediaUnavailableNotice` and call `ApplyMediaAvailability()`. `EmailGenerator` skips image/voicemail planning and calls when `_media == null` (lines 285, 376, 647-651). |
| 5 | `dotnet test` runs a passing test project; shared-Random and parallel-mutation bugs are fixed | VERIFIED | `dotnet test ReelDiscovery.slnx`: Passed 167, Failed 0. No `new Random` anywhere in app code; all randomness uses `Random.Shared`. `GenerationResult.AddError` is locked; `GenerationProgress.Clone()` snapshots are taken under `progressLock`; the result counters (`WordDocumentsGenerated++` etc.) are incremented inside `lock (progressLock)`; `_documentChains` is a `ConcurrentDictionary`; `threads` is concurrent. |

**Score:** 4/5 roadmap success criteria verified

### Plan must-have spot results (beyond roadmap SCs)

| Plan | Truth | Status | Evidence |
|------|-------|--------|----------|
| 01-03 | Refused/Auth/SpendLimit never retried; truncation 1 retry capped; invalid at most 3 | VERIFIED | `LlmGateway.GenerateAsync` switch; LlmGatewayTests |
| 01-04 | Anthropic 429 enforced_spend_limit_reached makes exactly one HTTP call | VERIFIED | `AnthropicSpendLimitHandler` wired in the production ctor; tests pass |
| 01-05 | OpenAI insufficient_quota not retried by transport | VERIFIED | `QuotaAwareRetryPolicy` in `CreateDefaultOptions`; tests pass |
| 01-07 | Clear removes a key from the store | PARTIAL | CR-02 race can write the key back |
| 01-08 | Storyline/Character on Story tier, Theme on Volume tier through the gateway | VERIFIED | `LlmTier.Story` in Storyline/CharacterGenerator, `LlmTier.Volume` in ThemeGenerator; StepStorylines/StepCharacters use `CreateGateway()` |
| 01-09 | Slack telemetry removed | VERIFIED | No `TelemetryService` or `hooks.slack` in app source; TelemetryRemovalTests |
| 01-10 | EmailGenerator on the gateway (Volume) + OpenAIMediaClient; `new EmailGenerator(_state.CreateGateway(), _state.CreateMediaClient())` | VERIFIED | `EmailGenerator.cs:49-51`, `StepGeneration.cs:188` |
| 01-11 | OpenAIService deleted; only Services/Ai uses the SDKs | VERIFIED | File is absent; no `using OpenAI`/`using Anthropic` outside `Services/Ai` |
| 01-11 | Fatal Auth/SpendLimit stops the run and saves completed threads | VERIFIED (partial-thread loss is WR-01, warning) | `catch (LlmCallException ex) when (ex.Result.IsFatal)` leads to `SetFatalError` and `runCts.Cancel()`; the outer catch saves threads |
| 01-11 | Failures reported clearly; nothing ends silently | FAILED | CR-01 (see SC3) |

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| `global.json` | VERIFIED | 10.0.401 latestFeature |
| `ReelDiscovery.csproj` | VERIFIED | net10.0-windows, Anthropic 12.51.0, OpenAI 2.14.0, OpenXml 3.5.1, M.E.AI.Abstractions, `Compile Remove="ReelDiscovery.Tests/**"`, InternalsVisibleTo |
| `ReelDiscovery.slnx` + `ReelDiscovery.Tests/` | VERIFIED | 25 test source files, 167 tests |
| `Services/Ai/LlmGateway.cs`, `JsonSchemaRegistry.cs`, `LlmResult.cs` | VERIFIED | Substantive and wired through `WizardState.CreateGateway()` |
| `Services/Ai/AnthropicTextProvider.cs`, `AnthropicSpendLimitHandler.cs` | VERIFIED, with CR-01 defect | Unfiltered OCE rethrow |
| `Services/Ai/OpenAITextProvider.cs`, `QuotaAwareRetryPolicy.cs`, `OpenAIMediaClient.cs` | VERIFIED, with CR-01 defect | Unfiltered OCE rethrow; media client handles only 400 (WR-03) |
| `Services/ApiKeyStore.cs` | VERIFIED | DPAPI, migration, corrupt entry treated as absent |
| `Models/WizardState.cs` | VERIFIED | TextProvider, IsMediaAvailable, ApplyMediaAvailability, CreateGateway, CreateMediaClient |
| `UserControls/StepApiConfiguration.cs` | PARTIAL | CR-02 race |
| `Services/EmailGenerator.cs` | VERIFIED, with CR-01 defect | Gateway plus media client; fatal stop; OCE handling can't tell timeout from cancel |
| `Models/GenerationResult.cs` | VERIFIED | `AddError` (locked), `FatalError`, `Clone()` |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| WizardState.CreateGateway | LlmGateway | `new LlmGateway(LlmProviderFactory.Create(...), story, volume, UsageTracker)` | WIRED |
| LlmGateway | JsonSchemaRegistry / TokenUsageTracker | `GetSchemaJson`, `RecordUsage` for every billed attempt | WIRED |
| StepStorylines / StepCharacters / StepGeneration | generators | `CreateGateway()` / `CreateMediaClient()` | WIRED |
| Program.cs | ApiKeyStore | `MigrateLegacyKey()` before Application.Run | WIRED |
| StepApiConfiguration | ILlmProvider.TestKeyAsync / ApiKeyStore | Test and Clear handlers | WIRED (race: CR-02) |
| Topic / GenerationConfig steps | IsMediaAvailable / ApplyMediaAvailability | OnEnter/Validate | WIRED |
| Provider transport timeout | LlmResult classification | should map to Failed(Network) | NOT WIRED (CR-01) |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Solution builds warning-free | `dotnet build ReelDiscovery.slnx` | 0 warnings, 0 errors | PASS |
| Test suite passes | `dotnet test ReelDiscovery.slnx` | 167 passed, 0 failed | PASS |
| Single-file publish | `dotnet publish ReelDiscovery.csproj -c Release -o <scratch>` | `ReelDiscovery.exe` (64,294,452 bytes) + pdb only | PASS |
| SDK pin honored | `dotnet --version` | 10.0.401 | PASS |

### Requirements Coverage

| Requirement | Source Plan | Status | Evidence |
|-------------|-------------|--------|----------|
| PLAT-01 | 01-01, 01-11 | SATISFIED | net10 target, pinned SDK, single-file publish confirmed |
| PLAT-02 | 01-06, 01-07 | SATISFIED | DPAPI store and startup migration (CR-02 affects Clear UX, not encryption/migration) |
| PLAT-03 | 01-01, 01-11 | PARTIAL, justified | Harness, EML round-trip, Office regression and gateway/provider tests exist. The planner, validators and answer-key writer do not exist until Phase 4, and ROADMAP Phase 4 lists PLAT-03, so the split is legitimate and not an orphaned gap. |
| PLAT-04 | 01-02, 01-11 | SATISFIED | Random.Shared everywhere; locked error sink; snapshot progress; locked counters |
| PROV-01 | 01-03, 01-07, 01-08 | SATISFIED | Provider plus Story/Volume model selection; gateway enforces one provider |
| PROV-02 | 01-04, 01-05, 01-07 | SATISFIED (CR-02 race) | Both key tests via free model-retrieve |
| PROV-03 | 01-03..01-05, 01-08, 01-10, 01-11 | SATISFIED | All generators route through ILlmGateway with strict schemas; no SDK use outside Services/Ai |
| PROV-04 | 01-03..01-05, 01-11 | BLOCKED (partial) | Refused/truncated/invalid/auth/spend-limit are classified and not retried, but transport-timeout failures escape classification and end the run silently (CR-01) |
| PROV-05 | 01-05, 01-09, 01-10 | SATISFIED | gpt-image-2.5-flare |
| PROV-06 | 01-07, 01-09, 01-11 | SATISFIED | Gating, notice, null media client, planner guard |

No orphaned requirements: all 10 phase IDs are claimed by plans.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| Services/Ai/OpenAITextProvider.cs, AnthropicTextProvider.cs | 54, 62 | Unfiltered `catch (OperationCanceledException) { throw; }` | Blocker | Timeout is treated as a cancel, giving a silent false success (CR-01) |
| Services/EmailGenerator.cs | ~178, ~428, ~466 | Same; outer catch records nothing | Blocker | Same root cause |
| UserControls/StepApiConfiguration.cs | 314-364 | Stale async result written after user action | Warning (Clear truth partial) | CR-02 |
| Services/EmailGenerator.cs | 659-729 | Partial storyline thread discarded on batch failure | Warning | WR-01: billed work lost, even on fatal stop |
| Services/Ai/OpenAIMediaClient.cs | 52-60 | Only 400 handled; raw `ex.Message` logged | Warning | WR-03 |
| UserControls/StepGeneration.cs | 204-212 | `_generationComplete=true` after fatal stop blocks re-run | Warning | WR-04 |
| Services/EmailGenerator.cs | 580-614 | DistributeEmails negative counts when storylines exceed emails | Warning | WR-06 (existing behavior) |

The review warnings WR-02 and WR-05 through WR-09 remain as quality issues. They don't block this phase's SCs.

### Human Verification Required

1. **Published exe launch and OpenAI end-to-end run.** Expected: the app launches without a .NET install and writes .eml files.
2. **Real key tests for both providers, then a restart.** Expected: green OK, and the keys reload from `keys.dat`, which holds no plaintext.
3. **Claude-selected run.** Expected: valid typed storylines, characters and emails.
4. **Legacy `reeldiscovery.settings` migration on a real profile.** Expected: the key is imported and the file is deleted.
5. **Media gating visuals and a live gpt-image image.** Expected: options are disabled with an orange notice when Claude is selected and there is no OpenAI key.

(Once the gaps are closed these become the human_needed items.)

### Gaps Summary

Both gaps come from async edge handling. The main architecture is sound.

1. **CR-01 (BLOCKER, SC3 / PROV-04).** A provider or transport timeout is surfaced as an `OperationCanceledException` that no layer tells apart from a user cancel. It gets past the classified-outcome gateway, silently ends every later pass, and the user sees "GENERATION COMPLETE!". The phase goal is that generation goes through a gateway producing classified outcomes, and at 5K-email scale with a 10-minute transport timeout this is a realistic path, so SC3 can't be accepted as met. The fix is small: add `when (ct.IsCancellationRequested)` filters, map other OCEs to `Failed(Network)`, record an error in the outer catch, and add a regression test.
2. **CR-02 (partial, 01-07 Clear truth).** A key test that completes after Clear or an edit writes the old key back to DPAPI storage and state. The fix: lock the key box and Clear during a test, and discard stale results.

The PLAT-03 partial status is justified. The remaining tests (planner, validators, answer-key writer) cover components that don't exist until Phase 4, and ROADMAP Phase 4 carries PLAT-03, so this is recorded as deferred, not as a gap.

---

_Verified: 2026-09-30T16:40:00Z_
_Verifier: Claude (gsd-verifier)_
