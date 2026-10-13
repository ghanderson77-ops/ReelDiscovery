---
phase: 01-foundation-provider-gateway
plan: 12
subsystem: provider-gateway
tags: [gap-closure, cancellation, timeouts, api-keys, CR-01, CR-02]
requires: []
provides:
  - "Transport timeouts classified as Failed(Network) in both text providers"
  - "Media timeouts return null (item skipped)"
  - "KeyTestGuard.IsResultCurrent and stale-result-safe key test"
affects:
  - Services/Ai/OpenAITextProvider.cs
  - Services/Ai/AnthropicTextProvider.cs
  - Services/Ai/OpenAIMediaClient.cs
  - UserControls/StepApiConfiguration.cs
tech-stack:
  added: []
  patterns:
    - "ct-filtered OCE: catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } then unfiltered OCE -> classified failure"
key-files:
  created:
    - Helpers/KeyTestGuard.cs
    - ReelDiscovery.Tests/Ai/TransportTimeoutTests.cs
    - ReelDiscovery.Tests/KeyTestGuardTests.cs
  modified:
    - Services/Ai/OpenAITextProvider.cs
    - Services/Ai/AnthropicTextProvider.cs
    - Services/Ai/OpenAIMediaClient.cs
    - Services/Ai/ILlmProvider.cs
    - UserControls/StepApiConfiguration.cs
decisions:
  - "Provider-layer OCE is rethrown only when the caller token is cancelled; any other OCE is a transport timeout mapped to Failed(Network) with a fixed message (no ex.Message)"
  - "Key test results are applied only if KeyTestGuard.IsResultCurrent(testedKey, keyBox.Text) (ordinal, trimmed); key box and Clear are locked during the test"
metrics:
  duration: "~10 min"
  completed: 2026-09-30
  tasks: 2
  files: 8
---

# Phase 1 Plan 12: Transport Timeout Classification and Stale Key-Test Guard Summary

Transport timeouts in OpenAITextProvider, AnthropicTextProvider and OpenAIMediaClient are now classified. Text calls return Failed(Network), key tests return a Network failure, and media calls return null. A caller cancel still throws. A key test that finishes after Clear or an edit can no longer write the stale key back to keys.dat.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | Classify transport timeouts (CR-01, provider layer) | 1705fcc (RED), 1c842d7 (GREEN) | Services/Ai/OpenAITextProvider.cs, AnthropicTextProvider.cs, OpenAIMediaClient.cs, ILlmProvider.cs, ReelDiscovery.Tests/Ai/TransportTimeoutTests.cs |
| 2 | Stale key-test guard (CR-02) | a2acfc7 (RED), ef5aaeb (GREEN) | Helpers/KeyTestGuard.cs, UserControls/StepApiConfiguration.cs, ReelDiscovery.Tests/KeyTestGuardTests.cs |

## TDD Gate Compliance

- Task 1 RED: all 6 timeout tests failed before the fix: OpenAI_Send, OpenAI_KeyTest, Anthropic_Send, Anthropic_KeyTest, Media_Image and Media_Speech. The Anthropic SDK did not wrap the TaskCanceledException as AnthropicIOException, so both Anthropic cases were genuinely RED. The 3 user-cancel tests (OpenAI_Send_UserCancel_Throws, Anthropic_Send_UserCancel_Throws, Media_Image_UserCancel_Throws) were green before the fix and serve as guards that cancellation is preserved. GREEN: 9/9 pass.
- Task 2 RED: the tests failed to compile because KeyTestGuard did not exist yet. GREEN: 6/6 pass.
- Each task has a test(...) commit followed by a fix(...) commit. No refactor commits were needed.

## Verification

- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors.
- `dotnet test ReelDiscovery.slnx`: 182 passed, 0 failed (167 existing + 9 + 6 new).
- Grep checks: OpenAI and Anthropic providers each have 2 `when (ct.IsCancellationRequested)` clauses and 4 `catch (OperationCanceledException)` clauses. The `throw;` count (2) equals the filtered count. The media client has 2 `when (!ct.IsCancellationRequested)` clauses.
- In StepApiConfiguration.cs, `KeyTestGuard.IsResultCurrent` (line 342) comes before `_keyStore.SetKey(provider, key)` (line 356).

## Notes

- Provider switching (`_cboProvider`) is not locked during a key test. The test uses the captured `provider`, `key` and `modelId`, and switching the text provider changes neither key box.
- Generator-side handling of CR-01 (EmailGenerator lambda / RunIsolatedAsync / outer catch, and the StepGeneration false-success message) is plan 01-13.
- Manual verification (UI): while a key test is in flight, that provider's key box and Clear button are greyed out. Pressing Clear right after Test is no longer possible. If a result arrives for a changed key, the status shows "Key changed during test; test again."

## Deviations from Plan

None. The plan was executed exactly as written.

## Known Stubs

None.

## Self-Check: PASSED
