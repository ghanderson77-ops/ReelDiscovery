# ReelDiscovery

## What This Is

ReelDiscovery is a Windows desktop app (.NET 8 WinForms wizard) that generates realistic, internally consistent eDiscovery sample data sets. The user describes a real-life-style dispute or investigation (e.g. "construction defect dispute between a window subcontractor, a general contractor and the developer on a midrise commercial project", or "correspondence inspired by Breaking Bad evidencing a criminal conspiracy"). The app first writes the legal "case guide" — a comprehensive Complaint / Petition / Investigation Memo plus an internal Cast of Key Characters and Summary of Key Facts & Events — and then generates a large email corpus (threads + attachments) that serves as the evidence of the alleged conduct, mixed with a user-controlled share of non-relevant material, plus an answer key describing which documents are relevant and why.

It is for people who need believable, coherent eDiscovery demo/training/test corpora — especially for demonstrating review workflows and automated relevance classification.

## Core Value

Every relevant email in a multi-thousand-document corpus must stay faithful to one consistent case story (the core pleading and its key-facts timeline) — the corpus reads as the actual evidence behind the allegations, not a pile of disconnected emails.

## Requirements

### Validated

<!-- Inferred from existing code (see .planning/codebase/). -->

- ✓ Multi-step WinForms wizard shell with shared state, Back/Next navigation and live token/cost ticker — existing
- ✓ OpenAI integration: chat, JSON-mode completions, DALL-E 3 images, TTS-1 speech, retry on 429/503, token/cost tracking — existing
- ✓ Email thread generation with realistic threading (Message-ID, In-Reply-To/References, RE:/FW:, quoting) — existing
- ✓ Themed HTML email bodies with per-domain branding and signatures — existing
- ✓ Attachments: Word/Excel/PowerPoint (OpenXml), calendar invites (.ics), inline images, voicemail audio — existing
- ✓ `.eml` output via MimeKit, optionally organized into per-sender folders — existing
- ✓ API key persistence, model/pricing configuration, disclaimer gate, opt-in telemetry — existing

### Active

<!-- The case-driven generation flow. Hypotheses until shipped. -->

**Case scenario & core document**
- [ ] User enters a free-text real-life scenario to seed a case
- [ ] User selects a complexity preset (Simple / Standard / Complex) that drives # of parties, issues/counts, characters, timeline span and core-document length
- [ ] System generates the core legal document appropriate to the scenario (Complaint / Petition / Investigation Memo), realistic and comprehensive
- [ ] User can review, edit, and regenerate the core document before continuing

**Case guide (internal, exportable)**
- [ ] System derives a Cast of Key Characters (name, title, organization, email address, relevant date range, summary of key involvement)
- [ ] System derives a Summary of Key Facts & Events as a dated timeline tied to the allegations/issues
- [ ] User can review, edit, and regenerate the cast and key-facts timeline
- [ ] Case guide artifacts (core doc, cast, key facts) can be exported (e.g. .docx)

**Ancillary discovery documents (optional)**
- [ ] User can optionally generate deposition transcripts for key witnesses
- [ ] User can optionally generate Requests for Production
- [ ] User can optionally generate Requests for Admission
- [ ] User can optionally generate Interrogatories
- [ ] All ancillary documents are consistent with the core document and key facts

**Timeline-driven evidence corpus**
- [ ] Relevant emails/threads are generated against specific key facts/events, with the characters involved at that point in time, so the corpus builds a cohesive story
- [ ] Continuity is maintained across batches (dates, roles, amounts, prior messages) so threads don't lose the thread
- [ ] Multiple overlapping issues/storylines can run concurrently among the same people without contradiction
- [ ] Existing attachment types (Office docs, calendar invites, images, voicemail) are generated as evidence driven by the case guide
- [ ] User controls the relevant vs. non-relevant proportion of the corpus
- [ ] Non-relevant material includes: same custodians' daily business, other ancillary characters, newsletters/spam/personal, and near-miss decoys (same project/keywords, not about the alleged conduct)
- [ ] Corpus scales to ~5,000 emails (majority may be non-relevant)

**Answer key**
- [ ] System produces a CSV/Excel answer key: one row per document with file, custodian, date, relevance, related issue(s)/allegation(s), and linked key fact(s)

**Reliability & cost at scale**
- [ ] Completed threads are saved incrementally — an interruption or single failure never loses finished work
- [ ] One failed item (attachment, image, etc.) does not abort the run
- [ ] Bulk generation (especially non-relevant filler) can use provider Batch APIs for lower cost

**AI providers**
- [ ] Anthropic Claude models are supported in addition to OpenAI (provider-selectable)

### Out of Scope

- Keeping the old topic → storylines → characters wizard flow — the case-driven flow replaces it
- Pause/resume of an interrupted run from a saved project file — not requested for v1; incremental saving covers data-loss
- Up-front cost estimate before generation — not requested for v1 (live cost ticker remains)
- DAT/Concordance load files and JSON answer key — CSV/Excel key only for v1
- Non-email native file collections (loose file shares, chat/Slack, SMS) — email corpus with attachments is the evidence format
- Cross-platform (macOS/Linux) — Windows WinForms app

## Context

- Brownfield: ~9,700 lines of C#, .NET 8 WinForms, single project, no tests. Codebase map in `.planning/codebase/`.
- The current flow works for small collections but breaks down on complex scenarios / overlapping issues: emails lose the thread and don't build a cohesive story. Root causes include generation from loose storylines rather than a fact timeline, per-batch context loss, and known bugs (e.g. `replyToIndex` resolved as absolute instead of batch-relative for storylines >15 emails).
- `Services/EmailGenerator.cs` is a ~2,000-line god class with dead code; `OpenAIService` is concrete with no interface — both block adding Claude and a new pipeline cleanly.
- Known data-loss bug: nothing is saved until all phases finish; one failed attachment/image/calendar/voicemail aborts the whole run and discards all output.
- Shared non-thread-safe `System.Random` and mutation in parallel sections.
- Prompts are hard-coded interpolated strings, not linked to DTO schemas.
- Output is `.eml` files on local disk; settings under `%LOCALAPPDATA%\ReelDiscovery\`.

## Constraints

- **Tech stack**: C# / .NET 8 WinForms, Windows x64, single-file self-contained publish — existing app and distribution model
- **Libraries**: MimeKit (EML), DocumentFormat.OpenXml (Office) — reuse existing builders
- **AI**: OpenAI remains supported; Claude added — provider abstraction needed
- **Scale**: ~5K emails per run — requires incremental persistence, bounded context per call, and cost control (Batch APIs)
- **Consistency**: generation must be grounded in structured case-guide data (cast, dated facts) rather than free-form context

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Case-driven flow replaces the existing storyline wizard | User wants one coherent approach; old flow fails on complex cases | — Pending |
| Core legal document is the "guide"; key-facts timeline drives email generation | Timeline-driven generation keeps threads cohesive across thousands of emails | — Pending |
| Complexity via presets (Simple/Standard/Complex) | Simple UX; presets drive parties, issues, characters, timeline span | — Pending |
| User reviews/edits core doc, cast, and key facts before corpus generation | Guide quality determines corpus quality; cheap to fix early | — Pending |
| Answer key in CSV/Excel | Needed to demonstrate/score automated classification | — Pending |
| Add Anthropic Claude alongside OpenAI | User request; enables provider choice | — Pending |
| Incremental saving + Batch API; no pause/resume in v1 | Protects long 5K runs and cuts cost without project-file complexity | — Pending |
| Keep all existing attachment types | Reuse working builders; richer evidence | — Pending |

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
*Last updated: 2026-09-29 after initialization*
