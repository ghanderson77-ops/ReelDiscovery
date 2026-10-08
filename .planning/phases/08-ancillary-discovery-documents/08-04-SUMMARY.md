---
phase: 08-ancillary-discovery-documents
plan: 04
subsystem: discovery-written-planner
tags: [discovery, rfp, interrogatories, rfa, planner, ANC-02, ANC-03, ANC-04]
requires:
  - "08-01: DiscoverySettings, DiscoveryKind/Variant, DiscoveryVariants.Of, DiscoveryCorpus/ExhibitCandidate"
  - "08-02: CounselDirectory.FirstPlaintiffSide/FirstDefenseSide/PartyOf, DiscoveryTestData"
provides:
  - "WrittenDiscoveryPlanner: Availability, ResolveParties, EffectiveCount, SetTitle, FileDocumentPart, Plan; constants MaxInterrogatories 25, MaxSlotsPerCall 25, PersonsWithKnowledgeSlots 2, MaxGenuineness 3"
  - "KindAvailability, PartyChoice, CountChoice, RequestSlot (with Copy), WrittenSetPlan"
affects: [08-builders, 08-worklist, 08-writers, 08-ui]
tech-stack:
  added: []
  patterns:
    - "Request slots keyed by code to guide issue/fact ids and written-corpus DocIDs only (D-04, T-08-12)"
    - "Apportion.Largest(effective, fact-count weights, min: 1) per issue for RFP and ROG contention slots"
    - "RFA genuineness count absorbs the remainder when EffectiveCount was lowered (plan revision 3), so Slots.Count == Count.Effective"
key-files:
  created:
    - Services/Discovery/WrittenDiscoveryPlanner.cs
    - ReelDiscovery.Tests/Discovery/WrittenDiscoveryPlannerTests.cs
  modified: []
decisions:
  - "08-04: RFA order treats Hot and Key as one tier ahead of Supporting, then date, then id"
  - "08-04: Memo file document part is \"{PropoundingShort}'s {memo label}\" (e.g. \"United States's CID document requests\"); civil is \"{Short}'s First Set of ...\""
  - "08-04: ROG persons-with-knowledge slots shrink to keep at least one contention slot (effective 1 -> 0 persons, 2 -> 1)"
  - "08-04: When issues exceed contention slots, issues are dealt round-robin heaviest first into shared slots; shared slots are ordered by their first issue in guide order"
  - "08-04: Custodians are Key Party/Staff/InHouseCounsel characters of the responding party who act in or know the issue's facts"
  - "08-04: A guide with no issues yields an RFP/ROG plan with zero slots and a note; RFAs still plan from dated facts"
metrics:
  duration: 20min
  completed: 2026-10-08
  tasks: 2
  files: 2
---

# Phase 8 Plan 04: Written Discovery Planner Summary

`WrittenDiscoveryPlanner` is pure code that decides everything about the written sets before any model call: which sets are offered for the document type, the label each one gets, which party serves which, the effective request counts and the set titles and file-name parts. It also assigns each numbered request to the issue, key fact or produced DocID it covers. The model only writes the request text.

## What Was Built

- **Availability (D-25).** Civil pleadings offer all three sets. A criminal memo offers only the `Subpoena rider (documents)`. A regulatory memo offers `CID document requests` and `CID interrogatories`. A civil memo offers `Pre-suit document requests`, `Written questions` and `Requests to admit`. Depositions are never offered here.
- **ResolveParties (D-16).** Saved party ids are used while they still exist. Otherwise the first plaintiff or complainant serves the first defendant or subject. A saved id that no longer exists sets `Fallback`. When both ids are equal they are kept, and `Same` is set.
- **EffectiveCount (D-17, D-18, D-19).** These clamps apply whatever the stored value (T-08-11):
  - RFPs are kept between 5 and 80, then raised to the issue count (never above 80).
  - Interrogatories are kept between 1 and 25, then raised to min(issues, 25).
  - RFAs are kept between 1 and 50, then capped at the number of dated facts plus up to 3 hot written documents that can be authenticated.
  - When the RFA cap applies, `Lowered` is set and the result carries `Available` and `LoweredReason`. With no dated facts the count is 0 and `Plan` returns null.
- **SetTitle and FileDocumentPart.** Civil titles read `PLAINTIFF X'S FIRST SET OF ... TO DEFENDANT Y`; petitions use PETITIONER and RESPONDENT. Memo titles are the subpoena rider, CID and pre-suit forms. File-name parts read `"{Short}'s First Set of ..."`, and memo sets use the memo label instead.
- **Plan.** Returns null for a deposition, a hidden kind, a disabled kind, unresolved parties, or RFAs with no dated facts.
  - **RFP:** requests are spread across issues in proportion to how many facts each has, with every issue getting at least one. Each request carries its issue's custodians, its project, contract, change-order and amount values, and the date range of its facts.
  - **Interrogatories:** two persons-with-knowledge requests come first, then contention requests per issue. When there are more issues than contention slots, several issues share a slot so every issue still appears.
  - **RFA:** one dated fact per request, Hot and Key facts first, then by date. Each request opens with `Admit that on {Month d, yyyy},`. After the fact requests come 1-3 genuineness requests (`Admit that the document produced as {DocId} is a true and correct copy of`) for hot DocIDs linked to the selected facts. `Slots.Count == Count.Effective` in every case.

## Verification

- Both `-warnaserror` builds (app and test project) passed with 0 warnings.
- `WrittenDiscoveryPlannerTests` passed 35/35, and `FullyQualifiedName~Tests.Discovery.` passed 130/130.
- Every test named in the plan passes.
- Every acceptance grep in the plan matches.

## Deviations from Plan

None in substance. Extra tests beyond the plan cover:
- the civil and null-document availability cases
- the memo default parties
- RFP never exceeding 80
- undated facts being skipped
- disabled kinds
- the no-guide plan
- RFP slots being independent copies

The RFP test was adjusted during GREEN: an issue with no facts still has weight 1 under largest-remainder apportionment, so with 25 requests it gets more than one. The test now checks that it has at least one, fewer than ISS-01, and no date range.

## TDD Gate Compliance

- Task 1: RED f24a137 (test), then GREEN 64b906b (feat)
- Task 2: RED 9e023c1 (test), then GREEN 92f9a5c (feat)
- No refactor commits were needed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | f24a137 | test(08-04): add failing tests for written discovery availability, parties, counts and titles |
| 1 GREEN | 64b906b | feat(08-04): add written discovery availability, parties, effective counts and titles |
| 2 RED | 9e023c1 | test(08-04): add failing tests for RFP, ROG and RFA request slots |
| 2 GREEN | 92f9a5c | feat(08-04): plan RFP, ROG and RFA request slots |

## Known Stubs

None. This is a pure planner with no UI.

## Self-Check: PASSED
