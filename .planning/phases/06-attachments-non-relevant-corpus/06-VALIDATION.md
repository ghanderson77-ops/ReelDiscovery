---
phase: 6
slug: attachments-non-relevant-corpus
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-10-04
revised: 2026-10-04 (cross-AI review, 06-REVIEWS.md; plan-checker revision: canonical meeting key, run-wide chain text, NotRun spend, repair-slot ordering, shared mediaAvailable)
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

Fixtures to reuse: `EvidenceTestData.ApprovedCase()`, `RenderResponder(plan, guide)`, `TempCaseFolder()`, `Fakes/FakeLlmProvider`, `Fakes/FakeHttpHandler`, `Fakes/StrictSchemaValidator`, `OpenXmlValidator`, `Render/RenderHarness` + `ClippingDetector`, frozen golden constants under `ReelDiscovery.Tests/Evidence/Golden/` (Base64, captured before the code they protect changes).

---

## Sampling Rate

- **After every task commit:** Run quick run command
- **After every plan wave:** Run full suite command; UI waves also run the render command
- **Before `/gsd-verify-work`:** Full + render suites green, PNGs inspected at 144 DPI, one live smoke run on the Office test case plus the recovery exercises reviewed (06-25)
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

Requirement → test map (task IDs filled in by the executor in 06-25; see 06-RESEARCH.md "Validation Architecture" and 06-REVIEWS.md for the rows marked "review").

