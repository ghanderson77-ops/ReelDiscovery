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

        // Consumed is appended before the waiter completes, so it is already on disk
        Assert.Contains(IdOf(Req("r0")), new BatchLedger(_folder).Replay().ConsumedCustomIds);

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

        // Not attached to the other provider's batch: a fresh submission
        using (BatchStage.Enter("wave 1"))
            await provider.SendAsync(Req("a")).WaitAsync(Wait);
        Assert.Single(transport.Submissions);
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
}
