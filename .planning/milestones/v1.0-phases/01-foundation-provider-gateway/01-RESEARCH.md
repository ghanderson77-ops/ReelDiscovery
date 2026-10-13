# Phase 1: Foundation & Provider Gateway - Research

**Researched:** 2026-09-29
**Domain:** .NET 10 WinForms retarget, multi-provider LLM gateway (Anthropic + OpenAI official SDKs), strict JSON-schema structured outputs, DPAPI key storage, xUnit test harness, concurrency fixes
**Confidence:** HIGH for SDK surfaces and build/test mechanics (verified by compiling and running probes against the real packages in this session). MEDIUM for OpenAI default-model choice (verified from official model pages, but it is a judgement call the user should confirm).

## Summary

Most of this phase was verified by building code rather than reading about it. A copy of the repo was retargeted to `net10.0-windows` with OpenAI 2.14.0, DocumentFormat.OpenXml 3.5.1, Anthropic 12.51.0 and Microsoft.Extensions.AI.Abstractions 10.10.1. It **compiled with zero errors**, and `dotnet publish -c Release` produced a 64 MB single-file `ReelDiscovery.exe` that launched and stayed running. An xUnit test project nested inside the repo and referencing the WinExe passed under a bare `dotnet test` at the repo root, including a DPAPI `ProtectedData` round trip with no extra package. The platform work (PLAT-01, PLAT-03) is therefore low risk. The only real trap is MSBuild default globbing: the app project must `Compile Remove` the test folder, or the build fails with confusing "duplicate attribute" errors.

Both SDKs were exercised through a fake `HttpMessageHandler`, so no network or API key was needed. This confirmed exactly what the outcome classifier (PROV-04) sees:
- **Anthropic:** `Message.StopReason.Raw()` returns `"refusal"`, `"max_tokens"` or `"end_turn"`. Errors arrive as typed exceptions (`AnthropicUnauthorizedException`, `AnthropicRateLimitException`, `AnthropicBadRequestException`, a generic `Anthropic4xxException` for 402, `Anthropic5xxException`) carrying `ErrorType` (including `BillingError`) and `ResponseBody`. The spend-cap body carries `enforced_spend_limit_reached`.
- **OpenAI:** `ChatCompletion.Refusal`, `FinishReason == Length`, and a `ClientResultException` whose raw body carries `insufficient_quota`.

This fake-HTTP technique is also the recommended unit-test strategy for the provider translators. It tests both the outgoing wire JSON and the response classification.

Model facts changed materially from the prior research:
1. **`gpt-image-1` and `gpt-image-1-mini` shut down on 2026-12-01.** Using them as the "dall-e-3 replacement" would break again in two months. Use **`gpt-image-2.5-flare`** at `quality: low`, 1024x1024 (about $0.006 per image).
2. **Claude Opus 5.5 (`claude-opus-5-5`) always thinks.** Thinking cannot be disabled, thinking tokens count against `max_tokens`, and responses begin with `thinking` blocks. It also rejects non-default `temperature`/`top_p`/`top_k` (400).
3. **Haiku 4.5 does not accept the `effort` parameter.** Send `effort` only when the catalog entry sets it.
4. **The OpenAI lineup is now gpt-6.x.** The best non-reasoning flagship-class option is **`gpt-6-sol` with `reasoning_effort: none`** ($2/$10). The cheapest tier is **`gpt-6-luna` with `reasoning_effort: none`** ($0.10/$0.50). `gpt-6.1-sol` is newer, but its lowest effort is `low`, so it is not a non-reasoning option.

**Primary recommendation:** Build an app-owned `ILlmGateway` in `Services/Ai/` with one translator per provider over the native SDKs. Generate a strict schema per DTO type once with `AIJsonUtilities.CreateJsonSchema` and cache it. Classify every response as succeeded, refused, truncated, invalid or failed (with a failure kind). Unit-test everything through fake HTTP handlers. Sequence the work as platform, then bug fixes, then gateway, then keys/catalog, then generator port, then UI.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Legacy flow on the gateway**
- **D-01:** Port all four legacy generators (`StorylineGenerator`, `CharacterGenerator`, `ThemeGenerator`, `EmailGenerator`) **fully** onto the new gateway with strict JSON-schema DTOs. The old topic → storylines → characters → emails flow must work end to end with **either** Claude or OpenAI. After the port, `OpenAIService` is no longer a text-generation path. It may survive only as the OpenAI media client (images/TTS) or be folded into the OpenAI provider.
- **D-02:** While porting, also fix the cheap known bugs in the old flow:
  - `replyToIndex` is resolved as absolute instead of batch-relative for storylines with more than 15 emails. Fix it to be batch-relative.
  - A single failed attachment, image, calendar invite or voicemail must no longer abort the whole run. Isolate per-item failures, log them to `GenerationResult.Errors`, skip the item and continue.
  - PLAT-04 concurrency fixes: use `Random.Shared` instead of the shared `System.Random`, and use thread-safe collections or remove mutation in parallel sections.
  - Larger dead-code removal stays in Phase 7 (PLAT-05). Only remove what the port naturally touches.
- **D-03:** Verification for this phase is **unit tests only**. No mandatory live wizard run or live-API test is part of the success gate. Tests cover the provider translators (request building for each provider), schema generation (strict: `additionalProperties:false`, all properties required, no unsupported constraint keywords), outcome classification, DPAPI key store and migration, and the concurrency fixes where testable.

