---
phase: 03-case-guide
plan: 06
subsystem: ui-copy-and-dialogs
tags: [copywriting, plurals, dialogs, dpi, render, tests]
requires:
  - "03-01: GuidePart, GuidePartStatus, CaseStage"
  - "03-02: RenderHarness, ClippingDetector, TextFieldHost"
provides:
  - "UserControls/CaseGuideUiText.cs (Plural, PartNoun, IsPluralPart, JoinParts, TabCaption, StateCardText, ChecksCaption, NoChecksText, StaleBannerText, NextGateDecision, DisplayPath, SubtitleText, StageLabel, RecentItemSecondLine, SaveLine, ExportLine, Summary*Line, count lines, Progress*, status lines, C1-C12, D1/D2 copy)"
  - "GuideTabState, BannerVariant, NextGateKind, StateCardContent, BannerContent, NextGateResult"
  - "Forms/ThemedDialog.cs (AddHeadline, AddBody, CreateWrappingLabel, AddContent, AddButtons, FinishLayout, LDU, ContentWidth)"
  - "Forms/RegenerateTabDialog.cs (D1: Guidance, ReplaceEdited)"
  - "Forms/DeleteReferencesDialog.cs (D2: DeleteChoice Choice)"
affects: [03-10, 03-11, 03-12, 03-13, 03-14, 03-15, 03-16, 03-17, 03-18, 03-19, 03-20]
tech-stack:
  added: []
  patterns:
    - "All Phase 3 copy with counts/parts/states comes from CaseGuideUiText; UI plans stay layout-only"
    - "Confirmations return (Title, Text) tuples for MessageBox"
    - "ThemedDialog scales the empty form once (SuspendLayout, AutoScale 7x15, Font, PerformAutoScale) before children are built with LogicalToDeviceUnits"
    - "Dialogs call FinishLayout() to size ClientSize from the root TLP preferred height; D2 calls it again in OnLoad once ListBox.ItemHeight is real"
key-files:
  created:
    - UserControls/CaseGuideUiText.cs
    - ReelDiscovery.Tests/Case/CaseGuideUiLogicTests.cs
    - Forms/ThemedDialog.cs
    - Forms/RegenerateTabDialog.cs
    - Forms/DeleteReferencesDialog.cs
    - ReelDiscovery.Tests/Render/DialogRenderTests.cs
  modified:
    - ReelDiscovery.Tests/Render/RenderHarness.cs
decisions:
  - "[03-06]: Empty reason/message strings drop the ': {message}' / '{reason}. ' clause instead of rendering ': .' (Colon/Sentence helpers)"
  - "[03-06]: Singular counts get singular follow-ups beyond the spec table: 'Fix it' (1 error), 'that link will be removed' (1 value), '1 fact still points ...; it is listed'"
  - "[03-06]: The C5 fact-delete confirmation is named C5DeleteFact (the plan's behavior list called it DeleteFactConfirm)"
  - "[03-06]: D2's fact list host uses padding 10/6 (the TextFieldHost text inset) instead of 1, so list text lines up with field text and does not touch the rounded border"
  - "[03-06]: RenderHarness.Capture draws the full window (form.Size) for bordered forms; DrawToBitmap includes the title bar, so ClientSize cropped the bottom and right edges of dialogs"
metrics:
  duration: 25min
  completed: 2026-10-01
---

# Phase 3 Plan 06: Case Guide Copy Helpers and Dialogs Summary

All Phase 3 UI copy that has a count, a part name or a state is now in one pure, unit-tested class, `CaseGuideUiText`. It gets grammar right ("The cast was not written", "1 fact references ...", "3 warnings remain"). This plan also adds the shared `ThemedDialog` chrome and the two Case Guide dialogs: D1 `RegenerateTabDialog` and D2 `DeleteReferencesDialog`. Both are content-sized and render cleanly at 144 DPI.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | CaseGuideUiText + CaseGuideUiLogicTests | 716dd83 (RED), 82a0987 (GREEN) | UserControls/CaseGuideUiText.cs, ReelDiscovery.Tests/Case/CaseGuideUiLogicTests.cs |
| 2 | ThemedDialog, D1, D2 + 144-DPI renders | 70592c2 | Forms/ThemedDialog.cs, Forms/RegenerateTabDialog.cs, Forms/DeleteReferencesDialog.cs, ReelDiscovery.Tests/Render/DialogRenderTests.cs, ReelDiscovery.Tests/Render/RenderHarness.cs |

