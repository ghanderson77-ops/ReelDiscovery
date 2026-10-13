---
phase: 06-attachments-non-relevant-corpus
plan: 06
subsystem: filler-shaping
tags: [non-relevant, length-bands, privilege, decoys, must-not-mention, amounts, determinism]
requires:
  - "06-01 AttachmentTestData.MixedCase"
  - "Phase 5 RenderAssignments (LengthBands, LabeledShare, SubjectTagShare, StableRank), RenderValidator.ContainsWord"
provides:
  - "FillerAssignments.Compute(plan) -> Dictionary<threadId, ThreadAssignment> for non-relevant threads"
  - "DecoyScanner.Code = \"D1\", DecoyScanner.Scan(rendered, thread), DecoyScanner.ScanText(text, seq, thread)"
  - "CanonicalValueScanner reads abbreviated amounts ($1.25M, $500K, $2.5MM, $3B)"
affects: [06 filler rendering / validation retry loop, answer-key and Phase 5 render checks that compare amounts]
tech-stack:
  added: []
  patterns: ["filler apportioned separately from relevant mail with its own salts, reusing the relevant share constants"]
key-files:
  created:
    - Services/Evidence/FillerAssignments.cs
    - Services/Evidence/DecoyScanner.cs
    - ReelDiscovery.Tests/Evidence/FillerAssignmentsTests.cs
    - ReelDiscovery.Tests/Evidence/DecoyScannerTests.cs
  modified:
    - Services/Case/CanonicalValueScanner.cs
    - ReelDiscovery.Tests/Case/CanonicalValueScannerTests.cs
decisions:
  - "FillerAssignments keeps the first copy of a repeated thread id, the same rule WavePlanner and the runner use"
  - "Bulk mail (NewsletterSpamPersonal) is banded by sender ExternalParty.Kind: Newsletter Long, anything else (Spam, Personal, unknown sender) Short; it is left out of the 55/35/10 population"
  - "DecoyScanner dedupes terms by trimmed, case-insensitive text so each (seq, term) yields at most one D1"
  - "A term counts as an amount when CanonicalValueScanner.TryParseAmount accepts it; then any scanned amount with AmountsEqual is a hit, otherwise RenderValidator.ContainsWord"
  - "DecoyScanner.Scan checks message bodies only; callers pass attachment text through ScanText"
  - "Amount suffix regex is (?:MM|[KMB]) with an optional single space and (?![A-Za-z]), placed after the word forms, so $12 Main Street and $5 Mbps stay unscaled"
metrics:
  duration: "~12 min"
  completed: 2026-10-04
  tasks: 2
  files: 6
---

# Phase 6 Plan 06: Filler Assignments and Decoy Scan Summary

Non-relevant mail now gets length bands and privilege markings from the same code-side rules as relevant mail, with its own salts. Decoys get a deterministic D1 must-not-mention check (no LLM judge). The amount scanner now reads `$1.25M`-style suffixes.

## What Was Built

- **FillerAssignments.Compute(plan)** (`Services/Evidence/FillerAssignments.cs`): covers non-relevant threads that have an id and messages, sorted by id. DailyBusiness, Ancillary and Decoy messages are split with `RenderAssignments.LengthBands` shares. Long goes only to New/Forward messages, and any shortfall becomes Medium. Short goes to replies first. The salt is `:filler-length`. For privileged non-relevant threads, `RenderAssignments.LabeledShare` and `SubjectTagShare` set Banner, BannerAndSubject or Unlabeled, with salts `:filler-privilege` and `:filler-subject`. Subtlety is always None. The method never calls `RenderAssignments.Compute` and never changes the plan.
- **DecoyScanner** (`Services/Evidence/DecoyScanner.cs`): runs only on decoy threads. A hit produces `mentions "{term}", which this email must not mention` under code D1. Amount terms match the same value written in any format, and other terms match whole words. Each term is checked inside its own try/catch, so a bad term never hides the others and never throws into the run.
- **CanonicalValueScanner**: both amount regex branches accept an optional K/M/MM/B suffix, and `TryParseAmount` applies the matching factor. Amounts without a suffix give the same results as before. All existing CanonicalValueScanner and RenderValidator tests pass.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | 93a01a1 | test(06-06): add failing tests for filler length and privilege assignments |
| 1 | GREEN | 1d188b8 | feat(06-06): filler length bands and privilege markings for non-relevant mail |
| 2 | RED | 23786ab | test(06-06): add failing tests for amount suffixes and decoy must-not-mention scan |
| 2 | GREEN | e700336 | feat(06-06): abbreviated amount suffixes and decoy must-not-mention scan |

## Verification

- Filtered: FillerAssignments, DecoyScanner, CanonicalValueScanner, RenderValidator and RenderAssignments tests: 145/145 passed.
- Full suite: 2909/2909 passed (baseline 2873 plus 36 new tests).
- App build has 0 warnings and 0 errors.
- Every grep check in the acceptance criteria returns the expected count, including 0 copied share literals in FillerAssignments.

## Deviations from Plan

None. The plan was executed as written.

## TDD Gate Compliance

Each task has a RED `test(...)` commit (compile failure confirmed) followed by a GREEN `feat(...)` commit. Neither task needed a refactor.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Evidence/FillerAssignments.cs, Services/Evidence/DecoyScanner.cs, ReelDiscovery.Tests/Evidence/FillerAssignmentsTests.cs, ReelDiscovery.Tests/Evidence/DecoyScannerTests.cs
- FOUND commits: 93a01a1, 1d188b8, 23786ab, e700336
