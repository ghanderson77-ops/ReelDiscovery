---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 14
subsystem: generation-ui
tags: [winforms, wizard-step, dashboard, journal-replay, render-tests, dpi]
requires:
  - phase: 05-06
    provides: GenerationUiText (tiles, activity, progress, outcome, entry decision, state cards, banner)
  - phase: 05-13
    provides: GenerationRunner / GenerationReset (the run 05-15 wires into this step)
  - phase: 05-10
    provides: GenerationJournal.Replay
  - phase: 05-11
    provides: GenerationRunState
provides:
  - StepGenerateCorpus (layout, ApplyView, ShowStateCard, ShowDashboard, ShowOutcome, SetStatus, LoadForRender)
  - StepGenerateCorpus.Run.cs (IWizardStep members, entry decision, journal-rebuilt dashboard, Next gate)
  - GenerationHistory.Events / GenerationHistory.Waves
  - GenerateCorpusRenderState, GenerateCorpusRenderTests, GenerateCorpusEntryTests
affects: [05-15 run wiring, WizardForm 8-step flow]
tech-stack:
  added: []
  patterns:
    - Snapshot painting: ApplyView updates wave rows in place and inserts new activity rows at the top in one batch (cap 500)
    - Entry replays the journal and builds the history view inside Task.Run; the UI thread only paints
    - Entry tests run on an STA thread with a WindowsFormsSynchronizationContext and assert there (tooltip-created handles belong to it)
key-files:
  created:
    - UserControls/StepGenerateCorpus.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - Services/Evidence/GenerationHistory.cs
    - ReelDiscovery.Tests/Evidence/GenerationHistoryTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
  modified:
    - UserControls/StatTile.cs
decisions:
  - "Dot columns use GridStyle.AddDotColumn (28 logical px), not UI-SPEC's 24; fixed widths total 308 (Waves) and 188 (Activity); WizardMin still gives Dates >= 120 and Event >= 160"
  - "Activity Time column is max(88, measured '12:59:59 PM' + 15 inset + 8): 88 trims the time at Segoe UI 9.5"
  - "Waves Dates tooltip is the full date range plus the UI-SPEC thread/email counts, because the column trims long ranges at WizardMin"
  - "Ready card cost uses _state.StoryModelConfig (the same model LlmGateway.ResolveModel(Story) returns) so entering the step never builds a provider"
  - "A case folder that is offline on entry shows the Unreadable card with the folder error"
  - "Reopened dashboard counts come from the journal (GenerationRunState + LedgerMerger.Build); Counts.Finished = no pending threads"
metrics:
  duration: ~35min
  completed: 2026-10-03
  tasks: 3
  files: 6
---

# Phase 5 Plan 14: Generate Corpus Step Layout and Entry Summary

The Generate Corpus wizard step is now code-built per 05-UI-SPEC S1: header, scope notice, partial-run banner, seven stat tiles, Waves and Activity cards side by side (or a full-width state card), and a status strip with a 220x6 bar and Cancel Generation. It paints any `GenerationView` snapshot. On entry it replays the journal off the UI thread and shows the right state: Not approved, Plan changed, Unreadable, Ready, or the journal-rebuilt dashboard. Entering the step never starts a run. Every state passes the 144-DPI render check at Large and WizardMin.

## What Was Built

**Task 1: Layout (`UserControls/StepGenerateCorpus.cs`)**
- Root TLP rows 0-5 with row-index comments. The banner row is sized absolutely from the page width (Review Plan pattern), and the notice wraps to the root width.
- Tiles row copied from Review Plan. The Threads tile measures its value at TitleFont against the tile's value width and falls back to variant 1 or 2 (DPI rule 19). Warnings and Failed tile values take their tone colors.
- Waves grid columns: dot, Wave, Dates (fill, min 120), Threads, Warnings, Failed. Activity grid columns: dot, Time, Thread, Event (fill, min 160). Both grids are read-only and not sortable, with `ScrollBars.Vertical`. Dots are painted with `Theme.DrawStatusDot` from a row tag.
- `ApplyView` creates wave rows once per wave count and then updates cells in place. New events go in at the top in one `SuspendLayout` batch, capped at 500 rows. While running it sets bar = (done + failed) / total and the status text to `ProgressText` in Accent.
- Other members: `ShowStateCard` (Danger or Primary action), `ShowDashboard` (header buttons per the Row 0 table, banner via `BannerBody`), `ShowOutcome`, and `SetStatus` (no-op after dispose). There is a 250 ms `_refreshTimer` that is not started yet, plus internal accessors for tests.

**Task 2: History and entry**
- `GenerationHistory.Events(plan, replay, assignments)` emits one event per replayed record in Utc order. The mapping:
  - Written becomes ThreadWritten (root `SubjectFor`, Emails, Details = ledger delta count, FilePath of the last message).
  - Warnings becomes KeptWithWarnings (with Violations).
  - ThreadFailed carries its Reason.
  - Then RunEnded maps Cancelled to RunCancelled (Done/Total), Fatal and InternalError to RunStopped, and Finished to RunFinished. `None` is skipped.
- `GenerationHistory.Waves(plan, state)` builds WaveProgress rows from `WavePlanner.Plan`.
- `StepGenerateCorpus.Run.cs` implements `IWizardStep`. On entry it:
  1. Resets on a case switch.
  2. Calls `CorpusPlanStore.EnsureLoaded`.
  3. Runs `Task.Run(GenerationJournal.Replay + GenerationRunState.From + history view)`.
  4. Calls `GenerationUiText.EntryDecision` and shows the matching card or dashboard.
