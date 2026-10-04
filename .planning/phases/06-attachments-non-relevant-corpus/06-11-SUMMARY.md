---
phase: 06-attachments-non-relevant-corpus
plan: 11
subsystem: evidence-attachment-text
tags: [attachments, prompts, strict-schema, clamping, knowledge-gate, images, voicemail]
requires:
  - "06-01 SideWorld/SideProject, LedgerDelta.FromAttachment/AttachmentDocId, AttachmentTestData.MixedCase"
  - "06-03 WordSection/SimpleTable/SlideContent (Models/DocumentMetadata.cs)"
  - "06-05 AttachmentChainLink, InviteSlot"
  - "06-15 GenerationCostEstimator.IsRichAttachment"
provides:
  - "AttachmentRenderContext (Thread, Parent, Spec, Title, FileName, ParentBody, AcceptedThread, VisibleThreadDeltas, Chain, PriorVersionText, Relevant, Rich, IsDecoy, Tier, SideWorld, Slot, LateRepair, LaterEstablished)"
  - "AttachmentRenderer: 7 strict DTOs + SchemaTypes, DtoTypeFor, BuildRequest, Map (clamp + \\p{Cc} strip), Flatten, VariantText, IsUsable; AttachmentContent"
  - "AttachmentPrompts: SystemPrompt, FillerSystemPrompt, Build, ImagePrompt, IsRich (delegates to GenerationCostEstimator.IsRichAttachment)"
  - "EvidencePrompts.PersonLine/KnownTo/Names/OrderedParticipants internal; EvidencePrompts.VoicemailInstruction"
  - "Test double AttachmentResponder.Create(plan, guide, overrideJson) and DefaultDto(kind, relevant)"
affects: [06-12 filler prompts, 06-16 filler responder, 06-17/06-18 attachment unit of work and scheduling, MediaGenerator image prompts]
tech-stack:
  added: []
  patterns:
    - "One strict DTO per attachment kind; filler variants omit peopleNamed/newDetails so filler can never write the ledger"
    - "Untrusted output clamped in Map before any builder sees it (6x8 sections, 8x60 tables, 12x8 slides, 2,000 chars)"
    - "Attachment prompt context gated to MayMentionBySeq / KnownValueIdsBySeq at the parent seq and the parent's local date; own-thread context via KnowledgeGate.Ancestors"
    - "Image prompts built in code with whole-word name scrubbing and a fixed no-text rule"
key-files:
  created:
    - Services/Evidence/AttachmentRenderContext.cs
    - Services/Evidence/AttachmentRenderer.cs
    - Services/Evidence/AttachmentPrompts.cs
    - ReelDiscovery.Tests/Evidence/AttachmentRendererTests.cs
    - ReelDiscovery.Tests/Evidence/AttachmentPromptsTests.cs
    - ReelDiscovery.Tests/Evidence/AttachmentResponder.cs
  modified:
    - Services/Evidence/EvidencePrompts.cs
decisions:
  - "Map turns line breaks and tabs into one space and removes other control characters. It drops blank paragraphs, bullets, empty sections and slides, and fully empty rows. Rows are padded or cut to the header count. A table whose headers are all blank becomes no table"
  - "Flatten(Excel) also includes the notes line, so checks see every model-written string. VariantText(Excel) keeps rows whose first cell matches a label (RenderValidator.ContainsWord), plus notes"
  - "Attachment deltas take Seq = parent seq, FromAttachment = true and AttachmentDocId = Spec.DocId. They are kept only when ctx.Relevant is true"
  - "Build uses ctx.Rich for sizing. Callers set it from AttachmentPrompts.IsRich, the single D-15 rule shared with the estimate"
  - "The attachment prompt filters ledger entries to those dated on or before the parent's local date and known to a parent participant (or to no one in particular). It also filters VisibleThreadDeltas to the parent seq and its ancestors, even though 06-18 fills them, as a second safeguard"
  - "ImagePrompt removes person name tokens and organization names and short names, whole-word and case-insensitive, including a trailing possessive and dangling connectors. The setting comes from the sender organization's Location (CaseOrganization has no industry field). Spam images use the thread subject as the product words"
  - "The voicemail transcript line replaces the length token only when the thread has exactly one message and that message has a Voicemail attachment"
metrics:
  duration: "~12 min"
  completed: 2026-10-04
  tasks: 3
  files: 7
---

# Phase 6 Plan 11: Attachment Text Call Summary

