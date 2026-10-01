---
phase: 03-case-guide
plan: 15
subsystem: case-guide-ui
tags: [winforms, ui, canonical-values, grid, linkpicker, editing, render, dpi]
requires:
  - "03-12: GuideTabPanel, LinkPicker, IGuideHost, RenderGuideHost, GridStyle"
  - "03-07: CaseGuideEditing (AddValue, SetValueField, SetValueFacts, MarkValueReviewed, DeleteValue), ValueField"
  - "03-06: CaseGuideUiText (Plural, C3RegenerateEdited, C6DeleteValue, EmptyGridText)"
  - "03-08: CaseGuideValidator label-conflict Error / unused-value Warning"
provides:
  - "ValuesTabPanel (Canonical Values tab: ID/Kind/Label/Value/Facts grid, detail editing with a Used in facts LinkPicker, needs-review notice with Mark Reviewed, Add/Delete/Regenerate Value, Regenerate Values...)"
  - "ValuesTabPanel.KindText/KindFromText/MetaLine/NoticeText/CountText"
affects: [03-16]
tech-stack:
  added: []
  patterns:
    - "Same skeleton as FactsTabPanel: deferred grid commits, in-place detail refresh, picker state string, notice sized after layout"
    - "UpdateRow invalidates the whole grid, because a label edit can change the dot of another row (label conflicts are an error on the other value too)"
key-files:
  created:
    - UserControls/ValuesTabPanel.cs
    - ReelDiscovery.Tests/Case/ValuesPanelLogicTests.cs
    - ReelDiscovery.Tests/Render/ValuesPanelRenderTests.cs
  modified: []
decisions:
  - "[03-15]: Canonical values are listed in ID order (V-001...). The registry has no timeline, and a header-click sort is kept across rebuilds by the base"
  - "[03-15]: The Kind column stays at the UI-SPEC 112. 'Change order number' ellipsizes cleanly ('Change order ...'), the header stays one line, and the full kind shows in the detail card"
  - "[03-15]: Used in facts lists facts in timeline order as 'F-003  2024-03-18  {title}' (the picker adds the id). Dateless facts show the title only"
  - "[03-15]: KindFromText is case-insensitive and maps unknown text to Other. The Kind combo commits the enum name, because SetValueField parses enum names"
  - "[03-15]: Mark Reviewed keeps the selection on the value (the Values tab has no Needs review filter)"
metrics:
  duration: 12min
  completed: 2026-10-01
---

# Phase 3 Plan 15: Canonical Values Tab Summary

The Canonical Values tab is now built on `GuideTabPanel`. It lists the registry (ID, Kind, Label, Value, Facts) with a status dot and an "{n} values" count. Label and Value can be edited in the grid. The detail card edits Kind, Label, Exact value (with the hint "Every document and email must use this exact text."), Description and Used in facts. A value whose linked fact was edited gets a Warning dot and an inline notice with a Mark Reviewed button. Every edit goes through `CaseGuideEditing`.

## What was built

- **`ValuesTabPanel : GuideTabPanel`** (`GuidePart.Values`, caption "Canonical values", noun "Value", with toolbar, guidance placeholder "Optional guidance for Regenerate Value, e.g. use a round amount"):
  - **Grid:** dot 28 · ID 64 · Kind 112 (`KindText`) · Label (Fill 35, min 140) · Value (Fill 30, min 120) · Facts 56. Facts is an int count, right-aligned with a 15 px inset like Who knew on the Facts tab. Its tooltip lists `F-003  {title}`, one fact per line. Headers are one line. Label and Value edit in place through `SetValueField`, applied with a deferred commit. If the edit is rejected, the cell is restored and the status line shows "Label cannot be empty." in Danger.
  - **Dot:** Accent while the value is being written and Danger on errors. Otherwise it is Warning on warnings or `NeedsReview`, and Success when there is nothing to flag.
  - **Notice (W3):** a `CardPanel` with a Warning border and padding 12. The text "Review this value: {ReviewReason}" (BodyFont, Text) wraps beside a `Mark Reviewed` button (Default, 34), which calls `CaseGuideEditing.MarkValueReviewed` and then `Host.NotifyEdited(Values)`.
  - **Detail:** the heading is the Label and the meta line is `MetaLine` ("V-004 · Invoice number", plus " · Edited by you" when the user edited it). Fields: Kind (hosted combo with the 10 kinds), Label, Exact value + hint, Description (3 lines), Used in facts (LinkPicker, "Filter facts"). Notes from generation appear (SmallFont, TextMuted) only when there are MappingNotes.
  - **Commits:** text fields commit on Leave and from `CommitPendingEdits`. Kind commits on `SelectionChangeCommitted` (deferred) and the picker on `CheckedChanged` (`SetValueFacts`). After each change the tab calls `Host.NotifyEdited(Values)`, then refreshes the row, the count and the card in place. Errors are shown with `ShowFieldError`.
  - **Actions:** `Add Value` adds the value, selects it and focuses Label with the text selected. `Delete Value` asks C6 (Yes/No, Warning, default No), deletes, then selects the neighbouring row. `Regenerate Value` asks C3 when the value was edited, then calls `Host.RegenerateEntryAsync`; the guidance is cleared on success. `Regenerate Values...` calls `Host.RegenerateTabAsync(GuidePart.Values)`. Every action checks `Host.IsBusy`, and both awaits are followed by an `IsDisposed` check.
