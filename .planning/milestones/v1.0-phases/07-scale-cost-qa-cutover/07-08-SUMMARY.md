---
phase: 07-scale-cost-qa-cutover
plan: 08
subsystem: ai
tags: [batching, batch-api, ledger, restart-recovery, RUN-03, RUN-04, D-01, D-07, D-09, D-10]
requires:
  - phase: 07-01
    provides: "LlmProviderRequest.AllowBatch, LlmProviderResponse.IsBatch, LlmRequest.AllowBatch, batch pricing in AIModelConfig.CalculateCost"
  - phase: 07-07
    provides: "IBatchTransport + batch DTOs, BatchLedger (Append/Replay/SaveResults/ReadResults), RequestIdentity.CustomIdFor, FakeBatchTransport"
provides:
  - "BatchStage: AsyncLocal stage label (Enter) and live scope (EnterLive / IsLive)"
  - "BatchingLlmProvider: transparent ILlmProvider decorator - per-stage queue, quiet-period flush, cap splitting, submit, poll, collect, deliver, live fallback"
  - "Restart recovery (ResumePending, ledger replay), Consumed bookkeeping, BillOrphansAsync, static CancelPendingAsync for Start Over"
  - "BatchTimings, BatchStatusKind, BatchStatusEvent (StatusChanged, raised off the UI thread)"
  - "First-attempt AllowBatch on ThreadRenderer, FillerRenderer and AttachmentRenderer requests"
affects: [07-10, 07-11]
tech-stack:
  added: []
  patterns: ["decorator behind LlmGateway (units keep awaiting GenerateAsync)", "AsyncLocal stage scoping", "ledger line before wait / Consumed line before delivery"]
key-files:
  created:
    - Services/Ai/BatchStage.cs
    - Services/Ai/BatchingLlmProvider.cs
    - ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs
  modified:
    - Services/Evidence/ThreadRenderer.cs
    - Services/Evidence/FillerRenderer.cs
    - Services/Evidence/AttachmentRenderer.cs
    - ReelDiscovery.Tests/Evidence/EvidencePromptsTests.cs
    - ReelDiscovery.Tests/Evidence/FillerPromptsTests.cs
    - ReelDiscovery.Tests/Evidence/AttachmentPromptsTests.cs
    - ReelDiscovery.Tests/Evidence/CastVoiceTests.cs
    - ReelDiscovery.Tests/Evidence/SideWorldTests.cs
decisions:
  - "[07-08]: BatchTimings carries an injectable Clock (Func<DateTime>, default UtcNow) so NextCheckUtc/slow-poll behavior is testable without wall-clock sleeps"
  - "[07-08]: An item whose every caller cancelled before the flush is dropped from the batch (nobody wants it, never pay for it)"
  - "[07-08]: An item that already errored or expired in a batch runs live on re-request, never in a new batch (D-10)"
  - "[07-08]: A failing StatusChanged subscriber never stops polling or strands waiting units"
  - "[07-08]: If the OrphansBilled append fails the spend is still reported (over-report, never under-report)"
  - "[07-08]: Only evidence first attempts (thread, filler, attachment) are batch-eligible; cast voice, side world, case guide, planner and QA calls stay live"
metrics:
  duration: ~4h (across interrupted executors)
  completed: 2026-10-06
  tasks: 3
  files: 11
---

# Phase 7 Plan 08: Transparent Batching Decorator Summary

