---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 04
subsystem: evidence-knowledge-ledger
tags: [knowledge-gate, ledger, determinism, corp-01, corp-08, corp-09]
requires: [05-01]
provides:
  - Services/Evidence/KnowledgeGate (ThreadKnowledge, KnowableValue, Build, KnowsFact, KnowsValue, Knowability, Ancestors)
  - Services/Evidence/LedgerSelector (Select, MaxLedgerEntries = 30)
  - Services/Evidence/LedgerMerger (EntriesFor, Build, NormalizeAbout, SameValue, MaxEntriesPerThread = 12)
affects: [render prompt builder, generation runner, journal replay]
tech-stack:
  added: []
  patterns: [pure projection of journaled deltas, point-in-time gating, GeneratedRegex id stripping]
key-files:
  created:
    - Services/Evidence/KnowledgeGate.cs
    - Services/Evidence/LedgerSelector.cs
    - Services/Evidence/LedgerMerger.cs
    - ReelDiscovery.Tests/Evidence/KnowledgeGateTests.cs
    - ReelDiscovery.Tests/Evidence/LedgerSelectorTests.cs
    - ReelDiscovery.Tests/Evidence/LedgerMergerTests.cs
  modified: []
decisions:
  - "[Phase 05] KnowledgeGate: a value linked to any public fact (no KnownBy/Actor ids) is public but still date-gated by its earliest linked fact; unresolvable linked fact ids are ignored"
  - "[Phase 05] KnownValueIdsBySeq also includes values linked to the message's own plan facts, mirroring MayMentionBySeq"
  - "[Phase 05] LedgerMerger dedupe key = Kind + NormalizeAbout(About), falling back to the normalized Statement when About is empty; duplicates within a thread absorb KnownBy/Fact ids, across threads only KnownByIds"
  - "[Phase 05] NormalizeAbout turns every non letter/digit into a space (Change-Order -> change order) before collapsing whitespace"
metrics:
  duration: 20min
  completed: 2026-10-03
  tasks: 2
  files: 6
---

# Phase 5 Plan 04: Knowledge Gate, Ledger Selector and Ledger Merger Summary

Point-in-time knowledge gating (facts and canonical values limited by date and by who on the thread knew them, with KnownByIds -> ActorIds -> public fallbacks), plus the narrative ledger's two pure halves: a selector that hands a later thread at most 30 earlier-wave, pre-dated, participant-known entries ranked by shared facts/issues/recency, and a merger that turns journaled deltas into code-numbered, id-free, deduplicated T-xxxx-Lnn entries.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | KnowledgeGate | 67edf92 (test), 0eccef6 (feat) |
| 2 | LedgerSelector and LedgerMerger | 4ccd362 (test), 0413e53 (feat) |

## What Was Built

- **KnowledgeGate:** `KnowersOf` (RESEARCH A6 fallbacks), `KnowsFact` (undated facts are not date-gated), `Knowability` (earliest linked fact date, union of knowers, public when unlinked or linked to a public fact), `KnowsValue`, `Ancestors` (root-first, visited set, stops at missing seq), and `Build` producing participants, first/last sender-local dates, gated facts (date then id), per-seq may-mention fact ids, thread-level values, per-seq known value ids and per-seq ancestors. Null-safe on every guide list. Uses `SkeletonContext.FactDate` and `MessageTime`.
- **LedgerSelector.Select:** filters `WaveIndex < wave`, `ParsedDate < FirstLocalDate`, KnownByIds intersecting participants; ranks by shared FactIds (thread + message plan facts), shared IssueIds, later date, Id; takes `cap` (default 30); returns chronological order.
- **LedgerMerger.EntriesFor:** maps deltas by seq (unknown dropped), strips every CH/F/ISS/T/V/ORG/EXT id token and collapses CR/LF, caps About 120 / Statement 300 / Value 120, drops empty statements, undefined Kind -> Other, canonicalizes FactIds and keeps only gated ones, derives IssueIds from kept facts, KnownByIds = source participants plus participants of later messages descending from it, merges same-detail duplicates (`SameValue` via `CanonicalValueScanner.AmountsEqual` or equal yyyy-MM-dd / "MMMM d, yyyy" dates), caps 12, ids `{thread}-L{nn}` invariant.
- **LedgerMerger.Build:** threads in the wave map only, ordered wave -> FirstUtc -> Id; cross-thread duplicates keep the earlier entry and union KnownByIds; ids never renumbered; capped at `NarrativeLedger.MaxEntries`; independent of dictionary insertion order.

## Verification

- `dotnet test --filter "FullyQualifiedName~KnowledgeGateTests"`: 13 passed.
- `dotnet test --filter "FullyQualifiedName~LedgerSelectorTests|FullyQualifiedName~LedgerMergerTests"`: 17 passed.
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 85 passed.
- `dotnet test --filter "Category!=Render"`: 2107 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 CS warnings.
- Acceptance greps: Build signature 1, `SkeletonContext.FactDate` present, `RelevanceCriteria` 1 (warning comment only), `MaxLedgerEntries = 30` 1, `"{0}-L{1:00}"` 1, `GeneratedRegex` present, `new Regex(` 0.

## Deviations from Plan

None in behavior. Small clarifications chosen during implementation (recorded as decisions above): public-fact values remain date-gated, empty-About dedupe falls back to the statement, and in-thread duplicates also absorb FactIds.

## TDD Gate Compliance

Both tasks followed RED -> GREEN with separate commits: `test(05-04)` 67edf92 then `feat(05-04)` 0eccef6; `test(05-04)` 4ccd362 then `feat(05-04)` 0413e53. RED failed at compile time (types not yet defined). No refactor commits were needed.

## Threat Mitigations

- T-05-09: seq mapping, gated FactIds, id-token/CRLF stripping, field length caps, 12 per thread (LedgerMerger).
- T-05-10: KnowledgeGate never exposes future or unknown facts/values and does not touch RelevanceCriteria or the full registry.
- T-05-11: selector cap of 30.

## Known Stubs

None.

## Self-Check: PASSED
- All 6 created files exist; commits 67edf92, 0eccef6, 4ccd362, 0413e53 are in git log.
