---
phase: 04-corpus-plan-answer-key
reviewed: 2026-10-03T00:00:00Z
depth: standard
files_reviewed: 53
files_reviewed_list:
  - Forms/ReplanDialog.cs
  - Forms/WizardForm.cs
  - Helpers/Apportion.cs
  - Helpers/FileLockDetector.cs
  - Helpers/PercentSlider.cs
  - Models/Case/CaseDraft.cs
  - Models/Case/CaseGuide.cs
  - Models/Case/CaseStage.cs
  - Models/Corpus/AnswerKeyRow.cs
  - Models/Corpus/CorpusPlan.cs
  - Models/Corpus/CorpusSettings.cs
  - Models/Corpus/CorpusState.cs
  - Models/Corpus/ExternalParty.cs
  - Models/Corpus/PlannedAttachment.cs
  - Models/Corpus/PlannedMessage.cs
  - Models/Corpus/PlannedThread.cs
  - Models/Corpus/ReviewProtocolProse.cs
  - Models/WizardState.cs
  - Services/Case/CaseGuideDocxExporter.cs
  - Services/Case/CaseGuidePrompts.cs
  - Services/Corpus/AnswerKeyBuilder.cs
  - Services/Corpus/AnswerKeyCsvWriter.cs
  - Services/Corpus/AnswerKeyXlsxWriter.cs
  - Services/Corpus/BudgetAllocator.cs
  - Services/Corpus/BusinessCalendar.cs
  - Services/Corpus/CorpusApproval.cs
  - Services/Corpus/CorpusPlanPrompts.cs
  - Services/Corpus/CorpusPlanStore.cs
  - Services/Corpus/CorpusPlanValidator.cs
  - Services/Corpus/CorpusPlanner.cs
  - Services/Corpus/DocIdPrefix.cs
  - Services/Corpus/GenerationCostEstimator.cs
  - Services/Corpus/NonRelevantSkeletonBuilder.cs
  - Services/Corpus/PlanCallRunner.cs
  - Services/Corpus/PlanNumbering.cs
  - Services/Corpus/PlanProgress.cs
  - Services/Corpus/PlanSummary.cs
  - Services/Corpus/RelevantThreadFiller.cs
  - Services/Corpus/ReviewProtocolBuilder.cs
  - Services/Corpus/SkeletonContext.cs
  - Services/Corpus/SubjectSeeder.cs
  - Services/Corpus/ThreadScheduler.cs
  - Services/Corpus/ThreadShapes.cs
  - Services/Corpus/ThreadSkeletonBuilder.cs
  - UserControls/CaseGuideUiText.cs
  - UserControls/CorpusUiText.cs
  - UserControls/MonthHistogram.cs
  - UserControls/StatTile.cs
  - UserControls/StepCaseScenario.cs
  - UserControls/StepCaseSummary.cs
  - UserControls/StepCorpusSettings.cs
  - UserControls/StepReviewPlan.Run.cs
  - UserControls/StepReviewPlan.cs
findings:
  critical: 2
  warning: 11
  info: 6
  total: 19
status: issues_found
---

# Phase 4: Code Review Report

**Reviewed:** 2026-10-03
**Depth:** standard
**Files Reviewed:** 53
**Status:** issues_found

## Summary

I reviewed the corpus planner pipeline (allocation, skeletons, scheduler, LLM filler and seeder, numbering, validator), plan persistence, the answer-key writers (CSV, XLSX, protocol DOCX), the approval state machine and the three new or changed wizard steps. For pre-existing files I reviewed only the Phase 4 diff (`c33f8e3^..HEAD`).

Several areas hold up well:
- The CSV writer guards formula injection on every field.
- The XLSX writer uses inline strings and strips XML-invalid characters, so LLM text cannot inject formulas or break the XML.
- Approval file paths are built only from the case folder plus constant names.
- The validator runs each rule in isolation.
- `PlanCallRunner`'s fan-out waits for every task before its `PlanCallRun` is disposed.

Two blockers:
1. **Crafted `case.json` crashes the app (CR-01).** A crafted or corrupted `case.json` crashes the app on Open Case. This breaks the explicit "never throws" contract on untrusted input.
2. **Auto re-plan on a reopened case (CR-02).** Reopening a case can start a paid re-planning run with no user action, and that run replaces the saved plan. This breaks the T-04-62 / D-25 rule that nothing plans or re-plans silently.

