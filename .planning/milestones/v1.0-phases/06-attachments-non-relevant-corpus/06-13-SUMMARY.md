---
phase: 06-attachments-non-relevant-corpus
plan: 13
subsystem: generation-ui-copy
tags: [ui-copy, generate-corpus, case-summary, tiles, banners, ready-card, pure-functions]
requires:
  - "06-02 GenerationCounts split, attachment counters, StageRowKind, new GenerationEventKind values"
  - "JournalReplay.LatestAttemptEnded"
provides:
  - "GenerationTileKind { Relevant, NonRelevant, Attachments, Emails, Warnings, Failed, Cost } with per-tile rule-19 variants"
  - "GenerationUiText.StageRowText(WaveProgress) for wave, Non-relevant mail and Attachments for written threads rows"
  - "ActivityText for every Phase 6 event kind; ProgressText for SideProjects / NonRelevant / Attachments stages"
  - "OutcomeText item-based (warnings, failed items, skipped media) with optional retryButtonShown"
  - "BannerHeading(bool runEnded), EntryBannerHeading(input), BannerBody(td, tt, ad, at, failed)"
  - "MediaBanner(skipped, failed, available, reason) -> MediaBannerContent"
  - "ReadyCard(GenerationReadyInput) with EstimateLine and MediaNotice; RetryFailedButtonText / RetryFailedTooltip"
  - "CG1CancelGeneration(threads, attachments), Phase 6 CG2/CG3, MailboxesRow line 3 + Line3Tooltip, AnswerKeyRunTooltip"
  - "GenerationView.StageAttachments"
affects: [06-22 layout, 06-23 run wiring, 06-24 Case Summary]
tech-stack:
  added: []
  patterns:
    - "Zero clauses are passed as empty strings to a Join helper; lists of 3+ clauses join with ', ' and a final ' and '"
    - "Item counts (threads + attachments) for Warnings / Failed; Skipped is never Failed"
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/StepGenerateCorpus.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - UserControls/StepCaseSummary.cs
    - Models/Evidence/GenerationProgress.cs
    - Services/Evidence/GenerationRunState.cs
    - Services/Evidence/GenerationHistory.cs
    - Services/Evidence/ThreadUnitOfWork.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
decisions:
  - "GenerationEntryInput.LatestAttemptEnded means the newest attempt ended with outcome Finished; the call site combines JournalReplay.LatestAttemptEnded with LastRunEnded.Outcome == Finished, because cancelled and fatal runs also journal RunEnded but must read 'The last run did not finish.'"
  - "ThreadWritten shows the ledger clause only when GenerationEvent.Relevant is true; ThreadUnitOfWork and GenerationHistory now set Relevant from the thread's relevance"
  - "ToCounts fills RelevantTotal/RelevantDone from the relevant-only run state so the Relevant tile is correct before 06-23"
  - "Ready card estimate and media notice are appended to the body label until 06-22 adds their own Small labels"
  - "Both-zero Warnings/Failed tooltips use 'Threads and attachments that ...' so the tile still explains itself"
  - "MailboxesRowInput replaces Total/Written/Warnings/Failed with the Relevant/NonRelevant/Attachment/Item fields"
metrics:
  duration: 25min
  completed: 2026-10-04
  tasks: 2
  files: 12
---

# Phase 6 Plan 13: Phase 6 Run-Screen and Summary Copy Summary

Every Phase 6 Generate Corpus and Case Summary string from 06-UI-SPEC S1-S3 now comes from pure, unit-tested `GenerationUiText` functions. That covers the seven new tiles, stage rows, the new activity events, the stage progress lines, item-based outcomes, the two partial-run headings, the media banner, the Ready card with its Story / Volume / media estimate, the confirmations and the Mailboxes row. The call sites were updated just enough to keep the app building and the existing step and render tests passing.

## What Was Built

- **Tiles:** Relevant, Non-relevant, Attachments, Emails, Warnings, Failed and Cost, each with the S1 value, sub-line and tooltip.
  - The Ledger tile is gone. Its count now appears in the Relevant tooltip.
  - The rule-19 variants (`3 / 12`, `3/12`, `3` + `of 12`) apply to each of tiles 1-3 independently. `StepGenerateCorpus.ApplyFractionTile(index)` fits each one separately.
  - Warnings and Failed count items (threads plus attachments). A zero clause is dropped together with its "and".
  - The Cost tooltip splits text and media cost only when media is planned.
