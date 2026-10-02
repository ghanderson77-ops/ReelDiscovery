# Requirements: ReelDiscovery

**Defined:** 2026-09-29
**Core Value:** Every relevant email in a multi-thousand-document corpus must stay faithful to one consistent case story (the core pleading and its key-facts timeline) — the corpus reads as the actual evidence behind the allegations, not a pile of disconnected emails.

## v1 Requirements

Requirements for the case-driven generation milestone. Each maps to roadmap phases.

### Platform & Foundation

- [x] **PLAT-01**: App targets .NET 10 (net10.0-windows) with the SDK pinned via global.json and still publishes as a single-file self-contained exe
- [x] **PLAT-02**: API keys are stored encrypted with Windows DPAPI (existing Base64 key file migrated)
- [ ] **PLAT-03**: A test project exists and runs unit tests for planner, validators, answer-key writer and EML output
- [x] **PLAT-04**: Known concurrency bugs are fixed (shared `System.Random`, non-thread-safe mutation in parallel sections)
- [ ] **PLAT-05**: Dead code in the old generation path is removed as it is replaced

### AI Providers

- [x] **PROV-01**: User can choose Anthropic Claude or OpenAI as the text-generation provider and select a model
- [x] **PROV-02**: User can enter and test API keys for both providers
- [x] **PROV-03**: All text generation goes through one provider abstraction that supports strict structured (JSON-schema) outputs on both providers
- [x] **PROV-04**: Provider responses are classified (succeeded / refused / truncated / invalid / failed) so refusals and spend-limit errors are not blindly retried
- [x] **PROV-05**: Image generation uses a current OpenAI image model (gpt-image family) instead of the retired dall-e-3
- [x] **PROV-06**: When Claude is the text provider, images and voicemail use a configured OpenAI key; if none is configured those media types are disabled with a clear notice
- [ ] **PROV-07**: Token usage and cost tracking accounts for cache writes, cache reads and batch discounts, with editable pricing per model

### Case Scenario & Core Document

- [x] **CASE-01**: User can enter a free-text real-life scenario to seed a case
- [x] **CASE-02**: User can pick a complexity preset (Simple / Standard / Complex) that sets the number of parties, issues/counts, characters, timeline span and core-document length
- [x] **CASE-03**: System detects real people/companies named in the scenario, warns the user, and substitutes fictional names by default (user can override)
- [x] **CASE-04**: System generates the appropriate core document: Complaint / Petition for civil disputes, Investigation Memo for criminal or regulatory scenarios
- [x] **CASE-05**: Core document follows realistic structure (caption, parties, jurisdiction/venue, factual allegations, counts/causes of action, prayer for relief — or the memo equivalent) consistent with one chosen jurisdiction
- [x] **CASE-06**: Core document does not cite invented case law (no-fabricated-citation policy, checked automatically)
- [x] **CASE-07**: User can review, edit, and regenerate the core document before continuing

### Case Guide (Cast, Facts, Issues)

- [x] **GUIDE-01**: System extracts an issue list (allegations/counts) with stable IDs from the core document
- [x] **GUIDE-02**: System generates a Cast of Key Characters with stable IDs: name, title, organization, email address, active date range, summary of key involvement
- [x] **GUIDE-03**: Cast includes counsel characters (in-house and outside) so privileged communications can exist
- [x] **GUIDE-04**: Cast includes ancillary (non-key) characters per organization for non-relevant and background traffic
- [x] **GUIDE-05**: System generates a Summary of Key Facts & Events as a dated timeline with stable IDs, each fact linked to one or more issues, the actors involved, and who knew about it
- [x] **GUIDE-06**: System maintains a registry of canonical values (amounts, invoice/change-order numbers, document titles, project names) that all generated content must use
- [x] **GUIDE-07**: A deterministic validator checks the guide for consistency (dates within range, facts reference existing cast/issues, no orphan IDs)
- [x] **GUIDE-08**: User can review, edit, and regenerate the cast and the facts timeline
- [x] **GUIDE-09**: Editing an upstream artifact (core doc, cast, facts) warns the user and marks downstream artifacts stale
- [x] **GUIDE-10**: Case guide is saved as a case file (case.json) alongside the output
- [x] **GUIDE-11**: User can export the core document, cast and key-facts summary as .docx

