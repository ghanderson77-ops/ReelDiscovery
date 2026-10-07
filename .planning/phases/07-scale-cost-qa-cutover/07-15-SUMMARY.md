---
phase: 07-scale-cost-qa-cutover
plan: 15
subsystem: qa
tags: [qa-scans, end-of-run-qa, QA-03, QA-02, D-14, D-15, U33]
requires:
  - phase: 07-13
    provides: "JournalReplay.InvalidatedThreadIds, DeltasByThread that keeps invalidated deltas"
  - phase: 07-14
    provides: "ScanReport/ScanFinding/ScanCheck, QaReportStore, LeakageAudit"
  - phase: 07-11
    provides: "Generate Corpus run wiring (RunCoreAsync completion block)"
provides:
  - "CorpusScans.Run(draft, plan, replay, overlay) -> ScanReport (QA-03, no model call)"
  - "QaRunner.RunEndOfRunAsync(draft, plan, caseFolder, overlay, ct) -> (ScanReport?, LeakageReport?)"
  - "GenerationRunner.BuildScaffold(draft, plan) -> RunScaffold { Waves, WaveOf, Assignments, People } (internal)"
  - "AttachmentRepair.Accepted (internal): accepted RenderedThread rebuilt from a ThreadDone record"
  - "StepGenerateCorpus.QaRun test seam; QA pass fired after every started run"
affects: [07-17, 07-18, 07-21]
tech-stack:
  added: []
  patterns: ["as-of-wave re-check: ledger from deltas of earlier waves only", "fire-and-forget report-only pass after the UI is restored"]
key-files:
  created:
    - Services/Qa/CorpusScans.cs
    - Services/Qa/QaRunner.cs
    - ReelDiscovery.Tests/Qa/CorpusScansTests.cs
    - ReelDiscovery.Tests/Qa/QaRunnerTests.cs
  modified:
    - Services/Evidence/GenerationRunner.cs
    - Services/Evidence/AttachmentRepair.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
decisions:
  - "[07-15]: Journaled violations carry no rule code, so they are classified by the RenderValidator wording (V4 'dated after this email' / 'does not know yet', V3 'the case value is' / 'not a case document number', V6 'earlier emails say' / 'adds a new detail after later emails'). Other rules (people, length, legal wording) are not scan findings"
  - "[07-15]: Excel attachments get an empty VariantText in the scans. Flattened line items are not labelled cells, so V3 and the V6 near-amount scan skip them (Pitfall 5). V4 and the V6 date/delta checks still run"
  - "[07-15]: Attachment findings use the parent email's DocId, and the text is prefixed with the attachment title ('{title}: {rule text}')"
  - "[07-15]: Non-relevant attachments are also scanned for printed case values (Attachment check, Relevant false), as well as the bodies"
  - "[07-15]: CorpusScans.Run leaves JournalUtc null; QaRunner sets it from the journal's last write time"
  - "[07-15]: The step passes a null overlay. QaRunner rebuilds it off the UI thread with GenerationRunState.BuildFullOverlay, which is the same overlay the answer-key projection uses"
  - "[07-15]: The QA pass runs after every run that started (outcome other than None, or an unexpected exception). QaRunner skips unreadable or plan-changed journals and runs with no written email"
metrics:
  duration: ~25min
  completed: 2026-10-06
  tasks: 2
  files: 8
---

# Phase 7 Plan 15: Corpus scans and automatic end-of-run QA Summary

QA-03 timeline, value and story-detail scans now re-run the tuned RenderValidator rules over the journaled corpus, using each thread's ledger as it stood before its wave. At the end of every run, a QA pass off the UI thread writes `qa-scans.json` and `qa-leakage.json`. It is report-only and makes no model calls.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | CorpusScans over the journal with as-of-wave context | a12c8fb (RED), c161cc6 (GREEN) |
| 2 | QaRunner and the automatic end-of-run hook | 4981188 (RED), 6fa3428 (GREEN) |

## What was built

