---
phase: 07-08
fixed_at: 2026-10-09T00:00:00Z
review_path: .planning/reviews/CODEX-REVIEW-p7p8.md
iteration: 1
findings_in_scope: 7
fixed: 7
skipped: 0
status: all_fixed
---

# Phases 7-8: Codex Review Fix Report

**Fixed at:** 2026-10-09
**Source review:** .planning/reviews/CODEX-REVIEW-p7p8.md
**Iteration:** 1

**Summary:**
- Findings in scope: 7 (CX-01 critical; CX-02..CX-07 warning)
- Fixed: 7
- Skipped: 0

**Verification:**
- Both `-warnaserror` builds pass with 0 warnings and 0 errors: `dotnet build ReelDiscovery.csproj` and `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`. Both used `--artifacts-path obj/gsd-artifacts`.
- `dotnet test --filter "Category!=Render&Category!=Scale"`: 4,322 passed, 0 failed.
- `dotnet test --filter "Category=Scale"`: 2 passed (3 min 12 s). This was run because the batching code and the runner changed.
- `DiscoveryDocumentsRenderTests` and `GenerateCorpusRenderTests`: 28 passed.
- The new concurrency tests were run 3 times each and passed every time: the batching, parity and discovery classes.
- No live or paid provider calls were made. All tests use `FakeLlmProvider` and `FakeBatchTransport`.

**Process note:** As instructed, all commits were made directly on `main` in the main working tree with `git commit`. No worktree or branch was used.

## Fixed Issues

### CX-01: Partition OpenAI batches by model as well as stage

**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`, `ReelDiscovery.Tests/Evidence/BatchParityTests.cs`
**Commit:** 7e8be94
**Applied fix:**
- Batch queues are keyed by (stage label, model id) for every provider, not only OpenAI. This covers the flush loop, `ReleaseQueue` and `RemoveWaiter`.
- Decoys (Story tier) and filler (Volume tier) under "non-relevant mail" now go to separate provider batches.
- The stage label on Submitted records and on status events is unchanged, so the per-wave and non-relevant row mapping and the run-end stale-wave close work as before.
- The journal is unchanged, so D-05 parity holds.

**Tests:**
- New: `CX01_SameStageDifferentModels_OneModelPerSubmission`.
- `LiveAndBatch_SameJournalEmlsAndAnswerKey` now also asserts one model per submission.
- `Restart_CancelWhileNonRelevantWaits_...` now expects one pending batch per model instead of a single batch.

### CX-05: Keep batches recoverable when result caching fails

**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Services/Ai/BatchLedger.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`
**Commit:** 63f6a71
**Applied fix:**
- `CollectAsync` appends Collected only after `SaveResults` succeeds.
  - If the cache could not be written, the batch stays pending in the ledger and its provider files are kept, so a later session downloads it again.
  - The results are still delivered in memory for the current session.
- New method `BatchLedger.TryReadResults`. It returns false when the cache is missing or unreadable.
- `Recover` re-tracks a Collected batch whose cache is missing as a resumed pending batch: its items attach and are downloaded again, never resubmitted. This applies only when the batch:
  - belongs to this plan and this provider,
  - is not orphan-billed, and
  - still has unconsumed items.
- `FailAtProviderAsync` now writes an all-Errored cache before Collected. A failed batch is therefore not polled again after a restart, and its items run live (D-10).

**Tests:**
- `CX05_ResultsCacheNotWritten_NotRecordedCollected_RestartDownloadsAgain`
- `CX05_CollectedButCacheMissing_DownloadedAgainNotResubmitted`
- `CX05_FailedBatch_CachedAsErrored_RestartRunsLiveWithoutPolling`

**Behavior change:** after a restart, items of a batch that failed at the provider now run live. Before, they were queued into a new batch. This matches D-10 ("never in a new batch").

### CX-06: Enforce the token cap across outstanding batches

