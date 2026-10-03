using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// What a generation run works from: the approved draft (read only), its plan and the verified case folder.
/// </summary>
public class GenerationRunInput
{
    public CaseDraft Draft { get; set; } = new();
    public CorpusPlan Plan { get; set; } = new();
    public string CaseFolder { get; set; } = string.Empty;
}

/// <summary>
/// A point in the run where the journal was re-projected: state, ledger and the files rewritten. The step applies it to
/// the draft on the UI thread (GenerationStateApplier) and saves case.json.
/// </summary>
public class GenerationCheckpoint
{
    public GenerationRunState State { get; set; } = new();
    public NarrativeLedger Ledger { get; set; } = new();
    public List<WrittenCorpusFile> Files { get; set; } = new();
    public GenerationFileResult? KeyResult { get; set; }
    public GenerationFileResult? StoryResult { get; set; }
    public bool IsFinal { get; set; }
    public GenerationOutcome Outcome { get; set; }
    public string Message { get; set; } = string.Empty;
}

public delegate Task GenerationPersist(GenerationCheckpoint checkpoint);

public class GenerationRunResult
{
    public GenerationOutcome Outcome { get; set; }
    public string Message { get; set; } = string.Empty;
    public GenerationRunState State { get; set; } = new();
    public NarrativeLedger Ledger { get; set; } = new();
}

/// <summary>
/// Runs relevant-evidence generation: chronological waves, parallel units of work inside a wave, the ledger merged after
/// each wave, debounced key re-projection and checkpoints, and a clean end on finish, cancel, fatal stop or a bug
/// (D-05, D-06, D-11, RUN-01, RUN-02, RUN-06). Generate, Continue and Retry Failed Threads are all this one run: it
/// writes every thread the journal does not list as done.
/// </summary>
public class GenerationRunner
{
    // Matches RelevantThreadFiller.MaxParallelCalls (A2)
    public const int MaxParallelThreads = 3;

    public const string RetryHint = "Click Continue Generating.";

    // Debounced so a burst of finishing threads writes the key at most every 2 s; wave and run end always write it
    public static readonly TimeSpan KeyProjectionInterval = TimeSpan.FromSeconds(2);

    private const string PlanChangedMessage =
        "The plan changed after this run started. Start over to generate emails for the current plan.";

    private readonly ILlmGateway _llm;
    private readonly int _maxParallel;

    public GenerationRunner(ILlmGateway llm, int maxParallel = MaxParallelThreads)
    {
        _llm = llm;
        _maxParallel = Math.Max(1, maxParallel);
    }

    // Test seam: lets tests force an exception outside a unit of work
    internal Func<int, Task>? BeforeWaveHook { get; set; }

