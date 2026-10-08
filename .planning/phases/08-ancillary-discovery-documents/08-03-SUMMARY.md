---
phase: 08-ancillary-discovery-documents
plan: 03
subsystem: discovery-planning
tags: [discovery, deposition, planner, case-number, ANC-01, ANC-05]
requires:
  - "08-01: DiscoverySettings, DepositionContent/DepositionExhibit/DepositionSpeaker, ExhibitAssignment, DiscoveryCorpus, DiscoveryVariants"
  - "08-02: CounselDirectory, DiscoveryTestData"
provides:
  - "CaseNumber.For(draft) / Label(doc): deterministic federal, state and memo case numbers and caption labels"
  - "DepositionPlanner: Eligible, DefaultWitnessIds, DocumentTitle, AssignRoles, Schedule, Plan"
  - "WitnessCandidate, DepositionSchedule, DepositionRoles, DepositionChunk, DepositionPlan (incl. Assignments with SourceKey)"
  - "DepositionExhibit.IsImpeachment"
affects: [08-worklist, 08-prompts, 08-writer, 08-generator, 08-ui]
tech-stack:
  added: []
  patterns:
    - "Every pick (tie-break, date jitter, start time, remote, reporter, defending attorney) comes from RenderAssignments.StableRank(caseId|witnessId|salt)"
    - "Witness knowledge slice = KnowledgeGate.KnowsFact / Knowability+KnowsValue / ledger KnownByIds, all as of the deposition date"
    - "Exhibit SourceKey: DOC:{DocId} for corpus emails, VAL:{valueId} for canonical documents"
key-files:
  created:
    - Services/Discovery/CaseNumber.cs
    - Services/Discovery/DepositionPlanner.cs
    - ReelDiscovery.Tests/Discovery/CaseNumberTests.cs
    - ReelDiscovery.Tests/Discovery/DepositionPlannerTests.cs
  modified:
    - Models/Discovery/DepositionContent.cs
decisions:
  - "08-03: DepositionPlan.Assignments carries the exhibits as ExhibitAssignment (with SourceKey DOC:/VAL:) so the generator can persist them without rebuilding source keys"
  - "08-03: A witness with existing sidecar assignments reuses only those (no new exhibits added); new numbers for other witnesses start after the highest valid existing number, including unselected witnesses' numbers"
  - "08-03: New exhibits are selected by rank (hot, significance, date) but numbered in transcript order (topic, date, source key); the impeachment tag follows the selection rank"
  - "08-03: A usable corpus with no qualifying emails yields zero exhibits (no fallback to canonical values); public values (no resolvable linked facts) count as known"
  - "08-03: Schedule makes a deposition remote when the examining side has no firm name (fallback counsel)"
metrics:
  duration: 9min
  completed: 2026-10-08
  tasks: 2
  files: 5
---

# Phase 8 Plan 03: Deposition Planner and Case Number Summary

This plan adds a pure, deterministic deposition planner and the case-number derivation. Before any model call, the planner settles which witnesses are deposed and who examines and defends them, plus the date, time, place, reporter and videographer. It also splits each deposition into topic chunks, gathers what the witness knew as of the deposition date, and picks and numbers the exhibits, including one impeachment exhibit for an adverse witness. Everything is SHA-256 ranked (`RenderAssignments.StableRank`) and never throws.

## What Was Built

- **CaseNumber**
  - `For(draft)` returns a federal number like `{1-4}:{yy}-cv-{nnnnn}`, a state number like `{yyyy}-CV-{nnnnnn}`, or a memo number like `{yyyy}-{nnnn}`.
  - The number comes from the StableRank of the CaseId. The year comes from FilingDate, then TimelineEnd, then CreatedUtc.
  - `Label(doc)` returns Case No., Cause No. (Petition), Grand Jury Matter No., CID No. or Matter No.
- **DepositionPlanner.Eligible**
  - Includes Key-tier Party/Staff characters and excludes counsel.
  - Ranks them by facts acted in or known, then facts acted in, then StableRank.
  - Resolves each witness's side through `CounselDirectory.PartyOf`; a witness with no party is NonParty.
- **DefaultWitnessIds** picks 1, 2 or 3 witnesses by preset. It starts with the defense side and alternates. When one side runs out it takes from the other, and non-party witnesses come only after both sides are exhausted.
- **DocumentTitle** returns DEPOSITION OF / TRANSCRIPT OF SWORN WITNESS INTERVIEW OF / EXAMINATION UNDER OATH OF, followed by the name upper-cased and otherwise unchanged.
- **AssignRoles**
  - The examining attorney is the opposing party's Partner. If there is none, it falls back to the core document counsel (`COUNSEL-CORE`, plaintiff side only), then to "Counsel for {party}".
  - The defending attorney is the Partner, or the Associate when the StableRank picks it. A non-party witness has no defending attorney.
  - Every other party's Partner (or a fallback) is listed as an appearance.
  - Each fallback adds a note, for example `Counsel for {party} is not in the cast, so the transcript says "Counsel for {party}".`
