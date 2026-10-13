---
phase: 01-foundation-provider-gateway
plan: 09
subsystem: wizard-media-gating
tags: [media-gating, prov-06, prov-05, telemetry-removal, d-16, wizard-ui]
requires:
  - "01-02: repo-root source-scan helper pattern (ConcurrencyTests)"
  - "01-07: WizardState.IsMediaAvailable, MediaUnavailableNotice, WantsImages/WantsVoicemails"
provides:
  - "WizardState.ApplyMediaAvailability(): forces WantsImages/WantsVoicemails and Config.IncludeImages/IncludeVoicemails off without a tested OpenAI key"
  - "Topic and Generation Config steps disable, uncheck and explain the image/voicemail options when media is unavailable"
  - "Slack telemetry removed; TelemetryRemovalTests source-scan guard"
affects:
  - "01-11: EmailGenerator-side media guard (defence in depth with this UI gating)"
tech-stack:
  added: []
  patterns:
    - "UI gating + state-level enforcement: controls disabled on enter, flags forced off in ValidateStepAsync"
    - "Source-scan regression tests with the needle built from parts so the test does not match itself"
key-files:
  created:
    - ReelDiscovery.Tests/MediaAvailabilityPolicyTests.cs
    - ReelDiscovery.Tests/TelemetryRemovalTests.cs
  modified:
    - Models/WizardState.cs
    - UserControls/StepTopicInput.cs
    - UserControls/StepGenerationConfig.cs
    - Program.cs
    - UserControls/StepComplete.cs
  deleted:
    - Services/TelemetryService.cs
    - Forms/TelemetryOptInDialog.cs
decisions:
  - "Topic step media row is now 60px tall and wraps, so the orange notice drops below the checkboxes instead of being clipped"
  - "The Generation Config notice label fills a 600x38 panel with word wrap, so the long notice text is not cut off"
  - "Existing %LOCALAPPDATA%\\ReelDiscovery\\settings.json is left alone (D-16 allows either)"
metrics:
  duration: 8min
  completed: 2026-09-30
  tasks: 3
  files: 9
---

# Phase 1 Plan 09: Media Gating and Telemetry Removal Summary

Image and voicemail options on the Topic and Generation Config steps now depend on having a tested OpenAI key. Without one, they are unchecked and disabled, an orange notice explains why, and `WizardState.ApplyMediaAvailability()` forces all four media flags off when the user leaves either step. The DALL-E wording now refers to OpenAI gpt-image. Slack telemetry has been removed completely (D-16).

## What Was Built

- **`Models/WizardState.cs`**: `ApplyMediaAvailability()` does nothing when `IsMediaAvailable` is true. Otherwise it clears `WantsImages`, `WantsVoicemails`, `Config.IncludeImages` and `Config.IncludeVoicemails`.
- **`UserControls/StepTopicInput.cs`**:
  - New `_lblMediaNotice` (DarkOrange, Segoe UI 9) in the media panel.
  - `OnEnterStepAsync` sets Enabled on the image/voicemail checkboxes from `_state.IsMediaAvailable`, unchecks them when media is unavailable, and shows `MediaUnavailableNotice`.
  - `ValidateStepAsync` calls `ApplyMediaAvailability()` after saving.
- **`UserControls/StepGenerationConfig.cs`**:
  - Wording changes: the header is now "AI-Generated Images (OpenAI gpt-image)", the checkbox reads "uses the OpenAI image API", and the hint reads "about $0.006/image at low quality". The hint panel was widened from 450 to 540 px to fit the longer text.
  - A notice row sits under the images header.
  - On enter, the image/voicemail checkboxes and percent spinners are gated on media availability.
  - `ValidateStepAsync` calls `ApplyMediaAvailability()`.
  - The Privacy section, `_chkTelemetry`, and the telemetry load/save lines are gone. `OnLeaveStepAsync` now just returns `Task.CompletedTask`.
- **Telemetry removal (D-16, resolves T-01-45)**:
  - `Services/TelemetryService.cs` and `Forms/TelemetryOptInDialog.cs` were deleted.
  - `Program.cs` no longer shows the opt-in dialog or sends the launch event. It keeps the disclaimer and the DPAPI key migration.
  - `StepComplete.OnEnterStepAsync` is now synchronous (`LoadStatistics(); return Task.CompletedTask;`).
  - The unused `using ReelDiscovery.Services;` was removed from StepComplete and StepGenerationConfig.
- **Tests**:
  - `MediaAvailabilityPolicyTests` (2 facts).
  - `TelemetryRemovalTests` (2 facts). One scans *.cs/*.csproj/*.json/*.resx/*.config (skipping bin, obj, .git and .planning) for the webhook host. The other checks that both telemetry files are gone and that app code no longer references `TelemetryService` or `TelemetryOptInDialog`.

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- Full suite `dotnet test ReelDiscovery.slnx`: 150/150 passed
- Acceptance greps:
  - StepTopicInput: `ApplyMediaAvailability()` found 1 time; `_state.IsMediaAvailable` and `MediaUnavailableNotice` present.
  - StepGenerationConfig: DALL-E / 0.04/image found 0 times; `ApplyMediaAvailability()` found 1 time.
  - No references to TelemetryService, TelemetryOptInDialog or _chkTelemetry remain in the app folders.
  - No `hooks.slack.com` in any .cs, .csproj, .json or .resx file outside .planning. `CLAUDE.md` still mentions it in prose; that file is not scanned and was not edited.

## TDD Gate Compliance

- Task 1: RED `f1d52e4` (did not compile because `ApplyMediaAvailability` was missing), GREEN `cd95255`
- Task 3: RED `7af158c` (both facts failed), GREEN `ab7331e`

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Topic step notice would be clipped**
- **Found during:** Task 1
- **Issue:** The plan put the roughly 550px notice after three checkboxes in a non-wrapping FlowLayoutPanel inside a 40px row, so in the 900px wizard it would be cut off.
- **Fix:** Set `WrapContents = true` and raised row 7 to 60px so the notice wraps onto a second line.
- **Commit:** cd95255

**2. [Rule 1 - Bug] Generation Config notice/hint clipping**
- **Found during:** Task 2
- **Issue:** The plan's auto-sized bold notice at (0,9) could run past the 600px panel. The longer image-cost hint did not fit the 450px panel.
- **Fix:** The notice is a Dock=Fill label with MiddleLeft alignment, so it wraps inside the panel. The hint panel was widened to 540px.
- **Commit:** 7865e73

## Manual Verification Needed (UI, not a gate per D-03)

- Choose Claude with no tested OpenAI key. On both the Topic and Generation Config steps, check that the orange notice appears and the image/voicemail options (and percent spinners) are unchecked and disabled.
- With a tested OpenAI key, check that the options can be enabled as before.
- Launch the app and confirm no telemetry opt-in dialog appears and the Generation Config step no longer has a Privacy section.
- The user should rotate or revoke the old Slack webhook (it is still in git history).

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: ReelDiscovery.Tests/MediaAvailabilityPolicyTests.cs, ReelDiscovery.Tests/TelemetryRemovalTests.cs
- DELETED as intended: Services/TelemetryService.cs, Forms/TelemetryOptInDialog.cs
- FOUND commits: f1d52e4, cd95255, 7865e73, 7af158c, ab7331e