| Task ID | Requirement | Behavior | Test Type | Automated Command (filter) | File Exists | Status |
|---------|-------------|----------|-----------|----------------------------|-------------|--------|
| TBD | ATT-01 | Word sections/headings/table, Excel numeric cells (no leading-zero loss) + safe sheet name (re-trimmed after cut), PPT subtitle not "Generated Email Dataset", org name header; OpenXmlValidator clean | unit | `FullyQualifiedName~OfficeDocumentServiceTests\|FullyQualifiedName~DocumentRegressionTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | ATT-01, D-01 | Per-kind attachment DTO schemas strict (≤16 unions); usable content defined per kind | unit | `FullyQualifiedName~CaseSchemaTests\|FullyQualifiedName~AttachmentRendererTests` | ✅ add types / ❌ W0 | ⬜ pending |
| TBD | ATT-01, RUN-02 | AttachmentUnitOfWork: render → bytes; refusal/invalid → Failed; identity on every outcome (Failed/Skipped/NotRun); no status reporting inside the unit; never throws | unit | `FullyQualifiedName~AttachmentUnitOfWorkTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-02, D-20 (review 6) | Meeting date from ledger Value (not source Date); topic + participant match; two invites for one meeting get one schedule; persisted slot reused; never before a same-day parent; fallback + warning | unit | `FullyQualifiedName~InviteSchedulerTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-02, D-20 (checker 06-05) | One canonical meeting key across sources: own delta then ledger entry (`Key_OwnDeltaThenLedger_SameKeyAndSlot`), fact then ledger (`Key_FactThenLedger_SameKeyAndSlot`), own delta with fact (`Key_OwnDeltaWithFact_SameAsFact`); committed slot registered under the key and reused by a later thread (`Invite_OwnDeltaThenLedger_ReusesRegisteredSlot`); wave-1 own delta and wave-2 ledger match give identical DTSTART/DTEND live and on Continue (`Invite_OwnDeltaInWave1_LedgerMatchInWave2_SameSchedule`) | unit / integration | `FullyQualifiedName~InviteSchedulerTests\|FullyQualifiedName~ThreadUnitOfWorkTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | ATT-02, D-20 (checker 06-05 final) | Own-delta keys come from `LedgerMerger.EntriesFor` output resolved through `LedgerMerger.SameDetail` (single `MeetingKeyFor(LedgerEntry, ...)` path, no delta overload); adversarial cases assert equal key AND equal DTSTART against a `LedgerMerger.Build` ledger: id token in About (`Key_OwnDeltaIdTokenInAbout_MatchesLedger`), non-canonical / gated-out fact id (`Key_OwnDeltaNonCanonicalFactId_MatchesLedger`), duplicate delta at two seqs incl. sibling copy (`Key_DuplicateDeltaTwoSeqs_MatchesLedger`), later-thread duplicate of an earlier-wave entry (`Key_LaterThreadDuplicateOfEarlierWaveEntry_UsesEarlierKey`), Build parity oracle (`Key_EqualsBuildEntryKey_ForEveryOwnDelta`), same-wave duplicate shares Start/End via the content seed (`Key_SameWaveDuplicate_SameDerivedSlot`); an attachment detail absorbed into a thread entry never changes its facts (`AttachmentAbsorb_NeverChangesThreadEntryFacts`) | unit | `FullyQualifiedName~InviteSchedulerTests\|FullyQualifiedName~LedgerMergerTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | ATT-02 | .ics deterministic UID/DTSTAMP, 75-octet folding without splitting UTF-8/surrogates, DQUOTE CN, CRLF stripped, correct offsets | unit | `FullyQualifiedName~CalendarServiceTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-03, D-06, D-27, D-28 | Inline image multipart/related + cid; voicemail notice; null media → Skipped; 401 → media stopped; 429 insufficient_quota → Stopped, rate_limit → Failed; malformed responses → Failed; queued calls after a stop make no request | unit | `FullyQualifiedName~MailboxWriterTests\|FullyQualifiedName~VoicemailComposerTests\|FullyQualifiedName~MediaGeneratorTests\|FullyQualifiedName~AttachmentUnitOfWorkTests` | ✅ extend / ❌ W0 | ⬜ pending |
| TBD | ATT-04, D-17 (review 4) | CheckAttachment V2/V3/V4/V6/V7 at parent knowledge with NewDetails, visible thread deltas and ancestor bodies; sibling branch invisible; V6 near-amount on labelled cells; late repair rejects new details; 2 retries then Warnings | unit | `FullyQualifiedName~AttachmentValidatorTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-04, D-18 (review 7) | Attachment ledger ids `{AttachmentDocId}-Lnn`, per-attachment cap 3, thread entries first; repairs never renumber/absorb/displace; Phase 5 ledger unchanged; replay restores | unit | `FullyQualifiedName~LedgerMergerTests\|FullyQualifiedName~GenerationRunStateTests` | ✅ extend | ⬜ pending |
| TBD | ATT-04, D-16 (review 7) | Version chains scoped by sender organization + kind + stem; order by SentAt; suffix in name; ChainScheduler gives finish-order-independent prior text | unit | `FullyQualifiedName~AttachmentChainsTests\|FullyQualifiedName~AttachmentNamingTests\|FullyQualifiedName~ChainSchedulerTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-04, D-16 (checker 06-18/06-20) | Committed chain text persists across steps of one run (`CrossStep_CompleteInStep1_VisibleInStep2`); in-step repaired predecessor waits then falls back to older committed text; same-thread predecessor never waits (`SameThread_PredecessorNeverWaits`); `WaitForPredecessors_WaitsOnlyForOtherInStepThreads`; fresh run gives v2 in a later wave v1's text (`Chain_FreshRun_V2InLaterWave_GetsV1Text`); a thread that never starts still completes on the scheduler | unit / integration | `FullyQualifiedName~ChainSchedulerTests\|FullyQualifiedName~ThreadUnitOfWorkTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | ATT-04, D-16, RUN-02 (checker 06-18 final) | Chain waits form a DAG by thread rank (first SentAt, then Id): waits only on lower-ranked in-step threads, higher-ranked in-step predecessors give step-start text (`RankThreads_OrderIsFirstSentThenId`, `Rank_HigherRankedInStepPredecessor_NeverWaits_UsesStepStartText`, `Rank_WaitGraphIsAcyclic`); interleaved (v1,v3 in A, v2 in B) and crossing chains finish under a 10 s guard with identical prompts in both launch orders at scheduler level (`Chain_InterleavedAcrossTwoThreads_SchedulerNoDeadlock`, `Chain_CrossingChainsTwoThreads_SchedulerNoDeadlock`), unit-of-work level (`Chain_InterleavedAcrossTwoThreads_NoDeadlock`, `Chain_CrossingChainsTwoThreads_NoDeadlock`), runner level (`Chain_InterleavedAcrossThreadsInOneWave_RunFinishes`) and repair path with 4 and 1 slots (`Repair_InterleavedChainsTwoThreads_NoDeadlock`, `Repair_CrossingChainsTwoThreads_NoDeadlock`) | unit / integration | `FullyQualifiedName~ChainSchedulerTests\|FullyQualifiedName~ThreadUnitOfWorkTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | D-29, RUN-02 (checker 06-19 final) | NotRun attachment tokens and spend reach RunEnded through the three-argument `AddUnjournaledSpend(inputTokens, outputTokens, cost)`: ThreadOutcome / AttachmentRepairOutcome `NotRunInputTokens`/`NotRunOutputTokens`/`NotRunCostUsd` on committed paths, 0 on ThreadFailed/Unjournaled paths (`Commit_FatalDuringAttachments_CommitsFinishedParts`, `NotRunSpend_FatalAfterPaidAttachmentCall_InRunEnded` asserts tokens and cost exactly once) | unit / integration | `FullyQualifiedName~ThreadUnitOfWorkTests.Commit_\|FullyQualifiedName~GenerationRunnerTests\|FullyQualifiedName~AttachmentRepairTests` | ✅ extend | ⬜ pending |
| TBD | RUN-02, D-26 (review 1, 2) | Journal: attachment outcomes nested in ThreadDone / AttachmentsRepaired; identity-validated replay; full text + media spend; LatestAttemptEnded follows the newest RunStarted | unit | `FullyQualifiedName~GenerationJournalTests` | ✅ extend | ⬜ pending |
| TBD | RUN-02 (review 1) | Commit boundary failure injection: cancel after one attachment keeps thread + part; second-copy write failure reports nothing written and journals all spend; ThreadDone append failure returns unjournaled spend; kill before append replays as pending | unit | `FullyQualifiedName~ThreadUnitOfWorkTests.Commit_` | ✅ extend | ⬜ pending |
| TBD | RUN-02 (review MEDIUM) | Snapshot attachment status replaced per DocId (Failed → Written moves once; repeats count once; Done+Failed+Skipped ≤ Total); AddCost for uncommitted spend | unit | `FullyQualifiedName~GenerationSnapshotTests` | ✅ extend | ⬜ pending |
| TBD | NOISE-01/02, D-08, D-12, D-13 | Filler on Volume tier, side-world in prompt, no ledger, structure checks V1/V5 only (no V8, no length retry: D-12 clarification 2026-10-04) | unit | `FullyQualifiedName~FillerPromptsTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | NOISE-01, D-21 | Side-world names and topics avoid every case term; templates validated; 24-organization cap with templates beyond it; persisted and reused on Continue; code fallback; persist failure stops the run | unit / integration | `FullyQualifiedName~SideWorldTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ W0 | ⬜ pending |
| TBD | ATT-03, D-28 | Per-cast voice call on Volume picks a valid tts-1 voice; invalid/unknown dropped; StableRank fallback on failure; persisted as CastVoices and reused; persist failure stops the run; speech uses the persisted voice | unit / integration | `FullyQualifiedName~CastVoiceTests\|FullyQualifiedName~AttachmentUnitOfWorkTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ W0 | ⬜ pending |
| TBD | RUN-02, D-26 (review 3) | Harvest per parent message from any copy; multi-message repair reuses both parts; incomplete first copy falls back to another; 64 MB file / 25 MB part / 100 MB call caps | unit | `FullyQualifiedName~AttachmentHarvesterTests\|FullyQualifiedName~AttachmentRepairTests` | ❌ W0 | ⬜ pending |
| TBD | RUN-02, D-26, UI-SPEC Decision 1 | Repair: one AttachmentsRepaired commit after all copies; partial rewrite converges; failed regeneration keeps existing copies; finished text never changes; Phase 5 voicemail keeps From/Subject/body and gains audio; harvested cid kept | unit / integration | `FullyQualifiedName~AttachmentRepairTests\|FullyQualifiedName~AnswerKeyOverlayTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | RUN-02, D-25, D-26 (review 5) | Chronological repair: earlier-wave repairs commit before later-wave new work and feed its prompts; wave-ordered repairs see earlier repairs; late repair keeps finished text and ledger (checked also against same-wave written threads); max 4 concurrent repairs; five same-wave chain links with reversed launch order finish without a slot deadlock (`Repair_FiveChainLinksSameWave_NoSlotDeadlock`) | integration | `FullyQualifiedName~GenerationRunnerTests` | ✅ extend | ⬜ pending |
| TBD | NOISE-03, D-22, D-24 | Newsletter masthead + unsubscribe, spam styling, personal plain; encoded; personal safety line; quoted bulk images become [image: title] | unit | `FullyQualifiedName~BulkMailFormatterTests\|FullyQualifiedName~FillerPromptsTests\|FullyQualifiedName~EmailComposerTests` | ❌ W0 | ⬜ pending |
| TBD | NOISE-04, D-09, D-23 | Decoy on Story tier; must-not-mention hit (incl. "$1.25M"-style amounts via the extended scanner) → retry ×2 → kept + flagged | unit | `FullyQualifiedName~DecoyScannerTests\|FullyQualifiedName~CanonicalValueScannerTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | NOISE-05, D-07, D-11 | Code-controlled parity on a synthetic plan AND a production-planner plan: assigned bands, prompt length tokens, privilege markers, headers per message shape, MIME roots per attachment shape, no "Document N" names; fake word counts labelled plumbing-only (no V8 on filler) | unit / integration | `FullyQualifiedName~DistributionParityTests\|FullyQualifiedName~FillerAssignmentsTests` | ❌ W0 | ⬜ pending |
| TBD | NOISE-05 (review MEDIUM) | Frozen compatibility: HTML formatter, composer and no-attachment EML output equal golden constants captured before the change | unit | `FullyQualifiedName~ComposeHtmlTests\|FullyQualifiedName~EmailComposerTests\|FullyQualifiedName~MailboxWriterTests` | ❌ W0 golden | ⬜ pending |
| TBD | OUT-01 | Non-relevant copies in every participating custodian mailbox; Start Over removes non-relevant paths and repaired copies | unit | `FullyQualifiedName~MailboxWriterTests\|FullyQualifiedName~GenerationResetTests` | ✅ extend | ⬜ pending |
| TBD | RUN-02, D-25, D-26 | Relevant waves then filler; failed attachment → email written + key row Failed; Continue regenerates only missing attachments and rewrites all copies; Phase-5 journals resume | integration | `FullyQualifiedName~GenerationRunnerTests\|FullyQualifiedName~GenerationJournalTests\|FullyQualifiedName~AnswerKeyOverlayTests` | ✅ extend | ⬜ pending |
| TBD | D-29 (review MEDIUM) | One pricing path (PriceWork) for the Ready card and remaining cost: tiers, richness, invites, side-world and voice calls, media without QA overhead, unavailable media = 0; full media spend of every attempt | unit | `FullyQualifiedName~GenerationCostEstimatorTests\|FullyQualifiedName~GenerationRunStateTests` | ❌ W0 / ✅ extend | ⬜ pending |
| TBD | D-29 (checker 06-19) | NotRun attachment spend (fatal after a paid call) returned as NotRunCostUsd from the thread unit and the repair, added once to RunEnded unjournaled spend; 0 on ThreadFailed / Unjournaled paths (no double count) (`NotRunSpend_FatalAfterPaidAttachmentCall_InRunEnded`, `Commit_FatalDuringAttachments_CommitsFinishedParts`); a replayed CastVoices set is never re-journaled | unit / integration | `FullyQualifiedName~ThreadUnitOfWorkTests.Commit_\|FullyQualifiedName~AttachmentRepairTests\|FullyQualifiedName~GenerationRunnerTests` | ✅ extend / ❌ W0 | ⬜ pending |
| TBD | D-29 (review LOW) | Media prices dialog: Cancel keeps session prices, Reset Defaults 0.006 / 15.00, OK normalizes, dialog edits a clone; a run keeps its start-time price clone | unit | `FullyQualifiedName~MediaPricesDialogTests\|FullyQualifiedName~GenerateCorpusStepTests` | ❌ W0 | ⬜ pending |
| TBD | RUN-02 (review MEDIUM) | Entry banner follows the latest run attempt; progress never exceeds its maximum; repair-row counting rule identical live and on reopen | unit | `FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~GenerateCorpusStepTests\|FullyQualifiedName~GenerationHistoryTests` | ✅ extend | ⬜ pending |
| TBD | RUN-02 (checker 06-23) | AttachmentSeeds and GenerationHistory.Waves take the runner's mediaAvailable; a skipped image of a done thread is on the repair row in both views or in neither (`Waves_MediaAvailableMatchesSeeds`, GenerationRunStateTests seeds case) | unit | `FullyQualifiedName~GenerationHistoryTests\|FullyQualifiedName~GenerationRunStateTests` | ✅ extend | ⬜ pending |
| TBD | D-04 | Generate Corpus stages/counts, Ready card, Case Summary, media prices dialog at 144 DPI, no clipping, Primary-action count per state | render | `Category=Render&FullyQualifiedName~GenerateCorpusRenderTests\|FullyQualifiedName~CaseSummaryRenderTests\|FullyQualifiedName~DialogRenderTests` | ✅ extend | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `ReelDiscovery.Tests/Evidence/AttachmentTestData.cs` — plan fixture with every attachment kind on relevant + non-relevant messages, a two-version chain, a same-title document from another organization, a multi-message attachment thread, a sibling-branch thread, two invites for one meeting (with a Meeting ledger entry whose Date ≠ Value), a voicemail single-message thread, a privileged non-relevant thread and a decoy with MustNotMention
- [ ] `ReelDiscovery.Tests/Fakes/FakeMediaGenerator.cs` — scripted bytes / failed / stopped behind the media interface
- [ ] `AttachmentResponder` — answers attachment, filler and cast-voice DTOs by schema name
- [ ] Golden constants (`Evidence/Golden/ComposeHtmlGolden.cs`, `EmailComposerGolden.cs`, `MailboxWriterGolden.cs`) captured from the unchanged code before 06-07 / 06-09 / 06-10 edit it
- [ ] New test files listed in the map above (❌ rows)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Attachments open and read correctly | ATT-01..03 | Office/ICS/media client behavior | Live run on the Office test case; open docx/xlsx/pptx/ics/png/mp3 from EMLs in Outlook/Thunderbird |
| Decoys read as near-misses | NOISE-04 | Semantic judgment | Spot-read several decoys; confirm they share terms but are not about the alleged conduct |
| Non-relevant mail looks like the same corpus; measured length parity | NOISE-05 | Style judgment; real model output | Compare relevant vs daily-business threads; record per-band median word counts (06-HUMAN-UAT Step 5) |
| Recovery paths on real data | RUN-02 | End-to-end with real providers and files | Controlled Cancel + Continue, multi-message repair, Phase 5 journal upgrade (06-HUMAN-UAT Steps 7-9; record skipped with a reason when not possible) |
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
