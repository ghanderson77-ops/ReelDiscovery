---
phase: 07-scale-cost-qa-cutover
fixed_at: 2026-10-08T00:00:00Z
review_path: .planning/phases/07-scale-cost-qa-cutover/07-REVIEW.md
iteration: 1
scope: Part A (CR-A*, WR-A*) and Part B (CR-B*, WR-B*, incl. WR-B02a); Info findings out of scope
findings_in_scope: 27
fixed: 25
skipped: 2
status: partial
---

# Phase 7: Code Review Fix Report (Parts A and B)

**Fixed at:** 2026-10-08
**Source review:** .planning/phases/07-scale-cost-qa-cutover/07-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 27
  - Part A: 17 (CR-A01..CR-A05, WR-A01..WR-A12)
  - Part B: 10 (CR-B01, CR-B02, WR-B01, WR-B02, WR-B02a, WR-B03..WR-B07)
- Fixed: 25 (15 in Part A, all 10 in Part B). WR-A01 and WR-A12 are partial fixes; see their entries.
- Skipped: 2 (WR-A06, WR-A08). Both need a design decision.

**Verification (Part A pass):**
- `dotnet build ReelDiscovery.csproj -warnaserror` and `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror` both pass with 0 warnings and 0 errors. Both builds used `--artifacts-path obj/gsd-artifacts`.
- `dotnet test --filter "Category!=Render&Category!=Scale"`: 3750 passed, 0 failed.
- `dotnet test --filter "Category=Scale"`: 2 passed (4 min 16 s). This was run because the batching code changed.

**Process note:** `git worktree add <wt> main` was refused because `main` is already checked out in the main tree. The fixes were committed on a temporary branch (`gsd/07-reviewfix-a`) in an isolated worktree, and `main` was then fast-forwarded. `main` was clean and had not moved. The worktree and the temporary branch have been removed. `gsd-sdk query commit` resolved to the main tree and reported "nothing_to_commit", so the commits were made with `git commit` directly in the conventional format.

## Part A

### CR-A01: Detach cancels an in-flight batch submit, so a paid provider batch can exist with no ledger line

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`
**Commit:** e9c93e2
**Applied fix:**
- `SubmitAsync` checks `_cts` before any bytes leave, then calls `_transport.SubmitAsync(..., CancellationToken.None)`. The transports' own network timeouts bound that call.
- The ledger append was already uncancellable.
- If the provider was detached during the submit, the batch is still recorded but no poll loop starts. A later Continue resumes it.
**Regression test:** `CRA01_DetachDuringSubmit_AcceptedBatchIsStillInTheLedger`. It uses a new `SubmitStarted`/`SubmitHold` seam on the fake transport.

### CR-A02: Consumed is keyed by custom id across all batches

**Status:** fixed
**Files modified:** `Services/Ai/BatchLedger.cs`, `Services/Ai/BatchingLlmProvider.cs`, tests
**Commit:** da9f3e8
**Applied fix:**
- `BatchLedgerState.ConsumedItems` is a set of `(BatchId, CustomId)` pairs, and `IsConsumed(batchId, customId)` checks it.
- Both `Recover` loops use it.
- `ConsumedCustomIds` is kept as a flat diagnostic set because existing tests read it.
**Regression tests:** `CRA02_RegeneratedRequest_PendingNewerBatch_AttachesInsteadOfResubmitting` and `CRA02_RegeneratedRequest_CollectedNewerBatch_IsServedNotDropped`.

### CR-A03: Orphan spend is marked billed before it is journaled

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Services/Evidence/GenerationRunner.cs`, tests
**Commit:** 2ffd624
**Applied fix:**
- Orphan billing is split in two:
  - `PriceOrphans()` returns an `OrphanBill` and has no side effects.
  - `MarkOrphansBilledAsync(bill)` appends `OrphansBilled` and stops serving those results.
