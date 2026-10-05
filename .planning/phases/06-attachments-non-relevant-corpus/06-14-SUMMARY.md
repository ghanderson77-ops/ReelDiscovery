---
phase: 06-attachments-non-relevant-corpus
plan: 14
subsystem: run-state-and-answer-key
tags: [run-state, answer-key, d-05, d-13, d-18, d-20, d-26, d-29, out-01, run-02, noise-05, att-04]
requires:
  - "06-01 JournalReplay.LastByAttachment (identity-validated), SpentCostUsd, MediaSpentCostUsd, LatestAttemptEnded, LastSideWorld, LastCastVoices"
  - "06-02 AttachmentRunStatus, AttachmentSeed, GenerationCounts / GenerationState Phase 6 fields"
  - "06-05 AttachmentNaming.NamesFor, AttachmentChainLink, InviteSlot"
  - "06-06 FillerAssignments.Compute"
  - "06-09 VoicemailComposer.IsVoicemail"
  - "06-15 GenerationCostEstimator.PriceWork / PricedWork"
provides:
  - "GenerationRunState over every generation thread: Relevant*/NonRelevant*/Attachments* counts, StatusByAttachment, AttachmentRecords, FillerTitlesByDocId, InviteSlotsByMeeting, HasSideWorld, HasCastVoices, LatestAttemptEnded, MediaCostUsd"
  - "DeltasByThread = thread deltas + committed non-late attachment deltas in plan order (FromAttachment, AttachmentDocId)"
  - "RepairGroups(plan, mediaAvailable) -> AttachmentRepairGroup { ThreadId; Relevant; WaveIndex; AttachmentDocIds; ParentDocIds }, AttachmentsToRepair"
  - "AttachmentSeeds(plan, waveOf, nonRelevantRow, repairRow, mediaAvailable)"
  - "EstimateRemaining(plan, state, story, volume, prices, mediaAvailable, guide = null) via GenerationCostEstimator.PriceWork"
  - "GenerationRunState.AllAssignments(plan); BuildOverlay(plan, assignments, chains = null)"
  - "AnswerKeyOverlayEntry.FileName / From / Note; attachment rows read status, final name and note from the overlay"
affects: [06-19 runner wiring, 06-20 repair runner, 06-23 step entry/banners, 06-24]
tech-stack:
  added: []
  patterns:
    - "Attachment status only from committed records of Done threads (replay ownership + Done check)"
    - "Remaining cost = PriceWork over an explicit pending-work list (same function as the Ready card)"
key-files:
  created: []
  modified:
    - Services/Evidence/GenerationRunState.cs
    - Services/Corpus/AnswerKeyBuilder.cs
    - Services/Evidence/StorySoFarDocxBuilder.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunStateTests.cs
    - ReelDiscovery.Tests/Evidence/AnswerKeyOverlayTests.cs
decisions:
  - "TotalThreads/Written/Warnings/Failed/Pending count every generation thread; GenerationState.ThreadsTotal..ThreadsFailed stay relevant-only through the applier"
  - "InviteSlotsByMeeting: the earliest planned committed invite (parent SentAt, then DocId) owns a meeting key's schedule"
  - "CostUsd = max(sum of last thread records, replay.SpentCostUsd): equals SpentCostUsd for every real journal, keeps hand-built test replays meaningful"
  - "EstimateRemaining takes an optional CaseGuide so remaining and Ready-card prices agree on attachment richness when the step passes Draft.Guide"
  - "Voicemail rationale note comes from the journaled code-written subject 'Voicemail from {Caller} ({m:ss})'; a Phase 5 voicemail record (empty From) gets no note and keeps the planned caller From"
  - "Interim until 06-19: StepGenerateCorpus.ShowRunResult decides partial/finished from relevant counts, because the runner does not write filler yet"
metrics:
  duration: 50min
  completed: 2026-10-04
  tasks: 2
  files: 6
---

# Phase 6 Plan 14: Whole-Corpus Run State and Answer-Key Overlay Summary

The run state and the answer key now project plan + journal for the whole corpus. That covers relevant and non-relevant threads and every planned attachment. Attachment status comes only from committed records of written threads. The state also provides chronological repair groups, persisted invite schedules, full media spend and a remaining-cost estimate that uses the Ready card's pricing function. Key rows use the EML's final part names and the voicemail senders and subjects recorded in the journal.

## What was built

**Task 1 (GenerationRunState):**
- `From` now covers relevant generatable threads plus non-relevant threads that have an id and messages (first copy per id).
- It tracks all-thread totals plus the Relevant/NonRelevant split, and every planned attachment in `StatusByAttachment`.
- `AttachmentRecords` keeps only records from `replay.LastByAttachment` whose thread is Done (review concern 1).
- `DeltasByThread` covers relevant done threads only (D-13). It holds the thread's own deltas, then committed Written/Warnings attachment deltas in plan attachment order, each tagged with `FromAttachment` and `AttachmentDocId`. LateRepair deltas are excluded (D-18, review concerns 5 and 7).
- New fields: `FillerTitlesByDocId`, `InviteSlotsByMeeting`, `HasSideWorld`, `HasCastVoices`, `LatestAttemptEnded`. `MediaCostUsd` comes from `MediaSpentCostUsd`.
- `RepairGroups` lists the attachments of done threads that are missing or failed. Skipped attachments are included only when media is available. Groups are ordered by wave, then first SentAt, then id, with filler groups last. `AttachmentsToRepair` and `AttachmentSeeds` use the same rule.
- `ToCounts` fills every Phase 6 `GenerationCounts` field: category totals, attachment kinds and MediaPlanned.
- The new `EstimateRemaining` overload builds a `PricedWork` and calls `GenerationCostEstimator.PriceWork`. The work list holds pending messages, pending and repair attachments, the side world (when filler is pending and no SideWorld record exists) and the voice cast (when a voicemail is pending, media is available and no CastVoices record exists).
- `GenerationStateApplier.Apply` caches the NonRelevant*, Attachments* and MediaCostUsd counts. ThreadsTotal and the other relevant thread counts stay relevant-only.

