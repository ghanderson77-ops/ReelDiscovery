---
phase: 05-relevant-evidence-generation-narrative-ledger
reviewed: 2026-10-03T00:00:00Z
depth: standard
files_reviewed: 56
files_reviewed_list:
  - Forms/ReplanDialog.cs
  - Forms/WizardForm.cs
  - Helpers/HtmlEmailFormatter.cs
  - Models/Case/CaseDraft.cs
  - Models/Case/CaseStage.cs
  - Models/Corpus/CorpusState.cs
  - Models/Evidence/GenerationProgress.cs
  - Models/Evidence/GenerationState.cs
  - Models/Evidence/GenerationWave.cs
  - Models/Evidence/JournalRecord.cs
  - Models/Evidence/LedgerDelta.cs
  - Models/Evidence/LedgerEntry.cs
  - Models/Evidence/NarrativeLedger.cs
  - Models/Evidence/RenderAssignment.cs
  - Models/Evidence/RenderViolation.cs
  - Models/Evidence/RenderedThread.cs
  - Services/Ai/LlmGateway.cs
  - Services/Ai/LlmResult.cs
  - Services/Case/CanonicalValueScanner.cs
  - Services/Corpus/AnswerKeyBuilder.cs
  - Services/Corpus/AnswerKeyXlsxWriter.cs
  - Services/Corpus/PlanCallRunner.cs
  - Services/Corpus/PlanNumbering.cs
  - Services/Evidence/EmailComposer.cs
  - Services/Evidence/EvidencePrompts.cs
  - Services/Evidence/GenerationHistory.cs
  - Services/Evidence/GenerationJournal.cs
  - Services/Evidence/GenerationOutputs.cs
  - Services/Evidence/GenerationReset.cs
  - Services/Evidence/GenerationRunState.cs
  - Services/Evidence/GenerationRunner.cs
  - Services/Evidence/GenerationSnapshot.cs
  - Services/Evidence/KnowledgeGate.cs
  - Services/Evidence/LedgerMerger.cs
  - Services/Evidence/LedgerSelector.cs
  - Services/Evidence/MailboxWriter.cs
  - Services/Evidence/MessageTime.cs
  - Services/Evidence/OrganizationThemes.cs
  - Services/Evidence/PeopleDirectory.cs
  - Services/Evidence/RenderAssignments.cs
  - Services/Evidence/RenderRepair.cs
  - Services/Evidence/RenderValidator.cs
  - Services/Evidence/StorySoFarDocxBuilder.cs
  - Services/Evidence/ThreadRenderContext.cs
  - Services/Evidence/ThreadRenderer.cs
  - Services/Evidence/ThreadUnitOfWork.cs
  - Services/Evidence/WavePlanner.cs
  - Services/ThemeGenerator.cs
  - UserControls/CaseGuideUiText.cs
  - UserControls/CorpusUiText.cs
  - UserControls/GenerationUiText.cs
  - UserControls/StatTile.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepGenerateCorpus.Run.cs
  - UserControls/StepGenerateCorpus.cs
  - UserControls/StepReviewPlan.Run.cs
findings:
  critical: 2
  warning: 7
  info: 5
  total: 14
status: issues_found
---

# Phase 5: Code Review Report

**Reviewed:** 2026-10-03T00:00:00Z
**Depth:** standard
**Files Reviewed:** 56
**Status:** issues_found

## Summary

I reviewed the Phase 5 relevant-evidence generation engine: the runner, unit of work, journal, mailbox writer, ledger, validators and composer, the Generate Corpus step and the Case Summary, ReplanDialog and WizardForm changes. For files that existed before the phase, I reviewed only the diff against `3964c82^`.

Several areas held up under adversarial reading:
- **HTML injection:** every model string goes through `WebUtility.HtmlEncode` before the allowlisted bold, list and table markup is added.
- **Header injection:** subjects have control characters stripped, and DocIDs and Message-IDs are built by code.
- **Path safety:** EML paths go through `IsSafeEmlPath` and `ResolveUnderCase`, and the story and journal names are built only from constants.
- **Journal tolerance:** journal appends are one flushed line each, and torn last lines are handled.
- **Cancellation:** the cancel path ends correctly through `Task.WhenAll`.

Two defects produce wrong output:
1. The best-attempt selection can throw away a usable render in favour of a structurally broken one.
2. A checkpoint built from an unreadable journal rewrites the answer key and the case.json generation state as if nothing had been generated.

Several warnings cover the live dashboard counts, cost accounting, the UI-thread marshalling edges and Start Over durability.

## Critical Issues

### CR-01: Best-attempt selection ignores structural violations, so a usable attempt can be discarded and the thread failed [BLOCKER]

