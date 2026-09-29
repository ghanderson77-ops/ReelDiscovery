# Pitfalls Research

**Domain:** LLM-generated synthetic eDiscovery corpora (case guide + ~5K-email evidence corpus + answer key), .NET 8 WinForms, OpenAI + Anthropic
**Researched:** 2026-09-29
**Confidence:** MEDIUM overall. Provider API facts are HIGH, checked against current official docs. Corpus-realism and relevance-leakage pitfalls are MEDIUM: they come from established NLP shortcut-learning findings plus eDiscovery practice, and are applied to this project by reasoning. Legal and defamation risk is MEDIUM/LOW; this is not legal advice.

> Phase names below follow the likely roadmap shape implied by PROJECT.md. The roadmapper may renumber them. The labels used are:
> **P-Found** (foundation: provider abstraction, per-item failure isolation, incremental persistence, bug fixes) ·
> **P-Guide** (scenario to core doc, cast, and key-facts timeline, with review/edit) ·
> **P-Ancillary** (depositions, RFPs, RFAs, interrogatories) ·
> **P-Plan** (deterministic corpus planner: allocation of dates, senders, labels, and facts) ·
> **P-Relevant** (timeline-driven relevant thread generation) ·
> **P-Filler** (non-relevant, ancillary, newsletters/spam, near-miss decoys) ·
> **P-Key** (answer key and corpus QA/validation) ·
> **P-Batch** (Batch APIs, caching, cost control) ·
> **P-Claude** (Anthropic provider) ·
> **P-UX** (long-run UI and progress).

---

## Critical Pitfalls

### Pitfall 1: Letting the LLM own the facts (dates, amounts, counts, labels) instead of the code

**What goes wrong:**
The model is asked to "write 15 emails about fact F7 between March and May". It decides the dates, the dollar figures, who is copied, how many emails to return, and implicitly whether each one is relevant. Across thousands of calls this drifts. The change order is $412,500 in one thread and $415,200 in another. A VP becomes a Director. Emails fall outside the window. The model returns 13 or 17 emails, which is the existing count-mismatch bug. The answer key then describes what was *planned*, not what was *written*.

**Why it happens:**
It is the easiest prompt to write, and it works at 50 emails. Context per call is bounded, so the model cannot see the rest of the corpus. The model's own world model silently fills the gaps.

**How to avoid:**
- Build a **deterministic corpus plan in code** (P-Plan) before any content call. Each planned document records: DocId, thread id, position in thread, sender, To/CC, sent timestamp with offset, relevance label, issue ids, key-fact ids, doc type (relevant / business-as-usual / ancillary / newsletter / spam / decoy), and a subtlety level. The LLM only writes the prose for a slot the code has already filled.
- Keep a **canonical Fact Ledger** (structured, not prose) for every number, date, project id, contract section, and product name. Inject only the ledger rows relevant to the slot, word for word.
- Treat returned JSON as untrusted. Map results by slot id, not by array position. Trim extras, top up shortfalls, and overwrite model-supplied dates, senders, and recipients with the planned values.

**Warning signs:**
Answer key rows whose dates don't match the EML `Date:` header. More than one distinct dollar value for the same ledger item across the corpus (check with a regex scan). Email counts per thread that differ from the plan.

**Phase to address:** P-Plan, with the ledger schema defined in P-Guide. It is the foundation for P-Relevant, P-Filler, and P-Key.

---

### Pitfall 2: Temporal knowledge leakage (characters know the future; emails speak "pleading-ese")

**What goes wrong:**
An email dated before the defect was discovered says "given the water intrusion issues". A character refers to the lawsuit, the "alleged conspiracy", or "Count III" months before the complaint exists. Someone emails a colleague who hasn't joined the company yet, or who left six months earlier. Emails parrot the complaint's phrasing ("in material breach of Section 4.2 of the Subcontract"), which people never write to each other.

**Why it happens:**
The whole complaint or timeline is put in the prompt as "context", so the model has the ending and uses it. The cast's "relevant date range" isn't enforced.

**How to avoid:**
- Build a **point-in-time context** per slot: only key facts dated on or before the email date, filtered to what *this sender and these recipients* could know. Give each fact a `knownBy[]` / `disclosedOn` field in the ledger.
- Never send the complaint text to email-generation prompts. Send a neutral, in-world summary of the situation "as of" the email date.
- Enforce cast active-date ranges when the plan assigns senders and recipients (P-Plan). Validate afterwards.
- Add a negative instruction plus a post-check: a banned-term scan for "plaintiff", "defendant", "allegedly", "complaint", "count", "cause of action", and the complaint's distinctive phrases, applied to any email dated before the triggering event.

**Warning signs:**
Legal vocabulary in early-timeline emails. Relevant emails that cleanly narrate the allegation. Reviewers saying "it reads like the complaint was written first", which it was.

**Phase to address:** P-Guide (ledger with `knownBy`/dates), P-Plan (date-range enforcement), P-Relevant (point-in-time prompt assembly), P-Key (automated checks).

---

### Pitfall 3: Relevant and non-relevant documents are trivially separable by style or metadata (label leakage)

**What goes wrong:**
The corpus is meant to demo relevance classification, but a classifier (or a person) can separate the classes without reading for meaning:
- Relevant docs come from a stronger model and filler from a cheaper model, or relevant docs are synchronous and filler is batched with a different prompt or temperature. Each has a detectable "voice" fingerprint.
- Relevant emails are longer, contain more names, dollar amounts, and dates, and have more attachments. Filler is short and generic.
- Relevant emails come only from key characters and filler only from ancillary characters. The sender alone predicts the label.
- Relevant emails cluster tightly around key-fact dates while filler is spread uniformly. The date alone predicts the label.
- Relevant threads are deep and filler is mostly singletons. Or all relevant docs sit in a few sender folders.
- File names, DocIds, or folder names encode the storyline or label (e.g., `F07_relevant_...eml`).

