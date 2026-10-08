---
phase: 07-scale-cost-qa-cutover
reviewed: 2026-10-08T17:00:00Z
depth: standard
files_reviewed: 71
files_reviewed_list:
  - Services/Ai/AnthropicBatchTransport.cs
  - Services/Ai/AnthropicTextProvider.cs
  - Services/Ai/BatchLedger.cs
  - Services/Ai/BatchStage.cs
  - Services/Ai/BatchingLlmProvider.cs
  - Services/Ai/IBatchTransport.cs
  - Services/Ai/LlmGateway.cs
  - Services/Ai/LlmProviderFactory.cs
  - Services/Ai/LlmProviderRequest.cs
  - Services/Ai/LlmRequest.cs
  - Services/Ai/LlmResult.cs
  - Services/Ai/OpenAIBatchTransport.cs
  - Services/Ai/OpenAITextProvider.cs
  - Services/Ai/RequestIdentity.cs
  - Services/Ai/TokenUsage.cs
  - Services/ApiKeyStore.cs
  - Models/AIModelConfig.cs
  - Models/TokenUsageTracker.cs
  - Models/WizardState.cs
  - Models/Evidence/JournalRecord.cs
  - Services/Corpus/GenerationCostEstimator.cs
  - Services/Corpus/PlanCallRunner.cs
  - Services/Evidence/GenerationJournal.cs
  - Services/Evidence/GenerationReset.cs
  - Services/Evidence/GenerationRunState.cs
  - Services/Evidence/GenerationRunner.cs
  - Services/Evidence/GenerationSnapshot.cs
  - Services/Evidence/ThreadInvalidation.cs
  - Services/Evidence/ThreadUnitOfWork.cs
  - Services/Evidence/AttachmentUnitOfWork.cs
  - Services/Evidence/AttachmentRepair.cs
  - Services/Evidence/AttachmentRenderer.cs
  - Services/Evidence/ThreadRenderer.cs
  - Services/Evidence/FillerRenderer.cs
  - Services/Evidence/ThreadRenderContext.cs
  - Services/Evidence/EvidencePrompts.cs
  - Services/Evidence/CastVoiceBuilder.cs
  - Services/Evidence/SideWorldBuilder.cs
  - Forms/BlindReviewDialog.cs
  - Forms/QaFindingsDialog.cs
  - Forms/WizardForm.cs
  - Helpers/BulkMailFormatter.cs
  - Helpers/FileNameHelper.cs
  - Helpers/HtmlEmailFormatter.cs
  - Helpers/ThemeSanitizer.cs
  - Helpers/ThreadingHelper.cs
  - Models/Evidence/GenerationProgress.cs
  - Models/Evidence/GenerationState.cs
  - Models/MediaSettings.cs
  - Models/Qa/BlindReviewVerdict.cs
  - Models/Qa/LeakageReport.cs
  - Models/Qa/ScanReport.cs
  - Program.cs
  - Services/Corpus/NonRelevantSkeletonBuilder.cs
  - Services/Evidence/InviteScheduler.cs
  - Services/Evidence/OrganizationThemes.cs
  - Services/Evidence/RenderProfile.cs
  - Services/Qa/BlindReviewPrompts.cs
  - Services/Qa/BlindReviewer.cs
  - Services/Qa/CorpusScans.cs
  - Services/Qa/LeakageAudit.cs
  - Services/Qa/LogisticRegression.cs
  - Services/Qa/QaReportStore.cs
  - Services/Qa/QaRunner.cs
  - UserControls/GenerationUiText.cs
  - UserControls/ModelConfigurationDialog.cs
  - UserControls/QaUiText.cs
  - UserControls/StepApiConfiguration.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepGenerateCorpus.Run.cs
  - UserControls/StepGenerateCorpus.cs
findings:
  critical: 0
  warning: 0
  info: 0
  total: 0
status: issues_found
parts: merged from two parallel reviewers (A: AI/batch/cost/runner, B: QA/UI)
---

# Phase 7 Code Review (merged)

## Part A: AI, batching, cost, runner


# Phase 07 (part A): Code Review Report

**Reviewed:** 2026-10-08
**Depth:** standard (diff cbe36a1..HEAD)
**Files Reviewed:** 38
**Status:** issues_found

## Summary

Scope: the batching decorator (`BatchingLlmProvider`), both batch transports, the batch ledger, cost accounting (TokenUsage/CostBreakdown, gateway, estimator, journal replay), the generation runner, and thread invalidation.

The overall design is careful. Every request has a deterministic custom id, the Submitted line is flushed before anyone waits, and there are explicit live fallbacks. The defects sit at the edges of that design:

- A Detach during submission can leave a provider batch that is billed but never written to the ledger.
- The ledger fails open when a read is blocked by a sharing violation. That can cause the app to resubmit work it already paid for, or to delete the ledger without cancelling anything.
- Consumed is keyed by custom id across all batches. That breaks recovery after "Regenerate thread".
- Orphan spend can be lost for good: the OrphansBilled line is written before RunEnded, and results from another provider are priced at $0 but still marked billed.

