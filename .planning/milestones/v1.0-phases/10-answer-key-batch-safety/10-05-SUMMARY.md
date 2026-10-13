---
phase: 10-answer-key-batch-safety
plan: 05
subsystem: corpus/answer-key
tags: [answer-key, relabel, funnel, approval, source-scan]
requires:
  - "10-01: AnswerKeyReader, AnswerKeyRelabelStore"
  - "10-03: AnswerKeyFiles.WriteAsync / BuildBytes, AnswerKeyWriteResult, AnswerKeyBackup"
provides:
  - "GenerationOutputs.WriteAnswerKeyAsync as a thin wrapper over AnswerKeyFiles.WriteAsync (writers 1-5)"
  - "CorpusApproval.WriteFilesAsync/ApproveAsync through the funnel with an optional AnswerKeyOverlay (writer 6, D-12 seam)"
  - "GenerationFileResult.Backups, ThreadInvalidationResult.KeyBackups, ApprovalResult.Backups"
  - "PlatformTests.AnswerKeyWriters_OnlyCalledFromTheFunnel source-scan guard"
affects:
  - "Plan 10-08: the Review Plan caller can pass the run-state overlay to ApproveAsync(overlay:)"
tech-stack:
  added: []
  patterns:
    - "Source-scan guard: production .cs files may call AnswerKeyCsvWriter.Build/AnswerKeyXlsxWriter.Build only in AnswerKeyFiles.cs"
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/AnswerKeyRelabelLifecycleTests.cs
  modified:
    - Services/Evidence/GenerationOutputs.cs
    - Services/Evidence/ThreadInvalidation.cs
    - Services/Corpus/CorpusApproval.cs
    - ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs
    - ReelDiscovery.Tests/PlatformTests.cs
decisions:
  - "Approval writes the two key files through the funnel with stopAtFirstFailure: true, then the review protocol with the existing try/catch; write order and stop-at-first-failure unchanged (D-26)"
  - "A damaged key backup is carried as data on every result type and never sets KeyError/FailedFileName (Pitfall 9)"
metrics:
  duration: "~35 min"
  completed: 2026-10-12
  tasks: 2
  files: 6
---

# Phase 10 Plan 05: Answer-Key Writers Through the Funnel Summary

All six answer-key writers now go through `AnswerKeyFiles.WriteAsync`. Manual relabels survive run checkpoints, Continue, Regenerate Thread, Start Over, reopen/crash recovery and re-approval of the same plan. A source-scan test fails if any production file calls the CSV/XLSX writers directly.

## What Was Built

### Task 1: Run-side writers (RED 0a6b8db, GREEN c2df797)
- `GenerationOutputs.WriteAnswerKeyAsync` keeps its signature and now only delegates to the funnel (`stopAtFirstFailure: false`) and maps the result, including `Backups`. `Stamp`/`RecordFailure` stay for `WriteStorySoFarAsync`. No call site changed (GenerationRunner, ThreadInvalidation, Start Over, leave retry, reopen).
- `ThreadInvalidationResult.KeyBackups` is filled from the rewrite; `KeyError` is still set only on a failed write.
- `AnswerKeyRelabelLifecycleTests` (7 facts) runs a full fake-provider run, relabels the CSV as a user would, and checks the relabel in both files after: a checkpoint write (Status Generated) and the following Continue write; Regenerate Thread (thread rows back to Planned, KeyError empty); Start Over plus an empty-overlay write (sidecar still present); the reopen projection (`BuildFullOverlay`). It also checks that a damaged CSV is reported in `Backups` (and in `KeyBackups` during Regenerate Thread), and that a locked CSV is reported as open elsewhere while the XLSX is still written.

### Task 2: Approval and the guard (RED 776d5f7, GREEN 942321c)
- `ApprovalResult.Backups` was added.
- `BuildFiles` takes optional `overlay` and `relabels` and builds the key bytes with `AnswerKeyFiles.BuildBytes`.
- `WriteFilesAsync` and `ApproveAsync` take `AnswerKeyOverlay? overlay = null` after `writer` and before `ct`, so existing positional calls still compile. The key goes through the funnel with `stopAtFirstFailure: true`, then the review protocol is written with the same try/catch. A failed key write returns before the protocol is written.
- New CorpusApprovalTests:
  - `ReApprove_SamePlanId_KeepsRelabel`
  - `ReApprove_WithOverlay_KeepsStatuses`
  - `Approve_NewPlanId_PlainKey_PrunesOldRelabels`
  - `Approve_CsvWriteFails_StopsBeforeXlsx` (a recording writer shows only answer-key.csv was attempted)
  - `Approve_DamagedXlsx_BackupReported`
- `PlatformTests.AnswerKeyWriters_OnlyCalledFromTheFunnel` follows the `OnlyServicesAiReferencesProviderSdks` pattern (same root discovery, `IsExcluded`, and a vacuous-pass guard that requires the funnel file to be found).
- The CSV edit helpers (`CsvCellOf`, `RelabelCsvFile`) are `internal static` on `AnswerKeyRelabelLifecycleTests` and are reused by CorpusApprovalTests.

## Verification
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors.
- Task 1 filter (AnswerKeyRelabel, ThreadInvalidationTests, AnswerKeyOverlayTests, GenerationRunnerTests, GenerationResetTests): 102 passed.
- Plan verification filter (AnswerKey, CorpusApprovalTests, PlatformTests, ThreadInvalidationTests, ReviewPlanLogicTests): 282 passed, 0 failed.
- RED gates: 6 of 7 lifecycle tests and 5 of the new approval/guard tests failed before implementation. The locked-file test and the CSV-failure stop test passed already, because the old code kept those behaviors.
- Acceptance greps:
  - `AnswerKeyFiles.WriteAsync(` in GenerationOutputs = 1
  - direct writer `Build(` calls in GenerationOutputs = 0 and in CorpusApproval = 0
  - `stopAtFirstFailure: true` = 1
  - overlay/ct signature = 2
  - production scan lists only `Services/Corpus/AnswerKeyFiles.cs`

## Deviations from Plan

**1. [Rule 2 - Coverage] An extra lifecycle test.** `DamagedKey_DuringRegenerateThread_ReportedAsBackup` checks that `KeyBackups` is populated and `KeyError` stays empty when the rewrite after Regenerate Thread backs up a damaged CSV. That is the Pitfall 9 path the plan's threat T-10-21 names.

**2. Surface stubs in the RED commits.** The RED commits include the result-type `Backups`/`KeyBackups` properties and the new overlay parameters (unused), so the failing tests compile. The behavior arrived in the GREEN commits.

Otherwise the plan was executed as written.

## TDD Gate Compliance
- Task 1: `test(10-05)` 0a6b8db, then `feat(10-05)` c2df797.
- Task 2: `test(10-05)` 776d5f7, then `feat(10-05)` 942321c.
- No refactor commits.

## Known Stubs
None.

## Threat Flags
None. T-10-19 (source-scan guard), T-10-20 (PlanId mismatch, so the old plan's relabels are pruned) and T-10-21 (backups carried as data) are mitigated and tested.

## Self-Check: PASSED
- FOUND: ReelDiscovery.Tests/Evidence/AnswerKeyRelabelLifecycleTests.cs
- FOUND: 0a6b8db, c2df797, 776d5f7, 942321c
