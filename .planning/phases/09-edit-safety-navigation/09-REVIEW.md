---
phase: 09-edit-safety-navigation
reviewed: 2026-10-12T00:00:00Z
depth: standard
files_reviewed: 24
files_reviewed_list:
  - Forms/QaFindingsDialog.cs
  - Models/Discovery/DiscoveryCorpus.cs
  - Models/Discovery/DiscoveryDocument.cs
  - Models/WizardState.cs
  - Services/Corpus/CorpusApproval.cs
  - Services/Discovery/CorpusDetector.cs
  - Services/Discovery/DiscoveryStaleness.cs
  - UserControls/CaseGuideUiText.cs
  - UserControls/CorpusUiText.cs
  - UserControls/DiscoveryStepModel.cs
  - UserControls/DiscoveryUiText.cs
  - UserControls/GenerationUiText.cs
  - UserControls/QaUiText.cs
  - UserControls/StepCaseGuide.Run.cs
  - UserControls/StepCaseGuide.cs
  - UserControls/StepCaseScenario.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepCorpusSettings.cs
  - UserControls/StepDiscoveryDocuments.cs
  - UserControls/StepGenerateCorpus.Run.cs
  - UserControls/StepGenerateCorpus.cs
  - UserControls/StepReviewPlan.Run.cs
  - UserControls/StepReviewPlan.cs
  - ./CLAUDE.md (context only)
findings:
  critical: 1
  warning: 3
  info: 6
  total: 10
status: issues_found
---

# Phase 9: Code Review Report

**Reviewed:** 2026-10-12
**Depth:** standard
**Files Reviewed:** 23 production files (diff `296fbb2..HEAD`)
**Status:** issues_found

## Summary

I reviewed the phase-9 diff against the six priority areas. Here is what I found for each:

- **Stale guide feeding a run:** every way a run can start (Generate Emails, Continue, Retry Failed, the banner button) goes through `RunCoreAsync`, and the new guard there is correct. However, the Review Plan **Next** gate can still approve a stale plan without the user ever clicking Keep This Plan. `RecordApproval` then quietly records the stale guide as kept, so the next run writes from the stale plan (CR-01).
- **Card action reaching StartOverAsync by accident:** none found. `ClickStateActionAsync` now names `PlanChanged` and `Unreadable` explicitly, and any other card does nothing. `_cardKind` starts as `Ready`, which routes to the guarded `StartRunAsync`, not to `StartOverAsync`.
- **Unrequested paid planning:** none found. `PlanOnEntryOnce` is set only by a successful Corpus Settings Next. Review Plan clears it on every entry, before the `_isPlanning` early return. Skip to Discovery Documents and Back/Forward never set it, and reopening a case clears it. The one weak spot is that the Case Guide banner's Review Plan button skips the guide's Next gate (WR-01).
- **Null vs empty stamps:** a null stamp (unknown) is handled the same way in `CorpusDetector` and `StepCaseSummary.LoadQaWithDiscovery`. One problem remains: a journal that is missing for any reason is treated as "Start Over happened", which can mark documents Out of date when they are not (WR-02).
- **Case Guide banner buttons:** Review Facts and Mark All Reviewed now show only for `NeedsReview`. Review Plan shows only for `PlanDependency`. `_bannerPrimaryShown` and `FixButtonFor` are unaffected. This change is correct.
- **UI-thread blocking:** none found. The new journal and sidecar reads (`StartOverCitesDiscovery`, `EmailStamps`, `DiscoveryCitedDocIds`) run inside `Task.Run` or in the off-thread `LoadQaWithDiscovery`. The Case Guide banner reads only cached counts and a hash computed in memory.

## Critical Issues

### CR-01: Review Plan Next approves a stale-guide plan, bypassing the disabled Approve button and Keep This Plan

**File:** `UserControls/StepReviewPlan.Run.cs:150-171` (with `UserControls/CorpusUiText.cs:621-646`, `Services/Corpus/CorpusApproval.cs:97-109`, `UserControls/StepReviewPlan.cs:1198`)

**Issue:** When the guide is stale, Review Plan disables the Approve button on purpose (`_btnApprove.Enabled = _bannerVariant != PlanBannerVariant.StaleGuide`). The user has to choose Re-plan or Keep This Plan first. The Next gate does not check for this:

