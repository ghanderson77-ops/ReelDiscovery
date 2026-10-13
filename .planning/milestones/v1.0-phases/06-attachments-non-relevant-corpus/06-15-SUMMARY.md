---
phase: 06-attachments-non-relevant-corpus
plan: 15
subsystem: start-over-and-cost-estimate
tags: [start-over, cost-estimate, d-29, d-28, d-21, d-15, out-01, run-02]
requires:
  - "06-01 GenerationJournal.TryListAllFiles (ThreadDone + AttachmentsRepaired files), SideWorld / CastVoices records"
  - "06-04 MediaPrices { ImageCost(), SpeechCost(chars) }"
provides:
  - "GenerationReset.StartOverAsync sweeps planned copies of every thread (relevant and non-relevant)"
  - "GenerationCostEstimator.Estimate(plan, story, volume, prices, mediaAvailable = true, guide = null)"
  - "GenerationCostEstimator.PriceWork(PricedWork, story, volume, prices, mediaAvailable) - the single pricing path for 06-14 EstimateRemaining"
  - "PricedWork { Messages; Attachments; IncludeSideWorld; IncludeVoiceCast; Guide; ForPlan(plan, guide) }"
  - "GenerationCostEstimator.IsRichAttachment(thread, parent, guide) - D-15 rule for AttachmentPrompts.IsRich to delegate to"
  - "CostEstimate.StoryCost / VolumeCost / MediaCost / VoiceCastCost / SideWorldCost / VolumeModelName"
  - "Constants VoicemailCharacters 400, VoiceCastInput/OutputTokens 2_500/400, SideWorldInput/OutputTokens 3_000/1_200"
affects: [06-14 EstimateRemaining, 06-11/06-12 AttachmentPrompts.IsRich, 06-22/06-23 Ready card]
tech-stack:
  added: []
  patterns:
    - "One pricing function over an explicit work list (PricedWork) so plan estimate and remaining-cost estimate cannot drift"
key-files:
  created:
    - ReelDiscovery.Tests/Corpus/GenerationCostEstimatorTests.cs
  modified:
    - Services/Evidence/GenerationReset.cs
    - Services/Corpus/GenerationCostEstimator.cs
    - ReelDiscovery.Tests/Evidence/GenerationResetTests.cs
decisions:
  - "Story tier = relevant threads + Decoy category; every other non-relevant category is Volume tier; Story messages use the Relevant per-message tokens"
  - "Null model fallback is symmetric: story ?? volume and volume ?? story; only both null gives a zero estimate"
  - "PricedWork.ForPlan sets IncludeSideWorld when any non-relevant thread (decoys included) has messages, matching 06-19 which builds the side world before any non-relevant thread"
  - "On the Volume tier every Office attachment and invite is small (IsRichAttachment is false for non-relevant threads); decoy Office attachments are small on Story"
  - "Side-world cost is not gated by mediaAvailable; voice-cast cost is (no voicemail is spoken without media)"
metrics:
  duration: 25min
  completed: 2026-10-04
  tasks: 2
  files: 4
---

# Phase 6 Plan 15: Start Over for All Mail and D-29 Tiered Estimate Summary

Start Over now deletes the planned custodian copies of every thread, non-relevant ones included. The plan estimate is split into a Story part (relevant threads and decoys), a Volume part (other filler, the side-world call and the voice-cast call) and a media part (images and voicemail, priced per unit). The estimate and the remaining-cost line both go through one function, `PriceWork`.

## What Was Built

**Task 1: Start Over for every thread (OUT-01, Pitfall 13)**
- `GenerationReset.DeleteAll` no longer filters on `t.Relevance == ThreadRelevance.Relevant`. Every planned message adds its `MailboxWriter.CopyPathsFor` paths. Every candidate still has to pass `ResolveUnderCase` / `IsSafeEmlPath` first (T-06-44).
- There are no new deletion targets. The side world and the cast voices are journal records, so deleting the journal removes them too. After Start Over, a fresh `Replay` has `LastSideWorld` and `LastCastVoices` null.
- New tests:
  - non-relevant orphan copies are deleted
  - the deleted count covers relevant and non-relevant files together
  - unsafe planned paths (traversal) are never deleted
  - files listed on `AttachmentsRepaired` records are deleted
  - SideWorld and CastVoices records go with the journal

**Task 2: Tiered, media-aware estimate (D-29, D-28, D-21, D-15, D-12)**
- New overload `Estimate(plan, story, volume, prices, mediaAvailable = true, guide = null)`, implemented as `PriceWork(PricedWork.ForPlan(plan, guide), ...)`.
- Office attachment pricing:
  - A rich Office attachment (`IsRichAttachment`: relevant thread and either a hot parent or a Hot/Key fact on the parent or thread) uses document tokens.
  - All other Office attachments and all invites use small-attachment tokens on the parent's tier.
  - Images and voicemail are never priced as text attachments.
- Media costs `images x ImageCost + voicemails x SpeechCost(400)` and has no QaOverhead. With `mediaAvailable` false, both media and voice-cast cost are 0.
- One side-world call is added when the plan has non-relevant threads. One voice-cast call is added when it has a voicemail. Both are Volume-tier calls with QaOverhead and are included in VolumeCost and Total.
- Filler gets no length-retry multiplier (D-12 strict).
- The Phase 5 `Estimate(plan, storyModel)` overload is unchanged. A test pins its values, and the existing PlanSummaryTests still pass.

## Verification

- `dotnet test --filter GenerationResetTests|GenerationCostEstimatorTests|PlanSummaryTests`: 39/39 passed
- Full suite: **3070 passed, 0 failed, 0 skipped** (6 m 31 s). This was the wave-2 post-wave gate. `StartOver_LockedFile_IsReported_RestStillDeleted` passed and was not flaky this run.
- `dotnet build ReelDiscovery.csproj`: 0 warnings
- All acceptance greps match. The relevant-only filter count is 0, and `MailboxWriter.CopyPathsFor` appears once in GenerationReset.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 5f57c25 | test(06-15): add failing Start Over tests for non-relevant orphans and journal-only records |
| 1 GREEN | c4c5e93 | feat(06-15): Start Over deletes planned copies of every thread |
| 2 RED | 44c34b5 | test(06-15): add failing tests for tiered and media-aware plan estimate |
| 2 GREEN | 8bed68a | feat(06-15): tiered and media-aware plan estimate with one pricing path |

## Deviations from Plan

None. The plan was executed as written. Two clarifications:
- The plan behavior "doubling every filler message's planned length band" does not apply directly, because length bands live in render assignments and not in `PlannedMessage`. The test inflates filler Intent/MustConvey instead and asserts that OtherCost does not change.
- `PricedWork` members are auto-properties rather than public fields (CLAUDE.md: models are mutable classes with auto-properties).

## TDD Gate Compliance

Both tasks have a `test(...)` RED commit followed by a `feat(...)` GREEN commit. No refactor commits were needed.

## Known Stubs

None. `AttachmentPrompts.IsRich` does not exist yet. The later attachment-prompt plan should delegate it to `GenerationCostEstimator.IsRichAttachment`.

## Self-Check: PASSED
- FOUND: Services/Evidence/GenerationReset.cs, Services/Corpus/GenerationCostEstimator.cs, ReelDiscovery.Tests/Evidence/GenerationResetTests.cs, ReelDiscovery.Tests/Corpus/GenerationCostEstimatorTests.cs
- FOUND commits: 5f57c25, c4c5e93, 44c34b5, 8bed68a
