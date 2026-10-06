using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai;

public class LlmGatewayTests
{
    public class ResultDto
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private static AIModelConfig StoryModel(LlmProviderKind provider = LlmProviderKind.OpenAI, int maxOutput = 128_000) => new()
    {
        ModelId = "story-m",
        DisplayName = "Story",
        Provider = provider,
        Effort = "none",
        MaxOutputTokens = maxOutput,
        InputTokenPricePerMillion = 2m,
        OutputTokenPricePerMillion = 10m,
        CacheWriteTokenPricePerMillion = 2.5m,
        CacheReadTokenPricePerMillion = 0.2m,
        BatchDiscountPercent = 50m
    };

    private static AIModelConfig VolumeModel(LlmProviderKind provider = LlmProviderKind.OpenAI) => new()
    {
        ModelId = "volume-m",
        DisplayName = "Volume",
        Provider = provider,
        Effort = null,
        MaxOutputTokens = 64_000,
        InputTokenPricePerMillion = 0.1m,
        OutputTokenPricePerMillion = 0.5m
    };

    private static LlmRequest Request(LlmTier tier = LlmTier.Story, int maxTokens = 16_000) => new()
    {
        Tier = tier,
        OperationName = "Test Operation",
        SystemPrompt = "system prefix",
        UserPrompt = "user suffix",
        MaxOutputTokens = maxTokens
    };

    private static (LlmGateway Gateway, FakeLlmProvider Fake, TokenUsageTracker Tracker) Create(int storyMaxOutput = 128_000)
    {
        var fake = new FakeLlmProvider();
        var tracker = new TokenUsageTracker();
        var gateway = new LlmGateway(fake, StoryModel(maxOutput: storyMaxOutput), VolumeModel(), tracker);
        return (gateway, fake, tracker);
    }

    [Fact]
    public async Task Succeeded_ReturnsTypedValue()
    {
        var (gateway, fake, tracker) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"A\",\"count\":2}"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(LlmOutcome.Succeeded, result.Outcome);
        Assert.Equal("A", result.Value!.Name);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(1, result.Attempts);
        Assert.Equal("Test Operation", result.OperationName);
        Assert.Equal("story-m", result.ModelId);
        Assert.Equal(LlmProviderKind.OpenAI, result.Provider);
        Assert.Single(tracker.Entries);
    }

    [Fact]
    public async Task Refused_NoRetry()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("I can't help with that"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Single(fake.Requests);
        Assert.Equal(LlmOutcome.Refused, result.Outcome);
        Assert.Equal("I can't help with that", result.Message);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Truncated_RetriesOnceAtDoubleTokens()
    {
        var (gateway, fake, tracker) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());

        var result = await gateway.GenerateAsync<ResultDto>(Request(maxTokens: 16_000));

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(16_000, fake.Requests[0].MaxTokens);
        Assert.Equal(32_000, fake.Requests[1].MaxTokens);
        Assert.Equal(LlmOutcome.Truncated, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, tracker.Entries.Count); // every attempt is billed
    }

    [Fact]
    public async Task Truncated_DoubleCappedAtModelCeiling()
    {
        var (gateway, fake, _) = Create(storyMaxOutput: 128_000);
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());