1. `PlanNextGate(running, hasPlan, approved)` has no stale-guide input.
2. For an unapproved plan it returns `AskApprove`, with a message that says nothing about the guide having changed.
3. On Yes, it calls `ApproveAsync()`.
4. `RecordApproval` then sets `DismissedStaleGuideHash = current`, because `plan.GuideHash != current`. The stale guide is now silently recorded as kept, and `IsGuideStale` returns false.

Phase 9 now sends users down this exact path. The new Guide-changed card on Generate Corpus links to Review Plan. The user clicks Next, answers Yes to "Approve this plan and continue?" (Yes is the default), and lands on a Ready card. Generate Emails then writes the corpus from a plan built from the old guide. This defeats W1/D-25 ("Generate Corpus will not write emails from this plan until you review it").

**Fix:** Give the gate the stale state, and do not approve while the guide is stale and not kept:
```csharp
// CorpusUiText
public static PlanNextGateResult PlanNextGate(bool running, bool hasPlan, bool approved, bool guideStale = false)
{
    ...
    if (!approved && guideStale)
        return new PlanNextGateResult
        {
            Kind = PlanNextGateKind.Blocked,   // new kind: OK-only message
            Title = "Plan Out of Date",
            Message = "The case guide changed after this plan was built. Click Re-plan... or Keep This Plan before approving."
        };
    ...
}

// StepReviewPlan.ValidateStepAsync
var gate = CorpusUiText.PlanNextGate(_isPlanning || _isApproving, corpus?.Plan != null, corpus?.Approved == true,
    CorpusApproval.IsGuideStale(Draft));
case PlanNextGateKind.Blocked:
    MessageBox.Show(FindForm(), gate.Message, gate.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return false;
```
Another option: `ApproveAsync` returns false (and sets a status) when `CorpusApproval.IsGuideStale(Draft)` is true. That covers every caller. Add a test: a stale plan plus Next with Yes must leave `Approved == false`.

## Warnings

### WR-01: The Case Guide banner's Review Plan button skips the guide's Next gate

**File:** `UserControls/StepCaseGuide.cs:558`

**Issue:** `_btnBannerReviewPlan.Click` calls `NavigateToStage(CaseStage.ReviewPlan)`. `ShowStep` runs `OnLeaveStepAsync` (commit and save) but not `ValidateStepAsync`. So the guide checks that normally block Next are skipped:
- unfinished parts (M1)
- validation errors (M2, Fix Automatically / Continue Anyway)
- warnings (M3)

From Review Plan the user can then start a paid Re-plan, or Keep and approve, using a guide the wizard would otherwise reject. The banner appears exactly when the guide has just been edited, which is when unfinished parts or errors are most likely.

**Fix:** Run the same gate before jumping:
```csharp
_btnBannerReviewPlan.Click += (s, e) =>
{
    if (_isGenerating) return;
    CommitAllEdits();
    RefreshAll();
    if (!EvaluateNextGate()) return;
    (FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan);
};
```

### WR-02: Any missing journal is treated as "every cited email deleted", so documents can be falsely marked Out of date

**File:** `Services/Discovery/CorpusDetector.cs:34-40`, `UserControls/StepCaseSummary.cs:1817-1822`

**Issue:** Both callers turn `!replay.Exists` into an empty stamp set, meaning "known to have no emails". `GenerationJournal.Replay` reports `Exists = false` in several cases:
- Start Over deleted the journal (the only intended trigger)
- `File.Exists(path)` returned false, for example on an access-denied file or an antivirus or sync lock during the probe
- the user moved or cleaned up the journal by hand

In every case except Start Over, all discovery documents that cite emails switch to "Out of date: an email it cites was regenerated or deleted". The `.eml` files are still there. That message invites a paid Regenerate. The priority rule says stamps must not falsely mark documents Out of date.

**Fix:** Confirm the deletion before claiming "known-empty". Two options:
- Record a positive marker at Start Over (for example `Draft.Corpus.Generation.StartedOverUtc`, or `EmailsWritten == 0` after `StartedOverText`) and treat a missing journal as empty only when that marker is present. Otherwise return null (unknown).
- Also require that the Mailboxes folder has no `.eml` files for the cited DocIds.

