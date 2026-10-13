---
phase: 01-foundation-provider-gateway
plan: 07
subsystem: provider-selection-ui
tags: [wizard-state, keys-screen, provider, tiers, media-gating, dpapi, model-catalog]
requires:
  - "01-03: ILlmGateway / LlmGateway, LlmProviderKind, LlmTier, AIModelConfig tier fields"
  - "01-04: AnthropicTextProvider(apiKey)"
  - "01-05: OpenAITextProvider(apiKey), OpenAIMediaClient, GenerationConfig.ImageModel/ImageQuality"
  - "01-06: ApiKeyStore GetKey/SetKey/ClearKey"
provides:
  - "LlmProviderFactory.Create(kind, key) -> ILlmProvider"
  - "WizardState: OpenAIApiKey/AnthropicApiKey, *KeyTested, TextProvider, SetTextProvider, StoryModelId/VolumeModelId, StoryModelConfig/VolumeModelConfig, IsMediaAvailable, MediaUnavailableNotice, CanProceedFromKeys, ProviderSummary, GetKeyTestModelId, GetApiKey, CreateGateway, CreateMediaClient"
  - "Two-provider keys screen (D-04..D-09) wired to DPAPI store"
  - "Model dialog edits Provider / Default For Tier / Effort / Max Output"
affects:
  - "01-08, 01-10: generators port from CreateOpenAIService() to CreateGateway()/CreateMediaClient()"
  - "01-09: telemetry call in StepComplete (now passes ProviderSummary) is deleted"
  - "01-11: CreateOpenAIService() deleted"
tech-stack:
  added: []
  patterns:
    - "Provider selection resets tier models to catalog defaults; configs filtered by TextProvider so tiers never mix providers"
    - "_suppressEvents guard for programmatic control updates in code-built WinForms steps"
    - "String helper properties (DefaultForTierText, EffortText) for DataGridView binding of nullable enum/string"
key-files:
  created:
    - Services/Ai/LlmProviderFactory.cs
    - ReelDiscovery.Tests/WizardStateProviderTests.cs
    - ReelDiscovery.Tests/MediaGatingTests.cs
  modified:
    - Models/WizardState.cs
    - Models/AIModelConfig.cs
    - UserControls/StepApiConfiguration.cs
    - UserControls/ModelConfigurationDialog.cs
    - UserControls/StepComplete.cs
decisions:
  - "Keys loaded from the DPAPI store on first entry are shown but untested; the user must click Test before Next (tested flags are never persisted)"
  - "Added WizardState.GetApiKey(provider) helper used by CreateGateway and the keys screen"
  - "Configure Models OK requires a Story and a Volume default for every provider; extra defaults per (Provider, Tier) are cleared, first one wins"
  - "Legacy CreateOpenAIService() uses the OpenAI Volume model (selected one when OpenAI is the text provider, else catalog default gpt-6-luna)"
metrics:
  duration: 6min
  completed: 2026-09-30
  tasks: 3
  files: 8
---

# Phase 1 Plan 07: Provider Selection, Keys Screen and Model Catalog Summary

`WizardState` now tracks one text provider per run (OpenAI by default) with Story and Volume model tiers, separate OpenAI and Anthropic keys with tested flags, and factories for the gateway and the OpenAI media client. The keys screen was rebuilt for two providers and saves keys to the DPAPI store. The model dialog now edits Provider, tier default, Effort and output ceiling.

## What Was Built

- **`Services/Ai/LlmProviderFactory.cs`**: maps `LlmProviderKind` plus a key to `OpenAITextProvider` or `AnthropicTextProvider`.
- **`Models/WizardState.cs`**: has every member listed in the plan's interfaces block, plus `GetApiKey(provider)`.
  - The constructor calls `SetTextProvider(OpenAI)`, which gives `gpt-6.1-sol` (Story) and `gpt-6-luna` (Volume). Choosing Anthropic resets these to `claude-opus-5-5` and `claude-haiku-4-5-20251001`.
  - `CreateGateway()` throws `"No Story model selected for {provider}"` or `"No Volume model selected for {provider}"` when a tier has no matching config.
  - The legacy members `ApiKey`, `SelectedModel`, `ConnectionTested`, `SelectedModelConfig` and `AvailableModels` were removed.
  - `CreateOpenAIService()` still exists for older code: it now uses `OpenAIApiKey` and the OpenAI Volume model. It will be deleted in 01-11.
