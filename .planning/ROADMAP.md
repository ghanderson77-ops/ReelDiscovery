# Roadmap: ReelDiscovery

## Overview

ReelDiscovery moves from a loose topic/storyline wizard to a case-guide-driven pipeline. We first stabilize the platform and put both AI providers (OpenAI and Claude) behind one structured-output gateway. Then the user can seed a case and get a realistic core pleading, followed by a structured, ID-stable case guide (issues, cast, dated facts, canonical values) saved as case.json. From that guide, code plans every thread and message and derives the answer key before any prose is written. The renderer then writes relevant evidence in chronological waves, using knowledge-gated context and a running narrative ledger, and saves each thread as it finishes. Attachments and non-relevant material fill out the full mixed corpus. Batch APIs, prompt caching, blind-review QA and the final cutover make 5,000-email runs affordable and trustworthy. Optional ancillary discovery documents depend only on the case guide and come last.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Foundation & Provider Gateway** - .NET 10 retarget, tests, concurrency fixes, DPAPI keys, and one OpenAI + Claude gateway with strict structured outputs
- [x] **Phase 2: Case Scenario & Core Document** - Scenario + preset input produces a realistic, citation-safe Complaint / Petition / Investigation Memo the user can edit (completed 2026-10-01)
- [x] **Phase 3: Case Guide** - ID-stable issues, cast, dated facts timeline and canonical-value registry, validated, editable, saved as case.json and exported to .docx
 (completed 2026-10-02)
- [x] **Phase 4: Corpus Plan & Answer Key** - Code-owned manifest of every thread/message with a validator, a plan review screen, the answer key and the review protocol derived from it (completed 2026-10-03)
- [x] **Phase 5: Relevant Evidence Generation & Narrative Ledger** - Knowledge-gated, chronologically waved rendering of relevant threads with a running narrative ledger, saved incrementally as per-custodian EMLs (completed 2026-10-03)
- [ ] **Phase 6: Attachments & Non-Relevant Corpus** - Case-driven attachments plus daily business, ancillary, newsletter/spam/personal and near-miss decoy traffic through the same pipeline
- [ ] **Phase 7: Scale, Cost, QA & Cutover** - Batch APIs, prompt caching, cost accounting, blind-review and leakage QA, 5K-scale runs, and removal of the old wizard flow
- [ ] **Phase 8: Ancillary Discovery Documents** - Optional depositions, RFPs, RFAs and interrogatories grounded in the case guide and exported as .docx

## Phase Details

### Phase 1: Foundation & Provider Gateway
**Goal**: The app runs on a supported runtime with tests and safe concurrency, and all text generation goes through one provider-agnostic gateway, so the user can choose OpenAI or Claude.
**Depends on**: Nothing (first phase)
**Requirements**: PLAT-01, PLAT-02, PLAT-03, PLAT-04, PROV-01, PROV-02, PROV-03, PROV-04, PROV-05, PROV-06
**Success Criteria** (what must be TRUE):
  1. The app builds on net10.0-windows with a pinned SDK and publishes as a single-file self-contained exe that launches and runs the existing flow.
  2. The user can enter and test both an OpenAI and an Anthropic API key. Keys are stored DPAPI-encrypted, and an existing Base64 key file is migrated automatically.
  3. The user can pick Claude or OpenAI plus a model, and a structured (JSON-schema) request returns a valid typed result from either provider. Refusals, truncations and spend-limit errors show up as classified outcomes and are not retried blindly.
  4. Image generation works again on a gpt-image model. With Claude selected and no OpenAI key configured, images and voicemail are disabled and the user sees a clear notice.
  5. `dotnet test` runs a test project that passes, and the shared-Random and parallel-mutation bugs are fixed.
**Plans**: 13 plans (11 + 2 gap closure)

