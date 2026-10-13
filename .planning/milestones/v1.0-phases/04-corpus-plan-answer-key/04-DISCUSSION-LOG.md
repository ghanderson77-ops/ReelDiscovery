# Phase 4: Corpus Plan & Answer Key - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-02
**Phase:** 04-corpus-plan-answer-key
**Areas discussed:** Corpus settings, How planning works, Plan review screen, Answer key & protocol

---

## Corpus settings

| Question | Options presented | Selected |
|----------|-------------------|----------|
| Size control | Presets + custom / Number box only / Derived from complexity | Presets + custom ✓ |
| Ratio control | Slider with live counts / Ratio presets / Exact counts | Slider with live counts ✓ |
| Non-relevant mix | Defaults + Advanced / Always visible / Fixed split | Defaults + Advanced ✓ |
| Hot/privileged/attachment rate | Automatic, shown in summary / Explicit controls / You decide | Automatic, shown in summary ✓ |
| Date window | Guide timeline + padding / Exactly the fact timeline / Free start/end | Guide timeline + padding ✓ |
| Step layout | Two steps / One combined step | Two steps ✓ |
| Media types | Type toggles in settings / All types always | Type toggles ✓ |
| Persist settings | Saved in case.json / Not saved | Saved in case.json ✓ |

## How planning works

| Question | Options presented | Selected |
|----------|-------------------|----------|
| Relevant planning | Code skeleton + LLM thread plans / Pure code / LLM-led plan | Code skeleton + LLM ✓ |
| Non-relevant slots | Code-only slots / Code + LLM subject seeds now | Code + LLM subject seeds now ✓ |
| Cost | Plan cost + generation estimate / Ticker only | Plan cost + generation estimate ✓ |
| Reproducibility | Seeded code layer / Not needed | Not needed ✓ |
| Volume spread | Weighted by significance / Even per fact | Weighted by significance ✓ |
| Calendar rules | US business calendar (full realism) / Simple business hours | Simple business hours ✓ |
| Calendar follow-up (PLAN-04 needs holidays + latency) | Simple + latency + holidays / Simple + latency, no holidays / Full realism | Simple + latency + holidays ✓ |
| Progress | Inline progress + Cancel / Keep partial chunks | Inline progress + Cancel ✓ |
| Repair | Auto-repair, then report / Report only | Auto-repair, then report ✓ |

## Plan review screen

| Question | Options presented | Selected |
|----------|-------------------|----------|
| Layout | Summary dashboard + thread list / Summary only | Summary only ✓ |
| Editing | Read-only + re-plan / Light edits / Re-plan one thread or chunk | Read-only + re-plan ✓ |
| Validator gate | Errors block, warnings ask / Warnings only | Warnings only ✓ |
| Stale plan | Stale banner, re-plan / Block until re-plan | Stale banner, re-plan ✓ |
| Breakdowns (PLAN-07) | Compact tables beside histogram / Segmented breakdown card | Compact tables ✓ |
| Re-plan | Re-plan with optional guidance / Re-plan button only | With optional guidance ✓ |
| Approve | Lock plan + write key and protocol / Lock only; export on demand | Lock + write key and protocol ✓ |

## Answer key & protocol

| Question | Options presented | Selected |
|----------|-------------------|----------|
| Relevance categories | Binary + flags / Tiered relevance | Binary + flags ✓ |
| Rows | Per unique document / Per mailbox file | Per unique document ✓ |
| Paths before rendering | Planned paths + status column / Blank paths | Planned paths + status ✓ |
| Protocol depth | Realistic reviewer manual / Code-only short manual | Realistic reviewer manual ✓ |
| Rationale source | From the plan / Code templates only | From the plan ✓ |
| Excel sheets | Key + Statistics + Legend / Key + Statistics only | Key + Statistics + Legend ✓ |
| DocID format | Case prefix + number / Plain sequence | Case prefix + number ✓ |

## Claude's Discretion

Plan storage location (case.json vs sibling file), chunk sizing, distribution parameters, extended validator rules, DocID prefix derivation, file naming, cost-estimate heuristic, holiday table.

## Deferred Ideas

- Per-thread drill-in/edit on Review Plan
- Evening/weekend/crunch-period mail realism
- Resuming a cancelled plan from completed chunks
