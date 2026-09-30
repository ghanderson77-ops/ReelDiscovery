using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Generation.EmailGeneratorTestSetup;

namespace ReelDiscovery.Tests.Generation;

/// <summary>
/// CR-01 (generator layer): an OperationCanceledException that is not a run cancel (a timeout)
/// must be recorded as a skip, never end the run silently. WR-05: state.Result is always current.
/// </summary>
public class EmailGeneratorTimeoutTests
{
    private static TaskCanceledException Timeout() =>
        new("The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing.");

    [Fact]
    public async Task StorylineTimeout_SkipsStorylineAndContinues()
    {
        var fake = new FakeLlmProvider();
        var calls = 0;
        fake.OnSend = _ =>
        {
            if (calls++ == 0)
                throw Timeout();
        };
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson("Storyline two")));

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.False(result.WasCancelled);
            Assert.Null(result.FatalError);
            var error = Assert.Single(result.Errors);
            Assert.Contains("Storyline 1", error);
            Assert.Contains("timed out", error);
            Assert.Equal(3, CountEml(folder));
            Assert.Equal(3, result.TotalEmailsGenerated);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task AllCallsTimeOut_NeverReportsCleanSuccess()
    {
        var fake = new FakeLlmProvider { ThrowOnSend = Timeout() };

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.False(result.WasCancelled);
            Assert.Null(result.FatalError);
            Assert.Equal(2, result.Errors.Count);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task RunIsolated_Timeout_RecordsSkip()
    {
        var result = new GenerationResult();
        using var runCts = new CancellationTokenSource();

        var value = await EmailGenerator.RunIsolatedAsync(() => throw Timeout(), result, "Image for 'X'", runCts);

        Assert.Equal(0, value);
        Assert.False(runCts.IsCancellationRequested);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Image for 'X' skipped (request timed out)", error);
    }

    [Fact]
    public async Task RunIsolated_RunCancelled_Rethrows()
    {
        var result = new GenerationResult();
        using var runCts = new CancellationTokenSource();
        runCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EmailGenerator.RunIsolatedAsync(
                () => throw new OperationCanceledException(runCts.Token), result, "Image for 'X'", runCts));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RecordUnexpectedStop_NoUserCancelNoFatal_AddsError()
    {
        var result = new GenerationResult();

        EmailGenerator.RecordUnexpectedStop(result, userCancelled: false);

        Assert.False(result.WasCancelled);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Run stopped unexpectedly", error);
    }

    [Fact]
    public void RecordUnexpectedStop_UserCancel_NoError()
    {
        var result = new GenerationResult();

        EmailGenerator.RecordUnexpectedStop(result, userCancelled: true);

        Assert.True(result.WasCancelled);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RecordUnexpectedStop_Fatal_NoExtraError()
    {
        var result = new GenerationResult { FatalError = "Auth: bad key" };

        EmailGenerator.RecordUnexpectedStop(result, userCancelled: false);

        Assert.False(result.WasCancelled);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task FatalBeforeAnyThread_SetsStateResult()
    {
        var fake = new FakeLlmProvider();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 2);
            state.Result = new GenerationResult(); // stale result from a previous run
            state.GeneratedThreads = new List<EmailThread> { new() };

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.Same(result, state.Result);
            Assert.Empty(state.GeneratedThreads);
            Assert.NotNull(state.Result!.FatalError);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }
}
