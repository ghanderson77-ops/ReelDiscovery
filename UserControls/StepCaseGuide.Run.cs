using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Case Guide step, run side: generation runs (auto first run, resume, retry, update, tab and entry regeneration),
/// cancel, autosave, the wizard lifecycle and the Next gate.
/// </summary>
public partial class StepCaseGuide
{
    private CancellationTokenSource? _cts;
    // The run in flight (whole guide, tab or entry), so closing can wait for it to merge its finished units (WR-01)
    private Task? _activeRun;
    private Guid _boundCaseId = Guid.Empty;

    // ----- Autosave (D-10) -----

    // Saves in the background; the store coalesces queued snapshots (latest wins)
    private void QueueSave()
    {
        _ = SaveAsync();
    }

    /// <summary>
    /// Writes case.json and shows the outcome: the subtitle's save time, or "Not saved: ..." in the status line.
    /// Saving is core work, so a failure is always surfaced and never swallowed (T-03-53).
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

    /// <summary>
    /// Applies every pending edit synchronously: cells still in edit mode and queued grid commits on every tab, then the
    /// selected tab's detail fields. Runs before a run sets the tabs busy, on Next, on leave and on close (WR-02).
    /// </summary>
    private void CommitAllEdits()
    {
        if (_isGenerating)
            return;

        foreach (var panel in _panels.Values)
            panel.CommitGridEdits();
        _panels[_selectedTab].CommitPendingEdits();
    }

    // ----- Runs -----

    // Every run goes through the gateway, which tracks usage and never exposes keys in its messages
    private CaseGuideGenerator CreateGenerator() => new CaseGuideGenerator(_state.CreateGateway());

    /// <summary>
    /// Writes every part that is not finished, in order (auto first run, Resume Guide, Generate Guide, Retry {Part}).
    /// </summary>
    private Task GenerateOrResumeAsync() =>
        RunAsync((g, p, done, ct) => g.GenerateAsync(Draft, p, done, ct));

    /// <summary>
    /// Update Guide (C1): rewrites only the stale parts, in order, keeping hand edits (D-20).
    /// </summary>
    private async Task UpdateGuideAsync()
    {
        if (_isGenerating || _state == null)
            return;

        var (title, text) = CaseGuideUiText.C1UpdateGuide(GuideStaleness.StaleParts(Draft));
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes || IsDisposed)
            return;