- The runner prices orphans before the final checkpoint and adds them to the unjournaled spend.
- `AppendRunEndedAsync` now sets `RunContext.RunEndedAppended`. The runner marks orphans billed only when that flag is true, so the journal is the commit point.
- `BillOrphansAsync` remains as a wrapper (price, then mark) for existing callers and tests.
**Regression test:** `CRA03_PriceOrphans_HasNoSideEffects_UntilMarkedBilled`. It checks that nothing is written before the mark, and that a restart still prices the orphan when RunEnded was never written.

### CR-A04: Orphans from another provider or from an unpriced model are billed at $0

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Models/WizardState.cs`, tests
**Commit:** e4e24c1
**Applied fix:**
- There is a new constructor overload that takes `Func<LlmProviderKind, string, AIModelConfig?>`. The old model-id-only overload wraps into it.
- `AvailableResult` carries the provider that ran the batch.
- `WizardState.CreateRunGateway` looks up price by `(provider, modelId)` across the whole catalog.
- A batch with any unpriced item is left out of the bill (`OrphanBill.UnpricedBatchIds`), so it never gets an `OrphansBilled` line at $0.
**Regression test:** `CRA04_OrphanOfAnotherProvider_PricedWithThatProvidersModel_UnpricedLeftUnbilled`.
**Note:** Unpriced batches are reported only on `OrphanBill.UnpricedBatchIds`. No UI warning shows them yet, because there is no generic warning event kind and adding one is a UI-SPEC decision.

### CR-A05: `BatchLedger.Replay` treats any IOException as an empty ledger

**Status:** fixed
**Files modified:** `Services/Ai/BatchLedger.cs`, `Services/Ai/BatchingLlmProvider.cs`, `Services/Evidence/GenerationReset.cs`, tests
**Commit:** 2eb5425
**Applied fix:**
- `Replay` opens the file with `FileShare.ReadWrite | FileShare.Delete` and retries up to 5 times on IOException.
- It reports `BatchLedgerState.Readable = false` instead of returning an empty state.
- When the ledger is unreadable:
  - the `BatchingLlmProvider` constructor sets `LiveOnly`, so every call runs live and nothing is resubmitted or resumed;
  - `CancelPendingAsync` throws IOException, so Start Over reports a cancel failure;
  - `GenerationReset` keeps the ledger and `batch-results/` and adds an error line.
**Regression tests:**
- `CRA05_ReplayWhileAnAppendHandleIsOpen_StillReadsTheLedger`
- `CRA05_LockedLedger_IsUnreadable_ProviderRunsLiveAndCancelRefuses`
- `CRA05_StartOver_UnreadableBatchLedger_IsKept`

### WR-A01: Consumed is written before delivery

**Status:** partially fixed. Needs human verification.
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, tests
**Commit:** d2239ab
**Applied fix:** Waiters on one shared batch item now share a `UsageClaim`. The first caller that actually receives the result reports the tokens, replacing the fixed waiter-0 rule, so a cancelled waiter 0 no longer drops the item's usage. `IdenticalRequests_OneItemBothCallersServed` now also checks that exactly one caller carries the tokens.
**Not fixed:** Consumed is still written before the unit journals its line. The fix would either write Consumed after the unit's journal line (from the gateway or unit) or reconcile consumed-but-unjournaled results against the journal at Recover. Both need a design decision. The gap is documented in a code comment.

### WR-A02: One malformed result line fails the whole batch

**Status:** fixed
**Files modified:** `Services/Ai/OpenAIBatchTransport.cs`, `Services/Ai/AnthropicBatchTransport.cs`
**Commit:** 2399d0d
**Applied fix:**
- OpenAI: `ParseResultLine` catches `Exception ex when ex is not OperationCanceledException` per line and skips that line. The decorator then re-runs the missing id live.
- Anthropic: a new `SafeItemResult` wraps `ToItemResult` per item and maps a failure to an Errored item. If even the custom id cannot be read, the item is skipped.

### WR-A03: Uploaded OpenAI batch input files leak at the provider

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, tests
**Commits:** ddfcb33, aebe277 (race follow-up)
**Applied fix:**
- On submit failure and on ledger-append failure, the uploaded input file is deleted best-effort.
- After a collect whose results cache was saved, the input and output files are deleted best-effort. `TrackedBatch` now carries `InputFileId`, from the submit result or from the ledger for resumed batches.
- Follow-up (aebe277): the full suite exposed a race. A poll could see a batch that `CancelPendingStagesAsync` had just cancelled and collect it, deleting its files, before the batch was marked closed. Matching batches are now marked closed before the provider cancel. A batch that was not actually closed is reopened, and its poll loop restarts if it had stopped.
**Test:** `WRA03_CollectedBatch_DeletesItsProviderFiles`.

### WR-A04: A SendAsync racing with Detach can wait forever

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`
**Commit:** 746a9b9
**Applied fix:**
- `SendAsync` checks `_cts.IsCancellationRequested` again inside `_lock` and goes live if it is set.
- `FlushLoopAsync`'s cancellation handler now releases anything still queued under its label (`ReleaseQueue`) when nothing had been taken yet.