- **Schedule**
  - The anchor is FilingDate, then the latest fact, then TimelineEnd, then CreatedUtc.
  - The window is [max(anchor+3mo, latest fact+1), max(anchor+9mo, start+30d)]. Witnesses get evenly spaced slots plus a stable jitter of up to half a slot, each moved to an unused business day.
  - Start time is 9:30 a.m. or 10:00 a.m.; about a third of depositions are remote.
  - Otherwise the location is "{examining firm}, {VenueCity}", falling back to the firm's location, then "the offices of {firm}".
  - Reporter and videographer names come from built-in invented names and skip any cast name. The CSR is `CSR No. nnnnn`.
- **Plan**
  - Resolves the witnesses: unknown ids are ignored and at most 6 are kept. Pages are clamped to 10-60, and the plans are ordered by deposition date.
  - Knowledge slice: facts, values and ledger entries the witness knew as of the deposition date.
  - Topic chunks:
    - Topics are the witness's facts grouped by issue, in guide issue order.
    - When there are more topics than pages, only the topics with the most facts are kept, one page each.
    - Pages are split with `Apportion.Largest(min: 1)`. A topic budget over 8 pages is split into near-equal parts.
    - Each chunk aims for TargetTurns = pages × 14.
    - With no facts, there is a single "Background and role" topic.
  - Exhibits:
    - Each deposition gets clamp(round(pages/5), 3, 8), capped by the candidates available.
    - With a usable corpus, candidates are emails written to or from the witness that are hot or link a Hot/Key fact, dated on or before the deposition. Otherwise they are the DocumentTitle, Contract, ChangeOrder, Invoice and PO values the witness knows.
    - Numbers run across depositions. Existing assignments are reused verbatim, and duplicate or non-positive numbers are ignored.
    - Each exhibit's TopicIndex follows the issue of its first linked fact, and `chunk.ExhibitNumbers` is filled on the topic's first part.
    - A Plaintiff or Defense witness gets exactly one `IsImpeachment` exhibit.
  - Speakers are the appearances plus the witness, REPORTER and VIDEOGRAPHER.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and `dotnet build ReelDiscovery.Tests -warnaserror`: both green.
- `--filter "FullyQualifiedName~CaseNumberTests|FullyQualifiedName~DepositionPlannerTests"`: 48/48 passed.
- `--filter "FullyQualifiedName~Tests.Discovery."`: 95/95 passed.
- Acceptance greps:
  - StableRank 5 (≥3), BusinessCalendar 2, Apportion.Largest 1, KnowledgeGate.KnowsFact 1, `Plan(` signature 1, `cv-` present.
  - Zero Random/Guid.NewGuid/GetHashCode.
  - All six named Plan_* tests exist.

## Deviations from Plan

### Auto-added

**1. [Rule 2 - Correctness] `DepositionPlan.Assignments` added**
- **Found during:** Task 2
- **Issue:** `DepositionExhibit` has no SourceKey, but 08-15 persists the plan's exhibits as `ExhibitAssignment`. Without a SourceKey, a reused assignment could not be ranked or traced back to its source.
- **Fix:** `Plan` also returns the same exhibits as `ExhibitAssignment` objects, with WitnessId and SourceKey (`DOC:{DocId}` / `VAL:{valueId}`). This adds data only; `Exhibits` is unchanged.
- **Commit:** 13ffc71

Small additions beyond the listed behaviors:
- A fallback examining speaker `COUNSEL-EXAMINING` covers drafts whose core document has no opposing party.
- A fallback defending speaker `COUNSEL-WITNESS` covers a witness whose party is not in the core document.
- Extra tests: pages clamping, the exhibit count scaling with pages, the speaker set, unknown and duplicate ids being ignored, the null draft, and canonical exhibits when there is no corpus.

## TDD Gate Compliance

- Task 1: RED 7820efd (test) → GREEN 3d9316a (feat)
- Task 2: RED 6124141 (test) → GREEN 13ffc71 (feat)
- No refactor commits.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 7820efd | test(08-03): add failing tests for case number and deposition witnesses, roles, schedule |
| 1 GREEN | 3d9316a | feat(08-03): implement case number and deposition witnesses, roles, schedule |
| 2 RED | 6124141 | test(08-03): add failing tests for deposition plan chunks, knowledge slice and exhibits |
| 2 GREEN | 13ffc71 | feat(08-03): implement DepositionPlanner.Plan with topic chunks, knowledge slice and exhibits |

## Known Stubs

None. This is pure planning code; prompts, writers and UI arrive in later plans.

## Self-Check: PASSED
