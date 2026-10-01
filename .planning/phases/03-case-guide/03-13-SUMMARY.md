---
phase: 03-case-guide
plan: 13
subsystem: case-guide-ui
tags: [winforms, ui, cast, grid, editing, render, dpi]
requires:
  - "03-12: GuideTabPanel, IGuideHost, RenderGuideHost, GridStyle"
  - "03-07: CaseGuideEditing (AddCharacter, SetCastField, FactsReferencing, DeleteCharacter)"
  - "03-06: CaseGuideUiText (CastCountText, LinkedFactsText, NoFilterMatchText, C3/C4, DeletedWithLinksText, FactsMarkedForReviewText), DeleteReferencesDialog"
provides:
  - "CastTabPanel (Cast tab: grid + detail editing, All/Key/Background filter, Add/Delete/Regenerate Character, Regenerate Cast...)"
  - "CastFilter enum; CastTabPanel.RoleText/TierText/SignatureStyleText/MetaLine/LinkedFactsText/SplitName/PassesFilter"
  - "GuideTabPanel.AddPairedCombos; drop-down fields hosted in the text fields' rounded 34 px border"
affects: [03-14, 03-15, 03-16]
tech-stack:
  added: []
  patterns:
    - "Detail fields are rebuilt only when the entity id or tier changes; otherwise values update in place (focused dirty boxes and invalid boxes are left alone)"
    - "Combo commits are deferred with BeginInvoke (SelectionChangeCommitted), so a tier change can rebuild the card without disposing the combo inside its own event"
    - "CommitPendingEdits commits every text field whose text differs from the stored value; an invalid value stays on screen with its Danger message and is never stored"
    - "Drop-down list fields sit in a CardPanel host (radius 6, BorderStrong, 34 high, text on the 10 px inner edge); while disabled the host shows the choice as a plain label instead of the grey system frame"
key-files:
  created:
    - UserControls/CastTabPanel.cs
    - ReelDiscovery.Tests/Case/CastPanelLogicTests.cs
    - ReelDiscovery.Tests/Render/CastPanelRenderTests.cs
    - .planning/phases/03-case-guide/deferred-items.md
  modified:
    - UserControls/GuideTabPanel.cs
decisions:
  - "[03-13]: Cast grid ID is 72 and Tier 100 (UI-SPEC 64/88 cut off 'CH-006' and 'Background'). The Fill minimums are Name 112, Title 92 and Organization 92, so the columns still fit 900x600 without a horizontal scrollbar (28+72+112+92+92+100 = 496)"
  - "[03-13]: Grid Name edits commit as CastField.FullName, an atomic split at the last space (same rule as SplitName), so one edit flags the referencing facts once"
  - "[03-13]: The status dot stays Accent for the entry being written even when DetailsPending; otherwise DetailsPending is BorderStrong"
  - "[03-13]: GuideTabPanel combos (AddCombo, AddPairedCombos) are hosted in the same rounded 34 px border as text fields. A disabled combo is swapped for a plain label so busy cards stay on theme"
  - "[03-13]: Regenerate Character clears the guidance only when the entry is no longer user-edited and was either edited before or changed (the host contract: success clears IsUserEdited); the card is rebuilt afterwards"
  - "[03-13]: A tier change that the current filter would hide resets the filter to All, and Add Character does the same, so the edited or new character stays selected"
metrics:
  duration: 14min
  completed: 2026-10-01
---

# Phase 3 Plan 13: Cast Tab Summary

The Cast tab is built on `GuideTabPanel`. It shows a grid of key and background characters with an All / Key / Background filter and a detail card where the user edits every field. Every change goes through `CaseGuideEditing`. The tab actions are Add Character, Delete (D2 when facts link to the character, C4 otherwise), Regenerate Character (C3 when edited) and Regenerate Cast.... The tab was checked in 6 renders at 144 DPI.

## What was built

