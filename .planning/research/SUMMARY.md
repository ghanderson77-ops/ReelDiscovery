# Project Research Summary

**Project:** ReelDiscovery
**Domain:** AI-driven synthetic eDiscovery corpus generator (brownfield .NET 8/10 WinForms desktop app) — case-guide-driven generation of a legally structured pleading, cast, dated fact timeline, ancillary discovery documents, and a large (~5K email) evidence corpus with an exact answer key
**Researched:** 2026-09-29
**Confidence:** MEDIUM-HIGH

## Executive Summary

ReelDiscovery is being rebuilt around a "case-guide-driven" pipeline: generate a legally structured core document (Complaint/Petition/Investigation Memo), extract structured Issues/Cast/Key-Facts/Entities from it, plan the entire evidence corpus deterministically in code, then render prose per planned slot, and finally derive an exact answer key from the plan and execution journal — never the other way around. This is not a novel domain pattern so much as a disciplined application of two well-established mitigations for long-form LLM generation: hierarchical top-down structure (narrative to structured layers, each referencing the layer above by stable ID) and plan-then-render with a pinned "fact ledger" so dates, dollar amounts, and character knowledge are decided once in code and never re-invented by the model. All four research tracks converge on the same conclusion: the current codebase's root failure is that the LLM is trusted to own facts, dates, counts, and relevance labels, and that nothing is saved until a multi-hour run finishes. Fixing those two things (deterministic planning plus incremental per-thread persistence) is the prerequisite for everything else, including the two most-requested capabilities: adding an Anthropic Claude provider and controlling cost at 5K-email scale via Batch APIs and prompt caching.

The recommended approach is: retarget to net10.0-windows (net8 goes EOL 2026-11-10, six weeks out); build a thin, app-owned ILlmProvider/ILlmGateway over the official Anthropic (12.51.0) and OpenAI (2.14.0) SDKs rather than binding directly to IChatClient, because batch requests need native request bodies and a single per-provider translator keeps sync and batch byte-identical (which also protects prompt-cache hits); introduce a SQLite-backed RunStore with an immutable manifest.json plan and an append-only journal.jsonl for status, so incremental saving and per-item failure isolation are structural, not bolted on; and drive every LLM call from a ContextAssembler that hands each thread only the case-guide prefix, the cast active at that date, and the facts its participants could know as of that date (knowledge gating) — never the full complaint and never a rolling "story so far" summary, which is the anti-pattern the current EmailGenerator uses and the direct cause of the "loses the thread" failure.

The dominant risks are not implementation bugs but corpus-quality and safety failure modes that are easy to miss until the corpus is inspected closely: relevant and non-relevant documents becoming trivially separable by style or metadata rather than content (label leakage, which defeats the entire purpose of a classifier-testing dataset), temporal knowledge leakage (characters or prose referencing facts or legal terms from the future), decoys and filler drifting on-topic, refusals or sanitization on criminal-scenario prompts, and legal-realism failures (hallucinated case citations, wrong discovery-rule limits, jurisdiction-format mismatches). Each has a concrete, code-enforceable mitigation (distribution-matched planning, knowledge-gated context, a "must-not-mention" list plus a blind-review QA pass, a refusal-aware result taxonomy, and a no-case-law-citation policy respectively), and PITFALLS.md maps every one to a specific phase and a verification check, which should be carried directly into the roadmap's acceptance criteria.

## Key Findings

### Recommended Stack

The existing stack (WinForms, OpenAI 2.1.0, MimeKit 4.18.1, DocumentFormat.OpenXml 3.0.2) is being extended, not replaced. The two headline stack risks are that .NET 8 reaches EOL on 2026-11-10 and that the hard-coded dall-e-3 model was shut down on 2026-05-12 (image generation is likely broken in production today). Both are one-line-ish fixes that should happen early.