        await RunAsync((g, p, done, ct) => g.UpdateStaleAsync(Draft, p, done, ct));
    }

    public Task RetryIssueDetailsAsync() =>
        RunAsync(async (g, p, done, ct) =>
        {
            var partResult = await g.RetryIssueDetailsAsync(Draft, p, ct);
            await done(GuidePart.Issues);
            return ToRunResult(GuidePart.Issues, partResult);
        }, IssueDetailsFailedText);

    /// <summary>
    /// Regenerate {Tab}... (D1): asks for guidance and whether edited entries are replaced, then rewrites the tab.
    /// </summary>
    public async Task RegenerateTabAsync(GuidePart part)
    {
        if (_isGenerating || _state == null || part == GuidePart.Issues || Draft.Guide == null)
            return;

        CommitAllEdits();
        string guidance;
        bool replaceEdited;
        using (var dialog = new RegenerateTabDialog(part, EditedCount(Draft.Guide, part)))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || IsDisposed)
                return;
            guidance = dialog.Guidance;
            replaceEdited = dialog.ReplaceEdited;
        }

        await RunAsync(async (g, p, done, ct) =>
        {
            var partResult = await g.RegenerateTabAsync(Draft, part, guidance, replaceEdited, p, ct);
            await done(part);
            return ToRunResult(part, partResult);
        }, RegenerateTabFailedText);
    }

    /// <summary>
    /// Regenerate Selected (Key Facts): asks for guidance and whether edited facts are replaced, then rewrites the selected facts
    /// as one run, so progress, Cancel Generation, autosave and the busy state work like every other run.
    /// </summary>
    public async Task<GuideEntriesResult?> RegenerateEntriesAsync(GuidePart part, IReadOnlyList<string> entityIds)
    {
        if (_isGenerating || _state == null || part != GuidePart.Facts || Draft.Guide == null || entityIds == null)
            return null;

        CommitAllEdits();
        var guide = Draft.Guide;
        var ids = entityIds.Where(id => guide.FindFact(id) != null).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
            return null;

        string guidance;
        bool replaceEdited;
        var edited = ids.Count(id => guide.FindFact(id)!.IsUserEdited);
        using (var dialog = new RegenerateSelectedDialog(ids.Count, edited))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || IsDisposed)
                return null;
            guidance = dialog.Guidance;
            replaceEdited = dialog.ReplaceEdited;
        }

        GuideEntriesResult? entries = null;
        await RunAsync(async (g, p, done, ct) =>
        {
            entries = await g.RegenerateEntriesAsync(Draft, part, ids, guidance, replaceEdited, p, ct);
            await done(part);
            return new CaseGuideGenerationResult { FatalError = entries.FatalError, Notes = entries.Notes };
        }, report: _ => ReportEntriesOutcome(entries));
        return entries;
    }

    private void ReportEntriesOutcome(GuideEntriesResult? entries)
    {
        if (entries == null)
            return;

        if (entries.IsFatal)
        {
            SetStatus(CaseGuideUiText.RegenerateFactsStoppedText(entries.FatalError, entries.Regenerated.Count, entries.Attempted), Theme.Danger);
            return;
        }

        var text = CaseGuideUiText.RegeneratedFactsText(entries.Regenerated.Count, entries.Attempted, entries.Failed.Count,
            entries.Failed.FirstOrDefault()?.Message ?? string.Empty, entries.SkippedEdited.Count);
        SetStatus(text, entries.Failed.Count > 0 ? Theme.Danger : entries.Regenerated.Count > 0 ? Theme.Success : Theme.Warning);
    }

    /// <summary>
    /// Regenerate {Noun} for one entry (the panel already asked C3 for a hand-edited entry). On failure the old text is kept.
    /// </summary>
    public Task RegenerateEntryAsync(GuidePart part, string entityId, string guidance) =>
        TrackRun(RegenerateEntryCoreAsync(part, entityId, guidance));

    private async Task RegenerateEntryCoreAsync(GuidePart part, string entityId, string guidance)
    {
        if (_isGenerating || _state == null || string.IsNullOrEmpty(entityId))
            return;

        // A typed cell or detail field is applied before the run makes the tabs read-only (WR-02)
        CommitAllEdits();
        var name = EntityName(part, entityId);
        _cts = new CancellationTokenSource();
        _writingEntityId = entityId;
        _progress.Value = 0;
        SetBusy(true);
        SetStatus(CaseGuideUiText.WritingEntityText(name), Theme.Accent);
        // The entry's dot turns Accent while it is written
        _panels[part].Invalidate(true);

        try
        {
            var generator = CreateGenerator();
            await generator.RegenerateEntryAsync(Draft, part, entityId, guidance ?? string.Empty, _cts.Token);
            if (IsDisposed)
                return;

            _writingEntityId = string.Empty;
            RefreshAll();
            await SaveAsync();
            if (IsDisposed)
                return;
            ReportReady();
        }
        catch (OperationCanceledException)
        {
            SetStatus(CaseGuideUiText.CancelledText, Theme.Warning);
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            SetStatus(CaseGuideUiText.FatalText(ex.Result.Message ?? ex.Message), Theme.Danger);
        }
        catch (LlmCallException ex)
        {
            // The generator leaves the entry untouched on failure
            SetStatus(CaseGuideUiText.EntryRegenFailedText(name, ex.Result.Message ?? ex.Message), Theme.Danger);
        }
        catch (Exception ex)
        {
            SetStatus(CaseGuideUiText.EntryRegenFailedText(name, ex.Message), Theme.Danger);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _writingEntityId = string.Empty;
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Runs one generator call with progress, per-part autosave, cancel and the outcome status line (one run at a time, T-03-52).
    /// <paramref name="failedText"/> words a failed part (default: "The {part} failed: ... Click Retry {Part}.");
    /// <paramref name="report"/> replaces the whole outcome status line when given.
    /// </summary>
    private Task RunAsync(
        Func<CaseGuideGenerator, IProgress<GuideProgress>, Func<GuidePart, Task>, CancellationToken, Task<CaseGuideGenerationResult>> work,
        Func<GuidePart, string, string>? failedText = null, Action<CaseGuideGenerationResult>? report = null) =>
        TrackRun(RunCoreAsync(work, failedText, report));

    private Task TrackRun(Task run)
    {
        if (!run.IsCompleted)
            _activeRun = run;
        return run;
    }

    /// <summary>
    /// Closing during a run (C12, D-10): cancels the run and pumps messages until it has unwound, so the units that
    /// already came back are merged into the draft before the close-time save (WR-01). Returns false on timeout.
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

        // The run's continuations (merge in the generator's finally, final autosave) resume on this UI thread
        var clock = Stopwatch.StartNew();
        while (!run.IsCompleted && clock.Elapsed < timeout)
        {
            Application.DoEvents();
            if (!run.IsCompleted)
                Thread.Sleep(15);
        }

        return run.IsCompleted;
    }

    private async Task RunCoreAsync(
        Func<CaseGuideGenerator, IProgress<GuideProgress>, Func<GuidePart, Task>, CancellationToken, Task<CaseGuideGenerationResult>> work,
        Func<GuidePart, string, string>? failedText = null, Action<CaseGuideGenerationResult>? report = null)
    {
        if (_isGenerating || _state == null)
            return;

        CommitAllEdits();
        _cts = new CancellationTokenSource();
        _userPickedTab = false;
        _writingPart = null;
        _progress.Value = 0;
        SetStatus(string.Empty, Theme.Accent);
        SetBusy(true);
        RefreshAll();

        try
        {
            var generator = CreateGenerator();
            var progress = new Progress<GuideProgress>(p =>
            {
                if (IsDisposed)
                    return;

                _writingPart = p.Part;
                SetStatus(p.Message, Theme.Accent);
                if (p.Total > 0)
                {
                    _progress.Marquee = false;
                    _progress.Maximum = p.Total;
                    _progress.Value = Math.Min(p.Total, p.Done);
                }
                else
                {
                    _progress.Marquee = true;
                }

                // The selection follows the part being written until the user picks a tab (U12)
                if (!_userPickedTab && _selectedTab != p.Part)
                    SelectTab(p.Part);
                RefreshAll();
            });

            // Autosave after every part, so a crash or close keeps finished parts (D-10)
            Func<GuidePart, Task> partCompleted = async part =>
            {
                if (IsDisposed)
                    return;

                RefreshAll();
                await SaveAsync();
            };

            var result = await work(generator, progress, partCompleted, _cts.Token);

            // The window may have closed during the run (WR-07)
            if (IsDisposed)
                return;

            if (report != null)
                report(result);
            else
                ReportOutcome(result, failedText ?? CaseGuideUiText.PartFailedText);
        }
        catch (OperationCanceledException)
        {
            SetStatus(CaseGuideUiText.CancelledText, Theme.Warning);
        }
        catch (LlmCallException ex) when (ex.Result.Outcome == LlmOutcome.Refused)
        {
            SetStatus(CaseGuideUiText.RefusedText(_writingPart ?? GuidePart.Issues, ex.Result.Message ?? ex.Message), Theme.Danger);
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            SetStatus(CaseGuideUiText.FatalText(ex.Result.Message ?? ex.Message), Theme.Danger);
        }
        catch (Exception ex)
        {
            SetStatus(ErrorText(ex), Theme.Danger);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                _isGenerating = false;
                _writingPart = null;
                _userPickedTab = false;
                SetBusy(false);
                RefreshAll();
                // Also covers a cancel: partial results are kept and saved
                await SaveAsync();
                if (!IsDisposed)
                    StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void ReportOutcome(CaseGuideGenerationResult result, Func<GuidePart, string, string> failedText)
    {
        if (result.IsFatal)
            SetStatus(CaseGuideUiText.FatalText(result.FatalError), Theme.Danger);
        else if (result.RefusedPart is { } refused)
            SetStatus(CaseGuideUiText.RefusedText(refused, ReasonWithoutRetryHint(result.RefusalMessage)), Theme.Danger);
        else if (result.PartsFailed.Count > 0)
            SetStatus(failedText(result.PartsFailed[0], ReasonWithoutRetryHint(result.FailureMessage)), Theme.Danger);
        else
            ReportReady();
    }

    private void ReportReady()
    {
        var findings = CaseGuideValidator.Validate(Draft);
        if (findings.Count == 0)
        {
            var guide = Draft.Guide;
            SetStatus(CaseGuideUiText.GuideReadyText(
                guide?.Issues.Count ?? 0,
                guide?.Cast.Count(c => c.Tier == CastTier.Key) ?? 0,
                guide?.Cast.Count(c => c.Tier == CastTier.Background) ?? 0,
                guide?.Facts.Count ?? 0,
                guide?.Values.Count ?? 0), Theme.Success);
        }
        else
        {
            SetStatus(CaseGuideUiText.GuideReadyWithFindingsText(
                CaseGuideValidator.CountErrors(findings), CaseGuideValidator.CountWarnings(findings)), Theme.Warning);
        }
    }

    // Maps a single-part result onto the whole-run outcome shape
    private static CaseGuideGenerationResult ToRunResult(GuidePart part, GuidePartResult partResult)
    {
        var result = new CaseGuideGenerationResult();
        result.Notes.AddRange(partResult.Notes);
        if (partResult.IsFatal)
        {
            result.FatalError = partResult.FatalError;
        }
        else if (partResult.Succeeded)
        {
            result.PartsWritten.Add(part);
        }
        else
        {
            result.PartsFailed.Add(part);
            result.FailureMessage = partResult.FailureMessage;
            if (partResult.WasRefused)
            {
                result.RefusedPart = part;
                result.RefusalMessage = partResult.FailureMessage;
            }
        }
        return result;
    }

    private static string IssueDetailsFailedText(GuidePart part, string message) =>
        string.IsNullOrWhiteSpace(message)
            ? "The issue details were not written. Click Retry Issue Details."
            : $"The issue details were not written: {message.Trim().TrimEnd('.')}. Click Retry Issue Details.";

    private static string RegenerateTabFailedText(GuidePart part, string message)
    {
        var noun = CaseGuideUiText.PartNoun(part, false);
        var verb = CaseGuideUiText.IsPluralPart(part) ? "were" : "was";
        var detail = string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message.Trim().TrimEnd('.')}";
        return $"The {noun} {verb} not regenerated{detail}. The previous {CaseGuideUiText.EntriesWord(part)} were kept.";
    }

    private static string ErrorText(Exception ex) =>
        ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}";

    private static int EditedCount(CaseGuide guide, GuidePart part) => part switch
    {
        GuidePart.Cast => guide.Cast.Count(c => c.IsUserEdited),
        GuidePart.Facts => guide.Facts.Count(f => f.IsUserEdited),
        GuidePart.Values => guide.Values.Count(v => v.IsUserEdited),
        _ => 0
    };

    private string EntityName(GuidePart part, string id)
    {
        var guide = Draft.Guide;
        var name = part switch
        {
            GuidePart.Cast => guide?.FindCast(id)?.FullName,
            GuidePart.Facts => guide?.FindFact(id)?.Title,
            GuidePart.Values => guide?.FindValue(id)?.Label,
            _ => guide?.FindIssue(id)?.Title
        };
        return string.IsNullOrWhiteSpace(name) ? id : name;
    }

    // ----- Cancel and banner actions -----

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (!_isGenerating || _cts == null)
            return;

        var (title, text) = CaseGuideUiText.C2CancelGeneration();
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            _btnCancel.Enabled = false;
            _cts?.Cancel();
        }
    }

    // Not Now: hide the stale banner until the basis changes again; Checks keep reporting the stale parts
    private void DismissStaleBanner()
    {
        if (_state == null || _isGenerating || Draft.Guide == null)
            return;

        Draft.Guide.DismissedStaleKey = GuideStaleness.StaleKey(Draft);
        RefreshAll();
        QueueSave();
    }

    // ----- IWizardStep lifecycle -----

    public async Task OnEnterStepAsync()
    {
        if (_isGenerating || _state == null)
            return;

        // Another case was opened: start from the Issues tab with a fresh status line
        if (Draft.CaseId != _boundCaseId)
        {
            _boundCaseId = Draft.CaseId;
            _userPickedTab = false;
            _writingPart = null;
            _writingEntityId = string.Empty;
            _selectedTab = GuidePart.Issues;
            foreach (var (p, toggle) in _tabButtons)
                toggle.Selected = p == _selectedTab;
            SetStatus(string.Empty, Theme.TextMuted);
        }

        if (!string.IsNullOrWhiteSpace(_state.PendingNotice))
        {
            SetStatus(_state.PendingNotice, Theme.Success);
            _state.PendingNotice = string.Empty;
        }

        Draft.AdvanceStage(CaseStage.CaseGuide);

        // A guide saved before active ranges were widened: generated characters cover every fact date they appear in;
        // the save below writes the change
        if (Draft.Guide != null && CaseGuideEditing.WidenActiveRanges(Draft))
            Draft.Revision++;

        RefreshAll();
        await SaveAsync();
        if (IsDisposed)
            return;

        // D-06: only a case with no guide at all runs automatically; a partial guide shows Resume Guide (U13)
        if (Draft.Guide == null)
            await GenerateOrResumeAsync();
    }

    public async Task OnLeaveStepAsync()
    {
        if (_state == null)
            return;

        CommitAllEdits();
        await SaveAsync();
    }

    /// <summary>
    /// Next gate: unfinished parts block and say how to finish (M1); errors open the errors dialog (M2) with each error's
    /// fix, Fix Automatically, Show Checks (the default) and Continue Anyway (confirmed, default No); warnings ask with No
    /// as the default (M3) (T-03-54).
    /// </summary>
    public Task<bool> ValidateStepAsync()
    {
        if (_isGenerating || _state == null)
            return Task.FromResult(false);

        CommitAllEdits();
        RefreshAll();
        return Task.FromResult(EvaluateNextGate());
    }

    private bool EvaluateNextGate()
    {
        var gate = CaseGuideUiText.NextGateDecision(_isGenerating, UnfinishedParts(Draft),
            CaseGuideValidator.CountErrors(_findings), CaseGuideValidator.CountWarnings(_findings));
        switch (gate.Kind)
        {
            case NextGateKind.Disabled:
                return false;
            case NextGateKind.Unfinished:
                MessageBox.Show(FindForm(), gate.Message, gate.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            case NextGateKind.Errors:
                return ErrorsGate();
            case NextGateKind.Warnings:
                var answer = MessageBox.Show(FindForm(), gate.Message, gate.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                return answer == DialogResult.Yes;
            default:
                return true;
        }
    }

    // M2: the errors dialog; Fix Automatically applies the automatic fixes and evaluates the gate again
    private bool ErrorsGate()
    {
        var errors = _findings.Where(f => f.Severity == IssueSeverity.Error).ToList();
        GuideErrorsChoice choice;
        using (var dialog = new GuideErrorsDialog(CaseGuideUiText.ErrorsGateText(errors)))
        {
            // Esc and the close box mean Show Checks (the dialog's Cancel button)
            choice = dialog.ShowDialog(FindForm()) == DialogResult.OK ? dialog.Choice : GuideErrorsChoice.ShowChecks;
        }
        if (IsDisposed)
            return false;

        switch (choice)
        {
            case GuideErrorsChoice.FixAutomatically:
                var applied = CaseGuideQuickFix.ApplyAll(Draft, errors);
                AfterFix(applied);
                // Nothing changed means the gate would only ask the same thing again
                if (applied == 0)
                {
                    ShowFirstError();
                    return false;
                }
                return EvaluateNextGate();

            case GuideErrorsChoice.ContinueAnyway:
                var (title, text) = CaseGuideUiText.C15ContinueWithErrors(errors.Count);
                var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Yes)
                    return true;
                ShowFirstError();
                return false;

            default:
                ShowFirstError();
                return false;
        }
    }

    /// <summary>
    /// Show Checks: selects the first error in the Checks list and focuses it; when the list is folded (short window) it
    /// opens the error's entry instead.
    /// </summary>
    private void ShowFirstError()
    {
        if (_state == null || IsDisposed)
            return;

        var index = _visibleFindings.FindIndex(f => f.Severity == IssueSeverity.Error);
        if (index < 0)
            return;

        if (_checksListShown)
            SelectCheck(index, focus: true);
        else
            NavigateToFinding(_visibleFindings[index]);
    }

    // ----- Checks fixes -----

    /// <summary>
    /// Fix (Checks card): applies the selected check's automatic fix, or starts the run its model fix needs through the
    /// same path (and confirmation) as the matching button.
    /// </summary>
    private async Task FixSelectedCheckAsync()
    {
        if (_isGenerating || _state == null || Draft.Guide == null || SelectedCheck() is not { } finding)
            return;

        CommitAllEdits();
        if (finding.HasAutomaticFix)
        {
            AfterFix(CaseGuideQuickFix.Apply(Draft, finding) ? 1 : 0);
            return;
        }

        switch (finding.QuickFix)
        {
            case GuideQuickFix.RegenerateEntry when PartFor(finding.Area) is { } part && part != GuidePart.Issues:
                if (ConfirmRegenerateEdited(part, finding.EntityId))
                    await RegenerateEntryAsync(part, finding.EntityId, string.Empty);
                break;
            case GuideQuickFix.RegenerateCast:
                await RegenerateTabAsync(GuidePart.Cast);
                break;
            case GuideQuickFix.RegenerateValues:
                await RegenerateTabAsync(GuidePart.Values);
                break;
            case GuideQuickFix.RetryIssueDetails:
                await RetryIssueDetailsAsync();
                break;
            case GuideQuickFix.UpdateGuide:
                await UpdateGuideAsync();
                break;
            case GuideQuickFix.ResumeGuide:
                await GenerateOrResumeAsync();
                break;
        }
    }

    // C3 for a hand-edited entry, like the detail card's Regenerate button
    private bool ConfirmRegenerateEdited(GuidePart part, string entityId)
    {
        var guide = Draft.Guide;
        var edited = part switch
        {
            GuidePart.Cast => guide?.FindCast(entityId)?.IsUserEdited,
            GuidePart.Facts => guide?.FindFact(entityId)?.IsUserEdited,
            GuidePart.Values => guide?.FindValue(entityId)?.IsUserEdited,
            _ => null
        };
        if (edited == null)
            return false;
        if (edited == false)
            return true;

        var noun = part switch { GuidePart.Cast => "Character", GuidePart.Values => "Value", _ => "Fact" };
        var (title, text) = CaseGuideUiText.C3RegenerateEdited(noun, EntityName(part, entityId));
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        return answer == DialogResult.Yes && !IsDisposed;
    }

    /// <summary>
    /// Fix All ({n}): lists the automatic fixes by kind (default Cancel) and applies them all.
    /// </summary>
    private void FixAllChecks()
    {
        if (_isGenerating || _state == null || Draft.Guide == null)
            return;

        CommitAllEdits();
        RefreshAll();
        var findings = _visibleFindings.Where(f => f.HasAutomaticFix).ToList();
        if (findings.Count == 0)
            return;

        using (var dialog = new FixAllDialog(CaseGuideUiText.FixAllText(CaseGuideQuickFix.CountAutomatic(findings))))
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || IsDisposed)
                return;
        }

        AfterFix(CaseGuideQuickFix.ApplyAll(Draft, findings));
    }

    // After any fix: revalidate, refresh, autosave and say what is left
    private void AfterFix(int fixedCount)
    {
        if (IsDisposed)
            return;

        RefreshAll();
        if (fixedCount > 0)
            QueueSave();
        SetStatus(CaseGuideUiText.FixedChecksText(fixedCount, CaseGuideValidator.CountErrors(_findings), CaseGuideValidator.CountWarnings(_findings)),
            fixedCount > 0 ? Theme.Success : Theme.Warning);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Closing the wizard or switching flows stops in-flight generation so no further calls are billed (WR-07)
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run already finished and released its token source; nothing to cancel
            }

            _checkHintFont.Dispose();

            // Tab actions of unselected tabs sit outside any control tree, so nothing else disposes them
            foreach (var panel in _panels.Values)
            {
                foreach (var action in panel.TabActions)
                {
                    if (action.Parent == null)
                        action.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
