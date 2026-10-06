---
phase: 06-attachments-non-relevant-corpus
fixed_at: 2026-10-06T01:29:57Z
review_path: .planning/phases/06-attachments-non-relevant-corpus/06-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 6: Code Review Fix Report

**Fixed at:** 2026-10-06T01:29:57Z
**Source review:** .planning/phases/06-attachments-non-relevant-corpus/06-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 6 (CR-01, WR-01..WR-05; IN-01..IN-07 out of scope)
- Fixed: 6
- Skipped: 0
- I checked every finding against the code before fixing it. All six were accurate.
- Verification: `dotnet build ReelDiscovery.csproj -c Release` gives 0 warnings and 0 errors. The full suite
  (`dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -c Release`) gives **3420 passed, 0 failed**. The
  baseline was 3415; the 5 extra tests are new regression tests. Existing tests were also given new assertions.

## Fixed Issues

### CR-01: Reopen and leave-step key projections write a key whose subjects and file names differ from the EMLs

**Files modified:** `Services/Evidence/GenerationRunState.cs`, `UserControls/StepGenerateCorpus.Run.cs`, `ReelDiscovery.Tests/Evidence/AnswerKeyOverlayTests.cs`
**Commit:** 30dad89
**Applied fix:** Added `GenerationRunState.BuildFullOverlay(plan, guide)`, which is `BuildOverlay(plan, AllAssignments(plan), AttachmentChains.Compute(plan, guide))` and matches the runner's projection. `ReadEntryAsync` (crash-recovery projection) and `OnLeaveStepAsync` (locked-key retry) now call it. `HistoryView` passes `GenerationRunState.AllAssignments(plan)` to `GenerationHistory.Events`.
**New tests:**
- `BuildFullOverlay_MatchesRunnerProjection_PrivilegedFillerKeepsSubjectTag`: the key fingerprint equals the runner's overlay, a `BannerAndSubject` filler thread keeps its `Privileged & Confidential - ` subject, and a pending chained attachment keeps `_v2`.
- `HistoryView_ActivityRows_UseFillerMarkingSubjects`.

### WR-01: A failed or unjournaled thread leaves its written EML copies in the mailboxes

**Files modified:** `Services/Evidence/MailboxWriter.cs`, `Services/Evidence/ThreadUnitOfWork.cs`, `ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs`
**Commit:** cd72fec
**Applied fix:**
- New `MailboxWriter.DeleteCopies(PlannedMessage)` removes every custodian copy path, best effort, and returns the paths it could not remove.
- `ThreadUnitOfWork` tracks the composed thread from the first write until the ThreadDone line is flushed. Every exit before that commit removes all copies of the thread: write failure, journal-append failure, IO and generic catches, and cancel.
- Copies that cannot be removed are named in the failure reason.
- This is safe because only non-done threads run, so no committed EML shares these paths.

AttachmentRepair was deliberately left alone. It rewrites EMLs of threads that are already committed, so deleting there would destroy valid output.
**Tests:** `Commit_SecondCopyWriteFails_NothingReportedWritten` and `Commit_ThreadDoneAppendFails_SpendReturnedUnjournaled` now also assert that no copy of the thread is left on disk.

### WR-02: Journal-save failures and incomplete saved text never appear in the run log

**Files modified:** `Models/Evidence/GenerationProgress.cs`, `UserControls/GenerationUiText.cs`, `Services/Evidence/ThreadUnitOfWork.cs`, `Services/Evidence/AttachmentRepair.cs`, `Services/Evidence/GenerationRunner.cs`, tests in `ThreadUnitOfWorkTests`, `AttachmentRepairTests`, `GenerationRunnerTests`, `GenerationUiTextTests`
**Commit:** 8945278
**Applied fix:**
- Added a new `GenerationEventKind.NotSaved`. It shows as a Danger row: "Not saved: {reason}. It stays pending for the next Continue."
- The event is raised on every Unjournaled path:
  - a failed ThreadDone append;
  - a repair whose saved text is incomplete;
  - a failed AttachmentsRepaired append;
  - a broken repair (`Broken` now takes the render context).
