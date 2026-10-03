using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Review Plan step, run side: the planning run (auto, re-plan, retry), cancel, Keep This Plan, approval, autosave, the
/// wizard lifecycle and the Next gate. UI glue only: every state transition goes through CorpusApproval (04-22).
/// </summary>
public partial class StepReviewPlan
{
    private CancellationTokenSource? _cts;
    // The run in flight, so closing can wait for it to unwind before the close-time save (WR-01)
    private Task? _activeRun;
    private Guid _boundCaseId = Guid.Empty;

    private CorpusPlanner CreatePlanner() => new(_state.CreateGateway());

    // ----- Autosave -----

    // Saves in the background; the store coalesces queued snapshots (latest wins)
    private void QueueSave()
    {
        _ = SaveAsync();
    }

    /// <summary>
    /// Writes case.json and shows the outcome: the subtitle's save time, or "Not saved: ..." in the status line.
    /// Saving is core work, so a failure is always surfaced and never swallowed.
    /// </summary>
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

    // ----- IWizardStep lifecycle -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        if (_state == null)
            return;

        // Rule 1: an active run keeps showing
        if (_isPlanning)
            return;

        // Another case was opened: start from the Issue table with a fresh status line
        if (Draft.CaseId != _boundCaseId)
        {
            _boundCaseId = Draft.CaseId;
            SelectTable(CountsTable.Issue);
            SetStatus(string.Empty, Theme.TextMuted);
        }

        if (!string.IsNullOrWhiteSpace(_state.PendingNotice))
        {
            SetStatus(_state.PendingNotice, Theme.Success);
            _state.PendingNotice = string.Empty;
        }

        Draft.Corpus ??= new CorpusState { Settings = CorpusSettings.CreateDefault(Draft) };
        var load = CorpusPlanStore.EnsureLoaded(Draft);
        var corpus = Draft.Corpus;
        var input = new ReviewPlanEntryInput
        {
            Running = _isPlanning,
            HasPlan = corpus.Plan != null,
            UnreadableReason = corpus.HasPlan && corpus.Plan == null ? load.Reason : string.Empty,
            Approved = corpus.Approved,
            // IsSettingsChanged / IsGuideStale already exclude a hash the user kept, so a kept change reads as unchanged
            SettingsChanged = CorpusApproval.IsSettingsChanged(Draft),
            SettingsDismissed = false,
            GuideStale = CorpusApproval.IsGuideStale(Draft),
            GuideDismissed = false,
            // Not reset here: Next on Corpus Settings or Plan Corpus / Retry Planning clear it (D-12 revision decision)
            SuppressAutoPlan = _state.SuppressAutoPlanOnce
        };
        // A referenced plan that did not load always shows the unreadable card, never a silent re-plan
        if (input.UnreadableReason.Length == 0 && corpus.HasPlan && corpus.Plan == null)
            input.UnreadableReason = "The saved plan could not be loaded";

