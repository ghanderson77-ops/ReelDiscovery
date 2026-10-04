# Phase 6: Attachments & Non-Relevant Corpus - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-03
**Phase:** 06-attachments-non-relevant-corpus
**Areas discussed:** Model tier vs. leakage, Attachment content, Non-relevant texture, Run & media failures

---

## Model tier vs. leakage

| Question | Options | Selected |
|----------|---------|----------|
| Tier for non-relevant threads | Story for all (rec) / Volume for filler / User setting default Story | Volume for filler |
| Batch filler threads per call | One per call (rec) / Batch 3-5 / You decide | One thread per call |
| Tier for decoys | Story for decoys (rec) / Volume like filler | Story for decoys |
| Style parity effort | Shared prompt + code shaping (rec) / Plus leakage note / Don't worry | Shared prompt + code shaping |
| Non-relevant checks | Lighter checks (rec) / Same full set / Structure only | Structure only |
| Filler and ledger | No (rec) / Per-custodian side ledger | No |

**Notes:** User knowingly accepted leakage risk of Volume filler for cost; Phase 7 audit measures it.

---

## Attachment content

| Question | Options | Selected |
|----------|---------|----------|
| When content is written | Separate call after thread (rec) / Same call | Separate call after thread |
| Richness | Realistic but bounded (rec) / Short stubs / Vary by importance | Vary by importance |
| Version chains | Yes for relevant (rec) / No / You decide | Yes for relevant docs |
| Relevant attachment checks | Same as emails (rec) / Canonical only | Same code checks as emails |
| Attachments write ledger | Yes (rec) / No | Yes |
| Doc look | Org-branded reuse builders (rec) / Plain as-is | Org-branded, reuse builders |
| Invites | Plan + email text (rec) / Tie to ledger meetings | Tie to ledger meetings |

---

## Non-relevant texture

| Question | Options | Selected |
|----------|---------|----------|
| Daily-business context | Small invented side-world (rec) / Ad hoc per thread | Small invented side-world |
| Bulk mail realism | Styled, recognizable (rec) / Business template | Styled, recognizable |
| Decoy guard | Code scan must-not-mention (rec) / + LLM judge / Structure only | Code scan of must-not-mention |
| Personal mail | Mundane & workplace-safe (rec) / Include sensitive | Mundane & workplace-safe |

---

## Run & media failures

| Question | Options | Selected |
|----------|---------|----------|
| Run order | Relevant waves then filler (rec) / Interleaved by date / Separate buttons | Relevant waves first, then filler |
| Failed attachment | Write email without it (rec) / Hold email as failed | Write email without it |
| Images | Contextual photos/graphics (rec) / Charts/scans with text | Contextual photos/graphics |
| Voicemail | Phone-system email + transcript (rec) / Audio only | Phone-system email + transcript |
| Cost display | Fold into ticker (rec) / Separate media line | Fold into existing ticker |

---

## Claude's Discretion

- Richness thresholds, attachment DTO shapes, version-chain identification, side-world generation/storage, bulk-mail formatter designs, voicemail sender/audio format, filler parallelism, journal records, invite-to-meeting matching and fallback, run-screen copy.

## Deferred Ideas

- LLM judge for decoys (Phase 7 QA); per-custodian side ledger; sensitive personal mail; separate media cost line; multi-thread batching.
