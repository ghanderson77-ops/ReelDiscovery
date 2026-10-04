---
phase: 06-attachments-non-relevant-corpus
plan: 05
subsystem: attachment-projections
tags: [attachments, version-chains, file-names, content-id, calendar-invite, tts, determinism]
requires:
  - "06-01 AttachmentTestData.MixedCase / SiteWalkMeeting, CastVoiceSet"
provides:
  - "AttachmentChains.Compute(plan, guide) -> Dictionary<DocId, AttachmentChainLink> (organization-scoped, BaseTitle)"
  - "AttachmentNaming.TitleFor / NamesFor / ContentIdFor (one final name for EML, journal and key)"
  - "InviteScheduler.Resolve / ForNonRelevant / MeetingKeyFor / SlotSeedFor / MeetingDate / MeetingTime, InviteRequest, InviteSlot, InviteSource"
  - "VoiceAssignments.For / Fallback"
  - "internal LedgerMerger.DetailKey / SameDetail / TryParseDate"
affects: [06-08 attachment ledger deltas, 06-09+ attachment rendering / MailboxWriter / answer key overlay]
tech-stack:
  added: []
  patterns: ["meeting identity keyed through the ledger's own normalization (EntriesFor + SameDetail), never a raw delta", "content slot seed so same-wave duplicates share a derived time"]
key-files:
  created:
    - Services/Evidence/AttachmentChains.cs
    - Services/Evidence/AttachmentNaming.cs
    - Services/Evidence/InviteScheduler.cs
    - Services/Evidence/VoiceAssignments.cs
    - ReelDiscovery.Tests/Evidence/AttachmentChainsTests.cs
    - ReelDiscovery.Tests/Evidence/AttachmentNamingTests.cs
    - ReelDiscovery.Tests/Evidence/InviteSchedulerTests.cs
  modified:
    - Services/Evidence/LedgerMerger.cs
decisions:
  - "Final attachment names keep RelevantThreadFiller.FileNameFor's sanitizing (spaces become underscores, e.g. CO-007_Pricing_Backup_v2.xlsx), matching every approved plan FileName; the plan's space-separated example names were illustrative"
  - "AttachmentChainLink carries BaseTitle (version 1's title without version tokens) so every version of a chain is named from one stem"
  - "Untitled attachments fall back to '{noun} {local date}' with nouns Notes/Worksheet/Slides/Photo/Meeting/Voicemail (never the 'Document N' shape)"
  - "ContentIdFor hashes 'cid:' + DocId with StableRank (16 hex) at the sender domain filtered to [a-z0-9.-]"
  - "Own-delta, ledger and fact invite candidates all apply the topic check; fact candidates come only from thread/parent FactIds and use no stated time"
  - "Derived invite times use the invite parent's time zone; invites for one meeting from different zones agree once the first schedule is persisted in KnownSlots"
metrics:
  duration: "~18 min"
  completed: 2026-10-04
  tasks: 2
  files: 8
---

# Phase 6 Plan 05: Attachment Chains, Naming, Invite Scheduling and Voices Summary

Four pure static projections turn plan + journal into everything an attachment needs in the EML: version chains scoped by sender organization, one safe final file name and Content-ID per attachment, invite schedules keyed by a single canonical meeting identity shared with the ledger, and the voicemail TTS voice (persisted cast voice first).

## What was built

**Task 1: AttachmentChains, AttachmentNaming, VoiceAssignments** (RED f3e853f, GREEN aac7973)
- `AttachmentChains.Compute(plan, guide)`: relevant Word/Excel/PowerPoint only. The group key is (organization key, kind, `NormalizeStem(title)`). The organization key is the cast OrganizationId, `ext:{domain}` for externals, `unknown` for unresolved senders, or `from:{FromId}` when there is no guide. Groups of 2+ are ordered by parent SentAt (UTC) then DocId. Version, Count, PreviousDocId and BaseTitle are pure functions of the plan, and reversing the thread order gives the same result.
- `NormalizeStem` removes v2, version 3, final, draft, rev 2, revised, updated and copy, plus ISO and US dates and punctuation (all via `[GeneratedRegex]`).
- `AttachmentNaming.NamesFor(message, fillerTitles, chains)`: names keyed by DocId in plan order.
  - Chain members are named BaseTitle plus `_v{n}` (for n > 1). Voicemails are named `Voicemail yyyy-MM-dd h-mm tt` in the sender's local time.
  - Anything else uses `TitleFor`: plan Title, then filler title, then the first 8 words of Description, then noun + date.
  - Every name goes through `RelevantThreadFiller.FileNameFor`, then a `..` collapse and `IsSafeFileName` guard, then " (2)"/" (3)" dedupe (case-insensitive). The D-02 narrow exception is documented in the class summary.
- `ContentIdFor(docId, domain)` returns `{16 hex}@{clean domain}`, or `EmailComposer.FallbackDomain` when the domain is blank.
- `VoiceAssignments.For(personId, cast)` returns the persisted CastVoiceSet voice when it is one of the six allowed voices. Otherwise it uses `Fallback` (StableRank of `{id}:voice` over the ordinal-sorted voice list).

