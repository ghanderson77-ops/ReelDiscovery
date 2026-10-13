---
phase: 10-answer-key-batch-safety
reviewed: 2026-10-13T00:00:00Z
depth: standard
files_reviewed: 26
files_reviewed_list:
  - Forms/QaFindingsDialog.cs
  - Forms/ReplanDialog.cs
  - Forms/WizardForm.cs
  - Models/AttachmentType.cs
  - Models/Corpus/AnswerKeyRelabels.cs
  - Models/Evidence/GenerationProgress.cs
  - Models/WizardState.cs
  - Services/Ai/BatchLedger.cs
  - Services/Ai/BatchingLlmProvider.cs
  - Services/Corpus/AnswerKeyCsvWriter.cs
  - Services/Corpus/AnswerKeyFiles.cs
  - Services/Corpus/AnswerKeyReader.cs
  - Services/Corpus/AnswerKeyRelabelStore.cs
  - Services/Corpus/AnswerKeyXlsxWriter.cs
  - Services/Corpus/CorpusApproval.cs
  - Services/Evidence/GenerationOutputs.cs
  - Services/Evidence/GenerationRunner.cs
  - Services/Evidence/ThreadInvalidation.cs
  - UserControls/CorpusUiText.cs
  - UserControls/GenerationUiText.cs
  - UserControls/QaUiText.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepCorpusSettings.cs
  - UserControls/StepGenerateCorpus.Run.cs
  - UserControls/StepGenerateCorpus.cs
  - UserControls/StepReviewPlan.Run.cs
findings:
  critical: 2
  warning: 10
  info: 6
  total: 18
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-10-13
**Depth:** standard (diff `d0307d7..HEAD`, with callers and callees traced where the diff depends on them)
**Files Reviewed:** 26 (`Models/Attachment.cs` was deleted on purpose and is not reviewed)
**Status:** issues_found

## Summary

The relabel harvest uses per-file SHA-256 baselines (D-20), a per-case write gate, and a rule that never overwrites a file it could not read. Most of the stale-XLSX and locked-file paths I traced converge correctly. The stale-wave drain (D-17/D-21) is sound: there is no Cancelled line, `Ended` is signalled from `UntrackLocked`, and the drain is bounded and cancellable.

The remaining defects fall into four groups:

1. **Run start can be blocked for good.** If the other provider can no longer find a batch, or its results have expired, every run start is refused. The message says "try again in a moment", and the only in-app way out is Start Over, which deletes the corpus.
2. **User text in the answer key can be silently lost or corrupted:**
   - The rationale check treats any edit that starts with the projected text as no edit.
   - CSV files saved in ANSI are decoded as Latin-1.
   - A foreign plan's snapshot is pruned even when its file was not overwritten.
   - The strict legacy-ownership test drops every relabel in a file once a single row is added or deleted.
3. **Spend accounting gaps:**
   - Start Over records the unused-results bill before it knows the ledger was deleted.
   - Cross-provider running batches are recorded Cancelled, so the partial spend on them is never itemized.
4. **UI-thread work and unguarded exceptions:**
   - The harvest runs on the UI thread in Approve and in Start Over.
   - `PeekRelabels` is documented and called as "never throws", but it catches only I/O exceptions.
   - XLSX parsing has no cap on decompressed size.

## Critical Issues

### CR-01: A batch the other provider cannot find (404) or whose results expired blocks every run, and Start Over is the only fix

**File:** `Services/Ai/BatchingLlmProvider.cs:614-628` (`ResolveOtherProviderPendingAsync`), surfaced at `UserControls/StepGenerateCorpus.Run.cs:514-522`

**Issue:** For each pending batch from the other provider, the code treats `!status.Succeeded` and `!results.Succeeded` as `Fail(...)`, and that returns `Blocked`. The run is then refused with `RunRefusedOtherProviderFailedText` ("Try again in a moment"). Some failures are permanent, not transient:
- The batch was deleted at the provider, or the key now belongs to a different org or project. The status call returns 404.
- Anthropic batch results are kept for only about 29 days. After that, the status reads `ended` but `GetResultsAsync` returns 404.

In both cases nothing changes on retry, so Generate and Continue are refused on every attempt. The batch stays pending in the ledger. The only in-app escape is Start Over: `CancelAllPendingAsync` treats 404 on cancel as success, so the ledger is deleted, but so is every generated EML. A batch that does not exist, or whose results cannot be downloaded, can no longer bill, so refusing the run protects nothing (D-22 is aimed at "could not be reached", not "does not exist").

