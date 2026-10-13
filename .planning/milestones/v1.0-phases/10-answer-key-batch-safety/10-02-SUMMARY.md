---
phase: 10-answer-key-batch-safety
plan: 02
subsystem: ai/batch-ledger
tags: [batch, ledger, provider-switch, start-over, pricing, orphans]
requires: []
provides:
  - "BatchLedger.PendingBatchCount(folder, planId, provider)"
  - "BatchLedger.PendingBatchCountOtherProviders(folder, planId, current)"
  - "OtherProviderResolution + BatchingLlmProvider.ResolveOtherProviderPendingAsync"
  - "BatchingLlmProvider.PriceUnusedResults (Start Over pricer)"
  - "public BatchingLlmProvider.OrphanOperation"
affects:
  - "Later phase-10 plans wire the resolver into run start (D-13/D-14/D-22), the banner count (D-15) and Start Over pricing (D-16)"
tech-stack:
  added: []
  patterns:
    - "Static ledger helpers run before CreateRunGateway, so Recover sees resolved batches as orphans (Pitfall 11)"
    - "One pricing path (TryPriceItems) and one T-07-23 classification path (ClassifyResults)"
key-files:
  created: []
  modified:
    - Services/Ai/BatchLedger.cs
    - Services/Ai/BatchingLlmProvider.cs
    - ReelDiscovery.Tests/Ai/BatchLedgerTests.cs
    - ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs
decisions:
  - "The resolver checks that every foreign provider has a transport before any network call, so a missing key leaves the ledger and the provider untouched (D-14)"
  - "A Collected line that cannot be written for a foreign batch blocks the run (Failed): the batch would otherwise stay pending, Recover would ignore it, and its items would be paid again"
  - "A failed foreign batch whose all-Errored cache cannot be saved still gets its Collected line (as FailAtProviderAsync): its items run live under the current provider at no extra spend"
  - "An unreadable ledger is simulated in tests with a FileShare.None lock: garbage lines are skipped by Replay and keep it readable"
metrics:
  duration: "~15 min"
  completed: 2026-10-12
  tasks: 2
  files: 4
---

# Phase 10 Plan 02: Batch-Ledger Building Blocks for Provider Switch and Start Over Summary

This plan adds static helpers in `Services/Ai` for the provider-switch and Start Over cases:
- Ledger counts filtered by provider.
- A run-start resolver that collects, cancels or blocks on another provider's pending batches of the plan, so the run never pays for the same items twice (W5).
- A Start Over pricer with no side effects. It bills collected-but-unused results of every plan the same way `PriceOrphans` does (WR-A06).

No UI code changed.

## What Was Built

### Task 1: Counts, shared classification, PriceUnusedResults (commits 52939d3 RED, d96c13d GREEN)
- **New ledger counts:**
  - `BatchLedger.PendingBatchCount(folder, planId, provider)` counts this plan's pending batches for one provider.
  - `PendingBatchCountOtherProviders(folder, planId, current)` counts the plan's pending batches for every other provider.
  - Both return 0 for a blank folder or an unreadable ledger.
  - The existing `PendingBatchCount(folder, planId)` and `PendingBatchCountAllPlans` are unchanged.
- **Shared code in `BatchingLlmProvider`:**
  - `OrphanOperation` is now a `public const`, placed next to `UsageTracker`.
  - The T-07-23 block of `CollectAsync` was moved into `ClassifyResults(TrackedBatch, IEnumerable<BatchItemResult?>?)`.
  - The per-item pricing loop of `PriceOrphans` was moved into `TryPriceItems`, and `AddToBill` applies the result to the bill. `usage.IsBatch = true;` now appears only once in the file.
- **`PriceUnusedResults(ledger, priceLookup)`:**
  - Selects collected, not-yet-billed batches of every plan, ordered by SubmittedUtc and then BatchId.
  - Skips batches with no cache.
  - Prices only the batch's own ids that are unconsumed and usable, using `priceLookup(record.Provider, ModelFor(id))`.
  - A batch with an unpriced model goes to `UnpricedBatchIds`.
  - An unreadable ledger gives an empty bill.

### Task 2: ResolveOtherProviderPendingAsync (commits aa2690c RED, 04fec21 GREEN)
`OtherProviderResolution` reports Provider, Found, Collected, Cancelled, KeyMissing, Failed, LedgerUnreadable and a diagnostic-only FailureMessage. `Blocked` means KeyMissing or Failed.

