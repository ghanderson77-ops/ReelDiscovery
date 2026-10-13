---
phase: 10-answer-key-batch-safety
fixed_at: 2026-10-13T17:49:52Z
review_path: .planning/phases/10-answer-key-batch-safety/10-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 10: Code Review Fix Report

**Fixed at:** 2026-10-13T17:49:52Z
**Source review:** .planning/phases/10-answer-key-batch-safety/10-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 6 (CR-01, CR-02, WR-01, WR-05, WR-06, WR-07, as selected by the orchestrator)
- Fixed: 6
- Skipped: 0
- Not in scope: WR-02, WR-03, WR-04, WR-08, WR-09, WR-10, IN-01 to IN-06 (left untouched)

Each fix has its own commit. Each commit built with `-warnaserror` and was checked with filtered `dotnet test` runs only, never the full suite. For CR-02, WR-01, WR-05 and WR-07, the new regression test was also confirmed to fail against the code before the fix. Line endings were kept per file: LF files stayed LF and CRLF files stayed CRLF.

## Fixed Issues

### CR-01: A batch the other provider cannot find (404) or whose results expired blocks every run

**Files modified:** `Services/Ai/IBatchTransport.cs`, `Services/Ai/AnthropicBatchTransport.cs`, `Services/Ai/OpenAIBatchTransport.cs`, `Services/Ai/BatchingLlmProvider.cs`, `UserControls/GenerationUiText.cs`, `UserControls/StepGenerateCorpus.Run.cs`, `ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`, `ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs`, `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs`
**Commit:** 80d3c70
**Applied fix:**
- `BatchStatusResult` and `BatchResultsResult` gained a `NotFound` flag. Both transports set it on HTTP 404.
- The run-start resolver now closes a batch that is not found (on either the status call or the results call) with a `Cancelled` ledger line under that batch's own provider. Such a batch can never bill, so it no longer blocks. Its uploaded files are deleted on a best-effort basis.
- Transient and auth failures still block the run (D-22).
- `OtherProviderResolution` now carries a `FailureKind`. When the key is refused (Auth), the new copy `RunRefusedOtherProviderKeyRefusedText` points to API Configuration instead of saying "Try again in a moment".
- Tests added: resolver status-404 and results-404 cases, the auth failure kind, the new copy, and two step tests (a refused key shows the key copy; a missing batch lets the run start).

### CR-02: A user Rationale that starts with the projected rationale is discarded

**Files modified:** `Services/Corpus/AnswerKeyFiles.cs`, `Services/Corpus/AnswerKeyBuilder.cs`, `Models/Corpus/AnswerKeyRelabels.cs`, `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs`
**Commit:** 87efc0c
**Applied fix:**
- `CustomRationale` no longer uses `StartsWith`. After line-ending normalization, a cell counts as unchanged only when it exactly equals one of:
  - the projected rationale;
  - the projected rationale plus the run note the file was written with;
  - the projected rationale plus the current overlay's note.
- Anything else counts as the user's edit, including text appended to the projected rationale.
- To keep Pitfall 6 working under exact matching, `AnswerKeyFileSnapshot` gained `Notes` (DocID to the overlay note written into that file; only DocIDs that had a note). `Harvest` now receives the overlay, and `AnswerKeyBuilder.WithNote` went from private to internal.
- Tests added: `AppendedRationale_IsUserEdit` and `RunNoteRationale_NotAnEdit`.

### WR-01: A foreign plan's snapshot is pruned even when its file was not overwritten

