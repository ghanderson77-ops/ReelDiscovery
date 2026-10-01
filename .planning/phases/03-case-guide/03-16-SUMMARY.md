---
phase: 03-case-guide
plan: 16
subsystem: case-guide-ui
tags: [winforms, ui, wizard-step, generation, autosave, validation, render, dpi]
requires:
  - "03-04: CaseStore (SaveAsync, LastSavedUtc, LastSaveError, CanSave)"
  - "03-06: CaseGuideUiText (TabCaption, StateCardText, ChecksCaption, StaleBannerText, NextGateDecision, SubtitleText, outcome texts, C1/C2), RegenerateTabDialog"
  - "03-07/03-08: GuideStaleness (StaleParts, StaleKey, NeedsReviewCount, Stamp), CaseGuideValidator (Validate, CountErrors, CountWarnings), GuideFinding"
  - "03-09..03-11: CaseGuideGenerator (GenerateAsync, UpdateStaleAsync, RegenerateTabAsync, RegenerateEntryAsync, RetryIssueDetailsAsync)"
  - "03-12..03-15: IGuideHost, GuideTabPanel and the Issues / Cast / Key Facts / Canonical Values panels"
provides:
  - "StepCaseGuide (IWizardStep + IGuideHost): header with save state and Resume Guide, stale / needs-review banner, segmented tabs with per-tab actions, state cards, Checks card, status strip"
  - "Generation runs: auto first run (D-06), Resume / Generate / Retry {Part}, Update Guide (C1), Retry Issue Details, Regenerate {Tab}... (D1), Regenerate {Noun}, Cancel (C2)"
  - "Autosave after every part and every edit; Not saved status and subtitle on failure"
  - "Next gate: M1 unfinished, M2 errors, M3 warnings (default No)"
  - "StepCaseGuide.TabStateFor / UnfinishedParts / VisibleFindings / PartFor / ReasonWithoutRetryHint (pure, unit tested); LoadForRender for renders"
affects: [03-17, 03-18, 03-19, 03-20]
tech-stack:
  added: []
  patterns:
    - "Partial class split: StepCaseGuide.cs = layout + display refresh, StepCaseGuide.Run.cs = runs, host actions, autosave, lifecycle, Next gate"
    - "RunAsync(work, failedText) wraps every generator call: one run at a time, progress follows the writing part until the user picks a tab, partCompleted autosaves, OperationCanceledException first in the catch ladder"
    - "RefreshAll = RefreshShell (validate, captions, header, banner, Checks, body) + selected panel rebuild; NotifyEdited uses RefreshShell only, so a panel's in-place row updates are never rebuilt under it"
    - "Rows whose content wraps (banner) are Absolute and sized from the page width; an AutoSize row measures wrapping labels at zero width"
key-files:
  created:
    - UserControls/StepCaseGuide.cs
    - UserControls/StepCaseGuide.Run.cs
    - ReelDiscovery.Tests/Case/CaseGuideStepLogicTests.cs
    - ReelDiscovery.Tests/Render/CaseGuideRenderTests.cs
  modified:
    - UserControls/CastTabPanel.cs
    - UserControls/GuideTabPanel.cs
    - .planning/phases/03-case-guide/deferred-items.md
decisions:
  - "[03-16]: A tab shows its panel as soon as its part has entries (Cast: key characters), or is Ready even when empty; otherwise a state card (Writing / Waiting / Failed / Not written / No guide). Generating left over from a closed run counts as Not written"
  - "[03-16]: Unfinished parts = any part not Ready (every part when there is no guide). Issues with only failed details count as finished; Retry Issue Details covers them"
  - "[03-16]: Mid-run Checks show only findings about Ready parts, and issue / cast findings wait until the key facts are Ready (they mostly check links from facts)"
  - "[03-16]: The Checks list (152) folds to its caption line plus 'Make the window taller to see the list.' when it would leave the body under 300 logical px"
  - "[03-16]: The Checks grid draws its selection only while focused, so the first row never looks picked on entry"
  - "[03-16]: The banner row is Absolute, sized from the page width in SizeBanner (the AutoSize row measured the wrapping labels at zero width)"
  - "[03-16]: The generator's trailing 'Click Retry {Part}.' is stripped from failure reasons before the state card and status lines add their own instruction"
  - "[03-16]: Tab actions are hidden while a state card shows (the card carries the action); unparented tab actions are disposed with the step"
  - "[03-16]: Regenerate Cast/Facts/Values failure reads 'The {part} was/were not regenerated: ... The previous {entries} were kept.'; Retry Issue Details failure reads 'The issue details were not written: ... Click Retry Issue Details.'"
  - "[03-16]: Detail card minimum is 344 (UI-SPEC 320) so Delete Character and Regenerate Character sit 8 apart; Cast grid minimums are Name 96, Title 80, Organization 96, Tier 96 so the grid fits beside it with a vertical scrollbar"
