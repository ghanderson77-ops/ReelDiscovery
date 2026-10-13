---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 06
subsystem: generation-ui-copy
tags: [ui-copy, plurals, tiles, entry-decision, case-summary]
requires:
  - 05-01 (GenerationCounts, GenerationEvent, GenerationView, GenerationStage, GenerationOutcome)
provides:
  - UserControls/GenerationUiText (ScopeNotice, Tile, WaveDates, WaveTooltip, ActivityText, ProgressText, OutcomeText, file/status lines)
  - GenerationUiText.EntryDecision / StateCard / BannerBody / CG1..CG3 / MailboxesRow / StoryRow / AnswerKeyRunClause / StoryMissingText
  - TileTone + TileContent.Tone; StatTile colors the value label by tone
affects: [StepGenerateCorpus, StepCaseSummary, WizardForm close-during-run]
tech-stack:
  added: []
  patterns: [pure static copy helper per step (03-06 precedent), first-match entry decision, shared CaseGuideUiText.Plural]
key-files:
  created:
    - UserControls/GenerationUiText.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
  modified:
    - UserControls/CorpusUiText.cs
    - UserControls/StatTile.cs
decisions:
  - "[Phase 05] Every 'EML files' count in Phase 5 copy goes through Plural ('1 EML file'), although 05-UI-SPEC writes {files:N0} EML files"
  - "[Phase 05] Plan-changed card and C-G1 use verb agreement at n = 1 ('1 thread was written', '1 thread ... stays saved'); all other counts keep the spec wording"
  - "[Phase 05] Finished-with-problems outcome drops the warnings clause when w = 0 ('Done with 2 failed threads. ...'), mirroring the spec's drop of the failed clause at f = 0"
  - "[Phase 05] Mailboxes row is Success only when every thread is written with 0 warnings and 0 failed; all-written-with-warnings shows the Warning partial line"
  - "[Phase 05] Cost tile sub-line shows 'this case' only when Counts.Finished; partial and running runs show '≈$X to go'"
metrics:
  duration: 7min
  completed: 2026-10-03
  tasks: 2
  files: 4
---

# Phase 5 Plan 06: Generation UI Copy and Tile Tone Summary

A pure, unit-tested `GenerationUiText` helper now produces all Generate Corpus and Case Summary copy from 05-UI-SPEC: the scope notice, 7 tiles, wave dates, activity lines, progress and outcome lines, the first-match entry decision, state cards, the partial-run banner, confirmations C-G1 to C-G3, and the Mailboxes and Story so far rows. Tiles also gained a Warning/Danger value tone (U9).

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | Tile tone and run-screen copy | 5172cb9 (test), 16bb274 (feat) |
| 2 | Entry decision, state cards, banner, confirmations, Case Summary rows | ddd56f0 (test), 3abd9d9 (feat) |

## What Was Built

- **TileTone:** `enum TileTone { Normal, Warning, Danger }`, plus `TileContent.Tone` (default Normal, so Phase 4 tiles are unchanged). `StatTile.SetContent` sets `_lblValue.ForeColor` to `Theme.Warning`, `Theme.Danger` or `Theme.Text`.
- **Run-screen copy:** `GenerationTileKind` (separate from the Phase 4 `TileKind`), `ActivityTone`, `ActivityLine`, `OutcomeLine`. `Tile(kind, counts, threadsVariant)` covers all 7 tiles, including the narrow Threads variants from DPI rule 19 (`3/1,000`, or `3` with `of 1,000`). Money is `$0.00` under $100 and `$N0` from $100. `WaveDates` uses en-US and an en dash. `ActivityText` covers all 11 event kinds; its tooltip is the text plus each violation on its own line. `ProgressText` covers all 5 stages, with Idle returning "". `OutcomeText` covers Finished (clean or with problems), Cancelled, Fatal and InternalError, and InternalError never mentions API Configuration. Also added: `StoppingText`, `AnswerKeyLockedText`, `FileWriteFailedText`, `StartedOverText` and `MissingEmlText`.
- **Entry/cards/summary:** `EntryDecision` applies rules 1-7 in order. The Ready card is the fallback when there is no saved run, and nothing auto-starts. `StateCard` returns 4 cards; plan-changed and unreadable use the Danger `Start Over...` button. Also added: `BannerHeading`, `BannerButton` and `BannerBody` (with the failed clause); `CG1CancelGeneration`, `CG2StartOver` and `CG3CloseDuringRun`; `MailboxesRow` (not generated / earlier plan / complete / partial, with zero clauses dropped); `StoryRow` (error / written / not written); `AnswerKeyRunClause`; and `StoryMissingText`. Date-times use the same `MMM d, yyyy h:mm tt` invariant format as `CorpusUiText`.
- **Tests:** 140 `GenerationUiTextTests`. Every count template is checked at n = 0, 1, 2 and 1,000.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Singular grammar at n = 1**
- **Found during:** Task 1/2 plural coverage
- **Issue:** Spec templates `{files:N0} EML files`, `{Plural(n,"thread")} were written` and `... already written stay saved` read wrongly at 1.
- **Fix:** EML file counts go through Plural. The plan-changed card and C-G1 switch to "was" and "stays" at 1. Output for n != 1 matches the spec exactly.
- **Files modified:** UserControls/GenerationUiText.cs
- **Commits:** 16bb274, 3abd9d9

**2. [Rule 1 - Bug] "Done with 0 warnings and ..." when only failures exist**
- **Found during:** Task 1
- **Issue:** The spec only defines dropping the failed clause when f = 0.
- **Fix:** The warnings clause is also dropped when w = 0.
- **Commit:** 16bb274

None of the other changes affect behavior. The internal-error template is written as a literal (`internal error: {message}`) so the acceptance grep matches. `StatTile.cs` and `CorpusUiText.cs` keep CRLF line endings.

## Known Stubs

None. This is a copy-only layer; the step UI that uses it comes in later plans.

## TDD Gate Compliance

Both tasks have a RED `test(05-06)` commit followed by a GREEN `feat(05-06)` commit. No refactor commits were needed.

## Verification

- `dotnet test --filter "FullyQualifiedName~GenerationUiTextTests|FullyQualifiedName~CorpusUiTextTests"`: passed.
- `dotnet test --filter "Category!=Render"`: 2285 passed, 0 failed. Phase 4 tiles are unchanged with Tone Normal.
- `dotnet build`: no warnings.
- Acceptance greps: all pass. There are no `Color.Red/Green/Orange` references.

## Self-Check: PASSED
