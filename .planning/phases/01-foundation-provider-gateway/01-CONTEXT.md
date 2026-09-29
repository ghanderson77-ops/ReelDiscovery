# Phase 1: Foundation & Provider Gateway - Context

**Gathered:** 2026-09-29
**Status:** Ready for planning

<domain>
## Phase Boundary

The app runs on a supported runtime (.NET 10, pinned SDK, still a single-file self-contained exe), has a test project, and has its known concurrency bugs fixed. API keys are DPAPI-encrypted, and the existing Base64 key file is migrated. **All** text generation goes through one app-owned, provider-agnostic gateway (OpenAI + Anthropic Claude) with strict JSON-schema structured outputs and classified outcomes (succeeded / refused / truncated / invalid / failed). Images move from the retired dall-e-3 to a gpt-image model. With Claude as the text provider, images and voicemail need a configured OpenAI key and are otherwise disabled with a clear notice.

Requirements: PLAT-01, PLAT-02, PLAT-03, PLAT-04, PROV-01, PROV-02, PROV-03, PROV-04, PROV-05, PROV-06.

Not in this phase: batch APIs, prompt caching, cache/batch cost accounting (Phase 7, PROV-07); case-driven flow (Phases 2+); removal of the old wizard (Phase 7).

</domain>

<decisions>
## Implementation Decisions

### Legacy flow on the gateway
- **D-01:** Port all four legacy generators (`StorylineGenerator`, `CharacterGenerator`, `ThemeGenerator`, `EmailGenerator`) **fully** onto the new gateway with strict JSON-schema DTOs. The old topic → storylines → characters → emails flow must work end to end with **either** Claude or OpenAI. After the port, `OpenAIService` is no longer a text-generation path. It may survive only as the OpenAI media client (images/TTS) or be folded into the OpenAI provider.
- **D-02:** While porting, also fix the cheap known bugs in the old flow:
  - `replyToIndex` is resolved as absolute instead of batch-relative for storylines with more than 15 emails. Fix it to be batch-relative.
  - A single failed attachment, image, calendar invite or voicemail must no longer abort the whole run. Isolate per-item failures, log them to `GenerationResult.Errors`, skip the item and continue.
  - PLAT-04 concurrency fixes: use `Random.Shared` instead of the shared `System.Random`, and use thread-safe collections or remove mutation in parallel sections.
  - Larger dead-code removal stays in Phase 7 (PLAT-05). Only remove what the port naturally touches.
- **D-03:** Verification for this phase is **unit tests only**. No mandatory live wizard run or live-API test is part of the success gate. Tests cover the provider translators (request building for each provider), schema generation (strict: `additionalProperties:false`, all properties required, no unsupported constraint keywords), outcome classification, DPAPI key store and migration, and the concurrency fixes where testable.

### Keys & provider screen (`UserControls/StepApiConfiguration.cs`)
- **D-04:** Layout is **two key sections**, OpenAI and Anthropic. Each has its own key box, a Test button and a status indicator. Below them is a **"Text provider: OpenAI / Claude"** picker and the tier model dropdowns (see D-11). A media status line under the OpenAI key section shows whether images and voicemail are available.
- **D-05:** On a fresh install the default text provider is **OpenAI**.
- **D-06:** Clicking Next requires only the **selected text provider's** key to have passed its test. If Claude is selected and the OpenAI key is missing or untested, the user can still continue. Images and voicemail are disabled and a clear notice is shown (PROV-06).
- **D-07:** Only the keys are persisted, encrypted with DPAPI (`ProtectedData`, CurrentUser scope) under `%LOCALAPPDATA%\ReelDiscovery\`. The provider choice and model selections are **not** remembered and reset to defaults on every launch.
- **D-08:** Each key has a **Clear** button that removes it from the encrypted store.
- **D-09:** The existing Base64 `reeldiscovery.settings` key file is migrated automatically to the DPAPI store (as the OpenAI key) on first launch. The plaintext-equivalent file is then removed or overwritten.

### Model lineup & tiers
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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project scope & requirements
- `.planning/ROADMAP.md` §"Phase 1: Foundation & Provider Gateway" — goal and 5 success criteria
- `.planning/REQUIREMENTS.md` — PLAT-01..04, PROV-01..06 (and PROV-07 / PLAT-05, deferred to Phase 7)
- `.planning/PROJECT.md` — Key Decisions table (retarget .NET 10, gpt-image, DPAPI, tests; Claude alongside OpenAI)
- `.planning/STATE.md` — timing blocker: .NET 8 EOL 2026-11-10

### Stack & provider research
- `.planning/research/STACK.md` — package versions (Anthropic 12.51.0, OpenAI 2.14.0, MEAI.Abstractions 10.10.1), recommended `ILlmProvider` shape, structured-output constraints per provider, dall-e-3 shutdown, gpt-image IDs, DPAPI, global.json, xUnit
- `.planning/research/ARCHITECTURE.md` — target architecture for gateway/provider layering
- `.planning/research/PITFALLS.md` — provider/structured-output pitfalls
- `.planning/research/SUMMARY.md` — research synthesis

### Existing codebase maps
- `.planning/codebase/INTEGRATIONS.md` — current OpenAI surface, retry policy, key storage (Base64), telemetry
- `.planning/codebase/STACK.md` — current packages and publish settings
- `.planning/codebase/ARCHITECTURE.md` — layers, WizardState, generator pattern
- `.planning/codebase/CONVENTIONS.md` — naming, error handling, UI-in-code conventions
- `.planning/codebase/CONCERNS.md` — known bugs (shared Random, parallel mutation, replyToIndex, abort-on-failure, data loss)
- `.planning/codebase/TESTING.md` — no tests today
- `CLAUDE.md` — project conventions (must follow)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Services/OpenAIService.cs` (302 lines): existing text/JSON/image/TTS methods plus retry shape. The source for the OpenAI provider translator and the media client (images/TTS).
- `Models/TokenUsageTracker.cs`: thread-safe usage accumulator. Extend it so it records the provider and model for each call (cache/batch pricing is Phase 7).
- `Models/AIModelConfig.cs` (`GetDefaultModels()`): hard-coded catalog and pricing. Add Provider and tier-default metadata, and replace the stale model list.
- `UserControls/ModelConfigurationDialog.cs`: catalog/pricing editor. Add a Provider column.
- `Helpers/ButtonHelper.cs`, `Helpers/LoadingOverlay.cs`: UI building blocks for the reworked keys screen.

