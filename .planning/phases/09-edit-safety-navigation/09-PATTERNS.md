# Phase 9: Edit Safety & Navigation - Pattern Map

**Mapped:** 2026-10-11
**Files analyzed:** 24 (17 production, 7+ test groups)
**Analogs found:** 24 / 24. This is a gap-closure phase: every change extends an existing mechanism, and each one has an in-file or sibling precedent.

All paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\`. Line numbers are as read on 2026-10-11.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Services/Corpus/CorpusApproval.cs` (optional `IsApprovedForCurrentGuide`) | service (static transitions) | transform | same file: `IsGuideStale` / `WithdrawApprovalIfStale` (l.190-220) | exact |
| `UserControls/StepCaseGuide.Run.cs` (`SaveAsync` withdrawal) | step glue | CRUD (save funnel) | `StepReviewPlan.Run.cs` l.111-121 (StaleBanner withdrawal) | exact |
| `UserControls/StepCaseScenario.cs` (reopen withdrawal; `MarkOpenedFromDisk`) | step glue | file-I/O (load) | same file l.866-891 | exact |
| `UserControls/GenerationUiText.cs` (EntryDecision, `GenerationEntry.GuideChangedCard`, `GenerationStateCard.GuideChanged`, StateCard copy, NotApproved split, run-refusal text, `MailboxesRow`, `SummaryRowContent.SecondaryText`, `CG2StartOver` clause) | pure UI-text | transform | same file l.40-105, 160-173, 724-787, 946-966 | exact |
| `UserControls/StepGenerateCorpus.Run.cs` (entry branch, `ClickStateActionAsync`, `RunCoreAsync` guard) | step glue | request-response | same file l.114-242, 306-318, 384-409 | exact |
| `UserControls/StepGenerateCorpus.cs` (`GenerateCorpusRenderState.GuideChanged`, `LoadForRender`) | step layout / render seam | request-response | same file l.10-27, 1294-1357 | exact |
| `UserControls/CaseGuideUiText.cs` (`BannerVariant.PlanDependency`, `DependencyNotice(...)`) | pure UI-text | transform | same file l.16-48, 227-269 (`StaleBannerText`) | exact |
| `UserControls/StepCaseGuide.cs` (header row 2 dependency line, banner variant, Review Plan banner button) | step layout | event-driven | same file l.134-263, 770-834; `StepCoreDocument.cs` l.150-186 (`_lblGuideNotice`) | exact |
| `UserControls/CorpusUiText.cs` (`ReviewPlanEntryInput.PlanRequested`, `PlanNextGateKind.AskSkip`, `StateCard` SecondaryText, `SettingsNotice`) | pure UI-text | transform | same file l.78-132, 210-219, 382-404, 537-565, 618-643 | exact |
| `UserControls/StepReviewPlan.Run.cs` (entry flag consumption, AskSkip gate, skip click) | step glue | request-response | same file l.58-170, 403-410 | exact |
| `UserControls/StepReviewPlan.cs` (state card secondary button in a FlowLayoutPanel) | step layout | event-driven | same file l.420-471, 1050-1085; `StepCaseGuide.cs` l.236-261 (banner button flow) | exact |
| `UserControls/StepCorpusSettings.cs` (header button flow, skip button, `RequestsPlanning` rename) | step layout + glue | request-response | same file l.161-164, 196-243, 1051-1077; `StepCaseGuide.cs` l.174-192 (headerButtons flow) | exact |
| `Models/WizardState.cs` (`PlanOnEntryOnce` replaces `SuppressAutoPlanOnce`) | model (session state) | n/a | same file l.191-192 | exact |
| `Models/Discovery/DiscoveryDocument.cs` (`StaleSource.Exhibits`, `ExhibitStamps`, Normalize) | model | file-I/O (sidecar) | same file l.44-52, 96-100, 117-142 | exact |
| `Models/Discovery/DiscoveryCorpus.cs` (`WrittenStamps`) | model | n/a | same file l.25-37 | exact |
| `Services/Discovery/DiscoveryStaleness.cs` (`EmailStamps`, Stamp exhibits, Check exhibits) | service | transform | same file l.33-66 + `Services/Qa/BlindReviewer.cs` l.81-118 (CR-B02) | exact |
| `Services/Discovery/CorpusDetector.cs` (set `WrittenStamps`) | service | file-I/O (journal replay) | same file l.20-92 | exact |
| `UserControls/DiscoveryStepModel.cs` (`Build`/`SavedRows`/`Row` pass stamps) | view-model | transform | same file l.80-161, 265-268 | exact |
| `UserControls/DiscoveryUiText.cs` (`RowStatusFull` Exhibits branch) | pure UI-text | transform | same file l.283-326, 369-375 | exact |
| `UserControls/QaUiText.cs` (`CQ1RegenerateThread` trailing bool clause) | pure UI-text | transform | same file l.335-343; `GenerationUiText.CG2StartOver` l.946-949 (`batchesPending` clause) | exact |
| `UserControls/StepCaseSummary.cs` (entry withdrawal, `_btnGenerateEmails` caption + refit, stamps into `SavedRows`) | step glue | request-response | same file l.914-941, 1074-1101, 1778-1796, 1741-1750 | exact |
| Tests: `Corpus/CorpusApprovalTests.cs`, `Corpus/ReviewPlanLogicTests.cs`, `Corpus/CorpusSettingsStepLogicTests.cs`, `Evidence/GenerationUiTextTests.cs`, `Evidence/GenerateCorpusStepTests.cs`, `Discovery/DiscoveryStalenessTests.cs`, `Discovery/CorpusDetectorTests.cs`, `Discovery/DiscoveryStepModelTests.cs`, `Discovery/DiscoveryUiTextTests.cs`, `Discovery/DiscoveryStoreTests.cs`, `Case/CaseScenarioUiLogicTests.cs`, `Case/CaseGuideUiLogicTests.cs` | test | n/a | same files (extend) | exact |
| Render tests: `Render/GenerateCorpusRenderTests.cs`, `CaseSummaryRenderTests.cs`, `CorpusReviewPlanRenderTests.cs`, `CaseGuideRenderTests.cs`, `DiscoveryDocumentsRenderTests.cs`, Corpus Settings render | test (render) | n/a | `Render/GenerateCorpusRenderTests.cs` l.119-129, 566-596 | exact |

