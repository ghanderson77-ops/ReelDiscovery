using System.ComponentModel;
using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Generate Corpus step, wizard side: binding, the entry decision (UI-SPEC S1 rules 1-7, never auto-starts), the
/// journal-rebuilt dashboard on reopen, the Next gate, and the run: start/continue/retry off the UI thread, live snapshot
/// draining, checkpoints applied on the UI thread, cancel, Start Over, crash recovery on reopen and opening an EML.
/// </summary>
public partial class StepGenerateCorpus : IWizardStep
{
    private Guid _boundCaseId = Guid.Empty;

    private CancellationTokenSource? _cts;
    // The run in flight, so closing can wait for it to unwind before the close-time save
    private Task? _activeRun;
    // Workers write it; the UI thread drains it every 250 ms (RUN-07)
    private GenerationSnapshot? _snapshot;
    // Checkpoints posted to the UI thread and not yet applied; released on dispose so the runner can finish
    private readonly object _pendingLock = new();
    private readonly List<TaskCompletionSource> _pendingCheckpoints = new();
    // Set under _pendingLock when the step is disposing: later checkpoints complete at once instead of posting (WR-03)
    private bool _closing;
    // The thread that created the step; only it may apply a checkpoint inline
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    // The last key/story write failure shown during the run; it outranks the outcome line at the end
    private (string Text, Color Color)? _fileErrorStatus;

    /// <summary>
    /// Yes/No confirmation (title, text, icon); default button = the safe choice. Test seam.
    /// </summary>
    internal Func<string, string, MessageBoxIcon, bool> Confirm = null!;

    /// <summary>
    /// Builds the run's gateway; null uses WizardState.CreateGateway. Test seam (fake provider).
    /// </summary>
    internal Func<ILlmGateway>? GatewayFactory;

    // Test seam: called after each checkpoint is applied to the draft
    internal Action? CheckpointApplied;

    private CaseDraft Draft => _state.Case;

    /// <summary>
    /// What entering the step found on disk: the replay and its projections, built off the UI thread.
    /// </summary>
    private sealed class EntryData
    {
        public JournalReplay Replay = new();
        public GenerationRunState State = new();
        public GenerationView? View;
        // Set when the journal was re-projected on entry (crash recovery)
        public NarrativeLedger? Ledger;
        public GenerationFileResult? KeyResult;
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
        if (_isGenerating)
            return;

        // Another case was opened: start from an empty dashboard and status line
        if (Draft.CaseId != _boundCaseId)
        {
            _boundCaseId = Draft.CaseId;
            ClearActivity();
            SetStatus(string.Empty, Theme.TextMuted);
        }

        RefreshHeader();
        // A referenced plan that does not load reads as "no plan" here; Review Plan shows its unreadable card
        CorpusPlanStore.EnsureLoaded(Draft);
        var corpus = Draft.Corpus;
        var plan = corpus?.Plan;
        var input = new GenerationEntryInput
        {
            Running = _isGenerating,
            HasPlan = plan != null,
            Approved = corpus?.Approved == true
        };