### Ancillary Discovery Documents

- [ ] **ANC-01**: User can optionally generate deposition transcript excerpts for chosen key witnesses, formatted with numbered lines and pages
- [ ] **ANC-02**: User can optionally generate Requests for Production keyed to the issues
- [ ] **ANC-03**: User can optionally generate Requests for Admission keyed to the key facts
- [ ] **ANC-04**: User can optionally generate Interrogatories keyed to the issues
- [ ] **ANC-05**: Ancillary documents are consistent with the core document, cast and facts, and are exported as .docx

### Corpus Planning

- [x] **PLAN-01**: User sets the corpus size (up to ~5,000 emails) and the relevant vs. non-relevant proportion
- [x] **PLAN-02**: System plans every thread and message in code before any prose is written: date/time, sender, recipients (To/CC), reply/forward structure, linked facts and issues, relevance label, attachments
- [ ] **PLAN-03**: Relevant threads are scheduled against the key-facts timeline so overlapping issues run concurrently among shared characters without contradiction
- [ ] **PLAN-04**: Timestamps are realistic (business hours, time zones, weekends/holidays, reply latency)
- [ ] **PLAN-05**: Plan includes realistic distributions for thread depth, reply vs. reply-all vs. forward, CC patterns and attachment rate
- [x] **PLAN-06**: A validator checks the plan (counts, ratio, dates within character active ranges, every key fact covered by evidence)
- [ ] **PLAN-07**: User sees a plan summary (counts by issue/fact/relevance/custodian, date histogram) and approves it or re-plans before generation starts

### Evidence Corpus Generation

- [ ] **CORP-01**: Each email is written from a point-in-time context: only facts dated on or before it, limited to what its participants could know
- [ ] **CORP-02**: Emails use canonical registry values, and generated output is checked against the plan (participants, dates, values); violations trigger a retry
- [ ] **CORP-03**: Relevant emails evidence the alleged conduct with varied subtlety (not all "smoking guns"), in natural business voice without legal framing
- [ ] **CORP-04**: A small set of hot documents is deliberately generated for key facts
- [ ] **CORP-05**: Privileged attorney-client communications are generated with counsel characters
- [ ] **CORP-06**: Threads have correct threading (replies quote the right prior message, Message-ID/In-Reply-To/References)
- [ ] **CORP-07**: Writing style varies by character (voice, length, signature) so emails don't all sound alike
- [ ] **CORP-08**: System maintains a running internal narrative as emails are generated: after each thread, new facts and details it introduced (commitments, decisions, meeting outcomes, dates, figures, people mentioned) are extracted into a narrative ledger tagged with date, participants and related issues/facts
- [ ] **CORP-09**: Each later email's context includes the relevant narrative-ledger entries (dated before it, known to its participants, related to its issues/facts) so the story builds on what was already written
- [ ] **CORP-10**: Relevant threads are generated in chronological waves so earlier narrative is available to later threads; new content that contradicts the ledger or canonical values is flagged and retried
- [ ] **CORP-11**: The narrative ledger is saved with the case file and exportable as a readable "story so far" summary

### Non-Relevant Material

- [ ] **NOISE-01**: Non-relevant corpus includes the same custodians' daily business (other projects, scheduling, admin)
- [ ] **NOISE-02**: Non-relevant corpus includes traffic from ancillary characters
- [ ] **NOISE-03**: Non-relevant corpus includes newsletters, spam and personal email
- [ ] **NOISE-04**: Non-relevant corpus includes near-miss decoys that share project names/keywords but are not about the alleged conduct
- [ ] **NOISE-05**: Relevant and non-relevant emails are generated through the same pipeline with matched style/length/metadata distributions (no label leakage)

### Attachments

- [ ] **ATT-01**: Word, Excel and PowerPoint attachments are generated from the plan and case guide (reusing existing builders)
- [ ] **ATT-02**: Calendar invites (.ics) are generated for planned meetings
- [ ] **ATT-03**: Inline images and voicemail audio are generated where planned (subject to PROV-06)
- [ ] **ATT-04**: Attachment content is consistent with the case guide and canonical values