---

## Pattern Assignments

### `Services/Corpus/CorpusApproval.cs` (service, transform)

**Analog:** same file. Do not change `KeepStalePlan` / `RecordApproval` (D-25). The one allowed transition is:

```csharp
// l.190-197
public static bool IsGuideStale(CaseDraft draft)
{
    var corpus = draft.Corpus;
    if (corpus == null || !corpus.HasPlan)
        return false;
    var current = (draft.Guide ?? new CaseGuide()).ComputeHash();
    return PlanGuideHash(corpus) != current && corpus.DismissedStaleGuideHash != current;
}

// l.214-220
public static bool WithdrawApprovalIfStale(CaseDraft draft)
{
    if (draft.Corpus == null || !draft.Corpus.Approved || !IsGuideStale(draft))
        return false;
    draft.Corpus.Approved = false;
    return true;
}
```

If you add the optional read-only helper, use the same expression-bodied `/// <summary>` style:
`public static bool IsApprovedForCurrentGuide(CaseDraft draft) => draft.Corpus?.Approved == true && !IsGuideStale(draft);`

---

### The withdrawal call-site template (used at 5 sites)

**Source:** `UserControls/StepReviewPlan.Run.cs` l.111-121:

```csharp
case ReviewPlanEntry.StaleBanner:
    // D-25: approval is withdrawn while the guide is stale and not kept; the files on disk stay
    if (CorpusApproval.WithdrawApprovalIfStale(Draft))
    {
        Draft.Revision++;
        await SaveAsync();
        if (IsDisposed)
            return;
    }
```

Apply it at each site as follows:

| Site | File / location | Variant |
|------|-----------------|---------|
| Case Guide save funnel | `StepCaseGuide.Run.cs` `SaveAsync()` l.33-45 | Synchronous prefix: `if (CorpusApproval.WithdrawApprovalIfStale(Draft)) Draft.Revision++;` placed **after** the `CanSave` guard and **before** `await _state.CaseStore.SaveAsync(Draft)` (Pitfall 2: close path runs only the sync prefix). Then call the new `RefreshDependencyNotice()` after the save. |
| Reopen | `StepCaseScenario.cs` l.866-867 | After `_state.Case = result.Draft!;` add `if (CorpusApproval.WithdrawApprovalIfStale(_state.Case)) _state.Case.Revision++;` next to `MarkOpenedFromDisk(_state);`. No await is needed: the save happens on the next step's entry. |
| Generate Corpus entry | `StepGenerateCorpus.Run.cs` `OnEnterStepAsync` | See below (GuideChangedCard branch). |
| `RunCoreAsync` | `StepGenerateCorpus.Run.cs` l.389-391 | See below. |
| Case Summary entry | `StepCaseSummary.cs` `OnEnterStepAsync` l.1087-1097 | After `Draft.AdvanceStage(CaseStage.Summary);` and before `if (CaseStore.CanSave(Draft))`: `if (CorpusApproval.WithdrawApprovalIfStale(Draft)) Draft.Revision++;`. The existing save on l.1092 persists it. |

The existing Case Guide save funnel (`StepCaseGuide.Run.cs` l.33-45):

```csharp
private async Task SaveAsync()
{
    if (_state == null || !CaseStore.CanSave(Draft))
        return;

    var ok = await _state.CaseStore.SaveAsync(Draft);
    if (IsDisposed)
        return;

    RefreshHeader();
    if (!ok)
        SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
}
```

The reopen site (`StepCaseScenario.cs` l.858-891):

```csharp
var result = _state.CaseStore.Load(path);
if (!result.Succeeded) { SetStatus(result.Message, Theme.Danger); RefreshRecentList(); return; }

_state.Case = result.Draft!;
MarkOpenedFromDisk(_state);
...
/// <summary>
/// A case opened from disk never starts a planning run by itself: Review Plan shows "No corpus plan yet" once (T-04-62).
/// </summary>
internal static void MarkOpenedFromDisk(WizardState state) => state.SuppressAutoPlanOnce = true;
```

For W2, change `MarkOpenedFromDisk` to `state.PlanOnEntryOnce = false;` and keep the doc comment. If the withdrawal is folded into this static helper (for example `MarkOpenedFromDisk(WizardState state)` also withdrawing on `state.Case`), it stays unit-testable from `CaseScenarioUiLogicTests` (l.113 test exists).

---

### `UserControls/GenerationUiText.cs` (pure UI-text, transform)

**Enum and input extension pattern** (l.40-49, 51-78, 80-86):

```csharp
public enum GenerationEntry
{
    KeepRun,
    NotApprovedCard,
    PlanChangedCard,
    ...
}

public class GenerationEntryInput
{
    public bool Running;
    public bool HasPlan;
    public bool Approved;
    ...
}

public enum GenerationStateCard
{
    Ready,
    NotApproved,
    PlanChanged,
    Unreadable
}
```

Add `GuideChangedCard` to `GenerationEntry`, `public bool GuideStale;` to `GenerationEntryInput` (public field with a `//` comment, as the existing fields have), and `GuideChanged` to `GenerationStateCard`.

**Entry decision** (l.724-744). Insert the new rule between `Running` and `!HasPlan || !Approved`:

```csharp
public static GenerationEntry EntryDecision(GenerationEntryInput input)
{
    ArgumentNullException.ThrowIfNull(input);
    if (input.Running)
        return GenerationEntry.KeepRun;
    if (!input.HasPlan || !input.Approved)
        return GenerationEntry.NotApprovedCard;
    ...
```