Plans:
- [x] 01-01-PLAN.md — Retarget to net10.0-windows, pin SDK, upgrade packages, xUnit test project + document regressions (Wave 1)
- [x] 01-02-PLAN.md — Concurrency fixes (Random.Shared, error sink, progress snapshots) and legacy bug fixes B1-B5 (Wave 2)
- [x] 01-03-PLAN.md — Gateway core: contracts, strict JSON-schema registry, LlmGateway retry policy, new model catalog (Wave 2)
- [x] 01-04-PLAN.md — Anthropic Claude provider translator, outcome classification, spend-cap retry guard, free key test (Wave 3)
- [x] 01-05-PLAN.md — OpenAI text provider, quota-aware retry, key test, gpt-image/tts-1 media client (Wave 3)
- [x] 01-06-PLAN.md — DPAPI ApiKeyStore + legacy Base64 key migration at startup (Wave 3)
- [x] 01-07-PLAN.md — WizardState provider/tier model, two-provider keys screen, model catalog dialog (Wave 4)
- [x] 01-08-PLAN.md — Port Storyline/Character/Theme generators to the gateway (Wave 5)
- [x] 01-09-PLAN.md — Media gating UI on Topic and Generation Config steps; remove Slack telemetry (D-16) (Wave 5)
- [x] 01-10-PLAN.md — Port EmailGenerator to the gateway + media client, delete dead code (Wave 5)
- [x] 01-11-PLAN.md — Fatal-stop/outcome UX, generator media guard, delete OpenAIService, phase gate (Wave 6)
- [x] 01-12-PLAN.md — Gap closure: classify provider/media transport timeouts (CR-01), stale key-test guard (CR-02) (Wave 1, gaps)
- [x] 01-13-PLAN.md — Gap closure: timeout-safe EmailGenerator + no silent success (CR-01), always-visible result + StepComplete status (WR-05), safe email distribution (WR-06) (Wave 1, gaps)
**UI hint**: yes

### Phase 2: Case Scenario & Core Document
**Goal**: The user describes a real-life dispute or investigation and gets a realistic, legally structured core document that they can review, edit and regenerate.
**Depends on**: Phase 1
**Requirements**: CASE-01, CASE-02, CASE-03, CASE-04, CASE-05, CASE-06, CASE-07
**Success Criteria** (what must be TRUE):
  1. The user can enter a free-text scenario and pick Simple, Standard or Complex, and the preset visibly changes the parties, counts, characters, timeline span and document length.
  2. If the scenario names real people or companies, the user sees a warning and fictional substitutes are applied by default. The user can override this.
  3. A civil scenario produces a Complaint/Petition, and a criminal or regulatory scenario produces an Investigation Memo. Either one has the full realistic structure (caption, parties, jurisdiction/venue, numbered factual allegations, counts, prayer, or the memo equivalent) for one consistent jurisdiction.
  4. Generated documents contain no invented case-law citations, and an automatic check flags any that appear.
  5. The user can edit the core document or regenerate it (in whole or by section) before continuing.
**Plans**: 9 plans

Plans:
- [x] 02-01-PLAN.md — Case domain model (CaseDraft, CoreDocument, sections, presets, substitutions), WizardFlow + WizardState.Case, CaseTestData scaffold, JSON round-trip (Wave 1)
- [x] 02-02-PLAN.md — CitationScanner (no case-law citations) + NameSubstitution (single-pass fictional names, leak scan) (Wave 2)
- [x] 02-03-PLAN.md — DocumentTypeResolver, CoreDocumentTemplates, CoreDocumentAssembler (code-owned numbering, caption, incorporation, memo header) (Wave 2)
- [x] 02-04-PLAN.md — CoreDocumentValidator + CoreDocumentEditing (Wave 3)
- [x] 02-05-PLAN.md — RealEntityDetector (Story-tier real-name pre-flight) (Wave 2)
- [x] 02-06-PLAN.md — CoreDocumentGenerator (outline, sections, citation retry, regenerate section) + CaseSchemaTests (Wave 3)
- [x] 02-07-PLAN.md — StepCaseScenario, EntityReviewDialog, LoadingOverlay EntityCheck (Wave 3)
- [x] 02-08-PLAN.md — StepCoreDocument (inline generation, editor, regenerate, issues, Finish gate) (Wave 4)
- [x] 02-09-PLAN.md — WizardForm flow switch, validated Finish + Close Case confirm, Workflow selector (Wave 5)
**UI hint**: yes

