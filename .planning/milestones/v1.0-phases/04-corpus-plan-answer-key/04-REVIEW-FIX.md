---
phase: 04-corpus-plan-answer-key
fixed_at: 2026-10-03T00:00:00Z
review_path: .planning/phases/04-corpus-plan-answer-key/04-REVIEW.md
iteration: 1
findings_in_scope: 13
fixed: 13
skipped: 0
status: all_fixed
---

# Phase 4: Code Review Fix Report

**Fixed at:** 2026-10-03
**Source review:** .planning/phases/04-corpus-plan-answer-key/04-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 13 (2 Critical, 11 Warning; Info excluded by scope)
- Fixed: 13 (WR-01, WR-02 and WR-08 are smallest-safe mitigations, documented below)
- Skipped: 0

**Verification:** build 0 warnings / 0 errors; `dotnet test --filter "Category!=Render"` 2013/2013 passed;
`Category=Render&FullyQualifiedName~Corpus` 20/20 passed; ReviewPlan + CaseSummary render tests 15/15 passed.

## Fixed Issues

### CR-01: Integer overflow in `CorpusSettings.Normalize` crashes Open Case

**Files modified:** `Models/Corpus/CorpusSettings.cs`, `Services/Case/CaseStore.cs`, `Services/Corpus/CorpusPlanStore.cs`, `ReelDiscovery.Tests/Case/CaseStoreTests.cs`, `ReelDiscovery.Tests/Corpus/CorpusSettingsTests.cs`
**Commit:** 939b731
**Applied fix:** Each mix share is clamped to 0..100 before summing. `OverflowException` was added to the catch filters of `CaseStore.Load` and `CorpusPlanStore.Load` as defense in depth. Regression tests: `Normalize_HugeShares_DoNotOverflow`, plus `Load_OverflowingCorpusMix_OpensWithoutThrowing`, which loads a crafted case.json end to end.

### CR-02: Opening a case at Review Plan auto-replans despite `SuppressAutoPlanOnce`

**Files modified:** `UserControls/CorpusUiText.cs`, `UserControls/StepReviewPlan.cs`, `UserControls/StepReviewPlan.Run.cs`, `ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs`
**Commit:** 13dc884
**Status:** fixed: requires human verification (entry-state logic)
**Applied fix:**
- `ReviewPlanEntryDecision` now returns AutoReplan only when `!SuppressAutoPlan`.
- An unapproved plan whose settings changed now falls through to the stale banner (stale still wins) or the SettingsBanner.
- `ShowCurrentPlan` shows the SettingsChanged banner for unapproved plans too. `PlanBannerText` got an `approved` parameter, so unapproved plans get wording that does not claim the plan was approved.
- Tests cover suppress + unapproved + settings changed, with dismissed and stale variants, and the unapproved banner wording.

### WR-01: Re-planning overwrites the only `corpus-plan.json` before `case.json` points at it

**Files modified:** `Services/Corpus/CorpusPlanStore.cs`, `ReelDiscovery.Tests/Corpus/CorpusPlanStoreTests.cs`
**Commit:** 4bd56fb
**Applied fix (smallest safe mitigation, as instructed):** The fixed file name and the write order are unchanged.
- `SaveAsync` first copies the plan being replaced to `corpus-plan.json.prev`, using the atomic writer.
- When the main file is readable but holds a different `PlanId`, `Load` falls back to the `.prev` file if that file matches the id case.json expects. It then copies the recovered plan back over `corpus-plan.json` (best effort), so the next re-plan backs up the right plan.
- The full per-plan-file redesign was not done.
- Tests: recovery after replacement, and normal loading of the new id.

### WR-02: A partial approval write leaves new-plan files on disk with old-plan stamps