- **Tests:** `ValuesPanelLogicTests` has 20 cases (round-trip for all 10 kinds, unknown text maps to Other, MetaLine with and without the edited flag, NoticeText, CountText). `ValuesPanelRenderTests` has 5 renders.

## Render inspection (144 DPI, every PNG opened)

- `values-first-selected-1100x750.png`: V-001 is selected. The Kind column shows "Change order ..." for V-002: clean ellipsis, header on one line. The hint "Every document and email must use this exact text." sits 4 px under Exact value in SmallFont TextMuted (also asserted). Content x matches the Facts and Cast renders: 54 in the grid card, 853 in the detail card. The combo looks the same as Significance on the Facts tab. V-002 shows a Warning dot because of the fixture's existing validator warning ("CO-007 does not appear in the core document or any fact"), so this is not a panel bug.
- `values-first-selected-900x600.png`: Label and Kind ellipsize. The grid has a horizontal scrollbar (the allowed exemption). The detail card scrolls vertically and has no horizontal scrollbar.
- `values-with-findings-1100x750.png`: V-002 now shares V-001's label with a different value. Its dot is Danger (asserted through `DotColor`), and V-001's dot stays Success because the validator reports the conflict on the later entry. The card shows "Notes from generation" and the mapping note in muted small text.
- `values-needs-review-1100x750.png`: the V-001 dot is Warning. The notice text "Review this value: F-003 was changed after it was written." wraps to two lines beside Mark Reviewed. The notice's right edge lines up with the guidance box and Regenerate Value.
- `values-empty-1100x750.png`: "No canonical values. Click Add Value or Regenerate Values..." is centred, with "0 values" in the header. The detail empty text sits on the same line.
- **Clipping detector:** "no findings" for all 5 renders.
- **Known limitation (03-12 LinkPicker, already in deferred-items.md):** long fact items, such as "Harmon confirms the change order procedure", are cut at the list's right edge without an ellipsis.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Whole-grid invalidate after a row edit**
- **Found during:** Task 1
- **Issue:** A label conflict is a validator finding, so editing one value's label can change another row's dot.
- **Fix:** `UpdateRow` calls `Grid.Invalidate()` (Facts uses `InvalidateRow`).
- **Commit:** 6153b2b

**2. [Plan detail] Extra public helpers**
- `NoticeText` and `CountText` were added to the public static helpers and unit-tested, the same way as on the Facts tab.

## TDD Gate Compliance

- RED: `0cc635e test(03-15): add failing Values panel logic tests` (did not compile: `ValuesTabPanel` missing).
- GREEN: `6153b2b feat(03-15): add Canonical Values tab grid, detail editing and actions`: 20/20 logic tests pass.

## Known Stubs

None. The panel is not yet hosted in the Case Guide step shell; 03-16 wires the tabs together.

## Threat Mitigations

- T-03-50: a label conflict shows a Danger dot (render-asserted). Checks and the Next gate come from the validator/shell.
- T-03-51: the C6 and C3 confirmations default to No (`MessageBoxDefaultButton.Button2`).

## Verification

- Build: 0 warnings, 0 errors.
- `FullyQualifiedName~ValuesPanelLogicTests`: 20/20.
- `Category=Render&FullyQualifiedName~ValuesPanelRenderTests`: 5/5, no clipping findings.
- `Category!=Render`: 1066/1066.

## Self-Check: PASSED

- Files exist: UserControls/ValuesTabPanel.cs, ReelDiscovery.Tests/Case/ValuesPanelLogicTests.cs, ReelDiscovery.Tests/Render/ValuesPanelRenderTests.cs.
- Commits 0cc635e, 6153b2b and dafcb39 are in `git log`.