**Keys & provider screen (`UserControls/StepApiConfiguration.cs`)**
- **D-04:** Layout is **two key sections**, OpenAI and Anthropic. Each has its own key box, a Test button and a status indicator. Below them is a **"Text provider: OpenAI / Claude"** picker and the tier model dropdowns (see D-11). A media status line under the OpenAI key section shows whether images and voicemail are available.
- **D-05:** On a fresh install the default text provider is **OpenAI**.
- **D-06:** Clicking Next requires only the **selected text provider's** key to have passed its test. If Claude is selected and the OpenAI key is missing or untested, the user can still continue. Images and voicemail are disabled and a clear notice is shown (PROV-06).
- **D-07:** Only the keys are persisted, encrypted with DPAPI (`ProtectedData`, CurrentUser scope) under `%LOCALAPPDATA%\ReelDiscovery\`. The provider choice and model selections are **not** remembered and reset to defaults on every launch.
- **D-08:** Each key has a **Clear** button that removes it from the encrypted store.
- **D-09:** The existing Base64 `reeldiscovery.settings` key file is migrated automatically to the DPAPI store (as the OpenAI key) on first launch. The plaintext-equivalent file is then removed or overwritten.

**Model lineup & tiers**
- **D-10:** Models are organized in **two tiers**:
  - **Story** (strong model): anything that defines or carries the case story. That means the core document, the case guide, and relevant or hot evidence in later phases.
  - **Volume** (cheap/fast model): bulk non-relevant filler, themes, and QA sampling in later phases.
  Every generator call declares the tier it needs, and the gateway resolves the tier to a concrete model for the selected provider. This comes from the user's explicit request that the story builder run on a higher model tier.
- **D-11:** **One text provider per run.** Both tiers use the selected provider, and the tiers never mix providers. The keys screen shows **two dropdowns**, "Story model" and "Volume model", filtered to the selected provider and prefilled with that provider's defaults. The existing "Configure Models…" dialog (`UserControls/ModelConfigurationDialog.cs`) still edits the catalog and pricing, and gains a **Provider** column.
- **D-12:** Claude defaults are **Story = Claude Opus 5.5**, **Volume = Claude Haiku 4.5**. The researcher verifies the exact model IDs and prices.
- **D-13:** OpenAI defaults: the **researcher verifies the current model IDs and prices.** Story is OpenAI's current flagship chat model (prefer non-reasoning or low-latency for throughput), and Volume is its cheapest current tier. Do not hard-code gpt-4o / gpt-4o-mini as the defaults without verifying. The research confidence on OpenAI naming is LOW.
- **D-14:** Legacy flow tier mapping: **Story** for storylines and characters, **Volume** for emails and themes.

### Claude's Discretion
- **Outcome UX for refusals, truncations and spend-limit errors:** how each classified outcome is shown to the user (status label or log color), and the retry policy for each class. Constraints: never blindly retry a refusal or a spend-limit or quota error. Truncation may get one retry with a higher max-token limit. Invalid JSON may be retried a bounded number of times. Rely on the SDKs' built-in 429/5xx retries, with no Polly and no double-retry.
- **Default gpt-image tier and size/quality** for PROV-05 (for example gpt-image-1-mini for cost). Pick a value and make it configurable where cheap to do so. TTS stays `tts-1` unless research shows otherwise.
- **Test framework:** xUnit is recommended by the research. The test project layout is up to Claude, as is whether an opt-in live smoke test gated by an environment key exists. It must not be required for the phase to pass.
- The exact `ILlmProvider` / `LlmRequest` / `LlmResult<T>` shape, provided it follows the research's direction: native SDKs, one translator per provider, MEAI used only for schema generation, and a stable-prefix / variable-suffix request structure so Phase 7 batch and caching fit without redesign.
- The upgrade sequencing of OpenAI 2.1.0 → 2.14.0 and DocumentFormat.OpenXml 3.0.2 → 3.5.1, and the regression checks for the existing Office builders.

### Deferred Ideas (OUT OF SCOPE)
- Remembering the provider and model selection between sessions. The user chose to persist keys only. Revisit if resetting to defaults becomes annoying.
- Mixing providers across tiers (for example Story on Claude, Volume on OpenAI). The user rejected this for now in favor of one provider per run.
- A per-task model table or a third "Evidence" tier. Two tiers were chosen. Revisit in Phase 7 if cost tuning needs finer control.
- A mandatory live smoke run of the legacy wizard on each provider. The user chose unit tests only.
- (Phase boundary) Batch APIs, prompt caching, cache/batch cost accounting (Phase 7, PROV-07); case-driven flow (Phases 2+); removal of the old wizard (Phase 7).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PLAT-01 | net10.0-windows, SDK pinned via global.json, still single-file self-contained exe | Verified: trial retarget compiles clean. `dotnet publish -c Release` produced a single `ReelDiscovery.exe` (64 MB) that launched. SDK 10.0.401 is installed. See "Platform Retarget" and "Code Examples: global.json/csproj". |
| PLAT-02 | DPAPI-encrypted keys, Base64 file migrated | Verified: `ProtectedData` is available on net10.0-windows with no package (xUnit round-trip passed). See "Pattern 6: ApiKeyStore". |
| PLAT-03 | Test project runs unit tests | Verified: nested `ReelDiscovery.Tests` (xUnit 2.9.3 VSTest) referencing the WinExe passes under bare `dotnet test` at repo root. Needs `Compile Remove`. Planner/answer-key tests belong to later phases. In Phase 1 the tests cover the gateway, schemas, key store, concurrency, EML and Office. |
| PLAT-04 | Shared Random and parallel-mutation bugs fixed | Codebase inventory with exact line numbers in "Concurrency & Bug Inventory". |
| PROV-01 | Choose Claude or OpenAI + model | Verified model IDs and prices (Anthropic official models page, OpenAI model pages). See "Model Catalog Defaults". |
| PROV-02 | Enter and test both keys | Free key test via `client.Models.Retrieve(modelId)` (Anthropic) and `OpenAIModelClient.GetModelAsync(modelId)`. This validates the key and model availability without spending tokens. |
| PROV-03 | One abstraction, strict JSON-schema on both | Verified wire JSON for both translators (`output_config.format` json_schema; `response_format` json_schema strict:true). Verified MEAI schema output. See "Schema Generation". |
| PROV-04 | Classified outcomes; no blind retry of refusal/spend | Verified exception types, ErrorType values, stop reasons, refusal field and quota body via fake HTTP. See "Outcome Classification Table". |
| PROV-05 | gpt-image model instead of dall-e-3 | Verified request serialization for `gpt-image-2.5-flare` with `quality: low`, `size: 1024x1024`, no `response_format`. gpt-image-1/1-mini shut down 2026-12-01. |
| PROV-06 | Claude + no OpenAI key: images and voicemail disabled with notice | Gating points identified: `StepTopicInput` (lines 139-157, 227-232), `StepGenerationConfig` (lines 225-318, 529-535, 592-595), `EmailGenerator` media passes (lines 214, 298, 483-488). See "Pattern 7: Media gating". |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- C# / .NET WinForms, Windows x64, single-file self-contained publish. Reuse the MimeKit and OpenXml builders.
- OpenAI stays supported. Claude is added through a provider abstraction.
- Naming: `*Service` for I/O/external APIs, `*Generator` for AI content, `*Helper` for static utilities, `*Dialog` for modals. Async methods end in `Async`, and `CancellationToken ct = default` is always the last parameter. Pass `string? operationName` labels for usage tracking.
- Fields use `_camelCase`, constants PascalCase, WinForms controls Hungarian after the underscore (`_txtOpenAIKey`, `_btnTestAnthropic`, `_cboStoryModel`).
- No `record` types, no primary constructors, no `#region`. Models are mutable classes with auto-properties. **Note:** this rules out `sealed record LlmRequest(...)` from ARCHITECTURE.md. Use classes.
- `[GeneratedRegex]` for any regex. `using var` for disposables. Target-typed `new()`. Nullable reference types on, and builds must stay warning-free by hand.
- Error handling: catch `OperationCanceledException` before `Exception`. Accumulate errors into `GenerationResult.Errors` during bulk generation. Use silent `catch` only for non-critical side effects, and always with an explanatory comment. Never use bare `catch` in core generation.
- UI is built in code, with `TableLayoutPanel` rows carrying row-index comments. Use `ButtonHelper.CreateButton(...)`, Segoe UI, and `MessageBox` warnings in `ValidateStepAsync`.
- Thread safety: `Interlocked`, concurrent collections, `lock (_lock)`, and **`Random.Shared` in new code**.
- Implicit usings: don't add explicit usings for `System`, `System.IO`, `System.Linq`, `System.Threading.Tasks`, `System.Windows.Forms`, `System.Drawing`.
- JSON DTOs are private nested `*Dto`/`*ApiResponse` classes. This research recommends relaxing them to `internal` nested classes plus `InternalsVisibleTo` so that schema tests can reach them (see Pitfall 7).
- GSD workflow: edits happen through GSD commands (execute-phase).

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Provider selection, key entry and test | UI (`StepApiConfiguration`) | Services (`ApiKeyStore`, gateway test call) | The UI only collects input and shows status. Key persistence and API calls live in services. |
| Key persistence (DPAPI) | Services (`Services/ApiKeyStore.cs`) | OS (DPAPI, `%LOCALAPPDATA%`) | Pure I/O, testable with a temp-folder constructor argument. |
| Tier → model resolution | Services (`LlmGateway`) | Models (`AIModelConfig` catalog, `WizardState` selections) | Generators declare a tier. The gateway resolves it using the selection snapshot taken when the gateway is created. |
| Request translation (wire JSON) | Services/Ai (`AnthropicTextProvider`, `OpenAITextProvider`) | — | One translator per provider, kept byte-stable for Phase 7 batch. |
| Schema generation | Services/Ai (`JsonSchemaRegistry`) | MEAI `AIJsonUtilities` | Cached per DTO type, with identical bytes on every call. |
| Outcome classification and retry policy | Services/Ai (`LlmGateway` + provider `Classify`/`MapException`) | SDK built-in 429/5xx retries | The gateway owns app-level retries (invalid, truncated). The SDKs own transport retries. |
| Usage/cost recording | Models (`TokenUsageTracker`) | Gateway (records after each call) | Existing thread-safe accumulator, extended with the provider. |
| Images / TTS | Services (`OpenAIMediaClient`) | — | OpenAI-only. Returns `null` on failure (existing convention). |
| Media availability gating | Models (`WizardState.IsMediaAvailable`) | UI (StepTopicInput, StepGenerationConfig), Services (EmailGenerator guard) | One computed property, read by three places. |
| Concurrency-safe generation | Services (`EmailGenerator`) | Models (`GenerationResult` error sink, progress snapshot) | The fixes are local to the orchestrator and result types. |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK / WinForms | SDK **10.0.401** (installed), runtime 10.0.12, `net10.0-windows` | Runtime | .NET 8 EOL 2026-11-10. Retarget verified to compile and publish. [VERIFIED: local `dotnet --list-sdks`, trial build] |
| `Anthropic` | **12.51.0** (latest, 2026-09-22) | Claude Messages, structured outputs, typed exceptions | Official SDK. Verified: `MessageCreateParams.OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = IReadOnlyDictionary<string, JsonElement> } }`, `Effort` enum (Low, Medium, High, Xhigh, Max), `StopReason` enum (EndTurn, MaxTokens, StopSequence, ToolUse, PauseTurn, Refusal, ModelContextWindowExceeded), `ClientOptions { ApiKey, HttpClient, MaxRetries, Timeout }`. [VERIFIED: NuGet index + reflection + compile] |
| `OpenAI` | **2.14.0** (latest) | Chat Completions (strict JSON schema), Images, TTS, Models | Official SDK. The upgrade from 2.1.0 compiled with **no errors** on the existing `OpenAIService.cs`. [VERIFIED: trial build] |
| `Microsoft.Extensions.AI.Abstractions` | **10.10.1** (latest) | `AIJsonUtilities.CreateJsonSchema` + `AIJsonSchemaTransformOptions { DisallowAdditionalProperties, RequireAllProperties, MoveDefaultKeywordToDescription }` | One schema source for both providers. Output verified (see Schema Generation). [VERIFIED: probe] |
| `DocumentFormat.OpenXml` | **3.5.1** (upgrade from 3.0.2) | Office attachments | Compiles clean. Regression-test the three builders. [VERIFIED: trial build] |
| `MimeKit` | 4.18.1 (unchanged, already latest) | EML | [VERIFIED: NuGet index] |
| `System.Security.Cryptography.ProtectedData` | in-box on net10.0-windows | DPAPI | **Do not add the NuGet package.** NU1510 (the .NET 10 SDK flags pruned direct references) would complain. [VERIFIED: xUnit round-trip with no package; CITED: learn.microsoft.com .NET 10 breaking changes list, "NU1510 is raised for direct references pruned by NuGet"] |

### Test
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `xunit` | **2.9.3** | Test framework | This is what the SDK 10.0.401 `dotnet new xunit` template emits. It runs on VSTest, so `dotnet test` works out of the box. [VERIFIED: template + passing run] |
| `xunit.runner.visualstudio` | **3.1.4** | VSTest adapter | Template version, verified working. |
| `Microsoft.NET.Test.Sdk` | **17.14.1** | Test host | Template version, verified working. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| xUnit 2.9.3 (VSTest) | `xunit.v3` 4.0.1 | v3 4.x depends on `xunit.v3.mtp-v2` (Microsoft Testing Platform v2). With the .NET 10 SDK, `dotnet test` then needs MTP opt-in configuration. That's more setup for no Phase 1 benefit. [VERIFIED: nuspec] |
| `gpt-6-sol` + `reasoning_effort: none` (Story) | `gpt-6.1-sol` (effort `low` minimum) | 6.1 is newer and has the same $2/$10 price, but it cannot run non-reasoning. The user observed that reasoning models are about 10x slower. Keep 6.1-sol in the catalog as an alternative. |
| `gpt-image-2.5-flare` | `gpt-image-2` / `gpt-image-1-mini` | 1-mini shuts down 2026-12-01. Flare is described as "fastest model for high-quality, everyday image generation". |
| Haiku 4.5 (Volume, D-12 locked) | Sonnet 5.5 ($2/$10) | Haiku 4.5's retirement is "not sooner than 2026-10-15", but no deprecation has been announced yet, and Anthropic gives at least 60 days' notice. Keep Haiku per D-12 and keep Sonnet 5.5 in the catalog as a fallback. |

