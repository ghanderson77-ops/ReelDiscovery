---
phase: 06-attachments-non-relevant-corpus
plan: 16
subsystem: filler-render-call
tags: [filler, prompts, strict-schema, noise-01, noise-02, noise-03, noise-04, noise-05, d-11, d-13, d-21, d-22, d-23, d-24]
requires:
  - "06-01 SideWorld, RenderedMessage.AttachmentTitles"
  - "06-06 FillerAssignments.Compute"
  - "06-11 AttachmentRenderer.SchemaTypes, AttachmentResponder.Create, EvidencePrompts shared helpers"
  - "06-12 SideWorldBuilder.ProjectsFor/Fallback/SchemaTypes, CastVoiceBuilder.SchemaTypes, CastVoiceSet.AllowedVoices"
provides:
  - "FillerPrompts.SystemPrompt (byte-stable const), PersonalSafetyLine, SpamLine, NewsletterLine, VendorLine"
  - "FillerPrompts.BuildThreadPrompt(ctx, sideWorld, feedback) on the EvidencePrompts skeleton with a category block"
  - "FillerRenderer.FillerThreadDto / FillerMessageDto (seq, body, signOff, attachmentTitles; no newDetails)"
  - "FillerRenderer.BuildRequest(ctx, sideWorld, tier, attempt, feedback), ToRendered(dto, ctx), CleanTitle, MaxTitleChars = 120"
  - "EvidencePrompts.CounselText / FeedbackHeader now internal (text unchanged)"
  - "AttachmentResponder answers FillerThreadDto (per-thread attempts + override) and CastVoiceDto requests; FillerDto/VoicesFor helpers"
  - "CaseSchemaTests registers AttachmentRenderer, SideWorldBuilder, CastVoiceBuilder and FillerRenderer schema types"
affects: [06-17, 06-19 runner wiring, 06-20]
tech-stack:
  added: []
  patterns:
    - "Filler DTO is mapped through RenderRepair.Apply by converting to ThreadRenderDto with empty PeopleNamed/NewDetails"
    - "Blank or missing filler titles take AttachmentNaming.TitleFor(spec, parent, null) so title i always pairs with attachment i"
key-files:
  created:
    - Services/Evidence/FillerPrompts.cs
    - Services/Evidence/FillerRenderer.cs
    - ReelDiscovery.Tests/Evidence/FillerPromptsTests.cs
  modified:
    - Services/Evidence/EvidencePrompts.cs
    - ReelDiscovery.Tests/Evidence/AttachmentResponder.cs
    - ReelDiscovery.Tests/Evidence/EvidenceTestData.cs
    - ReelDiscovery.Tests/Case/CaseSchemaTests.cs
decisions:
  - "Filler message lines keep the relevant shape (kind, from/to/cc, local time, length token) minus subtlety and may-mention; attachments are listed by kind noun with '(title needed)' or the planned title"
  - "Bulk style follows the first external participant's ExternalKind; Vendor senders get a routine-vendor line; unknown kinds fall back to the personal safety line"
  - "Decoy shared names = participant organization names + ProjectName values, minus any that overlap a must-not-mention term"
  - "ToRendered fills blank or missing titles with the code title so journal CleanStrings can never shift title-to-attachment pairing"
metrics:
  duration: 13min
  completed: 2026-10-05
---

# Phase 6 Plan 16: Filler Render Call Summary

Non-relevant threads now have their own model call. It uses the relevant prompt's skeleton, with the case-context sections swapped for a category block per thread type: "Ongoing work:" from the side world, a decoy angle with shared names and a must-not-mention list, or a style line for bulk mail. The response DTO has no ledger field and returns attachment titles. Every Phase 6 schema is now strict-checked.

## What was built