**Why it happens:**
Each class is generated by a different code path that has been tuned separately. This is the classic "annotation artifact / shortcut learning" failure documented in NLP benchmark literature. Synthetic data amplifies it because each generator has consistent stylistic tics.

**How to avoid:**
- Use the **same model, same system prompt skeleton, same style engine (character voice profiles), and same HTML/formatter path** for both classes. If a cheaper model is used for cost, use it for a *share of both classes*, not for one class.
- **Match distributions by design in P-Plan**: per custodian, keep the relevant and non-relevant length buckets, attachment rates, thread depth, CC counts, and hour-of-day distributions similar. Key custodians must have lots of mundane business email (already a requirement). Ancillary characters should appear on some relevant threads.
- Add a **leakage audit in P-Key**. Train a trivial metadata-only baseline (logistic regression or decision stump on length, sender, date, attachment count, thread depth, hour) against the answer key. If AUC is well above 0.5, say above 0.7, the corpus leaks. Also run a bag-of-words baseline restricted to style tokens (greetings, sign-offs, punctuation). This can be done in-process in C# with simple features. No ML library is required for a stump or naive Bayes.
- Use opaque DocIds and filenames. Never encode labels, fact ids, or storylines in paths.

**Warning signs:**
You can guess relevance from the file list. The average relevant email is more than 1.5 times the length of the average non-relevant one. The metadata-only baseline beats 0.7 AUC.

**Phase to address:** P-Plan (distribution matching), P-Filler (shared generation path), P-Key (leakage audit as an acceptance test).

---

### Pitfall 4: Decoys and filler that are accidentally relevant, and "relevant" docs that aren't

**What goes wrong:**
- **Near-miss decoys drift on-topic.** Given the same project, people, and keywords, the LLM "helpfully" ties the decoy back to the dispute ("btw, any update on the window leak issue?"). A doc labeled non-relevant is now responsive.
- **Business-as-usual email from key custodians** casually mentions the key events, because the model knows the storyline.
- **Quoted-thread contamination.** A reply labeled non-relevant ("Thanks, will do") quotes a hot message below it. In eDiscovery practice the whole document is responsive because the quoted text is part of it. The same applies to forwards and inclusive emails.
- **Family relevance.** An attachment is relevant but its parent email is labeled non-relevant, or the reverse. Review platforms usually code at the family level.
- **Sanitized relevant docs.** A safety-cautious model softens a relevant email until it carries no evidence (see Pitfall 7), but the key still says "Relevant: F12".

**Why it happens:**
LLMs are trained to be coherent and to reference context. The key is written from the plan, not verified against the text. Threading and families are an eDiscovery concept that generic generators ignore.

**How to avoid:**
- Decoy and filler prompts **must not receive allegation details**. Give them only neutral project context, the planned topic, and an explicit "must not mention" list (the key events, amounts, defect terms, code words).
- **Label at the document level after threading**. If a message quotes or forwards a relevant message, it inherits relevance, or the plan must break the thread so it doesn't quote. Propagate relevance across families (parent and attachments). Record both "family relevance" and "document-own relevance" columns if both are wanted.
- **Blind LLM review pass** (P-Key, cheap model, batchable). A reviewer model gets the complaint summary plus one document, *without its label*, and classifies it with a short reason. Disagreements with the key get flagged for regeneration or relabeling. This is the single highest-value QA step.

**Warning signs:**
Decoys containing ledger terms (a keyword scan catches this). Disagreement between the blind review and the key above roughly 5%. Replies labeled non-relevant whose quoted body contains hot terms.

**Phase to address:** P-Filler (prompt isolation), P-Key (propagation rules and blind review). Also P-Plan, which decides threading so that quoting is intentional.

---

### Pitfall 5: "LLM voice": every email sounds the same, and the same way across runs

**What goes wrong:**
Every email opens "I hope this email finds you well" or "Just circling back", uses "ensure", "delve", "robust", "moving forward", leans on em-dashes, has perfect grammar, is three tidy paragraphs, and signs off "Best regards". The general contractor's superintendent writes like the developer's in-house counsel. Every run reuses a small pool of LLM-favorite names (Sarah Chen, Michael Thompson, David Rodriguez...). Real mailboxes are full of one-liners ("ok", "see attached", "call me"), typos, mobile signatures, and terse forwards with no comment. Those are missing.

**Why it happens:**
Default model style, low diversity at scale, and no per-character voice specification. Homogenization of LLM text is well documented, and stylometric repetitiveness is the main signal AI-text detectors use.

**How to avoid:**
- **Per-character voice profiles** in the cast: formality, verbosity, typo rate, greeting/sign-off habits, mobile vs desktop share, jargon, signature block, reply latency. The code picks a per-email **length bucket** from a realistic distribution: a large share under 40 words, and a long tail.
- Plan **structural variety** in P-Plan: bare forwards, "see attached" emails, meeting logistics, auto-replies, calendar accepts, one-line replies, reply-all pile-ons.
- **Name generation in code** from census-style first/last name lists with a per-run seed, not by the LLM.
- **Corpus-level repetition monitor**: count the top n-grams and opener/closer frequency across the output. Keep a banned/overused phrase list in the prompt, and regenerate outliers.

**Warning signs:**
Any single opener in more than 5% of emails. All emails 120–250 words. Identical signature formatting across organizations. The same cast names across different scenarios.

**Phase to address:** P-Guide (voice profiles in the cast schema), P-Plan (length and structure distributions), P-Relevant/P-Filler (prompt style block), P-Key (repetition report).

---

### Pitfall 6: "Smoking gun" evidence that is too on-the-nose

