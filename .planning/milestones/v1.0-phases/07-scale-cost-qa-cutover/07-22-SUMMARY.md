---
phase: 07-scale-cost-qa-cutover
plan: 22
subsystem: live-verification
tags: [live-spike, prompt-caching, batch, uat, dpi, RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05]
requires:
  - phase: 07-11
    provides: "Ready-card batch checkbox and live/batch estimate"
  - phase: 07-18
    provides: "QA passes (scans, leakage audit, blind review, Regenerate Thread)"
  - phase: 07-20
    provides: "Single case-driven flow (old wizard removed)"
  - phase: 07-21
    provides: "5K live and batch scale tests"
provides:
  - "Opt-in live provider spike tests (Category=LiveSpike, REELDISCOVERY_LIVE_SPIKE=1)"
  - "Measured OpenAI gpt-6-luna Chat Completions caching behavior (implicit vs explicit) and batch round-trip latency"
  - "API Configuration step header and disclaimer rows scaled for 150% DPI"
  - "Phase 7 UAT sign-off (after gap closure 07-23/07-24)"
affects: [phase-07-verification]
tech-stack:
  added: []
  patterns:
    - "Live tests gated by an env var and an early return, so default test filters never make network calls"
key-files:
  created:
    - ReelDiscovery.Tests/LiveSpike/LiveProviderSpikeTests.cs
    - .planning/phases/07-scale-cost-qa-cutover/07-22-SPIKE-NOTES.md
  modified:
    - Services/Ai/OpenAITextProvider.cs
    - UserControls/StepApiConfiguration.cs
key-decisions:
  - "Task 2: user chose run (spike executed 2026-10-07)"
  - "OpenAI implicit prompt caching kept: explicit mode is accepted but disables cache reads too, so it costs more from the second call; branch A deliberately not applied (user did not object)"
  - "Anthropic spike not run (no Anthropic key in the key store): StablePrefixTokens stays [ASSUMED] 600 and A3 is unverified"
  - "UAT test 4 first run exposed per-wave batch latency; closed by 07-23 (relevant waves live in batch mode) and 07-24 (batch estimate and copy); user approved the UAT on 2026-10-08"
requirements-completed: [RUN-03, RUN-04, RUN-05]
metrics:
  duration: ~2 days elapsed (spike 28 min wall clock, UAT across two sessions)
  completed: 2026-10-08
---

# Phase 7 Plan 22: Live Spike, Real Batch Round Trip and 150% UAT Summary

The live spike showed that OpenAI bills implicit cache writes and that explicit mode turns off caching completely, so implicit caching stays. A real OpenAI batch took about 28 minutes. That latency surfaced the per-wave batch blocker in UAT, which 07-23/07-24 closed. The user then approved the UAT at 150% scaling.

## Tasks

| # | Task | Result | Commit |
|---|------|--------|--------|
| 1 | Opt-in live spike tests | `LiveProviderSpikeTests` (5 tests, `[Trait("Category","LiveSpike")]`). They return early unless `REELDISCOVERY_LIVE_SPIKE=1`, load keys from the DPAPI store and write to `ITestOutputHelper` only. Keys are never logged. | 1f621e6 |
| 2 | Decision: run the spike | **run** (user, in chat) | - |
| 3 | Run the spike and apply its result | Spike ran 2026-10-07 (27.9 min, 3 passed, 2 failed because there is no Anthropic key). Implicit caching kept. The result is recorded in a comment in `OpenAITextProvider.BuildOptions`. | aa8dc7a |
| - | Deviation: 150% DPI fix on API step | Rows 0, 4 and 12 of `StepApiConfiguration` now use `LogicalToDeviceUnits` | 12e866b |
| - | Spike notes, pre-UAT suite results | `07-22-SPIKE-NOTES.md` | c0c1650 |
| 4 | UAT at 150% (checkpoint:human-verify) | First run: blocker (see below). After 07-23/07-24: **approved** by the user in chat on 2026-10-08 | - |

## Spike results (Task 3)

### (a) OpenAI gpt-6-luna, implicit caching, ~1,829-token prompt

| Call | HTTP | Latency | prompt_tokens | cached_tokens | cache_write_tokens | completion |
|------|------|---------|---------------|---------------|--------------------|------------|
| 1 (identical) | 200 | 4378 ms | 1829 | 0 | 1826 | 36 |
| 2 (identical) | 200 | 1573 ms | 1829 | 1826 | 0 | 38 |
| 3 (new suffix) | 200 | 2061 ms | 1829 | 1816 | 10 | 31 |
| 4 (new suffix) | 200 | 1387 ms | 1830 | 1816 | 11 | 42 |

