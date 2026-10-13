---
phase: 10-answer-key-batch-safety
verified: 2026-10-13T00:00:00Z
status: human_needed
score: 4/4 roadmap success criteria verified (56/57 plan must-have truths verified in code; 1 is the deferred manual check)
overrides_applied: 0
human_verification:
  - test: "Real-Excel relabel round trip: open answer-key.xlsx and answer-key.csv in Excel, change a Relevance cell, save as xlsx and as csv (also in a ';' list-separator locale and as plain 'CSV (Comma delimited)' ANSI), then click Continue, Regenerate Thread on that thread, reopen the case, and Start Over"
    expected: "The relabel is still in both key files after each action; the Rationale cell of the relabeled row is not garbled (watch curly quotes and dashes after an ANSI CSV save, see WR-03)"
    why_human: "Real Excel save behavior (encoding, separator, shared strings) is only simulated by tests. Deferred by the user on 2026-10-13."
  - test: "At 150% Windows display scaling, open Re-plan Corpus on a plan with manual relabels, and open Corpus Settings for an unapproved plan with relabels"
    expected: "The '{N} manual relabels in the answer key will be dropped.' sentence is fully visible, wraps cleanly, and no button is clipped"
    why_human: "Render tests ran at 120 DPI, not 144, so 150% scaling is not covered. Deferred by the user on 2026-10-13."
  - test: "Live cross-provider resolution: start a batch run on OpenAI, close the app, switch the text provider to Claude with both keys entered, click Continue"
    expected: "Activity reports the OpenAI batches as collected or cancelled before generating; no item is submitted twice; a still-cancelling batch is collected at the next run start"
    why_human: "Live provider cancellation timing and partial-result delivery can only be checked against the real APIs. Optional; deferred by the user on 2026-10-13."
---

# Phase 10: Answer-Key & Batch Safety Verification Report

**Phase Goal:** Manual answer-key relabels and batch spend are never silently lost or double-paid.
**Verified:** 2026-10-13 (HEAD 7cde2f6, after all six review-fix commits)
**Status:** human_needed
**Re-verification:** No. This is the initial verification.

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | A manual relabel in the answer key is preserved (stored separately and re-applied) across Continue, Regenerate Thread, crash recovery and Start Over, and the user is warned before anything would discard it. | VERIFIED | **Storage:** `Services/Corpus/AnswerKeyFiles.cs` `WriteAsync` harvests both key files, applies relabels to the projection and saves `answer-key-relabels.json` (`AnswerKeyRelabelStore`). Snapshots are SHA-256 per file (D-20). **One writer:** the only production calls to `AnswerKeyCsvWriter.Build` and `AnswerKeyXlsxWriter.Build` are in `AnswerKeyFiles.BuildBytes` (grep), guarded by `PlatformTests.AnswerKeyWriters_OnlyCalledFromTheFunnel`. Every writer routes through it: `GenerationOutputs.WriteAnswerKeyAsync` (runner checkpoint l.1057, ThreadInvalidation l.101, Start Over l.1107, reopen l.1254, leave-retry l.1368) and `CorpusApproval.WriteFilesAsync`. **Survives Start Over:** `GenerationReset` does not delete the sidecar or the key files. **Tests:** `AnswerKeyRelabelLifecycleTests` covers Relabel_SurvivesCheckpoint (and Continue), SurvivesRegenerateThread, SurvivesStartOver and SurvivesReopenProjection. `CorpusApprovalTests.ReApprove_SamePlanId_KeepsRelabel` covers re-approval. **Warnings:** Re-plan is the only event that drops relabels (new PlanId, `CorpusPlanner.cs:119`). Both re-plan entry points warn first: `ReplanDialog` gets the count from `PeekRelabels` (`StepReviewPlan.Run.cs:488`), and the unapproved AutoReplan path is preceded by `SettingsNotice(..., relabels)` on Corpus Settings. The CG2/CQ1 "kept" clauses are wired (`StepGenerateCorpus.Run.cs:1050`, `QaFindingsDialog.cs:412`). |
| 2 | Switching text provider never resubmits items still pending in the other provider's batch; those batches are collected or cancelled when that provider's key is available, and the pending-batch banner reflects only resolvable batches. | VERIFIED | `BatchingLlmProvider.ResolveOtherProviderPendingAsync` (l.584) runs in `StepGenerateCorpus.RunCoreAsync` (l.486-526), after the ledger exists and **before** `CreateRunGateway` (l.530). All run buttons (Generate, Continue, banner, Retry Failed) go through `StartRunAsync` → `RunCoreAsync`. The resolver checks keys before any network call and blocks the run if one is missing (D-14). Ended batches are collected under their own provider. Running batches are cancelled through the transport with no Cancelled line, then collected now or at the next run start (D-25/WR-06). A batch the provider no longer has (404) is closed (CR-01). Transient or auth failures block the run (D-22). An exception in the resolver also refuses the run (l.500-504). **Banner:** `PendingBatchCount(folder, planId, provider)` is used at all three sites (`StepGenerateCorpus.cs:1320`, `.Run.cs:617`, `.Run.cs:1355`). Other-provider batches get their own clause or the key-missing notice (`GenerationUiText.BannerBody` l.967-979). Start Over keeps `PendingBatchCountAllPlans`. Activity events are emitted by `GenerationRunner` l.259-263. 15+ resolver unit tests and 7 step tests pass. |
| 3 | Collected-but-unused batch spend from a cancelled run is not lost on Start Over (WR-A06), and live relevant waves do not pay again for requests already pending in an earlier session's batch (WR-A08). | VERIFIED | **WR-A06:** Start Over (`StepGenerateCorpus.Run.cs:1072-1090`) prices `BatchingLlmProvider.PriceUnusedResults` only when the cancel succeeded. It records the spend under "Batch results not used" only after `GenerationReset` has run and only if `provider-batches.jsonl` is gone (`RecordUnusedResultsIfLedgerGone`, l.1180; WR-05 fix). Tests: StartOver_RecordsUnusedBatchResults, _CancelFailed_RecordsNothing, _LedgerDeleteFails_RecordsNothing, _UnreadableLedger_RecordsNothing, and PriceUnusedResults_MatchesPriceOrphans. **WR-A08:** `GenerationRunner.cs:298-302` calls `batch.DrainStaleStagesAsync(label => label.StartsWith("wave "))` right after RunStarted and before voice cast and waves. `DrainStaleStagesAsync` (`BatchingLlmProvider.cs:289`) cancels through `_transport.CancelAsync` with no Cancelled line (D-21), waits on `Ended` within `DrainTimeout`, and otherwise keeps polling. `Ended.TrySetResult` is called in `UntrackLocked` (l.1287). `CloseStaleWaveBatchesAsync`, `CancelPendingStagesAsync` and `IsClosed` are gone (grep finds 0). Test: `BatchParityTests.StaleWaveBatch_DrainedBeforeWaves_NotPaidTwice` (both cancel outcomes). |
| 4 | The leftover legacy `Attachment` / `AttachmentComplexity` types are removed and guarded by the deleted-types test. | VERIFIED | `Models/Attachment.cs` no longer exists. `Models/AttachmentType.cs` holds `enum AttachmentType` with the same members in the same order as `d0307d7:Models/Attachment.cs`. Grep finds no `class Attachment` or `AttachmentComplexity` outside the test. `PlatformTests.LegacyType_IsDeleted` has `[InlineData("ReelDiscovery.Models.Attachment")]` and `[InlineData("ReelDiscovery.Models.AttachmentComplexity")]` (l.138-139). |

