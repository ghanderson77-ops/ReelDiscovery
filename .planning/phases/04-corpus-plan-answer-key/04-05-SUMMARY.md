---
phase: 04-corpus-plan-answer-key
plan: 05
subsystem: corpus-answer-key
tags: [docids, custodians, planned-paths, answer-key, family-propagation]
requires:
  - Models/Corpus/CorpusPlan.cs, PlannedThread.cs, PlannedMessage.cs, PlannedAttachment.cs, ExternalParty.cs, AnswerKeyRow.cs (04-01)
  - Models/Corpus/CorpusSettings.cs (SanitizePrefix, DocIdPrefix)
  - Helpers/ThreadingHelper.cs (AddReplyPrefix / AddForwardPrefix)
provides:
  - DocIdPrefix.Derive(CaseDraft) / DocIdPrefix.Effective(CorpusSettings, CaseDraft) / DocIdPrefix.Fallback ("RD")
  - PlanNumbering.Assign(CorpusPlan, CaseGuide) + CustodianFolder(CastMember, IReadOnlyDictionary<string,string>) + MailboxesFolder/SentFolder/InboxFolder/UnassignedFolder
  - AnswerKeyBuilder.Build(CorpusPlan, CaseGuide) -> List<AnswerKeyRow>, CategoryLabel(NonRelevantCategory), StatusPlanned
affects:
  - 04-06 CSV/XLSX writers (serialize these rows), planner pipeline (calls Assign after scheduling), Phase 5 renderer (stamps these DocIDs, writes to these paths)
tech-stack:
  added: []
  patterns:
    - Pure static projection (no I/O, no guide mutation) mirroring CaseGuideDocxExporter
    - Locally formatted ids (T-0001, T-0001-M01, HB-000001, HB-000001.0001) never via guide id counters
key-files:
  created:
    - Services/Corpus/DocIdPrefix.cs
    - Services/Corpus/PlanNumbering.cs
    - Services/Corpus/AnswerKeyBuilder.cs
    - ReelDiscovery.Tests/Corpus/PlanNumberingTests.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyBuilderTests.cs
  modified: []
decisions:
  - "[04-05]: DocID prefix words drop non-ASCII letters per word before taking initials (Olwerk Eclair Bakery -> LCB); a single significant word yields its first two letters; the memo organization is matched to a CaseOrganization by name for its ShortName"
  - "[04-05]: Custodian folders are assigned in guide cast order (first wins on a shared local part; later members get -{CH id}); reserved device names also get -{CH id}; a message with no cast participant goes to Mailboxes/Unassigned/Inbox"
  - "[04-05]: A relevant thread's root is always Own (thread codes if it names no facts); Own issue codes are the issue ids of the message's facts, falling back to the thread's issues"
  - "[04-05]: Attachment rows copy the parent's path, date, people, subject, flags and category; relevant attachments carry the thread's issue/fact ids; non-relevant attachments read 'Attachment to non-relevant email {DocID}; family coded Not Relevant.'"
  - "[04-05]: Hot is Y only on IsHot emails of relevant threads and their attachments"
metrics:
  duration: 8min
  completed: 2026-10-02
  tasks: 2
  files: 5
---

# Phase 4 Plan 05: DocIDs, Custodians and Answer Key Rows Summary

Every planned document now gets its identity in code. Thread ids and DocIDs run in time order (`HB-000001..`, with attachments as `.0001`). The prefix comes from the producing party, and the user's override wins. Each message gets an owner custodian, the list of every mailbox that holds a copy, and a sanitized, opaque path `Mailboxes/{first.last}/{Sent Items|Inbox}/{DocID}.eml`. A pure `AnswerKeyBuilder` turns the plan into 23-column rows. Family relevance passes to quoted replies and attachments.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | DocIdPrefix + PlanNumbering (TDD) | 0ad802d (RED), 6937c91 (GREEN), 1ee267e (comment reword for acceptance grep) |
| 2 | AnswerKeyBuilder with family propagation (TDD) | f8c284e (RED), 23c91bc (GREEN) |

## Verification

- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 209 passed, 0 failed (18 PlanNumbering + 17 AnswerKeyBuilder new)
- Acceptance greps: Assign signature = 1, IdAllocator = 0 in both files, "RD" present, Build signature = 1, "family coded Relevant" = 1, ThreadingHelper prefixes = 2, `yyyy-MM-dd HH:mm zzz` = 1
- Assign is idempotent and leaves `guide.IdCounters` and `guide.ComputeHash()` unchanged (tested)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Comment mentioning IdAllocator broke the acceptance grep**
- **Found during:** Task 2 acceptance check
- **Issue:** The PlanNumbering XML summary said "never via IdAllocator", but the acceptance criteria require 0 occurrences
- **Fix:** Reworded it to "never via the guide id counters"
- **Commit:** 1ee267e

Interpretation choices the plan left open are recorded in the decisions above: per-word non-ASCII stripping, the Unassigned folder, root always Own, and how non-relevant attachments are worded.

## TDD Gate Compliance

Both tasks have a `test(04-05)` RED commit followed by a `feat(04-05)` GREEN commit. RED failed at compile time because the types did not exist yet.

## Threat Mitigations

- T-04-13: `CustodianFolder` filters the local part to [a-z0-9._-], collapses "..", trims dots, falls back to the CH id and suffixes reserved names. Paths are relative and use forward slashes, with fixed folder segments and only the DocID as the file name. Tested with "../evil", "con", invalid characters and leading/trailing dots.
- T-04-14: Thread ids and DocIDs are strictly chronological, and paths contain no label words (tested).
- T-04-15: Transferred to 04-06 (CSV/XLSX encoding).

## Known Stubs

None.

## Requirements

- KEY-03 is complete: labels come from the plan, and family relevance is passed to attachments and quoted replies.
- KEY-01 (needs the CSV/Excel output in 04-06) and PLAT-03 (needs planner and writer tests) are still Partial. This plan adds the row projection and its tests.

## Self-Check: PASSED
