using System.Text;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;
using Xunit;

namespace ReelDiscovery.Tests.Ai;

public class BatchLedgerTests : IDisposable
{
    private readonly string _folder;
    private readonly Guid _plan = Guid.NewGuid();

    public BatchLedgerTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "rd-batchledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder cleanup only; a locked file must not fail the test
        }
    }

    private BatchLedgerRecord Submitted(string batchId, Guid planId, params string[] customIds) => new()
    {
        Type = BatchLedgerRecordType.Submitted,
        Utc = DateTime.UtcNow,
        Provider = LlmProviderKind.Anthropic,
        BatchId = batchId,
        PlanId = planId,
        SubmittedUtc = DateTime.UtcNow,
        StageLabel = "Writing evidence",
        Items = customIds.Select(id => new BatchLedgerItem { CustomId = id, Operation = "Thread", ModelId = "claude-sonnet-4-5" }).ToList(),
        Total = customIds.Length
    };

    private static BatchLedgerRecord Simple(BatchLedgerRecordType type, string batchId) =>
        new() { Type = type, Utc = DateTime.UtcNow, BatchId = batchId };

    [Fact]
    public async Task SubmittedThenCollected_NotPending()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b1", _plan, "qa", "qb", "qc"));
        await ledger.AppendAsync(Simple(BatchLedgerRecordType.Collected, "b1"));

        var state = new BatchLedger(_folder).Replay();

        Assert.True(state.SubmittedById.ContainsKey("b1"));
        Assert.Equal(3, state.SubmittedById["b1"].Items.Count);
        Assert.Equal("claude-sonnet-4-5", state.SubmittedById["b1"].Items[0].ModelId);
        Assert.Contains("b1", state.CollectedIds);
        Assert.Empty(state.Pending(_plan));
        Assert.Equal("b1", state.BatchIdByCustomId["qa"]);
        Assert.Equal("b1", state.BatchIdByCustomId["qb"]);
        Assert.Equal("b1", state.BatchIdByCustomId["qc"]);
    }

    [Fact]
    public async Task SubmittedOnly_IsPendingForItsPlan()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b2", _plan, "qx"));

        var pending = ledger.Replay().Pending(_plan).ToList();

        Assert.Single(pending);
        Assert.Equal("b2", pending[0].BatchId);
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, _plan));
        Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, Guid.NewGuid()));
    }

    private async Task SeedMixedProvidersAsync()
    {
        var ledger = new BatchLedger(_folder);
        BatchLedgerRecord Of(string batchId, LlmProviderKind kind, Guid planId)
        {
            var record = Submitted(batchId, planId, "q" + batchId);
            record.Provider = kind;
            return record;
        }

        await ledger.AppendAsync(Of("o1", LlmProviderKind.OpenAI, _plan));
        await ledger.AppendAsync(Of("o2", LlmProviderKind.OpenAI, _plan));
        await ledger.AppendAsync(Of("a1", LlmProviderKind.Anthropic, _plan));
        await ledger.AppendAsync(Of("a-other-plan", LlmProviderKind.Anthropic, Guid.NewGuid()));

        // Collected and cancelled batches are not pending for any provider
        await ledger.AppendAsync(Of("o-collected", LlmProviderKind.OpenAI, _plan));
        await ledger.AppendAsync(Simple(BatchLedgerRecordType.Collected, "o-collected"));
        await ledger.AppendAsync(Of("a-cancelled", LlmProviderKind.Anthropic, _plan));
        await ledger.AppendAsync(Simple(BatchLedgerRecordType.Cancelled, "a-cancelled"));
    }

    [Fact]
    public async Task PendingBatchCount_ByProvider_CountsOnlyThatProvider()
    {
        await SeedMixedProvidersAsync();

        Assert.Equal(2, BatchLedger.PendingBatchCount(_folder, _plan, LlmProviderKind.OpenAI));
        Assert.Equal(1, BatchLedger.PendingBatchCount(_folder, _plan, LlmProviderKind.Anthropic));
        Assert.Equal(3, BatchLedger.PendingBatchCount(_folder, _plan));
        Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, Guid.NewGuid(), LlmProviderKind.OpenAI));
    }

    [Fact]
    public async Task PendingBatchCountOtherProviders_ExcludesCurrentAndOtherPlans()
    {
        await SeedMixedProvidersAsync();

        Assert.Equal(1, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.OpenAI));
        Assert.Equal(2, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.Anthropic));
        Assert.Equal(4, BatchLedger.PendingBatchCountAllPlans(_folder));
    }

    [Fact]
    public void PendingBatchCount_ByProvider_BlankOrMissingFolder_Zero()
    {
        Assert.Equal(0, BatchLedger.PendingBatchCount("", _plan, LlmProviderKind.OpenAI));
        Assert.Equal(0, BatchLedger.PendingBatchCount("  ", _plan, LlmProviderKind.Anthropic));
        Assert.Equal(0, BatchLedger.PendingBatchCountOtherProviders("", _plan, LlmProviderKind.OpenAI));
        Assert.Equal(0, BatchLedger.PendingBatchCountOtherProviders(Path.Combine(_folder, "nope"), _plan, LlmProviderKind.OpenAI));
    }

    [Fact]
    public async Task PendingBatchCount_ByProvider_UnreadableLedger_Zero()
    {
        await SeedMixedProvidersAsync();

        using (new FileStream(new BatchLedger(_folder).FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, _plan, LlmProviderKind.OpenAI));
            Assert.Equal(0, BatchLedger.PendingBatchCountOtherProviders(_folder, _plan, LlmProviderKind.OpenAI));
        }
    }

    [Fact]
    public async Task Cancelled_NotPending()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b2", _plan, "qx"));
        await ledger.AppendAsync(Simple(BatchLedgerRecordType.Cancelled, "b2"));

        var state = ledger.Replay();

        Assert.Contains("b2", state.CancelledIds);
        Assert.Empty(state.Pending(_plan));
        Assert.Equal(0, BatchLedger.PendingBatchCount(_folder, _plan));
    }

    [Fact]
    public async Task ConsumedAndOrphansBilled_Replay()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b1", _plan, "qa", "qb"));
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Consumed, BatchId = "b1",
            Items = new List<BatchLedgerItem> { new() { CustomId = "qa" } }
        });
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Consumed, BatchId = "b1",
            Items = new List<BatchLedgerItem> { new() { CustomId = "qb" } }
        });
        await ledger.AppendAsync(Simple(BatchLedgerRecordType.OrphansBilled, "b1"));

        var state = ledger.Replay();

        Assert.Contains("qa", state.ConsumedCustomIds);
        Assert.Contains("qb", state.ConsumedCustomIds);
        Assert.Contains("b1", state.OrphansBilledIds);
    }

    [Fact]
    public async Task TornLastLine_SkippedAndNextAppendStartsFreshLine()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b1", _plan, "qa"));
        File.AppendAllText(BatchLedger.PathFor(_folder), "{\"Type\":\"Submitted\",\"BatchId\":\"b9", Encoding.UTF8);

        var torn = ledger.Replay();
        Assert.Single(torn.SubmittedById);

        await ledger.AppendAsync(Submitted("b3", _plan, "qz"));

        var state = new BatchLedger(_folder).Replay();
        Assert.True(state.SubmittedById.ContainsKey("b1"));
        Assert.True(state.SubmittedById.ContainsKey("b3"));
        Assert.False(state.SubmittedById.ContainsKey("b9"));
    }

    [Fact]
    public async Task GarbageAndUnknownType_SkippedNeverThrows()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b1", _plan, "qa"));
        File.AppendAllText(BatchLedger.PathFor(_folder),
            "not json at all\n{\"Type\":99,\"BatchId\":\"b7\"}\n{\"Type\":\"Exploded\",\"BatchId\":\"b8\"}\nnull\n\n[1,2]\n", Encoding.UTF8);

        var state = ledger.Replay();

        Assert.Single(state.SubmittedById);
        Assert.Single(state.Pending(_plan));
    }

    [Fact]
    public void MissingFile_EmptyState()
    {
        var state = new BatchLedger(Path.Combine(_folder, "nope")).Replay();

        Assert.Empty(state.SubmittedById);
        Assert.Empty(state.Pending(_plan));
        Assert.Equal(0, BatchLedger.PendingBatchCount(Path.Combine(_folder, "nope"), _plan));
    }

    [Fact]
    public void SaveResults_ReadResults_RoundTrip()
    {
        var ledger = new BatchLedger(_folder);
        var items = new List<BatchItemResult>
        {
            new()
            {
                CustomId = "qa",
                Outcome = BatchItemOutcome.Succeeded,
                Response = new LlmProviderResponse
                {
                    Outcome = LlmOutcome.Truncated, FailureKind = LlmFailureKind.None, Text = "{\"x\":1}",
                    InputTokens = 11, CacheWriteTokens = 22, CacheReadTokens = 33, OutputTokens = 44, IsBatch = true
                }
            },
            new() { CustomId = "qb", Outcome = BatchItemOutcome.Errored, Message = "overloaded" },
            new() { CustomId = "qc", Outcome = BatchItemOutcome.Expired }
        };

        ledger.SaveResults("msgbatch_01", items);
        var read = new BatchLedger(_folder).ReadResults("msgbatch_01");

        Assert.Equal(3, read.Count);
        Assert.Equal("qa", read[0].CustomId);
        Assert.Equal(BatchItemOutcome.Succeeded, read[0].Outcome);
        var r = read[0].Response!;
        Assert.Equal(LlmOutcome.Truncated, r.Outcome);
        Assert.Equal(LlmFailureKind.None, r.FailureKind);
        Assert.Equal("{\"x\":1}", r.Text);
        Assert.Equal(11, r.InputTokens);
        Assert.Equal(22, r.CacheWriteTokens);
        Assert.Equal(33, r.CacheReadTokens);
        Assert.Equal(44, r.OutputTokens);
        Assert.True(r.IsBatch);
        Assert.Equal(BatchItemOutcome.Errored, read[1].Outcome);
        Assert.Equal("overloaded", read[1].Message);
        Assert.Null(read[1].Response);
        Assert.Equal(BatchItemOutcome.Expired, read[2].Outcome);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, BatchLedger.ResultsFolderName), "*.tmp*"));
    }

    [Fact]
    public void ReadResults_Missing_Empty()
    {
        Assert.Empty(new BatchLedger(_folder).ReadResults("never"));
    }

    [Fact]
    public void SafeFileId_KeepsSafeIds_HashesOthers()
    {
        Assert.Equal("msgbatch_01AbC-x", BatchLedger.SafeFileId("msgbatch_01AbC-x"));

        var evil = BatchLedger.SafeFileId("../../evil");
        Assert.Equal(41, evil.Length);
        Assert.Matches("^h[0-9a-f]{40}$", evil);

        var longId = BatchLedger.SafeFileId(new string('a', 100));
        Assert.Matches("^h[0-9a-f]{40}$", longId);
        Assert.Equal(longId, BatchLedger.SafeFileId(new string('a', 100)));

        // $ would accept a trailing newline; the anchor must be end-of-string
        Assert.Matches("^h[0-9a-f]{40}$", BatchLedger.SafeFileId("batch_1\n"));
    }

    [Fact]
    public void ResultsPathFor_StaysInsideResultsFolder()
    {
        var ledger = new BatchLedger(_folder);
        var root = Path.GetFullPath(Path.Combine(_folder, BatchLedger.ResultsFolderName)) + Path.DirectorySeparatorChar;

        foreach (var id in new[] { "../../evil", "..\\..\\evil", "C:\\Windows\\x", "/etc/passwd", "", "batch_ok" })
        {
            var path = Path.GetFullPath(ledger.ResultsPathFor(id));
            Assert.StartsWith(root, path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".jsonl", path);
        }
    }

    private static BatchItemRequest Item(string id) =>
        new() { CustomId = id, Request = new LlmProviderRequest { UserPrompt = id } };

    [Fact]
    public async Task Fake_SubmitStatusResults()
    {
        var fake = new FakeBatchTransport
        {
            PollsUntilEnded = 2,
            Respond = item => FakeLlmProvider.Ok("{\"id\":\"" + item.CustomId + "\"}")
        };
        fake.ErroredIds.Add("q2");
        fake.ExpiredIds.Add("q3");

        var submit = await fake.SubmitAsync(new[] { Item("q1"), Item("q2"), Item("q3") });
        Assert.True(submit.Succeeded);
        Assert.Equal("fb_1", submit.BatchId);
        Assert.Single(fake.Submissions);
        Assert.Equal(3, fake.Submissions[0].Count);

        Assert.Equal(BatchState.InProgress, (await fake.GetStatusAsync(submit.BatchId)).State);
        Assert.Equal(BatchState.InProgress, (await fake.GetStatusAsync(submit.BatchId)).State);
        var ended = await fake.GetStatusAsync(submit.BatchId);
        Assert.Equal(BatchState.Ended, ended.State);
        Assert.Equal(3, fake.StatusCalls);

        var results = await fake.GetResultsAsync(submit.BatchId);
        Assert.True(results.Succeeded);
        Assert.Equal(BatchItemOutcome.Succeeded, results.Items.Single(i => i.CustomId == "q1").Outcome);
        Assert.True(results.Items.Single(i => i.CustomId == "q1").Response!.IsBatch);
        Assert.Equal("{\"id\":\"q1\"}", results.Items.Single(i => i.CustomId == "q1").Response!.Text);
        Assert.Equal(BatchItemOutcome.Errored, results.Items.Single(i => i.CustomId == "q2").Outcome);
        Assert.Equal(BatchItemOutcome.Expired, results.Items.Single(i => i.CustomId == "q3").Outcome);

        await fake.CancelAsync("fb_1");
        await fake.DeleteFilesAsync("in", "out");
        Assert.Equal(new[] { "fb_1" }, fake.CancelledIds);
        Assert.Single(fake.DeletedFiles);
    }

    [Fact]
    public async Task Fake_SubmitFailureAndWholeBatchFailure()
    {
        var failing = new FakeBatchTransport { SubmitFailure = LlmFailureKind.RateLimited };
        var submit = await failing.SubmitAsync(new[] { Item("q1") });
        Assert.False(submit.Succeeded);
        Assert.Equal(LlmFailureKind.RateLimited, submit.FailureKind);

        var broken = new FakeBatchTransport { FailWholeBatch = true };
        var ok = await broken.SubmitAsync(new[] { Item("q1") });
        Assert.Equal(BatchState.Failed, (await broken.GetStatusAsync(ok.BatchId)).State);
    }

    // ----- CR-A05: unreadable is not empty -----

    [Fact]
    public async Task CRA05_ReplayWhileAnAppendHandleIsOpen_StillReadsTheLedger()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b1", _plan, "qa"));

        // The handle AppendAsync holds while writing (ReadWrite, shared for Read only)
        using (new FileStream(ledger.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            var state = new BatchLedger(_folder).Replay();

            Assert.True(state.Readable);
            Assert.Single(state.Pending(_plan));
        }
    }

    [Fact]
    public async Task CRA05_LockedLedger_IsUnreadable_ProviderRunsLiveAndCancelRefuses()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(Submitted("b1", _plan, "qa"));

        using (new FileStream(ledger.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var state = new BatchLedger(_folder).Replay();
            Assert.False(state.Readable);
            Assert.Empty(state.SubmittedById);

            // Batch mode falls back to live instead of resubmitting what may already be paid for
            var live = new FakeLlmProvider { Respond = r => FakeLlmProvider.Ok("live:" + r.UserPrompt) };
            var transport = new FakeBatchTransport();
            var provider = new BatchingLlmProvider(live, transport, new BatchLedger(_folder), _plan, _ => null,
                new BatchTimings { QuietPeriod = TimeSpan.FromMilliseconds(20) });
            try
            {
                Assert.True(provider.LiveOnly);
                LlmProviderResponse response;
                using (BatchStage.Enter("non-relevant mail"))
                    response = await provider.SendAsync(new LlmProviderRequest
                    {
                        ModelId = "m", SchemaName = "Thread", SchemaJson = "{}", UserPrompt = "a", AllowBatch = true
                    }).WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal("live:a", response.Text);
                Assert.Empty(transport.Submissions);
            }
            finally
            {
                provider.Detach();
            }

            // Start Over's cancel reports the failure instead of cancelling nothing
            await Assert.ThrowsAsync<IOException>(() =>
                BatchingLlmProvider.CancelPendingAsync(new BatchLedger(_folder), _ => transport, _plan));
        }

        Assert.True(new BatchLedger(_folder).Replay().Readable);
    }
}
