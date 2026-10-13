# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — Case-Driven Generation

**Shipped:** 2026-10-13
**Phases:** 10 | **Plans:** 170 | **Sessions:** not tracked

### What Was Built
- One OpenAI + Claude gateway with strict structured outputs, DPAPI keys, .NET 10 retarget and a test suite (from zero to 4,697 tests).
- Scenario → core legal document → ID-stable, validated case guide (case.json, .docx export).
- Code-owned corpus plan with the answer key and review protocol derived by construction.
- Knowledge-gated, chronologically waved evidence rendering with a narrative ledger and journaled incremental saving; case-driven attachments and non-relevant traffic including near-miss decoys.
- Batch APIs, prompt caching, cost accounting, blind-review and leakage QA, 5K-scale runs; legacy wizard removed.
- Optional depositions, RFPs, RFAs and interrogatories; edit-safety and spend-safety hardening (Phases 9-10).

### What Worked
- Code owns structure, the LLM writes prose: dates, participants, counts and labels never drift, and the answer key is exact by construction.
- Running the milestone audit before closing: it found eight cross-phase warnings (W1-W8) that no single phase verification caught, and two gap-closure phases fixed them all.
- Pure "UiText" decision functions for copy and gates made most UI behavior unit-testable without driving WinForms.
- Small UAT bugs fixed inline with filtered tests, instead of long agent runs.

### What Was Inefficient
- Human/live UAT was deferred in six of ten phases. Live-provider fidelity (both providers), Anthropic caching/batch and 150% DPI were never confirmed, so v1.0 shipped on code verification alone.
- The render harness's DPI guard was relaxed from 144 to 120 in Phase 3 and never restored; every later "150% check" was deferred instead.
- Bookkeeping drift: SUMMARY `requirements-completed` frontmatter missing in most plans, VALIDATION task columns never refreshed in six phases, checkboxes ticked late or early. The audit had to cross-check 43 requirements by hand.
- Cross-phase issues (stale guide after approval, relabels overwritten, stranded batches) only surfaced at the milestone audit, after eight phases.
- Auto-extracted milestone accomplishments picked up deviation notes instead of one-liners; SUMMARY one-liner fields were not used consistently.

### Patterns Established
- Per-case folder with case.json, corpus-plan.json, an append-only run journal and a batch ledger; atomic writes; never-throw loaders that degrade to warnings.
- One writer funnel per shared artifact (answer key), guarded by a grep-style platform test.
- Paid actions only on explicit user action; validators warn rather than block; confirmation dialogs name what will be lost.
- Deleted-type guards in PlatformTests for anything removed in a cutover.

### Key Lessons
1. Schedule live/human UAT as a blocking gate per phase, or accept it explicitly in the roadmap; "deferred" accumulated into the largest risk at close.
2. Run an integration check mid-milestone (e.g. after the pipeline phases), not only at the end; state that spans steps (approval, staleness, batches) is where gaps hide.
3. Enforce SUMMARY frontmatter (requirements-completed, one-liner) in the executor; it makes audits and milestone summaries automatic.
4. Keep the DPI harness at the target scaling (144 DPI) or the target is never verified.

### Cost Observations
- Model mix: not tracked
- Sessions: not tracked
- Notable: 1,072 commits in 15 days (2026-09-29 → 2026-10-13); the full non-scale test suite takes ~12 minutes, which pushed day-to-day work toward filtered runs.

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Sessions | Phases | Key Change |
|-----------|----------|--------|------------|
| v1.0 | — | 10 | Audit-driven gap-closure phases (9-10) added before close |

### Cumulative Quality

| Milestone | Tests | Coverage | Zero-Dep Additions |
|-----------|-------|----------|-------------------|
| v1.0 | 4,697 (+ Scale 2) | not measured | — |

### Top Lessons (Verified Across Milestones)

1. (Fill in once a second milestone confirms a v1.0 lesson.)
