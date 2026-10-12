---
phase: 09-edit-safety-navigation
plan: 09
subsystem: corpus-safety
tags: [confirmations, discovery, start-over, regenerate-thread, qa]
requires:
  - phase: 09-02
    provides: "DiscoveryStaleness.CitedDocIds / AnyCitesEmails"
  - phase: 09-05
    provides: "Start Over / Regenerate Thread flows"
  - phase: 09-08
    provides: "Exhibit staleness surfacing that the new clauses warn about"
provides:
  - "C-G2 Start Over and C-Q1 Regenerate Thread confirmations state that citing discovery documents will show Out of date (09 UI-SPEC S5)"
affects: [generate-corpus, case-summary, qa-findings]
tech-stack:
  added: []
  patterns:
    - "Conditional confirmation clauses via trailing bool parameters defaulting to false (existing callers and tests unchanged)"
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/QaUiText.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - UserControls/StepCaseSummary.cs
    - Forms/QaFindingsDialog.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Qa/QaUiTextTests.cs
    - ReelDiscovery.Tests/Qa/QaFindingsDialogTests.cs
    - ReelDiscovery.Tests/Qa/CaseSummaryQaTests.cs
decisions:
  - "[09-09]: The Start Over discovery check runs inside the existing Task.Run with the file and batch counts (StartOverCitesDiscovery seam, never throws)"
  - "[09-09]: Case Summary collects every saved document's cited DocIds into QaSnapshot.DiscoveryCitedDocIds; DescribeThread sets QaFindingRow.CitedByDiscovery when the thread's messages intersect it"
metrics:
  duration: 12min
  completed: 2026-10-11
---

# Phase 9 Plan 09: Discovery Clauses in Destructive Confirmations Summary

The Start Over and Regenerate Thread confirmations now add a sentence when saved discovery documents cite the emails being deleted: "Discovery documents that cite these emails will show Out of date." for Start Over and "Discovery documents that cite its emails will show Out of date." for Regenerate Thread. When no saved document cites those emails, both confirmations read exactly as before, with the same titles, buttons and default No.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | Conditional Out of date clauses in CG2StartOver and CQ1RegenerateThread | 2a45da3 |
| 2 | Compute the conditions at the Start Over and Regenerate Thread call sites | 35c1eb3 |

## What changed

- `GenerationUiText.CG2StartOver(files, batchesPending = false, citedByDiscovery = false)`: the discovery clause comes after the batches clause.
- `QaUiText.CQ1RegenerateThread(..., relevant, citedByDiscovery = false)`: the discovery clause is added at the end.
- `StepGenerateCorpus.StartOverCitesDiscovery(folder, caseId)` is `DiscoveryStaleness.AnyCitesEmails(DiscoveryStore.Load(...).Set)`. `StartOverAsync` calls it off the UI thread, together with the file and batch counts.
- `QaFindingRow.CitedByDiscovery` is new. `QaFindingsDialog.RegenerateSelectedAsync` passes it to C-Q1.
- `QaSnapshot.DiscoveryCitedDocIds` is filled in `LoadQaWithDiscovery`. `DescribeThread` sets `CitedByDiscovery` for both scan rows and blind-review rows.

## Deviations from Plan

**1. [Rule 3 - Blocking] `StepCaseSummary.ScanRows` made internal**
- **Found during:** Task 2
- **Issue:** The plan's test goes through the scan-row builder, which was private.
- **Fix:** Changed `private static` to `internal static`. Nothing else about it changed.
- **Commit:** 35c1eb3

## TDD Gate Compliance

Each task's tests were written before the implementation. The RED state was a compile failure because the new parameters did not exist yet. Every commit has to build with -warnaserror, so each task's test and implementation went into a single `feat` commit; there are no separate `test(...)` RED commits.

## Verification

- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror`: 0 warnings, 0 errors. `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors.
- Filter `GenerationUiTextTests|QaUiTextTests`: 330 passed.
- Filter `QaFindingsDialogTests|CaseSummaryQaTests|GenerateCorpusStepTests|QaUiTextTests`: 129 passed. This includes the unchanged existing check at QaFindingsDialogTests l.138 and the existing Start Over confirm test.

## Self-Check: PASSED