metrics:
  duration: 26min
  completed: 2026-10-01
---

# Phase 3 Plan 16: Case Guide Step Summary

The Case Guide wizard step is built and wired. It hosts the four tab panels under a header, a stale / needs-review banner, segmented tab toggles and a Checks card. It runs the guide generator with inline progress and Cancel, saves `case.json` after every finished part and every edit, and gates Next on unfinished parts, errors and warnings. A case with no guide starts generating as soon as the step opens. A reopened partial guide waits for Resume Guide.

## What was built

- **Layout (`StepCaseGuide.cs`):**
  - Root rows: header, banner, tabs row, body, Checks card and status strip, each with a row-index comment.
  - Header: "Case Guide" and the subtitle `{Documents\...} · Saved h:mm tt`, which turns Danger with `· Not saved` when a save fails. A `Resume Guide` button shows when parts are unfinished and no run is active.
  - Banner: a Warning border on a white card. Its variant comes from `StaleBannerText`. Stale variants show Update Guide and Not Now; the needs-review variant shows Review Facts. `DismissedStaleKey` hides the stale variants.
  - Tabs row: four Toggle buttons whose captions come from `TabCaption` (counts, "(writing)", "(failed)"). The selected panel's `TabActions` are moved onto the right side of the row.
  - Body: the selected panel, or a centered state card (heading, body, marquee bar or Primary action).
  - Checks card: caption from `ChecksCaption`, and a read-only `GridStyle` grid (Severity colored, Where, Check). Double-click or Enter opens the entity via `ShowTab` and `SelectEntity`. With no findings the card is one line ("No problems found..."). The card is hidden in the No guide state.
- **Runs (`StepCaseGuide.Run.cs`):**
  - `RunAsync` handles busy state, the progress bar (determinate when `Total > 0`), the tab that follows the writing part, autosave via `partCompleted`, and the outcome ladder: fatal, then refused, then part failed, then ready or ready with findings. It catches cancelled first, then refused, then fatal, then any other error. Its `finally` restores the UI and saves.
  - Entry points:
    - `GenerateOrResumeAsync`: auto-run, Resume, Generate and Retry.
    - `UpdateGuideAsync`: asks C1 first.
    - `RetryIssueDetailsAsync`.
    - `RegenerateTabAsync`: opens the D1 dialog.
    - `RegenerateEntryAsync`: shows `Writing {name}...` and turns the entry's dot Accent. On failure it shows `{Name} was not regenerated...`.
  - All runs go through one `CreateGenerator()` (`new CaseGuideGenerator(_state.CreateGateway())`).
- **Autosave:** `QueueSave` runs after every `NotifyEdited`. `SaveAsync` saves through `CaseStore.SaveAsync`, refreshes the subtitle, and shows `SaveFailedText` in Danger when the save fails. It has no bare catch.
- **Lifecycle:**
  - `OnEnterStepAsync` resets per-case UI when the case id changes and shows `PendingNotice`. It then runs `AdvanceStage(CaseGuide)`, saves, and auto-runs only when `Guide == null`.
  - `OnLeaveStepAsync` commits pending edits and saves.
  - `ValidateStepAsync` applies `NextGateDecision`: M1 and M2 block, and M3 asks Yes/No with No as the default.
  - `Dispose` cancels the run (WR-07).

## Verification