**Fix:** Map not-found to a terminal outcome instead of a failure. For example, add `LlmFailureKind.NotFound`, or a `NotFound` flag on `BatchStatusResult` / `BatchResultsResult`, set by both transports on 404. Then in the resolver:
```csharp
if (!status.Succeeded)
{
    if (status.NotFound)
    {
        // The provider no longer knows this batch: it cannot bill; close it so its items run once under the current provider
        await AppendCancelledAsync(ledger, record, ct);
        result.Cancelled++;
        continue;
    }
    return Fail(result, record.Provider, status.Message);
}
// same for results.NotFound: save an all-Expired cache and append Collected
```
Also, make the refusal copy name the provider error kind instead of always saying "try again in a moment".

### CR-02: A user Rationale that starts with the projected rationale is discarded (user edit silently lost)

**File:** `Services/Corpus/AnswerKeyFiles.cs:414-421` (`CustomRationale`)

**Issue:** The code returns `string.Empty` (meaning "not custom") whenever `text.StartsWith(baseText)`. The intent was to ignore the run note that the overlay appends (Pitfall 6). But the most natural way for a user to edit a rationale is to append to it, for example "...was relevant. Reviewer: actually only marginal, see 3/14 email." Every such edit on a relabeled row is classified as unchanged. The next write then replaces the cell with the projected rationale, and the user's text is gone from both files. This violates D-05 ("the user's Rationale cell, kept verbatim when it differs from the projected rationale") and D-23. No warning or backup is produced.

**Fix:** Compare against the exact rationale that was written, not a prefix. Either pass the overlay into `Harvest` and treat the text as unchanged only when it equals `projected` or `WithNote(projected, note)`, or store the written rationale per DocID in `AnswerKeyFileSnapshot` and compare to that:
```csharp
private static string CustomRationale(string? cell, string projected, string writtenWithNote)
{
    var text = Normalize(AnswerKeyReader.StripCsvGuard(cell)).Trim();
    if (text.Length == 0 || text == Normalize(projected).Trim() || text == Normalize(writtenWithNote).Trim())
        return string.Empty;
    return text.Length > AnswerKeyXlsxWriter.MaxCellLength ? text[..AnswerKeyXlsxWriter.MaxCellLength] : text;
}
```

## Warnings

### WR-01: A foreign plan's snapshot is pruned even when its file was not overwritten, so the next harvest falls back to legacy ownership

**File:** `Services/Corpus/AnswerKeyFiles.cs:129-132`

**Issue:** After the write loop, every snapshot whose `PlanId != plan.PlanId` is removed, whether or not that file was actually rewritten for the new plan. Example: a re-plan approval writes the CSV, then the XLSX write fails because Excel has it open. The old-plan XLSX stays on disk, but the snapshot that marked it as another plan's file is gone. On the next harvest the file has no snapshot, so `LegacyOwner` decides ownership by DocID count and thread id alone. `PlanNumbering` assigns DocIDs and thread ids sequentially, so a re-plan with the same structure (common for small corpora) passes that test. The old plan's labels then become "relabels" of the new plan wherever the two plans disagree. That brings old-plan relabels into the new plan (D-08 violation) and corrupts the new key.

**Fix:** Prune a snapshot only for a file that this call successfully wrote, or whose file no longer exists:
```csharp
var written = result.Files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
foreach (var name in set.Files.Where(f => f.Value == null
        || (f.Value.PlanId != plan.PlanId && (written.Contains(f.Key) || !File.Exists(Path.Combine(caseFolder, f.Key)))))
    .Select(f => f.Key).ToList())
    set.Files.Remove(name);
```

### WR-02: Legacy ownership rejects the whole file when any row was added or deleted, dropping every relabel in it

**File:** `Services/Corpus/AnswerKeyFiles.cs:340-352` (`LegacyOwner`)

**Issue:** A file with no snapshot covers two cases: a pre-Phase-10 key, or a sidecar that was deleted or damaged (`Load` returns an empty set on any JSON error). Such a file is attributed to the plan only if every row's DocID is in the plan with the exact planned thread id, and the DocID count equals the plan's. D-09 says rows the user added or deleted are ignored and do not invalidate the file. Here, one deleted or added row, a sorted or filtered sheet that dropped rows, a removed Thread ID column (`ThreadId` reads as `""`), or a reformatted thread id makes `LegacyOwner` return `Guid.Empty`. All of the file's relabels are then silently dropped and overwritten. This is exactly the path a damaged sidecar takes, so a sidecar corruption combined with one deleted row loses everything.