- **FillerPrompts** (D-11, D-07): the sections run in the same order as EvidencePrompts: name rules (no scenario), organizations, people (`EvidencePrompts.PersonLine`), category block, thread id/subject/purpose, then one line per message, the task and feedback last.
  - The category block replaces the facts, values and ledger sections, so filler sees no case facts (T-06-46).
  - DailyBusiness and Ancillary threads list each participant organization's side projects from `SideWorldBuilder.ProjectsFor`. Organizations past the cap or missing from the world get validated templates (D-21).
  - Decoy threads get `Angle:`, `Shared names you may use:` and `Do not mention:` (D-23).
  - Bulk-mail style follows the sender's kind: newsletters get 2-4 **Heading** sections, spam gets no links and no real brands (D-22), and personal mail gets the fixed D-24 safety line.
  - Privileged non-relevant threads get the counsel clause (Pitfall 2).
  - A single-message voicemail thread gets `EvidencePrompts.VoicemailInstruction` instead of a length token.
- **FillerPrompts.SystemPrompt** is a byte-stable const. It mirrors the evidence system prompt but swaps in the "ordinary correspondence" sentence and adds the attachment-title clause. It never says "fictional" and keeps the same formatting-allowance sentence.
- **FillerRenderer**:
  - `FillerThreadDto` has no newDetails (D-13).
  - `BuildRequest` takes the tier from the caller (Volume for filler, Story for decoys) and names the operation "Generate Corpus - {id}[ attempt n]".
  - `ToRendered` runs the output through `RenderRepair.Apply` with NewDetails empty. Titles have control characters stripped, are capped at 120 chars and are cut to the planned attachment count (T-06-47).
- **Test infrastructure**:
  - `AttachmentResponder` answers filler calls: band-midpoint bodies, "Thanks,", and "{Kind} notes {seq}" titles, with attempts counted per thread.
  - It also answers cast-voice calls: ordinal voice `(i + 2) % 6`.
  - `CaseSchemaTests` checks all Phase 6 DTOs.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 RED | 1169b78 | Failing FillerPromptsTests |
| 1 GREEN | 1937123 | FillerPrompts, FillerRenderer, shared EvidencePrompts consts |
| 2 | 16ed084 | Responder filler/cast-voice branches, schema registration |

## Verification

- `FillerPromptsTests|EvidencePromptsTests`: 25 passed
- `CaseSchemaTests|AttachmentResponderTests`: 32 passed
- Full suite: 3226 passed, 0 failed (baseline 3202)
- `dotnet build ReelDiscovery.csproj`: no warnings
- Acceptance greps: all match their expected counts

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Shared the filler-word body helper**
- **Found during:** Task 2
- **Issue:** The responder was meant to reuse "the same filler-word generator as EvidenceTestData", but `EvidenceTestData.Filler` was private.
- **Fix:** Changed it to `internal`, with a comment saying the responder uses it.
- **Files modified:** ReelDiscovery.Tests/Evidence/EvidenceTestData.cs
- **Commit:** 16ed084

**2. [Rule 2 - Correctness] Blank titles get the code title instead of an empty slot**
- **Found during:** Task 1
- **Issue:** Journal normalization (`CaseGuide.CleanStrings`) drops empty strings. An empty title in the middle of the list would then shift every later title onto the wrong attachment (NOISE-05 file names).
- **Fix:** `ToRendered` fills a blank or missing title with `AttachmentNaming.TitleFor(spec, parent, null)`, the same name the code would choose anyway.
- **Commit:** 1937123

`CastVoiceSet.AllowedVoices` is a set and cannot be indexed, so the `(i + 2) % 6` voice comes from its ordinal-sorted order, the same order `VoiceAssignments` uses. Task 2's schema-registration tests passed on first run because the DTOs were already strict. No implementation change was needed there, so the task was committed as `test(...)` only.

## TDD Gate Compliance

Task 1: a RED `test(06-16)` commit (1169b78) came before the GREEN `feat(06-16)` commit (1937123). Task 2 only touches test infrastructure, so it has a single `test(06-16)` commit.

## Known Stubs

None.

## Self-Check: PASSED
