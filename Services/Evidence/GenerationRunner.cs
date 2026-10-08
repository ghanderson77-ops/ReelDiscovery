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

    /// <summary>Batch mode (D-07): the decorator behind the run's gateway; null = live.</summary>
    public BatchingLlmProvider? Batch { get; set; }
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
/// Runs corpus generation (D-25): relevant threads with their attachments in chronological waves (parallel units of work
/// inside a wave, committed details merged into the ledger after each wave), then the side world and every non-relevant
/// thread with its attachments, with debounced key re-projection and checkpoints and a clean end on finish, cancel,
/// fatal stop or a bug (D-05, D-06, D-11, RUN-01, RUN-02, RUN-06). Generate, Continue and Retry Failed Threads are all
/// this one run: it writes every thread the journal does not list as done.
/// </summary>
public class GenerationRunner
{
    // Matches RelevantThreadFiller.MaxParallelCalls (A2)
    public const int MaxParallelThreads = 3;

    public const string RetryHint = "Click Continue Generating.";

    // Non-relevant threads per run step: bounds memory and sets the checkpoint cadence of Stage B (T-06-58)
    public const int FillerChunkSize = 50;

    // Review MEDIUM: bound attachment buffers held by repairs separately from provider slots
    public const int MaxConcurrentRepairs = 4;

    public const int BatchMaxParallel = 10_000; // batch mode, non-relevant stage only: queued requests cost nothing, so every unit of a chunk starts at once (research Pattern 1, amended D-07)

    // Batch mode Stage B chunk: one provider batch of non-relevant mail per chunk (live mode keeps FillerChunkSize)
    public const int BatchFillerChunkSize = 2_000;

    // Debounced so a burst of finishing threads writes the key at most every 2 s; wave and run end always write it
    public static readonly TimeSpan KeyProjectionInterval = TimeSpan.FromSeconds(2);

    // Upper bound of the adaptive interval: a long run still refreshes the key at least once a minute
    public static readonly TimeSpan MaxKeyProjectionInterval = TimeSpan.FromSeconds(60);

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

    // Test seam: reorders each step's repair list before launch (deadlock tests run it reversed)
    internal Func<List<AttachmentRepairGroup>, List<AttachmentRepairGroup>>? RepairOrderHook { get; set; }

    // Test seam: repair slots for this run; null uses MaxConcurrentRepairs
    internal int? RepairSlotsOverride { get; set; }

    // Test seam: passed to AttachmentRepair.BeforeAppendHook (runs just before each AttachmentsRepaired append)
    internal Func<JournalRecord, Task>? BeforeRepairAppendHook { get; set; }

    // Test seam: called when a repair takes a slot (+1) and gives it back (-1)
    internal Action<string, int>? RepairSlotHook { get; set; }

    /// <summary>
    /// Writes every thread not yet journaled as done: relevant waves first, then non-relevant mail. Never throws: cancel,
    /// fatal text-provider errors and unexpected exceptions all end with a final checkpoint and a RunEnded record; a media
    /// stop only marks the remaining images and voicemails Skipped.
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
        var scaffold = BuildScaffold(input.Draft, plan);
        var waves = scaffold.Waves;
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
            WaveOf = scaffold.WaveOf,
            Assignments = scaffold.Assignments,
            People = scaffold.People,
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

        // D-26 + review concern 5: attachments of written threads, repaired in story order (relevant by wave, then filler)
        var groups = state.RepairGroups(plan, mediaAvailable: input.Media != null)
            .Where(g => threads.ContainsKey(g.ThreadId) && state.Records.ContainsKey(g.ThreadId))
            .ToList();
        var relevantRepairs = groups.Where(g => g.Relevant).ToLookup(g => g.WaveIndex);
        var fillerRepairs = groups.Where(g => !g.Relevant).ToList();
        var kindOf = threads.Values.SelectMany(PlannedAttachments)
            .GroupBy(a => a.DocId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.Ordinal);

        // Media items this run will attempt: those of threads not yet written and of repairs (MediaStopped reports what is left)
        ctx.MediaGate.SetRemaining(threads.Values
            .Where(t => !state.IsDone(t.Id))
            .SelectMany(PlannedAttachments)
            .Count(a => a.Kind is AttachmentType.Image or AttachmentType.Voicemail)
            + groups.SelectMany(g => g.AttachmentDocIds)
                .Count(d => kindOf.TryGetValue(d, out var kind) && kind is AttachmentType.Image or AttachmentType.Voicemail));

        var attachmentsUow = new AttachmentUnitOfWork(_llm, snapshot, input.Media, ctx.MediaGate, ctx.StoryModel, ctx.VolumeModel);
        var mailbox = new MailboxWriter(folder, folders);
        var uow = new ThreadUnitOfWork(_llm, journal, mailbox, snapshot, ctx.StoryModel, attachmentsUow)
        {
            BeforeAppendHook = BeforeThreadAppendHook
        };
        var deltas = new Dictionary<string, List<LedgerDelta>>(state.DeltasByThread, StringComparer.Ordinal);

