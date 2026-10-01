---
phase: 03-case-guide
plan: 14
subsystem: case-guide-ui
tags: [winforms, ui, facts, grid, linkpicker, editing, render, dpi]
requires:
  - "03-12: GuideTabPanel, LinkPicker, IGuideHost, RenderGuideHost, GridStyle"
  - "03-07: CaseGuideEditing (AddFact, SetFactField, SetFactLinks, MarkReviewed, ValuesReferencing, DeleteFact)"
  - "03-06: CaseGuideUiText (Plural, C3RegenerateEdited, C5DeleteFact, NoFilterMatchText, EmptyGridText)"
  - "03-05: CaseGuideDocxExporter.LongDate"
provides:
  - "FactsTabPanel (Key Facts tab: chronological grid + detail editing with three LinkPickers, All / Needs review filter, Mark Reviewed, Add/Delete/Regenerate Fact, Regenerate Facts...)"
  - "FactFilter enum; FactsTabPanel.CountText/MetaLine/NoticeText/IssuesCell/NamesCell/PassesFilter"
  - "FactsTabPanel.ShowNeedsReview() for the needs-review banner's Review Facts button"
  - "GuideTabPanel.AddPairedTextAndCombo (text field + drop-down list pair)"
affects: [03-15, 03-16]
tech-stack:
  added: []
  patterns:
    - "Link pickers are re-set only when their items, checks or locks changed (a per-picker state string), so the list the user is clicking keeps its order and scroll"
    - "After an Actors change the Who knew picker is re-set with the actors locked; CaseGuideEditing unions actors into KnownByIds"
    - "The inline notice is anchored (not Fill) in the AutoSize notice slot and its height is set after layout (BeginInvoke) so a wrapped notice is never clipped"
key-files:
  created:
    - UserControls/FactsTabPanel.cs
    - ReelDiscovery.Tests/Case/FactsPanelLogicTests.cs
    - ReelDiscovery.Tests/Render/FactsPanelRenderTests.cs
  modified:
    - UserControls/GuideTabPanel.cs
decisions:
  - "[03-14]: Key Facts grid Who knew is 100 wide (UI-SPEC 64) and Who did it has a 112 minimum (UI-SPEC 96); the headers are single-line on this grid. At 64/96 the headers wrapped to two lines, so the grid did not top-align with the other tabs"
  - "[03-14]: Rows are listed chronologically (yyyy-MM-dd ordinal, facts without a date last, then id); a header-click sort is kept across rebuilds by the base"
  - "[03-14]: MetaLine adds ' · Edited by you' for user-edited facts, the same as the Cast meta line (UI-SPEC example shows the plain form)"
  - "[03-14]: Mark Reviewed under the Needs review filter moves the selection to the next fact to review; under All it stays on the fact"
  - "[03-14]: Add Fact resets the filter to All before selecting the new fact and focuses the Fact field (select-all), not Date"
metrics:
  duration: 25min
  completed: 2026-10-01
---

# Phase 3 Plan 14: Key Facts Tab Summary

The Key Facts tab is built on `GuideTabPanel`. It shows the facts timeline as a chronological, sortable grid with a status dot, an All / Needs review filter, and a detail card where the user edits the date, significance, title, description and the three link lists (Issues, Who did it, Who knew). Facts flagged by a cast edit show a Warning dot and an inline notice with Mark Reviewed. Every change goes through `CaseGuideEditing`. The tab actions are Add Fact, Delete Fact (C5), Regenerate Fact (C3 when edited) and Regenerate Facts....

## What was built

- **`FactsTabPanel : GuideTabPanel`** (`GuidePart.Facts`, caption "Key facts", noun "Fact", with toolbar):
  - **Header:** Toggles "All" (selected) and "Needs review", 28 high and 4 apart, then 12 px and `CountText` ("6 facts · 1 needs review").
  - **Grid:** dot 28 · Date 96 · Fact (Fill 45, min 160) · Issues 80 (`ISS-01, ISS-03`, tooltip lists `ISS-01  {title}` one per line) · Who did it (Fill 25, min 112, names) · Who knew 100 (int count, right-aligned, tooltip lists names). No ID column. Date and Fact edit in the grid through `SetFactField`; a rejected value restores the cell and puts `CaseGuideEditing.DateFormatError` or "Fact cannot be empty." on the status line in Danger. A date change re-sorts with a deferred rebuild.
  - **Dot:** Accent while written, Danger on errors, Warning on warnings or `NeedsReview`, otherwise Success.
  - **Needs review filter:** shows only flagged facts; with none it reads "No facts need review.". `ShowNeedsReview()` turns the filter on and selects the first such fact. `SelectEntity` clears a filter that hides the fact.
  - **Detail card:** heading = title, meta `F-004 · April 22, 2024 · Supporting` (· Edited by you). Notice card (Warning border, padding 12): `Review this fact: {ReviewReason}` wrapping beside `Mark Reviewed` (Default, 34). Fields: Date + Significance (pair, `YYYY-MM-DD` placeholder; Hot/Key/Supporting), Fact, What happened (5 lines), Issues / Who did it / Who knew LinkPickers, Notes from generation (SmallFont, TextMuted, only with mapping notes).
  - **Commits:** text on Leave and in `CommitPendingEdits`, Significance on `SelectionChangeCommitted` (deferred), pickers on `CheckedChanged` via `SetFactLinks(Issues|Actors|KnownBy)`. Each change calls `Host.NotifyEdited(Facts)`, refreshes the row, count line and card in place. Invalid detail input gets a Danger border and message and is not stored.
  - **Actions:** Add Fact, Delete Fact (C5 Yes/No, Warning, Button2, then selects the neighbour row), Regenerate Fact (C3 when edited; clears guidance on success; `IsDisposed` check after the await; card rebuilt), Regenerate Facts... (`Host.RegenerateTabAsync(Facts)`). Every action checks `Host.IsBusy`.