### Phase 3: Case Guide
**Goal**: The core document becomes a structured, validated, ID-stable case guide (issues, cast, dated facts, canonical values). This guide is the single source of truth for everything generated downstream.
**Depends on**: Phase 2
**Requirements**: GUIDE-01, GUIDE-02, GUIDE-03, GUIDE-04, GUIDE-05, GUIDE-06, GUIDE-07, GUIDE-08, GUIDE-09, GUIDE-10, GUIDE-11
**Success Criteria** (what must be TRUE):
  1. The user sees an issue list, a Cast of Key Characters (including in-house and outside counsel and ancillary per-organization staff), and a dated Key Facts timeline. Every entry has a stable ID, and each fact is linked to issues, actors and who knew about it.
  2. The user sees a registry of canonical values (amounts, invoice/change-order numbers, document titles, project names), and a validator reports any inconsistency: dates out of range, or facts pointing to missing cast members or issues.
  3. The user can edit and regenerate the cast and the timeline. Editing the core document, cast or facts warns the user and marks downstream artifacts stale.
  4. The case guide is saved as case.json next to the output, can be reloaded, and the user can export the core document, cast and key-facts summary as .docx files that open in Word.
**Plans**: 20 plans in 10 waves

Plans:
- [x] 03-01-PLAN.md — Guide data model, case.json fields (version, case id, stage, guide, Normalize), preset numbers, SampleGuidedDraft fixture (Wave 1)
- [x] 03-02-PLAN.md — 144-DPI render harness + clipping detector; GridStyle, TextFieldHost, DrawStatusDot (Wave 1)
- [x] 03-03-PLAN.md — IdAllocator, IssueExtractor, EmailIdentity, SignatureBuilder, UsTimeZones (Wave 2)
- [x] 03-04-PLAN.md — CaseFolderNaming + CaseStore (atomic autosave, load, recent cases, move) on WizardState (Wave 2)
- [x] 03-05-PLAN.md — CaseGuideDocxExporter: core document, cast, key facts .docx (Wave 2)
- [x] 03-06-PLAN.md — CaseGuideUiText copy helpers + ThemedDialog, Regenerate/Delete dialogs (Wave 2)
- [x] 03-07-PLAN.md — GuideStaleness + CaseGuideEditing (Wave 3)
- [x] 03-08-PLAN.md — CanonicalValueScanner + CaseGuideValidator (Wave 4)
- [x] 03-09-PLAN.md — CaseGuidePrompts + generator: issues and cast (roster, details, background) (Wave 4)
- [x] 03-10-PLAN.md — Generator: key facts, canonical values, GenerateAsync orchestration (Wave 5)
- [x] 03-11-PLAN.md — Generator: tab/entry regeneration and Update Guide (Wave 6)
- [x] 03-12-PLAN.md — IGuideHost, GuideTabPanel base, Issues tab, LinkPicker (Wave 5)
- [x] 03-13-PLAN.md — Cast tab (Wave 6)
- [x] 03-14-PLAN.md — Key Facts tab (Wave 6)
- [x] 03-15-PLAN.md — Canonical Values tab (Wave 6)
- [x] 03-16-PLAN.md — StepCaseGuide: runs, banner, Checks, autosave, Next gate (Wave 7)
- [x] 03-17-PLAN.md — StepCaseSummary: exports, open/move folder (Wave 5)
- [x] 03-18-PLAN.md — WizardForm 5-step flow + close semantics; StepCoreDocument autosave/notice/alignment (Wave 8)
- [x] 03-19-PLAN.md — Describe the Case: Recent cases, Open/New Case (Wave 9)
- [x] 03-20-PLAN.md — Phase gate: full suites + human check at 150% (Wave 10)
**UI hint**: yes

