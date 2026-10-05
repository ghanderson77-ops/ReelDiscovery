---
phase: 06-attachments-non-relevant-corpus
reviewed: 2026-10-05T19:32:57Z
depth: deep
diff_base: b6ad967
files_reviewed: 70
files_reviewed_list:
  - Helpers/BulkMailFormatter.cs
  - Helpers/HtmlEmailFormatter.cs
  - Models/Corpus/CorpusPlan.cs
  - Models/DocumentMetadata.cs
  - Models/Evidence/AttachmentRunStatus.cs
  - Models/Evidence/ComposedAttachment.cs
  - Models/Evidence/GenerationProgress.cs
  - Models/Evidence/GenerationState.cs
  - Models/Evidence/JournalRecord.cs
  - Models/Evidence/LedgerDelta.cs
  - Models/Evidence/RenderedThread.cs
  - Models/Evidence/SideWorld.cs
  - Models/MediaPrices.cs
  - Models/TokenUsageTracker.cs
  - Models/WizardState.cs
  - Services/CalendarService.cs
  - Services/Case/CanonicalValueScanner.cs
  - Services/Case/EmailIdentity.cs
  - Services/Corpus/AnswerKeyBuilder.cs
  - Services/Corpus/CorpusApproval.cs
  - Services/Corpus/CorpusPlanStore.cs
  - Services/Corpus/CorpusPlanner.cs
  - Services/Corpus/GenerationCostEstimator.cs
  - Services/Corpus/SkeletonContext.cs
  - Services/Corpus/SubjectSeeder.cs
  - Services/Corpus/ThreadSkeletonBuilder.cs
  - Services/Evidence/AttachmentChains.cs
  - Services/Evidence/AttachmentHarvester.cs
  - Services/Evidence/AttachmentNaming.cs
  - Services/Evidence/AttachmentPrompts.cs
  - Services/Evidence/AttachmentRenderContext.cs
  - Services/Evidence/AttachmentRenderer.cs
  - Services/Evidence/AttachmentRepair.cs
  - Services/Evidence/AttachmentUnitOfWork.cs
  - Services/Evidence/CastVoiceBuilder.cs
  - Services/Evidence/ChainScheduler.cs
  - Services/Evidence/DecoyScanner.cs
  - Services/Evidence/EmailComposer.cs
  - Services/Evidence/EvidencePrompts.cs
  - Services/Evidence/FillerAssignments.cs
  - Services/Evidence/FillerPrompts.cs
  - Services/Evidence/FillerRenderer.cs
  - Services/Evidence/GenerationHistory.cs
  - Services/Evidence/GenerationJournal.cs
  - Services/Evidence/GenerationReset.cs
  - Services/Evidence/GenerationRunState.cs
  - Services/Evidence/GenerationRunner.cs
  - Services/Evidence/GenerationSnapshot.cs
  - Services/Evidence/IMediaGenerator.cs
  - Services/Evidence/InviteScheduler.cs
  - Services/Evidence/LedgerMerger.cs
  - Services/Evidence/MailboxWriter.cs
  - Services/Evidence/MediaRunGate.cs
  - Services/Evidence/OpenAIMediaGenerator.cs
  - Services/Evidence/OrganizationThemes.cs
  - Services/Evidence/PeopleDirectory.cs
  - Services/Evidence/RenderProfile.cs
  - Services/Evidence/RenderValidator.cs
  - Services/Evidence/SideWorldBuilder.cs
  - Services/Evidence/StorySoFarDocxBuilder.cs
  - Services/Evidence/ThreadUnitOfWork.cs
  - Services/Evidence/VoiceAssignments.cs
  - Services/Evidence/VoicemailComposer.cs
  - Services/OfficeDocumentService.cs
  - UserControls/GenerationUiText.cs
  - UserControls/ModelConfigurationDialog.cs
  - UserControls/StepApiConfiguration.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepGenerateCorpus.Run.cs
  - UserControls/StepGenerateCorpus.cs
findings:
  blocker: 0
  high: 1
  medium: 5
  low: 7
  total: 13
  # template mapping: critical = blocker + high, warning = medium, info = low
  critical: 1
  warning: 5
  info: 7
status: issues_found
---

# Phase 6: Code Review Report

**Reviewed:** 2026-10-05T19:32:57Z
**Depth:** deep (cross-file call chains traced for the runner, unit of work, repair, journal, mailbox and answer-key paths)
**Files Reviewed:** 70 production files (the 55 changed test files were not reviewed for defects)
**Status:** issues_found

