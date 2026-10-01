---
phase: 02-case-scenario-core-document
reviewed: 2026-10-01T00:00:00Z
depth: standard
files_reviewed: 41
files_reviewed_list:
  - Forms/WizardForm.cs
  - Helpers/LoadingOverlay.cs
  - Models/Case/CaseDraft.cs
  - Models/Case/ComplexityPreset.cs
  - Models/Case/CoreDocument.cs
  - Models/Case/CoreDocumentSection.cs
  - Models/Case/EntitySubstitution.cs
  - Models/Case/Jurisdiction.cs
  - Models/WizardFlow.cs
  - Models/WizardState.cs
  - Services/Case/CitationScanner.cs
  - Services/Case/CoreDocumentAssembler.cs
  - Services/Case/CoreDocumentEditing.cs
  - Services/Case/CoreDocumentGenerator.cs
  - Services/Case/CoreDocumentTemplates.cs
  - Services/Case/CoreDocumentValidator.cs
  - Services/Case/DocumentTypeResolver.cs
  - Services/Case/NameSubstitution.cs
  - Services/Case/RealEntityDetector.cs
  - UserControls/EntityReviewDialog.cs
  - UserControls/StepApiConfiguration.cs
  - UserControls/StepCaseScenario.cs
  - UserControls/StepCoreDocument.cs
  - ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs
  - ReelDiscovery.Tests/Case/CaseDraftTests.cs
  - ReelDiscovery.Tests/Case/CaseScenarioUiLogicTests.cs
  - ReelDiscovery.Tests/Case/CaseSchemaTests.cs
  - ReelDiscovery.Tests/Case/CaseTestData.cs
  - ReelDiscovery.Tests/Case/CitationScannerTests.cs
  - ReelDiscovery.Tests/Case/ComplexityPresetTests.cs
  - ReelDiscovery.Tests/Case/CoreDocumentAssemblerTests.cs
  - ReelDiscovery.Tests/Case/CoreDocumentEditingTests.cs
  - ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs
  - ReelDiscovery.Tests/Case/CoreDocumentTemplatesTests.cs
  - ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs
  - ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs
  - ReelDiscovery.Tests/Case/DocumentTypeResolverTests.cs
  - ReelDiscovery.Tests/Case/NameSubstitutionTests.cs
  - ReelDiscovery.Tests/Case/RealEntityDetectorTests.cs
  - ReelDiscovery.Tests/Case/WizardFlowTests.cs
findings:
  critical: 2
  warning: 10
  info: 6
  total: 18
status: issues_found
---

# Phase 2: Code Review Report

**Reviewed:** 2026-10-01
**Depth:** standard
**Files Reviewed:** 41
**Status:** issues_found

## Summary

I reviewed the case-driven flow end to end: the scenario step, the real-name check and review dialog, outline-first generation, the section editor, the validator and the Finish gate. I also checked the provider and gateway cancellation code in `Services/Ai` that these files call. All 234 `Case` tests pass (`dotnet test --filter FullyQualifiedName~Case`).

Threading is mostly sound. No code in `Services/` uses `ConfigureAwait(false)`, so the generator changes section state on the UI thread. The parallel count/finding writes go through a `SemaphoreSlim`, which keeps them in step with the `Progress<string>` refreshes. Cancellation and fatal stops are also handled correctly: a user cancel goes through the linked `runCts`, and the provider sorts real cancels apart from SDK timeouts.

The serious problems are in how user work is kept:

1. **Scenario text is lost.** Going Back from the scenario step without clicking Next throws away the typed scenario.
2. **Hand-written sections are blocked and overwritten.** A hand-written Pending section can never pass Finish. Retry Section, or the empty-state Generate Document button, then overwrites it without asking.

Other gaps:

- The "keep the real name" option has no effect, because the system prompt forbids real names.
- The real-name leak scan skips the memo header and the court name.
- The validator lets structurally empty documents through.
- Regex timeouts can crash the UI because nothing catches the timeout exception.

## Critical Issues

### CR-01: Scenario text and settings are lost when the user goes Back from the scenario step without clicking Next