There is also a cluster of robustness and accounting warnings. One malformed result line fails a whole batch. OpenAI input files leak. Snapshot rows can stay stuck on "waiting". The header ticker misses orphan spend.

No API-key leakage was found. Error messages are built from `SafeMessage` / provider `error.message` / exception type names only, and the ledger and results cache store no headers. File names derived from provider ids go through `SafeFileId` (regex allowlist or SHA-256), so there is no path traversal.

## Critical Issues

### CR-A01: Detach cancels an in-flight batch submit, so a paid provider batch can exist with no ledger line (BLOCKER)

**File:** `Services/Ai/BatchingLlmProvider.cs:613`, `Services/Ai/OpenAIBatchTransport.cs:107-119`, `Services/Ai/AnthropicBatchTransport.cs:53`
**Issue:** `SubmitAsync` passes `_cts.Token` to `_transport.SubmitAsync`. `Detach()` cancels `_cts` from the runner's `finally`, including on user cancel (`GenerationRunner.cs:560-564`). Consider a user who presses Stop while a 2,000-item Stage B chunk is uploading or while `CreateBatchAsync` / `Batches.Create` waits on its response. The request may already be accepted by the provider, but the OCE escapes and `FlushLoopAsync` catches it (`:558-563`). No Submitted line is written, no cancel is sent and the input file is not deleted. The provider then runs and bills a batch the app never tracks or reports. This breaks the RUN-04/D-09 invariant ("every submitted batch is in the ledger before anyone waits on it"). Detach is not the only route: cancelling while the upload is in progress also leaks the uploaded prompt file.
**Fix:** Never cancel the create call itself. Check for cancellation before the upload starts, then submit and append the ledger line with `CancellationToken.None`:
```csharp
_cts.Token.ThrowIfCancellationRequested();            // before any bytes leave
var submit = await _transport.SubmitAsync(requests, CancellationToken.None);
// ... ledger append (already CancellationToken.None) ...
// If _cts was cancelled meanwhile: still record + start nothing, or record and CancelAsync best-effort
```

### CR-A02: Consumed is keyed by custom id across all batches, so Regenerate plus restart double-pays or drops paid results (BLOCKER)

**File:** `Services/Ai/BatchLedger.cs:211-214`, `Services/Ai/BatchingLlmProvider.cs:117`, `:132`
**Issue:** `ConsumedCustomIds` is a flat set of custom ids. Request ids are deterministic, so "Regenerate thread" on a non-relevant thread re-sends the same request (filler has no PriorDeltas) with the same id, X, which already appears in an older Consumed line. Two failures follow after a restart:
1. **Re-pay:** while the new batch is still pending, `Recover` skips X when it rebuilds `_inflight` (`:132`, `!state.ConsumedCustomIds.Contains(id)`). The unit therefore queues X into another new batch and pays a second time. When the resumed batch is collected, its X result has no waiter and becomes an orphan, so the run pays a third time.
2. **Lost result:** once the new batch is collected but not yet consumed, `Recover` treats X as consumed (`:117`). It is never served and never billed as an orphan, so spend the provider billed is never reported.
**Fix:** Key consumption by `(batchId, customId)`: `HashSet<(string BatchId, string CustomId)>`. Check `state.IsConsumed(record.BatchId, id)` in both `Recover` loops. The Consumed record already carries `BatchId`.

### CR-A03: Orphan spend is marked billed in the ledger before it is journaled; a failed RunEnded loses it for good (BLOCKER)

**File:** `Services/Ai/BatchingLlmProvider.cs:192-213`, `Services/Evidence/GenerationRunner.cs:536-541`, `:1128-1143`, `:1166-1169`
**Issue:** `BillOrphansAsync` appends `OrphansBilled` to `provider-batches.jsonl` and removes the results from `_available`. Only afterwards does the runner write the spend into the RunEnded journal line, via `CheckpointAsync(final: true)` → `AppendRunEndedAsync`, which swallows every exception. If that append fails (journal locked, disk full, `JournalFullException`) or the process dies between the two writes, every later session skips the batch (`Recover` `:110`) and the spend is never reported anywhere. The comment at `GenerationRunner.cs:1140` ("an unbilled orphan stays in the ledger and the next finished run bills it") is wrong once the ledger line exists. This violates D-10/D-16 (one real total).
**Fix:** Make the journal the commit point. Compute the orphan cost without side effects, append RunEnded, and only after that succeeds append `OrphansBilled` and drop the results from `_available`. For example, split it into `PriceOrphans()` and `MarkOrphansBilledAsync(batchIds)`.

### CR-A04: Orphans from another provider or from an unpriced model are billed at $0 and still marked billed (BLOCKER)

