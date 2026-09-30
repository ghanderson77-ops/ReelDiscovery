---
phase: 01-foundation-provider-gateway
plan: 06
subsystem: platform-security
tags: [dpapi, api-keys, migration, security]
requires:
  - "01-03: LlmProviderKind enum (ReelDiscovery.Services.Ai)"
provides:
  - "ApiKeyStore: GetKey / SetKey / ClearKey / MigrateLegacyKey (DPAPI CurrentUser, keys.dat)"
  - "Startup migration of legacy Base64 reeldiscovery.settings"
affects:
  - "01-07: keys screen reads/writes through ApiKeyStore"
tech-stack:
  added: []
  patterns:
    - "DPAPI ProtectedData (CurrentUser + app entropy), Base64 blobs in a small JSON file"
    - "Atomic write: temp file + File.Move(overwrite: true)"
key-files:
  created:
    - Services/ApiKeyStore.cs
    - ReelDiscovery.Tests/ApiKeyStoreTests.cs
  modified:
    - Program.cs
    - UserControls/StepApiConfiguration.cs
decisions:
  - "Legacy StepApiConfiguration Save/LoadApiKey now go through ApiKeyStore (interim bridge until 01-07)"
  - "A per-entry corrupt/foreign blob only hides that provider's key; an unparseable keys.dat hides all keys and is replaced on next SetKey/ClearKey"
metrics:
  duration: 3min
  completed: 2026-09-30
  tasks: 2
  files: 4
---

# Phase 1 Plan 06: DPAPI API Key Store Summary

OpenAI and Anthropic API keys are now stored in `%LOCALAPPDATA%\ReelDiscovery\keys.dat`, each encrypted with DPAPI (CurrentUser scope plus app entropy). The legacy Base64 `reeldiscovery.settings` key is imported once at startup, then overwritten and deleted.

## What Was Built

- **`Services/ApiKeyStore.cs`** follows the plan's contract. Each key is stored as `Base64(ProtectedData.Protect(UTF8(key), Entropy, CurrentUser))` in a JSON DTO (`openai`, `anthropic`).
  - Writes go to `keys.dat.tmp` first and then replace `keys.dat` with `File.Move(overwrite: true)`.
  - A `JsonException`, `CryptographicException` or `FormatException` counts as "key absent". There are no bare catches.
  - `MigrateLegacyKey()` never overwrites an existing DPAPI OpenAI key and is safe to run repeatedly. If the legacy file isn't valid Base64, it is left in place.
- **`Program.cs`** runs `new ApiKeyStore().MigrateLegacyKey()` after the telemetry opt-in and before `Application.Run`. If migration fails, a warning dialog shows `ex.Message` only.
- **Tests:** there are 9 `ApiKeyStoreTests`, and each uses its own temp folder. They cover:
  - saving and reading back both keys
  - no plaintext and no Base64 of the key on disk
  - clearing one provider's key without affecting the other
  - importing and deleting the legacy file, and that a second run does nothing
  - not overwriting an existing key
  - leaving an invalid Base64 legacy file in place
  - corrupt entries and a corrupt file
  - leaving telemetry `settings.json` untouched

## Verification

- `dotnet test --filter ApiKeyStoreTests`: 9/9 passed
- Full suite: 118/118 passed
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- Acceptance greps: no bare catch, no `settings.json` reference, and the migration runs before `Application.Run` (line 31 vs 44)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Legacy API step would lose the key and recreate the Base64 file**
- **Found during:** Task 2
- **Issue:** The startup migration deletes `reeldiscovery.settings`. The existing `StepApiConfiguration.LoadApiKey` still read that file, so the saved key would stop pre-filling. `SaveApiKey` would also write the plaintext-equivalent file again on the next Next click, which undoes D-09.
- **Fix:** `SaveApiKey`/`LoadApiKey` now call `ApiKeyStore.SetKey/GetKey(OpenAI)`. Removed the legacy path helper and constant. Replaced the bare catches with `catch (Exception)` plus explanatory comments.
- **Files modified:** UserControls/StepApiConfiguration.cs
- **Commit:** 71521e7

## TDD Gate Compliance

RED `f8b30e8` (test) came before GREEN `ef0b0bb` (feat). No refactor was needed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 (RED) | f8b30e8 | test(01-06): add failing ApiKeyStore DPAPI and migration tests |
| 1 (GREEN) | ef0b0bb | feat(01-06): implement DPAPI ApiKeyStore with legacy key migration |
| 2 | a885652 | feat(01-06): migrate legacy API key to DPAPI store at startup |
| fix | 71521e7 | fix(01-06): route legacy API step key persistence through ApiKeyStore |

## Known Stubs

None.

## Threat Flags

None. All surface is covered by T-01-21..T-01-25.

## Self-Check: PASSED