## Summary

Scope: every non-`.planning` file changed between `b6ad967` and `HEAD`. I traced these paths end to end:
`GenerationRunner` -> `ThreadUnitOfWork` / `AttachmentRepair` -> `AttachmentUnitOfWork` -> `MailboxWriter` / `GenerationJournal`,
then replay -> `GenerationRunState` -> `BuildOverlay` -> `AnswerKeyBuilder`, plus the `StepGenerateCorpus` checkpoint
marshalling. `dotnet build ReelDiscovery.csproj` finishes with 0 warnings and 0 errors.

These areas are sound:
- **Journal and commit boundaries.** A thread and its attachments go into one ThreadDone line. A repair goes into one
  AttachmentsRepaired line. Status, events, chain text and invite slots are applied only after the flush.
- **Deadlocks.** The chain wait graph is ordered by thread rank. Repairs wait for their predecessors before taking a
  repair slot. LLM slots are held only during the call itself. `ProjectionGate` never nests.
- **Deterministic EML output.** Message-IDs, boundaries, Content-IDs, the ICS UID and DTSTAMP, and the PO-box footer are
  all derived from hashes. Repairs reuse the attachment bytes harvested from the existing copies.
- **Encoding.** Untrusted model text is HTML-encoded before the allowlisted markup is applied. ICS text is TEXT-escaped
  and folded. MIME paths and file names are checked with `IsSafeEmlPath` / `IsSafeFileName` / `ResolveUnderCase`.
- **Bounded reads.** The harvester caps EML, part and total bytes.

One HIGH defect: the answer key is written two ways. The runner uses `AllAssignments` plus chains. Two other places
(the reopen/crash-recovery projection and the leave-step retry) use the relevant-only assignments with no chains, and
produce subjects and file names that differ from the EMLs. The MEDIUM items are:
- partial EML writes left on disk after a thread fails;
- journal-save failures that never appear in the activity log;
- version chains whose order does not match the generation order;
- same-step invite schedules that depend on timing;
- the planner editing the live case guide while it awaits LLM calls.

No BLOCKER-level crash, injection or data-loss path was found.

## Critical Issues

### CR-01 (HIGH): Reopen and leave-step key projections write a key whose subjects and file names differ from the EMLs

**File:** `UserControls/StepGenerateCorpus.Run.cs:839`, `UserControls/StepGenerateCorpus.Run.cs:944`, `UserControls/StepGenerateCorpus.Run.cs:969`
**Evidence:**
- The runner projects the key with every assignment and the chains:
  `state.BuildOverlay(plan, ctx.Assignments, ctx.Chains)`. See `GenerationRunner.cs:926`, with
  `Assignments = GenerationRunState.AllAssignments(plan)` at `GenerationRunner.cs:149`.
- The step writes the same file from two other places with `state.BuildOverlay(plan, RenderAssignments.Compute(plan))`:
  - `ReadEntryAsync` (crash recovery when case.json is behind the journal), line 944;
  - `OnLeaveStepAsync` (the retry after the key was locked in Excel), line 839.
- `RenderAssignments.Compute` covers relevant threads only ("Non-relevant threads get no entry", `RenderAssignments.cs:44`).
  `FillerAssignments.AssignPrivilege` gives privileged non-relevant threads `BannerAndSubject`
  (`FillerAssignments.cs:131-138`). `BuildOverlay` takes the subject from the assignment marking
  (`GenerationRunState.cs:360-374`). So these two paths drop the `Privileged & Confidential - ` prefix from every such
  filler email row, while the EML subject has it.
- With `chains == null`, pending chained relevant attachments also lose their `_vN` names (`GenerationRunState.cs:408-410`).
- `OnLeaveStepAsync` then sets `generation.KeyError` to empty after the write (line 856). Nothing retries it, so the
  wrong key stays on disk until the next run.
- `HistoryView` (line 969) passes the same relevant-only assignments to `GenerationHistory.Events`, so reopened
  activity rows show the wrong subjects too.

