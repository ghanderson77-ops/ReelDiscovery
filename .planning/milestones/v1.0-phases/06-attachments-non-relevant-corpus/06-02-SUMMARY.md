---
phase: 06-attachments-non-relevant-corpus
plan: 02
subsystem: evidence-progress
tags: [progress, snapshot, attachments, non-relevant, case-state, composer-contracts]
requires: []
provides:
  - "AttachmentRunStatus { Pending, Written, Warnings, Failed, Skipped } + AttachmentSeed"
  - "GenerationSnapshot.RecordAttachment (per-DocId status replacement, row moves) and AddCost (uncommitted spend)"
  - "GenerationSnapshot.Reset(counts, rows, attachment seeds) and RecordThread(..., relevant)"
  - "GenerationStage SideProjects/NonRelevant/Attachments; StageRowKind; 11 new GenerationEventKind values"
  - "GenerationCounts relevant/non-relevant split, attachment counts by status and kind, MediaPlanned, MediaCostUsd"
  - "WaveProgress RowKind + attachment counters; GenerationEvent attachment/side-project fields"
  - "GenerationState cached non-relevant/attachment counts + MediaCostUsd (clamped, default 0)"
  - "ComposedAttachment; ComposedEmail.Attachments and internal QuoteFragment"
affects: [06 composer, mailbox writer, units of work, runner and UI plans]
tech-stack:
  added: []
  patterns:
    - "Attachment counters are status transitions per DocId: previous status removed from its previous row, new status added to the target row"
    - "Status counters move only on committed outcomes; uncommitted spend goes through AddCost"
key-files:
  created:
    - Models/Evidence/AttachmentRunStatus.cs
    - Models/Evidence/ComposedAttachment.cs
  modified:
    - Models/Evidence/GenerationProgress.cs
    - Models/Evidence/GenerationState.cs
    - Services/Evidence/GenerationSnapshot.cs
    - Services/Evidence/EmailComposer.cs
    - ReelDiscovery.Tests/Evidence/GenerationSnapshotTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationStateTests.cs
decisions:
  - "CostUsd includes media spend; MediaCostUsd is the media share (RecordAttachment/AddCost add cost + mediaCost to CostUsd)"
  - "Reset expects counts and rows to already include the seeded attachment statuses; seeds only tell RecordAttachment what to subtract"
  - "Thread failures are not split by relevance, so the previous-Failed correction only touches the shared Failed counters"
metrics:
  duration: 11min
  completed: 2026-10-04
  tasks: 2
  files: 8
---

# Phase 6 Plan 02: Progress, State and Composition Contracts Summary

Per-DocId attachment status in GenerationSnapshot (replace, never accumulate), a separate AddCost path for spend that was not committed, a relevant/non-relevant thread split, stage row kinds, 11 new activity event kinds, cached Phase 6 counts in case.json, and attachment parts on ComposedEmail.

## What Was Built

- **AttachmentRunStatus / AttachmentSeed** (`Models/Evidence/AttachmentRunStatus.cs`): per-attachment run state plus the replayed (row, status) seed.
- **GenerationSnapshot**:
  - `Reset(counts, rows, attachments = null)` copies the seeds into an ordinal dictionary. Existing 2-argument calls still compile.
  - `RecordAttachment(docId, rowIndex, status, cost, mediaCost)` removes the DocId's previous status from the counts and from its previous row (clamped at 0), adds the new status to the counts and the target row, and stores the new seed.
  - `AddCost` changes only CostUsd and MediaCostUsd.
  - `RecordThread(..., relevant = true)` sends done threads to RelevantDone or NonRelevantDone.
  - Every change runs under `lock (_lock)`.
- **GenerationProgress**:
  - New stages SideProjects, NonRelevant and Attachments, plus `StageRowKind`.
  - New event kinds, appended to the end of the enum.
  - New count fields: the relevant/non-relevant split, non-relevant categories, attachments by status and by kind, MediaPlanned and MediaCostUsd.
  - WaveProgress gets RowKind and attachment counters.
  - GenerationEvent gets FileName, Attachments, Projects, Organizations, Files, Relevant, AttachmentsDone, AttachmentsTotal and MediaRemaining.
- **GenerationState**: nine cached ints plus MediaCostUsd. Normalize clamps all of them to 0 or above, and a Phase 5 case.json loads them as 0.
- **ComposedAttachment**: DocId, Kind, FileName, Title, MimeType, `[JsonIgnore] Bytes`, IsInline and ContentId. ComposedEmail adds `Attachments` and an internal `QuoteFragment`. Composition logic is unchanged.

## Tests

- 13 new GenerationSnapshot tests:
  - relevant/non-relevant split, including the previous-Failed correction
  - the Non-relevant row (Index 99) updated like a wave row
  - a seeded Failed attachment moving to Done
  - a repeated failure counted once
  - Skipped to Written
  - Warnings counted in Done and in Warnings
  - a row move (row 1 to row 99)
  - a 1,000-sequence random property test with `new Random(7)`: Done + Failed + Skipped <= Total, no negatives, row sums equal the totals
  - AddCost isolation
  - 8 x 500 parallel RecordAttachment giving 4,000
  - Drain copies
- 5 new GenerationState tests: a Phase 5 JSON loads as 0, negatives clamp, round trip, ComposedEmail defaults, and ComposedAttachment defaults with Bytes left out of the JSON.
- Full suite: 2722 passed, 0 failed (baseline 2705).

## Commits

| Task | Gate | Commit | Message |
| ---- | ---- | ------ | ------- |
| 1 | RED | 3733b3d | test(06-02): add failing tests for per-DocId attachment status and relevant split |
| 1 | GREEN | 8994565 | feat(06-02): per-DocId attachment status in snapshot, stage row kinds and new event kinds |
| 2 | RED | 986a621 | test(06-02): add failing tests for cached Phase 6 counts and composed attachment parts |
| 2 | GREEN | 85b7de4 | feat(06-02): cached non-relevant/attachment counts and composed attachment parts |

## TDD Gate Compliance

Both tasks have a RED test commit followed by a GREEN feat commit. In both cases RED failed at compile time because the new members did not exist yet. No refactor commits were needed.

## Deviations from Plan

None. The plan was executed as written.

## Notes

- `ComposedEmail.Attachments` is a public, serialized property. If ComposedEmail is ever written to JSON, the attachment metadata will be included, but the bytes will not, because Bytes is JsonIgnore (T-06-06 accepted).
- The xUnit1031 analyzer warnings in `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs` were already there and are outside this plan. The app build has 0 warnings.

## Self-Check: PASSED

- FOUND: Models/Evidence/AttachmentRunStatus.cs, Models/Evidence/ComposedAttachment.cs
- FOUND commits: 3733b3d, 8994565, 986a621, 85b7de4