## Render inspection (ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/dialog-*.png)

I opened all 5 PNGs: dialog-regenerate-cast, dialog-regenerate-facts, dialog-regenerate-values, dialog-delete-references-9 and dialog-delete-references-1. Every `.clip.txt` reports no findings.

- **Width:** the client area is 780 px (520 logical). Headline and body text wrap inside the 472-logical content width.
- **Background and buttons:** the background is Theme.Background with no stock grey. All buttons are 51 px tall (34 logical) and right-aligned, and the rightmost button's right edge lines up with the field/list right edge. No button text is ellipsized; D2's three buttons fit on one row.
- **Spacing:** the footer sits 24 logical below the last content row, and the bottom padding is 24.
- **D1:** the guidance host and the checkbox start on the content edge. The checkbox appears only when editedCount > 0: the facts render with 0 has none, and the values render reads "Also replace my 1 edited value".
- **D2:** the 9-line render shows 6 rows plus a scrollbar, and the 1-line render shows a single row. List text is inset 10 logical, matching the text-field inset.
- **Fixed during inspection:**
  - The first render cut off the bottom and right of every dialog. `DrawToBitmap` includes the title bar, so the harness now captures `form.Size` for bordered forms.
  - D1's 3-line guidance box showed an always-on stock scrollbar while empty. It now uses `ScrollBars.None`; the edit control still scrolls with the caret.
- **Known render limit:** the placeholder text does not appear in `DrawToBitmap` output. The accepted StepCoreDocument guidance box has the same limit, so this is not a dialog bug.
- **Label inset:** label text starts about 3 logical px to the right of the field/checkbox edge. This is the standard WinForms Label text padding, the same as the reference screen.

## Verification

- `dotnet test --filter "FullyQualifiedName~CaseGuideUiLogicTests"`: 86 passed
- `dotnet test --filter "Category=Render&FullyQualifiedName~DialogRenderTests"`: 6 passed (DPI 144)
- All render tests: 9 passed. All non-render tests: 759 passed.
- `dotnet build ReelDiscovery.csproj` and the test project: 0 warnings, 0 errors
- All acceptance greps pass. There are no Color./SystemColors references in CaseGuideUiText, and no raw colors or `new Font(` in the three forms.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Render harness cropped bordered dialogs**
- **Found during:** Task 2 (PNG inspection)
- **Issue:** `Capture` used `ClientSize` for the bitmap, but `DrawToBitmap` paints the whole window including the title bar. The dialog bottoms and right edges were missing from the PNGs.
- **Fix:** For forms with a border, the bitmap is now `form.Size`. Borderless hosts are unchanged.
- **Files modified:** ReelDiscovery.Tests/Render/RenderHarness.cs
- **Commit:** 70592c2

**2. [Rule 1 - Visual] Stock scrollbar in the empty D1 guidance box**
- `_txtGuidance.ScrollBars = ScrollBars.None` (commit 70592c2).

**3. [Rule 2 - Correctness] Grammar for singular counts and empty messages**
- Added singular agreement and empty-message handling beyond the literal spec templates (see decisions). Commit 82a0987.

**4. D2 list host padding**
- The plan's padding of 1 became 10/6, so list text aligns with the field text inset and the list height is rows × ItemHeight + padding + 2.

**Threat mitigations applied:**
- T-03-20: labels and the checkbox use `UseMnemonic = false`, and `&` in the first name is escaped to `&&` in the `Keep {firstName}` button, because ThemedButton draws its text without NoPrefix.
- T-03-21: wrapping labels have `MaximumSize` width, the list shows at most 6 rows and scrolls, and the footer wraps within the content width.

## Not marked complete

GUIDE-08 and GUIDE-09 stay Pending. This plan supplies their copy and dialogs; the screens that use them come in later plans (same approach as 03-02).

## TDD Gate Compliance

Task 1 has a RED commit (`test(03-06)` 716dd83, which failed to compile because CaseGuideUiText did not exist yet) followed by the GREEN commit `feat(03-06)` 82a0987.

## Known Stubs

None.

## Self-Check: PASSED