**What goes wrong:**
Relevant emails say, in effect, "Let's hide the defect from the owner so we don't get sued," or "Remember, our meth operation must stay secret." Real evidence is oblique: euphemisms, code words, "let's discuss offline", a spreadsheet with a changed number, a calendar invite at an odd hour, a deleted-then-restored draft. An on-the-nose corpus makes review and classifier demos trivially easy and unconvincing to legal professionals.

**Why it happens:**
The model is asked to "evidence allegation X", so it states X. It has no notion of how guilty people write.

**How to avoid:**
- Give each relevant slot a **subtlety level** in the plan (e.g., hot / warm / circumstantial / background). Realistic mixes are mostly circumstantial with few hot documents.
- Give each storyline **in-world concealment behavior**: code words defined in the ledger, "call me" deflections, moving to phone, personal-email usage, attachments carrying the substance.
- Instruct: "The email must be plausible if read by a colleague uninvolved in the conduct; the relevance must be inferable, not stated." Verify with the blind review pass, which should find warm and circumstantial docs *less* obviously relevant.

**Warning signs:**
The blind reviewer marks nearly every relevant doc "highly relevant" with very high confidence. Relevant emails contain words like "illegal", "cover up", or "fraud".

**Phase to address:** P-Plan (subtlety allocation), P-Relevant (prompting), P-Key (verification).

---

### Pitfall 7: Content-policy refusals and silent sanitization on criminal-conspiracy scenarios

**What goes wrong:**
Scenarios like "inspired by Breaking Bad: correspondence evidencing a criminal conspiracy" hit safety systems. The failure modes are:
- **Hard refusal.** Anthropic structured outputs return `stop_reason: "refusal"` with no JSON. OpenAI returns a `refusal` field instead of schema output. The current JSON-retry loop treats this as a parse failure, retries 4 times, pays each time, then throws.
- **Soft refusal / sanitization.** The model writes the emails but removes the substance. The key says relevant and the text is benign.
- **Moralizing insertions** ("Note: this is fictional and illegal") inside email bodies.
- **Image generation refusals** for drug-lab or weapon imagery, and inconsistent refusals across a batch.
- Operational detail requests (synthesis steps, quantities of precursors, laundering mechanics) are the most likely to be refused. They are also unnecessary for eDiscovery realism.

**Why it happens:**
The request looks like a request to author criminal planning content. Refusal rates vary by model and change over time.

**How to avoid:**
- **Frame and constrain.** A system prompt that states the purpose (a fictional training dataset for legal review), plus explicit instructions to keep criminal *operational* detail abstract (quantities as "product", "batches", "the blue", invoices for a front business). The evidence value lies in who knew what and when, money flows, and cover stories, not in how-to content.
- **Refusal detection as a first-class result type** in the provider abstraction: `Succeeded | Refused | Truncated | Invalid | Failed`. Do not JSON-retry a refusal. Retry once with a softened slot, then mark the slot `Skipped: refused` in the log and answer key.
- A **sanitization check** via the blind review pass (Pitfall 4): if a planned hot doc is judged non-relevant, flag it.
- A **scenario pre-flight** in P-Guide: generate the core doc first. If it is refused or sanitized there, warn the user before spending on 5K emails.
- A post-filter that strips disclaimer and moralizing sentences from bodies.

**Warning signs:**
A spike in JSON failures on one storyline. Emails with "I can't help with..." text. Relevant-doc blind-review recall far below planned. Refusal rates differ between OpenAI and Claude for the same case.

**Phase to address:** P-Found (result taxonomy in the provider abstraction), P-Claude (Anthropic `refusal` stop reason), P-Guide (pre-flight), P-Key (sanitization detection).

---

### Pitfall 8: Batch API assumptions that break a desktop app

**What goes wrong:**
- **Latency.** Both providers allow up to 24 hours. Anthropic says most batches finish within 1 hour, but results can only be fetched once all requests are done or 24 hours have passed. A WinForms user who closes the app, or whose laptop sleeps, orphans the batch. PROJECT.md explicitly excludes pause/resume, which conflicts with this.
- **Sequential dependencies.** Reply N+1 depends on reply N's text. Doing one round trip per reply through a batch means up to 24 hours *per message*. A batch only suits independent units.
- **Ordering.** Neither provider returns results in input order. Both say to match on `custom_id`. (HIGH)
- **custom_id rules.** Anthropic requires `^[a-zA-Z0-9_-]{1,64}$` (HIGH). Ids containing dots, spaces, or GUID braces are rejected.
- **Partial results.** Anthropic results are `succeeded | errored | canceled | expired`, and errored/expired requests aren't billed. OpenAI writes failures to a separate `error_file_id`, and an `expired` batch still returns completed results. Code that treats a batch as all-or-nothing loses work or double-bills on resubmission. (HIGH)
- **Late validation.** Anthropic validates `params` asynchronously, so a malformed request shape surfaces only when the *whole batch* ends. (HIGH)
- **Limits.** OpenAI allows 50,000 requests or 200 MB per batch file, plus a per-model enqueued-prompt-token limit that low-tier accounts can exceed with 5K large-context requests. Anthropic allows 100,000 requests or 256 MB, and results are kept for 29 days. Anthropic batches can slightly overshoot the workspace spend limit. (HIGH)

**Why it happens:**
Batch is treated as "a cheaper synchronous call".

**How to avoid:**
- Use a **whole-thread-per-request** generation unit for batched work, so there are no inter-request dependencies. Use batch mainly for filler, decoys, blind-review QA, and attachment prose. Keep relevant storyline threads, where intra-thread coherence matters most, synchronous or thread-atomic.
- **Persist a run manifest** (JSON on disk) with the plan, batch ids, custom_id to slot map, and per-slot status, *before* submitting. Even without full resume, provide "collect results of pending batch" on restart. This is minimal and necessary.
- A custom_id scheme such as `r{run}-s{slot:D6}-a{attempt}`. Validate it with the regex before submitting.
- Resubmit **only** `errored`/`expired` slots, idempotently. Never re-write EMLs for slots already `Done`.
- Validate the request shape with one synchronous call before submitting a batch.
- Prevent system sleep while a run or poll is active (`SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`).

