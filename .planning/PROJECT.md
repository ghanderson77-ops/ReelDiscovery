# ReelDiscovery

## What This Is

ReelDiscovery is a Windows desktop app (.NET 10 WinForms) that generates realistic, internally consistent eDiscovery sample data sets on OpenAI or Anthropic Claude. The user describes a real-life-style dispute or investigation (e.g. "construction defect dispute between a window subcontractor, a general contractor and the developer on a midrise commercial project", or "correspondence inspired by Breaking Bad evidencing a criminal conspiracy") and picks a complexity preset. The app writes the core legal document (Complaint / Petition / Investigation Memo), derives a validated case guide (issues, cast, dated key facts, canonical values), plans every thread and message in code, and then generates an email corpus of up to ~5,000 messages with attachments. The relevant mail is the evidence behind the allegations; the rest is a user-controlled mix of daily business, ancillary, newsletter/spam/personal traffic and near-miss decoys. Every run produces an exact CSV/XLSX answer key and a review protocol, and can optionally produce depositions, RFPs, RFAs and interrogatories.

It is for people who need believable, coherent eDiscovery demo/training/test corpora — especially for demonstrating review workflows and automated relevance classification.

## Core Value

Every relevant email in a multi-thousand-document corpus must stay faithful to one consistent case story (the core pleading and its key-facts timeline) — the corpus reads as the actual evidence behind the allegations, not a pile of disconnected emails.

## Current State

**Shipped:** v1.0 Case-Driven Generation (2026-10-13). 10 phases, 170 plans. See `.planning/MILESTONES.md`.

- The case-driven flow is the only flow; the legacy topic → storylines wizard was removed in the v1.0 cutover.
- ~85,600 lines of app C# and ~83,100 lines of tests (xUnit, 4,697 passing outside the Scale/LiveSpike suites).
- Live testing on both providers was reported complete by the user on 2026-10-13; all phase verifications are closed. Two debug sessions remain open (STATE.md Deferred Items). Released publicly as ReelDiscovery 2.0.0.

## Next Milestone Goals

Not yet defined — start with `/gsd-new-milestone`. Candidates:

- Open audit warnings N2-N4 (discovery staleness false positives, guide-changed crash recovery, relabel edge cases) and Phase 7 design debt (leakage sender weighting, deterministic OpenXml, CX-07 drain bound).
- v2 requirements below.

## Requirements

### Validated

- ✓ Themed HTML email bodies with per-domain branding and signatures; realistic threading (Message-ID, In-Reply-To/References, RE:/FW:, quoting); `.eml` output via MimeKit — existing, kept
- ✓ Attachments: Word/Excel/PowerPoint (OpenXml), calendar invites (.ics), inline images, voicemail audio — existing, now driven by the case guide (v1.0)
- ✓ Supported runtime (.NET 10), test suite, thread-safe generation; one provider-agnostic gateway with strict structured outputs for OpenAI and Claude; DPAPI-encrypted keys; gpt-image replaces dall-e-3 — v1.0
- ✓ Free-text scenario + complexity preset (Simple / Standard / Complex) → realistic Complaint / Petition / Investigation Memo, citation-safe, reviewable, editable and regenerable — v1.0
- ✓ ID-stable case guide (issues, cast, dated facts timeline, canonical values), validated, editable, saved as case.json and exported to .docx — v1.0
- ✓ User controls corpus size (up to 5,000), relevant share, non-relevant mix, date window and attachment types — v1.0
- ✓ Code plans every thread and message before prose; validator + plan review/re-plan/approve; answer key (CSV/XLSX) and review protocol derived from the approved plan — v1.0
- ✓ Relevant threads rendered against specific key facts in chronological waves with a point-in-time knowledge gate and a narrative ledger; overlapping issues among the same people without contradiction — v1.0
- ✓ Incremental, journaled saving; one failed item never aborts the run; continue / start over / regenerate thread — v1.0
- ✓ Per-custodian mailboxes with X-ReelDiscovery-DocID headers matching the answer key — v1.0
- ✓ Non-relevant traffic (daily business, ancillary characters, newsletters/spam/personal, near-miss decoys) through the same pipeline — v1.0
- ✓ ~5,000-email runs with Batch APIs, prompt caching, cost accounting, blind-review and leakage QA — v1.0
- ✓ Optional depositions, RFPs, RFAs and interrogatories consistent with the guide, exported as .docx — v1.0
- ✓ Edit and spend safety: guide edits withdraw plan approval; discovery staleness tracking; manual answer-key relabels preserved; cross-provider batch resolution — v1.0

### Active

<!-- v2 candidates carried from v1.0 REQUIREMENTS.md (deferred section). Confirm in /gsd-new-milestone. -->

- [ ] LEGAL-01: Indictment / affidavit in support of criminal complaint as a core document type
- [ ] LEGAL-02: Responses and objections to written discovery
- [ ] LEGAL-03: Depositions citing corpus DocIDs as exhibits; genuineness RFAs referencing specific emails (partly delivered in v1.0 Phase 8)
- [ ] EXP-01: DAT/OPT load files and Bates numbering
- [ ] EXP-02: Privilege log
- [ ] EXP-03: JSON answer key
- [ ] EXP-04: Seed/control-set suggestion columns and dedupe group IDs
- [ ] WF-01: Pause/resume a run from a saved project file (partly delivered: reopening a case resumes at its stage; Continue Generating)
- [ ] WF-02: Up-front cost estimate before generation (partly delivered: Review Plan shows an estimate)
- [ ] CONT-01: Non-email sources (chat/RSMF, loose files)
- [ ] CONT-02: PII-bearing documents with safe synthetic identifiers for PII-detection demos