**Fix:** Use a tolerant ownership test. For example: require that at least N% of the plan's DocIDs are present, that no DocID maps to a different planned thread when the thread column exists, and ignore extra or missing rows. Alternatively, record the PlanId inside the key files themselves (for example a hidden column or the XLSX Statistics sheet) so that ownership does not depend on structure.

### WR-03: ANSI CSV is decoded as Latin-1, so relabeled rows pick up a garbled "custom" rationale that is then saved permanently

**File:** `Services/Corpus/AnswerKeyReader.cs:242-251` (`Decode`), consumed by `AnswerKeyFiles.cs:376-387`

**Issue:** Excel's "CSV (Comma delimited)" save writes the system ANSI code page (Windows-1252 in the US). Decoding it with `Encoding.Latin1` turns bytes such as 0x91-0x97 (curly quotes and dashes, common in LLM rationales) into C1 control characters. Characters outside cp1252 are saved by Excel as `?`. In both cases the decoded cell no longer starts with the projected rationale, so `CustomRationale` returns the garbled text. On relabeled rows, the D-23 branch (lines 382-387) then records it as a user rationale edit, and it is written back into both key files on every later write. The user never edited those cells.

**Fix:**
- Decode non-UTF-8 bytes with the ANSI code page: register `CodePagesEncodingProvider.Instance` at startup and use `Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage)`.
- When the source file is a non-UTF-8 CSV, compare rationales after folding `?` and unmappable characters.
- Simplest option: ignore rationale-only votes coming from a CSV that was not decoded as UTF-8.

### WR-04: A damaged key file that cannot be overwritten gets a new backup on every checkpoint

**File:** `Services/Corpus/AnswerKeyFiles.cs:89-102, 104-126`

**Issue:** When a file reads but does not parse (for example the user renamed the Relevance header in Excel and Excel still holds the file open), every call does the following:
1. `BackupAsync`, which succeeds because the backup is a new file.
2. Then `writer(path, ...)`, which fails because the file is locked.

The snapshot is not updated, so the next call repeats both steps. During a run, `GenerationRunner` writes the key at every checkpoint, which can mean hundreds of backups named `answer-key (unreadable yyyyMMdd-HHmmss[-n]).csv` in the case folder. Within one second the counter runs up to 99 and then reports "No free backup name".

**Fix:** Before backing up, check whether the file can be written (for example open it with `FileAccess.Write, FileShare.None` and close it). If it cannot, record the failure and skip the backup. Alternatively, store the hash of the last backed-up bytes in the sidecar and skip the backup when the damaged file's hash matches.

### WR-05: Start Over records the unused-results bill before it knows the ledger was deleted, so the same spend is counted twice

**File:** `UserControls/StepGenerateCorpus.Run.cs:1069-1083`

**Issue:** `PriceUnusedResults` is recorded on `_state.UsageTracker` before `GenerationReset.StartOverAsync` runs. `GenerationReset.TryDelete` reports a locked or undeletable ledger through `result.Errors` and does not throw (OneDrive, antivirus and the indexer are the usual causes). When that happens, the ledger survives with no `OrphansBilled` line. The next Finished batch run's `PriceOrphans` / `MarkOrphansBilled` then bills the same results again, both in the ticker and in the journal's RunEnded spend. The same double count occurs if `StartOverAsync` throws after the usage was recorded.

**Fix:** Price first and record afterwards. Record only once the ledger is confirmed gone, or append `OrphansBilled` lines for the priced batches before the delete so a surviving ledger is not billed again:
```csharp
var bill = !cancelFailed ? await Task.Run(() => BatchingLlmProvider.PriceUnusedResults(...)) : null;
var reset = await GenerationReset.StartOverAsync(folder, plan, Draft.Guide, keepBatchLedger: cancelFailed);
if (bill != null && !File.Exists(BatchLedger.PathFor(folder)))
    foreach (var line in bill.Lines) _state.UsageTracker.RecordUsage(...);
```

### WR-06: Running batches from the other provider are recorded Cancelled, so the spend on items that finished before the cancel is never itemized

**File:** `Services/Ai/BatchingLlmProvider.cs:684-704`

