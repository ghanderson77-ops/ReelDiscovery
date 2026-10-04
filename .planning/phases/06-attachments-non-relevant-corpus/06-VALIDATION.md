---
phase: 6
slug: attachments-non-relevant-corpus
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-10-04
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 (net10.0-windows) |
| **Config file** | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (no runsettings; app has `InternalsVisibleTo ReelDiscovery.Tests`) |
| **Quick run command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"` |
| **Full suite command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| **Render command** | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` (PNGs under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`, inspect at 144 DPI) |
| **Estimated runtime** | ~31 s quick (Evidence), ~60-90 s full non-render |

Fixtures to reuse: `EvidenceTestData.ApprovedCase()`, `RenderResponder(plan, guide)`, `TempCaseFolder()`, `Fakes/FakeLlmProvider`, `Fakes/FakeHttpHandler`, `Fakes/StrictSchemaValidator`, `OpenXmlValidator`, `Render/RenderHarness` + `ClippingDetector`.

---

## Sampling Rate

- **After every task commit:** Run quick run command
- **After every plan wave:** Run full suite command; UI waves also run the render command
- **Before `/gsd-verify-work`:** Full + render suites green, PNGs inspected at 144 DPI, one live smoke run on the Office test case reviewed
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

Requirement → test map (task IDs filled in by the planner/executor; see 06-RESEARCH.md "Validation Architecture").

