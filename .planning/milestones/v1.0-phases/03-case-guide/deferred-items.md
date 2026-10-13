# Deferred Items (Phase 03)

## From 03-13

- **Flaky render test:** `DialogRenderTests.RegenerateTabDialog_RendersWithoutClipping(part: Cast, editedCount: 3)` failed once in a full `Category=Render` run. The clipping detector reported the "Also replace my 3 edited characters" CheckBox at 348x5 when it needs 348x30. It passed when run alone, both with and without the 03-13 changes, and passed on the next full run (23/23). This looks like a layout-timing race in the dialog's AutoSize CheckBox. 03-13 did not touch that file, so the issue is out of scope here.
- **Render-harness artifact:** `DrawToBitmap` does not clip the native vertical scrollbar of a multiline TextBox to its scrolled parent panel. As a result, the scrollbar arrows of Key involvement and Writing voice show up outside the detail fields area in the cast PNGs. Real windows clip child windows, so this does not happen at runtime. The clipping detector does not flag it.

## From 03-14

- **LinkPicker long items:** `CheckedListBox` cannot ellipsize, so a cast item such as `CH-001  Maria Delgado — President, Brightline Glazing, LLC` is cut at the list's right edge in the 40% detail card. The id, name and " (did it)" suffix always show (the suffix is placed before " — " for this reason). A tooltip or owner-drawn ellipsis in `LinkPicker` would fix it; out of scope for the Facts tab.

## From 03-16

- **Wizard window too small for the Case Guide (recommend fixing in 03-18):** `WizardForm` opens at a 900x600 client, and its header band and nav bar take some of that, so the Case Guide gets less height than the 900x600 render. At 900x600 with the banner and a needs-review notice, the Key Facts detail card has only a few pixels left for fields (they scroll), and the Checks list folds to its caption line ("Make the window taller to see the list."). At 1100x750 everything fits. 03-18 (WizardForm case flow) should open the case flow at about 1100x750 logical, or maximized, and raise `MinimumSize`.
- **Values grid at 900x600:** the Canonical Values columns (28 + 64 + 112 + 140 + 120 + 56 = 520) are wider than the grid beside the detail card, so the grid scrolls sideways at the minimum window. 03-15 exempted this in `ValuesPanelRenderTests`, but the UI-SPEC allows that only on Key Facts. 03-16 renders Values only at 1100x750.
- **Tab filters on case switch:** `OnEnterStepAsync` resets the selected tab and the run state when a different case is bound, but the Cast (All/Key/Background) and Key Facts (All/Needs review) filters have no public reset. `SelectEntity` already clears a filter that hides the target, so this only affects which rows show first.
- **Render-harness artifact (again):** in the full-step PNGs, the detail panel's native scrollbar (and a multiline field's scrollbar) can be painted past the detail card edge as a thin grey strip, and TextBox placeholder text is not drawn. Both come from `DrawToBitmap` (WM_PRINT ignores child clipping and does not paint placeholders). They do not occur at runtime.

## From 03-18

- **Resolved:** the 03-16 "wizard window too small" item. WizardForm opens at a 1150x900 logical client (minimum 1000x700, both clamped to the working area); the Case Guide keeps its Checks list at the default size.
- **NavigateToStage has no caller yet:** opening a recent case at its stage is wired by the case-opening plan.