The answer key is the core deliverable, and its subjects must match the EMLs (Pitfall 2).
**Fix:** use one projection helper everywhere:
```csharp
// GenerationRunState
public AnswerKeyOverlay BuildFullOverlay(CorpusPlan plan, CaseGuide guide) =>
    BuildOverlay(plan, AllAssignments(plan), AttachmentChains.Compute(plan, guide));

// StepGenerateCorpus.Run.cs:839 and :944
state.BuildFullOverlay(plan, guide)
// :969
GenerationHistory.Events(plan, replay, GenerationRunState.AllAssignments(plan))
```
Add a test: a privileged `BannerAndSubject` filler thread must get the same key Subject from `ReadEntryAsync`,
`OnLeaveStepAsync` and the runner checkpoint.

## Warnings

### WR-01 (MEDIUM): A failed or unjournaled thread leaves its written EML copies in the mailboxes

**File:** `Services/Evidence/ThreadUnitOfWork.cs:242-255`, `Services/Evidence/ThreadUnitOfWork.cs:311-324`, `Services/Evidence/ThreadUnitOfWork.cs:398-403`
**Evidence:**
- Copies are written one email at a time (lines 242-249). When copy N throws `IOException`, `FailAsync` journals
  ThreadFailed. The copies of messages 1..N-1 stay on disk, and no record lists them.
- When the ThreadDone append fails (lines 311-324), every copy stays on disk and the thread stays Pending.
- An exception after the writes goes through the generic catch (line 398), which journals ThreadFailed with the files
  still present.

In each case the custodian mailboxes hold EMLs whose key row says Failed or Planned. If the user stops there (a
partial corpus is a valid result, per the U7 Next gate), the delivered corpus contradicts its answer key. Start Over
cleans the planned paths, but Continue only overwrites them if the thread succeeds next time.
**Fix:** track the paths written in this unit, and delete them (best effort, logged) on every non-committed exit:
```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    await DeleteWrittenAsync(files);   // File.Delete via MailboxWriter.ResolveUnderCase, errors ignored with a comment
    return await FailAsync(...);
}
```
Alternatively, journal the written paths on the ThreadFailed record so `ListAllFiles` and the key can account for them.

### WR-02 (MEDIUM): Journal-save failures and incomplete saved text never appear in the run log

**File:** `Services/Evidence/ThreadUnitOfWork.cs:311-324`, `Services/Evidence/AttachmentRepair.cs:94-99`, `Services/Evidence/AttachmentRepair.cs:343-352`, `Services/Evidence/GenerationRunner.cs:743-754`, `Services/Evidence/GenerationRunner.cs:436-438`
**Evidence:**
- The Unjournaled paths set `Reason = "Could not save the run journal: ..."` (ThreadUnitOfWork 318, AttachmentRepair
  348). `Accepted()` returning null sets `"The saved text of thread {id} is incomplete."` (AttachmentRepair 96).
- No `GenerationEvent` is added for any of these, and `RunRepairAsync` / `RunThreadAsync` ignore `outcome.Reason`.
- On a full disk, the user sees threads that neither finish nor fail, and then "Finished" with pending items and no
  cause.
- A thread whose saved text is incomplete is repaired, silently, on every Continue, and never converges.
- `WaveFinished` counts the Unjournaled outcome as Failed (`Kind = Failed`, line 438), but `RecordThread` was never
  called for it. The event row and the tiles disagree.

**Fix:** add an activity event (for example `ThreadFailed` with the reason, or a new `JournalWriteFailed` kind) on
every Unjournaled return. For repairs, emit an `AttachmentFailed`-style event with `result.Reason`. Count Unjournaled
separately in `WaveFinished`, or exclude it: `results.Count(r => r.Kind == ThreadOutcomeKind.Failed && !r.Unjournaled)`.

### WR-03 (MEDIUM): Version-chain order and generation order disagree, so a "_v2" can be written before (and without) v1 (D-16)

**File:** `Services/Evidence/AttachmentChains.cs:71-76`, `Services/Evidence/ChainScheduler.cs:53-66`, `Services/Evidence/ChainScheduler.cs:128-139`, `Services/Evidence/WavePlanner.cs:39-41`
**Evidence:**
- Chains order versions by the parent message's `SentAt` (AttachmentChains 74).
- Waves and `RankThreads` order threads by their first message (WavePlanner 39-41, ChainScheduler 60).
- Example: thread X starts Jan 1 and attaches "Pricing Backup" on Jan 30. Thread Y starts Jan 15 and attaches it on
  Jan 16. Y holds v1 and X holds v2, but X is in an earlier wave (or ranks lower in the same step).
- `PriorTextAsync` returns step-start or committed text, which is empty (lines 132-139). X writes
  `Pricing Backup_v2.docx` (AttachmentNaming 87-92) with "earlier draft not available". Y's v1 is written later and
  knows nothing of v2.
