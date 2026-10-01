---
phase: 03-case-guide
plan: 12
subsystem: case-guide-ui
tags: [winforms, ui, tab-skeleton, grid, linkpicker, render, dpi]
requires:
  - "03-02: GridStyle, TextFieldHost, Theme.DrawStatusDot, RenderHarness, ClippingDetector"
  - "03-06: CaseGuideUiText (EmptyDetailText, EmptyGridText, IssuesSourceText, SelectedCountText, LinkedFactsText, MoreLinkedText, Plural)"
  - "03-08: GuideFinding, CaseGuideValidator.Validate/WorstFor"
  - "03-03: IssueExtractor.SourceLabel"
provides:
  - "IGuideHost (contract between StepCaseGuide and tab panels)"
  - "GuideTabPanel abstract base (grid card + detail card, field helpers, status dots, rebuild keeping sort/selection, SetBusy)"
  - "IssuesTabPanel (read-only Issues tab, Retry Issue Details tab action)"
  - "LinkPicker + LinkItem (filter + checklist, max 6 rows, locked items)"
  - "RenderGuideHost test double + GuidePanelRenderTests"
  - "GridStyle.HeaderTextMargin (header padding compensation)"
affects: [03-13, 03-14, 03-15, 03-16]
tech-stack:
  added: []
  patterns:
    - "Tab panels derive from GuideTabPanel: BuildColumns (called from the base ctor), BuildRows (cells may omit the dot cell), ShowDetail(id) using AddCaptionedLabel/AddCaptionedLines/AddTextField/AddPairedTextFields/AddCombo/AddField/AddNote"
    - "RefreshHeader() override updates CountLabel and TabActions before each rebuild; RequestRefresh() defers a rebuild with BeginInvoke from cell events"
    - "Fields TLP width locked to the scroll panel's ClientSize via MinimumSize/MaximumSize width (no horizontal scrollbar)"
    - "LinkPicker height = filter host + 4 + min(items, 6) x ItemHeight + 2, recomputed after SetItems/filter and in OnHandleCreated"
key-files:
  created:
    - UserControls/IGuideHost.cs
    - UserControls/GuideTabPanel.cs
    - UserControls/IssuesTabPanel.cs
    - UserControls/LinkPicker.cs
    - ReelDiscovery.Tests/Render/RenderGuideHost.cs
    - ReelDiscovery.Tests/Render/GuidePanelRenderTests.cs
  modified:
    - Helpers/GridStyle.cs
    - ReelDiscovery.Tests/Case/SharedUiHelperTests.cs
decisions:
  - "[03-12]: GridStyle header left padding is LDU(15) - 2: DataGridViewColumnHeaderCell adds a fixed, unscaled 2 px text margin; right-aligned header cells subtract it from their right padding too"
  - "[03-12]: GuideTabPanel.BuildColumns runs from the base constructor, so it may only use Grid and LogicalToDeviceUnits; tab actions are created in the subclass ctor body"
  - "[03-12]: The detail column switches to Percent 100 | Absolute LDU(320)+LDU(8) when 40% would be narrower than 328 logical px"
  - "[03-12]: Empty-detail text gets top padding (1 + header height - 6) so it centers on the same line as the grid's empty text"
  - "[03-12]: LinkPicker inserts the locked suffix after the name (before ' — ') so a narrow list never cuts it off; order is locked, then other checked, then original order"
  - "[03-12]: LinkPicker keeps checked ids that are not in its item list (dangling refs) in CheckedIds, so a commit never drops a reference the user did not touch"
  - "[03-12]: Issues 'Summary' shows the count summary, falling back to the enrichment Description when the count has none; list fields (elements, linked facts) are one label per line, 4 apart"
metrics:
  duration: 8min
  completed: 2026-10-01
---

# Phase 3 Plan 12: Case Guide Tab Skeleton, Issues Tab and LinkPicker Summary

The shared Case Guide tab layout is built and checked at 144 DPI. `GuideTabPanel` gives every tab the same grid card and detail card (60/40, detail card at least 320 wide). The read-only Issues tab is the first tab built on it. `LinkPicker` is the filter-and-checklist control that the Key Facts and Canonical Values tabs will use. A render host lets each tab panel render without the step shell.

## What was built

- **`IGuideHost`**: the contract from the plan, unchanged.
- **`GuideTabPanel`** (abstract):
  - **Grid card** (`Padding = 1`): a header with a 15 px inset and a minimum height of 28 plus padding, the caption on the left, and `HeaderRight` (ending with `CountLabel`) on the right. Below it are a 1 px divider and the grid. The grid uses `GridStyle.Apply`, `EditOnKeystrokeOrF2` and a suppressed `DataError`. A centered label replaces the grid when there are no rows.
  - **Detail card** (`Padding = 16`): heading and meta, a `NoticeSlot` (a 1-column TLP), the fields in an AutoScroll panel, then the divider, guidance field and Delete / Regenerate toolbar. The last three rows exist only when `hasToolbar` is set.
  - **Field helpers**: `AddCaptionedLabel`, `AddCaptionedLines`, `AddTextField` (with hint), `AddPairedTextFields`, `AddCombo`, `AddField` (for any control, e.g. LinkPicker), `AddNote`, `ShowFieldError` / `ClearFieldErrors` (Danger border plus a hidden Danger label under each field) and `ClearFields`.
  - **Behaviour**: `RefreshFromDraft` keeps the sort column, direction, selected id and first displayed row. Selecting a row commits pending edits, then shows the detail. The Delete key triggers `DeleteButton`. `SetBusy` makes the grid and fields read-only and disables the toolbar and tab actions. Status dots are painted in `CellPainting` using `Theme.DrawStatusDot` and `CaseGuideValidator.WorstFor`; the entry being written is Accent.
