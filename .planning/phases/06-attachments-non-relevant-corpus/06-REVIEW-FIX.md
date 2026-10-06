---
phase: 06-attachments-non-relevant-corpus
fixed_at: 2026-10-06T14:27:22Z
review_path: .planning/phases/06-attachments-non-relevant-corpus/06-REVIEW.md
iteration: 2
findings_in_scope: 13
fixed: 13
skipped: 0
status: all_fixed
---

# Phase 6: Code Review Fix Report

**Fixed at:** 2026-10-06T01:29:57Z
**Source review:** .planning/phases/06-attachments-non-relevant-corpus/06-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope (iteration 1): 6 (CR-01, WR-01..WR-05; IN-01..IN-07 were handled in iteration 2, below)
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

## Iteration 2: Info findings (IN-01..IN-07)

**Fixed at:** 2026-10-06T14:27:22Z
**Scope:** `all`. CR-01 and WR-01..WR-05 were fixed in iteration 1 and were not touched again.

**Summary:**
- Findings in scope this iteration: 7 (IN-01..IN-07)
- Fixed: 7
- Skipped: 0
- I checked every finding against the current code first. All seven still applied. Some line numbers had moved because of the WR fixes; for example, IN-07's sender code is now at `VoicemailComposer.cs:69-84`, not 321-334.
- For two findings I used a different fix than the reviewer suggested: IN-05 (a "no space" rule would have broken tests set in review 06-06) and IN-06 (I chose to refuse appends rather than move FlattenedText out of the journal). Details are below.
- Verification: `dotnet build ReelDiscovery.csproj -c Release --no-incremental` gives 0 warnings and 0 errors. The full suite (`dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -c Release`) gives **3443 passed, 0 failed**. The baseline was 3420; the 23 extra tests are new regression tests. One old voicemail test was replaced because IN-07 changed the behavior it checked.

### IN-01: Truncation can split a surrogate pair, and the Office builders do not strip lone surrogates

**Files modified:** `Services/Evidence/AttachmentRenderer.cs`, `Services/Evidence/FillerRenderer.cs`, `Models/Evidence/SideWorld.cs`, `Services/OfficeDocumentService.cs`, `ReelDiscovery.Tests/Evidence/SurrogateCutTests.cs` (new)
**Commit:** c7264af
**Applied fix:**
- `AttachmentRenderer.Clean`, `FillerRenderer.CleanTitle` and `SideWorld.Clip` now cut one character earlier when the cut would end on a high surrogate.
- `OfficeDocumentService.Clean` keeps only complete surrogate pairs and drops unpaired high or low surrogates.
- `AttachmentRenderer.Clean`, `SideWorld.Clip` and `OfficeDocumentService.Clean` are now `internal`, so the tests can call them.

**Tests:** 6 new tests in `SurrogateCutTests`:
- each of the three cut points, with a pair straddling the cut;
- a pair just before the cut is kept whole;
- `Clean` drops lone surrogates;
- a Word document built from text with lone surrogates opens and holds no lone surrogate.

### IN-02: MailboxWriter silently drops parts that the journal records as Written

**Files modified:** `Services/Evidence/MailboxWriter.cs`, `Services/Evidence/ThreadUnitOfWork.cs`, `Services/Evidence/AttachmentRepair.cs`, `ReelDiscovery.Tests/Evidence/AttachmentRepairTests.cs`
**Commit:** b8df9e9
**Applied fix:**
- New `MailboxWriter.CanCarry(part)`: the part has bytes and a safe bare file name. `AddAttachments` now uses it, so there is one rule for which parts the writer keeps.
- `ThreadUnitOfWork` and `AttachmentRepair` check every committed outcome before building the journal line. If the writer would drop its part, the outcome is marked as a Write failure (`ThreadUnitOfWork.FailUncarriable`, with the spend kept). The key then says Failed, not Generated.
- In repair, `Rebuilt` falls back to the planned `AttachmentNaming.NamesFor` name when the journaled name is unsafe (or was blanked by `Normalize`). A reused part that still cannot be carried is regenerated, not dropped.

**Tests:**
- `Repair_UnsafeJournaledFileName_ReusedPartKeptUnderPlannedName`. I confirmed it fails on the old `AttachmentRepair`.
- `CanCarry_RejectsEmptyBytesAndUnsafeNames`.
- `FailUncarriable_MarksWriteFailure_DropsPartKeepsSpend`.

**Note:** in the first-write path, the unit of work already fails empty media bytes, and `NamesFor` names are always safe. So in that path this is defense in depth; the repair path is where the bug could actually happen.

### IN-03: The side world is rebuilt on every Continue when one covered organization has no projects

**Files modified:** `Services/Evidence/SideWorldBuilder.cs`, `ReelDiscovery.Tests/Evidence/SideWorldTests.cs`
**Commit:** 469e807
**Applied fix:** `FromRecord` now only requires that every covered organization is listed. An empty project list counts as listed, because `ProjectsFor` already falls back to templates. A guide that gains a new organization still forces a rebuild.
**Tests:** new `FromRecord_CoveredOrganizationWithNoProjects_WorldReused`. The existing coverage test (a missing organization still means null) still passes.