### Phase 4: Corpus Plan & Answer Key
**Goal**: Before any prose is written, code plans every thread and message against the facts timeline. The user reviews the plan, and the answer key and review protocol come straight from it.
**Depends on**: Phase 3
**Requirements**: PLAN-01, PLAN-02, PLAN-03, PLAN-04, PLAN-05, PLAN-06, PLAN-07, KEY-01, KEY-02, KEY-03, KEY-04, KEY-05, PLAT-03
**Success Criteria** (what must be TRUE):
  1. The user sets the corpus size (up to about 5,000) and the relevant/non-relevant ratio. The resulting plan fixes every message's date/time, sender, To/CC, reply/forward structure, linked facts/issues, relevance label and attachments.
  2. Planned relevant threads follow the key-facts timeline, with overlapping issues running at the same time among shared characters. Timestamps respect business hours, time zones, weekends/holidays and reply latency, and thread depth, CC and attachment distributions look realistic.
  3. A plan validator reports counts, ratio, dates outside each character's active range, and any key fact with no planned evidence.
  4. The user sees a plan summary (counts by issue, fact, relevance and custodian, plus a date histogram) and can approve the plan or re-plan.
  5. From the plan alone, the user gets a CSV and Excel answer key with one row per email and attachment. It has all required columns, family relevance propagated to attachments and quoted replies, and a statistics sheet. The user also gets a review protocol / coding manual document.
**Plans**: 22 plans in 8 waves

Plans:
- [x] 04-01-PLAN.md — Corpus data contracts: settings, state, plan model, answer-key row, Apportion, case.json format v2 + CaseDraft.Corpus (Wave 1)
- [x] 04-02-PLAN.md — BusinessCalendar, PlanCallRunner + PlanProgress/FillResult, rich fixture, thread-safe fake, WizardMin (Wave 1)
- [x] 04-03-PLAN.md — ThreadShapes, ThreadScheduler, BudgetAllocator, SkeletonContext (Wave 2)
- [x] 04-04-PLAN.md — CorpusPlanValidator (warnings-only rules) + PlanFixtures (Wave 2)
- [x] 04-05-PLAN.md — DocIdPrefix, PlanNumbering (chronological DocIDs, custodians, paths), AnswerKeyBuilder (Wave 2)
- [x] 04-06-PLAN.md — AnswerKeyCsvWriter (RFC 4180 + OWASP) and AnswerKeyXlsxWriter (3 sheets) (Wave 2)
- [x] 04-07-PLAN.md — CorpusPlanPrompts system prompt + ReviewProtocolBuilder (prose call, fallback, docx) (Wave 2)
- [x] 04-08-PLAN.md — CorpusPlanStore (corpus-plan.json), PlanSummary, GenerationCostEstimator (Wave 2)
- [x] 04-09-PLAN.md — ThreadSkeletonBuilder: relevant threads, multi-issue, hot, privileged, attachments, repair (Wave 3)
- [x] 04-10-PLAN.md — NonRelevantSkeletonBuilder: daily, ancillary, newsletter/spam/personal, decoys, externals (Wave 3)
- [x] 04-11-PLAN.md — CorpusUiText: all Phase 4 copy + Review Plan entry/gate decisions (Wave 3)
- [x] 04-12-PLAN.md — PercentSlider, MonthHistogram, StatTile, ReplanDialog + 144-DPI renders (Wave 4)
- [x] 04-13-PLAN.md — RelevantThreadFiller: chunked Story-tier thread plans, mapping, re-ask once, templates (Wave 4)
- [x] 04-14-PLAN.md — SubjectSeeder: non-relevant subjects, decoy must-not-mention, external senders (Wave 4)
- [x] 04-15-PLAN.md — Case Summary Corpus group (plan, answer key, protocol rows) + renders (Wave 4)
- [x] 04-16-PLAN.md — CorpusPlanner.PlanAsync orchestration, guidance weights, schema registration (Wave 5)
- [x] 04-17-PLAN.md — StepCorpusSettings + renders (Wave 5)
- [x] 04-18-PLAN.md — StepReviewPlan: summary, planning run, staleness, re-plan, Approve via CorpusApproval + renders (Wave 6)
- [x] 04-19-PLAN.md — WizardForm 7-step flow, stage mapping, C-P2, reopen without auto-plan (Wave 7)
- [x] 04-20-PLAN.md — Phase gate: full suites + human check at 150% (Wave 8)
- [x] 04-21-PLAN.md — CaseStage CorpusSettings/ReviewPlan + content-only CaseGuide.ComputeHash (Wave 1)
- [x] 04-22-PLAN.md — CorpusApproval service: approve writes, apply new plan (plan file first, stage -> ReviewPlan), staleness rules, FileLockDetector (Wave 3)
**UI hint**: yes