**Warning signs:**
Results mapped by index. A batch "stuck" after the user reopens the app. Duplicate EMLs after a resubmit. Validation errors appearing hours later.

**Phase to address:** P-Batch (explicitly after P-Found persistence and P-Plan slot ids exist). Flag P-Batch for deeper phase research.

---

### Pitfall 9: Nothing saved until the end, and the answer key drifts from what is on disk

**What goes wrong:**
This is a known bug (CONCERNS.md). One exception discards everything, and cancellation saves nothing. In the new design the risks multiply: 5K emails plus images and audio held in memory, and an answer key written only at the end, so a crash leaves EMLs with no key. Organizing by sender changes paths, so answer key "file" columns break. Re-running into the same folder mixes two runs.

**How to avoid:**
- **Per-thread unit of work**: content, then attachments, then EML write (temp file plus atomic rename), then an **append-only answer-key row** (CSV/JSONL, flushed), then a manifest status update, then release the byte arrays.
- Per-item try/catch in *every* enrichment lambda. A failed attachment degrades to "no attachment" and is logged. It never aborts the run.
- A **run folder per run** (timestamped) containing a manifest. Refuse to write into a non-empty folder.
- Build Excel from the CSV/JSONL at the end, so the key survives a crash and the Excel is a derived artifact.
- The answer key references DocId plus a relative path computed from the final layout.

**Warning signs:**
The row count in the answer key differs from the EML count (make this a post-run check). Memory above 2 GB mid-run.

**Phase to address:** P-Found (must precede all scale work), P-Key.

---

### Pitfall 10: Unrealistic metadata (timestamps, time zones, threading, CC, custodians)

**What goes wrong:**
- Every email sent at a uniform random time, including 3 a.m. Sundays and Christmas Day. Or the opposite: everything exactly 9–5 with no evening emails from executives.
- No time zones. `DateTime` without offsets, culture-dependent `TryParse` (existing concern), and DST not handled. A Chicago developer and a Phoenix subcontractor show identical local hours. Replies timestamped *before* the message they reply to.
- Thread shapes all identical. Real corpora are dominated by one- to three-message threads, with occasional long reply-all chains.
- CC patterns that make no sense: bosses never copied, BCC recipients visible in headers, or everyone always copied.
- Custodian semantics wrong. In eDiscovery a message sent from A to B exists in A's Sent Items and B's Inbox, so the answer key's "custodian" is ambiguous. Per-sender folders imply the sender's mailbox only.
- Invalid time values from the model (hour 24, minute 60) crash the build. This is a known bug in the calendar code.

**How to avoid:**
- **The code assigns all timestamps** (P-Plan). Use each sender's IANA/Windows time zone from the cast, a working-hours profile per persona, weekends and a holiday calendar for the jurisdiction, a reply-latency distribution (minutes to days, log-normal), and monotonic ordering within threads. Store `DateTimeOffset` everywhere, and let MimeKit write the offset in `Date:`.
- Thread-depth and CC distributions come from the plan. Include org-chart-driven CC (managers and project distribution lists).
- Decide the custodian model explicitly. Recommended: the custodian is the mailbox the file is placed in. Optionally duplicate into recipient custodians, with a dedupe hash column in the key.
- Never parse model-supplied dates for anything authoritative.

**Warning signs:**
A histogram of send hours that is flat. Replies preceding parents. `Date:` headers without offsets. Holiday emails.

**Phase to address:** P-Plan (timestamp engine), P-Found (fix the parse/validation bugs), P-Key (metadata sanity checks).

---

### Pitfall 11: Hallucinated law, wrong-jurisdiction formatting, and implausible discovery documents

**What goes wrong:**
- The complaint cites nonexistent cases or mis-cites statutes. Fabricated citations are a well-known LLM failure. Courts sanctioned lawyers over this in *Mata v. Avianca* (S.D.N.Y. 2023) and many later cases. A legal-tech demo containing fake citations damages credibility with exactly the target audience.
- Federal-style caption in a state-court case. The wrong document type (a "Complaint" where a Petition or Information/Indictment fits, or where a criminal matter would be an investigation memo or search-warrant affidavit). Invented judges who share names with real judges. Case numbers in the wrong format.
- Discovery requests that ignore rule limits. For example, FRCP 33 limits interrogatories to 25 including discrete subparts. California caps specially prepared interrogatories at 35 without a declaration. RFAs are numbered wrong. Definitions and instructions sections are missing.
- Deposition transcripts with no page:line format, no appearances, no examination by counsel, and witnesses testifying to facts contradicting the timeline.
- Ancillary docs generated from the complaint text alone drift from the edited cast and timeline.

**How to avoid:**
- Default policy: **no case-law citations** in generated pleadings. Statutory citations only from a small curated, per-jurisdiction table the app ships with, or clearly marked `[citation omitted — synthetic]`. Put this in the prompt *and* check with a regex for reporter patterns (e.g., `\d+ (U\.S\.|F\.\d?d|F\. Supp\.)`).
- A jurisdiction and court-type selection (or inference, confirmed by the user) drives a **template**: caption block, numbered paragraphs, counts, prayer for relief, verification/signature block. Keep the LLM filling sections rather than inventing the format.
- Encode rule limits as template constants (25 federal interrogatories, etc.). They are MEDIUM confidence and need verification per jurisdiction during P-Ancillary research.
- Fictional judges and counsel, with names generated by code.
- Ancillary generation consumes the **structured** cast and ledger, not the raw complaint.

**Warning signs:**
Any "v." citation with a reporter volume. Captions naming real sitting judges. More than 25 federal interrogatories.