- `WaveFinished` and `NonRelevantFinished` no longer count Unjournaled outcomes as Failed, so they now agree with the tiles.

**Tests:** new `Activity_NotSaved_ShowsReasonAsDanger`, plus NotSaved and WaveFinished assertions added to the existing unjournaled tests.
**Still open (needs a design decision):** a thread whose saved text is incomplete is now reported on every Continue, but it still never converges. Making it converge would need a choice about whether to journal those attachments as Failed.

### WR-03: Version-chain order and generation order disagree, so a "_v2" can be written before (and without) v1

**Files modified:** `Services/Evidence/AttachmentUnitOfWork.cs`, `ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs`
**Commit:** 4aaa99a
**Applied fix:** I used the reviewer's second option:
- Chain numbering stays in SentAt order (D-16 / Pitfall 15).
- When a version above 1 is rendered with no predecessor text, it is kept with a code-owned `C1` warning, added after the checks like the existing `I1` invite warning so no retries are spent on it: "C1: earlier version not yet written; this version was drafted without its text, so its figures may not build on it".
- This happens when the predecessor is generated later, or when it failed.
- The attachment becomes "Generated - with warnings" in the key.

I rejected reordering by generation rank. It would number a version that is sent earlier as the higher version (a v2 emailed before its v1), and it would break the SentAt numbering contract.
**Status:** fixed: requires human verification. This is a policy choice: flagging instead of reordering.
**Tests:** `Chain_InterleavedAcrossTwoThreads_NoDeadlock` now checks that v3 (written without v2) is journaled with C1 and has status Warnings, and that v2 (written with v1's text) has no C1.

### WR-04: Same-step invite schedules depend on finish order

**Files modified:** `Services/Evidence/ThreadUnitOfWork.cs`, `Services/Evidence/AttachmentRepair.cs`, `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs`
**Commit:** 120b6a0
**Applied fix:**
- Each relevant wave thread unit now gets its own `StepContext` copy, so the step-start slots are frozen. Repairs already had their own contexts.
- Committed invite slots go to a new `ThreadAttachmentContext.CommittedInvites` (DocId, parent SentAt UTC, slot) instead of the shared `InviteSlots`.
- `GenerationRunner.KeepSlots` publishes them at the step end, ordered by parent SentAt then DocId, first one wins. This is the same rule `GenerationRunState.InviteSlotsByMeeting` uses on replay, so a Continue sees the same schedules as an uninterrupted run.

**Status:** fixed: requires human verification. This is a scheduling-semantics change.
**Tests:**
- New `KeepSlots_SameStepCommits_EarliestPlannedInviteWins_WhateverTheFinishOrder`.
- The two invite tests that read `InviteSlots` after a unit now expect it to stay frozen. They publish through `KeepSlots`.

### WR-05: The planner edits the live case guide while it awaits LLM calls

**Files modified:** `Services/Corpus/CorpusPlanner.cs`, `ReelDiscovery.Tests/Corpus/CorpusPlannerTests.cs`
**Commit:** e0ee53f
**Applied fix:**
- `PlanAsync` now takes a deep copy before the first await (`CaseDraft.FromJson(draft.ToJson())`, with `FolderPath` carried over) and runs the whole pass on it: skeleton context, `FillLoneKnowers`, the filler, the seeder, the protocol and the validator.
- The save/restore block is gone, so the live draft is never mutated.
- `plan.AddedKnowers` still carries the additions, and `CorpusApproval.ApplyNewPlanAsync` still applies them on save.
- `GuideHash` still equals the hash of the guide as applied.
- The existing contract holds: draft JSON unchanged after PlanAsync, and the `WithAddedKnowers` helper.

**Tests:** new `PlanAsync_LoneKnowerFact_LiveDraftNeverChangesDuringLlmCalls`. It snapshots the live draft JSON on every LLM call and checks that each snapshot equals the pre-plan JSON. It also checks that `AddedKnowers` holds the lone fact and that GuideHash matches `WithAddedKnowers`. I confirmed that this test fails on the old code.

---

_Fixed: 2026-10-06T01:29:57Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
