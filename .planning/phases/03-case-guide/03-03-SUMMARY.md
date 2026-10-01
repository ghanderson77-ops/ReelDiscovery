---
phase: 03-case-guide
plan: 03
subsystem: case-guide-building-blocks
tags: [ids, issues, email-identity, signatures, time-zones, tests]
requires:
  - "03-01 (CaseGuide, CaseIssue, CaseOrganization, CastMember, IdCounters)"
provides:
  - "Services/Case/IdAllocator.cs (Issue/Organization/Character/Fact/Value prefixes, WidthFor, Next, Normalize)"
  - "Services/Case/IssueExtractor.cs (Extract, SourceLabel, internal NormalizeTitle)"
  - "Services/Case/EmailIdentity.cs (BaseName, DomainFor, AddressFor, Fold, IsReservedDomain, IsWellFormed, DomainOf)"
  - "Services/Case/SignatureBuilder.cs (Build, FictionalPhone)"
  - "Services/Case/UsTimeZones.cs (ForLocation, Default, internal StateCount)"
affects: [03-07, 03-08, 03-09, 03-10, 03-11]
tech-stack:
  added: []
  patterns:
    - "Code owns every id: max(existing ids with the prefix, persisted counter) + 1; counters only grow"
    - "Issue extraction matches in passes across all counts (CountId+title, then title, then CountId) so a retitled count cannot steal an issue whose title moved"
    - "Identity text folded to [a-z0-9-] before it becomes a domain or local part"
key-files:
  created:
    - Services/Case/IdAllocator.cs
    - Services/Case/IssueExtractor.cs
    - Services/Case/EmailIdentity.cs
    - Services/Case/SignatureBuilder.cs
    - Services/Case/UsTimeZones.cs
    - ReelDiscovery.Tests/Case/IdAllocatorTests.cs
    - ReelDiscovery.Tests/Case/IssueExtractorTests.cs
    - ReelDiscovery.Tests/Case/CastIdentityTests.cs
  modified: []
decisions:
  - "[03-03]: Individual parties get {last}family.com, agencies always .gov, and the domain stem comes from ShortName when set, else Name (Dunder Mifflin -> michael.scott@dundermifflin.com)"
  - "[03-03]: IssueExtractor runs each match rule as a pass over all counts (not count-by-count), so title matches always win over CountId retitle matches"
  - "[03-03]: Legal suffixes are compared with periods removed (L.L.C. = LLC, N.A. = NA) and stripped repeatedly from the end, keeping at least one token; S.A. and similar foreign forms are not stripped"
  - "[03-03]: Split-zone states use the zone of the most populous part (IN, KY, MI, FL Eastern; TN, TX, KS, NE, ND, SD Central; ID Mountain; OR, NV Pacific)"
metrics:
  duration: 12min
  completed: 2026-10-01
---

# Phase 3 Plan 03: Deterministic ID, Issue and Identity Building Blocks Summary

This plan adds five pure, deterministic services. The issue/cast generator (03-09), editing (03-07) and the UI rely on them, so code owns every ID, issue identity, email address and signature, and the model only fills in content:

- **IdAllocator** issues IDs from persisted counters and never reuses them.
- **IssueExtractor** builds the issue list from the core document's counts/findings. Issues keep their IDs and details across re-extraction and reordering.
- **EmailIdentity** derives realistic organization domains and `first.last@domain` addresses, and never produces a reserved domain.
- **SignatureBuilder** builds signatures in four styles and generates fictional 555-01xx phone numbers.
- **UsTimeZones** maps all 50 states plus DC to an IANA zone for Phase 4 scheduling.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | IdAllocator and IssueExtractor | 7a581a6 (RED), 25bb3e1 (GREEN) | IdAllocator.cs, IssueExtractor.cs, IdAllocatorTests.cs, IssueExtractorTests.cs |
| 2 | EmailIdentity, SignatureBuilder, UsTimeZones | 093e4d6 (RED), e7fffa4 (GREEN) | EmailIdentity.cs, SignatureBuilder.cs, UsTimeZones.cs, CastIdentityTests.cs |

## Resolved Discretion

- Individual parties get a personal-style domain, `{last}family.com` (Walter White -> `whitefamily.com`).
- Government agencies always get `.gov`.
- The domain stem comes from `ShortName` when it is set, otherwise from `Name`. So "Dunder Mifflin Paper Company, Inc." with ShortName "Dunder Mifflin" becomes `dundermifflin.com` and `michael.scott@dundermifflin.com`.
- Law firm domains use the first two surnames (`thistlebracken.com`, `okaforlindqvist.com`).
- Collisions are resolved in this order: `{base}law.com` for law firms or `{base}group.com` for everyone else, then `{base}2.com`, `{base}3.com`, and so on.

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- `dotnet test --filter "Category!=Render"`: 616 passed, 0 failed. This includes 34 IdAllocator/IssueExtractor tests and 62 CastIdentity tests.
- Acceptance greps:
  - The exact `Next`, `Extract` and `DomainFor` signatures are present.
  - `IdAllocator.Next` is used in IssueExtractor.
  - EmailIdentity has a single `GeneratedRegex`, and it uses `CitationScanner.MatchTimeoutMs`.
  - "Sent from my mobile" appears exactly once.
  - `America/Phoenix` and the `michael.scott@dundermifflin.com` test literal are present.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] IdAllocator.Next was not null-safe on an un-normalized guide**
- **Found during:** Task 1 (`Extract_NullSafe` test)
- **Issue:** `CaseGuide.AllIds()` throws when a list is null, for example a guide loaded from case.json before `Normalize()` runs.
- **Fix:** `Next` now scans a private null-tolerant `SafeIds(guide)` with the same five lists as `AllIds()`. The model was left unchanged.
- **Files modified:** Services/Case/IdAllocator.cs
- **Commit:** 25bb3e1

### Interpretation notes (no behavior beyond the contracts)

- **Matching order:** IssueExtractor applies match rules (1), (2) and (3) as separate passes over every count, rather than trying all three rules on one count before moving to the next. With a count-by-count order, a count retitled to an earlier title could claim an issue by CountId before the count that now carries its title was reached. The `Extract_RetitledCountDoesNotStealClaimedIssue` test covers this.
- **Count labels out of range:** `SourceLabel` falls back to "Count {n}" when the number is outside 1-3999, where `ToRoman` would throw.
- **Empty names:** `AddressFor` uses the local part "contact" when both names fold to empty. `DomainFor` uses "org" when the stem is empty, as the plan specifies.
- **Location parsing:** `UsTimeZones.ForLocation` scans comma parts from last to first and ignores ZIP codes and periods, so "Chicago, Illinois, USA", "Seattle, WA 98101" and "Washington, D.C." resolve. The internal `StateCount` (51) lets the tests confirm coverage.

## TDD Gate Compliance

Both tasks have a `test(03-03)` RED commit (a compile failure that proved the types were missing) followed by a `feat(03-03)` GREEN commit. No refactor commits were needed.

## Requirements

GUIDE-01/02/03 are not marked complete. These are building blocks only: issue extraction is wired into the generator and UI in 03-09 and later, and the cast/counsel generation also comes in 03-09.

## Known Stubs

None.

## Self-Check: PASSED
