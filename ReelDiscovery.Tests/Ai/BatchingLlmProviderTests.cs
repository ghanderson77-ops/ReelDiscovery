using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;
using Xunit;

namespace ReelDiscovery.Tests.Ai;

public class BatchingLlmProviderTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private readonly string _folder;
    private readonly Guid _plan = Guid.NewGuid();
    private readonly List<BatchingLlmProvider> _providers = new();

    public BatchingLlmProviderTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "rd-batching-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Detach();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder cleanup only; a locked file must not fail the test
        }
    }

    // ----- helpers -----

    private static BatchTimings Fast() => new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(50),
        PollInterval = TimeSpan.FromMilliseconds(20),
        SlowPollInterval = TimeSpan.FromMilliseconds(40),
        SlowAfter = TimeSpan.FromMilliseconds(200)
    };

    private static FakeLlmProvider Live()
    {
        var live = new FakeLlmProvider();
        live.Respond = r => FakeLlmProvider.Ok("live:" + r.UserPrompt);
        return live;
    }

    private static FakeBatchTransport Transport()
    {
        var transport = new FakeBatchTransport();
        transport.Respond = item => FakeLlmProvider.Ok("batch:" + item.Request.UserPrompt, 1000, 500);
        return transport;
    }

    private static LlmProviderRequest Req(string user, bool allowBatch = true) => new()
    {
        ModelId = "gpt-test",
        SchemaName = "Thread",
        SchemaJson = "{}",
        SystemPrompt = "system",
        UserPrompt = user,
        MaxTokens = 100,
        AllowBatch = allowBatch
    };

    private static string IdOf(LlmProviderRequest request) => RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, request);

    private BatchingLlmProvider Create(ILlmProvider live, IBatchTransport transport, BatchTimings? timings = null,
        Func<string, AIModelConfig?>? priceLookup = null)
    {
        var provider = new BatchingLlmProvider(live, transport, new BatchLedger(_folder), _plan,
            priceLookup ?? (_ => null), timings ?? Fast());
        _providers.Add(provider);
        return provider;
    }

    private sealed class EventLog
    {
        private readonly object _lock = new();
        private readonly List<BatchStatusEvent> _events = new();
        private readonly List<(Func<BatchStatusEvent, bool> Match, TaskCompletionSource<BatchStatusEvent> Tcs)> _watchers = new();

        public void Attach(BatchingLlmProvider provider) => provider.StatusChanged += Add;

        public void Add(BatchStatusEvent e)
        {
            List<TaskCompletionSource<BatchStatusEvent>> hit;
            lock (_lock)
            {
                _events.Add(e);
                hit = _watchers.Where(w => w.Match(e)).Select(w => w.Tcs).ToList();
                _watchers.RemoveAll(w => hit.Contains(w.Tcs));
            }

            foreach (var tcs in hit)
                tcs.TrySetResult(e);
        }

        public List<BatchStatusEvent> All
        {
            get
            {
                lock (_lock)
                    return _events.ToList();
            }
        }

        public Task<BatchStatusEvent> WaitFor(Func<BatchStatusEvent, bool> match)
        {
            var tcs = new TaskCompletionSource<BatchStatusEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                var existing = _events.FirstOrDefault(match);
                if (existing != null)
                    return Task.FromResult(existing);

                _watchers.Add((match, tcs));
            }

            return tcs.Task.WaitAsync(Wait);
        }

        public Task<BatchStatusEvent> WaitFor(BatchStatusKind kind) => WaitFor(e => e.Kind == kind);
    }

    private static List<Task<LlmProviderResponse>> SendAll(BatchingLlmProvider provider, string label, int count,
        string prefix = "r")
    {
        using (BatchStage.Enter(label))
            return Enumerable.Range(0, count).Select(i => provider.SendAsync(Req($"{prefix}{i}"))).ToList();
    }

    // ----- BatchStage -----

    [Fact]
    public void BatchStage_DefaultsAndRestores()
    {
        Assert.Equal("batch", BatchStage.Current);
        Assert.False(BatchStage.IsLive);

        using (BatchStage.Enter("wave 3"))
        {
            Assert.Equal("wave 3", BatchStage.Current);
            using (BatchStage.EnterLive())
                Assert.True(BatchStage.IsLive);

            Assert.False(BatchStage.IsLive);
        }

        Assert.Equal("batch", BatchStage.Current);
    }

    // ----- routing -----

    [Fact]
    public async Task AllowBatchFalse_GoesLive()
    {
        var live = Live();
        var transport = Transport();
        var provider = Create(live, transport);

        var response = await provider.SendAsync(Req("a", allowBatch: false)).WaitAsync(Wait);

        Assert.Equal("live:a", response.Text);
        Assert.Single(live.Requests);
        Assert.Empty(transport.Submissions);
    }

    [Fact]
    public async Task EnterLive_GoesLiveEvenWhenBatchAllowed()
    {
        var live = Live();
        var transport = Transport();
        var provider = Create(live, transport);

        LlmProviderResponse response;
        using (BatchStage.Enter("wave 1"))
        using (BatchStage.EnterLive())
            response = await provider.SendAsync(Req("repair")).WaitAsync(Wait);

        Assert.Equal("live:repair", response.Text);
        Assert.Single(live.Requests);
        Assert.Empty(transport.Submissions);
    }

    [Fact]
    public async Task TwelveRequestsInOneStage_OneSubmission()
    {
        var live = Live();
        var transport = Transport();
        var provider = Create(live, transport);

        var tasks = SendAll(provider, "wave 1", 12);
        var responses = await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.Single(transport.Submissions);
        Assert.Equal(12, transport.Submissions[0].Count);
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal($"batch:r{i}", responses[i].Text);
            Assert.True(responses[i].IsBatch);
        }

        Assert.Empty(live.Requests);
    }

    [Fact]
    public async Task DifferentStageLabels_NeverShareABatch()
    {
        var transport = Transport();
        var provider = Create(Live(), transport);

        var tasks = SendAll(provider, "wave 1", 3, "w").Concat(SendAll(provider, "non-relevant mail", 4, "n")).ToList();
        await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.Equal(2, transport.Submissions.Count);
        foreach (var submission in transport.Submissions)
        {
            var prefixes = submission.Select(i => i.Request.UserPrompt[0]).Distinct().ToList();
            Assert.Single(prefixes);
        }

        var labels = new BatchLedger(_folder).Replay().SubmittedById.Values.Select(r => r.StageLabel).OrderBy(l => l).ToList();
        Assert.Equal(new[] { "non-relevant mail", "wave 1" }, labels);
    }

    [Fact]
    public async Task CX01_SameStageDifferentModels_OneModelPerSubmission()
    {
        var transport = Transport();
        var provider = Create(Live(), transport);
        var events = new EventLog();
        events.Attach(provider);

        // Decoys (Story tier) and ordinary filler (Volume tier) share the non-relevant label
        List<Task<LlmProviderResponse>> tasks;
        using (BatchStage.Enter("non-relevant mail"))
        {
            tasks = new[] { "story-model", "volume-model", "story-model", "volume-model", "volume-model" }
                .Select((model, i) =>
                {
                    var request = Req($"r{i}");
                    request.ModelId = model;
                    return provider.SendAsync(request);
                })
                .ToList();
        }

        var responses = await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.Equal(2, transport.Submissions.Count);
        Assert.All(transport.Submissions, s => Assert.Single(s.Select(i => i.Request.ModelId).Distinct()));
        Assert.Equal(new[] { 2, 3 }, transport.Submissions.Select(s => s.Count).OrderBy(c => c).ToArray());
        Assert.All(responses, r => Assert.True(r.IsBatch));

        // The stage label (status display, run-end close) is unchanged
        var records = new BatchLedger(_folder).Replay().SubmittedById.Values.ToList();
        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal("non-relevant mail", r.StageLabel);
            Assert.Single(r.Items.Select(i => i.ModelId).Distinct());
        });
        Assert.All(events.All.Where(e => e.Kind == BatchStatusKind.Submitted), e => Assert.Equal("non-relevant mail", e.StageLabel));
    }

    [Fact]
    public async Task CX06_TokenCap_AppliesAcrossOutstandingBatches_NextChunkWaitsForCapacity()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        // Each item is ~130 estimated tokens ((prompts + 512 overhead) / 4): two fit under 300, so four requests are two chunks
        transport.MaxEstimatedInputTokens = 300;
        var provider = Create(Live(), transport);
        var events = new EventLog();
        events.Attach(provider);

        var tasks = SendAll(provider, "non-relevant mail", 4);
        await events.WaitFor(BatchStatusKind.Submitted);

        // The second chunk would push the queued tokens of this model over the limit: it waits while the first is polled
        await Until(() => transport.StatusCalls >= 5);
        Assert.Single(transport.Submissions);
        Assert.Equal(2, transport.Submissions[0].Count);

        // The first batch finishes and is collected: the second chunk goes
        transport.PollsUntilEnded = 0;
        var responses = await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.Equal(2, transport.Submissions.Count);
        Assert.All(responses, r => Assert.True(r.IsBatch));
        Assert.All(new BatchLedger(_folder).Replay().SubmittedById.Values,
            r => Assert.InRange(r.EstimatedInputTokens, 1, 300));
    }

    [Fact]
    public async Task CX06_ResumedPendingBatch_CountsAgainstCap_DetachRunsTheWaitingChunkLive()
    {
        // An earlier session left 250 estimated tokens of this model queued at the provider
        await new BatchLedger(_folder).AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.OpenAI,
            BatchId = "batch_queued",
            PlanId = _plan,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "non-relevant mail",
            Items = new List<BatchLedgerItem> { new() { CustomId = "q-other", Operation = "Thread", ModelId = "gpt-test" } },
            EstimatedInputTokens = 250
        });

        var live = Live();
        var transport = Transport();
        transport.MaxEstimatedInputTokens = 300;
        var provider = Create(live, transport);

        Task<LlmProviderResponse> waiting;
        using (BatchStage.Enter("non-relevant mail"))
            waiting = provider.SendAsync(Req("a"));

        // Several quiet periods: 250 + ~130 is over the limit, so nothing is submitted
        await Task.Delay(400);
        Assert.Empty(transport.Submissions);
        Assert.False(waiting.IsCompleted);

        // Stop: the chunk waiting for capacity runs live instead of hanging
        provider.Detach();
        Assert.Equal("live:a", (await waiting.WaitAsync(Wait)).Text);
        Assert.Empty(transport.Submissions);
        Assert.Single(live.Requests);
    }

    [Fact]
    public async Task RequestCap_SplitsIntoSeveralBatches()
    {
        var transport = Transport();
        transport.MaxRequestsPerBatch = 5;
        var provider = Create(Live(), transport);

        var responses = await Task.WhenAll(SendAll(provider, "wave 1", 12)).WaitAsync(Wait);

        Assert.Equal(new[] { 5, 5, 2 }, transport.Submissions.Select(s => s.Count).OrderByDescending(c => c).ToArray());
        Assert.All(responses, r => Assert.True(r.IsBatch));
    }

    [Fact]
    public async Task ByteCap_SplitsIntoSeveralBatches()
    {
        var transport = Transport();
        // Each item is ~520 bytes (prompts + 512 overhead): two fit, a third does not
        transport.MaxBatchBytes = 1100;
        var provider = Create(Live(), transport);

        await Task.WhenAll(SendAll(provider, "wave 1", 5)).WaitAsync(Wait);

        Assert.Equal(new[] { 2, 2, 1 }, transport.Submissions.Select(s => s.Count).OrderByDescending(c => c).ToArray());
    }

    [Fact]
    public async Task IdenticalRequests_OneItemBothCallersServed()
    {
        var live = Live();
        var transport = Transport();
        var provider = Create(live, transport);

        Task<LlmProviderResponse> a, b;
        using (BatchStage.Enter("wave 1"))
        {
            a = provider.SendAsync(Req("same"));
            b = provider.SendAsync(Req("same"));
        }

        var responses = await Task.WhenAll(a, b).WaitAsync(Wait);

        Assert.Single(transport.Submissions);
        Assert.Single(transport.Submissions[0]);
        Assert.All(responses, r => Assert.Equal("batch:same", r.Text));
        Assert.Empty(live.Requests);

        // WR-A01: one billed item, so exactly one caller reports its tokens
        Assert.Equal(1000, responses.Sum(r => r.InputTokens));
        Assert.Equal(500, responses.Sum(r => r.OutputTokens));
    }

    // ----- live fallback (D-10) -----

    [Fact]
    public async Task ErroredAndExpiredItems_RunLive()
    {
        var live = Live();
        var transport = Transport();
        transport.ErroredIds.Add(IdOf(Req("r0")));
        transport.ExpiredIds.Add(IdOf(Req("r1")));
        var provider = Create(live, transport);
        var events = new EventLog();
        events.Attach(provider);

        var responses = await Task.WhenAll(SendAll(provider, "wave 1", 4)).WaitAsync(Wait);

        Assert.Equal("live:r0", responses[0].Text);
        Assert.Equal("live:r1", responses[1].Text);
        Assert.Equal("batch:r2", responses[2].Text);
        Assert.Equal("batch:r3", responses[3].Text);
        Assert.Equal(2, live.Requests.Count);
        Assert.All(live.Requests, r => Assert.False(r.AllowBatch));

        var finished = await events.WaitFor(BatchStatusKind.FinishedWithFailures);
        Assert.Equal(2, finished.Succeeded);
        Assert.Equal(2, finished.Failed);
        Assert.Equal(4, finished.Total);
    }

    [Fact]
    public async Task WholeBatchFailed_AllItemsLive()
    {
        var live = Live();
        var transport = Transport();
        transport.FailWholeBatch = true;
        var provider = Create(live, transport);
        var events = new EventLog();
        events.Attach(provider);

        var responses = await Task.WhenAll(SendAll(provider, "wave 1", 3)).WaitAsync(Wait);

        Assert.All(responses, r => Assert.StartsWith("live:", r.Text));
        Assert.Equal(3, live.Requests.Count);
        var failed = await events.WaitFor(BatchStatusKind.FailedAtProvider);
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
        Assert.Contains(new BatchLedger(_folder).Replay().CollectedIds, id => id == failed.BatchId);
    }

    [Fact]
    public async Task SubmitAuthFailure_ReturnedAsFailure_NotLive()
    {
        var live = Live();
        var transport = Transport();
        transport.SubmitFailure = LlmFailureKind.Auth;
        var provider = Create(live, transport);

        var responses = await Task.WhenAll(SendAll(provider, "wave 1", 3)).WaitAsync(Wait);

        Assert.All(responses, r =>
        {
            Assert.Equal(LlmOutcome.Failed, r.Outcome);
            Assert.Equal(LlmFailureKind.Auth, r.FailureKind);
        });
        Assert.Empty(live.Requests);
    }

    [Fact]
    public async Task SubmitNetworkFailure_AllLive()
    {
        var live = Live();
        var transport = Transport();
        transport.SubmitFailure = LlmFailureKind.Network;
        var provider = Create(live, transport);

        var responses = await Task.WhenAll(SendAll(provider, "wave 1", 3)).WaitAsync(Wait);

        Assert.All(responses, r => Assert.StartsWith("live:", r.Text));
        Assert.Equal(3, live.Requests.Count);
    }

    // ----- ledger (RUN-04) -----

    [Fact]
    public async Task CleanBatch_LedgerHasSubmittedThenCollectedAndResultsFile()
    {
        var transport = Transport();
        var provider = Create(Live(), transport);

        await Task.WhenAll(SendAll(provider, "wave 1", 3)).WaitAsync(Wait);

        var ledger = new BatchLedger(_folder);
        var state = ledger.Replay();
        var submitted = Assert.Single(state.SubmittedById.Values);
        Assert.Equal("wave 1", submitted.StageLabel);
        Assert.Equal(_plan, submitted.PlanId);
        Assert.Equal(LlmProviderKind.OpenAI, submitted.Provider);
        Assert.Equal(3, submitted.Items.Count);
        Assert.All(submitted.Items, i =>
        {
            Assert.Equal("gpt-test", i.ModelId);
            Assert.Equal("Thread", i.Operation);
        });
        Assert.Contains(submitted.BatchId, state.CollectedIds);
        Assert.True(File.Exists(ledger.ResultsPathFor(submitted.BatchId)));

        var lines = File.ReadAllLines(ledger.FilePath);
        var submittedLine = Array.FindIndex(lines, l => l.Contains("\"Submitted\""));
        var collectedLine = Array.FindIndex(lines, l => l.Contains("\"Collected\""));
        Assert.True(submittedLine >= 0 && collectedLine > submittedLine);
    }

    [Fact]
    public async Task LedgerAppendFailure_CancelsBatchAndRunsLive()
    {
        var live = Live();
        var transport = Transport();
        var ledgerPath = BatchLedger.PathFor(_folder);
        File.WriteAllText(ledgerPath, string.Empty);
        var provider = Create(live, transport);

        // Exclusive handle: the Submitted append cannot open the file
        using (new FileStream(ledgerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var responses = await Task.WhenAll(SendAll(provider, "wave 1", 3)).WaitAsync(Wait);
            Assert.All(responses, r => Assert.StartsWith("live:", r.Text));
        }

        Assert.Single(transport.Submissions);
        Assert.Equal(new[] { "fb_1" }, transport.CancelledIds);
        Assert.Equal(3, live.Requests.Count);
    }

    // ----- cancellation (D-09) -----

    [Fact]
    public async Task CallerCancellation_StopsWaitingButNeverCancelsProviderBatch()
    {
        var live = Live();
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var provider = Create(live, transport);
        var events = new EventLog();
        events.Attach(provider);

        using var cts = new CancellationTokenSource();
        Task<LlmProviderResponse> task;
        using (BatchStage.Enter("wave 1"))
            task = provider.SendAsync(Req("a"), cts.Token);

        await events.WaitFor(BatchStatusKind.Submitted);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        Assert.Empty(transport.CancelledIds);
        Assert.Empty(live.Requests);
        Assert.Equal(1, provider.PendingBatchCount);
    }

    // ----- status events -----

    [Fact]
    public async Task StatusEvents_SubmittedProgressFinished()
    {
        var transport = Transport();
        transport.PollsUntilEnded = 2;
        var provider = Create(Live(), transport);
        var events = new EventLog();
        events.Attach(provider);

        await Task.WhenAll(SendAll(provider, "wave 1", 3)).WaitAsync(Wait);
        var finished = await events.WaitFor(BatchStatusKind.Finished);

        var all = events.All;
        Assert.Equal(BatchStatusKind.Submitted, all[0].Kind);
        Assert.Equal(BatchStatusKind.Finished, all[^1].Kind);
        var progress = all.Where(e => e.Kind == BatchStatusKind.Progress).ToList();
        Assert.Equal(2, progress.Count);
        Assert.All(progress, p =>
        {
            Assert.Equal(3, p.Total);
            Assert.Equal(0, p.Done);
            Assert.Equal("wave 1", p.StageLabel);
            Assert.True(p.NextCheckUtc > p.SubmittedUtc);
        });
        Assert.All(all, e => Assert.Equal(all[0].BatchId, e.BatchId));
        Assert.Equal(3, finished.Succeeded);
        Assert.Equal(0, finished.Failed);
        Assert.True(finished.Duration >= TimeSpan.Zero);
        Assert.Equal(LlmProviderKind.OpenAI, finished.Provider);
    }

    [Fact]
    public async Task StatusEvents_NextCheckUsesSlowIntervalAfterSlowAfter()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var start = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        long nowTicks = start.Ticks;
        var timings = Fast();
        timings.Clock = () => new DateTime(Interlocked.Read(ref nowTicks), DateTimeKind.Utc);
        var provider = Create(Live(), transport, timings);
        var events = new EventLog();
        events.Attach(provider);

        Task<LlmProviderResponse> task;
        using (BatchStage.Enter("wave 1"))
            task = provider.SendAsync(Req("a"));

        var early = await events.WaitFor(BatchStatusKind.Progress);
        Assert.Equal(start + TimeSpan.FromMilliseconds(20), early.NextCheckUtc);

        // Ten minutes of fake waiting later the poll interval is the slow one
        var later = start + TimeSpan.FromMinutes(10);
        Interlocked.Exchange(ref nowTicks, later.Ticks);
        var slow = await events.WaitFor(e => e.Kind == BatchStatusKind.Progress && e.NextCheckUtc > later);
        Assert.Equal(later + TimeSpan.FromMilliseconds(40), slow.NextCheckUtc);

        transport.PollsUntilEnded = 0;
        var response = await task.WaitAsync(Wait);
        Assert.True(response.IsBatch);
    }

    // ----- Consumed bookkeeping, restart recovery, orphans, Start Over (Task 2) -----

    [Fact]
    public async Task DeliveredItems_ConsumedLineWrittenBeforeWaiterCompletes()
    {
        var transport = Transport();
        var provider = Create(Live(), transport);

        var tasks = SendAll(provider, "wave 1", 3);
        var first = await tasks[0].WaitAsync(Wait);
        Assert.True(first.IsBatch);

        // Consumed is appended before the waiter completes, so it is already on disk. Read with FileShare.ReadWrite:
        // the other items' Consumed appends may hold the file open for writing right now
        string text;
        using (var stream = new FileStream(BatchLedger.PathFor(_folder), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
            text = reader.ReadToEnd();

        Assert.Contains(text.Split('\n'), l => l.Contains("\"Consumed\"") && l.Contains(IdOf(Req("r0"))));

        await Task.WhenAll(tasks).WaitAsync(Wait);
        var lines = File.ReadAllLines(BatchLedger.PathFor(_folder));
        Assert.Equal(3, lines.Count(l => l.Contains("\"Consumed\"")));
        Assert.Equal(new[] { "r0", "r1", "r2" }.Select(u => IdOf(Req(u))).OrderBy(x => x, StringComparer.Ordinal),
            new BatchLedger(_folder).Replay().ConsumedCustomIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Restart_PendingBatch_AttachesAndNeverResubmits()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var a = Create(Live(), transport);
        var aEvents = new EventLog();
        aEvents.Attach(a);

        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> task;
            using (BatchStage.Enter("wave 1"))
                task = a.SendAsync(Req("a"), cts.Token);

            await aEvents.WaitFor(BatchStatusKind.Submitted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        }

        a.Detach();
        transport.PollsUntilEnded = 0;

        var live = Live();
        var b = Create(live, transport);
        var bEvents = new EventLog();
        bEvents.Attach(b);
        Assert.Equal(1, b.PendingBatchCount);

        // Sent before resuming: attaches to the seeded in-flight item
        Task<LlmProviderResponse> again;
        using (BatchStage.Enter("wave 1 (continued)"))
            again = b.SendAsync(Req("a"));

        Assert.Equal(1, b.ResumePending());
        var response = await again.WaitAsync(Wait);

        Assert.Equal("batch:a", response.Text);
        Assert.True(response.IsBatch);
        Assert.Single(transport.Submissions);
        Assert.Empty(live.Requests);
        await bEvents.WaitFor(BatchStatusKind.CollectedAfterRestart);
        Assert.Equal(0, b.PendingBatchCount);
    }

    [Fact]
    public async Task Restart_CollectedNotConsumed_ServedFromResultsFile()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var a = Create(Live(), transport);
        var aEvents = new EventLog();
        aEvents.Attach(a);

        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> task;
            using (BatchStage.Enter("wave 1"))
                task = a.SendAsync(Req("a"), cts.Token);

            await aEvents.WaitFor(BatchStatusKind.Submitted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        }

        // A collects with nobody waiting: the result is cached, not consumed
        transport.PollsUntilEnded = 0;
        await aEvents.WaitFor(BatchStatusKind.Finished);
        a.Detach();
        Assert.Empty(new BatchLedger(_folder).Replay().ConsumedCustomIds);

        var statusCalls = transport.StatusCalls;
        var live = Live();
        var b = Create(live, transport);
        Assert.Equal(0, b.PendingBatchCount);
        Assert.Equal(0, b.ResumePending());

        LlmProviderResponse response;
        using (BatchStage.Enter("wave 1"))
            response = await b.SendAsync(Req("a")).WaitAsync(Wait);

        Assert.Equal("batch:a", response.Text);
        Assert.True(response.IsBatch);
        Assert.Single(transport.Submissions);
        Assert.Equal(statusCalls, transport.StatusCalls);
        Assert.Empty(live.Requests);
        Assert.Contains(IdOf(Req("a")), new BatchLedger(_folder).Replay().ConsumedCustomIds);
    }

    // Session A submits "a", its caller stops waiting, then the provider finishes and A collects with nobody waiting
    private async Task CollectUnwantedInFirstSessionAsync(FakeBatchTransport transport)
    {
        transport.PollsUntilEnded = int.MaxValue;
        var a = Create(Live(), transport);
        var aEvents = new EventLog();
        aEvents.Attach(a);

        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> task;
            using (BatchStage.Enter("non-relevant mail"))
                task = a.SendAsync(Req("a"), cts.Token);

            await aEvents.WaitFor(BatchStatusKind.Submitted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        }

        transport.PollsUntilEnded = 0;
        await aEvents.WaitFor(BatchStatusKind.Finished);
        a.Detach();
    }

    // Session B: the request attaches to the batch being downloaded again; nothing is resubmitted or run live
    private async Task AssertDownloadedAgainAsync(FakeBatchTransport transport, string batchId)
    {
        var live = Live();
        var b = Create(live, transport);
        Assert.Equal(1, b.PendingBatchCount);

        Task<LlmProviderResponse> again;
        using (BatchStage.Enter("non-relevant mail"))
            again = b.SendAsync(Req("a"));

        Assert.Equal(1, b.ResumePending());
        var response = await again.WaitAsync(Wait);

        Assert.Equal("batch:a", response.Text);
        Assert.True(response.IsBatch);
        Assert.Single(transport.Submissions);
        Assert.Empty(live.Requests);

        var ledger = new BatchLedger(_folder);
        await Until(() => ledger.Replay().CollectedIds.Contains(batchId));
        Assert.True(ledger.TryReadResults(batchId, out var cached));
        Assert.Single(cached);
    }

    [Fact]
    public async Task CX05_ResultsCacheNotWritten_NotRecordedCollected_RestartDownloadsAgain()
    {
        var transport = Transport();
        var ledger = new BatchLedger(_folder);

        // A directory where the cache file goes: SaveResults fails
        var blocker = ledger.ResultsPathFor("fb_1");
        Directory.CreateDirectory(blocker);
        await CollectUnwantedInFirstSessionAsync(transport);

        var state = ledger.Replay();
        Assert.DoesNotContain("fb_1", state.CollectedIds);
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, _plan));
        // The provider files stay, so the results can be downloaded again
        Assert.Empty(transport.DeletedFiles);

        Directory.Delete(blocker);
        await AssertDownloadedAgainAsync(transport, "fb_1");
    }

    [Fact]
    public async Task CX05_CollectedButCacheMissing_DownloadedAgainNotResubmitted()
    {
        var transport = Transport();
        await CollectUnwantedInFirstSessionAsync(transport);

        // An earlier build recorded Collected although the cache never reached the disk
        var ledger = new BatchLedger(_folder);
        Assert.Contains("fb_1", ledger.Replay().CollectedIds);
        File.Delete(ledger.ResultsPathFor("fb_1"));

        await AssertDownloadedAgainAsync(transport, "fb_1");
    }

    [Fact]
    public async Task CX05_FailedBatch_CachedAsErrored_RestartRunsLiveWithoutPolling()
    {
        var transport = Transport();
        transport.FailWholeBatch = true;
        var a = Create(Live(), transport);
        using (BatchStage.Enter("non-relevant mail"))
            Assert.StartsWith("live:", (await a.SendAsync(Req("a")).WaitAsync(Wait)).Text);
        a.Detach();

        var ledger = new BatchLedger(_folder);
        Assert.True(ledger.TryReadResults("fb_1", out var cached));
        Assert.Equal(BatchItemOutcome.Errored, Assert.Single(cached).Outcome);

        var statusCalls = transport.StatusCalls;
        var live = Live();
        var b = Create(live, transport);
        Assert.Equal(0, b.PendingBatchCount);
        using (BatchStage.Enter("non-relevant mail"))
            Assert.StartsWith("live:", (await b.SendAsync(Req("a")).WaitAsync(Wait)).Text);
        Assert.Equal(statusCalls, transport.StatusCalls);
        Assert.Single(transport.Submissions);
    }

    [Fact]
    public async Task ConsumedResult_NeverServedTwice_InSessionOrAfterRestart()
    {
        var transport = Transport();
        var a = Create(Live(), transport);

        using (BatchStage.Enter("wave 1"))
            await a.SendAsync(Req("a")).WaitAsync(Wait);
        Assert.Single(transport.Submissions);

        // Regenerate in the same session: a new submission
        using (BatchStage.Enter("wave 1"))
            await a.SendAsync(Req("a")).WaitAsync(Wait);
        Assert.Equal(2, transport.Submissions.Count);
        a.Detach();

        // Continue after a restart: the consumed result is not reused either
        var b = Create(Live(), transport);
        using (BatchStage.Enter("wave 1"))
        {
            var response = await b.SendAsync(Req("a")).WaitAsync(Wait);
            Assert.True(response.IsBatch);
        }

        Assert.Equal(3, transport.Submissions.Count);
    }

    [Fact]
    public async Task ResumePending_IgnoresBatchesOfAnotherProvider()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.Anthropic,
            BatchId = "msgbatch_1",
            PlanId = _plan,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "wave 1",
            Items = new List<BatchLedgerItem> { new() { CustomId = IdOf(Req("a")), Operation = "Thread", ModelId = "claude" } }
        });

        var transport = Transport();
        var provider = Create(Live(), transport);

        Assert.Equal(0, provider.PendingBatchCount);
        Assert.Equal(0, provider.ResumePending());
        Assert.Equal(0, transport.StatusCalls);

        // W5: Recover still ignores another provider's batch; the run start resolves it first (see ResolveOtherProvider_CancelsForeignBatch_BeforeCurrentProviderSubmits)
    }

    // ----- ResolveOtherProviderPendingAsync (D-13, D-14, D-22) -----

    private static FakeBatchTransport Anthropic()
    {
        var transport = new FakeBatchTransport { Kind = LlmProviderKind.Anthropic };
        transport.Respond = item => FakeLlmProvider.Ok("claude:" + item.Request.UserPrompt, 1000, 500);
        return transport;
    }

    // Submits a real batch on the fake (it only knows batches it issued) and records it in the ledger
    private async Task<BatchLedgerRecord> SeedForeignAsync(FakeBatchTransport transport, Guid plan, params string[] prompts)
    {
        var items = prompts.Select(p => new BatchItemRequest { CustomId = "c-" + p, Request = Req(p) }).ToList();
        var submitted = await transport.SubmitAsync(items);
        Assert.True(submitted.Succeeded);
        return await AppendSubmittedAsync(transport.Kind, submitted.BatchId, submitted.InputFileId, plan,
            items.Select(i => i.CustomId).ToArray());
    }

    private async Task<BatchLedgerRecord> AppendSubmittedAsync(LlmProviderKind kind, string batchId, string inputFileId,
        Guid plan, params string[] customIds)
    {
        var record = new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = kind,
            BatchId = batchId,
            PlanId = plan,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "wave 1",
            InputFileId = inputFileId,
            Items = customIds.Select(id => new BatchLedgerItem { CustomId = id, Operation = "Thread", ModelId = "claude-test" }).ToList()
        };
        await new BatchLedger(_folder).AppendAsync(record);
        return record;
    }

    private List<BatchLedgerRecord> LedgerLines() =>
        File.ReadAllLines(new BatchLedger(_folder).FilePath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => System.Text.Json.JsonSerializer.Deserialize<BatchLedgerRecord>(l, ReelDiscovery.Models.Case.CaseDraft.JsonOptions)!)
            .ToList();

    private Task<OtherProviderResolution> ResolveAsync(Func<LlmProviderKind, IBatchTransport?> transportFor) =>
        BatchingLlmProvider.ResolveOtherProviderPendingAsync(new BatchLedger(_folder), transportFor, _plan,
            LlmProviderKind.OpenAI).WaitAsync(Wait);

    [Fact]
    public async Task ResolveOtherProvider_None_NoCalls()
    {
        var openAi = Transport();
        var anthropic = Anthropic();
        await SeedForeignAsync(openAi, _plan, "own");

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : openAi);

        Assert.Equal(0, result.Found);
        Assert.False(result.Blocked);
        Assert.False(result.LedgerUnreadable);
        Assert.Equal(0, openAi.StatusCalls);
        Assert.Equal(0, anthropic.StatusCalls);
        Assert.Empty(openAi.CancelledIds);
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, _plan, LlmProviderKind.OpenAI));
    }

    [Fact]
    public async Task ResolveOtherProvider_KeyMissing_Blocked_TouchesNothing()
    {
        var anthropic = Anthropic();
        var openAi = Transport();
        var foreign = await SeedForeignAsync(anthropic, _plan, "a");
        var before = LedgerLines().Count;

        var result = await ResolveAsync(k => k == LlmProviderKind.OpenAI ? openAi : null);

        Assert.Equal(1, result.Found);
        Assert.True(result.KeyMissing);
        Assert.True(result.Blocked);
        Assert.False(result.Failed);
        Assert.Equal(LlmProviderKind.Anthropic, result.Provider);
        Assert.Equal(before, LedgerLines().Count);
        Assert.Equal(0, anthropic.StatusCalls);
        Assert.Equal(0, openAi.StatusCalls);
        Assert.Contains(new BatchLedger(_folder).Replay().Pending(_plan), r => r.BatchId == foreign.BatchId);
    }

    [Fact]
    public async Task ResolveOtherProvider_Ended_CollectedUnderOwnProvider_ThenBilledAsOrphan()
    {
        var anthropic = Anthropic();
        anthropic.PollsUntilEnded = 0;
        var foreign = await SeedForeignAsync(anthropic, _plan, "a", "b");

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.False(result.Blocked);
        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.Collected);
        Assert.Equal(0, result.Cancelled);
        Assert.Empty(anthropic.CancelledIds);

        var collected = Assert.Single(LedgerLines(), r => r.Type == BatchLedgerRecordType.Collected);
        Assert.Equal(LlmProviderKind.Anthropic, collected.Provider);
        Assert.Equal(foreign.BatchId, collected.BatchId);
        Assert.Equal(_plan, collected.PlanId);
        Assert.Equal(2, collected.Total);
        Assert.True(new BatchLedger(_folder).TryReadResults(foreign.BatchId, out var cached));
        Assert.Equal(2, cached.Count);
        Assert.Equal(0, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.OpenAI));
        Assert.Contains((foreign.InputFileId, "file_out_" + foreign.BatchId), anthropic.DeletedFiles.Select(f => (f.Item1!, f.Item2!)));

        // CR-A04: the OpenAI run's gateway sees the results as orphans and prices them with Anthropic's model
        var claude = PricedModel(LlmProviderKind.Anthropic, "claude-test", 3m, 15m);
        var lookups = new List<LlmProviderKind>();
        var provider = new BatchingLlmProvider(Live(), Transport(), new BatchLedger(_folder), _plan, (kind, id) =>
        {
            lookups.Add(kind);
            return kind == LlmProviderKind.Anthropic && id == "claude-test" ? claude : null;
        }, Fast());
        _providers.Add(provider);
        var bill = provider.PriceOrphans();
        Assert.Equal(new[] { foreign.BatchId }, bill.ItemsByBatch.Keys);
        Assert.Equal(2 * BatchCost(claude).TotalUsd, bill.Cost.TotalUsd);
        Assert.All(lookups, k => Assert.Equal(LlmProviderKind.Anthropic, k));
    }

    [Fact]
    public async Task ResolveOtherProvider_FailedBatch_ErroredCacheAndCollected()
    {
        var anthropic = Anthropic();
        anthropic.FailWholeBatch = true;
        var foreign = await SeedForeignAsync(anthropic, _plan, "a", "b");

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.False(result.Blocked);
        Assert.Equal(1, result.Collected);
        Assert.Equal(0, result.Cancelled);
        var collected = Assert.Single(LedgerLines(), r => r.Type == BatchLedgerRecordType.Collected);
        Assert.Equal(LlmProviderKind.Anthropic, collected.Provider);
        Assert.True(new BatchLedger(_folder).TryReadResults(foreign.BatchId, out var cached));
        Assert.Equal(2, cached.Count);
        Assert.All(cached, r => Assert.Equal(BatchItemOutcome.Errored, r.Outcome));
        Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, _plan));
    }

    [Fact]
    public async Task ResolveOtherProvider_Running_Cancelled()
    {
        var anthropic = Anthropic();
        var foreign = await SeedForeignAsync(anthropic, _plan, "a");

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.False(result.Blocked);
        Assert.Equal(1, result.Cancelled);
        Assert.Equal(0, result.Collected);
        Assert.Equal(new[] { foreign.BatchId }, anthropic.CancelledIds);
        var cancelled = Assert.Single(LedgerLines(), r => r.Type == BatchLedgerRecordType.Cancelled);
        Assert.Equal(foreign.BatchId, cancelled.BatchId);
        Assert.Equal(LlmProviderKind.Anthropic, cancelled.Provider);
        Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, _plan));
    }

    [Fact]
    public async Task ResolveOtherProvider_StatusFails_Blocked()
    {
        var anthropic = Anthropic();
        await AppendSubmittedAsync(LlmProviderKind.Anthropic, "msgbatch_never_issued", string.Empty, _plan, "c-x");
        var before = LedgerLines().Count;

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.True(result.Failed);
        Assert.True(result.Blocked);
        Assert.False(result.KeyMissing);
        Assert.Equal(LlmProviderKind.Anthropic, result.Provider);
        Assert.Equal(before, LedgerLines().Count);
        Assert.Empty(anthropic.CancelledIds);
        Assert.Equal(1, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.OpenAI));
    }

    [Fact]
    public async Task ResolveOtherProvider_CancelFails_Blocked_NoCancelledLine()
    {
        var anthropic = Anthropic();
        await SeedForeignAsync(anthropic, _plan, "a");
        anthropic.CancelFailure = new InvalidOperationException("provider down");

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.True(result.Failed);
        Assert.True(result.Blocked);
        Assert.Equal(LlmProviderKind.Anthropic, result.Provider);
        Assert.Equal(0, result.Cancelled);
        Assert.DoesNotContain(LedgerLines(), r => r.Type == BatchLedgerRecordType.Cancelled);
        Assert.Equal(1, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.OpenAI));
    }

    [Fact]
    public async Task ResolveOtherProvider_OtherPlans_Untouched()
    {
        var anthropic = Anthropic();
        anthropic.PollsUntilEnded = 0;
        var otherPlan = Guid.NewGuid();
        await SeedForeignAsync(anthropic, otherPlan, "a");
        var before = LedgerLines().Count;

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.Equal(0, result.Found);
        Assert.False(result.Blocked);
        Assert.Equal(0, anthropic.StatusCalls);
        Assert.Empty(anthropic.CancelledIds);
        Assert.Equal(before, LedgerLines().Count);
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, otherPlan));
    }

    [Fact]
    public async Task ResolveOtherProvider_UnreadableLedger_NoOp()
    {
        var anthropic = Anthropic();
        await SeedForeignAsync(anthropic, _plan, "a");

        OtherProviderResolution result;
        using (new FileStream(new BatchLedger(_folder).FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : Transport());

        Assert.True(result.LedgerUnreadable);
        Assert.False(result.Blocked);
        Assert.Equal(0, result.Found);
        Assert.Equal(0, anthropic.StatusCalls);
        Assert.Empty(anthropic.CancelledIds);
    }

    [Fact]
    public async Task ResolveOtherProvider_CancelsForeignBatch_BeforeCurrentProviderSubmits()
    {
        var anthropic = Anthropic();
        var foreign = await SeedForeignAsync(anthropic, _plan, "a");
        anthropic.NeverEndIds.Add("c-a");
        var openAi = Transport();

        var result = await ResolveAsync(k => k == LlmProviderKind.Anthropic ? anthropic : openAi);

        // Resolved before the run's gateway exists: the foreign batch is Cancelled and nothing was submitted yet
        Assert.False(result.Blocked);
        Assert.Equal(1, result.Cancelled);
        Assert.Contains(foreign.BatchId, new BatchLedger(_folder).Replay().CancelledIds);
        Assert.Empty(openAi.Submissions);

        var provider = Create(Live(), openAi);
        using (BatchStage.Enter("wave 1"))
            await provider.SendAsync(Req("a")).WaitAsync(Wait);
        Assert.Single(openAi.Submissions);

        // The ledger order shows the foreign batch closed before the current provider's single submission
        var lines = LedgerLines();
        var cancelledAt = lines.FindIndex(r => r.Type == BatchLedgerRecordType.Cancelled && r.BatchId == foreign.BatchId);
        var submittedAt = lines.FindIndex(r => r.Type == BatchLedgerRecordType.Submitted && r.Provider == LlmProviderKind.OpenAI);
        Assert.True(cancelledAt >= 0 && submittedAt > cancelledAt);
        Assert.Equal(0, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.OpenAI));
    }

    [Fact]
    public async Task ResumePending_IgnoresOtherPlans()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.OpenAI,
            BatchId = "batch_other",
            PlanId = Guid.NewGuid(),
            SubmittedUtc = DateTime.UtcNow,
            Items = new List<BatchLedgerItem> { new() { CustomId = IdOf(Req("a")), Operation = "Thread", ModelId = "gpt-test" } }
        });

        var provider = Create(Live(), Transport());

        Assert.Equal(0, provider.PendingBatchCount);
        Assert.Equal(0, provider.ResumePending());
    }

    [Fact]
    public async Task BillOrphans_PricesUnconsumedItemsOnce()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var model = new AIModelConfig
        {
            ModelId = "gpt-test",
            InputTokenPricePerMillion = 2m,
            OutputTokenPricePerMillion = 8m,
            BatchDiscountPercent = 50m
        };
        var provider = Create(Live(), transport, priceLookup: id => id == "gpt-test" ? model : null);
        var events = new EventLog();
        events.Attach(provider);

        using var cts = new CancellationTokenSource();
        Task<LlmProviderResponse> kept, abandoned;
        using (BatchStage.Enter("wave 1"))
        {
            kept = provider.SendAsync(Req("kept"));
            abandoned = provider.SendAsync(Req("abandoned"), cts.Token);
        }

        await events.WaitFor(BatchStatusKind.Submitted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(Wait));
        transport.PollsUntilEnded = 0;
        await kept.WaitAsync(Wait);
        var finished = await events.WaitFor(BatchStatusKind.Finished);

        var (cost, input, output) = await provider.BillOrphansAsync().WaitAsync(Wait);

        var expected = model.CalculateCost(new TokenUsage { InputTokens = 1000, OutputTokens = 500, IsBatch = true });
        Assert.Equal(expected.TotalUsd, cost.TotalUsd);
        Assert.True(cost.BatchSavingsUsd > 0);
        Assert.Equal(1000, input);
        Assert.Equal(500, output);
        Assert.Contains(finished.BatchId, new BatchLedger(_folder).Replay().OrphansBilledIds);

        var (again, againIn, againOut) = await provider.BillOrphansAsync().WaitAsync(Wait);
        Assert.Equal(0m, again.TotalUsd);
        Assert.Equal(0, againIn);
        Assert.Equal(0, againOut);

        // A restart does not bill (or serve) the billed orphan again
        provider.Detach();
        var b = Create(Live(), transport, priceLookup: _ => model);
        var (afterRestart, _, _) = await b.BillOrphansAsync().WaitAsync(Wait);
        Assert.Equal(0m, afterRestart.TotalUsd);
    }

    [Fact]
    public async Task CancelPending_CancelsPendingBatchesAndDeletesFiles()
    {
        var ledger = new BatchLedger(_folder);
        BatchLedgerRecord Submitted(string id, LlmProviderKind kind, string inputFile, Guid plan) => new()
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = kind,
            BatchId = id,
            PlanId = plan,
            SubmittedUtc = DateTime.UtcNow,
            InputFileId = inputFile,
            Items = new List<BatchLedgerItem> { new() { CustomId = "q" + id, Operation = "Thread", ModelId = "m" } }
        };

        await ledger.AppendAsync(Submitted("batch_open", LlmProviderKind.OpenAI, "file_in_1", _plan));
        await ledger.AppendAsync(Submitted("batch_done", LlmProviderKind.OpenAI, "file_in_2", _plan));
        await ledger.AppendAsync(new BatchLedgerRecord { Type = BatchLedgerRecordType.Collected, Utc = DateTime.UtcNow, BatchId = "batch_done" });
        await ledger.AppendAsync(Submitted("msgbatch_open", LlmProviderKind.Anthropic, string.Empty, _plan));
        await ledger.AppendAsync(Submitted("batch_other_plan", LlmProviderKind.OpenAI, "file_in_3", Guid.NewGuid()));

        var openAi = new FakeBatchTransport();
        await BatchingLlmProvider.CancelPendingAsync(ledger,
            kind => kind == LlmProviderKind.OpenAI ? openAi : null, _plan).WaitAsync(Wait);

        Assert.Equal(new[] { "batch_open" }, openAi.CancelledIds);
        var state = new BatchLedger(_folder).Replay();
        Assert.Contains("batch_open", state.CancelledIds);
        Assert.DoesNotContain("msgbatch_open", state.CancelledIds);
        Assert.DoesNotContain("batch_other_plan", state.CancelledIds);
        Assert.Equal(new[] { "file_in_1", "file_in_2" }, openAi.DeletedFiles.Select(f => f.Item1).OrderBy(f => f, StringComparer.Ordinal));
    }

    // ----- amended D-07: live scopes, live gate, closing stale stages -----

    /// <summary>Live provider that holds every call for a while and records the peak number of concurrent calls.</summary>
    private sealed class SlowLive : ILlmProvider
    {
        private int _inFlight;
        private int _max;
        private int _calls;

        public LlmProviderKind Kind => LlmProviderKind.OpenAI;
        public int? MaxTokensCeiling => null;
        public int MaxConcurrency => Volatile.Read(ref _max);
        public int Calls => Volatile.Read(ref _calls);

        public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(20, ct);
                return FakeLlmProvider.Ok("live:" + request.UserPrompt);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) =>
            Task.FromResult(new LlmKeyTestResult { Succeeded = true });
    }

    [Fact]
    public async Task EnterLive_UsesAlreadyCollectedResult_NoLiveCall_NoSubmission()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var a = Create(Live(), transport);
        var aEvents = new EventLog();
        aEvents.Attach(a);

        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> task;
            using (BatchStage.Enter("wave 1"))
                task = a.SendAsync(Req("r0"), cts.Token);

            await aEvents.WaitFor(BatchStatusKind.Submitted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        }

        a.Detach();
        transport.PollsUntilEnded = 0;

        var live = Live();
        var b = Create(live, transport, priceLookup: _ => new AIModelConfig
        {
            ModelId = "gpt-test", InputTokenPricePerMillion = 2m, OutputTokenPricePerMillion = 8m, BatchDiscountPercent = 50m
        });
        var bEvents = new EventLog();
        bEvents.Attach(b);
        Assert.Equal(1, b.ResumePending());
        await bEvents.WaitFor(BatchStatusKind.CollectedAfterRestart);

        LlmProviderResponse response;
        using (BatchStage.Enter("wave 1"))
        using (BatchStage.EnterLive())
            response = await b.SendAsync(Req("r0")).WaitAsync(Wait);

        Assert.Equal("batch:r0", response.Text);
        Assert.True(response.IsBatch);
        Assert.Empty(live.Requests);
        Assert.Single(transport.Submissions);
        Assert.Contains(IdOf(Req("r0")), new BatchLedger(_folder).Replay().ConsumedCustomIds);

        var (orphans, input, _) = await b.BillOrphansAsync().WaitAsync(Wait);
        Assert.Equal(0m, orphans.TotalUsd);
        Assert.Equal(0, input);
    }

    [Fact]
    public async Task EnterLive_InFlightItem_GoesLiveWithoutWaiting()
    {
        var live = Live();
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var provider = Create(live, transport);
        var events = new EventLog();
        events.Attach(provider);

        using var cts = new CancellationTokenSource();
        Task<LlmProviderResponse> waiting;
        using (BatchStage.Enter("wave 1"))
            waiting = provider.SendAsync(Req("r0"), cts.Token);
        await events.WaitFor(BatchStatusKind.Submitted);

        LlmProviderResponse response;
        using (BatchStage.Enter("wave 1"))
        using (BatchStage.EnterLive())
            response = await provider.SendAsync(Req("r0")).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("live:r0", response.Text);
        Assert.Single(transport.Submissions);
        Assert.Single(live.Requests);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Wait));
    }

    [Fact]
    public async Task EnterLive_AllowBatchFalse_NeverConsumesCollectedResult()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var a = Create(Live(), transport);
        var events = new EventLog();
        events.Attach(a);

        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> task;
            using (BatchStage.Enter("wave 1"))
                task = a.SendAsync(Req("r0"), cts.Token);

            await events.WaitFor(BatchStatusKind.Submitted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        }

        transport.PollsUntilEnded = 0;
        await events.WaitFor(BatchStatusKind.Finished);
        a.Detach();

        var live = Live();
        var b = Create(live, transport);
        LlmProviderResponse response;
        using (BatchStage.EnterLive())
            response = await b.SendAsync(Req("r0", allowBatch: false)).WaitAsync(Wait);

        Assert.Equal("live:r0", response.Text);
        Assert.Single(live.Requests);
        Assert.Empty(new BatchLedger(_folder).Replay().ConsumedCustomIds);
    }

    [Fact]
    public async Task LiveGate_LimitsConcurrentLiveSends()
    {
        var gated = new SlowLive();
        var provider = Create(gated, Transport());
        using var gate = new SemaphoreSlim(2, 2);

        List<Task<LlmProviderResponse>> tasks;
        using (BatchStage.UseLiveGate(gate))
            tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() => provider.SendAsync(Req("g" + i, allowBatch: false)))).ToList();
        var responses = await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.All(responses, r => Assert.StartsWith("live:", r.Text));
        Assert.Equal(2, gated.MaxConcurrency);

        var ungated = new SlowLive();
        var free = Create(ungated, Transport());
        var freeTasks = Enumerable.Range(0, 10).Select(i => Task.Run(() => free.SendAsync(Req("u" + i, allowBatch: false)))).ToList();
        await Task.WhenAll(freeTasks).WaitAsync(Wait);
        Assert.True(ungated.MaxConcurrency > 2, $"ungated max concurrency {ungated.MaxConcurrency}");
    }

    [Fact]
    public async Task LiveGate_AppliesToD10Fallback()
    {
        var gated = new SlowLive();
        var transport = Transport();
        transport.FailWholeBatch = true;
        var provider = Create(gated, transport);
        using var gate = new SemaphoreSlim(2, 2);

        List<Task<LlmProviderResponse>> tasks;
        using (BatchStage.Enter("non-relevant mail"))
        using (BatchStage.UseLiveGate(gate))
            tasks = Enumerable.Range(0, 8).Select(i => provider.SendAsync(Req("f" + i))).ToList();
        var responses = await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.All(responses, r => Assert.StartsWith("live:", r.Text));
        Assert.Equal(8, gated.Calls);
        Assert.InRange(gated.MaxConcurrency, 1, 2);
        Assert.Single(transport.Submissions);
    }

    // ----- D-17/WR-A08: stale wave batches are cancelled but still collected (no Cancelled line, D-21) -----

    // Session A submits each item under its label, its callers stop waiting and it detaches; returns the batch id per item
    private async Task<List<string>> SeedAbandonedBatchesAsync(FakeBatchTransport transport, params (string Label, string Prompt)[] items)
    {
        var a = Create(Live(), transport);
        var events = new EventLog();
        events.Attach(a);

        using (var cts = new CancellationTokenSource())
        {
            var tasks = new List<Task<LlmProviderResponse>>();
            foreach (var (label, prompt) in items)
            {
                using (BatchStage.Enter(label))
                    tasks.Add(a.SendAsync(Req(prompt), cts.Token));
            }

            await Until(() => events.All.Count(e => e.Kind == BatchStatusKind.Submitted) == items.Length);
            cts.Cancel();
            foreach (var task in tasks)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Wait));
        }

        a.Detach();
        var submitted = events.All.Where(e => e.Kind == BatchStatusKind.Submitted).ToList();
        return items.Select(i => submitted.Single(e => e.StageLabel == i.Label).BatchId).ToList();
    }

    private static bool IsWave(string label) => label.StartsWith("wave ", StringComparison.Ordinal);

    [Fact]
    public async Task DrainStaleStages_CancelsCollectsAndServesFree_NoCancelledLine()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var batchId = (await SeedAbandonedBatchesAsync(transport, ("wave 1", "w0")))[0];

        var live = Live();
        var b = Create(live, transport);
        Assert.Equal(1, b.ResumePending());

        var drained = await b.DrainStaleStagesAsync(IsWave).WaitAsync(Wait);

        Assert.Equal(1, drained);
        Assert.Contains(batchId, transport.CancelledIds);
        var state = new BatchLedger(_folder).Replay();
        Assert.DoesNotContain(batchId, state.CancelledIds);
        Assert.Contains(batchId, state.CollectedIds);
        Assert.Equal(0, b.PendingBatchCount);
        Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, _plan));

        // A live scope uses the drained result free
        LlmProviderResponse response;
        using (BatchStage.Enter("wave 1"))
        using (BatchStage.EnterLive())
            response = await b.SendAsync(Req("w0")).WaitAsync(Wait);

        Assert.Equal("batch:w0", response.Text);
        Assert.True(response.IsBatch);
        Assert.Empty(live.Requests);
    }

    [Fact]
    public async Task DrainStaleStages_IgnoresOtherLabels()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var ids = await SeedAbandonedBatchesAsync(transport, ("wave 1", "w0"), ("non-relevant mail", "n0"));

        var b = Create(Live(), transport);
        Assert.Equal(2, b.ResumePending());

        Assert.Equal(1, await b.DrainStaleStagesAsync(IsWave).WaitAsync(Wait));

        Assert.Equal(new[] { ids[0] }, transport.CancelledIds);
        var state = new BatchLedger(_folder).Replay();
        Assert.Empty(state.CancelledIds);
        Assert.Equal(new[] { ids[1] }, state.Pending(_plan).Select(r => r.BatchId));
        Assert.Equal(1, b.PendingBatchCount);
    }

    [Fact]
    public async Task DrainStaleStages_CancelFails_BoundExpires_StillPending()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        transport.NeverEndIds.Add(IdOf(Req("w0")));
        var batchId = (await SeedAbandonedBatchesAsync(transport, ("wave 2", "w0")))[0];

        transport.CancelFailure = new InvalidOperationException("provider down");
        var timings = Fast();
        timings.DrainTimeout = TimeSpan.FromMilliseconds(200);
        var b = Create(Live(), transport, timings);
        Assert.Equal(1, b.ResumePending());

        var drained = await b.DrainStaleStagesAsync(IsWave).WaitAsync(Wait);

        Assert.Equal(1, drained);
        Assert.Empty(transport.CancelledIds);
        var state = new BatchLedger(_folder).Replay();
        Assert.DoesNotContain(batchId, state.CancelledIds);
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, _plan));
        Assert.Equal(1, b.PendingBatchCount);
    }

    [Fact]
    public async Task DrainStaleStages_UserCancel_Throws()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        transport.NeverEndIds.Add(IdOf(Req("w0")));
        await SeedAbandonedBatchesAsync(transport, ("wave 1", "w0"));

        transport.CancelFailure = new InvalidOperationException("provider down");
        var b = Create(Live(), transport);
        Assert.Equal(1, b.ResumePending());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.DrainStaleStagesAsync(IsWave, cts.Token).WaitAsync(Wait));
        Assert.Equal(1, b.PendingBatchCount);
    }

    [Fact]
    public async Task DrainStaleStages_NotPolling_NotAwaited()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        await SeedAbandonedBatchesAsync(transport, ("wave 1", "w0"));

        // ResumePending is never called: the batch is tracked but nobody polls it, so the drain must not wait on it
        var b = Create(Live(), transport);
        Assert.Equal(1, b.PendingBatchCount);

        Assert.Equal(0, await b.DrainStaleStagesAsync(IsWave).WaitAsync(Wait));
        Assert.Empty(transport.CancelledIds);
        Assert.Equal(1, b.PendingBatchCount);
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, _plan));
    }

    [Fact]
    public async Task CancelPending_DefaultArguments_PropagateCancelFailure()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.OpenAI,
            BatchId = "batch_open",
            PlanId = _plan,
            SubmittedUtc = DateTime.UtcNow,
            Items = new List<BatchLedgerItem> { new() { CustomId = "q1", Operation = "Thread", ModelId = "m" } }
        });

        var transport = new FakeBatchTransport { CancelFailure = new InvalidOperationException("provider down") };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BatchingLlmProvider.CancelPendingAsync(ledger, _ => transport, _plan).WaitAsync(Wait));
        Assert.DoesNotContain("batch_open", new BatchLedger(_folder).Replay().CancelledIds);
    }

    // ----- code review 07 (part A) -----

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in time");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task CRA01_DetachDuringSubmit_AcceptedBatchIsStillInTheLedger()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.SubmitStarted = started;
        transport.SubmitHold = hold.Task;
        var provider = Create(Live(), transport);

        Task<LlmProviderResponse> pending;
        using (BatchStage.Enter("non-relevant mail"))
            pending = provider.SendAsync(Req("a"));

        // Stop while the upload is in flight: the waiter runs live, the submit itself is not cancelled
        await started.Task.WaitAsync(Wait);
        provider.Detach();
        Assert.Equal("live:a", (await pending.WaitAsync(Wait)).Text);

        hold.SetResult();
        await Until(() => BatchLedger.PendingBatchCount(_folder, _plan) == 1);

        Assert.Single(transport.Submissions);
        var record = Assert.Single(new BatchLedger(_folder).Replay().Pending(_plan));
        Assert.Equal(IdOf(Req("a")), Assert.Single(record.Items).CustomId);
    }

    [Fact]
    public async Task CX07_DetachAsync_WaitsForTheInFlightSubmitAndItsLedgerLine()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.SubmitStarted = started;
        transport.SubmitHold = hold.Task;
        var provider = Create(Live(), transport);

        Task<LlmProviderResponse> pending;
        using (BatchStage.Enter("non-relevant mail"))
            pending = provider.SendAsync(Req("a"));

        await started.Task.WaitAsync(Wait);
        var detach = provider.DetachAsync();
        Assert.Equal("live:a", (await pending.WaitAsync(Wait)).Text);

        // The upload is still in flight: the run may not report stopped yet
        await Task.Delay(200);
        Assert.False(detach.IsCompleted);

        hold.SetResult();
        await detach.WaitAsync(Wait);

        // No polling: the accepted batch is already in the ledger when DetachAsync returns
        var record = Assert.Single(new BatchLedger(_folder).Replay().Pending(_plan));
        Assert.Equal(IdOf(Req("a")), Assert.Single(record.Items).CustomId);
    }

    [Fact]
    public async Task CX07_DetachAsync_IsBounded_NeverWaitsOnRemoteExecution()
    {
        var transport = Transport();
        transport.PollsUntilEnded = int.MaxValue;
        var timings = Fast();
        timings.DrainTimeout = TimeSpan.FromMilliseconds(200);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Create(Live(), transport, timings);
        var events = new EventLog();
        events.Attach(provider);

        // One batch submitted and still running at the provider: never waited on
        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> running;
            using (BatchStage.Enter("non-relevant mail"))
                running = provider.SendAsync(Req("running"), cts.Token);
            await events.WaitFor(BatchStatusKind.Submitted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Wait));
        }

        // A second upload that hangs past the bound
        transport.SubmitStarted = started;
        transport.SubmitHold = hold.Task;
        Task<LlmProviderResponse> stuck;
        using (BatchStage.Enter("non-relevant mail"))
            stuck = provider.SendAsync(Req("stuck"));
        await started.Task.WaitAsync(Wait);

        await provider.DetachAsync().WaitAsync(Wait);
        Assert.Equal("live:stuck", (await stuck.WaitAsync(Wait)).Text);

        // The late submit still records its batch when it returns
        hold.SetResult();
        await Until(() => BatchLedger.PendingBatchCount(_folder, _plan) == 2);
    }

    // "Regenerate thread" re-sends the same request (same custom id) after an older batch's result was consumed
    private async Task<string> RegeneratedLedgerAsync(bool collectSecond)
    {
        var ledger = new BatchLedger(_folder);
        var id = IdOf(Req("a"));
        BatchLedgerRecord Submitted(string batchId) => new()
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.OpenAI,
            BatchId = batchId,
            PlanId = _plan,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "non-relevant mail",
            Items = new List<BatchLedgerItem> { new() { CustomId = id, Operation = "Thread", ModelId = "gpt-test" } }
        };

        await ledger.AppendAsync(Submitted("batch_old"));
        await ledger.AppendAsync(new BatchLedgerRecord { Type = BatchLedgerRecordType.Collected, Utc = DateTime.UtcNow, BatchId = "batch_old", PlanId = _plan });
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Consumed,
            Utc = DateTime.UtcNow,
            BatchId = "batch_old",
            PlanId = _plan,
            Items = new List<BatchLedgerItem> { new() { CustomId = id } }
        });
        await ledger.AppendAsync(Submitted("batch_new"));
        if (collectSecond)
        {
            ledger.SaveResults("batch_new", new[]
            {
                new BatchItemResult { CustomId = id, Outcome = BatchItemOutcome.Succeeded, Response = FakeLlmProvider.Ok("batch:new", 1000, 500) }
            });
            await ledger.AppendAsync(new BatchLedgerRecord { Type = BatchLedgerRecordType.Collected, Utc = DateTime.UtcNow, BatchId = "batch_new", PlanId = _plan });
        }

        return id;
    }

    [Fact]
    public async Task CRA02_RegeneratedRequest_PendingNewerBatch_AttachesInsteadOfResubmitting()
    {
        await RegeneratedLedgerAsync(collectSecond: false);
        var transport = Transport();
        var live = Live();
        var provider = Create(live, transport);
        Assert.Equal(1, provider.PendingBatchCount);

        using var cts = new CancellationTokenSource();
        Task<LlmProviderResponse> waiting;
        using (BatchStage.Enter("non-relevant mail"))
            waiting = provider.SendAsync(Req("a"), cts.Token);

        // Several quiet periods: nothing is queued, because the request is already in batch_new
        await Task.Delay(400);
        Assert.Empty(transport.Submissions);
        Assert.Empty(live.Requests);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Wait));
    }

    [Fact]
    public async Task CRA02_RegeneratedRequest_CollectedNewerBatch_IsServedNotDropped()
    {
        await RegeneratedLedgerAsync(collectSecond: true);
        var transport = Transport();
        var live = Live();
        var provider = Create(live, transport);

        LlmProviderResponse response;
        using (BatchStage.Enter("non-relevant mail"))
            response = await provider.SendAsync(Req("a")).WaitAsync(Wait);

        Assert.Equal("batch:new", response.Text);
        Assert.Equal(1000, response.InputTokens);
        Assert.Empty(transport.Submissions);
        Assert.Empty(live.Requests);
        Assert.True(new BatchLedger(_folder).Replay().IsConsumed("batch_new", IdOf(Req("a"))));
    }

    [Fact]
    public async Task CRA03_PriceOrphans_HasNoSideEffects_UntilMarkedBilled()
    {
        // A collected result nobody consumed
        await RegeneratedLedgerAsync(collectSecond: true);
        var model = new AIModelConfig
        {
            ModelId = "gpt-test",
            InputTokenPricePerMillion = 2m,
            OutputTokenPricePerMillion = 8m,
            BatchDiscountPercent = 50m
        };
        var provider = Create(Live(), Transport(), priceLookup: _ => model);

        var bill = provider.PriceOrphans();
        var expected = model.CalculateCost(new TokenUsage { InputTokens = 1000, OutputTokens = 500, IsBatch = true });
        Assert.Equal(expected.TotalUsd, bill.Cost.TotalUsd);
        Assert.Equal(new[] { "batch_new" }, bill.ItemsByBatch.Keys);

        // Not marked yet: the ledger has no OrphansBilled line, and a restart (RunEnded never written) still prices it
        Assert.Empty(new BatchLedger(_folder).Replay().OrphansBilledIds);
        provider.Detach();
        var restarted = Create(Live(), Transport(), priceLookup: _ => model);
        var again = restarted.PriceOrphans();
        Assert.Equal(expected.TotalUsd, again.Cost.TotalUsd);

        // Marked after the journal commit: billed once, never again; WR-A09: the session ticker gets it too
        var tracker = new TokenUsageTracker();
        restarted.UsageTracker = tracker;
        Assert.Equal(0m, tracker.TotalCost);
        await restarted.MarkOrphansBilledAsync(again).WaitAsync(Wait);
        Assert.Equal(expected.TotalUsd, tracker.TotalCost);
        Assert.Equal(expected.BatchSavingsUsd, tracker.TotalBatchSavings);
        Assert.Contains("batch_new", new BatchLedger(_folder).Replay().OrphansBilledIds);
        Assert.True(restarted.PriceOrphans().IsEmpty);
        restarted.Detach();
        Assert.True(Create(Live(), Transport(), priceLookup: _ => model).PriceOrphans().IsEmpty);
    }

    private async Task CollectedOrphanAsync(string batchId, LlmProviderKind kind, string modelId)
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = kind,
            BatchId = batchId,
            PlanId = _plan,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "non-relevant mail",
            Items = new List<BatchLedgerItem> { new() { CustomId = "q-" + batchId, Operation = "Thread", ModelId = modelId } }
        });
        ledger.SaveResults(batchId, new[]
        {
            new BatchItemResult { CustomId = "q-" + batchId, Outcome = BatchItemOutcome.Succeeded, Response = FakeLlmProvider.Ok("{}", 1000, 500) }
        });
        await ledger.AppendAsync(new BatchLedgerRecord { Type = BatchLedgerRecordType.Collected, Utc = DateTime.UtcNow, Provider = kind, BatchId = batchId, PlanId = _plan });
    }

    [Fact]
    public async Task CRA04_OrphanOfAnotherProvider_PricedWithThatProvidersModel_UnpricedLeftUnbilled()
    {
        await CollectedOrphanAsync("msgbatch_claude", LlmProviderKind.Anthropic, "claude-test");
        await CollectedOrphanAsync("batch_unknown", LlmProviderKind.OpenAI, "edited-away");
        var claude = new AIModelConfig
        {
            Provider = LlmProviderKind.Anthropic,
            ModelId = "claude-test",
            InputTokenPricePerMillion = 3m,
            OutputTokenPricePerMillion = 15m,
            BatchDiscountPercent = 50m
        };

        // The current text provider is OpenAI; the lookup is by (provider, model) across the whole catalog
        var provider = new BatchingLlmProvider(Live(), Transport(), new BatchLedger(_folder), _plan,
            (kind, id) => kind == LlmProviderKind.Anthropic && id == "claude-test" ? claude : null, Fast());
        _providers.Add(provider);

        var bill = provider.PriceOrphans();
        var expected = claude.CalculateCost(new TokenUsage { InputTokens = 1000, OutputTokens = 500, IsBatch = true });
        Assert.True(expected.TotalUsd > 0);
        Assert.Equal(expected.TotalUsd, bill.Cost.TotalUsd);
        Assert.Equal(1000, bill.InputTokens);
        Assert.Equal(new[] { "msgbatch_claude" }, bill.ItemsByBatch.Keys);
        Assert.Equal(new[] { "batch_unknown" }, bill.UnpricedBatchIds);

        // Only the priced batch is marked billed; the unpriced one is never recorded at $0
        await provider.MarkOrphansBilledAsync(bill).WaitAsync(Wait);
        var state = new BatchLedger(_folder).Replay();
        Assert.Contains("msgbatch_claude", state.OrphansBilledIds);
        Assert.DoesNotContain("batch_unknown", state.OrphansBilledIds);
        Assert.Equal(new[] { "batch_unknown" }, provider.PriceOrphans().UnpricedBatchIds);
    }

    // ----- PriceUnusedResults (D-16, WR-A06) -----

    private static AIModelConfig PricedModel(LlmProviderKind kind, string modelId, decimal input, decimal output) => new()
    {
        Provider = kind,
        ModelId = modelId,
        InputTokenPricePerMillion = input,
        OutputTokenPricePerMillion = output,
        BatchDiscountPercent = 50m
    };

    private static CostBreakdown BatchCost(AIModelConfig model) =>
        model.CalculateCost(new TokenUsage { InputTokens = 1000, OutputTokens = 500, IsBatch = true });

    private async Task SeedCollectedAsync(string batchId, LlmProviderKind kind, Guid plan, string modelId,
        IEnumerable<(string Id, BatchItemOutcome Outcome)> items, bool cache = true)
    {
        var list = items.ToList();
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = kind,
            BatchId = batchId,
            PlanId = plan,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "non-relevant mail",
            Items = list.Select(i => new BatchLedgerItem { CustomId = i.Id, Operation = "Thread", ModelId = modelId }).ToList()
        });
        if (cache)
        {
            ledger.SaveResults(batchId, list.Select(i => i.Outcome == BatchItemOutcome.Succeeded
                ? new BatchItemResult { CustomId = i.Id, Outcome = BatchItemOutcome.Succeeded, Response = FakeLlmProvider.Ok("{}", 1000, 500) }
                : new BatchItemResult { CustomId = i.Id, Outcome = i.Outcome, Message = "Scripted" }).ToList());
        }

        await ledger.AppendAsync(new BatchLedgerRecord { Type = BatchLedgerRecordType.Collected, Utc = DateTime.UtcNow, Provider = kind, BatchId = batchId, PlanId = plan });
    }

    [Fact]
    public async Task PriceUnusedResults_AllPlans_SkipsConsumedErroredAndBilled()
    {
        var otherPlan = Guid.NewGuid();
        await SeedCollectedAsync("batch_a", LlmProviderKind.OpenAI, _plan, "gpt-test", new[]
        {
            ("q1", BatchItemOutcome.Succeeded), ("q2", BatchItemOutcome.Succeeded), ("q3", BatchItemOutcome.Errored)
        });
        await SeedCollectedAsync("msgbatch_b", LlmProviderKind.Anthropic, otherPlan, "claude-test", new[] { ("q4", BatchItemOutcome.Succeeded) });
        await SeedCollectedAsync("batch_billed", LlmProviderKind.OpenAI, _plan, "gpt-test", new[] { ("q5", BatchItemOutcome.Succeeded) });

        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Consumed, Utc = DateTime.UtcNow, Provider = LlmProviderKind.OpenAI, BatchId = "batch_a", PlanId = _plan,
            Items = new List<BatchLedgerItem> { new() { CustomId = "q2" } }
        });
        await ledger.AppendAsync(new BatchLedgerRecord { Type = BatchLedgerRecordType.OrphansBilled, Utc = DateTime.UtcNow, Provider = LlmProviderKind.OpenAI, BatchId = "batch_billed", PlanId = _plan });

        var gpt = PricedModel(LlmProviderKind.OpenAI, "gpt-test", 2m, 8m);
        var claude = PricedModel(LlmProviderKind.Anthropic, "claude-test", 3m, 15m);
        var lookups = new List<(LlmProviderKind, string)>();
        var bill = BatchingLlmProvider.PriceUnusedResults(ledger, (kind, id) =>
        {
            lookups.Add((kind, id));
            return kind == LlmProviderKind.OpenAI && id == "gpt-test" ? gpt
                : kind == LlmProviderKind.Anthropic && id == "claude-test" ? claude : null;
        });

        Assert.Equal(new[] { "batch_a", "msgbatch_b" }, bill.ItemsByBatch.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { "q1" }, bill.ItemsByBatch["batch_a"]);
        Assert.Equal(new[] { "q4" }, bill.ItemsByBatch["msgbatch_b"]);
        Assert.Equal(2, bill.Lines.Count);
        Assert.Equal(BatchCost(gpt).TotalUsd + BatchCost(claude).TotalUsd, bill.Cost.TotalUsd);
        Assert.Equal(2000, bill.InputTokens);
        Assert.Equal(1000, bill.OutputTokens);
        Assert.Empty(bill.UnpricedBatchIds);
        Assert.Contains((LlmProviderKind.Anthropic, "claude-test"), lookups);

        // No side effects: nothing was marked billed
        Assert.DoesNotContain("batch_a", new BatchLedger(_folder).Replay().OrphansBilledIds);
    }

    [Fact]
    public async Task PriceUnusedResults_UnpricedModel_ListedNotBilled()
    {
        await SeedCollectedAsync("batch_priced", LlmProviderKind.OpenAI, _plan, "gpt-test", new[] { ("q1", BatchItemOutcome.Succeeded) });
        await SeedCollectedAsync("batch_unknown", LlmProviderKind.OpenAI, _plan, "edited-away", new[] { ("q2", BatchItemOutcome.Succeeded) });
        var gpt = PricedModel(LlmProviderKind.OpenAI, "gpt-test", 2m, 8m);

        var bill = BatchingLlmProvider.PriceUnusedResults(new BatchLedger(_folder), (_, id) => id == "gpt-test" ? gpt : null);

        Assert.Equal(new[] { "batch_priced" }, bill.ItemsByBatch.Keys);
        Assert.Equal(new[] { "batch_unknown" }, bill.UnpricedBatchIds);
        Assert.Equal(BatchCost(gpt).TotalUsd, bill.Cost.TotalUsd);
        Assert.Single(bill.Lines);
    }

    [Fact]
    public async Task PriceUnusedResults_NoCache_AddsNothing()
    {
        await SeedCollectedAsync("batch_nocache", LlmProviderKind.OpenAI, _plan, "gpt-test", new[] { ("q1", BatchItemOutcome.Succeeded) }, cache: false);
        var gpt = PricedModel(LlmProviderKind.OpenAI, "gpt-test", 2m, 8m);

        var bill = BatchingLlmProvider.PriceUnusedResults(new BatchLedger(_folder), (_, _) => gpt);

        Assert.True(bill.IsEmpty);
        Assert.Empty(bill.UnpricedBatchIds);
        Assert.Equal(0m, bill.Cost.TotalUsd);
    }

    [Fact]
    public async Task PriceUnusedResults_UnreadableLedger_Empty()
    {
        await SeedCollectedAsync("batch_a", LlmProviderKind.OpenAI, _plan, "gpt-test", new[] { ("q1", BatchItemOutcome.Succeeded) });
        var gpt = PricedModel(LlmProviderKind.OpenAI, "gpt-test", 2m, 8m);
        var ledger = new BatchLedger(_folder);

        OrphanBill bill;
        using (new FileStream(ledger.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            bill = BatchingLlmProvider.PriceUnusedResults(ledger, (_, _) => gpt);

        Assert.True(bill.IsEmpty);
        Assert.Empty(bill.Lines);
        Assert.Empty(bill.UnpricedBatchIds);
        Assert.Equal(0m, bill.Cost.TotalUsd);
    }

    [Fact]
    public async Task PriceUnusedResults_MatchesPriceOrphans()
    {
        await SeedCollectedAsync("batch_a", LlmProviderKind.OpenAI, _plan, "gpt-test", new[]
        {
            ("q1", BatchItemOutcome.Succeeded), ("q2", BatchItemOutcome.Succeeded)
        });
        await SeedCollectedAsync("msgbatch_b", LlmProviderKind.Anthropic, _plan, "claude-test", new[] { ("q3", BatchItemOutcome.Succeeded) });
        var gpt = PricedModel(LlmProviderKind.OpenAI, "gpt-test", 2m, 8m);
        var claude = PricedModel(LlmProviderKind.Anthropic, "claude-test", 3m, 15m);
        Func<LlmProviderKind, string, AIModelConfig?> lookup = (kind, _) => kind == LlmProviderKind.OpenAI ? gpt : claude;

        var provider = new BatchingLlmProvider(Live(), Transport(), new BatchLedger(_folder), _plan, lookup, Fast());
        _providers.Add(provider);
        var orphans = provider.PriceOrphans();
        var unused = BatchingLlmProvider.PriceUnusedResults(new BatchLedger(_folder), lookup);

        Assert.True(orphans.Cost.TotalUsd > 0);
        Assert.Equal(orphans.Cost.TotalUsd, unused.Cost.TotalUsd);
        Assert.Equal(orphans.Cost.BatchSavingsUsd, unused.Cost.BatchSavingsUsd);
        Assert.Equal(orphans.InputTokens, unused.InputTokens);
        Assert.Equal(orphans.OutputTokens, unused.OutputTokens);
        Assert.Equal(orphans.Lines.Count, unused.Lines.Count);
        Assert.Equal(orphans.ItemsByBatch.Keys.OrderBy(k => k, StringComparer.Ordinal), unused.ItemsByBatch.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WRA03_CollectedBatch_DeletesItsProviderFiles()
    {
        var transport = Transport();
        var provider = Create(Live(), transport);
        var events = new EventLog();
        events.Attach(provider);

        await Task.WhenAll(SendAll(provider, "non-relevant mail", 2)).WaitAsync(Wait);
        var finished = await events.WaitFor(BatchStatusKind.Finished);

        await Until(() => transport.DeletedFiles.Count > 0);
        var input = new BatchLedger(_folder).Replay().SubmittedById[finished.BatchId].InputFileId;
        Assert.Equal(new[] { (input, "file_out_" + finished.BatchId) },
            transport.DeletedFiles.Select(f => (f.Item1!, f.Item2!)));
    }

    [Fact]
    public async Task WRA05_CancelAllPending_CancelsEveryPlan_ReportsProvidersWithoutTransport()
    {
        var ledger = new BatchLedger(_folder);
        BatchLedgerRecord Submitted(string id, LlmProviderKind kind, string inputFile, Guid plan) => new()
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = kind,
            BatchId = id,
            PlanId = plan,
            SubmittedUtc = DateTime.UtcNow,
            InputFileId = inputFile,
            Items = new List<BatchLedgerItem> { new() { CustomId = "q" + id, Operation = "Thread", ModelId = "m" } }
        };

        await ledger.AppendAsync(Submitted("batch_open", LlmProviderKind.OpenAI, "file_in_1", _plan));
        await ledger.AppendAsync(Submitted("batch_earlier_plan", LlmProviderKind.OpenAI, "file_in_2", Guid.NewGuid()));
        await ledger.AppendAsync(Submitted("msgbatch_no_key", LlmProviderKind.Anthropic, string.Empty, Guid.NewGuid()));
        Assert.Equal(3, BatchLedger.PendingBatchCountAllPlans(_folder));

        var openAi = new FakeBatchTransport();
        var notCancelled = await BatchingLlmProvider.CancelAllPendingAsync(ledger,
            kind => kind == LlmProviderKind.OpenAI ? openAi : null).WaitAsync(Wait);

        Assert.Equal(new[] { "msgbatch_no_key" }, notCancelled);
        Assert.Equal(new[] { "batch_earlier_plan", "batch_open" }, openAi.CancelledIds.OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(new[] { "file_in_1", "file_in_2" }, openAi.DeletedFiles.Select(f => f.Item1).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(1, BatchLedger.PendingBatchCountAllPlans(_folder));
    }

    [Fact]
    public async Task CRB01_CancelAllPending_ProviderCancelFails_ReportedAndKeptPending_OthersStillCancelled()
    {
        var ledger = new BatchLedger(_folder);
        BatchLedgerRecord Submitted(string id, LlmProviderKind kind, string inputFile) => new()
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = kind,
            BatchId = id,
            PlanId = _plan,
            SubmittedUtc = DateTime.UtcNow,
            InputFileId = inputFile,
            Items = new List<BatchLedgerItem> { new() { CustomId = "q" + id, Operation = "Thread", ModelId = "m" } }
        };

        await ledger.AppendAsync(Submitted("batch_unreachable", LlmProviderKind.OpenAI, "file_in_1"));
        await ledger.AppendAsync(Submitted("msgbatch_ok", LlmProviderKind.Anthropic, string.Empty));

        var openAi = new FakeBatchTransport
        {
            CancelFailure = new BatchCancelFailedException("batch_unreachable", LlmFailureKind.Network, "timed out")
        };
        var anthropic = new FakeBatchTransport();
        var notCancelled = await BatchingLlmProvider.CancelAllPendingAsync(ledger,
            kind => kind == LlmProviderKind.OpenAI ? openAi : anthropic).WaitAsync(Wait);

        Assert.Equal(new[] { "batch_unreachable" }, notCancelled);
        Assert.Equal(new[] { "msgbatch_ok" }, anthropic.CancelledIds);
        // The failed batch keeps its pending ledger line and its uploaded input file
        Assert.Equal(1, BatchLedger.PendingBatchCountAllPlans(_folder));
        Assert.Empty(openAi.DeletedFiles);
    }
}
