using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Generation.EmailGeneratorTestSetup;

namespace ReelDiscovery.Tests.Generation;

public class EmailGeneratorOutcomeTests
{
    [Fact]
    public async Task SpendLimit_StopsRun_SavesCompletedThreads()
    {
        var fake = new FakeLlmProvider();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson("Storyline one")));
        fake.Responses.Enqueue(new LlmProviderResponse { Outcome = LlmOutcome.Failed, FailureKind = LlmFailureKind.SpendLimit, Message = "Spend limit reached" });

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.NotNull(result.FatalError);
            Assert.Contains("SpendLimit", result.FatalError);
            Assert.False(result.WasCancelled);
            Assert.Equal(3, CountEml(folder));
            Assert.Equal(3, result.TotalEmailsGenerated);
            Assert.Equal(2, fake.Requests.Count);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task Auth_IsFatal()
    {
        var fake = new FakeLlmProvider();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.NotNull(result.FatalError);
            Assert.Contains("Auth", result.FatalError);
            Assert.False(result.WasCancelled);
            Assert.Single(fake.Requests);
            Assert.Equal(0, CountEml(folder));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task Refused_SkipsStorylineAndContinues()
    {
        var fake = new FakeLlmProvider();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("I can't write that."));
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson("Storyline two")));

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.Null(result.FatalError);
            Assert.False(result.WasCancelled);
            var error = Assert.Single(result.Errors);
            Assert.Contains("skipped (refused by model)", error);
            Assert.Contains("Storyline 1", error);
            Assert.Equal(3, CountEml(folder));

            // The refused storyline was not retried: one request per storyline
            Assert.Equal(2, fake.Requests.Count);
            Assert.Single(fake.Requests, r => r.UserPrompt.Contains("Storyline 1"));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task RunIsolated_Fatal_CancelsRunCts()
    {
        var result = new GenerationResult();
        using var runCts = new CancellationTokenSource();
        var fatal = new LlmResult { Outcome = LlmOutcome.Failed, FailureKind = LlmFailureKind.SpendLimit, Message = "Spend limit reached", OperationName = "Word" };

        var value = await EmailGenerator.RunIsolatedAsync(
            () => throw new LlmCallException(fatal), result, "Attachment for 'X'", runCts);

        Assert.Equal(0, value);
        Assert.True(runCts.IsCancellationRequested);
        Assert.NotNull(result.FatalError);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task RunIsolated_Truncated_SkipsWithClassifiedMessage()
    {
        var result = new GenerationResult();
        using var runCts = new CancellationTokenSource();
        var truncated = new LlmResult { Outcome = LlmOutcome.Truncated, Message = "max tokens", OperationName = "Word" };

        var value = await EmailGenerator.RunIsolatedAsync(
            () => throw new LlmCallException(truncated), result, "Attachment for 'X'", runCts);

        Assert.Equal(0, value);
        Assert.False(runCts.IsCancellationRequested);
        Assert.Null(result.FatalError);
        Assert.Contains("Attachment for 'X' skipped (response truncated): max tokens", result.Errors);
    }

    [Fact]
    public async Task UserCancel_StillWasCancelled()
    {
        using var userCts = new CancellationTokenSource();
        var fake = new FakeLlmProvider();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson("Storyline one")));
        fake.OnSend = i =>
        {
            if (i == 1)
                userCts.Cancel();
        };

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>(), userCts.Token);

            Assert.True(result.WasCancelled);
            Assert.Null(result.FatalError);
            Assert.Equal(3, CountEml(folder));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }
}