### WR-A05: Start Over cancels only the current plan's batches, then deletes the whole ledger

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Services/Ai/BatchLedger.cs`, `Services/Evidence/GenerationReset.cs`, `UserControls/StepGenerateCorpus.Run.cs`, tests
**Commit:** d6a02cc
**Applied fix:**
- New `BatchingLlmProvider.CancelAllPendingAsync`:
  - cancels pending batches of every plan;
  - deletes the files of every batch it can reach;
  - returns the ids it could not cancel because their provider has no transport (no key).
- New `BatchLedger.PendingBatchCountAllPlans`.
- Start Over uses both. When any cancel fails or is skipped, it passes `keepBatchLedger: true` to `GenerationReset.StartOverAsync`, so the ledger survives.
- The shared cancel and delete logic was moved into private helpers. `CancelPendingAsync` behaves as before, except that Cancelled lines now carry the record's own PlanId.
**Tests:** `WRA05_CancelAllPending_CancelsEveryPlan_ReportsProvidersWithoutTransport`. `StartOver_CancelFailure_StillClears_WithWarning` now also asserts that the ledger is kept.
**Note:** Real transports still swallow cancel failures (CR-B01). That is left for the Part B pass.

### WR-A06: Orphans are billed only on Finished

**Status:** skipped (needs a design decision)
**File:** `Services/Evidence/GenerationRunner.cs:532-537`
**Reason:**
- **Start Over case:** Start Over deletes the journal, so every RunEnded line goes with it. There is no surviving total to add orphan spend to.
- **Cancelled or Fatal case:** billing orphans would append `OrphansBilled`. `Recover` then skips the whole batch, so a later Continue could not consume those results and would pay for them live again. Leaving them unbilled is the safer choice.
- **What a fix would need:** either a case-level spend record that outlives Start Over, or orphan accounting kept separate from the "do not serve" flag. That is a design change.
**Original issue:** Collected but unconsumed results after a Cancelled or Fatal run, followed by Start Over, never reach any RunEnded line.

### WR-A07: Snapshot batch rows can stay stuck on "Waiting on provider batch"

**Status:** fixed. Needs human verification of the Activity wording.
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, tests
**Commit:** e0d730a
**Applied fix:**
- `ReleaseBatch` takes an optional reason. With a reason, it raises a terminal `FailedAtProvider` event, but only if the batch was still tracked, so collected batches never get a duplicate.
- Callers that now pass a reason:
  - fatal poll failures, result-download failures and the bookkeeping catch pass "the app stopped waiting on it";
  - closing a stale wave batch passes "it was closed because this run no longer uses it".
- `FailAtProviderAsync` still raises its own event.
- The Activity row reads "Batch failed at {provider}: {reason}. Its N requests run live at full price." A human should confirm this wording for the closed-stale-batch case.
**Test:** `CancelPendingStagesAsync_CancelsOnlyMatchingLabels_RecordsCancelled` now asserts that the terminal event is raised for the closed batch only.

### WR-A08: Relevant (live) scopes ignore items already in flight in resumed batches

**Status:** skipped (needs a design decision)
**File:** `Services/Ai/BatchingLlmProvider.cs:352-371`
**Reason:**
- The suggested fix is to cancel stale wave batches before the relevant waves start. That trades away results those batches may still deliver for free: the live scopes already use any result that has been collected.
- Recording the partial spend of cancelled batches would mean collecting them after cancellation, which the current Closed and poll flow does not do.
- This path only affects batches submitted per wave by builds before amended D-07. The timing choice should be made deliberately.
**Original issue:** A live-scope request whose id is in a resumed pending batch is sent live anyway, and the batch item is still billed. That spend is not itemized.

### WR-A09: Orphan spend never reaches the session TokenUsageTracker

**Status:** fixed
**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Models/WizardState.cs`, tests
**Commit:** e9787cd
**Applied fix:**
- `OrphanBill.Lines` keeps the model, usage and cost of each priced item.
- `MarkOrphansBilledAsync` records them on `BatchingLlmProvider.UsageTracker` under the operation "Batch results not used". `WizardState.CreateRunGateway` sets that tracker to the session's `UsageTracker`.
- Recording happens at mark time, which is after RunEnded, so the header ticker and the journal agree. This includes `BatchSavingsUsd`.
**Test:** extended `CRA03_PriceOrphans_HasNoSideEffects_UntilMarkedBilled`.

