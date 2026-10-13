---
phase: 04-corpus-plan-answer-key
plan: 01
subsystem: corpus-models
tags: [corpus, models, answer-key, case-json, persistence]
requires: []
provides:
  - CorpusSettings (RelevantCount, DefaultTotalFor, CreateDefault, DefaultWindow, ClampWindow, EnabledAttachmentTypes, ComputeHash, Normalize, SanitizePrefix)
  - CorpusState / WrittenCorpusFile / CorpusFileKind
  - CorpusPlan / PlannedThread / PlannedMessage / PlannedAttachment / ExternalParty / ReviewProtocolProse / IssueProse
  - AnswerKeyRow + AnswerKeyColumns.Headers (23 columns)
  - Helpers.Apportion.Largest
  - CaseDraft.CurrentFormatVersion = 2 + CaseDraft.Corpus
affects: [04-02..04-22 planner, validator, answer key, protocol, UI plans]
tech-stack:
  added: []
  patterns: [largest-remainder apportionment with minimum, deep Normalize for untrusted JSON, JsonIgnore runtime plan reference]
key-files:
  created:
    - Helpers/Apportion.cs
    - Models/Corpus/CorpusSettings.cs
    - Models/Corpus/CorpusState.cs
    - Models/Corpus/CorpusPlan.cs
    - Models/Corpus/PlannedThread.cs
    - Models/Corpus/PlannedMessage.cs
    - Models/Corpus/PlannedAttachment.cs
    - Models/Corpus/ExternalParty.cs
    - Models/Corpus/ReviewProtocolProse.cs
    - Models/Corpus/AnswerKeyRow.cs
    - ReelDiscovery.Tests/Corpus/CorpusSettingsTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusModelTests.cs
  modified:
    - Models/Case/CaseDraft.cs
    - ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideModelTests.cs
    - ReelDiscovery.Tests/Case/CaseStoreTests.cs
decisions:
  - "[04-01]: CaseDraft.Normalize upgrades FormatVersion below current to current, so a v1 case re-saved with Corpus is refused by older builds instead of losing Corpus (T-04-03)"
  - "[04-01]: ClampWindow only widens toward the fact range (start later than first fact -> first fact; end earlier than last fact -> last fact); invalid dates are flagged and left as typed; EndBeforeStart compares the typed dates"
  - "[04-01]: Override ranges: HotPerHotFact 1-5, PrivilegedPercent 0-30, AttachmentPercent 0-60; negative = automatic (null)"
  - "[04-01]: Apportion.Largest treats negative weights as 0"
metrics:
  duration: 12min
  completed: 2026-10-02
  tasks: 3
  files: 16
---

# Phase 4 Plan 01: Corpus Data Contracts Summary

This plan adds the Phase 4 data contracts that the later plans build on. `CorpusSettings` holds the clamped defaults, the window math and the one shared relevant/non-relevant split. `CorpusState` is saved in case.json and keeps the plan itself out of the file. The plan model (thread, message, attachment, external party, protocol prose) has code-owned fields and a deep `Normalize`. The answer-key contract is fixed at 23 columns. A largest-remainder helper is added, and case.json moves to format v2 with `CaseDraft.Corpus`.

## What Was Built

- **Apportion.Largest(total, weights, min)**: largest-remainder split, ties broken by index, equal split when every weight is zero, all-min when the total cannot cover the minimums, empty when there are no items.
- **CorpusSettings**:
  - Constants: 50-5,000 emails, 5-60% relevant, presets 250/1,000/2,500/5,000, mix 50/20/20/10.
  - `RelevantCount` rounds away from zero; it is the shared split for the screen and the allocator.
  - `DefaultTotalFor` maps Simple/Standard/Complex to 250/1,000/2,500.
  - `DefaultWindow` runs from the first fact minus 1 month to the last fact plus 2 months; `ClampWindow` widens the window so every fact stays inside.
  - `EnabledAttachmentTypes` gates Image and Voicemail on media availability.
  - `ComputeHash`, and `Normalize` (clamps, mix renormalization via Apportion, DocID prefix sanitized to A-Z with at most 6 letters).
- **CorpusState**: settings, PlanId, plan basis hashes, approval, dismissed hashes and `WrittenFiles`. `[JsonIgnore] Plan` and `[JsonIgnore] HasPlan`. `PlanFileName = "corpus-plan.json"`.
- **Plan model**: `PlannedMessage.SentAt` is a `DateTimeOffset` and keeps its offset through a round-trip. Enums are written as names. `CorpusPlan.Normalize` null-cleans every list and string at every level. `AllMessages()` returns messages in thread order, then by Seq. `EmailCount`/`AttachmentCount` are not serialized.
- **AnswerKeyRow / AnswerKeyColumns.Headers**: the D-32 column order plus Doc Type, File Name and Parent DocID (23 columns). `ToValues()` returns values in header order.
- **CaseDraft**: `CurrentFormatVersion = 2`, a `Corpus` property after `Guide`, and `Corpus?.Normalize()` right after `Guide?.Normalize()`.

## Tests Changed In Place (version hard-codes)

- `ReelDiscovery.Tests/Case/CaseGuideModelTests.cs`: `Assert.Equal(1, ...FormatVersion)` changed to `CaseDraft.CurrentFormatVersion` (2 places).
- `ReelDiscovery.Tests/Case/CaseStoreTests.cs`: `Load_NewerVersion` now writes `CurrentFormatVersion + 1` instead of the literal 2.

## Verification

- Focused filter (CaseDraft, CaseStoreTests, CorpusSettingsTests, CorpusModelTests, CaseGuideModelTests): 105 passed.
- Full suite excluding Render tests (`Category!=Render`): 1,348 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 warnings.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Minimal CorpusPlan placeholder in Task 1**
- **Found during:** Task 1
- **Issue:** `CorpusState.Plan` is typed `CorpusPlan`, which belongs to Task 2.
- **Fix:** Task 1 shipped a placeholder `CorpusPlan` with only `Notes`. Task 2 replaced it with the full model.
- **Commits:** 211c72e, 808613c

**2. [Rule 2 - Critical] FormatVersion upgrade on Normalize (T-04-03)**
- **Found during:** Task 3
- **Issue:** `Normalize` only repaired `FormatVersion <= 0`. A v1 case.json loaded and re-saved with Corpus data would still say version 1, so an older build would open it and silently drop Corpus. That defeats mitigation T-04-03.
- **Fix:** `Normalize` now raises any version below current to current. Newer files never reach Normalize, because CaseStore refuses them first. A test covers loading v1 through CaseStore.
- **Files modified:** Models/Case/CaseDraft.cs
- **Commit:** 0e586c8

## TDD Gate Compliance

RED/GREEN pairs for each task: c33f8e3 -> 211c72e, e9cfba2 -> 808613c, 9389b45 -> 0e586c8. No refactor commits were needed.

## Known Stubs

None. These are data contracts only; the later Phase 4 plans fill them.

## Self-Check: PASSED