**Phase to address:** P-Guide (core doc templates, citation policy), P-Ancillary. Flag P-Ancillary for deeper research on jurisdiction formats and limits.

---

### Pitfall 12: Real-person, real-company, and IP risk in "inspired by" scenarios

**What goes wrong:**
The user types "fraud at [real company]", or the model populates a scenario with real brands, real executives, real domains (e.g., `@actualcompany.com`), or real phone numbers. The output is realistic fake evidence of crimes attributed to real entities, which creates defamation and false-light risk if it leaks or gets demoed publicly. It also produces EML files with real email addresses that review tools or users might accidentally reply to. For "Breaking Bad" scenarios, using the show's character names and plot wholesale raises copyright and trademark questions for a commercial tool (LOW confidence on the degree of risk; flag for the owner).

**How to avoid:**
- In P-Guide, **entity pre-flight**: ask the model to list real persons, organizations, and brands in the scenario, and warn the user. By default substitute fictional names. "Inspired by" means renamed characters and a transposed setting.
- **Code-generated identities**. Use fictional company names, and domains on reserved TLDs/names per RFC 2606/6761 (`.example`, `.test`, `example.com`) *or* a clearly synthetic pattern. Use 555-01xx phone numbers. Accept the realism trade-off and make it configurable, with safe as the default.
- Keep the existing disclaimer banner, plus a `X-ReelDiscovery-Synthetic: true` header in every EML, plus a disclaimer page in exported case-guide documents.
- Remove topic text from telemetry (existing concern). It matters more now that scenarios may name real parties.

**Warning signs:**
Cast emails on domains that resolve. The core doc naming a Fortune 500 company as defendant.

**Phase to address:** P-Guide (pre-flight and substitution), P-Found (synthetic header, telemetry fix).

---

### Pitfall 13: Case-guide edits that don't propagate (stale derived artifacts)

**What goes wrong:**
The user edits the complaint after the cast and timeline were derived, or renames a character in the cast grid. Emails are generated from stale data. Or the free-text edit breaks the structured parse and the timeline silently loses facts.

**How to avoid:**
- Build a dependency graph: core doc, then cast and ledger/timeline, then ancillary docs and corpus plan. On an upstream edit, mark downstream items "stale" and ask whether to regenerate or keep.
- The cast and timeline are **edited as structured grids** (ids are stable, and text fields are editable). The corpus plan references ids, never names.
- Validate the guide before corpus generation: every issue has at least one fact, every fact has at least one character, dates fall within the timeline span, and email addresses are unique.

**Phase to address:** P-Guide.

---

### Pitfall 14: Structured-output failure modes (truncation, schema limits, refusals, count mismatch)

**What goes wrong:**
- Batches of 15 HTML-heavy emails exceed `max_tokens`. The JSON is cut off mid-string, retried 4 times at full price, then thrown. Both providers document that truncation voids schema guarantees: OpenAI returns `status: "incomplete"`, and Anthropic `stop_reason: "max_tokens"`. (HIGH)
- Anthropic structured outputs **do not support** `minItems` above 1, `maxItems`, `minimum`/`maximum`, `pattern`, `minLength`/`maxLength`, or recursive schemas, and require `additionalProperties: false` on every object. OpenAI strict mode requires **every property to be in `required`**, with optionals expressed as `type: [X, "null"]`. A DTO-derived schema that works on one provider fails on the other. (HIGH)
- "Exactly N emails" cannot be enforced by schema on Claude, so the count-mismatch bug persists unless it is validated in code.
- Changing `output_config.format` invalidates the Anthropic prompt cache, and the first use of a new schema adds grammar-compile latency (cached 24 hours). (HIGH)
- Numbers arriving as strings, or strings as numbers (the existing `ExcelDocResponseRaw` workaround).

**How to avoid:**
- Keep generation units small: one short thread or at most about 5 emails per call. Set `max_tokens` generously and **always check `stop_reason`/`finish_reason`** before parsing. On truncation, split the slot rather than retrying the same request.
- A single **schema generator** from the C# DTOs, with provider-specific adapters (OpenAI strict nullability, Anthropic keyword stripping). Unit-test both emitted schemas.
- Enforce counts, ranges, and formats in C# validation after parsing. Log every fallback (existing concern: silent placeholders).
- Keep the schema stable within a run to preserve caching.

**Phase to address:** P-Found (provider abstraction and schema adapters), P-Claude.

---

### Pitfall 15: Cost blowups

**What goes wrong:**
- The full case guide (complaint, cast, timeline: easily 10–20K tokens) is re-sent on every call: 1,000+ calls means 10–20M input tokens before any output.
- JSON retries re-pay the whole request, and truncation loops multiply this.
- Reasoning models bill hidden reasoning tokens as output.
- Images (DALL-E/gpt-image) and TTS dominate cost per item but are counted separately or not at all (existing: `TotalAttachmentsGenerated` undercounts). Hard-coded pricing tables are stale (existing concern).
- The cost ticker ignores cache writes (1.25x for a 5-minute TTL and 2x for a 1-hour TTL on Anthropic; 1.25x writes on newer OpenAI models), cache reads (about 0.1x, varying by model), and the 50% batch discount, so it shows the wrong number in both directions.
- Hitting the Anthropic monthly spend cap returns **429 with no `retry-after`**, distinguishable by `error.details.error_code == "enforced_spend_limit_reached"`. Naive retry code, including SDK auto-retry, spins until the run dies. (HIGH)