The warnings cover:
- **Data consistency:** the plan file is overwritten before `case.json` is updated, and a partial approval write leaves stamps that describe the wrong files.
- **Planning crashes:** edge-case guides (no active cast in the window, extreme fact dates, duplicate fact ids) make planning fail or crash.
- **Weak decoy enforcement** (D-14).
- **UI behaviour:** the summary loses track of written files, the Cancel button misleads, and the banner text promises behaviour the code does not deliver.

## Critical Issues

### CR-01: Integer overflow in `CorpusSettings.Normalize` crashes Open Case and violates the "never throws" load contract

**File:** `Models/Corpus/CorpusSettings.cs:205-210` (reached from `Services/Case/CaseStore.cs:217-218`, `Services/Corpus/CorpusPlanStore.cs:71-75`, called from `UserControls/StepCaseScenario.cs:858`)

**Issue:** `Normalize()` clamps each mix value only from below (`Math.Max(0, x)`) and then calls `mix.Sum()`. `Enumerable.Sum` over `int` uses checked arithmetic. So a `case.json` (or `corpus-plan.json`) with, for example, `"DailyBusinessPercent": 2147483647, "AncillaryPercent": 1` throws `OverflowException`.

Neither `CaseStore.Load` nor `CorpusPlanStore.Load` lists `OverflowException` in its catch filter. Both document themselves as "never throws", and T-04-01 treats `case.json` as untrusted. For `case.json`, the exception escapes `StepCaseScenario.OpenCase`, which is a click handler with no try/catch, and the app crashes. A crafted `corpus-plan.json` escapes `CorpusPlanStore.EnsureLoaded` the same way. That call sits in `StepReviewPlan.OnEnterStepAsync`, which runs from `async void ShowStep`, and in `StepCaseSummary.RefreshCorpusRows`.

**Fix:** Clamp each share into range before summing, and widen the catch filters as defense in depth:
```csharp
var mix = new[] { DailyBusinessPercent, AncillaryPercent, NewsletterPercent, DecoyPercent }
    .Select(x => Math.Clamp(x, 0, 100)).ToArray();
```
Also add `or OverflowException` to the catch filters in `CaseStore.Load` (line 223) and `CorpusPlanStore.Load` (line 81).

### CR-02: Opening a case at Review Plan auto-replans (billed LLM calls, plan replaced) despite `SuppressAutoPlanOnce`

**File:** `UserControls/CorpusUiText.cs:370-373`, `UserControls/StepReviewPlan.Run.cs:96, 107-110`, `UserControls/StepCaseScenario.cs:1289`

**Issue:** `SuppressAutoPlanOnce` is set when a case is opened from disk. Its stated purpose (T-04-62) is: "A case opened from disk never starts a planning run by itself". But `ReviewPlanEntryDecision` checks the flag only in the `!HasPlan` branch:
```csharp
if (!input.HasPlan)
    return input.SuppressAutoPlan ? ReviewPlanEntry.NoPlanCard : ReviewPlanEntry.AutoPlan;
if (!input.Approved && input.SettingsChanged)
    return ReviewPlanEntry.AutoReplan;   // ignores SuppressAutoPlan
```
Repro:
1. Build a plan and leave it unapproved.
2. Click Back to Corpus Settings and change the size or % relevant. The change is autosaved on leave.
3. Close the app without pressing Next.
4. Reopen the case. `OpenCase` calls `NavigateToStage(CaseStage.ReviewPlan)` (the furthest stage), and `OnEnterStepAsync` immediately runs `StartPlanningAsync`.

The result: Story-tier LLM calls are billed with no user action, and the saved plan is replaced. This breaks T-04-62 and D-25 ("Nothing re-plans silently").

**Fix:** Respect the flag on the re-plan path too and show the plan with the settings banner instead:
```csharp
if (!input.Approved && input.SettingsChanged)
    return input.SuppressAutoPlan ? ReviewPlanEntry.SettingsBanner : ReviewPlanEntry.AutoReplan;
```
Also make `ShowCurrentPlan` show the SettingsChanged banner for unapproved plans in this case. Add a test for `HasPlan && !Approved && SettingsChanged && SuppressAutoPlan`.

## Warnings

### WR-01: Re-planning overwrites the only `corpus-plan.json` before `case.json` points at it, so the previous plan is lost in the crash/save-failure window

**File:** `Services/Corpus/CorpusApproval.cs:128-160`, `Services/Corpus/CorpusPlanStore.cs:47-48, 76-77`, `UserControls/StepReviewPlan.Run.cs:277-290`

