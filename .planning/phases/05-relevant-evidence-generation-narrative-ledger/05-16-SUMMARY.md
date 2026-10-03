---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 16
subsystem: case-summary-ui
tags: [winforms, case-summary, story-so-far, mailboxes, replan-dialog, render-tests]
requires:
  - phase: 05-06
    provides: GenerationUiText.MailboxesRow / StoryRow / AnswerKeyRunClause / StoryMissingText
  - phase: 05-11
    provides: GenerationOutputs.WriteStorySoFarAsync, GenerationRunState, PeopleDirectory
  - phase: 05-15
    provides: CaseStage.Generation mapping, NavigateToStage, GenerationState run counts in case.json
provides:
  - Case Summary Mailboxes row (counts, Open Mailboxes Folder, Generate Emails)
  - Case Summary Story so far row (Open in Word, Export Story So Far from the ledger in case.json)
  - Answer key run clause "· {g} generated, {p} planned"
  - ReplanBody(approved, generatedThreads) and the D-RP generated-threads warning
affects: [05-17]
tech-stack:
  added: []
  patterns:
    - "Case Summary rows map SummaryRowContent tones to Theme colors via one ApplySummaryRow helper"
    - "Shell-open test seams (OpenFile / OpenFolder) on StepCaseSummary, like StepGenerateCorpus"
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/CaseSummaryGenerationTests.cs
  modified:
    - UserControls/StepCaseSummary.cs
    - UserControls/GenerationUiText.cs
    - UserControls/CorpusUiText.cs
    - Forms/ReplanDialog.cs
    - UserControls/StepReviewPlan.Run.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
    - ReelDiscovery.Tests/Render/DialogRenderTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusUiTextTests.cs
decisions:
  - "[Phase 05] Export Story So Far is offered only for a run on the current plan; a ledger from an earlier plan is cleared by Start Over on Generate Corpus, not exported against the new plan"
  - "[Phase 05] The answer-key run clause replaces line 3 (it already starts with 'Written {time}') and appears only when the key is Written for the current plan and the run has generated something"
  - "[Phase 05] Mailboxes 'generated' = any written, warning or failed thread or any written file in the cached run counts"
metrics:
  duration: ~20min
  completed: 2026-10-03
  tasks: 2
  files: 9
---

# Phase 5 Plan 16: Case Summary Mailboxes and Story So Far Rows Summary

Case Summary now shows what Generate Corpus produced, in two new Corpus rows. The Mailboxes row shows the EML and custodian counts and the written/partial/not generated/earlier plan state, with Open Mailboxes Folder and Generate Emails. The Story so far row has Open in Word and Export Story So Far, which rewrites the .docx from the ledger saved in case.json. The Answer key row gains a generated/planned clause. The re-plan dialog now warns that threads already generated for the plan will no longer match it.

## What Was Built

**Task 1: Case Summary rows (`StepCaseSummary.cs`)**
- Two new rows after Review protocol: `Mailboxes` (Open Mailboxes Folder, Generate Emails) and `Story so far` (Open in Word, Export Story So Far).
- `RefreshGenerationRows` builds `MailboxesRowInput` and `StoryRowInput` from `Corpus.Generation` and passes them to `GenerationUiText.MailboxesRow` / `StoryRow`. `ApplySummaryRow` maps the tones: Success/Warning/Danger, with TextMuted for line 3 and BorderStrong for the dot. Thread count = distinct `SourceThreadId` in the ledger.
- Answer key line 3 = `AnswerKeyRunClause(written, EmailsWritten, max(0, EmailCount + AttachmentCount - EmailsWritten - EmailsFailed))` when a run exists for the current plan.
- `MailboxesPath(caseFolder)` uses `GetFullPath` to confirm the result is exactly `{case folder}\Mailboxes`, otherwise null (T-05-52). `OpenMailboxesFolder` opens only that path.
- `OpenStory` builds the path from the verified case folder plus `StorySoFarDocxBuilder.FileNameFor(folder name)`. If the file is missing it refreshes the row and shows `StoryMissingText` in Danger.
- `ExportStoryAsync` runs off the UI thread (`Task.Run`): journal replay, `GenerationRunState.From`, `PeopleDirectory.Create`, then `GenerationOutputs.WriteStorySoFarAsync` with the saved ledger. After that it calls `RecordPartialWrites`, sets or clears `StoryError` and saves. Every await is followed by an `IsDisposed` check. Status copy is new in `GenerationUiText`: StoryExportingText, StoryExportedText, StoryLockedText (Warning) and StoryExportFailedText (Danger).
- Generate Emails calls `NavigateToStage(CaseStage.Generation)`.

