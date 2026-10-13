---
phase: 04-corpus-plan-answer-key
plan: 22
subsystem: corpus
tags: [approval, answer-key, review-protocol, staleness, tdd]
requires:
  - phase: 04-01
    provides: CorpusState, WrittenCorpusFile, CorpusFileKind, CorpusSettings.ComputeHash
  - phase: 04-05
    provides: AnswerKeyBuilder.Build
  - phase: 04-06
    provides: AnswerKeyCsvWriter / AnswerKeyXlsxWriter
  - phase: 04-07
    provides: ReviewProtocolBuilder.Build / FileNameFor
  - phase: 04-08
    provides: CorpusPlanStore.SaveAsync / Load
  - phase: 04-21
    provides: content-only CaseGuide.ComputeHash, CaseStage.ReviewPlan
provides:
  - CorpusApproval (BuildFiles, WriteFilesAsync, RecordApproval, ApproveAsync, ApplyNewPlanAsync, IsGuideStale, IsSettingsChanged, WithdrawApprovalIfStale, KeepStalePlan, KeepSettingsChange)
  - ApprovalResult, PlanApplyResult, FileWriter and PlanSaver delegates
  - FileLockDetector.IsFileOpenElsewhere (shared open-in-another-program check)
affects: [04-15, 04-18]
tech-stack:
  added: []
  patterns: [injectable writer/saver delegates for failure tests, save-plan-file-before-draft-reference]
key-files:
  created:
    - Services/Corpus/CorpusApproval.cs
    - Helpers/FileLockDetector.cs
    - ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs
  modified: []
key-decisions:
  - "Staleness compares the loaded plan's GuideHash/SettingsHash and falls back to the case.json PlanGuideHash/PlanSettingsHash copy when the plan is not loaded; no plan = never stale"
  - "WriteFilesAsync names the protocol from the case folder's own name (Path.GetFileName) and builds paths only from the folder plus constant names"
  - "ApplyNewPlanAsync keeps WrittenFiles so files from the previous plan read as written for an earlier plan"
requirements-completed: []
duration: 10min
completed: 2026-10-02
---

# Phase 4 Plan 22: Corpus Approval Service Summary

**UI-free CorpusApproval service that writes answer-key.csv/.xlsx and the review protocol atomically before recording approval, saves a new plan file before the draft points at it, and holds the stale-guide / settings-changed / keep-plan rules, with 16 behavioral tests.**

## Performance

- **Duration:** ~10 min
- **Tasks:** 2/2
- **Files:** 3 created

## Accomplishments

- `BuildFiles` returns the three approval files in order: CSV, XLSX, then `{folder} - Review Protocol.docx`. `WriteFilesAsync` writes them through `CaseStore.WriteAtomicAsync` (injectable) and stops at the first IO/access failure. It reports the file name, the error and whether the file is open in another program, and it never changes the draft.
- `ApproveAsync` records approval only after all three writes succeed. Recording sets Approved and ApprovedUtc, replaces WrittenFiles, clears the dismissed stale hash and advances the stage to ReviewPlan.
- `ApplyNewPlanAsync` saves corpus-plan.json first. If the save fails, the draft JSON is byte-identical to before. If it succeeds, the draft is assigned the new plan, PlanId, hashes and created time, approval and both dismissed hashes are reset, and the stage advances to ReviewPlan (D-04 decision).
- `IsGuideStale` uses the content-only hash, so toggling NeedsReview does not make a plan stale. `KeepStalePlan` and `KeepSettingsChange` dismiss only the current hash. `WithdrawApprovalIfStale` removes approval only when the plan is approved, stale and not dismissed.
- `FileLockDetector` copies the StepCaseSummary check: sharing or lock-violation HResults, or a FileShare.None probe. StepCaseSummary itself is unchanged.

## Task Commits

1. **Task 1 RED:** `fd25f07` test(04-22): add failing tests for corpus approval writes and file lock detection
2. **Task 1 GREEN:** `5557532` feat(04-22): add CorpusApproval writes/approval and FileLockDetector
3. **Task 2 RED:** `188e60f` test(04-22): add failing tests for plan apply, staleness and keep-plan rules
4. **Task 2 GREEN:** `85136f7` feat(04-22): add plan apply, staleness and keep-plan rules to CorpusApproval

## Verification

- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 488 passed, 0 failed
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- Acceptance greps: ApproveAsync signature = 1, `CaseStore.WriteAtomicAsync` >= 1, WinForms/MessageBox = 0, `AdvanceStage(CaseStage.ReviewPlan)` = 2, Fact count = 16

## Deviations from Plan

None - plan executed exactly as written.

## TDD Gate Compliance

RED (`test`) commits precede GREEN (`feat`) commits for both tasks. No refactor commit was needed.

## Requirements

KEY-01, KEY-05, PLAN-07 and PLAT-03 are advanced but not completed. The Review Plan step (04-18) and Case Summary (04-15) still have to wire this service into the UI, so REQUIREMENTS.md was not changed.

## Next Phase Readiness

04-18 and 04-15 call `CorpusApproval` for Approve, apply-after-planning, banners and Keep This Plan. Afterwards they save case.json themselves.

## Self-Check: PASSED
