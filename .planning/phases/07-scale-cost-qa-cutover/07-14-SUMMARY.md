---
phase: 07-scale-cost-qa-cutover
plan: 14
subsystem: qa
tags: [leakage-audit, qa-reports, logistic-regression, D-14, D-23]
requires:
  - phase: 07-03
    provides: "D-23 daily privilege parity (the audit proves the privilege leak is gone)"
provides:
  - "Models/Qa: LeakageReport/LeakageSignal/LeakageVerdict, ScanReport/ScanFinding/ScanCheck, BlindReviewVerdict/BlindReviewHeader"
  - "QaReportStore: qa-scans.json / qa-leakage.json (atomic, never-throwing reads) and qa-blind-review.jsonl (append, latest header wins)"
  - "LogisticRegression (internal): deterministic L2 batch GD, BalancedAccuracy, rank AUC"
  - "LeakageAudit.Run(plan, guide, overlay, generatedOnly) / VerdictFor / AssignFolds (QA-02)"
affects: [07-10, 07-15, 07-16, 07-17, 07-18]
tech-stack:
  added: []
  patterns: ["thread-grouped StableRank folds", "out-of-fold balanced accuracy at the prevalence threshold"]
key-files:
  created:
    - Models/Qa/LeakageReport.cs
    - Models/Qa/ScanReport.cs
    - Models/Qa/BlindReviewVerdict.cs
    - Services/Qa/QaReportStore.cs
    - Services/Qa/LogisticRegression.cs
    - Services/Qa/LeakageAudit.cs
    - ReelDiscovery.Tests/Qa/QaReportStoreTests.cs
    - ReelDiscovery.Tests/Qa/LogisticRegressionTests.cs
    - ReelDiscovery.Tests/Qa/LeakageAuditTests.cs
  modified: []
decisions:
  - "[07-14]: Out-of-fold balanced accuracy uses the relevant share as the cut-off, not 0.5 (a 0.5 cut on 20/80 data hides real leakage, T-07-41)"
  - "[07-14]: Attachment kinds come from the plan's PlannedAttachment.Kind (Word, Excel, PowerPoint, image, calendar invite, voicemail); synthetic rows fall back to the file extension"
  - "[07-14]: CC and thread depth signals are cumulative thresholds ('n or more CC recipients' n=1..3, 'thread depth n+' n=2..4); the model uses CC count and depth buckets 1/2/3/4+"
  - "[07-14]: A feature value on every audited email is never a signal; a one-class training fold predicts the training share"
  - "[07-14]: D-23 before/after proof runs the real planner at 250 emails with PrivilegedPercent 20; the default 8% gives only 2 privileged relevant emails (below support 5)"
metrics:
  duration: 7min
  completed: 2026-10-06
  tasks: 2
  files: 9
---

# Phase 7 Plan 14: Leakage audit and shared QA report storage Summary

QA-02 is now a pure C# service: a thread-grouped, 5-fold L2 logistic regression on answer-key metadata only, with a Pass/Warning/Fail/Not enough data verdict and plain-words top signals. It comes with the shared QA report models and the `QaReportStore` that the later QA plans write to.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | QA report models, QaReportStore, LogisticRegression | 3f4a7b1 (RED), 92cf040 (GREEN), 5992d98 (refactor) |
| 2 | LeakageAudit: grouped CV, verdict thresholds, top signals | 41b49ea (RED), a9c22a0 (GREEN) |

## What was built