**Task 2: Re-plan warning**
- `CorpusUiText.ReplanBody(bool approved, int generatedThreads = 0)` appends ` {Plural(n, "thread")} already generated for this plan will no longer match it. After re-planning, Generate Corpus asks you to start over.` when n > 0.
- `ReplanDialog(bool approved, int generatedThreads = 0)`. `StepReviewPlan.ReplanGeneratedThreads(corpus)` returns ThreadsWritten + ThreadsWithWarnings when `RunPlanId == PlanId`, otherwise 0.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Export Story So Far hidden for an earlier plan's ledger**
- **Found during:** Task 1
- **Issue:** The export needs the current plan, but a ledger from an earlier run would be exported against a plan it does not belong to.
- **Fix:** The Export button shows only when `RunPlanId` equals the current plan id, and `ExportStoryAsync` checks this again.
- **Commit:** c62db43

### Other adjustments
- I added the story-export status copy to `GenerationUiText.cs` (not in files_modified). The existing Case Guide export texts say "Export Again", which is the wrong button for this row.
- The run clause replaces line 3 instead of being appended to it. `AnswerKeyRunClause` already begins with `Written {time}`, so appending would print the time twice.
- `LoadForRender` needed no change. The render tests seed `Corpus.Generation` on the draft before binding.
- I added test seams (`OpenFile`, `OpenFolder`) and internal accessors (row labels, dot colors, buttons, `StatusLabel`), matching the StepGenerateCorpus pattern.
- `StepReviewPlan.ReplanGeneratedThreads` is a static helper so the count can be tested without the step.

## Inspected PNGs (144 DPI)

- `case-summary-run-partial-1100x750`: the Mailboxes dot and line are amber ("7 of 10 relevant threads written · 1 with warnings · 2 failed") and Open Mailboxes Folder shows. Story so far is red: "Could not write: ..." with only Export Story So Far showing. The Answer key line reads "Written Sep 3, 2024 10:30 AM · 18 generated, 0 planned". Nothing is clipped and there is no horizontal scrollbar.
- `case-summary-run-complete-1000x540`: scrolled to the Corpus group. Both rows are green, Open in Word and Export Story So Far sit side by side, and no button text is ellipsized.
- `case-summary-run-notgenerated-1000x540`: grey dots, "Not generated yet", only Generate Emails shows, and Story so far has no buttons.
- `case-summary-run-earlierplan-1000x540`: amber "Written for an earlier plan. Start over on Generate Corpus.", Open Mailboxes Folder, and Story so far with Open in Word only.
- `replan-dialog-generated`: the extra sentence wraps inside the body, and Cancel / Re-plan Corpus are fully visible.

## Verification

- `--filter "FullyQualifiedName~CaseSummaryRenderTests|FullyQualifiedName~CaseSummaryGenerationTests"`: 14 passed
- `--filter "FullyQualifiedName~CorpusUiTextTests|FullyQualifiedName~DialogRenderTests|FullyQualifiedName~CorpusControlsRenderTests"`: 82 passed
- `--filter "Category!=Render"`: 2561 passed, 0 failed
- `--filter "Category=Render"`: 101 passed, 0 failed
- Build: only the 2 existing xUnit1031 warnings in GenerateCorpusStepTests
- Acceptance greps: "Open Mailboxes Folder" = 1, "Export Story So Far" = 1, MailboxesRow|StoryRow = 2, `NavigateToStage(CaseStage.Generation)` = 1, `WriteStorySoFarAsync` = 1, the ReplanBody signature = 1, the warning sentence = 1, generatedThreads in ReplanDialog = 5

## TDD Gate Compliance

- Task 1: RED 8fafd3d, then GREEN c62db43
- Task 2: RED 6c57f57, then GREEN 666261c

## Requirements

CORP-11 was already Complete; this plan adds the Case Summary open and re-export path. OUT-01 stays Pending, as decided in 05-13: relevant mail lands in every custodian mailbox, but non-relevant mail is still to come.

## Known Stubs

None.

## Self-Check: PASSED