- `CaseGuideStepLogicTests` has 26 tests: TabStateFor, UnfinishedParts, VisibleFindings, ReasonWithoutRetryHint, and a failed-state body that says Retry once. All pass.
- Full suite: 1136/1136 passed (render tests included). The build has 0 warnings (app and tests).
- Plan grep checks:
  - StepCaseGuide.cs: class declaration present, 15 row comments, `CaseGuideValidator.Validate`, exactly one `StaleBannerText`, exactly one `LogicalToDeviceUnits(152)`.
  - StepCaseGuide.Run.cs: one `new CaseGuideGenerator(_state.CreateGateway())`, `CaseStore.SaveAsync`, `AdvanceStage(CaseStage.CaseGuide)`, 7 `if (IsDisposed)`, 3 `Button2`.
  - Both files: every member defined exactly once. No bare catch, no stock colors, no `new Font(`.

## Render inspection (12 PNGs, opened one by one; display at 120 DPI = 125%, DeviceDpi >= MinimumDpi)

- `case-guide-generating-cast-1100x750`: the "Cast (writing)" toggle is selected, and the centered card reads "Writing the cast..." with a marquee. The Checks caption is "Checks — run as each part finishes". The status strip shows the bar and Cancel Generation in Danger.
- `case-guide-cast-key-1100x750` / `-900x600`:
  - The tab toggles are 5 px apart (4 logical), all the same 34 height, with counts in their captions.
  - The left edge of "Issues (1)" lines up with the card left edge (x 31). The right edge of "Regenerate Cast..." lines up with the card right edge (x 1344).
  - At 900x600 there is no horizontal grid scrollbar and "Background" and "Organization" are not cut off. Delete and Regenerate Character are 8 apart.
- `case-guide-cast-background-1100x750`: CH-008 Priya Nair is selected, and its meta line reads "Background · Staff".
- `case-guide-facts-needs-review-1100x750` / `-900x600`: the banner reads "1 fact references characters you changed." with Review Facts. The F-004 dot is Warning, the notice and Mark Reviewed show, and the count reads "6 facts · 1 needs review".
- `case-guide-values-1100x750`: Add Value and Regenerate Values... sit on the tabs row, and V-001 is selected.
- `case-guide-issues-1100x750`: the Issues tab is read-only and shows its detail labels. The grid card header is the same height (67 px) on every tab.
- `case-guide-stale-with-checks-1100x750` / `-900x600`: the banner reads "The guide is out of date with the core document." with Update Guide (Primary) and Not Now. Checks reads "(1 error, 12 warnings)" with Error in Danger. At 900x600 the list folds to its caption line.
- `case-guide-failed-part-1100x750`: the "Key Facts (failed)" toggle and "The key facts were not written". The body reads "The response was cut off. Click Retry Key Facts to try again. Finished parts were kept." (one instruction), with Retry Key Facts as the Primary button and Resume Guide in the header.
- `case-guide-no-guide-1100x750`: "No case guide yet" with Generate Guide, and the Checks card hidden.
- All 12: no ellipsized button text, no stock grey surfaces, the banner is a Warning border on white, status dots are centered, and the Checks caption never says "Issues". The clipping detector found nothing.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The banner pushed the tabs and body off the screen**
- **Found during:** Task 3 (render)
- **Issue:** The banner sat in an AutoSize row. That row measured the wrapping labels at zero width, so it became about 1,700 px tall and the body shrank to 1 px. Anchoring the card or pinning Min/MaxSize did not help.
- **Fix:** The banner row is now Absolute. `SizeBanner` computes its height from the page width on every root resize, using a `_showBanner` flag (Visible is false until the form is shown).
- **Files modified:** UserControls/StepCaseGuide.cs
- **Commit:** 36827bc

**2. [Rule 1 - Bug] Doubled retry instruction**
- **Found during:** Task 3
- **Issue:** Generator failure messages end with "Click Retry Key Facts.", and the state card and status lines add their own instruction, so the text read "...Click Retry Key Facts. Click Retry Key Facts to try again."
- **Fix:** Added `ReasonWithoutRetryHint`, which strips the trailing hint. It is applied to the state card reason and to the failed / refused status lines. Unit tested.
- **Files modified:** UserControls/StepCaseGuide.cs, UserControls/StepCaseGuide.Run.cs, ReelDiscovery.Tests/Case/CaseGuideStepLogicTests.cs
- **Commit:** 36827bc

