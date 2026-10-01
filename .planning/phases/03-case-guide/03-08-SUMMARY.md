---
phase: 03-case-guide
plan: 08
subsystem: case-guide-validation
tags: [validator, canonical-values, regex, real-name-leaks, staleness, tdd]
requires:
  - "03-01: CaseGuide model, ComplexityPreset BackgroundPerOrg/Facts ranges, SampleGuidedDraft fixture"
  - "03-03: EmailIdentity.IsWellFormed/IsReservedDomain/DomainOf"
  - "03-07: GuideStaleness.IsStale/StaleReason, CaseGuideEditing review reasons"
provides:
  - "Services/Case/CanonicalValueScanner.cs (Scan, AmountsEqual, IsRegistered; ScannedKind, ScannedValue)"
  - "Services/Case/CaseGuideValidator.cs (Validate, CountErrors, CountWarnings, FindingsFor, WorstFor; GuideFinding, GuideArea)"
affects: [03-10, 03-11, 03-13, 03-14, 03-15, 03-16]
tech-stack:
  added: []
  patterns:
    - "Validator builds a null-cleaned Context (lists without null entries, first-wins id lookups) once; every rule group runs in its own try/catch so one failure becomes a 'A check could not run' Warning"
    - "Scanner regexes are [GeneratedRegex] with CitationScanner.MatchTimeoutMs; timeouts return partial results"
key-files:
  created:
    - Services/Case/CanonicalValueScanner.cs
    - Services/Case/CaseGuideValidator.cs
    - ReelDiscovery.Tests/Case/CanonicalValueScannerTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideValidatorTests.cs
  modified: []
decisions:
  - "[03-08]: Leak messages use the entity id as {where} (\"F-003 uses the real name \\\"Acme Windows\\\".\"); organization findings use Area Guide with the ORG id as EntityId, so Where reads \"Guide · ORG-02\""
  - "[03-08]: Conflict findings (duplicate label, same value under two labels, shared org domain) are reported once on the later entry and name the earlier entry first"
  - "[03-08]: Label conflicts compare values as exact trimmed text (canonical means identical), so \"$184,250\" vs \"$184,250.00\" under one label is an Error"
  - "[03-08]: Text-checked value kinds (must appear in core doc or facts): Amount, InvoiceNumber, ChangeOrderNumber, ContractNumber, PurchaseOrder, AccountNumber; Amount also matches via AmountsEqual"
  - "[03-08]: Missing/invalid active range is an Error for every cast member (plan rule 10), even though CaseGuideEditing allows typing an empty date; inactive-actor checks skip members whose range is invalid"
  - "[03-08]: Knowers who are also actors are checked only by the actor rule (no duplicate Warning)"
  - "[03-08]: Party-renamed warning trims a trailing period from the party name to avoid \"Inc..\""
  - "[03-08]: ScannedValue uses auto-properties (project convention) rather than the public fields shown in the plan contract"
metrics:
  duration: 14min
  completed: 2026-10-01
---

# Phase 3 Plan 08: Canonical Value Scanner and Case Guide Validator Summary

This plan adds the deterministic case guide validator (GUIDE-07). It implements all 24 rules in the plan's table with the exact message text, and it never throws. It also adds `CanonicalValueScanner`, which finds dollar amounts and document identifiers in the core document and key facts and reports any that are missing from the canonical values registry (GUIDE-06 cross-check).

## What was built

**CanonicalValueScanner**: three `[GeneratedRegex]` patterns, each with `MatchTimeoutMs`:
- amounts, including "$7.5 million"
- prefixed identifiers (CO/INV/PO/SOW/CN/AC/ACCT-n)
- "No. nn" / "#nn" identifiers with at least two digits

`Scan` returns matches in order of appearance with duplicates removed (case-insensitive). If a regex times out, it returns whatever was found so far. `AmountsEqual` parses amounts to decimal and handles the words thousand, million and billion. `IsRegistered` checks scanned amounts against Amount-kind values; scanned identifiers match a value when either text contains the other, ignoring case.