### WR-A10: Thread invalidation deletes the EMLs before journaling

**Status:** fixed
**Files modified:** `Services/Evidence/ThreadInvalidation.cs`, `ReelDiscovery.Tests/Evidence/ThreadInvalidationTests.cs`
**Commit:** 29d00b0
**Applied fix:**
- The existing copies are computed first, then `ThreadInvalidated` is appended with `Files` set to those copies. The files are deleted only after the append succeeds.
- The append catch is now `Exception ex when ex is not OperationCanceledException`, which includes `JournalFullException`. On failure nothing is deleted and the thread stays Written.
- `Files` now lists the copies about to be removed rather than the copies actually removed. Nothing reads `Files` on this record type: `ListAllFiles` only reads ThreadDone and AttachmentsRepaired.
- `LockedCopy_IsReported_AndLineStillAppended` was updated to match: the locked copy is now listed, and it is still reported and still on disk.
**Test:** `WRA10_JournalAppendFails_NothingDeleted_ThreadStaysWritten`.

### WR-A11: A tampered or inconsistent CostDetail can produce negative totals

**Status:** fixed
**Files modified:** `Models/Evidence/JournalRecord.cs`, `Services/Evidence/GenerationJournal.cs`, tests
**Commit:** 324ea58
**Applied fix:**
- `Normalize` clamps `BatchSavingsUsd` to the range 0 to the sum of the four categories.
- `AddSpend` uses the detail only when `0 <= detail.TotalUsd <= text`. Otherwise the whole text spend counts as unsplit, so `SpentDetail + media + unsplit == SpentCostUsd` always holds.
**Tests:** `WRA11_Normalize_BatchSavingsCappedAtTheCategorySum`, `WRA11_Replay_DetailLargerThanCost_CountsAsUnsplit_ItemsStillAddUp`.

### WR-A12: The batch estimate contradicts D-17 and the D-10 retry pricing

