---
phase: 06-attachments-non-relevant-corpus
plan: 22
subsystem: generate-corpus-ui
tags: [winforms, generate-corpus, stages-grid, media-banner, ready-card, render-tests, dpi]
requires:
  - "06-02 WaveProgress.RowKind and attachment counters, StageRowKind"
  - "06-13 GenerationUiText Tile / StageRowText / BannerHeading / BannerBody / MediaBanner / ReadyCard / RetryFailedButtonText"
provides:
  - "Stages card: hint 'Relevant waves oldest first, then non-relevant mail', Done column, stage rows from StageRowText with item-based Warnings / Failed and dot colors"
  - "One banner slot: ShowPartialBanner(runEnded, td, tt, ad, at, failed) and ShowMediaBanner(MediaBannerContent); partial first; a media banner button hides Retry Failed Items"
  - "ShowDashboard(banner, done, total, failedItems, finishedWithFailures, runEnded, attachmentsDone, attachmentsTotal)"
  - "Ready card estimate line (Small, TextMuted) and media notice (Small, Warning) labels"
  - "Header Retry Failed Items with RetryFailedTooltip"
  - "GenerateCorpusRenderState: ReadyWithMedia, ReadyMediaUnavailable, RunningNonRelevant, PartialUpgrade, FinishedSkippedMedia, FinishedSkippedMediaWithFailures, FinishedSkippedMediaUnavailable"
  - "LoadForRender(..., ready, media, runEnded); accessors EstimateLabel, MediaNoticeLabel, BannerButton, BannerKind, BannerBodyText"
  - "ShowOutcome(..., retryButtonShown)"
affects: [06-23 run wiring, 06-25 human render inspection]
tech-stack:
  added: []
  patterns:
    - "Banner layout keeps its own has-button flag because Control.Visible reads false while a parent is hidden"
    - "Per-state expected Primary-button count in render tests, never a universal 'exactly one'"
key-files:
  created: []
  modified:
    - UserControls/StepGenerateCorpus.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
decisions:
  - "Every banner button (Continue Generating, Create Skipped Media, Retry Failed and Skipped Items) is the same _btnContinue control, so all of them start the same run through the existing handler. No separate MediaBannerButtonClicked event."
  - "The Done column widens past the spec's 80 so it fits '9,999 / 9,999' at Segoe UI 9.5 (same approach as the Time column); Dates still keeps at least 120 at WizardMin"
  - "The running stage row scrolls into view unless the user scrolled in the last 5 s. This implements a 05-UI-SPEC S1 rule that was never wired and now matters because stage rows sit below 14 wave rows."
  - "ShowDashboard takes bool runEnded instead of a heading string; the entry code passes input.LatestAttemptEnded, which still carries the 06-13 Finished-outcome filter"
  - "PartialUpgrade (entry onto a Phase 5 run) shows no outcome line, matching what real entry does"
metrics:
  duration: 25min
  completed: 2026-10-04
  tasks: 2
  files: 3
---

# Phase 6 Plan 22: Generate Corpus Phase 6 Layout Summary

The Generate Corpus dashboard now shows the Phase 6 run: seven tiles, a Stages grid with Non-relevant mail and Attachments for written threads rows, one banner slot (partial-run or media), a Retry Failed Items header button, and a Ready card with its own estimate line and Warning media notice. All of it is verified at 144 DPI at Large and WizardMin.

## What Was Built

- **Tiles:** `ApplyProgressTiles()` applies the rule-19 variant fallback to Relevant, Non-relevant and Attachments, each on its own. The tiles already used the new `GenerationTileKind` order from 06-13.
- **Stages card:** caption `Stages`, the new hint, and a `Done` column instead of `Threads`.
  - `ApplyWaves` fills Dates, Done and the tooltip for every row from `GenerationUiText.StageRowText`. Stage rows leave the Wave cell blank.
  - Warnings and Failed count threads plus attachments on wave and Non-relevant rows, and attachments only on the repair row. Both cells are blank at 0.
  - Dot colors (`StageColor`): running Accent, any failure Danger, pending BorderStrong, warnings or skipped Warning, otherwise Success.
- **Banner slot:** `SetBanner` fills the single Phase 3 banner.
  - `ShowPartialBanner` and `ShowDashboard(banner: true)` show the partial-run banner with `BannerHeading(runEnded)` and the five-argument `BannerBody`.
  - `ShowMediaBanner` shows the media banner. It is ignored while a partial banner shows, and it hides Retry Failed Items when it has a button.
  - With no button, the button panel is hidden and the text uses the full width. The button sizes to its text through `FitToText`.
- **Header:** `Retry Failed Items` has the `RetryFailedTooltip`. It shows when failed **items** (threads plus attachments) exist and no banner button takes its place.
- **Ready card:** two new labels under the body, both max width 520 with UseMnemonic off:
  - the estimate line in `SmallFont` / `TextMuted`
  - the media notice in `SmallFont` / `Theme.Warning`

  Margins follow the spec: body bottom 8 when a line follows, estimate bottom 4 before the notice, and the last line bottom 20.
- **Render states:** seven new `GenerateCorpusRenderState` values, plus the `ready`, `media` and `runEnded` overrides on `LoadForRender`.

## Tests

