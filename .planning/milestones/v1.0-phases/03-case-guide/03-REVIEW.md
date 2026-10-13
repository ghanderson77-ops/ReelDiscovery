---
phase: 03-case-guide
reviewed: 2026-10-01T00:00:00Z
depth: standard
files_reviewed: 46
files_reviewed_list:
  - Forms/DeleteReferencesDialog.cs
  - Forms/RegenerateTabDialog.cs
  - Forms/ThemedDialog.cs
  - Forms/WizardForm.cs
  - Helpers/GridStyle.cs
  - Helpers/TextFieldHost.cs
  - Helpers/Theme.cs
  - Models/Case/CanonicalValue.cs
  - Models/Case/CaseDraft.cs
  - Models/Case/CaseGuide.cs
  - Models/Case/CaseIssue.cs
  - Models/Case/CaseOrganization.cs
  - Models/Case/CaseStage.cs
  - Models/Case/CastMember.cs
  - Models/Case/ComplexityPreset.cs
  - Models/Case/GuidePartState.cs
  - Models/Case/KeyFact.cs
  - Models/WizardState.cs
  - Services/Case/CanonicalValueScanner.cs
  - Services/Case/CaseFolderNaming.cs
  - Services/Case/CaseGuideDocxExporter.cs
  - Services/Case/CaseGuideEditing.cs
  - Services/Case/CaseGuideGenerator.cs
  - Services/Case/CaseGuidePrompts.cs
  - Services/Case/CaseGuideValidator.cs
  - Services/Case/CaseStore.cs
  - Services/Case/CoreDocumentGenerator.cs
  - Services/Case/EmailIdentity.cs
  - Services/Case/GuideStaleness.cs
  - Services/Case/IdAllocator.cs
  - Services/Case/IssueExtractor.cs
  - Services/Case/SignatureBuilder.cs
  - Services/Case/UsTimeZones.cs
  - UserControls/CaseGuideUiText.cs
  - UserControls/CastTabPanel.cs
  - UserControls/FactsTabPanel.cs
  - UserControls/GuideTabPanel.cs
  - UserControls/IGuideHost.cs
  - UserControls/IssuesTabPanel.cs
  - UserControls/LinkPicker.cs
  - UserControls/StepCaseGuide.cs
  - UserControls/StepCaseGuide.Run.cs
  - UserControls/StepCaseScenario.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepCoreDocument.cs
  - UserControls/ValuesTabPanel.cs
findings:
  critical: 2
  warning: 10
  info: 6
  total: 18
status: issues_found
---

# Phase 3: Code Review Report

**Reviewed:** 2026-10-01
**Depth:** standard
**Files Reviewed:** 46
**Status:** issues_found

## Summary

Reviewed the Phase 3 production changes (diff base `2402dff`): the case model and `case.json` store, guide generation/regeneration/merge, editing and staleness, validator, prompts, Word export, folder naming, and the Case Guide / Case Summary / Describe the Case / Review Core Document UI. Test projects were skimmed for gaps.

Solid parts: atomic temp-file + rename writes with a writer lock and sequence-based coalescing; `SaveNow` on close; size/version-capped, never-throwing load; code-owned IDs that only grow; schema-ordered OpenXml (table/column widths sum to the page width, `sectPr` last, CT_PPr/CT_Style ordering correct); folder names confined to the root.

