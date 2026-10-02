using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Shared state of one bounded fan-out of planning calls: a linked token that a fatal error cancels and a slot semaphore.
/// </summary>
internal sealed class PlanCallRun : IDisposable
{
    private readonly object _lock = new();
    private string _fatalError = string.Empty;

    public PlanCallRun(CancellationToken userToken, int maxParallel, string retryHint)
    {
        var slots = Math.Max(1, maxParallel);
        UserToken = userToken;
        RetryHint = retryHint;
        Cts = CancellationTokenSource.CreateLinkedTokenSource(userToken);
        Semaphore = new SemaphoreSlim(slots, slots);
    }

    public CancellationTokenSource Cts { get; }
    public SemaphoreSlim Semaphore { get; }
    public CancellationToken UserToken { get; }

    /// <summary>
    /// Last sentence of failure messages, e.g. "Click Retry Plan.".
    /// </summary>
    public string RetryHint { get; }

    public string FatalError
    {
        get
        {
            lock (_lock)
                return _fatalError;
        }
    }

    public bool IsFatal => FatalError.Length > 0;

    /// <summary>
    /// Records the first fatal message and cancels every queued and in-flight call.
    /// </summary>
    public void SetFatal(string message)
    {
        lock (_lock)
        {
            if (_fatalError.Length == 0)
                _fatalError = message.Length > 0 ? message : "The run stopped.";
        }

        Cts.Cancel();
    }

    public void Dispose()
    {
        Cts.Dispose();
        Semaphore.Dispose();
    }
}

internal sealed class PlanCallOutcome<T> where T : class
{
    public T? Value { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool Refused { get; set; }
    public string RefusalMessage { get; set; } = string.Empty;
}

internal static class PlanCallRunner
{
    /// <summary>
    /// One bounded call: waits for a slot, skips when a fatal error already stopped the run, and classifies the outcome
    /// like CaseGuideGenerator.RunUnitAsync. Throws only when the user cancels.
    /// </summary>
    public static async Task<PlanCallOutcome<T>> RunAsync<T>(ILlmGateway llm, LlmRequest request, PlanCallRun run, Action? onStart = null)
        where T : class
    {
        var acquired = false;
        try
        {
            // Waiting on the run token lets a fatal stop release queued callers
            await run.Semaphore.WaitAsync(run.Cts.Token);
            acquired = true;

            // A fatal error in another call stopped the run: leave this unit pending
            if (run.Cts.IsCancellationRequested)
            {
                run.UserToken.ThrowIfCancellationRequested();
                return new PlanCallOutcome<T>();
            }

            onStart?.Invoke();
            var value = (await llm.GenerateAsync<T>(request, run.Cts.Token)).GetValueOrThrow();
            return new PlanCallOutcome<T> { Value = value };
        }
        catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (run.Cts.IsCancellationRequested)
        {
            // Fatal stop from another call
            return new PlanCallOutcome<T>();
        }
        catch (OperationCanceledException)
        {
            // Run not cancelled: the request timed out
            return new PlanCallOutcome<T> { Error = $"The request timed out. {run.RetryHint}" };
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop all further calls, keep completed units
            run.SetFatal(ex.Result.Message ?? ex.Message);
            return new PlanCallOutcome<T>();
        }
        catch (LlmCallException ex)
        {
            var refused = ex.Result.Outcome == LlmOutcome.Refused;
            return new PlanCallOutcome<T>
            {
                Error = DescribeOutcome(ex.Result, run.RetryHint),
                Refused = refused,
                RefusalMessage = refused ? ex.Result.Message ?? string.Empty : string.Empty
            };
        }
        catch (Exception ex)
        {
            return new PlanCallOutcome<T> { Error = ex.Message };
        }
        finally
        {
            if (acquired)
                run.Semaphore.Release();
        }
    }

    public static string DescribeOutcome(LlmResult result, string retryHint) => result.Outcome switch
    {
        LlmOutcome.Refused => $"The model declined: {result.Message}",
        LlmOutcome.Truncated => $"The response was cut off. {retryHint}",
        LlmOutcome.Invalid => $"The response could not be read. {retryHint}",
        _ => $"{result.FailureKind}: {result.Message}"
    };
}