**Installation:**
```bash
# in ReelDiscovery.csproj: <TargetFramework>net10.0-windows</TargetFramework>
dotnet add ReelDiscovery.csproj package OpenAI --version 2.14.0
dotnet add ReelDiscovery.csproj package DocumentFormat.OpenXml --version 3.5.1
dotnet add ReelDiscovery.csproj package Anthropic --version 12.51.0
dotnet add ReelDiscovery.csproj package Microsoft.Extensions.AI.Abstractions --version 10.10.1
# tests (created with dotnet new xunit, then retargeted to net10.0-windows + UseWindowsForms)
```

**Version verification:** Checked against the NuGet flat-container index on 2026-09-29. Anthropic 12.51.0, OpenAI 2.14.0, MEAI.Abstractions 10.10.1, DocumentFormat.OpenXml 3.5.1, MimeKit 4.18.1, xunit 2.9.3 and xunit.v3 4.0.1 are all the latest stable versions.

## Model Catalog Defaults (D-12, D-13)

| Provider | Tier | Model ID | Input $/MTok | Output $/MTok | Effort param to send | Notes |
|----------|------|----------|-------------|---------------|---------------------|-------|
| Anthropic | **Story default** | `claude-opus-5-5` | 4.00 | 20.00 | omit (API default `medium`) | Released 2026-09-22. Adaptive thinking **always on**, and thinking counts against `max_tokens`. Returns 400 if `temperature`/`top_p`/`top_k` are non-default. Structured outputs supported. Max output 128K. [VERIFIED: platform.claude.com models overview + Opus 5.5 page] |
| Anthropic | alt | `claude-sonnet-5-5` | 2.00 | 10.00 | omit | "Best combination of speed and intelligence". Adaptive thinking (default effort `high`). Fallback if Haiku retires. [VERIFIED] |
| Anthropic | **Volume default** | `claude-haiku-4-5-20251001` (alias `claude-haiku-4-5`) | 1.00 | 5.00 | **must omit** (effort "Not supported") | Fastest. Extended thinking is off unless enabled. Max output 64K. Structured outputs supported. Retirement "not sooner than 2026-10-15" with no deprecation notice yet. [VERIFIED: models overview, deprecations page] |
| OpenAI | **Story default** | `gpt-6-sol` | 2.00 | 10.00 | `reasoning_effort: none` | Supports none/low/medium(default)/high/xhigh/max. Chat Completions, Batch and structured outputs supported. Page notes "A newer version, GPT-6.1 Sol, is now available". [VERIFIED: developers.openai.com/api/docs/models/gpt-6-sol] |
| OpenAI | alt | `gpt-6.1-sol` | 2.00 | 10.00 | `low` (none/minimal **not supported**) | Newest flagship-class model, but it always reasons. [VERIFIED: model page] |
| OpenAI | **Volume default** | `gpt-6-luna` | 0.10 | 0.50 | `reasoning_effort: none` | "Our most efficient model for focused, high-volume tasks". Chat Completions, Batch and structured outputs supported. [VERIFIED: model page] |
| OpenAI (media) | image | `gpt-image-2.5-flare` | token-priced ($5 text in / $30 image out) | ≈ $0.006 per 1024² low | `quality: low` | b64 only. Sizes 1024x1024 / 1536x1024 / 1024x1536. [VERIFIED: model page, image guide; per-image estimate from third-party pricing articles: LOW] |
| OpenAI (media) | TTS | `tts-1` | $15 / 1M chars | — | — | Not deprecated. Keep it. [VERIFIED: deprecations page, pricing page] |

**Catalog shape implication:** `AIModelConfig` needs `Provider` (enum), `DefaultForTier` (Story / Volume / none), and **`Effort` (`string?`, sent only when non-null)**. Sending `reasoning_effort` to a model that doesn't support it, or `effort` to Haiku 4.5, is a 400 error [ASSUMED for OpenAI non-reasoning models; CITED for Haiku "Not supported"]. Also add `MaxOutputTokens` (model ceiling, used to cap truncation retries: Opus 5.5 128K, Haiku 64K, gpt-6 128K).

## Architecture Patterns

### System Architecture Diagram

```
 WizardForm / Step* (UI thread)
   │  keys, provider, story/volume model picks
   ▼
 WizardState ──CreateGateway()──► LlmGateway (ILlmGateway) ◄── TokenUsageTracker (records provider/model/tokens)
   │                                  │  1. resolve Tier → AIModelConfig (selected provider only)
   │                                  │  2. JsonSchemaRegistry.Get<T>() → cached schema string (MEAI, strict)
   │                                  │  3. provider.SendAsync(LlmProviderRequest)
   │                                  │  4. classify: Succeeded | Refused | Truncated | Invalid | Failed(kind)
   │                                  │  5. deserialize T (case-insensitive) + app retry policy
   │                                  ▼
   │                   ┌──────────────┴───────────────┐
   │                   ▼                              ▼
   │        AnthropicTextProvider            OpenAITextProvider
   │        LlmProviderRequest→              LlmProviderRequest→
   │        MessageCreateParams              ChatMessages+ChatCompletionOptions
   │        (system blocks, output_config)   (system/user, response_format strict)
   │        Message→ProviderResponse         ChatCompletion→ProviderResponse
   │        Anthropic*Exception→FailureKind  ClientResultException→FailureKind
   │                   │ HTTPS (SDK retries 429/5xx)  │
   │                   ▼                              ▼
   │             api.anthropic.com               api.openai.com
   │
   ├──CreateMediaClient()──► OpenAIMediaClient (null when no tested OpenAI key)
   │                            GenerateImageAsync (gpt-image-2.5-flare, b64)  GenerateSpeechAsync (tts-1)
   ▼
 Generators (Storyline[Story], Character[Story], Theme[Volume], Email[Volume])
   └─ EmailGenerator: storyline threads → per-item isolated enrichment (docs, images*, calendar, voicemail*)
        → ConcurrentQueue errors → EML save          (* skipped when !IsMediaAvailable)
```

### Recommended Project Structure
```
ReelDiscovery.slnx                 # new (dotnet new sln defaults to .slnx on SDK 10)
global.json                        # new: pin SDK 10.0.401, rollForward latestFeature
ReelDiscovery.csproj               # net10.0-windows, packages, Compile Remove tests, InternalsVisibleTo
Services/
├── Ai/                            # the ONLY folder referencing Anthropic / OpenAI.Chat / MEAI
│   ├── LlmTier.cs                 # enum Story, Volume
│   ├── LlmProviderKind.cs         # enum OpenAI, Anthropic
│   ├── LlmRequest.cs              # Tier, OperationName, SystemPrompt (stable), UserPrompt (variable), MaxOutputTokens
│   ├── LlmResult.cs               # LlmResult<T>, LlmOutcome, LlmFailureKind, LlmCallException
│   ├── ILlmGateway.cs / LlmGateway.cs
│   ├── ILlmProvider.cs            # SendAsync(LlmProviderRequest) → LlmProviderResponse
│   ├── LlmProviderRequest.cs      # resolved: ModelId, Effort, schema name+json, prompts, MaxTokens
│   ├── AnthropicTextProvider.cs   # translator + classifier + exception mapper
│   ├── OpenAITextProvider.cs      # translator + classifier + exception mapper (+ QuotaAwareRetryPolicy)
│   ├── JsonSchemaRegistry.cs      # cached strict schemas per Type
│   └── OpenAIMediaClient.cs       # images + TTS (replaces OpenAIService)
├── ApiKeyStore.cs                 # DPAPI + legacy migration
└── (generators ported to ILlmGateway)
ReelDiscovery.Tests/
├── ReelDiscovery.Tests.csproj     # net10.0-windows, UseWindowsForms, xunit 2.9.3
├── Fakes/FakeHttpHandler.cs       # canned status/body, captures request JSON
├── Fakes/FakeLlmProvider.cs
├── Ai/*Tests.cs, ApiKeyStoreTests.cs, Concurrency/*Tests.cs, Documents/*Tests.cs
```

### Pattern 1: Gateway contract (classes, not records, per CLAUDE.md)
```csharp
// Services/Ai/LlmRequest.cs
public class LlmRequest
{
    public LlmTier Tier { get; set; }
    public string OperationName { get; set; } = string.Empty;   // "Storyline Generation"
    public string SystemPrompt { get; set; } = string.Empty;    // stable prefix: byte-identical across calls (Phase 7 caching)
    public string UserPrompt { get; set; } = string.Empty;      // variable suffix
    public int MaxOutputTokens { get; set; } = 16_000;          // includes thinking on Opus 5.5 / reasoning on OpenAI
}

public enum LlmOutcome { Succeeded, Refused, Truncated, Invalid, Failed }
public enum LlmFailureKind { None, Auth, SpendLimit, RateLimited, Transient, BadRequest, Network, Unknown }

public class LlmResult<T> where T : class
{
    public LlmOutcome Outcome { get; set; }
    public LlmFailureKind FailureKind { get; set; }
    public T? Value { get; set; }
    public string? RawText { get; set; }
    public string? Message { get; set; }          // refusal text / error message (never contains the API key)
    public string Provider { get; set; } = "";
    public string ModelId { get; set; } = "";
    public int Attempts { get; set; }
    public bool IsFatal => FailureKind is LlmFailureKind.Auth or LlmFailureKind.SpendLimit;
    public T GetValueOrThrow() => Outcome == LlmOutcome.Succeeded && Value != null
        ? Value : throw new LlmCallException(this);   // generators keep "throw on missing output" convention
}

public interface ILlmGateway
{
    LlmProviderKind Provider { get; }
    Task<LlmResult<T>> GenerateAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class;
}
```