**Files modified:** `Services/Corpus/CorpusApproval.cs`, `UserControls/StepReviewPlan.Run.cs`, `ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs`
**Commit:** da0bc0b
**Applied fix (mitigation, reviewer's alternative):**
- The new `CorpusApproval.RecordPartialWrites` re-stamps `WrittenFiles` entries only for files that were actually overwritten before the failure. `ApproveAsync` calls it on failure.
- Approval itself is still recorded only after all three writes succeed (D-26).
- On a failure that wrote files, the Review Plan step now saves case.json so the stamps persist.
- Staged temp-file writes were not used, because the common failure (Word holding the file open) happens at the final move anyway.
- The existing test `Approve_SecondFileFails_RecordsNothing` was updated, since its assertion encoded the bug. It is now `..._ApprovesNothing_StampsOnlyTheWrittenFile`. A new test covers a re-plan with a locked protocol.

### WR-03: Non-relevant skeleton indexes empty sender lists when no cast member is active

**Files modified:** `Services/Corpus/NonRelevantSkeletonBuilder.cs`, `ReelDiscovery.Tests/Corpus/NonRelevantSkeletonTests.cs`
**Commit:** a12583f
**Applied fix:**
- The new `ActiveSenders(run)` falls back to the whole cast, with one `ctx.Notes` entry, when nobody is active in the window. Daily, ancillary and the decoy fallback all use it.
- `WeightedIndex` returns 0 when the weights sum to 0 or less.
- I confirmed the regression test fails with `ArgumentOutOfRangeException` without the fix and passes with it.

### WR-04: `DefaultWindow` throws on fact dates at the edges of the `DateTime` range

**Files modified:** `Models/Corpus/CorpusSettings.cs`, `Services/Corpus/SkeletonContext.cs`, `ReelDiscovery.Tests/Corpus/CorpusSettingsTests.cs`
**Commit:** acf8d4a
**Applied fix:** Added `CorpusSettings.SafeAddMonths`, which clamps to `DateTime.MinValue.Date` / `MaxValue.Date` instead of throwing. It is used in `DefaultWindow` and in `SkeletonContext.ResolveWindow`'s +/-3-month fallbacks. Tests cover facts dated 0001-01-15 and 9999-11-20.

### WR-05: `PlanSummary` converts untrusted `SentAt` without the edge guard

**Files modified:** `Services/Corpus/BusinessCalendar.cs`, `ReelDiscovery.Tests/Corpus/PlanSummaryTests.cs`
**Commit:** 528ef10
**Applied fix:** `BusinessCalendar.LocalDate` now applies the validator's guard: if the UTC year is below 2 or above 9998, it uses the instant's own offset date. This protects `PlanSummary.MonthOf` and every other caller. Tests use SentAt at 0001-01-01 and 9999-12-31 with an America/* zone.

### WR-06: Decoy prompts include the full case scenario; the must-not-mention check is effectively unenforced

**Files modified:** `Services/Corpus/SubjectSeeder.cs`, `ReelDiscovery.Tests/Corpus/SubjectSeederTests.cs`
**Commit:** 84a994b
**Status:** fixed: requires human verification (heuristic thresholds)
**Applied fix:**
- `BuildDecoyPrompt` passes `includeScenario: false`.
- Added a code-side paraphrase check. `FactTitleTokens` collects each fact title's distinctive words: 4 or more characters, lower-cased, plural "s" dropped, stop words removed, and words from organization, project and cast names excluded, because D-14 allows those in decoys. `ParaphrasesFact` rejects a subject or angle that carries at least 3 of a fact's distinctive words and at least 60% of them. Rejected seeds go through the existing re-ask and fallback path.
- The existing whole-title and value substring check is kept.
- Tests cover the decoy prompt omitting the scenario, and paraphrase versus keyword-sharing cases.

### WR-07: Guidance weights crash planning on duplicate or case-variant fact ids

**Files modified:** `Services/Corpus/CorpusPlanner.cs`, `ReelDiscovery.Tests/Corpus/CorpusPlannerTests.cs`
**Commit:** f61075c
**Applied fix:** `MapWeights` builds the known-id map with `TryAdd`, so the first id wins. Test: `MapWeights_DuplicateAndCaseVariantFactIds_FirstWins`.

### WR-08: Review protocol file name depends on the case folder name

**Files modified:** `Services/Corpus/ReviewProtocolBuilder.cs`, `UserControls/StepCaseSummary.cs`, `ReelDiscovery.Tests/Corpus/ReviewProtocolTests.cs`
**Commit:** 9aa8f27
**Applied fix (mitigation, reviewer's alternative; naming unchanged):**
- New `ReviewProtocolBuilder.IsProtocolFileName` accepts only a bare `... - Review Protocol.docx` name: no folder part, no `..`, no invalid characters, not rooted.
- New `ResolveFileName(folder, recordedName)` returns the current-folder name when that file exists. Otherwise it returns the validated recorded `WrittenFiles` name, if that file exists.
- Case Summary uses this resolution for the status, the Open button and the displayed name, so a renamed or suffixed folder no longer shows "Not written".
- Known gap: re-approving after a rename still writes a protocol under the new name next to the old file. The old file is never deleted automatically.

### WR-09: Cancel stays enabled after the planner returns

**Files modified:** `UserControls/StepReviewPlan.Run.cs`, `UserControls/CorpusUiText.cs`
**Commit:** 6d173f0
**Status:** fixed: requires human verification (UI timing; no unit test for the WinForms click path)
**Applied fix:**
- Once `PlanAsync` returns, `_btnCancel` is disabled and `_cts.Token.ThrowIfCancellationRequested()` runs before applying. A cancel confirmed while the planner was finishing takes the cancelled path and discards the new plan (D-18).
- `BtnCancel_Click` re-checks state after the confirmation dialog. If the plan was already applied, it shows the new `CancelTooLateText` instead of pretending to cancel.

### WR-10: Loaded plan paths and names are trusted without validation

**Files modified:** `Models/Corpus/CorpusPlan.cs`, `ReelDiscovery.Tests/Corpus/CorpusPlanStoreTests.cs`
**Commit:** 302a1e6
**Applied fix:**
- `CorpusPlan.Normalize` keeps `message.FilePath` only when it matches PlanNumbering's shape: 4 `/` segments starting with `Mailboxes`, each segment a safe name, ending in `.eml`, not rooted, no backslash. Any other value becomes empty.
- An attachment `FileName` with `..`, invalid characters or surrounding whitespace also becomes empty. The validator's existing "no file name" check and the answer key's DocId fallback then cover it.
- Tests: tampered load, the path-shape theory, and a `MailboxesRoot == PlanNumbering.MailboxesFolder` guard.

### WR-11: Keep This Plan does not restore the approval the stale banner withdrew

**Files modified:** `UserControls/CorpusUiText.cs`, `ReelDiscovery.Tests/Corpus/ReviewPlanLogicTests.cs`, `Services/Corpus/CorpusApproval.cs`, `.planning/phases/04-corpus-plan-answer-key/04-UI-SPEC.md`
**Commit:** af839c7
**Applied fix:**
- Took the reviewer's text option rather than restoring approval. The banner now reads "Approval is withdrawn until you re-plan, or keep this plan and approve it again."
- Reason: re-approving rewrites the protocol from the current guide, and this matches the existing `KeptStaleText` ("Click Approve Plan to write the answer key again").
- The UI-SPEC copy, the banner test and the `KeepStalePlan` doc comment were updated to match.
- Render tests pass at 144 DPI.

---

_Fixed: 2026-10-03_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
