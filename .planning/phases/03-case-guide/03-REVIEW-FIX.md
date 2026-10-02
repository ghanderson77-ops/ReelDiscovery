---
phase: 03-case-guide
fixed_at: 2026-10-01T00:00:00Z
review_path: .planning/phases/03-case-guide/03-REVIEW.md
iteration: 1
findings_in_scope: 12
fixed: 12
skipped: 0
status: all_fixed
---

# Phase 3: Code Review Fix Report

**Fixed at:** 2026-10-01
**Source review:** .planning/phases/03-case-guide/03-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 12 (CR-01, CR-02, WR-01..WR-10)
- Fixed: 12
- Skipped: 0

Verification after all fixes: `dotnet build ReelDiscovery.csproj` reports 0 warnings and 0 errors. `dotnet test --filter "Category!=Render"` passes 1125/1125. `dotnet test --filter "Category=Render"` passes 54/54.

## Fixed Issues

### CR-01: The scenario is never sent to any guide prompt, so names that appear only in the scenario cannot be kept

**Files modified:** `Services/Case/CaseGuidePrompts.cs`, `ReelDiscovery.Tests/Case/CaseTestData.cs`, `ReelDiscovery.Tests/Case/CaseGuidePromptInvariantTests.cs`
**Commit:** 7e922ea
**Applied fix:** `AppendNameRules`, which every guide builder calls, now starts with `AppendScenario`. That block adds `draft.EffectiveScenario` (the substituted text, capped at 8,000 characters) to the user prompt, followed by "Keep every name the scenario uses exactly as written..." and the rule to invent a name only when neither the scenario nor the document gives one. The system prompt is unchanged and byte-stable. The roster requirement now reads "everyone the document or the scenario names". The fixture scenario now names "Oscar Martinez", who does not appear in the complaint. The prompt-invariant theory covers all 13 request paths (roster, details, background, counsel top-up, facts, values, the three tab regenerations, the three entry regenerations and issue details). For each path it asserts that the user prompt contains the effective scenario, "Oscar Martinez" and the scenario keep-names rule, and that the system prompt does not.

### CR-02: Issue details left Pending after a cancel or fatal error can never be written, and the validator reports nothing

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `Services/Case/CaseGuideValidator.cs`, `UserControls/IssuesTabPanel.cs`, `ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs`, `ReelDiscovery.Tests/Case/CaseGuideValidatorTests.cs`
**Commit:** f92b7b1
**Applied fix:**
- Added `CaseGuideGenerator.IssueDetailsMissing`: Issues is Ready but some issue has no details.
- Added `IssueDetailsInterrupted`: the same, with a details state that is not Failed.
- `GenerateAsync` (Resume Guide) now re-runs the details first when they were interrupted (Pending, or Generating left behind by a closed run), through `RunTrackedAsync` so the autosave hook fires. A fatal error stops the run. A Failed state is left for the Retry Issue Details button.
- That button now shows whenever details are missing, and is hidden only while this run is writing them.
- The validator warns "The issue details were not written. Click Retry Issue Details." when the state is Failed or when any issue has no description.
- Tests cover resume from Pending and from Generating, a Failed state waiting for the button, a fatal error during resume, and the validator warning for Pending and Generating.

### WR-01: Closing during a run loses paid-for units of the part being written (D-10)

**Files modified:** `UserControls/StepCaseGuide.Run.cs`, `Forms/WizardForm.cs`
**Commit:** d1673b3
**Applied fix:** I took the review's second option and wait for the run to finish instead of merging each unit as it completes. Per-unit merging would have changed the order in which F- and CH- IDs are assigned.
- `StepCaseGuide` now tracks the run in flight (`RunAsync` / `RegenerateEntryAsync` wrap the core methods through `TrackRun`).
- New `CancelAndWaitForRun(timeout)`: cancels the run, then pumps messages until it has unwound, so the generator's `finally` merges the finished units into the draft.
- `WizardForm.StopActiveRun` calls it with a 10-second timeout before it disposes the step. The form is disabled and shows a wait cursor during the wait, so no click and no second close can re-enter it. `OnFormClosing`'s `SaveNow` therefore writes those units.
- On timeout, closing falls back to the previous behavior.
- Status: **fixed: requires human verification.** No automated test covers this; it needs a real close during a live run. `StepCoreDocument` runs still use dispose-only.

