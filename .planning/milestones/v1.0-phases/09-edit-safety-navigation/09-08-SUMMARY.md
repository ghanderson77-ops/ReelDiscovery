---
phase: 09-edit-safety-navigation
plan: 08
subsystem: discovery-documents
tags: [discovery, staleness, exhibits, case-summary, render]
requires:
  - phase: 09-02
    provides: "StaleSource.Exhibits, DiscoveryDocument.ExhibitStamps, DiscoveryCorpus.WrittenStamps, DiscoveryStaleness.EmailStamps/Check(currentStamps)"
  - phase: 09-05
    provides: "Start Over / Regenerate Thread flows whose journal changes this plan surfaces"
provides:
  - "Exhibit staleness (Out of date) on the Discovery Documents step and in the Case Summary discovery row"
  - "Exhibits Out of date status copy"
affects: [discovery-documents, case-summary]
tech-stack:
  added: []
  patterns:
    - "Current stamps threaded as an optional trailing IReadOnlyDictionary parameter; null = unknown skips the check"
key-files:
  created: []
  modified:
    - UserControls/DiscoveryStepModel.cs
    - UserControls/DiscoveryUiText.cs
    - UserControls/StepCaseSummary.cs
    - UserControls/StepDiscoveryDocuments.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryStepModelTests.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryUiTextTests.cs
    - ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs
decisions:
  - "[09-08]: Case Summary stamps come from the qa.Replay that LoadQa already performed: a missing journal (Start Over) counts as known-empty, unreadable or other-plan journals count as unknown (null), and a null Replay (no plan or folder, or a replay that threw) counts as unknown"
  - "[09-08]: StaleWhat(Exhibits) = 'a cited email' keeps the switch total; RowStatusFull uses the dedicated Exhibits sentence"
metrics:
  duration: 15min
  completed: 2026-10-11
---

# Phase 9 Plan 08: Exhibit Staleness Surfacing Summary

After Start Over or Regenerate Thread, saved depositions and written-discovery sets that cite a changed or deleted email now show "Out of date" on the Discovery Documents step and in the Case Summary discovery row. The check uses the journal's ThreadDone stamps, and the row shows the dedicated Exhibits status text.

## What Was Built

- **DiscoveryStepModel**: `Build` passes `corpus?.WrittenStamps` into `BuildRows`. Before the corpus probe finishes this is null, so the exhibit check is skipped and no row flickers to Out of date. `SavedRows` takes a new trailing optional `currentStamps`. `BuildRows` and `Row` pass it on to `DiscoveryStaleness.Check(doc, draft, currentStamps)`. Both never-throw `catch (Exception)` blocks are unchanged.
- **DiscoveryUiText**: when `row.Stale == StaleSource.Exhibits`, `RowStatusFull` returns "Out of date: an email it cites was regenerated or deleted after this document was written. Click Regenerate... to write it again." `StaleWhat(Exhibits)` returns "a cited email". The text for Guide, Core, Caption and CorpusPlan is unchanged.
- **StepCaseSummary.LoadQaWithDiscovery**: builds the stamps from the replay `LoadQa` already performed. `GenerationJournal.Replay(` still occurs 4 times in the file, the same as before this plan. The method is now `internal` so tests can call it.
- **StepDiscoveryDocuments**: new `internal string DetailTooltipText` accessor, used only by the render test.

## QaSnapshot.Replay null check (flagged in PATTERNS.md)

`LoadQa` sets `qa.Replay` whenever the folder is non-empty and the plan is non-null. `GenerationJournal.Replay` returns `Exists = false` when there is no journal and does not return null. So `qa.Replay` is null only when there is no plan, no folder, or the replay threw (caught by `LoadQa`'s try). All three cases are treated as unknown. The plan's snippet works as written, and a comment in the code records this reasoning.

## Tests

- `DiscoveryUiTextTests.RowStatusFull_OutOfDate_Exhibits`
- `DiscoveryStepModelTests` (new `09 W3: exhibit staleness` section), all through the real services with no changes to `GenerationReset` / `ThreadInvalidation`:
  - Start Over: both citing documents go from Generated to OutOfDate/Exhibits. Unknown stamps keep them Generated. `Build` with `DiscoveryCorpus.None()` shows no OutOfDate rows.
  - Regenerate Thread T-0001: the deposition citing RD-000001 becomes OutOfDate/Exhibits while the RFA citing RD-000002 stays Generated. After a later ThreadDone rewrite of T-0001, the deposition is still OutOfDate.
  - Case Summary: `LoadQaWithDiscovery` after Start Over gives a Line3 containing " · 2 out of date" in Warning tone.
- Render: `discovery-results-exhibits-stale` at Large (1100x750) and WizardMin (1000x540).

Filtered runs: DiscoveryStepModel/UiText/Staleness/CaseSummaryQa/CaseSummaryLogic gave 249/249 passing. DiscoveryDocumentsRenderTests gave 8/8 passing. The plan's verification filter (`FullyQualifiedName~Discovery|FullyQualifiedName~CaseSummary`) gave 4550/4550 passing. Both builds passed with -warnaserror and 0 warnings.

## Render inspection

This machine renders at 120 DPI, not 144, so the PNGs are 120 DPI. In both sizes:
- The Status cell shows "Out of date" in full.
- The selected row's detail line starts "Out of date: an email it cites..." in Warning tone and is ellipsized at the card width. The tooltip carries the full text (asserted).
- Regenerate... is visible and enabled.
- No clipping, and no horizontal grid scrollbar.

**A human check at 150% scaling is still needed** (rule 48 / render check 6).

## Deviations from Plan

**1. [Rule 3 - Blocking] `LoadQaWithDiscovery` changed from private to internal.** The plan's Case Summary behavior test calls `StepCaseSummary.LoadQaWithDiscovery`, which was private. Commit 52ab8df.

**2. [Plan-sanctioned] Added the `StepDiscoveryDocuments.DetailTooltipText` internal accessor.** No accessor existed for the detail-label tooltip. Commit 931cedb.

**3. TDD gate.** Task 1's tests and implementation went in one `feat` commit, because the tests call the new `SavedRows` parameter. A tests-only commit would not compile, and this phase requires every commit to build with -warnaserror. Task 2 is a `test` commit, since the production behavior came from Task 1.

## TDD Gate Compliance

There is no separate RED commit for Task 1, for the reason given in deviation 3. The new tests could not compile against the old signature, so they could not pass before the implementation.

## Commits

- 52ab8df feat(09-08): exhibit staleness on Discovery Documents and Case Summary rows
- 931cedb test(09-08): Discovery Documents render with an Exhibits-stale row

## Self-Check: PASSED