### Phase 5: Relevant Evidence Generation & Narrative Ledger
**Goal**: Relevant threads are written as a coherent evidentiary story. Each email knows only what its participants could know at that date, later emails build on a running narrative ledger, and every finished thread is saved immediately.
**Depends on**: Phase 4
**Requirements**: CORP-01, CORP-02, CORP-03, CORP-04, CORP-05, CORP-06, CORP-07, CORP-08, CORP-09, CORP-10, CORP-11, OUT-01, OUT-02, RUN-01, RUN-02, RUN-06, RUN-07
**Success Criteria** (what must be TRUE):
  1. Running an approved plan produces relevant threads in chronological waves. Later emails correctly refer to commitments, figures, meetings and people that earlier emails introduced, and no email mentions facts dated after it or unknown to its participants.
  2. After each thread, new details are extracted into the narrative ledger. Content that contradicts the ledger, canonical values or the plan (participants, dates, values) is flagged and retried. The ledger is saved in case.json and exports as a readable "story so far".
  3. The relevant emails read as natural business correspondence with varied subtlety, deliberate hot documents, privileged counsel threads, distinct per-character voices and signatures, and correct threading (quoting, Message-ID, In-Reply-To, References).
  4. EML files appear in each participating custodian's mailbox folder (sent and received), and each one carries an X-ReelDiscovery-DocID header that matches the answer key.
  5. Killing the app or pressing Cancel mid-run keeps every completed thread and its answer-key rows. A single failed email is logged and retried or skipped without aborting the run, and live progress and cost stay responsive throughout.
**Plans**: 17 plans
**UI hint**: yes

Plans:
- [x] 05-01-PLAN.md — Evidence models (ledger, journal, assignments, progress), CaseStage.Generation, format v3, approved-plan fixture (Wave 1)
- [x] 05-02-PLAN.md — Gateway token totals, custodian folder map, amount parser, answer-key status/subject overlay (Wave 1)
- [x] 05-03-PLAN.md — WavePlanner (chronological waves) + RenderAssignments (subtlety/length/privilege mixes) (Wave 2)
- [x] 05-04-PLAN.md — KnowledgeGate (point-in-time context) + LedgerSelector + LedgerMerger (Wave 2)
- [x] 05-05-PLAN.md — PeopleDirectory, deterministic organization themes, allowlist-only ComposeHtml (Wave 2)
- [x] 05-06-PLAN.md — GenerationUiText copy, entry decision, tile tone (Wave 2)
- [x] 05-07-PLAN.md — EmailComposer (Message-IDs, threading, subjects, signatures, privilege markers, quoting) (Wave 3)
- [x] 05-08-PLAN.md — ThreadRenderContext, EvidencePrompts, ThreadRenderer strict DTOs, RenderRepair, fake responder (Wave 3)
- [x] 05-09-PLAN.md — RenderValidator code checks V1-V8 (Wave 4)
- [x] 05-10-PLAN.md — MailboxWriter (per-custodian atomic EMLs) + GenerationJournal (append/replay) (Wave 4)
- [x] 05-11-PLAN.md — GenerationRunState, answer-key re-projection, story-so-far .docx, draft applier (Wave 5)
- [x] 05-12-PLAN.md — GenerationSnapshot + ThreadUnitOfWork (check-retry, keep best, write, journal) (Wave 5)
- [x] 05-13-PLAN.md — GenerationRunner (waves, cancel, fatal, continue) + Start Over + end-to-end tests (Wave 6)
- [x] 05-14-PLAN.md — StepGenerateCorpus layout, entry display, 144-DPI render tests (Wave 7)
- [x] 05-15-PLAN.md — Run lifecycle (off-UI run, persistence, cancel, Start Over, reopen) + 8-step wizard shell (Wave 8)
- [x] 05-16-PLAN.md — Case Summary Mailboxes/Story so far rows + re-plan warning (Wave 9)
- [x] 05-17-PLAN.md — Phase gate: full/render suites + human verification at 144 DPI and live run (Wave 10)