**File:** `Services/Evidence/ThreadUnitOfWork.cs:98-103, 130-136`
**Issue:** `best` is chosen only by violation count (`violations.Count <= bestViolations.Count`). After the loop, the thread is failed if `bestViolations` contains V1 or V5. Here is how that goes wrong:
- Attempt 1 has one V1 violation (a message is missing or empty).
- Attempt 2 is fully composable but has two soft violations (V2 or V3).
- Attempt 1 wins on count, and the thread is journaled as `ThreadFailed` ("did not return a usable thread").

A usable render that was already paid for is thrown away. The thread then shows as Failed, and Retry Failed Threads pays for it again. This also breaks D-14 ("keep the best attempt"). A structurally broken attempt can never be composed, so it must never beat one that can.
**Fix:** Rank structural validity first, then the violation count:
```csharp
static bool IsStructural(List<RenderViolation> v) => v.Any(x => x.Code is "V1" or "V5");

var better = best == null
    || (IsStructural(bestViolations) && !IsStructural(violations))
    || (IsStructural(bestViolations) == IsStructural(violations) && violations.Count <= bestViolations.Count);
if (better) { best = rendered; bestViolations = violations; }
```

### CR-02: A checkpoint built from an unreadable journal overwrites the answer key and case.json generation state with an empty run [BLOCKER]

**File:** `Services/Evidence/GenerationRunner.cs:336-346, 357-367, 409-419`; `Services/Evidence/GenerationJournal.cs:126-130, 163-170`
**Issue:** `CheckpointCoreAsync` re-projects from `GenerationJournal.Replay(...)` on every checkpoint (every 2 s per finishing thread, at each wave end, and at run end). Replay does not throw. When the journal cannot be read, it returns `Exists = true`, a `Reason`, and an **empty** `LastByThread`. That happens on a transient IO error (antivirus, OneDrive or indexer lock, for example) or when the journal grows past the 200 MB cap during the run.

`GenerationRunState.From` turns that into "0 written / all pending". The checkpoint then does three things:
1. It rewrites `answer-key.csv` and `answer-key.xlsx` with every relevant row back to `Planned`.
2. It persists the empty state through `GenerationStateApplier.Apply`. That zeroes `ThreadsWritten`, `EmailsWritten` and `FilesWritten`, empties `Ledger` in case.json, and stamps `ProjectedUtc = now`.
3. On the final checkpoint, it skips the story file (`ledger.Entries.Count == 0`).

Because `ProjectedUtc` is now newer than the journal's last write, `NeedsProjection` on the next step entry returns false. The wrong key and the emptied case.json state therefore stay until another run happens. `OnLeaveStepAsync` (StepGenerateCorpus.Run.cs:710) already guards against exactly this ("An unreadable or foreign journal would reset the key to Planned"). The runner does not.
**Fix:** Skip projection and persist when the replay is not readable, or belongs to another plan, and keep the last good state:
```csharp
var replay = GenerationJournal.Replay(input.CaseFolder, plan);
if (!replay.Readable && replay.Exists || replay.PlanChanged)
{
    // Keep the previous projection; report instead of overwriting the key with an empty run
    ctx.Snapshot.AddEvent(new GenerationEvent { Kind = GenerationEventKind.RunStopped, LocalTime = DateTime.Now, Reason = replay.Reason });
    return new GenerationCheckpoint { State = ctx.LastGoodState, Ledger = ctx.LastGoodLedger, IsFinal = final, Outcome = outcome, Message = message ?? string.Empty };
}
```
Also treat the `catch (Exception)` fallback at lines 341-346 the same way: never project an empty `GenerationRunState`.

## Warnings

### WR-01: The live dashboard double-counts failures during Retry Failed Threads / Continue

**File:** `Services/Evidence/GenerationRunner.cs:129-142`; `Services/Evidence/GenerationSnapshot.cs:71-100`; `UserControls/StepGenerateCorpus.cs:671-672`
**Issue:** `snapshot.Reset` seeds `FailedThreads` and `wave.Failed` from the replayed state, which includes threads that failed in earlier runs. This run then retries those threads. `RecordThread(Written)` increments `DoneThreads` but never decrements `FailedThreads`, and a thread that fails again increments `FailedThreads` a second time. During the run:
- Done + Failed can exceed Total.
- The Failed tile shows failures that are already fixed.
- The progress bar (`DoneThreads + FailedThreads`) starts part-filled and then saturates early.

The final view is correct because it is rebuilt from the replay. The live view is wrong for the whole run.
**Fix:** Pass the thread's previous status into `RecordThread`. If it was `Failed`, decrement `FailedThreads` and `wave.Failed` before applying the new status. Alternatively, have the runner record a "retrying" transition when a previously failed thread starts.