- **`CastTabPanel : GuideTabPanel`** (`GuidePart.Cast`, caption "Characters", noun "Character", with toolbar):
  - **Header:** three compact Toggles, 28 high and 4 apart ("All" selected by default, "Key", "Background"), then 12 px of space and `{k} key · {b} background`. The filter hides rows. When no row matches, the grid shows "No background characters." or "No key characters.".
  - **Grid columns:** dot · ID · Name (Fill, ToolTipText = email) · Title · Organization · Tier. Only Name and Title are editable. `CellEndEdit` is deferred with `BeginInvoke`. If a value is rejected, the cell gets the stored value back and the status line shows `Name cannot be empty.` in Danger. A change updates only that row (the grid is rebuilt only if the sorted column changed), calls `NotifyEdited(Cast)`, and shows the facts-for-review status in Warning.
  - **Detail card for a key character:**
    - First name + Last name
    - Title
    - Organization (`{Org name} · {domain}`)
    - Role + Tier
    - Email, with the "Built from the name and organization. Edit to override." hint
    - Active from + Active to (placeholder `YYYY-MM-DD`)
    - Key involvement (4 lines)
    - Writing voice (2 lines)
    - Signature style
    - Signature (read-only, TextMuted, keeps its line breaks), with the "Built from the fields above." hint
    - Linked facts
  - **Detail card for a background character:** the same fields without Writing voice, Signature style and Signature. "Key involvement" is captioned "Role at the organization" and is 2 lines.
  - **When edits are saved:** text fields save on `Leave` and combos on `SelectionChangeCommitted` (deferred). `CommitPendingEdits` saves every field that changed.
  - **Errors:** a rejected value gets a Danger border and a message under the field, and is not stored.
  - **Facts marked for review:** when a name, organization or active-range edit flags facts, the status line reports `{n} facts reference {name} and were marked for review.`.
  - **Actions:**
    - Add Character selects the new character and focuses First name.
    - Delete shows D2 (`DeleteReferencesDialog`) when facts link to the character, or C4 (Yes/No, `Button2`) when none do. Afterwards it selects the neighbouring row.
    - Regenerate Character asks C3 (Yes/No, `Button2`) when the character was edited, then calls `Host.RegenerateEntryAsync(Cast, id, guidance)`.
    - Regenerate Cast... calls `Host.RegenerateTabAsync(Cast)`.
    - Every action checks `Host.IsBusy` first, and checks `IsDisposed` after an `await`.
- **`GuideTabPanel`:**
  - New `AddPairedCombos`.
  - `AddPairedTextFields` now uses a shared private `AddPair`.
  - Combos are hosted in a rounded field border (see Deviations).
- **Tests:**
  - `CastPanelLogicTests`: 19 cases covering roles, the meta line with and without the edited flag, linked facts for 0/1/3, `SplitName` (including a single word, extra spaces and an empty string) and the filter.
  - `CastPanelRenderTests`: 6 renders.

## Render inspection (144 DPI, every PNG opened)

- `cast-key-selected-1100x750.png`:
  - **Left edges:** the "Characters" caption is at x 64 and the dots at x 65. The ID header text and the cell text are both at x 106, and the Name header and cells are both at x 214.
  - **Toggles:** 41–42 px high (28 logical), with no ellipsized text.
  - **Paired fields:** First/Last name and Role/Tier have a 12–13 px gap (8 logical) between them.
  - **Combo text:** Organization, Role and Tier text starts at x 1033, the same x as the text field text.
  - **Tab buttons:** Delete Character and Regenerate Character are 34 high and not cut off.
- `cast-key-selected-900x600.png` (scrolled to the end of the card):
  - The Signature block shows all five lines: name, title, firm, phone and email.
  - The "Built from the fields above." hint and "Not in any fact yet." follow it.
  - The detail panel has no horizontal scrollbar.
  - The grid has no horizontal scrollbar and the clipping detector reports no findings.
