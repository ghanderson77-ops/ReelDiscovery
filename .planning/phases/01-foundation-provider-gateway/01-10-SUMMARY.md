---
phase: 01-foundation-provider-gateway
plan: 10
subsystem: generation
tags: [llm-gateway, structured-outputs, strict-schema, email-generation, media]
requires:
  - phase: 01-02
    provides: RunIsolatedAsync, ResolveReferencedEmail, BuildMeetingStart, DocumentChainState.ReserveNextVersion
  - phase: 01-05
    provides: OpenAIMediaClient (gpt-image / tts-1)
  - phase: 01-07
    provides: WizardState.CreateGateway(), WizardState.CreateMediaClient()
provides:
  - Gateway-based EmailGenerator (Volume tier) with strict-schema DTOs and SchemaTypes
  - Media via OpenAIMediaClient; null media skips the image and voicemail passes
  - EmailGeneratorPortTests (schema strictness + end-to-end fake-provider EML run)
affects: [01-11]
tech-stack:
  added: []
  patterns:
    - "EmailGenerator.GenerateVolumeAsync<T> wraps ILlmGateway.GenerateAsync with LlmTier.Volume + GetValueOrThrow"
key-files:
  created:
    - ReelDiscovery.Tests/Generation/EmailGeneratorPortTests.cs
  modified:
    - Services/EmailGenerator.cs
    - UserControls/StepGeneration.cs
key-decisions:
  - "VoicemailScriptResponse reduced to voicemailScript only (shouldCreateVoicemail/recipientName were used only by the deleted dead method)"
  - "GeneratePlannedVoicemailAsync returns before the script LLM call when no media client exists (no spend on unusable voicemails)"
  - "Thread subject rule moved from the JSON template into CRITICAL RULES as plain text (first batch: original subject; later batches: exact existing subject)"
patterns-established:
  - "Same legacy-port pattern as 01-08: internal DTOs + [Description] + SchemaTypes theory test + call-site factory swap"
requirements-completed: []
duration: 4min
completed: 2026-09-30
---

# Phase 01 Plan 10: EmailGenerator Gateway Port Summary

**EmailGenerator now generates threads, Word/Excel/PowerPoint attachments, meeting detection and voicemail scripts through ILlmGateway on the Volume tier with strict-schema DTOs. Images and TTS go through OpenAIMediaClient. OpenAIService is no longer referenced anywhere except WizardState.CreateOpenAIService.**

## Performance

- **Duration:** about 4 min
- **Completed:** 2026-09-30
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments

- All nested DTOs are `internal`, with 38 `[Description]` attributes taken from the old prompt JSON templates. `EmailDto.CcEmails` is non-null. `ExcelDocResponseRaw` and the JsonElement conversion are gone; strict mode forces string cells.
- `EmailGenerator.SchemaTypes` lists the 6 gateway DTOs, and each one passes the strict-schema validator with 16 or fewer union types.
- New constructor `EmailGenerator(ILlmGateway llm, OpenAIMediaClient? media = null)`. The six text calls go through `GenerateVolumeAsync<T>` with these token limits: thread batch 32K, Word/Excel/PowerPoint 16K, voicemail script 4K, meeting detection 4K. A failed call throws `LlmCallException`, which the existing per-storyline catch and `RunIsolatedAsync` log before moving on.
- Removed 5 embedded JSON templates and 6 "Respond with (valid) JSON only" lines. All behavioural instructions stay.
- Images use `_media.GenerateImageAsync` and TTS uses `_media.GenerateSpeechAsync`. When there is no media client, both passes return without calling any API.
- Deleted the dead methods `SelectEmailsForAttachments`, `GenerateAttachmentAsync`, `GenerateImageForEmailAsync` (with `ImageSuggestionResponse`) and `GenerateVoicemailForEmailAsync`. The file shrank by about 400 lines.
- `StepGeneration` now calls `new EmailGenerator(_state.CreateGateway(), _state.CreateMediaClient())`.
- The full test suite passes: 159 tests.

## Task Commits

1. **Task 1: Harden all EmailGenerator DTOs for strict schemas** - `f12a8f6` (feat)
2. **Task 2: Switch EmailGenerator to ILlmGateway + OpenAIMediaClient, delete dead code, update StepGeneration** - `e4e7344` (feat)

## Files Created/Modified

- `Services/EmailGenerator.cs` - gateway calls, media client, hardened DTOs, SchemaTypes, dead code removed
- `UserControls/StepGeneration.cs` - factory call-site swap
- `ReelDiscovery.Tests/Generation/EmailGeneratorPortTests.cs` - schema theory, SchemaTypes coverage, end-to-end EML run, null-media constructor

## Decisions Made

- `VoicemailScriptResponse` keeps only `voicemailScript`. Its other two fields were used only by the deleted dead method, as the plan allows in action 6.
- The voicemail pass checks `_media == null` before the script call, so it never pays for a script whose audio cannot be produced (T-01-38). The image pass checks the same thing before the image call.
- The "DALL-E" wording in the image planning instruction became "an image model".

## Deviations from Plan

None. The plan was executed as written.

## Issues Encountered

None.

## TDD Gate Compliance

Both tasks were marked `tdd="true"`, but each task committed its tests and implementation together, following the 01-08 precedent. The schema tests need `SchemaTypes` to compile, and the end-to-end test needs the new constructor. There are no separate `test(...)` RED commits.

## Known Stubs

None.

## Next Phase Readiness

- All four legacy generators are on the gateway. Plan 01-11 adds fatal-stop handling and the media planning guard with its tests, and deletes `OpenAIService` / `CreateOpenAIService()`.

## Self-Check: PASSED

- FOUND: Services/EmailGenerator.cs, UserControls/StepGeneration.cs, ReelDiscovery.Tests/Generation/EmailGeneratorPortTests.cs
- FOUND: f12a8f6, e4e7344