### WR-02: Spend on earlier failed attempts and on fatal-stop calls is dropped from the recorded cost

**File:** `Services/Evidence/GenerationRunState.cs:353-356`; `Services/Evidence/ThreadUnitOfWork.cs:76-77, 230`
**Issue:** `CostUsd` is summed only over `replay.LastByThread`, which is the last record per thread. When a thread fails (cost journaled) and is later retried successfully, the `ThreadFailed` record is replaced and its cost disappears from `GenerationState.CostUsd` and from the Cost tile. The comment at ThreadUnitOfWork.cs:230 ("GenerationRunState sums cost over ThreadDone and ThreadFailed records") does not describe what the code does.

A second leak: when a fatal provider error happens, the unit returns `NotRun` without a journal line. Tokens billed for that thread's earlier attempts, and for the fatal call itself (`PlanCallOutcome` carries them), are never recorded. The project constraints call for cost control, so the reported spend should not under-count.
**Fix:**
- In `Replay`, accumulate cost across every `ThreadDone` and `ThreadFailed` record of the current plan (for example `replay.TotalCostUsd`), and use that in `From`.
- For `NotRun`, journal a cost-only record, or add the tokens to the `RunEnded` record.

### WR-03: The UI-thread marshalling falls back to the worker thread when the handle is gone, and can leave the runner waiting forever

**File:** `UserControls/StepGenerateCorpus.Run.cs:455-494, 497-508, 861-883`
**Issue:** There are two edge cases.
1. `if (!InvokeRequired) return ApplyCheckpointAsync(checkpoint);` runs on the pool thread. WinForms `InvokeRequired` returns **false** when no window handle exists in the parent chain. While the step is disposing, the handle is destroyed before `IsDisposed` becomes true, so `ApplyCheckpointAsync` can run on the worker thread. It then mutates `CaseDraft`, calls `SetStatus` on controls and starts `SaveAsync` off the UI thread, which breaks T-05-42.
2. `Dispose` calls `ReleasePendingCheckpoints()` before `base.Dispose` destroys the handle. If a worker adds a TCS and calls `BeginInvoke` after the release but before the handle is destroyed, the posted message is dropped. Nothing ever completes the TCS, and the runner waits forever inside `ProjectionGate` (leaked task and semaphore). This path is reachable on flow switch (`BuildSteps` disposes steps) and when `CancelAndWaitForRun` times out.

**Fix:** Never apply a checkpoint inline from `PersistOnUiThreadAsync`. Check `IsHandleCreated && !IsDisposed && !Disposing`, otherwise return `Task.CompletedTask`. Set a `_disposed` flag under `_pendingLock` before releasing, and in `PersistOnUiThreadAsync` complete the TCS immediately when that flag is set:
```csharp
lock (_pendingLock)
{
    if (_closing) return Task.CompletedTask;
    _pendingCheckpoints.Add(tcs);
}
```

### WR-04: Relevant threads with no messages count toward the total but are never scheduled, so the run never finishes cleanly

**File:** `Services/Evidence/WavePlanner.cs:148`; `Services/Evidence/GenerationRunState.cs:467-469`
**Issue:** `WavePlanner.Plan` drops relevant threads whose `Messages` is empty. `GenerationRunState.RelevantThreads` does not, so `TotalThreads` includes them and `Pending` never reaches 0. The run still reports `Finished`, but `ShowRunResult` computes `partial = Done + Failed < Total` and shows the "Continue Generating" banner. Continue then does nothing, every time.
**Fix:** Apply the same filter in both places. Use one shared predicate, `t.Relevance == Relevant && !string.IsNullOrEmpty(t.Id) && t.Messages?.Any(m => m != null) == true`, in `WavePlanner`, `GenerationRunState`, `RenderAssignments` and the step's `RelevantThreads`.

### WR-05: Start Over deletes the journal even when it could not list the journal's files, leaving old EMLs untracked

**File:** `Services/Evidence/GenerationReset.cs:163, 188-189`; `Services/Evidence/GenerationJournal.cs:186-188, 199-203`
**Issue:** `ListAllFiles` quietly returns an empty or partial list when the journal is over the size cap or an IO error happens mid-read. `DeleteAll` then deletes only the paths derived from the **current** plan, and deletes the journal anyway. EMLs written under an earlier plan (the Plan Changed card is the main reason to Start Over) are left in `Mailboxes/`. No record of them remains, so no later Start Over can find them. The case folder ends up with stale evidence that is not in the answer key, which damages the corpus's core value.
**Fix:** Have `ListAllFiles` report whether it read the whole file, for example `(List<string> Files, bool Complete)`. If it is not complete, do not delete the journal: add an error to `result.Errors` and leave the journal in place so a later Start Over can retry.