- `cast-background-selected-1100x750.png`: shows "Role at the organization" (2 lines), with no Writing voice, Signature style or Signature.
- `cast-filter-background-1100x750.png`: the Background toggle is selected and the grid shows CH-008 and CH-009.
- `cast-field-error-1100x750.png`: Active from "03/03/2024" has a Danger border and the message "Use a date like 2024-03-03 (YYYY-MM-DD)." wrapped under it. The stored value did not change.
- `cast-busy-1100x750.png`: the CH-002 dot is Accent. The combos show plain text inside the rounded border (no grey system frame), and the toolbar buttons are disabled.
- **Clipping detector:** "no findings" for all 6 renders.
- **Harness artifact:** `DrawToBitmap` paints the native scrollbars of multiline TextBoxes outside the scrolled fields panel (for example, small arrows near the guidance box). Windows clips child windows at runtime, so this only appears in the renders. It is recorded in deferred-items.md.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Visual] ID and Tier cells were cut off ("CH-0...", "Backgrou...")**
- **Found during:** Task 2 render inspection
- **Issue:** The UI-SPEC widths (64 and 88) are too narrow for "CH-006" and "Background" plus the 15 px cell inset at 144 DPI.
- **Fix:** ID is now 72 and Tier 100. The Fill minimums went from Name 120 / Title 100 / Organization 100 to 112 / 92 / 92, so the 900x600 grid still has no horizontal scrollbar. The first attempt, which widened ID and Tier without trimming the minimums, caused one at 900x600 (754 > 752).
- **Files modified:** UserControls/CastTabPanel.cs
- **Commit:** 6228053

**2. [Rule 1 - Visual] Drop-down fields did not line up with the text fields**
- **Found during:** Task 2 render inspection
- **Issue:** The flat ComboBox from `AddCombo` had no border, and its text sat at x 1022 while text field text sat at x 1033. When disabled during a run, it painted a grey system 3D frame, which is off-theme.
- **Fix:** `GuideTabPanel` now puts each combo in a `CardPanel` (radius 6, BorderStrong, 34 high, text on the 10 px inner edge). While the combo is disabled, the host shows its text as a plain label instead. `ShowFieldError` works on the host too.
- **Files modified:** UserControls/GuideTabPanel.cs
- **Commit:** 6228053

**3. [Rule 3 - Blocking] There was no paired-combo helper for Role + Tier**
- **Fix:** Added `GuideTabPanel.AddPairedCombos`. It shares a new private `AddPair` with `AddPairedTextFields`.
- **Commit:** 6f6185b

**4. [Plan detail] Render test details**
- The field-error render drives the real commit path: it types an invalid date and calls `CommitPendingEdits()`, instead of calling the protected `ShowFieldError` directly.
- The Small key render is scrolled to the end of the card so that the Signature block is checked.
- There are still exactly 6 cast PNGs.

## TDD Gate Compliance

- RED: `50358b2 test(03-13): add failing Cast panel logic tests`. It failed to compile because `CastTabPanel` and `CastFilter` did not exist yet.
- GREEN: `6f6185b feat(03-13): ...`. All 19 logic tests pass.

## Known Stubs

None. The Signature value shows "—" when the signature is empty, which is by design for characters without one.

## Deferred Issues

See `deferred-items.md`:
- `DialogRenderTests.RegenerateTabDialog_RendersWithoutClipping(Cast)` failed once in a full render run (a CheckBox measured 5 px high). It passed alone and on the next full run. 03-13 did not change that file.
- The `DrawToBitmap` scrollbar artifact described above.

## Requirements

- GUIDE-02, GUIDE-03 and GUIDE-04 are marked Complete. Earlier plans built the generation, and the Cast tab now shows the result.
- GUIDE-08 and GUIDE-09 are left Pending. Cast review and editing are done, but the Facts and Values tabs and the step shell with its stale banner come in 03-14 to 03-16.

## Self-Check: PASSED

- The created files exist: UserControls/CastTabPanel.cs, ReelDiscovery.Tests/Case/CastPanelLogicTests.cs, ReelDiscovery.Tests/Render/CastPanelRenderTests.cs.
- Commits 50358b2, 6f6185b and 6228053 are in `git log`.
- Build: 0 warnings, 0 errors.
- Tests: 1032 non-render tests pass, and all 23 render tests pass, including the 6 new cast renders.