**CaseGuideValidator**: `Validate(CaseDraft?)` returns an empty list when the draft, the core document or the guide is missing. It reads untrusted lists and strings with null checks, so a guide loaded through `FromJson` without `Normalize` still validates cleanly. The rule groups are:
- ids
- facts (links, dates, filing/memo date, timeline, actor and knower active ranges)
- cast (range, name, email, domain, reserved domain, organization)
- shared organization domains
- issues (count gone, no fact)
- key characters with no fact
- counsel coverage (in-house for Party organizations; outside firm with a partner and an associate for Party and Individual organizations)
- preset ranges
- canonical values (label/value conflicts, empty values, unknown facts, scanner cross-check, registered values that never appear in the text)
- real-name leaks through `NameSubstitution.FindLeaks`: multi-word leaks are Errors, single-token leaks are Warnings
- unfinished parts, and stale parts using `GuideStaleness.IsStale` and `StaleReason`
- NeedsReview facts and values; a deleted fact keeps the stored reason
- mapping notes
- core document parties vs organizations

`GuideFinding.Where` produces the Checks card labels. `WorstFor` drives the status dots.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | 7a72f39 | test(03-08): add failing CanonicalValueScanner tests |
| 1 | GREEN | 969e349 | feat(03-08): add CanonicalValueScanner for amounts and identifiers |
| 2 | RED | b0d73b5 | test(03-08): add failing CaseGuideValidator tests |
| 2 | GREEN | 8ac29d9 | feat(03-08): add CaseGuideValidator with the full guide rules table |

## Verification

- `dotnet test ... --filter "FullyQualifiedName~ReelDiscovery.Tests.Case&Category!=Render"`: 669 passed, 0 failed. New tests: 15 scanner cases and 61 validator cases (54 `[Fact]`/`[Theory]` attributes).
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- Acceptance greps:
  - `GeneratedRegex` count = `MatchTimeoutMs)]` count = 3
  - `RegexMatchTimeoutException` in the scanner: 1
  - Validate signature: 1
  - `NameSubstitution.FindLeaks`, `CanonicalValueScanner.Scan` and `GuideStaleness.StaleReason`: 1 each; `GuideStaleness.`: 2
  - The counsel, party-without-organization and "name differs from party" messages: 1 each
- `Fixture_HasNoErrors` passes. The fixture also produces no "A check could not run" warnings. With null lists and entries loaded via `FromJson`, the validator runs without any rule failing.

## Deviations from Plan

None that change behavior. The plan left a few details open; the choices made are recorded as decisions above (the `{where}` label for leaks, which entry a conflict is reported on, exact-text label conflicts, which value kinds are checked against the text, and the trailing period). Two small test-only additions were made in the GREEN commit: the fixture and FromJson tests also assert that no rule failed.

## TDD Gate Compliance

Each task has a `test(03-08)` RED commit (a compile failure because the types did not exist yet), followed by a `feat(03-08)` GREEN commit. No refactor commits were needed.

## Threat Mitigations

- **T-03-26 (DoS):** the validator reads data through a null-cleaned Context, and each rule has its own try/catch. When a rule fails, the validator adds a Warning and keeps going. Regex timeouts produce the warning "Some text is too large to scan for real names." Tested by NullSafe, the FromJson null test and the 2 MB HugeText test.
- **T-03-27 (real-name leaks):** `FindLeaks` scans the text fields of organizations, cast, facts and values. A multi-word leak is an Error, so it blocks Next.
- **T-03-28 (orphans):** orphan issue, cast, organization and fact links are Errors, and so is an actor who is inactive on the fact date.

## Requirements

No requirements are marked complete here. This plan delivers the validator logic. The Checks card, the Next gate (GUIDE-07, in 03-16), canonical value generation (GUIDE-06), counsel generation (GUIDE-03) and the stale banner UI (GUIDE-09) land in later plans.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Case/CanonicalValueScanner.cs, Services/Case/CaseGuideValidator.cs, ReelDiscovery.Tests/Case/CanonicalValueScannerTests.cs, ReelDiscovery.Tests/Case/CaseGuideValidatorTests.cs
- FOUND commits: 7a72f39, 969e349, b0d73b5, 8ac29d9