### IN-04: The voicemail TTS input has no length limit in code

**Files modified:** `Services/Evidence/AttachmentUnitOfWork.cs`, `ReelDiscovery.Tests/Evidence/AttachmentUnitOfWorkTests.cs`
**Commit:** 81e6e9a
**Applied fix:**
- New `AttachmentUnitOfWork.SpeechText`: text up to `MaxSpeechChars` (4,000) passes through unchanged.
- Longer text is cut at the last sentence end in the second half of the window. Without one, it is cut at the last space. It never cuts inside a surrogate pair.
- A clipped voicemail is kept as Warnings with a code-owned `S1` violation ("the audio stops before the end of the written text"). Before, it got a 400 that failed the same way on every Retry.
- The email transcript (BodyPlain) is unchanged.

**Tests:**
- `Voicemail_OverlongTranscript_ClippedAtSentence_KeptWithWarning`.
- `SpeechText_ShortUnchanged_NoSentenceCutsAtSpace_NeverSplitsPair`.

### IN-05: The new K/M/B amount suffixes produce false positives

**Files modified:** `Services/Case/CanonicalValueScanner.cs`, `ReelDiscovery.Tests/Case/CanonicalValueScannerTests.cs`
**Commit:** ca8e721
**Applied fix:** I used the reviewer's second option, refined. I rejected the first option ("no space before a single-letter suffix") because existing tests deliberately accept `$1.25 M`, a decision from review 06-06. The new rules:
- A suffix written directly after the number must not be followed by a letter or `&`.
- A suffix written after a space must also not be followed by `-`.

So `$500 M&A`, `$500M&A` and `$10 K-cups` are no longer scaled. `$1.25 M`, `$2.5 MM` and ranges such as `$500K-$600K` still scale.
**Status:** fixed: requires human verification. This is a heuristic change to the regex.
**Tests:** 4 new `InlineData` cases in `Amounts_SuffixOnlyAtWordBoundary`, plus new `Amounts_AdjacentSuffixRange_BothEndsScaled`.

### IN-06: Nothing stops the journal from growing past the 200 MB replay cap

**Files modified:** `Services/Evidence/GenerationJournal.cs`, `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Evidence/GenerationJournalTests.cs`, `ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs`
**Commit:** d04eef5
**Applied fix:** I used the reviewer's first option, a stop near 90% of the cap. I did not defer it to Phase 7, because a minimal fix was safe: it reuses paths that already exist.
- `AppendAsync` refuses a line that would take the file past `StopBytes`, which is 90% of the replay cap. The `RunEnded` line may use the remaining space up to the cap. A refused line throws `JournalFullException`, which is a subclass of `IOException`.
- Because it is an `IOException`, every existing caller already handles it as a failed journal save. The thread stays pending, copies are removed (WR-01), and a NotSaved row is shown (WR-02). The journal stays readable, so the Unreadable card and Start Over are never forced.
- `GenerationRunner` checks `journal.IsFull` after each thread or repair and calls `run.SetFatal(...)`. This is the same path as a provider fatal error, so no further calls are paid for.
- If the journal is already past the stop line, the `RunStarted` append is refused. That now gives a `Fatal` outcome before any LLM call is made, instead of `InternalError`.

**Still open (Phase 7, Claude's discretion per 07-CONTEXT.md):** a full journal stops the run, but it does not compact. Moving FlattenedText out of the journal, or compacting superseded lines, would raise the real limit.
**Tests:**
- `Append_PastStopLine_RefusedFileUnchanged_ReplayStaysReadable`.
- `Append_RunEnded_MayUseTheRestOfTheCap`.
- `JournalAtStopLine_StopsRunFatal_JournalStaysReadable`: one thread is requested, the outcome is Fatal with the full message, the journal holds only RunStarted and RunEnded, a NotSaved event is raised, and no EML is left.
- `JournalAlreadyPastStopLine_StopsBeforeAnyCall`.

### IN-07: The voicemail system sender takes the first recipient's domain, even a consumer one

**Files modified:** `Services/Evidence/VoicemailComposer.cs`, `ReelDiscovery.Tests/Evidence/VoicemailComposerTests.cs`
**Commit:** 36f35cb
**Applied fix:** The notice sender is now chosen in this order:
1. the first cast recipient (To, then Cc);
2. the caller, if the caller is cast;
3. the first recipient, as before.

A personal or vendor external no longer produces `voicemail@gmail.com`.
**Status:** fixed: requires human verification. This changes the D-28 sender rule: a cast caller's organization now owns the notice when the recipient is external.
**Tests:**
- `Compose_recipient_without_organization_uses_plain_voice_mail_and_own_domain` checked the old behavior, so I replaced it with `Compose_personal_external_recipient_uses_the_cast_callers_organization`.
- New `Compose_cast_recipient_in_cc_wins_over_external_to_and_caller`.
- New `Compose_no_cast_on_the_message_uses_plain_voice_mail_and_the_recipients_domain`, which keeps the plain "Voice Mail" fallback covered.

---

_Fixed: 2026-10-06T01:29:57Z (iteration 1), 2026-10-06T14:27:22Z (iteration 2)_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 2_