**State card copy** (l.750-787). The signature has positional args `(card, threads, emails, waves, cost, modelName, earlierThreads, reason)`. For GuideChanged and the no-plan split, either add trailing optional parameters (`int emailsWritten = 0, bool hasPlan = true`) or add a dedicated `GuideChangedCard(int emailsWritten)` function. A trailing default keeps the existing callers (`StepGenerateCorpus.Run.cs` l.222, 225, 229; `StepGenerateCorpus.cs` l.1348-1354) compiling.

```csharp
GenerationStateCard.NotApproved => new GenerationCardContent
{
    Heading = "Approve the plan first",
    Body = "Emails are written from an approved corpus plan. Approve the plan on Review Plan, then come back to this step.",
    ButtonText = "Review Plan"
},
```

The copy comes verbatim from UI-SPEC S1. Use `Plural(n, ...)` (already in scope in this class, l.718) for the `{n} emails already written stay in place` sentence, with an explicit singular branch for `n == 1` ("stays").

**Run-refusal status text.** Add `const string` or static members next to `StartedOverText` (l.717-720). Precedent:

```csharp
public static string StartedOverText(int files) =>
    $"Cleared the earlier run: deleted {Plural(files, "EML file", "EML files")}, the narrative ledger and the side projects.";
```

**Confirmation trailing-bool clause** (l.946-949). This is the template for both `CG2StartOver(..., bool citedByDiscovery = false)` and `QaUiText.CQ1RegenerateThread(..., bool citedByDiscovery = false)`:

```csharp
public static (string Title, string Text) CG2StartOver(int files, bool batchesPending = false) =>
    ("Start Over",
        $"Delete the {Plural(files, ...)} ... The corpus plan and case guide are kept."
        + (batchesPending ? " Pending provider batches are cancelled." : ""));
```

Append ` Discovery documents that cite these emails will show Out of date.` **after** the `batchesPending` clause.

**Mailboxes row** (l.160-173, 958-966). Add `public string SecondaryText = "";` to `SummaryRowContent` with its comment updated, then:

```csharp
var row = new SummaryRowContent
{
    Line2 = ...,
    ShowPrimary = input.FolderExists,
    ShowSecondary = !input.Generated          // -> true
    // + SecondaryText = input.Generated ? "Generate Corpus" : "Generate Emails"
};
```

Note: `DiscoveryUiText.SummaryRow` and `StoryRow` also return `SummaryRowContent`. The new field defaults to `""`, so they do not change.

---

### `UserControls/StepGenerateCorpus.Run.cs` (step glue, request-response)

**Entry input and decision switch** (l.136-143, 214-241):

```csharp
CorpusPlanStore.EnsureLoaded(Draft);
var corpus = Draft.Corpus;
var plan = corpus?.Plan;
var input = new GenerationEntryInput
{
    Running = _isGenerating,
    HasPlan = plan != null,
    Approved = corpus?.Approved == true
    // + GuideStale = CorpusApproval.IsGuideStale(Draft)   (computed BEFORE withdrawal)
};

EntryData? data = null;
if (plan != null && input.Approved)   // + && !input.GuideStale  -> skip the journal read
{ ... }

SetNotice(plan == null ? string.Empty : NoticeFor(plan));
var decision = GenerationUiText.EntryDecision(input);
switch (decision)
{
    case GenerationEntry.KeepRun:
        return;
    case GenerationEntry.NotApprovedCard:
        SetNotice(string.Empty);
        ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty), GenerationStateCard.NotApproved);
        return;
    ...
```

New `case GenerationEntry.GuideChangedCard:` does the following: withdraw, then `Draft.Revision++`, then `await SaveAsync(); if (IsDisposed || _isGenerating) return;` (this guard is from l.194-196), then `SetNotice(string.Empty)`, then `ShowStateCard(..., GenerationStateCard.GuideChanged)`. The email count comes from `Draft.Corpus.Generation?.EmailsWritten ?? 0`. The NotApproved case passes `hasPlan: plan != null` for the copy split.

**Card action: avoid the Start Over fall-through** (l.306-318; this is Pitfall 1):

```csharp
internal Task ClickStateActionAsync()
{
    switch (_cardKind)
    {
        case GenerationStateCard.Ready:
            return StartRunAsync();
        case GenerationStateCard.NotApproved:
            (FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan);
            return Task.CompletedTask;
        default:
            return StartOverAsync();   // <- GuideChanged MUST NOT reach here
    }
}
```

Add `case GenerationStateCard.GuideChanged:` stacked with `NotApproved`. Update the `<summary>` text.

**RunCoreAsync guard** (l.384-409). Replace the silent return on l.389-391. All of this runs before `CreateRunGateway` (l.402), so nothing is billed:

```csharp
var plan = Draft.Corpus?.Plan;
if (plan == null || Draft.Corpus?.Approved != true)
    return;
```

The status color pattern is `SetStatus(text, Theme.Warning)`, used elsewhere in this file (l.950: `SetStatus(..., Theme.Warning)`). The save pattern is `await SaveAsync()` (l.261-273). Because `RunCoreAsync` is already async, the withdrawal can await the save before showing the card.

**Confirm seam for tests** (l.280-281). `Confirm` is a settable delegate. Step tests use `RunGatewayFactory` / `GatewayFactory` fakes (l.401-402) to count provider calls. Assert 0 calls on the stale path.

---

### `UserControls/StepGenerateCorpus.cs` (render seam)

**Enum** (l.10-27): add `GuideChanged` to `internal enum GenerateCorpusRenderState`.

**LoadForRender** (l.1294-1357): add a case next to NotApproved and keep `SetNotice` empty for it (l.1300 currently checks only `NotApproved`):