**Task 2 (answer key):**
- `AnswerKeyOverlayEntry` gains `FileName`, `From` and `Note`.
- Email rows apply `From` and append `Note`. `AttachmentRow` takes its overlay entry, so Status is no longer hard-coded to Planned.
- `BuildOverlay(plan, assignments, chains = null)` covers every generation thread.
- `AllAssignments` combines render and filler assignments, so non-relevant subjects carry the filler privilege markings.
- Voicemail messages of done threads use the journaled Subject. They use the journaled From only when it is non-empty (a Phase 5 record keeps the caller From) and add the note "Voicemail from {Caller}."
- Attachment rows map Pending/Written/Warnings/Failed/Skipped to the key statuses. Skipped rows get the note "Skipped: {reason}".
- File name: the record's FileName when it has one. Otherwise, for relevant or done threads, the `AttachmentNaming.NamesFor` final name. A pending filler attachment keeps its planned name.

## Verification

- `dotnet test --filter "FullyQualifiedName~GenerationRunStateTests"`: 38 passed
- `dotnet test --filter "...AnswerKeyOverlayTests|...GenerationRunStateTests|...AnswerKey"`: 125 passed
- Evidence suite (Category!=Render): 992 passed
- Full suite: 3202 passed (baseline 3180 + 22 new)
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- All acceptance greps for both tasks match

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] The Generate Corpus step showed a partial run after a full relevant run**
- **Found during:** Task 1 (Evidence suite)
- **Issue:** `TotalThreads` and `Pending` now include non-relevant threads, but the runner only writes filler from 06-19 onward. `ShowRunResult` therefore always showed the Continue banner. Two tests failed: `GenerateCorpusStepTests.GenerateEmails_RunsToCompletion...` and `Cancel_KeepsFinishedThreads...`.
- **Fix:** As an interim measure, `ShowRunResult` decides partial/finished from relevant counts. A comment marks this until 06-19 writes filler. 06-23 rewrites this method.
- **Files modified:** UserControls/StepGenerateCorpus.Run.cs
- **Commit:** 882c131

**2. [Rule 1 - Bug] The Story So Far document would count filler threads as ledger sources**
- **Found during:** Task 1
- **Issue:** `StorySoFarDocxBuilder` printed "from N generated threads" using `Written + Warnings`. Those counts now include non-relevant threads, but the ledger is built from relevant threads only (D-13).
- **Fix:** It now uses `RelevantWritten + RelevantWarnings`.
- **Files modified:** Services/Evidence/StorySoFarDocxBuilder.cs
- **Commit:** 882c131

**3. [Rule 2 - Consistency] Optional guide on the new EstimateRemaining overload**
- **Issue:** The Ready card (06-23) prices with `Draft?.Guide`, which affects attachment richness (D-15). The planned 6-argument overload had no guide, so the two estimates could disagree.
- **Fix:** Added a trailing `CaseGuide? guide = null` parameter. The 6-argument call shape from the plan still compiles.
- **Commit:** 882c131

**4. Test fixture voicemail sender values:** As the plan specifies, the tests use "Dunder Mifflin Voice Mail" and "Michael Scott" as journal values. These are fiction names, which the project keeps as-is.

Existing tests changed to match the whole-corpus semantics: `From_ProjectsStatusesAndCounts`, `RelevantThreadWithoutMessages...`, `ToCounts_FillsGenerationCounts` (EmailsPlanned now covers all generation threads), `Apply_NotFinal...` (ThreadsTotal is RelevantTotal), and `BuildOverlay_MapsStatusesAndComposerSubjects` (pending non-relevant emails are now overlaid as Planned).

## Notes for later plans

- `GenerationRunner` still calls the 2-argument `BuildOverlay` with `RenderAssignments.Compute`, and the 3-argument `EstimateRemaining`. 06-19 switches these to `AllAssignments`, chains and the PriceWork overload.
- `StepGenerateCorpus.Run.cs` still uses `RenderAssignments.Compute(plan)` for its overlay calls, plus the interim relevant-only partial check in `ShowRunResult`. Both are for 06-23 to replace.

## TDD Gate Compliance

- RED: d99a7b6 (Task 1), 4bd0a35 (Task 2). Both failed at compile time against missing members.
- GREEN: 882c131 (Task 1), 6aa50e1 (Task 2).

## Self-Check: PASSED

- FOUND: Services/Evidence/GenerationRunState.cs, Services/Corpus/AnswerKeyBuilder.cs, both test files
- FOUND commits: d99a7b6, 882c131, 4bd0a35, 6aa50e1
