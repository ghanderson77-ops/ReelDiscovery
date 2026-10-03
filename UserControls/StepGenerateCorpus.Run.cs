using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Generate Corpus step, wizard side: binding, the entry decision (UI-SPEC S1 rules 1-7, never auto-starts), the
/// journal-rebuilt dashboard on reopen and the Next gate. The run itself is wired by 05-15.
/// </summary>
public partial class StepGenerateCorpus : IWizardStep
{
    private Guid _boundCaseId = Guid.Empty;

    private CaseDraft Draft => _state.Case;

    /// <summary>
    /// What entering the step found on disk: the replay and its projections, built off the UI thread.
    /// </summary>
    private sealed class EntryData
    {
        public JournalReplay Replay = new();
        public GenerationRunState State = new();
        public GenerationView? View;
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
                // Replay and projections run off the UI thread (200 MB cap in the journal, T-05-45)
                data = await Task.Run(() => ReadEntry(folder, plan, guide, model));
                if (IsDisposed || _isGenerating)
                    return;

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
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty));
                return;
            case GenerationEntry.PlanChangedCard:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.PlanChanged, 0, 0, 0, 0m, string.Empty,
                    data?.Replay.ForeignThreadCount ?? 0, string.Empty));
                return;
            case GenerationEntry.UnreadableCard:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.Unreadable, 0, 0, 0, 0m, string.Empty, 0,
                    input.UnreadableReason));
                return;
            case GenerationEntry.ReadyCard:
                ShowStateCard(ReadyCard(plan!));
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

    public Task OnLeaveStepAsync() => Task.CompletedTask;

    /// <summary>
    /// Next gate (UI-SPEC U7): disabled during a run; otherwise a partial corpus is a valid result and Next advances.
    /// </summary>
    public Task<bool> ValidateStepAsync() => Task.FromResult(!_isGenerating);

    // ----- Entry helpers -----

    private static EntryData ReadEntry(string folder, CorpusPlan plan, CaseGuide guide, AIModelConfig? model)
    {
        var replay = GenerationJournal.Replay(folder, plan);
        var state = GenerationRunState.From(plan, replay);
        var data = new EntryData { Replay = replay, State = state };
        if (state.HasRun && replay.Readable && !replay.PlanChanged)
            data.View = HistoryView(plan, guide, replay, state, model);
        return data;
    }

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
            // No snapshot is applied after the step is gone
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}