### Out of Scope

- Keeping the old topic → storylines wizard — replaced by the case-driven flow and deleted in v1.0
- Second LLM pass grading every document's relevance — labels are known from the plan; QA samples instead
- Interactive per-email editing of the corpus — unmanageable at 5K and breaks continuity; edit at guide level (a manual answer-key relabel is supported)
- Full-length (200+ page) depositions — cost and drift; excerpts labeled as such
- Forensic-grade headers / PST/MSG containers — different product (processing-tool testing)
- Jurisdiction-perfect local rules for every court — cannot validate; one consistent jurisdiction per case
- Multi-language corpora — English only
- Cross-platform (macOS/Linux) — Windows WinForms app

## Context

- v1.0 rebuilt the app around a case guide: the ~2,000-line `EmailGenerator` god class, `OpenAIService`, Slack telemetry and the legacy wizard are gone. Work is organized under `Services/Ai`, `Services/Case`, `Services/Corpus`, `Services/Evidence`, `Services/Discovery` with case state in a per-case folder (case.json, corpus-plan.json, run journal, batch ledger, answer key + relabel sidecar).
- Generation is grounded in structured data: code owns dates, participants, counts and labels; the LLM writes prose within a knowledge gate and a narrative ledger; end-of-run timeline and canonical-value scans check consistency.
- Known debt (see `milestones/v1.0-MILESTONE-AUDIT.md`): render tests run at 120 DPI (150% checked by hand at release); leakage audit Warning on the default plan (sender weighting); Office attachments not byte-deterministic; SUMMARY requirements frontmatter missing in many plans.

## Constraints

- **Tech stack**: C# / .NET 10 WinForms (net10.0-windows), Windows x64, single-file self-contained publish
- **Libraries**: MimeKit (EML), DocumentFormat.OpenXml (Office), OpenAI and Anthropic .NET SDKs
- **AI**: OpenAI and Claude both supported behind one gateway; media (images, TTS) via OpenAI
- **Scale**: ~5K emails per run — incremental persistence, bounded context per call, Batch APIs and prompt caching
- **Consistency**: generation must be grounded in structured case-guide data (cast, dated facts, canonical values)

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Case-driven flow replaces the existing storyline wizard | User wants one coherent approach; old flow fails on complex cases | ✓ Good — legacy flow deleted in v1.0 cutover |
| Core legal document is the "guide"; key-facts timeline drives email generation | Timeline-driven generation keeps threads cohesive across thousands of emails | ✓ Good — Phase 5 live UAT confirmed consistency on one run |
| Complexity via presets (Simple/Standard/Complex) | Simple UX; presets drive parties, issues, characters, timeline span | ✓ Good |
| User reviews/edits core doc, cast, and key facts before corpus generation | Guide quality determines corpus quality; cheap to fix early | ✓ Good — needed Phase 9 to make later edits safe |
| Answer key in CSV/Excel | Needed to demonstrate/score automated classification | ✓ Good — relabels round-trip via sidecar |
| Add Anthropic Claude alongside OpenAI | User request; enables provider choice | ✓ Good — both providers tested live at release |
| Incremental saving + Batch API; no pause/resume in v1 | Protects long 5K runs and cuts cost without project-file complexity | ✓ Good — journal + batch ledger; reopen resumes in practice |
| Keep all existing attachment types | Reuse working builders; richer evidence | ✓ Good |
| Running narrative ledger; relevant threads in chronological waves | Later emails build on earlier inventions; trade-off: less parallel | ✓ Good |
| Code-owned plan (manifest) before prose; answer key derived from plan | LLM must not own dates/counts/labels; exact key by construction | ✓ Good |
| Plan stored in sibling `corpus-plan.json`; case.json format v2 holds settings/approval | ~3-6 MB plan at 5K would make every autosave slow | ✓ Good |
| Validator findings are warnings only; nothing blocks Approve; nothing re-plans silently | User choice (D-24/D-25); paid runs only on explicit action | ✓ Good — extended in Phase 9 (no paid planning on pass-through) |
| Binary relevance + Hot/Privileged/Near-miss flags; chronological DocIDs with opaque paths | Classifier-friendly key without label leakage via IDs or paths | ⚠️ Revisit — leakage audit still flags sender weighting / weekday skew |
| Per-custodian mailbox folders; privilege tagging, hot docs, review protocol, blind-review QA in v1 | User scoping after research | ✓ Good |
| Retarget .NET 10; gpt-image replaces dall-e-3; DPAPI keys; add tests | .NET 8 EOL; dall-e-3 retired; security; no tests | ✓ Good — legacy Base64 key migration later dropped (D-22) |
| Guide edits withdraw approval everywhere; run refuses a stale/unapproved plan | Audit W1: approval survived guide edits | ✓ Good (Phase 9) |
| Single answer-key writer funnel + relabel sidecar; relabels dropped only on Re-plan, with a warning | Audit W4: relabels were overwritten | ✓ Good (Phase 10) |
| Other provider's pending batches resolved (collect/cancel) before any run; never resubmitted silently | Audit W5: provider switch double-paid | ✓ Good (Phase 10); foreign items finished before cancel are itemized, not avoided (D-25) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-10-13 after v1.0 milestone*