        await gateway.GenerateAsync<ResultDto>(Request(maxTokens: 100_000));

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(100_000, fake.Requests[0].MaxTokens);
        Assert.Equal(128_000, fake.Requests[1].MaxTokens);
    }

    [Fact]
    public async Task Truncated_AtProviderCeiling_NoRetry()
    {
        var (gateway, fake, _) = Create(storyMaxOutput: 128_000);
        fake.MaxTokensCeiling = 21_333;
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());

        var result = await gateway.GenerateAsync<ResultDto>(Request(maxTokens: 32_000));

        Assert.Single(fake.Requests);
        Assert.Equal(21_333, fake.Requests[0].MaxTokens);
        Assert.Equal(LlmOutcome.Truncated, result.Outcome);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Truncated_RetryCappedAtProviderCeiling()
    {
        var (gateway, fake, _) = Create(storyMaxOutput: 128_000);
        fake.MaxTokensCeiling = 21_333;
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());

        var result = await gateway.GenerateAsync<ResultDto>(Request(maxTokens: 16_000));

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(16_000, fake.Requests[0].MaxTokens);
        Assert.Equal(21_333, fake.Requests[1].MaxTokens);
        Assert.Equal(LlmOutcome.Truncated, result.Outcome);
    }

    [Fact]
    public async Task Truncated_ThenSucceeded()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Truncated());
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"B\",\"count\":1}"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(LlmOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal("B", result.Value!.Name);
    }

    [Fact]
    public async Task InvalidJson_ThreeAttemptsMax()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("not json"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(3, fake.Requests.Count);
        Assert.Equal(LlmOutcome.Invalid, result.Outcome);
        Assert.Equal(3, result.Attempts);
        Assert.Equal("not json", result.RawText);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task EmptyText_IsInvalid()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(""));
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"C\",\"count\":3}"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(LlmOutcome.Succeeded, result.Outcome);

        var (gateway2, fake2, _) = Create();
        fake2.Responses.Enqueue(FakeLlmProvider.Ok("   "));

        var result2 = await gateway2.GenerateAsync<ResultDto>(Request());

        Assert.Equal(3, fake2.Requests.Count);
        Assert.Equal(LlmOutcome.Invalid, result2.Outcome);
    }

    [Theory]
    [InlineData(LlmFailureKind.SpendLimit)]
    [InlineData(LlmFailureKind.Auth)]
    public async Task SpendLimit_FatalNoRetry(LlmFailureKind kind)
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(kind, "limit reached"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Single(fake.Requests);
        Assert.Equal(LlmOutcome.Failed, result.Outcome);
        Assert.Equal(kind, result.FailureKind);
        Assert.True(result.IsFatal);
        Assert.Equal("limit reached", result.Message);
    }

    [Theory]
    [InlineData(LlmFailureKind.RateLimited)]
    [InlineData(LlmFailureKind.Transient)]
    [InlineData(LlmFailureKind.BadRequest)]
    [InlineData(LlmFailureKind.Network)]
    public async Task RateLimitedAndTransient_NoAppRetry(LlmFailureKind kind)
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(kind));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Single(fake.Requests);
        Assert.Equal(LlmOutcome.Failed, result.Outcome);
        Assert.False(result.IsFatal);
    }

    [Fact]
    public async Task TierResolution_UsesSelectedModels()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"A\",\"count\":2}"));

        await gateway.GenerateAsync<ResultDto>(Request(LlmTier.Story));
        await gateway.GenerateAsync<ResultDto>(Request(LlmTier.Volume));

        Assert.Equal(2, fake.Requests.Count);

        var story = fake.Requests[0];
        Assert.Equal("story-m", story.ModelId);
        Assert.Equal("none", story.Effort);
        Assert.Equal(JsonSchemaRegistry.GetSchemaJson(typeof(ResultDto)), story.SchemaJson);
        Assert.Equal(JsonSchemaRegistry.GetSchemaName(typeof(ResultDto)), story.SchemaName);
        Assert.Equal("system prefix", story.SystemPrompt);
        Assert.Equal("user suffix", story.UserPrompt);

        var volume = fake.Requests[1];
        Assert.Equal("volume-m", volume.ModelId);
        Assert.Null(volume.Effort);

        Assert.Equal("story-m", gateway.ResolveModel(LlmTier.Story).ModelId);
        Assert.Equal("volume-m", gateway.ResolveModel(LlmTier.Volume).ModelId);
        Assert.Equal(LlmProviderKind.OpenAI, gateway.Provider);
    }

    [Fact]
    public void Constructor_RejectsMixedProviders()
    {
        var openAI = new FakeLlmProvider(LlmProviderKind.OpenAI);

        Assert.Throws<ArgumentException>(() =>
            new LlmGateway(openAI, StoryModel(LlmProviderKind.Anthropic), VolumeModel(LlmProviderKind.OpenAI)));
        Assert.Throws<ArgumentException>(() =>
            new LlmGateway(openAI, StoryModel(LlmProviderKind.OpenAI), VolumeModel(LlmProviderKind.Anthropic)));
    }

    [Fact]
    public async Task GetValueOrThrow_ThrowsLlmCallException()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("nope"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        var ex = Assert.Throws<LlmCallException>(() => result.GetValueOrThrow());
        Assert.Equal(LlmOutcome.Refused, ex.Result.Outcome);
        Assert.Contains("Test Operation", ex.Message);
        Assert.Contains("Refused", ex.Message);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var (gateway, fake, _) = Create();
        fake.ThrowOnSend = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.GenerateAsync<ResultDto>(Request()));

        var (gateway2, fake2, _) = Create();
        fake2.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"A\",\"count\":2}"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway2.GenerateAsync<ResultDto>(Request(), cts.Token));
        Assert.Empty(fake2.Requests);
    }

    [Fact]
    public async Task TokenTotals_SumEveryBilledAttempt()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Truncated(10, 5));
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"B\",\"count\":1}", 20, 7));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(LlmOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(30, result.InputTokens);
        Assert.Equal(12, result.OutputTokens);
    }

    [Fact]
    public async Task TokenTotals_RefusedReportsSingleResponse()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("no"));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(LlmOutcome.Refused, result.Outcome);
        Assert.Equal(10, result.InputTokens);
        Assert.Equal(2, result.OutputTokens);
    }

    [Fact]
    public async Task TokenTotals_InvalidRetriesAreCounted()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("not json", 3, 1));
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"C\",\"count\":3}", 4, 2));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        Assert.Equal(LlmOutcome.Succeeded, result.Outcome);
        Assert.Equal(7, result.InputTokens);
        Assert.Equal(3, result.OutputTokens);
    }

    private static LlmProviderResponse Cached(string text, int input, int cacheWrite, int cacheRead, int output, bool isBatch = false) => new()
    {
        Outcome = LlmOutcome.Succeeded,
        Text = text,
        InputTokens = input,
        CacheWriteTokens = cacheWrite,
        CacheReadTokens = cacheRead,
        OutputTokens = output,
        IsBatch = isBatch
    };

    [Fact]
    public async Task CacheTokens_FlowToResultAndCost()
    {
        var (gateway, fake, tracker) = Create();
        fake.Responses.Enqueue(Cached("{\"name\":\"A\",\"count\":1}", 100, 50, 400, 20));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        var model = gateway.ResolveModel(LlmTier.Story);
        var expected = model.CalculateCost(new TokenUsage { InputTokens = 100, CacheWriteTokens = 50, CacheReadTokens = 400, OutputTokens = 20 });

        Assert.Equal(550, result.InputTokens);
        Assert.Equal(50, result.CacheWriteTokens);
        Assert.Equal(400, result.CacheReadTokens);
        Assert.Equal(20, result.OutputTokens);
        Assert.Equal(expected.TotalUsd, result.Cost.TotalUsd);
        Assert.Equal(expected.CacheReadUsd, result.Cost.CacheReadUsd);
        Assert.Equal(expected.TotalUsd, result.CostUsd);
        Assert.True(result.Cost.CacheReadUsd > 0);
        Assert.Equal(expected.TotalUsd, tracker.TotalCost);
        Assert.Equal(550, tracker.TotalInputTokens);
    }

    [Fact]
    public async Task CostAndTokens_SumOverAttempts()
    {
        var (gateway, fake, tracker) = Create();
        fake.Responses.Enqueue(Cached("not json", 10, 0, 100, 5));
        fake.Responses.Enqueue(Cached("{\"name\":\"A\",\"count\":1}", 20, 30, 200, 7));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        var model = gateway.ResolveModel(LlmTier.Story);
        var expected = model.CalculateCost(new TokenUsage { InputTokens = 10, CacheReadTokens = 100, OutputTokens = 5 });
        expected.Add(model.CalculateCost(new TokenUsage { InputTokens = 20, CacheWriteTokens = 30, CacheReadTokens = 200, OutputTokens = 7 }));

        Assert.Equal(LlmOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(360, result.InputTokens);
        Assert.Equal(30, result.CacheWriteTokens);
        Assert.Equal(300, result.CacheReadTokens);
        Assert.Equal(12, result.OutputTokens);
        Assert.Equal(expected.TotalUsd, result.Cost.TotalUsd);
        Assert.Equal(2, tracker.Entries.Count);
        Assert.Equal(expected.TotalUsd, tracker.TotalCost);
    }

    [Fact]
    public async Task AllowBatch_OnlyOnFirstAttempt()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("not json"));
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"A\",\"count\":1}"));

        var request = Request();
        request.AllowBatch = true;
        await gateway.GenerateAsync<ResultDto>(request);

        Assert.Equal(2, fake.Requests.Count);
        Assert.True(fake.Requests[0].AllowBatch);
        Assert.False(fake.Requests[1].AllowBatch);
    }

    [Fact]
    public async Task AllowBatchFalse_NeverSent()
    {
        var (gateway, fake, _) = Create();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("not json"));
        fake.Responses.Enqueue(FakeLlmProvider.Ok("{\"name\":\"A\",\"count\":1}"));

        await gateway.GenerateAsync<ResultDto>(Request());

        Assert.All(fake.Requests, r => Assert.False(r.AllowBatch));
    }

    [Fact]
    public async Task BatchResponse_PricedWithDiscount()
    {
        var (gateway, fake, tracker) = Create();
        fake.Responses.Enqueue(Cached("{\"name\":\"A\",\"count\":1}", 1000, 0, 0, 500, isBatch: true));

        var result = await gateway.GenerateAsync<ResultDto>(Request());

        var model = gateway.ResolveModel(LlmTier.Story);
        var expected = model.CalculateCost(new TokenUsage { InputTokens = 1000, OutputTokens = 500, IsBatch = true });

        Assert.True(result.Cost.BatchSavingsUsd > 0);
        Assert.Equal(expected.BatchSavingsUsd, result.Cost.BatchSavingsUsd);
        Assert.Equal(expected.TotalUsd, result.CostUsd);
        Assert.Equal(expected.BatchSavingsUsd, tracker.TotalBatchSavings);
    }

    [Fact]
    public void Tracker_BreakdownAndMedia()
    {
        var tracker = new TokenUsageTracker();
        var model = StoryModel();
        var usage = new TokenUsage { InputTokens = 1000, CacheWriteTokens = 200, CacheReadTokens = 5000, OutputTokens = 300, IsBatch = true };
        var cost = model.CalculateCost(usage);

        tracker.RecordUsage("Op", model, usage, cost);
        tracker.RecordMediaUsage("Image - x", 0.04m);

        Assert.Equal(cost.TotalUsd + 0.04m, tracker.TotalCost);
        Assert.Equal(cost.InputUsd, tracker.TotalInputCost);
        Assert.Equal(cost.CacheWriteUsd, tracker.TotalCacheWriteCost);
        Assert.Equal(cost.CacheReadUsd, tracker.TotalCacheReadCost);
        Assert.Equal(cost.OutputUsd, tracker.TotalOutputCost);
        Assert.Equal(cost.BatchSavingsUsd, tracker.TotalBatchSavings);
        Assert.Equal(0.04m, tracker.TotalMediaCost);
        Assert.Equal(6200, tracker.TotalInputTokens);
        Assert.Equal(200, tracker.TotalCacheWriteTokens);
        Assert.Equal(5000, tracker.TotalCacheReadTokens);
        Assert.Equal(300, tracker.TotalOutputTokens);
        Assert.Equal(cost.TotalUsd, tracker.Entries[0].Cost);
    }

    [Fact]
    public void Tracker_LegacyRecordUsage_Unchanged()
    {
        var tracker = new TokenUsageTracker();
        var model = StoryModel();

        tracker.RecordUsage("Op", model, 1000, 500);

        Assert.Equal(model.CalculateTotalCost(1000, 500), tracker.TotalCost);
        Assert.Equal(model.CalculateTotalCost(1000, 500), tracker.TotalInputCost + tracker.TotalOutputCost);
        Assert.Equal(1000, tracker.TotalInputTokens);
        Assert.Equal(500, tracker.TotalOutputTokens);
    }
}