**File:** `Services/Ai/BatchingLlmProvider.cs:108-125`, `:184-187`; `Models/WizardState.cs:110`
**Issue:** The `Recover` loop over collected batches filters by plan only, not by provider. Suppose the user switches text provider between sessions. Collected but unconsumed results from the old provider's batches are loaded into `_available`. They can never be consumed, because ids are hashed with `_live.Kind`. At run end, `_priceLookup` returns null, since the lookup filters on `m.Provider == provider` (the current provider). `cost.Add(null)` adds $0, but the tokens are still counted and `OrphansBilled` is still appended. The same happens for any ledger item with an empty or edited `ModelId`. Real provider spend is permanently reported as $0.
**Fix:** Price orphans with a lookup by `(record.Provider, ModelId)` across the whole catalog, not just the current provider. If the price is still null, skip appending `OrphansBilled` for that batch and surface a warning instead of recording $0:
```csharp
var model = _priceLookup(orphan.ModelId);
if (model == null) { unpriced = true; continue; }   // leave batch un-billed
```

### CR-A05: `BatchLedger.Replay` treats any IOException, including its own sharing violation, as an empty ledger (BLOCKER)

**File:** `Services/Ai/BatchLedger.cs:115`, `:149-156`; callers `BatchingLlmProvider.cs:97`, `:234`
**Issue:** `AppendAsync` opens the file with `FileAccess.ReadWrite, FileShare.Read`. `File.ReadAllLines` opens with `FileShare.Read`, which is refused while another handle has write access. Any `Replay()` that overlaps an append therefore throws IOException, and Replay returns an empty state. The overlap can come from another `BatchLedger` instance (the UI's `BatchLedger.PendingBatchCount`, Start Over's `new BatchLedger(folder)`), from a poll loop still appending, or from OneDrive or antivirus holding the file. The callers act on that empty state as if it were true:
- `BatchingLlmProvider` constructor → `Recover` sees no pending or collected batches, so Continue resubmits everything (the double payment D-09 forbids).
- `CancelPendingAsync` (Start Over) cancels nothing, then `GenerationReset` deletes the ledger, so running batches are never cancelled or tracked again.
**Fix:** Read with `new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)` and retry briefly on IOException, as `SaveResults` does. Report "unreadable" separately from "empty" (e.g. `BatchLedgerState.Readable`). If the ledger is unreadable, the constructor should refuse batch mode and fall back to live-only, and Start Over should not delete it.

## Warnings

### WR-A01: Consumed is written before delivery, so a cancelled or crashed unit's batch spend is never reported and the request is paid again (WARNING)

**File:** `Services/Ai/BatchingLlmProvider.cs:893-897`, `:431-440`, `:987-992`
**Issue:** `DeliverAsync` appends Consumed before the waiters complete. Suppose the unit is cancelled between that point and its journal line, or the app crashes before the unit journals. The result will never be served again, `Recover` skips consumed ids, and the tokens/cost were attached only to the dropped `LlmResult`, so nobody reports them. Continue then re-sends the same request and pays again. The same race applies to `CompleteResult`: if waiter 0, the only one with `CarriesUsage = true`, was cancelled concurrently, the other waiters report 0 tokens for a billed item.
**Fix:** Write Consumed from the gateway/unit after its journal line is flushed, or treat consumed-but-unjournaled results as orphans at the next Recover. Give usage to the first waiter that actually observes the result instead of index 0.

### WR-A02: One malformed result line fails the whole batch, and it retries forever (WARNING)

**File:** `Services/Ai/OpenAIBatchTransport.cs:314-385`, `Services/Ai/AnthropicBatchTransport.cs:102-105`, `:162-180`
**Issue:** `ParseResultLine` catches only `JsonException`, `FormatException`, `InvalidOperationException` and `KeyNotFoundException`. `ModelReaderWriter.Read<ChatCompletion>` or `Classify` can throw other types on a hostile or unexpected body (`ArgumentException`, `NullReferenceException`, `OverflowException`). One bad line then reaches `GetResultsAsync`'s catch-all, and the entire download is reported as failed. Anthropic has the same problem: any exception in `ToItemResult`/`Classify` fails the whole stream. After 5 attempts, `PollLoopAsync` releases every waiter to live at full price. The batch is never marked Collected, so every later session downloads, fails and pays live again.
**Fix:** In `ParseResultLine` and around `ToItemResult`, catch `Exception ex when ex is not OperationCanceledException` per line and map it to an Errored item (or skip it), so a single line cannot poison the batch.

### WR-A03: Uploaded OpenAI batch input files (full prompts) leak at the provider (WARNING)

**File:** `Services/Ai/BatchingLlmProvider.cs:615-623`, `:649-655`; `Services/Ai/OpenAIBatchTransport.cs:129-134`, `:228`
**Issue:** `BatchSubmitResult.InputFileId` is returned on failure ("lets the caller clean it up"), but `SubmitAsync` ignores it. The ledger-append-failure path cancels the batch but does not delete its input file. Files are deleted only on Start Over or when stale batches are closed, never after a normal collect. The comment "files expire after 30 days" is not a guarantee: OpenAI batch-purpose files persist until deleted unless `expires_after` is set. Case prompts (fictional, but user-supplied) stay at the provider indefinitely.
**Fix:** Call `_transport.DeleteFilesAsync(submit.InputFileId, null, CancellationToken.None)` on both failure paths. After `CollectAsync` has saved the results, delete the input and output files best-effort.

