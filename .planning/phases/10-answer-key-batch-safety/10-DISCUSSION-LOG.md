# Phase 10: Answer-Key & Batch Safety - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-10-12
**Phase:** 10-answer-key-batch-safety
**Mode:** `--auto`. All gray areas were auto-selected, and each question took the recommended option from a code-grounded assumptions analysis (gsd-assumptions-analyzer). The user was not asked any of the questions.
**Areas discussed:** relabel storage, relabel key/scope, relabel lifecycle, QA consumers, re-approval reset, cross-provider batches, banner semantics, WR-A06, WR-A08, legacy types, testing

---

## Relabel capture and storage
| Option | Description | Selected |
|--------|-------------|----------|
| In-app Relabel action | New UI writes a sidecar (reopens D-13) | |
| case.json field | Draft state, saved on the UI thread | |
| Journal records | Deleted by Start Over | |
| Harvest from key files into a sidecar | `answer-key-relabels.json`, harvested before every key write | ✓ |

[auto] Recommended default.

## Relabel key and scope
| Option | Description | Selected |
|--------|-------------|----------|
| (PlanId, DocID); Relevance + Rationale on relabeled rows | DocIDs change only on re-plan | ✓ |
| Thread + Seq | Duplicates the DocID identity | |
| All coding columns | Guide-driven changes would look like user edits | |

[auto] Recommended default.

## Relabel lifecycle
| Option | Description | Selected |
|--------|-------------|----------|
| Keep across Regenerate Thread/Start Over; drop on re-plan with a warning; back up an unparseable key | Matches success criterion 1 | ✓ |
| Drop on Regenerate Thread | Fails criterion 1 | |
| Refuse to write when the key is unparseable | Leaves the key stuck | |

[auto] Recommended default. The analyzer flagged backup vs refuse as the most debatable choice.

## QA consumers
| Option | Description | Selected |
|--------|-------------|----------|
| Key files only | QA keeps plan labels (KEY-03) | ✓ |
| Apply relabels in AnswerKeyBuilder.Build | Re-review would count relabels as resolved | |

[auto] Recommended default. The analyzer marked its confidence "Unclear".

## Re-approval reset (09 Pitfall 9)
| Option | Description | Selected |
|--------|-------------|----------|
| Defer | Breaks criterion 1 on re-approval | |
| Relabels only | Statuses still reset to Planned | |
| Relabels + run-state overlay for the same PlanId | Fixes both at one call site | ✓ |

[auto] Recommended default.

## Cross-provider pending batches (W5)
| Option | Description | Selected |
|--------|-------------|----------|
| Gate the switch on API Configuration | TextProvider resets on launch; not enough | |
| Resolve at run start; block when the key is missing | No double pay | ✓ |
| Resolve when possible, else run anyway | Pays twice | |
| Map items to threads and skip them | Impossible: custom ids hash the provider and model | |

[auto] Recommended default.

## Banner semantics
| Option | Description | Selected |
|--------|-------------|----------|
| Current-provider count; separate clause for other providers; blocking notice when the key is missing | Banner reflects only resolvable batches | ✓ |

[auto] Recommended default.

## WR-A06
| Option | Description | Selected |
|--------|-------------|----------|
| Bill orphans on Cancelled/Fatal | Rejected in 07-REVIEW-FIX | |
| Persistent case-level spend record | Scope creep | |
| Price at Start Over onto the session UsageTracker | Matches existing semantics | ✓ |

[auto] Recommended default.

## WR-A08
| Option | Description | Selected |
|--------|-------------|----------|
| Leave it | Pays twice | |
| Cancel at start and forget | Completed items still paid twice | |
| Live scopes wait on in-flight items | Breaks amended D-07 | |
| Cancel-but-track, poll with a bound, collect partials, itemize orphans | Uses the existing drain bound | ✓ |

[auto] Recommended default.

## Legacy types (W8)
| Option | Description | Selected |
|--------|-------------|----------|
| Delete Attachment/AttachmentComplexity; move AttachmentType; extend LegacyType_IsDeleted | No references outside the file | ✓ |

[auto] Recommended default.

## Testing
| Option | Description | Selected |
|--------|-------------|----------|
| Existing xUnit fakes; CSV and XLSX round-trips; one Scale run | No new infrastructure | ✓ |

[auto] Recommended default.

---

## Claude's Discretion
- Harvest parser structure, Activity log wording, and the timestamp format of the backup file name.

## Deferred Ideas
- Persisting TextProvider; stranding on a same-provider model change; an in-app relabel UI; a persistent spend ledger; relabels feeding QA; W7 traceability docs.
