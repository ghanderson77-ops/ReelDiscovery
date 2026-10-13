---
phase: 06-attachments-non-relevant-corpus
plan: 08
subsystem: evidence-validation-ledger
tags: [validation, attachments, filler, ledger, knowledge-gate, voicemail, determinism]
requires:
  - "06-01 LedgerDelta.FromAttachment / AttachmentDocId, AttachmentTestData.MixedCase"
  - "06-05 internal LedgerMerger.DetailKey / SameDetail, InviteScheduler.MeetingKeyFor"
provides:
  - "AttachmentCheckInput (ParentSeq, Text, VariantText, PeopleNamed, AcceptedThread, NewDetails, LateRepair, LaterEstablished)"
  - "RenderValidator.CheckAttachment(input, ctx): V2/V3/V4/V6/V7, never V1/V5/V8"
  - "RenderValidator.CheckFiller(rendered, ctx): V1/V5 only"
  - "RenderValidator.Check skips V8 for messages carrying a Voicemail attachment"
  - "LedgerMerger.MaxEntriesPerAttachment = 3; attachment entry ids {AttachmentDocId}-Lnn"
affects: [06-17 attachment render/repair loop, 06-xx filler rendering, ledger consumers, InviteScheduler]
tech-stack:
  added: []
  patterns:
    - "Attachment checks reuse the email rules on a one-message RenderedThread, with an overloaded CheckContext that supplies the visible bodies and extra ledger entries"
    - "Two text inputs per attachment: full text for people, knowledge, dates and legal wording; labelled VariantText for V3 and the V6 near-amount scan"
    - "Ledger built in two passes (thread entries, then attachment entries) so attachments never displace thread entries"
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/AttachmentValidatorTests.cs
  modified:
    - Services/Evidence/RenderValidator.cs
    - Services/Evidence/LedgerMerger.cs
    - ReelDiscovery.Tests/Evidence/LedgerMergerTests.cs
decisions:
  - "CheckAttachment treats the parent body as seen text along with its ancestors. The overloaded CheckContext's SeenText returns the thread subject plus the supplied seenBodies, which are already limited to the parent and KnowledgeGate.Ancestors"
  - "The accepted thread's own deltas become ledger entries with id {thread.Id}-own-n, known by the parent's participants; LaterEstablished entries are added only when LateRepair is true"
  - "Attachment NewDetails are copied at ParentSeq, so the caller's deltas keep their own seqs"
  - "Attachment ledger entries are known by the parent message's participants only. Later quoting messages do not carry the parent's attachment, unlike thread entries"
  - "Build pass 2 never lets one attachment entry absorb another attachment entry from the same thread. This matches the EntriesFor rule, so repairing one attachment cannot make a sibling attachment's entry disappear. Attachment entries can still be absorbed by any thread entry, or by an earlier attachment entry from another thread"
  - "Legacy attachment deltas with no AttachmentDocId are grouped under {thread.Id}-A, and their SourceDocId falls back to the parent message DocId"
metrics:
  duration: "~14 min"
  completed: 2026-10-04
  tasks: 2
  files: 4
---

# Phase 6 Plan 08: Attachment and Filler Validation, Stable Attachment Ledger Ids Summary

Relevant attachments are now checked with the same deterministic rules as relevant email, gated to the parent message's ancestry. The checks cover people, canonical variants, knowledge, ledger values and dates, and legal wording. Filler threads get structure checks only. Voicemail messages are exempt from the length check. Ledger details that come from attachments get ids derived from the attachment DocId, which no repair can renumber, displace or absorb.

## What Was Built

### Task 1: RenderValidator entry points (commits 8ba7e6c RED, 2084f70 GREEN)
- `AttachmentCheckInput` holds everything V2-V7 need for one attachment (review concern 4).
- `CheckAttachment`:
  - The visible seqs are `{ParentSeq}` plus `KnowledgeGate.Ancestors(thread, ParentSeq)`.
  - Seen bodies are the accepted-thread bodies at those seqs. The accepted thread's own deltas at those seqs, plus LaterEstablished in late-repair mode, are added to the ledger view.
  - People, knowledge, ledger dates and legal wording run on the full `Text`. V3 and V6 (NewDetails contradictions and the near-amount scan) run on `VariantText`.
  - A late repair that adds any new detail gets the V6 violation "adds a new detail after later emails were written".
  - Every rule runs inside `Run(...)`, and duplicate violations are removed the same way as in `Check`.