**Issue:** The plan file name is fixed. `ApplyNewPlanAsync` atomically replaces `corpus-plan.json` with the new plan (new `PlanId`), and only afterwards is `case.json` saved. If the process dies, or `SaveAsync` fails (only a status line is shown), between the two writes, `case.json` still holds the old `PlanId` while the file holds the new plan. `Load` then refuses it ("The saved plan does not match this case").

The previous plan, possibly approved and with `answer-key.csv`/`.xlsx` on disk, is gone with no recovery. The class comment says the write order protects against a crash, but it only protects against a *missing* file, not a *replaced* one.

**Fix:** Write the plan to a per-plan file (for example `corpus-plan-{PlanId:N}.json`), save `case.json`, and only then delete older plan files. Alternatively, keep `corpus-plan.json.prev` and fall back to it in `Load` when the `PlanId` does not match.

### WR-02: A partial approval write leaves new-plan files on disk while `WrittenFiles` stamps them with the old plan

**File:** `Services/Corpus/CorpusApproval.cs:52-92`, `UserControls/StepCaseSummary.cs` (`FileStatusFor`)

**Issue:** `WriteFilesAsync` writes CSV, then XLSX, then DOCX, and returns at the first failure without recording anything. A common case is Word holding the protocol open. By then `answer-key.csv` and `answer-key.xlsx` have already been overwritten with the new plan's rows, but `draft.Corpus.WrittenFiles` still holds the previous entries.

`StepCaseSummary.FileStatusFor` therefore reports the answer key as "written for an earlier plan" (or "Written" for a stale stamp), even though the file contents now belong to the unapproved current plan. Reviewers can be handed an answer key that does not match the approved plan.

**Fix:** Build all three byte arrays, write them to temp names in the case folder, and move them into place only after all three temp writes succeed. Alternatively, update `WrittenFiles` for each file that was written even when a later file fails.

### WR-03: The non-relevant skeleton indexes empty sender lists when no cast member is active in the window

**File:** `Services/Corpus/NonRelevantSkeletonBuilder.cs:126, 166-171, 247-254, 307-309, 867-877`

**Issue:** The guard at line 126 checks only `ctx.CastById.Count == 0` and an empty `BusinessDays` list. If every cast member's ActiveFrom/ActiveTo range misses the window, the `senders` lists filtered by `Eligible(...).Count > 0` are empty.

- `BuildDaily`: `WeightedIndex` returns `-1` (`random.Next(0) == 0`, the loop never runs), so `senders[-1]` throws.
- `BuildAncillary` and the decoy fallback: `senders[ctx.Random.Next(0)]` reads index 0 of an empty list and throws.

Planning then fails with "Index was out of range" instead of a useful message. Active ranges come from the LLM-written guide, so this can happen in practice.

**Fix:** When the eligible list is empty, fall back to `ctx.CastById.Keys` (as `BuildBulk` already does at line 413), or skip the category with a `ctx.Notes` entry. Make `WeightedIndex` return 0 (or throw a clear error) when `weights.Sum() <= 0`.

### WR-04: `DefaultWindow` throws on fact dates at the edges of the `DateTime` range

**File:** `Models/Corpus/CorpusSettings.cs:116-123` (called from `UserControls/StepCorpusSettings.cs:1020-1027, 1382`, `Services/Corpus/SkeletonContext.cs:129, 140-149`, `Services/Corpus/CorpusPlanner.cs:59`)

**Issue:** `CaseDraft.TryParseDate` accepts any `yyyy-MM-dd`. A key fact dated `0001-01-15` makes `First.AddMonths(-1)` throw `ArgumentOutOfRangeException`, and `9999-11-20` does the same through `Last.AddMonths(2)`. In `StepCorpusSettings.EnterCore`, called from `OnEnterStepAsync` → `async void ShowStep`, this is unhandled and crashes the wizard. The guide and `case.json` are untrusted, and the LLM or the user can enter such a date.

**Fix:** Clamp in `FactRange`/`DefaultWindow` (ignore facts outside, say, years 1900-2200), or guard the `AddMonths` calls:
```csharp
static DateTime SafeAddMonths(DateTime d, int m) =>
    (m < 0 && d < DateTime.MinValue.AddMonths(-m)) || (m > 0 && d > DateTime.MaxValue.AddMonths(-m)) ? d : d.AddMonths(m);
```

### WR-05: `PlanSummary` converts untrusted `SentAt` values without the edge guard the validator uses

**File:** `Services/Corpus/PlanSummary.cs:145-150` (called from `UserControls/StepReviewPlan.cs:891`, `UserControls/StepCaseSummary.cs` `RefreshCorpusRows`)