| Task ID | Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|---------|-------------|----------|-----------|----------------------------|-------------|--------|
| TBD | ATT-01 | Word sections/headings/table, Excel numeric cells + safe sheet name, PPT subtitle not "Generated Email Dataset", org name header; OpenXmlValidator clean | unit | `FullyQualifiedName~OfficeDocumentServiceTests\|FullyQualifiedName~DocumentRegressionTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | ATT-01, D-01 | Per-kind attachment DTO schemas strict (≤16 unions) | unit | `FullyQualifiedName~CaseSchemaTests` | ✅ add types | ⬜ pending |
| TBD | ATT-01, RUN-02 | AttachmentUnitOfWork: render → bytes; refusal/invalid → AttachmentFailed; never throws; email still written | unit | `FullyQualifiedName~AttachmentUnitOfWorkTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-02, D-20 | Invite matches Meeting delta / ledger meeting / fact date; fallback + warning; organizer/attendees from plan | unit | `FullyQualifiedName~InviteSchedulerTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-02 | .ics deterministic UID/DTSTAMP, line folding, DQUOTE CN, CRLF stripped, correct offsets | unit | `FullyQualifiedName~CalendarServiceTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-03, D-06, D-27, D-28 | Inline image multipart/related + cid; voicemail notice From/Subject/transcript; null media client → Skipped; 401 → media stopped, text continues | unit | `FullyQualifiedName~MailboxWriterTests\|FullyQualifiedName~VoicemailComposerTests\|FullyQualifiedName~MediaGeneratorTests` | ✅ extend / ❌ W0 | ⬜ pending |
| TBD | ATT-04, D-17 | CheckAttachment runs V2/V3/V4/V6/V7 at parent-message knowledge; 2 retries then Warnings | unit | `FullyQualifiedName~AttachmentValidatorTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-04, D-18 | Attachment deltas merged at wave end, stable ids, cap respected; replay restores | unit | `FullyQualifiedName~LedgerMergerTests\|FullyQualifiedName~GenerationRunStateTests` | ✅ extend | ⬜ pending |
| TBD | ATT-04, D-16 | Version chains: grouping, order by SentAt, suffix in name, prior text passed | unit | `FullyQualifiedName~AttachmentChainsTests\|FullyQualifiedName~AttachmentNamingTests` | ❌ W0 | ⬜ pending |
| TBD | NOISE-01/02, D-08, D-12, D-13 | Filler on Volume tier, side-world in prompt, no ledger, structure checks V1/V5 only (no V8, no length retry: D-12 clarification 2026-10-04) | unit | `FullyQualifiedName~FillerPromptsTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | NOISE-01, D-21 | Side-world avoids case terms; persisted and reused on Continue; code fallback | unit | `FullyQualifiedName~SideWorldTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-03, D-28 | Per-cast voice call on Volume picks a valid tts-1 voice per cast member; invalid/unknown dropped; StableRank fallback on failure; persisted as CastVoices record and reused on Continue/Retry Failed; speech uses the persisted voice | unit / integration | `FullyQualifiedName~CastVoiceTests\|FullyQualifiedName~AttachmentUnitOfWorkTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ W0 | ⬜ pending |
| TBD | RUN-02, D-26, UI-SPEC Decision 1 | Phase 5 voicemail thread repaired in Stage C keeps From/Subject/body, gains audio; key row matches EML; harvested inline cid kept; as-of-wave ledger for repairs | unit / integration | `FullyQualifiedName~AttachmentRepairTests\|FullyQualifiedName~AnswerKeyOverlayTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | NOISE-03, D-22, D-24 | Newsletter masthead + unsubscribe, spam styling, personal plain; encoded; personal safety line | unit | `FullyQualifiedName~BulkMailFormatterTests\|FullyQualifiedName~FillerPromptsTests` | ❌ W0 | ⬜ pending |
| TBD | NOISE-04, D-09, D-23 | Decoy on Story tier; must-not-mention hit → retry ×2 → kept + flagged | unit | `FullyQualifiedName~DecoyScannerTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 | ⬜ pending |
| TBD | NOISE-05, D-07, D-11 | Band/CC/attachment/depth parity between relevant and non-relevant (assigned bands + prompt length tokens exact; measured lengths loose, no V8 retry on filler); privileged non-relevant markers; no "Document N" names; same composer path | unit | `FullyQualifiedName~DistributionParityTests\|FullyQualifiedName~FillerAssignmentsTests` | ❌ W0 | ⬜ pending |
| TBD | OUT-01 | Non-relevant copies in every participating custodian mailbox; Start Over removes non-relevant paths | unit | `FullyQualifiedName~MailboxWriterTests\|FullyQualifiedName~GenerationResetTests` | ✅ extend | ⬜ pending |
| TBD | RUN-02, D-25, D-26 | Relevant waves then filler; failed attachment → email written + key row Failed; Continue regenerates only missing attachments and rewrites all copies; Phase-5 journals resume | integration | `FullyQualifiedName~GenerationRunnerTests\|FullyQualifiedName~GenerationJournalTests\|FullyQualifiedName~AnswerKeyOverlayTests` | ✅ extend | ⬜ pending |
| TBD | D-29 | Estimate/ticker: Volume for filler, Story for decoys/relevant, attachments by tier, media per unit | unit | `FullyQualifiedName~GenerationCostEstimatorTests\|FullyQualifiedName~GenerationRunStateTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | D-04 | Generate Corpus stages/counts, Ready card, Case Summary at 144 DPI, no clipping | render | `Category=Render&FullyQualifiedName~GenerateCorpusRenderTests\|FullyQualifiedName~CaseSummaryRenderTests` | ✅ extend | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Evidence/AttachmentTestData.cs` — plan fixture with every attachment kind on relevant + non-relevant messages, a two-version chain, an invite root with a Meeting delta, a voicemail single-message thread, a privileged non-relevant thread and a decoy with MustNotMention
- [ ] `ReelDiscovery.Tests/Fakes/FakeMediaGenerator.cs` — scripted bytes / null / throw-401 behind the media interface
- [ ] `RenderResponder` extension — answers attachment and filler DTOs by schema name
- [ ] New test files listed in the map above (❌ rows)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Attachments open and read correctly | ATT-01..03 | Office/ICS/media client behavior | Live run on the Office test case; open docx/xlsx/pptx/ics/png/mp3 from EMLs in Outlook/Thunderbird |
| Decoys read as near-misses | NOISE-04 | Semantic judgment | Spot-read several decoys; confirm they share terms but are not about the alleged conduct |
| Non-relevant mail looks like the same corpus | NOISE-05 | Style judgment | Compare a sample of relevant vs. daily-business threads side by side |
| 144-DPI inspection | D-04 | Human visual check | Inspect render PNGs at 150% scaling |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
