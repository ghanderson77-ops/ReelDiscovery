---
phase: 09-edit-safety-navigation
plan: 04
subsystem: generate-corpus
tags: [w1-gate, guide-staleness, approval, state-card, render]
requires:
  - "09-01: CorpusApproval.WithdrawIfGuideChanged, GenerationEntry.GuideChangedCard, GenerationStateCard.GuideChanged, StateCard(emailsWritten, hasPlan), RunRefused* constants"
provides:
  - "Generate Corpus entry GuideChanged branch (withdraw + save, no journal read, no provider call)"
  - "RunCoreAsync refusal for stale/unapproved plans before EnsureFolder and gateway creation"
  - "ClickStateActionAsync explicit cases: Start Over only from PlanChanged/Unreadable; default no-op"
  - "GenerateCorpusRenderState.GuideChanged / NoPlan"
affects: [UserControls/StepGenerateCorpus.Run.cs, UserControls/StepGenerateCorpus.cs]
tech-stack:
  added: []
  patterns: ["gate re-check at run start (stored Approved flag never starts a run alone)"]
key-files:
  created: []
  modified:
    - UserControls/StepGenerateCorpus.Run.cs
    - UserControls/StepGenerateCorpus.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
decisions:
  - "Generate Corpus state card default branch is a no-op; Start Over is reachable only from the PlanChanged and Unreadable cards (09 Pitfall 1)"
  - "RunCoreAsync re-checks IsGuideStale and Approved on every run start (Generate Emails, Continue, Retry, banner) before any folder/gateway work"
metrics:
  duration: "~10 min"
  completed: 2026-10-12
  tasks: 2
  files: 4
---

# Phase 9 Plan 04: Generate Corpus guide-changed gate Summary

Generate Corpus now pauses on a guide edited after planning: entry withdraws and saves approval and shows "The case guide changed after this plan was built" (Review Plan), every run start re-checks staleness/approval before a gateway exists and explains a refusal in a Warning status line, and the card action can no longer fall through to Start Over.

## Tasks

| Task | Name | Commit | Files |
| ---- | ---- | ------ | ----- |
| 1 | GuideChanged entry branch, explicit card action, RunCoreAsync refusal | f31c98e | StepGenerateCorpus.Run.cs, GenerateCorpusStepTests.cs |
| 2 | Render states GuideChanged and NoPlan | 596310d | StepGenerateCorpus.cs, GenerateCorpusRenderTests.cs |

## What changed

- `OnEnterStepAsync`: `GuideStale = CorpusApproval.IsGuideStale(Draft)` in the entry input; journal read skipped when stale; new `GenerationEntry.GuideChangedCard` case withdraws (`WithdrawIfGuideChanged`) + `SaveAsync`, then `ShowGuideChangedCard()` (body carries the "N emails already written stay in place" sentence). NotApproved card passes `hasPlan: plan != null` ("No corpus plan yet" variant).
- `ClickStateActionAsync`: `GuideChanged` stacked with `NotApproved` (navigate to Review Plan); `PlanChanged`/`Unreadable` explicitly run `StartOverAsync`; `default` returns `Task.CompletedTask`.
- `RunCoreAsync`: stale guide -> withdraw/save, GuideChanged card, `RunRefusedGuideChangedText` in `Theme.Warning`; no plan / unapproved -> NotApproved card, `RunRefusedNotApprovedText` in Warning. Both above `EnsureFolder` and the gateway factory. `ShowStateCard` already hides the dashboard, banner, Start Over and Retry buttons.
- `LoadForRender`: `GuideChanged` and `NoPlan` states; notice hidden for NotApproved, NoPlan, GuideChanged.

## Tests

- New step tests (6): stale entry (card, Approved false in memory and on disk, no requests, no journal), kept plan -> Ready, unapproved + stale -> GuideChanged, journal survives GuideChanged click (Confirm never called, journal files unchanged, Generation intact), stale refusal, not-approved refusal (Continue). All assert no provider requests.
- `GenerateCorpusStepTests|GenerationUiTextTests`: 314/314 passed.
- `Category=Render&GenerateCorpusRenderTests`: 23/23 passed (Clipping empty, no state-card overflow, 1 Primary).
- `-warnaserror` build: 0 warnings, 0 errors on both commits.

## Render inspection

Inspected by eye: `generate-corpus-guide-changed-1000x540.png`, `generate-corpus-guide-changed-1100x750.png`, `generate-corpus-no-plan-1000x540.png`, `generate-corpus-no-plan-1100x750.png` (plus `.clip.txt` = "no findings"). Heading and body fully visible and wrapped, "Review Plan" not ellipsized, content centered in the card, no scrollbar, plan notice hidden.

**DPI note:** this session rendered at 120 DPI (125%), not 144. The harness accepts >= 120 and layouts are DPI-scaled, but a human check of the GuideChanged and No-plan cards at 150% scaling is flagged.

## Deviations from Plan

None in behavior. Process note: TDD RED commits were not made separately; tests and implementation were committed together per task (both feat commits). The journal-survives test would fail against the old `default: StartOverAsync()` branch (Confirm called, journal deleted).

## TDD Gate Compliance

No separate `test(...)` RED commits; the plan's tasks are tdd="true" but each was committed as a single feat commit containing tests and implementation.

## Known Stubs

None.

## Self-Check: PASSED