**Issue:** For running batches, the resolver calls `CancelPendingAsync`, which appends a `Cancelled` line. Both providers bill the requests that finished before the cancel took effect, and both return those partial results. With the Cancelled line written, the batch is never collected or priced, so that spend never reaches `PriceOrphans` or Start Over's `PriceUnusedResults` (WR-A06). The items then run again under the current provider. D-17/D-21 avoided exactly this for stale waves by cancelling at the provider without a Cancelled line. The cross-provider path does not do the same.

**Fix:**
- Cancel through the transport directly, without a Cancelled line, and count the batch as "cancelling".
- On the next run start, the resolver sees state `Cancelled` and collects it through the existing Ended/Expired/Cancelled branch, which bills it as orphans.
- Keep the run unblocked: these items run under the current provider anyway, and Recover ignores other-provider pending batches.

### WR-07: The harvest (reading both key files, parsing the XLSX, SHA-256) runs on the UI thread in Approve and Start Over

**File:** `UserControls/StepReviewPlan.Run.cs:574`, `UserControls/StepGenerateCorpus.Run.cs:1094`, `Services/Corpus/AnswerKeyFiles.cs:56-64`

**Issue:** `await gate.WaitAsync(ct).ConfigureAwait(false)` completes synchronously when the gate is free, which is the normal case. So `ConfigureAwait(false)` does not hop to the thread pool, and `Harvest` plus `BuildBytes` run on the caller's thread. Two callers await it directly from the UI thread:
- `CorpusApproval.ApproveAsync`
- Start Over's `GenerationOutputs.WriteAnswerKeyAsync`

Each harvest reads up to 2 x 100 MB (`MaxFileBytes`), builds an OpenXml DOM of the XLSX, and hashes both files. On a corpus of about 5K emails this freezes the wizard. The phase's own pattern is to read off the UI thread ("Writes to the answer key happen off the UI thread").

**Fix:** Wrap both call sites in `Task.Run`, as the leave-retry and reopen paths already do, or make the funnel always hop:
```csharp
var result = await Task.Run(() => CorpusApproval.ApproveAsync(folder, Draft!, plan, overlay: overlay));
```
(Note that `RecordApproval` mutates `Draft`; either keep it on the UI thread by splitting `WriteFilesAsync` from `RecordApproval`, or marshal back afterwards.)

### WR-08: `PeekRelabels` is called as "never throws" but catches only I/O exceptions; two call sites have no guard

**File:** `Services/Corpus/AnswerKeyFiles.cs:155-168`; `UserControls/StepCaseSummary.cs:1803-1805`; `UserControls/StepCorpusSettings.cs:1038`

**Issue:** `PeekRelabels` catches only `IOException` and `UnauthorizedAccessException`. Anything else escapes:
- `AnswerKeyBuilder.Build` on a hand-edited plan (an NRE on a null list);
- `OutOfMemoryException`, which `ParseXlsx` deliberately rethrows;
- `ArgumentException` from path APIs.

In `LoadQaWithDiscovery`, the new call sits outside `LoadQa`'s protective try/catch, and that method's own comment says its callers "end in async-void handlers, so nothing may escape here". An escape there crashes the process. In `StepCorpusSettings.OnEnterStepAsync`, a throw aborts entry before `AdvanceStage` and `SaveAsync`. The comments at all three call sites ("PeekRelabels never throws") are wrong.

**Fix:** Inside `PeekRelabels`, catch `Exception ex when ex is not OperationCanceledException` and fall back to the sidecar count, or 0 if the fallback throws too. Or guard each call site:
```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // A dry harvest only feeds warnings and copy; never fail the caller
    try { ... sidecar fallback ... } catch (Exception) { return new(StringComparer.Ordinal); }
}
```

### WR-09: XLSX reads cap the compressed size but not the decompressed size

**File:** `Services/Corpus/AnswerKeyReader.cs:50, 196, 224`

**Issue:** `MaxFileBytes` (100 MB) bounds only the bytes on disk. `SpreadsheetDocument.Open` followed by `part.Worksheet` loads the whole sheet XML into a DOM. A highly compressible worksheet, whether crafted or simply huge, expands to gigabytes. `ParseXlsx` explicitly does not catch `OutOfMemoryException`, so it propagates through `Harvest`. In the runner it is swallowed as a KeyError. Through `PeekRelabels` (WR-08) it crashes the UI. The phase brief asks for size caps on reads.