**3. [Rule 1 - Visual] Overlapping detail card at the minimum window**
- **Found during:** Task 3
- **Issue:** At 900x600 the fixed 152 Checks card left about 175 px for the body. The detail card's notice and guidance box overlapped.
- **Fix:** The Checks list folds to its caption line plus "Make the window taller to see the list." when showing it would leave the body under 300 logical px. The render test asserts the list at 1100x750 and the folded line at 900x600.
- **Files modified:** UserControls/StepCaseGuide.cs, ReelDiscovery.Tests/Render/CaseGuideRenderTests.cs
- **Commit:** 36827bc, 7ead908

**4. [Rule 1 - Visual] The first Checks row looked selected**
- **Found during:** Task 3
- **Issue:** The grid makes its first row current after it is shown, so `ClearSelection` alone did not keep the row unselected.
- **Fix:** The selection is drawn in AccentSoft only while the grid has focus (Enter/Leave), and in Surface otherwise.
- **Commit:** 36827bc

**5. [Rule 1 - Visual] Mid-run Checks noise**
- **Found during:** Task 3
- **Issue:** While the cast was writing, Checks listed "No fact is linked to ISS-01".
- **Fix:** Mid-run, issue and cast findings wait until the key facts are Ready. Unit tested.
- **Commit:** 36827bc

**6. [Rule 1 - Visual] Cast grid scrolled sideways at 900x600; Delete and Regenerate touched**
- **Found during:** Task 3
- **Issue:** 03-13 fitted the Cast columns to 900x600 without allowing for a vertical scrollbar, which any real cast shows in the shorter step body. In the narrow detail card, "Delete Character" and "Regenerate Character" also had no gap.
- **Fix:**
  - Cast minimums are now Name 96, Title 80, Organization 96 and Tier 96 (Tier stays wide enough for "Background"; Organization stays wide enough for its header).
  - The detail card minimum is now 344 logical (UI-SPEC 320).
  - Regenerate has an 8 px left margin.
- **Files modified:** UserControls/CastTabPanel.cs, UserControls/GuideTabPanel.cs
- **Commit:** 36827bc

**7. [Rule 3 - Blocking] Task 1 stubs needed for the handlers wired in InitializeUI**
- Besides the listed stubs, Task 1 also stubbed `GenerateOrResumeAsync`, `UpdateGuideAsync`, `DismissStaleBanner` and `BtnCancel_Click` (all marked "Task 1 stub"). Task 2 removed them all; the grep count is 0.
- `_cts` is declared in StepCaseGuide.Run.cs, because an unused field in Task 1 caused a CS0169 warning.

**8. [Rule 2] Run-specific failure copy**
- Regenerate-tab and Retry Issue Details failures get their own status lines instead of the generic "Click Retry {Part}.". Neither has a Retry {Part} button, so the generic line would point to a button that does not exist.

## Known Stubs

None. Every control reads live draft data.

## Deferred Issues

See `deferred-items.md` (03-16 section):
- The wizard's 900x600 default window is too small for this step. 03-18 should open the case flow at about 1100x750.
- The 03-15 Values grid scrolls sideways at 900x600.
- The tab filters have no reset when the case changes.
- `DrawToBitmap` artifacts (stray scrollbar strips, missing placeholders) appear only in the PNGs, not at runtime.

## Threat Flags

None. No new network endpoints, file paths or trust boundaries. Saves go through the existing `CaseStore`, and calls go through the existing gateway (T-03-52..55 mitigated as planned).

## TDD Gate Compliance

- RED: 4e19bcb `test(03-16)`. The tests failed to compile because `StepCaseGuide` did not exist yet.
- GREEN: de99a81 `feat(03-16)`, with 16/16 tests passing.

## Self-Check: PASSED

All 4 created files exist, all 5 task commits (4e19bcb, de99a81, e62b7b6, 36827bc, 7ead908) are in git history, and 12 `case-guide-*.png` renders exist.