**How to avoid:**
- Point-in-time *slices* of the guide (Pitfall 2) are also the cost fix: send only the relevant cast and facts.
- A **hard budget cap** per run in settings. The run stops cleanly, with everything so far saved, when projected spend exceeds it. (This is not the "up-front estimate" that is out of scope; it is a guardrail.)
- The cost calculator reads provider usage fields: `cache_creation_input_tokens`, `cache_read_input_tokens`, `input_tokens`, `output_tokens` (Anthropic); `cached_tokens` and reasoning tokens (OpenAI). It applies batch pricing, with prices from an editable config file.
- Treat spend-cap, auth (401/403), and invalid-request (400) errors as **fatal, non-retryable**. Stop the run gracefully.

**Phase to address:** P-Found (error taxonomy, pricing config), P-Batch (cache- and batch-aware accounting, budget cap).

---

## Moderate Pitfalls

### Pitfall 16: Prompt caching that silently doesn't cache
- **Anthropic**: the prompt must exceed a model-specific minimum (512 tokens on current Opus/Sonnet 5.x; 1,024–4,096 on older models such as Haiku 4.5 at 4,096). Below it, caching is **silently skipped**. There are at most 4 breakpoints, and a 20-block lookback. Any change before a breakpoint (a timestamp, a per-slot line, a different tool set, adding an image, a different `output_config.format`) misses. The default TTL is 5 minutes. For batches, hits are "best-effort" (30–98%), and the docs recommend the 1-hour TTL, whose writes cost 2x. (HIGH)
- **OpenAI**: caching is automatic from 1,024 tokens on an exact prefix. `prompt_cache_key` helps routing, and traffic above about 15 requests per minute per prefix can overflow-route and miss. (HIGH per the current docs; parameter names differ between model generations, so verify against the model chosen.)
- **Prevention:** order the prompt as [static instructions, then the case guide core (stable for the whole run), then storyline context, then slot specifics]. Put breakpoints after the stable blocks. Never put timestamps, run ids, or random seeds in the prefix. **Log cache read/write tokens per call**, and alert if the read ratio stays near 0 after warm-up.
- **Phase:** P-Batch / P-Claude.

### Pitfall 17: Rate limits and retry storms at 10-way parallelism
- Existing: fixed backoff with no jitter, `Retry-After` ignored, 5xx not retried. Anthropic also applies **acceleration limits** (429s on sharp ramps) and enforces RPM at sub-minute granularity. Cached reads don't count toward Anthropic ITPM for most models, so caching raises throughput. (HIGH)
- **Prevention:** a single retry policy with exponential backoff plus jitter that honors `retry-after`, retries 408/409/429/5xx/timeouts, and fails fast on 400/401/403/spend-cap. Use an adaptive concurrency limiter (start low and ramp up, backing off on 429). Reuse the SDKs' built-in retry where possible instead of the four copy-pasted blocks.
- **Phase:** P-Found.

### Pitfall 18: Attachments contradicting the emails and the ledger
- The Excel shows a different amount than the email that transmits it. Document versions collide (the existing race on `VersionNumber++`). DALL-E images of "the defect" don't match the described defect. The voicemail TTS voice doesn't match the character's persona and changes between messages.
- **Prevention:** attachment prompts receive the same ledger slice as the parent. Numbers in spreadsheets are **filled by code from the ledger**, with the LLM producing only labels and narrative. Assign one TTS voice per character in the cast. Lock the version counters. Remember that Anthropic has no image or TTS API, so a Claude-only user needs OpenAI for media, or media must be disabled gracefully.
- **Phase:** P-Relevant / P-Claude.

### Pitfall 19: Context drift inside long threads
- Long threads re-quote every prior body. Tokens grow quadratically, and the model loses early details. The existing `replyToIndex` bug means batch 2 onward quotes the wrong parent.
- **Prevention:** the plan caps thread length (most threads are short). For each new batch of a thread, give the model a **rolling structured thread summary** (who said what, open questions, commitments) plus only the last 1–2 messages verbatim. The code builds the quoting (`FormatQuotedReply`) from stored bodies. The LLM never re-types quoted text. Fix the batch-relative index, or better, use slot ids.
- **Phase:** P-Found (bug), P-Relevant.

### Pitfall 20: Non-relevant filler that is obviously fake
- Spam and newsletters that are too clean and grammatical, with no unsubscribe footer and no marketing HTML. Personal emails that are all "Happy birthday!" The same newsletter sender and template repeated 300 times with the same text. Real phishing-like spam may trip content filters.
- **Prevention:** a small set of code-driven newsletter/spam templates with LLM-written sections. Vary the senders. Put spam at realistic proportions per custodian. Keep phishing content benign-looking.
- **Phase:** P-Filler.

---

## Minor Pitfalls