### WR-A04: A SendAsync racing with Detach can wait forever (WARNING)

**File:** `Services/Ai/BatchingLlmProvider.cs:346`, `:377-419`, `:499-563`
**Issue:** `SendAsync` checks `_cts.IsCancellationRequested` outside `_lock`. If `Detach` runs between that check and the lock, Detach's stranded-waiter sweep misses the new waiter. `FlushLoopAsync` then throws OCE at `Task.Delay` with `taken == null`, so nothing completes the waiter. The caller hangs until its own token is cancelled.
**Fix:** Re-check `_cts.IsCancellationRequested` inside the lock and go live if it is set. In `FlushLoopAsync`'s OCE handler, also release every item still in `_queues[label]`.

### WR-A05: Start Over cancels only the current plan's batches, then deletes the whole ledger (WARNING)

**File:** `Services/Ai/BatchingLlmProvider.cs:236`, `:269-271`; `Services/Evidence/GenerationReset.cs:94-95`
**Issue:** `CancelPendingAsync(…, planId)` cancels and deletes files only for `planId`. After a re-plan, batches from an earlier plan id that are still pending are neither cancelled nor cleaned up, and `GenerationReset` then deletes `provider-batches.jsonl`. Those batches keep running and billing with no record left, and their OpenAI input files are never deleted. The same applies to a provider whose key is no longer present (`transportFor` returns null → `continue`).
**Fix:** For Start Over, cancel and delete files for every pending record regardless of plan (pass `include: _ => true` with a plan-agnostic pending query). If any record could not be cancelled, keep the ledger rather than deleting it.

### WR-A06: Orphans are billed only on Finished; a cancelled or fatal run followed by Start Over never reports them (WARNING)

**File:** `Services/Evidence/GenerationRunner.cs:532-537`
**Issue:** Collected but unconsumed results after a Cancelled or Fatal run stay in `_available` and in `batch-results/`. If the user then chooses Start Over, the ledger and results are deleted, and that billed spend never reaches any RunEnded line.
**Fix:** Bill orphans for every outcome except a plain user cancel that will be continued, or bill them at Start Over before deleting the ledger.

### WR-A07: Snapshot batch rows can stay stuck on "Waiting on provider batch" (WARNING)

**File:** `Services/Evidence/GenerationSnapshot.cs:271-312`; `Services/Ai/BatchingLlmProvider.cs:314`, `:704`, `:736`
**Issue:** `RecordBatchStatus` removes a track only on a terminal event. `ReleaseBatch` raises no event when it releases after repeated fatal poll failures (`:704`), after result-download failures (`:736`), after `CancelPendingStagesAsync` closes a batch (`:314`), or from the bookkeeping catch (`:759`). The stage row keeps `BatchWaiting = true`, the strip keeps counting down to a next check that will never come, and `_pendingProviderBatches` is not decremented.
**Fix:** In `ReleaseBatch`, raise a terminal `BatchStatusEvent`, for example `FailedAtProvider` with the reason "stopped waiting; items run live", so the snapshot drops the track.

### WR-A08: Relevant (live) scopes ignore items already in flight in resumed batches, so they pay twice (WARNING)

**File:** `Services/Ai/BatchingLlmProvider.cs:352-371`
**Issue:** Under `BatchStage.IsLive`, only `_available` is checked. A request whose id is already in a resumed pending batch (a wave batch submitted by an earlier build or session) is sent live anyway. The batch item is still billed: it becomes an orphan if collected, or is closed by `CloseStaleWaveBatchesAsync` with its spend explicitly "not itemized". The cost ticker under-reports in this path.
**Fix:** At minimum, on Finished, cancel stale wave batches before the relevant waves start rather than after the run, so the provider stops processing them early. Also record their partial spend once collected instead of declaring it un-itemized.

### WR-A09: Orphan spend never reaches the session TokenUsageTracker (WARNING)

**File:** `Services/Evidence/GenerationRunner.cs:1128-1143`
**Issue:** `AddOrphanSpendAsync` adds the orphan cost to the journal (RunEnded) and to `snapshot.AddCost`, but not to `UsageTracker`, which drives the wizard header ticker and its D-16 tooltip breakdown. The header total and the journaled total then diverge.
**Fix:** Expose a `RecordUsage` overload for pre-priced batch spend, or pass the tracker into `BillOrphansAsync`, and record the orphan breakdown (including `BatchSavingsUsd`) there.

### WR-A10: Thread invalidation deletes the EMLs before journaling; if the append fails, the thread is "Written" with no files (WARNING)