### Phase 6: Attachments & Non-Relevant Corpus
**Goal**: The corpus becomes a full mixed collection. Case-consistent attachments serve as evidence, and non-relevant traffic (including near-miss decoys) comes from the same pipeline, so nothing but content separates relevant from non-relevant.
**Depends on**: Phase 5
**Requirements**: ATT-01, ATT-02, ATT-03, ATT-04, NOISE-01, NOISE-02, NOISE-03, NOISE-04, NOISE-05, OUT-01, RUN-02
**Success Criteria** (what must be TRUE):
  1. Planned Word, Excel and PowerPoint attachments, calendar invites, inline images and voicemails are generated. Their content matches the case guide and canonical values, and media types follow the provider availability rules.
  2. The corpus includes the custodians' own daily business, ancillary-character traffic, and newsletters/spam/personal email in the proportion the user set.
  3. Near-miss decoys share project names and keywords with the case, but a reader can confirm they are not about the alleged conduct.
  4. Relevant and non-relevant emails show matched length, style, attachment, CC and thread-depth distributions (they come from the same model and formatter path).
**Plans**: 25 plans (revised 2026-10-04 for the cross-AI review)
**UI hint**: yes

Plans:
- [x] 06-01-PLAN.md — Journal commit groups (attachment outcomes nested in ThreadDone / AttachmentsRepaired), identity-validated replay, latest-attempt detection, side world and cast voices, adversarial mixed fixture (Wave 1)
- [x] 06-02-PLAN.md — Progress/state contracts: per-DocId attachment status transitions, uncommitted spend, stage rows, events, composed attachment parts (Wave 1)
- [x] 06-03-PLAN.md — Office builder overloads (branded, numeric Excel, safe sheet names, real subtitles, metadata) + deterministic RFC 5545 invites (Wave 1)
- [x] 06-04-PLAN.md — Media seam (Ok/Failed/Stopped), media prices, media cost tracking, FakeMediaGenerator (Wave 1)
- [x] 06-05-PLAN.md — Organization-scoped version chains, final attachment names (D-02 exception documented), meeting-keyed invite scheduling, TTS voice lookup (Wave 2)
- [x] 06-06-PLAN.md — Filler length/privilege assignments + decoy must-not-mention scanner + "$1.25M"-style amount suffixes (Wave 2)
- [x] 06-07-PLAN.md — Inline images/quote fragments in HtmlEmailFormatter (frozen golden) + newsletter/spam/personal formatter with quote placeholders (Wave 1)
- [x] 06-08-PLAN.md — CheckAttachment with complete V6 inputs and ancestry gating, V1/V5-only filler checks (no V8), voicemail-aware V8, stable attachment ledger ids (Wave 3)
- [x] 06-09-PLAN.md — Composer: attachment parts, inline images, bulk branch, voicemail phone-system notice (Wave 2)
- [x] 06-10-PLAN.md — Deterministic multipart MIME (frozen golden) + per-message, multi-copy, bounded attachment harvester (Wave 2)
- [x] 06-11-PLAN.md — Attachment DTOs, prompts, image prompt, voicemail transcript line, test responder (Wave 3)
- [x] 06-12-PLAN.md — Per-organization side world with collision checks and fallback + per-cast voicemail voice call (Wave 3)
- [x] 06-13-PLAN.md — Phase 6 UI copy (tiles, stages, activity, banners, Ready card, summary rows) (Wave 2)
- [x] 06-14-PLAN.md — Whole-corpus run state from committed records, chronological repair groups, persisted invite slots, shared estimate, answer-key overlay (Wave 4)
- [x] 06-15-PLAN.md — Start Over for all threads + Story/Volume/media cost estimate (Wave 2)
- [x] 06-16-PLAN.md — Filler prompts and DTO on the shared skeleton + schema registration (Wave 4)
- [ ] 06-17-PLAN.md — AttachmentUnitOfWork: identified outcomes, no pre-commit reporting, Office/invite render-check-retry-build, images, voicemail, media gate with post-slot recheck (Wave 4)
- [ ] 06-18-PLAN.md — RenderProfile + ChainScheduler + ThreadUnitOfWork attachment step with a durable commit boundary and failure-injection tests (Wave 5)
- [ ] 06-19-PLAN.md — Runner: cast voices, attachments in relevant waves with committed-only merge, side world, non-relevant stage (Wave 6)
- [ ] 06-20-PLAN.md — Chronological attachment repair inside the wave loop (per-message harvest, one commit line, late-repair constraints) + Phase 5 upgrade path (Wave 7)
- [ ] 06-21-PLAN.md — End-to-end parity suite on synthetic and production-planner plans, honest about what fake output proves (Wave 8)
- [x] 06-22-PLAN.md — Generate Corpus layout (tiles, Stages, banners, Ready card) + 144-DPI renders (Wave 3)
- [ ] 06-23-PLAN.md — Generate Corpus run wiring (media, entry/banners, estimate, progress, reopen rows) (Wave 8)
- [ ] 06-24-PLAN.md — Case Summary counts + media prices dialog row + 144-DPI renders (Wave 5)
- [ ] 06-25-PLAN.md — Phase gate: full/render suites + 144-DPI inspection, live run, controlled failure/Continue, multi-message repair and Phase 5 upgrade exercises (Wave 9)

