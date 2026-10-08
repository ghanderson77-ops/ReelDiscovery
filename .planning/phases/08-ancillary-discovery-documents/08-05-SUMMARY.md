---
phase: 08-ancillary-discovery-documents
plan: 05
subsystem: discovery-corpus-detector
tags: [discovery, corpus, exhibits, ledger, ANC-01, ANC-03]
requires:
  - "08-01: DiscoveryCorpus/ExhibitCandidate models"
  - "08-02: DiscoveryTestData.LitigationDraft"
provides:
  - "CorpusDetector.Detect(CaseDraft) -> DiscoveryCorpus (usable snapshot or None with reason; never throws)"
  - "DiscoveryCorpusFixture.WithWrittenCorpusAsync / PlanOf / Done (test fixture: saved plan + journal)"
affects: [08-builders, 08-worklist, 08-ui]
tech-stack:
  added: []
  patterns:
    - "Exhibit candidates = relevant-thread messages whose DocId is in BlindReviewer.GeneratedEmailDocIds (journal-confirmed writes)"
    - "Ledger slice rebuilt with GenerationRunState.From -> LedgerMerger.Build(WavePlanner.WaveIndexByThread(...)), same chain as Case Summary"
key-files:
  created:
    - Services/Discovery/CorpusDetector.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryCorpusFixture.cs
    - ReelDiscovery.Tests/Discovery/CorpusDetectorTests.cs
  modified: []
decisions:
  - "08-05: Missing journal, unreadable journal and plan-changed journal each return a distinct user-facing reason"
  - "08-05: Candidate FactIds fall back to the thread's FactIds when the message has none; ParticipantIds are From+To+Cc distinct, blanks dropped"
metrics:
  duration: 8min
  completed: 2026-10-08
  tasks: 2
  files: 3
---

# Phase 8 Plan 05: Corpus Detector Summary

`CorpusDetector.Detect` decides whether discovery documents can cite a generated corpus. It loads the plan that case.json references, replays the journal (which must exist, be readable and match the plan), and returns exhibit candidates only for relevant-thread emails the journal records as written. It also returns the merged narrative ledger, built with the same chain Case Summary uses.

## What was built

- **Services/Discovery/CorpusDetector.cs**: read-only static probe. If there is no guide or folder, no plan, the plan file cannot be read, the journal is missing, unreadable or belongs to another plan, or no relevant email has been written, it returns `DiscoveryCorpus.None(reason)`. Otherwise it returns `Usable = true`, the `PlanId`, the candidates (DocId, thread, subject, yyyy-MM-dd date, participants, hot flag, fact ids) and `Ledger = merged.Entries`. An outer catch rethrows `OperationCanceledException` and turns any other exception into None (D-05: a broken corpus must never block discovery documents).
- **DiscoveryCorpusFixture**: builds a LitigationDraft with one-message relevant threads that use the key witnesses (From/To/Cc) and Hot/Key fact ids, plus one non-relevant thread. It saves the plan with `CorpusPlanStore.SaveAsync` and journals real ThreadDone records through `GenerationJournal.AppendAsync`.
- **CorpusDetectorTests** (7 tests, all passing): NoCorpus_NotUsable, WrittenRelevantEmails_AreCandidates (loads the plan from the file, checks hot flag, participants, date, fact ids and that the non-relevant thread is excluded), PlanChanged_NotUsable, JournalMissing_NotUsable, UnwrittenDocIds_Excluded, NoRelevantWritten_NotUsable, CorruptPlan_NotUsableNoThrow.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 | a02d44f | feat(08-05): add CorpusDetector usable-corpus probe |
| 2 | 98aad52 | test(08-05): add discovery corpus fixture and CorpusDetector tests |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 warnings, 0 errors
- `dotnet test ... --filter "FullyQualifiedName~CorpusDetectorTests"`: 7 passed
- Acceptance greps: GeneratedEmailDocIds 1, `?? draft.Corpus` 0, LedgerMerger.Build 1, `catch (OperationCanceledException)` 1, file writes 0

## Deviations from Plan

**1. [Rule 2 - Clarity] Separate reasons for journal states.** Step 3 named one combined "not Readable or PlanChanged" check. A missing journal has an empty `Reason`, so the detector returns "No emails have been generated yet." for it and a plan-specific message for PlanChanged. That keeps `Reason` non-empty, which the JournalMissing test requires.

**2. [Test] Extra test NoRelevantWritten_NotUsable.** Covers the zero-candidate path. That gives 7 tests where the plan asked for at least 6.

## TDD Gate Compliance

Task 2 is marked `tdd="true"`, but the plan puts the implementation (Task 1) before the tests, so the tests could never be seen failing first. The order is `feat` (a02d44f) then `test` (98aad52), with no RED commit before GREEN. The tests check behavior against real plan and journal files.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Discovery/CorpusDetector.cs
- FOUND: ReelDiscovery.Tests/Discovery/DiscoveryCorpusFixture.cs
- FOUND: ReelDiscovery.Tests/Discovery/CorpusDetectorTests.cs
- FOUND: a02d44f, 98aad52
