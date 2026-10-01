---
phase: 02-case-scenario-core-document
plan: 02
subsystem: case-safety-utilities
tags: [citations, name-substitution, regex, tests]
requires: ["02-01"]
provides:
  - "Services/Case/CitationScanner.cs (CitationScanner.Scan/HasErrors, CitationFinding, CitationSeverity)"
  - "Services/Case/NameSubstitution.cs (NameSubstitution.Apply/FindLeaks)"
affects: [02-04, 02-06, 02-07, 02-08]
tech-stack:
  added: []
  patterns: ["[GeneratedRegex] fixed scanners", "Runtime escaped alternation regex with 1s timeout and single-pass dictionary replace"]
key-files:
  created:
    - Services/Case/CitationScanner.cs
    - Services/Case/NameSubstitution.cs
    - ReelDiscovery.Tests/Case/CitationScannerTests.cs
    - ReelDiscovery.Tests/Case/NameSubstitutionTests.cs
  modified: []
decisions:
  - "[02-02]: Person/FictionalWorkCharacter single-token aliases that prefix the original's first name (nicknames like Walt for Walter) map to the substitute's first name; other non-surname aliases map to the full substitute"
  - "[02-02]: FindLeaks reports canonical stored terms (distinct, first-appearance order); single-token surname aliases are reported as the token itself"
metrics:
  duration: 8min
  completed: 2026-10-01
---

# Phase 2 Plan 02: Citation Scanner and Name Substitution Summary

This plan adds two deterministic utilities in `Services/Case`. `CitationScanner` uses `[GeneratedRegex]` patterns to flag reporter citations and Westlaw/Lexis cites as errors and bare case names and citation signals as warnings. It never flags statutes, rules or the document's own caption. `NameSubstitution` swaps accepted real names for fictional ones in a single regex pass and provides a leak scan that runs after generation.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | CitationScanner with positive/negative corpus | 9a96dac (RED), 689be5c (GREEN) | Services/Case/CitationScanner.cs, ReelDiscovery.Tests/Case/CitationScannerTests.cs |
| 2 | NameSubstitution single-pass replace + leak scan | 143ccff (RED), 46d1cd7 (GREEN) | Services/Case/NameSubstitution.cs, ReelDiscovery.Tests/Case/NameSubstitutionTests.cs |

## Verification

- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors
- `dotnet test ReelDiscovery.slnx`: 290 passed, 0 failed (26 CitationScanner and 16 NameSubstitution tests are new)
- The acceptance greps pass: 5 `[GeneratedRegex]` and no `new Regex(` in CitationScanner; the exact `Scan` signature; 5 `U.S.C.` negatives in the tests; `Regex.Escape`, a single `TimeSpan.FromSeconds(1)`, and the `FindLeaks`/`Apply` signatures in NameSubstitution.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The alias-target rule conflicted with the "Walt agreed." behavior**
- **Found during:** Task 2
- **Issue:** The plan's rule only mapped an alias to the substitute's first name when the alias exactly equals the first token of the original. "Walt" does not equal "Walter", so the rule would produce "Hank Brennan agreed.", but the behavior list requires "Hank agreed."
- **Fix:** For Person and FictionalWorkCharacter entries, a single-token alias that is a prefix of the original's first token (for example "Walt" for "Walter") also maps to the substitute's first name. Surname matching and the full-substitute fallback for other aliases (for example "Heisenberg") are unchanged.
- **Files modified:** Services/Case/NameSubstitution.cs
- **Commit:** 46d1cd7

### Additions (non-breaking)
- `Findings_OrderedByIndex` and tests for empty or whitespace text in both utilities.

## TDD Gate Compliance

Both tasks have a `test(02-02)` RED commit (a compile failure) followed by a `feat(02-02)` GREEN commit.

## Known Stubs

None.

## Self-Check: PASSED
