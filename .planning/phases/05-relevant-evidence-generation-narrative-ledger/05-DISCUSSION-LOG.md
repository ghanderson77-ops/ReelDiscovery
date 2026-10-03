# Phase 5: Relevant Evidence Generation & Narrative Ledger - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-03
**Phase:** 05-relevant-evidence-generation-narrative-ledger
**Areas discussed:** Narrative ledger, Waves checks & retries, Email look & voice
**Not selected:** Run screen & re-runs (left to Claude's discretion)

---

## Narrative ledger

| Option | Description | Selected |
|--------|-------------|----------|
| Same call | Thread-writing call also returns structured new details | ✓ |
| Separate extraction call | Second cheaper call extracts details; ~2x calls | |
| You decide | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Structured detail | Date, kind, statement, value, knowers, fact/issue IDs, source DocID | ✓ |
| Short prose summary per thread | 2-3 sentences per thread | |

| Option | Description | Selected |
|--------|-------------|----------|
| Filtered and capped | Earlier + known to participants, ranked, ~25-40 entries | ✓ |
| Everything participants know | Uncapped | |
| You decide | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Export only | Saved with case; "story so far" .docx from Case Summary | ✓ |
| Read-only viewer | Browse entries in app | |
| Viewer + editing | Editable entries affecting later threads | |

---

## Waves, checks & retries

| Option | Description | Selected |
|--------|-------------|----------|
| Time-window waves, parallel inside | Short date windows; parallel within a wave; ledger from earlier waves | ✓ |
| Strictly sequential | One thread at a time | |
| Dependency-aware | Shared participants/facts sequential, rest parallel | |

| Option | Description | Selected |
|--------|-------------|----------|
| Code checks + ledger values | Counts/order, unknown people, canonical values, future facts, ledger conflicts | ✓ |
| Code checks + LLM consistency judge | Extra LLM pass | |
| You decide | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Keep it, flag it | Save best attempt; journal + key status warning; enters ledger | ✓ |
| Skip it | Mark Failed, exclude from ledger | |
| Keep, but exclude from ledger | | |

| Option | Description | Selected |
|--------|-------------|----------|
| 2 retries | 3 attempts with violation feedback | ✓ |
| 1 retry | | |
| You decide | | |

---

## Email look & voice

| Option | Description | Selected |
|--------|-------------|----------|
| Themed HTML + plain-text part | Reuse HtmlEmailFormatter, multipart/alternative | ✓ |
| Plain text only | | |

**User's note:** "I'm open to formatted text where appropriate, bolding, etc. Not too crazy."

| Option | Description | Selected |
|--------|-------------|----------|
| Mostly indirect | ~60% indirect / 30% clear / 10% explicit; hot always explicit; code-assigned | ✓ |
| Balanced thirds | | |
| Let the model judge | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Realistic markers | Banner/subject tag on most; a few unlabeled | ✓ |
| Always labeled | | |
| Content only | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Story tier | Strong model for relevant threads | ✓ |
| Volume tier | | |
| Story for hot, Volume for rest | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Cast-driven, code-appended signature | VoiceNote + SignatureStyle in prompt; code appends Signature | ✓ |
| Model writes signatures | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Realistic spread | Code-assigned length band per message | ✓ |
| Let the model decide | | |

---

## Claude's Discretion

- Run screen, progress, partial-run continue/start-over behavior, close-during-run confirm
- Journal format/storage, mailbox layout details, threading details, org themes for case orgs
- Wave size, in-wave parallelism, ledger cap/ranking, prompt layout, extra code checks, story-so-far .docx layout

## Deferred Ideas

- In-app ledger viewer/editor
- LLM consistency judge (possible Phase 7 QA)
- Strictly sequential / dependency-aware scheduling