`GenerateCorpusRenderTests` now has 17 render facts (9 new) plus 4 entry tests, and all pass at both sizes.

- **New facts:**
  - ReadyWithMediaEstimate and ReadyMediaUnavailableNotice
  - RunningNonRelevantStage: bar at 70%, the Non-relevant row running and in view, and attachment retry, kept, failed, skipped and media-stopped activity rows
  - PartialUpgradeBanner: "Part of the corpus is not written yet." with an Attachments for written threads row
  - FinishedSkippedMedia, FinishedSkippedMediaWithFailures and FinishedSkippedMediaUnavailable
  - FinishedCleanTiles: counts up to 9,999
  - FinishedWithFailuresHeaderButton
- **Checks the shared harness runs on every fact at both sizes:**
  - no clipping
  - no horizontal scrollbar in either grid
  - the state card content fits inside the card
  - the visible Primary count equals the state's expected count from the table, and is never more than 1
  - no visible button text is ellipsized
  - for dashboards: tiles have equal height, tile values 1-3 never ellipsize, and Done cells are never cut
- **Phase 5 facts updated:** the Ready estimate now sits in its own label, and Primary counts were added. The entry test reads the `Done` column.
- **PNG inspection:** I inspected the 144-DPI PNGs for ready-media-unavailable, running-non-relevant (both sizes), finished-skipped-media-failures, finished-skipped-media-unavailable, partial-upgrade and finished-failures-header. There was no clipping, one Primary button per state, the banner text wraps beside the button or spans the full width, and the dots are centered. At WizardMin the Dates cells ellipsize, which DPI rule 22 allows.

## Commits

| Task | Commit | Message |
| ---- | ------ | ------- |
| 1 | 37de3c3 | feat(06-22): Stages grid, media banner, Retry Failed Items and Ready card estimate lines |
| 2 (fix found in render review) | 2f580b2 | fix(06-22): fit Done cells to 9,999 / 9,999 and keep the running stage row in view |
| 2 | 4352ba7 | test(06-22): 144-DPI render checks for Phase 6 Generate Corpus states |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Done cells ellipsized at Large for 5K-run counts**
- **Found during:** Task 2 PNG inspection
- **Issue:** The spec's 80-wide Done column showed `2,240 / 3...` at Large. The clipping detector does not inspect grid cells.
- **Fix:** The Done column width is at least the measured width of `9,999 / 9,999` plus the 15 right inset and 8, the same approach as the Time column. A new harness check makes sure no Done cell is cut. Dates still keeps at least 120 at WizardMin.
- **Files modified:** UserControls/StepGenerateCorpus.cs, ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
- **Commit:** 2f580b2

**2. [Rule 2 - Missing functionality] The running stage row was never scrolled into view**
- **Found during:** Task 2 PNG inspection
- **Issue:** The 05-UI-SPEC S1 rule ("running wave row scrolled into view unless the user scrolled in the last 5 s") was never implemented. The running Non-relevant mail row, which comes after 14 wave rows, was hidden below the fold.
- **Fix:** `ScrollRunningStageIntoView` runs from `ApplyWaves`, the grid's `SizeChanged` and `VisibleChanged`. It skips the scroll if the user scrolled within the last 5 seconds, and a flag keeps programmatic scrolls from counting as user scrolls.
- **Commit:** 2f580b2

**3. [Rule 3 - Blocking] ShowDashboard signature change**
- `ShowDashboard` now takes `bool runEnded` and the attachment counts instead of a heading string. The entry call site in `StepGenerateCorpus.Run.cs` passes `input.LatestAttemptEnded`, which keeps the 06-13 Finished-outcome filter.
- **Commit:** 37de3c3

### Acceptance-criterion notes
- `grep -c "Assert.Empty(result.Clipping)"` returns 1, not >= 12. The assertion lives once in the shared `RenderBoth` harness, which runs it for each of the 17 facts at both sizes (34 renders). I did not copy it into each fact.
- The plan mentions `public event EventHandler? MediaBannerButtonClicked` "/ reuse the existing Continue handler". I chose to reuse the handler: the media banner button is the same control.

## Known Stubs

The run code still passes Phase 5 inputs; 06-23 wires the real values:
- `StepGenerateCorpus.Run.cs` entry and after-run `ShowDashboard` calls pass `counts.FailedThreads`, with no attachment counts and no media banner.
- `ReadyCard(plan)` passes relevant-only numbers with one model and no media.
- `ApplyView` still sets the bar over threads only; 06-23 moves it to all work units.

Render states use explicit `ready` / `media` fixtures, so the layout is fully exercised.

## Threat Flags

None. T-06-66 holds: the new estimate, notice and banner labels set `UseMnemonic = false`, and Dates cells carry the full text in the tooltip.

## Full Suite

`dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`: **3,180 of 3,180 passed**, 0 failed, 0 skipped (7 m 17 s). The baseline was 3,171; the 9 new render facts account for the difference. The known `GenerationResetTests` timing flake did not occur.

## Self-Check: PASSED

- FOUND: UserControls/StepGenerateCorpus.cs, ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs, 06-22-SUMMARY.md
- FOUND commits: 37de3c3, 2f580b2, 4352ba7
- No file deletions in any commit
- App build: 0 warnings, 0 errors
- PNGs exist under ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/ for every new state at 1100x750 and 1000x540