- **`IssuesTabPanel`**: columns ID 64, Source 88, Issue (Fill, min 160) and Facts 56 (int values, right-aligned). The detail is read-only: Source, Against, Legal basis, Summary, What must be proven, What relevant evidence looks like, and Linked facts (up to 8, then "and {n} more"), followed by the footer note. The count line depends on the document type (pleadings or memo). `Retry Issue Details` appears in `TabActions` only when `IssueDetailsState` is Failed.
- **`LinkPicker`**: a filter field with a "{n} selected" count, and a `CheckedListBox` with a 1 px border, at most 6 rows tall. Locked items list first, cannot be unchecked, and show their suffix. Hidden checked items stay checked. `ItemCheck` reads `e.NewValue` and applies it through `BeginInvoke`. `ReadOnly` resets `e.NewValue`.
- **Tests**: `RenderGuideHost` is the test double. `GuidePanelRenderTests` has 5 tests:
  - Issues with the first row selected at 1100x750 and 900x600
  - Issues with no issues
  - a rebuild that keeps a descending sort and the ISS-02 selection
  - three LinkPickers in a 360-wide card (3 issues, 9 cast with 2 locked, 6 facts)

  The guided fixture has only one count, so the tests add two more counts and issues locally.

## Render inspection (144 DPI, PNGs opened and measured with PIL)

- `issues-first-selected-1100x750.png`:
  - **Grid card edge x = 36.** The "Issues" caption, the "ID" header text and the "ISS-01" cell text all start at x 65–66. Before the GridStyle fix the header text sat at 68, 2 px right of the cells.
  - **Detail card edge x = 994.** Its captions and values start at x 1024. That is the same 30 px offset from the card edge as in the grid card (24 px Content x plus GDI label padding).
  - **Right edges.** The right-aligned Facts header, its cells and the header count line all end at x 938.
  - **Header height.** The grid card header is 78 px (52 logical) on every tab, then the 1 px divider.
  - **Surfaces.** The detail panel has no horizontal scrollbar, and there are no stock grey surfaces.
- `issues-first-selected-900x600.png`:
  - The detail card stays at about 333 logical px wide.
  - The vertical scrollbar appears inside the 16 px padding and there is no horizontal scrollbar.
  - Elements and linked facts are one label per line, so a wrapped line no longer reads as a new entry. This was changed after the first render.
- `issues-empty-1100x750.png`:
  - "No issues..." and "Select an issue..." are centered on the same line (y 617 and 616).
  - They were 36 px apart before the alignment fix.
- `linkpicker-1100x750.png`:
  - The Issues list is 3 rows, Cast is capped at 6 rows with a scrollbar, and Facts is 6 rows with no scrollbar.
  - "(did it)" is visible after Tom Becker and Lisa Chen, and those two are listed first. Before the fix the suffix was cut off at the end of the line.
  - "{n} selected" is vertically centered with the filter field.
- Clipping detector: "no findings" for all 5 renders.
- Known render limitation: `DrawToBitmap` does not paint TextBox `PlaceholderText`, so the filter placeholders do not show in the PNGs. They do show at runtime.
- The native vertical scrollbar track (detail panel, cast list) is the OS scrollbar, the same as on the reference screen.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Grid header text was 2 px right of cell text**
- **Found during:** Task 2 render inspection
- **Issue:** `DataGridViewColumnHeaderCell` adds an unscaled 2 px text margin, so header text sat at x 68 and cell text at x 66. That breaks UI-SPEC rule 10, which allows a 1 px tolerance.
- **Fix:** Added `GridStyle.HeaderTextMargin = 2` and set the header left padding to `LDU(15) - 2`. The right-aligned Facts header subtracts it on the right. Updated the `SharedUiHelperTests` assertion.
- **Files modified:** Helpers/GridStyle.cs, ReelDiscovery.Tests/Case/SharedUiHelperTests.cs, UserControls/IssuesTabPanel.cs
- **Commit:** 9ea98b6

**2. [Rule 1 - Bug] LinkPicker cut off the "(did it)" suffix in narrow lists**
- **Found during:** Task 2 render inspection
- **Fix:** The suffix is now inserted after the name (before " — "), and locked items list first.
- **Commit:** f583d7c

**3. [Rule 1 - Visual] Empty grid text and empty detail text were on different lines**
- **Fix:** The empty-detail label gets top padding computed from the grid card header height.
- **Commit:** f583d7c

**4. [Plan detail] LinkPicker sizing**
- The plan describes LinkPicker as "AutoSize". It sets its own `Height` from the measured parts instead, because a fixed-height control in an AutoSize TLP row is the reliable WinForms pattern (TextFieldHost works the same way). The result is identical: it sizes to its content.

## Known Stubs

None. `RegenerateButton`, `DeleteButton` and `GuidanceBox` are built but have no handlers in the base; the Cast, Facts and Values tabs (03-13 to 03-15) wire them. The Issues tab has no toolbar.

## Notes for 03-13..03-15

- `ShowDetail` rebuilds the fields every time `RefreshFromDraft` runs. Subclasses should commit the fields before calling `Host.NotifyEdited`. If a rebuild must not steal focus, update values in place when the id is unchanged.
- Call `base.ApplyEditableState()`, then set the editable columns' `ReadOnly = false`.
- Use `AddHeaderControl` for the compact filter toggles. Override `SelectEntity` to clear a filter that hides the entity, and override `EmptyRowsText` to supply the no-match text.

## Self-Check: PASSED

- All 6 created files and 2 modified files exist.
- Commits 11e56e6, 9ea98b6 and f583d7c are in `git log`.
- Build: 0 warnings, 0 errors.
- Tests: 946 non-render tests pass, and all 14 render tests pass, including the 5 new ones.
