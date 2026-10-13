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
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;

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

    /// <summary>
    /// Builds the run's gateway and batching decorator from the case's batch choice; null uses GatewayFactory, then
    /// WizardState.CreateRunGateway. Test seam: fake live provider + fake batch transport.
    /// </summary>
    internal Func<bool, (ILlmGateway Gateway, BatchingLlmProvider? Batch)>? RunGatewayFactory;

    /// <summary>
    /// Batch transport per provider for Start Over's cancel and the D-15 key check; null uses LlmProviderFactory with the
    /// session's keys. Test seam.
    /// </summary>
    internal Func<LlmProviderKind, IBatchTransport?>? BatchTransportFactory;

    // Provider batches of this plan of the current text provider not yet collected (D-15): the ledger count on entry and
    // after a run, the live count during one
    private int _pendingProviderBatches;

    // D-15: this plan's pending batches of the other text provider, resolved at run start (D-13) or blocking it (D-14)
    private int _otherProviderBatches;
    private LlmProviderKind _otherProvider;
    private bool _otherProviderBlocked;

    // What the batch discount took off this case's spend so far (journal on entry, plus this session's runs)
    private decimal _caseBatchSavings;

    // Session batch savings when the current run started, so the cost tile adds only this run's share
    private decimal _runSavingsBase;

    /// <summary>
    /// Provider batches submitted for this plan of the current text provider (D-15) and not yet collected (C-G1..C-G3
    /// batch clauses, the banner clause).
    /// </summary>
    internal int PendingProviderBatches => _pendingProviderBatches;

    /// <summary>
    /// D-15: this plan's pending batches of the other text provider (the banner's resolve clause or D-14 notice).
    /// </summary>
    internal int OtherProviderBatches => _otherProviderBatches;

    // D-14: the other provider's batches are pending and no transport can be built for it
    internal bool OtherProviderBlocked => _otherProviderBlocked;

    // D-08: the case's saved batch choice; Continue Generating, Retry Failed Items and Create Skipped Media reuse it
    private bool CaseUseBatch => _state?.Case?.Corpus?.Generation?.UseBatch == true;

    // Test seam: called after each checkpoint is applied to the draft
    internal Action? CheckpointApplied;

    /// <summary>
    /// Called with each run's input before the runner starts; tests read it or swap Media for a fake. Test seam.
    /// </summary>
    internal Action<GenerationRunInput>? RunInputCreated;

    // Whether the last run had media (input.Media != null): its Stages rows use the same rule as the runner (checker 06-23)
    private bool _runMediaAvailable;

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
        // Provider batches of this plan still pending in the ledger for the current text provider (07 S1 banner clause)
        public int PendingBatches;
        // D-15: this plan's pending batches of the other text provider
        public int OtherProviderBatches;
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
        _pendingProviderBatches = 0;
        _otherProviderBatches = 0;
        _otherProviderBlocked = false;
        _caseBatchSavings = 0m;
        // A referenced plan that does not load reads as "no plan" here; Review Plan shows its unreadable card
        CorpusPlanStore.EnsureLoaded(Draft);
        var corpus = Draft.Corpus;
        var plan = corpus?.Plan;
        var input = new GenerationEntryInput
        {
            Running = _isGenerating,
            HasPlan = plan != null,
            Approved = corpus?.Approved == true,
            // Computed before withdrawal (09 W1): a guide edited after planning pauses generation
            GuideStale = CorpusApproval.IsGuideStale(Draft)
        };

        EntryData? data = null;
        if (plan != null && input.Approved && !input.GuideStale)
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
                // What the next Continue would use: the session's models, prices and media availability
                var pricing = new EntryPricing
                {
                    Story = _state.StoryModelConfig,
                    Volume = _state.VolumeModelConfig,
                    Prices = _state.MediaPrices.Clone(),
                    MediaAvailable = _state.IsMediaAvailable,
                    Batch = CaseUseBatch
                };
                var cached = corpus!.Generation;
                var cachedPlanId = cached?.RunPlanId;
                var cachedProjected = cached?.ProjectedUtc;
                // Replay and projections run off the UI thread (200 MB cap in the journal, T-05-45); never a provider call (T-05-51)
                var textProvider = _state.TextProvider;
                data = await Task.Run(() => ReadEntryAsync(folder, plan, guide, pricing, textProvider, cachedPlanId, cachedProjected));
                if (IsDisposed || _isGenerating)
                    return;

                _pendingProviderBatches = data.PendingBatches;
                SetOtherProviderBatches(data.OtherProviderBatches);
                _caseBatchSavings = data.Replay.SpentDetail?.BatchSavingsUsd ?? 0m;

                if (data.Ledger != null)
                {
                    // Crash recovery: the journal is the source of truth (D-05, Pitfall 4)
                    GenerationStateApplier.Apply(Draft, plan, data.State, data.Ledger, data.KeyResult?.Files ?? new List<WrittenCorpusFile>(),
                        GenerationOutcome.None, string.Empty, DateTime.UtcNow, final: false);
                    if (data.KeyResult != null)
                    {
                        Draft.Corpus!.Generation!.KeyError = FileErrorOf(data.KeyResult);
                        ShowFileError(data.KeyResult);
                        ShowKeyBackups(data.KeyResult);
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
                input.AttachmentsTotal = data.State.AttachmentsTotal;
                input.AttachmentsFinal = AttachmentsFinal(data.State);
                // The newest attempt only (an older RunEnded never counts); a cancelled or stopped attempt also journals
                // RunEnded, so only a Finished one makes it "ended" (UI-SPEC Row 2, review MEDIUM 06-13/06-23)
                input.LatestAttemptEnded = data.State.LatestAttemptEnded
                    && data.Replay.LastRunEnded?.Outcome == GenerationOutcome.Finished;
            }
        }

        SetNotice(plan == null ? string.Empty : NoticeFor(plan));
        var decision = GenerationUiText.EntryDecision(input);
        switch (decision)
        {
            case GenerationEntry.KeepRun:
                return;
            case GenerationEntry.GuideChangedCard:
                // 09 W1 / D-25: the plan was built from an earlier guide; approval is withdrawn and saved before the card shows
                if (CorpusApproval.WithdrawIfGuideChanged(Draft))
                {
                    await SaveAsync();
                    if (IsDisposed || _isGenerating)
                        return;
                }
                ShowGuideChangedCard();
                return;
            case GenerationEntry.NotApprovedCard:
                SetNotice(string.Empty);
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty,
                    hasPlan: plan != null), GenerationStateCard.NotApproved);
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
                ShowResultDashboard(decision == GenerationEntry.PartialDashboard, input.LatestAttemptEnded, data.State,
                    _state.IsMediaAvailable);
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
        // Continue Generating, Create Skipped Media and Retry Failed and Skipped Items are this one banner button
        _btnContinue.Click += async (s, e) => await ClickBannerButtonAsync();
        _btnRetryFailed.Click += async (s, e) => await ClickRetryFailedAsync();
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
    /// The state card's single action: Generate Emails starts the run; Review Plan opens that step (Not approved and
    /// Guide changed cards); Start Over... clears a run for another plan or an unreadable run. Only the Plan-changed and
    /// Unreadable cards can delete anything (09 Pitfall 1).
    /// </summary>
    internal Task ClickStateActionAsync()
    {
        switch (_cardKind)
        {
            case GenerationStateCard.Ready:
                return StartRunAsync();
            case GenerationStateCard.GuideChanged:
            case GenerationStateCard.NotApproved:
                (FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan);
                return Task.CompletedTask;
            case GenerationStateCard.PlanChanged:
            case GenerationStateCard.Unreadable:
                return StartOverAsync();
            default:
                // An unknown card never deletes anything (09 Pitfall 1)
                return Task.CompletedTask;
        }
    }

    // The Guide-changed card (09 UI-SPEC S1): the plan's notice is hidden; the body names the emails already written
    private void ShowGuideChangedCard()
    {
        SetNotice(string.Empty);
        ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.GuideChanged, 0, 0, 0, 0m, string.Empty, 0, string.Empty,
            Draft.Corpus?.Generation?.EmailsWritten ?? 0), GenerationStateCard.GuideChanged);
    }

    /// <summary>
    /// Continue Generating on the partial-run banner (the same run as Generate Emails, U5).
    /// </summary>
    internal Task ClickContinueAsync() => StartRunAsync();

    /// <summary>
    /// The banner button: Continue Generating, Create Skipped Media or Retry Failed and Skipped Items (D-25).
    /// </summary>
    internal Task ClickBannerButtonAsync() => StartRunAsync();

    /// <summary>
    /// Header Retry Failed Items (D-26): the same run, which repairs the failed items of written threads.
    /// </summary>
    internal Task ClickRetryFailedAsync() => StartRunAsync();

    // Generate Emails, Continue Generating, Retry Failed Items, Create Skipped Media and Retry Failed and Skipped Items are
    // one run: it writes every thread and attachment the journal does not list as final, plus skipped media when media is
    // available (UI-SPEC U5, no confirmation). RunCoreAsync's _isGenerating guard prevents a second run (T-06-69)
    private Task StartRunAsync() => TrackRun(RunCoreAsync());

    private Task TrackRun(Task run)
    {
        if (!run.IsCompleted)
            _activeRun = run;
        return run;
    }

    /// <summary>
    /// How long closing waits for a run to unwind: longer than the batch drain bound, so an upload the provider may
    /// already have accepted gets its Submitted ledger line before the process exits (CX-07). Returns early once done.
    /// </summary>
    internal static readonly TimeSpan CloseWaitTimeout = new BatchTimings().DrainTimeout + TimeSpan.FromSeconds(30);

    /// <summary>
    /// Closing during a run (C-G3): cancels the run and pumps messages until it has unwound. In-flight threads are
    /// discarded; finished ones are journaled and their final checkpoint is applied. Returns false on timeout.
    /// </summary>
    public bool CancelAndWaitForRun(TimeSpan timeout)
    {
        var run = _activeRun;
        if (run == null || run.IsCompleted)
            return true;

        // 10 D-13: a run-start batch resolution finishes (it is never interrupted mid-ledger), then no run starts
        if (_isResolvingBatches)
            _stopAfterResolve = true;

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
        if (_isGenerating || _isResetting || _isResolvingBatches || _state == null)
            return;

        var plan = Draft.Corpus?.Plan;
        // 09 W1: never write emails from a plan whose guide changed or that is not approved; nothing is billed yet
        if (plan != null && CorpusApproval.IsGuideStale(Draft))
        {
            if (CorpusApproval.WithdrawIfGuideChanged(Draft))
            {
                await SaveAsync();
                if (IsDisposed)
                    return;
            }
            ShowGuideChangedCard();
            SetStatus(GenerationUiText.RunRefusedGuideChangedText, Theme.Warning);
            return;
        }
        if (plan == null || Draft.Corpus?.Approved != true)
        {
            SetNotice(string.Empty);
            ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty,
                hasPlan: plan != null), GenerationStateCard.NotApproved);
            SetStatus(GenerationUiText.RunRefusedNotApprovedText, Theme.Warning);
            return;
        }

        string folder;
        ILlmGateway gateway;
        BatchingLlmProvider? batch;
        // D-08: the saved choice, so Continue Generating and the retry buttons run the way the case was set up
        var useBatch = CaseUseBatch;
        try
        {
            folder = _state.CaseStore.EnsureFolder(Draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No folder: nothing was started or billed
            SetStatus(ErrorText(ex), Theme.Danger);
            return;
        }

        // 10 D-13/D-14/D-22: another provider's pending batches of this plan are collected or cancelled before the run's
        // gateway exists (its Recover reads the ledger once, Pitfall 11), in batch and live mode alike (Pitfall 12).
        // Without a ledger file there is nothing to resolve, and the run starts synchronously as before
        OtherProviderResolution? resolution = null;
        if (File.Exists(BatchLedger.PathFor(folder)))
        {
            _isResolvingBatches = true;
            _stopAfterResolve = false;
            RaiseStateChanged();
            try
            {
                var transportFor = TransportFor();
                var current = _state.TextProvider;
                var planId = plan.PlanId;
                resolution = await Task.Run(() => BatchingLlmProvider.ResolveOtherProviderPendingAsync(new BatchLedger(folder),
                    transportFor, planId, current, CancellationToken.None));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // D-22: never run anyway when another provider's batches could not be checked
                resolution = new OtherProviderResolution { Provider = OtherThan(_state.TextProvider), Failed = true, FailureMessage = ex.Message };
            }
            finally
            {
                _isResolvingBatches = false;
                RaiseStateChanged();
            }
            // Closing (C-G3) or disposing during the resolution: what was resolved stays in the ledger; no run starts
            if (IsDisposed || _stopAfterResolve)
                return;
            if (resolution.Blocked)
            {
                SetOtherProviderBatches(resolution.Found);
                RaiseStateChanged();
                // CR-01: a batch the provider no longer has never blocks; what remains is transient, or a refused key
                SetStatus(resolution.KeyMissing
                    ? GenerationUiText.RunRefusedOtherProviderKeyText(resolution.Provider.ToString())
                    : resolution.FailureKind == LlmFailureKind.Auth
                        ? GenerationUiText.RunRefusedOtherProviderKeyRefusedText(resolution.Provider.ToString())
                        : GenerationUiText.RunRefusedOtherProviderFailedText(resolution.Provider.ToString()), Theme.Warning);
                return;
            }
            SetOtherProviderBatches(0);
        }

        try
        {
            (gateway, batch) = RunGatewayFactory?.Invoke(useBatch)
                ?? (GatewayFactory != null ? (GatewayFactory(), null) : _state.CreateRunGateway(useBatch, folder, plan.PlanId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No model: nothing was started or billed
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
        _runSavingsBase = _state.UsageTracker.TotalBatchSavings;
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
            // WR-B05: a QA pass of the previous run must not write its reports while this run changes the journal
            await StopQaAsync();

            var runner = new GenerationRunner(gateway);
            // PROV-06: media only with a tested OpenAI key this session; a media stop never ends the run (UI-SPEC Decision 2).
            // The run keeps its own copy of the prices, so editing them mid-run never changes its estimate or ticker
            var input = new GenerationRunInput
            {
                Draft = Draft,
                Plan = plan,
                CaseFolder = folder,
                Media = _state.CreateMediaGenerator(),
                MediaPrices = _state.MediaPrices.Clone(),
                Batch = batch,
                OtherProviderResolution = resolution is { Found: > 0 } ? resolution : null
            };
            RunInputCreated?.Invoke(input);
            _runMediaAvailable = input.Media != null;
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

        // This run's batch discount joins the case total; the ledger says which batches are still at the provider
        _caseBatchSavings += Math.Max(0m, _state.UsageTracker.TotalBatchSavings - _runSavingsBase);
        _runSavingsBase = _state.UsageTracker.TotalBatchSavings;
        // D-15: the banner counts the current provider's batches and names the other provider's
        var provider = _state.TextProvider;
        (_pendingProviderBatches, var otherBatches) = await Task.Run(() =>
            (BatchLedger.PendingBatchCount(folder, plan.PlanId, provider), BatchLedger.PendingBatchCountOtherProviders(folder, plan.PlanId, provider)));
        if (IsDisposed)
            return;

        SetOtherProviderBatches(otherBatches);

        if (result != null)
            ShowRunResult(plan, result);
        else
            ShowDashboardFromDraft();
        RefreshHeader();
        RaiseStateChanged();

        // D-15 / UI-SPEC U33: fresh QA reports after every run that started, fired after the UI is back so navigation and
        // export never wait for them
        if (result == null || result.Outcome != GenerationOutcome.None)
            _ = RunQaAfterRunAsync(plan, folder);
    }

    /// <summary>
    /// The end-of-run QA pass in flight (scans + leakage audit), or null before the first run end. Test seam.
    /// </summary>
    internal Task? QaRun { get; private set; }

    // Cancelled when the step closes, so a QA pass never outlives the wizard
    private readonly CancellationTokenSource _qaCts = new();

    /// <summary>Test seam: replaces the end-of-run QA pass (plan, case folder, token).</summary>
    internal Func<CorpusPlan, string, CancellationToken, Task>? QaPassOverride;

    // WR-B05: the QA pass in flight; Start Over and a new run cancel it and wait, so it never writes reports afterwards
    private CancellationTokenSource? _qaRunCts;

    private Task RunQaAfterRunAsync(CorpusPlan plan, string folder)
    {
        // A newer run end supersedes a pass still in flight
        CancelQaRun();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_qaCts.Token);
        _qaRunCts = cts;
        var run = RunQaCoreAsync(plan, folder, cts);
        QaRun = run;
        return run;
    }

    private async Task RunQaCoreAsync(CorpusPlan plan, string folder, CancellationTokenSource cts)
    {
        try
        {
            // Null overlay: QaRunner rebuilds it from the journal exactly as the answer-key projection does
            // (GenerationRunState.BuildFullOverlay), off the UI thread
            if (QaPassOverride != null)
                await QaPassOverride(plan, folder, cts.Token);
            else
                await QaRunner.RunEndOfRunAsync(Draft, plan, folder, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // The step is closing, or Start Over / a new run stopped it; the next run end writes fresh reports
            return;
        }
        catch (Exception)
        {
            // QA is report-only (D-15): a failure must never surface as a run error; the reports read as Not run
            return;
        }
        finally
        {
            if (ReferenceEquals(_qaRunCts, cts))
                _qaRunCts = null;
            cts.Dispose();
        }

        // Case Summary reads the reports on entry; tell the shell the step's state moved on
        if (!IsDisposed)
            RaiseStateChanged();
    }

    private void CancelQaRun()
    {
        try
        {
            _qaRunCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The pass already finished and released its token source
        }
    }

    /// <summary>
    /// WR-B05: cancels the end-of-run QA pass and waits for it, so it cannot save reports over a reset or a new run.
    /// Never throws: QA is report-only.
    /// </summary>
    private async Task StopQaAsync()
    {
        CancelQaRun();
        var run = QaRun;
        if (run == null)
            return;

        try
        {
            await run;
        }
        catch (Exception)
        {
            // RunQaCoreAsync catches everything itself; nothing a report pass does may block Start Over or a run
        }
    }

    private void DrainSnapshot()
    {
        if (IsDisposed || !_isGenerating || _snapshot == null)
            return;

        ApplyView(_snapshot.Drain());
    }

    /// <summary>
    /// After a run: the dashboard with the partial-run banner when threads or attachments are left, the media banner when
    /// only skipped media is left, or Retry Failed Items when the run finished with failed items; then the outcome line.
    /// A key or story file that could not be written takes the status line.
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

        // Whole corpus: the runner writes relevant threads, non-relevant threads and attachments (06-19)
        var state = result.State;
        var partial = IsPartial(state);
        // The remaining cost is what the next run would spend: the session's models, prices and media availability
        var counts = state.ToCounts(plan, WavePlanner.Plan(plan).Count, result.Ledger.Entries.Count,
            GenerationRunState.EstimateRemaining(plan, state, _state.StoryModelConfig, _state.VolumeModelConfig,
                _state.MediaPrices, _state.IsMediaAvailable, Draft.Guide, CaseUseBatch),
            running: false, finished: !partial);
        // Stages rows with the mediaAvailable the run used (input.Media != null), so they match the live rows it showed
        ApplyView(new GenerationView { Counts = counts, Waves = GenerationHistory.Waves(plan, state, _runMediaAvailable) });
        var retryShown = ShowResultDashboard(partial, result.Outcome == GenerationOutcome.Finished, state, _state.IsMediaAvailable);

        var generation = Draft.Corpus?.Generation;
        var fileError = result.Outcome is GenerationOutcome.Finished or GenerationOutcome.Cancelled
            && generation != null && (generation.KeyError.Length > 0 || generation.StoryError.Length > 0);
        if (fileError && _fileErrorStatus != null)
            SetStatus(_fileErrorStatus.Value.Text, _fileErrorStatus.Value.Color);
        else
            ShowOutcome(result.Outcome, counts, result.Message, retryShown);
    }

    // After an unexpected exception: the whole-corpus counts the last checkpoint stored (relevant + non-relevant threads)
    private void ShowDashboardFromDraft()
    {
        var g = Draft.Corpus?.Generation;
        if (g == null)
        {
            _ = ReenterAsync(string.Empty);
            return;
        }

        var threadsDone = g.ThreadsWritten + g.ThreadsWithWarnings + g.NonRelevantWritten + g.NonRelevantWithWarnings;
        var threadsFailed = g.ThreadsFailed + g.NonRelevantFailed;
        var threadsTotal = g.ThreadsTotal + g.NonRelevantTotal;
        var attachmentsDone = g.AttachmentsWritten + g.AttachmentsWithWarnings;
        var attachmentsFinal = attachmentsDone + g.AttachmentsFailed + g.AttachmentsSkipped;
        var partial = threadsDone + threadsFailed < threadsTotal || attachmentsFinal < g.AttachmentsTotal;
        ShowResultDashboard(partial, g.LastOutcome == GenerationOutcome.Finished, threadsDone, threadsTotal, attachmentsDone,
            g.AttachmentsTotal, threadsFailed + g.AttachmentsFailed, g.AttachmentsSkipped, _state.IsMediaAvailable, string.Empty);
    }

    // Attachments with a final record: Written + Warnings + Failed + Skipped
    private static int AttachmentsFinal(GenerationRunState state) =>
        state.AttachmentsWritten + state.AttachmentsWarnings + state.AttachmentsFailed + state.AttachmentsSkipped;

    // Unfinished = any thread or attachment without a final record (the GenerationUiText.EntryDecision rule)
    private static bool IsPartial(GenerationRunState state) =>
        state.Pending > 0 || AttachmentsFinal(state) < state.AttachmentsTotal;

    private bool ShowResultDashboard(bool partial, bool runEnded, GenerationRunState state, bool mediaAvailable) =>
        ShowResultDashboard(partial, runEnded, state.Written + state.Warnings, state.TotalThreads,
            state.AttachmentsWritten + state.AttachmentsWarnings, state.AttachmentsTotal, state.Failed + state.AttachmentsFailed,
            state.AttachmentsSkipped, mediaAvailable, MostFrequentSkippedReason(state));

    /// <summary>
    /// UI-SPEC S1 Row 0/2 rules: the partial-run banner when items are left (it outranks the media banner), otherwise the
    /// media banner when media was skipped, otherwise Retry Failed Items when items failed. Returns whether the header's
    /// Retry Failed Items shows, because the outcome line names it only then.
    /// </summary>
    private bool ShowResultDashboard(bool partial, bool runEnded, int threadsDone, int threadsTotal, int attachmentsDone,
        int attachmentsTotal, int failedItems, int skipped, bool mediaAvailable, string skippedReason)
    {
        ShowDashboard(false, threadsDone, threadsTotal, failedItems, !partial && failedItems > 0, runEnded, attachmentsDone,
            attachmentsTotal);
        if (partial)
        {
            ShowPartialBanner(runEnded, threadsDone, threadsTotal, attachmentsDone, attachmentsTotal, failedItems);
            return false;
        }

        if (skipped > 0)
        {
            var media = GenerationUiText.MediaBanner(skipped, failedItems, mediaAvailable, skippedReason);
            ShowMediaBanner(media);
            return failedItems > 0 && media.ButtonText.Length == 0;
        }

        return failedItems > 0;
    }

    /// <summary>
    /// The most frequent reason among the journal's AttachmentSkipped records (ties in ordinal order); empty when none.
    /// Reasons are plain-words strings written by the runner and never carry a key (T-06-70).
    /// </summary>
    internal static string MostFrequentSkippedReason(GenerationRunState state) =>
        state.AttachmentRecords.Values
            .Where(r => r != null && r.Type == JournalRecordType.AttachmentSkipped && !string.IsNullOrWhiteSpace(r.Reason))
            .GroupBy(r => r.Reason.Trim(), StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault() ?? string.Empty;

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
        ShowKeyBackups(checkpoint.KeyResult);
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

    // 10 D-10: a damaged key file was backed up; a warning, never the leave-retry error field (Pitfall 9).
    // A write failure shown by ShowFileError wins (callers show the backup only before any later file error)
    private void ShowKeyBackups(GenerationFileResult? result)
    {
        if (IsDisposed || result == null || !result.Succeeded || result.Backups.Count == 0)
            return;

        var backup = result.Backups[0];
        SetStatus(GenerationUiText.AnswerKeyBackupText(backup.FileName, backup.BackupFileName), Theme.Warning);
    }

    // 09 S5: whether saved discovery documents cite emails (never throws: DiscoveryStore.Load is size-capped and tolerant)
    internal static bool StartOverCitesDiscovery(string folder, Guid caseId) =>
        DiscoveryStaleness.AnyCitesEmails(DiscoveryStore.Load(folder, caseId).Set);

    // ----- Cancel (C-G1) -----

    internal void RequestCancel()
    {
        if (!_isGenerating || _cts == null || !_btnCancel.Enabled)
            return;

        var (title, text) = CancelConfirmation(_lastCounts, _pendingProviderBatches > 0);
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

    // C-G1 with the threads and attachments written so far in this view
    internal static (string Title, string Text) CancelConfirmation(GenerationCounts? counts, bool batchesPending = false) =>
        GenerationUiText.CG1CancelGeneration(counts?.DoneThreads ?? 0, counts?.AttachmentsDone ?? 0, batchesPending);

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
        if (_state == null || _isGenerating || _isResetting || _isResolvingBatches)
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

        // The journal can be large; counting its files (and the batch ledger) stays off the UI thread. WR-A05: pending
        // batches of every plan, since Start Over deletes the whole ledger. 09 S5: the discovery sidecar read too.
        // 10 D-07: relabel count too (PeekRelabels never throws; a failure counts as none)
        var caseId = Draft.CaseId;
        var guide = Draft.Guide ?? new CaseGuide();
        var (files, pending, cited, relabels) = await Task.Run(() =>
            (GenerationJournal.ListAllFiles(folder).Count, BatchLedger.PendingBatchCountAllPlans(folder),
                StartOverCitesDiscovery(folder, caseId),
                plan == null ? 0 : AnswerKeyFiles.PeekRelabels(folder, plan, guide).Count));
        if (IsDisposed || _isGenerating || _isResetting || _isResolvingBatches)
            return;

        var (title, text) = GenerationUiText.CG2StartOver(files, pending > 0, cited, relabelsKept: relabels > 0);
        if (!Confirm(title, text, MessageBoxIcon.Warning))
            return;

        _isResetting = true;
        _btnStartOver.Enabled = false;
        _btnStateAction.Enabled = false;
        RaiseStateChanged();
        var useBatch = CaseUseBatch;
        try
        {
            // WR-B05: an end-of-run QA pass still reading the old journal must not save reports after the reset
            await StopQaAsync();
            if (IsDisposed)
                return;

            var cancelFailed = false;
            if (pending > 0)
                cancelFailed = !await CancelPendingBatchesAsync(folder);
            if (IsDisposed)
                return;

            // D-16/WR-A06: results already collected but never used were paid for; price them before the ledger is deleted.
            // Only when the ledger goes (cancel succeeded); an unreadable ledger prices as empty (Pitfall 13)
            OrphanBill? unused = null;
            if (!cancelFailed)
            {
                unused = await Task.Run(() => BatchingLlmProvider.PriceUnusedResults(new BatchLedger(folder), _state.PriceBatchModel));
                if (IsDisposed)
                    return;
            }

            // Deletes only journal-listed and planned relevant EML paths under Mailboxes plus two constant-name files (T-05-49).
            // WR-A05: a batch that could not be cancelled keeps its ledger record so it is never lost
            StartOverResult reset;
            try
            {
                reset = await GenerationReset.StartOverAsync(folder, plan, Draft.Guide, keepBatchLedger: cancelFailed);
            }
            finally
            {
                // WR-05: recorded only once the ledger is gone; even when the reset failed part way
                RecordUnusedResultsIfLedgerGone(folder, unused);
            }

            if (IsDisposed)
                return;

            GenerationStateApplier.Clear(Draft);
            // D-08: the batch choice belongs to the case, not to the run that was cleared
            if (useBatch && Draft.Corpus != null)
                (Draft.Corpus.Generation ??= new GenerationState()).UseBatch = true;
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
            else if (cancelFailed)
                SetStatus(GenerationUiText.StartedOverText(reset.FilesDeleted) + " " + GenerationUiText.BatchCancelFailedText, Theme.Warning);
            else if (key is { Backups.Count: > 0 })
                SetStatus(GenerationUiText.StartedOverText(reset.FilesDeleted) + " " + GenerationUiText.AnswerKeyBackupText(key.Backups[0].FileName, key.Backups[0].BackupFileName), Theme.Warning);
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

    /// <summary>
    /// Start Over (T-07-53): cancels every pending provider batch in the ledger, whatever plan submitted it (WR-A05), before
    /// the ledger is deleted. Best effort: returns false when a provider could not be reached or has no key, and Start Over
    /// continues but keeps the ledger. Keys go only to the transport constructors (T-07-54).
    /// </summary>
    private async Task<bool> CancelPendingBatchesAsync(string folder)
    {
        var transportFor = TransportFor();

        try
        {
            var notCancelled = await Task.Run(() =>
                BatchingLlmProvider.CancelAllPendingAsync(new BatchLedger(folder), transportFor, CancellationToken.None));
            return notCancelled.Count == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Best effort by design: a provider outage must not block deleting the case's own files
            return false;
        }
    }

    // WR-05: a ledger that survived Start Over (locked, unreadable, or the reset failed) keeps these results unbilled, so a
    // later Finished batch run bills them once; recording them here too would count the same spend twice
    private void RecordUnusedResultsIfLedgerGone(string folder, OrphanBill? bill)
    {
        if (bill == null || bill.Lines.Count == 0 || File.Exists(BatchLedger.PathFor(folder)))
            return;

        foreach (var line in bill.Lines)
            _state.UsageTracker.RecordUsage(BatchingLlmProvider.OrphanOperation, line.Model, line.Usage, line.Cost);
    }

    // Keys go only to the transport constructors (T-07-54); the test seam replaces them
    private Func<LlmProviderKind, IBatchTransport?> TransportFor() => BatchTransportFactory ?? (kind =>
    {
        var key = _state.GetApiKey(kind);
        return string.IsNullOrWhiteSpace(key) ? null : LlmProviderFactory.CreateBatchTransport(kind, key);
    });

    // D-22: a key counts as available when a transport can be built for that provider
    internal bool HasBatchTransport(LlmProviderKind kind) =>
        BatchTransportFactory != null ? BatchTransportFactory(kind) != null : !string.IsNullOrWhiteSpace(_state.GetApiKey(kind));

    // The provider that is not the current text provider (LlmProviderKind has two members)
    private static LlmProviderKind OtherThan(LlmProviderKind current) =>
        current == LlmProviderKind.OpenAI ? LlmProviderKind.Anthropic : LlmProviderKind.OpenAI;

    // D-15: the other provider's pending batch count and whether its missing key blocks the run (D-14)
    private void SetOtherProviderBatches(int count)
    {
        _otherProviderBatches = count;
        _otherProvider = OtherThan(_state.TextProvider);
        _otherProviderBlocked = count > 0 && !HasBatchTransport(_otherProvider);
    }

    // ----- Leaving the step (Pitfall 8) -----

    /// <summary>
    /// Retries an answer-key or story-so-far write that failed during the run (a file open in Excel or Word), once, and
    /// saves. Never throws to the shell: a file still locked keeps its error for the next leave.
    /// </summary>
    public async Task OnLeaveStepAsync()
    {
        if (_state == null || _isGenerating || _isResetting || _isResolvingBatches)
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
                    ? await GenerationOutputs.WriteAnswerKeyAsync(folder, guide, plan, state.BuildFullOverlay(plan, guide))
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
            ShowKeyBackups(key);
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

    // Models, prices (a clone) and media availability for the entry estimate, read on the UI thread
    private sealed class EntryPricing
    {
        public AIModelConfig? Story;
        public AIModelConfig? Volume;
        public MediaPrices Prices = MediaPrices.Defaults();
        public bool MediaAvailable;
        // D-08: the remaining estimate uses batch prices when the case runs in batch mode
        public bool Batch;
    }

    private static async Task<EntryData> ReadEntryAsync(string folder, CorpusPlan plan, CaseGuide guide, EntryPricing pricing,
        LlmProviderKind textProvider, Guid? cachedPlanId, DateTime? cachedProjectedUtc)
    {
        var replay = GenerationJournal.Replay(folder, plan);
        var state = GenerationRunState.From(plan, replay);
        var data = new EntryData
        {
            Replay = replay,
            State = state,
            PendingBatches = BatchLedger.PendingBatchCount(folder, plan.PlanId, textProvider),
            OtherProviderBatches = BatchLedger.PendingBatchCountOtherProviders(folder, plan.PlanId, textProvider)
        };
        if (!state.HasRun || !replay.Readable || replay.PlanChanged)
            return data;

        data.View = HistoryView(plan, guide, replay, state, pricing.Story, pricing.Volume, pricing.Prices, pricing.MediaAvailable,
            pricing.Batch);

        // case.json is behind the journal (killed mid-run, or never projected): rebuild the ledger and the key from it
        if (NeedsProjection(plan, replay, cachedPlanId, cachedProjectedUtc))
        {
            data.Ledger = LedgerMerger.Build(plan, guide, WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan)), state.DeltasByThread);
            data.KeyResult = await GenerationOutputs.WriteAnswerKeyAsync(folder, guide, plan,
                state.BuildFullOverlay(plan, guide));
        }

        return data;
    }

    internal static bool NeedsProjection(CorpusPlan plan, JournalReplay replay, Guid? cachedPlanId, DateTime? cachedProjectedUtc) =>
        cachedPlanId == null || cachedPlanId != plan.PlanId || cachedProjectedUtc == null || cachedProjectedUtc < replay.LastWriteUtc;

    /// <summary>
    /// Dashboard of a saved run rebuilt from the journal: counts, ledger size, waves and activity (reopen, RUN-06).
    /// </summary>
    internal static GenerationView HistoryView(CorpusPlan plan, CaseGuide guide, JournalReplay replay, GenerationRunState state,
        AIModelConfig? story, AIModelConfig? volume, MediaPrices prices, bool mediaAvailable, bool batch = false)
    {
        var waves = WavePlanner.Plan(plan);
        var ledger = LedgerMerger.Build(plan, guide, WavePlanner.WaveIndexByThread(waves), state.DeltasByThread);
        // Same pricing path as the Ready card and the runner (06-15); batch prices when the case runs in batch mode
        var counts = state.ToCounts(plan, waves.Count, ledger.Entries.Count,
            GenerationRunState.EstimateRemaining(plan, state, story, volume, prices, mediaAvailable, guide, batch), running: false,
            finished: !IsPartial(state));
        counts.BatchOn = batch;
        counts.BatchSavingsUsd = replay.SpentDetail?.BatchSavingsUsd ?? 0m;
        return new GenerationView
        {
            Counts = counts,
            // What the next Continue will use (_state.IsMediaAvailable), the same value its runner seeds with
            Waves = GenerationHistory.Waves(plan, state, mediaAvailable),
            Events = GenerationHistory.Events(plan, replay, GenerationRunState.AllAssignments(plan))
        };
    }

    /// <summary>
    /// Ready card (UI-SPEC S1 Row 4): the whole run in order with the D-29 estimate split Story / Volume / media, and the
    /// media notice when media is planned without a tested OpenAI key.
    /// </summary>
    private GenerationCardContent ReadyCard(CorpusPlan plan)
    {
        var relevant = RelevantThreads(plan);
        var nonRelevant = NonRelevantThreads(plan);
        // Same pricing path as EstimateRemaining (06-15)
        var est = GenerationCostEstimator.Estimate(plan, _state.StoryModelConfig, _state.VolumeModelConfig, _state.MediaPrices,
            _state.IsMediaAvailable, Draft?.Guide);
        var media = relevant.Concat(nonRelevant)
            .SelectMany(t => t.Messages.Where(m => m != null))
            .SelectMany(m => (m.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null))
            .Count(a => a.Kind is AttachmentType.Image or AttachmentType.Voicemail);
        return GenerationUiText.ReadyCard(new GenerationReadyInput
        {
            RelevantThreads = relevant.Count,
            NonRelevantThreads = nonRelevant.Count,
            Emails = plan.EmailCount,
            Waves = WavePlanner.Plan(plan).Count,
            Attachments = plan.AttachmentCount,
            StoryCost = est.StoryCost,
            VolumeCost = est.VolumeCost,
            MediaCost = est.MediaCost,
            // D-17: both totals from this one estimate
            LiveTotal = est.Total,
            BatchStoryCost = est.BatchStoryCost,
            BatchVolumeCost = est.BatchVolumeCost,
            BatchTotal = est.BatchTotal,
            BatchChecked = CaseUseBatch,
            StoryModel = est.ModelName.Length > 0 ? est.ModelName : _state.StoryModelId,
            VolumeModel = est.VolumeModelName.Length > 0 ? est.VolumeModelName : _state.VolumeModelId,
            MediaPlanned = media,
            MediaAvailable = _state.IsMediaAvailable
        });
    }

    /// <summary>
    /// The Ready card's batch checkbox (D-08): saves the choice to the case and redraws the card so the hint follows it.
    /// </summary>
    private void OnUseBatchChanged()
    {
        if (_syncingBatch || _state == null || _cardKind != GenerationStateCard.Ready || Draft.Corpus == null)
            return;

        (Draft.Corpus.Generation ??= new GenerationState()).UseBatch = _chkUseBatch.Checked;
        QueueSave();
        var plan = Draft.Corpus.Plan;
        if (plan != null)
            ShowStateCard(ReadyCard(plan), GenerationStateCard.Ready);
    }

    // Non-relevant threads the run writes (an id and at least one message, as GenerationRunState counts them)
    private static List<PlannedThread> NonRelevantThreads(CorpusPlan plan) =>
        (plan.Threads ?? new List<PlannedThread>())
        .Where(t => t != null && t.Relevance == ThreadRelevance.NotRelevant && !string.IsNullOrEmpty(t.Id)
                    && t.Messages?.Any(m => m != null) == true)
        .GroupBy(t => t.Id, StringComparer.Ordinal)
        .Select(g => g.First())
        .ToList();

    private static string NoticeFor(CorpusPlan plan)
    {
        var relevant = RelevantThreads(plan);
        var nonRelevantThreads = (plan.Threads ?? new List<PlannedThread>())
            .Count(t => t != null && t.Relevance != ThreadRelevance.Relevant);
        return GenerationUiText.ScopeNotice(relevant.Count, nonRelevantThreads, plan.AttachmentCount);
    }

    private static List<PlannedThread> RelevantThreads(CorpusPlan plan) =>
        (plan.Threads ?? new List<PlannedThread>())
        .Where(WavePlanner.IsGeneratable)
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

            // A QA pass never outlives the step; its reports are rewritten at the next run end
            try
            {
                _qaCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A second Dispose: the QA token was cancelled and released the first time
            }
            _qaCts.Dispose();

            // No snapshot is applied after the step is gone
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _toolTip.Dispose();
            ReleasePendingCheckpoints();
        }

        base.Dispose(disposing);
    }
}