**File:** `Services/Evidence/ThreadInvalidation.cs:64-84`
**Issue:** `DeleteCopies` runs first. If the `ThreadInvalidated` append then fails (journal locked, IO error), the journal still says `ThreadDone`. The answer key keeps listing documents that no longer exist, and Continue will not re-render the thread because `IsDone` is true. Exceptions other than IO and authorization exceptions (e.g. `JournalFullException`) also escape after the deletion.
**Fix:** Append `ThreadInvalidated` first, listing the files to be deleted. Then delete them; any copy left behind is overwritten by the next run, as the doc comment already promises. Catch `Exception ex when ex is not OperationCanceledException` around the append.

### WR-A11: A tampered or inconsistent CostDetail can produce negative totals and break the replay invariant (WARNING)

**File:** `Models/Evidence/JournalRecord.cs` (Normalize, CostDetail block); `Services/Evidence/GenerationJournal.cs:347-363`
**Issue:** Each category is clamped to at least 0, but `BatchSavingsUsd` is not capped at the category sum. As a result, `CostDetail.TotalUsd` can be negative and `SpentDetail` can go negative. When `CostDetail.TotalUsd > CostUsd`, `UnsplitSpentUsd` is clamped to 0 while `SpentDetail` still adds the larger detail, so the documented invariant `SpentDetail.TotalUsd + Media + Unsplit == SpentCostUsd` no longer holds.
**Fix:** In Normalize, clamp `BatchSavingsUsd` to at most the sum of the four categories. In `AddSpend`, when `detail.TotalUsd > text`, either drop the detail (count everything as unsplit) or scale it.

### WR-A12: The batch estimate contradicts D-17 and the D-10 retry pricing (WARNING)

**File:** `Services/Corpus/GenerationCostEstimator.cs` (`Price`, `Split` in `PriceWork`)
**Issue:** D-17 requires both totals to include the expected cache effect, but `Price(..., batch: true)` never applies cache reads. Separately, `Split` multiplies the batch-priced count by `QaOverhead`. That overhead models retries, which D-10 and the gateway always run live at full price, so the retry share is under-priced by the batch discount.
**Fix:** Price the base call at the batch rate and the overhead portion at the live rate: `batchCount * (Price(batch) + (QaOverhead - 1) * Price(live))`. Either apply cache reads in batch mode where the provider supports them (Anthropic) or document the D-17 deviation.

## Info

### IN-A01: `_cts` in BatchingLlmProvider is never disposed (WARNING, minor)

**File:** `Services/Ai/BatchingLlmProvider.cs:76`, `:476-495`
**Issue:** The `CancellationTokenSource` is cancelled in `Detach` but never disposed. This is a small resource leak per run.
**Fix:** Dispose it in `Detach` after the loops observe cancellation, or make the provider `IDisposable`.

### IN-A02: `ReasonFor` maps any message containing "limit" to "over the provider's size limit" (WARNING, minor)

**File:** `Services/Ai/BatchingLlmProvider.cs:1017`
**Issue:** "rate limit", "spend limit" and "token_limit_exceeded" (an enqueued-token cap) are all shown to the user as a size problem.
**Fix:** Match specific phrases ("too large", "max file size", "token_limit") and add a separate quota reason.

### IN-A03: The slow-poll switch measures from the session's poll start, not from submission (WARNING, minor)

**File:** `Services/Ai/BatchingLlmProvider.cs:682`, `:689`
**Issue:** A resumed batch submitted hours ago polls at the fast 30 s interval for another 10 minutes.
**Fix:** Use `Now() - batch.SubmittedUtc` in `IntervalFor`.

---

_Reviewed: 2026-10-08_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_


## Part B: QA services and UI


# Phase 7 (part B): Code Review Report

**Reviewed:** 2026-10-08T16:24:17Z
**Depth:** standard
**Files Reviewed:** 33
**Status:** issues_found

## Summary

Scope was the changes since `cbe36a1`: the QA services (blind review, leakage audit, corpus scans, report store, end-of-run runner), the QA dialogs and Case Summary rows, the batch additions on Generate Corpus, the theme sanitizer extraction and the legacy-flow cutover.

Things that checked out:
- Path traversal on Open Email. `OpenEmail` goes through `CopyPathsFor` (filtered by `IsSafeEmlPath`) and `ResolveUnderCase`, which checks that the full path stays under the case root.
- Theme CSS injection. Colors are limited to exactly 6 hex characters and fonts to an allowlist, on both the template and bulk paths.
- Blind-review prompts. The system prompt is built from the guide and the protocol prose only. `KeyRelevant`, `DocId` and `ThreadId` never reach the user prompt.
- Cross-thread progress. Review progress is marshalled with `BeginInvoke` or `SynchronizationContext.Post`, and stale counts are ignored.

Main problems:
1. Start Over always reports that pending provider batches were cancelled, even when the cancel failed.
2. Blind-review verdicts survive a thread regeneration, so findings describe emails that no longer exist.
3. Several async-void UI paths let unexpected exceptions escape and crash the app.
4. A cancel at the wrong moment can drop blind-review verdicts that were already paid for.