        RefreshHeader();
        switch (CorpusUiText.ReviewPlanEntryDecision(input))
        {
            case ReviewPlanEntry.KeepRun:
                return;
            case ReviewPlanEntry.AutoPlan:
            case ReviewPlanEntry.AutoReplan:
                await StartPlanningAsync(string.Empty);
                return;
            case ReviewPlanEntry.StaleBanner:
                // D-25: approval is withdrawn while the guide is stale and not kept; the files on disk stay
                if (CorpusApproval.WithdrawApprovalIfStale(Draft))
                {
                    Draft.Revision++;
                    await SaveAsync();
                    if (IsDisposed)
                        return;
                }
                ShowCurrentPlan();
                return;
            case ReviewPlanEntry.SettingsBanner:
            case ReviewPlanEntry.ShowPlan:
                ShowCurrentPlan();
                return;
            case ReviewPlanEntry.UnreadableCard:
                ShowStateCard(PlanStateCard.Unreadable, input.UnreadableReason);
                return;
            default:
                ShowStateCard(PlanStateCard.NoPlan);
                return;
        }
    }

    public async Task OnLeaveStepAsync()
    {
        if (_state == null || _isPlanning)
            return;

        await SaveAsync();
    }

    /// <summary>
    /// Next gate (UI-SPEC S2): disabled during a run; no plan blocks (M-P1); an unapproved plan asks to approve (M-P2,
    /// default Yes) and continues only if every file was written; an approved plan advances.
    /// </summary>
    public async Task<bool> ValidateStepAsync()
    {
        if (_state == null)
            return false;

        var corpus = Draft.Corpus;
        var gate = CorpusUiText.PlanNextGate(_isPlanning || _isApproving, corpus?.Plan != null, corpus?.Approved == true);
        switch (gate.Kind)
        {
            case PlanNextGateKind.Disabled:
                return false;
            case PlanNextGateKind.NoPlan:
                MessageBox.Show(FindForm(), gate.Message, gate.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            case PlanNextGateKind.AskApprove:
                var answer = MessageBox.Show(FindForm(), gate.Message, gate.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (answer != DialogResult.Yes)
                    return false;
                return await ApproveAsync();
            default:
                return true;
        }
    }

    /// <summary>
    /// The saved plan with its banner: stale guide wins over changed settings; nothing re-plans here. An unapproved plan
    /// with changed settings reaches here only on a reopened case or after a cancelled/failed re-plan (CR-02).
    /// </summary>
    private void ShowCurrentPlan()
    {
        var plan = Draft.Corpus?.Plan;
        if (plan == null)
        {
            ShowStateCard(PlanStateCard.NoPlan);
            return;
        }

        ShowPlan(plan, CorpusPlanValidator.Validate(Draft, plan, _state.IsMediaAvailable));
        var variant = CorpusApproval.IsGuideStale(Draft) ? PlanBannerVariant.StaleGuide
            : CorpusApproval.IsSettingsChanged(Draft) ? PlanBannerVariant.SettingsChanged
            : PlanBannerVariant.None;
        ShowBanner(variant);
    }

    // ----- Planning run (D-18) -----

    private Task StartPlanningAsync(string guidance) => TrackRun(PlanCoreAsync(guidance));

    private Task TrackRun(Task run)
    {
        if (!run.IsCompleted)
            _activeRun = run;
        return run;
    }

    /// <summary>
    /// Closing during a run (C-P2): cancels the run and pumps messages until it has unwound. Planning never mutates the
    /// draft, so the previous plan and settings are intact. Returns false on timeout.
    /// </summary>
    public bool CancelAndWaitForRun(TimeSpan timeout)
    {
        var run = _activeRun;
        if (run == null || run.IsCompleted)
            return true;

        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run released its token source while finishing; it is unwinding already
        }

        // The run's continuations resume on this UI thread
        var clock = Stopwatch.StartNew();
        while (!run.IsCompleted && clock.Elapsed < timeout)
        {
            Application.DoEvents();
            if (!run.IsCompleted)
                Thread.Sleep(15);
        }

        return run.IsCompleted;
    }

    private async Task PlanCoreAsync(string guidance)
    {
        if (_isPlanning || _isApproving || _state == null)
            return;

        _cts = new CancellationTokenSource();
        _isPlanning = true;
        ShowStateCard(PlanStateCard.Planning);
        _progress.Marquee = true;
        _progress.Value = 0;
        _progress.Visible = true;
        _btnCancel.Enabled = true;
        _btnCancel.Visible = true;
        SetStatus(CorpusUiText.ProgressText(PlanStage.Allocating, 0, 0), Theme.Accent);
        RefreshHeader();
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var planner = CreatePlanner();
            var progress = new Progress<PlanProgress>(p =>
            {
                if (IsDisposed || !_isPlanning)
                    return;

                SetStatus(CorpusUiText.ProgressText(p.Stage, p.StageDone, p.StageTotal), Theme.Accent);
                if (p.Total > 0)
                {
                    _progress.Marquee = false;
                    _progress.Maximum = p.Total;
                    _progress.Value = Math.Min(p.Total, p.Done);
                }
                UpdateStageChecklist(p.Stage, p.StageDone, p.StageTotal);
            });

            var result = await planner.PlanAsync(Draft, _state.IsMediaAvailable, guidance, progress, _cts.Token);

            // The window may have closed during the run (WR-07)
            if (IsDisposed)
                return;

            if (result.Succeeded)
            {
                // corpus-plan.json first, then the draft points at it; the stage advances to ReviewPlan (D-04)
                var apply = await CorpusApproval.ApplyNewPlanAsync(_state.CaseStore.EnsureFolder(Draft), Draft, result.Plan!);
                if (IsDisposed)
                    return;

                if (!apply.Saved)
                {
                    var reason = $"Could not save the plan: {apply.Error}";
                    ShowAfterFailedRun(reason);
                    SetStatus(reason, Theme.Danger);
                    return;
                }

                Draft.Revision++;
                await SaveAsync();
                if (IsDisposed)
                    return;

                _isPlanning = false;
                ShowPlan(result.Plan!, result.Findings);
                ShowBanner(PlanBannerVariant.None);
                ShowReadyStatus(result.Plan!, result.Findings);
            }
            else if (result.IsFatal)
            {
                ShowAfterFailedRun(result.FatalError);
                SetStatus(CorpusUiText.FatalText(result.FatalError), Theme.Danger);
            }
            else if (result.RefusedChunks > 0)
            {
                ShowAfterFailedRun(result.RefusalMessage);
                SetStatus(CorpusUiText.RefusedText(result.RefusedChunks, result.TotalChunks, result.RefusalMessage), Theme.Danger);
            }
            else
            {
                ShowFailedStatus(ShowAfterFailedRun(result.FailureMessage), result.FailureMessage);
            }
        }
        catch (OperationCanceledException)
        {
            if (IsDisposed)
                return;

            // A cancelled run discards the new plan; settings and any previous plan are untouched
            var previous = Draft.Corpus?.Plan != null;
            _isPlanning = false;
            if (previous)
                ShowCurrentPlan();
            else
                ShowStateCard(PlanStateCard.NoPlan);
            SetStatus(CorpusUiText.CancelledText(previous), Theme.Warning);
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            if (IsDisposed)
                return;

            var message = ex.Result.Message ?? ex.Message;
            ShowAfterFailedRun(message);
            SetStatus(CorpusUiText.FatalText(message), Theme.Danger);
        }
        catch (Exception ex)
        {
            // UI boundary: an unexpected failure is shown on the Failed card; the draft was never changed
            if (IsDisposed)
                return;

            var message = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            ShowFailedStatus(ShowAfterFailedRun(message), message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                _isPlanning = false;
                _progress.Visible = false;
                _progress.Marquee = false;
                _btnCancel.Visible = false;
                RefreshHeader();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// After a failed run: the previous plan (unchanged, with its approval) comes back, or the Failed card with the reason.
    /// Returns true when the previous plan is shown.
    /// </summary>
    private bool ShowAfterFailedRun(string reason)
    {
        _isPlanning = false;
        if (Draft.Corpus?.Plan != null)
        {
            ShowCurrentPlan();
            return true;
        }

        ShowStateCard(PlanStateCard.Failed, reason);
        return false;
    }

    /// <summary>
    /// A plain failure: the Failed card carries the reason; over a kept previous plan the status line says what happened.
    /// </summary>
    private void ShowFailedStatus(bool previousShown, string reason)
    {
        if (!previousShown)
        {
            SetStatus(string.Empty, Theme.TextMuted);
            return;
        }

        var why = (reason ?? string.Empty).Trim().TrimEnd('.');
        SetStatus(why.Length == 0
            ? "The new plan was not built. The previous plan is still in place."
            : $"The new plan was not built: {why}. The previous plan is still in place.", Theme.Danger);
    }

    // Plan Corpus / Retry Planning: the user asked explicitly, so a reopened case may plan now (D-12)
    private async Task PlanFromStateCardAsync()
    {
        if (_state == null || _isPlanning || _isApproving)
            return;

        _state.SuppressAutoPlanOnce = false;
        await StartPlanningAsync(string.Empty);
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (!_isPlanning || _cts == null)
            return;

        var (title, text) = CorpusUiText.CP1CancelPlanning(Draft.Corpus?.Plan != null);
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        _btnCancel.Enabled = false;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished while the confirmation was open; nothing left to cancel
        }
    }

    // ----- Re-plan, keep plan (D-23, D-25) -----

    private async Task ReplanAsync()
    {
        if (_state == null || _isPlanning || _isApproving || Draft.Corpus?.Plan == null)
            return;

        string guidance;
        using (var dialog = new ReplanDialog(Draft.Corpus.Approved))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;
            guidance = dialog.Guidance;
        }
        if (IsDisposed)
            return;

        await StartPlanningAsync(guidance);
    }

    private void KeepPlan()
    {
        if (_state == null || _isPlanning || _isApproving || Draft.Corpus?.Plan == null)
            return;

        if (_bannerVariant == PlanBannerVariant.StaleGuide)
        {
            CorpusApproval.KeepStalePlan(Draft);
            SetStatus(CorpusUiText.KeptStaleText, Theme.Warning);
        }
        else if (_bannerVariant == PlanBannerVariant.SettingsChanged)
        {
            CorpusApproval.KeepSettingsChange(Draft);
        }

        Draft.Revision++;
        ShowCurrentPlan();
        QueueSave();
    }

    // ----- Approve (D-26) -----

    /// <summary>
    /// Writes answer-key.csv, answer-key.xlsx and the review protocol, then records approval and saves; a failed write
    /// approves nothing. Returns true when the plan is approved.
    /// </summary>
    private async Task<bool> ApproveAsync()
    {
        var plan = Draft?.Corpus?.Plan;
        if (_state == null || _isPlanning || _isApproving || plan == null)
            return false;

        _isApproving = true;
        RefreshHeader();
        StateChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(CorpusUiText.ApprovingText, Theme.Accent);
        var approved = false;
        try
        {
            var folder = _state.CaseStore.EnsureFolder(Draft!);
            var result = await CorpusApproval.ApproveAsync(folder, Draft!, plan);
            if (IsDisposed)
                return false;

            if (result.Succeeded)
            {
                approved = true;
                Draft!.Revision++;
                _isApproving = false;
                ShowCurrentPlan();
                SetStatus(CorpusUiText.ApprovedText(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))), Theme.Success);
                await SaveAsync();
            }
            else if (result.FileOpenElsewhere)
            {
                SetStatus(CorpusUiText.ApproveFileOpenText(result.FailedFileName), Theme.Danger);
            }
            else
            {
                SetStatus(CorpusUiText.ApproveFailedText(result.Error), Theme.Danger);
            }
        }
        catch (OperationCanceledException)
        {
            // Approve has no cancel of its own; a cancelled write approves nothing
            if (!IsDisposed)
                SetStatus(CorpusUiText.ApproveFailedText("the write was cancelled"), Theme.Danger);
        }
        catch (Exception ex)
        {
            // UI boundary: the case folder could not be created or a writer failed unexpectedly; nothing was approved
            if (!IsDisposed)
                SetStatus(CorpusUiText.ApproveFailedText(ex.Message), Theme.Danger);
        }
        finally
        {
            if (!IsDisposed)
            {
                _isApproving = false;
                RefreshHeader();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        return approved;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Closing the wizard or switching flows stops planning so no further calls are billed (T-04-58, WR-07)
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run already finished and released its token source; nothing to cancel
            }
        }

        base.Dispose(disposing);
    }
}