- No warning or flag records this. The D-16 promise that "later versions build on earlier content" fails silently, and
  the figures may conflict across the chain.

**Fix:** order chain versions by generation order:
`.OrderBy(i => waveOf[thread]).ThenBy(i => threadRank).ThenBy(i => i.Parent.SentAt).ThenBy(DocId)`. Alternatively,
keep SentAt order but flag a link whose predecessor ranks higher with a Warnings violation, such as `"C1: earlier
version not yet written"`, so the key shows it.

### WR-04 (MEDIUM): Same-step invite schedules depend on finish order

**File:** `Services/Evidence/GenerationRunner.cs:1098-1104`, `Services/Evidence/ThreadUnitOfWork.cs:347-349`, `Services/Evidence/ThreadUnitOfWork.cs:458-468`, `Services/Evidence/InviteScheduler.cs:112-121`
**Evidence:**
- `StepContext` builds one `ThreadAttachmentContext` per step, and every thread unit in that step shares its
  `InviteSlots`.
- Right after its commit, a unit does `attachments.InviteSlots.TryAdd(slot.MeetingKey, slot)` (line 349).
- A sibling unit that resolves its invite afterwards reads the same dictionary as `KnownSlots` (line 466).
  `InviteScheduler.Resolve` prefers a known slot over its own derived or stated time (lines 112-116).
- So whether thread B's invite for `fact:F-012` uses A's stated "10:30 AM" or B's own grid time depends on which unit
  finished first. A Continue after a crash can produce a different schedule than an uninterrupted run would have.
- The class comment and the T-06-16c note assume same-wave threads cannot see each other. This shared dictionary
  breaks that assumption at the commit boundary.

**Fix:** give each unit a frozen copy of the step-start slots, and publish committed slots only at the step end
(`KeepSlots` already does this):
```csharp
// GenerationRunner.RunThreadAsync / RunRepairAsync: per-unit context
var unitCtx = StepContext(ctx, step.RowIndex); // own copy of ctx.InviteSlots
...
// after Task.WhenAll: KeepSlots(ctx, unitCtx) for each unit, in thread-rank order
```

### WR-05 (MEDIUM): The planner edits the live case guide while it awaits LLM calls

**File:** `Services/Corpus/CorpusPlanner.cs:52-67`, `Services/Corpus/CorpusPlanner.cs:110-113`, `Services/Corpus/ThreadSkeletonBuilder.cs:818-866`, `Services/Corpus/SkeletonContext.cs:60`
**Evidence:**
- `SkeletonContext.DatedFacts` holds references to `draft.Guide.Facts`. `FillLoneKnowers` then rewrites
  `fact.KnownByIds` on those live objects (via `AddKnowers`).
- Minutes of awaited LLM work follow on the same draft: the filler at line 147, the seeder at 153 and the protocol at
  178. Only the `finally` at lines 62-66 restores the lists.
- Any save during that window persists knowers the user never accepted: the WizardForm close-time `SaveNow`, a
  `QueueSave` on the step, or the Cast/Facts views. A cancelled or failed planning run then leaves case.json out of
  step with memory until the next save.
- The restore also replaces each list instance, so any UI binding that holds the old list goes stale.

This breaks the project rule that planning never writes into the draft (T-04-48).
**Fix:** plan against a deep copy (`var guide = CaseGuide.FromJson(draft.Guide.ToJson())` or a `Clone()`), so that
`SkeletonContext.Create` and `FillLoneKnowers` mutate only the copy. `ApplyNewPlanAsync` already applies
`plan.AddedKnowers` to the real guide on success. Then remove the save/restore block.

## Info

### IN-01 (LOW): Truncation can split a surrogate pair, and the Office builders do not strip lone surrogates

**File:** `Services/Evidence/AttachmentRenderer.cs:325-331`, `Services/Evidence/FillerRenderer.cs:99-105`, `Models/Evidence/SideWorld.cs` (`Clip`), `Services/OfficeDocumentService.cs` (`Clean`)
**Issue:** `text[..MaxChars]` at 2,000 or 120 chars can end on a high surrogate, for example when an emoji or astral
character sits at the cut. `OfficeDocumentService.Clean` removes `char.IsControl` only. OpenXml's XmlWriter rejects a
lone surrogate with an `ArgumentException`, so the attachment becomes Failed for no content reason, and a new call is
billed on Retry. JSON serialization is safe: it was verified to write U+FFFD.
**Fix:** cut at a rune boundary (`if (char.IsHighSurrogate(text[max - 1])) max--;`), and drop unpaired surrogates in `Clean`.