## Critical Issues

### CR-B01: Start Over always says pending provider batches were cancelled; the failure path never runs

**File:** `UserControls/StepGenerateCorpus.Run.cs:905-931` (with `Services/Ai/OpenAIBatchTransport.cs:191-210`, `Services/Ai/AnthropicBatchTransport.cs:121-135`, `Services/Ai/BatchingLlmProvider.cs:242-249`)
**Issue:** `CancelPendingBatchesAsync` returns `false` only when `BatchingLlmProvider.CancelPendingAsync` throws. Both real transports catch every non-caller exception inside `CancelAsync`: timeouts, 5xx errors, auth failures, DNS failures. `CancelPendingAsync` then records the batch as `Cancelled` and moves on. So `cancelFailed` is never true in production and `BatchCancelFailedText` is unreachable. The C-G2 text ("Pending provider batches are cancelled.") and the green "Started over" status are shown even when nothing was cancelled. `GenerationReset.StartOverAsync` then deletes the batch ledger, so the app loses every record of those batches. A batch that is still running at the provider can finish and be billed, and nothing tells the user.
**Fix:** Have the transports report whether the cancel worked (for example `Task<bool> CancelAsync`, or a result enum). Treat 404/409 "already ended" as success and everything else as failure. Make `CancelPendingAsync` return the IDs that failed, and keep their ledger records (or write a `CancelFailed` record) so `cancelFailed` reflects reality:
```csharp
var failed = await BatchingLlmProvider.CancelPendingAsync(...); // returns failed ids
return failed.Count == 0;
```

### CR-B02: Blind-review verdicts outlive Regenerate Thread, so stale verdicts and disagreements are reported on rewritten emails

**File:** `Services/Qa/BlindReviewer.cs:203-223`, `UserControls/StepCaseSummary.cs:271-278` (LoadQa), `Forms/QaFindingsDialog.cs:398-454`
**Issue:** DocIDs are deterministic from the plan, so a regenerated thread gets the same DocIDs. After Regenerate Thread... and Continue Generating, the current pass in `qa-blind-review.jsonl` still holds the old verdicts for those DocIDs:
- `RunAsync` puts them in `kept`/`reviewed` and never sends the new text to the reviewer.
- `LoadQa` counts them toward Done.
- The disagreement list shows reasons about the old email text.

Regenerate Thread is the action the dialog itself suggests for resolving a disagreement, so the "fixed" email never gets reviewed again and keeps showing as a disagreement. `ThreadInvalidation` only touches the journal and EMLs, never the QA log.
**Fix:** Tie each verdict to the content it judged. One option is to store the journal `ThreadDone` record's UTC (or a hash of subject + body) on `BlindReviewVerdict` and drop kept verdicts whose stamp no longer matches the replay in both `RunAsync` and `LoadQa`. Another is to have `ThreadInvalidation.InvalidateAsync` append a superseding line (for example `Kind = "Void", DocIds = [...]`) that `LoadBlindReview` uses to remove those DocIDs from the current pass.

## Warnings

### WR-B01: Cancelling between the restated header and the verdict re-append drops paid verdicts

**File:** `Services/Qa/BlindReviewer.cs:214-220`
**Issue:** When the sample size changes, the code appends a new header with `ct`, then re-appends every kept verdict, also with `ct`. `LoadBlindReview` drops everything before the latest header. A Cancel Review click, a step leave or an app close in that window (one fsync per verdict, so it can be long for big samples) leaves a new header followed by only some of the old verdicts. Already-paid verdicts are lost and get billed again on the rerun, which breaks D-13 ("a verdict that was paid for is always kept").
**Fix:** Write the header and the kept verdicts as one append that cannot be cancelled. Build all the lines, then make one `AppendLinesAsync(lines, CancellationToken.None)` call under the gate. Alternatively, have `LoadBlindReview` carry verdicts across headers when plan, provider and model match.

### WR-B02: QA load/reproject paths catch only a few exception types, and their callers are async void

**File:** `UserControls/StepCaseSummary.cs:280-281` (LoadQa), `:876` (ReprojectAfterRegenerateAsync), callers at `:81-85` (button `async` lambdas), `Forms/WizardForm.cs:358-389` (`async void ShowStep` awaits `OnEnterStepAsync` → `RefreshQaAsync`)
**Issue:** `LoadQa` claims to "never throw for unreadable files", but it only catches IO, UnauthorizedAccess, InvalidOperation, Argument and NotSupported exceptions. It runs `AnswerKeyBuilder.Build`, `BuildFullOverlay`, `BuildSystemPrompt` (`guide.Cast.Where`, `guide.Issues`, `protocol.Issues.FirstOrDefault`), `BuildDocuments` and `Estimate` over hand-editable `case.json` data. A null list or a duplicate key (`ToDictionary` in `BuildDocuments`) throws `NullReferenceException` or `ArgumentException`/`KeyNotFoundException`, and a malformed report gives `JsonException`/`FormatException`. Any of these escapes `RefreshQaAsync`. `RefreshQaAsync` is awaited in the `finally` of `RunAuditAsync`, `RunBlindReviewAsync` and `ReprojectAfterRegenerateAsync`, and in `OnEnterStepAsync`. All of those end in async-void handlers, so an escaped exception crashes the app on entering Case Summary. `ReprojectAfterRegenerateAsync` has the same narrow filter around `GenerationStateApplier.Apply` and `SaveAsync`.
**Fix:** These are report-only views, so catch everything except cancellation at the outer boundary:
```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // Report view: whatever was read so far is shown; the rest reads as Not run
}
```
Also wrap the body of `RefreshQaAsync` in the same guard.

