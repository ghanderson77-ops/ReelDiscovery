---
phase: 06-attachments-non-relevant-corpus
verified: 2026-10-05T00:00:00Z
status: passed
score: 6/6 must-haves verified
overrides_applied: 1
overrides:
  - must_have: "Relevant and non-relevant emails show matched length, style, attachment, CC and thread-depth distributions (they come from the same model and formatter path)"
    reason: "06-CONTEXT D-08: the user chose the Volume tier for ordinary filler (daily business, ancillary, newsletter/spam/personal) to save cost and accepted the style-leakage risk. Decoys use the Story tier, the same model as relevant mail (D-09). Everything else is shared: the unit of work, the formatter, the ThreadShapes samplers and the length-band shares (D-11). The Phase 7 leakage audit (QA-02) measures the remaining style signal."
    accepted_by: "greg45 (06-CONTEXT D-08)"
    accepted_at: "2026-10-04T00:00:00Z"
---

# Phase 6: Attachments & Non-Relevant Corpus Verification Report

**Phase Goal:** The corpus becomes a full mixed collection. Case-consistent attachments serve as evidence, and non-relevant traffic (including near-miss decoys) comes from the same pipeline, so nothing but content separates relevant from non-relevant.
**Verified:** 2026-10-05
**Status:** passed
**Re-verification:** No (initial verification)

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Planned Word/Excel/PowerPoint attachments, invites, inline images and voicemails are generated; content matches the case guide and canonical values; media follows provider availability | VERIFIED | `AttachmentUnitOfWork` builds Office files with `OfficeDocumentService` and invites with `CalendarService`. It runs images and TTS through `IMediaGenerator` behind `MediaRunGate`. Content is checked by `RenderValidator.CheckAttachment` (ledger, ancestors and `KnowledgeGate`). `WizardState.CreateMediaGenerator()` returns null without a tested OpenAI key, and media items are then Skipped (PROV-06). UAT step 3 approved. |
| 2 | The corpus includes daily business, ancillary traffic and newsletters/spam/personal mail in the proportion the user set | VERIFIED | `NonRelevantSkeletonBuilder` builds the DailyBusiness, Ancillary, NewsletterSpamPersonal and Decoy categories from the per-category message counts. `SideWorldBuilder` supplies each organization's other work. The UAT plan had 146 daily-business, 77 ancillary and 154 newsletter/spam/personal threads, plus 25 decoys. |
| 3 | Near-miss decoys share project names and keywords, but a reader can confirm they are not about the alleged conduct | VERIFIED | `BuildDecoys` anchors each decoy within 30 days of a key fact. `FillerPrompts.AppendDecoy` adds the shared names, the angle and a MustNotMention list. `DecoyScanner` runs on decoy threads (`RenderProfile.For`) and decoy attachments (`AttachmentUnitOfWork`:399). All 25 UAT decoys carry MustNotMention. UAT step 4 approved. |
| 4 | Relevant and non-relevant mail show matched length, style, attachment, CC and thread-depth distributions (same model and formatter path) | PASSED (override) | One `ThreadUnitOfWork` path renders, validates, composes, writes and journals both populations (`GenerationRunner.RunNonRelevantAsync` calls the same `RunThreadAsync`). Filler uses the same `ThreadShapes` samplers for To/CC, reply kind and attachments, and the same `RenderAssignments.LengthBands` shares. `DistributionParityTests` covers length bands, length tokens, privilege markers, headers, structure and attachment names. One deviation: ordinary filler renders on the Volume tier, not the Story model (D-08, the user's cost decision). Decoys stay on Story. |
| 5 | OUT-01: non-relevant mail is written to every participating custodian's folder | VERIFIED | Filler goes through the same `MailboxWriter.CopyPathsFor` (sender copy first, then recipients in CustodianIds order). UAT step 6 approved. |
| 6 | RUN-02: a failed attachment, image, invite or voicemail is logged and retried or skipped without aborting the run | VERIFIED | `AttachmentOutcomeKind` has Written, Warnings, Failed, Skipped and NotRun, and each outcome is journaled. Repairs are scheduled in time order through `AttachmentRepair`. A media stop ends only media (`MediaRunGate.TryStop`). UAT steps 7 and 8 approved. |

**Score:** 6/6 (1 by override)

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| `Services/Evidence/AttachmentUnitOfWork.cs` (603 lines) | VERIFIED | Wired from `GenerationRunner`:191 into `ThreadUnitOfWork` and `AttachmentRepair` |
| `Services/Evidence/RenderProfile.cs` | VERIFIED | Picks the strategy for each thread type; used in the relevant waves and in the non-relevant stage |
| `Services/Corpus/NonRelevantSkeletonBuilder.cs` | VERIFIED | Builds all four categories with the shared shape samplers |
| `Services/Evidence/FillerRenderer.cs`, `FillerPrompts.cs`, `FillerAssignments.cs` | VERIFIED | Filler request, DTO mapping, length and privilege assignment |
| `Services/Evidence/DecoyScanner.cs` | VERIFIED | Called for decoy threads and decoy attachments |
| `Services/Evidence/SideWorldBuilder.cs`, `CastVoiceBuilder.cs` | VERIFIED | Journaled records, replayed on Continue |
| `Services/Evidence/AttachmentRepair.cs`, `MediaRunGate.cs`, `AttachmentHarvester.cs` | VERIFIED | Repair and media-stop path |
| `Services/Corpus/AnswerKeyBuilder.cs` | VERIFIED | Non-relevant category, near-miss flag, rationale, attachment rows, and Generated/Failed/Skipped status |
| `UserControls/StepGenerateCorpus.Run.cs` | VERIFIED | Passes `_state.CreateMediaGenerator()` to the runner |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| GenerationRunner non-relevant stage | ThreadUnitOfWork | `RunThreadAsync(ctx, uow, render, RenderProfile.For(...))` | WIRED |
| ThreadUnitOfWork | AttachmentUnitOfWork | `RunAttachmentsAsync`, then `EmailComposer.Compose` with the parts | WIRED |
| AttachmentUnitOfWork | Office/Calendar builders, media seam | `_office`, `_calendar`, `_media.GenerateImageAsync/GenerateSpeechAsync` | WIRED |
| RenderProfile (decoy) | DecoyScanner | `CheckFiller(...).Concat(DecoyScanner.Scan(...))` | WIRED |
| WizardState | IMediaGenerator | `CreateMediaGenerator()` is null without a key | WIRED |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Full test suite | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -c Release` | Passed 3415, Failed 0, Skipped 0 (8 m 59 s). This includes the tests from 31cd841 and 262f9c9. | PASS |

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| ATT-01 | SATISFIED | Office builders reused through AttachmentUnitOfWork; Truth 1 |
| ATT-02 | SATISFIED | CalendarService invites with InviteScheduler slots; Truth 1 |
| ATT-03 | SATISFIED | Image (inline or file) and TTS behind the media gate and PROV-06; Truth 1. The UAT run had no inline images; the inline path is covered by the frozen golden tests. |
| ATT-04 | SATISFIED | `RenderValidator.CheckAttachment` with the canonical ledger; Truth 1 |
| NOISE-01 | SATISFIED | Daily business plus the per-organization side world; Truth 2 |
| NOISE-02 | SATISFIED | Ancillary category; Truth 2 |
| NOISE-03 | SATISFIED | Newsletter/spam/personal category plus BulkMailFormatter; Truth 2 |
| NOISE-04 | SATISFIED | Decoys plus the MustNotMention scan; Truth 3 |
| NOISE-05 | SATISFIED (override) | Same pipeline and shared distributions; Volume tier for ordinary filler by D-08; Truth 4 |
| OUT-01 | SATISFIED | Truth 5 |
| RUN-02 | SATISFIED | Truth 6 |

Every Phase 6 ID in REQUIREMENTS.md is claimed by at least one plan. No requirements are orphaned. The plans also mention RUN-01 and RUN-06 (one plan each, carried over from Phase 5).

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `Services/Evidence`, `Services/Corpus`, `StepGenerateCorpus*` | - | No TODO, FIXME, NotImplementedException or placeholder | - | - |
| `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs` | 422, 424 | xUnit1031 analyzer warning (blocking task call in a test) | Info | Test project only; the app build is warning-free per 06-25 |

### Human Verification

The user approved the human UAT on 2026-10-05 (06-HUMAN-UAT.md): 8 steps passed and 2 were skipped. Step 9 (Phase 5 upgrade) was skipped because no Phase 5 run folder was available; automated upgrade-path tests cover it. Step 10 is optional. The three UAT issues were fixed before approval (b5831ce, 7d78ac6, 31cd841, 262f9c9), and the full suite above includes all of them.

### Deferred (non-blocking, recorded in deferred-items.md and handed to Phase 7)

- Small corpora can have privilege banners on relevant mail only (Phase 7 leakage audit).
- Invites for the same meeting can get different times when sent from different time zones.
- `GenerationReset.TryDelete` has no retry when a file is briefly locked.
- Theme fonts in the business HTML path are not sanitized inside `BuildEmailTemplate`.
- The style-leakage risk from Volume-tier filler (D-08) is measured by QA-02.

### Gaps Summary

No blocking gaps. The only departure from the roadmap wording is that ordinary filler is written by the Volume-tier model rather than "the same model". This was the user's documented cost decision (D-08) and is recorded as an override. Decoys, the hardest case to tell apart, use the same model as relevant mail.

---

_Verified: 2026-10-05_
_Verifier: Claude (gsd-verifier)_
