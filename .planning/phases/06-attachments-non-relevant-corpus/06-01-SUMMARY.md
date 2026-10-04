---
phase: 06-attachments-non-relevant-corpus
plan: 01
subsystem: evidence-journal
tags: [journal, replay, attachments, side-world, cast-voices, fixture]
requires: []
provides:
  - "JournalRecordType AttachmentDone/AttachmentFailed/AttachmentSkipped/SideWorld/CastVoices/AttachmentsRepaired"
  - "JournalRecord nested Attachments commit group + attachment identity fields + invite schedule"
  - "SideWorld / CastVoiceSet models with capped Normalize"
  - "JournalReplay.LastByAttachment, MediaSpentCostUsd, LastSideWorld, LastCastVoices, LatestAttemptEnded"
  - "AttachmentTestData.MixedCase() shared Wave 0 fixture"
affects: [06-02..06-25 (all later Phase 6 plans read these contracts)]
tech-stack:
  added: []
  patterns:
    - "Attachment outcomes nested inside the thread's own journal line (one flush = one commit group)"
    - "Replay ownership map (attachment DocId -> thread + parent DocId); spend always counted, state only when owned"
key-files:
  created:
    - Models/Evidence/SideWorld.cs
    - ReelDiscovery.Tests/Evidence/AttachmentTestData.cs
  modified:
    - Models/Evidence/JournalRecord.cs
    - Models/Evidence/LedgerDelta.cs
    - Models/Evidence/RenderedThread.cs
    - Services/Evidence/GenerationJournal.cs
    - ReelDiscovery.Tests/Evidence/GenerationJournalTests.cs
decisions:
  - "Nested attachment records are flattened to one level and capped at 32; non-attachment types and blank DocIds are dropped by Normalize"
  - "AttachmentsRepaired on a thread that is not Done at that point counts spend only; its outcomes are ignored"
  - "A fresh ThreadDone removes every LastByAttachment entry the plan assigns to that thread before applying its own list"
  - "LatestAttemptEnded compares read-order line indexes of the newest RunStarted and RunEnded; legacy RunEnded without RunStarted counts as ended"
  - "IO failure during replay also clears LastRunEnded (so LatestAttemptEnded is false on an unreadable journal)"
metrics:
  duration: "~27 min"
  completed: "2026-10-04"
  tasks: 3
  files: 7
---

# Phase 6 Plan 01: Journal Contracts for Attachments and Non-Relevant Mail Summary

The journal now stores each thread's attachment outcomes inside that thread's ThreadDone line (one flush, so they commit together). Replay checks each outcome's identity against the plan before using it. Every attempt's text and media spend is counted, the side world and per-cast tts-1 voices are persisted, replay can tell whether the latest run attempt ended, and later plans share one adversarial mixed-corpus fixture.

## What was built

**Task 1 – record contracts** (`Models/Evidence/JournalRecord.cs`, `LedgerDelta.cs`, `RenderedThread.cs`, `SideWorld.cs`)
- New enum values are appended at the end of `JournalRecordType` only, so older builds skip lines they do not know.
- `JournalRecord` gains the attachment identity fields (`AttachmentDocId`, `ParentDocId`, `AttachmentKind`), plus `FileName`, `Title`, `FlattenedText`, `MediaCostUsd`, `LateRepair`, `InviteMeetingKey`/`InviteStart`/`InviteEnd`, the nested `Attachments` commit group, `SideWorld` and `CastVoices`.
- `Normalize` caps text at 20,000 chars, 8 titles x 120 chars, 32 nested records and 120-char meeting keys. It also:
  - clears unsafe file names and strips control characters from titles;
  - resets an undefined kind to Word and clamps negative media cost;
  - clears an invite schedule whose end is not after its start;
  - flattens nested records to one level;
  - sets `LedgerDelta.FromAttachment` whenever `AttachmentDocId` is set.
