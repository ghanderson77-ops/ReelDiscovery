# 07-22 Live Spike Results (draft input for 07-22-SUMMARY.md)

Task 2 decision: **run** (user, in chat).
Run: 2026-10-07, `REELDISCOVERY_LIVE_SPIKE=1 dotnet test ... --filter "Category=LiveSpike"`. Total 27.9 min; 3 passed, 2 failed (no Anthropic key in the DPAPI key store; not transient, not retried).

## (a) OpenAI gpt-6-luna, implicit caching (Chat Completions), ~1,829-token prompt

| Call | HTTP | Latency | prompt_tokens | cached_tokens | cache_write_tokens | completion |
|------|------|---------|---------------|---------------|--------------------|------------|
| 1 (identical) | 200 | 4378 ms | 1829 | 0 | 1826 | 36 |
| 2 (identical) | 200 | 1573 ms | 1829 | 1826 | 0 | 38 |
| 3 (new suffix) | 200 | 2061 ms | 1829 | 1816 | 10 | 31 |
| 4 (new suffix) | 200 | 1387 ms | 1830 | 1816 | 11 | 42 |

Writes ARE billed and reported in `usage.prompt_tokens_details.cache_write_tokens` (parsed correctly by `ReadCacheWriteTokens`).

## (b) OpenAI gpt-6-luna, `prompt_cache_options {"mode":"explicit"}` via Patch

| Call | HTTP | Latency | prompt_tokens | cached_tokens | cache_write_tokens |
|------|------|---------|---------------|---------------|--------------------|
| 1 | 200 | 2086 ms | 1829 | 0 | 0 |
| 2 | 200 | 1637 ms | 1829 | 0 | 0 |
| 3 | 200 | 2903 ms | 1829 | 0 | 0 |

Explicit mode is accepted but disables caching entirely (no writes AND no reads, even right after the implicit calls warmed the same prefix).

## (c) Anthropic claude-sonnet-5-5 evidence prefix: NOT RUN (no Anthropic key in key store)

A3 stays unverified; `StablePrefixTokens = 600` left `[ASSUMED]`.

## (d) Batch round trips

| Provider | Submitted (UTC) | Round trip | Final | Results |
|----------|-----------------|------------|-------|---------|
| OpenAI | 2026-10-07T13:29:57Z | 00:27:38 | Ended, 2/2 ok | input=47, output=12 each, batch=True |
| Anthropic | not run (no key) | - | - | - |

## Branch applied

Literal branch A trigger was met (writes > 0, explicit HTTP 200, explicit removes writes), but explicit mode also removes all cache reads. At catalog prices (input 0.10, write 0.125, read 0.01 per M) implicit costs ~1.25x on the first call and ~0.1x thereafter; explicit costs 1.0x every call, so it is more expensive from the second call. Applied: no request change (behaves as branch B/C: implicit caching kept, honest write accounting already in place). Comment in `OpenAITextProvider.BuildOptions` records the spike; existing wire test asserts `prompt_cache_options` is absent.
