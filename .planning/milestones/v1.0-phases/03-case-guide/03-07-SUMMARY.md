---
phase: 03-case-guide
plan: 07
subsystem: case-guide-editing
tags: [staleness, content-hash, editing, needs-review, tdd]
requires:
  - "03-01: CaseDraft, CaseGuide, GuidePartState (BasisHash, BasisComponents), CastMember, KeyFact, CanonicalValue"
  - "03-03: IdAllocator, EmailIdentity, SignatureBuilder"
provides:
  - "Services/Case/GuideStaleness.cs (Order, CoreDocumentHash, BasisFor, Stamp, IsStale, StaleParts, StaleKey, AbsorbHandEdit, NeedsReviewCount, ValuesNeedingReviewCount, StaleReason)"
  - "Services/Case/CaseGuideEditing.cs (AddCharacter, SetCastField, FactsReferencing, DeleteCharacter, AddFact, SetFactField, SetFactLinks, MarkReviewed, MarkValueReviewed, ValuesReferencing, DeleteFact, AddValue, SetValueField, SetValueFacts, DeleteValue, internal RefreshIdentity)"
  - "CastField, FactField, FactLinkKind, ValueField enums; GuideEditResult"
affects: [03-11, 03-13, 03-14, 03-15]
tech-stack:
  added: []
  patterns:
    - "Part basis = SHA-256 over ordered component hashes (core; +issues/cast for Facts; +facts for Values); components stored at stamp time so StaleReason can name what changed"
    - "Hand edits run inside GuideStaleness.AbsorbHandEdit: downstream parts that were fresh are restamped; review happens via NeedsReview flags instead"
    - "Edit methods validate first (return GuideEditResult.Fail with UI-SPEC copy), then build a single apply action, so rejected input never mutates the guide"
key-files:
  created:
    - Services/Case/GuideStaleness.cs
    - Services/Case/CaseGuideEditing.cs
    - ReelDiscovery.Tests/Case/GuideStalenessTests.cs
    - ReelDiscovery.Tests/Case/CaseGuideEditingTests.cs
  modified: []
decisions:
  - "[03-07]: Role/Tier/SignatureStyle/Significance/Kind edits accept enum names only (case-insensitive); anything else returns 'Choose a value from the list.' (copy not in the plan's error list)"
  - "[03-07]: Counsel sync runs on any identity change (name, organization or typed email) when the character's old full name matches Counsel.AttorneyName"
  - "[03-07]: Empty ActiveFrom/ActiveTo is allowed (unknown); empty fact Date is rejected with DateFormatError"
  - "[03-07]: AddCharacter assigns a fictional 555-01xx phone so the Full signature is complete"
  - "[03-07]: MarkReviewed/MarkValueReviewed bump Revision (autosave) but do not set IsUserEdited and do not touch staleness"
  - "[03-07]: GuideEditResult uses auto-properties (project convention) instead of the public fields shown in the plan contract; call sites are identical"
metrics:
  duration: 10min
  completed: 2026-10-01
---

# Phase 3 Plan 07: Guide Staleness and Editing Summary

Content-hash staleness per guide part (GUIDE-09, D-20) plus every hand edit / add / delete for cast, facts and canonical values with deterministic NeedsReview propagation (GUIDE-08, D-18, D-19, D-21) — pure, UI-free services with 48 unit tests.

## What was built

**GuideStaleness** — Issues and Cast are based on the core document hash (parties, counts, fact plan, timeline, filing date, section ids + paragraphs; the signature block is excluded). Facts add issues (Id, Title) and cast (Id, FullName, OrganizationId, active range); Values add facts (Id, Date, Title, Description). `Stamp` stores the basis and its components; `IsStale` requires Ready status and a non-empty stored basis, so unstamped/failed parts are never stale and the hashes survive a case.json round-trip. `AbsorbHandEdit` restamps only downstream parts that were fresh before the edit, so existing staleness is never hidden. `StaleReason` names the first changed component ("core document", "issues", "cast", "key facts").

**CaseGuideEditing** — All mutations are no-ops for unknown ids or unchanged values (Revision untouched); on change they run in `AbsorbHandEdit`, set `IsUserEdited` and bump `Revision`. Cast name/org edits recompute email (unless `EmailIsUserEdited`) and signature; name/org/active-range edits flag citing facts with "{name} was changed after it was written." and renames rewrite the old full name in those facts' Title/Description using an escaped, letter/digit-bounded regex with a 1 s timeout (timeout skips the rewrite but keeps the flag). Fact edits, link edits and deletes flag linked canonical values. Link edits keep only existing ids, distinct, order-preserving, and keep KnownBy a superset of Actors.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | d3e51a7 | test(03-07): add failing GuideStaleness tests |
| 1 | GREEN | cacf51b | feat(03-07): add GuideStaleness content-hash staleness per guide part |
| 2 | RED | 8cc0bd5 | test(03-07): add failing CaseGuideEditing tests |
| 2 | GREEN | b3b5e4f | feat(03-07): add CaseGuideEditing for cast, facts and values |

## Verification

- `dotnet test ... --filter "FullyQualifiedName~ReelDiscovery.Tests.Case&Category!=Render"`: 593 passed, 0 failed (14 GuideStaleness + 34 CaseGuideEditing new).
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- Acceptance greps: AbsorbHandEdit signature 1; "Counsel" in GuideStaleness.cs 0; `GuideStaleness.AbsorbHandEdit` in CaseGuideEditing.cs 12; `Regex.Escape` 1; `TimeSpan.FromSeconds(1)` 1; "was changed after it was written." 1; date error copy 1.

## Deviations from Plan

None affecting behavior; small gaps the plan left open were filled and recorded as decisions above (enum choice error copy, counsel sync on email edits, phone on new characters, properties instead of fields on GuideEditResult).

## TDD Gate Compliance

Both tasks have a `test(...)` RED commit followed by a `feat(...)` GREEN commit. No refactor commits were needed.

## Threat Mitigations

- T-03-23 (ReDoS): `Regex.Escape` on the old name + `TimeSpan.FromSeconds(1)`; `RegexMatchTimeoutException` skips the rewrite but still flags the fact. Replacement text escapes `$`.
- T-03-24: `SetFactLinks` / `SetValueFacts` keep only ids that exist.
- T-03-25: dates via `CaseDraft.TryParseDate` (yyyy-MM-dd), emails via `EmailIdentity.IsWellFormed`; invalid input is rejected before any mutation.

## Requirements

GUIDE-08 and GUIDE-09 are not marked complete: this plan delivers their logic layer; the review/edit/regenerate UI and the stale banner land in 03-11 and 03-13..03-15.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Case/GuideStaleness.cs, Services/Case/CaseGuideEditing.cs, ReelDiscovery.Tests/Case/GuideStalenessTests.cs, ReelDiscovery.Tests/Case/CaseGuideEditingTests.cs
- FOUND commits: d3e51a7, cacf51b, 8cc0bd5, b3b5e4f