    /// <summary>
    /// Writes every relevant thread not yet journaled as done, wave by wave. Never throws: cancel, fatal provider errors
    /// and unexpected exceptions all end with a final checkpoint and a RunEnded record.
    /// </summary>
    public async Task<GenerationRunResult> RunAsync(GenerationRunInput input, GenerationSnapshot snapshot, GenerationPersist? persist = null, CancellationToken ct = default)
    {
        // Threading rule: the draft is only read here (guide, title); workers never mutate it and checkpoints go to the
        // persist callback, which the step marshals to the UI thread (T-05-42)
        var plan = input.Plan;
        var folder = input.CaseFolder;

        var replay = GenerationJournal.Replay(folder, plan);
        if ((!replay.Readable && replay.Exists) || replay.PlanChanged)
        {
            // The UI never starts a run in these states; Start Over is the way out
            return new GenerationRunResult
            {
                Outcome = GenerationOutcome.None,
                Message = replay.PlanChanged ? PlanChangedMessage : replay.Reason,
                State = GenerationRunState.From(plan, replay)
            };
        }

        var state = GenerationRunState.From(plan, replay);
        var guide = input.Draft.Guide ?? new CaseGuide();

        // Whole plan, never the remaining threads: Continue sees the same waves and ledger visibility (Pitfall 3)
        var waves = WavePlanner.Plan(input.Plan);
        var folders = PlanNumbering.CustodianFolders(guide);
        var journal = new GenerationJournal(folder);
        var ctx = new RunContext
        {
            Input = input,
            Guide = guide,
            Waves = waves,
            WaveOf = WavePlanner.WaveIndexByThread(waves),
            Assignments = RenderAssignments.Compute(plan),
            People = PeopleDirectory.Create(guide, plan),
            Journal = journal,
            Snapshot = snapshot,
            Persist = persist,
            StoryModel = _llm.ResolveModel(LlmTier.Story),
            LastProjectionUtc = DateTime.UtcNow
        };
        var uow = new ThreadUnitOfWork(_llm, journal, new MailboxWriter(folder, folders), snapshot, ctx.StoryModel);
        var threads = (plan.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var deltas = new Dictionary<string, List<LedgerDelta>>(state.DeltasByThread, StringComparer.Ordinal);

        var startLedger = LedgerMerger.Build(plan, guide, ctx.WaveOf, deltas);
        ctx.LastGoodState = state;
        ctx.LastGoodLedger = startLedger;
        snapshot.Reset(
            state.ToCounts(plan, waves.Count, startLedger.Entries.Count,
                GenerationRunState.EstimateRemaining(plan, state, ctx.StoryModel), running: true, finished: false),
            waves.Select(w => new WaveProgress
            {
                Index = w.Index,
                Start = w.Start,
                End = w.End,
                Threads = w.ThreadIds.Count,
                Emails = w.MessageCount,
                Done = w.ThreadIds.Count(state.IsDone),
                Warnings = w.ThreadIds.Count(id => Status(state, id) == ThreadRunStatus.Warnings),
                Failed = w.ThreadIds.Count(id => Status(state, id) == ThreadRunStatus.Failed)
            }));

        var pendingTotal = state.TotalThreads - state.Written - state.Warnings;
        snapshot.AddEvent(new GenerationEvent
        {
            Kind = state.Records.Count == 0 ? GenerationEventKind.RunStarted : GenerationEventKind.RunContinued,
            LocalTime = DateTime.Now,
            WaveCount = waves.Count,
            Threads = pendingTotal,
            Total = state.TotalThreads
        });

        var outcome = GenerationOutcome.Finished;
        var message = string.Empty;
        GenerationCheckpoint? final = null;

        using var run = new PlanCallRun(ct, _maxParallel, RetryHint);
        try
        {
            await journal.AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.RunStarted,
                Utc = DateTime.UtcNow,
                PlanId = plan.PlanId
            }, CancellationToken.None).ConfigureAwait(false);

            for (var i = 0; i < waves.Count; i++)
            {
                if (BeforeWaveHook != null)
                    await BeforeWaveHook(i).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
                var wave = waves[i];
                // Distinct: two units of work on one thread would pay twice and race on the same EML paths
                var pending = wave.ThreadIds.Where(id => !state.IsDone(id) && threads.ContainsKey(id))
                    .Distinct(StringComparer.Ordinal).ToList();
                if (pending.Count == 0)
                    continue;

                snapshot.SetStage(GenerationStage.Writing, wave.Index, waves.Count, wave.Start, wave.End, pending.Count);
                snapshot.SetWaveRunning(wave.Index, true);
                snapshot.AddEvent(new GenerationEvent
                {
                    Kind = GenerationEventKind.WaveStarted,
                    LocalTime = DateTime.Now,
                    WaveIndex = wave.Index,
                    WaveCount = waves.Count,
                    WaveStart = wave.Start,
                    WaveEnd = wave.End,
                    Threads = pending.Count,
                    Emails = pending.Sum(id => threads[id].Messages.Count(m => m != null))
                });

                // Earlier waves only: LedgerSelector filters by wave, and this wave's deltas are not merged yet
                var ledger = LedgerMerger.Build(plan, guide, ctx.WaveOf, deltas);
                var tasks = pending
                    .Select(id =>
                    {
                        var render = ThreadRenderContext.Create(input.Draft, plan, threads[id],
                            ctx.Assignments.TryGetValue(id, out var a) ? a : new ThreadAssignment { ThreadId = id },
                            ctx.People, ledger.Entries, wave.Index);
                        render.PreviousStatus = Status(state, id);
                        return RunThreadAsync(ctx, uow, render, run);
                    })
                    .ToList();
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                // Merged after the wave completes: same-wave threads never see each other's details (D-11)
                foreach (var result in results)
                {
                    if (result.Kind is ThreadOutcomeKind.Written or ThreadOutcomeKind.Warnings && result.Record != null)
                        deltas[result.Record.ThreadId] = result.Record.LedgerDelta.ToList();
                }

                snapshot.SetWaveRunning(wave.Index, false);

                if (run.IsFatal)
                {
                    outcome = GenerationOutcome.Fatal;
                    message = run.FatalError;
                    break;
                }

                snapshot.SetStage(GenerationStage.Merging, wave.Index, waves.Count, wave.Start, wave.End);
                var merged = LedgerMerger.Build(plan, guide, ctx.WaveOf, deltas);
                snapshot.SetLedgerEntries(merged.Entries.Count);
                snapshot.AddEvent(new GenerationEvent
                {
                    Kind = GenerationEventKind.WaveFinished,
                    LocalTime = DateTime.Now,
                    WaveIndex = wave.Index,
                    WaveCount = waves.Count,
                    WaveStart = wave.Start,
                    WaveEnd = wave.End,
                    Written = results.Count(r => r.Kind == ThreadOutcomeKind.Written),
                    Warnings = results.Count(r => r.Kind == ThreadOutcomeKind.Warnings),
                    Failed = results.Count(r => r.Kind == ThreadOutcomeKind.Failed),
                    LedgerTotal = merged.Entries.Count
                });

                await CheckpointAsync(ctx, final: false, outcome, message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // In-flight threads were never journaled and stay pending (UI-SPEC C-G1)
            outcome = GenerationOutcome.Cancelled;
            message = string.Empty;
        }
        catch (Exception ex)
        {
            outcome = GenerationOutcome.InternalError;
            message = ex.Message;
        }
        finally
        {
            // Belt and braces: units never throw (05-12), but the run must still end cleanly with every finished thread projected
            foreach (var wave in waves)
                snapshot.SetWaveRunning(wave.Index, false);
            if (outcome == GenerationOutcome.Cancelled)
                snapshot.SetStage(GenerationStage.Stopping);

            final = await CheckpointAsync(ctx, final: true, outcome, message).ConfigureAwait(false);
            FinishSnapshot(snapshot, final, outcome, message);
        }

        return new GenerationRunResult
        {
            Outcome = outcome,
            Message = message,
            State = final?.State ?? state,
            Ledger = final?.Ledger ?? startLedger
        };
    }

    private static async Task<ThreadOutcome> RunThreadAsync(RunContext ctx, ThreadUnitOfWork uow, ThreadRenderContext render,
        PlanCallRun run)
    {
        var outcome = await uow.RunAsync(render, run).ConfigureAwait(false);
        if (outcome.Kind != ThreadOutcomeKind.NotRun)
            await MaybeProjectAsync(ctx).ConfigureAwait(false);
        else
            ctx.AddUnjournaledSpend(outcome.InputTokens, outcome.OutputTokens, outcome.CostUsd);
        return outcome;
    }

    /// <summary>
    /// Re-projects the key and checkpoints after a thread finishes, at most every <see cref="KeyProjectionInterval"/> and
    /// never twice at once. Never throws: a locked key is reported in the checkpoint and the thread stays finished.
    /// </summary>
    private static async Task MaybeProjectAsync(RunContext ctx)
    {
        if (DateTime.UtcNow - ctx.ReadLastProjection() < KeyProjectionInterval)
            return;

        // Skip when a projection is already running; the next finishing thread or the wave end catches up
        if (!await ctx.ProjectionGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (DateTime.UtcNow - ctx.ReadLastProjection() < KeyProjectionInterval)
                return;

            await CheckpointCoreAsync(ctx, final: false, GenerationOutcome.None, string.Empty).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A projection is a convenience view of the journal; the thread is already on disk and the wave end re-projects
        }
        finally
        {
            ctx.ProjectionGate.Release();
        }
    }

    private static async Task<GenerationCheckpoint> CheckpointAsync(RunContext ctx, bool final, GenerationOutcome outcome,
        string message)
    {
        // Wave and run end wait for a running per-thread projection so key writes never overlap
        await ctx.ProjectionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            ctx.Snapshot.SetStage(GenerationStage.UpdatingKey);
            return await CheckpointCoreAsync(ctx, final, outcome, message).ConfigureAwait(false);
        }
        finally
        {
            ctx.ProjectionGate.Release();
        }
    }

