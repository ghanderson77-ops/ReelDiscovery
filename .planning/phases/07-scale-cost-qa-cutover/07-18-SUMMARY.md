---
phase: 07-scale-cost-qa-cutover
plan: 18
subsystem: case-summary-qa
tags: [case-summary, qa, blind-review, leakage-audit, consistency-scans, cost, dpi, QA-01, QA-02, QA-03, PROV-07, D-11, D-12, D-13, D-14, D-15, D-16, D-04]
requires:
  - phase: 07-12
    provides: "GenerationUiText.CostRow (R1 copy and tooltip)"
  - phase: 07-13
    provides: "ThreadInvalidation.InvalidateAsync (Regenerate Thread...)"
  - phase: 07-15
    provides: "QaReportStore scans/leakage, LeakageAudit.Run, ScanReport.JournalUtc, end-of-run QaRunner"
  - phase: 07-16
    provides: "BlindReviewer (BuildDocuments, Estimate, RunAsync, Disagreements), BlindReviewPrompts sample order, WizardState.CreateReviewerGateway"
  - phase: 07-17
    provides: "QaUiText rows/confirmations/status lines, QaFindingsDialog, BlindReviewDialog"
  - phase: 07-19
    provides: "single case-driven step list and the WizardForm close flow"
provides:
  - "Case Summary 'Generation cost' row (Corpus group) from the cross-session journal replay"
  - "Case Summary 'Quality checks' group: Consistency scans, Leakage audit (Run Audit), Blind review (run, progress, Cancel Review, resume)"
  - "View Findings / View Report open QaFindingsDialog with Regenerate Thread... and Open Email wired"
  - "C-Q3 leave/close guard during a blind review (WizardForm.ShowStep and OnFormClosing)"
  - "WizardState.ChooseReviewer (reviewer provider/model without creating a client)"
affects: [07-20, 07-21, 07-22]
tech-stack:
  added: []
  patterns: ["QA snapshot loaded off the UI thread with a version counter (newest refresh wins)", "Progress marshalled with InvokeRequired/BeginInvoke, falling back to the captured SynchronizationContext before a handle exists"]
key-files:
  created:
    - ReelDiscovery.Tests/Qa/CaseSummaryQaTests.cs
  modified:
    - UserControls/StepCaseSummary.cs
    - Forms/WizardForm.cs
    - Models/WizardState.cs
    - UserControls/QaUiText.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
    - .planning/phases/07-scale-cost-qa-cutover/07-UI-SPEC.md
decisions:
  - "[07-18]: Case Summary QA data (cost, reports, sample order, not-run estimate) is one snapshot loaded off the UI thread on entry and after every QA action; LoadForRender loads it synchronously"
  - "[07-18]: The not-run estimate uses WizardState.ChooseReviewer, which picks the same reviewer as CreateReviewerGateway without creating a provider client; with no tested key it prices the writer provider's Volume default"
  - "[07-18]: A scan or leakage report from another plan is ignored (leakage) or out of date (scans); a scan whose JournalUtc is older than the journal's last write is out of date"
  - "[07-18]: The leave guard lives in WizardForm.ShowStep (covers Back, Review Plan, Plan Corpus, Generate Emails); closing uses IsRunActive + the C-Q3 title switch and CancelAndWaitForReview"
  - "[07-18]: After Regenerate Thread... the step re-projects case.json's run counts and ledger from the journal (GenerationStateApplier, final: false) and saves, so Mailboxes turns partial at once"
  - "[07-18]: Every QA line 3 tooltip starts with the full line (rule 28), then a blank line and the row's extra tooltip; blind review shows 'Not run yet.' before any email is generated; Run Audit failures use QaUiText.LeakageFailedText (UI-SPEC R3/R4 amended)"
  - "[07-18]: Move Case Folder... is disabled while a review runs (the review appends to qa-blind-review.jsonl in the case folder)"
metrics:
  duration: 35min
  completed: 2026-10-06
  tasks: 2
  files: 7
---

# Phase 7 Plan 18: Case Summary QA section and cost row Summary

Case Summary now reports what the case has cost across every session and runs the three quality checks in place. A new `Generation cost` row shows the journal's itemized spend. A new `Quality checks` group holds Consistency scans, Leakage audit and Blind review: you can view findings, run the free audit, and run, follow, cancel and resume a paid blind review. Regenerate Thread... and Open Email work from the findings dialog.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | Rows, report loading, audit/regenerate/review wiring | addf8ed (RED), b5b6e62 (GREEN) |
| 2 | 144-DPI renders of the Case Summary QA states | 081e2a9 |

## What was built