```csharp
case GenerateCorpusRenderState.NotApproved:
    ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty), GenerationStateCard.NotApproved);
    break;
```

**Button refit after a text change** (l.1071-1075). `ShowStateCard` already does this, so the new card needs no extra layout code:

```csharp
ButtonHelper.StyleButton(_btnStateAction, content.Danger ? ButtonStyle.Danger : ButtonStyle.Primary);
_btnStateAction.Text = content.ButtonText;
if (_btnStateAction is ThemedButton themed)
    themed.FitToText(0, _buttonHeight);
_btnStateAction.Visible = content.ButtonText.Length > 0;
```

---

### `UserControls/CaseGuideUiText.cs` (pure UI-text)

**Enum and content class** (l.16-22, 41-48): add `PlanDependency` as the **last** `BannerVariant` (lowest priority). Reuse `BannerContent` (`Heading`, `Body`, `PrimaryText`, `SecondaryText`). For `DependencyNotice`, add a new small result class next to it following the same public-field style, e.g.:

```csharp
public enum DependencyNoticeKind { None, Info, Changed }

public class DependencyNoticeContent
{
    public DependencyNoticeKind Kind;
    public string Line = "";
    public string Heading = "";
    public string Body = "";
    public string ButtonText = "";
}
```

**Priority function pattern** (l.227-269, `StaleBannerText`). The function returns in priority order and falls back to `new BannerContent { Variant = BannerVariant.None }`. `DependencyNotice(bool hasPlan, bool approved, bool guideStale, int emailsWritten)` mirrors this with the 4-row decision table in UI-SPEC S2. `Plural` is at l.126-129 (`n.ToString("N0", Invariant) + " " + ...`).

---

### `UserControls/StepCaseGuide.cs` (step layout)

**Header layout** (l.135-193). Add row 2, extend the row-span to 3, and keep the row-index comments:

```csharp
headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Title
headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Subtitle
// + headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Dependency line
...
headerLayout.Controls.Add(_lblTitle, 0, 0);
headerLayout.Controls.Add(_lblSubtitle, 0, 1);
headerLayout.Controls.Add(headerButtons, 1, 0);
headerLayout.SetRowSpan(headerButtons, 2);   // -> 3
```

Set `RowCount = 3` on the `TableLayoutPanel` initializer (l.138-139).

**Dependency line label.** Precedent is `StepCoreDocument.cs` l.150-163 and l.185, but that one ellipsizes. UI-SPEC requires **wrapping**, so use the banner-label style (AutoSize + MaximumSize width):

```csharp
// StepCoreDocument.cs l.151-163 (placement precedent, row 2 of the header)
_lblGuideNotice = new Label
{
    Text = "The case guide was built from this document. Edits will mark it out of date.",
    AutoSize = false,
    AutoEllipsis = true,
    Dock = DockStyle.Fill,
    TextAlign = ContentAlignment.TopLeft,
    Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0),
    Font = Theme.SmallFont(),
    ForeColor = Theme.TextMuted,
    Visible = false
};
headerLayout.Controls.Add(_lblGuideNotice, 0, 2);

// StepCaseGuide.cs l.227-235 (wrapping label style to copy instead)
_lblBannerBody = new Label
{
    AutoSize = true,
    UseMnemonic = false,
    Margin = new Padding(0),
    Font = Theme.BodyFont(),
    ForeColor = Theme.TextMuted,
    BackColor = Theme.Surface
};
```

Combine them: `AutoSize = true`, `UseMnemonic = false`, `Font = Theme.SmallFont()`, `Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0)`, `Visible = false`. Set `MaximumSize = new Size(width, 0)` on resize, the same way `SizeBanner` sets `_lblBannerBody.MaximumSize` (l.824-827). Use `ForeColor` `Theme.TextMuted` for Info and `Theme.Warning` for Changed.

**Banner button flow** (l.236-261). Add `_btnBannerReviewPlan = ButtonHelper.CreateButton("Review Plan", 0, buttonHeight, ButtonStyle.Default);` with `Margin = new Padding(0)` to `_bannerButtons`. Wire it after all controls exist: `(FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan)`. That is the same call as `StepGenerateCorpus.Run.cs` l.313.

**RefreshBanner** (l.770-808). This is where the variant is chosen and the button visibility toggled:

```csharp
content = CaseGuideUiText.StaleBannerText(stale, GuideStaleness.NeedsReviewCount(draft));
...
var show = content.Variant != BannerVariant.None;
if (show)
{
    _lblBannerHeading.Text = content.Heading;
    _lblBannerBody.Text = content.Body;
    var stale = content.Variant is BannerVariant.StaleCore or BannerVariant.StaleDownstream;
    _btnUpdateGuide.Visible = stale;
    _btnNotNow.Visible = stale;
    _btnReviewFacts.Visible = !stale;
    _btnMarkAllReviewed.Visible = !stale;
    ...
}
_bannerPrimaryShown = show && content.Variant is BannerVariant.StaleCore or BannerVariant.StaleDownstream;
```

When `content.Variant == None`, consult `DependencyNotice(...)`. If its Kind is Changed, show the PlanDependency banner (only `_btnBannerReviewPlan` visible, disabled while `_isGenerating`). If not, show or hide the dependency line. Inputs: `hasPlan = Draft.Corpus?.HasPlan == true`, `approved = Draft.Corpus?.Approved == true`, `guideStale = CorpusApproval.IsGuideStale(Draft)`, `emailsWritten = Draft.Corpus?.Generation?.EmailsWritten ?? 0`.

**SizeBanner** (l.810-834) needs no change. It measures `_bannerButtons.GetPreferredSize`, so the new button is accounted for automatically.

---

### `UserControls/CorpusUiText.cs` (pure UI-text)

**Entry input** (l.90-109). Rename or invert the flag:

```csharp
// Reopened case: show the No corpus plan yet card instead of planning (Phase 3 U13 cost control)
public bool SuppressAutoPlan;
// -> // One-shot: Next on Corpus Settings asked for planning (D-12); every other entry shows the card
//    public bool PlanRequested;
```

**Entry decision** (l.382-398). Change `input.SuppressAutoPlan ? NoPlanCard : AutoPlan` to `input.PlanRequested ? AutoPlan : NoPlanCard`, and change `!input.SuppressAutoPlan` on AutoReplan to `input.PlanRequested`. Keep the `<summary>` updated.

**Next gate** (l.119-132, 618-643). Replace the `NoPlan` kind with `AskSkip`, using the same result shape as AskApprove:

```csharp
if (!approved)
{
    return new PlanNextGateResult
    {
        Kind = PlanNextGateKind.AskApprove,
        Title = "Approve the Plan",
        Message = "Approve this plan and continue? ..."
    };
}
```

**State cards** (l.537-565). `StateCardContent` is declared in `CaseGuideUiText.cs` l.33-39 and shared with Case Guide. Add `public string SecondaryText = "";` there; it is additive and Case Guide ignores it. Set `SecondaryText = "Skip to Discovery Documents"` on NoPlan, Failed and Unreadable. Do not set it on Planning. Use the body copy from UI-SPEC S3.

**SettingsNotice** (l.210-219). Edit the copy of the `!hasPlan` and `!approved` returns. The approved return is unchanged.

---

### `UserControls/StepReviewPlan.Run.cs` (step glue)

**Entry flag** (l.84-97). Read and clear the flag **before** the decision (Pitfall 5):

```csharp
var input = new ReviewPlanEntryInput
{
    ...
    // Not reset here: Next on Corpus Settings or Plan Corpus / Retry Planning clear it (D-12 revision decision)
    SuppressAutoPlan = _state.SuppressAutoPlanOnce
};
```

Change it to `var requested = _state.PlanOnEntryOnce; _state.PlanOnEntryOnce = false;` before building the input, then `PlanRequested = requested`. Rewrite the comment.

**Next gate** (l.147-170). Replace the `NoPlan` case with AskSkip, reusing the AskApprove `MessageBox` call shape (YesNo, Question, Button1):

```csharp
case PlanNextGateKind.AskApprove:
    var answer = MessageBox.Show(FindForm(), gate.Message, gate.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
        MessageBoxDefaultButton.Button1);
    if (answer != DialogResult.Yes)
        return false;
    return await ApproveAsync();
```

AskSkip returns `answer == DialogResult.Yes`. Update the method's `<summary>` ("no plan blocks (M-P1)" no longer holds).

**Plan-from-card handler** (l.402-410). Remove `_state.SuppressAutoPlanOnce = false;`. The explicit button is the user asking, and the flag is already cleared on entry. Add a sibling skip handler:

```csharp
// Plan Corpus / Retry Planning: the user asked explicitly, so a reopened case may plan now (D-12)
private async Task PlanFromStateCardAsync()
{
    if (_state == null || _isPlanning || _isApproving)
        return;

    _state.SuppressAutoPlanOnce = false;
    await StartPlanningAsync(string.Empty);
}
```

Skip handler: guard `_isPlanning || _isApproving`, then `(FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments))`. `NavigateToStep(Type)` is a public method on `Forms/WizardForm.cs` (research verified).

---

### `UserControls/StepReviewPlan.cs` (step layout)

**State card button** (l.435-436, 464-468):

```csharp
_btnStateAction = ButtonHelper.CreateButton("Plan Corpus", 0, buttonHeight, ButtonStyle.Primary);
_btnStateAction.Margin = new Padding(0);
...
_stateContent.Controls.Add(_lblStateHeading);
_stateContent.Controls.Add(_lblStateBody);
_stateContent.Controls.Add(_btnStateAction);
_stateContent.Controls.Add(_stageList);
```

Wrap `_btnStateAction` and the new `_btnStateSkip` (`ButtonStyle.Default`, `Margin = new Padding(smallGap, 0, 0, 0)`) in a `FlowLayoutPanel`. The template is the `_bannerButtons` flow in `StepCaseGuide.cs` l.236-245 (`LeftToRight`, `WrapContents = false`, `AutoSize`, `GrowAndShrink`, `BackColor = Theme.Surface`). Add the flow panel to `_stateContent` in place of the button. Wire `_btnStateSkip.Click` in the handler-wiring block next to l.194 (`_btnStateAction.Click += async (s, e) => await PlanFromStateCardAsync();`).

**ShowStateCard** (l.1050-1064). Set the secondary visibility the same way as the primary:

```csharp
_btnStateAction.Text = content.ButtonText.Length > 0 ? content.ButtonText : "Plan Corpus";
_btnStateAction.Visible = content.ButtonText.Length > 0;
// + _btnStateSkip.Visible = content.SecondaryText.Length > 0; _btnStateSkip.Enabled = !_isPlanning;
```

Focus goes to `_btnStateAction`, never to the skip button (UI-SPEC focus rule). `CenterStateContent` (l.1080-1085) re-centers on `_stateContent.SizeChanged` (l.202), so the wider pair stays centered automatically.

---

### `UserControls/StepCorpusSettings.cs` (step layout and glue)

**Header** (l.196-243). Today the header has a single button in column 1:

```csharp
_btnResetDefaults = ButtonHelper.CreateButton("Reset to Defaults", 0, buttonHeight, ButtonStyle.Default);
_btnResetDefaults.Anchor = AnchorStyles.Right;
_btnResetDefaults.Margin = new Padding(gap, 0, 0, 0);

headerLayout.Controls.Add(_lblTitle, 0, 0);
headerLayout.Controls.Add(_lblSubtitle, 0, 1);
headerLayout.Controls.Add(_btnResetDefaults, 1, 0);
headerLayout.SetRowSpan(_btnResetDefaults, 2);
```