- `JournalMessage` gains `From` and `AttachmentTitles`; `RenderedMessage` gains `AttachmentTitles`.
- `SideWorld` allows up to 24 organizations, 4 projects each and 4 topics per project, with names clipped to 80 chars. `CastVoiceSet` keeps only the six tts-1 voices, lower-cased, first entry per person, at most 200; `VoiceFor` looks a voice up.

**Task 2 – replay** (`Services/Evidence/GenerationJournal.cs`)
- An ownership map (attachment DocId -> owning thread and parent message) is built from the plan. `ApplyAttachment` always adds text and media spend, but stores state only when the thread and parent match the plan.
- **ThreadDone:** resets that thread's attachment state, then applies its nested records.
- **ThreadFailed:** adds media spend.
- **AttachmentsRepaired:** applied only on top of a Done thread; otherwise only its spend counts.
- **Standalone attachment lines:** validated the same way.
- **SideWorld / CastVoices:** last record wins, cost counted.
- `LastRunStartedLine` / `LastRunEndedLine` back `LatestAttemptEnded`.
- The IO-failure catch clears all the new state.
- `TryListAllFiles` also lists the copies an AttachmentsRepaired line rewrote.

**Task 3 – fixture** (`ReelDiscovery.Tests/Evidence/AttachmentTestData.cs`)
- `MixedCase()` adds T-0901..T-0915 to `EvidenceTestData.ApprovedCase()`:
  - a hot Word/Excel/PowerPoint root;
  - a same-organization v2 chain, plus a same-title Excel from another organization;
  - a multi-message attachment thread and a sibling-branch thread;
  - two invites for one meeting, one with an inline image;
  - relevant and non-relevant voicemails;
  - a privileged daily-business thread whose Word file has code-default name and title;
  - an ancillary thread, and newsletter, spam and personal mail from EXT-901..903 (inline images on newsletter and spam);
  - a decoy whose MustNotMention holds a fact title and its amount.
- DocIds are `RD-900001..` in SentAt order, with PlanNumbering-format attachment ids, paths and custodians.
- `AttachmentsOf` and `SiteWalkMeeting` are provided; the meeting's `Value` is 7 days after its `Date`, so a test can tell the scheduler reads `Value`.
- `AttachmentTestDataTests` (6 tests) cover:
  - every attachment kind and every category;
  - the cross-organization title collision;
  - the adversarial shapes;
  - ids and formats, and every cast participant being active on the send date;
  - replay ownership on the fixture;
  - surviving `Normalize`.

## Verification

- `GenerationJournalTests`: 43 pass (18 existing + 25 new).
- `AttachmentTestDataTests`: 6 pass.
- Full suite: **2705 passed, 0 failed** (baseline 2673).
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixture used a cast member outside his active window**
- **Found during:** Task 3
- **Issue:** The multi-message thread (2024-04-23) first used CH-009, who is only active from 2024-05-01 to 2024-06-15 in RichDraft.
- **Fix:** The thread now uses CH-001 and CH-005. A test now asserts that every cast participant is active on the local send date.
- **Commit:** 88e8eb7

**2. [Rule 2 - Correctness] Replay IO catch also clears LastRunEnded**
- **Found during:** Task 2
- **Issue:** Without this, an unreadable journal could leave `LatestAttemptEnded` reporting a stale value.
- **Fix:** `LastRunEnded` is cleared along with the other markers.
- **Commit:** 60f6090

Task 2 tests use an inline plan in `GenerationJournalTests` (the plan allowed this), and `AttachmentTestDataTests` adds one replay test on `MixedCase()`.

## TDD Gate Compliance

- Task 1: RED aa8b939, then GREEN 2063ed3.
- Task 2: RED acb4926, then GREEN 60f6090.
- Task 3 is a fixture task (type auto, not TDD); its self-tests are committed with it in 88e8eb7.

## Known Stubs

None. The new fields are contracts that later Phase 6 plans will populate.

## Self-Check: PASSED