Each attachment kind now has its own bounded text call. The call uses strict per-kind DTOs and clamps and cleans the model output in code. Prompts for relevant attachments see only what the parent message's participants know: facts and values gated at the parent seq and date, and own-thread context taken from the parent's ancestry. Filler and decoy prompts carry no case context. Image prompts are built in code with no names and a no-text rule, and single-message voicemail threads are written as spoken transcripts.

## What was built

- **AttachmentRenderContext**: the attachment call's inputs (D-14). `LaterEstablished` is used by checks only and never shown in the prompt.
- **AttachmentRenderer**:
  - Seven strict DTOs: Word, Excel, PowerPoint, invite, and filler Word/Excel/PowerPoint. All pass `StrictSchemaValidator` and have at most 16 unions.
  - The relevant DTOs reuse `ThreadRenderer.LedgerDeltaDto` for newDetails.
  - Also provides `DtoTypeFor`, `BuildRequest` (Story/Volume tier from the context, filler system prompt for non-relevant), `Map`, `Flatten`, `VariantText` and `IsUsable`, which applies the per-kind usable-content rules of review 06-11.
- **AttachmentPrompts**:
  - Byte-stable system prompts that keep names exactly as in the guide (D-03) and never use the word "fictional".
  - `Build` writes sections in order (a)-(h): name rules, organizations, people, gated knowledge or filler/decoy context, the parent email (Attachment id, Thread id, subject, from/to/cc, local time, body), the spec with sizing, chain and invite blocks, the task line, and feedback last.
  - `ImagePrompt` builds image prompts in code. `IsRich` delegates to `GenerationCostEstimator.IsRichAttachment`.
- **EvidencePrompts**: the shared helpers are now internal. Single-message voicemail threads get `VoicemailInstruction(caller)` instead of the `length:` token. All existing EvidencePromptsTests pass unchanged.
- **AttachmentResponder** (tests):
  - Answers attachment calls by SchemaName and the `Attachment id:` line, with attempts counted per DocId and an override hook.
  - Returns deterministic valid DTOs for each kind and passes every other request to `EvidenceTestData.RenderResponder`.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 6e0778d | test(06-11): add failing tests for attachment DTOs, clamping, mapping and flattening |
| 1 GREEN | b1212af | feat(06-11): attachment DTOs, context, clamping, mapping and flattening |
| 2 tests | 05ae0a5 | test(06-11): add tests for attachment, image and voicemail prompts |
| 2 GREEN | c1b293b | feat(06-11): attachment prompts, image prompt and voicemail transcript line |
| 3 | bfa6078 | test(06-11): AttachmentResponder test double for attachment DTOs |

## Verification

- `--filter AttachmentRendererTests`: 26 passed
- `--filter AttachmentPromptsTests|EvidencePromptsTests`: 28 passed
- `--filter AttachmentResponderTests`: 3 passed
- Full suite: 3148 passed, 0 failed (baseline 3104)
- `dotnet build ReelDiscovery.csproj`: no warnings. The new test files add no warnings.
- Every acceptance-criteria grep passes.

## Deviations from Plan

### Ordering adjustments

**1. `BuildRequest` landed with Task 2 instead of Task 1**
- **Reason:** it calls `AttachmentPrompts.Build` and the system prompts, which Task 2 creates. Putting it in Task 1 would have needed a stub.
- **Commit:** c1b293b

**2. The fixture-usability test moved to `AttachmentResponderTests`**
- **Reason:** "each kind's deterministic fixture DTO is usable" needs `AttachmentResponder.DefaultDto` from Task 3, so it now lives in Task 3's test class (`DefaultDtos_AreUsableForEveryKind`).

## TDD Gate Compliance

- **Task 1:** RED commit 6e0778d (tests did not compile without the implementation), then GREEN b1212af.
- **Task 2:** the prompt implementation was written before its test file. The `test(...)` commit 05ae0a5 still comes before the `feat(...)` commit c1b293b, but a failing run was never observed. The first test run had a single failure, and that was a wrong expectation in the test (subject capitalization), fixed in the test.

## Known Stubs

None. `AttachmentRenderContext.VisibleThreadDeltas`, `PriorVersionText`, `SideWorld` and `Slot` are filled by the attachment unit of work and scheduler in later plans (06-17, 06-18), as the plan intends.

## Threat Flags

None. The image prompt is the only content that leaves the machine, and it is covered by T-06-34 (names removed, no-text rule).

## Self-Check: PASSED

- All 7 created or modified files exist.
- Commits 6e0778d, b1212af, 05ae0a5, c1b293b and bfa6078 are present in `git log`.