- **Answer key CSV breaks Excel:** unescaped commas and quotes, Excel mangling leading-zero DocIds and dates, UTF-8 without a BOM showing mojibake. Write with a proper CSV writer and a BOM, and produce the .xlsx via the existing OpenXml tooling. (P-Key)
- **Filename collisions and illegal characters** at 5K scale with long subjects (the existing `FileNameHelper`). Use DocId-based names. (P-Found)
- **Elapsed time wraps after 1 hour** (existing), and runs will be multi-hour. (P-UX)
- **Reproducibility:** without a saved plan, seed, and guide JSON, a good corpus can't be regenerated or extended. Persist them in the run folder. (P-Plan)
- **Cast email-address collisions** (two "jsmith@"), or display names that don't match addresses. (P-Guide)
- **Model catalog drift:** deprecated model ids (o1-preview, dall-e-3) and parameter differences (reasoning models reject `temperature` and system messages in some generations). Load the catalog from config. (P-Found/P-Claude)

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Generate the new pipeline inside `EmailGenerator.cs` | No refactor needed | Blocks Claude, batch, and tests; bugs persist | Never. Split first (P-Found) |
| LLM decides dates, senders, and counts | Simpler prompts | Drift, key mismatch, crashes on invalid values | Never for authoritative metadata |
| Send the full complaint as context to every email call | "Maximum consistency" | Temporal leakage, cost blowup, pleading-ese | Never. Use point-in-time slices |
| Cheaper model only for filler | Lower cost | Model-fingerprint label leakage | Only if the same model mix is used for relevant docs too |
| Answer key written at end | Simple | Lost on crash, drifts from disk | Never. Append per thread |
| Regex-free "trust the model" citation policy | Richer pleadings | Fabricated case law in a legal product | Never |
| Skipping blind-review QA | Saves about 5–10% cost | Mislabeled key, and the demo fails on stage | Only for Simple preset drafts, with a warning |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Anthropic Message Batches | Map results by order; custom_id with dots or braces | Match on `custom_id` (`^[a-zA-Z0-9_-]{1,64}$`), handle `succeeded/errored/canceled/expired` per request |
| OpenAI Batch | Ignore `error_file_id`; treat `expired` as total failure | Read both output and error files; completed results survive expiry |
| Both batch APIs | Submit without persisting ids; app closed means orphaned | Persist the manifest first; offer "collect pending batch" on launch (results kept 29 days on Anthropic) |
| Anthropic structured outputs | Use `maxItems`/`minimum`/`pattern` in the schema | Strip unsupported keywords; validate in C#; check `stop_reason` for `refusal`/`max_tokens` |
| OpenAI strict schema | Optional DTO fields left out of `required` | All fields required; optionals as nullable unions |
| Anthropic rate limits | Retry spend-cap 429s | Detect `enforced_spend_limit_reached` (no `retry-after`) and stop the run |
| Prompt caching | Timestamp or slot id at the top of the system prompt | Stable prefix first; verify with the usage cache fields |
| MimeKit dates | `DateTime` with `Kind=Unspecified` | `DateTimeOffset` with the sender's offset |
| OpenAI images/TTS | Assume Claude parity | Media stays OpenAI-only; degrade gracefully when no OpenAI key |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Holding all bytes in memory until the end | RAM climbs; OOM or GC stalls | Per-thread write-and-release | ~1–2K emails with images |
| Base64 banner in every body (existing) | About 60 MB per 1K emails | `cid:` attachment | Already at 500 |
| Phase barriers across the whole corpus | Idle workers; slow first output | Per-thread pipeline under a limiter | Any large run |
| Quadratic quoting in long threads | Token cost climbs per message | Code-built quoting plus summaries | Threads over ~15 messages |
| UI log `RichTextBox` with unbounded lines | UI lag, then freeze, over hours | Ring buffer (e.g., last 2K lines) plus a file log | ~10K+ log lines |
| `Invoke` per progress event from 10 workers | UI thread saturated | Throttled `IProgress` of immutable snapshots (≤ 4–10 per second) | Immediately at high parallelism |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Real domains, emails, or phones in synthetic evidence | Defamation, misdirected replies | Reserved/synthetic domains, 555-01xx numbers, `X-ReelDiscovery-Synthetic` header |
| Scenario text in telemetry (existing) | Leaks matter or client names | Drop or hash it |
| Base64-stored API keys, now two providers (existing) | Key theft | DPAPI `CurrentUser` |
| Hard-coded Slack webhook (existing) | Channel abuse | Rotate and relay |
| Batch input files left on provider storage | Scenario data retained remotely | Delete uploaded OpenAI batch files after collection; note retention in the disclaimer |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| A 3-hour run with only a spinner | User assumes a hang and closes the app, losing work (existing close bug) | Per-phase counts, ETA, cost so far, last saved DocId; block or confirm close; cancel means save and stop |
| Batch mode with no status | "Nothing happened for 40 minutes" | Show batch state and request counts; allow closing with "collect later" |
| Laptop sleeps mid-run | Run stalls or network errors | `SetThreadExecutionState` during runs |
| Refusals shown as generic "JSON failed" | Confusion | Distinct "Model declined this content" messages with a count and a suggestion |
| Reviewing a 30-page complaint in a TextBox | Edits get lost | Structured section view; regenerate a section |

## "Looks Done But Isn't" Checklist

