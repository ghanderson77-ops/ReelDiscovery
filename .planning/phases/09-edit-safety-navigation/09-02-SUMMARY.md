---
phase: 09-edit-safety-navigation
plan: 02
subsystem: discovery-staleness
tags: [W3, staleness, discovery, journal, tdd]
requires: []
provides:
  - StaleSource.Exhibits
  - DiscoveryDocument.ExhibitStamps (+ Normalize repair)
  - DiscoveryCorpus.WrittenStamps (null = unknown, empty = known-empty)
  - DiscoveryStaleness.EmailStamps / CitedDocIds / AnyCitesEmails
  - DiscoveryStaleness.Check(document, draft, currentStamps = null)
affects:
  - 09-08 (UI text for Exhibits staleness, SavedRows with current stamps)
tech-stack:
  added: []
  patterns: [derived staleness from journal ThreadDone stamps (CR-B02 precedent, no attachment pass)]
key-files:
  created: []
  modified:
    - Models/Discovery/DiscoveryDocument.cs
    - Models/Discovery/DiscoveryCorpus.cs
    - Services/Discovery/DiscoveryStaleness.cs
    - Services/Discovery/CorpusDetector.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryStalenessTests.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryStoreTests.cs
    - ReelDiscovery.Tests/Discovery/CorpusDetectorTests.cs
decisions:
  - "Exhibit check runs last (after CorpusPlan) and only when current stamps are known; null stamps never mark a document Out of date"
  - "Legacy documents (empty ExhibitStamps) get an existence-only check; a changed stamp counts only when one was recorded"
  - "EmailStamps uses ThreadDone Utc only; attachment completions never move a stamp (Pitfall 4)"
  - "CorpusDetector: missing journal = known-empty stamps (Start Over); unreadable / plan changed / no plan / exception = null"
metrics:
  duration: 8min
  completed: 2026-10-11
  tasks: 2
  files: 7
---

# Phase 9 Plan 02: Per-exhibit staleness for discovery documents Summary

Discovery documents now record the journal ThreadDone time of every email they cite, and the derived staleness check reports `StaleSource.Exhibits` when a cited email was regenerated or deleted. The current stamps come from the journal replay that `CorpusDetector` already runs, so Start Over and Regenerate Thread need no code changes.

## What Was Built

- **Model:** `StaleSource.Exhibits` added as the last enum member. `DiscoveryDocument.ExhibitStamps` maps DocId to Utc. `Normalize()` turns null into an empty map, drops blank keys, collapses duplicates and rebuilds it with the Ordinal comparer. `DiscoveryCorpus.WrittenStamps` is nullable, and `None(...)` leaves it null.
- **DiscoveryStaleness:**
  - `EmailStamps(replay)` copies only the ThreadDone loop of `BlindReviewer.WrittenStamps`.
  - `CitedDocIds` collects DocIds from deposition exhibits and written-discovery requests.
  - `AnyCitesEmails(set)` reports whether any document cites an email.
  - `Stamp` records stamps only for cited ids that the corpus reports as written.
  - `Check` gains an optional `currentStamps` argument, so existing callers are unchanged.
- **CorpusDetector:**
  - A missing journal gives known-empty stamps.
  - "No relevant emails written yet" and a usable corpus both carry `EmailStamps(replay)`.
  - Unreadable journal, plan changed, no plan and exceptions keep `WrittenStamps` null.
- The `DiscoveryGenerator` Stamp call count is unchanged (1), and `SaveAgainAsync` still does not stamp.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | ec9a33c | test(09-02): add failing tests for discovery exhibit stamps and staleness |
| 1 | GREEN | 0af84ad | feat(09-02): add per-exhibit stamps and Exhibits staleness for discovery documents |
| 2 | RED | 7f7d52d | test(09-02): add failing tests for CorpusDetector WrittenStamps |
| 2 | GREEN | 4d16594 | feat(09-02): publish written-email stamps from CorpusDetector |

## Verification

- Build with `-warnaserror`: 0 warnings, 0 errors.
- Filtered tests `CorpusDetectorTests|DiscoveryStalenessTests|DiscoveryStoreTests|DiscoveryGeneratorTests`: 79/79 passed.
- No attachment code was copied: `grep -v "^\s*//" DiscoveryStaleness.cs | grep -c Attachment` = 0.

## Deviations from Plan

- **Acceptance grep count (plan-spec inconsistency, no code impact):** the plan expects `grep -c "WrittenStamps = DiscoveryStaleness.EmailStamps(replay)" CorpusDetector.cs` = 1, but it returns 2. The plan's own required line `none.WrittenStamps = DiscoveryStaleness.EmailStamps(replay);` also contains that substring. The usable-result initializer line exists exactly once, so the key_link pattern is satisfied.
- **EmailStamps attachment test:** this test builds a `JournalReplay` in memory with an `AttachmentDone` record in `LastByAttachment`, rather than appending to the fixture journal. The fixture threads plan no attachments, so replay would drop a journaled attachment line before `EmailStamps` saw it.

Otherwise the plan was executed as written.

## Known Stubs

None. `DiscoveryUiText.StaleWhat` falls back to "it" for `Exhibits` until plan 09-08 adds the dedicated copy, as planned.

## TDD Gate Compliance

Both tasks have a RED `test(...)` commit followed by a GREEN `feat(...)` commit. No refactor was needed.

## Self-Check: PASSED