**Issue:** `CorpusPlanValidator.Local` deliberately returns null for instants near `DateTimeOffset.MinValue`/`MaxValue`, because `TimeZoneInfo.ConvertTime` throws there. `PlanSummary.MonthOf` calls `BusinessCalendar.LocalDate`, which uses `ConvertTime`, with no such guard. A tampered `corpus-plan.json` (for example `"SentAt":"0001-01-01T01:00:00+00:00"` with an America/* zone id) throws `ArgumentOutOfRangeException` from `ShowPlan` and `RefreshCorpusRows`, neither of which has a try/catch. `CorpusPlan.Normalize` does not range-check `SentAt`.

**Fix:** In `CorpusPlan.Normalize`, drop or clamp messages whose `SentAt.UtcDateTime.Year` falls outside a sane range. Alternatively, reuse the validator's `Local` guard in `PlanSummary.MonthOf`.

### WR-06: Decoy prompts include the full case scenario, and the must-not-mention post-check is effectively unenforced (D-14)

**File:** `Services/Corpus/SubjectSeeder.cs:504, 338-339, 439-460`

**Issue:** D-14 says decoys "must not be about the alleged conduct". `BuildDecoyPrompt` calls `CaseGuidePrompts.AppendNameRules(sb, draft)` with the default `includeScenario: true`, so the user's whole scenario narrative is in the prompt. The everyday prompt explicitly passes `includeScenario: false`.

The only code-side enforcement is `MentionsForbidden`, a substring match against whole fact titles (for example "Brightline submits change order CO-14 for $48,500"). A model-written subject almost never contains the full title, so decoys that paraphrase the conduct pass and are coded Not Relevant in the answer key. That puts wrong labels in the answer key.

**Fix:** Pass `includeScenario: false` to the decoy prompt. Build the must-not-mention list from distinctive tokens: values, change-order numbers, amounts, and key noun phrases from fact titles, rather than whole titles. Consider a word-overlap threshold against each fact title.

### WR-07: Guidance weights crash planning when the guide has duplicate or case-variant fact ids

**File:** `Services/Corpus/CorpusPlanner.cs:456-459`

**Issue:** `ToDictionary(id => RelevantThreadFiller.CanonicalId(id), ...)` throws `ArgumentException` when two facts share a canonical id (for example `F-003` twice in a tampered `case.json`, or `f-003` vs `F-003`). The exception is not an `LlmCallException`, so it escapes `GuidanceWeightsAsync`, and every Re-plan with guidance fails with "An item with the same key has already been added". Everywhere else, the planner treats duplicate ids as first-wins.

**Fix:**
```csharp
var known = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var f in guide.Facts.Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id)))
    known.TryAdd(RelevantThreadFiller.CanonicalId(f.Id), f.Id);
```

### WR-08: The review protocol file name depends on the case folder name, so moving or renaming the folder orphans it

**File:** `Services/Corpus/CorpusApproval.cs:44, 56`, `Services/Corpus/ReviewProtocolBuilder.cs:114`, `UserControls/StepCaseSummary.cs` (`CorpusFileName`)

**Issue:** The protocol is written as `"{folderName} - Review Protocol.docx"`. `CaseStore.MoveFolder` uses `UniqueFolderPath`, which can rename the leaf (for example `Case (2)`), and users can rename folders in Explorer. After either, `CorpusFileExists(ReviewProtocol)` looks for the new name. The summary then shows "Not written" and hides Open, and re-approving writes a second protocol next to the orphaned one.

**Fix:** Use a constant file name (for example `review-protocol.docx`). Alternatively, validate `WrittenFiles[...].FileName` (no path separators, must end with ` - Review Protocol.docx`) and use it when that file exists.

### WR-09: Cancel stays enabled after the planner returns; confirming "discard" then has no effect

**File:** `UserControls/StepReviewPlan.Run.cs:268-297, 406-425`

**Issue:** Once `PlanAsync` returns successfully, the code awaits `ApplyNewPlanAsync` and then `SaveAsync`, both without a token, while `_btnCancel` is still visible and enabled. A user who clicks Cancel and confirms "Stop planning? The partial plan is discarded." gets the new plan saved and shown as ready anyway. If a previous approved plan existed, it is replaced against the user's explicit choice.

**Fix:** Set `_btnCancel.Enabled = false` (and `_isPlanning`-style gating) as soon as `PlanAsync` returns, before applying the plan. Alternatively, check `_cts.IsCancellationRequested` before `ApplyNewPlanAsync` and take the cancelled path.

### WR-10: Loaded plan paths and names are trusted without validation

**File:** `Models/Corpus/CorpusPlan.cs:107-130`

**Issue:** `Normalize` only null-coalesces `message.FilePath`, `message.DocId`, `attachment.FileName` and `attachment.DocId`. A tampered `corpus-plan.json` can carry `"FilePath": "..\\..\\Users\\x\\AppData\\...\\evil.eml"`. That value goes into the answer key's File Path column unchanged and, per D-29, becomes the "deterministic planned relative file path" Phase 5 writes to. `PlanNumbering` builds safe paths, but nothing re-establishes that invariant for a loaded plan.

**Fix:** In `Normalize` (or `CorpusPlanStore.Load`), reject or regenerate any `FilePath` that is rooted, contains `..`, or does not start with `Mailboxes/`. Sanitize attachment `FileName` with `RelevantThreadFiller.FileNameFor`-style rules. Simplest: re-run `PlanNumbering.Assign(plan, guide)` on load, or refuse the plan with a reason.

### WR-11: Keep This Plan does not restore the approval that the stale banner withdrew, despite the banner text

**File:** `Services/Corpus/CorpusApproval.cs:189-204`, `UserControls/CorpusUiText.cs:400`, `UserControls/StepReviewPlan.Run.cs:111-120, 447-465`

**Issue:** The banner says "Approval is withdrawn until you re-plan or keep this plan." `WithdrawApprovalIfStale` sets `Approved = false`, but `KeepStalePlan` only records the dismissed hash and never restores `Approved`. After Keep This Plan, the Next gate asks the user to approve again and rewrites all three files. The UI text promises behaviour the code does not implement.

**Fix:** Either restore approval in `KeepStalePlan` when it was withdrawn by staleness, using a `WithdrawnForStaleHash` field on `CorpusState`, or change the banner text to "Approve the plan again after keeping it."

## Info

### IN-01: The timeout branch in `PlanCallRunner` is dead code

**File:** `Services/Corpus/PlanCallRunner.cs:107-111`
**Issue:** Both text providers map HTTP timeouts to `Outcome = Failed`, as documented in `ILlmProvider.cs:15`. `GenerateAsync` throws `OperationCanceledException` only on token cancel, so the "request timed out" branch never runs and makes the error handling look different from how it actually behaves.
**Fix:** Remove the branch or add a comment explaining why it stays.

### IN-02: `ClampWindow.EndBeforeStart` is computed from the pre-clamp dates

**File:** `Models/Corpus/CorpusSettings.cs:147-148`
**Issue:** After clamping, a window flagged `EndBeforeStart` can be valid (start clamped down, end clamped up). The UI recomputes this itself, so the flag is stale and unused.
**Fix:** Compute it after clamping, or remove it.

### IN-03: The validator's DocID "in order" rule only checks set membership

**File:** `Services/Corpus/CorpusPlanValidator.cs:504-511`
**Issue:** The rule never compares each message's DocID to its chronological rank, so swapped DocIDs pass the check despite the message "not unique and in order".
**Fix:** Compare `entry.Message.DocId` to `DocId(prefix, rank + 1)` while iterating in `SentAt` order.

### IN-04: One-letter DocID prefixes are rejected by the UI but accepted from `case.json`

**File:** `Models/Corpus/CorpusSettings.cs:229-244`, `UserControls/StepCorpusSettings.cs:1499-1505`
**Issue:** `SanitizePrefix`/`Normalize` keep a one-letter prefix, and `DocIdPrefix.Effective` uses it. The UI refuses to commit one, and an invalid prefix silently does not block Next.
**Fix:** Treat length 1 as empty in `SanitizePrefix`. Optionally block Next while the prefix field is invalid.

### IN-05: "By Custodian" statistics group by display name

**File:** `Services/Corpus/AnswerKeyXlsxWriter.cs:170-178`
**Issue:** Two cast members with the same full name are merged into one row.
**Fix:** Group by cast id (add a hidden CustodianId to `AnswerKeyRow`) and display the name.

### IN-06: Misleading attachment check message for file-name problems

**File:** `Services/Corpus/CorpusPlanValidator.cs:464-473`
**Issue:** A missing or unsafe file name is reported as "an attachment the settings do not allow (no file name)".
**Fix:** Use separate messages for disallowed type, calendar invite on a reply, and invalid file name.

---

_Reviewed: 2026-10-03_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
