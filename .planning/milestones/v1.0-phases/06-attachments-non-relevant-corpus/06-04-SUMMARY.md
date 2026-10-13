---
phase: 06-attachments-non-relevant-corpus
plan: 04
subsystem: media-seam
tags: [openai, images, tts, voicemail, cost-tracking, fakes]
requires: []
provides:
  - "IMediaGenerator { GenerateImageAsync(prompt, label, ct), GenerateSpeechAsync(text, voice, label, ct) }"
  - "MediaResult { Kind, Bytes, Reason, CostUsd } + enum MediaResultKind { Ok, Failed, Stopped }"
  - "OpenAIMediaGenerator(OpenAIMediaClient, MediaPrices, TokenUsageTracker?)"
  - "MediaPrices { ImagePerUnit 0.006, TtsPerMillionChars 15.00, ImageCost(), SpeechCost(chars), Normalize(), Clone(), Defaults() }"
  - "TokenUsageTracker.RecordMediaUsage(operation, cost)"
  - "WizardState.MediaPrices + WizardState.CreateMediaGenerator() (null without a tested OpenAI key)"
  - "ReelDiscovery.Tests.Fakes.FakeMediaGenerator (Requests, Respond, StopAfter, Failed/Stopped helpers)"
affects: [06 attachment unit of work, media realizers, ModelConfigurationDialog media prices (UI-SPEC S4)]
tech-stack:
  added: []
  patterns:
    - "Seam never throws except on user cancel: catch ladder ClientResultException 401/403 -> 429 quota -> 429 -> other status -> HttpRequestException -> Exception -> non-user OperationCanceledException"
    - "429 classified by error.code (insufficient_quota / billing_hard_limit_reached) or a quota/billing message; anything else is retryable throttling"
key-files:
  created:
    - Models/MediaPrices.cs
    - Services/Evidence/IMediaGenerator.cs
    - Services/Evidence/OpenAIMediaGenerator.cs
    - ReelDiscovery.Tests/Fakes/FakeMediaGenerator.cs
    - ReelDiscovery.Tests/Evidence/MediaGeneratorTests.cs
  modified:
    - Models/TokenUsageTracker.cs
    - Models/WizardState.cs
decisions:
  - "Speech usage entries are labelled \"Voicemail - {label}\" (image entries \"Image - {label}\") so the ticker breakdown reads in plain words"
  - "A non-user OperationCanceledException (internal timeout escaping the client) becomes Failed \"the OpenAI media request failed: timed out\" rather than propagating"
  - "Media cost is charged only on Ok results; Failed/Stopped carry CostUsd 0 and record no tracker entry"
metrics:
  duration: 12min
  completed: 2026-10-04
  tasks: 2
  files: 7
---

# Phase 6 Plan 04: Media Seam Summary

IMediaGenerator seam over the OpenAI media client that classifies every image/speech call as Ok, Failed (retryable) or Stopped (rejected key or exhausted quota) with plain-words reasons, prices successes per unit from editable MediaPrices into the session cost ticker, and ships a scriptable FakeMediaGenerator for downstream tests.

## What Was Built

- **Task 1 (TDD):** `MediaResultKind`/`MediaResult`/`IMediaGenerator`; `OpenAIMediaGenerator` maps null/empty client results to Failed ("the image request was refused" / "the model returned no usable content"), 401/403 to Stopped "the OpenAI key was rejected", quota/billing 429 to Stopped "the OpenAI quota is used up", other 429 to Failed "the OpenAI media service is busy (rate limited)", other statuses to Failed "the OpenAI media request failed ({status})", and transport/SDK/JSON surprises to Failed "the OpenAI media request failed: ...". User cancellation rethrows. `MediaPrices` with clamped Normalize; `TokenUsageTracker.RecordMediaUsage` (ModelId "media", Provider "OpenAI", 0 tokens); `WizardState.CreateMediaGenerator()` gated on `IsMediaAvailable` (legacy `CreateMediaClient` kept).
- **Task 2:** `FakeMediaGenerator` (sealed, `_lock`, `Requests`, `Respond`, `StopAfter`, static `Failed`/`Stopped`), deterministic PNG-signature / ID3-prefixed bytes, CostUsd 0.006 / 0.001; smoke fact `FakeMediaGenerator_StopAfter_ReturnsStopped`.

## Commits

- 7ea8747 test(06-04): add failing tests for media seam classification and pricing
- 1a70855 feat(06-04): media seam with Ok/Failed/Stopped classification and per-unit pricing
- 3878428 test(06-04): add FakeMediaGenerator test double with scripted mid-run stop

## Verification

- `dotnet test --filter FullyQualifiedName~MediaGeneratorTests`: 23 passed (wire tests via FakeHttpHandler, no network)
- Full suite: 2781 passed, 0 failed (baseline 2758 + 23)
- `dotnet build ReelDiscovery.csproj`: 0 warnings

## Deviations from Plan

None - plan executed exactly as written. (Speech operation label "Voicemail - {label}" was unspecified in the plan and chosen here; recorded as a decision.)

## TDD Gate Compliance

RED `test(06-04)` 7ea8747 (did not compile: types missing) followed by GREEN `feat(06-04)` 1a70855. No refactor needed.

## Threat Mitigations

- T-06-11: Stopped results for auth/quota let the caller halt remaining media; throttling stays retryable Failed.
- T-06-12: Reasons for 401/403/429 are fixed strings; no exception messages or keys are echoed for those paths.
- T-06-13: Every Ok result records a tracker entry and carries CostUsd.

## Self-Check: PASSED

- FOUND: Models/MediaPrices.cs, Services/Evidence/IMediaGenerator.cs, Services/Evidence/OpenAIMediaGenerator.cs, ReelDiscovery.Tests/Fakes/FakeMediaGenerator.cs, ReelDiscovery.Tests/Evidence/MediaGeneratorTests.cs
- FOUND: 7ea8747, 1a70855, 3878428