        EntryData? data = null;
        if (plan != null && input.Approved)
        {
            string folder;
            try
            {
                folder = _state.CaseStore.EnsureFolder(Draft);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The case folder is offline: nothing on disk can be read, so the run is shown as unreadable
                folder = string.Empty;
                input.UnreadableReason = ex.Message;
            }

            if (folder.Length > 0)
            {
                var guide = Draft.Guide ?? new CaseGuide();
                var model = _state.StoryModelConfig;
                var cached = corpus!.Generation;
                var cachedPlanId = cached?.RunPlanId;
                var cachedProjected = cached?.ProjectedUtc;
                // Replay and projections run off the UI thread (200 MB cap in the journal, T-05-45); never a provider call (T-05-51)
                data = await Task.Run(() => ReadEntryAsync(folder, plan, guide, model, cachedPlanId, cachedProjected));
                if (IsDisposed || _isGenerating)
                    return;

                if (data.Ledger != null)
                {
                    // Crash recovery: the journal is the source of truth (D-05, Pitfall 4)
                    GenerationStateApplier.Apply(Draft, plan, data.State, data.Ledger, data.KeyResult?.Files ?? new List<WrittenCorpusFile>(),
                        GenerationOutcome.None, string.Empty, DateTime.UtcNow, final: false);
                    if (data.KeyResult != null)
                    {
                        Draft.Corpus!.Generation!.KeyError = FileErrorOf(data.KeyResult);
                        ShowFileError(data.KeyResult);
                    }

                    await SaveAsync();
                    if (IsDisposed || _isGenerating)
                        return;
                }

                input.HasSavedRun = data.Replay.Exists && (data.State.HasRun || data.Replay.PlanChanged);
                input.PlanChanged = data.Replay.PlanChanged;
                input.UnreadableReason = data.Replay.Reason;
                input.Total = data.State.TotalThreads;
                input.Written = data.State.Written + data.State.Warnings;
                input.Failed = data.State.Failed;
            }
        }

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
            case GenerationEntry.PlanChangedCard:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.PlanChanged, 0, 0, 0, 0m, string.Empty,
                    data?.Replay.ForeignThreadCount ?? 0, string.Empty), GenerationStateCard.PlanChanged);
                return;
            case GenerationEntry.UnreadableCard:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.Unreadable, 0, 0, 0, 0m, string.Empty, 0,
                    input.UnreadableReason), GenerationStateCard.Unreadable);
                return;
            case GenerationEntry.ReadyCard:
                ShowStateCard(ReadyCard(plan!), GenerationStateCard.Ready);
                return;
            default:
                ClearActivity();
                ApplyView(data!.View!);
                var counts = data.View!.Counts;
                ShowDashboard(decision == GenerationEntry.PartialDashboard, counts.DoneThreads, counts.TotalThreads,
                    counts.FailedThreads, counts.FailedThreads > 0);
                return;
        }
    }

    /// <summary>
    /// Next gate (UI-SPEC U7): disabled during a run; otherwise a partial corpus is a valid result and Next advances.
    /// </summary>
    public Task<bool> ValidateStepAsync() => Task.FromResult(!_isGenerating);

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

    // ----- Run (RUN-01, RUN-02, RUN-06, RUN-07) -----

    private void WireRunHandlers()
    {
        // Default button = the safe choice (No) for every confirmation on this step
        Confirm = (title, text, icon) => MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, icon,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        _refreshTimer.Tick += (s, e) => DrainSnapshot();
        _btnStateAction.Click += async (s, e) => await ClickStateActionAsync();
        _btnContinue.Click += async (s, e) => await StartRunAsync();
        _btnRetryFailed.Click += async (s, e) => await StartRunAsync();
        _btnCancel.Click += (s, e) => RequestCancel();
        _btnStartOver.Click += async (s, e) => await StartOverAsync();
        _gridActivity.CellDoubleClick += (s, e) => OpenEml(ActivityEventAt(e.RowIndex));
        _gridActivity.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter || _gridActivity.CurrentRow == null)
                return;

            // Enter opens the row instead of moving to the next one
            e.Handled = true;
            e.SuppressKeyPress = true;
            OpenEml(ActivityEventAt(_gridActivity.CurrentRow.Index));
        };
    }

    /// <summary>
    /// The state card's single action: Generate Emails starts the run; Review Plan opens that step; Start Over... clears
    /// a run for another plan or an unreadable run.
    /// </summary>
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
                return StartOverAsync();
        }
    }

    /// <summary>
    /// Continue Generating on the partial-run banner (the same run as Generate Emails, U5).
    /// </summary>
    internal Task ClickContinueAsync() => StartRunAsync();

    // Generate Emails, Continue Generating and Retry Failed Threads are one run: it writes every thread the journal does
    // not list as done (UI-SPEC U5, no confirmation)
    private Task StartRunAsync() => TrackRun(RunCoreAsync());

    private Task TrackRun(Task run)
    {
        if (!run.IsCompleted)
            _activeRun = run;
        return run;
    }

    /// <summary>
    /// Closing during a run (C-G3): cancels the run and pumps messages until it has unwound. In-flight threads are
    /// discarded; finished ones are journaled and their final checkpoint is applied. Returns false on timeout.
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

        // The run's continuations and checkpoints resume on this UI thread
        var clock = Stopwatch.StartNew();
        while (!run.IsCompleted && clock.Elapsed < timeout)
        {
            Application.DoEvents();
            if (!run.IsCompleted)
                Thread.Sleep(15);
        }

        return run.IsCompleted;
    }

    private async Task RunCoreAsync()
    {
        if (_isGenerating || _isResetting || _state == null)
            return;

        var plan = Draft.Corpus?.Plan;
        if (plan == null || Draft.Corpus?.Approved != true)
            return;

        string folder;
        ILlmGateway gateway;
        try
        {
            folder = _state.CaseStore.EnsureFolder(Draft);
            gateway = GatewayFactory?.Invoke() ?? _state.CreateGateway();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No folder or no model: nothing was started or billed
            SetStatus(ErrorText(ex), Theme.Danger);
            return;
        }

        // Checkpoints are marshalled with BeginInvoke, which needs a window handle on this (UI) thread
        _ = Handle;

        var cts = new CancellationTokenSource();
        _cts = cts;
        _isGenerating = true;
        var snapshot = new GenerationSnapshot();
        _snapshot = snapshot;
        _fileErrorStatus = null;
        _btnCancel.Enabled = true;
        _progress.Marquee = false;
        _progress.Value = 0;
        SetRunChrome(true);
        RefreshHeader();
        SetStatus(string.Empty, Theme.TextMuted);
        RaiseStateChanged();
        _refreshTimer.Start();

        GenerationRunResult? result = null;
        try
        {
            var runner = new GenerationRunner(gateway);
            var input = new GenerationRunInput { Draft = Draft, Plan = plan, CaseFolder = folder };
            var token = cts.Token;
            // Engine runs on the pool; workers only write the snapshot (RUN-07, Pitfall 7)
            result = await Task.Run(() => runner.RunAsync(input, snapshot, PersistOnUiThreadAsync, token));
        }
        catch (OperationCanceledException)
        {
            // The runner ends cancelled runs itself; a cancel before it started leaves nothing to show
        }
        catch (Exception ex)
        {
            // UI boundary: the runner never throws by contract, so this is a defect; finished threads are journaled
            if (!IsDisposed)
                SetStatus(ErrorText(ex), Theme.Danger);
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            if (!IsDisposed)
            {
                _refreshTimer.Stop();
                ApplyView(snapshot.Drain());
                _isGenerating = false;
                _btnCancel.Enabled = true;
                SetRunChrome(false);
            }
        }

        if (IsDisposed)
            return;

        if (result != null)
            ShowRunResult(plan, result);
        else
            ShowDashboardFromDraft();
        RefreshHeader();
        RaiseStateChanged();
    }

    private void DrainSnapshot()
    {
        if (IsDisposed || !_isGenerating || _snapshot == null)
            return;

        ApplyView(_snapshot.Drain());
    }

    /// <summary>
    /// After a run: the dashboard with the banner when threads are left (or Retry Failed Threads when the run finished
    /// with failures) and the outcome line; a key or story file that could not be written takes the status line.
    /// </summary>
    private void ShowRunResult(CorpusPlan plan, GenerationRunResult result)
    {
        if (result.Outcome == GenerationOutcome.None)
        {
            // The run did not start (plan changed or unreadable journal): show what entering the step would show
            var message = result.Message;
            _ = ReenterAsync(message);
            return;
        }

        var state = result.State;
        var counts = state.ToCounts(plan, WavePlanner.Plan(plan).Count, result.Ledger.Entries.Count,
            GenerationRunState.EstimateRemaining(plan, state, _state.StoryModelConfig), running: false, finished: state.Pending == 0);
        ApplyView(new GenerationView { Counts = counts, Waves = GenerationHistory.Waves(plan, state) });
        var partial = counts.DoneThreads + counts.FailedThreads < counts.TotalThreads;
        ShowDashboard(partial, counts.DoneThreads, counts.TotalThreads, counts.FailedThreads, counts.FailedThreads > 0);

        var generation = Draft.Corpus?.Generation;
        var fileError = result.Outcome is GenerationOutcome.Finished or GenerationOutcome.Cancelled
            && generation != null && (generation.KeyError.Length > 0 || generation.StoryError.Length > 0);
        if (fileError && _fileErrorStatus != null)
            SetStatus(_fileErrorStatus.Value.Text, _fileErrorStatus.Value.Color);
        else
            ShowOutcome(result.Outcome, counts, result.Message);
    }

    // After an unexpected exception: the counts the last checkpoint stored
    private void ShowDashboardFromDraft()
    {
        var g = Draft.Corpus?.Generation;
        if (g == null)
        {
            _ = ReenterAsync(string.Empty);
            return;
        }

        var done = g.ThreadsWritten + g.ThreadsWithWarnings;
        ShowDashboard(done + g.ThreadsFailed < g.ThreadsTotal, done, g.ThreadsTotal, g.ThreadsFailed, g.ThreadsFailed > 0);
    }

    private async Task ReenterAsync(string message)
    {
        await OnEnterStepAsync();
        if (!IsDisposed && message.Length > 0)
            SetStatus(message, Theme.Danger);
    }

    // ----- Checkpoints on the UI thread (T-05-42, T-05-48) -----

    /// <summary>
    /// Persist callback for the runner (called on a pool thread): applies the checkpoint to the draft on the UI thread
    /// and completes when it is applied and saved. The draft is never touched from a worker. Internal for tests.
    /// </summary>
    internal Task PersistOnUiThreadAsync(GenerationCheckpoint checkpoint)
    {
        if (IsDisposed || Disposing)
            return Task.CompletedTask;

        // InvokeRequired is false when no window handle exists (the step is disposing), so it cannot tell a worker from the
        // UI thread; the creating thread id can, and a worker never applies a checkpoint inline (T-05-42)
        if (Environment.CurrentManagedThreadId == _uiThreadId)
            return ApplyCheckpointAsync(checkpoint);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock)
        {
            // Released already: a posted callback could be dropped with the handle and the runner would wait forever
            if (_closing)
                return Task.CompletedTask;
            _pendingCheckpoints.Add(tcs);
        }

        try
        {
            BeginInvoke(new Action(async () =>
            {
                try
                {
                    await ApplyCheckpointAsync(checkpoint);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    lock (_pendingLock)
                        _pendingCheckpoints.Remove(tcs);
                }
            }));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The window handle is gone (closing): the journal, EMLs and key are already on disk and reopen re-projects them
            lock (_pendingLock)
                _pendingCheckpoints.Remove(tcs);
            tcs.TrySetResult();
        }

        return tcs.Task;
    }

    // A disposed step never runs its posted callbacks; release the runner so it can finish unwinding
    private void ReleasePendingCheckpoints()
    {
        List<TaskCompletionSource> pending;
        lock (_pendingLock)
        {
            _closing = true;
            pending = _pendingCheckpoints.ToList();
            _pendingCheckpoints.Clear();
        }

        foreach (var tcs in pending)
            tcs.TrySetResult();
    }

    private async Task ApplyCheckpointAsync(GenerationCheckpoint checkpoint)
    {
        if (IsDisposed || _state == null)
            return;

        var plan = Draft.Corpus?.Plan;
        if (plan == null)
            return;

        GenerationStateApplier.Apply(Draft, plan, checkpoint.State, checkpoint.Ledger, checkpoint.Files, checkpoint.Outcome,
            checkpoint.Message, DateTime.UtcNow, checkpoint.IsFinal);
        var generation = Draft.Corpus!.Generation!;
        if (checkpoint.KeyResult != null)
            generation.KeyError = FileErrorOf(checkpoint.KeyResult);
        if (checkpoint.StoryResult != null)
            generation.StoryError = FileErrorOf(checkpoint.StoryResult);
        ShowFileError(checkpoint.KeyResult);
        ShowFileError(checkpoint.StoryResult);
        CheckpointApplied?.Invoke();

        await SaveAsync();
    }

    private static string FileErrorOf(GenerationFileResult result) =>
        result.Succeeded ? string.Empty : $"{result.FailedFileName}: {result.Error}";

    // Locked (open in Excel/Word) reads as Warning; any other write failure as Danger (UI-SPEC outcome table)
    private void ShowFileError(GenerationFileResult? result)
    {
        if (IsDisposed)
            return;
        if (result == null || result.Succeeded)
            return;

        var text = result.FileOpenElsewhere
            ? GenerationUiText.AnswerKeyLockedText(result.FailedFileName)
            : GenerationUiText.FileWriteFailedText(result.FailedFileName, result.Error);
        var color = result.FileOpenElsewhere ? Theme.Warning : Theme.Danger;
        _fileErrorStatus = (text, color);
        SetStatus(text, color);
    }

    // ----- Cancel (C-G1) -----

    internal void RequestCancel()
    {
        if (!_isGenerating || _cts == null || !_btnCancel.Enabled)
            return;

        var done = _lastCounts?.DoneThreads ?? 0;
        var (title, text) = GenerationUiText.CG1CancelGeneration(done);
        if (!Confirm(title, text, MessageBoxIcon.Question))
            return;

        // The run may have finished while the confirmation was open (WR-09)
        if (!_isGenerating || _cts == null)
            return;

        _btnCancel.Enabled = false;
        SetStatus(GenerationUiText.StoppingText, Theme.Accent);
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished while the confirmation was open; nothing left to cancel
        }
    }

    private static string ErrorText(Exception ex) =>
        ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;

    // ----- Start Over (C-G2, UI-SPEC U6) -----

    internal Task ClickStartOverAsync() => StartOverAsync();

    /// <summary>
    /// Deletes the generated EMLs, the journal, the ledger and the story file after C-G2 (Warning, default No), then
    /// re-projects the answer key to Planned. The plan, the guide and case.json stay.
    /// </summary>
    private async Task StartOverAsync()
    {
        if (_state == null || _isGenerating || _isResetting)
            return;

        var plan = Draft.Corpus?.Plan;
        string folder;
        try
        {
            folder = _state.CaseStore.EnsureFolder(Draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(ErrorText(ex), Theme.Danger);
            return;
        }

        // The journal can be large; counting its files stays off the UI thread
        var files = await Task.Run(() => GenerationJournal.ListAllFiles(folder).Count);
        if (IsDisposed || _isGenerating || _isResetting)
            return;

        var (title, text) = GenerationUiText.CG2StartOver(files);
        if (!Confirm(title, text, MessageBoxIcon.Warning))
            return;

        _isResetting = true;
        _btnStartOver.Enabled = false;
        _btnStateAction.Enabled = false;
        RaiseStateChanged();
        try
        {
            // Deletes only journal-listed and planned relevant EML paths under Mailboxes plus two constant-name files (T-05-49)
            var reset = await GenerationReset.StartOverAsync(folder, plan, Draft.Guide);
            if (IsDisposed)
                return;

            GenerationStateApplier.Clear(Draft);
            GenerationFileResult? key = null;
            if (plan != null && Draft.Corpus?.Approved == true)
            {
                key = await GenerationOutputs.WriteAnswerKeyAsync(folder, Draft.Guide ?? new CaseGuide(), plan, new AnswerKeyOverlay());
                if (IsDisposed)
                    return;

                CorpusApproval.RecordPartialWrites(Draft, key.Files);
            }

            await SaveAsync();
            if (IsDisposed)
                return;

            _isResetting = false;
            await OnEnterStepAsync();
            if (IsDisposed)
                return;

            if (reset.Errors.Count > 0)
                SetStatus($"Some files could not be deleted: {reset.Errors[0]}", Theme.Danger);
            else if (key is { Succeeded: false })
                ShowFileError(key);
            else
                SetStatus(GenerationUiText.StartedOverText(reset.FilesDeleted), Theme.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // UI boundary: files that were deleted stay deleted; the journal (if still there) is reread on the next entry
            if (!IsDisposed)
                SetStatus(ErrorText(ex), Theme.Danger);
        }
        finally
        {
            if (!IsDisposed)
            {
                _isResetting = false;
                _btnStartOver.Enabled = true;
                _btnStateAction.Enabled = true;
                RaiseStateChanged();
            }
        }
    }

    // ----- Leaving the step (Pitfall 8) -----

    /// <summary>
    /// Retries an answer-key or story-so-far write that failed during the run (a file open in Excel or Word), once, and
    /// saves. Never throws to the shell: a file still locked keeps its error for the next leave.
    /// </summary>
    public async Task OnLeaveStepAsync()
    {
        if (_state == null || _isGenerating || _isResetting)
            return;

        var corpus = Draft.Corpus;
        var plan = corpus?.Plan;
        var generation = corpus?.Generation;
        if (plan == null || generation == null || corpus!.Approved != true || generation.RunPlanId != plan.PlanId)
            return;

        var retryKey = generation.KeyError.Length > 0;
        var retryStory = generation.StoryError.Length > 0 && generation.Ledger.Entries.Count > 0;
        if (!retryKey && !retryStory)
            return;

        var folder = Draft.FolderPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        var guide = Draft.Guide ?? new CaseGuide();
        var ledger = generation.Ledger;
        var draft = Draft;
        GenerationFileResult? key = null;
        GenerationFileResult? story = null;
        try
        {
            (key, story) = await Task.Run(async () =>
            {
                var replay = GenerationJournal.Replay(folder, plan);
                // An unreadable or foreign journal would reset the key to Planned; the entry card handles those
                if (!replay.Readable || replay.PlanChanged)
                    return ((GenerationFileResult?)null, (GenerationFileResult?)null);

                var state = GenerationRunState.From(plan, replay);
                var k = retryKey
                    ? await GenerationOutputs.WriteAnswerKeyAsync(folder, guide, plan, state.BuildOverlay(plan, RenderAssignments.Compute(plan)))
                    : null;
                var s = retryStory
                    ? await GenerationOutputs.WriteStorySoFarAsync(folder, draft, plan, ledger, state, PeopleDirectory.Create(guide, plan))
                    : null;
                return (k, s);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The writers report locked files in their results; anything else keeps the error for the next leave
            return;
        }

        var written = new List<WrittenCorpusFile>();
        if (key != null)
        {
            generation.KeyError = FileErrorOf(key);
            written.AddRange(key.Files);
        }
        if (story != null)
        {
            generation.StoryError = FileErrorOf(story);
            written.AddRange(story.Files);
        }
        if (written.Count == 0 && key == null && story == null)
            return;

        CorpusApproval.RecordPartialWrites(Draft, written);
        await SaveAsync();
    }

    // ----- Opening an EML from the activity grid (U15) -----

    /// <summary>
    /// Opens a file through the shell. Test seam.
    /// </summary>
    internal Action<string> OpenFile = path =>
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    };

    private GenerationEvent? ActivityEventAt(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _gridActivity.Rows.Count ? (_gridActivity.Rows[rowIndex].Tag as DotTag)?.Event : null;

    /// <summary>
    /// Written and Kept-with-warnings rows open the thread's last EML. The path must pass IsSafeEmlPath and resolve under
    /// the case folder (T-05-47); nothing else is ever opened.
    /// </summary>
    internal void OpenEml(GenerationEvent? e)
    {
        if (e == null || e.Kind is not (GenerationEventKind.ThreadWritten or GenerationEventKind.KeptWithWarnings))
            return;

        var folder = Draft.FolderPath;
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var full = MailboxWriter.ResolveUnderCase(folder, e.FilePath);
        if (full == null || !full.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(full))
        {
            SetStatus(GenerationUiText.MissingEmlText(Path.GetFileNameWithoutExtension(full)), Theme.Danger);
            return;
        }

        try
        {
            OpenFile(full);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus($"Could not open {Path.GetFileName(full)}: {ex.Message.Trim().TrimEnd('.')}.", Theme.Danger);
        }
    }

    // ----- Entry helpers -----

    private static async Task<EntryData> ReadEntryAsync(string folder, CorpusPlan plan, CaseGuide guide, AIModelConfig? model,
        Guid? cachedPlanId, DateTime? cachedProjectedUtc)
    {
        var replay = GenerationJournal.Replay(folder, plan);
        var state = GenerationRunState.From(plan, replay);
        var data = new EntryData { Replay = replay, State = state };
        if (!state.HasRun || !replay.Readable || replay.PlanChanged)
            return data;

        data.View = HistoryView(plan, guide, replay, state, model);

        // case.json is behind the journal (killed mid-run, or never projected): rebuild the ledger and the key from it
        if (NeedsProjection(plan, replay, cachedPlanId, cachedProjectedUtc))
        {
            data.Ledger = LedgerMerger.Build(plan, guide, WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan)), state.DeltasByThread);
            data.KeyResult = await GenerationOutputs.WriteAnswerKeyAsync(folder, guide, plan,
                state.BuildOverlay(plan, RenderAssignments.Compute(plan)));
        }

        return data;
    }

    internal static bool NeedsProjection(CorpusPlan plan, JournalReplay replay, Guid? cachedPlanId, DateTime? cachedProjectedUtc) =>
        cachedPlanId == null || cachedPlanId != plan.PlanId || cachedProjectedUtc == null || cachedProjectedUtc < replay.LastWriteUtc;

    /// <summary>
    /// Dashboard of a saved run rebuilt from the journal: counts, ledger size, waves and activity (reopen, RUN-06).
    /// </summary>
    internal static GenerationView HistoryView(CorpusPlan plan, CaseGuide guide, JournalReplay replay, GenerationRunState state,
        AIModelConfig? model)
    {
        var waves = WavePlanner.Plan(plan);
        var ledger = LedgerMerger.Build(plan, guide, WavePlanner.WaveIndexByThread(waves), state.DeltasByThread);
        return new GenerationView
        {
            Counts = state.ToCounts(plan, waves.Count, ledger.Entries.Count,
                GenerationRunState.EstimateRemaining(plan, state, model), running: false, finished: state.Pending == 0),
            Waves = GenerationHistory.Waves(plan, state),
            Events = GenerationHistory.Events(plan, replay, RenderAssignments.Compute(plan))
        };
    }

    private GenerationCardContent ReadyCard(CorpusPlan plan)
    {
        var relevant = RelevantThreads(plan);
        var model = _state.StoryModelConfig;
        var cost = GenerationCostEstimator.Estimate(plan, model).RelevantCost;
        return GenerationUiText.StateCard(GenerationStateCard.Ready, relevant.Count,
            relevant.Sum(t => t.Messages.Count(m => m != null)), WavePlanner.Plan(plan).Count, cost,
            model?.DisplayName ?? _state.StoryModelId, 0, string.Empty);
    }

    private static string NoticeFor(CorpusPlan plan)
    {
        var relevant = RelevantThreads(plan);
        var nonRelevantEmails = (plan.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && t.Relevance != ThreadRelevance.Relevant)
            .Sum(t => t.Messages.Count(m => m != null));
        return GenerationUiText.ScopeNotice(relevant.Count, nonRelevantEmails, plan.AttachmentCount);
    }

    private static List<PlannedThread> RelevantThreads(CorpusPlan plan) =>
        (plan.Threads ?? new List<PlannedThread>())
        .Where(t => t != null && t.Relevance == ThreadRelevance.Relevant && !string.IsNullOrEmpty(t.Id))
        .ToList();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Closing the wizard or switching flows stops generation so no further calls are billed (T-05-50)
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run already finished and released its token source; nothing to cancel
            }

            // No snapshot is applied after the step is gone
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _toolTip.Dispose();
            ReleasePendingCheckpoints();
        }

        base.Dispose(disposing);
    }
}