**Status:** fixed (retry part only, per the caller's instruction)
**Files modified:** `Services/Corpus/GenerationCostEstimator.cs`, `ReelDiscovery.Tests/Corpus/GenerationCostEstimatorTests.cs`
**Commit:** 442bf0e
**Applied fix:**
- `Split` now prices a batched call as `Price(batch) + (QaOverhead - 1) * Price(live)`: the retries that D-10 runs live are priced at the live rate.
- The batch total still excludes cache savings. That is the approved, conservative choice (UI-SPEC S1 tooltip: "The batch total assumes no cache savings").
- The two estimator tests that pin the exact batch totals were updated, using a new `BQ` helper.

## Part B

**Verification (Part B pass):**
- Both builds pass with `-warnaserror` (0 warnings, 0 errors): `dotnet build ReelDiscovery.csproj` and `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`. Both used `--artifacts-path obj/gsd-artifacts`.
- `dotnet test --filter "Category!=Render&Category!=Scale"`: 3766 passed, 0 failed.
- `dotnet test --filter "Category=Render"`: 135 passed, 0 failed.
- The API step renders were inspected at 150% DPI (`step-api-configuration-1000x540.png`, `wizard-shell-api-configuration.png`). Instructions shows in full, and the clipping detector reports no findings.

**Process note:** As instructed, Part B was committed directly on `main` in the main working tree with `git commit`. No worktree or branch was used.

### CR-B01: Start Over always says pending provider batches were cancelled

**Status:** fixed
**Files modified:** `Services/Ai/IBatchTransport.cs`, `Services/Ai/OpenAIBatchTransport.cs`, `Services/Ai/AnthropicBatchTransport.cs`, `Services/Ai/BatchingLlmProvider.cs`, tests
**Commit:** 32b17c1
**Applied fix:**
- The contract for `IBatchTransport.CancelAsync` changed. A cancel that did not take effect now throws the new `BatchCancelFailedException` (BatchId, FailureKind, and a safe message built by `MapFailure`, so no key or payload is included).
- These cases still count as success:
  - a 404 or 409 from either provider;
  - an OpenAI batch whose rehydrated operation is already `HasCompleted` (no cancel is posted).
- `CancelAllPendingAsync` (Start Over) catches a failed cancel for each batch. For that batch it:
  - adds the id to the returned not-cancelled list;
  - writes no `Cancelled` line, so the batch stays pending in the ledger;
  - leaves its uploaded files in place.

  It then goes on to cancel the remaining batches. `CancelPendingBatchesAsync` therefore returns false. Start Over shows `BatchCancelFailedText` and passes `keepBatchLedger: true`.
- Other callers:
  - `CancelPendingAsync` with default arguments still propagates the failure.
  - The run-end close path (`closeOnCancelFailure: true`) still records the batch as closed.
  - The untracked-batch cancel after a failed ledger append is now wrapped in a best-effort catch.

**Regression tests:**
- `CRB01_Cancel_ServerError_Throws_WithoutKey` and `CRB01_Cancel_NetworkFailure_Throws` (OpenAI and Anthropic wire tests)
- `CRB01_Cancel_EndedBatch_IsSuccess_NoCancelPosted`
- `CRB01_CancelAllPending_ProviderCancelFails_ReportedAndKeptPending_OthersStillCancelled`

The existing 404 tests still pass.

### CR-B02: Blind-review verdicts outlive Regenerate Thread

**Status:** fixed. Needs human verification.
**Files modified:** `Models/Qa/BlindReviewVerdict.cs`, `Services/Qa/BlindReviewPrompts.cs`, `Services/Qa/BlindReviewer.cs`, `UserControls/StepCaseSummary.cs`, tests
**Commit:** acc18ce
**Applied fix:**
- Each verdict now records `WrittenUtc`: the journal time of the content the reviewer judged.
  - `BlindReviewer.WrittenStamps(replay)` computes it. It is the thread's ThreadDone `Utc`, or a later `AttachmentDone` time for that email's attachments.
  - `BuildDocuments` puts the stamp on `BlindReviewDocument.WrittenUtc`, and `ReviewOneAsync` copies it into the verdict.
- `RunAsync` keeps only verdicts whose stamp matches the document's current stamp, so a regenerated email is sent to the reviewer again.
- `LoadQa` filters `ReviewVerdicts` with `BlindReviewer.IsCurrent`. Done counts, the disagreement list and the "already reviewed" set used by the run dialog now cover only the current text.
- **Design choice (please confirm):** a verdict with no stamp (written before this change) counts as current, so no verdict the user already paid for is dropped or billed again. A different design would have `ThreadInvalidation` append a superseding "Void" line instead.

**Regression tests:**
- `CRB02_RegeneratedDoc_VerdictDropped_AndReviewedAgain`
- `CRB02_IsCurrent_MatchesStamp_LegacyVerdictKept`
- `CRB02_WrittenStamps_ThreadDoneOrLaterAttachment`

### WR-B01: Cancelling between the restated header and the verdict re-append drops paid verdicts

**Status:** fixed
**Files modified:** `Services/Qa/QaReportStore.cs`, `Services/Qa/BlindReviewer.cs`, `ReelDiscovery.Tests/Qa/QaReportStoreTests.cs`
**Commit:** e4a012c
**Applied fix:**
- New `QaReportStore.RestateBlindReviewAsync(header, keep, ct)`. It checks `ct` once, before anything is written, then writes the header and every kept verdict in a single `AppendLinesAsync(..., CancellationToken.None)` call: one stream write and one fsync, under the gate.
- `AppendLineAsync` now delegates to `AppendLinesAsync`.
- The resized-sample branch of `RunAsync` uses the new method.

**Test:** `WRB01_Restate_WritesHeaderAndKeptVerdictsTogether_CancelledBeforeLeavesFileUnchanged`.

### WR-B02: QA load/reproject paths catch only a few exception types

**Status:** fixed
**Files modified:** `UserControls/StepCaseSummary.cs`, `ReelDiscovery.Tests/Qa/CaseSummaryQaTests.cs`
**Commit:** df9ad8f
**Applied fix:**
- `LoadQa` and `ReprojectAfterRegenerateAsync` now catch `Exception ex when ex is not OperationCanceledException`, with comments explaining why the failure is ignored.
- The body of `RefreshQaAsync` is wrapped in the same guard. If a refresh fails, the rows keep their last good values.

**Test:** `WRB02_LoadQa_UnexpectedException_IsContained_PartialSnapshotReturned`. It throws a `NullReferenceException` from inside the load and checks that the partial snapshot is still returned.

### WR-B02a: Regenerate button handler is async void and rethrows OperationCanceledException

**Status:** fixed
**Files modified:** `Forms/QaFindingsDialog.cs`, `ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs`
**Commit:** 602fb9a
**Applied fix:** `RegenerateSelectedAsync` no longer rethrows `OperationCanceledException`. Instead it shows a Danger footer: `QaUiText.RegenerateFailed("the operation was cancelled")`, which reads "Could not queue the thread for regeneration: the operation was cancelled." The wording is new and needs UI-SPEC sign-off.
**Test:** `WRB02a_Regenerate_Cancelled_ShowsFooter_DoesNotThrow`.

### WR-B03: Closing QaFindingsDialog while a regeneration is in flight loses `QueuedAny`

**Status:** fixed
**Files modified:** `Forms/QaFindingsDialog.cs`, `ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs`
**Commit:** 2f9a541
**Applied fix:**
- `OnFormClosing` cancels the close while `_busy` is set, when the close reason is `UserClosing` or `None`. That covers the Close button, Esc and the title-bar X.
- Windows shutdown and `Application.Exit` still close.
- As a result, `QueuedAny` is final when `ShowDialog` returns, and the regenerate continuation never writes to a disposed dialog.

**Test:** `WRB03_CloseWhileRegenerating_IsRefused_UntilTheRegenerateFinishes`.

### WR-B04: Logistic regression uses a fixed step size with no stability or convergence guard

**Status:** fixed. Needs human verification (numerical change).
**Files modified:** `Services/Qa/LogisticRegression.cs`, `ReelDiscovery.Tests/Qa/LogisticRegressionTests.cs`
**Commit:** c849284
**Applied fix:**
- Every iteration evaluates the regularized objective: mean log loss (stable softplus) plus `(lambda/2n)·|w|²`, together with the gradient.
- If a step raised the loss, or the loss is not finite, the fit returns to the last accepted point and halves the step. This is deterministic.
- The fit stops when:
  - the relative decrease falls to 1e-12 or below, or
  - the step falls below 1e-12.
- The last step is verified too: the result is never worse than the last accepted point.
- A stable fit takes exactly the same steps as before, until it converges. The existing LeakageAudit, QaRunner and LogisticRegression tests pass unchanged.
- An internal `Loss(x, y, lambda)` method was added for tests.

**Test:** `WRB04_CorrelatedColumns_LossNeverIncreases_AndTheFitLearns`. It uses 20 perfectly correlated columns, where λmax ≈ 20 means the old fixed 0.5 step overshoots. The test checks that:
- the loss never increases across iteration prefixes 0..59;
- it ends well below ln 2;
- the weights are finite;
- balanced accuracy is in [0.75, 0.85];
- two fits give identical results.

### WR-B05: Start Over does not cancel an in-flight end-of-run QA pass

**Status:** fixed (cancel-and-await part)
**Files modified:** `UserControls/StepGenerateCorpus.Run.cs`, `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs`
**Commit:** cc20670
**Applied fix:**
- Each end-of-run QA pass gets its own `_qaRunCts`, linked to the step-lifetime `_qaCts`. A newer run end cancels the previous pass.
- `StopQaAsync()` cancels the pass and awaits `QaRun`. It never throws. It runs:
  - at the start of Start Over's reset, before batches are cancelled and before `GenerationReset` deletes the reports;
  - at the start of a new run, before the runner starts.
- New test seam `QaPassOverride`.

**Not done:** stamping `LeakageReport` with the journal time. With the cancel-and-await in place, a late write can no longer happen. Showing an out-of-date leakage state would also need new UI-SPEC wording.
**Test:** `WRB05_StartOver_StopsTheQaPassFirst_SoItsReportIsNotWrittenAfterTheReset`.

### WR-B06: API step at 150% DPI: some rows and the layout `MinimumSize` stay in raw pixels

**Status:** fixed
**Files modified:** `UserControls/StepApiConfiguration.cs`
**Commit:** c396d08
**Applied fix:**
- Rows 2, 3, 6 and 10 (24) and rows 7 and 8 (36) now use `LogicalToDeviceUnits`.
- `MinimumSize` is now `LogicalToDeviceUnits(520)`. The fixed rows sum to 400 logical px, so Instructions keeps about 120 logical px at any DPI, and the control scrolls rather than collapsing that row.
- At 100% DPI the layout is identical.

**Verification:** Render category passes (including `ApiConfigurationStep_NoGapBelowDisclaimer` at both sizes). The 150% renders were inspected: the status rows have room for their text, Instructions is fully visible, and there are no clipping findings.

### WR-B07: `BlindReviewer.RunAsync` dereferences null documents before filtering them

**Status:** fixed
**Files modified:** `Services/Qa/BlindReviewer.cs`, `ReelDiscovery.Tests/Qa/BlindReviewTests.cs`
**Commit:** 7876d98
**Applied fix:** `RunAsync` drops null documents and documents with an empty DocId at the top of the method. The later null checks were simplified.
**Test:** `WRB07_NullAndIdlessDocuments_AreSkipped`.

---

_Fixed: 2026-10-08_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1 (Part A and Part B passes)_