**Rows** (`UserControls/StepCaseSummary.cs`)
- `Generation cost` is the last row of the Corpus group and has no buttons.
  - Line 3 comes from `GenerationUiText.CostRow`, using `JournalReplay.SpentCostUsd`, `SpentDetail`, `MediaSpentCostUsd` and `UnsplitSpentUsd`.
  - Its tooltip is the breakdown, one item per line.
- The `Quality checks` group sits between Corpus and Case guide:
  - `Consistency scans`: View Findings.
  - `Leakage audit`: View Report and Run Audit.
  - `Blind review`: View Findings, Run Blind Review... and Cancel Review. Cancel Review is restyled Danger and shown only while a review runs.
- Every new button is busy-gated in `UpdateButtons`. Cancel Review is the exception: it stays enabled so a running review can always be stopped.

**Data**
- `LoadQa` replays the journal once and reads `qa-scans.json`, `qa-leakage.json` and `qa-blind-review.jsonl`. It also builds:
  - the answer-key email rows (with the run overlay)
  - the sample order and default sample size
  - the review system prompt (plan protocol prose, or the fallback prose)
  - the not-run estimate
- It runs off the UI thread on entry and after every QA action, guarded by a version counter.
- Unreadable files read as Not run.

**Actions**
- **Run Audit**
  - Disables the step's actions while it runs and shows `Running the leakage audit...` (Accent).
  - Runs `LeakageAudit.Run` off the UI thread with the overlay rebuilt from the journal, then saves with `SaveLeakage` and refreshes.
  - A failure shows `Could not run the leakage audit: {message}.`
- **Run Blind Review...**
  - Gets the gateway from `ReviewerGatewayFactory` or `CreateReviewerGateway(writer)`.
  - If no gateway: shows the Warning `Test an API key on the API Configuration step first.` and opens no dialog.
  - Otherwise builds the reviewer documents off the UI thread, then opens `BlindReviewDialog`:
    - The estimate counts only emails not yet reviewed in the same pass.
    - After a stopped review, the dialog defaults to the stopped sample size.
  - Start Review runs `BlindReviewer.RunAsync` with a step-owned token:
    - Line 3 shows `Reviewing: d of n emails...` (Accent line and dot); the status strip shows the progress line.
    - Finishing shows Success or Warning; a fatal message or refused calls show the Danger `Blind review stopped: ...`.
    - A cancel keeps the verdicts already written and line 3 shows the Stopped state.
- **Cancel Review** asks C-Q2 (Question icon, No is the default) through the `Confirm` seam.
- **View Findings / View Report** open `QaFindingsDialog`:
  - Scan rows come from the findings; blind-review rows come from the disagreements, with the reviewer's reason.
  - Subjects come from the answer key.
  - The C-Q1 facts (thread subject, EML copies, attachments) come from the plan.
  - `regenerate` calls `ThreadInvalidation.InvalidateAsync`.
  - When anything was queued, the step re-projects `Corpus.Generation` from the journal, saves and refreshes. Mailboxes then shows the partial corpus.
- **Open Email** (T-07-60) opens the sender copy (first `CopyPathsFor` path), only when `ResolveUnderCase` accepts it and the file exists. Otherwise it opens nothing.

**Leave and close guards**
- Leaving the step: `WizardForm.ShowStep` asks `StepCaseSummary.ConfirmLeave()` (C-Q3). No keeps you on the step; Yes stops the review.
- Closing the app: `IsRunActive` includes `StepCaseSummary { IsReviewRunning: true }`, so the close prompt shows C-Q3. `StopActiveRun` then calls `CancelAndWaitForReview`.
- Disposing the step also cancels the review, so no further calls are billed.

**Test seams:** `Confirm`, `ReviewerGatewayFactory`, `ShowDialogOverride`, `ShowFindingsOverride`, `OpenFile`.

## Render inspection (render checks 5-6, DPI rule 28)

These PNGs were inspected by eye. The display was at 125% (120 DPI); the harness accepts 120 or more, and every size goes through `LogicalToDeviceUnits`.
- `renders/case-summary-qa-not-run-1100x750.png`, `renders/case-summary-qa-not-run-1000x540.png`: all three QA rows are Not run with BorderStrong dots and no QA buttons.
- `renders/case-summary-qa-done-1100x750.png`, `renders/case-summary-qa-done-1000x540.png`:
  - Generation cost shows every clause: `$3.61 spent · input · cache write · cache read · output · batch discount −$0.20 · images and voicemail $1.24 · not itemized $0.37`.
  - Scans show the Warning with all four clauses.
  - Leakage shows the Warning; its line ellipsizes at both sizes and the tooltip holds the full text.
  - Blind review shows 2 disagreements (80% agree) with the same-provider clause.