**File:** `UserControls/StepCaseScenario.cs:281-310`
**Issue:** `OnEnterStepAsync` always copies the draft back into the controls (`_txtScenario.Text = draft.Scenario;` plus complexity and the combos). `OnLeaveStepAsync` is a no-op, and the draft is only written in `ValidateStepAsync`. Example: the user types or pastes a scenario (up to 8,000 characters), clicks Back to change the provider or key on the API step, then clicks Next. The scenario box is overwritten with the previous `draft.Scenario`, which is empty on first use, and the complexity and court choices are reset. Nothing warns the user.
**Fix:** Save the raw control values when leaving, without running validation or the entity check:
```csharp
public Task OnLeaveStepAsync()
{
    // Keep unsubmitted input; the entity check still runs on Next because EntityCheckHash is unchanged
    var draft = _state.Case;
    draft.Scenario = _txtScenario.Text;
    draft.Complexity = SelectedLevel();
    draft.DocumentPreference = (DocumentTypePreference)Math.Max(0, _cmbDocument.SelectedIndex);
    draft.CourtPreference = (CourtPreference)Math.Max(0, _cmbCourt.SelectedIndex);
    var selectedState = _cmbState.SelectedItem as string ?? AutoState;
    draft.StatePreference = draft.CourtPreference == CourtPreference.State && selectedState != AutoState ? selectedState : string.Empty;
    return Task.CompletedTask;
}
```
Do not touch `EffectiveScenario` or `EntityCheckHash` here. The next Next click re-runs the check because the text hash will differ.

### CR-02: Hand-written Pending sections can never pass Finish, and Retry Section or Generate Document overwrites them without asking

**File:** `Services/Case/CoreDocumentEditing.cs:46-51`, `Services/Case/CoreDocumentGenerator.cs:82`, `UserControls/StepCoreDocument.cs:343, 496, 756-773`
**Issue:** After a cancel or fatal stop, the unwritten sections are left `Pending`, and the editor is enabled for them (`UpdateEditorEnabled` only checks `hasSection`). `ApplyEdit` only moves `Failed` to `Ready`, so a Pending section the user writes by hand stays `Pending` with `IsUserEdited = true`. That causes three problems:
1. `CoreDocumentValidator.ValidateStructure` reports "has not been written yet" as an Error, so Finish stays blocked even though the section has text.
2. Retry Section on any Pending/Failed section calls `GenerateSectionsAsync`, which rewrites every Pending/Failed section (`todo` at generator line 82). Hand-edited sections are overwritten without checking `IsUserEdited`. The confirm at lines 756-766 only mentions the selected section, and only when that section is the edited one.
3. When every section is Pending (for example, cancelled right after the outline), line 496 switches to the empty-state panel, which hides the user's hand-written text. The panel's "Generate Document" button (line 343) runs `RunGenerationAsync(fullRegenerate: true)` with no confirmation. That replaces the whole document, outline included.
**Fix:** In `ApplyEdit`, treat any non-empty hand edit of a Pending or Failed section as Ready:
```csharp
if (section.Status is SectionStatus.Failed or SectionStatus.Pending && paragraphs.Count > 0)
{
    section.Status = SectionStatus.Ready;
    section.FailureMessage = string.Empty;
}
```
Also skip `IsUserEdited` sections in `GenerateSectionsAsync`, or ask once listing all of them. Base the empty-state check on "no paragraphs anywhere", not "all Pending". When an outline already exists, make the empty-state button resume (`fullRegenerate: false`), or ask before regenerating.

## Warnings

### WR-01: "Keep the real name" (CASE-03 override) has no effect because the system prompt forbids real names