- **`GenerationRunner.BuildScaffold`**: the scaffolding that `RunAsync` used to build inline (waves, wave map, all assignments, people directory) is now an `internal static` helper returning `RunScaffold`. `RunAsync` calls it, with no change in behavior; the existing runner tests pass.
- **`AttachmentRepair.Accepted`**: changed from private to internal so the scans rebuild the accepted `RenderedThread` with exactly the repair code.
- **`CorpusScans.Run`** works through every ThreadDone in `replay.LastByThread`, skipping threads in `InvalidatedThreadIds`:
  - **Relevant threads:** the ledger is `LedgerMerger.Build` over `DeltasByThread` of earlier-wave threads only, and the context comes from `ThreadRenderContext.Create`. `RenderValidator.Check` results are filtered to V4 (Timeline), V3 (Value) and V6 (Story detail). `RenderValidator.CheckAttachment` runs on each AttachmentDone of the thread (Attachment), using the attachment's own deltas as NewDetails.
  - **Non-relevant threads:** `CanonicalValueScanner.Scan` hits on bodies and attachment text that are `IsRegistered` against the guide values produce "prints the case value {value}".
  - **Warnings threads:** their journaled violations, and the attachments' journaled violations, are merged in. Findings are de-duplicated by DocId + Check + text.
  - Each finding carries the thread id, the answer-key DocId and subject (overlay-rendered), the sender-local date sent, the check, the text and relevance. Findings are ordered by DateSent, then DocId, Check and Text.
  - Every rule call is guarded so it never throws. A thread whose context cannot be built is skipped.
- **`QaRunner.RunEndOfRunAsync`** runs inside `Task.Run`:
  1. Replays the journal. If it is unreadable, the plan changed, or no email was written, it returns (null, null).
  2. Runs the scans and calls `SaveScans` with `JournalUtc`.
  3. Runs `LeakageAudit.Run` and calls `SaveLeakage`.
  - OperationCanceledException is rethrown. Any other exception is swallowed and the method returns whatever finished.
- **Generate Corpus**: after `RunCoreAsync` restores the UI and raises StateChanged, it fires `_ = RunQaAfterRunAsync(plan, folder)`. That call catches everything and raises StateChanged again when it finishes, so Case Summary reads fresh reports. A step-lifetime `_qaCts` is cancelled on Dispose.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- `--filter "FullyQualifiedName~ReelDiscovery.Tests.Qa|FullyQualifiedName~GenerateCorpus&Category!=Render"`: 118 passed.
- Full non-render suite (`Category!=Render&Category!=Scale`): 3675 passed, 0 failed.
- Acceptance greps:
  - `RenderValidator.Check(|CheckAttachment(` in CorpusScans: 2
  - `CanonicalValueScanner`: 2
  - `ILlmGateway`: 0
  - `QaRunner.RunEndOfRunAsync(` in the step: 1
  - `SaveScans(|SaveLeakage(` in QaRunner: 2

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Fixture] The V6 test compares earlier and later waves instead of using a same-wave thread**
- **Found during:** Task 1
- **Issue:** In the ApprovedCase fixture, every relevant thread is alone in its wave, so there is no second wave-1 thread to hold the contradicting text.
- **Fix:** A theory with both value orders. The finding is always on the later-wave thread and never on the earlier one, which proves the earlier wave never sees a later ledger (the "no earlier ledger, no V6" behavior).
- **Commit:** c161cc6

**2. [Rule 1 - Bug] Disposing the step twice threw ObjectDisposedException on the QA token**
- **Found during:** Task 2 (`PersistFromWorker_WithoutWindowHandle_NeverAppliesOffTheUiThread`)
- **Fix:** `_qaCts.Cancel()` is guarded like the existing `_cts.Cancel()`.
- **Commit:** 6fa3428

**3. Shared helper outside files_modified:** `Services/Evidence/AttachmentRepair.cs` changed only by making `Accepted` internal. The plan allows this ("extract a static helper if it is private").

**4. Additional tests:** a step test (`RunEnd_WritesQaReportsAfterNavigationIsBack`), a test that only failed threads write nothing, and a test that a journaled V4 line the re-check no longer finds is still reported.

## Requirements

QA-03 and QA-02 stay Pending in REQUIREMENTS.md. This plan computes and stores the scan and leakage reports at every run end. Showing them to the user (findings dialog, Case Summary Quality checks) comes in 07-17 and 07-18.

## TDD Gate Compliance

Each task has a `test(...)` RED commit (the build failed without the new type), followed by a `feat(...)` GREEN commit.

## Known Stubs

None.

## Self-Check: PASSED
- FOUND: Services/Qa/CorpusScans.cs, Services/Qa/QaRunner.cs, ReelDiscovery.Tests/Qa/CorpusScansTests.cs, ReelDiscovery.Tests/Qa/QaRunnerTests.cs
- FOUND commits: a12c8fb, c161cc6, 4981188, 6fa3428