### Pattern 2: App retry policy (inside the gateway, on top of SDK transport retries)
| Outcome / kind | App retries | Next step |
|---|---|---|
| Succeeded | — | return |
| Refused | **0** | return Refused. Generator skips the item and logs it Orange ("Refused by model: {category/text}"). |
| Truncated | **1**, with `MaxOutputTokens * 2` capped at `AIModelConfig.MaxOutputTokens` | then return Truncated → skip the item |
| Invalid (JSON parse failed / null / empty text on `end_turn`) | **2** (3 attempts total) | then return Invalid → skip the item |
| Failed: Auth, SpendLimit | **0** | return. `IsFatal` → the EmailGenerator cancels the run via a linked CTS, saves what it has, and logs Red. |
| Failed: RateLimited, Transient, Network | **0** (the SDK already retried: Anthropic 2x, OpenAI 3x) | skip the item and log Red |
| Failed: BadRequest | 0 | skip and log Red (usually a schema/model bug; message included) |

### Pattern 3: Anthropic translator (compiled and serialized in probe)
```csharp
// Source: verified against Anthropic 12.51.0 in scratch probe (RawBodyData dump)
using Anthropic;
using Anthropic.Models.Messages;
using AnthropicMessage = Anthropic.Models.Messages.Message;   // REQUIRED: 'Message' clashes with System.Windows.Forms.Message

internal static MessageCreateParams BuildParams(LlmProviderRequest r) => new()
{
    Model = r.ModelId,                                   // string converts implicitly
    MaxTokens = r.MaxTokens,
    System = new List<TextBlockParam> { new(r.SystemPrompt) },   // list form → Phase 7 adds CacheControl on this block
    Messages = [new() { Role = Role.User, Content = r.UserPrompt }],
    OutputConfig = new OutputConfig
    {
        Format = new JsonOutputFormat { Schema = r.SchemaDictionary },  // Dictionary<string, JsonElement> from cached schema JSON
        // Effort only when catalog sets it (never for Haiku 4.5):
        // Effort = Effort.Low
    },
    // NEVER set Temperature/TopP/TopK (400 on Opus 4.7+), NEVER set Thinking (Opus 5.5 rejects disabled/enabled)
};
// Wire JSON produced: {"model":...,"max_tokens":...,"system":[{"type":"text","text":...}],
//   "messages":[...],"output_config":{"format":{"type":"json_schema","schema":{...}}}}

internal static LlmProviderResponse Classify(AnthropicMessage m)
{
    var text = new StringBuilder();
    foreach (var block in m.Content)                    // skip thinking blocks: select by type, not position
        if (block.TryPickText(out var t)) text.Append(t.Text);
    var stop = m.StopReason?.Raw();                     // raw string: robust to future enum values
    var outcome = stop switch
    {
        "end_turn" or "stop_sequence" => LlmOutcome.Succeeded,   // parse → Invalid if JSON bad
        "refusal" => LlmOutcome.Refused,                // m.StopDetails?.Category / Explanation for the log
        "max_tokens" => LlmOutcome.Truncated,
        _ => LlmOutcome.Failed,                         // pause_turn, tool_use, model_context_window_exceeded
    };
    // usage: m.Usage.InputTokens, m.Usage.OutputTokens (long), CacheReadInputTokens (Phase 7)
    ...
}
```

### Pattern 4: OpenAI translator (compiled and serialized in probe)
```csharp
// Source: verified against OpenAI 2.14.0 via fake transport; captured request body shown below
var options = new ChatCompletionOptions
{
    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
        jsonSchemaFormatName: r.SchemaName,            // DTO type name; keep to [A-Za-z0-9_-]
        jsonSchema: BinaryData.FromString(r.SchemaJson),
        jsonSchemaIsStrict: true),
    MaxOutputTokenCount = r.MaxTokens,                 // serialized as max_completion_tokens
};
if (r.Effort != null) options.ReasoningEffortLevel = new ChatReasoningEffortLevel(r.Effort);  // "none" → "reasoning_effort":"none"
var messages = new List<ChatMessage> { new SystemChatMessage(r.SystemPrompt), new UserChatMessage(r.UserPrompt) };
var completion = (await chatClient.CompleteChatAsync(messages, options, ct)).Value;
// classify:
//   !string.IsNullOrEmpty(completion.Refusal)            → Refused   (Content.Count == 0 in that case)
//   completion.FinishReason == ChatFinishReason.Length   → Truncated
//   completion.FinishReason == ChatFinishReason.ContentFilter → Refused
//   Stop                                                  → Succeeded (text = completion.Content[0].Text)
// usage: completion.Usage.InputTokenCount / OutputTokenCount / InputTokenDetails?.CachedTokenCount
// Wire: {"messages":[...],"model":"gpt-6-luna","reasoning_effort":"none","max_completion_tokens":1000,
//        "response_format":{"type":"json_schema","json_schema":{"name":"...","schema":{...},"strict":true}}}
```

### Pattern 5: Exception → FailureKind mapping (verified via fake HTTP)
```csharp
// Anthropic (catch order: most specific first; OperationCanceledException before all)
catch (AnthropicApiException ex)
{
    var body = ex.ResponseBody ?? "";
    var kind = ex switch
    {
        AnthropicUnauthorizedException or AnthropicForbiddenException => LlmFailureKind.Auth,
        AnthropicRateLimitException when body.Contains("enforced_spend_limit_reached") => LlmFailureKind.SpendLimit,
        AnthropicRateLimitException => LlmFailureKind.RateLimited,
        _ when (ex as AnthropicServiceException)?.ErrorType == ErrorType.BillingError => LlmFailureKind.SpendLimit, // 402
        AnthropicBadRequestException when body.Contains("usage limits") || body.Contains("credit balance")
            => LlmFailureKind.SpendLimit,   // user-set spend limit returns 400 "You have reached your specified ... API usage limits"
        AnthropicBadRequestException or AnthropicNotFoundException or AnthropicUnprocessableEntityException => LlmFailureKind.BadRequest,
        Anthropic5xxException => LlmFailureKind.Transient,   // 500/529 after SDK retries
        _ => LlmFailureKind.Unknown,
    };
}
catch (AnthropicIOException) { /* Network */ }

// OpenAI
catch (ClientResultException ex)
{
    var body = ex.GetRawResponse()?.Content?.ToString() ?? "";
    var kind = ex.Status switch
    {
        401 or 403 => LlmFailureKind.Auth,
        429 when body.Contains("insufficient_quota") => LlmFailureKind.SpendLimit,
        429 => LlmFailureKind.RateLimited,
        400 or 404 or 422 => LlmFailureKind.BadRequest,
        >= 500 => LlmFailureKind.Transient,
        0 => LlmFailureKind.Network,
        _ => LlmFailureKind.Unknown,
    };
}
```
Verified observations: an Anthropic 429 spend-cap with the default `MaxRetries` made **3 HTTP calls (≈1.4 s)** before throwing. That is unbilled and acceptable. An OpenAI 429 `insufficient_quota` would be retried 3x by the default `ClientRetryPolicy`. To avoid that, subclass `ClientRetryPolicy` and override the virtual `ShouldRetry`/`ShouldRetryAsync` so a 429 whose body contains `insufficient_quota` is not retried (MEDIUM: API verified by reflection, behaviour not yet run).

### Pattern 6: ApiKeyStore (DPAPI + migration)
```csharp
// Services/ApiKeyStore.cs. Folder injectable for tests (default %LOCALAPPDATA%\ReelDiscovery)
public class ApiKeyStore
{
    private const string KeysFileName = "keys.dat";                  // JSON: { "openai": "<b64 of protected bytes>", "anthropic": "..." }
    private const string LegacySettingsFileName = "reeldiscovery.settings";
    private static readonly byte[] Entropy = "ReelDiscovery.ApiKeys.v1"u8.ToArray();
    private readonly string _folder;
    public ApiKeyStore(string? folder = null) { _folder = folder ?? DefaultFolder(); }
    public string? GetKey(LlmProviderKind provider) { ... ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser) ... }
    public void SetKey(LlmProviderKind provider, string key) { ... Protect ... write temp file then File.Move(overwrite: true) ... }
    public void ClearKey(LlmProviderKind provider) { ... }
    /// Migrates legacy Base64 file → OpenAI key; deletes legacy file. Idempotent; never overwrites an existing DPAPI OpenAI key.
    public bool MigrateLegacyKey() { ... }
}
```
- Call `MigrateLegacyKey()` once at startup (`Program.cs`, before `WizardForm`) or in `StepApiConfiguration.OnEnterStepAsync`. Wrap it in try/catch and **log** rather than swallow silently (it is not core generation, but a failed migration should be visible).
- Before deleting the legacy file, overwrite its contents (write empty text), then delete it (D-09 "removed or overwritten").
- Corrupt or undecryptable entries (for example a profile copied to another machine, where DPAPI throws `CryptographicException`): treat the key as absent and do not crash.

### Pattern 7: Media gating (PROV-06)
```csharp
// Models/WizardState.cs
public bool OpenAIKeyTested { get; set; }
public bool IsMediaAvailable => !string.IsNullOrWhiteSpace(OpenAIApiKey) && OpenAIKeyTested;
public string MediaUnavailableNotice =>
    "Images and voicemail need a tested OpenAI key. Add one on the API Keys step to enable them.";
```
- `StepApiConfiguration`: media status line under the OpenAI section. Green "Images and voicemail available" or Orange with the notice.
- `StepTopicInput.OnEnterStepAsync`: `_chkImages.Enabled = _chkVoicemails.Enabled = _state.IsMediaAvailable`. If unavailable, uncheck them and show the notice label.
- `StepGenerationConfig.OnEnterStepAsync`: same for `_chkImages`, `_numImagePercent`, `_chkVoicemails`, `_numVoicemailPercent`. Force `Config.IncludeImages/IncludeVoicemails = false` in `ValidateStepAsync` when unavailable. Also update the "DALL-E" section header and the "~$0.04/image" text.
- `EmailGenerator`: defensive guard. Treat `IncludeImages`/`IncludeVoicemails` as false when the media client is `null`, so the prompts also stop planning those attachments (lines 483-488).