OpenAI bills cache writes and reports them in `usage.prompt_tokens_details.cache_write_tokens`. `ReadCacheWriteTokens` parses them correctly, so cost accounting (PROV-07) is accurate.

### (b) `prompt_cache_options {"mode":"explicit"}` via `ChatCompletionOptions.Patch`

All 3 calls returned HTTP 200, with cached_tokens 0 and cache_write_tokens 0, even right after the implicit calls had warmed the same prefix. The API accepts explicit mode, but it turns off cache reads as well as writes.

### (c) Anthropic claude-sonnet-5-5 evidence prefix: not run

There was no Anthropic key in the DPAPI key store, so A3 is **unverified**. `GenerationCostEstimator.StablePrefixTokens` stays `600 [ASSUMED]`.

### (d) Batch round trips

| Provider | Submitted (UTC) | Round trip | Final | Results |
|----------|-----------------|------------|-------|---------|
| OpenAI | 2026-10-07T13:29:57Z | 00:27:38 | Ended, 2/2 ok | input=47, output=12 each, batch=True |
| Anthropic | not run (no key) | - | - | - |

### Branch applied

The literal trigger for branch A was met: writes > 0, explicit mode returned HTTP 200, and explicit mode removed the writes. Explicit mode also removed every cache read, though. At catalog prices (input 0.10, write 0.125, read 0.01 per M), implicit caching costs about 1.25x on the first call and about 0.1x after that, while explicit costs 1.0x on every call. **Applied: no request change** (effectively branch B/C), so implicit caching stays and the honest write accounting already in place is kept. The existing wire test still asserts that `prompt_cache_options` is absent. The user was told and did not object.

## UAT (Task 4)

- Pre-handover suites after 12e866b: non-render 3719/3719, Render 135/135, Scale 2/2. After 07-24: non-render 3737/3737 and the targeted render tests were green.
- **First run (2026-10-08):** batch mode on a ~445-thread case (59 relevant threads in 24 waves) submitted one provider batch per relevant wave. The user asked "Is each wave going to take up to 24 hours?" This was logged as a blocker in 07-UAT.md, along with a cosmetic issue: the API step's media notice pointed at the step the user was already on.
- **Gap closure:** 07-23 runs relevant waves live in batch mode and batches only non-relevant mail (amended D-07). 07-24 prices the batch total the same way, updates the Ready-card/UI-SPEC copy and fixes the media notice.
- **Re-run:** the user approved in chat on 2026-10-08. 07-UAT.md test 4 is marked pass.
- The `provider-batches.jsonl` duplicate-custom-id check came from the user's approval, not from a script run during this finalization. No live or paid calls were made while writing this summary.

## Requirements

- **RUN-03** (Batch API): delivered and exercised on OpenAI with a real batch. Relevant waves run live and non-relevant mail is batched (amended D-07). The Anthropic batch transport is covered by tests but was not exercised live.
- **RUN-04** (on-disk batch tracking and restart collection): delivered (07-09/07-10). Restart and Continue were covered in the approved UAT.
- **RUN-05** (prompt caching): delivered. OpenAI automatic prefix caching was measured live, and the blind-review protocol prefix is cached (07-16). **Anthropic evidence-path caching is implemented, but its prefix size was not measured**, so `StablePrefixTokens = 600` is still an assumption (A3). If the real prefix turns out to be below Anthropic's cache minimum, the D-19-safe case-directory prefix (research Pattern 5) is available as a follow-up via `/gsd-plan-phase --gaps`.

Every plan listing RUN-03/04/05 (07-02, 06, 07, 08, 09, 10, 11, 16, 21, 22, 23, 24) now has a SUMMARY.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1/3 - DPI bug] API step rows clipped at 150%**
- **Found during:** Task 4 pre-handover Render suite (2/135 failed once the machine ran at 150%)
- **Issue:** Section header rows (28 raw px; the text needs 30) were clipped, and the NOTICE disclaimer row (44 raw px) showed only its first line
- **Fix:** `LogicalToDeviceUnits` on rows 0, 4 and 12 of `StepApiConfiguration`
- **Files modified:** UserControls/StepApiConfiguration.cs
- **Commit:** 12e866b

**2. OpenAI branch A deliberately not applied.** See "Branch applied". Following the literal rule would have raised cost on every call after the first.

### Gaps raised and closed

- The per-wave batch latency blocker and the cosmetic media-notice wording were closed by 07-23 and 07-24 (see 07-UAT.md).

## Known Open Items

- A3 / Anthropic evidence-prefix size is unmeasured, and the Anthropic batch round trip was not run live (no key). Rerun with `REELDISCOVERY_LIVE_SPIKE=1 dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=LiveSpike"` once an Anthropic key is stored.

## Self-Check: PASSED