### Answer Key

- [ ] **KEY-01**: System produces an answer key in CSV and Excel with one row per document (emails and attachments)
- [x] **KEY-02**: Each row includes DocID, file path, custodian, date, from/to, subject, thread ID, family/parent ID, relevance category, issue codes, linked fact IDs, hot flag, privilege flag and a one-sentence rationale
- [x] **KEY-03**: Labels come from the plan at generation time, with family-level relevance propagated to attachments and quoted replies
- [x] **KEY-04**: Excel key includes a corpus statistics sheet (counts by custodian, issue, relevance category, date)
- [ ] **KEY-05**: System generates a review protocol / coding manual document describing issues and relevance criteria for reviewers

### Corpus QA

- [ ] **QA-01**: A blind-review pass classifies a sample (or all) documents without seeing labels; disagreements are flagged and can be relabeled or regenerated
- [ ] **QA-02**: A label-leakage audit checks that metadata alone cannot predict relevance and reports the result
- [ ] **QA-03**: Timeline and value scans flag emails that reference future facts or contradict canonical values

### Run Reliability & Cost

- [ ] **RUN-01**: Each completed thread (EML files plus its answer-key rows) is saved immediately; an interruption never loses finished work
- [ ] **RUN-02**: A failed item (email, attachment, image, invite, voicemail) is logged and retried or skipped without aborting the run
- [ ] **RUN-03**: Bulk generation can use the provider's Batch API (OpenAI and Anthropic) for lower cost
- [ ] **RUN-04**: Submitted batch jobs are tracked on disk so results can be collected if the app is restarted
- [ ] **RUN-05**: Shared case-guide context uses prompt caching where the provider supports it
- [ ] **RUN-06**: User can cancel a run and keep everything generated so far
- [ ] **RUN-07**: Live progress and cost display stays responsive during long runs

### Output

- [ ] **OUT-01**: EML files are organized per custodian mailbox; an email appears in each participating custodian's folder (sent and received), as in a real collection
- [ ] **OUT-02**: Each EML carries its DocID in a custom header (X-ReelDiscovery-DocID) traceable to the answer key
- [ ] **OUT-03**: The case-driven flow replaces the old topic/storyline wizard flow

## v2 Requirements

Deferred. Tracked but not in the current roadmap.

### Legal Documents

- **LEGAL-01**: Indictment / affidavit in support of criminal complaint as a core document type
- **LEGAL-02**: Responses and objections to written discovery
- **LEGAL-03**: Depositions citing corpus DocIDs as exhibits; genuineness RFAs referencing specific emails

### Review Tooling Exports

- **EXP-01**: DAT/OPT load files and Bates numbering
- **EXP-02**: Privilege log
- **EXP-03**: JSON answer key
- **EXP-04**: Seed/control-set suggestion columns and dedupe group IDs

### Workflow

- **WF-01**: Pause/resume a run from a saved project file
- **WF-02**: Up-front cost estimate before generation

### Content

- **CONT-01**: Non-email sources (chat/RSMF, loose files)
- **CONT-02**: PII-bearing documents with safe synthetic identifiers for PII-detection demos

## Out of Scope