`BatchingLlmProvider` is an `ILlmProvider` decorator. It holds first-attempt evidence requests in a queue per stage label (`BatchStage.Enter`) and flushes each queue as one provider batch after a 3 s quiet period, splitting at the transport's count, byte or token cap. It writes a Submitted ledger line before any caller waits, polls every 30 s (60 s after 10 minutes) and writes a Consumed line before handing each result to its caller. Errored, expired and failed items, and items from failed batches, are re-sent live. After a restart it picks up pending batches again, and it bills results that were collected but never used exactly once.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | BatchStage and decorator core (queue, flush, submit, poll, deliver, live fallback) | 1767e38 (RED), ce057bf (GREEN) | Services/Ai/BatchStage.cs, Services/Ai/BatchingLlmProvider.cs, ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs |
| 2 | Restart recovery, Consumed bookkeeping, orphan billing, Start Over cancel | 8136e99 (RED), 688a796 (GREEN) | Services/Ai/BatchingLlmProvider.cs, ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs |
| 3 | First-attempt AllowBatch on evidence request builders | 57e672b | Services/Evidence/{Thread,Filler,Attachment}Renderer.cs + 5 evidence test files |

## What Was Built

- **BatchStage** (51 lines): `AsyncLocal<string?>` label (`Current` defaults to "batch") and `AsyncLocal<bool>` live scope. Both are restored when their scope is disposed.
- **BatchingLlmProvider** (about 1085 lines): all shared state sits under one lock, and callbacks and awaits run outside it.
  - **Routing:** `AllowBatch == false` or `BatchStage.IsLive` sends the request live.
  - **Queuing:** a request first looks for an available result, then attaches to an item already in flight, then joins the queue, so the same custom id is never submitted twice.
  - **Failures:** fatal submit or poll failures (Auth/SpendLimit) come back as Failed responses so the gateway stops the run (T-07-25).
  - **Ledger:** if the Submitted append fails, the provider cancels the batch (best effort) and sends its items live.
  - **Untrusted results:** only ids this batch was submitted with are delivered (T-07-23).
  - **Status events:** Submitted, Progress, Finished, FinishedWithFailures, FailedAtProvider and CollectedAfterRestart.
- **Recovery:** the constructor replays the ledger. Results that were collected but not consumed become available, and items in pending batches for the same provider kind are treated as in flight. `ResumePending()` restarts polling for them. `BillOrphansAsync` prices leftover results at batch prices and writes OrphansBilled. The static `CancelPendingAsync` cancels pending batches through the transport for each record's provider and deletes the batch files.
- **Request flag:** `AllowBatch = attempt == 1` in the three evidence `BuildRequest` methods, with the D-07/D-10 comment.

## Verification

- `BatchingLlmProviderTests`: 25 facts; together with `BatchLedgerTests` that is 38 tests, which passed in 5 consecutive runs (stable).
- Filtered Task 3 run (renderers, prompts, cast voice, side world, all `ReelDiscovery.Tests.Ai`): 303 passed, 0 failed.
- Acceptance greps: `AllowBatch = attempt == 1` appears once in each of the three renderers; `AllowBatch` appears 0 times in CastVoiceBuilder and SideWorldBuilder.
- `dotnet build`: 0 warnings in files touched by this plan. `-warnaserror` still fails on a pre-existing CS8603 in `Forms/WizardForm.cs(153,20)` from 07-12. It is logged in deferred-items.md and is out of scope here.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing test coverage] Task 3 flag assertions**
- **Found during:** Task 3
- **Issue:** The plan listed no tests for the new flag.
- **Fix:** Added AllowBatch true/false assertions for attempts 1 and 2 to the existing BuildRequest tests for thread, filler and attachment requests, plus `AllowBatch == false` assertions for the CastVoice and SideWorld builders.
- **Commit:** 57e672b

**2. [Additive] `BatchTimings.Clock`**
- Not in the planned public surface. It is an injectable clock used by the tests for NextCheckUtc and slow polling. The default is `DateTime.UtcNow`, so production behavior does not change.

The work was split across two interrupted executor sessions. The Task 3 renderer edits from the earlier session were checked against the plan, kept as written, and committed with their tests.

## Deferred Issues

- `Forms/WizardForm.cs(153,20)` CS8603 makes `-warnaserror` builds fail (from 07-12 `CostTooltipForTest`). See deferred-items.md.

## Known Stubs

None. The decorator is not wired into the runner or gateway composition yet; 07-10 and 07-11 own that.

## Self-Check: PASSED
