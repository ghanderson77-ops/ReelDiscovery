---
phase: 07-scale-cost-qa-cutover
plan: 02
subsystem: ai-providers
tags: [prompt-cache, usage, anthropic, openai, PROV-07, RUN-05]
requires:
  - "07-01: LlmProviderResponse CacheWriteTokens/CacheReadTokens"
provides:
  - "AnthropicTextProvider.BuildOutputConfig (shared with batch transport)"
  - "Anthropic system block cache_control ephemeral"
  - "Anthropic and OpenAI normalized cache usage on LlmProviderResponse"
  - "OpenAITextProvider.ReadCacheWriteTokens(BinaryData?) and Classify(ChatCompletion, BinaryData?)"
affects: [07-batch, 07-runtime-cost, 07-22-spike]
tech-stack:
  added: []
  patterns:
    - "Block-level cache breakpoint on the byte-stable system prompt only; never top-level cache_control"
    - "Provider usage normalized to uncached input + cache write + cache read + output"
key-files:
  created: []
  modified:
    - Services/Ai/AnthropicTextProvider.cs
    - Services/Ai/OpenAITextProvider.cs
    - ReelDiscovery.Tests/Ai/Anthropic/AnthropicWireTests.cs
    - ReelDiscovery.Tests/Ai/OpenAI/OpenAIWireTests.cs
decisions:
  - "07-02: Anthropic marks only the non-empty system text block with cache_control ephemeral; an empty system prompt keeps the previous wire body"
  - "07-02: OpenAI InputTokens = prompt - cached - cache_write (clamped >= 0); cache_write_tokens read from raw JSON, 0 when absent or malformed; no cache options sent until the 07-22 spike"
metrics:
  duration: 4min
  completed: 2026-10-06
  tasks: 2
  files: 4
---

# Phase 7 Plan 02: Provider Prompt Caching and Usage Normalization Summary

Anthropic requests now put a block-level `cache_control: ephemeral` marker on the byte-stable system block, and nothing at the top level. Both providers report uncached input, cache writes and cache reads separately. OpenAI reads `cache_write_tokens` defensively from the raw response JSON. The structured-output builder is shared so the batch transport (07-09) can reuse it.

## What Was Built

- `AnthropicTextProvider.BuildOutputConfig(r)`: the json_schema format and optional effort, pulled out of `BuildParams`, which now calls it.
- `BuildParams`: a non-empty system prompt gets `CacheControl = new CacheControlEphemeral()` on its `TextBlockParam`. `MessageCreateParams.CacheControl` is never set.
- Anthropic `Classify`: `InputTokens` = input_tokens (this count already excludes cached tokens), `CacheWriteTokens` = cache_creation_input_tokens, `CacheReadTokens` = cache_read_input_tokens. A missing field counts as 0. Every value is clamped to the range 0..int.MaxValue.
- OpenAI `SendAsync` keeps the `ClientResult` and passes `GetRawResponse().Content` to `Classify(ChatCompletion, BinaryData? rawBody = null)`. Callers that pass one argument still work.
- OpenAI `Classify`: `CacheReadTokens` = cached_tokens, `CacheWriteTokens` = raw cache_write_tokens, `InputTokens` = max(0, prompt - cached - write).
- `ReadCacheWriteTokens`: walks the raw JSON step by step and checks the type at each level. It catches `JsonException` and `ArgumentException` and returns a non-negative int32, or 0 (T-07-05).
- `BuildOptions` sends no cache options (OQ1). A comment there explains why.

## Tests

- Anthropic: the system block has `cache_control` of type ephemeral. The root has no `cache_control` and the user content stays a plain string. An empty system prompt sends no `cache_control`. Cache write (120/600/0/40) and cache read are normalized, and missing cache fields read as 0.
- OpenAI: 2000/1200/300 gives 500/1200/300. Without write tokens it gives 800/0. Without details it gives 2000/0/0. Nine malformed or missing raw bodies each give 0, a nested value is read correctly, and the request body has no `prompt_cache_options` or `prompt_cache_breakpoint`.
- Verification: `dotnet build ReelDiscovery.csproj -warnaserror` gives 0 warnings. Ai tests pass 152/152 and Anthropic + EvidencePromptsTests pass 49/49. EvidencePromptsTests were not changed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | ada21d9 | test(07-02): add failing Anthropic cache marker and cache usage tests |
| 1 GREEN | 38a80c6 | feat(07-02): Anthropic block-level cache marker, shared OutputConfig builder and cache usage |
| 2 RED | 916fb57 | test(07-02): add failing OpenAI inclusive cache usage and raw cache_write_tokens tests |
| 2 GREEN | fd1f2f1 | feat(07-02): OpenAI inclusive usage normalization with raw cache_write_tokens |

## Deviations from Plan

None. The plan was carried out as written. The only addition is a small `ClampTokens(long)` helper in the Anthropic provider, which does the clamp-negatives-to-0 step the plan asked for.

## TDD Gate Compliance

Each task has a RED `test(...)` commit followed by a GREEN `feat(...)` commit. No refactor was needed. Task 2's RED failed at compile time because `ReadCacheWriteTokens` did not exist yet.

## Known Stubs

None.

## Self-Check: PASSED