- `ValidateStepAsync => Task.FromResult(!_isGenerating)`. Dispose stops and disposes the timer.

**Task 3: LoadForRender and tests**
- `LoadForRender(state, render, view, reason, earlierThreads)` shows any of the 8 states with no I/O and no run.
- `GenerateCorpusRenderTests` covers render states 1-6 at both sizes (8 facts).
- `GenerateCorpusEntryTests` (not a render test) covers four cases:
  - No journal: Ready card, with no journal or Mailboxes folder written. This stands in for "fake provider receives 0 requests", because entry never builds a gateway.
  - Unapproved plan: Not approved card.
  - Pending journal: dashboard and banner, with 3 activity rows newest first.
  - Foreign-plan journal: Plan changed card, "1 thread was written...".

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Activity rows were shorter than wave rows**
- **Found during:** Task 3 PNG inspection
- **Issue:** `new DataGridViewRow()` ignores `RowTemplate.Height`, so activity rows were about 22 px instead of 32.
- **Fix:** Rows are now cloned from `_gridActivity.RowTemplate`.
- **Commit:** a5de0ac

**2. [Rule 1 - Bug] The Time column trimmed the time**
- **Issue:** At 88 logical px, "12:59:59 PM" plus the 15 px inset does not fit at Segoe UI 9.5.
- **Fix:** The width is now max(88, measured text + 23). Event still gets well over 160 at WizardMin.
- **Commit:** 4721b72

**3. [Rule 2] Full dates in the Dates tooltip**
- **Issue:** At WizardMin the Dates column trims long ranges such as "Dec 18, 2023 – Jan 7, 2024". That is within spec (min 120), but the information was lost.
- **Fix:** The tooltip line 1 is now the full range, followed by the UI-SPEC counts.
- **Commit:** a5de0ac

### Planned deviation (recorded per plan)
- **Dot column:** 28 logical px (`GridStyle.AddDotColumn`) instead of UI-SPEC's 24. The render test asserts the UI-SPEC minimums at WizardMin: Dates >= 120 and Event >= 160 logical.

### Other adjustments
- `_state` is declared in `StepGenerateCorpus.cs` instead of Run.cs so Task 1 compiles on its own. `RefreshHeader` lives with the layout.
- The Ready-card model comes from `_state.StoryModelConfig` instead of `_state.CreateGateway().ResolveModel(LlmTier.Story)`. It is the same model, and entry no longer needs a provider or key.
- `StatTile` gained `internal ValueColor` and `ValueWidth` for the tone and rule-19 assertions. The file is outside the plan's `files_modified`.
- Button click handlers (Generate Emails, Continue Generating, Retry Failed Threads, Start Over..., Review Plan, Cancel Generation) are not wired yet. Plan 05-15 owns them.

## Inspected PNGs (144 DPI)

I opened and checked these: `generate-corpus-running-1100x750`, `generate-corpus-running-1000x540`, `generate-corpus-partial-1100x750`, `generate-corpus-partial-1000x540`, `generate-corpus-finished-clean-1100x750`, `generate-corpus-finished-issues-1000x540`, `generate-corpus-ready-1000x540`, `generate-corpus-not-approved-1000x540`, `generate-corpus-unreadable-1000x540`. The rest (ready/not-approved/plan-changed/unreadable/finished-issues at 1100x750, plan-changed and finished-clean at 1000x540) passed the automated checks.

Results:
- No clipped buttons or labels, and no horizontal grid scrollbars.
- Tile edges line up with the cards. The header's rightmost button lines up with the cards' right edge.
- Dots are crisp and centered, and all rows are the same height (after fix 1).
- Expected trims at WizardMin, all allowed by the spec: the Failed tile sub-line ("threads not wri..."), long Dates ranges, and Event text in cell trimming.
- At WizardMin with the banner showing, each grid shows about 3 rows plus a scrollbar.

## Verification

- `dotnet test --filter "FullyQualifiedName~GenerationHistoryTests"`: 9 passed.
- `dotnet test --filter "FullyQualifiedName~GenerateCorpus"`: 12 passed (8 render, 4 entry). All 16 `generate-corpus-*` PNGs were written under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`.
- `dotnet test --filter "Category!=Render"`: 2538 passed, 0 failed.
- `CorpusReviewPlanRenderTests` still pass (StatTile change).
- `dotnet build`: 0 warnings. All acceptance greps were met: "// 0: Header" = 1, AddDotColumn = 2, `Interval = 250` = 1, and no `Color.Red`, `RichTextBox` or `new Font(`.

## TDD Gate Compliance

- Task 1 is layout only (build-verified): `4721b72` feat.
- Task 2: RED `7cb09de` (compile failure), then GREEN `39919cd`.
- Task 3: RED `fc982e8` (the tests reference `LoadForRender`, which did not exist yet), then GREEN `a5de0ac`.

## Known Stubs

- The state-card action, the banner's Continue Generating, the header's Start Over... and Retry Failed Threads, and Cancel Generation have no click handlers yet, and `_refreshTimer` is never started. All of these are intentional: plan 05-15 wires the run, Start Over and navigation. This plan's goal is display and entry only.

## Self-Check: PASSED