- **Models (`ReelDiscovery.Models.Qa`):** the plan's types, implemented exactly as specified.
- **`QaReportStore`:** has the constants `ScansFileName`, `LeakageFileName` and `BlindReviewFileName`. Each path is the case folder combined with one of those names. JSON writes are atomic: a temp file with WriteThrough, then `File.Move(overwrite)`, with 5 attempts 100 ms apart, and the temp file is cleaned up on failure. Reads catch IO, access, JSON and NotSupported errors and return null. The blind-review jsonl writes `{Kind, Header|Verdict}` lines through a SemaphoreSlim gate, repairs a torn last line by adding a newline, and flushes to disk. On load the latest header wins, verdicts after it are kept with the last one per DocId winning in first-seen order, torn lines are skipped, and verdicts written before any header are ignored.
- **`LogisticRegression`:** standardizes each column (a std of 0 becomes 1). It runs 300 fixed batch gradient-descent iterations with `lambda / n` applied to the weights but not the bias, and uses a numerically stable sigmoid. There is no Random, and the same input always gives the same weights.
- **`LeakageAudit`:**
  - **Input rows:** `AnswerKeyBuilder.Build` email rows only. Attachment rows are never audited; their kinds become features of the parent email. Generated / Generated - with warnings rows are used when any exist, otherwise every row.
  - **Features:** top-20 senders and top-20 To/Cc addresses (each with an "other" bucket), To count, CC count, weekday, hour bucket in the sender's local clock, month offset, subject length bucket (measured without the privileged tag), attachment count and one flag per kind, thread depth buckets, privilege banner, and privileged subject tag.
  - **Folds:** `StableRank(threadId) % k` for k = 5 down to 2, stopping at the first k where every fold has both classes.
  - **Signals:** feature values with support of at least 5, ranked by |P - base| x support, ties broken by ordinal order, top 10. Feature text follows UI-SPEC R3.

## Measured results (real-planner 250-email fixture, seed 2106)

| Plan | Verdict | BA | AUC | Privilege banner signal |
|------|---------|----|-----|-------------------------|
| PrivilegedPercent 20, pre-D-23 seam | Fail | 85.3% | 0.90 | `privilege banner: only on relevant mail` (n=9, ranked 3rd) |
| PrivilegedPercent 20, as 07-03 builds it | Fail | 77.2% | 0.82 | absent (daily privileged threads exist) |
| Default (8%) | Warning | 67.7% | 0.74 | n/a (2 privileged relevant emails) |

Without the privilege banner, the remaining signal comes from senders (Raymond Harmon and Maria Delgado send about 45% relevant mail against a 20% base rate) and from weekday skew. Per D-14/D-06 this is a measurement only. The audit does not change the corpus, and the UI tooltip already notes that real custodians and dispute dates predict relevance. Whether planner sender weighting should be tuned is a follow-up decision and is out of scope here.

## Deviations from Plan

**1. [Rule 1 - Correctness] Balanced-accuracy cut-off at the relevant share instead of the 0.5 default**
- **Found during:** Task 2
- **Issue:** A 0.5 cut-off on 20/80 data labels almost everything non-relevant. That pulls BA toward 50% and hides leakage (T-07-41 false comfort).
- **Fix:** `LogisticRegression.BalancedAccuracy(labels, p, threshold: relevant / n)`. The helper still defaults to 0.5, as planned.
- **Commit:** a9c22a0

**2. [Rule 3 - Fixture] D-23 proof uses PrivilegedPercent 20**
- **Found during:** Task 2
- **Issue:** At the default 8%, the 250-email real-planner plan has 2 privileged relevant emails. That is below `MinSignalSupport` 5, so no privilege signal can appear before or after D-23.
- **Fix:** both before/after tests set `PrivilegedPercent = 20`, which gives 9 privileged relevant emails. The post-D-23 test also asserts that support is at least 5, so the "absent" result is meaningful.
- **Commit:** a9c22a0

**3. Internal test seams:** `LeakageAudit.RunRows(rows, planId, generatedOnly, attachmentKinds)` and `AssignFolds` are internal (the project already has InternalsVisibleTo), so synthetic rows can be audited directly.

**4. Acceptance grep:** the store's class summary originally named the three files, which made the grep count 5 instead of 3. The comment was reworded (5992d98).

## Requirements

QA-02 has been left Pending in REQUIREMENTS.md. This plan delivers the audit service, but running it at run end, persisting it through the store and showing it in Case Summary / QaFindingsDialog come in later plans (07-15+).

## Verification

- `dotnet test --filter FullyQualifiedName~ReelDiscovery.Tests.Qa`: 43 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings.
- Acceptance greps: the Pass/Warning constants appear once each; `StableRank(` appears 2 times; `Random` in LogisticRegression 0 times; `new Random|Random.Shared` in LeakageAudit 0 times; `BodyPlain|FlattenedText` 0 times; `AnswerKeyBuilder.Build(` once; the 3 file names appear only in the constants.

## TDD Gate Compliance

Each task has a `test(...)` RED commit followed by a `feat(...)` GREEN commit.

## Self-Check: PASSED