Replace it with a `headerButtons` `FlowLayoutPanel`, copying `StepCaseGuide.cs` l.174-192 exactly (`Anchor = Right`, `Margin = new Padding(gap, 0, 0, 0)`, row span 2). Children: `_btnSkipToDiscovery` (Default, `Margin = new Padding(0, 0, smallGap, 0)`) then `_btnResetDefaults` (`Margin = new Padding(0)`, Anchor removed). Set the tooltip through the step's existing `ToolTip` instance (grep `SetToolTip` in this file before adding a new one).

**Validation and flag** (l.161-164, 1051-1077):

```csharp
/// <summary>
/// A successful Next (mix totals 100 and both dates valid) lets Review Plan plan a reopened case (D-12).
/// </summary>
public static bool ClearsAutoPlanSuppression(bool mixOk, bool datesOk) => mixOk && datesOk;
...
if (ClearsAutoPlanSuppression(mixOk, datesOk: true))
    _state.SuppressAutoPlanOnce = false;
return Task.FromResult(true);
```

Rename it to `RequestsPlanning` and set `_state.PlanOnEntryOnce = true`. Extract the mix and date checks (l.1056-1072) into a private `bool ValidateFields()` so that the skip click runs the same validation without setting the flag (UI-SPEC S4). Then call `(FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments))`. Visibility rule: `!(Draft.Corpus?.HasPlan == true && Draft.Corpus.Approved)`, refreshed wherever the notice line is refreshed (`CorpusUiText.SettingsNotice` caller).

---

### `Models/WizardState.cs` (session model)

**Analog** (l.191-192):

```csharp
/// <summary>Set when a case is opened from disk (04-19) so Review Plan shows No corpus plan yet instead of auto-planning; cleared by Next on Corpus Settings or by Plan Corpus on Review Plan.</summary>
public bool SuppressAutoPlanOnce { get; set; }
```

Replace it with `public bool PlanOnEntryOnce { get; set; }` and a rewritten one-line `<summary>`: "set only by Corpus Settings Next; read and cleared on every Review Plan entry; false after reopen". Callers to update (grep): `StepCaseScenario.cs:891`, `StepCorpusSettings.cs:1075`, `StepReviewPlan.Run.cs:96,408`, and the tests `WizardShellRenderTests` l.92, `CaseScenarioUiLogicTests` l.113, `CorpusSettingsStepLogicTests` l.96, `ReviewPlanLogicTests` l.69/77.

---

### `Models/Discovery/DiscoveryDocument.cs` and `DiscoveryCorpus.cs` (models, sidecar I/O)

**StaleSource enum** (`DiscoveryDocument.cs` l.44-52). Append `Exhibits` last, after `CorpusPlan`. The order matches the precedence in `Check`.

**Source-stamp properties** (l.96-100):

```csharp
// Source stamp (D-27)
public string GuideHash { get; set; } = string.Empty;
public string CoreHash { get; set; } = string.Empty;
public string CaptionHash { get; set; } = string.Empty;
public Guid? CorpusPlanId { get; set; }
```

Add `public Dictionary<string, DateTime> ExhibitStamps { get; set; } = new();` here. It is additive and a missing JSON property gives an empty dictionary.

**Normalize** (l.117-142) uses the untrusted-sidecar repair style (`??=`, `CaseGuide.CleanList`). Add:

```csharp
ExhibitStamps = (ExhibitStamps ?? new())
    .Where(p => !string.IsNullOrWhiteSpace(p.Key))
    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
```

**DiscoveryCorpus** (`DiscoveryCorpus.cs` l.25-37). Add `public Dictionary<string, DateTime>? WrittenStamps { get; set; }` with a `// null = unknown (probe not run / unreadable); empty = no written emails` comment. `None(...)` (l.36) must leave it null (Pitfall 3).

**Exhibit DocId sources:** `Models/Discovery/DepositionContent.cs` l.61 (`exhibit.DocId`) and `Models/Discovery/WrittenDiscoveryContent.cs` l.21 (`request.DocId`). Both are already normalized to `string.Empty` (l.146, l.82).

---

### `Services/Discovery/DiscoveryStaleness.cs` (service, transform)

**Analog:** same file (l.33-66) and the CR-B02 loop in `Services/Qa/BlindReviewer.cs` l.81-93. Copy **only the ThreadDone pass**; skip the attachment pass at l.95-101 (Pitfall 4):

```csharp
public static Dictionary<string, DateTime> WrittenStamps(JournalReplay? replay)
{
    var stamps = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    foreach (var record in (replay?.LastByThread ?? new()).Values)
    {
        if (record == null || record.Type != JournalRecordType.ThreadDone)
            continue;
        foreach (var message in record.Messages ?? new())
        {
            if (message != null && !string.IsNullOrEmpty(message.DocId))
                stamps.TryAdd(message.DocId, record.Utc);
        }
    }
    // (attachment pass at l.95-101 NOT copied)
    return stamps;
}
```

**Stamp** (l.33-39). Add the exhibit stamping after `CorpusPlanId`, only for cited DocIds that are present in `corpus?.WrittenStamps`:

```csharp
public static void Stamp(DiscoveryDocument document, CaseDraft draft, DiscoveryCorpus? corpus)
{
    document.GuideHash = draft.Guide?.ComputeHash() ?? string.Empty;
    ...
    document.CorpusPlanId = corpus is { Usable: true } ? corpus.PlanId : null;
}
```

**Check** (l.45-66). Use the early-return chain and add an optional trailing parameter `IReadOnlyDictionary<string, DateTime>? currentStamps = null` after the CorpusPlan check. Existing callers and tests (`DiscoveryStalenessTests` l.30 `Check(doc, draft)`) still compile.

```csharp
if (document.CorpusPlanId != null && document.CorpusPlanId != draft.Corpus?.PlanId)
    return StaleSource.CorpusPlan;

return StaleSource.None;
```