**Files modified:** `Services/Corpus/AnswerKeyFiles.cs`, `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs`
**Commit:** dfb9718
**Applied fix:**
- Files this call wrote already carry the new plan's snapshot.
- Another plan's snapshot is now pruned only when its file no longer exists on disk.
- Snapshots that are null, or keyed by an unknown name, are still dropped.
- Tests added: `OtherPlanFile_NotOverwritten_KeepsItsSnapshot` (XLSX locked during a re-plan write; the next write must not import the old plan's label) and `OtherPlanSnapshot_FileGone_Pruned`.

### WR-05: Start Over records the unused-results bill before it knows the ledger was deleted

**Files modified:** `UserControls/StepGenerateCorpus.Run.cs`, `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs`
**Commit:** f9d05d7
**Applied fix:**
- Start Over still prices unused results first, while the ledger is readable.
- The bill is now recorded on the session tracker in a `finally` after `GenerationReset.StartOverAsync`, and only if `provider-batches.jsonl` no longer exists. That also covers a reset that throws part way.
- If the ledger survives, the results stay unbilled, and a later Finished batch run bills them once.
- Test added: `StartOver_LedgerDeleteFails_RecordsNothing` (ledger held readable but not deletable).

### WR-06: Running batches from the other provider are recorded Cancelled, so partial spend is never itemized

**Files modified:** `Services/Ai/BatchingLlmProvider.cs`, `ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs`, `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`, `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs`
**Commit:** 420906e
**Applied fix:**
- The resolver no longer calls `CancelPendingAsync` for running other-provider batches. It now cancels each one through its own transport and writes no `Cancelled` line, as D-21 does for stale waves.
- It then re-checks the status once:
  - If the provider already reports the batch over, it is collected right away through the shared `TryCollectEndedAsync` helper (results cache plus `Collected` line). What it finished before the cancel then becomes orphans that are billed once at Finished.
  - If the batch is still cancelling, it stays pending and the next run start collects it.
- The run is not blocked. The foreign custom ids never match the current provider's, so items still run once under the current provider (D-13). A failed cancel still blocks (D-22).
- The ended-batch handling was factored into `TryCollectEndedAsync` (shared by the first pass and the post-cancel pass).
- Tests updated: the running-batch resolver test (now expects a `Collected` line, partial results cached, and orphan pricing with the Anthropic model) and three existing tests that asserted `CancelledIds`.
- Tests added: `ResolveOtherProvider_StillCancelling_StaysPending_CollectedNextRunStart`, plus an orphan-billing assertion in `Run_OtherProviderRunning_CancelledThenRunsOnce`.
- Note: the D-13 text in 10-CONTEXT.md still says running batches are cancelled "via CancelPendingAsync". The implementation now deviates from that text, as the orchestrator's guidance asked.

### WR-07: The harvest runs on the UI thread in Approve and Start Over

**Files modified:** `UserControls/StepReviewPlan.Run.cs`, `UserControls/StepReviewPlan.cs`, `UserControls/StepGenerateCorpus.Run.cs`, `Services/Corpus/AnswerKeyFiles.cs`, `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs`, `ReelDiscovery.Tests/Render/CorpusReviewPlanRenderTests.cs`
**Commit:** 1b65bbb
**Applied fix:**
- Approve Plan now runs `CorpusApproval.WriteFilesAsync` inside `Task.Run`. It then calls `RecordApproval` or `RecordPartialWrites` back on the UI thread, because those mutate the draft.
- Start Over now wraps `GenerationOutputs.WriteAnswerKeyAsync` in `Task.Run`.
- Test seams added:
  - `AnswerKeyFiles.HarvestObservers`: per case-folder key, empty in the app; `GateKey` was made internal so tests can register.
  - `StepReviewPlan.ClickApproveAsync`.
- Tests added: `Approve_HarvestsAnswerKey_OffTheUiThread` and `StartOver_HarvestsAnswerKey_OffTheUiThread`.

## Not in Scope

Not in scope for this run and left untouched:
- WR-02: legacy ownership rejects the whole file
- WR-03: ANSI CSV decoded as Latin-1
- WR-04: repeated backups of a damaged, locked file
- WR-08: PeekRelabels catches only I/O exceptions
- WR-09: no decompressed-size cap on XLSX
- WR-10: cross-provider resolution has no timeout, progress or cancel
- IN-01 to IN-06

---

_Fixed: 2026-10-13T17:49:52Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
