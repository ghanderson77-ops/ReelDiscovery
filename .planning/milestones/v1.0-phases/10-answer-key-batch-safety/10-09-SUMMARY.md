---
phase: 10-answer-key-batch-safety
plan: 09
subsystem: ui/copy
tags: [answer-key, relabel, copy, D-05, D-06, D-07, D-08]
requires:
  - "10-08 (latest edits to GenerationUiText / QaUiText)"
provides:
  - "QaUiText.CQ1RegenerateThread(..., bool relabelsKept = false)"
  - "GenerationUiText.CG2StartOver(..., bool relabelsKept = false)"
  - "CorpusUiText.RelabelsDroppedText(int), ReplanBody(..., int relabels = 0), SettingsNotice(..., int relabels = 0)"
  - "Blind-review findings tooltip and XLSX Legend Relevance definition name the Relevance column"
affects:
  - "Plan 10-10 wires relabel counts from callers into these optional parameters"
tech-stack:
  added: []
  patterns:
    - "New copy arrives through optional trailing parameters whose defaults reproduce today's text (Phase 9 precedent)"
key-files:
  created: []
  modified:
    - UserControls/QaUiText.cs
    - UserControls/GenerationUiText.cs
    - UserControls/CorpusUiText.cs
    - Services/Corpus/AnswerKeyXlsxWriter.cs
    - ReelDiscovery.Tests/Qa/QaUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Corpus/CorpusUiTextTests.cs
    - ReelDiscovery.Tests/Corpus/AnswerKeyXlsxWriterTests.cs
decisions:
  - "[Phase 10] The D-08 dropped-relabels clause goes only on the unapproved 'replaces the current plan' settings notice and the re-plan body. It comes last, after the generated-threads sentence."
metrics:
  duration: "~10 min"
  completed: 2026-10-12
  tasks: 2
  files: 8
---

# Phase 10 Plan 09: Relabel warnings and Relevance-column guidance Summary

This plan adds copy only. Regenerate Thread and Start Over now say that relabels are kept (D-06/D-07). The re-plan body and the settings notice say how many relabels a re-plan drops (D-08). The blind-review tooltip and the XLSX Legend tell the user to edit the Relevance column (D-05). All four helpers default to the current text.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | D-06/D-07/D-08 clauses as optional trailing parameters | 529a7f4 |
| 2 | "Edit the Relevance column" tooltip and Legend guidance | 06c7b3b |

## What was built

- `CQ1RegenerateThread(..., relabelsKept)` ends with " Relabels you made for its emails in the answer key are kept." when the flag is set. The clause comes after the discovery clause.
- `CG2StartOver(..., relabelsKept)` adds " Relabels you made in the answer key are kept." when the flag is set. Clause order is batches, then discovery, then relabels.
- `CorpusUiText.RelabelsDroppedText(n)` returns "1 manual relabel / N manual relabels in the answer key will be dropped." using `CaseGuideUiText.Plural` (N0 format). It is used by:
  - `ReplanBody`, where it comes last;
  - the unapproved `SettingsNotice` branch only.
- The blind-review tooltip and the Legend Relevance row now use the exact D-05 wording.

## Verification

- `dotnet build ... -warnaserror` passed with 0 errors before each commit.
- Task 1 filter (QaUiText, GenerationUiText, CorpusUiText tests): 410 passed.
- Task 2 filter (QaUiText, AnswerKeyXlsxWriter, QaFindingsDialog, QaDialogRender tests): 100 passed.
- Plan verification (`~UiTextTests|~AnswerKeyXlsxWriterTests`): 568 passed.
- All grep acceptance counts match the plan. `RelabelsDroppedText(` appears 3 times, `int relabels = 0` twice, `bool relabelsKept = false` twice, and the old tooltip text 0 times.
- Line endings are unchanged: each file kept its LF or CRLF working-tree style.

## DPI note (on-screen text for a 150% check)

QaDialogRenderTests passed on this 120 DPI machine. The longer blind-review tooltip appears in a tooltip, and tooltips wrap or size themselves. The new dialog clauses (CQ1, CG2, re-plan body, settings notice) are not on screen until Plan 10 wires them. When they are wired, check these at 150%:
- the Corpus Settings notice label with the relabels clause;
- the Re-plan dialog body;
- the Start Over and Regenerate Thread message boxes;
- the blind-review summary tooltip.

## TDD Gate Compliance

For each task the tests were written before the implementation. A separate RED `test(...)` commit was not made, because the new tests do not compile without the new parameters and every commit had to build with -warnaserror. Each task is one `feat` commit containing both tests and code.

## Deviations from Plan

None. The plan was executed as written.

## Known Stubs

None. Callers still pass the defaults until Plan 10-10 wires the relabel counts, as planned.

## Self-Check: PASSED
