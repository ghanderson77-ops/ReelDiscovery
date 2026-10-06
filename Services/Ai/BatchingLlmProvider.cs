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
    private readonly Func<string, AIModelConfig?> _priceLookup;
    private readonly BatchTimings _timings;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    // All below guarded by _lock
    private readonly Dictionary<string, StageQueue> _queues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingItem> _inflight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AvailableResult> _available = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrackedBatch> _batches = new(StringComparer.Ordinal);

    public BatchingLlmProvider(ILlmProvider live, IBatchTransport transport, BatchLedger ledger, Guid planId,
        Func<string, AIModelConfig?> priceLookup, BatchTimings? timings = null)
    {
        _live = live ?? throw new ArgumentNullException(nameof(live));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _planId = planId;
        _priceLookup = priceLookup ?? throw new ArgumentNullException(nameof(priceLookup));
        _timings = timings ?? new BatchTimings();
    }

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

        if (!request.AllowBatch || BatchStage.IsLive || _cts.IsCancellationRequested)
            return await _live.SendAsync(request, ct);

        var label = BatchStage.Current;
        var id = RequestIdentity.CustomIdFor(_live.Kind, request);

        TaskCompletionSource<Delivery>? waiter = null;
        AvailableResult? ready = null;
        var goLive = false;
        var startFlush = false;
        lock (_lock)
        {
            if (_available.Remove(id, out var found))
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
                    if (!_queues.TryGetValue(label, out var queue))
                    {
                        queue = new StageQueue();
                        _queues[label] = queue;
                        startFlush = true;
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

        if (startFlush)
            _ = Task.Run(() => FlushLoopAsync(label));

        if (ready != null)
            return Copy(ready.Result.Response!, isBatch: true, withUsage: true);

        if (goLive)
            return await _live.SendAsync(Live(request), ct);

        Delivery delivery;
        try
        {
            delivery = await waiter!.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // D-09: the caller stops waiting; the provider batch keeps running and stays in the ledger
            RemoveWaiter(label, id, waiter!);
            throw;
        }

        if (delivery.Fatal != null)
            return delivery.Fatal;

        if (delivery.Result is { Outcome: BatchItemOutcome.Succeeded, Response: not null } result)
            return Copy(result.Response, isBatch: true, withUsage: delivery.CarriesUsage);

        // D-10: errored, expired or cancelled items run live at full price
        return await _live.SendAsync(Live(request), ct);
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

    // ----- queue and flush (D-07) -----

    private async Task FlushLoopAsync(string label)
    {
        List<PendingItem>? taken = null;
        try
        {
            var delay = _timings.QuietPeriod;
            while (true)
            {
                await Task.Delay(delay, _cts.Token);

                lock (_lock)
                {
                    if (!_queues.TryGetValue(label, out var queue))
                        return;

                    // Every caller of an item cancelled before the flush: nobody wants it, so never pay for it
                    foreach (var abandoned in queue.Items.Where(i => i.Waiters.Count == 0).ToList())
                    {
                        queue.Items.Remove(abandoned);
                        queue.ById.Remove(abandoned.CustomId);
                    }

                    if (queue.Items.Count == 0)
                    {
                        _queues.Remove(label);
                        return;
                    }

                    var quietFor = Stopwatch.GetElapsedTime(queue.LastEnqueueTimestamp);
                    if (quietFor < _timings.QuietPeriod && queue.Items.Count < Math.Max(1, _transport.MaxRequestsPerBatch))
                    {
                        delay = _timings.QuietPeriod - quietFor;
                        continue;
                    }

                    // New arrivals start a fresh queue and flush loop for this label
                    _queues.Remove(label);
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

    private static long EstimateBytes(LlmProviderRequest request) =>
        Encoding.UTF8.GetByteCount(request.SchemaJson ?? string.Empty)
        + Encoding.UTF8.GetByteCount(request.SystemPrompt ?? string.Empty)
        + Encoding.UTF8.GetByteCount(request.UserPrompt ?? string.Empty)
        + ItemOverheadBytes;

    // ----- submit -----

    private async Task SubmitAsync(string label, List<PendingItem> chunk)
    {
        var requests = chunk.Select(i => new BatchItemRequest { CustomId = i.CustomId, Request = i.Request }).ToList();
        var submit = await _transport.SubmitAsync(requests, _cts.Token);

        if (!submit.Succeeded)
        {
            // T-07-25: an auth or spend-limit failure stops the run through the gateway instead of thousands of live retries
            var delivery = IsFatal(submit.FailureKind)
                ? Delivery.FatalFailure(submit.FailureKind, submit.Message)
                : Delivery.Errored();
            ReleaseItems(chunk, delivery);
            return;
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
            Total = chunk.Count
        };

        try
        {
            await _ledger.AppendAsync(record, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // RUN-04: a batch we cannot track must not exist; cancel it (best effort) and run its items live
            await _transport.CancelAsync(submit.BatchId, CancellationToken.None);
            ReleaseItems(chunk, Delivery.Errored());
            return;
        }

        var batch = new TrackedBatch(submit.BatchId, label, submittedUtc, resumed: false);
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

        _ = Task.Run(() => PollLoopAsync(batch));
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

                var status = await _transport.GetStatusAsync(batch.BatchId, _cts.Token);
                var now = Now();

                if (!status.Succeeded)
                {
                    // Network blips keep polling; only repeated auth/spend-limit failures release the waiters
                    statusFailures = IsFatal(status.FailureKind) ? statusFailures + 1 : 0;
                    if (statusFailures >= MaxConsecutiveFailures)
                    {
                        ReleaseBatch(batch, Delivery.FatalFailure(status.FailureKind, status.Message));
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
                            : Delivery.Errored());
                        return;
                    }

                    continue;
                }

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
            ReleaseBatch(batch, Delivery.Errored());
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

        try
        {
            _ledger.SaveResults(batch.BatchId, ordered);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Results stay in memory for this session; only a restart before consumption loses the cached copy
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
                OutputFileId = status.OutputFileId ?? string.Empty,
                Done = status.Done,
                Total = ordered.Count
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A restart collects this batch again; Consumed lines keep it from being delivered twice
        }

        foreach (var result in ordered)
            await DeliverAsync(batch, result);

        lock (_lock)
            _batches.Remove(batch.BatchId);

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
    }

    private async Task FailAtProviderAsync(TrackedBatch batch, BatchStatusResult status)
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

    private Task DeliverAsync(TrackedBatch batch, BatchItemResult result)
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
                _available[result.CustomId] = new AvailableResult(batch.BatchId, result, batch.ModelFor(result.CustomId));
        }

        CompleteResult(waiters, result);
        return Task.CompletedTask;
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

    private void ReleaseUnsubmitted(List<PendingItem> taken)
    {
        List<PendingItem> unsubmitted;
        lock (_lock)
            unsubmitted = taken.Where(i => i.BatchId == null).ToList();

        ReleaseItems(unsubmitted, Delivery.Errored());
    }

    private void ReleaseBatch(TrackedBatch batch, Delivery delivery)
    {
        var waiters = new List<TaskCompletionSource<Delivery>>();
        lock (_lock)
        {
            _batches.Remove(batch.BatchId);
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
    }

    private void RemoveWaiter(string label, string id, TaskCompletionSource<Delivery> waiter)
    {
        lock (_lock)
        {
            if (_inflight.TryGetValue(id, out var inflight) && inflight.Waiters.Remove(waiter))
                return;

            if (_queues.TryGetValue(label, out var queue) && queue.ById.TryGetValue(id, out var queued))
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
        // Identical requests share one billed item: only the first caller reports its tokens
        for (var i = 0; i < waiters.Count; i++)
            waiters[i].TrySetResult(new Delivery { Result = result, CarriesUsage = i == 0 });
    }

    // ----- small helpers -----

    private DateTime Now() => _timings.Clock();

    private TimeSpan IntervalFor(TimeSpan waited) =>
        waited >= _timings.SlowAfter ? _timings.SlowPollInterval : _timings.PollInterval;

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
        public bool CarriesUsage { get; init; } = true;

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
        public AvailableResult(string batchId, BatchItemResult result, string modelId)
        {
            BatchId = batchId;
            Result = result;
            ModelId = modelId;
        }

        public string BatchId { get; }
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
        public List<string> ItemIds { get; } = new();

        public void Add(string customId, string modelId)
        {
            if (string.IsNullOrEmpty(customId) || !_modelById.TryAdd(customId, modelId ?? string.Empty))
                return;

            ItemIds.Add(customId);
        }

        public bool Contains(string? customId) => customId != null && _modelById.ContainsKey(customId);

        public string ModelFor(string customId) => _modelById.TryGetValue(customId, out var model) ? model : string.Empty;
    }
}
