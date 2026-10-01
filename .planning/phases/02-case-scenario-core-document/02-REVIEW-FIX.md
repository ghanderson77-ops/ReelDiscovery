---
phase: 02-case-scenario-core-document
fixed_at: 2026-10-01T16:37:04Z
review_path: .planning/phases/02-case-scenario-core-document/02-REVIEW.md
iteration: 1
findings_in_scope: 12
fixed: 12
skipped: 0
status: all_fixed
---

# Phase 2: Code Review Fix Report

**Fixed at:** 2026-10-01T16:37:04Z
**Source review:** .planning/phases/02-case-scenario-core-document/02-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 12 (CR-01, CR-02, WR-01 to WR-10; Info findings were out of scope)
- Fixed: 12
- Skipped: 0
- Verification: `dotnet build ReelDiscovery.slnx` gives 0 warnings and 0 errors. `dotnet test ReelDiscovery.slnx` passes 489 of 489 tests (the baseline was 447; 42 tests were added).

## Fixed Issues

### CR-01: Scenario text and settings are lost when the user goes Back from the scenario step without clicking Next

**Files modified:** `UserControls/StepCaseScenario.cs`, `ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs`
**Commit:** fd51e17
**Applied fix:** `OnLeaveStepAsync` now saves the trimmed scenario, the complexity and the document, court and state choices through a new static `StoreInputs` helper. `ValidateStepAsync` uses the same helper. Leaving the step never touches `EffectiveScenario`, `Substitutions` or `EntityCheckHash`, so if the text changed, Next runs the real-name check again.

### CR-02: Hand-written Pending sections can never pass Finish, and Retry Section or Generate Document overwrites them without asking

**Files modified:** `Services/Case/CoreDocumentEditing.cs`, `Services/Case/CoreDocumentGenerator.cs`, `UserControls/StepCoreDocument.cs`, plus tests in `CoreDocumentEditingTests`, `CoreDocumentGeneratorTests` and `CoreDocumentUiLogicTests`
**Commit:** 9d49bde
**Applied fix:**
- `ApplyEdit` moves a Pending or Failed section to Ready when the edit has text.
- `GenerateSectionsAsync` skips user-edited sections that have text.
- The empty state (new static `ShowsEmptyState`) only appears when every section is Pending and has no text.
- The empty-state Generate Document button now resumes an existing outline (`fullRegenerate: CoreDocument == null`) instead of replacing it.
- While resuming, the status reads "Writing the remaining sections...".

**Status:** fixed: requires human verification (this changes section status logic).

### WR-01: "Keep the real name" (CASE-03 override) has no effect because the system prompt forbids real names

**Files modified:** `Services/Case/CoreDocumentGenerator.cs`, `ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs`
**Commit:** a902f5e
**Applied fix:** `BuildOutlinePrompt` adds a line to the user prompt that lists the kept names (`Apply == false`) as an approved exception to the fictional-names rule. The system prompt is unchanged, so prompt caching still works. The existing test that required kept names to be absent from the prompt was updated: it now checks that kept names are not in the "Never use" line.

### WR-02: The real-name leak scan skips memo header, court name and counsel fields that are shown in the document

**Files modified:** `Services/Case/CoreDocumentValidator.cs`, `ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs`
**Commit:** dab670c
**Applied fix:** The outline leak scan now covers every outline field that appears in the document:
- party names
- title
- court name, district and venue city
- every counsel field
- Memo To, From, Cc, Re and Organization

The title and the memo RE line also go through `CitationScanner`.

### WR-03: The validator accepts documents with no factual allegations, no counts, or empty Ready sections

**Files modified:** `Services/Case/CoreDocumentValidator.cs`, `ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs`
**Commit:** b6932fb
**Applied fix:** New Errors for:
- a pleading with no counts
- any document with no fact subsections
- a Ready section with no paragraphs

A new Warning checks the fact-subsection count against the preset's `FactSubsectionsMin/Max`.

### WR-04: A regex timeout throws `RegexMatchTimeoutException` into UI event handlers, and the static patterns have no timeout at all

**Files modified:** `Services/Case/CitationScanner.cs`, `Services/Case/CoreDocumentValidator.cs`, `Services/Case/CoreDocumentGenerator.cs`, `UserControls/StepCaseScenario.cs`, plus tests
**Commit:** 890564e
**Applied fix:**
- The four citation patterns and `ParagraphReference` now have a 1,000 ms match timeout (`CitationScanner.MatchTimeoutMs`).
- `Validate` catches timeouts for each section and for the outline scan, and reports a Warning instead ("Section too large to scan..."). It also ignores a timeout in the state-name heuristic.
- The generator's citation-retry check treats a timeout as "no findings", and the validator then reports the section.
- The scenario step catches a timeout in `NameSubstitution.Apply` instead of letting it reach the `async void` Next handler.

A reflection test checks that the timeouts are set. A stress test checks that a long run of capitalized words does not throw.

### WR-05: Retry Section ignores the guidance text and regenerates other sections too