### WR-B02a: Regenerate button handler is async void and rethrows OperationCanceledException

**File:** `Forms/QaFindingsDialog.cs:236`, `:441-444`
**Issue:** `_btnRegenerate.Click += async (s, e) => await RegenerateSelectedAsync();`. `RegenerateSelectedAsync` explicitly rethrows `OperationCanceledException`, and from an async-void lambda that goes to the dispatcher as an unhandled exception and crashes the app. `ThreadInvalidation.InvalidateAsync` does file IO and can surface a `TaskCanceledException` from an IO timeout.
**Fix:** Inside the dialog, treat a cancel as a footer message and do not rethrow:
```csharp
catch (OperationCanceledException) { ShowFooter(QaUiText.RegenerateFailed("cancelled"), Theme.Danger); }
```

### WR-B03: Closing QaFindingsDialog while a regeneration is in flight loses `QueuedAny`, so Case Summary is not reprojected

**File:** `Forms/QaFindingsDialog.cs:411-423`, `UserControls/StepCaseSummary.cs:744-758`
**Issue:** `_busy` disables the action buttons but not Close, Esc (`CancelButton`) or the title-bar X. If the user closes during `await _regenerate(...)`, `ShowDialog` returns and `ViewFindingsAsync` reads `QueuedAny == false` before the invalidation finishes. The thread is then invalidated in the journal and its EMLs deleted, but `ReprojectAfterRegenerateAsync` never runs: `case.json`'s cached counts and the Mailboxes row stay stale. The continuation also writes to a disposed dialog's controls.
**Fix:** Block closing while busy:
```csharp
protected override void OnFormClosing(FormClosingEventArgs e)
{
    if (_busy) { e.Cancel = true; return; }
    base.OnFormClosing(e);
}
```
Alternatively, set `QueuedAny = true` before awaiting and have the caller always reproject when it is set.

### WR-B04: Logistic regression uses a fixed step size of 0.5 with no stability or convergence guard

**File:** `Services/Qa/LogisticRegression.cs:29-86`
**Issue:** Batch gradient descent with `learningRate = 0.5` is stable only when `0.5 < 8 / λmax(ZᵀZ/n)`, roughly when the largest eigenvalue of the standardized feature covariance is below 16. `LeakageAudit.BuildFeatures` produces about 60-70 columns made of highly correlated one-hot blocks: up to 20 recipient one-hots that co-occur on distribution-list threads, sender one-hots that duplicate recipients within threads, an "other" column that is a linear function of its block, 7 weekday one-hots that sum to 1, and depth buckets that overlap the depth thresholds. On real corpora λmax can exceed 16. The weights then oscillate or diverge over the fixed 300 iterations, and the out-of-fold probabilities, balanced accuracy and Pass/Warning/Fail verdict become numerical noise. That is either a false Pass (hiding leakage) or a false Fail. Nothing checks for divergence.
**Fix:** Pick a provably stable step size, for example `lr = 4.0 / (maxRowSquaredNorm + lambda/n)`, using the bound `λmax ≤ max‖zᵢ‖²`. Or use backtracking or Newton/IRLS, which is cheap at this dimension. Also stop when the loss stops decreasing. Add a test with 20 perfectly correlated columns that checks the loss decreases monotonically.

### WR-B05: Start Over does not cancel an in-flight end-of-run QA pass, which can write stale reports after the reset

**File:** `UserControls/StepGenerateCorpus.Run.cs:139-182` (RunQaCoreAsync), `:809-900` (StartOverAsync)
**Issue:** `RunQaAfterRunAsync` is fire-and-forget with `_qaCts`, which is cancelled only on Dispose. Start Over checks `_isGenerating` but not `QaRun`. If the user clicks Start Over while the QA pass is running (the UI returns before QA finishes, by design), the QA task may already have replayed the old journal. `GenerationReset` then deletes `qa-scans.json` and `qa-leakage.json`, and QA saves them again afterwards. Case Summary then shows findings and a leakage verdict for a corpus that no longer exists. The leakage report has no `JournalUtc`, so nothing marks it out of date. A new run started during the QA pass has the same race.
**Fix:** Use a per-run token, cancel it and await `QaRun` before Start Over and before a new run starts:
```csharp
_qaRunCts?.Cancel();
if (QaRun != null) { try { await QaRun; } catch { /* report-only */ } }
```
Also stamp `LeakageReport` with the journal time, as `ScanReport` is, so a late write is recognised as stale.

