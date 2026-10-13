---
phase: 02-case-scenario-core-document
plan: 08
subsystem: case-scenario-ui
tags: [winforms, wizard-step, core-document, review, ui-spec]
requires:
  - "02-03 CoreDocumentAssembler (RenderPlainText, CountNumberedParagraphs)"
  - "02-04 CoreDocumentValidator, CoreDocumentEditing"
  - "02-06 CoreDocumentGenerator (GenerateAsync / GenerateSectionsAsync / RegenerateSectionAsync)"
provides:
  - "UserControls/StepCoreDocument (Review Core Document step; SectionSuffix/SectionSuffixColor/DocumentTitle/ReadyStatus/IssuesCaption static helpers)"
affects: [02-09]
tech-stack:
  added: []
  patterns:
    - "Owner-drawn ListBox (heading + colored status suffix, Accent selection)"
    - "Inline generation with IProgress<string> refreshing the list live (no LoadingOverlay)"
    - "Section list rebinds only when section object identity changes; selection restored by Id under a suppress flag"
    - "Pending edits applied on Apply / section switch / editor Leave / step leave / Finish / before any run"
key-files:
  created:
    - UserControls/StepCoreDocument.cs
    - ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs
  modified: []
decisions:
  - "[02-08]: The empty state is never shown while a run is in progress, so the first run shows the outline's sections filling in rather than the Generate Document panel"
  - "[02-08]: ApplyPendingEdit always normalizes the editor text to the section's joined paragraphs, so whitespace-only changes do not leave Apply Edits enabled"
  - "[02-08]: SplitContainer Panel1MinSize and SplitterDistance are both set on the first Layout/SizeChanged with enough width (setting them earlier can throw)"
  - "[02-08]: Retry Section (Pending/Failed) runs GenerateSectionsAsync for all Pending/Failed sections; Regenerate Section (Ready) calls RegenerateSectionAsync with the guidance text"
metrics:
  duration: 14min
  completed: 2026-10-01
---

# Phase 2 Plan 08: Review Core Document step Summary

`StepCoreDocument` ("Review Core Document", UI-SPEC S3) generates the core document inline on first entry: the status line shows the generator's progress messages, the progress bar counts Ready sections, and the owner-drawn section list fills in with ` — pending` / ` — writing...` / ` — failed` / ` — {n} issues` / ` — edited` suffixes in their colors. `Cancel Generation` (C5, default No) cancels and keeps completed sections. Refused outlines show the refused copy in the status line (and in the empty-state body when there is no document), fatal Auth/SpendLimit stops show the fatal copy, and per-section failures show the section-failure copy. The Section tab edits a section's paragraphs without numbers. Apply Edits, a section switch, leaving the editor, leaving the step and Finish all apply edits through `CoreDocumentEditing.ApplyEdit` and re-run `CoreDocumentValidator.Validate`, which updates the issues grid, caption, list suffixes and the Full Document preview (`CoreDocumentAssembler.RenderPlainText`). Regenerate Section accepts optional guidance and asks C2 first if the section was edited. Failed or pending sections show Retry Section, which resumes all Pending/Failed sections. Regenerate All asks C3. If the source hash changed, entering the step asks C4 once per hash and never regenerates silently. Double-clicking an issue jumps to its section. Finish is blocked by citation errors ("Case Citations Found") or other errors, and warnings need an explicit Yes ("Review Warnings", default No). The step is not in the wizard list yet; 02-09 wires it in.

## Tasks

| Task | Name | Commits | Files |
| ---- | ---- | ------- | ----- |
| 1 | Layout, states, inline generation, display helpers | ef94416 (RED), 80d24fd (GREEN) | UserControls/StepCoreDocument.cs, ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs |
| 2 | Section editing, Regenerate/Retry Section, issues navigation, Finish gate | 298f65a | UserControls/StepCoreDocument.cs |

## Verification

- `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors
- `CoreDocumentUiLogicTests`: 13 passed; full suite: 443 passed
- All grep acceptance criteria for both tasks met (`"Generation cancelled..."` is a single const shared by both run paths)
- One-off STA smoke test (not committed) hosted the step in a 1100x750 form with the sample complaint plus an injected case citation. It checked the title, the owner-drawn list (` — 2 issues` in red), the issues grid (1 error, 2 warnings) and the Full Document preview. It then edited the citation out, switched sections and confirmed the edit was applied (`IsUserEdited`, ` — edited` suffix, caption `Issues (0 errors, 1 warnings)`). Screenshots were reviewed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Empty state would cover a first run in progress**
- **Found during:** Task 1
- **Issue:** The plan's rule `emptyPanel.Visible = doc == null || all sections Pending` would show the Generate Document panel while the first run was writing the outline and while all sections were still pending. That hides the live list that UI-SPEC "Generating (first run)" requires.
- **Fix:** The empty state is shown only when no run is in progress.
- **Commit:** 80d24fd

**2. [Rule 2 - Correctness] Edits applied before any generation run**
- `RunGenerationAsync` calls `ApplyPendingEdit()` first, so Retry Section / Regenerate All never discard unapplied editor text without going through the model (Regenerate All still replaces it after C3).
- **Commit:** 298f65a

**3. [Rule 1 - Bug] Issues grid auto-selected its first row**
- The grid's default selection hid the first row's severity color. `ClearSelection()` now runs after each refresh.
- **Commit:** 80d24fd

## TDD Gate Compliance

Task 1: the RED `test(02-08)` commit ef94416 (compile failure) comes before the GREEN `feat(02-08)` commit 80d24fd. Task 2 is `type="auto"` and has no TDD requirement.

## Known Stubs

None. The step is complete but not registered in `WizardForm`; plan 02-09 does that and wires the Finish flow (C6).

## Self-Check: PASSED

- FOUND: UserControls/StepCoreDocument.cs, ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs
- FOUND commits: ef94416, 80d24fd, 298f65a