    // Caller holds ProjectionGate. Every step catches its own failure so one never skips the others.
    private static async Task<GenerationCheckpoint> CheckpointCoreAsync(RunContext ctx, bool final, GenerationOutcome outcome,
        string message)
    {
        var input = ctx.Input;
        var plan = input.Plan;
        ctx.WriteLastProjection(DateTime.UtcNow);

        // Before the replay, so the final projection includes the spend RunEnded carries
        if (final)
            await AppendRunEndedAsync(ctx, outcome, message).ConfigureAwait(false);

        // The journal is the source of truth: anything journaled is in the key, whatever happened in memory
        GenerationRunState? state = null;
        NarrativeLedger? ledger = null;
        string unusableReason;
        try
        {
            var replay = GenerationJournal.Replay(input.CaseFolder, plan);
            if (!replay.Readable || replay.PlanChanged)
            {
                // An unreadable (IO lock, size cap) or foreign journal replays as "nothing generated"
                unusableReason = replay.PlanChanged ? PlanChangedMessage
                    : replay.Reason.Length > 0 ? replay.Reason : "The saved run could not be found.";
            }
            else
            {
                state = GenerationRunState.From(plan, replay);
                ledger = LedgerMerger.Build(plan, ctx.Guide, ctx.WaveOf, state.DeltasByThread);
                unusableReason = string.Empty;
            }
        }
        catch (Exception ex)
        {
            // Replay never throws by contract; a bug in the projection must not stop the remaining end-of-run steps
            unusableReason = ex.Message;
        }

        if (state == null || ledger == null)
            return UnprojectedCheckpoint(ctx, final, outcome, message, unusableReason);

        ctx.LastGoodState = state;
        ctx.LastGoodLedger = ledger;

        var checkpoint = new GenerationCheckpoint
        {
            State = state,
            Ledger = ledger,
            IsFinal = final,
            Outcome = outcome,
            Message = message ?? string.Empty
        };

        try
        {
            checkpoint.KeyResult = await GenerationOutputs.WriteAnswerKeyAsync(input.CaseFolder, ctx.Guide, plan,
                state.BuildOverlay(plan, ctx.Assignments), null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            checkpoint.KeyResult = new GenerationFileResult { FailedFileName = AnswerKeyCsvWriter.FileName, Error = ex.Message };
        }

        checkpoint.Files.AddRange(checkpoint.KeyResult.Files);

        if (final && ledger.Entries.Count > 0)
        {
            ctx.Snapshot.SetStage(GenerationStage.WritingStory);
            try
            {
                checkpoint.StoryResult = await GenerationOutputs.WriteStorySoFarAsync(input.CaseFolder, input.Draft, plan, ledger,
                    state, ctx.People, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(input.CaseFolder));
                checkpoint.StoryResult = new GenerationFileResult
                {
                    FailedFileName = StorySoFarDocxBuilder.FileNameFor(folderName),
                    Error = ex.Message
                };
            }

            checkpoint.Files.AddRange(checkpoint.StoryResult.Files);
        }

        if (ctx.Persist != null)
        {
            try
            {
                await ctx.Persist(checkpoint).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The step may be gone (window closing); EMLs, journal and key are already on disk and reopen re-projects them
            }
        }

        ctx.Snapshot.SetLedgerEntries(ledger.Entries.Count);
        ctx.Snapshot.SetRemainingCost(GenerationRunState.EstimateRemaining(plan, state, ctx.StoryModel));
        return checkpoint;
    }

    /// <summary>
    /// The journal could not be projected (unreadable, foreign plan or a projection bug). Keeps the last good projection:
    /// the key, story and case.json are left as they are rather than overwritten with an empty run, and the next readable
    /// projection catches up. RunEnded is already appended by the caller. Never throws.
    /// </summary>
    private static GenerationCheckpoint UnprojectedCheckpoint(RunContext ctx, bool final, GenerationOutcome outcome,
        string message, string reason)
    {
        return new GenerationCheckpoint
        {
            State = ctx.LastGoodState,
            Ledger = ctx.LastGoodLedger,
            KeyResult = new GenerationFileResult { FailedFileName = AnswerKeyCsvWriter.FileName, Error = reason },
            IsFinal = final,
            Outcome = outcome,
            Message = message ?? string.Empty
        };
    }

    private static async Task AppendRunEndedAsync(RunContext ctx, GenerationOutcome outcome, string? message)
    {
        // Calls a fatal stop left without a thread record are still spend (WR-02)
        var (inputTokens, outputTokens, cost) = ctx.ReadUnjournaledSpend();
        try
        {
            await ctx.Journal.AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.RunEnded,
                Utc = DateTime.UtcNow,
                PlanId = ctx.Input.Plan.PlanId,
                Outcome = outcome,
                Message = message ?? string.Empty,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsd = cost
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // RunEnded is an audit line; replay works without it and every thread record is already flushed
        }
    }

    private static void FinishSnapshot(GenerationSnapshot snapshot, GenerationCheckpoint final, GenerationOutcome outcome,
        string message)
    {
        var state = final.State;
        var done = state.Written + state.Warnings;
        var e = new GenerationEvent { LocalTime = DateTime.Now, Done = done, Total = state.TotalThreads };
        switch (outcome)
        {
            case GenerationOutcome.Finished:
                e.Kind = GenerationEventKind.RunFinished;
                e.Written = state.Written;
                e.Warnings = state.Warnings;
                e.Failed = state.Failed;
                e.LedgerTotal = final.Ledger.Entries.Count;
                break;
            case GenerationOutcome.Cancelled:
                e.Kind = GenerationEventKind.RunCancelled;
                break;
            default:
                e.Kind = GenerationEventKind.RunStopped;
                e.Reason = message;
                break;
        }

        snapshot.AddEvent(e);
        snapshot.SetStage(GenerationStage.Idle);
        snapshot.SetFinished(running: false, finished: outcome == GenerationOutcome.Finished);
    }

    private static ThreadRunStatus Status(GenerationRunState state, string id) =>
        state.StatusByThread.TryGetValue(id, out var status) ? status : ThreadRunStatus.Pending;

    // Per-run shared values; one instance per RunAsync call
    private sealed class RunContext
    {
        private readonly object _lock = new();
        private DateTime _lastProjectionUtc;
        private int _unjournaledInput;
        private int _unjournaledOutput;
        private decimal _unjournaledCost;

        public GenerationRunInput Input { get; init; } = new();
        public CaseGuide Guide { get; init; } = new();
        public List<GenerationWave> Waves { get; init; } = new();
        public Dictionary<string, int> WaveOf { get; init; } = new();
        public Dictionary<string, ThreadAssignment> Assignments { get; init; } = new();
        public PeopleDirectory People { get; init; } = null!;
        public GenerationJournal Journal { get; init; } = null!;
        public GenerationSnapshot Snapshot { get; init; } = null!;
        public GenerationPersist? Persist { get; init; }
        public AIModelConfig? StoryModel { get; init; }
        public SemaphoreSlim ProjectionGate { get; } = new(1, 1);

        // Last projection that came from a readable journal; read and written only under ProjectionGate
        public GenerationRunState LastGoodState { get; set; } = new();
        public NarrativeLedger LastGoodLedger { get; set; } = new();

        public DateTime LastProjectionUtc
        {
            init => _lastProjectionUtc = value;
        }

        public DateTime ReadLastProjection()
        {
            lock (_lock)
                return _lastProjectionUtc;
        }

        public void WriteLastProjection(DateTime utc)
        {
            lock (_lock)
                _lastProjectionUtc = utc;
        }

        public void AddUnjournaledSpend(int inputTokens, int outputTokens, decimal cost)
        {
            lock (_lock)
            {
                _unjournaledInput += inputTokens;
                _unjournaledOutput += outputTokens;
                _unjournaledCost += cost;
            }
        }

        public (int InputTokens, int OutputTokens, decimal CostUsd) ReadUnjournaledSpend()
        {
            lock (_lock)
                return (_unjournaledInput, _unjournaledOutput, _unjournaledCost);
        }
    }
}