**File:** `Services/Case/CoreDocumentGenerator.cs:21, 486-493`
**Issue:** The fixed `SystemPrompt` says "Every person, company, law firm and judge must be fictional; never use the name of a real person, company...". When the user unchecks a row in `EntityReviewDialog` and confirms "Keep Real Names?", `BuildOutlinePrompt` only lists the `Apply == true` entries. Nothing tells the model that the kept names are allowed, so the model is likely to replace them with its own invented names. The user's explicit, confirmed choice is ignored, and the document no longer matches the scenario.
**Fix:** Add a line to the user prompt, keeping the system prompt byte-stable for caching:
```csharp
var kept = draft.Substitutions.Where(s => !s.Apply && !string.IsNullOrWhiteSpace(s.Original)).ToList();
if (kept.Count > 0)
    sb.AppendLine($"The user has approved keeping these names exactly as written: {string.Join(", ", kept.Select(s => s.Original.Trim()))}");
```
Alternatively, add a "user-approved names" carve-out to the system prompt.

### WR-02: The real-name leak scan skips memo header, court name and counsel fields that are shown in the document

**File:** `Services/Case/CoreDocumentValidator.cs:344-349`
**Issue:** `outlineText` only covers party names, the title, the firm name and the attorney name. `BuildMemoHeader` displays `Memo.To`, `Memo.From`, `Memo.Cc` and `Memo.Re`, and `DocumentTitle` displays `Memo.Organization`. The caption shows `Jurisdiction.CourtName` and `District`, and the signature block shows `Counsel.Address` and `Counsel.Email`. None of these are scanned. A real name substituted out of the scenario can come back in an investigation memo's FROM/TO line and pass the Finish gate with no Error.
**Fix:** Build `outlineText` from every rendered outline field (Memo.To/From/Cc/Re/Organization, Jurisdiction.CourtName/District/VenueCity, Counsel.*). Also run `CitationScanner.Scan` over the title and memo RE line.

### WR-03: The validator accepts documents with no factual allegations, no counts, or empty Ready sections

**File:** `Services/Case/CoreDocumentValidator.cs:74-93, 253-284`; `Services/Case/CoreDocumentEditing.cs:32-55`
**Issue:**
- `ComplexityPreset.FactSubsectionsMin/Max` is never checked. An outline with `factSubsections: []` produces a complaint with no FACTUAL ALLEGATIONS section, and that only gets a paragraph-count Warning.
- Zero counts only gets the preset range Warning. A complaint with no causes of action can be Finished after one "Continue anyway?" click.
- A user can clear a Ready section's editor. `ApplyEdit` then stores zero paragraphs with status `Ready`, and no rule flags an empty required section.
**Fix:** Report an Error for a pleading with `Counts.Count == 0`, and for any document with `FactPlan.Count == 0`. Add `AddRangeWarning` for `FactPlan.Count` against `FactSubsectionsMin/Max`. Report an Error for `Status == Ready && Paragraphs.Count == 0`.

### WR-04: A regex timeout throws `RegexMatchTimeoutException` into UI event handlers, and the static patterns have no timeout at all