**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Services/Ai/BatchLedger.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`
**Commit:** 0fa6a6a
**Applied fix:**
- The provider tracks estimated queued input tokens per model across submitted-but-unfinished batches. The estimate is bytes / 4, the same one `Split` uses.
- Before submitting a chunk it waits until queued tokens plus the chunk fit `MaxEstimatedInputTokens`. A chunk always goes when nothing of its model is queued.
- The reservation is released when:
  - the submit fails,
  - the ledger append fails, or
  - the batch is untracked (collected, released, failed or closed).

  Releasing wakes every chunk that is waiting.
- Submitted records now carry `EstimatedInputTokens`. Resumed pending batches count against the cap again (older lines count as 0).
- A Detach ends the wait, and the waiting chunk runs live.
- Tracking is per model inside one decorator. Each `BatchingLlmProvider` has exactly one transport, so this is per (provider, model).

**Tests:**
- `CX06_TokenCap_AppliesAcrossOutstandingBatches_NextChunkWaitsForCapacity`: the second chunk is not submitted while the first batch is polled, and goes once it is collected.
- `CX06_ResumedPendingBatch_CountsAgainstCap_DetachRunsTheWaitingChunkLive`

### CX-07: Drain in-flight submissions before declaring the run stopped

**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `Services/Evidence/GenerationRunner.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`, `ReelDiscovery.Tests/Evidence/BatchParityTests.cs`
**Commit:** 12df546
**Applied fix:**
- Flush loops, which own every submission, are now started and recorded under the provider lock.
- New `BatchingLlmProvider.DetachAsync()`:
  - It calls `Detach()`, then waits for the running flush loops: an upload or batch creation in flight, plus its Submitted ledger line.
  - The wait is bounded by the new `BatchTimings.DrainTimeout` (default 5 minutes).
  - It never waits on remote batch execution, and never throws.
- The runner's `finally` now awaits `DetachAsync()` instead of calling `Detach()`.

**Tests:**
- `CX07_DetachAsync_WaitsForTheInFlightSubmitAndItsLedgerLine`
- `CX07_DetachAsync_IsBounded_NeverWaitsOnRemoteExecution`
- Runner level: `CX07_StopDuringSubmit_RunReturnsOnlyOnceTheSubmittedLineIsDurable`. The run does not complete while the upload is held. When it returns, the ledger has a pending line for every submission.

**Please check:**
- The 5-minute bound is a judgment call: the transports' own network timeout is 10 minutes per call.
- `WizardForm` close still waits only 10 seconds (`CancelAndWaitForRun`). Closing the app in the middle of a slow upload can still end the process before the line is written. Start Over and in-app Stop are covered.

### CX-04: Add the admission prefix only once in Word output

**Files modified:** `Services/Discovery/DiscoveryDocxBuilder.cs`, `ReelDiscovery.Tests/Discovery/DiscoveryDocxBuilderTests.cs`, `ReelDiscovery.Tests/Discovery/WrittenSetWriterTests.cs`
**Commit:** e6982d5
**Applied fix:**
- The writer still owns `Text` with the prefix joined (`Joined(slot.Prefix, text)`), as guided.
- The builder's new `DiscoveryDocxBuilder.RequestText` removes every leading copy of the prefix (with or without its trailing comma, case-insensitive), then joins the prefix exactly once.
- Stored content prints correctly on Save Again with no model call, whether its text:
  - already has the prefix joined (the writer's shape),
  - has the prefix twice, or
  - has no prefix.

**Tests:**
- `CX04_Rfa_WordOutput_HasThePrefixExactlyOnce`: writer output goes through the builder, and each prefixed paragraph contains the prefix exactly once.
- `CX04_Build_Rfa_StoredTextAlreadyJoinedOrDoubled_PrefixPrintedOnce`

### CX-02: Reject empty best attempts before replacing saved documents

**Files modified:** `Services/Discovery/WrittenSetWriter.cs`, `Services/Discovery/DepositionWriter.cs`, `ReelDiscovery.Tests/Discovery/DiscoveryGeneratorTests.cs`
**Commit:** d054b76
**Status:** fixed: requires human verification (this changes the writers' attempt logic)
**Applied fix:**
- **Written discovery:** an attempt counts as no content when it parses but no request lands on a code-issued slot. That covers an empty list, blank texts, or only unknown slots.
- **Deposition:** a chunk attempt with zero normalized turns counts as no content.

  Such an attempt is:
  - retried with the validator's feedback,
  - never kept as the best attempt, and
  - when an earlier attempt exists, handled by keeping that one (the same as for a null value).
- A part left with nothing returns an Error outcome (`"The model returned no requests."` / `"... no testimony."`), and the post-loop guard also rejects an empty best.
- `DiscoveryGenerator` then records `ReasonNoContent` (Failed) and keeps the previous content and file (U54).

**Tests:**
- `CX02_RegenerateReturnsNoUsableRequests_FailedPreviousDocumentKept`, with three cases: empty, blank and unknown.
- `CX02_RegenerateDepositionWithNoTestimony_FailedPreviousTranscriptKept`

### CX-03: Serialize Save Again with other discovery writes

**Files modified:** `Services/Discovery/DiscoveryGenerator.cs`, `UserControls/StepDiscoveryDocuments.cs`, `UserControls/StepDiscoveryDocuments.Run.cs`, `ReelDiscovery.Tests/Discovery/DiscoveryGeneratorTests.cs`, `ReelDiscovery.Tests/Discovery/StepDiscoveryDocumentsRunTests.cs`
**Commit:** 2428df1
**Applied fix:**
- The generator's existing `_writeLock` could not be reused on its own: it belongs to one run instance, and `SaveAgainAsync` is static. A run also keeps its own in-memory set for the whole run, so a lock alone cannot stop a later run save from overwriting a Save Again update. Both mechanisms below are therefore applied.
- **Generator:**
  - A static lock per case folder (case-insensitive path) covers Save Again's whole load, Word write and save. Each run mutation's save also takes it, inside `_writeLock`.
  - Overlapping Save Again calls are therefore serialized, and they never interleave with a run's save.
  - New test seam: `SaveAgainLoadedHook`.
- **Step:**
  - A new `_savingAgain` flag disables Generate Documents, Regenerate... and Save Again while a Save Again is pending.
  - `RunClickAsync`, `RegenerateSelectedAsync`, `RunDocumentsAsync` and `SaveAgainSelectedAsync` return immediately while the flag is set.
  - Save Again was already refused during a run.

**Tests:**
- `CX03_OverlappingSaveAgain_SerializedSoNeitherUpdateIsLost`: the second Save Again waits for the first, and both records end up FileSaved.
- `CX03_SaveAgainPending_GenerateRegenerateAndSaveAgainBlocked`

---

_Fixed: 2026-10-09_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