### Established Patterns
- Generators take their AI dependency through the constructor (`new CharacterGenerator(openAI)`), and services are created via `WizardState.CreateOpenAIService()`. Replace this with a gateway factory on `WizardState` so usage tracking stays attached.
- LLM responses are deserialized into private nested `*Dto` classes inside each generator. These become the schema source for strict structured output. Keep the DTOs free of `[Range]`/`[StringLength]`/`[RegularExpression]`.
- Error-handling conventions: catch `OperationCanceledException` before `Exception`; accumulate errors instead of aborting during bulk generation; use silent `catch` only for non-critical side effects, with a comment explaining why.
- The UI is built in code with `TableLayoutPanel`. Keep the row-index comments on the row styles.

### Integration Points
- `Models/WizardState.cs`: `ApiKey`, `SelectedModel` and `CreateOpenAIService()` become per-provider keys, the selected provider, the Story/Volume model selections, and a gateway factory.
- `UserControls/StepApiConfiguration.cs` (380 lines): rebuilt per D-04..D-09 (`SaveApiKey`/`LoadApiKey` → DPAPI store).
- `Services/EmailGenerator.cs` (2,036 lines): the biggest port surface. Its media passes (images, voicemail) must respect PROV-06 gating.
- `UserControls/StepGenerationConfig.cs`: the image and voicemail options must reflect media availability (disabled with a notice when there is no OpenAI key under Claude).
- `ReelDiscovery.csproj`: `net10.0-windows`, new `global.json`, package upgrades, and the new test project that references the app project.

</code_context>

<specifics>
## Specific Ideas

- The user explicitly wants the story-building work (core document, case guide, relevant evidence) to run on a **higher model tier** than bulk work. This is the reason for the Story/Volume tiers. An earlier PoC showed that a strong model for key evidence plus a cheap model for fill works well, and that reasoning models (for example GPT-5) were about 10x slower per call, which makes them impractical for volume.
- Memory notes describe an earlier Case Bible PoC (`Services/CaseBible/`, `CaseBibleForm`, `makebible` CLI). **That code is not present in this repo's `main`**, so treat Phase 1 as starting from the original codebase.

</specifics>

<deferred>
## Deferred Ideas

- Remembering the provider and model selection between sessions. The user chose to persist keys only. Revisit if resetting to defaults becomes annoying.
- Mixing providers across tiers (for example Story on Claude, Volume on OpenAI). The user rejected this for now in favor of one provider per run.
- A per-task model table or a third "Evidence" tier. Two tiers were chosen. Revisit in Phase 7 if cost tuning needs finer control.
- A mandatory live smoke run of the legacy wizard on each provider. The user chose unit tests only.

</deferred>

---

*Phase: 01-foundation-provider-gateway*
*Context gathered: 2026-09-29*