        var startLedger = LedgerMerger.Build(plan, guide, ctx.WaveOf, deltas);
        ctx.LastGoodState = state;
        ctx.LastGoodLedger = startLedger;

        // One repair service per run; never re-renders email text (D-26)
        var repair = new AttachmentRepair(journal, mailbox, attachmentsUow, snapshot, folder, folders)
        {
            BeforeAppendHook = BeforeRepairAppendHook
        };
        ctx.Repair = repair;
        ctx.RepairSlots = new SemaphoreSlim(RepairSlotsOverride ?? MaxConcurrentRepairs);
        ctx.RepairAttachments = groups.Sum(g => g.AttachmentDocIds.Count);
        ctx.RepairThreads = groups.Count;
        ctx.RepairRow = repairRow;

        // Review concern 5: a repair is late when a later relevant wave holds a thread finished before this run (text and
        // attachments; a thread with its own pending repair is completed after this one, in story order)
        var repairing = new HashSet<string>(groups.Select(g => g.ThreadId), StringComparer.Ordinal);
        ctx.LastFinishedWave = threads.Values
            .Where(t => t.Relevance == ThreadRelevance.Relevant && state.IsDone(t.Id) && !repairing.Contains(t.Id)
                        && ctx.WaveOf.ContainsKey(t.Id))
            .Select(t => ctx.WaveOf[t.Id])
            .DefaultIfEmpty(0)
            .Max();
        ctx.StartLedger = startLedger;

        // Same row rules and mediaAvailable as RepairGroups and GenerationHistory.Waves (checker 06-23)
        var seeds = state.AttachmentSeeds(plan, ctx.WaveOf, fillerRow, repairRow, mediaAvailable: mediaAvailable);
        snapshot.Reset(
            state.ToCounts(plan, waves.Count, startLedger.Entries.Count,
                EstimateRemaining(ctx, state), running: true, finished: false),
            StageRows(plan, waves, state, seeds, threads, fillerRow, repairRow, groups.Count),
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

        // D-09: provider batch status reaches the stage rows; batches an earlier session left pending are polled again
        var batch = input.Batch;
        if (batch != null)
        {
            // Wave rows only show batches an earlier build submitted per wave (pre amended D-07)
            foreach (var wave in waves)
                snapshot.MapBatchStage(WaveStageLabel(wave.Index), wave.Index);
            snapshot.MapBatchStage(NonRelevantStageLabel, fillerRow);
            batch.StatusChanged += snapshot.RecordBatchStatus;
            var resumed = batch.ResumePending();
            snapshot.SetCollectingAfterRestart(resumed > 0);
            snapshot.SetPendingProviderBatches(resumed);
        }

        var outcome = GenerationOutcome.Finished;
        var message = string.Empty;
        GenerationCheckpoint? final = null;

        // Amended D-07: relevant waves, voice cast and side world are live in both modes, so they keep live parallelism (research anti-pattern: never raise live parallelism)
        using var run = new PlanCallRun(ct, _maxParallel, RetryHint);
        try
        {
            await journal.AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.RunStarted,
                Utc = DateTime.UtcNow,
                PlanId = plan.PlanId,
                // D-12: the provider writing the corpus (input for the reviewer choice)
                Provider = _llm.Provider.ToString()
            }, CancellationToken.None).ConfigureAwait(false);

