using System.Text.Json.Serialization;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

internal class EchoDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class PlanCallRunnerTests
{
    private const string Hint = "Click Retry Plan.";
    private const string EchoJson = """{ "text": "hello" }""";

    private static LlmRequest Request(string name = "Plan Test") => new()
    {
        Tier = LlmTier.Story,
        OperationName = name,
        SystemPrompt = "system",
        UserPrompt = "user"
    };

    [Fact]
    public async Task Success_ReturnsValue()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(EchoJson));
        using var run = new PlanCallRun(CancellationToken.None, 3, Hint);

        var started = false;
        var outcome = await PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run, () => started = true);

        Assert.True(started);
        Assert.Equal("hello", outcome.Value!.Text);
        Assert.Equal(string.Empty, outcome.Error);
        Assert.False(outcome.Refused);
        Assert.False(run.IsFatal);
    }

    [Fact]
    public async Task Refused_SetsRefusedNotFatal()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("Not appropriate."));
        using var run = new PlanCallRun(CancellationToken.None, 3, Hint);

        var outcome = await PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run);

        Assert.Null(outcome.Value);
        Assert.True(outcome.Refused);
        Assert.Equal("Not appropriate.", outcome.RefusalMessage);
        Assert.Contains("declined", outcome.Error);
        Assert.False(run.IsFatal);
    }

    [Fact]
    public async Task FatalAuth_CancelsSiblings()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key."));
        fake.Responses.Enqueue(FakeLlmProvider.Ok(EchoJson));
        using var run = new PlanCallRun(CancellationToken.None, 1, Hint);

        var first = await PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run);
        var second = await PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run);

        Assert.True(run.IsFatal);
        Assert.Equal("Invalid API key.", run.FatalError);
        Assert.True(run.Cts.IsCancellationRequested);
        Assert.Null(first.Value);
        Assert.Equal(string.Empty, first.Error);
        Assert.Null(second.Value);
        Assert.Equal(string.Empty, second.Error);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task FatalStop_ReleasesQueuedWaiters()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(EchoJson));
        using var run = new PlanCallRun(CancellationToken.None, 1, Hint);

        // Hold the only slot, queue a waiter, then stop the run: the waiter must return without sending
        await run.Semaphore.WaitAsync();
        var waiter = PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run);
        run.SetFatal("Spend limit reached.");
        var outcome = await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        run.Semaphore.Release();

        Assert.Null(outcome.Value);
        Assert.Empty(fake.Requests);
        // The waiter never acquired the slot, so it must not have released it
        Assert.Equal(1, run.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task UserCancel_Throws()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(EchoJson));
        using var userCts = new CancellationTokenSource();
        fake.OnSend = _ => userCts.Cancel();
        using var run = new PlanCallRun(userCts.Token, 3, Hint);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run));
        Assert.False(run.IsFatal);
    }

    [Fact]
    public async Task NonUserCancel_IsTimeout()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.ThrowOnSend = new OperationCanceledException();
        using var run = new PlanCallRun(CancellationToken.None, 3, Hint);

        var outcome = await PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run);

        Assert.Equal($"The request timed out. {Hint}", outcome.Error);
        Assert.False(run.IsFatal);
    }

    [Fact]
    public async Task OtherException_RecordsMessage()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.ThrowOnSend = new InvalidOperationException("boom");
        using var run = new PlanCallRun(CancellationToken.None, 3, Hint);

        var outcome = await PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run);

        Assert.Equal("boom", outcome.Error);
    }

    [Fact]
    public async Task Semaphore_BoundsConcurrency()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(EchoJson));
        var inFlight = 0;
        var maxInFlight = 0;
        fake.OnSend = _ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            while ((seen = Volatile.Read(ref maxInFlight)) < now && Interlocked.CompareExchange(ref maxInFlight, now, seen) != seen)
            {
            }

            Thread.Sleep(60);
            Interlocked.Decrement(ref inFlight);
        };
        using var run = new PlanCallRun(CancellationToken.None, 2, Hint);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => PlanCallRunner.RunAsync<EchoDto>(gateway, Request(), run)))
            .ToArray();
        var outcomes = await Task.WhenAll(tasks);

        Assert.All(outcomes, o => Assert.Equal("hello", o.Value!.Text));
        Assert.InRange(maxInFlight, 1, 2);
        Assert.Equal(5, fake.Requests.Count);
    }

    [Fact]
    public void DescribeOutcome_MatchesCaseGuideWording()
    {
        Assert.Equal("The model declined: no", PlanCallRunner.DescribeOutcome(new LlmResult { Outcome = LlmOutcome.Refused, Message = "no" }, Hint));
        Assert.Equal($"The response was cut off. {Hint}", PlanCallRunner.DescribeOutcome(new LlmResult { Outcome = LlmOutcome.Truncated }, Hint));
        Assert.Equal($"The response could not be read. {Hint}", PlanCallRunner.DescribeOutcome(new LlmResult { Outcome = LlmOutcome.Invalid }, Hint));
        Assert.Equal("Network: down", PlanCallRunner.DescribeOutcome(new LlmResult { Outcome = LlmOutcome.Failed, FailureKind = LlmFailureKind.Network, Message = "down" }, Hint));
    }

    [Fact]
    public void FillResult_IsFatalFollowsFatalError()
    {
        var result = new FillResult();
        Assert.False(result.IsFatal);
        result.FatalError = "Invalid API key.";
        Assert.True(result.IsFatal);
    }
}