**File:** `Services/Case/NameSubstitution.cs:146`, `Services/Case/CoreDocumentValidator.cs:~46-52`, `Services/Case/CitationScanner.cs:45`, `UserControls/StepCoreDocument.cs:346, 449, 682`
**Issue:** `NameSubstitution` and the state matchers use a 1-second timeout, but nothing catches `RegexMatchTimeoutException`. `CoreDocumentValidator.Validate` claims it "Never throws on bad data", yet it runs inside `RefreshAll`. `RefreshAll` is called from the editor's `Leave` handler, `SelectedIndexChanged` and the `Progress<string>` callback, so a timeout would end up as an unhandled exception on the UI thread. The opposite problem exists in `CitationScanner.CaseName` (`[A-Z][\w.&'\-]*(?:\s+(?:[A-Z]...|of|the|and|&))*\s+v\.`): it has no timeout and backtracks roughly O(n²) over long runs of capitalized words with no "v.". The editor has `MaxLength = 0`, so its input size is unbounded.
**Fix:** Give the `[GeneratedRegex]` patterns a `matchTimeoutMilliseconds` (e.g. `[GeneratedRegex(..., RegexOptions.None, 1000)]`). In `Validate`, wrap each section's content scan in `try { ... } catch (RegexMatchTimeoutException) { issues.Add(Warning(..., "Section too large to scan")); }` so the validator really never throws.

### WR-05: Retry Section ignores the guidance text and regenerates other sections too

**File:** `UserControls/StepCoreDocument.cs:768-773`
**Issue:** For a Pending/Failed section the button says "Retry Section". It calls `RunGenerationAsync(fullRegenerate: false)`, which writes every Pending/Failed section and silently drops `_txtGuidance.Text`. The user's guidance never reaches the model. The guidance box is not cleared, which suggests it was used, and the "Section" label misdescribes a document-wide resume.
**Fix:** Either disable `_txtGuidance` while a Pending/Failed section is selected, or retry only the selected section through `RegenerateSectionAsync(..., guidance)` and offer "Resume All" as a separate action.

### WR-06: Substitutes are not validated against the originals, and the substitution decisions cannot be reopened

**File:** `UserControls/EntityReviewDialog.cs:223-259, 277-278`; `UserControls/StepCaseScenario.cs:333-334`
**Issue:** `FindBlankSubstitute` only rejects blank names. A substitute that equals the original, or contains it or one of its aliases (e.g. "Walter White" -> "Walter White Jr."), is accepted. `NameSubstitution.FindLeaks` then flags the substitute itself in every section: an Error for multi-word names, which blocks Finish. The user cannot fix this, because the dialog only reopens when the scenario text hash changes (`hash != draft.EntityCheckHash`). There is no way to revisit substitutions without editing the scenario.
**Fix:** In `BtnApply_Click`, reject substitutes that, after `NameSubstitution.FindLeaks(substitute, [entity])`, still contain the original or an alias. Add a "Review real names" link or button on the scenario step that reopens the dialog with `draft.Substitutions`.

### WR-07: The real-name check cannot be cancelled, and closing the form does not cancel in-flight LLM work

**File:** `UserControls/StepCaseScenario.cs:345`; `Forms/WizardForm.cs:356-363, 380-385`; `UserControls/StepCoreDocument.cs:47`
**Issue:** `DetectAsync(text)` gets no token. While it runs, Back is disabled and there is no cancel control, so the user waits for the full provider timeout. Meanwhile the wizard's Cancel button and the window X stay active. Closing the form mid-check, or mid-generation in `StepCoreDocument` (whose `_cts` is never cancelled on close), leaves the awaited continuations running against disposed controls. Examples are `_loadingOverlay.Hide()`, `new EntityReviewDialog(...).ShowDialog(this)`, and `_lblStatus.Text = ...` inside `async void` handlers, and any of these can raise `ObjectDisposedException`. Calls already sent also keep billing.
**Fix:** Give `StepCaseScenario` a `CancellationTokenSource` and pass it to `DetectAsync`. Override `Dispose` in both steps, or handle `WizardForm.OnFormClosing`, so it cancels pending work. After each `await`, check `IsDisposed` before touching controls.

### WR-08: Closing the window with X, or with the Cancel button, skips the Finish "discard the case" confirmation

**File:** `Forms/WizardForm.cs:328-339, 356-363, 380-385`
**Issue:** The new Finish path warns that closing discards the scenario and document, but `OnFormClosing` does not. The X button discards a fully written document (many Story-tier calls) with no prompt. The Cancel button only asks a generic "cancel?" question. The same data-loss case is guarded on one path but not the others.
**Fix:** Move the CaseDriven discard confirmation into `OnFormClosing`. Set `e.Cancel = true` on No when `_state.Case.CoreDocument != null || !string.IsNullOrWhiteSpace(_state.Case.Scenario)`, and skip the prompt when Finish has already confirmed it (a `_closeConfirmed` flag).

### WR-09: The empty-state "Generate Document" button discards an existing outline and pays for a new outline call

**File:** `UserControls/StepCoreDocument.cs:343, 496`
**Issue:** When the outline succeeded but every section is still Pending (cancel right after the outline, or a fatal stop on the first section), the empty state is shown. Its button calls `RunGenerationAsync(fullRegenerate: true)`, which makes a new outline call. That replaces the parties, counts and timeline the user has not seen yet and doubles the outline cost. There is no confirmation, unlike Regenerate All, which shows C3.
**Fix:** Use `fullRegenerate: _state.Case.CoreDocument == null` for the empty-state button, so it resumes the existing outline. Also show the cancelled or fatal message in `_lblEmptyBody` instead of resetting it to `DefaultEmptyBody`.

### WR-10: Own-caption detection uses substring matching and hides real case-name warnings

**File:** `Services/Case/CitationScanner.cs:107-113`
**Issue:** `SideMatches` returns true when either side contains the other as a raw substring (`p.Contains(side) || side.Contains(p)`). A short party name such as "Ace" or "Gold" matches inside unrelated surnames ("Grace", "Goldberg"). For example, "Grace v. Pace" is treated as the document's own caption, and its CaseName warning is dropped. One-letter or two-letter short names make this much worse.
**Fix:** Compare whole tokens. Split both normalized strings on spaces and require every side token to appear in a party's token set (or the reverse), instead of `string.Contains`.

## Info

### IN-01: The Finish warnings dialog describes every warning as a citation or name problem

**File:** `UserControls/StepCoreDocument.cs:973`
**Issue:** "{n} warnings remain (possible case names or citation phrases)" is also shown for preset-range, timeline, stale-document and jurisdiction warnings, which is misleading.
**Fix:** Use a neutral message ("{n} warnings remain (listed under Issues). Continue anyway?").

### IN-02: A refused section regeneration tells the user to "click Retry Section" on a section that shows "Regenerate Section"

**File:** `UserControls/StepCoreDocument.cs:801-805`
**Issue:** On failure the section stays `Ready` (the old text is kept), so the button still reads "Regenerate Section". Refusals also get the generic failure copy rather than the rephrase guidance.
**Fix:** Add a `when (ex.Result.Outcome == LlmOutcome.Refused)` branch, and say "click Regenerate Section".

### IN-03: The validator's "never throws" claim does not hold for null collections

**File:** `Services/Case/CoreDocumentValidator.cs:83`, `Services/Case/RealEntityDetector.cs:56`
**Issue:** A case.json with `"parties": null`, `"sections": null` or `"paragraphs": null` deserializes to null lists, and `doc.Parties.Count` or `string.Join(..., section.Paragraphs)` then throws `NullReferenceException`/`ArgumentNullException`. `RealEntityDetector` has the same problem with `response.Entities` set to null. This matters once Phase 3 loads case.json.
**Fix:** Normalize nulls to empty lists after `CaseDraft.FromJson`, or use `?? new()` at the use sites.

### IN-04: The `LoadingOverlay` in StepCaseScenario is never disposed

**File:** `UserControls/StepCaseScenario.cs:190`, `Helpers/LoadingOverlay.cs:155-166`
**Issue:** `Hide()` removes the overlay from its parent, so when the step is disposed (e.g. a flow switch in `BuildSteps`) the overlay and its two `System.Windows.Forms.Timer`s are not disposed.
**Fix:** Override `Dispose(bool)` in the step and dispose `_loadingOverlay`.

### IN-05: `FinishPath_ValidatesBeforeClose` tests source text, not behavior

**File:** `ReelDiscovery.Tests/Case/WizardFlowTests.cs:45-62`
**Issue:** It searches the text of `WizardForm.cs` for string positions. It breaks on harmless refactors (renaming `BtnBack_Click`, extracting a method) and passes when the order is wrong in a helper.
**Fix:** Extract the Finish decision into a testable static method (e.g. `ShouldClose(bool validated, WizardFlow flow, Func<bool> confirm)`) and test that.

### IN-06: The generator's "request timed out" branch is effectively unreachable

**File:** `Services/Case/CoreDocumentGenerator.cs:214-218`
**Issue:** Both text providers turn SDK timeouts into `LlmOutcome.Failed`/`Network` results (`OpenAITextProvider.cs:58-61`), so a timeout surfaces as `LlmCallException`. That is handled by `DescribeOutcome` ("Network: Request to OpenAI timed out"), not by the friendlier copy in this branch.
**Fix:** Map `LlmFailureKind.Network` in `DescribeOutcome` to "The request timed out. Click Retry Section.", and keep the OCE branch only as a fallback.

---

_Reviewed: 2026-10-01_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