### WR-02: Grid-cell edits are applied later and silently dropped (run start, window close, Next validation)

**Files modified:** `UserControls/GuideTabPanel.cs`, `UserControls/CastTabPanel.cs`, `UserControls/FactsTabPanel.cs`, `UserControls/ValuesTabPanel.cs`, `UserControls/StepCaseGuide.cs`, `UserControls/StepCaseGuide.Run.cs`, `ReelDiscovery.Tests/Render/CastPanelRenderTests.cs`
**Commit:** d61c959
**Applied fix:**
- In `GuideTabPanel`, `CellEndEdit` handlers now call `QueueGridEdit`, which adds the commit to a queue and posts a flush. The row rebuild is still deferred, so nothing re-enters the grid.
- New `GuideTabPanel.CommitGridEdits()`: calls `Grid.EndEdit()` if a cell is still in edit mode, then applies the queue synchronously.
- New `StepCaseGuide.CommitAllEdits()`: commits grid edits on every tab, then the selected tab's detail fields. It runs before `SetBusy(true)` in `RunAsync`, in `RegenerateEntryAsync` (which committed nothing before), in `RegenerateTabAsync`, in `OnLeaveStepAsync` (which also covers the close flush), and in `ValidateStepAsync` before `RefreshAll`. Switching tabs also commits the previous tab's grid edits.
- No layout or visual changes.
- Test: an STA render test types into a Title cell that stays in edit mode, calls `CommitGridEdits`, then sets the host busy. It asserts the draft keeps the new title.