### WR-B06: API step at 150% DPI: some rows and the layout `MinimumSize` stay in raw pixels, so the Instructions row collapses

**File:** `UserControls/StepApiConfiguration.cs:62`, `:69-70`, `:73-75`, `:77`
**Issue:** This change converted rows 0, 4 and 12 to `LogicalToDeviceUnits`, but rows 2, 3, 6 and 10 (24 px) and rows 7 and 8 (36 px), plus `MinimumSize = new Size(0, 520)`, are still raw device pixels. `TableLayoutPanel` absolute row styles are not auto-scaled. At 150% the scaled rows take about 414 px and the raw rows 168 px, already more than the 520 px minimum before padding. So when the window is near its minimum, the percent Instructions row (row 11) gets 0 height and is clipped instead of scrolling. The 24 px status rows also leave no room for 9 pt text at 150% (about 23 px line height).
**Fix:** Wrap every absolute row height and the minimum in `LogicalToDeviceUnits(...)`, and compute the minimum from the row sum:
```csharp
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(24)));  // 2: OpenAI status
...
MinimumSize = new Size(0, LogicalToDeviceUnits(560))
```

### WR-B07: `BlindReviewer.RunAsync` dereferences null documents before filtering them

**File:** `Services/Qa/BlindReviewer.cs:201`, `:223`
**Issue:** `requested` is built with `docs.Select(d => d.DocId)`, which throws `NullReferenceException` on a null entry. Line 223 then filters `d != null`, which shows the method is meant to tolerate nulls. The exception escapes into `RunBlindReviewAsync`, whose catch reports it as "Blind review stopped: Object reference not set...", which is not actionable.
**Fix:** `docs = docs.Where(d => d != null && !string.IsNullOrEmpty(d.DocId)).ToList();` at the top of the method.

## Info

### IN-B01: Leakage percentage is rounded but the verdict is not, so Warning can show "60%" against a "Pass is 60% or lower" tooltip

**File:** `UserControls/QaUiText.cs:186-193`, `:400`
**Issue:** A balanced accuracy of 0.6040 is classed Warning (`> 0.60 + 1e-9`) but displays as "Warning: ... 60%", while the tooltip says "Pass is 60% or lower".
**Fix:** Show one decimal place near the thresholds, or classify on the rounded value.

### IN-B02: `QaReportStore.LoadBlindReview` fails with a sharing violation while an append is open

**File:** `Services/Qa/QaReportStore.cs:68`, `:128-129`
**Issue:** The writer opens with `FileAccess.ReadWrite, FileShare.Read`. `File.ReadAllLines` asks for `FileShare.Read`, which does not allow the writer's existing write handle, so it throws `IOException`. The load then returns `(null, empty)`, and a refresh during a review (for example Run Audit's `finally`) briefly shows the row as Not run.
**Fix:** Read with `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)`.

### IN-B03: Scan findings sort by sender-local wall-clock time

**File:** `Services/Qa/CorpusScans.cs:344`, `:96-97`
**Issue:** `DateSent = MessageTime.Local(message).DateTime` drops the offset, so findings from senders in different zones sort out of real order.
**Fix:** Sort on `MessageTime.Local(message).UtcDateTime` and keep the local value for display only.

### IN-B04: Redundant case-insensitive lookup in `SanitizeFont`

**File:** `Helpers/ThemeSanitizer.cs:55-57`
**Issue:** `AllowedFonts` is already `OrdinalIgnoreCase`, so the `FirstOrDefault(... OrdinalIgnoreCase)` fallback can never match anything the `Contains` check missed. The "close match" comment is misleading.
**Fix:** If canonical casing is wanted, use `AllowedFonts.TryGetValue(font, out var canonical) ? canonical : null`.

### IN-B05: A stated invite time is now read in the case's canonical zone, not the sender's

**File:** `Services/Evidence/InviteScheduler.cs:105`, `:125`, `:365-371`
**Issue:** D-24 derives new slots in `CanonicalTimeZoneId`, including `candidate.StatedTime`. A Pacific sender whose ledger text says "2:00 PM" gets an invite at 2:00 PM in the case zone (for example 11:00 AM Pacific in their own view). That may contradict the email body that states the time.
**Fix:** Confirm this is intended. If the statement is the sender's local time, derive stated times in the sender's zone and use the canonical zone only for seeded slots.

### IN-B06: Batch Discount % column format hides fractional discounts

**File:** `UserControls/ModelConfigurationDialog.cs:195-202`
**Issue:** `Format = "0"` displays 12.5 as "13", while `CellParsing` accepts and stores 12.5, so the grid and the stored value disagree.
**Fix:** Use `"0.#"`.

---

_Reviewed: 2026-10-08T16:24:17Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