### Phase 7: Scale, Cost, QA & Cutover
**Goal**: The user can run a full 5,000-email case affordably and verify its quality, and the case-driven flow is the only flow in the app.
**Depends on**: Phase 6
**Requirements**: RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05
**Success Criteria** (what must be TRUE):
  1. The user can turn on Batch API execution for OpenAI or Anthropic. Submitted batches are tracked on disk, and after an app restart their results are collected and failed or expired items are re-run.
  2. Shared case-guide context uses prompt caching. The cost display reflects cache writes, cache reads and batch discounts, and the user can edit pricing per model.
  3. A roughly 5,000-email run completes with the expected file and answer-key counts and without running out of memory.
  4. The user can run a blind-review pass that flags label disagreements for relabel or regeneration, a metadata-only leakage audit with a reported result, and timeline/value scans that flag future-fact references or canonical-value contradictions.
  5. The old topic/storyline wizard steps and their dead generation code are gone, and the app opens directly into the case-driven flow.
**Plans**: TBD
**UI hint**: yes

### Phase 8: Ancillary Discovery Documents
**Goal**: The user can optionally generate realistic discovery documents that are consistent with the case guide.
**Depends on**: Phase 3 (off the corpus critical path; scheduled last)
**Requirements**: ANC-01, ANC-02, ANC-03, ANC-04, ANC-05
**Success Criteria** (what must be TRUE):
  1. The user can pick key witnesses and generate deposition transcript excerpts with numbered lines and pages.
  2. The user can generate Requests for Production and Interrogatories keyed to the issues, and Requests for Admission keyed to the key facts.
  3. All ancillary documents use the same names, dates, amounts and allegations as the core document, cast and facts, and export as .docx.
**Plans**: TBD
**UI hint**: yes

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8. Phase 8 depends only on Phase 3 and can be pulled forward if desired.

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation & Provider Gateway | 12/13 | In Progress|  |
| 2. Case Scenario & Core Document | 9/9 | Complete    | 2026-10-01 |
| 3. Case Guide | 20/20 | Complete    | 2026-10-02 |
| 4. Corpus Plan & Answer Key | 22/22 | Complete    | 2026-10-03 |
| 5. Relevant Evidence Generation & Narrative Ledger | 17/17 | Complete    | 2026-10-03 |
| 6. Attachments & Non-Relevant Corpus | 17/25 | In Progress|  |
| 7. Scale, Cost, QA & Cutover | 0/TBD | Not started | - |
| 8. Ancillary Discovery Documents | 0/TBD | Not started | - |
