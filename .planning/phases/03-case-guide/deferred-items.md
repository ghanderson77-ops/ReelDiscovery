# Deferred Items (Phase 03)

## From 03-13

- **Flaky render test:** `DialogRenderTests.RegenerateTabDialog_RendersWithoutClipping(part: Cast, editedCount: 3)` failed once in a full `Category=Render` run. The clipping detector reported the "Also replace my 3 edited characters" CheckBox at 348x5 when it needs 348x30. It passed when run alone, both with and without the 03-13 changes, and passed on the next full run (23/23). This looks like a layout-timing race in the dialog's AutoSize CheckBox. 03-13 did not touch that file, so the issue is out of scope here.
- **Render-harness artifact:** `DrawToBitmap` does not clip the native vertical scrollbar of a multiline TextBox to its scrolled parent panel. As a result, the scrollbar arrows of Key involvement and Writing voice show up outside the detail fields area in the cast PNGs. Real windows clip child windows, so this does not happen at runtime. The clipping detector does not flag it.

## From 03-14

- **LinkPicker long items:** `CheckedListBox` cannot ellipsize, so a cast item such as `CH-001  Maria Delgado — President, Brightline Glazing, LLC` is cut at the list's right edge in the 40% detail card. The id, name and " (did it)" suffix always show (the suffix is placed before " — " for this reason). A tooltip or owner-drawn ellipsis in `LinkPicker` would fix it; out of scope for the Facts tab.