**Core technologies:**
- net10.0-windows (SDK 10.0.4xx): current LTS runtime (support to 2028-11-14); trivial retarget, all other packages already support it — do first
- Anthropic 12.51.0 (official SDK): Claude chat, Message Batches, structured outputs (OutputConfig/JsonOutputFormat), prompt caching (CacheControlEphemeral) — do not use the old Anthropic.SDK/tryAGI.Anthropic community packages
- OpenAI 2.14.0 (upgrade from 2.1.0): GA ChatClient plus strict JSON-schema structured output, BatchClient (experimental, suppress OPENAI001), replaces dead dall-e-3 with gpt-image-1/gpt-image-2
- Microsoft.Extensions.AI.Abstractions 10.10.1: generates one strict-mode JSON Schema per DTO shared by both providers, replacing hand-written "respond in this shape" prompt strings that drift from the DTOs
- Microsoft.Data.Sqlite 10.0.12: local project store (case guide, plan/status, batch-id to custom_id maps) giving atomic transactional incremental saves — not EF Core, which is unnecessary weight for a single-file publish
- DocumentFormat.OpenXml 3.5.1 (upgrade from 3.0.2): long legal .docx with Word's native line-numbering for pleadings and 25-line deposition pages
- ClosedXML 0.105.1 plus CsvHelper 33.1.0: Excel/CSV answer key with correct quoting/styling at 5K rows

**Design decision (MEDIUM confidence, deliberate):** build a thin app-owned ILlmProvider/ILlmGateway above the native SDKs rather than making Microsoft.Extensions.AI.IChatClient the core abstraction. IChatClient has no batch concept; both batch APIs need native request bodies, so a single translator per provider used for both sync and batch keeps requests byte-identical (protecting prompt-cache hits and letting a failed batch item be retried synchronously with the exact same request). Both SDKs' IChatClient adapters remain available later for middleware/telemetry — nothing is lost by starting native.

### Expected Features

Domain reference research (MEDIUM-HIGH confidence — FRCP/discovery-practice conventions are well-established; corpus realism ratios are informed defaults, not verified statistics) establishes a clear anatomy checklist for every artifact type (complaint, investigation memo, deposition, RFP/RFA/interrogatory, answer-key coding fields) that legal/eDiscovery users will immediately judge the output against.

**Must have (table stakes):**
- Free-text scenario plus complexity preset driving parties/cast/timeline/doc length
- Legally-structured core document (caption, parties, jurisdiction/venue, consecutively numbered factual paragraphs, counts with elements and incorporation, prayer, jury demand, signature), generated section-by-section with section-level review/edit/regenerate
- Structured, ID-stable Cast, Key-Facts timeline, and Issue-code taxonomy — the "spine" every later artifact references
- Timeline-driven relevant threads anchored to specific facts with correct participants for that date, including pre/post-event chatter — this is the Core Value fix
- User-controlled relevant/non-relevant ratio, non-relevant noise categories (business, ancillary, newsletters/spam, personal), and near-miss decoys sharing vocabulary/people but not the alleged conduct
- Stable DocID/FamilyID/ThreadID and a CSV/Excel answer key (one row per email and attachment) with relevance, issue codes, key-fact links, and a one-line rationale captured at generation time, not inferred afterward
- Scale to ~5K emails with incremental save and per-item failure isolation
- Fictional entities only (legal/ethical requirement, echoing the real EDRM Enron PII controversy)