- [ ] **Answer key:** row count equals EML count plus attachment rows; every DocId resolves to a file; relevance is propagated through quoting and families.
- [ ] **Leakage audit:** the metadata-only baseline AUC is reported and below threshold.
- [ ] **Blind review:** agreement with the key is at or above the target. Disagreements have been regenerated or relabeled.
- [ ] **Timeline integrity:** no email references a fact dated after it; no sender or recipient is outside their active range.
- [ ] **Ledger consistency:** every amount, date, and id mentioned matches the ledger (regex scan).
- [ ] **Metadata:** send-hour histogram per custodian time zone looks human; replies follow parents; `Date:` has offsets.
- [ ] **Pleadings:** zero reporter-style case citations unless whitelisted; the caption matches the jurisdiction.
- [ ] **Failure isolation:** kill the process mid-run and confirm the finished threads and their key rows are on disk.
- [ ] **Batch:** force one invalid request and confirm the other slots complete and only the failed one is resubmitted.
- [ ] **Caching:** the logged cache-read ratio is above 50% after warm-up on a Standard run.
- [ ] **Refusals:** a Breaking-Bad-style scenario runs to completion with refusals counted, not crashing.
- [ ] **Style:** no opener in more than 5% of emails; the length distribution has a short-email mode.

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Mislabeled docs found after generation | LOW | Blind-review pass, then relabel in the key (cheaper than regenerating) |
| Leakage found (style or metadata) | MEDIUM | Regenerate the offending class share with the matched generator; rebalance via the plan |
| Ledger contradictions | LOW–MEDIUM | Regenerate the affected slots only (slot ids make this possible) |
| Orphaned batch | LOW | Load the manifest and fetch results by batch id (within 29 days on Anthropic) |
| Fake citations in delivered case guide | LOW | Strip or replace via the citation regex; regenerate the section |
| Real-entity names in output | MEDIUM | Name substitution map across EMLs and docs (why names should come from the cast by id) |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| 1 LLM owns facts | P-Plan (+ ledger in P-Guide) | Key and EML headers match 100% |
| 2 Temporal leakage | P-Guide, P-Plan, P-Relevant | Point-in-time scan: zero future-fact references |
| 3 Style/metadata label leakage | P-Plan, P-Filler, P-Key | Metadata-only baseline AUC < ~0.7 |
| 4 Decoy/quote/family mislabeling | P-Filler, P-Key | Blind-review agreement ≥ target |
| 5 LLM voice homogeneity | P-Guide, P-Plan, P-Key | N-gram and opener report within thresholds |
| 6 On-the-nose evidence | P-Plan, P-Relevant | Blind-review confidence spread across subtlety levels |
| 7 Refusals and sanitization | P-Found, P-Claude, P-Guide | Criminal-scenario smoke test completes |
| 8 Batch assumptions | P-Batch | Kill/restart plus forced-error test |
| 9 Persistence and key drift | P-Found | Kill mid-run test |
| 10 Metadata realism | P-Plan, P-Found | Header and histogram checks |
| 11 Legal doc realism | P-Guide, P-Ancillary | Citation regex; jurisdiction template review |
| 12 Real-entity/IP risk | P-Guide, P-Found | Entity pre-flight warning shown; synthetic header present |
| 13 Stale guide edits | P-Guide | Edit, then stale-flag test |
| 14 Structured output | P-Found, P-Claude | Schema unit tests per provider; truncation test |
| 15 Cost blowups | P-Found, P-Batch | Budget cap triggers a clean stop; cost matches provider usage |
| 16 Cache misses | P-Batch, P-Claude | Cache-read ratio logged |
| 17 Rate limits | P-Found | 10-way run without retry storms |
| 18 Attachment contradictions | P-Relevant | Ledger scan over attachment text |
| 19 Thread drift | P-Found, P-Relevant | Reply-parent correctness test (replyToIndex) |
| 20 Fake-looking filler | P-Filler | Manual spot check plus repetition report |

**Research flags:** P-Batch (desktop lifecycle and manifest design; OpenAI .NET SDK batch/file API ergonomics not verified here), P-Ancillary (jurisdictional formats and rule limits), P-Key (practical in-process leakage-audit design), and P-Claude (the Anthropic C# SDK's structured-output and batch surface; verify with Context7 during the phase).

## Sources

- Anthropic Message Batches docs (limits, 24h expiry, 29-day retention, result types, unordered results, custom_id regex, async validation, 1h cache TTL tip, 30–98% best-effort cache hits): https://platform.claude.com/docs/en/build-with-claude/batch-processing (HIGH)
- Anthropic prompt caching (minimum lengths per model, silent skip, 4 breakpoints, 20-block lookback, invalidation table, TTL pricing): https://platform.claude.com/docs/en/build-with-claude/prompt-caching (HIGH)
- Anthropic structured outputs (unsupported keywords, `additionalProperties:false`, `refusal`, truncation, grammar cache, format change invalidates the prompt cache): https://platform.claude.com/docs/en/build-with-claude/structured-outputs (HIGH)
- Anthropic rate limits (cache-aware ITPM, acceleration limits, spend-cap 429 without retry-after, `enforced_spend_limit_reached`, batch queue limits): https://platform.claude.com/docs/en/api/rate-limits (HIGH)
- OpenAI Batch guide (50K requests / 200 MB, 24h window, unordered output, error file, expired partial results, enqueued token limits, 50% discount): https://developers.openai.com/api/docs/guides/batch (HIGH)
- OpenAI prompt caching (1,024-token minimum, exact prefix, `prompt_cache_key`, ~15 RPM overflow, `cached_tokens`): https://developers.openai.com/api/docs/guides/prompt-caching (HIGH)
- OpenAI structured outputs (strict mode: all fields required, nullable unions, refusal field, incomplete status): https://developers.openai.com/api/docs/guides/structured-outputs (HIGH)
- Shortcut learning and surface-feature leakage: "Judging by the Cover" https://arxiv.org/pdf/2609.13003; "UNMASK" https://arxiv.org/pdf/2608.09209; "Shortcut Learning in Legal Judgment Prediction" https://arxiv.org/pdf/2607.04261 (MEDIUM; applied by analogy)
- Synthetic-data artifacts and homogeneity: "Synthetic Artifact Auditing" https://arxiv.org/html/2502.00808; "Survey on Evaluating Quality and Trustworthiness in LLM-Generated Data" https://arxiv.org/pdf/2601.17717; ACM "Detection of AI-Generated Emails" https://dl.acm.org/doi/fullHtml/10.1145/3664476.3670465 (MEDIUM)
- eDiscovery corpus context: EDRM "Still on Dial-Up: Why It's Time to Retire the Enron Email Corpus" https://edrm.net/2025/08/still-on-dial-up-why-its-time-to-retire-the-enron-email-corpus/; ediscovery-email-generator https://github.com/JW2WW/ediscovery-email-generator (MEDIUM)
- Hallucinated citations: *Mata v. Avianca, Inc.*, No. 22-cv-1461 (S.D.N.Y. 2023) sanctions order (training knowledge; widely reported; MEDIUM)
- FRCP 33(a)(1) 25-interrogatory limit; Cal. CCP §2030.030 35 special interrogatories (training knowledge; MEDIUM; verify in P-Ancillary)
- RFC 2606 / RFC 6761 reserved domain names (training knowledge; HIGH for existence)
- Project-specific: `.planning/codebase/CONCERNS.md` (replyToIndex, abort-on-failure, Random, progress mutation, date parsing, pricing staleness)

---
*Pitfalls research for: LLM-generated synthetic eDiscovery corpora (ReelDiscovery case-driven milestone)*
*Researched: 2026-09-29*
