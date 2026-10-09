using System.Diagnostics;
using System.Text;
using ReelDiscovery.Models;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Flush and poll timings of <see cref="BatchingLlmProvider"/> (discretion: 3 s quiet period, 30 s polls, 60 s after
/// 10 minutes of waiting).
/// </summary>
public class BatchTimings
{
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan SlowPollInterval { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan SlowAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Wall clock for submitted/next-check times and the slow-poll switch; tests substitute a fixed clock.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// CX-07: how long <see cref="BatchingLlmProvider.DetachAsync"/> waits for submissions already in flight (upload, batch
    /// creation and their Submitted ledger line). Remote batch execution is never waited on.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

public enum BatchStatusKind
{
    Submitted,
    Progress,
    Finished,
    FinishedWithFailures,
    FailedAtProvider,
    CollectedAfterRestart
}

/// <summary>
/// One provider batch status change for the UI (D-09): "Waiting on provider batch, submitted 2:14 PM, 3 of 5 done".
/// </summary>
public class BatchStatusEvent
{
    public BatchStatusKind Kind { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string StageLabel { get; set; } = string.Empty;
    public LlmProviderKind Provider { get; set; }
    public DateTime SubmittedUtc { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }

    /// <summary>FailedAtProvider: plain-language reason ("the batch expired before it finished").</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTime NextCheckUtc { get; set; }

    /// <summary>Finished/FinishedWithFailures/FailedAtProvider/CollectedAfterRestart: time since submission.</summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Orphan spend priced by <see cref="BatchingLlmProvider.PriceOrphans"/> and not yet marked billed (CR-A03).
/// </summary>
public class OrphanBill
{
    public CostBreakdown Cost { get; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Priced custom ids per batch id; MarkOrphansBilledAsync marks these batches billed.</summary>
    public Dictionary<string, List<string>> ItemsByBatch { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// CR-A04: batches holding an item whose (provider, model) has no catalog price. They are left unbilled rather than
    /// recorded at $0; a later finished run whose catalog prices them bills them.
    /// </summary>
    public List<string> UnpricedBatchIds { get; } = new();

    /// <summary>Per priced item: its batch, catalog model, usage and cost (recorded on the session ticker, WR-A09).</summary>
    public List<(string BatchId, AIModelConfig Model, TokenUsage Usage, CostBreakdown Cost)> Lines { get; } = new();

    public bool IsEmpty => ItemsByBatch.Count == 0;
}

/// <summary>
/// Transparent provider batching (RUN-03, D-01): an <see cref="ILlmProvider"/> decorator behind LlmGateway. First attempts
/// that allow batching queue per <see cref="BatchStage"/> label and flush as one provider batch after a quiet period;
/// units keep awaiting <see cref="SendAsync"/> exactly as in live mode. Everything that cannot be answered from a batch
/// runs live at full price (D-10). Every submitted batch is in the ledger before anyone waits on it (RUN-04, D-09).
/// </summary>
public class BatchingLlmProvider : ILlmProvider
{
    // Per-item JSONL envelope (custom_id, method, url, params) on top of the prompt text
    private const int ItemOverheadBytes = 512;

    // Consecutive fatal (Auth/SpendLimit) poll failures, or any result download failures, before the waiters are released
    private const int MaxConsecutiveFailures = 5;

    private readonly ILlmProvider _live;
    private readonly IBatchTransport _transport;
    private readonly BatchLedger _ledger;
    private readonly Guid _planId;
    private readonly Func<LlmProviderKind, string, AIModelConfig?> _priceLookup;
    private readonly BatchTimings _timings;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    // All below guarded by _lock
    // CX-01: keyed by (stage label, model id). OpenAI accepts one model per batch input file, so a chunk mixing decoys
    // (Story tier) and filler (Volume tier) under one label would fail validation and run live; done for every provider
    private readonly Dictionary<(string Label, string ModelId), StageQueue> _queues = new();
    private readonly Dictionary<string, PendingItem> _inflight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AvailableResult> _available = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedBatch> _batches = new(StringComparer.Ordinal);
    private readonly HashSet<string> _orphansBilled = new(StringComparer.Ordinal);

    // CX-06: estimated input tokens of this provider's submitted-but-unfinished batches per model. The provider's queue
    // limit (OpenAI enqueued tokens) applies across every queued batch of a model, not per input file
    private readonly Dictionary<string, long> _queuedTokens = new(StringComparer.Ordinal);
    private TaskCompletionSource _capacityFreed = NewSignal();

    // CX-07: running flush loops (they own every submission), so DetachAsync can wait for in-flight submits
    private readonly List<Task> _flushes = new();

    /// <summary>Prices orphans by model id only (tests and single-provider catalogs).</summary>
    public BatchingLlmProvider(ILlmProvider live, IBatchTransport transport, BatchLedger ledger, Guid planId,
        Func<string, AIModelConfig?> priceLookup, BatchTimings? timings = null)
        : this(live, transport, ledger, planId, ToProviderLookup(priceLookup), timings)
    {
    }

    /// <summary>
    /// CR-A04: <paramref name="priceLookup"/> takes the provider that ran the batch and the item's model id, so results a
    /// batch of another provider left behind are priced with that provider's catalog model.
    /// </summary>
    public BatchingLlmProvider(ILlmProvider live, IBatchTransport transport, BatchLedger ledger, Guid planId,
        Func<LlmProviderKind, string, AIModelConfig?> priceLookup, BatchTimings? timings = null)
    {
        _live = live ?? throw new ArgumentNullException(nameof(live));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _planId = planId;
        _priceLookup = priceLookup ?? throw new ArgumentNullException(nameof(priceLookup));
        _timings = timings ?? new BatchTimings();

        // CR-A05: a ledger that exists but cannot be read is not "no batches": resubmitting would pay twice (D-09), so
        // this session runs every call live and leaves the ledger alone
        var state = _ledger.Replay();
        if (!state.Readable)
        {
            LiveOnly = true;
            return;
        }

        Recover(state);
    }

    /// <summary>
    /// True when the batch ledger could not be read at construction (CR-A05): every call runs live and nothing is
    /// submitted, resumed or billed from the ledger in this session.
    /// </summary>
    public bool LiveOnly { get; }

    private static Func<LlmProviderKind, string, AIModelConfig?> ToProviderLookup(Func<string, AIModelConfig?> priceLookup)
    {
        ArgumentNullException.ThrowIfNull(priceLookup);
        return (_, modelId) => priceLookup(modelId);
    }

    /// <summary>
    /// Rebuilds what an earlier session left behind (RUN-04): collected-but-unconsumed results become available, and items
    /// of this plan's pending batches of this provider become in-flight so matching requests attach instead of resubmitting.
    /// </summary>
    private void Recover(BatchLedgerState state)
    {
        _orphansBilled.UnionWith(state.OrphansBilledIds);

        foreach (var record in state.SubmittedById.Values.Where(r => r.PlanId == _planId && state.CollectedIds.Contains(r.BatchId)))
        {
            if (state.OrphansBilledIds.Contains(record.BatchId))
                continue;

            var batch = TrackedBatch.From(record, resumed: false);
            if (!_ledger.TryReadResults(record.BatchId, out var cached))
            {
                // CX-05: collected by a build that recorded Collected before its results cache was durable (or the cache
                // was lost since). Already paid for: download it again rather than resubmitting its items
                if (record.Provider == _transport.Kind
                    && batch.ItemIds.Any(id => !state.IsConsumed(record.BatchId, id) && !_inflight.ContainsKey(id)))
                {
                    var again = TrackedBatch.From(record, resumed: true);
                    _batches[again.BatchId] = again;
                    foreach (var id in again.ItemIds.Where(id => !state.IsConsumed(again.BatchId, id) && !_inflight.ContainsKey(id)))
                        _inflight[id] = new PendingItem(id, new LlmProviderRequest()) { BatchId = again.BatchId };
                }

                continue;
            }

            foreach (var result in cached)
            {
                // T-07-23: only ids this batch was submitted with; consumed results are never served twice (D-09). CR-A02:
                // keyed by batch, so a result of a newer batch for a regenerated thread's id is still served
                if (!batch.Contains(result.CustomId) || state.IsConsumed(record.BatchId, result.CustomId))
                    continue;

                if (IsUsable(result))
                    _available[result.CustomId] = new AvailableResult(record.BatchId, record.Provider, result, batch.ModelFor(result.CustomId));
                else
                    _failedIds.Add(result.CustomId);
            }
        }

        // Batches of another provider are left alone: their requests hash differently under the new model ids
        foreach (var record in state.Pending(_planId).Where(r => r.Provider == _transport.Kind))
        {
            var batch = TrackedBatch.From(record, resumed: true);
            _batches[batch.BatchId] = batch;

            // CX-06: still queued at the provider, so it counts against the queue limit until it is collected
            batch.QueuedModel = record.Items.FirstOrDefault(i => i != null)?.ModelId ?? string.Empty;
            batch.QueuedTokens = Math.Max(0, record.EstimatedInputTokens);
            ReserveLocked(batch.QueuedModel, batch.QueuedTokens);

            // CR-A02: consumption is per batch; the same id consumed from an older batch does not make this item done
            foreach (var id in batch.ItemIds.Where(id => !state.IsConsumed(batch.BatchId, id) && !_inflight.ContainsKey(id)))
                _inflight[id] = new PendingItem(id, new LlmProviderRequest()) { BatchId = batch.BatchId };
        }
    }

    /// <summary>
    /// Starts polling every pending batch of this plan and provider from an earlier session; their results are saved and
    /// served to matching requests. Returns the number of batches resumed.
    /// </summary>
    public int ResumePending()
    {
        if (_cts.IsCancellationRequested)
            return 0;

        List<TrackedBatch> resume;
        lock (_lock)
        {
            resume = _batches.Values.Where(b => b.Resumed && !b.Polling).ToList();
            foreach (var batch in resume)
            {
                batch.Polling = true;
                batch.PollStarted = true;
            }
        }

        foreach (var batch in resume)
            _ = Task.Run(() => PollLoopAsync(batch));

        return resume.Count;
    }

    /// <summary>
    /// Prices collected results of this plan that no unit consumed (still billed by the provider) with the catalog model at
    /// batch prices, appends OrphansBilled per batch and returns the spend. A second call returns zero (T-07-26). The run
    /// uses <see cref="PriceOrphans"/> and <see cref="MarkOrphansBilledAsync"/> instead, so the journal commits first (CR-A03).
    /// </summary>
    public async Task<(CostBreakdown Cost, int InputTokens, int OutputTokens)> BillOrphansAsync(CancellationToken ct = default)
    {
        var bill = PriceOrphans();
        await MarkOrphansBilledAsync(bill, ct);
        return (bill.Cost, bill.InputTokens, bill.OutputTokens);
    }

    /// <summary>
    /// Prices collected results of this plan that no unit consumed, without side effects: nothing is written and the
    /// results stay available. Call <see cref="MarkOrphansBilledAsync"/> only after the spend is journaled (CR-A03), so a
    /// failed or killed RunEnded leaves the orphans for the next finished run to bill.
    /// </summary>
    public OrphanBill PriceOrphans()
    {
        List<IGrouping<string, KeyValuePair<string, AvailableResult>>> groups;
        lock (_lock)
        {
            groups = _available.Where(kv => !_orphansBilled.Contains(kv.Value.BatchId))
                .GroupBy(kv => kv.Value.BatchId, StringComparer.Ordinal)
                .ToList();
        }

        var bill = new OrphanBill();
        foreach (var group in groups)
        {
            var cost = new CostBreakdown();
            var input = 0;
            var output = 0;
            var priced = true;
            var lines = new List<(string, AIModelConfig, TokenUsage, CostBreakdown)>();
            foreach (var (_, orphan) in group)
            {
                // CR-A04: the batch's own provider and model; no catalog price means unbilled, never a recorded $0
                var model = _priceLookup(orphan.Provider, orphan.ModelId);
                if (model == null)
                {
                    priced = false;
                    break;
                }

                var usage = orphan.Result.Response!.ToUsage();
                usage.IsBatch = true;
                usage.Normalize();
                var itemCost = model.CalculateCost(usage);
                cost.Add(itemCost);
                lines.Add((group.Key, model, usage, itemCost));
                input += usage.TotalInputTokens;
                output += usage.OutputTokens;
            }

            if (!priced)
            {
                bill.UnpricedBatchIds.Add(group.Key);
                continue;
            }

            bill.Cost.Add(cost);
            bill.Lines.AddRange(lines);
            bill.InputTokens += input;
            bill.OutputTokens += output;
            bill.ItemsByBatch[group.Key] = group.Select(kv => kv.Key).ToList();
        }

        return bill;
    }

    /// <summary>
    /// Appends OrphansBilled for every batch of <paramref name="bill"/> and stops serving its results, so neither this nor
    /// a later session bills them again. Call after the spend is journaled (CR-A03). A batch already marked is skipped.
    /// </summary>
    public async Task MarkOrphansBilledAsync(OrphanBill bill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bill);

        foreach (var (batchId, ids) in bill.ItemsByBatch)
        {
            ct.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_orphansBilled.Contains(batchId))
                    continue;
            }

            try
            {
                await _ledger.AppendAsync(new BatchLedgerRecord
                {
                    Type = BatchLedgerRecordType.OrphansBilled,
                    Utc = Now(),
                    Provider = _transport.Kind,
                    BatchId = batchId,
                    PlanId = _planId
                }, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The spend is already journaled; without the line a later session may report it again (over, never under)
            }

            lock (_lock)
            {
                foreach (var id in ids)
                {
                    if (_available.TryGetValue(id, out var found) && found.BatchId == batchId)
                        _available.Remove(id);
                }

                _orphansBilled.Add(batchId);
            }

            // WR-A09: the header ticker shows the same spend RunEnded journaled (D-16)
            var tracker = UsageTracker;
            if (tracker != null)
            {
                foreach (var line in bill.Lines.Where(l => string.Equals(l.BatchId, batchId, StringComparison.Ordinal)))
                    tracker.RecordUsage(OrphanOperation, line.Model, line.Usage, line.Cost);
            }
        }
    }

    private const string OrphanOperation = "Batch results not used";

    /// <summary>
    /// Session ticker (WR-A09): orphan spend is recorded here when it is marked billed, after the journal carries it.
    /// </summary>
    public TokenUsageTracker? UsageTracker { get; set; }

    /// <summary>
    /// Cancels this plan's pending provider batches (one transport per record's provider; a null transport is skipped),
    /// appends Cancelled, then deletes uploaded batch files (best effort). <paramref name="include"/> limits both the
    /// cancel and the file deletion to matching records; <paramref name="closeOnCancelFailure"/> records a batch Cancelled
    /// even when the provider cancel call fails. Returns the batch ids recorded Cancelled (or attempted). Start Over uses
    /// <see cref="CancelAllPendingAsync"/> instead (WR-A05).
    /// </summary>
    public static async Task<List<string>> CancelPendingAsync(BatchLedger ledger,
        Func<LlmProviderKind, IBatchTransport?> transportFor, Guid planId, Func<BatchLedgerRecord, bool>? include = null,
        bool closeOnCancelFailure = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(transportFor);

        var state = ReadForCancel(ledger);
        var pending = state.Pending(planId).Where(r => include == null || include(r)).ToList();
        var (cancelled, _) = await CancelRecordsAsync(ledger, pending, transportFor, closeOnCancelFailure,
            skipOnCancelFailure: false, ct);

        var deleteFrom = include == null
            ? state.SubmittedById.Values.Where(r => r.PlanId == planId)
            : state.SubmittedById.Values.Where(r => cancelled.Contains(r.BatchId, StringComparer.Ordinal));
        await DeleteRecordFilesAsync(deleteFrom, transportFor, ct);

        return cancelled;
    }

    /// <summary>
    /// Start Over (WR-A05): cancels every pending batch in the ledger, whatever plan submitted it (a re-plan leaves batches of
    /// earlier plan ids), and deletes the uploaded files of every batch it could reach. Returns the ids of pending batches
    /// that were not cancelled: no transport exists for their provider (no key), or the provider cancel call failed
    /// (CR-B01). Those get no Cancelled line and the caller keeps the ledger; the remaining batches are still cancelled.
    /// </summary>
    public static async Task<List<string>> CancelAllPendingAsync(BatchLedger ledger,
        Func<LlmProviderKind, IBatchTransport?> transportFor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(transportFor);

        var state = ReadForCancel(ledger);
        var pending = state.SubmittedById.Values
            .Where(r => !state.CollectedIds.Contains(r.BatchId) && !state.CancelledIds.Contains(r.BatchId))
            .OrderBy(r => r.SubmittedUtc)
            .ThenBy(r => r.BatchId, StringComparer.Ordinal)
            .ToList();
        var (_, notCancelled) = await CancelRecordsAsync(ledger, pending, transportFor, closeOnCancelFailure: false,
            skipOnCancelFailure: true, ct);

        await DeleteRecordFilesAsync(
            state.SubmittedById.Values.Where(r => !notCancelled.Contains(r.BatchId, StringComparer.Ordinal)), transportFor, ct);

        return notCancelled;
    }

    // CR-A05: never act on (or let Start Over delete) a ledger that could not be read
    private static BatchLedgerState ReadForCancel(BatchLedger ledger)
    {
        var state = ledger.Replay();
        if (!state.Readable)
            throw new IOException(BatchLedger.UnreadableMessage);

        return state;
    }

    // Returns the ids recorded Cancelled and the ids skipped for want of a transport (or, with skipOnCancelFailure, because
    // the provider cancel failed). Without either flag a failed cancel propagates.
    private static async Task<(List<string> Cancelled, List<string> Skipped)> CancelRecordsAsync(BatchLedger ledger,
        IEnumerable<BatchLedgerRecord> pending, Func<LlmProviderKind, IBatchTransport?> transportFor, bool closeOnCancelFailure,
        bool skipOnCancelFailure, CancellationToken ct)
    {
        var cancelled = new List<string>();
        var skipped = new List<string>();
        foreach (var record in pending)
        {
            var transport = transportFor(record.Provider);
            if (transport == null)
            {
                skipped.Add(record.BatchId);
                continue;
            }

            try
            {
                await transport.CancelAsync(record.BatchId, ct);
            }
            catch (Exception ex) when (skipOnCancelFailure && ex is not OperationCanceledException)
            {
                // CR-B01: the batch may still be running and billing; leave it pending in the ledger so it stays tracked
                skipped.Add(record.BatchId);
                continue;
            }
            catch (Exception ex) when (closeOnCancelFailure && ex is not OperationCanceledException)
            {
                // Best effort: the run already finished; record the batch closed so it is never waited on again (the provider may still bill items it completed)
            }

            cancelled.Add(record.BatchId);
            try
            {
                await ledger.AppendAsync(new BatchLedgerRecord
                {
                    Type = BatchLedgerRecordType.Cancelled,
                    Utc = DateTime.UtcNow,
                    Provider = record.Provider,
                    BatchId = record.BatchId,
                    PlanId = record.PlanId
                }, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Start Over deletes the ledger next; the provider batch is cancelled either way (a run-end close retries next run)
            }
        }

        return (cancelled, skipped);
    }

    private static async Task DeleteRecordFilesAsync(IEnumerable<BatchLedgerRecord> records,
        Func<LlmProviderKind, IBatchTransport?> transportFor, CancellationToken ct)
    {
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.InputFileId) && string.IsNullOrEmpty(record.OutputFileId))
                continue;

            var transport = transportFor(record.Provider);
            if (transport == null)
                continue;

            // Best effort: the transport ignores delete failures; uploaded prompts should not outlive the case
            await transport.DeleteFilesAsync(NullIfEmpty(record.InputFileId), NullIfEmpty(record.OutputFileId), ct);
        }
    }

    /// <summary>
    /// Closes this plan's pending batches of this provider whose stage label matches: best-effort provider cancel, a
    /// Cancelled ledger line (even when the cancel call fails) and file deletion, then stops tracking them so
    /// <see cref="PendingBatchCount"/> drops. For batches an earlier build submitted per relevant wave (pre amended D-07),
    /// which no unit consumes any more. Anyone still waiting on a closed batch runs live (D-10). Returns the number closed.
    /// </summary>
    public async Task<int> CancelPendingStagesAsync(Func<string, bool> stageMatch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stageMatch);

        // The poll loop stops at its next check instead of collecting a cancelled batch nobody waits on. Closed before the
        // provider cancel, so a poll that sees the cancelled batch never collects (or cleans up) it in the meantime
        List<TrackedBatch> closing;
        lock (_lock)
        {
            closing = _batches.Values.Where(b => !b.Closed && stageMatch(b.StageLabel ?? string.Empty)).ToList();
            foreach (var batch in closing)
                batch.Closed = true;
        }

        List<string> closed;
        try
        {
            closed = await CancelPendingAsync(_ledger, kind => kind == _transport.Kind ? _transport : null, _planId,
                include: r => r.Provider == _transport.Kind && stageMatch(r.StageLabel ?? string.Empty),
                closeOnCancelFailure: true, ct: ct);
        }
        catch (Exception)
        {
            // Nothing was recorded closed: keep polling these batches so their waiters are never stranded
            Reopen(closing);
            throw;
        }

        Reopen(closing.Where(b => !closed.Contains(b.BatchId, StringComparer.Ordinal)));
        foreach (var batchId in closed)
        {
            TrackedBatch? batch;
            lock (_lock)
            {
                if (!_batches.TryGetValue(batchId, out batch))
                    continue;

                batch.Closed = true;
            }

            ReleaseBatch(batch, Delivery.Errored(), ClosedReason);
        }

        return closed.Count;
    }

    // Undoes an early close; a poll loop that already stopped on it is started again (never while detached)
    private void Reopen(IEnumerable<TrackedBatch> batches)
    {
        var restart = new List<TrackedBatch>();
        lock (_lock)
        {
            foreach (var batch in batches)
            {
                batch.Closed = false;
                if (!batch.Polling && (!batch.Resumed || batch.PollStarted) && _batches.ContainsKey(batch.BatchId)
                    && !_cts.IsCancellationRequested)
                {
                    batch.Polling = true;
                    restart.Add(batch);
                }
            }
        }

        foreach (var batch in restart)
            _ = Task.Run(() => PollLoopAsync(batch));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Raised off the UI thread; subscribers marshal to the UI themselves.</summary>
    public event Action<BatchStatusEvent>? StatusChanged;

    public LlmProviderKind Kind => _live.Kind;

    public int? MaxTokensCeiling => _live.MaxTokensCeiling;

    /// <summary>Provider batches submitted (or resumed) and not yet collected.</summary>
    public int PendingBatchCount
    {
        get
        {
            lock (_lock)
                return _batches.Count;
        }
    }

    public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) =>
        _live.TestKeyAsync(modelId, ct);

    public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AllowBatch || LiveOnly || _cts.IsCancellationRequested)
            return await SendLiveAsync(request, ct);

        var label = BatchStage.Current;
        var key = (label, request.ModelId ?? string.Empty);
        var id = RequestIdentity.CustomIdFor(_live.Kind, request);

        if (BatchStage.IsLive)
        {
            // Amended D-07: live scopes (relevant waves, repairs) never wait a provider cycle; a result an earlier session
            // already collected is free, so use it
            AvailableResult? collected = null;
            lock (_lock)
            {
                if (_available.TryGetValue(id, out var found) && IsUsable(found.Result))
                {
                    _available.Remove(id);
                    collected = found;
                }
            }

            if (collected == null)
                return await SendLiveAsync(request, ct);

            await AppendConsumedAsync(collected.BatchId, id);
            return Copy(collected.Result.Response!, isBatch: true, withUsage: true);
        }

        TaskCompletionSource<Delivery>? waiter = null;
        AvailableResult? ready = null;
        var goLive = false;
        lock (_lock)
        {
            if (_cts.IsCancellationRequested)
            {
                // WR-A04: Detach ran after the unlocked check above; its stranded-waiter sweep would miss a new waiter
                goLive = true;
            }
            else if (_available.Remove(id, out var found))
            {
                ready = found;
            }
            else if (_failedIds.Contains(id))
            {
                // D-10: an item that already errored or expired in a batch runs live, never in a new batch
                goLive = true;
            }
            else
            {
                waiter = NewWaiter();
                if (_inflight.TryGetValue(id, out var inflight))
                {
                    // RUN-04/D-09: already in a pending batch; attach instead of paying twice
                    inflight.Waiters.Add(waiter);
                }
                else
                {
                    if (!_queues.TryGetValue(key, out var queue))
                    {
                        queue = new StageQueue();
                        _queues[key] = queue;

                        // Started under the lock, so a DetachAsync that cancels after this point always sees it (CX-07)
                        _flushes.RemoveAll(t => t.IsCompleted);
                        _flushes.Add(Task.Run(() => FlushLoopAsync(key)));
                    }

                    if (!queue.ById.TryGetValue(id, out var queued))
                    {
                        queued = new PendingItem(id, Snapshot(request));
                        queue.ById[id] = queued;
                        queue.Items.Add(queued);
                    }

                    queued.Waiters.Add(waiter);
                    queue.LastEnqueueTimestamp = Stopwatch.GetTimestamp();
                }
            }
        }

        if (ready != null)
        {
            await AppendConsumedAsync(ready.BatchId, id);
            return Copy(ready.Result.Response!, isBatch: true, withUsage: true);
        }

        if (goLive)
            return await SendLiveAsync(Live(request), ct);

        Delivery delivery;
        try
        {
            delivery = await waiter!.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // D-09: the caller stops waiting; the provider batch keeps running and stays in the ledger
            RemoveWaiter(key, id, waiter!);
            throw;
        }

        if (delivery.Fatal != null)
            return delivery.Fatal;

        if (delivery.Result is { Outcome: BatchItemOutcome.Succeeded, Response: not null } result)
            return Copy(result.Response, isBatch: true, withUsage: delivery.TryTakeUsage());

        // D-10: errored, expired or cancelled items run live at full price
        return await SendLiveAsync(Live(request), ct);
    }

    /// <summary>
    /// Every live send goes through here: inside <see cref="BatchStage.UseLiveGate"/> it waits on the gate (amended D-07).
    /// The gate is held only during the live call, never while waiting on a batch, so it cannot deadlock with run
    /// semaphores.
    /// </summary>
    private async Task<LlmProviderResponse> SendLiveAsync(LlmProviderRequest request, CancellationToken ct)
    {
        var gate = BatchStage.LiveGate;
        if (gate != null)
            await gate.WaitAsync(ct);

        try
        {
            return await _live.SendAsync(request, ct);
        }
        finally
        {
            gate?.Release();
        }
    }

    /// <summary>
    /// Stops the flush and poll loops. Provider batches keep running (D-09) and stay in the ledger for a later Continue.
    /// </summary>
    public void Detach()
    {
        if (_cts.IsCancellationRequested)
            return;

        _cts.Cancel();

        // Called after the run ended, so any remaining waiter was already released by its caller's token; anyone still
        // waiting runs live rather than hanging on a loop that no longer exists
        List<TaskCompletionSource<Delivery>> stranded;
        lock (_lock)
        {
            stranded = _queues.Values.SelectMany(q => q.Items).Concat(_inflight.Values)
                .SelectMany(i => i.Waiters).ToList();
            foreach (var item in _queues.Values.SelectMany(q => q.Items).Concat(_inflight.Values))
                item.Waiters.Clear();
        }

        Complete(stranded, Delivery.Errored());
    }

    /// <summary>
    /// CX-07: <see cref="Detach"/>, then waits (up to <see cref="BatchTimings.DrainTimeout"/>) for submissions already in
    /// flight to finish, so every batch the provider accepted has its Submitted ledger line before the run reports
    /// stopped. Never waits on remote batch execution; never throws.
    /// </summary>
    public async Task DetachAsync()
    {
        Detach();

        Task[] flushes;
        lock (_lock)
            flushes = _flushes.Where(t => !t.IsCompleted).ToArray();

        if (flushes.Length == 0)
            return;

        try
        {
            // Flush loops catch their own failures; after the cancel only an upload already started keeps one running
            await Task.WhenAll(flushes).WaitAsync(_timings.DrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A submit stuck past the bound still records its batch when it returns; the run must not hang on it
        }
    }

    // ----- queue and flush (D-07) -----

    private async Task FlushLoopAsync((string Label, string ModelId) key)
    {
        var label = key.Label;
        List<PendingItem>? taken = null;
        try
        {
            var delay = _timings.QuietPeriod;
            while (true)
            {
                await Task.Delay(delay, _cts.Token);

                lock (_lock)
                {
                    if (!_queues.TryGetValue(key, out var queue))
                        return;

                    // Every caller of an item cancelled before the flush: nobody wants it, so never pay for it
                    foreach (var abandoned in queue.Items.Where(i => i.Waiters.Count == 0).ToList())
                    {
                        queue.Items.Remove(abandoned);
                        queue.ById.Remove(abandoned.CustomId);
                    }

                    if (queue.Items.Count == 0)
                    {
                        _queues.Remove(key);
                        return;
                    }

                    var quietFor = Stopwatch.GetElapsedTime(queue.LastEnqueueTimestamp);
                    if (quietFor < _timings.QuietPeriod && queue.Items.Count < Math.Max(1, _transport.MaxRequestsPerBatch))
                    {
                        delay = _timings.QuietPeriod - quietFor;
                        continue;
                    }

                    // New arrivals start a fresh queue and flush loop for this label and model
                    _queues.Remove(key);
                    taken = new List<PendingItem>();
                    foreach (var item in queue.Items)
                    {
                        if (_inflight.TryGetValue(item.CustomId, out var existing))
                        {
                            // The same request was taken from another stage's queue first: share its batch item
                            existing.Waiters.AddRange(item.Waiters);
                            continue;
                        }

                        // BatchId stays null while submitting; identical requests attach instead of resubmitting
                        _inflight[item.CustomId] = item;
                        taken.Add(item);
                    }
                }

                foreach (var chunk in Split(taken).ToList())
                    await SubmitAsync(label, chunk);

                return;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Detached while submitting: whatever was not submitted runs live
            if (taken != null)
                ReleaseUnsubmitted(taken);
            else
                ReleaseQueue(key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bookkeeping bug must not strand units: whatever was not submitted runs live
            if (taken != null)
                ReleaseUnsubmitted(taken);
        }
    }

    private IEnumerable<List<PendingItem>> Split(List<PendingItem> items)
    {
        var maxCount = Math.Max(1, _transport.MaxRequestsPerBatch);
        var tokenCap = _transport.MaxEstimatedInputTokens;
        var chunk = new List<PendingItem>();
        long bytes = 0;

        foreach (var item in items)
        {
            var itemBytes = EstimateBytes(item.Request);

            // Bytes / 4 is the usual estimate of input tokens (OpenAI batch queue limit, Pitfall 6)
            var full = chunk.Count >= maxCount
                       || bytes + itemBytes > _transport.MaxBatchBytes
                       || (tokenCap != null && (bytes + itemBytes) / 4 > tokenCap.Value);
            if (full && chunk.Count > 0)
            {
                yield return chunk;
                chunk = new List<PendingItem>();
                bytes = 0;
            }

            chunk.Add(item);
            bytes += itemBytes;
        }

        if (chunk.Count > 0)
            yield return chunk;
    }

    // Bytes / 4 is the usual estimate of input tokens; the same estimate Split uses for its per-chunk cap
    private static long EstimateTokens(IEnumerable<PendingItem> chunk) => chunk.Sum(i => EstimateBytes(i.Request)) / 4;

    /// <summary>
    /// CX-06: waits until this model's queued tokens plus <paramref name="tokens"/> fit the provider's queue limit, then
    /// reserves them. A chunk always goes when nothing of its model is queued (Split already keeps it within the cap). The
    /// wait ends when a batch of any model finishes or is released, or throws when the provider is detached.
    /// </summary>
    private async Task WaitForCapacityAsync(string model, long tokens)
    {
        var cap = _transport.MaxEstimatedInputTokens;
        while (true)
        {
            Task freed;
            lock (_lock)
            {
                var queued = _queuedTokens.GetValueOrDefault(model);
                if (cap == null || queued == 0 || queued + tokens <= cap.Value)
                {
                    ReserveLocked(model, tokens);
                    return;
                }

                freed = _capacityFreed.Task;
            }

            await freed.WaitAsync(_cts.Token);
        }
    }

    // Callers hold _lock (or run in the constructor)
    private void ReserveLocked(string model, long tokens)
    {
        if (tokens > 0)
            _queuedTokens[model] = _queuedTokens.GetValueOrDefault(model) + tokens;
    }

    // Callers hold _lock; wakes every chunk waiting for queue capacity
    private void FreeLocked(string model, long tokens)
    {
        if (tokens <= 0)
            return;

        var left = _queuedTokens.GetValueOrDefault(model) - tokens;
        if (left > 0)
            _queuedTokens[model] = left;
        else
            _queuedTokens.Remove(model);

        var freed = _capacityFreed;
        _capacityFreed = NewSignal();
        freed.TrySetResult();
    }

    // Callers hold _lock. Stops tracking a batch and returns its queued tokens; false when it was not tracked
    private bool UntrackLocked(TrackedBatch batch)
    {
        if (!_batches.TryGetValue(batch.BatchId, out var tracked) || !ReferenceEquals(tracked, batch))
            return false;

        _batches.Remove(batch.BatchId);
        FreeLocked(tracked.QueuedModel, tracked.QueuedTokens);
        return true;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static long EstimateBytes(LlmProviderRequest request) =>
        Encoding.UTF8.GetByteCount(request.SchemaJson ?? string.Empty)
        + Encoding.UTF8.GetByteCount(request.SystemPrompt ?? string.Empty)
        + Encoding.UTF8.GetByteCount(request.UserPrompt ?? string.Empty)
        + ItemOverheadBytes;

    // ----- submit -----

    private async Task SubmitAsync(string label, List<PendingItem> chunk)
    {
        // CX-06: every chunk of a model waits for room in the provider's queue; one rejected over the limit would run live
        // at full price. Chunks are single-model (CX-01)
        var model = chunk[0].Request.ModelId ?? string.Empty;
        var tokens = EstimateTokens(chunk);
        _cts.Token.ThrowIfCancellationRequested();
        await WaitForCapacityAsync(model, tokens);

        var tracked = false;
        try
        {
            tracked = await SubmitReservedAsync(label, chunk, model, tokens);
        }
        finally
        {
            // Not submitted, or submitted but not tracked: the reservation is not queued at the provider
            if (!tracked)
            {
                lock (_lock)
                    FreeLocked(model, tokens);
            }
        }
    }

    // Returns true once the batch is tracked (its reservation then lives on the TrackedBatch)
    private async Task<bool> SubmitReservedAsync(string label, List<PendingItem> chunk, string model, long tokens)
    {
        var requests = chunk.Select(i => new BatchItemRequest { CustomId = i.CustomId, Request = i.Request }).ToList();

        // CR-A01: a Detach before any bytes leave runs the chunk live; once the upload starts, the submit and its ledger
        // line always complete, so a batch the provider accepted (and bills) is never left untracked (RUN-04, D-09).
        // The transports bound the call with their own network timeouts.
        _cts.Token.ThrowIfCancellationRequested();
        var submit = await _transport.SubmitAsync(requests, CancellationToken.None);

        if (!submit.Succeeded)
        {
            // T-07-25: an auth or spend-limit failure stops the run through the gateway instead of thousands of live retries
            var delivery = IsFatal(submit.FailureKind)
                ? Delivery.FatalFailure(submit.FailureKind, submit.Message)
                : Delivery.Errored();
            ReleaseItems(chunk, delivery);

            // WR-A03: an upload whose batch was never created must not leave the prompts at the provider
            await DeleteFilesBestEffortAsync(submit.InputFileId, null);
            return false;
        }

        var submittedUtc = Now();
        var record = new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = submittedUtc,
            Provider = _transport.Kind,
            BatchId = submit.BatchId,
            PlanId = _planId,
            SubmittedUtc = submittedUtc,
            StageLabel = label,
            InputFileId = submit.InputFileId ?? string.Empty,
            Items = chunk.Select(i => new BatchLedgerItem
            {
                CustomId = i.CustomId,
                Operation = i.Request.SchemaName,
                ModelId = i.Request.ModelId
            }).ToList(),
            Total = chunk.Count,
            EstimatedInputTokens = tokens
        };

        try
        {
            await _ledger.AppendAsync(record, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // RUN-04: a batch we cannot track must not exist; cancel it (best effort) and run its items live
            try
            {
                await _transport.CancelAsync(submit.BatchId, CancellationToken.None);
            }
            catch (Exception cancelEx) when (cancelEx is not OperationCanceledException)
            {
                // Best effort: nothing more can be done for an untracked batch; its items still run live below
            }

            ReleaseItems(chunk, Delivery.Errored());
            await DeleteFilesBestEffortAsync(submit.InputFileId, null);
            return false;
        }

        var batch = new TrackedBatch(submit.BatchId, label, submittedUtc, resumed: false)
        {
            InputFileId = submit.InputFileId ?? string.Empty,
            QueuedModel = model,
            QueuedTokens = tokens
        };
        foreach (var item in record.Items)
            batch.Add(item.CustomId, item.ModelId);

        lock (_lock)
        {
            _batches[batch.BatchId] = batch;
            batch.Polling = true;
            foreach (var item in chunk)
                item.BatchId = batch.BatchId;
        }

        Raise(Event(BatchStatusKind.Submitted, batch, e =>
        {
            e.Total = chunk.Count;
            e.NextCheckUtc = submittedUtc + _timings.PollInterval;
        }));

        // Detached while submitting: the batch is in the ledger and a later Continue resumes it; nothing polls it now
        if (_cts.IsCancellationRequested)
        {
            lock (_lock)
                batch.Polling = false;
            return true;
        }

        _ = Task.Run(() => PollLoopAsync(batch));
        return true;
    }

    // ----- poll and collect -----

    private async Task PollLoopAsync(TrackedBatch batch)
    {
        var started = Now();
        var statusFailures = 0;
        var resultFailures = 0;
        try
        {
            while (true)
            {
                await Task.Delay(IntervalFor(Now() - started), _cts.Token);
                if (IsClosed(batch))
                    return;

                var status = await _transport.GetStatusAsync(batch.BatchId, _cts.Token);
                if (IsClosed(batch))
                    return;
                var now = Now();

                if (!status.Succeeded)
                {
                    // Network blips keep polling; only repeated auth/spend-limit failures release the waiters
                    statusFailures = IsFatal(status.FailureKind) ? statusFailures + 1 : 0;
                    if (statusFailures >= MaxConsecutiveFailures)
                    {
                        ReleaseBatch(batch, Delivery.FatalFailure(status.FailureKind, status.Message), StoppedWaitingReason);
                        return;
                    }

                    continue;
                }

                statusFailures = 0;
                switch (status.State)
                {
                    case BatchState.InProgress:
                        Raise(Event(BatchStatusKind.Progress, batch, e =>
                        {
                            e.Done = status.Done;
                            e.Total = status.Total;
                            e.Failed = status.Failed;
                            e.NextCheckUtc = now + IntervalFor(now - started);
                        }));
                        continue;
                    case BatchState.Failed:
                        await FailAtProviderAsync(batch, status);
                        return;
                }

                // Ended, Expired or Cancelled: whatever finished is downloadable; the rest is expired
                var results = await _transport.GetResultsAsync(batch.BatchId, _cts.Token);
                if (!results.Succeeded)
                {
                    resultFailures++;
                    if (resultFailures >= MaxConsecutiveFailures)
                    {
                        // Not marked Collected, so a later Continue can still collect and bill it
                        ReleaseBatch(batch, IsFatal(results.FailureKind)
                            ? Delivery.FatalFailure(results.FailureKind, results.Message)
                            : Delivery.Errored(), StoppedWaitingReason);
                        return;
                    }

                    continue;
                }

                if (IsClosed(batch))
                    return;

                await CollectAsync(batch, status, results);
                return;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Detached: stop polling; the provider batch keeps running (D-09)
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bookkeeping bug must not strand units: they run live
            ReleaseBatch(batch, Delivery.Errored(), StoppedWaitingReason);
        }
        finally
        {
            lock (_lock)
                batch.Polling = false;
        }
    }

    private async Task CollectAsync(TrackedBatch batch, BatchStatusResult status, BatchResultsResult results)
    {
        // T-07-23: only ids this batch was submitted with are delivered; foreign or repeated ids are ignored
        var byId = new Dictionary<string, BatchItemResult>(StringComparer.Ordinal);
        foreach (var item in results.Items)
        {
            if (item != null && batch.Contains(item.CustomId) && !byId.ContainsKey(item.CustomId))
                byId[item.CustomId] = item;
        }

        var ordered = batch.ItemIds.Select(id => byId.TryGetValue(id, out var found)
            ? found
            : new BatchItemResult { CustomId = id, Outcome = BatchItemOutcome.Expired, Message = "Missing from the batch results" })
            .ToList();

        var saved = false;
        try
        {
            _ledger.SaveResults(batch.BatchId, ordered);
            saved = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Results stay in memory for this session; the batch is not recorded Collected (below), so a restart
            // downloads it again instead of resubmitting its already-paid items
        }

        // CX-05: Collected is the commit point of a durable results cache. Without the cache the batch stays pending in
        // the ledger and a later session downloads its results again (the provider files are kept, below)
        if (saved)
        {
            try
            {
                await _ledger.AppendAsync(new BatchLedgerRecord
                {
                    Type = BatchLedgerRecordType.Collected,
                    Utc = Now(),
                    Provider = _transport.Kind,
                    BatchId = batch.BatchId,
                    PlanId = _planId,
                    SubmittedUtc = batch.SubmittedUtc,
                    StageLabel = batch.StageLabel,
                    OutputFileId = status.OutputFileId ?? string.Empty,
                    Done = status.Done,
                    Total = ordered.Count
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A restart collects this batch again; Consumed lines keep it from being delivered twice
            }
        }

        foreach (var result in ordered)
            await DeliverAsync(batch, result);

        lock (_lock)
            UntrackLocked(batch);

        var succeeded = ordered.Count(IsUsable);
        var failed = ordered.Count - succeeded;
        var kind = batch.Resumed
            ? BatchStatusKind.CollectedAfterRestart
            : failed > 0 ? BatchStatusKind.FinishedWithFailures : BatchStatusKind.Finished;
        Raise(Event(kind, batch, e =>
        {
            e.Done = ordered.Count;
            e.Total = ordered.Count;
            e.Succeeded = succeeded;
            e.Failed = failed;
            e.Duration = Now() - batch.SubmittedUtc;
        }));

        // WR-A03: the results are cached on disk, so the uploaded prompts and the output file are no longer needed at the
        // provider (OpenAI keeps batch files until they are deleted). Kept when the cache could not be written.
        if (saved)
            await DeleteFilesBestEffortAsync(batch.InputFileId, status.OutputFileId);
    }

    private async Task DeleteFilesBestEffortAsync(string? inputFileId, string? outputFileId)
    {
        if (string.IsNullOrEmpty(inputFileId) && string.IsNullOrEmpty(outputFileId))
            return;

        try
        {
            await _transport.DeleteFilesAsync(NullIfEmpty(inputFileId), NullIfEmpty(outputFileId), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort: a file left at the provider is cleaned up by Start Over; it never affects the run
        }
    }

    private async Task FailAtProviderAsync(TrackedBatch batch, BatchStatusResult status)
    {
        // CX-05: a failed batch has no results to lose; an all-errored cache keeps a restart from downloading it again
        // (its items then run live, never in a new batch, D-10)
        try
        {
            _ledger.SaveResults(batch.BatchId, batch.ItemIds
                .Select(id => new BatchItemResult { CustomId = id, Outcome = BatchItemOutcome.Errored, Message = "The batch failed" })
                .ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a restart polls this batch once more, sees it failed and sends its items live: no extra spend
        }

        try
        {
            await _ledger.AppendAsync(new BatchLedgerRecord
            {
                Type = BatchLedgerRecordType.Collected,
                Utc = Now(),
                Provider = _transport.Kind,
                BatchId = batch.BatchId,
                PlanId = _planId,
                SubmittedUtc = batch.SubmittedUtc,
                StageLabel = batch.StageLabel,
                Total = batch.ItemIds.Count
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A restart polls this batch again, sees it failed and sends its items live: no extra spend
        }

        lock (_lock)
        {
            foreach (var id in batch.ItemIds)
                _failedIds.Add(id);
        }

        // D-10, Pitfall 6: a failed batch costs nothing; every item runs live
        ReleaseBatch(batch, Delivery.Errored());

        Raise(Event(BatchStatusKind.FailedAtProvider, batch, e =>
        {
            e.Total = batch.ItemIds.Count;
            e.Failed = batch.ItemIds.Count;
            e.Reason = ReasonFor(status);
            e.Duration = Now() - batch.SubmittedUtc;
        }));
    }

    private async Task DeliverAsync(TrackedBatch batch, BatchItemResult result)
    {
        var usable = IsUsable(result);
        List<TaskCompletionSource<Delivery>> waiters;
        lock (_lock)
        {
            waiters = new List<TaskCompletionSource<Delivery>>();
            if (_inflight.TryGetValue(result.CustomId, out var item) && item.BatchId == batch.BatchId)
            {
                _inflight.Remove(result.CustomId);
                waiters.AddRange(item.Waiters);
                item.Waiters.Clear();
            }

            if (!usable)
                _failedIds.Add(result.CustomId);
            else if (waiters.Count == 0)
                _available[result.CustomId] = new AvailableResult(batch.BatchId, _transport.Kind, result, batch.ModelFor(result.CustomId));
        }

        // Consumed before the waiter completes, so a later Continue (or Regenerate) never reuses this result (D-09).
        // Known gap (WR-A01): a unit cancelled or killed after this line but before its journal line loses this result
        if (usable && waiters.Count > 0)
            await AppendConsumedAsync(batch.BatchId, result.CustomId);

        CompleteResult(waiters, result);
    }

    private async Task AppendConsumedAsync(string batchId, string customId)
    {
        try
        {
            await _ledger.AppendAsync(new BatchLedgerRecord
            {
                Type = BatchLedgerRecordType.Consumed,
                Utc = Now(),
                Provider = _transport.Kind,
                BatchId = batchId,
                PlanId = _planId,
                Items = new List<BatchLedgerItem> { new() { CustomId = customId } }
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still delivered: the unit's journal line is the source of truth for finished work
        }
    }

    // ----- release helpers -----

    private void ReleaseItems(IEnumerable<PendingItem> items, Delivery delivery)
    {
        var waiters = new List<TaskCompletionSource<Delivery>>();
        lock (_lock)
        {
            foreach (var item in items)
            {
                if (_inflight.TryGetValue(item.CustomId, out var current) && ReferenceEquals(current, item))
                    _inflight.Remove(item.CustomId);

                waiters.AddRange(item.Waiters);
                item.Waiters.Clear();
            }
        }

        Complete(waiters, delivery);
    }

    // WR-A04: detached before the flush took the queue; anyone still queued under this label runs live
    private void ReleaseQueue((string Label, string ModelId) key)
    {
        List<PendingItem> queued;
        lock (_lock)
        {
            if (!_queues.Remove(key, out var queue))
                return;

            queued = queue.Items.ToList();
        }

        ReleaseItems(queued, Delivery.Errored());
    }

    private void ReleaseUnsubmitted(List<PendingItem> taken)
    {
        List<PendingItem> unsubmitted;
        lock (_lock)
            unsubmitted = taken.Where(i => i.BatchId == null).ToList();

        ReleaseItems(unsubmitted, Delivery.Errored());
    }

    // WR-A07: plain words for the Activity row when the app stops waiting on a batch (T-07-52: fixed text only)
    private const string StoppedWaitingReason = "the app stopped waiting on it";
    private const string ClosedReason = "it was closed because this run no longer uses it";

    /// <summary>
    /// Stops tracking a batch and releases its waiters. With a <paramref name="reason"/> it also raises a terminal
    /// FailedAtProvider event (WR-A07), so the Stages row and the pending count never stay on "Waiting on provider batch";
    /// a caller that raises its own terminal event passes null.
    /// </summary>
    private void ReleaseBatch(TrackedBatch batch, Delivery delivery, string? reason = null)
    {
        var waiters = new List<TaskCompletionSource<Delivery>>();
        bool wasTracked;
        lock (_lock)
        {
            wasTracked = UntrackLocked(batch);
            foreach (var id in batch.ItemIds)
            {
                if (!_inflight.TryGetValue(id, out var item) || item.BatchId != batch.BatchId)
                    continue;

                _inflight.Remove(id);
                waiters.AddRange(item.Waiters);
                item.Waiters.Clear();
            }
        }

        Complete(waiters, delivery);

        if (reason != null && wasTracked)
        {
            Raise(Event(BatchStatusKind.FailedAtProvider, batch, e =>
            {
                e.Total = batch.ItemIds.Count;
                e.Failed = batch.ItemIds.Count;
                e.Reason = reason;
                e.Duration = Now() - batch.SubmittedUtc;
            }));
        }
    }

    private void RemoveWaiter((string Label, string ModelId) key, string id, TaskCompletionSource<Delivery> waiter)
    {
        lock (_lock)
        {
            if (_inflight.TryGetValue(id, out var inflight) && inflight.Waiters.Remove(waiter))
                return;

            if (_queues.TryGetValue(key, out var queue) && queue.ById.TryGetValue(id, out var queued))
                queued.Waiters.Remove(waiter);
        }
    }

    private static void Complete(IEnumerable<TaskCompletionSource<Delivery>> waiters, Delivery delivery)
    {
        foreach (var waiter in waiters)
            waiter.TrySetResult(delivery);
    }

    private static void CompleteResult(List<TaskCompletionSource<Delivery>> waiters, BatchItemResult result)
    {
        // Identical requests share one billed item: only one caller reports its tokens. WR-A01: the first caller that
        // actually receives the result takes them, so a waiter cancelled meanwhile never drops the item's usage
        var claim = new UsageClaim();
        foreach (var waiter in waiters)
            waiter.TrySetResult(new Delivery { Result = result, Claim = claim });
    }

    // ----- small helpers -----

    private DateTime Now() => _timings.Clock();

    private TimeSpan IntervalFor(TimeSpan waited) =>
        waited >= _timings.SlowAfter ? _timings.SlowPollInterval : _timings.PollInterval;

    private bool IsClosed(TrackedBatch batch)
    {
        lock (_lock)
            return batch.Closed;
    }

    private static bool IsFatal(LlmFailureKind kind) => kind is LlmFailureKind.Auth or LlmFailureKind.SpendLimit;

    private static bool IsUsable(BatchItemResult result) =>
        result.Outcome == BatchItemOutcome.Succeeded && result.Response != null;

    private static string ReasonFor(BatchStatusResult status)
    {
        var message = (status.Message ?? string.Empty).ToLowerInvariant();
        if (message.Contains("expire"))
            return "the batch expired before it finished";
        if (message.Contains("size") || message.Contains("too large") || message.Contains("limit"))
            return "the batch is over the provider's size limit";
        if (message.Contains("timeout") || message.Contains("timed out") || status.FailureKind == LlmFailureKind.Network)
            return "the request timed out";

        return "the provider rejected the batch";
    }

    private BatchStatusEvent Event(BatchStatusKind kind, TrackedBatch batch, Action<BatchStatusEvent> fill)
    {
        var e = new BatchStatusEvent
        {
            Kind = kind,
            BatchId = batch.BatchId,
            StageLabel = batch.StageLabel,
            Provider = _transport.Kind,
            SubmittedUtc = batch.SubmittedUtc
        };
        fill(e);
        return e;
    }

    private void Raise(BatchStatusEvent e)
    {
        var handler = StatusChanged;
        if (handler == null)
            return;

        try
        {
            handler(e);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failing status subscriber (UI) must never stop polling or strand waiting units
        }
    }

    private static TaskCompletionSource<Delivery> NewWaiter() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static LlmProviderRequest Snapshot(LlmProviderRequest r) => new()
    {
        ModelId = r.ModelId,
        Effort = r.Effort,
        SchemaName = r.SchemaName,
        SchemaJson = r.SchemaJson,
        SystemPrompt = r.SystemPrompt,
        UserPrompt = r.UserPrompt,
        MaxTokens = r.MaxTokens,
        AllowBatch = r.AllowBatch
    };

    private static LlmProviderRequest Live(LlmProviderRequest r)
    {
        var copy = Snapshot(r);
        copy.AllowBatch = false;
        return copy;
    }

    private static LlmProviderResponse Copy(LlmProviderResponse r, bool isBatch, bool withUsage) => new()
    {
        Outcome = r.Outcome,
        FailureKind = r.FailureKind,
        Text = r.Text,
        Message = r.Message,
        InputTokens = withUsage ? r.InputTokens : 0,
        CacheWriteTokens = withUsage ? r.CacheWriteTokens : 0,
        CacheReadTokens = withUsage ? r.CacheReadTokens : 0,
        OutputTokens = withUsage ? r.OutputTokens : 0,
        IsBatch = isBatch
    };

    // ----- private state types -----

    private sealed class Delivery
    {
        public BatchItemResult? Result { get; init; }
        public LlmProviderResponse? Fatal { get; init; }

        // Shared by every waiter of one batch item; null when this delivery is the only one (it carries the usage)
        public UsageClaim? Claim { get; init; }

        /// <summary>True for exactly one caller of a shared item: the first that observes the result.</summary>
        public bool TryTakeUsage() => Claim == null || Interlocked.Exchange(ref Claim.Taken, 1) == 0;

        public static Delivery Errored() =>
            new() { Result = new BatchItemResult { Outcome = BatchItemOutcome.Errored } };

        public static Delivery FatalFailure(LlmFailureKind kind, string? message) => new()
        {
            Fatal = new LlmProviderResponse
            {
                Outcome = LlmOutcome.Failed,
                FailureKind = kind,
                Message = string.IsNullOrWhiteSpace(message) ? "Provider batch rejected: " + kind : message
            }
        };
    }

    private sealed class UsageClaim
    {
        public int Taken;
    }

    private sealed class PendingItem
    {
        public PendingItem(string customId, LlmProviderRequest request)
        {
            CustomId = customId;
            Request = request;
        }

        public string CustomId { get; }
        public LlmProviderRequest Request { get; }

        /// <summary>Null while queued or submitting; the provider batch id once submitted (or resumed).</summary>
        public string? BatchId { get; set; }

        public List<TaskCompletionSource<Delivery>> Waiters { get; } = new();
    }

    private sealed class StageQueue
    {
        public List<PendingItem> Items { get; } = new();
        public Dictionary<string, PendingItem> ById { get; } = new(StringComparer.Ordinal);
        public long LastEnqueueTimestamp { get; set; }
    }

    private sealed class AvailableResult
    {
        public AvailableResult(string batchId, LlmProviderKind provider, BatchItemResult result, string modelId)
        {
            BatchId = batchId;
            Provider = provider;
            Result = result;
            ModelId = modelId;
        }

        public string BatchId { get; }

        /// <summary>Provider that ran the batch; prices the result when it becomes an orphan (CR-A04).</summary>
        public LlmProviderKind Provider { get; }
        public BatchItemResult Result { get; }
        public string ModelId { get; }
    }

    private sealed class TrackedBatch
    {
        private readonly Dictionary<string, string> _modelById = new(StringComparer.Ordinal);

        public TrackedBatch(string batchId, string stageLabel, DateTime submittedUtc, bool resumed)
        {
            BatchId = batchId;
            StageLabel = stageLabel;
            SubmittedUtc = submittedUtc;
            Resumed = resumed;
        }

        public string BatchId { get; }
        public string StageLabel { get; }
        public DateTime SubmittedUtc { get; }
        public bool Resumed { get; }
        public bool Polling { get; set; }

        /// <summary>A resumed batch whose polling ResumePending started (a submitted batch polls from the start).</summary>
        public bool PollStarted { get; set; }

        /// <summary>Uploaded input file (OpenAI); deleted once the results are cached (WR-A03).</summary>
        public string InputFileId { get; init; } = string.Empty;

        /// <summary>Closed at run end (CancelPendingStagesAsync): never collected or delivered by this session.</summary>
        public bool Closed { get; set; }

        /// <summary>CX-06: model and estimated input tokens this batch holds in the provider's queue until it is untracked.</summary>
        public string QueuedModel { get; set; } = string.Empty;

        public long QueuedTokens { get; set; }

        public List<string> ItemIds { get; } = new();

        public void Add(string customId, string modelId)
        {
            if (string.IsNullOrEmpty(customId) || !_modelById.TryAdd(customId, modelId ?? string.Empty))
                return;

            ItemIds.Add(customId);
        }

        public static TrackedBatch From(BatchLedgerRecord record, bool resumed)
        {
            var batch = new TrackedBatch(record.BatchId, record.StageLabel ?? string.Empty, record.SubmittedUtc, resumed)
            {
                InputFileId = record.InputFileId ?? string.Empty
            };
            foreach (var item in record.Items.Where(i => i != null))
                batch.Add(item.CustomId, item.ModelId);

            return batch;
        }

        public bool Contains(string? customId) => customId != null && _modelById.ContainsKey(customId);

        public string ModelFor(string customId) => _modelById.TryGetValue(customId, out var model) ? model : string.Empty;
    }
}