### Anti-Patterns to Avoid
- **`IChatClient` as the core abstraction:** the locked direction is native SDKs, with MEAI used only for schemas.
- **Letting generators catch provider exceptions:** generators should only see `LlmResult<T>` / `LlmCallException`. Keep all SDK types inside `Services/Ai/`.
- **Prompt-embedded JSON templates ("Respond with JSON in this exact format: {...}"):** with strict schemas these duplicate the schema and can conflict with it. Remove them. Move field semantics into `[Description("...")]` on DTO properties (MEAI emits them into the schema), and keep behavioural instructions in the prompt.
- **`ChatResponseFormat.CreateJsonObjectFormat()` and the "Respond ONLY with valid JSON" suffix:** replace them with strict schema.
- **Setting `ImageGenerationOptions.ResponseFormat` or `Style` for gpt-image models:** these are DALL-E parameters. gpt-image returns b64 only [CITED: image generation guide]. Verified: omitting them serializes cleanly.
- **Two retry layers:** don't wrap SDK calls in retry loops for 429/503. Keep the SDK's retries and the app's semantic retries (invalid/truncated) only.

## Schema Generation (verified output)

`AIJsonUtilities.CreateJsonSchema(type, serializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } }, inferenceOptions: new AIJsonSchemaCreateOptions { TransformOptions = new() { DisallowAdditionalProperties = true, RequireAllProperties = true, MoveDefaultKeywordToDescription = true } })` produced:

| C# shape | Emitted schema | OK for OpenAI strict? | OK for Anthropic? |
|---|---|---|---|
| `string` (NRT non-null) | `{"type":"string"}` | yes | yes |
| `string?` | `{"type":["string","null"]}` | yes (documented) | yes (counts toward the 16-union limit) |
| `int` / `bool` / `double` | integer / boolean / number | yes | yes |
| `List<string>` | array of string | yes | yes |
| `List<string>?` | `{"type":["array","null"],...}` | uncertain [ASSUMED] | yes |
| nested DTO? (`DateRangeDto?`) | `{"type":["object","null"],...}` | **uncertain** [ASSUMED]. The docs show only primitive unions. | yes |
| `List<List<JsonElement>>` (`ExcelDocResponseRaw`) | `"items":{}` (empty schema) | **no** | risky |
| `[Range]`, `[StringLength]`, `[RegularExpression]` | `minimum`/`maximum`/... (verified for `[Range]`) | yes | **no (rejected)** |
| enum without a string converter | `{"type":"integer"}` | yes, but meaningless | yes |
| `DateTime` | `{"type":"string","format":"date-time"}` | yes | yes |
| `[Description("...")]` | `"description"` | yes | yes |
| private nested class | works (reflection) | — | — |
| JsonPropertyName | respected | — | — |

**DTO hardening rules for the port (prescriptive):**
1. No nullable lists or objects. Use `List<string> CcEmails = new()` and a non-null `DateRangeDto` with empty-string fields ("leave empty if not requested"). This removes both "uncertain" rows and cuts the union count.
2. Replace `ExcelDocResponseRaw` (JsonElement rows) with `List<List<string>>`. Strict mode forces strings, so the workaround is obsolete. Keep the numeric-looking-string handling in `OfficeDocumentService` if it exists.
3. No DataAnnotations constraint attributes. Enforce counts and ranges in C# after parsing (clamp `hour` 0-23 and `minute` 0-59 in calendar; trim `response.Emails` to `batchSize`).
4. Register every DTO in `JsonSchemaRegistry` and cache the serialized JSON string per `Type` (identical bytes on every call).
5. Anthropic limits per request: **≤16 union-typed parameters**, ≤24 optional parameters (none if all are required), `minItems` only 0 or 1, no recursion [CITED: platform.claude.com structured-outputs]. `EmailDto` today has 5 nullable fields and `MeetingDetectionResponse` has 5. Both are fine, but assert it in a test.
6. Anthropic may alter the **capitalization of enum/const values** [CITED]. Always deserialize with `PropertyNameCaseInsensitive = true` and compare string values with `OrdinalIgnoreCase` (existing `PlannedDocumentType.ToLowerInvariant()` already does).