            // D-28: one voice set for every voicemail of every stage, chosen before the first one is spoken
            var proceed = true;
            if (mediaAvailable && VoicemailPending(threads.Values, state))
            {
                var voices = CastVoiceBuilder.FromRecord(replay.LastCastVoices, guide);
                if (voices == null)
                {
                    var vo = await CastVoiceBuilder.BuildAsync(_llm, input.Draft, guide, run).ConfigureAwait(false);
                    var voiceCost = vo.Cost.TotalUsd;
                    snapshot.AddCost(voiceCost);
                    if (run.IsFatal && vo.Voices == null)
                    {
                        ctx.AddUnjournaledSpend(vo.Cost, 0m, vo.InputTokens, vo.OutputTokens);
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
                            CostUsd = voiceCost,
                            CostDetail = vo.Cost.Clone()
                        };
                        try
                        {
                            await AppendOwnRecordAsync(journal, record).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // Review 06-19: Continue promises the same voices; never use a choice the journal does not hold
                            ctx.AddUnjournaledSpend(vo.Cost, 0m, vo.InputTokens, vo.OutputTokens);
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
                var waveRepairs = relevantRepairs[wave.Index].ToList();
                if (pending.Count == 0 && !relevantRepairs[wave.Index].Any())
                    continue;

                if (pending.Count > 0)
                {
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
                }
                else
                {
                    // Only attachments of written threads in this step ("Adding missing attachments: ...")
                    snapshot.SetStage(GenerationStage.Attachments, wave.Index, waves.Count, wave.Start, wave.End,
                        waveRepairs.Sum(g => g.AttachmentDocIds.Count));
                }

                // Earlier waves only: LedgerSelector filters by wave, and this wave's deltas are not merged yet; deltas from
                // earlier-wave repairs of this run are already in
                var ledger = LedgerMerger.Build(plan, guide, ctx.WaveOf, deltas);
                ctx.ChainScheduler.BeginStep(pending.Concat(waveRepairs.Select(g => g.ThreadId)));
                // WR-04: one frozen context per unit; committed slots are published only at the step end
                var threadSteps = new List<ThreadAttachmentContext>();
                var relevantProfile = RenderProfile.Relevant(_llm);
                List<Task<ThreadOutcome>> tasks;

                // Amended D-07: relevant waves run live in batch mode too (thread renders and every attachment they start, via the AsyncLocal); only non-relevant mail is batched
                using (BatchStage.Enter(WaveStageLabel(wave.Index)))
                using (BatchStage.EnterLive())
                {
                    tasks = pending
                        .Select(id =>
                        {
                            var step = StepContext(ctx, wave.Index);
                            threadSteps.Add(step);
                            var render = ThreadRenderContext.Create(input.Draft, plan, threads[id],
                                ctx.Assignments.TryGetValue(id, out var a) ? a : new ThreadAssignment { ThreadId = id },
                                ctx.People, ledger.Entries, wave.Index);
                            render.PreviousStatus = Status(state, id);

                            // OQ3: a regenerated relevant thread keeps the details later waves already relied on (D-02)
                            if (state.InvalidatedDeltas.TryGetValue(id, out var prior) && prior is { Count: > 0 })
                                render.PriorDeltas = prior;
                            return RunThreadAsync(ctx, uow, render, relevantProfile, step, run);
                        })
                        .ToList();
                }

                var repairSteps = new List<ThreadAttachmentContext>();
                var repairTasks = new List<Task<AttachmentRepairOutcome?>>();
                var repairIds = new List<string>();
                if (waveRepairs.Count > 0)
                {
                    StartRepairs(ctx);
                    snapshot.SetWaveRunning(repairRow, true);

                    // Revision 1 W9: at most MaxConcurrentRepairs in flight, so repairs never wait a provider cycle in a batch
                    using var live = BatchStage.EnterLive();
                    foreach (var group in RepairOrderHook?.Invoke(waveRepairs.ToList()) ?? waveRepairs)
                    {
                        var render = ThreadRenderContext.Create(input.Draft, plan, threads[group.ThreadId],
                            ctx.Assignments.TryGetValue(group.ThreadId, out var a) ? a : new ThreadAssignment { ThreadId = group.ThreadId },
                            ctx.People, ledger.Entries, wave.Index);
                        var actx = RepairContext(ctx, wave.Index, group.ThreadId);
                        repairSteps.Add(actx);
                        repairIds.Add(group.ThreadId);
                        repairTasks.Add(RunRepairAsync(ctx, state, group, render, relevantProfile, actx, run));
                    }
                }

                ThreadOutcome[] results;
                AttachmentRepairOutcome?[] repaired;
                try
                {
                    await Task.WhenAll(tasks.Cast<Task>().Concat(repairTasks)).ConfigureAwait(false);
                    results = tasks.Select(t => t.Result).ToArray();
                    repaired = repairTasks.Select(t => t.Result).ToArray();
                }
                finally
                {
                    KeepSlots(ctx, threadSteps.Concat(repairSteps));
                    snapshot.SetWaveRunning(repairRow, false);
                }

                // Merged after the wave completes: same-wave threads never see each other's details (D-11)
                foreach (var result in results)
                {
                    // D-18 + review concern 1: only details whose line is durable reach later waves
                    if (result.Kind is ThreadOutcomeKind.Written or ThreadOutcomeKind.Warnings && result.Record != null)
                        deltas[result.Record.ThreadId] = result.Record.LedgerDelta.Concat(result.AttachmentDeltas).ToList();
                }

                // Review concern 5: earlier-wave repairs reach later waves; same-wave threads still never see each other
                for (var r = 0; r < repaired.Length; r++)
                {
                    var done = repaired[r];
                    if (done == null || done.Unjournaled || done.Deltas.Count == 0)
                        continue;

                    var id = repairIds[r];
                    deltas[id] = (deltas.TryGetValue(id, out var existing) ? existing : new List<LedgerDelta>()).Concat(done.Deltas).ToList();
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
                if (pending.Count > 0)
                {
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
                        // WR-02: an Unjournaled thread stays pending (its NotSaved row explains why), as on the tiles
                        Failed = results.Count(r => r.Kind == ThreadOutcomeKind.Failed && !r.Unjournaled),
                        LedgerTotal = merged.Entries.Count
                    });
                }

                await CheckpointAsync(ctx, final: false, outcome, message).ConfigureAwait(false);
            }

            // D-25: non-relevant mail only after the last relevant wave, in the same run and progress stream
            if (proceed && outcome == GenerationOutcome.Finished)
            {
                ct.ThrowIfCancellationRequested();
                var pendingFiller = NonRelevantThreads(threads.Values)
                    .Where(t => !state.IsDone(t.Id))
                    .OrderBy(t => t.Messages.Where(m => m != null).Min(m => m.SentAt.UtcDateTime))
                    .ThenBy(t => t.Id, StringComparer.Ordinal)
                    .ToList();
                if (pendingFiller.Count > 0 || fillerRepairs.Count > 0)
                    (outcome, message) = await RunNonRelevantAsync(ctx, uow, state, replay, pendingFiller, fillerRepairs, threads,
                        fillerRow, run, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // In-flight threads were never journaled and stay pending (UI-SPEC C-G1)
            outcome = GenerationOutcome.Cancelled;
            message = string.Empty;
        }
        catch (JournalFullException ex)
        {
            // IN-06: the journal was already at its stop line (RunStarted refused): nothing was called or written
            outcome = GenerationOutcome.Fatal;
            message = ex.Message;
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

            // D-10: results collected but never used are still billed; RunEnded carries them once (OrphansBilled)
            OrphanBill? orphans = null;
            if (outcome == GenerationOutcome.Finished && batch != null)
            {
                // Amended D-07: wave batches an earlier build submitted are never consumed now; close them so Continue never waits on them (the provider may still bill items it completed; that spend is not itemized)
                await CloseStaleWaveBatchesAsync(batch, snapshot).ConfigureAwait(false);
                orphans = AddOrphanSpend(ctx, batch);
            }

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

            // CR-A03: the journal is the commit point; orphans are marked billed only once RunEnded carries their spend
            if (orphans != null && batch != null && ctx.RunEndedAppended)
                await MarkOrphansBilledAsync(batch, orphans).ConfigureAwait(false);

            // Always runs, so the snapshot never stays "running" without a RunEnded event
            FinishSnapshot(snapshot, final, outcome, message);

            // D-09: stop polling; provider batches still running stay in the ledger for the next Continue
            if (batch != null)
            {
                batch.StatusChanged -= snapshot.RecordBatchStatus;
                batch.Detach();
            }
        }

        return new GenerationRunResult
        {
            Outcome = outcome,
            Message = message,
            State = final?.State ?? state,
            Ledger = final?.Ledger ?? startLedger
        };
    }

    /// <summary>
    /// Stage B (D-25, NOISE-01..05): the side world from the journal or one Volume call, journaled before use, then every
    /// pending non-relevant thread in chunks with its filler assignment and category profile, then the attachment repairs of
    /// written non-relevant threads. Returns the run outcome: Finished, Fatal (text provider) or InternalError (side
    /// projects not saved). Throws only when the user cancels.
    /// </summary>
    private async Task<(GenerationOutcome Outcome, string Message)> RunNonRelevantAsync(RunContext ctx, ThreadUnitOfWork uow,
        GenerationRunState state, JournalReplay replay, List<PlannedThread> pending, List<AttachmentRepairGroup> repairs,
        IReadOnlyDictionary<string, PlannedThread> threads, int fillerRow, PlanCallRun run, CancellationToken ct)
    {
        var input = ctx.Input;
        var plan = input.Plan;
        var snapshot = ctx.Snapshot;

        // D-21: one side world per case, replayed on Continue so filler keeps talking about the same projects
        var sideWorld = SideWorldBuilder.FromRecord(replay.LastSideWorld, ctx.Guide);
        if (sideWorld == null)
        {
            snapshot.SetStage(GenerationStage.SideProjects, count: SideWorldBuilder.CoveredOrganizations(ctx.Guide).Count);
            var so = await SideWorldBuilder.BuildAsync(_llm, input.Draft, ctx.Guide, run).ConfigureAwait(false);
            var cost = so.Cost.TotalUsd;
            snapshot.AddCost(cost);
            if (run.IsFatal && so.World == null)
            {
                ctx.AddUnjournaledSpend(so.Cost, 0m, so.InputTokens, so.OutputTokens);
                return (GenerationOutcome.Fatal, run.FatalError);
            }

            sideWorld = so.World!;
            try
            {
                await AppendOwnRecordAsync(ctx.Journal, new JournalRecord
                {
                    Type = JournalRecordType.SideWorld,
                    Utc = DateTime.UtcNow,
                    PlanId = plan.PlanId,
                    SideWorld = sideWorld,
                    InputTokens = so.InputTokens,
                    OutputTokens = so.OutputTokens,
                    CostUsd = cost,
                    CostDetail = so.Cost.Clone()
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Review 06-19: Continue promises the same side projects; never use a choice the journal does not hold
                ctx.AddUnjournaledSpend(so.Cost, 0m, so.InputTokens, so.OutputTokens);
                return (GenerationOutcome.InternalError, $"Could not save the side projects: {ex.Message}");
            }

            snapshot.AddEvent(new GenerationEvent
            {
                Kind = GenerationEventKind.SideProjectsReady,
                LocalTime = DateTime.Now,
                Projects = sideWorld.Organizations.Sum(o => o.Projects.Count),
                Organizations = sideWorld.Organizations.Count
            });
        }

        if (pending.Count > 0)
        {
            snapshot.SetStage(GenerationStage.NonRelevant, count: pending.Count);
            snapshot.SetWaveRunning(fillerRow, true);
            snapshot.AddEvent(new GenerationEvent
            {
                Kind = GenerationEventKind.NonRelevantStarted,
                LocalTime = DateTime.Now,
                Threads = pending.Count,
                Emails = pending.Sum(t => t.Messages.Count(m => m != null))
            });

            var written = 0;
            var warnings = 0;
            var failed = 0;

            // Amended D-07: Stage B fans out at batch width, but its live attempts (D-10 fallbacks, retries) keep live parallelism
            using var batchRun = input.Batch != null ? new PlanCallRun(run.UserToken, BatchMaxParallel, RetryHint) : null;
            using var liveGate = input.Batch != null ? new SemaphoreSlim(_maxParallel, _maxParallel) : null;
            var unitRun = batchRun ?? run;
            try
            {
                // Filler has no ledger dependency: chunks only bound memory and checkpoint cadence (D-25)
                // D-07: batch mode sends one provider batch per 2,000 threads; live mode keeps the 50-thread cadence
                foreach (var chunk in pending.Chunk(input.Batch != null ? BatchFillerChunkSize : FillerChunkSize))
                {
                    ct.ThrowIfCancellationRequested();
                    snapshot.SetStage(GenerationStage.NonRelevant, count: pending.Count);
                    ctx.ChainScheduler.BeginStep(chunk.Select(t => t.Id));
                    var step = StepContext(ctx, fillerRow);
                    List<Task<ThreadOutcome>> tasks;
                    using (BatchStage.Enter(NonRelevantStageLabel))
                    using (liveGate != null ? BatchStage.UseLiveGate(liveGate) : null)
                    {
                        tasks = chunk
                            .Select(thread =>
                            {
                                // D-08, D-09, D-13: filler assignments and the category profile; never the ledger
                                var render = ThreadRenderContext.Create(input.Draft, plan, thread,
                                    ctx.Assignments.TryGetValue(thread.Id, out var a) ? a : new ThreadAssignment { ThreadId = thread.Id },
                                    ctx.People, new List<LedgerEntry>(), fillerRow);
                                render.PreviousStatus = Status(state, thread.Id);
                                return RunThreadAsync(ctx, uow, render, RenderProfile.For(thread, _llm, sideWorld), step, unitRun);
                            })
                            .ToList();
                    }
                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                    written += results.Count(r => r.Kind == ThreadOutcomeKind.Written);
                    warnings += results.Count(r => r.Kind == ThreadOutcomeKind.Warnings);
                    failed += results.Count(r => r.Kind == ThreadOutcomeKind.Failed && !r.Unjournaled);

                    if (run.IsFatal)
                        return (GenerationOutcome.Fatal, run.FatalError);
                    if (unitRun.IsFatal)
                        return (GenerationOutcome.Fatal, unitRun.FatalError);

                    ct.ThrowIfCancellationRequested();
                    await CheckpointAsync(ctx, final: false, GenerationOutcome.Finished, string.Empty).ConfigureAwait(false);
                }
            }
            finally
            {
                snapshot.SetWaveRunning(fillerRow, false);
            }

            snapshot.AddEvent(new GenerationEvent
            {
                Kind = GenerationEventKind.NonRelevantFinished,
                LocalTime = DateTime.Now,
                Written = written,
                Warnings = warnings,
                Failed = failed,
                Relevant = false
            });
        }

        // Non-relevant repairs after the side world exists: no ledger, same slots, the repair row
        if (repairs.Count == 0)
            return (GenerationOutcome.Finished, string.Empty);

        StartRepairs(ctx);
        snapshot.SetWaveRunning(ctx.RepairRow, true);
        try
        {
            foreach (var chunk in repairs.Chunk(FillerChunkSize))
            {
                ct.ThrowIfCancellationRequested();
                snapshot.SetStage(GenerationStage.Attachments, count: repairs.Sum(g => g.AttachmentDocIds.Count));
                var ordered = RepairOrderHook?.Invoke(chunk.ToList()) ?? chunk.ToList();
                ctx.ChainScheduler.BeginStep(ordered.Select(g => g.ThreadId));
                var steps = new List<ThreadAttachmentContext>();
                var tasks = new List<Task<AttachmentRepairOutcome?>>();

                // Revision 1 W9: repairs run live even in batch mode
                using (BatchStage.EnterLive())
                {
                    foreach (var group in ordered)
                    {
                        var thread = threads[group.ThreadId];
                        var render = ThreadRenderContext.Create(input.Draft, plan, thread,
                            ctx.Assignments.TryGetValue(thread.Id, out var a) ? a : new ThreadAssignment { ThreadId = thread.Id },
                            ctx.People, new List<LedgerEntry>(), fillerRow);
                        var actx = StepContext(ctx, ctx.RepairRow);
                        steps.Add(actx);
                        tasks.Add(RunRepairAsync(ctx, state, group, render, RenderProfile.For(thread, _llm, sideWorld), actx, run));
                    }
                }

                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                finally
                {
                    KeepSlots(ctx, steps);
                }

                // A fatal stop skips the remaining repairs; committed ones stay journaled
                if (run.IsFatal)
                    return (GenerationOutcome.Fatal, run.FatalError);

                ct.ThrowIfCancellationRequested();
                await CheckpointAsync(ctx, final: false, GenerationOutcome.Finished, string.Empty).ConfigureAwait(false);
            }
        }
        finally
        {
            snapshot.SetWaveRunning(ctx.RepairRow, false);
        }

        return (GenerationOutcome.Finished, string.Empty);
    }

    // The first repair step of the run announces every repair (UI-SPEC "Adding n missing attachments to m written threads")
    private static void StartRepairs(RunContext ctx)
    {
        if (Interlocked.Exchange(ref ctx.RepairAnnounced, 1) == 1)
            return;

        ctx.Snapshot.AddEvent(new GenerationEvent
        {
            Kind = GenerationEventKind.AttachmentRepairStarted,
            LocalTime = DateTime.Now,
            Attachments = ctx.RepairAttachments,
            Threads = ctx.RepairThreads
        });
    }

    // One attachment context per relevant repair: known invite slots, the repair row and the late-repair constraints
    private static ThreadAttachmentContext RepairContext(RunContext ctx, int waveIndex, string threadId)
    {
        var actx = StepContext(ctx, ctx.RepairRow);
        actx.LateRepair = ctx.LastFinishedWave > waveIndex;

        // Checker 06-20 info: a late repair must not contradict later waves, nor same-wave mail written before this run
        actx.LaterEstablished = ctx.StartLedger.Entries
            .Where(e => e != null && (e.WaveIndex > waveIndex || (e.WaveIndex == waveIndex && e.SourceThreadId != threadId)))
            .ToList();
        return actx;
    }

    /// <summary>
    /// One thread's attachment repair: waits for its lower-ranked chain predecessors, then takes a repair slot. Returns
    /// null when the run stopped before the repair started. Throws only when the user cancels.
    /// </summary>
    private async Task<AttachmentRepairOutcome?> RunRepairAsync(RunContext ctx, GenerationRunState state, AttachmentRepairGroup group,
        ThreadRenderContext render, RenderProfile profile, ThreadAttachmentContext actx, PlanCallRun run)
    {
        var threadId = group.ThreadId;
        var started = false;
        var acquired = false;
        try
        {
            if (run.IsFatal)
            {
                ctx.ChainScheduler.Complete(threadId, EmptyTexts);
                return null;
            }

            // Checker 06-18/06-20: never hold a repair slot while a lower-ranked predecessor still needs one; waits only point to lower ranks (a DAG), so slot holders can always finish
            await ctx.ChainScheduler.WaitForPredecessorsAsync(threadId, run.UserToken).ConfigureAwait(false);
            await ctx.RepairSlots.WaitAsync(run.UserToken).ConfigureAwait(false);
            acquired = true;
            RepairSlotHook?.Invoke(threadId, 1);

            if (run.IsFatal)
            {
                ctx.ChainScheduler.Complete(threadId, EmptyTexts);
                return null;
            }

            started = true;
            var outcome = await ctx.Repair.RepairThreadAsync(render, profile, state.Records[threadId], state.AttachmentRecords, group,
                actx, run).ConfigureAwait(false);
            StopIfJournalFull(ctx, run);

            // Checker 06-19: tokens and dollars each counted exactly once
            if (outcome.Unjournaled)
                ctx.AddUnjournaledSpend(outcome.Cost, outcome.MediaCostUsd, outcome.InputTokens, outcome.OutputTokens);
            else
                ctx.AddUnjournaledSpend(outcome.NotRunCost, outcome.NotRunMediaCostUsd, outcome.NotRunInputTokens,
                    outcome.NotRunOutputTokens);

            if (!outcome.Unjournaled)
                await MaybeProjectAsync(ctx).ConfigureAwait(false);
            return outcome;
        }
        catch (Exception)
        {
            // The repair completes in its own finally; this covers a cancel while waiting (idempotent)
            if (!started)
                ctx.ChainScheduler.Complete(threadId, EmptyTexts);
            throw;
        }
        finally
        {
            if (acquired)
            {
                RepairSlotHook?.Invoke(threadId, -1);
                ctx.RepairSlots.Release();
            }
        }
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
            StopIfJournalFull(ctx, run);
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
            ctx.AddUnjournaledSpend(outcome.UnjournaledCost, outcome.UnjournaledMediaCostUsd, outcome.InputTokens,
                outcome.OutputTokens);

        AddUnjournaled(ctx, outcome);
        return outcome;
    }

    // D-29 + review concern 1: every token and dollar with no journal line of its own reaches RunEnded exactly once
    private static void AddUnjournaled(RunContext ctx, ThreadOutcome result)
    {
        // A NotRun thread has no attachments and Unjournaled false (counted above); a Failed thread's line already carries its spend
        if (result.Unjournaled)
            ctx.AddUnjournaledSpend(result.UnjournaledCost, result.UnjournaledMediaCostUsd, result.InputTokens, result.OutputTokens);
        else if (result.NotRunCostUsd > 0 || result.NotRunInputTokens > 0 || result.NotRunOutputTokens > 0)
        {
            // Checker 06-19: NotRun attachments have no record; the Unjournaled totals already include them
            ctx.AddUnjournaledSpend(result.NotRunCost, result.NotRunMediaCostUsd, result.NotRunInputTokens,
                result.NotRunOutputTokens);
        }
    }

    /// <summary>
    /// Minimum time between per-thread projections: ten times the last one's duration, never under
    /// <see cref="KeyProjectionInterval"/> and never over <see cref="MaxKeyProjectionInterval"/>.
    /// </summary>
    internal static TimeSpan ProjectionIntervalFor(TimeSpan lastDuration)
    {
        // Pattern 12: at 5K emails one projection (replay + key CSV/XLSX + case.json) takes seconds; never spend most of the run re-projecting
        return TimeSpan.FromTicks(Math.Min(MaxKeyProjectionInterval.Ticks,
            Math.Max(KeyProjectionInterval.Ticks, lastDuration.Ticks * 10)));
    }

    /// <summary>
    /// Re-projects the key and checkpoints after a thread finishes, at most every <see cref="ProjectionIntervalFor"/> of the
    /// last projection's duration and never twice at once. Never throws: a locked key is reported in the checkpoint and the
    /// thread stays finished.
    /// </summary>
    private static async Task MaybeProjectAsync(RunContext ctx)
    {
        if (DateTime.UtcNow - ctx.ReadLastProjection() < ProjectionIntervalFor(ctx.ReadProjectionDuration()))
            return;

        // Skip when a projection is already running; the next finishing thread or the wave end catches up
        if (!await ctx.ProjectionGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (DateTime.UtcNow - ctx.ReadLastProjection() < ProjectionIntervalFor(ctx.ReadProjectionDuration()))
                return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await CheckpointCoreAsync(ctx, final: false, GenerationOutcome.None, string.Empty).ConfigureAwait(false);
            }
            finally
            {
                ctx.RecordProjectionDuration(watch.Elapsed);
            }
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
            ctx.Input.Media != null, ctx.Guide, ctx.Input.Batch != null);

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

    // D-07 stage labels: provider batches never mix two labels
    private const string NonRelevantStageLabel = "non-relevant mail";

    private static string WaveStageLabel(int waveIndex) => $"wave {waveIndex}";

    private static async Task CloseStaleWaveBatchesAsync(BatchingLlmProvider batch, GenerationSnapshot snapshot)
    {
        try
        {
            await batch.CancelPendingStagesAsync(label => label.StartsWith("wave ", StringComparison.Ordinal), CancellationToken.None)
                .ConfigureAwait(false);
            snapshot.SetPendingProviderBatches(batch.PendingBatchCount);
        }
        catch (Exception)
        {
            // RunAsync never throws; a stale wave batch left pending is retried at the next finished run
        }
    }

    // D-10 + T-07-36: orphan spend joins the unjournaled spend RunEnded carries. CR-A03: priced without side effects; the
    // batches are marked billed only after RunEnded is appended, so a failed append leaves them for the next finished run
    private static OrphanBill? AddOrphanSpend(RunContext ctx, BatchingLlmProvider batch)
    {
        try
        {
            var orphan = batch.PriceOrphans();
            if (orphan.IsEmpty)
                return null;

            if (orphan.Cost.TotalUsd > 0 || orphan.InputTokens > 0 || orphan.OutputTokens > 0)
            {
                ctx.AddUnjournaledSpend(orphan.Cost, 0m, orphan.InputTokens, orphan.OutputTokens);
                ctx.Snapshot.AddCost(orphan.Cost.TotalUsd);
            }

            return orphan;
        }
        catch (Exception)
        {
            // RunAsync never throws; an unpriced orphan stays unbilled in the ledger and the next finished run bills it
            return null;
        }
    }

    private static async Task MarkOrphansBilledAsync(BatchingLlmProvider batch, OrphanBill orphans)
    {
        try
        {
            await batch.MarkOrphansBilledAsync(orphans, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // RunAsync never throws; RunEnded already carries the spend, so at worst a later run reports it again (over, never under)
        }
    }

    private static async Task AppendRunEndedAsync(RunContext ctx, GenerationOutcome outcome, string? message)
    {
        // Calls a fatal stop left without a thread record are still spend (WR-02)
        var (inputTokens, outputTokens, detail, media) = ctx.ReadUnjournaledSpend();
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
                // Fully itemized (revision 1): text = CostDetail, media apart; replay counts CostUsd + MediaCostUsd
                CostUsd = detail.TotalUsd,
                MediaCostUsd = media,
                CostDetail = detail
            }, CancellationToken.None).ConfigureAwait(false);
            ctx.RunEndedAppended = true;
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

    // IN-06: once the journal refuses lines, every further call would be paid for and never saved; stop the run instead
    private static void StopIfJournalFull(RunContext ctx, PlanCallRun run)
    {
        if (ctx.Journal.IsFull)
            run.SetFatal(GenerationJournal.FullMessage);
    }

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

    // Slots committed in the step stay known to later steps. D-20 + WR-04: per meeting key the earliest planned invite
    // (parent SentAt, then DocId) wins, the rule GenerationRunState applies on replay, so finish order never matters
    internal static void KeepSlots(ConcurrentDictionary<string, InviteSlot> known, IEnumerable<ThreadAttachmentContext> steps)
    {
        var committed = steps
            .Where(s => s != null)
            .SelectMany(s => s.CommittedInvites.Values)
            .Where(i => i?.Slot != null && !string.IsNullOrEmpty(i.Slot.MeetingKey))
            .OrderBy(i => i.ParentUtc)
            .ThenBy(i => i.DocId, StringComparer.Ordinal)
            .ToList();
        foreach (var invite in committed)
            known.TryAdd(invite.Slot.MeetingKey, invite.Slot);
    }

    private static void KeepSlots(RunContext ctx, IEnumerable<ThreadAttachmentContext> steps) => KeepSlots(ctx.InviteSlots, steps);

    /// <summary>
    /// Wave rows plus the Non-relevant mail row (when the plan has non-relevant threads), with thread and attachment
    /// counts from the replayed state and the attachment seeds so live counters start where the journal left off.
    /// </summary>
    private static List<WaveProgress> StageRows(CorpusPlan plan, List<GenerationWave> waves, GenerationRunState state,
        IReadOnlyDictionary<string, AttachmentSeed> seeds, IReadOnlyDictionary<string, PlannedThread> threads, int fillerRow,
        int repairRow, int repairThreads)
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

        // Only when the run starts with repairs pending; its attachment counters come from the seeds on repairRow, so
        // wave rows count only attachments of threads pending at run start and tiles never double count
        if (repairThreads > 0)
        {
            rows.Add(new WaveProgress
            {
                Index = repairRow,
                RowKind = StageRowKind.Attachments,
                Threads = repairThreads
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

    /// <summary>
    /// The plan-wide render scaffolding every thread context is built from: waves of the whole plan, the wave of each
    /// relevant thread, every assignment (filler included) and the people directory. Shared with the QA scans so a
    /// re-check sees exactly what the run saw.
    /// </summary>
    internal static RunScaffold BuildScaffold(CaseDraft draft, CorpusPlan plan)
    {
        var guide = draft?.Guide ?? new CaseGuide();
        var waves = WavePlanner.Plan(plan);
        return new RunScaffold
        {
            Waves = waves,
            WaveOf = WavePlanner.WaveIndexByThread(waves),
            Assignments = GenerationRunState.AllAssignments(plan),
            People = PeopleDirectory.Create(guide, plan)
        };
    }

    // Per-run shared values; one instance per RunAsync call
    private sealed class RunContext
    {
        private readonly object _lock = new();
        private DateTime _lastProjectionUtc;
        private TimeSpan _lastProjectionDuration;
        private int _unjournaledInput;
        private int _unjournaledOutput;
        private readonly CostBreakdown _unjournaledDetail = new();
        private decimal _unjournaledMedia;

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

        // D-26: attachment repair of written threads, bounded by its own slots
        public AttachmentRepair Repair { get; set; } = null!;
        public SemaphoreSlim RepairSlots { get; set; } = null!;
        public int RepairAttachments { get; set; }
        public int RepairThreads { get; set; }
        public int RepairRow { get; set; }

        // Set once by the first repair step (Interlocked)
        public int RepairAnnounced;

        // Review concern 5: highest wave holding a relevant thread finished (text and attachments) before this run
        public int LastFinishedWave { get; set; }
        public NarrativeLedger StartLedger { get; set; } = new();

        public SemaphoreSlim ProjectionGate { get; } = new(1, 1);

        // CR-A03: set once the final RunEnded line (with the unjournaled and orphan spend) is flushed
        public bool RunEndedAppended { get; set; }

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

        // Duration of the last per-thread projection; drives the adaptive interval (Pattern 12)
        public TimeSpan ReadProjectionDuration()
        {
            lock (_lock)
                return _lastProjectionDuration;
        }

        public void RecordProjectionDuration(TimeSpan duration)
        {
            lock (_lock)
                _lastProjectionDuration = duration;
        }

        // PROV-07: text spend arrives itemized from the gateway, media apart, so RunEnded is fully itemized
        public void AddUnjournaledSpend(CostBreakdown text, decimal media, int inputTokens, int outputTokens)
        {
            lock (_lock)
            {
                _unjournaledInput += inputTokens;
                _unjournaledOutput += outputTokens;
                _unjournaledDetail.Add(text);
                _unjournaledMedia += Math.Max(0m, media);
            }
        }

        public (int InputTokens, int OutputTokens, CostBreakdown Text, decimal Media) ReadUnjournaledSpend()
        {
            lock (_lock)
                return (_unjournaledInput, _unjournaledOutput, _unjournaledDetail.Clone(), _unjournaledMedia);
        }
    }
}

/// <summary>
/// Plan-wide render scaffolding (see <see cref="GenerationRunner.BuildScaffold"/>).
/// </summary>
internal class RunScaffold
{
    public List<GenerationWave> Waves { get; init; } = new();
    public Dictionary<string, int> WaveOf { get; init; } = new();
    public Dictionary<string, ThreadAssignment> Assignments { get; init; } = new();
    public PeopleDirectory People { get; init; } = null!;
}