Put this logic in one helper shared by `CorpusDetector` and `LoadQaWithDiscovery`, so the two cannot drift apart.

### WR-03: The Guide-changed entry skips crash recovery, so the card shows a stale email count

**File:** `UserControls/StepGenerateCorpus.Run.cs:148-149, 220-229`

**Issue:** `if (plan != null && input.Approved && !input.GuideStale)` now skips `ReadEntryAsync` whenever the guide is stale. That means:
- `GenerationStateApplier.Apply` (journal-is-truth crash recovery, D-05) never runs.
- `_pendingProviderBatches` stays 0.

The Guide-changed card body ("The N emails already written stay in place"), and the Case Guide dependency line and banner, read `Draft.Corpus.Generation.EmailsWritten`. After an interrupted or crashed run that cached value is behind the journal, so both screens show the wrong count. Nothing re-syncs the count until the user approves again.

**Fix:** Still replay the journal (it is read-only and runs off the UI thread) when the plan exists and the guide is stale, and apply the recovered state before choosing the card. Only the Ready, Partial and Finished branches need to be gated on approval:
```csharp
if (plan != null && (input.Approved || input.GuideStale)) { ... data = await Task.Run(...); Apply(...); }
```

## Info

### IN-01: The "one place" withdrawal helper is not the only place

**File:** `Services/Corpus/CorpusApproval.cs:221-233`, `UserControls/StepReviewPlan.Run.cs:116-118`

**Issue:** The doc comment says `WithdrawIfGuideChanged` is "The one place a guide edit withdraws plan approval". Review Plan's StaleBanner branch still calls `WithdrawApprovalIfStale` and then `Draft.Revision++` by hand.

**Fix:** Use `CorpusApproval.WithdrawIfGuideChanged(Draft)` there as well.

### IN-02: Dead conditional in Corpus Settings Next

**File:** `UserControls/StepCorpusSettings.cs:1081-1082`

**Issue:** `if (RequestsPlanning(true, datesOk: true))` is always true, because the real checks already returned earlier in `ValidateFields`. `RequestsPlanning` exists only for tests.

**Fix:** Assign `_state.PlanOnEntryOnce = true;` directly, or pass the real validation results.

### IN-03: ToolTip component is never disposed

**File:** `UserControls/StepCorpusSettings.cs:378`

**Issue:** `_toolTip = new ToolTip()` is not owned by a container and is not disposed in `Dispose(bool)`.

**Fix:** Dispose it in `Dispose(bool)`, or create it with `new ToolTip(components)`.

### IN-04: Duplicate stamp logic

**File:** `Services/Discovery/DiscoveryStaleness.cs:39-52`

**Issue:** `EmailStamps` copies the first loop of `BlindReviewer.WrittenStamps` (`Services/Qa/BlindReviewer.cs:81-93`). The difference is intentional (attachments are excluded), but the shared ThreadDone walk is now written twice.

**Fix:** Have `BlindReviewer.WrittenStamps` start from `DiscoveryStaleness.EmailStamps(replay)` and then layer the attachment pass on top.

### IN-05: "No corpus plan yet" is shown when a plan exists but failed to load

**File:** `UserControls/StepGenerateCorpus.Run.cs:233-235, 422-428`

**Issue:** `hasPlan: plan != null` uses the loaded plan. A case whose `Corpus.HasPlan` is true but whose plan file did not load gets the "No corpus plan yet / write discovery documents without emails" card instead of an unreadable-plan message.

**Fix:** Pass `hasPlan: Draft.Corpus?.HasPlan == true`. Review Plan already shows the unreadable card.

### IN-06: The dependency Info line says "built from this guide" for a kept plan

**File:** `UserControls/CaseGuideUiText.cs:362-371`, `UserControls/StepCaseGuide.cs:815-817`

**Issue:** `IsGuideStale` is false once the user keeps the plan, so `DependencyNotice` reports "The approved corpus plan ... were built from this guide". The plan was actually built from an earlier guide.

**Fix:** Pass a `kept` flag (`DismissedStaleGuideHash == current hash`) and word the line to match, for example "The approved corpus plan was kept for this guide...".

---

_Reviewed: 2026-10-12_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