**StrictSchemaValidator (test helper, also usable in a DEBUG assert):** walk the JSON. Every `"type":"object"` node (or type array containing `object`) must have `additionalProperties:false`, and `required` must equal the property keys. Forbidden keywords anywhere: `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `minLength`, `maxLength`, `pattern`, `maxItems`, `minItems`>1, `allOf`, `not`, `if`, `patternProperties`. Also forbidden: empty schema `{}` and root not `object`. The union count must be ≤16.

## Concurrency & Bug Inventory (PLAT-04, D-02)

All locations verified by grep on `main` @ e3d6147.

| # | Bug | Location | Fix |
|---|-----|----------|-----|
| C1 | Shared `System.Random` field used in parallel lambdas | `Services/EmailGenerator.cs:13` (`_random`), uses at 273, 885, 1100, 1136, 1144, 1176, 1350, 1355, 1364, 1410 | Delete the field and use `Random.Shared` |
| C2 | Static shared Random | `Helpers/DateHelper.cs:5`, uses at 29, 38, 61, 71, 102 (`RandomDateInRange` is called from parallel storyline tasks via `ConvertDtoToEmailMessage:915`) | `Random.Shared` |
| C3 | Per-instance Random (not parallel today, but convention) | `Services/CharacterGenerator.cs:9`, uses at 31, 35, 62, 66, 72 | `Random.Shared` |
| C4 | `result.Errors.Add` from parallel tasks without a lock | `EmailGenerator.cs:127` (storyline catch). New per-item catches will add more | Add a thread-safe sink: `GenerationResult.AddError(string)` doing `lock (_errorsLock) Errors.Add(...)`. Keep `List<string> Errors` for existing readers (StepGeneration:201-204). |
| C5 | Mutable `GenerationProgress` reported while being mutated | `EmailGenerator.cs:95-96, 116, 133, 147, 152, 156, 168, 181, 220-222, 233, 243, 267, 304, 316, 348, 355, 391`; UI reads it in `StepGeneration.cs:182-186` | Add `GenerationProgress Clone()` (MemberwiseClone). Always `progress.Report(Snapshot())`, taking the snapshot inside `lock (progressLock)`. Mutate only under the lock (lines 147, 152, 156, 220-222, 267, 304, 348, 355 currently mutate outside it). |
| C6 | `DocumentChainState.VersionNumber++` on shared chain objects | `EmailGenerator.cs:1146` (read), `1170` (increment), `1172` (label) | Lock the chain (`lock (chainState)`) around read-increment-label, or use `Interlocked.Increment` and take the returned value for both the prompt and the label. The prompt at 1146 reads `VersionNumber + 1` before the call, so reserve the version number **before** generating. |
| C7 | `result.WordDocumentsGenerated++` etc. | already under `progressLock` (175-195, 243-246, 324-329) | keep |
| B1 | `replyToIndex` resolved absolutely | `EmailGenerator.cs:926-929` in `ConvertDtoToEmailMessage`. The prompt says batch-relative (679, 684, 832). | Pass `batchOffset` (= `thread.Messages.Count` at batch start). Resolve `idx = batchOffset + e.ReplyToIndex` when `0 <= e.ReplyToIndex < (emails already added in this batch)`, else fall back to `Last()`. Extract it as an `internal static EmailMessage? ResolveReferencedEmail(IReadOnlyList<EmailMessage> messages, int batchOffset, int replyToIndex)` for unit tests. |
| B2 | One failed enrichment aborts the run and nothing is saved | Attachment lambda 159-207, image 224-257, calendar 278-291, voicemail 307-338: each has `try/finally` without `catch` → `Task.WhenAll` faults → outer catch at 402 → `SaveAllEmailsAsync` (359) never runs | Add a per-item `catch (OperationCanceledException) { throw; } catch (Exception ex) { result.AddError($"Attachment for '{email.Subject}' failed: {ex.Message}"); return 0; }` inside each lambda. `LlmCallException` with `IsFatal` → cancel a linked CTS so remaining items stop, and **still save** the completed threads (move the save out of the happy path or into a `finally`-style block that runs on fatal and cancel). |
| B3 | Calendar `new DateTime(..., hour, minute, 0)` throws on hour ≥ 24 / minute ≥ 60 | `EmailGenerator.cs:1792-1796` | Clamp with `Math.Clamp` or fall back to 10:00. |
| B4 | `characters.Where(c => c.Id != fromChar.Id).First()` throws with one character | `EmailGenerator.cs:896` | `FirstOrDefault() ?? fromChar`. It's in code the port touches, so fix it cheaply. |
| B5 | Count mismatch: `emailsGenerated += response.Emails.Count` trusts the model | `EmailGenerator.cs:549` | Trim `response.Emails` to `batchSize`. A model shortfall is acceptable (log it). This prevents a zero or negative next `batchSize`. |

Not in scope (Phase 7 / PLAT-05 or later): dead methods (`SelectEmailsForAttachments`, `GenerateAttachmentAsync`, `GenerateImageForEmailAsync`, `GenerateVoicemailForEmailAsync`, `ImageSuggestionResponse`). Exception: they call `_openAI.*`, so the port **must** either delete them or re-point them. **Recommend deleting them** ("only remove what the port naturally touches" covers them, because they won't compile after `OpenAIService` loses its text methods).

## Call-Site Inventory for the Port (D-01)

| Call site | DTO | Tier (D-14) | Operation name |
|---|---|---|---|
| `StorylineGenerator.cs:233` | `StorylineApiResponse` (+`StorylineDto`, `DateRangeDto?` → make non-null) | Story | "Storyline Generation" |
| `CharacterGenerator.cs:319` (batched, 25/batch) | `CharacterApiResponse` (+`CharacterDto`) | Story | "Character Generation" |
| `ThemeGenerator.cs:94` | `ThemeApiResponse` (+`ThemeDto`) | Volume | "Theme Generation" |
| `EmailGenerator.cs:531` | `ThreadApiResponse` (+`EmailDto`: `CcEmails?` → non-null) | Volume | "Email Thread Generation (batch n/m)" |
| `EmailGenerator.cs:1318` | `VoicemailScriptResponse` | Volume | "Voicemail Script" |
| `EmailGenerator.cs:1484` | `WordDocResponse` | Volume | "Word Attachment" |
| `EmailGenerator.cs:1543` | `ExcelDocResponseRaw` → replace with `ExcelDocResponse` (strings) | Volume | "Excel Attachment" |
| `EmailGenerator.cs:1609` | `PowerPointDocResponse` (+`SlideDto`) | Volume | "PowerPoint Attachment" |
| `EmailGenerator.cs:1781` | `MeetingDetectionResponse` | Volume | "Meeting Detection" |
| `EmailGenerator.cs:1664, 1884` | dead code (`ImageSuggestionResponse`, old voicemail) | — | delete |
| `EmailGenerator.cs:1209` | image bytes | media | `OpenAIMediaClient.GenerateImageAsync` |
| `EmailGenerator.cs:1324` | TTS bytes | media | `OpenAIMediaClient.GenerateSpeechAsync` |
| `StepApiConfiguration.cs:230-231` | TestConnection | — | per-provider key test (Models.Retrieve / GetModelAsync) |
| `StepStorylines.cs:268`, `StepCharacters.cs:299` (+323 theme), `StepGeneration.cs:179` | `_state.CreateOpenAIService()` | — | `_state.CreateGateway()` (+ `CreateMediaClient()` for EmailGenerator) |
| `WizardState.cs:48-56` | `CreateOpenAIService` | — | replace with `CreateGateway()` / `CreateMediaClient()` |
| `StepComplete.cs:158, 192`, `TelemetryService.SendGenerationEventAsync(model)` | `_state.SelectedModel` | — | show and send "{provider}: {story} / {volume}" |

Generators change their constructor from `(OpenAIService openAI)` to `(ILlmGateway llm)`. `EmailGenerator` takes `(ILlmGateway llm, OpenAIMediaClient? media)`. This also makes them unit-testable with a `FakeLlmProvider` behind a real `LlmGateway`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON Schema from DTOs | Hand-written schema strings or prompt JSON templates | `AIJsonUtilities.CreateJsonSchema` + transform options | Handles NRT nullability, required lists, nested types and JsonPropertyName. Verified output. |
| HTTP retry/backoff for 429/5xx | Custom loops (the four copies in `OpenAIService`) | SDK built-ins: Anthropic `MaxRetries` (default 2, honors retry-after), OpenAI `ClientRetryPolicy` (default 3) | Avoids double-retry storms. |
| Key encryption | Base64, custom AES, key files next to the exe | `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` | OS-managed per-user key. In-box. |
| Wire-format tests | Mocking SDK internals or interfaces | Real SDK clients over a fake `HttpMessageHandler` (Anthropic `ClientOptions.HttpClient`; OpenAI `OpenAIClientOptions.Transport = new HttpClientPipelineTransport(new HttpClient(handler))`) | Tests the true serialized request and the real response parsing and exception mapping. Verified working for both SDKs. |
| Refusal detection | String-matching "I can't help" | `stop_reason == "refusal"` (+`StopDetails.Category`) / `ChatCompletion.Refusal` | Official, structured signals. |
| Thread-safe random | Locks around `new Random()` | `Random.Shared` | Thread-safe by design. |

**Key insight:** every piece of this phase that looks custom (retries, schema, crypto, wire tests) has a first-party primitive that was verified in this session. The custom code should be limited to the outcome taxonomy, the tier resolution and the translators.

## Common Pitfalls

### Pitfall 1: Test folder compiled into the app
**What goes wrong:** `ReelDiscovery.csproj` sits at the repo root, and SDK globbing compiles `ReelDiscovery.Tests/**/*.cs`, including the test project's `obj/*.AssemblyInfo.cs`. The build fails with a wall of `CS0579 Duplicate ... attribute` and `FactAttribute not found` errors. (Reproduced in this session.)
**How to avoid:** Add `<Compile Remove="ReelDiscovery.Tests/**" />` plus `EmbeddedResource` and `None` removes to the app csproj **before** first building the test project. Use forward slashes. A backslash lost to shell escaping silently produced `ReelDiscovery.Tests**` here and the remove did nothing.
**Warning signs:** duplicate `TargetFrameworkAttribute` errors pointing into `obj/`.

### Pitfall 2: `Message` is ambiguous
**What goes wrong:** WinForms implicit usings import `System.Windows.Forms`, so `Message` in any file with `using Anthropic.Models.Messages;` fails with CS0104. (Reproduced.)
**How to avoid:** `using AnthropicMessage = Anthropic.Models.Messages.Message;` in the Anthropic provider.

### Pitfall 3: Opus 5.5 thinking consumes `max_tokens`, and the first content block is not text
**What goes wrong:** Code that reads `Content[0]` gets a `thinking` block (with empty text at the default display). A small `max_tokens` ends as `max_tokens` with no text at all (reproduced with a canned response: `stop=MaxTokens text=''`).
**How to avoid:** Select blocks with `TryPickText`. Budget `MaxOutputTokens` generously (≥16K for Story calls). Treat `max_tokens` as Truncated and retry once at 2x.

### Pitfall 4: Sampling and thinking parameters rejected
**What goes wrong:** `temperature`/`top_p`/`top_k` non-default → 400 on Claude 4.7+ (including Opus 5.5 and Sonnet 5.5). `thinking: disabled` → 400 on Opus 5.5. `effort` on Haiku 4.5 → unsupported. `reasoning_effort` on OpenAI models that lack it → error [ASSUMED].
**How to avoid:** The translators never send sampling parameters. Effort is sent only from the catalog's `Effort` field. Test this with a wire-JSON assertion (`Assert.DoesNotContain("temperature", body)`).

### Pitfall 5: Spend-limit errors look like rate limits
**What goes wrong:** Anthropic's tier spend cap is `429 rate_limit_error` without `retry-after`, and the body has `error.details.error_code: "enforced_spend_limit_reached"`. A user-set spend limit is **400** `invalid_request_error` starting "You have reached your specified API usage limits". 402 is `billing_error`. OpenAI's quota exhaustion is `429` with `code: insufficient_quota`. [CITED: platform.claude.com/docs/en/api/rate-limits, /api/errors; VERIFIED classification in probe]
**How to avoid:** Classify from the body, as in Pattern 5. Treat as fatal: stop the run and save what exists.

### Pitfall 6: Choosing gpt-image-1-mini as the dall-e-3 replacement
**What goes wrong:** gpt-image-1 and gpt-image-1-mini shut down **2026-12-01** [CITED: developers.openai.com/api/docs/deprecations]. The image feature would break a second time.
**How to avoid:** Default to `gpt-image-2.5-flare`. Put `ImageModel` and `ImageQuality` on `GenerationConfig` (defaults `"gpt-image-2.5-flare"`, `"low"`) so they can be changed without code.

### Pitfall 7: Private nested DTOs are unreachable from tests
**What goes wrong:** Schema-strictness tests need `typeof(ThreadApiResponse)`, which is `private` inside `EmailGenerator`.
**How to avoid:** Change nested DTOs to `internal` (still nested). Add `<ItemGroup><InternalsVisibleTo Include="ReelDiscovery.Tests" /></ItemGroup>` to the app csproj. Alternatively expose an `internal static IReadOnlyList<Type> SchemaTypes` on each generator so the test enumerates them all, which also guards against forgetting a new DTO.

### Pitfall 8: Publishing the solution instead of the app
**What goes wrong:** Once a `.slnx` exists, `dotnet publish` at the root may try to publish the test project too, and single-file settings don't fit a test project [ASSUMED].
**How to avoid:** Always use `dotnet publish ReelDiscovery.csproj -c Release`. Document it in the README/plan.

### Pitfall 9: Reporting the same mutable progress object
**What goes wrong:** `Progress<T>` posts to the UI thread asynchronously. The worker keeps mutating `progressData`, so the UI shows torn or late values.
**How to avoid:** Report `Clone()` snapshots (C5).

### Pitfall 10: Silent key-test failures
**What goes wrong:** The current `TestConnectionAsync` returns `false` from a bare `catch`, which hides whether the key was bad, the model was wrong or the network was down.
**How to avoid:** The key test returns a classified result (`Auth`, `BadRequest`/not found = "model not available to this key", `Network`) and the status label shows it.

## Code Examples

### global.json
```json
{
  "sdk": { "version": "10.0.401", "rollForward": "latestFeature" }
}
```
`rollForward: latestFeature` accepts 10.0.4xx and later feature bands, and never 9.x or 11.x. [VERIFIED: builds with 10.0.401]

### App csproj additions
```xml
<TargetFramework>net10.0-windows</TargetFramework>
...
<ItemGroup>
  <PackageReference Include="Anthropic" Version="12.51.0" />
  <PackageReference Include="DocumentFormat.OpenXml" Version="3.5.1" />
  <PackageReference Include="MimeKit" Version="4.18.1" />
  <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.10.1" />
  <PackageReference Include="OpenAI" Version="2.14.0" />
</ItemGroup>
<ItemGroup>
  <Compile Remove="ReelDiscovery.Tests/**" />
  <EmbeddedResource Remove="ReelDiscovery.Tests/**" />
  <None Remove="ReelDiscovery.Tests/**" />
</ItemGroup>
<ItemGroup>
  <InternalsVisibleTo Include="ReelDiscovery.Tests" />
</ItemGroup>
```

### Test csproj (verified passing)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup><ProjectReference Include="..\ReelDiscovery.csproj" /></ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
</Project>
```
Keeping `RuntimeIdentifier`/`SelfContained`/`PublishSingleFile` unconditional in the app csproj did **not** break the test reference (verified). There is no need to move them into a Release-only condition.

### Fake HTTP for both SDKs (verified)
```csharp
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public int Status { get; set; } = 200;
    public string Body { get; set; } = "{}";
    public string? LastRequestBody { get; private set; }
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage((System.Net.HttpStatusCode)Status)
            { Content = new StringContent(Body, System.Text.Encoding.UTF8, "application/json") };
    }
}
// Anthropic
var anthropic = new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = "test", HttpClient = new HttpClient(handler), MaxRetries = 0 });
// OpenAI
var openAIOptions = new OpenAIClientOptions
{
    Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(new HttpClient(handler)),
    RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(0),
};
var chat = new ChatClient("gpt-6-luna", new System.ClientModel.ApiKeyCredential("test"), openAIOptions);
```
Providers must therefore accept injectable client options (constructor overload taking `HttpClient`/transport, or an internal factory) so tests can pass the fake.

### Image generation (gpt-image)
```csharp
// Verified wire: {"prompt":"...","model":"gpt-image-2.5-flare","quality":"low","output_format":"png","size":"1024x1024"}
var imageClient = _client.GetImageClient(_imageModel);   // "gpt-image-2.5-flare"
var options = new ImageGenerationOptions
{
    Quality = GeneratedImageQuality.LowQuality,          // or new GeneratedImageQuality(config.ImageQuality)
    Size = GeneratedImageSize.W1024xH1024,
    OutputFileFormat = GeneratedImageFileFormat.Png,     // attachments are named *.png
    // DO NOT set ResponseFormat or Style (DALL-E-only)
};
var result = await imageClient.GenerateImageAsync(prompt, options, ct);
return result.Value?.ImageBytes?.ToArray();
```
Keep the existing `catch (ClientResultException ex) when (ex.Status == 400) → return null` (content moderation). Drop the manual 429/503 loops, because the SDK retries.

### Free key test
```csharp
// Anthropic: validates key + model access, no tokens billed
await anthropic.Models.Retrieve(modelId, new Anthropic.Models.Models.ModelRetrieveParams(), ct);
// OpenAI
await new OpenAI.Models.OpenAIModelClient(new ApiKeyCredential(key), options).GetModelAsync(modelId, ct);
```
Both methods were verified to exist by reflection. The exact `ModelRetrieveParams` namespace/overload should be confirmed at implementation time (MEDIUM). If it's awkward, use `Retrieve(ModelRetrieveParams { ModelID = ... })`.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `dall-e-3` with `response_format` URL/bytes | gpt-image family, b64 only. gpt-image-1/1-mini also retire 2026-12-01. | DALL-E shut down 2026-05-12 | Use `gpt-image-2.5-flare` |
| JSON mode + prompt shapes | Strict JSON-schema structured outputs on both providers | — | Removes the JsonException retry loops |
| Extended thinking with `budget_tokens`, `thinking: disabled` | Adaptive thinking + `output_config.effort`. Opus 5.5 always thinks. | Claude 4.7+ / Opus 5.5 (2026-09-22) | Don't send thinking. Budget `max_tokens`. |
| `temperature` tuning | Not accepted (non-default → 400) on Claude 4.7+ | Claude 4.7 | Never send it |
| gpt-4o / gpt-4o-mini defaults | gpt-6 family (sol/luna) with `reasoning_effort` | 2026 | New catalog |
| .NET 8 | .NET 10 LTS | EOL 2026-11-10 | Retarget (verified trivial) |
| `dotnet new sln` → .sln | → `.slnx` | .NET 10 SDK | Use `ReelDiscovery.slnx` |

**Deprecated/outdated in this codebase:** `o1-preview`/`o1-mini`/`gpt-3.5-turbo`/`gpt-4-turbo` catalog entries (remove them). `GeneratedImageQuality.Standard` + `ResponseFormat = Bytes` (DALL-E). `OpenAIService` text methods and the four copy-pasted retry loops.

## Runtime State Inventory

(This phase includes a migration of stored key state.)

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings` (Base64 OpenAI key, written by `StepApiConfiguration.SaveApiKey`) | **Data migration:** `ApiKeyStore.MigrateLegacyKey()` on first launch, then overwrite and delete the legacy file (D-09). **Code edit:** remove `SaveApiKey`/`LoadApiKey`. |
| Stored data | `%LOCALAPPDATA%\ReelDiscovery\settings.json` (telemetry opt-in) | None. It's untouched, and the new `keys.dat` must use a different file name. |
| Live service config | None. There is no server-side config. Verified: no config files other than the two above. | None |
| OS-registered state | None. No installer, scheduled tasks or registry use found (grep for `Registry`/`TaskScheduler` returned nothing relevant). | None |
| Secrets/env vars | No env vars are read by the app. The API keys move from Base64 to DPAPI. The Slack webhook is hard-coded in `TelemetryService.cs:14` (out of scope, flagged in CONCERNS). | None for Phase 1 (the webhook is noted under Security) |
| Build artifacts | `bin/`, `obj/` from net8.0 builds | Delete `bin/` and `obj/` after the retarget to avoid stale `project.assets.json` confusion (harmless, but it avoids noise). |

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK 10.0.4xx | PLAT-01 build/test | ✓ | 10.0.401 (also 9.0.103) | — |
| Microsoft.WindowsDesktop.App runtime | tests / run | ✓ | 10.0.12 | — |
| NuGet.org access | package restore | ✓ | — | — |
| DPAPI (Windows) | PLAT-02 | ✓ | OS | — |
| OpenAI / Anthropic API keys | live use only | not needed for tests | — | Unit tests use fake HTTP (D-03) |
| git | commits | ✓ | — | — |

**Missing dependencies with no fallback:** none.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + xunit.runner.visualstudio 3.1.4 + Microsoft.NET.Test.Sdk 17.14.1 (VSTest) |
| Config file | none. Created in Wave 0: `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`, `ReelDiscovery.slnx`, `global.json` |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~<Area>"` |
| Full suite command | `dotnet test` (repo root; verified to resolve the slnx and run tests) |
| Build/publish gate | `dotnet build ReelDiscovery.csproj -warnaserror` is optional (the repo has one pre-existing CS0067 warning in `StepComplete.cs:20`, so fix that first or don't use -warnaserror). Publish: `dotnet publish ReelDiscovery.csproj -c Release`, then check that `bin/Release/net10.0-windows/win-x64/publish/ReelDiscovery.exe` exists. |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PLAT-01 | Targets net10.0-windows, global.json pins 10.0.4xx, publish yields a single exe | build/smoke (script) + unit | `dotnet publish ReelDiscovery.csproj -c Release` + `test -f .../publish/ReelDiscovery.exe`. Optional unit test: `PlatformTests.AppAssembly_TargetsNet10` (reads the `TargetFrameworkAttribute` of `typeof(WizardState).Assembly`). | ❌ Wave 0 |
| PLAT-02 | Round trip, clear, legacy migration + deletion, corrupt entry tolerated, no plaintext on disk | unit | `dotnet test --filter FullyQualifiedName~ApiKeyStoreTests` | ❌ Wave 0 |
| PLAT-03 | Test project runs | meta | `dotnet test` exit 0 | ❌ Wave 0 |
| PLAT-04 | Error sink thread-safe (Parallel.For 10k AddError → count 10k); progress snapshot independence; chain version unique under parallel reservation; no `new Random(` in Services/Helpers (optional source-scan test) | unit | `--filter FullyQualifiedName~ConcurrencyTests` | ❌ Wave 0 |
| D-02 | `ResolveReferencedEmail` batch-relative (offset 15, idx 2 → message 17); isolated enrichment (one throwing item → error logged, others complete, save runs); calendar clamp (hour 25 → valid DateTime) | unit | `--filter FullyQualifiedName~EmailGeneratorFixTests` | ❌ Wave 0 |
| PROV-01 | Catalog defaults per provider/tier (claude-opus-5-5 / claude-haiku-4-5-20251001 / gpt-6-sol / gpt-6-luna); gateway resolves Story/Volume to the selected provider only; one provider per run | unit | `--filter FullyQualifiedName~ModelCatalogTests\|FullyQualifiedName~LlmGatewayTests` | ❌ Wave 0 |
| PROV-02 | Key test classifies 401 → Auth, 404 → model unavailable, IO → Network (fake HTTP), for both providers | unit | `--filter FullyQualifiedName~KeyTestTests` | ❌ Wave 0 |
| PROV-03 | Anthropic wire JSON has `output_config.format.type=json_schema` + exact registry schema, list-form system, no temperature/thinking, effort only when set. OpenAI wire has `response_format.json_schema.strict=true`, `reasoning_effort` only when set. Every registered DTO schema passes StrictSchemaValidator (additionalProperties false everywhere, all required, no forbidden keywords, no `{}`, unions ≤16). Round trip: canned success JSON → typed DTO for both providers. | unit | `--filter FullyQualifiedName~Ai.` | ❌ Wave 0 |
| PROV-04 | Classification matrix: end_turn → Succeeded; refusal → Refused (0 retries, Calls==1); max_tokens → Truncated (1 retry with 2x tokens); bad JSON → Invalid (≤3 attempts); 429+enforced_spend_limit_reached / 400 "usage limits" / 402 / insufficient_quota → SpendLimit (fatal, 0 app retries); 401 → Auth; 529/500 → Transient; OpenAI refusal field → Refused; FinishReason Length → Truncated | unit | `--filter FullyQualifiedName~OutcomeClassificationTests` | ❌ Wave 0 |
| PROV-05 | Image request wire: model `gpt-image-2.5-flare`, `quality: low`, no `response_format`; b64 → bytes; 400 → null | unit (fake transport) | `--filter FullyQualifiedName~OpenAIMediaClientTests` | ❌ Wave 0 |
| PROV-06 | `WizardState.IsMediaAvailable` matrix (Claude + no key → false; Claude + tested key → true; OpenAI selected + tested → true); EmailGenerator with null media client skips image/voicemail passes and planning | unit | `--filter FullyQualifiedName~MediaGatingTests` | ❌ Wave 0 |
| Regression | OfficeDocumentService docx/xlsx/pptx open with OpenXml and `OpenXmlValidator` reports 0 errors (or a pinned baseline) on 3.5.1; EmlFileService output parses with MimeKit (Message-ID, In-Reply-To, attachments) | unit | `--filter FullyQualifiedName~DocumentRegressionTests` | ❌ Wave 0 |
| UI (manual-only) | Keys screen layout, notices, Next gating (D-04..D-08) | manual | WinForms UI is not unit-tested. The gating logic is extracted into `WizardState`/a pure helper (`CanProceedFromKeys(provider, openAITested, anthropicTested)`) which **is** unit-tested. | — |

### Sampling Rate
- **Per task commit:** `dotnet test --filter FullyQualifiedName~<area touched>` (< 10 s)
- **Per wave merge:** `dotnet test` (full suite; under 30 s expected, since all tests are in-process with fake HTTP)
- **Phase gate:** full suite green, `dotnet publish ReelDiscovery.csproj -c Release` succeeds and the exe exists, before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `global.json`, `ReelDiscovery.slnx`, app csproj retarget + `Compile Remove` + `InternalsVisibleTo`
- [ ] `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (template above)
- [ ] `ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs`, `Fakes/FakeLlmProvider.cs`, `Fakes/StrictSchemaValidator.cs`
- [ ] Optional opt-in live smoke test: `[Fact(Skip = ...)]` or a runtime skip when env `REELDISCOVERY_LIVE_ANTHROPIC_KEY` is absent. It must not be required (D-03).

## Security Domain

### Applicable ASVS Categories (Level 1)

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no (no app users). Third-party API keys are credentials: see V6/V8. | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | yes: LLM output is untrusted input | Strict JSON schema + C# post-validation (clamp hour/minute, trim counts, existing theme hex/font allowlist in ThemeGenerator). Deserialize into typed DTOs only. |
| V6 Stored Cryptography | yes | DPAPI `ProtectedData` CurrentUser + app entropy. Never hand-roll crypto. |
| V7 Error Handling & Logging | yes | Error messages shown or logged must never include the API key. SDK exception messages don't contain keys, but don't log request headers. `ANTHROPIC_LOG=debug` must not be enabled in shipped builds. |
| V8 Data Protection | yes | Keys at rest are encrypted. The legacy Base64 file is overwritten, then deleted. Key TextBoxes keep `UseSystemPasswordChar = true` (the existing OpenAI box has it; replicate it for the Anthropic box). |
| V9 Communications | yes | HTTPS via the SDKs. No custom endpoints. |
| V14 Configuration | partial | No secrets in source for the new code. The existing hard-coded Slack webhook (`TelemetryService.cs:14`) is a known HIGH issue outside Phase 1 scope. Flag it to the user, but don't expand the phase. |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| API key disclosure from disk (other local process/user, backups) | Information disclosure | DPAPI CurrentUser. Delete the legacy Base64 file. |
| Key leaked via telemetry or logs | Information disclosure | Telemetry sends provider and model names only. Never interpolate keys into messages. Test: `ApiKeyStoreTests` asserts `keys.dat` bytes do not contain the plaintext key. |
| Runaway spend from retry loops on quota/spend errors | Denial of wallet | Fatal classification + run stop (PROV-04). No app retries on SpendLimit/Auth/Refused. |
| Malformed or hostile model output crashing generation (e.g., hour=25) | Tampering / DoS | Schema + clamps + per-item isolation (D-02). |
| DPAPI blob copied to another machine or user | Spoofing | Unprotect fails → key treated as absent (graceful, re-enter). |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | OpenAI Story default `gpt-6-sol` with `reasoning_effort: none` is the right "flagship, non-reasoning" pick (vs `gpt-6.1-sol` at `low`) | Model Catalog | Quality/latency tradeoff. The user may prefer the newest model. It's editable in the catalog, so the risk is low. **Confirm with the user.** |
| A2 | Sending `reasoning_effort` to an OpenAI model that doesn't support it returns an error, so the effort must be per-catalog-entry and nullable | Model Catalog / Pitfall 4 | If it's silently ignored instead, nothing breaks. The design is safe either way. |
| A3 | OpenAI strict mode may not accept `type: ["object","null"]` or `["array","null"]` | Schema Generation | Avoided by the DTO rule "no nullable lists/objects". There's no risk if the rule is followed. |
| A4 | gpt-image-2.5-flare low 1024² costs ≈ $0.006 per image | Model Catalog | Only affects the UI hint text. Pricing is token-based and read from third-party articles. |
| A5 | Anthropic "credit balance too low" is reported as a 400 whose message contains "credit balance" | Pattern 5 | A missed match falls to BadRequest (still non-retried and visible). Low risk. |
| A6 | Custom `ClientRetryPolicy.ShouldRetry` override can inspect the response body to skip `insufficient_quota` | Pattern 5 | Without it, OpenAI retries a quota 429 3x (unbilled, a few seconds). Low risk, and the override is optional. |
| A7 | Publishing the `.slnx` (rather than the csproj) causes problems | Pitfall 8 | Documentation-only. Always publish the csproj. |
| A8 | Haiku 4.5 will not be retired before this milestone ships (no deprecation notice yet; ≥60 days' notice policy) | Model Catalog | If a deprecation is announced, switch the Volume default to `claude-sonnet-5-5` (a catalog change only). |

## Open Questions (RESOLVED)

1. **OpenAI Story default: `gpt-6-sol` (none) vs `gpt-6.1-sol` (low)?**
   - What we know: both are $2/$10, both support Chat Completions, strict outputs and Batch. Only 6-sol can run with `reasoning_effort: none`.
   - What's unclear: the quality delta on story generation, and the user's latency tolerance.
   - RESOLVED: ship `gpt-6-sol` + `none` as the default (matches D-13's "prefer non-reasoning") and include `gpt-6.1-sol` (effort `low`) in the catalog. Surface it to the user at plan review.
2. **Opus 5.5 effort for the legacy Story calls?**
   - What we know: the default is `medium`, adaptive thinking is always on, and thinking tokens are billed as output.
   - RESOLVED: omit it (API default). Keep `Effort` editable in the catalog. Set Story-call `MaxOutputTokens` to 32K so thinking doesn't truncate the JSON.
3. **Where does the legacy-key migration run?**
   - RESOLVED: `Program.cs` before `WizardForm`, so it runs exactly once per launch regardless of navigation. `StepApiConfiguration` then only reads the store.

## Sources

### Primary (HIGH confidence)
- Local verification (this session): NuGet flat-container indexes. A trial retarget build and publish of the repo copy. A reflection dump of Anthropic 12.51.0, OpenAI 2.14.0 and MEAI 10.10.1 types. A fake-HTTP probe of both SDKs (request JSON, refusal/length/stop parsing, exception types, retry counts). MEAI schema output for sample DTOs. An xUnit project nested in the repo referencing the WinExe (DPAPI round trip) passing under `dotnet test`.
- https://platform.claude.com/docs/en/about-claude/models/overview: model IDs, prices, max output, thinking, effort support
- https://platform.claude.com/docs/en/models/opus-5-5/overview and /whats-new-opus-5-5: always-on thinking, 400s, refusal `stop_details`, pricing
- https://platform.claude.com/docs/en/about-claude/model-deprecations: Haiku 4.5 status, temperature/top_p/top_k deprecation
- https://platform.claude.com/docs/en/build-with-claude/structured-outputs: supported models, JSON Schema limits (16 unions, 24 optional, minItems 0/1), refusal/max_tokens behaviour, enum-casing caveat
- https://platform.claude.com/docs/en/api/errors and /api/rate-limits: error types, 402 billing, spend-cap 429 `enforced_spend_limit_reached`, user spend-limit 400, SDK default 2 retries
- https://developers.openai.com/api/docs/models/gpt-6-sol, /gpt-6.1-sol, /gpt-6-luna: reasoning effort levels, endpoints, prices
- https://developers.openai.com/api/docs/pricing: text/TTS/image price list
- https://developers.openai.com/api/docs/deprecations: DALL-E shutdown 2026-05-12, gpt-image-1/1-mini shutdown 2026-12-01, no tts-1 deprecation
- https://developers.openai.com/api/docs/guides/image-generation: parameters, b64-only
- https://developers.openai.com/api/docs/guides/structured-outputs: strict mode requirements, refusal field, `length` finish
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0: WinForms/SDK breaking changes (none affecting this app; .slnx default; NU1510)

### Secondary (MEDIUM confidence)
- https://developers.openai.com/api/docs/models/gpt-image-2.5-flare: model positioning, token pricing

### Tertiary (LOW confidence)
- Web search (cometapi.com, virse.ai, segmind blog): gpt-image-2.5-flare ≈ $0.006 per low-quality 1024² image

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. Versions are from the NuGet index. The retarget, upgrade and publish were executed.
- SDK surfaces / classification: HIGH. Compiled and run against the real packages through fake HTTP.
- Model defaults: HIGH for IDs and prices (official pages). MEDIUM for the OpenAI Story choice (a judgement call).
- Architecture: MEDIUM-HIGH. It follows the locked direction. The shapes are recommendations.
- Pitfalls: HIGH. Pitfalls 1-3 and 5 were reproduced or verified in this session.

**Research date:** 2026-09-29
**Valid until:** 2026-10-15 for model IDs and prices (a fast-moving lineup; Haiku 4.5's earliest retirement date). 2026-10-29 for SDK and build mechanics.