What the resolver does, by case:

| Case | Action | Result |
|------|--------|--------|
| Ledger unreadable | Changes nothing | `LedgerUnreadable` |
| A foreign provider has no transport (no key) | Returns before any call | `KeyMissing` |
| Status call fails | Stops | `Failed` |
| Batch Ended, Expired or Cancelled | Results classified with `ClassifyResults`, cached, Collected line written under the batch's own provider and PlanId, provider files deleted (best effort) | `Collected` +1 |
| Results, save or Collected-append fails | Stops | `Failed` |
| Batch Failed | All-Errored cache plus a Collected line | `Collected` +1 |
| Batch InProgress | Cancelled through `CancelPendingAsync` with `include: r => r.Provider != current && ids.Contains(r.BatchId)` | `Cancelled` +1 |
| Cancel throws | Stops | `Failed` (D-22) |

`OperationCanceledException` always propagates.

Tests:
- `ResumePending_IgnoresBatchesOfAnotherProvider` was revised, not deleted: it keeps its first three assertions and drops the W5 fresh-submission block, which is replaced by a pointer comment.
- Ten `ResolveOtherProvider_*` tests were added, including `ResolveOtherProvider_CancelsForeignBatch_BeforeCurrentProviderSubmits`. That test checks the ledger order: the foreign batch's Cancelled line comes before the OpenAI Submitted line.

## Verification
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors (checked at every commit).
- `--filter "FullyQualifiedName~BatchLedgerTests|FullyQualifiedName~BatchingLlmProviderTests"`: 83 passed.
- `--filter "FullyQualifiedName~Batch"`: 215 passed.
- Every grep acceptance criterion was met. `ResolveOtherProvider_` appears 11 times, because the revised test's comment also names one of the resolver tests.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Test method] Unreadable ledger simulated with a file lock, not garbage**
- **Found during:** Task 1 RED
- **Issue:** The plan said to write garbage into `provider-batches.jsonl` to make Replay unreadable. Replay skips garbage lines (`GarbageAndUnknownType_SkippedNeverThrows`) and stays readable. Only a lock that outlasts the retries makes it unreadable (CR-A05 tests).
- **Fix:** The unreadable-ledger tests hold a `FileShare.None` stream on the ledger, as `CRA05_LockedLedger_IsUnreadable_ProviderRunsLiveAndCancelRefuses` does.
- **Files modified:** ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs, ReelDiscovery.Tests/Ai/BatchLedgerTests.cs
- **Commits:** 52939d3, aa2690c

**2. [Rule 2 - Robustness] Best-effort file delete in the resolver is wrapped**
- **Found during:** Task 2 GREEN
- **Issue:** The plan called `transport.DeleteFilesAsync` directly. `CollectAsync` goes through `DeleteFilesBestEffortAsync`, which catches failures, so a throwing transport would have failed the resolution after the batch was already collected.
- **Fix:** The call is wrapped in try/catch, with `OperationCanceledException` excluded, and `Collected` is counted before the delete.
- **Commit:** 04fec21

### Additions within scope
- `PriceUnusedResults` ignores repeated custom ids within one batch's cache, so an id is never priced twice.
- `ClassifyResults` accepts a null item list.
- Extra tests cover the per-provider counts with an unreadable ledger and with a missing folder.

## TDD Gate Compliance
- Task 1: RED `test(10-02)` 52939d3, then GREEN `feat(10-02)` d96c13d.
- Task 2: RED `test(10-02)` aa2690c, then GREEN `feat(10-02)` 04fec21.
- RED commits used `NotImplementedException` stubs, so the build stayed green under `-warnaserror` while the tests failed at runtime (9 of 9 and 10 of 11; the revised existing test passed).

## Known Stubs
None.

## Threat Flags
None. These helpers only call the existing batch transport methods (status, results, cancel, delete) and the ledger. Threats T-10-06 to T-10-09 are mitigated as planned:
- The resolver blocks instead of resubmitting.
- Only submitted ids are delivered (ClassifyResults).
- FailureMessage is diagnostic only.
- PriceUnusedResults uses the same pricing path as PriceOrphans and returns an empty bill when the ledger is unreadable.

## Self-Check: PASSED
