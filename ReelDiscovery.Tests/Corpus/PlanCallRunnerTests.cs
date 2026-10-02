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
    public void RichDraft_IsConsistent()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;

        Assert.Equal(8, guide.Facts.Count);
        Assert.Equal(8, CorpusTestData.DatedFactCount(draft));
        Assert.Equal(2, guide.Issues.Count);
        Assert.Equal(DayOfWeek.Saturday, DateOnly.Parse(guide.Facts.Single(f => f.Id == "F-007").Date).DayOfWeek);
        Assert.Equal("2024-07-04", guide.Facts.Single(f => f.Id == "F-008").Date);
        Assert.Equal(new[] { "CH-008" }, guide.Facts.Single(f => f.Id == "F-008").KnownByIds);
        Assert.Contains(guide.Facts, f => f.Significance == Models.Case.FactSignificance.Hot);
        Assert.All(guide.Facts, f => Assert.All(f.IssueIds, id => Assert.Contains(guide.Issues, i => i.Id == id)));
        Assert.Equal("America/Phoenix", BusinessCalendar.Resolve(guide.FindOrganization("ORG-02")).Id);
        Assert.Equal("2024-06-15", guide.Cast.Single(c => c.Id == "CH-009").ActiveTo);
        Assert.Equal(8, guide.IdCounters["F"]);
        Assert.Equal(2, guide.IdCounters["ISS"]);
    }

    [Fact]
    public void PromptIdHelpers_ParseIds()
    {
        const string prompt = "T-0001 msg 1 | Maria\nT-0001 msg 2 | Tom\n  T-0003 msg 1 | Lisa\nslot S-0002 then S-0001 and S-0002\nsee T-0001";

        Assert.Equal(new[] { "T-0001", "T-0003" }, CorpusTestData.ThreadIdsIn(prompt));
        Assert.Equal(new[] { "S-0002", "S-0001" }, CorpusTestData.SlotIdsIn(prompt));
        var seqs = CorpusTestData.ThreadSeqsIn(prompt);
        Assert.Equal(2, seqs.Count);
        Assert.Equal(new[] { 1, 2 }, seqs[0].Seqs);
        Assert.Equal("T-0003", seqs[1].ThreadId);
    }

    [Fact]
    public async Task Respond_IsUsedAndThreadSafe()
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("queue must be ignored"));
        fake.Respond = request => FakeLlmProvider.Ok($$"""{ "text": "{{request.UserPrompt}}" }""");
        using var run = new PlanCallRun(CancellationToken.None, 3, Hint);

        var tasks = Enumerable.Range(1, 3)
            .Select(i => Task.Run(() => PlanCallRunner.RunAsync<EchoDto>(gateway, new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = "Plan Test",
                SystemPrompt = "system",
                UserPrompt = $"prompt {i}"
            }, run)))
            .ToArray();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(3, fake.Requests.Count);
        Assert.Equal(new[] { "prompt 1", "prompt 2", "prompt 3" }, outcomes.Select(o => o.Value!.Text));
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