Add a private static `CitedDocIds(DiscoveryDocument)` helper that reads `Deposition?.Exhibits` and `Written?.Requests`, skips empty DocIds, and uses Ordinal distinct.

**Stamp caller:** `Services/Discovery/DiscoveryGenerator.cs` l.443 (`DiscoveryStaleness.Stamp(record, context.Draft, context.Corpus);`). No change is needed there because `context.Corpus` will carry the stamps. `SaveAgainAsync` must not call `Stamp`.

---

### `Services/Discovery/CorpusDetector.cs` (service, journal replay)

**Analog:** same file l.33-82. Set the stamps at these points:

```csharp
var replay = GenerationJournal.Replay(draft.FolderPath, plan);
if (!replay.Exists)
    return DiscoveryCorpus.None("No emails have been generated yet.");   // + WrittenStamps = new() (known-empty)
if (!replay.Readable)
    return DiscoveryCorpus.None(replay.Reason);                          // stays null
if (replay.PlanChanged)
    return DiscoveryCorpus.None("The saved run belongs to a different corpus plan.");  // stays null
...
if (candidates.Count == 0)
    return DiscoveryCorpus.None("No relevant emails have been written yet.");   // + stamps from replay
...
return new DiscoveryCorpus
{
    Usable = true,
    PlanId = plan.PlanId,
    Candidates = candidates,
    Ledger = merged.Entries
    // + WrittenStamps = DiscoveryStaleness.WrittenStamps(replay)
};
```

`None(...)` returns a new object, so use an object initializer on its result: `var none = DiscoveryCorpus.None(...); none.WrittenStamps = ...; return none;`. Alternatively, add an optional parameter to `None`. Keep the `catch (OperationCanceledException) { throw; } catch (Exception ex)` shape (l.84-91) unchanged.

---

### `UserControls/DiscoveryStepModel.cs` (view-model)

**Threading point:** `Row(...)` l.265-268 calls `DiscoveryStaleness.Check(doc, draft)`. Thread `IReadOnlyDictionary<string, DateTime>? currentStamps` through `BuildRows` (l.238-263) and `Row`:
- `Build(...)` (l.80-140) passes `corpus?.WrittenStamps` (null before the probe, so there is no flicker).
- `SavedRows(...)` (l.146-161) gets a new optional parameter `IReadOnlyDictionary<string, DateTime>? currentStamps = null` placed **before** `fileExists`, or after it with a named argument. Check the existing test callers in `DiscoveryStepModelTests` first.
- Keep both `catch (Exception)` blocks and their comments (l.131-138, 156-160).

```csharp
private static DiscoveryRowInfo Row(CaseDraft draft, DiscoveryDocument? doc, DiscoveryWorkItem? item, string key,
    DiscoveryVariant variant, string caseFolder, Func<string, bool> probe)
{
    var stale = doc == null ? StaleSource.None : DiscoveryStaleness.Check(doc, draft);
```

---

### `UserControls/DiscoveryUiText.cs` (pure UI-text)

**Analog** (l.312-313, 369-375). Add an Exhibits sub-branch inside the `OutOfDate` case. Do not route it through `StaleWhat`:

```csharp
case DiscoveryRowState.OutOfDate:
    return $"Out of date with the case guide: {StaleWhat(row.Stale)} changed after this document was written. Click Regenerate... to write it again.";
```

becomes `row.Stale == StaleSource.Exhibits ? "Out of date: an email it cites was regenerated or deleted after this document was written. Click Regenerate... to write it again." : <existing>`. `ResolveState` (l.243-261) already maps any `stale != None` to OutOfDate, so it needs no change.

---

### `UserControls/QaUiText.cs` (pure UI-text)

**Analog** (l.335-343):

```csharp
public static (string Title, string Text) CQ1RegenerateThread(string subject, int emlFiles, int attachments, bool relevant)
{
    ...
    return ("Regenerate Thread",
        $"Regenerate \"{subject}\"? Its {files}{att} {verb} deleted now and the answer key marks the thread Planned. Continue Generating on the Generate Corpus step writes it again{keep}.");
}
```

Add `bool citedByDiscovery = false` and append ` Discovery documents that cite its emails will show Out of date.`. The caller (Case Summary Regenerate Thread, near `StepCaseSummary.cs` l.2273) computes the bool from the loaded discovery set: any saved document whose cited DocIds intersect the thread's DocIds. Do this off the UI thread if a read is needed. The `QaSnapshot` already loads the set in `LoadQaWithDiscovery`, so cache the cited-DocId set there.

---

### `UserControls/StepCaseSummary.cs` (step glue)

**Mailboxes button** (l.937-941):

```csharp
ApplySummaryRow(_rowMailboxes, mailboxes);
_toolTip.SetToolTip(_rowMailboxes.Line3!, mailboxes.Line3Tooltip);
_btnOpenMailboxes.Visible = mailboxes.ShowPrimary;
_btnGenerateEmails.Visible = mailboxes.ShowSecondary;
```

Add `_btnGenerateEmails.Text = mailboxes.SecondaryText;`, then refit using the pattern from `StepGenerateCorpus.cs` l.1073-1074 (`if (_btnGenerateEmails is ThemedButton themed) themed.FitToText(0, <buttonHeight>);`). After that, re-measure the row's button column. Check how `ApplySummaryRow` / `ContentRow` lays out `Buttons` before choosing the re-layout call (UI-SPEC S6, rule 47). The click handler (l.554) is unchanged.

**Entry withdrawal** (l.1074-1101): see the template table above.

**Exhibit stamps for SavedRows** (l.1778-1796). `LoadQa` already replays the journal into `qa.Replay` (used at l.1748: `BlindReviewer.WrittenStamps(qa.Replay)`). Do not replay again:

