# Feature Research

**Domain:** Synthetic eDiscovery case + evidence-corpus generator (legal case guide -> timeline-driven email corpus -> answer key) for demo, training and TAR/AI-classifier testing
**Researched:** 2026-09-29
**Confidence:** MEDIUM overall. Legal document anatomy and discovery rules: HIGH (FRCP and well-established practice). Answer-key/coding-field conventions: MEDIUM-HIGH (Relativity docs, TREC Legal Track). Corpus realism statistics (thread depth, attachment ratio, etc.): LOW-MEDIUM (training knowledge plus partial research; no authoritative published distribution found — treat numbers as sensible defaults, not facts).

---

## Domain Reference (what "realistic" means for each artifact)

This section is the checklist the prompts and reviewers should hold generated artifacts against. It is what users with legal/eDiscovery backgrounds will immediately notice if missing.

### A. Core document: Complaint (federal / generic civil)

Standard anatomy, in order (HIGH confidence, standard pleading practice; FRCP 8, 10, 11):

1. **Caption** — court name ("UNITED STATES DISTRICT COURT / FOR THE [DISTRICT] OF [STATE]"), parties block (Plaintiff(s) v. Defendant(s), with entity type e.g. "a Delaware corporation"), "Case No. ____", document title ("COMPLAINT FOR BREACH OF CONTRACT, NEGLIGENCE AND ... ; DEMAND FOR JURY TRIAL").
2. **Introduction / Nature of the Action** — 1-5 paragraphs telling the story in summary.
3. **Parties** — one numbered paragraph per party: name, entity type, state of incorporation/principal place of business (or residence), role in the story.
4. **Jurisdiction and Venue** — subject-matter basis (diversity 28 U.S.C. 1332 with amount-in-controversy > $75,000, or federal question 1331), personal jurisdiction, venue (28 U.S.C. 1391).
5. **Factual Allegations** — the bulk; **consecutively numbered paragraphs** across the whole document, with sub-headings by topic/phase, specific dates, amounts, named individuals, references to documents/emails ("On March 14, 2023, Smith emailed Jones stating ...") and exhibits.
6. **Causes of Action / Counts** — "COUNT I — Breach of Contract (Against Defendant X)"; each count opens with an incorporation paragraph ("Plaintiff realleges and incorporates paragraphs 1 through 87"), then pleads each element, then damages.
7. **Prayer for Relief** — "WHEREFORE, Plaintiff respectfully requests..." with lettered items (compensatory damages, consequential damages, punitive where available, pre/post-judgment interest, attorneys' fees and costs, injunctive/declaratory relief, other relief).
8. **Jury Demand** (if applicable).
9. **Signature block** — date, firm, attorney name, bar number, address, phone, email, "Attorneys for Plaintiff".
10. Optional: **Verification** (required for some petitions/verified complaints), **Exhibit list**.

**Petition variant** (state-court terminology, e.g. Texas "Plaintiff's Original Petition"): same structure but typically adds a **Discovery Control Plan level** paragraph and a **statement of relief sought within a monetary range** (Tex. R. Civ. P. 190 / 47), "Conditions Precedent" paragraph, "Request for Disclosure", and uses "Petitioner/Respondent" or "Plaintiff/Defendant" depending on proceeding type (family/probate use Petitioner). MEDIUM confidence on exact state specifics — prompt should pick a jurisdiction and be internally consistent rather than try to be jurisdiction-perfect.

**Criminal / regulatory scenarios** (e.g. the "Breaking Bad conspiracy" example): a civil complaint is the wrong vehicle. Realistic options are an **Internal/Government Investigation Memo**, an **Indictment** (counts under e.g. 21 U.S.C. 846 / 18 U.S.C. 1956, overt acts section), or an **Affidavit in Support of Criminal Complaint** (agent background, probable cause narrative). The PROJECT scopes Complaint/Petition/Investigation Memo — recommend the Investigation Memo covers criminal/regulatory scenarios in v1 and Indictment is a v1.x addition.

### B. Core document: Investigation Memo (internal/regulatory/criminal)

Standard anatomy (MEDIUM-HIGH; common law-firm/compliance practice):

1. **Header** — "PRIVILEGED & CONFIDENTIAL / ATTORNEY-CLIENT COMMUNICATION / ATTORNEY WORK PRODUCT"; TO / FROM / CC / DATE / RE.
2. **Executive Summary** — conclusions up front.
3. **Background** — organization, business line, how the matter arose (hotline tip, audit finding, subpoena, whistleblower).
4. **Scope and Methodology** — custodians, date range, data sources collected, search terms, interviews conducted (this maps directly onto the corpus: custodians and date range should match what is generated).
5. **Key Individuals** — maps to the Cast.
6. **Chronology / Factual Findings** — dated, maps to the Key Facts timeline.
7. **Findings by Issue / Allegation** — each issue with supporting evidence, maps to issue codes.
8. **Legal Analysis / Exposure** — applicable statutes/policies.
9. **Conclusions and Recommendations** — remediation, disclosure, discipline.
10. Appendices — interview list, document index.

### C. Deposition transcripts

Conventions (HIGH for layout norms; state rules vary on lines/page):

- **Cover/caption page** — court, case caption, case no., "DEPOSITION OF [NAME]", date, time, location ("taken remotely via Zoom" is now common), reporter name and CSR number.
- **Appearances page** — counsel for each party (and "ALSO PRESENT: videographer", corporate representative).
- **Index** — examinations by attorney with page numbers; **Exhibit index** (exhibit no., description, page marked).
- **Stipulations** (optional) and oath ("[Name], having been first duly sworn, testified as follows:").
- **Body** — Q/A format, `Q.` / `A.` lines, speaker labels for colloquy (`MR. SMITH:`), "EXAMINATION BY MR. SMITH:" headings, objections in the form "Objection, form." / "Objection; foundation." / instructions not to answer on privilege, "(Deposition Exhibit 12 was marked for identification.)", "(Recess taken.)", time stamps for going on/off the video record.
- **Layout** — **25 numbered lines per page** is the dominant deposition convention (California statewide minimum 25; some official courts 26/28), left-margin line numbers, page numbers; citations are "page:line" (e.g. 45:3-17). Rendering real line-numbered pages in .docx is a meaningful complexity item.
- **Close** — "(Whereupon the deposition concluded at 4:12 p.m.)", **witness signature/errata page**, **reporter's certificate** (with signature line, CSR number).
- **Realism detail users notice:** the witness's testimony is consistent with (or plausibly evasive about) the key facts; exhibits are **real documents from the generated corpus** referenced by their ID/Bates-like number; typical length 150-300+ pages in reality — a synthetic one should be a representative excerpt (20-60 pages) and say so, not attempt full length.

### D. Written discovery: RFPs, RFAs, Interrogatories

Shared structure (HIGH): caption; title with propounding/responding party and set number ("PLAINTIFF ACME CORP.'S FIRST SET OF REQUESTS FOR PRODUCTION TO DEFENDANT BUILDCO, INC."); preliminary statement citing the rule (FRCP 34 / 36 / 33) and 30-day response time; **Definitions** ("Document", "Communication", "Concerning", "You/Your", "Person", "Identify", named project/defined terms); **Instructions** (relevant time period, ESI form of production, privilege-log requirement, continuing duty to supplement under Rule 26(e)); numbered requests; signature block with date and certificate of service.

- **RFPs (Rule 34):** "REQUEST FOR PRODUCTION NO. 1: All Documents and Communications Concerning ..." — typically 20-80 requests; should track the issues/counts and name custodians, the project, date ranges. RFPs are what **define relevance** in real review — the answer key's issue codes should be traceable to specific RFP numbers when RFPs are generated.
- **RFAs (Rule 36):** "REQUEST FOR ADMISSION NO. 1: Admit that on June 3, 2023, You received the email attached as Exhibit A." — one discrete fact each; include **genuineness-of-document** RFAs referencing corpus documents. No federal numerical cap (local rules often cap, e.g. 25-50).
- **Interrogatories (Rule 33):** **max 25 including discrete subparts** without leave (federal). "INTERROGATORY NO. 1: Identify all Persons with knowledge of ...", "State all facts supporting Your contention that ...". Answers require **verification** under oath (only relevant if responses are generated).
- Responses/objections to these requests are a separate document type (see Anti-Features / v2).

### E. eDiscovery answer key / coding conventions

What review platforms and TAR evaluations actually use (MEDIUM-HIGH; Relativity docs, TREC Legal Track):

- **Responsiveness** — binary positive/negative choice (Relativity Active Learning requires at least one positive and one negative choice). TREC used per-topic relevance judgments made against a Topic Authority's **coding manual**.
- **Issue codes** — multi-select tags mapped to allegations/counts/RFPs.
- **Privilege** — Attorney-Client / Work Product / Not Privileged (+ "partially privileged -> redact").
- **Hot / Key document** flag.
- **Confidentiality designation** (per protective order: Confidential / Highly Confidential-AEO) — optional.
- **Family relationships** — email parent + attachments share a **Family/Group Identifier**; coding is usually **propagated at family level** (if an attachment is responsive, the whole family is produced). Answer key must list attachments as their own rows with a ParentID/FamilyID, and state both doc-level and family-level relevance.
- **Thread/conversation ID** — for email threading and "inclusive email" demos.
- **Duplicates** — real collections contain the same message in multiple custodians' mailboxes; dedupe (global vs. custodial) is a standard demo step.
- **Richness/prevalence** — real-world responsive rates are frequently low (often 1-10%, sometimes <1%). Demo sets usually want higher richness (10-30%) so relevant material is findable; the user-controlled ratio covers this.
- **Seed / training / control sets** — TAR 1.0 workflows use a random control set and a judgmental seed set; TAR 2.0 (continuous active learning) just needs labels to simulate reviewer decisions. An answer key with full labels supports both.

### F. Corpus realism parameters (LOW-MEDIUM; defaults to tune, not facts)

| Dimension | Realistic behavior | Suggested default |
|-----------|-------------------|-------------------|
| Custodian distribution | Power-law: a few key custodians hold most volume; many peripheral senders | 5-15 key custodians (preset-driven) + 20-100 ancillary senders; top 3 custodians ~40-50% of volume |
| Thread depth | Most conversations are 1-3 messages; long tail of 10-25-message threads around contested events | ~50% singletons (incl. newsletters/notifications), ~35% 2-5, ~15% 6+ |
| Reply patterns | Mix of Reply, Reply-All, Forward; forwards to new people who were not on the original (esp. to counsel, to a boss, to personal accounts) | ~60% reply / ~25% reply-all / ~15% forward of non-root messages |
| Recipients | 1 To typical; CC common in management chains; BCC rare but evidentiary | Avg 1-3 recipients; occasional distribution lists |
| Attachments | Business email: roughly 15-30% carry attachments; versions of the same doc recur across threads | 20% default; document version chains (existing feature) |
| Timing | Weekday business hours dominate, sender time zones, lulls on weekends/holidays; **volume bursts around key events** (deadlines, incidents, litigation hold) | Weight dates by key-fact density |
| Noise types | Same-custodian daily business; ancillary characters; newsletters/marketing; IT/HR notifications; calendar auto-mail; personal (family, fantasy football, lunch); spam | User-set mix |
| Near-miss decoys | Same project names, same people, same keywords (e.g. "window leak" in a warranty email unrelated to the alleged defect), same time window — but not about the alleged conduct | 5-15% of non-relevant; must be labeled as such in the key |
| Evidentiary texture | Ambiguity, euphemism, "let's discuss by phone", deleting/"don't put this in email", personal-account forwarding, after-the-fact rationalization | Prompt guidance on relevant threads |
| Privilege | In-house or outside counsel on threads; "Privileged & Confidential" headers; forwarding to counsel after the fact | Small % of relevant + some non-relevant |

---

## Feature Landscape

### Table Stakes (Users Expect These)

Missing any of these and legal/eDiscovery users (demo engineers, trainers, litigation support, TAR evaluators) will judge the dataset unusable.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Free-text scenario input + complexity preset (Simple/Standard/Complex) | Entry point; presets must visibly change # parties, counts, cast size, timeline span, doc length | LOW | Preset is a parameter object feeding every downstream prompt; keep it explicit and shown to user |
| Core document type chosen to fit scenario (Complaint / Petition / Investigation Memo) | A civil complaint for a criminal-conspiracy scenario reads as wrong to any lawyer | MEDIUM | Auto-suggest type + allow override; jurisdiction chosen once and reused everywhere |
| Legally-structured core document (caption, parties, jurisdiction/venue, numbered factual paragraphs, counts with elements + incorporation, prayer, jury demand, signature) | The "realistic and comprehensive" bar; anyone in the field recognizes this skeleton | MEDIUM-HIGH | Generate in sections (outline -> sections) to get length without truncation; consecutive paragraph numbering must be enforced in code, not trusted to the model |
| Review / edit / regenerate core document before continuing | Guide quality determines corpus quality; cheapest place to fix | MEDIUM | Section-level regenerate is much more useful than whole-doc regenerate |
| Structured Cast of Key Characters (name, title, org, email, active date range, involvement summary, side/role) | Needed for custodians, addresses and the answer key | MEDIUM | Must be derived from, and consistent with, the core doc; editable grid (existing grid pattern) |
| Dated Key Facts timeline with stable IDs, linked to characters and issues/counts | The backbone of timeline-driven generation and the answer key's "linked key fact" column | MEDIUM | Each fact: ID, date (or range), description, participants, issue code(s), evidentiary weight (hot/supporting) |
| Issue-code taxonomy derived from counts/allegations | Answer key needs a controlled vocabulary; reviewers code by issue | LOW-MEDIUM | Generate once from the core doc; every fact, email and ancillary doc references these codes |
| Consistency across all artifacts (names, titles, email addresses, dates, amounts, org names) | Contradictions are the #1 tell of synthetic data | HIGH | Grounding all prompts in structured case-guide data (not prose) is the mechanism; add a deterministic validator pass (unknown names/addresses, dates outside character date ranges) |
| Export case guide (core doc, cast, key facts) to .docx | Demo/training users hand these to attendees | LOW-MEDIUM | Reuse OfficeDocumentService; also export machine-readable JSON (see below) |
| Timeline-driven relevant threads (each thread anchored to one or more key facts, with correct participants for that date) | Core Value; fixes the existing "lose the thread" failure | HIGH | Also include **pre-event and post-event** chatter (planning before, cover-up/rationalization after) — real evidence is spread around the event, not only on its date |
| User-controlled relevant vs. non-relevant ratio | Richness is the key TAR/demo variable | LOW | Show resulting counts; sensible default 15-25% relevant for demos |
| Non-relevant noise categories: same-custodian business, ancillary characters, newsletters/notifications/spam, personal | Real mailboxes; a corpus of only case emails is useless for classification demos | MEDIUM | Cheap, template-ish content — prime candidate for Batch API and a cheaper model |
| Near-miss decoys | Without them, keyword search trivially solves the dataset and classifier demos are meaningless | MEDIUM | Must share vocabulary/people/time window; labeled distinctly in the key (e.g. RelevanceCategory = "Decoy") |
| Realistic email mechanics (threading headers, RE:/FW:, quoting, signatures, reply-all/forward, CC, time zones, business-hours timestamps) | Already largely exists; regressions would be noticed immediately | LOW (existing) | Add: forwards to new recipients, date bursts near events, weekday weighting |
| Attachments as evidence (docs/spreadsheets/invites/images/voicemail) driven by case guide | Evidence frequently lives in attachments (contracts, change orders, invoices, spreadsheets) | MEDIUM (existing builders) | Attachment content must reference the same facts/amounts; attachments get their own answer-key rows |
| Answer key CSV/Excel: one row per document (emails **and** attachments) | Required to demo/score classification | MEDIUM | Minimum columns: DocID, FilePath, FamilyID, ParentDocID, ThreadID, Custodian, From, To, CC, DateSent, Subject, DocType, Relevant (Y/N), RelevanceCategory, IssueCodes, KeyFactIDs, CharactersInvolved, Rationale (1 sentence). Excel with a second "Issues" and "Key Facts" sheet as lookups |
| Stable document IDs (control numbers, e.g. RD000001) in file names and key | Every downstream reference (key, depo exhibits, RFAs) needs a stable handle | LOW | Assign at save time deterministically; embed in .eml header (X-header) too |
| Scale to ~5K emails with incremental save and per-item failure isolation | 5K runs take hours and cost real money; losing a run is unacceptable | HIGH | Already in Active requirements; answer key rows should be appended incrementally as threads are saved |
| Fictional entities only (fictional people, companies, domains) | Legal/ethical and trust expectation; the EDRM Enron set's PII controversy shows the cost of real data | LOW | Use reserved/fictional domains; prompt-level rule plus a name-collision warning for famous names |

### Differentiators (Competitive Advantage)

Aligned with the Core Value (every relevant email faithful to one case story). Nothing public combines pleading + timeline + labeled corpus: EDRM Enron has real data but no case story and only topic-specific TREC judgments; existing synthetic generators (e.g. JW2WW/ediscovery-email-generator) produce unlabeled ingest-test mail with no narrative.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Hot-document designation | Demos of "find the smoking gun" and prioritized review; trainers need known key docs | LOW | Flag in key; driven by key facts marked high-weight; ensure a controlled number (e.g. 10-40) |
| Privilege-bearing documents + privilege column in key | Privilege review is the other half of real review; attorney-client threads make the set dramatically more realistic | MEDIUM | Add counsel characters to cast; some relevant threads forwarded to counsel; key column Privilege = AC/WP/None. Enables privilege-log demos later |
| Review protocol / coding manual document | Mirrors TREC's Topic Authority coding manual and real review protocols; tells reviewers/AI what "relevant" means for this case — essential for fair classifier evaluation | LOW-MEDIUM | Generated from core doc + issue codes (+ RFPs if present): background, issue definitions, relevance criteria, decoy examples, privilege guidance |
| Ancillary docs cross-referencing the corpus (depo exhibits = corpus DocIDs; RFAs authenticating specific corpus emails; RFPs mapped to issue codes) | Makes the whole package feel like one real matter; lets a trainer walk from a depo exhibit to the native email | MEDIUM-HIGH | Requires corpus generation (or at least hot-doc selection) **before** depositions/RFAs if exhibits are to cite real DocIDs — ordering decision for roadmap |
| Deposition transcripts in true transcript format (caption, appearances, index, exhibit index, 25 numbered lines/page, Q/A, objections, reporter certificate, errata) | Visually indistinguishable from a real transcript excerpt; high "wow" per dollar | MEDIUM-HIGH | Line-numbered pagination is the hard part: generate Q/A as structured turns, then lay out deterministically in code (wrap to ~56-65 chars/line, 25 lines/page) |
| Witness testimony with realistic tone (evasive, "I don't recall", impeachment by prior email) | Training value for deposition-prep demos | MEDIUM | Feed witness's own emails as context; a few impeachment sequences using hot docs |
| Custodial duplicates (same message saved into each participant's custodian folder) | Enables dedupe / email-threading / "inclusive" demos, the standard first steps in any platform demo | LOW-MEDIUM | Already has per-sender folders; add per-custodian copies with same Message-ID; key gets DupGroupID/MD5Hash |
| Suggested seed/control-set column in answer key | Lets users run a TAR 1.0 demo immediately (pre-picked judgmental seed + random control sample) | LOW | Pure post-processing of the key |
| Relevance rationale per document (why relevant / why decoy) | Lets demoers explain classifier hits and misses; audit of AI review | LOW if captured during generation | Capture in the same generation call, not a second pass |
| Concurrent overlapping storylines among same people without contradiction | Complex cases are exactly where the old flow failed; this is the headline improvement | HIGH | Timeline + per-character state ("what X knows as of date D") summaries per batch |
| Machine-readable case bundle (case.json: preset, core doc sections, cast, facts, issues, doc manifest) | Enables reuse, re-export, regeneration of ancillary docs later, and integration testing | LOW | Not pause/resume — just a sidecar file; cheap insurance |
| Evidentiary texture controls (euphemism, "call me", personal-email forwarding, deletion requests) | Separates "emails about the topic" from "emails that are evidence" | LOW-MEDIUM | Prompt guidance keyed to fact weight |
| Corpus stats summary at end (counts by category, custodian, issue, month; richness actually achieved) | Demoers need to quote numbers; verifies the ratio was honored | LOW | Extra sheet in answer-key workbook |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Real people / real companies / real case names as characters | "Inspired by" scenarios, extra realism | Defamation/right-of-publicity risk; distribution of datasets becomes unsafe; mirrors the EDRM Enron PII backlash | Fictional analogs; allow "inspired by" tone only; warn on famous-name collisions |
| Realistic PII (valid-format SSNs, card numbers, DOBs) sprinkled in | PII-detection demos | Fake-but-valid numbers can collide with real people; Luhn-valid cards look like leaked data | If ever added, use obviously-invalid reserved ranges (e.g. SSN 000-/9xx) and label as PII in key — defer to v2 |
| Jurisdiction-perfect pleadings for every state/court | "Make it exactly like a Texas petition" | Endless rule surface; LLM will hallucinate local rules; no validation possible | Pick one jurisdiction per case, be internally consistent, include a disclaimer; offer federal + generic state styles |
| Full-length (200+ page) deposition transcripts | Realism | Enormous token cost, drift, repetitive filler | Representative excerpt (20-60 pages) with correct formatting, labeled "Excerpt" |
| Responses & objections to discovery, privilege logs, production volumes with Bates-stamped PDFs/TIFFs, DAT/OPT load files | "Complete litigation package" | Each is a separate artifact family with its own conventions; load files are explicitly out of scope for v1 | CSV/Excel key now; load files + privilege log are natural v2 once DocIDs and privilege column exist |
| Chat/Slack/SMS/Teams, loose file shares | Modern collections include them | Explicitly out of scope; different formats (RSMF etc.) and threading models | Keep email + attachments; note RSMF as a future direction |
| Interactive per-email editing of the 5K corpus | Control | Unmanageable UI; edits break continuity and the key | Edit at the guide level (core doc, cast, facts, ratio) and regenerate |
| Statistically matching Enron distributions exactly | "Scientific realism" | Over-engineering; no one validates the distribution; costs tuning time | Reasonable parameterized defaults (table above) exposed as a few knobs |
| Forensic-grade headers (Received chains, DKIM, PST/MSG containers) | Forensic realism | Processing-tool testing is a different product; PST writing on .NET is non-trivial | Keep .eml with sane headers; add X-ReelDiscovery-DocID for traceability |
| Second LLM pass to "grade" every document's relevance after generation | Accuracy of the key | Doubles cost; the generator already knows the intended label | Label at generation time (the document was generated *for* a fact or *as* noise); only spot-check decoys |
| Multi-language corpora | International matters | Multiplies prompt/QA surface | Defer; English only |
| Up-front cost estimator, pause/resume from project file | Nice UX | Explicitly out of scope for v1 | Live cost ticker + incremental save + case.json sidecar |

---

## Feature Dependencies

```
Scenario + Complexity Preset
    └──requires──> (nothing)

Core Document (Complaint/Petition/Memo)
    └──requires──> Scenario + Preset (+ jurisdiction/doc-type choice)

Issue-Code Taxonomy
    └──requires──> Core Document (counts / findings-by-issue)

Cast of Key Characters ──requires──> Core Document
Key Facts Timeline ──requires──> Core Document, Cast, Issue Codes

Case Guide Export (.docx + case.json) ──requires──> Core Doc, Cast, Facts, Issues

Review Protocol / Coding Manual ──requires──> Issue Codes, Key Facts (+ RFPs if generated)

RFPs ──requires──> Issue Codes, Cast, Facts   (RFPs define relevance; generate before protocol)
Interrogatories ──requires──> Issue Codes, Cast
RFAs (fact-based) ──requires──> Key Facts
RFAs (document-genuineness) ──requires──> Corpus DocIDs (hot docs)
Depositions (basic) ──requires──> Cast, Key Facts
Depositions (with real exhibits) ──requires──> Corpus DocIDs (hot docs) 

Relevant Thread Generation ──requires──> Key Facts, Cast, Issue Codes, provider abstraction, incremental save
Non-Relevant Filler ──requires──> Cast (custodians) + ancillary characters; enhanced by Batch API
Near-Miss Decoys ──requires──> Key Facts + Issue Codes (to know what to be *near* but not *on*)
Attachments as Evidence ──requires──> Relevant Threads (planned flags) + Key Facts
Stable DocIDs ──requires──> Save pipeline
Answer Key ──requires──> DocIDs, FamilyIDs, ThreadIDs, labels captured at generation time
Hot Docs / Privilege / Seed columns ──enhance──> Answer Key
Custodial Duplicates ──enhances──> Answer Key (DupGroupID) ; conflicts with simple per-sender-folder-only output

Section-level regenerate ──enhances──> Core Document review
Editing Cast/Facts ──invalidates──> anything generated downstream (ancillary docs, corpus)
```

### Dependency Notes

- **Issue codes and fact IDs are the spine.** The answer key, decoys, RFPs, protocol and depositions all reference them. They must exist as structured data with stable IDs before any corpus generation starts; retrofitting IDs later breaks the key.
- **Ancillary docs split into two tiers by dependency:** RFPs/Interrogatories/fact-RFAs/basic depositions only need the case guide and can come *before* the corpus; exhibit-citing depositions and genuineness RFAs need corpus DocIDs and must come *after* (or after at least the relevant/hot-doc slice). Recommend: generate ancillary docs after the corpus, or split the phase.
- **Labels must be captured at generation time**, not inferred afterward. A thread generated for fact KF-012 is relevant to KF-012's issues by construction; a decoy is a decoy by construction. This makes the answer key nearly free and exact.
- **Editing upstream invalidates downstream.** Changing cast/facts after ancillary docs or corpus exist must warn and clear/regenerate dependents (the wizard's Back navigation needs this rule).
- **Custodial duplicates conflict with the current per-sender folder layout** (one copy per message). If adopted, output layout becomes per-custodian mailbox folders with Sent/Inbox copies; decide before building the answer key writer.
- **Privilege depends on counsel characters in the cast** — must be decided at cast generation time, not bolted on to the corpus.

---

## MVP Definition

### Launch With (v1)

- [ ] Scenario input + complexity preset — entry point
- [ ] Core document with legal structure, doc-type auto-suggestion (Complaint / Petition / Investigation Memo), section-level review/edit/regenerate — the guide
- [ ] Issue-code taxonomy derived from counts/findings — spine for everything downstream
- [ ] Cast (with custodian flag, email, date range, role) and Key Facts timeline with stable IDs, editable — drives generation
- [ ] Case guide export (.docx) + case.json sidecar — deliverable and insurance
- [ ] Timeline-driven relevant threads with continuity + overlapping storylines — Core Value
- [ ] Non-relevant filler (business, ancillary, newsletters/spam, personal) + near-miss decoys, user-set ratio — makes it a real classification dataset
- [ ] Case-driven attachments using existing builders — evidence lives in attachments
- [ ] Stable DocIDs, FamilyIDs, ThreadIDs; answer key CSV/Excel with columns listed above incl. RelevanceCategory, IssueCodes, KeyFactIDs, Rationale, **Hot** flag — the payoff
- [ ] Incremental save, per-item failure isolation, Batch API for filler, Claude provider — scale and cost
- [ ] RFPs, Interrogatories, RFAs (fact-based) — optional, straightforward given case guide
- [ ] Deposition transcript excerpts with correct transcript formatting — optional, high perceived value

### Add After Validation (v1.x)

- [ ] Review protocol / coding manual document — trigger: users running classifier/TAR evaluations
- [ ] Privilege column + counsel characters — trigger: privilege-review demos requested
- [ ] Depositions citing corpus DocIDs as exhibits; genuineness RFAs — trigger: once DocIDs are stable
- [ ] Custodial duplicates + DupGroupID — trigger: dedupe/threading demos
- [ ] Seed/control-set suggestion columns; corpus stats sheet — trigger: TAR demo users
- [ ] Indictment / criminal complaint affidavit core doc type — trigger: criminal scenarios common

### Future Consideration (v2+)

- [ ] DAT/OPT load files, Bates numbering, privilege log — out of scope v1
- [ ] Responses & objections to written discovery
- [ ] Non-email sources (chat/RSMF, loose files)
- [ ] PII-bearing documents with safe synthetic identifiers for PII-detection demos
- [ ] Pause/resume from project file, up-front cost estimate

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Structured core document w/ section regenerate | HIGH | MEDIUM | P1 |
| Issue codes + fact IDs as structured data | HIGH | LOW | P1 |
| Cast + Key Facts editable | HIGH | MEDIUM | P1 |
| Timeline-driven relevant threads w/ continuity | HIGH | HIGH | P1 |
| Non-relevant filler + ratio control | HIGH | MEDIUM | P1 |
| Near-miss decoys | HIGH | MEDIUM | P1 |
| Answer key (full columns, attachments as rows) | HIGH | MEDIUM | P1 |
| Stable DocIDs / FamilyIDs | HIGH | LOW | P1 |
| Incremental save + failure isolation | HIGH | MEDIUM | P1 |
| Case guide .docx export + case.json | MEDIUM | LOW | P1 |
| Hot-doc flag | MEDIUM | LOW | P1 |
| RFPs / Interrogatories / RFAs | MEDIUM | LOW-MEDIUM | P2 |
| Deposition transcripts (formatted excerpts) | MEDIUM-HIGH | MEDIUM-HIGH | P2 |
| Review protocol / coding manual | MEDIUM-HIGH | LOW | P2 |
| Privilege column + counsel characters | MEDIUM | MEDIUM | P2 |
| Corpus stats sheet, seed/control columns | MEDIUM | LOW | P2 |
| Exhibits cross-referencing corpus | MEDIUM | MEDIUM | P2 |
| Custodial duplicates | MEDIUM | LOW-MEDIUM | P3 |
| Indictment / criminal affidavit doc type | LOW-MEDIUM | LOW | P3 |
| Load files / privilege log / Bates | MEDIUM | HIGH | P3 (out of scope v1) |

**Priority key:** P1 must have for launch; P2 should have; P3 future.

---

## Competitor Feature Analysis

| Feature | EDRM Enron (+ TREC Legal Track) | Generic synthetic email generators (e.g. JW2WW/ediscovery-email-generator) | Vendor demo workspaces (Relativity/Everlaw etc.) | ReelDiscovery approach |
|---------|------------------------------|------------------------------------------|-----------------------------------------------|------------------------|
| Case narrative / pleading | None (real FERC investigation data); TREC provided one-sentence production requests + coding manual per topic | None | Not publicly documented (LOW confidence; appear to be curated/private) | Generated pleading/memo + cast + dated facts |
| Relevance labels | TREC qrels for specific topics (401-403 in 2011), sampled/pooled assessments only | None | Unknown | Complete per-document labels by construction |
| Issue codes / hot / privilege | Not provided | None | Unknown | Issue codes + hot (v1), privilege (v1.x) |
| Families/attachments | Yes (real ~455K emails + 230K attachments in v2) | Attachments, nested forwards, ZIPs | Yes | Yes, with FamilyID in key |
| Noise / decoys | Real noise, uncontrolled richness | Content scraped from public sources, no semantics | — | Controlled ratio + deliberate near-miss decoys |
| Size | ~0.5-0.7M items, 150+ custodians | Configurable, small batches | — | ~5K emails, preset-driven custodians |
| PII / ethics | Known PII problem; Nuix/EDRM cleansed >10K items, criticized as incomplete | Invented addresses | — | Fictional by design |
| Ancillary discovery docs | None | None | — | Depos, RFPs, RFAs, interrogatories consistent with case |

---

## Sources

- TREC 2009 Legal Track overview (topic authorities, coding manuals, assessment process): https://trec.nist.gov/pubs/trec18/papers/LEGAL09.OVERVIEW.pdf — HIGH
- TREC 2011 Legal Track learning task (topics 401-403, coding manual): https://trec.nist.gov/pubs/trec20/papers/OpenText.legal.update.pdf ; https://pages.nist.gov/trec-browser/trec20/legal/proceedings/ — HIGH
- "Learning from Litigation" (EDRM Enron v2 stats: 455,449 emails / 230,143 attachments): https://arxiv.org/pdf/2405.19164 — MEDIUM
- FRCP Rule 33 (25-interrogatory limit incl. subparts): https://www.law.cornell.edu/rules/frcp/rule_33 — HIGH
- Rule 36 RFAs (no federal numeric cap; local rules vary): https://en.wikipedia.org/wiki/Request_for_admissions ; E.D. Mo. L.R. 3.03: https://www.moed.uscourts.gov/sites/moed/files/documents/local-rules/03_03.pdf — MEDIUM
- Federal discovery/deposition practice: https://plaintiffmagazine.com/recent-issues/item/discovery-and-deposition-practice-in-federal-court — MEDIUM
- Transcript format (25 lines/page, CA 16 CCR 2473, NCRA guidelines): https://stenobox.com/guide-california-transcript-format.html ; https://legalwritingexperts.com/civil-litigation/deposition-transcript ; https://www.cfc.uscourts.gov/sites/cfc/files/transcript_format_requirements.pdf — MEDIUM-HIGH
- Relativity Active Learning guide (positive/negative responsiveness choices, issue/privilege fields): https://help.relativity.com/PDFDownloads/Server2024_PDF/Relativity%20-%20Assisted%20Review%20Active%20Learning%20Guide.pdf — HIGH
- Relativity family propagation: https://help.relativity.com/RelativityOne/Content/Relativity/Fields/Applying_propagation_to_documents.htm ; https://help.relativity.com/RelativityOne/Content/Relativity/aiR_for_Privilege/Propagate_results_to_family_members.htm — HIGH
- Review protocol contents: https://help.relativity.com/RelativityOne/Content/Getting_Started/Review_Template/Reviewing_and_producing_documents.htm — MEDIUM
- TAR richness/elusion/recall: https://edrm.net/wp-content/uploads/2019/06/Draft-TAR-Protocol.pdf ; https://legal.fronteousa.com/considering-the-impact-of-richness-on-control-set-metrics/ ; https://aceds.org/the-elusion-illusion-and-the-ai-revolution-aceds-blog/ — MEDIUM
- EDRM Enron PII controversy / Nuix cleansing: https://complexdiscovery.com/pii-presentism-and-persistence-the-enron-data-set/ ; https://cloudnine.com/ediscoverydaily/electronic-discovery/some-additional-perspective-on-the-edrm-enron-data-set-controversy-ediscovery-trends/ — MEDIUM
- Enron corpus composition / quoting duplication: https://www.colorado.edu/ics/sites/default/files/attached-files/01-11_0.pdf ; https://enrondata.readthedocs.io/en/latest/data/edrm-enron-email-datasets/ — MEDIUM
- Existing synthetic eDiscovery email generator (no labels/narrative): https://github.com/JW2WW/ediscovery-email-generator — MEDIUM
- Complaint/petition/investigation memo anatomy, reply/attachment/thread-depth ratios: training knowledge, standard practice — MEDIUM for structure, LOW for numeric ratios (not verified against a published distribution)

---
*Feature research for: synthetic eDiscovery case + corpus generator (ReelDiscovery case-driven milestone)*
*Researched: 2026-09-29*