| Feature | Reason |
|---------|--------|
| Keeping the old topic → storylines wizard | Replaced by the case-driven flow |
| Second LLM pass grading every document's relevance | Labels are known from the plan; QA samples instead of full re-grading |
| Interactive per-email editing of the corpus | Unmanageable at 5K; edits break continuity and the key — edit at guide level instead |
| Full-length (200+ page) depositions | Cost and drift; excerpts labeled as such |
| Forensic-grade headers / PST/MSG containers | Different product (processing-tool testing) |
| Jurisdiction-perfect local rules for every court | Cannot validate; one consistent jurisdiction per case |
| Multi-language corpora | English only for v1 |
| Cross-platform (macOS/Linux) | Windows WinForms app |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PLAT-01 | Phase 1 | Complete |
| PLAT-02 | Phase 1 | Complete |
| PLAT-03 | Phase 1, Phase 4 | Partial (Phase 1: harness + EML tests; planner/validator/answer-key tests in Phase 4) |
| PLAT-04 | Phase 1 | Complete |
| PLAT-05 | Phase 7 | Pending |
| PROV-01 | Phase 1 | Complete |
| PROV-02 | Phase 1 | Complete |
| PROV-03 | Phase 1 | Complete |
| PROV-04 | Phase 1 | Complete |
| PROV-05 | Phase 1 | Complete |
| PROV-06 | Phase 1 | Complete |
| PROV-07 | Phase 7 | Pending |
| CASE-01 | Phase 2 | Complete |
| CASE-02 | Phase 2 | Complete |
| CASE-03 | Phase 2 | Complete |
| CASE-04 | Phase 2 | Complete |
| CASE-05 | Phase 2 | Complete |
| CASE-06 | Phase 2 | Complete |
| CASE-07 | Phase 2 | Complete |
| GUIDE-01 | Phase 3 | Complete |
| GUIDE-02 | Phase 3 | Complete |
| GUIDE-03 | Phase 3 | Complete |
| GUIDE-04 | Phase 3 | Complete |
| GUIDE-05 | Phase 3 | Complete |
| GUIDE-06 | Phase 3 | Complete |
| GUIDE-07 | Phase 3 | Complete |
| GUIDE-08 | Phase 3 | Complete |
| GUIDE-09 | Phase 3 | Complete |
| GUIDE-10 | Phase 3 | Complete |
| GUIDE-11 | Phase 3 | Complete |
| ANC-01 | Phase 8 | Pending |
| ANC-02 | Phase 8 | Pending |
| ANC-03 | Phase 8 | Pending |
| ANC-04 | Phase 8 | Pending |
| ANC-05 | Phase 8 | Pending |
| PLAN-01 | Phase 4 | Complete |
| PLAN-02 | Phase 4 | Complete |
| PLAN-03 | Phase 4 | Pending |
| PLAN-04 | Phase 4 | Pending |
| PLAN-05 | Phase 4 | Pending |
| PLAN-06 | Phase 4 | Complete |
| PLAN-07 | Phase 4 | Pending |
| CORP-01 | Phase 5 | Pending |
| CORP-02 | Phase 5 | Pending |
| CORP-03 | Phase 5 | Pending |
| CORP-04 | Phase 5 | Pending |
| CORP-05 | Phase 5 | Pending |
| CORP-06 | Phase 5 | Pending |
| CORP-07 | Phase 5 | Pending |
| CORP-08 | Phase 5 | Pending |
| CORP-09 | Phase 5 | Pending |
| CORP-10 | Phase 5 | Pending |
| CORP-11 | Phase 5 | Pending |
| NOISE-01 | Phase 6 | Pending |
| NOISE-02 | Phase 6 | Pending |
| NOISE-03 | Phase 6 | Pending |
| NOISE-04 | Phase 6 | Pending |
| NOISE-05 | Phase 6 | Pending |
| ATT-01 | Phase 6 | Pending |
| ATT-02 | Phase 6 | Pending |
| ATT-03 | Phase 6 | Pending |
| ATT-04 | Phase 6 | Pending |
| KEY-01 | Phase 4 | Pending |
| KEY-02 | Phase 4 | Complete |
| KEY-03 | Phase 4 | Complete |
| KEY-04 | Phase 4 | Complete |
| KEY-05 | Phase 4 | Pending |
| QA-01 | Phase 7 | Pending |
| QA-02 | Phase 7 | Pending |
| QA-03 | Phase 7 | Pending |
| RUN-01 | Phase 5 | Pending |
| RUN-02 | Phase 5 | Pending |
| RUN-03 | Phase 7 | Pending |
| RUN-04 | Phase 7 | Pending |
| RUN-05 | Phase 7 | Pending |
| RUN-06 | Phase 5 | Pending |
| RUN-07 | Phase 5 | Pending |
| OUT-01 | Phase 5 | Pending |
| OUT-02 | Phase 5 | Pending |
| OUT-03 | Phase 7 | Pending |

**Coverage:**
- v1 requirements: 80 total
- Mapped to phases: 80
- Unmapped: 0

---
*Requirements defined: 2026-09-29*
*Last updated: 2026-09-29 after roadmap creation*