```csharp
var qa = LoadQa(folder, inputs.Plan, inputs.Guide, inputs.Prose, inputs.ModelFor, set?.TotalCostUsd ?? 0m);
...
qa.Discovery = DiscoveryRow(DiscoveryStepModel.SavedRows(inputs.Draft, set, folder), DiscoveryStore.HasDocuments(folder));
```

Compute `currentStamps` as follows:
- `inputs.Plan == null`: null.
- `qa.Replay` exists, readable, and not PlanChanged: `DiscoveryStaleness.WrittenStamps(qa.Replay)`.
- Replay does not exist: `{}`.
- Otherwise: null.

Pass it to `SavedRows`. Check `qa.Replay`'s nullability in `QaSnapshot` before relying on it.

---

## Shared Patterns

### Pure `*UiText` copy and decisions (applies to all six UI surfaces)
**Source:** `GenerationUiText.cs`, `CorpusUiText.cs`, `CaseGuideUiText.cs`, `DiscoveryUiText.cs`, `QaUiText.cs`
- Every string and every decision is a `public static` function returning public-field content classes (`GenerationCardContent`, `StateCardContent`, `BannerContent`, `SummaryRowContent`, `PlanNextGateResult`).
- Counts use `Plural(n, singular, plural)` (`CaseGuideUiText.cs` l.126-129).
- Copy changes are additive optional trailing parameters (`bool batchesPending = false`), so existing callers and tests keep their text.
- Labels that show copy set `UseMnemonic = false`.

### State transitions live in services, steps are glue
**Source:** `Services/Corpus/CorpusApproval.cs`. Never write `Draft.Corpus.Approved = false` in a step. Never compare hashes in a step; use `IsGuideStale`. Never put withdrawal in `CaseStore.SaveAsync`.

### Autosave after a draft mutation
**Source:** `StepGenerateCorpus.Run.cs` l.261-273 / `StepCaseGuide.Run.cs` l.33-45

```csharp
Draft.Revision++;
await SaveAsync();
if (IsDisposed)
    return;
```

A save failure surfaces through `SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger)`. It is never swallowed.

### Wizard navigation from a step
**Source:** `StepGenerateCorpus.Run.cs` l.313, `StepCaseSummary.cs` l.554

```csharp
(FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan);
// Discovery has no CaseStage (Phase 8 D-09): use
(FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments));
```

### Buttons and DPI
**Source:** `ButtonHelper.CreateButton(text, 0, buttonHeight, ButtonStyle.X)`. Refit with `ThemedButton.FitToText(0, buttonHeight)` after any `Text` change (`StepGenerateCorpus.cs` l.1071-1074). Wrap pairs of buttons in a `FlowLayoutPanel` (`LeftToRight`, `WrapContents = false`, `AutoSize`, `GrowAndShrink`), as in `StepCaseGuide.cs` l.174-187 and 236-257. All sizes go through `LogicalToDeviceUnits`. Fonts and colors come from `Theme` only.

### Error handling
- `catch (OperationCanceledException) { throw; }` comes before `catch (Exception ...)` (`CorpusApproval.cs` l.68-79, `CorpusDetector.cs` l.84-91).
- The `catch (Exception)` blocks in view-model and report code keep their explanatory comment (`DiscoveryStepModel.cs` l.131-138; `StepCaseSummary.cs` l.1752-1756).

### Off-UI-thread journal reads
**Source:** `StepGenerateCorpus.Run.cs` l.175-176 (`await Task.Run(() => ReadEntryAsync(...))`) and `StepCaseSummary.cs` l.1814 (`await Task.Run(() => LoadQaWithDiscovery(inputs))`). New stamp computations reuse these replays and never add a second replay.

### Tests
- **Pure logic:** extend the existing xUnit `[Fact]` files. Examples: `ReviewPlanLogicTests.cs` l.69-78 and 432-447 (assert Kind, Title, Message verbatim); `CorpusApprovalTests.cs` l.310-358 (`Setup()`, then `ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask)`, then mutate `draft.Guide!.Facts[0].Title += " (revised)"`).
- **Staleness:** `DiscoveryStalenessTests.cs` l.11-19 `Stamped()` helper (`CaseTestData.SampleCounselDraft()`, `new DiscoveryCorpus { Usable = true, PlanId = ... }`). Extend it with `WrittenStamps` and Deposition/Written content that has DocIds.
- **Render:** `GenerateCorpusRenderTests.cs` l.119-129 (`RenderBoth(name, state, null, (step, labels) => { Assert... }, primary: 1)`) and l.566-596 (Large + WizardMin, `Assert.Empty(result.Clipping)`, `DeviceDpi >= MinimumDpi`). Its `StepFixture(approved: render != GenerateCorpusRenderState.NotApproved)` (l.575) needs a GuideChanged-aware setup, e.g. a `setup:` callback that edits a fact title after approval.
- **Old-behavior tests** that must be updated deliberately: see RESEARCH Pitfall 8 (`ReviewPlanLogicTests` l.69/77/434, `CaseScenarioUiLogicTests` l.113, `CorpusSettingsStepLogicTests` l.96, `WizardShellRenderTests` l.92, `GenerationUiTextTests` l.1627).

## No Analog Found

None. Every file is an extension of an existing mechanism. The only genuinely new UI composition is the two-button state-card action row on Review Plan. It is assembled from the existing `_bannerButtons` flow pattern, so it has a pattern source.

## Metadata

**Analog search scope:** `Services/Corpus`, `Services/Discovery`, `Services/Qa`, `Models/Discovery`, `Models/WizardState.cs`, `UserControls/*UiText.cs`, `UserControls/Step{GenerateCorpus,ReviewPlan,CorpusSettings,CaseGuide,CaseScenario,CaseSummary,CoreDocument}*.cs`, `UserControls/DiscoveryStepModel.cs`, `ReelDiscovery.Tests/{Corpus,Discovery,Render,Evidence}`
**Files scanned:** ~30
**Pattern extraction date:** 2026-10-11
