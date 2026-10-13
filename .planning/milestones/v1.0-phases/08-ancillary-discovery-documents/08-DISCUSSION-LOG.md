# Phase 8: Ancillary Discovery Documents - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-08
**Phase:** 08-ancillary-discovery-documents
**Mode:** --auto (recommended option auto-selected for every question; no user prompts)
**Areas discussed:** Placement in flow, Screen layout, Witness selection, Written discovery shape, Deposition format, Investigation-memo cases, Consistency enforcement, Model/cost, Persistence/output, Answer key, Regenerate/edit

[--auto] Selected all gray areas. No todos matched phase 8. No SPEC.md. No existing CONTEXT.md or checkpoint.

---

## Placement in flow

| Option | Description | Selected |
|--------|-------------|----------|
| New optional step between Generate Corpus and Case Summary, plus a Case Summary row that navigates to it | Room for pickers, estimate and results. Can cite corpus DocIDs. Off the corpus critical path | ✓ |
| New step right after Case Guide (before corpus settings) | Matches the research diagram, but adds a step to the critical path and cannot cite DocIDs | |
| Case Summary action opening a dialog | Smallest change, but cramped for 4 document types, witness picking and per-document results | |

**Choice:** [auto] New optional step before Case Summary (recommended). No new CaseStage value (D-09).

## Screen layout

| Option | Description | Selected |
|--------|-------------|----------|
| Three cards: Depositions / Written discovery / Ready + results grid | Same visual language as the other steps; one Generate button | ✓ |
| Tabbed per document type | More clicks; inconsistent with the Ready-card pattern | |

**Choice:** [auto] Three cards (recommended).

## Witness selection

| Option | Description | Selected |
|--------|-------------|----------|
| Key non-counsel cast ranked by fact involvement; preset default 1/2/3 pre-checked; max 6 | Grounded in fact links; user can override | ✓ |
| User must pick manually with nothing preselected | More friction | |
| All key characters | Cost and length explode | |

**Choice:** [auto] Ranked list with preset defaults (recommended). Examining and defending counsel come from the cast. The reporter and videographer are code-invented.

## Written discovery shape

| Option | Description | Selected |
|--------|-------------|----------|
| One "First Set" per type, plaintiff to primary defendant (changeable); preset counts; every issue/fact covered; ROGs capped at 25 | Realistic FRCP norms, deterministic coverage | ✓ |
| Multiple sets / both directions | Scope growth; deferred | |
| LLM picks counts and keys | Violates "code owns structure" | |

**Choice:** [auto] One set per type with code-keyed slots (recommended). RFP 15/25/40, ROG 12/18/25 (hard cap 25), RFA 10/20/30.

## Deposition format

| Option | Description | Selected |
|--------|-------------|----------|
| LLM returns structured Q/A turns; code lays out 25 numbered lines per page in Courier New with explicit page breaks; full front and back matter | Deterministic page:line, testable, realistic | ✓ |
| Word's built-in line numbering | Line counts are not guaranteed per page; not deterministic | |
| LLM writes pre-formatted transcript text | Drift, unreliable numbering | |

**Choice:** [auto] Code-laid-out transcript (recommended). Excerpt pages 15/25/40 by preset, adjustable 10-60, labelled "EXCERPT". Code assigns dates, case number and consecutive exhibit numbers.

## Investigation-memo cases

| Option | Description | Selected |
|--------|-------------|----------|
| Investigative equivalents (sworn interview transcript, subpoena rider/CID); RFAs hidden for Criminal/Regulatory | Feature still useful for memo cases without faking civil procedure | ✓ |
| Disable the whole step for memo cases | Loses depositions/interviews, which fit well | |
| Use civil documents regardless | Unrealistic for criminal/regulatory matters | |

**Choice:** [auto] Investigative equivalents (recommended).

## Consistency enforcement

| Option | Description | Selected |
|--------|-------------|----------|
| Structured-guide grounding + code checks (canonical values, known people, valid IDs, RFA values verbatim, no future events) + 2 retries, then keep and flag | Mirrors Phase 5 D-12..D-14; reuses CanonicalValueScanner/RenderValidator | ✓ |
| Prompt grounding only | No guarantee of ANC-05 | |
| LLM judge pass | Rejected project-wide (cost; Phases 5 and 7) | |

**Choice:** [auto] Grounding + code checks + retries (recommended). Staleness is tracked against the guide hash and core revision.

## Model / cost

| Option | Description | Selected |
|--------|-------------|----------|
| Story tier, live calls, estimate on the Ready card via GenerationCostEstimator | Small volume; quality matters; one pricing path | ✓ |
| Batch API | Saves little at this volume and adds long waits | |
| Volume tier | Weaker legal prose | |

**Choice:** [auto] Story tier, live (recommended).

## Persistence / output

| Option | Description | Selected |
|--------|-------------|----------|
| Settings in case.json; content in a sibling discovery-documents.json; .docx auto-written to a Discovery\ subfolder | Follows the corpus-plan.json precedent; keeps case.json small; outside the produced corpus | ✓ |
| Everything inside case.json | Slower autosaves as transcripts grow | |
| Export-on-demand only | Extra click; Phase 5 story-so-far precedent is auto plus open | |

**Choice:** [auto] Sidecar JSON + Discovery\ folder (recommended). Start Over must not delete these files.

## Answer key

| Option | Description | Selected |
|--------|-------------|----------|
| Not added to the answer key or review protocol | Not review-corpus documents | ✓ |
| Add rows / RFP-to-issue mapping | Scope growth; deferred | |

**Choice:** [auto] Not in the key (recommended).

## Regenerate / edit

| Option | Description | Selected |
|--------|-------------|----------|
| Per-document Regenerate with optional guidance; edit in Word; stale badge, never silent | Consistent with Phase 3 D-19/D-20 | ✓ |
| In-app editor | Large UI, low value | |

**Choice:** [auto] Regenerate with guidance (recommended).

## Claude's Discretion

- DTO shapes and chunking; transcript wrap width and indentation rules.
- Reporter name list, CSR format, case-number format, deposition date spacing.
- Witness ranking tie-breaks; issue-slot apportioning; boilerplate wording.
- How a usable corpus is detected for the DocID enrichments.
- Investigative document titles; whether to extend WordBuilder or add a sibling builder.

## Deferred Ideas

- Responses and objections, verifications.
- Privilege log.
- RFP-to-issue mapping in the answer key and protocol.
- Multiple sets and party pairs; 30(b)(6) depositions.
- Video and synced transcript formats.
- In-app editor.