- `CheckFiller` runs `CheckStructure` only (D-12 strict).
- `CheckLength` skips planned messages that carry an `AttachmentType.Voicemail` attachment (D-28).
- The dedupe step is now a shared `Distinct` helper. `Check` output is unchanged, and all existing RenderValidatorTests are green.

### Task 2: Stable attachment ledger ids (commits db02644 RED, 4810ba1 GREEN)
- `MaxEntriesPerAttachment = 3`. A private `Partition` splits deltas into thread entries and attachment groups:
  - Thread entries are handled exactly as before.
  - Attachment groups are keyed by AttachmentDocId and processed in ordinal order. Each group is checked for duplicates against the kept thread entries and its own entries, using `SameDetail`.
  - Ids are `{AttachmentDocId}-Lnn`, and `SourceDocId` is the attachment DocId.
- New `AbsorbKnowers`: when an attachment detail duplicates a thread entry, it adds only KnownByIds. The thread entry's FactIds, IssueIds, About, Value and SourceDocId stay as they were, so `InviteScheduler.MeetingKeyFor` gives the same key before and after.
- `Build` now places entries in two passes over the same thread order. The first pass places every thread entry, the second places attachment entries. Both passes test duplicates with `SameDetail` and absorb knowers only.
- The Phase 5 golden test pins the output of a Build with no attachment deltas. The expected output was captured from the code before this change.

## Verification
- `--filter AttachmentValidatorTests|RenderValidatorTests`: 101 passed
- `--filter LedgerMergerTests|InviteSchedulerTests`: 55 passed. No InviteScheduler fixture needed changes.
- `--filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 903 passed
- Full suite: 3104 passed (baseline 3070, plus 34 new tests)
- `dotnet build ReelDiscovery.csproj`: 0 warnings
- All acceptance greps meet their targets. The CheckFiller body has no CheckLength call, and KnowledgeGate.Ancestors appears 3 times.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Build pass 2 does not absorb across attachments of the same thread**
- **Found during:** Task 2
- **Issue:** The plan says pass 2 lets "any earlier entry" absorb an attachment entry. In Build, that would let a repaired attachment A (which sorts before B by DocId) absorb sibling attachment B's existing entry. EntriesFor explicitly forbids this, and must-have truth 7 rules it out.
- **Fix:** Pass 2 skips earlier attachment entries from the same thread when looking for a duplicate. Absorption by thread entries, and by attachment entries from other threads, is unchanged. Late cross-thread absorption is already blocked by the CheckAttachment late-repair rule, which allows no new details after later waves.
- **Files modified:** Services/Evidence/LedgerMerger.cs
- **Commit:** 4810ba1

## Notes for Downstream Plans
- In Build, an attachment entry from an earlier wave that repeats a later wave's thread entry is absorbed into that later entry, because thread entries go first. If an invite was already scheduled from the attachment detail, its meeting key may name a ledger entry that Build no longer stores separately. Late repairs cannot add details, so this only happens when an attachment and a later thread state the same meeting independently.

## Known Stubs
None.

## TDD Gate Compliance
Both tasks have a `test(06-08)` RED commit followed by a `feat(06-08)` GREEN commit. No refactor commits were needed.

## Self-Check: PASSED
- FOUND: Services/Evidence/RenderValidator.cs, Services/Evidence/LedgerMerger.cs, ReelDiscovery.Tests/Evidence/AttachmentValidatorTests.cs, ReelDiscovery.Tests/Evidence/LedgerMergerTests.cs
- FOUND commits: 8ba7e6c, 2084f70, db02644, 4810ba1
