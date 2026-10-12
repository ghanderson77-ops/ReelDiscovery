---
phase: 10-answer-key-batch-safety
plan: 08
subsystem: ui/answer-key
tags: [answer-key, relabel, overlay, backup, D-10, D-12]
requires:
  - "10-03/10-05 (AnswerKeyBackup, GenerationFileResult.Backups, ThreadInvalidationResult.KeyBackups, ApprovalResult.Backups, CorpusApproval.ApproveAsync overlay parameter)"
  - "10-07 (StepGenerateCorpus run-start resolution guards, kept intact)"
provides:
  - "GenerationUiText.AnswerKeyBackupText(fileName, backupFileName)"
  - "StepGenerateCorpus.ShowKeyBackups: backup warning at entry, checkpoints, leave retry and Start Over"
  - "QA findings footer backup line after Regenerate Thread"
  - "StepReviewPlan.ApprovalOverlay(folder, plan, guide) and overlay passed on approval"
affects:
  - "Re-approving the same plan (e.g. after Keep This Plan) keeps Generated/Failed/Skipped statuses and manual relabels"
tech-stack:
  added: []
  patterns:
    - "Backup notice is a status/footer warning only; it never writes Generation.KeyError, so it never triggers the leave retry"
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - Forms/QaFindingsDialog.cs
    - UserControls/StepReviewPlan.Run.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs
    - ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs
decisions:
  - "[Phase 10] A damaged-key backup notice shows only when the key write succeeded; at a checkpoint it is shown before the story-file error so a later write failure still wins the status line"
  - "[Phase 10] Review Plan approval computes the run-state overlay off the UI thread from a readable journal of the same PlanId only; otherwise the plain plan key is written"
metrics:
  duration: "~25 min"
  completed: 2026-10-12
  tasks: 2
  files: 7
---

# Phase 10 Plan 08: Approval overlay and answer-key backup notice Summary

Review Plan re-approval now passes the journal's run-state overlay to `CorpusApproval.ApproveAsync`, so the answer key keeps run statuses and relabels. A damaged key file that was moved aside is reported as a warning that names both files: on Generate Corpus, in the QA findings footer and on Review Plan. It never sets KeyError.

## Tasks

| Task | Name | Commit |
| ---- | ---- | ------ |
| 1 | Backup notice copy, surfaced on Generate Corpus and in the QA footer (D-10) | a551b3e |
| 2 | Review Plan passes the run-state overlay on re-approval (D-12) | d3e71fe |

## What was built

- `GenerationUiText.AnswerKeyBackupText`: "{file} could not be read, so it was kept as "{backup}" and a new answer key was written. Copy any relabels from the backup into the Relevance column."
- `StepGenerateCorpus.ShowKeyBackups` is called on step entry (after `ShowFileError(data.KeyResult)`), when a checkpoint is applied, on the leave retry (after the `KeyError` assignment) and in the Start Over status chain. The Start Over order is: reset errors, key write failure, cancelFailed, key backup, success. The 10-07 `_isResolvingBatches` / `_stopAfterResolve` guards are unchanged.
- In the QA findings dialog, `result.KeyBackups` adds a footer line after the key-not-updated line. Because it adds a line, the footer shows in the Warning color.
- `StepReviewPlan.ApprovalOverlay` returns null when the journal is missing, unreadable or belongs to another plan (PlanChanged), or when there has been no run. Otherwise it returns `GenerationRunState.BuildFullOverlay`. `ApproveAsync` runs it in `Task.Run`, checks `IsDisposed`, then calls `CorpusApproval.ApproveAsync(folder, Draft!, plan, overlay: overlay)`. On success, if `result.Backups` is non-empty, the approved status also shows the backup notice in the Warning color.

## Acceptance counts

- `AnswerKeyBackupText(string fileName, string backupFileName)` in GenerationUiText.cs: 1
- `ShowKeyBackups(` in StepGenerateCorpus.Run.cs: 4 calls plus the definition (grep count 5)
- `KeyError` in StepGenerateCorpus.Run.cs: 5 before, 5 after (unchanged)
- `result.KeyBackups` in QaFindingsDialog.cs: 1 or more
- `ApprovalOverlay(string folder, CorpusPlan plan, CaseGuide guide)`: 1; `CorpusApproval.ApproveAsync(folder, Draft!, plan, overlay: overlay)`: 1
- ReviewPlanLogicTests: 4 `ApprovalOverlay_*` tests (NoJournal, SamePlanJournal, OtherPlanJournal, UnreadableJournal) and `Reapprove_KeepsRelabelAndStatus`
- Services/Corpus still has no reference to Services/Evidence

## Verification

- `dotnet build ReelDiscovery.Tests -warnaserror`: 0 warnings, 0 errors after each task
- The filter `GenerationUiTextTests|QaFindingsDialogTests|GenerateCorpusStepTests` passed 358/358
- The filter `ReviewPlanLogicTests|CorpusApprovalTests` passed 94/94

## Deviations from Plan

**1. [Rule 1 - Ordering] Checkpoint backup notice comes before the story-file error.** At a checkpoint, `ShowKeyBackups(checkpoint.KeyResult)` runs between `ShowFileError(KeyResult)` and `ShowFileError(StoryResult)`. If it ran last, a story-file write failure could be overwritten by the backup warning, which goes against "a write failure shown by ShowFileError wins".

**2. [Rule 2] Added `ApprovalOverlay_UnreadableJournal_Null`.** The plan's behavior covers an unreadable journal, but its test list did not include one. The test uses `GenerationJournal.SizeCapOverride` (AsyncLocal, reset in finally).

**TDD note:** Both tasks are tdd="true", but each was committed as one feat commit: implementation and tests together. There is no separate RED commit. Every behavior has a test that passes.

## UI text to check at 150% DPI

This machine renders at 120 DPI, so the new text has not been checked at 150%:
- Generate Corpus status line: the AnswerKeyBackupText sentence, which is long and is also appended to StartedOverText after Start Over
- QA findings footer: an additional line
- Review Plan status: ApprovedText plus the backup sentence

All three need a check for wrapping and clipping at 150%.

## Known Stubs

None.

## Threat Flags

None. T-10-29 is mitigated: the overlay is used only from a Readable journal with PlanChanged false. T-10-30 is accepted.

## Self-Check: PASSED