- **Stage rows:** `StageRowText` returns the Dates, Done and tooltip text for wave rows, the `Non-relevant mail` row and the `Attachments for written threads` row. The counting rule is documented in a comment.
  - The repair row counts repairs only.
  - Wave rows keep the dates line first in the tooltip.
  - `ApplyWaves` now uses `StageRowText`.
- **Activity:** new texts and tones for 11 event kinds:
  - side projects ready
  - non-relevant start and finish
  - attachment retry, kept, failed and skipped
  - media stopped
  - repair started
  - attachments added
  - attachment regenerated
  - Run started, continued and cancelled also gain attachment clauses. Non-relevant `ThreadWritten` rows have no ledger clause.
- **Progress:**
  - The Writing stage adds an attachment clause from the new `GenerationView.StageAttachments`.
  - New lines for the SideProjects, NonRelevant (from Counts) and Attachments (from the repair row) stages.
  - New Stopping text.
- **Outcomes:** a clean finish carries the attachment clause. A finish with problems joins warnings, failed items and skipped media with commas and a final "and". The Retry tail appears only when the header button shows. The Cancelled line carries the attachment progress, and the Fatal and Started-over texts are updated.
- **Entry and banners:**
  - Entry is unfinished when any thread or attachment lacks a final record.
  - `BannerHeading(runEnded)` / `EntryBannerHeading(input)` pick the heading; `BannerBody` takes five arguments.
  - `MediaBanner` shows `Create Skipped Media`, `Retry Failed and Skipped Items`, or no button.
- **Ready card:** `ReadyCard(GenerationReadyInput)` returns the body in run order, `EstimateLine` (one model name or a Story / Volume split, plus a media clause) and the Warning `MediaNotice`. The Ready branch of `StateCard` delegates to it.
- **Confirmations and summary:**
  - `Retry Failed Items` text and tooltip (the header button already uses the text).
  - CG1 with an attachment clause and correct verb agreement; Phase 6 copy for CG2 and CG3.
  - `MailboxesRow` line 3 now uses item counts, and a new `Line3Tooltip` carries the full breakdown. Case Summary fills it from GenerationState and sets the tooltip.
  - `AnswerKeyRunTooltip(skipped)`.

## Tests

- `GenerationUiTextTests` has 143 test methods (222 cases), up from 83 methods before the plan.
  - Every new plural template is tested at n = 0, 1, 2 and 1,000.
  - The dropped-clause variants are covered: no attachments, no non-relevant threads, no media.
  - A banned-word scan ("evidence", "noise", "filler") runs over every activity kind, every outcome, every tile and stage row, and every banner, card and confirmation.
- Updated `GenerateCorpusStepTests`, `GenerateCorpusRenderTests` and `CaseSummaryRenderTests` for the new Ready heading, estimate wording, `BannerHeading(false)`, the five-argument `BannerBody` and the item-based Mailboxes line 3.
- Filtered runs: GenerationUiTextTests, GenerateCorpusStepTests, CaseSummaryGenerationTests and GenerateCorpusRender all pass (247).
- Full suite: 3,048 of 3,050 passed (baseline 2,968). The 2 failures were `CaseSummaryRenderTests.Summary_GeneratedCorpusRows` (Complete, Partial), which still expected the Phase 5 Mailboxes copy. After updating them (0da21c2), `CaseSummaryRenderTests` passes 9 of 9.

## Commits

| Task | Gate | Commit | Message |
| ---- | ---- | ------ | ------- |
| 1 | RED | f14109f | test(06-13): add failing tests for Phase 6 tiles, stage rows, activity, progress and outcome copy |
| 1 | GREEN | fb6ebab | feat(06-13): Phase 6 tiles, stage rows, activity, progress and outcome copy |
| 2 | RED | 214540a | test(06-13): add failing tests for entry, banners, Ready card, confirmations and Mailboxes row copy |
| 2 | GREEN | 7681de5 | feat(06-13): entry, banners, Ready card, confirmations and Case Summary copy |
| 2 | test update | 0da21c2 | test(06-13): update Case Summary render expectations to Phase 6 Mailboxes copy |

## TDD Gate Compliance