**Should have (differentiators — no public competitor combines these):**
- Hot-document flag, privilege-bearing threads plus privilege column, a review-protocol/coding-manual document (mirrors TREC's Topic Authority manual), deposition transcripts in true transcript format (25 lines/page, exhibit index, Q/A, reporter certificate), relevance rationale per document, a machine-readable case.json sidecar, and — the hardest and most valuable — concurrent overlapping storylines among shared characters without contradiction.

**Defer (v2+):** DAT/OPT load files, Bates numbering, privilege logs, discovery responses/objections, non-email sources (chat/RSMF), PII-detection datasets, pause/resume from a project file, up-front cost estimate, jurisdiction-perfect pleadings for every state, custodial duplicates (conflicts with current per-sender folder layout — needs an explicit decision before the answer-key writer is built).

### Architecture Approach

The system is a strict left-to-right pipeline — CaseGuide to CorpusPlanner to RunEngine to Artifacts — where a structured case guide (core doc to Issues to Cast to dated Facts to canonical Entity registry, all with stable IDs) is generated once and frozen, a CorpusPlanner turns it into an immutable Manifest that pins every message's date, sender, recipients, relevance label, and pinned specifics before any prose is written, and a RunEngine renders each ThreadPlan independently (sync or via provider Batch APIs), enriches attachments with per-item failure isolation, writes the EML, and appends one line to an append-only journal — never rewriting the manifest. The answer key is a pure projection of manifest plus journal, so it is correct by construction and survives a crash or cancellation.

**Major components:**
1. CaseGuideBuilder (CoreDocumentGenerator, Issue/Cast/Fact extractors, EntityRegistryBuilder, GuideValidator) — produces the single source of truth, user-editable before anything expensive runs
2. CorpusPlanner (BudgetAllocator, RelevantThreadPlanner, DecoyPlanner, FillerPlanner, ManifestValidator) — the hardest component; decides every date/sender/recipient/fact-reference/relevance-label in code, so the renderer only writes prose
3. ContextAssembler plus ThreadRenderer plus RenderValidator — builds a bounded, knowledge-gated context pack per thread (stable case-guide prefix plus point-in-time facts plus thread plan) and validates rendered output against the plan, with a bounded repair-retry loop
4. RunEngine / ThreadUnitOfWork — the per-thread unit of work (render, validate, map, enrich, write EML, journal, release memory) that fixes the data-loss, memory, and phase-barrier bugs in one move; routes independent work (filler/decoys) to Batch APIs and sequential/relevant threads to sync calls
5. Ai/ gateway layer (ILlmGateway, IBatchGateway, IMediaGateway) — the only folder touching provider SDKs directly; everything else is testable against fakes
6. RunStore/AnswerKeyWriter — atomic JSON plus append-only journal persistence; answer key and .docx/.xlsx exports are pure projections, regenerable after any partial run

The old EmailGenerator/storyline flow is retired via the strangler pattern: reusable leaves (signature correction, DTO mapping, document/image/voicemail/calendar generators) are extracted into the new pipeline, and the orchestration, phase barriers, and WizardState cycle are deleted once the new pipeline produces EMLs end-to-end.

### Critical Pitfalls

1. Letting the LLM own facts, dates, amounts, and counts instead of the code — build the deterministic plan first; the LLM only writes prose for a slot the code has already filled; overwrite model-supplied metadata with planned values.
2. Temporal knowledge leakage (characters/prose reference future facts or parrot pleading-ese) — never send the full complaint to email-generation prompts; give each render only knowledge-gated, point-in-time facts; scan for banned legal vocabulary before the triggering event.
3. Label leakage (relevant vs. non-relevant becomes separable by style/model/length/sender/date rather than content, which defeats the dataset's purpose) — use the same model/prompt/formatter path for both classes; match length/attachment/CC/thread-depth distributions by design in the planner; run a cheap metadata-only baseline classifier as an acceptance test (target AUC below about 0.7).
4. Decoys/filler drifting on-topic, and relevance not propagating through quoting/forwards/families — decoy/filler prompts must never receive allegation details; label at the document level after threading; run a blind-review QA pass (cheap model, no label shown) as the single highest-value QA step.
5. Refusals and silent sanitization on sensitive (e.g., criminal-conspiracy) scenarios — treat refusal/truncation as a first-class result type, not a JSON-parse failure to retry; frame prompts around evidentiary purpose and keep operational criminal detail abstract; run a core-document pre-flight before spending on the full corpus.
6. (Also flagged as critical, worth carrying forward): nothing-saved-until-the-end persistence, batch-API desktop-lifecycle assumptions (24h latency, unordered results, custom_id rules, orphaned batches on app close), and hallucinated case-law citations in generated pleadings.

## Implications for Roadmap

ARCHITECTURE.md's suggested build order is well-reasoned from the dependency graph and should be the roadmap's backbone; PITFALLS.md's phase labels align with it directly. Below is a synthesis into roadmap-shaped phases.

### Phase 1: Foundation and Provider Gateway
**Rationale:** Every later component calls the LLM gateway and the persistence layer; fixing known concurrency/data-loss bugs here is far cheaper than after the pipeline exists. Building Claude support here (not bolted on later) satisfies an explicit user requirement early.
**Delivers:** Test project scaffolding; deletion of dead code; Random.Shared/lock fixes; ILlmGateway/IBatchGateway/IMediaGateway abstractions over official Anthropic 12.51.0 and OpenAI 2.14.0 SDKs; structured-output schema generation shared across providers; DPAPI key storage for two providers; refusal/truncation-aware result taxonomy; retarget to net10.0-windows.
**Addresses:** "Add Anthropic Claude" requirement; provider abstraction constraint.
**Avoids:** Pitfalls 7 (refusals), 14 (structured-output failure modes), 17 (rate-limit retry storms).

### Phase 2: Domain Model and Persistence
**Rationale:** The manifest/journal schema is the contract every other component builds against; incremental persistence must exist before anything expensive is generated, not retrofitted after a data-loss incident.
**Delivers:** Domain/ records (CaseGuide, Fact, CastMember, Issue, Entity, Manifest, ThreadPlan, MessagePlan); RunStore with atomic JSON writes and an append-only JSONL journal; CaseSession replacing WizardState.
**Addresses:** Reliability/data-loss requirements ("completed threads saved incrementally," "one failed item doesn't abort the run").
**Avoids:** Pitfall 9 (nothing saved until the end / answer-key drift), Pitfall 6 (rewriting the manifest is O(n^2)).

### Phase 3: Case Guide Pipeline (Scenario to Core Doc to Cast/Facts/Issues)
**Rationale:** Guide quality bounds corpus quality, and this is cheap to fix early via review/edit before any bulk generation. It is also user-visible value on its own.
**Delivers:** Scenario/preset input, complexity presets, section-by-section CoreDocumentGenerator with numbered-paragraph enforcement, Issue/Cast/Fact/Entity extractors with stable IDs, deterministic CaseGuideValidator, review/edit/regenerate UI, .docx plus case.json export.
**Addresses:** Core-document, cast, and key-facts-timeline requirements; "review/edit/regenerate before continuing."
**Avoids:** Pitfall 2 (temporal leakage — ledger with knownBy/dates originates here), Pitfall 11 (legal realism/citation policy), Pitfall 12 (real-entity/IP risk pre-flight), Pitfall 13 (stale-edit propagation).

### Phase 4: Corpus Planner and Manifest (including Answer-Key projection from the plan)
**Rationale:** This is the hardest prompt in the system and the direct fix for the "loses the thread" failure. Validating the plan before spending render tokens is far cheaper than validating rendered prose, and the answer key can be demoed from the plan alone.
**Delivers:** BudgetAllocator (largest-remainder distribution), chunked RelevantThreadPlanner, DecoyPlanner (with MustNotMention), FillerPlanner, deterministic ManifestValidator, plan-review UI, answer-key writer as a manifest projection.
**Addresses:** Timeline-driven generation, continuity across batches, overlapping storylines, user-controlled relevance ratio, near-miss decoys, answer-key requirement.
**Avoids:** Pitfall 1 (LLM owns facts), Pitfall 3 (label leakage — distribution matching happens here), Pitfall 10 (unrealistic metadata/timestamps).

### Phase 5: Renderer and Sync Execution (text-only EMLs end-to-end)
**Rationale:** First fully working end-to-end corpus at small scale, proving the plan-then-render model before adding enrichment or batch complexity.
**Delivers:** Knowledge-gated ContextAssembler, ThreadRenderer plus RenderValidator with a bounded repair loop, RenderedThreadMapper (fixes the replyToIndex bug class by construction), incremental EML save plus journal, sync RunEngine with immutable progress reporting.
**Addresses:** Core Value (consistent evidence corpus); reliability requirements.
**Avoids:** Pitfall 2 (temporal leakage at render time), Pitfall 6 (on-the-nose evidence — subtlety levels), Pitfall 19 (thread drift/replyToIndex).

### Phase 6: Enrichment Extraction (attachments)
**Rationale:** Reuses working, already-existing builders; plugs into an existing, tested unit of work, so risk is contained.
**Delivers:** Document/Image/Voicemail/Calendar enrichers extracted from EmailGenerator, driven by manifest AttachmentSpecs, each with independent failure isolation.
**Addresses:** "Attachments as evidence" requirement (existing builders retained).
**Avoids:** Pitfall 18 (attachments contradicting the ledger); Pitfall 9's per-item try/catch requirement.

### Phase 7: Non-Relevant Corpus at Scale
**Rationale:** This is the majority of the corpus volume, so memory/scale problems surface here, and it must exist before batching pays off.
**Delivers:** Full FillerPlanner content generation, decoy rendering with the forbidden-facts list enforced, ratio control end-to-end, a 5K-scale test (memory, file counts, the base64-banner memory issue).
**Addresses:** Non-relevant category requirements, scale-to-5K requirement.
**Avoids:** Pitfall 3 (leakage audit becomes testable here), Pitfall 4 (decoy drift), Pitfall 20 (obviously-fake filler).

### Phase 8: Batch Execution and Cost Control
**Rationale:** Only valuable once there is a large body of independent work to batch; depends on LlmRequest being serializable (Phase 1) and slot IDs existing (Phase 4).
**Delivers:** IBatchGateway for OpenAI and Anthropic, persisted batches.json, requeue-of-errored/expired-items-to-sync, prompt-cache-aware and batch-aware cost accounting, hard budget cap, economy-mode toggle.
**Addresses:** "Bulk generation can use provider Batch APIs for lower cost" requirement.
**Avoids:** Pitfall 8 (batch desktop-lifecycle assumptions — the single highest-risk integration), Pitfall 15 (cost blowups), Pitfall 16 (silent cache misses).

### Phase 9: Ancillary Discovery Documents
**Rationale:** Off the critical path for the core value; only needs the case guide (Phase 3), so it can run in parallel with Phases 4-8 or be scheduled last depending on team bandwidth.
**Delivers:** Depositions (with correct transcript formatting), RFPs, RFAs, Interrogatories, all grounded in the structured cast/ledger rather than raw complaint text.
**Addresses:** Optional ancillary-document requirements.
**Avoids:** Pitfall 11 (jurisdiction/rule-limit realism), Pitfall 13 (must consume structured data, not stale prose).

### Phase 10: Cutover and Cleanup
**Rationale:** Strangler-pattern completion; must be last so the old flow remains a safety net until the new pipeline is proven end-to-end.
**Delivers:** Removal of old wizard steps/generators and WizardState, UI caps raised to 5K, optional blind-classifier QA pass, full "Looks Done But Isn't" checklist run (leakage audit, blind-review agreement, timeline integrity, ledger consistency, metadata sanity, citation policy, kill-mid-run test, batch-failure test, cache-hit ratio, refusal handling, style-repetition report).

### Phase Ordering Rationale

- Dependency-driven: domain model and gateway must exist before anything is generated (Phases 1-2); the guide must exist before planning (3 to 4); the plan must be validated before spending render tokens (4 to 5); enrichment and batch both plug into an already-working sync renderer (5 to 6, 5 to 7 to 8).
- Risk-driven: the two highest-uncertainty components — the corpus planner (Phase 4) and batch execution (Phase 8) — are scheduled after their dependencies are proven at small scale, not attempted first.
- Value-driven: the case guide (Phase 3) is a standalone demoable artifact even before any corpus exists, and ancillary docs (Phase 9) are deliberately decoupled so they don't block the corpus critical path.
- Pitfall-driven: persistence and failure isolation (Phase 2) are placed before any bulk generation specifically because Pitfall 9 (data loss) and the existing "one failed attachment aborts the run" bug are structural, not cosmetic, and must be fixed at the foundation rather than patched later.

### Research Flags

Needs deeper research during phase planning (per ARCHITECTURE.md and PITFALLS.md, both independently flagging the same phases):
- Phase 4 (Corpus Planner): highest uncertainty in the system — chunk size for planning, how many neighbor facts to include, and planning cost at 5K scale need experimentation, not just design.
- Phase 8 (Batch Execution): verify the OpenAI .NET SDK's batch/file surface in the pinned version (marked experimental, OPENAI001); verify how Anthropic cache_control passes through Microsoft.Extensions.AI for the sync path; desktop-lifecycle manifest design for orphaned batches needs a spike.
- Phase 9 (Ancillary Docs): jurisdictional discovery-rule limits (interrogatory caps, RFA numbering, transcript line-count conventions) need per-jurisdiction verification before being encoded as template constants.
- Phase 7 / P-Key (leakage audit): practical in-process design for a metadata-only baseline classifier (feature set, threshold) needs a short spike, though no external library is required.

Phases with standard, well-documented patterns (safe to skip /gsd-research-phase):
- Phase 1 (Foundation/Gateway): provider SDK surfaces, retry/backoff, and structured-output mechanics are verified against official docs (HIGH confidence).
- Phase 2 (Domain/Persistence): SQLite plus append-only journal is a standard, low-risk pattern.
- Phase 5 (Renderer, sync path): the plan-then-render pattern and knowledge-gating are fully specified in ARCHITECTURE.md with example code.
- Phase 6 (Enrichment extraction): pure extraction of already-working existing builders.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH for package versions/API surfaces (verified against NuGet/SDK source); MEDIUM for architecture choices built on them (e.g., app-owned gateway vs. IChatClient-as-core is a design judgment) |
| Features | MEDIUM overall — legal document anatomy and discovery rules are HIGH (FRCP, established practice); answer-key/coding conventions MEDIUM-HIGH (Relativity docs, TREC Legal Track); corpus realism statistics (thread depth, attachment ratio, custodian distribution) are LOW-MEDIUM — sensible defaults, not verified published distributions |
| Architecture | MEDIUM-HIGH — provider/SDK/batch facts verified against official docs; the pipeline pattern itself is synthesized from long-form-generation research plus the existing codebase, so it is a recommended design, not an established off-the-shelf framework |
| Pitfalls | MEDIUM overall — provider API facts (batch limits, caching minimums, structured-output constraints) are HIGH; corpus-realism and relevance-leakage pitfalls are MEDIUM (established NLP shortcut-learning findings applied by analogy); legal/defamation risk framing is MEDIUM/LOW (not legal advice) |

**Overall confidence:** MEDIUM-HIGH. Technical/provider facts are strongly sourced; the domain-specific design (plan-then-render, knowledge gating, label-leakage auditing) is well-reasoned and internally consistent across all four research files but is a recommended synthesis rather than a proven-in-production pattern, since no directly comparable public product exists.

### Gaps to Address

- OpenAI Responses vs. Chat Completions and the exact .NET batch/file call sequence are marked experimental/shifting in the SDK; confirm against the pinned package version before building Phase 8.
- Anthropic cache_control pass-through when going via Microsoft.Extensions.AI.IChatClient is unconfirmed (may need provider-specific RawRepresentationFactory); resolve during Phase 8 planning.
- Corpus realism ratios (thread depth, attachment percentage, custodian power-law distribution) have no authoritative published source — treat FEATURES.md's table as defaults to tune during Phase 4/7 implementation, not hard requirements.
- Custodial duplicates vs. per-sender-folder output layout is an open design decision (FEATURES.md flags a direct conflict) that should be resolved explicitly before the answer-key writer (Phase 4) is built, even though duplicates themselves are v1.x.
- Jurisdiction-specific discovery rule limits (interrogatory caps, RFA numbering conventions, deposition line-count rules) are MEDIUM confidence training knowledge and need verification during Phase 9.
- Whether ancillary docs (exhibit-citing depositions, genuineness RFAs) require corpus DocIDs to exist first is a real ordering dependency FEATURES.md surfaces — the roadmap should decide whether Phase 9 splits into "guide-only" and "post-corpus" sub-phases.

## Sources

### Primary (HIGH confidence)
- github.com/anthropics/anthropic-sdk-csharp and platform.claude.com/docs (SDKs, batches, prompt caching, structured outputs, rate limits) — verified API surfaces and limits
- github.com/openai/openai-dotnet and developers.openai.com/api/docs (Batch API, prompt caching, structured outputs, deprecations) — verified API surfaces and limits
- NuGet flat-container/registration API (queried 2026-09-29) — package versions for Anthropic, OpenAI, Microsoft.Extensions.AI, ClosedXML, DocumentFormat.OpenXml, Microsoft.Data.Sqlite, CsvHelper, Dapper
- dotnet.microsoft.com support policy — .NET 8 EOL 2026-11-10, .NET 10 LTS to 2028-11-14
- FRCP Rules 8, 10, 11, 33, 34, 36 (law.cornell.edu) — pleading and discovery-rule structure
- Relativity Active Learning / family-propagation docs — answer-key/coding conventions
- Project's own .planning/codebase/ (ARCHITECTURE.md, CONCERNS.md, STRUCTURE.md) — existing bugs and constraints grounding all four research tracks

### Secondary (MEDIUM confidence)
- TREC Legal Track overview papers (2009/2011) — coding-manual and topic-authority conventions
- arXiv: FactTrack, ConWriter/ConStory-Bench, "Lost in Stories" — long-form LLM consistency failure modes and mitigations informing the plan-then-render/knowledge-gating design
- arXiv: shortcut-learning / synthetic-artifact-auditing papers — label-leakage risk applied by analogy to this domain
- "Learning from Litigation" (EDRM Enron v2 stats), EDRM Enron PII controversy writeups — corpus scale and ethics precedent
- Mata v. Avianca sanctions order — hallucinated-citation risk precedent

### Tertiary (LOW confidence)
- Corpus realism numeric ratios (thread depth, attachment percentage, custodian distribution) — training-knowledge defaults, not a verified published distribution
- OpenAI current model naming/pricing — single summarized fetch, flagged for verification before hard-coding

---
*Research completed: 2026-09-29*
*Ready for roadmap: yes*