### IN-02 (LOW): MailboxWriter silently drops parts that the journal records as Written

**File:** `Services/Evidence/MailboxWriter.cs:154-162`, `Services/Evidence/ThreadUnitOfWork.cs:269-271`, `Services/Evidence/AttachmentRepair.cs:486`
**Issue:** a part with empty bytes or a name that fails `IsSafeFileName` is skipped with no signal. The nested record
still says AttachmentDone with `Files = copies`, so the key says Generated for an attachment that is not in the EML.
`Rebuilt` uses `record.FileName`, which `Normalize` blanks when unsafe, so this can happen on repair.
**Fix:** return the list of skipped DocIds from `BuildMime` / `WriteAsync`, and mark those outcomes Failed before the
journal line is built. Alternatively, fall back to `AttachmentNaming.NamesFor` when `record.FileName` is empty.

### IN-03 (LOW): The side world is rebuilt on every Continue when one covered organization has no projects

**File:** `Services/Evidence/SideWorldBuilder.cs:197-201`, `Services/Evidence/SideWorldBuilder.cs:287-297`
**Issue:** `TemplatesFor` can return an empty list when every template collides with case terms. `FromRecord` requires
`Projects.Count > 0` for every covered organization, so the journaled world is rejected. Each Continue then pays for
a new call, appends a new SideWorld record, and gives filler different side projects than earlier runs (D-21).
**Fix:** accept a persisted world when its organization ids cover the guide, whether or not every list is non-empty.
`ProjectsFor` already falls back to templates or routine work.

### IN-04 (LOW): The voicemail TTS input has no length limit in code

**File:** `Services/Evidence/AttachmentUnitOfWork.cs:201`, `Services/Ai/OpenAIMediaClient.cs:104`
**Issue:** the transcript length (20-90 words) is enforced only by the prompt. A long model body above the tts-1 limit
of 4,096 chars returns a 400, which becomes null and then Failed ("the model returned no usable content"). Every Retry
repeats the same failure, because the journaled transcript is reused.
**Fix:** clip the speech text to about 4,000 chars at a sentence boundary before `GenerateSpeechAsync`, and report it
as a warning.

### IN-05 (LOW): The new K/M/B amount suffixes produce false positives

**File:** `Services/Case/CanonicalValueScanner.cs:27`
**Issue:** `\s?(?:MM|[KMB])(?![A-Za-z])` with IgnoreCase scales "$500 M&A budget" to $500,000,000 and "$10 K-cups" to
$10,000. These scans feed V3 variant checks, `DecoyScanner` amount hits and Excel numeric cells, so they can produce
false violations and wasted retries.
**Fix:** require no space before a single-letter suffix (`\$\d+(?:\.\d+)?[KMB]\b`), or require a word boundary that
excludes `&` and `-` (`(?![A-Za-z&\-])`).

### IN-06 (LOW): Nothing stops the journal from growing past the 200 MB replay cap

**File:** `Services/Evidence/GenerationJournal.cs:101-128`, `Services/Evidence/GenerationJournal.cs:154-158`
**Issue:**
- Each ThreadDone line carries every message body plus up to 20,000 chars of FlattenedText per attachment, and retried
  threads add new lines.
- `AppendAsync` keeps appending past `MaxJournalBytes`. Once over the cap, every checkpoint is unprojected, and
  reopening shows the Unreadable card, where the only action is Start Over (which deletes the corpus).
- The target of about 5K emails fits today, but the failure mode is total.

**Fix:** check the file length in `AppendAsync`. Above about 90% of the cap, report it as a fatal-style stop with a
clear message, so the run ends cleanly before the journal becomes unreadable. Alternatively, store FlattenedText
outside the journal.

### IN-07 (LOW): The voicemail system sender takes the first recipient's domain, even a consumer one

**File:** `Services/Evidence/VoicemailComposer.cs:321-334`
**Issue:** when the first recipient is a personal external (for example gmail.com), the notice comes from
`voicemail@gmail.com` with the name "Voice Mail". That is not plausible, and it is a cheap signal for the near-miss and
filler mix.
**Fix:** use the first cast recipient's organization domain. Use the caller's domain only when no cast recipient exists.

---

_Reviewed: 2026-10-05T19:32:57Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