### WR-03: Regenerate Document rebinds organizations and issues to different entities (positional party-N / count-N IDs)

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs`
**Commit:** 5a7ec88
**Applied fix:**
- `SyncOrganizations` now matches parties to organizations in three passes, the same way `IssueExtractor` matches counts to issues:
  1. Same PartyId and same normalized name.
  2. Same name anywhere. This handles parties that come back in a different order; the organization is re-pointed to the new PartyId.
  3. Same PartyId under a new name, which is a real rename.
- An overload reports `modified`, so callers bump `Revision`.
- `GenerateIssuesAsync` remembers the previous issue titles. When an issue keeps its ID for a retitled count, every fact linked to it gets `NeedsReview` with the reason "ISS-01 changed from \"X\" to \"Y\" in the core document."
- Tests: reordered parties keep their organization, domain and cast. A retitled count flags its facts. Unchanged titles flag nothing.
- Status: **fixed: requires human verification** (matching logic).

### WR-04: RegenerateValuesAsync leaves ValuesState stuck in Generating on an unexpected exception

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `ReelDiscovery.Tests/Case/CaseGuideRegenerationTests.cs`
**Commit:** 4d2385b
**Applied fix:** Added a `catch (Exception)` that records the failure message. The code then falls through to `state.Status = previous`. The test uses a throwing gateway and asserts the state is Ready again and the values are unchanged.

### WR-05: A cross-volume folder move can leave a split, half-deleted case

**Files modified:** `Services/Case/CaseStore.cs`, `UserControls/CaseGuideUiText.cs`, `UserControls/StepCaseSummary.cs`, `ReelDiscovery.Tests/Case/CaseStoreTests.cs`
**Commit:** b735a55
**Applied fix:**
- **Copy fails:** the partial target is deleted and the error is rethrown. The case stays where it was.
- **Copy succeeds:** `draft.FolderPath` is switched to the target, the pending snapshot is dropped, and the Recent cases entry is updated, all before the source is deleted.
- **Deleting the source fails:** this is now best effort. The leftover folder is reported through the new `CaseStore.LastMoveLeftover`, and Case Summary shows a Warning-colored status (`CaseGuideUiText.MovedWithLeftoverText`).
- An internal `CopyOnMove` test seam forces the copy path on one volume.
- Tests: with a file held open the way Word holds one, the case follows the copy, later saves go there, and the leftover is reported. A clean source is removed.

### WR-06: EnsureFolder silently forks a case into a new folder when its folder is missing

**Files modified:** `Services/Case/CaseStore.cs`, `ReelDiscovery.Tests/Case/CaseStoreTests.cs`
**Commit:** 756da18
**Applied fix:** `EnsureFolder` creates a folder only when `FolderPath` is empty. When it is set but missing, it throws `DirectoryNotFoundException`: "The case folder {path} is not available. Reconnect the drive or restore the folder; the case saves again on the next change." `SaveAsync` and `SaveNow` already turn that into "Not saved", and the C10 close prompt handles it. The test asserts both saves fail, `FolderPath` is unchanged, and no "(2)" folder is created.

### WR-07: Regenerate Cast churns IDs of seeded people and drops their fact links

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `ReelDiscovery.Tests/Case/CaseGuideRegenerationTests.cs`
**Commit:** 6d3510f
**Applied fix:** `MergeRoster` no longer removes the document author when the model leaves them out. The new `IsDocumentAuthor` uses the same name test as `SeedAuthor`: the pleading's signing attorney, or the person named in the memo's From line. `SeedAuthor` therefore finds them and does not re-add them under a new CH- ID, and their fact links stay. The test omits CH-006 (Morgan Ellis) from the roster and asserts that one Morgan Ellis is left, still CH-006, that the fact link survives, and that no review flag is set. Background staff are rewritten on purpose by Regenerate Cast and were left unchanged.

### WR-08: Cancelling a resumed Cast run skips the generator's autosave contract

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs`
**Commit:** b7cf1d9
**Applied fix:** `UnitRun.Merged` is set inside the merge `finally` of `WriteDetailsAsync` and `WriteBackgroundAsync`, on the calling thread. The user-cancel catch in `GenerateCastAsync` bumps `Revision` when `changed || run.Merged`, so `RunTrackedAsync` calls `partCompleted`. A new test cancels a resumed cast after one background unit finished. It asserts that the staff were merged, `Revision` was bumped and the Cast autosave hook ran. I confirmed the test fails without the fix.

### WR-09: Regenerated facts that the model drops silently lose their canonical-value links

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `Services/Case/CaseGuideEditing.cs`, `ReelDiscovery.Tests/Case/CaseGuideRegenerationTests.cs`
**Commit:** 3a02aee
**Applied fix:** `MergeRegeneratedFacts` calls `CaseGuideEditing.FlagValues(guide, fact.Id, "{id} was removed when the facts were regenerated.")` before it strips the ID. `FlagValues` now skips null values and null FactIds, since case.json is untrusted. The `RegenFacts` test asserts that V-003 is flagged with that reason and V-001 is not.

### WR-10: Organizations for parties removed from the core document linger and drive counsel and preset checks

**Files modified:** `Services/Case/CaseGuideGenerator.cs`, `Services/Case/CaseGuideValidator.cs`, `ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs`, `ReelDiscovery.Tests/Case/CaseGuideValidatorTests.cs`
**Commit:** d85c663
**Applied fix:**
- **Sync:** a party-mirrored organization that no party claims is kept, so its cast and domain stay valid. It is demoted (Party or Individual becomes ThirdParty), its PartyId is cleared, and the change is reported as modified. Roster prompts, counsel top-ups and counsel checks therefore stop asking for its counsel.
- **Validator:** `CheckParties` warns "{org} is no longer a party in the core document. Click Update Guide or Regenerate Cast." while the guide still points at the removed party.
- **Tests:** demotion (no counsel gap, not in the roster requirements) and the validator warning.

---

_Fixed: 2026-10-01_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