**Files modified:** `UserControls/StepCoreDocument.cs`, `ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs`, `ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs`
**Commit:** dc22df8
**Applied fix:**
- Retry Section on a Pending or Failed section now calls `RegenerateSectionAsync(..., guidance)`. Only that section is written, and the guidance reaches the model.
- A new "Resume All" header button (static `HasUnfinishedSections`) takes over the document-wide resume. It only shows when unwritten sections remain and the empty state is not visible.
- The fatal-stop message now says "click Resume All or Regenerate All".

**Status:** fixed: requires human verification. This is the reviewer's second option. It adds a UI control, so please check the header layout and the wording.

### WR-06: Substitutes are not validated against the originals, and the substitution decisions cannot be reopened

**Files modified:** `UserControls/EntityReviewDialog.cs`, `UserControls/StepCaseScenario.cs`, `ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs`
**Commit:** 276822b
**Applied fix:**
- A new static `FindLeakingSubstitute` uses `NameSubstitution.FindLeaks(substitute, [entity])`. Apply Substitutes rejects a substitute that still contains the original name or one of its aliases.
- A "Review real names" link on the scenario step reopens `EntityReviewDialog` with `draft.Substitutions`. It shows when substitutions exist.
- `EffectiveScenario` is rebuilt on the next Next. The hash has not changed, so this makes no new LLM call.

### WR-07: The real-name check cannot be cancelled, and closing the form does not cancel in-flight LLM work

**Files modified:** `UserControls/StepCaseScenario.cs`, `UserControls/StepCoreDocument.cs`, `Forms/WizardForm.cs`, `ReelDiscovery.Tests/Case/RealEntityDetectorTests.cs`
**Commit:** 68a3db2
**Applied fix:**
- The scenario step creates a `CancellationTokenSource` for `DetectAsync`. A "Cancel check" link shows while the check runs, and cancelling shows "Real-name check cancelled".
- Both case steps override `Dispose(bool)` to cancel in-flight work.
- After each await, the code checks `IsDisposed` before touching controls. In `StepCoreDocument`, status writes go through a new `SetStatus` helper that does nothing once the step is disposed. `WizardForm.BtnNext_Click` also checks `IsDisposed` after validation.
- Incidentally, the scenario step's `Dispose` also disposes the `LoadingOverlay`, which is the fix for IN-04.

**Status:** fixed: requires human verification. Unit tests only cover the detector honoring the token, because the tests do not create WinForms controls. Please check by hand: close the window while the check runs and while a document is generating.

### WR-08: Closing the window with X, or with the Cancel button, skips the Finish "discard the case" confirmation

**Files modified:** `Forms/WizardForm.cs`, `ReelDiscovery.Tests/Case/WizardFlowTests.cs`
**Commit:** d9a273f
**Applied fix:**
- The "Close Case" prompt moved from the Finish branch into `OnFormClosing`. It shows for `CloseReason.UserClosing` when the new static `HasUnsavedCase(flow, draft)` returns true. Choosing No sets `e.Cancel = true`, and a `_closeConfirmed` flag stops a second prompt.
- Before the check, the current case step's unsaved input (typed scenario, pending section edit) is saved through its synchronous `OnLeaveStepAsync`.
- When unsaved case work exists, the Cancel button skips its generic prompt so the user is not asked twice.
- `FinishPath_ValidatesBeforeClose` now looks for the prompt in `OnFormClosing`. New tests cover `HasUnsavedCase`.

### WR-09: The empty-state "Generate Document" button discards an existing outline and pays for a new outline call

**Files modified:** `UserControls/StepCoreDocument.cs`, `ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs`
**Commit:** 3d2c65b (the change to the button's `fullRegenerate` is in 9d49bde, part of CR-02)
**Applied fix:** After a cancel or a fatal stop, the empty-state body now explains what happened and says whether the outline was kept. It no longer resets to the first-run text. The text comes from a new static `EmptyStateBody` helper.

### WR-10: Own-caption detection uses substring matching and hides real case-name warnings

**Files modified:** `Services/Case/CitationScanner.cs`, `ReelDiscovery.Tests/Case/CitationScannerTests.cs`
**Commit:** 35f132a
**Applied fix:** `SideMatches` now compares whole tokens: either the caption side's tokens are all in a party's tokens, or the party's tokens are all in the side's tokens. "Grace v. Pace" and "Goldberg v. Pace" are no longer hidden by short names like "Ace" or "Gold". Real captions that use short or full names still pass.

**Status:** fixed: requires human verification (this changes matching logic).

## Notes

- The worktree could not be attached to `main`, because `main` is already checked out in the main working tree. The fixes were made in a temporary worktree on a branch called `reviewfix-02`. After the final build and tests passed, `main` was fast-forwarded to it. The worktree and the branch have been removed. The main working tree was clean before the fast-forward.
- Info findings IN-01 to IN-03, IN-05 and IN-06 were not addressed. IN-04 was fixed as a side effect of the `Dispose` override in WR-07.

---

_Fixed: 2026-10-01T16:37:04Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