**Task 2: InviteScheduler with meeting identity** (RED ce51f7a, GREEN 8bed2e5)
- LedgerMerger: `DetailKey`, `SameDetail` and `TryParseDate` changed from private to internal, with summaries added. The bodies are unchanged and LedgerMergerTests stay green.
- Own-delta candidates follow checker 06-05:
  - Only Meeting deltas on the parent seq or its ancestors count.
  - Each delta is normalized by `LedgerMerger.EntriesFor(thread, [d])`, then resolved to the in-thread entry `EntriesFor(thread, all deltas)` keeps (`SameDetail`, thread-message or same SourceDocId).
  - It is then resolved to the earliest visible ledger entry Build would absorb it into (WaveIndex, then Id).
  - The date, stated time, key and seed all come from that effective entry.
- Ledger candidates need KnownByIds to intersect the invite participants. Fact candidates come from thread/parent FactIds with intersecting Actor/KnownBy ids (or none). Every candidate must be topic-compatible (a shared fact id, or a shared 4+ letter non-stopword).
- `MeetingKeyFor(LedgerEntry, date, guide)` has a single code path. It returns `fact:{id}` for the ordinal-first cited fact dated on the meeting day, otherwise `mtg:{SourceThreadId}:{SourceDocId}:{DetailKey}`. Candidates are deduplicated by key, keeping the first in source order.
- `MeetingDate` tries `LedgerMerger.TryParseDate(Value)`, then the first ISO date in Value, then in Statement. It never reads LedgerEntry.Date. `MeetingTime` reads `h:mm tt` / `HH:mm`.
- Schedule:
  - KnownSlots[key] wins when present. Otherwise the start is the stated time, or 09:00 + 30·(rank % 14) minutes. The duration is 30 or 60 minutes from the same rank, where rank = StableRank(SlotSeedFor + ":invite-slot").
  - The seed is the fact key for `fact:` keys and `mtg~{DetailKey}|{date}` for `mtg:` keys.
  - A candidate is rejected when its start is at or before parent local time + 15 minutes. Ordering is earliest date, then OwnDelta < Ledger < Fact, then key.
- Fallback: a StableRank(invite DocId) slot on the next business day, with warning "invite not tied to a known meeting". `ForNonRelevant` uses the same slot without a warning.
- All cross-source tests build their ledger views with `LedgerMerger.Build`. The parity oracle covers 17 own-delta cases (MixedCase plus adversarial fixtures).

## Verification

- `dotnet test --filter "AttachmentChainsTests|AttachmentNamingTests"`: 30 passed.
- `dotnet test --filter "InviteSchedulerTests|LedgerMergerTests"`: 45 passed.
- Full suite: 2873 passed, 0 failed (baseline 2808 + 65 new).
- Main project build: 0 warnings.
- All acceptance greps for both tasks match the expected counts.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Expected file names in the plan's behavior assumed spaces survive FileNameFor**
- **Found during:** Task 1
- **Issue:** The plan requires names to go through `RelevantThreadFiller.FileNameFor`, but `FileNameHelper.SanitizeForFileName` replaces spaces with underscores. The plan's literal examples ("CO-007 Pricing Backup_v2.xlsx", "Q3 Supplier Review.docx", "Voicemail 2024-03-05 2-07 PM.mp3") therefore can't be produced. Changing the sanitizer would change every approved relevant plan FileName.
- **Fix:** Kept FileNameFor unchanged. Tests assert the sanitized forms (`CO-007_Pricing_Backup_v2.xlsx`, `Q3_Supplier_Review.docx`, `Voicemail_2024-03-05_2-07_PM.mp3`) and that they equal `FileNameFor(...)`. Dedupe suffixes keep the plan's " (2)" form.
- **Files modified:** ReelDiscovery.Tests/Evidence/AttachmentNamingTests.cs
- **Commit:** f3e853f

**2. [Rule 2 - Missing functionality] AttachmentChainLink.BaseTitle**
- **Found during:** Task 1
- **Issue:** `NamesFor` gets only the message and the chain map. Without the chain's base title, each version would be named from its own title, and versions whose titles differ beyond version tokens would drift apart.
- **Fix:** Added `BaseTitle` (version 1's `ChainTitle`) to the link. All versions are named from it.
- **Commit:** aac7973

## TDD Gate Compliance

- Task 1: RED test commit f3e853f (compile failure: types missing), then GREEN feat commit aac7973.
- Task 2: RED test commit ce51f7a. Before committing, the implementation draft was moved out of the project and the test build was confirmed to fail on the missing types. The implementation was then restored and committed in GREEN 8bed2e5. No refactor commits.

## Known Stubs

None. `OwnDelta_AttachmentDelta_KeyedByTheEntrySourceDoc` asserts the key follows whatever SourceDocId `EntriesFor` assigns. That is the message DocId today and becomes the AttachmentDocId when 06-08 lands, with no scheduler change needed.

## Threat Flags

None. All threat-register mitigations are implemented:
- T-06-14: FileNameFor plus the `..` collapse and IsSafeFileName guard.
- T-06-15: hex Content-ID with an allowlisted domain.
- T-06-16: TryParseExact only; malformed candidates are skipped.
- T-06-16b: canonical key, content seed and KnownSlots.

T-06-16c (accepted residual) is documented in the InviteScheduler summary.

## Self-Check: PASSED

- FOUND: Services/Evidence/AttachmentChains.cs, AttachmentNaming.cs, InviteScheduler.cs, VoiceAssignments.cs
- FOUND: ReelDiscovery.Tests/Evidence/AttachmentChainsTests.cs, AttachmentNamingTests.cs, InviteSchedulerTests.cs
- FOUND commits: f3e853f, aac7973, ce51f7a, 8bed2e5
