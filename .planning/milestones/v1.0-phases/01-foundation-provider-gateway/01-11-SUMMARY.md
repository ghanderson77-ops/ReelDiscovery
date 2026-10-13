---
phase: 01-foundation-provider-gateway
plan: 11
subsystem: generation / provider gateway
tags: [prov-04, prov-06, fatal-stop, outcome-ux, openaiservice-removal, phase-gate]
requires:
  - "01-02: RunIsolatedAsync(runCts), SaveAndFinalizeAsync on cancel"
  - "01-03: LlmCallException / LlmResult.IsFatal / LlmOutcome"
  - "01-10: EmailGenerator(ILlmGateway, OpenAIMediaClient?)"
provides:
  - "GenerationResult.FatalError"
  - "Fatal (Auth/SpendLimit) run stop that still saves completed threads"
  - "EmailGenerator.DescribeOutcome classified skip messages"
  - "Media planning guard (_media != null) in prompts and media passes"
  - "PlatformTests.OnlyServicesAiReferencesProviderSdks architecture rule"
affects:
  - Services/EmailGenerator.cs
  - UserControls/StepGeneration.cs
  - Models/WizardState.cs
tech-stack:
  added: []
  patterns:
    - "catch (LlmCallException ex) when (ex.Result.IsFatal) -> runCts.Cancel()"
    - "ThrowIfRunStopped(runCts) between generation passes"
key-files:
  created:
    - ReelDiscovery.Tests/Generation/EmailGeneratorOutcomeTests.cs
    - ReelDiscovery.Tests/Generation/EmailGeneratorMediaGatingTests.cs
    - ReelDiscovery.Tests/Generation/EmailGeneratorTestSetup.cs
  modified:
    - Services/EmailGenerator.cs
    - Models/GenerationResult.cs
    - Models/WizardState.cs
    - UserControls/StepGeneration.cs
    - ReelDiscovery.Tests/PlatformTests.cs
    - .planning/REQUIREMENTS.md
    - .planning/ROADMAP.md
  deleted:
    - Services/OpenAIService.cs
key-decisions:
  - "Fatal provider stop sets FatalError and leaves WasCancelled false; WasCancelled now means user cancel only"
  - "Run log colors: Orange = skipped by classified outcome, Red = failure / fatal stop"
  - "PLAT-03 recorded as partial in Phase 1 and carried to Phase 4"
requirements-completed: [PROV-01, PROV-02, PROV-03, PROV-04, PROV-05, PROV-06, PLAT-01, PLAT-02, PLAT-04]
metrics:
  duration: 9min
  completed: 2026-09-30
  tasks: 2
  files: 11
---

# Phase 1 Plan 11: Fatal Stop, Outcome UX, OpenAIService Removal and Phase Gate Summary

An auth or spend-limit error from the provider now cancels the run's linked token straight away. The threads finished so far are still saved to EML, and the run log shows why the run stopped. Refused, truncated and invalid items are skipped once, with an orange classified message, and are not retried. The generator never plans or calls image/voicemail media unless it has a media client. `OpenAIService` is deleted. The full test suite (167 tests) passes and the single-file net10.0-windows exe publishes.

## What was built

**Task 1: EmailGenerator fatal stop, classified skips, media guard (commit 6fc175c)**
- `GenerationResult.FatalError` (string?).
- `RunIsolatedAsync` and the per-storyline catch each gained two new branches:
  - `catch (LlmCallException ex) when (ex.Result.IsFatal)` records the first fatal message (under a lock) and calls `runCts.Cancel()`.
  - A non-fatal `LlmCallException` branch logs `"... skipped ({DescribeOutcome}): {message}"`.
- `internal static string DescribeOutcome(LlmResult)` returns "refused by model", "response truncated", "invalid response" or "failed: {FailureKind}".
- `ThrowIfRunStopped(runCts)` runs after the storyline pass and after every enrichment `Task.WhenAll`, so no later pass starts after a fatal stop. The outer OCE catch sets `WasCancelled = ct.IsCancellationRequested`, so only a user cancel counts as cancelled. It still saves completed threads.
- Media guard (PROV-06 defence in depth):
  - `mediaAvailable = _media != null` now feeds the image and voicemail planning counts, so the batch prompt never asks for media without a client.
  - The image and voicemail passes also require `_media != null`.