Two blockers:
1. D-01 cannot be met. No guide prompt includes the scenario text, so names that appear only in the scenario (the user's example: Oscar Martinez, David Wallace) get replaced by invented names.
2. If the user cancels during issue details, or a fatal error stops them, the issue details stay `Pending` with no way back. Nothing retries them, nothing shows the gap, and the validator reports "No problems found" while every issue is missing its `RelevanceCriteria`.

The warnings are mostly about lost work and link integrity:
- Closing the window during a run drops finished units of the part being written.
- Grid-cell edits are applied later through `BeginInvoke` and get silently dropped when a run starts. They are also not saved when the window closes.
- The core document's positional party and count IDs, combined with "sync in place", rebind the cast and issues to different entities after Regenerate Document.
- A cross-volume folder move can leave a split case.

## Critical Issues

### CR-01: D-01 broken: the scenario is never sent to any guide prompt, so names that appear only in the scenario cannot be kept

**File:** `Services/Case/CaseGuidePrompts.cs:13, 22-43, 82-132` (and every `Build*Prompt`)
**Issue:** The system prompt tells the model to "Keep each person, company and organization named in the scenario or the document exactly as named", and `AppendNameRules` adds "Keep every name the document uses". But no builder includes `draft.Scenario` / `draft.EffectiveScenario`. A grep of `CaseGuidePrompts.cs` and `CaseGuideGenerator.cs` finds no use of `Scenario`. The roster, background, facts and entry prompts only see the core document. Any character the user named in the scenario (CONTEXT "Specific Ideas": Michael Scott, Ryan Howard, Oscar Martinez, David Wallace) but the complaint did not name will be invented under a new name. That is the opposite of D-01 ("Names the scenario gives are kept exactly... Invent names only for people/orgs the core document does not name"). `CaseGuidePromptInvariantTests` only checks that the scenario is *absent* from the system prompt. No test checks that it reaches the user prompts.
**Fix:** Append the effective (substituted) scenario to the roster prompt, and also to the background, counsel top-up and cast-entry prompts, with an explicit instruction:
```csharp
// CaseGuidePrompts.BuildRosterPrompt, before "Document:"
if (!string.IsNullOrWhiteSpace(draft.EffectiveScenario))
{
    sb.AppendLine("Scenario from the user (keep every person and company it names, exactly as written, including names from films, TV series, books or games):");
    sb.AppendLine(Cap(draft.EffectiveScenario, 8_000));
    sb.AppendLine();
}
```
Add a prompt-invariant test asserting that the roster prompt contains `EffectiveScenario` and that a scenario-only name reaches the request.

### CR-02: Issue details left `Pending` after a cancel or fatal error can never be written, and the validator reports nothing

**File:** `Services/Case/CaseGuideGenerator.cs:64-65, 283-299`; `UserControls/IssuesTabPanel.cs:62`; `Services/Case/CaseGuideValidator.cs:587-588`
**Issue:** `GenerateIssuesAsync` marks `IssuesState` `Ready` before enrichment. When the user cancels or a fatal error hits during `EnrichIssuesAsync`, `IssueDetailsState` is set to `Pending` (lines 286, 297; tests `Issues_UserCancel` and `Issues_Fatal` lock this in). After that:
- `GenerateAsync` / Resume Guide skips Issues because `IssuesState == Ready` (line 64), and `UnfinishedParts` only looks at the four main parts.
- The Retry Issue Details button is shown only for `Failed` (`IssuesTabPanel.cs:62`).
- The validator warns only for `Failed` (`CaseGuideValidator.cs:587`).
- `UpdateStaleAsync` re-runs issues only when the core document changes.

Cancelling once during the first step therefore leaves every issue without Description, Elements and `RelevanceCriteria` (which "feeds the answer key"). The guide then passes Checks with "No problems found". The same happens when a crash leaves the state `Generating`.
**Fix:** Treat any non-`Ready` details state as unfinished. Resume should re-enrich, and the button and validator should cover `Pending`/`Generating` when issues lack details:
```csharp
// GenerateAsync, before the part loop
if (guide.IssuesState.Status == GuidePartStatus.Ready && guide.Issues.Any(i => !i.HasDetails))
{
    var r = new GuidePartResult { Part = GuidePart.Issues };
    await EnrichIssuesAsync(draft, r, progress, ct);   // records fatal/notes as usual
}
// IssuesTabPanel: Visible = guide.Issues.Any(i => !i.HasDetails) && !running
// Validator: if (ctx.Issues.Any(i => !i.HasDetails)) Warning("The issue details were not written. Click Retry Issue Details.")
```

## Warnings

### WR-01: Closing during a run loses paid-for units of the part being written (D-10)

**File:** `Forms/WizardForm.cs` (`OnFormClosing` → `StopActiveRun` → `SaveNow`); `Services/Case/CaseGuideGenerator.cs:766-786, 859-883, 1163-1173`; `UserControls/StepCaseGuide.Run.cs:216-223, 249-265`
**Issue:** The cast details, background staff and fact sections are merged into the draft only in the `finally` that runs after `Task.WhenAll` resumes. `StopActiveRun` disposes the step, which cancels the token, and then `SaveNow` runs synchronously on the same UI turn. The continuations that would merge finished batches have not run yet, so they are not in the snapshot. Once they do run, `partCompleted` and `RunAsync`'s final save both bail out on `IsDisposed`. Closing while (say) 5 of 6 fact sections are back throws away 5 paid Story calls. D-10 says "A crash/close never loses paid-for work". The C12 copy ("Finished parts are saved") hides that finished *units* are lost.
**Fix:** Merge each unit as soon as its task completes, on the calling context, and record `CompletedUnits` there instead of in one final `finally`. For example, loop `await Task.WhenAny(...)` and merge each finished task right away. Alternatively, have `StopActiveRun` cancel and then await the step's run task (with a short timeout, pumping messages) before calling `SaveNow`.

### WR-02: Grid-cell edits are applied later and silently dropped (run start, window close, Next validation)

**File:** `UserControls/CastTabPanel.cs:278-297`, `UserControls/FactsTabPanel.cs:333-352`, `UserControls/ValuesTabPanel.cs:275-280`; `UserControls/GuideTabPanel.cs:559-561`
**Issue:** `CellEndEdit` posts `CommitGridEdit` with `BeginInvoke`, and `CommitGridEdit` returns early when `Host.IsBusy`. This causes three problems:
1. Edit a Name or Fact cell, then click Regenerate {Entry}, Update Guide or Resume Guide directly. The button click ends the edit and queues the commit. The click handler then calls `SetBusy(true)` before the queued commit runs, so the commit is dropped. The grid shows the typed text until the next rebuild, and the text is never stored.
2. `CommitPendingEdits` (used by `OnLeaveStepAsync` and the close flush) commits only the detail text boxes. A cell still in edit mode when the window's X is clicked is never committed, because the X click does not move focus, so the edit is lost.
3. Next: `ValidateStepAsync` runs `RefreshAll` (which rebuilds rows with the old value) and validates **before** the queued commit applies. The user gets blocked by an error they just fixed, or passes the gate on stale data.

**Fix:** Commit grid edits synchronously inside the shared commit path, and only defer the *row rebuild*:
```csharp
public override void CommitPendingEdits()
{
    if (Grid.IsCurrentCellInEditMode) Grid.EndEdit();   // fires CellEndEdit
    FlushQueuedGridEdits();                              // apply queued (id, field, text) now, ignoring Busy for user-typed input
    ...existing detail commits...
}
```
Keep a small queue in `CellEndEdit` rather than posting the commit itself, and call `CommitPendingEdits` from `RunAsync`/`RegenerateEntryAsync` *before* `SetBusy(true)` (`RegenerateEntryAsync` currently never calls it).

### WR-03: Regenerate Document rebinds organizations and issues to different entities (positional `party-N` / `count-N` IDs)

**File:** `Services/Case/CoreDocumentGenerator.cs:578, 596, 617`; `Services/Case/CaseGuideGenerator.cs:391-426`; `Services/Case/IssueExtractor.cs:59-73`
**Issue:** Party, count and fact-section IDs are positional (`party-{i+1}`). `SyncOrganizations` matches organizations by `PartyId` and *renames them in place*, keeping Id, Domain and every cast member. When the core document is regenerated (C9 allows this with a guide present) and the parties come back in a different order, ORG-01's cast and `@dundermifflin.com` addresses are moved onto whatever party is now `party-1`. The validator's domain check compares against the unchanged `org.Domain`, and the name-mismatch check compares against the freshly synced name, so nothing is flagged. In the same way, IssueExtractor pass 3 keeps `ISS-01` for a retitled `count-1` and drops its details, while facts stay linked to it without `NeedsReview` even if the count is now a different claim.
**Fix:** In `SyncOrganizations`, match by normalized name first and fall back to `PartyId` only when the name also matches loosely. If a `PartyId` match has a different name, create a new organization rather than renaming one that has cast/domain attached (or at least set `NeedsReview` on the facts citing its cast and warn). In `IssueExtractor` pass 3, set `NeedsReview` with a reason on facts linked to a retitled issue.

### WR-04: `RegenerateValuesAsync` leaves `ValuesState` stuck in `Generating` on an unexpected exception

**File:** `Services/Case/CaseGuideGenerator.cs:2062-2104`
**Issue:** Unlike `GenerateValuesAsync` (line 1456), the regenerate path has no `catch (Exception)`. Any non-`LlmCallException` failure propagates with `state.Status == Generating`, for example a `JsonException`/`InvalidOperationException` from the gateway or the mapper, or an `ArgumentException` from `MergeEntries`. `RunAsync`'s `finally` then saves that state to `case.json`. As a result the Next gate reports Values "not written", `IsStale` ignores the part, and Resume Guide runs a full `GenerateValuesAsync` that merges a second registry into the existing one.
**Fix:**
```csharp
catch (Exception ex)
{
    result.FailureMessage = ex.Message;   // falls through to state.Status = previous
}
```

### WR-05: A cross-volume folder move can leave a split, half-deleted case

**File:** `Services/Case/CaseStore.cs:275-283`
**Issue:** On different roots, `CopyDirectory` then `Directory.Delete(source, recursive: true)` runs. If the delete fails partway, for example because an exported .docx is open in Word (the common case on this very screen), the method throws *after* deleting some source files. `draft.FolderPath` still points at the half-emptied source, and the next autosave recreates `case.json` there. The full copy sits orphaned at the target and is not in Recent cases. A failure partway through `CopyDirectory` leaves a partial target. The next attempt then goes to `"<name> (2)"`.
**Fix:** After a successful copy, switch `draft.FolderPath` to the target (and update recent) *before* deleting the source. Treat source deletion as best effort and report leftover files rather than throwing. On copy failure, delete the partial target.

### WR-06: `EnsureFolder` silently forks a case into a new folder when its folder is missing

**File:** `Services/Case/CaseStore.cs:82-91, 371-378`
**Issue:** If `draft.FolderPath` is set but the directory is temporarily gone (USB or network drive unplugged, OneDrive folder offline, user renamed it), the next autosave creates `Documents\ReelDiscovery\Cases\<name> (2)`, re-points `FolderPath`, and writes there. Nothing tells the user. The user-chosen location (D-09) is abandoned, exports and the later corpus are split across two folders, and the old entry drops out of Recent cases on the next load.
**Fix:** Allocate a folder only when `FolderPath` is empty. When it is set but missing, fail the save ("The case folder {path} is not available") so the existing "Not saved" UI and C10 close prompt handle it.

### WR-07: Regenerate Cast churns IDs of seeded people and drops their fact links

**File:** `Services/Case/CaseGuideGenerator.cs:1682-1686, 1735-1757, 633-694`
**Issue:** `MergeRoster` removes every unedited key character that the model left out of the response, through `RemoveCastMember`, which strips the character from facts and flags them. `SeedAuthor` then runs and re-adds the signing attorney or memo author when absent, with a **new** `CH-` ID. With an ordinary model omission, the attorney's `CH-00x` disappears, the facts that cited them lose actor/knower links, and a fresh ID appears. Background staff go through the same pattern via `RewriteBackgroundAsync`. This conflicts with "keep IDs of matched entries".
**Fix:** Run `SeedAuthor`'s match against the *pre-merge* cast and treat the author as claimed (never removed) in `MergeEntries`. More generally, before removing an unclaimed character, try to match it by full name against the response.

### WR-08: Cancelling a resumed Cast run skips the generator's autosave contract

**File:** `Services/Case/CaseGuideGenerator.cs:368-382, 122-138`
**Issue:** `changed |= await WriteDetailsAsync(...)` does not execute the assignment when `Task.WhenAll` throws on cancel, even though the `finally` already merged finished batches. On a resume (`resume == true`, no organizations added), `changed` stays false, so `Revision` is not bumped and `RunTrackedAsync` does not call `partCompleted`. This contradicts the documented contract ("awaited ... after a cancelled part that kept partial results"). It is currently masked only by `StepCaseGuide.RunAsync`'s unconditional final save. `WriteBackgroundAsync` has the same problem.
**Fix:** Have `WriteDetailsAsync`/`WriteBackgroundAsync` report merges through a field on `UnitRun` (or bump `draft.Revision` inside the merge `finally`) so the cancel path sees them.

### WR-09: Regenerated facts that the model drops silently lose their canonical-value links

**File:** `Services/Case/CaseGuideGenerator.cs:2009-2014`
**Issue:** `MergeRegeneratedFacts` removes unclaimed facts and strips their IDs from `value.FactIds`, but does not flag the values. `CaseGuideEditing.DeleteFact` does the same removal *and* sets `NeedsReview` with "{id} was deleted." (W3). After Regenerate Facts, values can end up linked to no fact, or with fewer links, without the user being told.
**Fix:** Call `CaseGuideEditing.FlagValues(guide, fact.Id, $"{fact.Id} was removed when the facts were regenerated.")` before stripping the ID.

### WR-10: Organizations for parties removed from the core document linger and drive counsel and preset checks

**File:** `Services/Case/CaseGuideGenerator.cs:391-426`; `Services/Case/CaseGuideValidator.cs:638-660`
**Issue:** `SyncOrganizations` only adds or refreshes. When an edit removes a party, its `Kind = Party` organization stays with a dangling `PartyId`. Rule 24 checks only parties without an organization and name mismatches, never organizations whose party is gone. The ghost organization keeps receiving counsel requirements in roster prompts (`BuildRosterPrompt` line 111), counsel top-ups, background staff, and validator counsel/preset warnings.
**Fix:** In `CheckParties`, warn when `org.PartyId` is non-empty and no party has that ID. In `SyncOrganizations`, report such organizations (or demote them to `ThirdParty`) so prompts stop asking for their counsel.

## Info

### IN-01: Reserved-name guard does not neutralize names with an extension

**File:** `Services/Case/CaseFolderNaming.cs:350-353`
**Issue:** For "CON. Inc" the base name is `CON` and the code appends " Case", giving "CON. Inc Case", whose base name is still `CON`.
**Fix:** Insert the suffix into the base name instead: `result = baseName + " Case" + result[baseName.Length..]`.

### IN-02: Export files are found by folder name, so a renaming move hides earlier exports

**File:** `UserControls/StepCaseSummary.cs:486-487`; `Services/Case/CaseStore.cs:273`
**Issue:** `ExportFileName` uses the folder name. A move that collides becomes "Name (2)", so earlier "Name - Core Document.docx" files show as "not exported". Choosing the current parent as the target also renames the folder to "(2)" for no reason.
**Fix:** Use `CaseFolderNaming.DefaultFolderName(draft)` (stable) for export names, and treat a target equal to the current parent as a no-op.

### IN-03: `MoveFolder` discards the queued snapshot, and waiting `SaveAsync` callers report success

**File:** `Services/Case/CaseStore.cs:286-290, 392-394`
**Issue:** A `SaveAsync` already waiting on the drain gets `_lastOutcome` (success) even though its snapshot was dropped. The current UI saves right after the move, so this is harmless today but fragile.
**Fix:** Re-target the pending snapshot to the new path instead of nulling it.

### IN-04: The seeded pleading attorney is always titled "Partner"

**File:** `Services/Case/CaseGuideGenerator.cs:657`
**Issue:** The signature block's attorney may be an associate. The hard-coded title can also wrongly satisfy the "partner" counsel rule.
**Fix:** Use the counsel title from the core document when available.

### IN-05: Unknown locations default to Eastern time

**File:** `Services/Case/UsTimeZones.cs:83-96`
**Issue:** "Los Angeles" or "Chicago" without a state falls back to `America/New_York`, which skews Phase 4 business hours.
**Fix:** Add a small major-city map, or return empty and let Phase 4 choose.

### IN-06: `RunTrackedAsync` can mask the original exception

**File:** `Services/Case/CaseGuideGenerator.cs:133-137`
**Issue:** If `partCompleted` throws inside `finally`, it replaces the user-cancel or failure exception.
**Fix:** Wrap the callback in try/catch and keep the original exception.

---

_Reviewed: 2026-10-01_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