- **`UserControls/StepApiConfiguration.cs`**: rebuilt as a 13-row code-built layout.
  - The OpenAI and Anthropic sections each have a masked Consolas key box, a Test button, a Clear button and a status line. A media status line sits under the OpenAI section.
  - Below them are the Text provider combo (OpenAI/Claude), Story and Volume model combos filtered to that provider, the Configure Models button, a pricing line, instructions and the updated disclaimer.
  - Test calls `LlmProviderFactory.Create(p, key).TestKeyAsync(_state.GetKeyTestModelId(p))` and saves the key to the store only when the test passes. Clear removes the key from the store.
  - `CanMoveNext` is `_state.CanProceedFromKeys()`. The old Base64 save/load code is gone.
- **`UserControls/StepComplete.cs`**: shows "Models Used" from `ProviderSummary`, which is also passed to telemetry as a temporary step until 01-09.
- **`UserControls/ModelConfigurationDialog.cs`** and **`Models/AIModelConfig.cs`**:
  - The clone now copies every field.
  - New columns: Provider (combo bound to the enum), Default For Tier (None/Story/Volume combo via `DefaultForTierText`), Effort (via `EffortText`) and Max Output.
  - The dialog is 980x480, with a minimum of 820x360.
  - `IsDefault` was removed everywhere. On OK, each (Provider, Tier) must have exactly one default.
  - Add creates an OpenAI model with a 16,000-token output limit.
  - Invalid cell input (such as text in Max Output) is rejected through a `DataError` handler instead of crashing the dialog.

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- Full suite `dotnet test ReelDiscovery.slnx`: 135/135 passed, including the 17 new WizardStateProviderTests and MediaGatingTests
- Acceptance greps: `UseSystemPasswordChar = true` found 2 times; `TestKeyAsync(` 4; `_keyStore.*Key` 3; legacy save/Base64 0; legacy WizardState members 0; `IsDefault` found nowhere in Models, UserControls, Services, Forms or Tests

## TDD Gate Compliance

- RED: `1fad1fb` test(01-07). The tests did not compile because the WizardState members did not exist yet.
- GREEN: `bcc081c` feat(01-07). All 17 tests pass.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] DataError handler in the model dialog**
- **Found during:** Task 3
- **Issue:** Adding the integer Max Output column meant that typing an invalid value would raise the DataGridView's default DataError dialog/exception.
- **Fix:** A `GridModels_DataError` handler shows a validation warning and cancels the edit.
- **Commit:** 9a38fd5

**2. [Rule 1 - Bug] Programmatic control updates must not reset tested flags**
- **Found during:** Task 2
- **Issue:** Filling the key boxes in `OnEnterStepAsync` fires `TextChanged`, which would clear the tested flags when navigating Back to the step.
- **Fix:** An `_suppressEvents` guard wraps programmatic updates of the key boxes, provider combo and model combos.
- **Commit:** 21712b3

**3. Small refactor:** the key TextBoxes were first built by a factory method and then inlined, so the plan's grep (`UseSystemPasswordChar = true` found 2 times) matches literally (c50bc72).

## Manual Verification Needed (UI, not a gate per D-03)

- Launch the app and confirm both key sections render and all 13 rows fit or scroll within the 900x600 wizard. The layout has `MinimumSize` height 520 and `AutoScroll` enabled on the step.
- Select Claude with no OpenAI key: the orange media notice should show and Next should unlock after the Anthropic test passes.
- Clear a key, relaunch, and confirm the key is gone.
- In Configure Models, edit Provider, Default For Tier and Effort, click OK, and reopen the dialog to confirm the edits persisted.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Ai/LlmProviderFactory.cs, ReelDiscovery.Tests/WizardStateProviderTests.cs, ReelDiscovery.Tests/MediaGatingTests.cs
- FOUND commits: 1fad1fb, bcc081c, 21712b3, c50bc72, 9a38fd5