**Score:** 4/4 roadmap success criteria verified.

### Plan must-have truths (10-01..10-11)

There are 57 truths across 11 plans. All except one are supported by the code and by named tests in the green suite. Two need a note:

- **10-02 truth 2 and 10-07 truth 1** say running foreign batches are cancelled "through CancelPendingAsync with include r.Provider != current". Since commit 420906e (WR-06), the code instead cancels through the transport with no Cancelled line, and collects the batch now or at the next run start. D-25 in 10-CONTEXT.md (locked 2026-10-13) records this as a refinement of D-13. I verified against D-25. The intent (collect or cancel, never resubmit, itemize partial spend) is met more strictly than the original wording required.
- **10-11 truth 5** says "a person has checked the real-Excel round trip and 150% DPI". This was deferred by the user and is outstanding. It is listed under Human Verification, not counted as passed.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Services/Corpus/AnswerKeyReader.cs` | CSV/XLSX read-back, shared read | VERIFIED | 469 lines; used by `AnswerKeyFiles.Harvest` |
| `Services/Corpus/AnswerKeyRelabelStore.cs` | Sidecar `answer-key-relabels.json` | VERIFIED | 130 lines; Load/Save used in `WriteAsync` and `PeekRelabels` |
| `Models/Corpus/AnswerKeyRelabels.cs` | Relabel, snapshot, set DTOs (+ Notes from CR-02) | VERIFIED | Used by the funnel |
| `Services/Corpus/AnswerKeyFiles.cs` | Harvest/apply/write funnel | VERIFIED | 539 lines; the single production key writer |
| `Models/AttachmentType.cs` | Enum moved verbatim | VERIFIED | Same member order as before |
| `Services/Ai/BatchLedger.cs` | Provider-filtered counts | VERIFIED | `PendingBatchCount(…, provider)` and `PendingBatchCountOtherProviders` |
| `Services/Ai/BatchingLlmProvider.cs` | Resolver, PriceUnusedResults, DrainStaleStagesAsync | VERIFIED | All three wired to callers |
| `Services/Evidence/GenerationRunner.cs` | Drain call and other-provider Activity | VERIFIED | l.259-263, l.298-302 |
| `Services/Evidence/GenerationOutputs.cs`, `Services/Corpus/CorpusApproval.cs` | Go through the funnel | VERIFIED | `AnswerKeyFiles.WriteAsync(` in both |
| `UserControls/GenerationUiText.cs`, `QaUiText.cs`, `CorpusUiText.cs` | D-06/D-07/D-08/D-15 copy | VERIFIED | Copy matches the D-06/07/08 text exactly |
| `Forms/ReplanDialog.cs`, `UserControls/StepCaseSummary.cs`, `StepCorpusSettings.cs`, `StepReviewPlan.Run.cs`, `StepGenerateCorpus*.cs` | Counts wired to dialogs and notices | VERIFIED | See key links |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| AnswerKeyReader.StripCsvGuard | AnswerKeyCsvWriter.IsGuarded | shared guard list | WIRED |
| AnswerKeyFiles | AnswerKeyReader.ReadShared/ParseCsv/ParseXlsx | Harvest | WIRED |
| AnswerKeyFiles | AnswerKeyRelabelStore.Save | after the writes, under the per-case gate | WIRED |
| GenerationOutputs / CorpusApproval | AnswerKeyFiles.WriteAsync | delegation (`stopAtFirstFailure: true` on approval) | WIRED |
| StepGenerateCorpus.RunCoreAsync | ResolveOtherProviderPendingAsync | `Task.Run`, before CreateRunGateway | WIRED |
| GenerationRunner.RunAsync | DrainStaleStagesAsync | after the RunStarted append when `batch != null` | WIRED |
| UntrackLocked | TrackedBatch.Ended | `Ended.TrySetResult()` | WIRED |
| Start Over | PriceUnusedResults → UsageTracker | priced when `!cancelFailed`, recorded once the ledger is gone | WIRED |
| Start Over | CG2StartOver(relabelsKept:) | PeekRelabels count in the Task.Run tuple | WIRED |
| QaFindingsDialog | CQ1RegenerateThread(relabelsKept) | `selected.RelabelsKept` from `StepCaseSummary.cs:2340` | WIRED |
| StepReviewPlan / StepCorpusSettings | PeekRelabels → ReplanDialog / SettingsNotice | off the UI thread | WIRED |
| StepReviewPlan ApproveAsync | CorpusApproval.WriteFilesAsync(overlay:) | Task.Run (WR-07) | WIRED |

### Data-Flow Trace (Level 4)

| Artifact | Data | Source | Real data | Status |
|----------|------|--------|-----------|--------|
| Answer-key files | relabels | Harvest of the on-disk CSV/XLSX + sidecar | Yes (lifecycle tests write real files and read them back) | FLOWING |
| Pending-batch banner | `_pendingProviderBatches`, `_otherProviderBatches` | `BatchLedger.Replay()` filtered by provider | Yes | FLOWING |
| Start Over spend | OrphanBill lines | ledger + `batch-results/` cache | Yes | FLOWING |
| Re-plan / settings / CG2 / CQ1 warnings | relabel count / DocID set | `AnswerKeyFiles.PeekRelabels` | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full suite at HEAD 7cde2f6 (after the six review fixes) | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` | Passed 4697, Failed 0, Skipped 0 (10 m 53 s); log `obj/phase10-verify-test.log` | PASS |
| Scale run (batching changed after the gate: CR-01, WR-06) | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Scale" --no-build` | Passed 2, Failed 0 (4 m 29 s) | PASS |
| Funnel bypass gate | grep `AnswerKey(Csv\|Xlsx)Writer.Build(` outside tests | only `AnswerKeyFiles.cs:194-195` | PASS |
| Removed batching methods | grep `CloseStaleWaveBatchesAsync\|CancelPendingStagesAsync\|IsClosed` | 0 hits | PASS |
| Legacy types | grep `AttachmentComplexity\|class Attachment\b` | only the PlatformTests InlineData | PASS |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|----------|
| QA-01 | 10-01, 03, 05, 08, 09, 10, 11 | Blind review; disagreements can be relabeled or regenerated | SATISFIED (hardened) | Relabels persist and are re-applied (SC1) |
| KEY-03 | 10-03, 05, 08, 11 | Labels come from the plan at generation time | SATISFIED | `AnswerKeyBuilder.Build` stays a pure projection; relabels are applied after it, key files only (D-11) |
| RUN-03 | 10-02, 04, 07, 11 | Batch API for lower cost | SATISFIED (hardened) | Cross-provider resolver, stale-wave drain |
| RUN-04 | 10-02, 04, 06, 07, 11 | Batches tracked on disk, collected after restart | SATISFIED (hardened) | Foreign and stale batches stay tracked until collected |
| PROV-01 | 10-02, 06, 07, 11 | Choose Claude or OpenAI | SATISFIED (hardened) | A provider switch no longer strands batches (W5) |
| PROV-07 | 10-02, 06, 07, 11 | Usage/cost tracking incl. batch discounts | SATISFIED (hardened) | WR-A06 Start Over billing; orphan billing for foreign and stale batches |
| PLAT-05 | 10-01, 04, 11 | Dead code removed | SATISFIED | Attachment/AttachmentComplexity deleted and guarded; dead batching methods removed |

All seven phase IDs appear in plan frontmatter and in REQUIREMENTS.md. No orphaned requirements: REQUIREMENTS.md maps none of these IDs to Phase 10, since the phase hardens IDs owned by Phases 1/4/7.

### Anti-Patterns / Open Review Advisories (not fixed; judged against the success criteria)

| Item | File | Severity | Impact on SC |
|------|------|----------|--------------|
| WR-03: a CSV that is not valid UTF-8 is decoded as Latin-1 (`AnswerKeyReader.cs` Decode l.233-247) | `Services/Corpus/AnswerKeyReader.cs` | WARNING | The Relevance relabel itself survives, because the values are ASCII. On a **relabeled** row saved by Excel as ANSI CSV, a projected rationale containing cp1252 punctuation reads as a "custom" rationale and is written back garbled. The user's label is kept, but the user's key text can be silently altered. The real-Excel human check should cover this. |
| WR-02: strict legacy ownership (`AnswerKeyFiles.LegacyOwner`) | `Services/Corpus/AnswerKeyFiles.cs` | WARNING | Applies only when no snapshot exists: a pre-Phase-10 key, or a deleted or damaged sidecar. In that case one added or deleted row drops every relabel in the file. The normal post-Phase-10 path always has a snapshot, so SC1 holds on the supported path. |
| WR-08: `PeekRelabels` catches only I/O exceptions; the call sites in `StepCaseSummary.cs:1805` and `StepCorpusSettings.cs:1038` are unguarded | UI | WARNING | Unlikely to trigger (OOM, or a plan edited by hand). If it does, it can crash an async-void path. It does not discard relabels. |
| WR-04: repeated backups of a damaged key file that is also locked | `AnswerKeyFiles.cs` | WARNING | Clutters the case folder; nothing is lost |
| WR-09: no cap on decompressed XLSX size | `AnswerKeyReader.cs` | WARNING | Robustness only |
| WR-10: resolver has no timeout, progress or cancel | `StepGenerateCorpus.Run.cs` | WARNING | UX; the ledger stays consistent |
| IN-02: `CorpusApproval.BuildFiles` builds key bytes without a harvest | `CorpusApproval.cs:39` | INFO | Not used in production (tests only); writes nothing |
| By design (D-13/D-25): items a foreign batch finished before the cancel are paid there and again under the current provider | `BatchingLlmProvider.cs` | INFO | Accepted trade-off. That spend is itemized as orphans, not silent, so it is consistent with "never silently ... double-paid". |
| Unreadable saved plan → "Retry Planning" creates a new PlanId without a relabel count | `StepReviewPlan.Run.cs:411` | INFO | Relabels cannot be counted without the plan; edge case |

No blocker anti-patterns, stubs or placeholders were found in the phase files.

### Human Verification Required

1. **Real-Excel relabel round trip.** Edit Relevance in Excel and save as xlsx, as UTF-8 CSV, as ANSI "CSV (Comma delimited)", and in a `;` locale. Then click Continue, Regenerate Thread, reopen the case, and Start Over. Expected: the relabel persists in both files, and the Rationale of relabeled rows is not garbled. Why human: real Excel behavior; deferred by the user on 2026-10-13.
2. **150% DPI.** Check the ReplanDialog relabel sentence and the Corpus Settings relabel notice at 150% scaling. Expected: no clipping. Why human: renders ran at 120 DPI; deferred by the user on 2026-10-13.
3. **Live cross-provider resolution (optional).** Use both keys, switch provider with pending batches, and click Continue. Expected: the Activity collect/cancel lines appear and nothing is resubmitted twice. Why human: live API timing; deferred.

### Gaps Summary

No blocking gaps. All four roadmap success criteria hold in the code at HEAD 7cde2f6:
- the full suite passed (4697/4697) after the six post-gate fix commits;
- Scale passed (2/2);
- every key link traced to a real caller.

The status is `human_needed` because the user deferred the real-Excel round trip and the 150% DPI checks; they are outstanding, not passed. Before closing the milestone, the user should weigh WR-03: ANSI CSV saves can alter the Rationale text of relabeled rows. The real-Excel check is the natural place to confirm or rule it out.

---

_Verified: 2026-10-13_
_Verifier: Claude (gsd-verifier)_
