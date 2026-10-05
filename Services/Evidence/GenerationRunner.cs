using System.Collections.Concurrent;
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

    // Images and voicemail audio; null when no OpenAI key was tested this session (every media item is Skipped)
    public IMediaGenerator? Media { get; set; }

    public MediaPrices MediaPrices { get; set; } = MediaPrices.Defaults();
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

    // Test seam: runs just before the runner appends its own CastVoices or SideWorld record
    internal Func<JournalRecord, Task>? BeforeRecordAppendHook { get; set; }

    // Test seam: passed to ThreadUnitOfWork.BeforeAppendHook (runs just before each ThreadDone append)
    internal Func<JournalRecord, Task>? BeforeThreadAppendHook { get; set; }

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
        var mediaAvailable = input.Media != null;

        // Whole plan, never the remaining threads: Continue sees the same waves and ledger visibility (Pitfall 3)
        var waves = WavePlanner.Plan(input.Plan);
        var fillerRow = waves.Count + 1;
        var repairRow = waves.Count + 2;
        var folders = PlanNumbering.CustodianFolders(guide);
        var journal = new GenerationJournal(folder);
        var chains = AttachmentChains.Compute(plan, guide);
        var ctx = new RunContext
        {
            Input = input,
            Guide = guide,
            Waves = waves,
            WaveOf = WavePlanner.WaveIndexByThread(waves),
            Assignments = GenerationRunState.AllAssignments(plan),
            People = PeopleDirectory.Create(guide, plan),
            Journal = journal,
            Snapshot = snapshot,
            Persist = persist,
            StoryModel = _llm.ResolveModel(LlmTier.Story),
            VolumeModel = _llm.ResolveModel(LlmTier.Volume),
            Chains = chains,
            // D-16 + checker 06-18: one committed store for the run, seeded from the journal; waits only on lower-ranked threads
            ChainScheduler = new ChainScheduler(chains, CommittedTexts(state), ChainScheduler.ThreadOfAttachment(plan),
                ChainScheduler.RankThreads(plan)),
            MediaGate = new MediaRunGate(),
            LastProjectionUtc = DateTime.UtcNow
        };

        // D-20 + review concern 6: persisted schedules first, so a later invite for a known meeting reuses its slot
        foreach (var (key, slot) in state.InviteSlotsByMeeting)
            ctx.InviteSlots.TryAdd(key, slot);

        var threads = (plan.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Media items this run will attempt: those of threads not yet written (MediaStopped reports what is left)
        ctx.MediaGate.SetRemaining(threads.Values
            .Where(t => !state.IsDone(t.Id))
            .SelectMany(PlannedAttachments)
            .Count(a => a.Kind is AttachmentType.Image or AttachmentType.Voicemail));

        var attachmentsUow = new AttachmentUnitOfWork(_llm, snapshot, input.Media, ctx.MediaGate, ctx.StoryModel, ctx.VolumeModel);
        var uow = new ThreadUnitOfWork(_llm, journal, new MailboxWriter(folder, folders), snapshot, ctx.StoryModel, attachmentsUow)
        {
            BeforeAppendHook = BeforeThreadAppendHook
        };
        var deltas = new Dictionary<string, List<LedgerDelta>>(state.DeltasByThread, StringComparer.Ordinal);

        var startLedger = LedgerMerger.Build(plan, guide, ctx.WaveOf, deltas);
        ctx.LastGoodState = state;
        ctx.LastGoodLedger = startLedger;

        // Same row rules and mediaAvailable as RepairGroups and GenerationHistory.Waves (checker 06-23)
        var seeds = state.AttachmentSeeds(plan, ctx.WaveOf, fillerRow, repairRow, mediaAvailable: mediaAvailable);
        snapshot.Reset(
            state.ToCounts(plan, waves.Count, startLedger.Entries.Count,
                EstimateRemaining(ctx, state), running: true, finished: false),
            StageRows(plan, waves, state, seeds, threads, fillerRow),
            seeds);

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

            // D-28: one voice set for every voicemail of every stage, chosen before the first one is spoken
            var proceed = true;
            if (mediaAvailable && VoicemailPending(threads.Values, state))
            {
                var voices = CastVoiceBuilder.FromRecord(replay.LastCastVoices, guide);
                if (voices == null)
                {
                    var vo = await CastVoiceBuilder.BuildAsync(_llm, input.Draft, guide, run).ConfigureAwait(false);
                    var voiceCost = ModelCost(ctx.VolumeModel, vo.InputTokens, vo.OutputTokens);
                    snapshot.AddCost(voiceCost);
                    if (run.IsFatal && vo.Voices == null)
                    {
                        ctx.AddUnjournaledSpend(vo.InputTokens, vo.OutputTokens, voiceCost);
                        outcome = GenerationOutcome.Fatal;
                        message = run.FatalError;
                        proceed = false;
                    }
                    else
                    {
                        voices ??= vo.Voices;
                        var record = new JournalRecord
                        {
                            Type = JournalRecordType.CastVoices,
                            Utc = DateTime.UtcNow,
                            PlanId = plan.PlanId,
                            CastVoices = voices,
                            InputTokens = vo.InputTokens,
                            OutputTokens = vo.OutputTokens,
                            CostUsd = voiceCost
                        };
                        try
                        {
                            await AppendOwnRecordAsync(journal, record).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // Review 06-19: Continue promises the same voices; never use a choice the journal does not hold
                            ctx.AddUnjournaledSpend(vo.InputTokens, vo.OutputTokens, voiceCost);
                            outcome = GenerationOutcome.InternalError;
                            message = $"Could not save the voicemail voices: {ex.Message}";
                            proceed = false;
                        }
                    }
                }

                if (proceed)
                    attachmentsUow.CastVoices = voices;
            }

            for (var i = 0; proceed && i < waves.Count; i++)
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
                ctx.ChainScheduler.BeginStep(pending);
                var step = StepContext(ctx, wave.Index);
                var relevantProfile = RenderProfile.Relevant(_llm);
                var tasks = pending
                    .Select(id =>
                    {
                        var render = ThreadRenderContext.Create(input.Draft, plan, threads[id],
                            ctx.Assignments.TryGetValue(id, out var a) ? a : new ThreadAssignment { ThreadId = id },
                            ctx.People, ledger.Entries, wave.Index);
                        render.PreviousStatus = Status(state, id);
                        return RunThreadAsync(ctx, uow, render, relevantProfile, step, run);
                    })
                    .ToList();
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                KeepSlots(ctx, step);

                // Merged after the wave completes: same-wave threads never see each other's details (D-11)
                foreach (var result in results)
                {
                    // D-18 + review concern 1: only details whose line is durable reach later waves
                    if (result.Kind is ThreadOutcomeKind.Written or ThreadOutcomeKind.Warnings && result.Record != null)
                        deltas[result.Record.ThreadId] = result.Record.LedgerDelta.Concat(result.AttachmentDeltas).ToList();
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

            try
            {
                final = await CheckpointAsync(ctx, final: true, outcome, message).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Every checkpoint step catches its own failure; this only guards the "never throws" contract
                final = new GenerationCheckpoint
                {
                    State = ctx.LastGoodState,
                    Ledger = ctx.LastGoodLedger,
                    IsFinal = true,
                    Outcome = outcome,
                    Message = message
                };
            }

            // Always runs, so the snapshot never stays "running" without a RunEnded event
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
        RenderProfile profile, ThreadAttachmentContext attachments, PlanCallRun run)
    {
        var threadId = render.Thread.Id;
        ThreadOutcome outcome;
        try
        {
            // A registered thread must always complete, or same-step successors wait forever
            if (run.IsFatal)
            {
                ctx.ChainScheduler.Complete(threadId, EmptyTexts);
                return new ThreadOutcome { Kind = ThreadOutcomeKind.NotRun, Reason = run.FatalError };
            }

            if (run.UserToken.IsCancellationRequested)
            {
                ctx.ChainScheduler.Complete(threadId, EmptyTexts);
                run.UserToken.ThrowIfCancellationRequested();
            }

            outcome = await uow.RunAsync(render, profile, run, attachments).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The unit completes in its own finally; this covers anything thrown before it started (idempotent)
            ctx.ChainScheduler.Complete(threadId, EmptyTexts);
            throw;
        }

        if (outcome.Kind != ThreadOutcomeKind.NotRun)
            await MaybeProjectAsync(ctx).ConfigureAwait(false);
        else
            ctx.AddUnjournaledSpend(outcome.InputTokens, outcome.OutputTokens, outcome.CostUsd);

        AddUnjournaled(ctx, outcome);
        return outcome;
    }

    // D-29 + review concern 1: every token and dollar with no journal line of its own reaches RunEnded exactly once
    private static void AddUnjournaled(RunContext ctx, ThreadOutcome result)
    {
        // A NotRun thread has no attachments and Unjournaled false (counted above); a Failed thread's line already carries its spend
        if (result.Unjournaled)
            ctx.AddUnjournaledSpend(result.InputTokens, result.OutputTokens, result.CostUsd);
        else if (result.NotRunCostUsd > 0 || result.NotRunInputTokens > 0 || result.NotRunOutputTokens > 0)
        {
            // Checker 06-19: NotRun attachments have no record; the Unjournaled totals already include them
            ctx.AddUnjournaledSpend(result.NotRunInputTokens, result.NotRunOutputTokens, result.NotRunCostUsd);
        }
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
                state.BuildOverlay(plan, ctx.Assignments, ctx.Chains), null, CancellationToken.None).ConfigureAwait(false);
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
            catch (Exception)
            {
                // Best effort, cancellation-type exceptions included: the step may be gone (window closing); EMLs, journal
                // and key are already on disk and reopen re-projects them, and RunAsync must still end cleanly
            }
        }

        ctx.Snapshot.SetLedgerEntries(ledger.Entries.Count);
        ctx.Snapshot.SetRemainingCost(EstimateRemaining(ctx, state));
        return checkpoint;
    }

    // D-29: Story, Volume and media with the shared pricing, same mediaAvailable as the seeds and repairs
    private static decimal EstimateRemaining(RunContext ctx, GenerationRunState state) =>
        GenerationRunState.EstimateRemaining(ctx.Input.Plan, state, ctx.StoryModel, ctx.VolumeModel, ctx.Input.MediaPrices,
            ctx.Input.Media != null, ctx.Guide);

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
                e.AttachmentsDone = state.AttachmentsWritten + state.AttachmentsWarnings;
                e.AttachmentsTotal = state.AttachmentsTotal;
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

    private static readonly IReadOnlyDictionary<string, string> EmptyTexts = new Dictionary<string, string>(StringComparer.Ordinal);

    private static bool IsCommitted(AttachmentRunStatus status) =>
        status is AttachmentRunStatus.Written or AttachmentRunStatus.Warnings;

    // Flattened text of every committed attachment in the journal: the chain predecessors of this run's successors
    private static Dictionary<string, string> CommittedTexts(GenerationRunState state)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (docId, record) in state.AttachmentRecords)
        {
            if (record != null && state.StatusByAttachment.TryGetValue(docId, out var status) && IsCommitted(status))
                texts[docId] = record.FlattenedText ?? string.Empty;
        }

        return texts;
    }

    private static IEnumerable<PlannedAttachment> PlannedAttachments(PlannedThread thread) =>
        (thread.Messages ?? new List<PlannedMessage>())
        .Where(m => m != null)
        .SelectMany(m => m.Attachments ?? new List<PlannedAttachment>())
        .Where(a => a != null && !string.IsNullOrEmpty(a.DocId));

    // A planned voicemail with no committed outcome yet (pending thread, or Failed/Skipped on a written one)
    private static bool VoicemailPending(IEnumerable<PlannedThread> threads, GenerationRunState state) =>
        threads.SelectMany(PlannedAttachments)
            .Where(a => a.Kind == AttachmentType.Voicemail)
            .Any(a => !state.StatusByAttachment.TryGetValue(a.DocId, out var status) || !IsCommitted(status));

    private static decimal ModelCost(AIModelConfig? model, int inputTokens, int outputTokens) =>
        model?.CalculateTotalCost(inputTokens, outputTokens) ?? 0m;

    // CastVoices and SideWorld must be durable before anything uses them; the caller decides what a failure means
    private async Task AppendOwnRecordAsync(GenerationJournal journal, JournalRecord record)
    {
        if (BeforeRecordAppendHook != null)
            await BeforeRecordAppendHook(record).ConfigureAwait(false);
        await journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
    }

    // One attachment context per run step: the run's chain scheduler and a copy of the known invite slots
    private static ThreadAttachmentContext StepContext(RunContext ctx, int rowIndex)
    {
        var step = new ThreadAttachmentContext { Chains = ctx.ChainScheduler, RowIndex = rowIndex };
        foreach (var (key, slot) in ctx.InviteSlots)
            step.InviteSlots.TryAdd(key, slot);
        return step;
    }

    // Slots registered by committed invites of the step stay known to later steps (D-20: the first one wins)
    private static void KeepSlots(RunContext ctx, ThreadAttachmentContext step)
    {
        foreach (var (key, slot) in step.InviteSlots)
            ctx.InviteSlots.TryAdd(key, slot);
    }

    /// <summary>
    /// Wave rows plus the Non-relevant mail row (when the plan has non-relevant threads), with thread and attachment
    /// counts from the replayed state and the attachment seeds so live counters start where the journal left off.
    /// </summary>
    private static List<WaveProgress> StageRows(CorpusPlan plan, List<GenerationWave> waves, GenerationRunState state,
        IReadOnlyDictionary<string, AttachmentSeed> seeds, IReadOnlyDictionary<string, PlannedThread> threads, int fillerRow)
    {
        var rows = waves.Select(w => new WaveProgress
        {
            Index = w.Index,
            Start = w.Start,
            End = w.End,
            Threads = w.ThreadIds.Count,
            Emails = w.MessageCount,
            Done = w.ThreadIds.Count(state.IsDone),
            Warnings = w.ThreadIds.Count(id => Status(state, id) == ThreadRunStatus.Warnings),
            Failed = w.ThreadIds.Count(id => Status(state, id) == ThreadRunStatus.Failed)
        }).ToList();

        var filler = NonRelevantThreads(threads.Values).ToList();
        if (filler.Count > 0)
        {
            var first = filler.Min(MessageTime.FirstLocalDate);
            var last = filler.SelectMany(t => t.Messages.Where(m => m != null)).Select(MessageTime.LocalDate).Max();
            rows.Add(new WaveProgress
            {
                Index = fillerRow,
                RowKind = StageRowKind.NonRelevant,
                Start = first,
                End = last,
                Threads = filler.Count,
                Emails = filler.Sum(t => t.Messages.Count(m => m != null)),
                Done = filler.Count(t => state.IsDone(t.Id)),
                Warnings = filler.Count(t => Status(state, t.Id) == ThreadRunStatus.Warnings),
                Failed = filler.Count(t => Status(state, t.Id) == ThreadRunStatus.Failed)
            });
        }

        foreach (var seed in seeds.Values)
        {
            var row = rows.FirstOrDefault(r => r.Index == seed.RowIndex);
            if (row == null)
                continue;

            row.Attachments++;
            switch (seed.Status)
            {
                case AttachmentRunStatus.Written:
                    row.AttachmentsDone++;
                    break;
                case AttachmentRunStatus.Warnings:
                    row.AttachmentsDone++;
                    row.AttachmentWarnings++;
                    break;
                case AttachmentRunStatus.Failed:
                    row.AttachmentsFailed++;
                    break;
                case AttachmentRunStatus.Skipped:
                    row.AttachmentsSkipped++;
                    break;
            }
        }

        return rows;
    }

    // Non-relevant generation threads (same rule as GenerationRunState): an id and at least one message
    private static IEnumerable<PlannedThread> NonRelevantThreads(IEnumerable<PlannedThread> threads) =>
        threads.Where(t => t.Relevance != ThreadRelevance.Relevant && t.Messages != null && t.Messages.Any(m => m != null));

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
        public AIModelConfig? VolumeModel { get; init; }

        // D-16: chain links of the whole plan and the run-wide predecessor scheduler
        public Dictionary<string, AttachmentChainLink> Chains { get; init; } = new(StringComparer.Ordinal);
        public ChainScheduler ChainScheduler { get; init; } = null!;

        // D-20: canonical meeting key -> schedule, seeded from the journal and grown by committed invites
        public ConcurrentDictionary<string, InviteSlot> InviteSlots { get; } = new(StringComparer.Ordinal);

        // One per run: a media stop ends only media (UI-SPEC Decision 2)
        public MediaRunGate MediaGate { get; init; } = null!;

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