- `renders/case-summary-qa-review-running-1100x750.png`, `renders/case-summary-qa-review-running-1000x540.png`:
  - Cancel Review is Danger, and line 3, the dot and the status strip are Accent.
  - Move Case Folder... is disabled.

Checked in every render:
- The clipping detector reported nothing.
- No button text is ellipsized.
- Line 3 stays on one line and never runs under the button column.
- Case Summary has no Primary button besides Export All.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Reviewer choice without creating a client**
- **Issue:** the not-run estimate needs the reviewer model on every refresh. `CreateReviewerGateway` builds a provider, and `AnthropicTextProvider` creates a new `HttpClient` each time.
- **Fix:** added `WizardState.ChooseReviewer(writer)`; `CreateReviewerGateway` now uses it, with the same results.
- **Files:** `Models/WizardState.cs`
- **Commit:** b5b6e62

**2. [Rule 2 - Correctness] Copy gaps found while wiring**
- Added `QaUiText.LeakageFailedText` for a failed Run Audit.
- Added `QaUiText.BlindReviewNotRunNoEmails`. Without it, the not-run line read "≈$0.00 to review 0 emails" before any email was generated.
- QA line 3 tooltips start with the full line (rule 28).
- UI-SPEC R3/R4 amended.
- **Commit:** b5b6e62

**3. [Rule 2 - Correctness] Move Case Folder... during a review**
- **Issue:** a running review appends to `qa-blind-review.jsonl` inside the case folder, so moving the folder mid-review is unsafe.
- **Fix:** the button is disabled while a review runs.
- **Commit:** b5b6e62

**4. [Rule 3 - Blocking] Regenerate must show partial Mailboxes**
- **Issue:** the Mailboxes row reads cached counts from case.json, which the invalidation does not touch.
- **Fix:** after a queued regenerate, the step re-projects the counts and ledger with `GenerationStateApplier.Apply(final: false)` and saves. Generate Corpus does the same projection on entry.
- **Commit:** b5b6e62

**5. Placement note:** the plan said to put the C-Q3 branch in `DecideClose`. `DecideClose` is a pure function that only takes a `runActive` flag; the step-specific prompt is chosen in `OnFormClosing`'s title switch. So the branch went into `IsRunActive` plus that switch, with the same behavior.

**6. Test harness:** the running-review render installs a WinForms `SynchronizationContext` with `AutoInstall = false`, as `GenerateCorpusStepTests.RunSta` does. Without it, the harness's `Application.DoEvents` pumping strands the review's continuations. This affects tests only; the app runs under `Application.Run`.

## Requirements

- QA-01, QA-02 and QA-03 are complete: the blind review, the leakage audit and the scans can now be run and viewed, and flagged threads can be regenerated, from Case Summary.
- PROV-07 is complete: Case Summary now shows the D-16 cache, batch and media breakdown. Pricing editing and tracking came in earlier plans.

## TDD Gate Compliance

Task 1: test addf8ed (RED; it failed to compile because the members did not exist), then feat b5b6e62 (GREEN).

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- The plan's filter (`CaseSummary` non-render plus `Tests.Qa`): 195 passed.
- Full non-render, non-scale suite: 3,757 passed, 0 failed.
- `Category=Render&FullyQualifiedName~CaseSummaryRenderTests`: 14 passed, 6 of them new (3 states x 2 sizes).
- Acceptance greps:
  - `"Quality checks"` appears 1 time.
  - The four row titles appear 4 times.
  - `new QaFindingsDialog(` appears 1 time.
  - `ThreadInvalidation.InvalidateAsync(` appears 1 time.
  - `IsReviewRunning` appears in WizardForm 1 time.
  - `GenerationUiText.CostRow(` appears 2 times.
  - `RunAsync(` appears 1 time.

## Known Stubs

None.

## Threat Flags

None. Every new surface is in the plan's threat model:
- T-07-61: the estimate is shown before Start Review, Cancel Review is available, and a resume skips emails already reviewed.
- T-07-62: progress is marshalled to the UI thread and actions are busy-gated.
- T-07-63: only provider and model names are shown.
- T-07-64: C-Q1 is asked in the dialog, and ThreadInvalidation validates paths.

## Self-Check: PASSED
- FOUND: ReelDiscovery.Tests/Qa/CaseSummaryQaTests.cs, UserControls/StepCaseSummary.cs, Forms/WizardForm.cs, ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
- FOUND commits: addf8ed, b5b6e62, 081e2a9
