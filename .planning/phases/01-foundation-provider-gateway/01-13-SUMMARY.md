---
phase: 01-foundation-provider-gateway
plan: 13
subsystem: generation
tags: [cancellation, timeout, error-reporting, distribution, gap-closure]
requires:
  - phase: 01-11
    provides: FatalError / runCts fatal stop, RunIsolatedAsync
  - phase: 01-12
    provides: provider-level timeout classification (CR-01 provider layer)
provides:
  - Run-token-filtered OCE handling in EmailGenerator (timeouts become recorded skips)
  - RecordUnexpectedStop (no silent aborted run)
  - state.Result set on every exit path
  - Outcome-aware StepComplete (DescribeStatus / BuildSummaryText)
  - Safe internal static DistributeEmails (largest remainder, never negative)
affects: [phase-05-corpus-renderer, phase-07-batch]
tech-stack:
  added: []
  patterns:
    - "catch (OperationCanceledException) when (runCts.IsCancellationRequested) rethrow; unfiltered OCE = timeout skip"
    - "Pure static outcome/summary helpers on UI controls for testability"
key-files:
  created:
    - ReelDiscovery.Tests/Generation/EmailGeneratorTimeoutTests.cs
    - ReelDiscovery.Tests/Generation/EmailDistributionTests.cs
    - ReelDiscovery.Tests/RunSummaryTests.cs
  modified:
    - Services/EmailGenerator.cs
    - UserControls/StepComplete.cs
    - ReelDiscovery.Tests/Generation/EmailGeneratorFixTests.cs
key-decisions:
  - "Generator OCE rethrown only when the run token is cancelled; any other OCE is a recorded timeout skip (defence in depth over 01-12)"
  - "Outer OCE catch without user cancel or FatalError records 'Run stopped unexpectedly' so a run can never report clean success"
  - "DistributeEmails: 1 each then largest-remainder by SuggestedEmailCount; fewer emails than storylines -> first N get 1; 0-email storylines skip the LLM call"
requirements-completed: [PROV-04]
duration: 3min
completed: 2026-09-30
---

# Phase 1 Plan 13: Generator timeout hardening, true run outcome, safe distribution Summary

**EmailGenerator now records non-run OperationCanceledExceptions (timeouts) as per-storyline/per-item skips instead of silently aborting, always exposes the current GenerationResult, StepComplete reports fatal/cancelled/errors status, and the email budget split is a non-negative largest-remainder distribution.**

## Performance

- **Duration:** ~3 min
- **Completed:** 2026-09-30
- **Tasks:** 2 (TDD, 4 commits)
- **Files modified:** 6

## Accomplishments

- CR-01 (generator layer): storyline lambda and `RunIsolatedAsync` rethrow OCE only `when (runCts.IsCancellationRequested)`; other OCE adds `Storyline '...' skipped (request timed out)` / `{label} skipped (request timed out)` and the run continues.
- `RecordUnexpectedStop(result, userCancelled)` replaces `WasCancelled = ct.IsCancellationRequested` in the outer catch; a stop with no user cancel and no FatalError adds an error.
- WR-05: `state.Result = result` at run start; outer `catch (Exception)` now saves completed threads like the cancel path; runs with no threads reset `state.GeneratedThreads` to empty (shared `SavePartialAsync` local function).
- StepComplete: `Status` row first, `Errors / Skipped Items` row when errors exist, `_lblSummary.Text = BuildSummaryText(result)`, Firebrick text on fatal stop.
- WR-06: `internal static List<int> DistributeEmails(IReadOnlyList<Storyline>, int)`; 0 storylines throws "No storylines to generate emails for..." (recorded via outer catch); 0-email storylines return before the semaphore.

## Task Commits

1. **Task 1 RED** - `d87d135` test(01-13): add failing timeout, unexpected-stop and run summary tests
2. **Task 1 GREEN** - `5a7cb4f` fix(01-13): record timeouts as skips and report true run outcome (CR-01, WR-05)
3. **Task 2 RED** - `c257223` test(01-13): add failing email distribution tests (WR-06)
4. **Task 2 GREEN** - `8a94434` fix(01-13): safe largest-remainder email distribution across storylines (WR-06)

## Files Created/Modified

- `Services/EmailGenerator.cs` - filtered OCE catches, RecordUnexpectedStop, state.Result on all paths, SavePartialAsync, DistributeEmails rewrite, 0-email guard
- `UserControls/StepComplete.cs` - DescribeStatus, BuildSummaryText, Status/Errors rows, summary color
- `ReelDiscovery.Tests/Generation/EmailGeneratorTimeoutTests.cs` - 8 tests (CR-01, WR-05)
- `ReelDiscovery.Tests/RunSummaryTests.cs` - 5 tests (WR-05)
- `ReelDiscovery.Tests/Generation/EmailDistributionTests.cs` - 11 test methods (16 cases incl. Theory) (WR-06)
- `ReelDiscovery.Tests/Generation/EmailGeneratorFixTests.cs` - `RunIsolatedAsync_Cancellation_Propagates` now uses a cancelled run token (old version encoded the CR-01 bug)

## Decisions Made

See key-decisions in frontmatter.

## Deviations from Plan

None - plan executed as written. Minor additions within scope:
- The duplicated partial-save block in the two outer catches was factored into a local `SavePartialAsync()` function (same behavior as specified).
- Added an extra `NegativeSuggested_ClampedToZero` distribution test covering the T-01-13-03 clamp.

## TDD Gate Compliance

RED commits (`d87d135`, `c257223`) precede GREEN commits (`5a7cb4f`, `8a94434`); RED was a compile failure on the missing members.

## Verification

- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors
- `dotnet test ReelDiscovery.slnx`: 211 passed, 0 failed
- Acceptance greps: filtered OCE catch x2, "skipped (request timed out)" x2, RecordUnexpectedStop call x1, old WasCancelled assignment x0, `state.Result = result` x2, old `private List<int> DistributeEmails` / `distribution[0] += diff` x0

## Manual Verification (UI)

- StepComplete after a fatal stop / cancel / run with skips: check the Status row, the error-count row, and the summary text/color. Not automated (WinForms); helpers are unit tested.

## Known Stubs

None.

## Next Phase Readiness

Final plan of phase 01; phase ready for re-verification. Deferred review items (WR-01..04, WR-07..09, IN-*) remain tracked in 01-REVIEW.md.

## Self-Check: PASSED