- **`GuideTabPanel.AddPairedTextAndCombo`:** text field + hosted drop-down side by side, reusing the private `AddPair`.
- **Tests:** `FactsPanelLogicTests` (14 cases); `FactsPanelRenderTests` (7 renders).

## Render inspection (144 DPI, every PNG opened)

- `facts-needs-review-top-1100x750.png`: the F-004 Warning dot is vertically centred in its row; the other dots are Success. Dates run 2024-01-15 to 2024-06-20 in order. Header is one line ("Who knew" no longer wraps). The notice card spans the detail width with the text in two lines and Mark Reviewed (34 high) on its right, right edge aligned with the guidance box and Regenerate Fact. Date/Significance pair shares one row; the Significance text sits on the text fields' inner edge.
- `facts-needs-review-top-900x600.png`: the notice text wraps to three lines and the card grows to fit (first attempt clipped it at 100 of 111 px, fixed). The grid shows a horizontal scrollbar (the only allowed exemption); the detail panel has none.
- `facts-pickers-issues/actors/knowers-1100x750.png`: each picker scrolled into view; lists are at most 6 rows (asserted). Who knew lists `CH-002  Tom Becker (did it) — ...` first, checked, then CH-004 (checked), then the rest.
- `facts-filter-needs-review-1100x750.png`: Needs review toggle selected; only F-004 listed and selected.
- `facts-empty-1100x750.png`: "No key facts. Click Add Fact or Regenerate Facts..." centred; count "0 facts".
- **Clipping detector:** "no findings" for all 7 renders.
- **Known limitation (03-12 LinkPicker):** long cast item text (`... — Project Manager, Brightline Glazing, LLC`) is cut at the list's right edge without an ellipsis (CheckedListBox cannot ellipsize). The id, name and "(did it)" always show; recorded in deferred-items.md.
- **Harness artifact (known from 03-13):** `DrawToBitmap` paints the multiline TextBox and checklist scrollbars outside the scrolled fields panel in some renders.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Visual] "Who knew" / "Who did it" headers wrapped to two lines**
- **Found during:** Task 2 render inspection
- **Issue:** At the UI-SPEC widths (64 and min 96) the right-aligned header and the sort-glyph space wrapped "Who knew" (Large) and "Who did it" (Small), making this grid's header taller than the other tabs'.
- **Fix:** Who knew 100, Who did it minimum 112, `ColumnHeadersDefaultCellStyle.WrapMode = False`.
- **Commit:** 9e2aaa1

**2. [Rule 1 - Visual] Needs-review notice clipped at 900x600**
- **Found during:** Task 2 render inspection
- **Issue:** The notice wrapped to three lines but the AutoSize slot kept the two-line height (card 111 px in a 100 px slot) because the height was set inside a layout pass.
- **Fix:** Notice anchored instead of Fill; its height is set after layout via `BeginInvoke`, then the slot's parent is laid out again.
- **Commit:** 9e2aaa1

**3. [Rule 3 - Blocking] No text + drop-down pair helper for Date + Significance**
- **Fix:** Added `GuideTabPanel.AddPairedTextAndCombo`.
- **Commit:** 51e9bf6

**4. [Plan detail] Render tests**
- The knowers ordering/lock check and the 6-row check run inside the three picker renders, so there are exactly 7 facts PNGs.
- `PassesFilter` was added to the public helpers and unit-tested.

## TDD Gate Compliance

- RED: `0f84c93 test(03-14): add failing Facts panel logic tests` (did not compile: `FactsTabPanel`, `FactFilter` missing).
- GREEN: `51e9bf6 feat(03-14): ...` — 14/14 logic tests pass.

## Known Stubs

None.

## Deferred Issues

See `deferred-items.md` (LinkPicker long-item truncation).

**Render re-run pending (environment):** the final panel code (commit 9e2aaa1) passed all 7 facts renders at 144 DPI with no clipping findings, and the PNGs above come from that run. Afterwards one assertion was added to `Facts_NeedsReview_Top_Large` (the "Notes from generation" caption exists). Then the display switched from 150% to 125% partway through the full `Category=Render` run. The 3 failures in that run, and every render run after it (polled for 9 minutes), came from the harness's 144-DPI guard ("this display is 120 DPI"), not from layout findings. Re-run `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` on the 150% monitor to confirm. Non-render suite: 1046/1046 green; build 0 warnings.

## Requirements

- GUIDE-05 (facts timeline shown with ids, issues, actors and knowers) and GUIDE-08 (cast and facts review, edit, regenerate) are marked Complete.
- GUIDE-09 stays Pending: the step shell's stale / needs-review banner arrives in 03-16; the Facts tab already exposes `ShowNeedsReview()` and Mark Reviewed for it.

## Self-Check: PASSED

- Files exist: UserControls/FactsTabPanel.cs, ReelDiscovery.Tests/Case/FactsPanelLogicTests.cs, ReelDiscovery.Tests/Render/FactsPanelRenderTests.cs.
- Commits 0f84c93, 51e9bf6 and 9e2aaa1 are in `git log`.
- Build: 0 warnings, 0 errors. Non-render tests 1046/1046. Facts renders 7/7 at 144 DPI before the display change (see Deferred Issues).