- Tests:
  - EmailGeneratorOutcomeTests (6 facts): SpendLimit stop + save + request count 2, Auth fatal, Refused skip + continue + no retry, RunIsolated fatal cancels runCts, RunIsolated truncated classified skip, user cancel still WasCancelled.
  - EmailGeneratorMediaGatingTests (1 fact).
  - Shared `EmailGeneratorTestSetup`.
- RED check: with the pre-change `EmailGenerator.cs`, 6 of the 7 new tests failed. The exception was the user-cancel regression guard, which passed as expected.

**Task 2: Outcome UX, OpenAIService deletion, phase gate (commit 14c3c51)**
- StepGeneration now checks `FatalError` first:
  - Any errors are logged first.
  - Then two red lines: "Run stopped: ..." and "Completed threads were saved. Check your API key, billing or spend limit, then run again."
  - Status label: "Generation stopped by a provider error."; the step is marked complete.
  - Error lines containing " skipped (" are orange; all others are red.
- Deleted `Services/OpenAIService.cs` and `WizardState.CreateOpenAIService()`. No `OpenAIService` references remain in any `.cs` file.
- New `PlatformTests.OnlyServicesAiReferencesProviderSdks` scans every `.cs` file under the repo root (excluding bin/obj/tests/.planning). It fails if any file outside `Services/Ai/` has `using Anthropic` or `using OpenAI`, and it guards against passing vacuously.
- Phase gate:
  - `dotnet test` at the repo root: **167 passed, 0 failed**, including TelemetryRemovalTests.
  - `dotnet build`: 0 warnings.
  - `dotnet publish ReelDiscovery.csproj -c Release` produced `bin/Release/net10.0-windows/win-x64/publish/ReelDiscovery.exe` (64 MB single file).
  - The `hooks.slack.com` source scan is clean (D-16 re-checked).

## Requirement coverage

- PLAT-03 partial in Phase 1: test project, `dotnet test`, EML/Office regression, gateway/provider/concurrency tests delivered. Planner, validator and answer-key-writer tests carry to Phase 4 (those components are built there).
  - REQUIREMENTS.md: checkbox left unchecked, traceability row set to `Phase 1, Phase 4 | Partial`. An earlier plan had wrongly marked it complete; this plan reverted that.
  - ROADMAP.md: PLAT-03 added to the Phase 4 Requirements line.
- Marked complete in this closing plan, because code and tests now back them: PROV-01, PROV-02, PROV-03, PROV-04, PROV-05, PROV-06, PLAT-02. PLAT-01 and PLAT-04 were already complete.

## Deviations from Plan

**1. [Rule 2 - Correctness] A refused storyline still counts toward progress**
- **Issue:** The new non-fatal storyline branch would otherwise skip the progress bump that the generic catch does.
- **Fix:** It now adds the storyline's email count to completed progress, the same as the generic catch.
- **Commit:** 6fc175c

**2. [Rule 1 - Race] FatalError set under a lock**
- **Issue:** Parallel items can fail fatally at the same moment.
- **Fix:** `SetFatalError` wraps `??=` in a lock so the first message wins deterministically, instead of using a bare `result.FatalError ??= ex.Message`.
- **Commit:** 6fc175c

**3. [Addition] Extra test and shared test setup**
- **Change:** Added `RunIsolated_Truncated_SkipsWithClassifiedMessage` (7 new facts instead of 6) and the shared `EmailGeneratorTestSetup.cs`.
- **Commit:** 6fc175c

**4. [Process] TDD order**
- **Issue:** The implementation was written before the tests.
- **Mitigation:** RED was checked afterwards by running the new tests against the HEAD version of `EmailGenerator.cs`; 6 of 7 failed as expected. No separate `test(...)` commit exists.

## TDD Gate Compliance

Task 1 was `tdd="true"`. The RED and GREEN commits were combined into one feat commit (6fc175c). The RED failure was still demonstrated manually (see Deviation 4).

## Manual verification (not a gate, for /gsd-verify-work)

- Run the published exe through the Topic step with each provider.
- Test with an invalid key during generation and confirm the red "Run stopped" lines appear and partial output is saved.

## Threat Flags

None. All threat-register mitigations T-01-40..45 were implemented and are covered by tests.

## Self-Check: PASSED