### WR-06: Duplicate thread ids in a plan run the same thread twice at the same time

**File:** `Services/Evidence/WavePlanner.cs:186-193`; `Services/Evidence/GenerationRunner.cs:175, 195-199`
**Issue:** The runner removes duplicate threads (`GroupBy(t => t.Id)`), but `WavePlanner` does not. A plan with a repeated id puts it in `wave.ThreadIds` twice, so `pending` holds it twice. Two units of work then render, write the same EML paths and journal two records for one thread in parallel. That pays twice and creates write races on the same target files. `corpus-plan.json` is read from disk and is not trusted (T-04-xx), so this can happen without a code path generating the duplicate.
**Fix:** Remove duplicates in `WavePlanner.Plan` (`.GroupBy(t => t.Id).Select(g => g.First())` before ordering), and use `.Distinct(StringComparer.Ordinal)` on `pending` in the runner.

### WR-07: `RunAsync` can still throw, and leave the snapshot stuck in "running", if Persist raises OperationCanceledException

**File:** `Services/Evidence/GenerationRunner.cs:409-419, 249-259`
**Issue:** The persist catch excludes OCE (`when (ex is not OperationCanceledException)`). `PersistOnUiThreadAsync` passes any exception from `ApplyCheckpointAsync` or `SaveAsync` through `tcs.TrySetException`. If a cancellation-type exception surfaces there during the final checkpoint in `finally`, it escapes `RunAsync`. That breaks its "never throws" contract and skips `FinishSnapshot`, so `Running` stays true and no `RunEnded` event is added.
**Fix:** Catch every exception from `ctx.Persist` inside `CheckpointCoreAsync`, since persistence is best-effort by design. Alternatively, wrap the `finally` body so that `FinishSnapshot` always runs.

## Info

### IN-01: `Replay` dereferences `plan.Threads` without a null check, despite its "never throws" contract

**File:** `Services/Evidence/GenerationJournal.cs:113-115`
**Issue:** `plan.Threads.Where(...)` sits outside the try block. Every other projection uses `plan?.Threads ?? new List<PlannedThread>()`.
**Fix:** `(plan?.Threads ?? new List<PlannedThread>()).Where(...)`.

### IN-02: Wave ordering and ledger date gating use different definitions of a thread's "first message"

**File:** `Services/Evidence/MessageTime.cs:312-317`
**Issue:** `FirstUtc` takes the minimum `SentAt` across messages, but `FirstLocalDate` and `LastLocalDate` take the lowest and highest `Seq`. If Seq order and time order disagree, a thread is placed in a wave by one date and gated (`LedgerSelector`, `KnowledgeGate`) by another.
**Fix:** Pick one definition (chronological is the safer choice) and use it in all three helpers.

### IN-03: A journal line that parses to a default record (for example `{}`) flips the whole journal to "Plan changed"

**File:** `Services/Evidence/GenerationJournal.cs:134-141, 263-269`
**Issue:** `{}` deserializes to `Type = RunStarted` (enum value 0) with `PlanId = Guid.Empty`. That record sets `PlanChanged = true`, and the only way out the UI offers is the destructive Start Over. Torn writes are very unlikely to produce such a line, but a hand edit or a partially synced copy can.
**Fix:** Skip records with `PlanId == Guid.Empty` (count them in `SkippedLines`) instead of treating them as a different plan.

### IN-04: Story-so-far export from the Case Summary silently reports "0 generated threads" when the journal is unreadable

**File:** `UserControls/StepCaseSummary.cs` (`ExportStoryAsync`, the `Task.Run` block)
**Issue:** The comment says "an unreadable journal still exports the ledger". It does, but the header line built by `StorySoFarDocxBuilder.Build` ("from 0 generated threads") is wrong, and the warnings section is empty.
**Fix:** When `!replay.Readable`, fall back to the cached counts in `GenerationState`, or leave the thread count out of the header.

### IN-05: Mailbox writes and Start Over deletes follow junctions inside the case folder

**File:** `Services/Evidence/MailboxWriter.cs:379-387`; `Services/Evidence/GenerationReset.cs:177-186`
**Issue:** `ResolveUnderCase` checks the lexical full path only. A junction or symlink at `Mailboxes/<folder>` that points outside the case folder would let writes and Start Over deletes reach the target. The case folder is user-owned, so the risk is low, but T-05-27 states "never leave the case folder".
**Fix:** Before writing or deleting, reject paths where any directory segment under the case root has `FileAttributes.ReparsePoint` set.

---

_Reviewed: 2026-10-03T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