Each task has a RED test commit followed by a GREEN feat commit. Both RED commits failed at compile time because the new members did not exist yet. No refactor commits were needed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The banner heading would have said "Part of the corpus is not written yet." after a cancelled run**
- **Found during:** Task 2
- **Issue:** Cancelled, fatal and internal-error runs all journal a RunEnded record. Using `JournalReplay.LatestAttemptEnded` alone would show the "ended" heading on reentry after a cancel, which contradicts the UI-SPEC Row 2 table ("cancelled, crashed, fatal" → "The last run did not finish.").
- **Fix:** The entry call site sets `LatestAttemptEnded = replay.LatestAttemptEnded && replay.LastRunEnded?.Outcome == Finished`, and the field comment documents this. **06-23 should keep this outcome filter** rather than use `replay.LatestAttemptEnded` directly.
- **Files modified:** UserControls/StepGenerateCorpus.Run.cs, UserControls/GenerationUiText.cs
- **Commit:** 7681de5

**2. [Rule 3 - Blocking] The Relevant tile would have shown 0 / 0**
- **Found during:** Task 1
- **Issue:** `RelevantTotal` and `RelevantDone` were never set; `GenerationRunState.ToCounts` only filled the shared thread counts.
- **Fix:** `ToCounts` fills them from the run state, which tracks relevant threads only.
- **Files modified:** Services/Evidence/GenerationRunState.cs
- **Commit:** fb6ebab

**3. [Rule 3 - Blocking] Relevant thread rows would have lost their ledger clause**
- **Found during:** Task 1
- **Issue:** `GenerationEvent.Relevant` defaults to false and nothing set it, so the spec'd "no ledger clause when not relevant" rule would have stripped the clause from every current row.
- **Fix:** `ThreadUnitOfWork` and `GenerationHistory` now set `Relevant` from the thread's relevance.
- **Files modified:** Services/Evidence/ThreadUnitOfWork.cs, Services/Evidence/GenerationHistory.cs, Models/Evidence/GenerationProgress.cs (comment)
- **Commit:** fb6ebab

**4. [Rule 3 - Blocking] The Writing progress line had no attachment count to read**
- **Found during:** Task 1
- **Issue:** `GenerationView` had no field for the attachments in the current wave step.
- **Fix:** Added `GenerationView.StageAttachments`. It stays 0, which drops the clause, until the runner sets it.
- **Files modified:** Models/Evidence/GenerationProgress.cs
- **Commit:** fb6ebab

**5. [Rule 2 - Missing functionality] The Ready card estimate would have disappeared before 06-22**
- **Found during:** Task 2
- **Issue:** The estimate moved out of `Body` into `EstimateLine`, and nothing displays that field yet.
- **Fix:** `ShowStateCard` appends `EstimateLine` and `MediaNotice` to the body label, separated by blank lines, until 06-22 adds their own labels.
- **Files modified:** UserControls/StepGenerateCorpus.cs
- **Commit:** 7681de5

**6. [Small additions beyond the plan's mechanical scope]**
- `OutcomeText` gained an optional `retryButtonShown = true` parameter, needed for the S1 tail rule.
- `WaveTooltip` gained an optional attachments parameter.
- The header button text now uses `RetryFailedButtonText` so it matches the outcome copy.
- `ShowDashboard` gained an optional `bannerHeading` parameter.
- The scope notice now counts non-relevant **threads** instead of emails, per the new template.

### Notes

- Acceptance check `grep -v "^\s*//" UserControls/GenerationUiText.cs | grep -ci "evidence\|noise\|filler"` returns 1. The match is the `using ReelDiscovery.Models.Evidence;` namespace import, not user-facing copy. The banned-word unit tests check every rendered string.
- `MailboxesRowInput` no longer has `Total`, `Written`, `Warnings` or `Failed`. `StepCaseSummary` is the only producer and has been updated.

## Known Stubs

These counts are wired to 0 by design here; their wiring is planned in 06-23:
- `UserControls/StepGenerateCorpus.cs` `ShowDashboard`: `BannerBody(done, total, 0, 0, failed)`, attachment counts are 0.
- `UserControls/StepGenerateCorpus.Run.cs` `RequestCancel`: `CG1CancelGeneration(done, 0)`.
- The entry decision's `AttachmentsTotal` / `AttachmentsFinal` are not filled yet, so they stay 0.
- `ReadyCard(plan)` passes relevant-only numbers with one model and no media.

## Threat Flags

None. T-06-39 holds: every function returns plain text, and the labels showing model text keep UseMnemonic = false (banner and state labels are unchanged).

## Self-Check: PASSED

- FOUND: UserControls/GenerationUiText.cs, 06-13-SUMMARY.md
- FOUND commits: f14109f, fb6ebab, 214540a, 7681de5, 0da21c2
- No unexpected file deletions in any commit
- App build: 0 warnings