**Fix:** Open with `new OpenSettings { MaxCharactersInPart = <e.g. 200_000_000> }` so oversized parts throw `OpenXmlPackageException`, which is caught and makes the file Damaged. Optionally, check `part.GetStream().Length` before loading, and stream rows with `OpenXmlReader` instead of building the DOM.

### WR-10: Cross-provider resolution has no timeout, no progress and no cancel; closing during it blocks for up to about 5.5 minutes, then abandons it

**File:** `UserControls/StepGenerateCorpus.Run.cs:487-510`, `411-416`; `Forms/WizardForm.cs:564`

**Issue:**
- The resolution runs with `CancellationToken.None`. It makes one status call per batch, may download full results, and makes cancel and delete calls, each of which can take up to the HttpClient default of 100 s.
- No status or progress is shown while `_isResolvingBatches` is set. Generate, Continue and Start Over clicks are silently ignored by the early-return guards, with no feedback.
- On close, `CancelAndWaitForRun` sets `_stopAfterResolve` and pumps messages for `CloseWaitTimeout` (DrainTimeout + 30 s) with the form disabled. After that the process exits while the background resolution is still running.

Each ledger step is idempotent (cache, then Collected; cancel, then Cancelled), so the ledger stays consistent. But the code comment "it is never interrupted mid-ledger" is not true, and the user gets a frozen-looking window.

**Fix:**
- Show a status such as "Collecting {Provider} batches from an earlier session..." while resolving.
- Pass a token that closing cancels between batches, but not mid-step: check it at the top of each loop iteration.
- Correct the comment.

## Info

### IN-01: `PriceBatchModel` was inserted between `CreateRunGateway`'s doc comment and its declaration

**File:** `Models/WizardState.cs:87-99`
**Issue:** `CreateRunGateway`'s `<summary>` now belongs to `PriceBatchModel`, which therefore carries two `<summary>` blocks, and `CreateRunGateway` has none.
**Fix:** Move the `PriceBatchModel` member and its summary above the `CreateRunGateway` doc comment.

### IN-02: `CorpusApproval.BuildFiles` is production-dead and bypasses the harvest

**File:** `Services/Corpus/CorpusApproval.cs:39-51`
**Issue:** Only tests call it now. It takes caller-supplied `relabels` and builds key bytes without harvesting, which is the kind of writer D-02 says must not exist ("No writer may bypass it").
**Fix:** Remove it, or make it internal and test-only. Point the tests at `WriteFilesAsync`.

### IN-03: A relabel with a custom rationale drops the run note

**File:** `Services/Corpus/AnswerKeyFiles.cs:196-198`
**Issue:** `Apply` replaces `row.Rationale` wholesale, so the overlay's run note ("failed", "repaired", and so on) disappears from relabeled rows that have a custom rationale.
**Fix:** Append the overlay note after the custom rationale, or document this as intended.

### IN-04: A new batch transport, each with its own `HttpClient`, is created on every `transportFor` call and never disposed

**File:** `UserControls/StepGenerateCorpus.Run.cs:1166-1170`; `Services/Ai/BatchingLlmProvider.cs:600-605, 612, 764, 816`
**Issue:** The resolver calls `transportFor` for the key check, once per record, and again inside `CancelRecordsAsync` and `DeleteRecordFilesAsync`. Each call constructs a new transport with a new handler.
**Fix:** Cache one transport per kind for the duration of the resolution, and dispose it afterwards.

### IN-05: The Re-plan dialog can open after the user has navigated away

**File:** `UserControls/StepReviewPlan.Run.cs:473-501`
**Issue:** `_isOpeningReplan` does not gate `CanMoveNext` or `CanMoveBack`, and steps are hidden rather than disposed. If the user navigates during the `PeekRelabels` read, `ReplanDialog` pops up over another step.
**Fix:** Also return early when `!Visible`, or include `_isOpeningReplan` in the navigation gate.

### IN-06: Refused-run banner state is not re-rendered, and an exception-path refusal shows no blocking clause

**File:** `UserControls/StepGenerateCorpus.Run.cs:500-516`
**Issue:**
- `SetOtherProviderBatches(resolution.Found)` updates fields but does not call `SetBanner` or `ShowDashboard`, so the banner keeps its old clause.
- On the exception path, `Found` is 0, which forces `_otherProviderBlocked` to false.
**Fix:** Re-render the banner after a refusal, and on the exception path reuse the ledger count from `PendingBatchCountOtherProviders`.

---

_Reviewed: 2026-10-13_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
