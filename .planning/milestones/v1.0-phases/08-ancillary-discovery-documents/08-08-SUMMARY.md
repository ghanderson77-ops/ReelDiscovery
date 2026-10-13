---
phase: 08-ancillary-discovery-documents
plan: 08
subsystem: discovery-validation
tags: [discovery, validator, regex, D-26, ANC-03, ANC-04, ANC-05]
requires:
  - "08-01: DiscoveryViolation, DiscoveryRule, DepositionTurn, DiscoveryKind"
  - "08-02: DiscoveryTestData"
provides:
  - "TextScan (internal): NotSurnames, Honorifics, NameBigram, HonorificName, Whitespace, IdentifierPrefix, IsoDate, MonthDate, DatesIn, DateMatchesIn, TryDate"
  - "DiscoveryCheckContext"
  - "DiscoveryValidator.CheckDepositionChunk / CheckRequests / IsBetter / FeedbackLine / FeedbackText / FeedbackHeader / MinTurnShare"
affects: [08-deposition-generator, 08-written-discovery-generator]
tech-stack:
  added: []
  patterns:
    - "One copy of the name/date/identifier scanners in TextScan, shared by RenderValidator and DiscoveryValidator"
    - "DiscoveryValidator mirrors RenderValidator V2/V3 and the ThreadUnitOfWork keep-best comparator"
key-files:
  created:
    - Services/Evidence/TextScan.cs
    - Services/Discovery/DiscoveryValidator.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryValidatorTests.cs
  modified:
    - Services/Evidence/RenderValidator.cs
decisions:
  - "08-08: D1 flags a deposition chunk below 50% of its target turns (MinTurnShare, research Pattern 8); an exhibit counts as shown when a turn carries its ExhibitNumber or the text says 'Exhibit N'"
  - "08-08: D5 requires every Amount value linked to the slot's fact and every fact actor by full, first or last name (whole word); genuineness slots are skipped"
  - "08-08: D4 duplicates are reported as D4ExtraRequest with Expected = 1 (Expected = 0 = slot nobody asked for), so feedback can word the two cases differently"
  - "08-08: D8 reports only the first matching original/alias per applied substitution"
  - "08-08: Organization names and short names (whole and by token) and ExtraNames tokens count as known surnames for D2"
  - "08-08: TextScan adds DateMatchesIn (date plus matched text) so D6 can report the offending date text; DatesIn delegates to it"
metrics:
  duration: 15min
  completed: 2026-10-08
  tasks: 2
  files: 4
---

# Phase 8 Plan 08: TextScan Regex Move and DiscoveryValidator Summary

Shared name/date/identifier scanners moved into `TextScan` with no Phase 5 behavior change, and a static `DiscoveryValidator` that enforces D-26 (D1-D8) on deposition chunks and written discovery requests, with a keep-best comparator and model-facing retry feedback.

## What Was Built

- **Task 1 (refactor):** `Services/Evidence/TextScan.cs` (`internal static partial class`) now owns `NotSurnames`, `Honorifics`, the six source-generated regexes (`NameBigram`, `HonorificName`, `Whitespace`, `IdentifierPrefix`, `IsoDate`, `MonthDate`, same patterns and 1000 ms timeouts), `DatesIn` and `TryDate`. RenderValidator call sites were changed to `TextScan.X()`; `GreetingName`, `CountNumber` and `SentenceBreak` stayed in RenderValidator. 153 RenderValidator/RenderRepair/ThreadUnitOfWork/AttachmentValidator tests pass, and no tests were edited.
- **Task 2 (TDD):** `Services/Discovery/DiscoveryValidator.cs` with `DiscoveryCheckContext`:
  - `CheckDepositionChunk` runs D1 (short part, exhibits not shown), D2 (peopleNamed plus a bigram/honorific scan), D3, D6 and D8 over the joined turn text.
  - `CheckRequests` runs D4 (missing, duplicate and extra slots), D2, D3 and D8 per request, plus D5 for RFAs and D7 for interrogatories.
  - `IsBetter` copies the ThreadUnitOfWork rules. Any attempt beats none. A non-structural attempt beats a structural one. After that, fewer violations wins, and a tie goes to the later attempt.
  - `FeedbackText` writes the header "Problems in the previous attempt (fix all of them and rewrite the whole part):" and then one `- ` line per violation.
  - Results are deduped by (Rule, Number, Value). Null or empty input never throws.
  - 31 tests cover every rule, IsBetter, FeedbackText and null input.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] D7 SubpartMarker would flag "person(s) and document(s)"**
- **Found during:** Task 2
- **Issue:** The planned pattern `\(\s*(?:[a-z]|...)\s*\)` matches the "(s)" in "person(s)". That happens in almost every interrogatory, so it would have caused false D7 retries.
- **Fix:** Added a `(?<![A-Za-z0-9])` lookbehind so a marker must not be attached to a word. A test case covers this.
- **Files modified:** Services/Discovery/DiscoveryValidator.cs
- **Commit:** 2dc2f7f

**2. [Rule 2 - Missing functionality] D6 needs the matched date text**
- **Found during:** Task 2
- **Issue:** `DatesIn` returns only `DateOnly` values, but the D6 violation has to report the original text (for example "July 2, 2025").
- **Fix:** Added `TextScan.DateMatchesIn` (text plus date, in order of appearance). `DatesIn` delegates to it and returns the same results as before.
- **Files modified:** Services/Evidence/TextScan.cs
- **Commit:** 2127842

## TDD Gate Compliance

- RED: c32deb8 `test(08-08): add failing DiscoveryValidator tests...` (did not compile: types missing)
- GREEN: 2dc2f7f `feat(08-08): implement DiscoveryValidator...`
- REFACTOR: not needed

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror`: 0 warnings, 0 errors
- DiscoveryValidatorTests + RenderValidatorTests + ThreadUnitOfWorkTests: 146 passed
- Acceptance greps: no private NameBigram/MonthDate/IsoDate in RenderValidator; 6 `internal static partial Regex` in TextScan; 8 `TextScan.` uses in RenderValidator; DiscoveryValidator has `CanonicalValueScanner.Scan` x2, `TextScan.` x11, "rewrite the whole part" x1, `new Regex(` x0

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Evidence/TextScan.cs, Services/Discovery/DiscoveryValidator.cs, ReelDiscovery.Tests/Discovery/DiscoveryValidatorTests.cs
- FOUND commits: 2127842, c32deb8, 2dc2f7f
