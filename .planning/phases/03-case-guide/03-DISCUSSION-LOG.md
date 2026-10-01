# Phase 3: Case Guide - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-01
**Phase:** 03-case-guide
**Areas discussed:** Guide screen layout, Saving & reopening cases, Cast shape & email identities, Editing, regenerating & staleness

---

## Guide screen layout

| Question | Options | Selected |
|----------|---------|----------|
| Wizard layout | One step, tabbed / Separate steps / One step, side navigation | One step, tabbed |
| Generation trigger | Automatically, in order / One button per part | Automatically, in order |
| Facts view | Table + detail panel / Visual timeline / Table + timeline strip | Table + detail panel |
| Issues source | From the core document (read-only) / Editable list | From the core document |

## Saving & reopening cases

| Question | Options | Selected |
|----------|---------|----------|
| Case folder | Per-case folder, auto-named / Pick folder up front / Ask on first save | Per-case folder, auto-named |
| Saving | Autosave / Explicit Save | Autosave |
| Reopen | Open Case... + recent list on first screen / File picker only | Open Case... + recent list |
| End of flow | Finish shows a case summary / Finish on the guide step | Case summary step |

## Cast shape & email identities

| Question | Options | Selected |
|----------|---------|----------|
| Cast size | Preset-driven / Set numbers on the guide step | Preset-driven |
| Email domains | Realistic .com-style / Reserved test domains | Realistic .com-style |
| Counsel | In-house + outside per party / Minimal | In-house + outside per party |
| Voice | Short style notes + signature / Required fields only | Short style notes |

## Editing, regenerating & staleness

| Question | Options | Selected |
|----------|---------|----------|
| Editing | Table + detail panel / Detail panel only | Table + detail panel |
| Regenerate | Whole tab or one entry / Whole tab only | Whole tab or one entry |
| Staleness | Warn + offer update / Warn only | Warn + offer update |
| Add/delete | Yes, validator catches breaks / Edit only | Yes |

**Wrap-up:** user chose "I'm ready for context".

## Claude's Discretion

case.json shape and versioning, stable ID format, canonical values registry contents/UI, validator rules beyond the minimum, generation batching, Word export layout, recent-cases storage.

## Deferred Ideas

None.
