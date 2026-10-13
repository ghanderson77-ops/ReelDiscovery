---
phase: 07-scale-cost-qa-cutover
plan: 20
subsystem: legacy-removal
tags: [cutover, dead-code, PLAT-05, OUT-03, D-20]
requires:
  - phase: 07-04
    provides: "ThemeSanitizer moved out of ThemeGenerator"
  - phase: 07-19
    provides: "WizardForm references only the case steps"
provides:
  - "Models/MediaSettings.cs: ImageModel/ImageQuality for CreateMediaClient/CreateMediaGenerator"
  - "WizardState without any legacy member"
  - "PlatformTests reflection guards for every deleted legacy type and WizardState member"
affects: []
tech-stack:
  added: []
  patterns: ["Reflection guard (Assembly.GetType == null) pins deleted types so they cannot creep back"]
key-files:
  created:
    - Models/MediaSettings.cs
  modified:
    - Models/WizardState.cs
    - Helpers/ThreadingHelper.cs
    - Helpers/FileNameHelper.cs
    - Models/OrganizationTheme.cs (renamed from Models/PresentationTheme.cs)
    - ReelDiscovery.Tests/Documents/DocumentRegressionTests.cs
    - ReelDiscovery.Tests/Concurrency/ConcurrencyTests.cs
    - ReelDiscovery.Tests/PlatformTests.cs
  deleted:
    - Services/EmailGenerator.cs
    - Services/StorylineGenerator.cs
    - Services/CharacterGenerator.cs
    - Services/ThemeGenerator.cs
    - Services/EmlFileService.cs
    - UserControls/StepTopicInput.cs
    - UserControls/StepStorylines.cs
    - UserControls/StepCharacters.cs
    - UserControls/StepGenerationConfig.cs
    - UserControls/StepGeneration.cs
    - UserControls/StepComplete.cs
    - Models/Storyline.cs
    - Models/Character.cs
    - Models/EmailThread.cs
    - Models/EmailMessage.cs
    - Models/GenerationResult.cs
    - Models/GenerationConfig.cs
    - Helpers/DateHelper.cs
    - ReelDiscovery.Tests/Generation/ (8 files)
    - ReelDiscovery.Tests/RunSummaryTests.cs
    - ReelDiscovery.Tests/MediaAvailabilityPolicyTests.cs
decisions:
  - "[07-20]: ApplyMediaAvailability removed outright: its only callers were the legacy steps; the case flow gates media through IsMediaAvailable / CreateMediaGenerator"
  - "[07-20]: Models/PresentationTheme.cs renamed to Models/OrganizationTheme.cs (git mv) so file name = type name after the empty PresentationTheme alias was deleted"
  - "[07-20]: DocumentRegressionTests EML round-trip now writes a ComposedEmail root + reply (Word attachment from OfficeDocumentService) through MailboxWriter and checks In-Reply-To, RE: subject and the .docx MIME part"
  - "[07-20]: ThreadingHelper keeps GenerateMessageId, AddReplyPrefix, AddForwardPrefix, GetCleanSubject, QuoteText; FileNameHelper keeps only SanitizeForFileName"
metrics:
  duration: 15min
  completed: 2026-10-07
  tasks: 2
  files: 38
---

# Phase 7 Plan 20: Delete the Legacy Generation Path Summary

The legacy topic -> storylines -> characters -> emails path is gone. Its steps, generators, EML writer, models, date helper and tests have been deleted. The image model and quality defaults moved to a new `MediaSettings` class first. The case flow builds and runs as before.

## Tasks

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Delete legacy steps, generators and their tests | db317c7 | 5 services, 6 step controls, Tests/Generation/, RunSummaryTests, MediaAvailabilityPolicyTests deleted; DocumentRegressionTests ported; ConcurrencyTests trimmed |
| 2 | MediaSettings, trimmed WizardState/helpers, legacy models deleted, reflection guard | 1953532 | Models/MediaSettings.cs, WizardState, ThreadingHelper, FileNameHelper, OrganizationTheme.cs (renamed), 7 files deleted, PlatformTests |

## What changed

- **MediaSettings**: `ImageModel = "gpt-image-2.5-flare"` and `ImageQuality = "low"`, with the same defaults and comments as `GenerationConfig`. `WizardState.MediaSettings` feeds `CreateMediaClient` and `CreateMediaGenerator`.
- **WizardState**: removed Topic, AdditionalInstructions, StorylineCount, WantsDocuments/Images/Voicemails, Storylines, AISuggested*, Characters, CompanyDomain, DomainThemes, Config, GeneratedThreads, Result and `ApplyMediaAvailability`.
- **Helpers**: removed `ThreadingHelper.SetupThreading`, `FormatQuotedReply` and `FormatForwardedContent`, plus `FileNameHelper.GenerateEmlFileName`, `GenerateAttachmentFileName` and `TruncateSubject`.
- **Tests**:
  - `ConcurrencyTests` keeps only `NoSharedRandomInstances`. The GenerationResult, GenerationProgress and EmailGenerator chain tests went with their types.
  - `MediaGatingTests` was unchanged; its assertions were already case-flow only.
  - `PlatformTests` adds `LegacyType_IsDeleted` (21 types), `WizardState_HasNoLegacyMembers` and `MediaSettings_KeepsImageDefaults`.

## Verification

- `dotnet clean` then `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- Test project build: 0 errors. The 5 warnings it shows are pre-existing and in files this plan did not touch (see Deferred Issues).
- Non-render suite (`Category!=Render&Category!=Scale`): 3710 passed.
- Render suite (`Category=Render`): 135 passed.
- No `Category=Scale` tests exist at this point.
- Acceptance greps:
  - No legacy model, helper or alias names remain in Forms, UserControls, Services, Models, Helpers or Program.cs.
  - `class OrganizationTheme` appears exactly once.
  - WizardState has no legacy fields.

## Deviations from Plan

- **MediaAvailabilityPolicyTests deleted in Task 1 instead of trimmed.** Every assertion it made covered legacy fields (`WantsImages`, `Config.Include*`) through `ApplyMediaAvailability`, so nothing case-flow remained. The case-flow gating assertions live in `MediaGatingTests`, which is kept.
- **The ConcurrencyTests GenerationResult/GenerationProgress tests were removed in Task 2**, together with their types. This kept Task 1 green while the models still existed.

## Deferred Issues

- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror` fails on 5 pre-existing warnings:
  - xUnit1031 in `Evidence/GenerateCorpusStepTests.cs` (2 warnings)
  - xUnit1031 in `Qa/CaseSummaryQaTests.cs` (2 warnings)
  - CS8600 in `Render/QaDialogRenderTests.cs` (1 warning)

  These files are out of scope for this plan and are logged in `deferred-items.md`. The app project is warning-free.

## Self-Check: PASSED

- FOUND: Models/MediaSettings.cs, Models/OrganizationTheme.cs
- MISSING (intended): every deleted file listed above
- FOUND commits: db317c7, 1953532
