using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests.Fakes;

/// <summary>
/// Scripted ILlmProvider: dequeues canned responses and records every request.
/// When the queue is empty the last dequeued response is repeated.
/// </summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly object _lock = new();
    private LlmProviderResponse? _last;

    public FakeLlmProvider(LlmProviderKind kind = LlmProviderKind.OpenAI)
    {
        Kind = kind;
    }

    public LlmProviderKind Kind { get; }

    public int? MaxTokensCeiling { get; set; }

    public Queue<LlmProviderResponse> Responses { get; } = new();

    public List<LlmProviderRequest> Requests { get; } = new();

    public LlmKeyTestResult KeyTestResult { get; set; } = new() { Succeeded = true, Message = "OK" };

    public Exception? ThrowOnSend { get; set; }

    /// <summary>
    /// Invoked at the start of SendAsync with the zero-based call index, before recording.
    /// Lets tests cancel a run deterministically mid-flight.
    /// </summary>
    public Action<int>? OnSend { get; set; }

    /// <summary>
    /// When set, builds the response from the recorded request and the Responses queue is ignored.
    /// Lets parallel planner tests answer by prompt content instead of call order.
    /// </summary>
    public Func<LlmProviderRequest, LlmProviderResponse>? Respond { get; set; }

    public Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
    {
        int index;
        lock (_lock)
            index = Requests.Count;

        // Invoked outside the lock so a test callback can block without serializing parallel callers
        OnSend?.Invoke(index);
        ct.ThrowIfCancellationRequested();

        // Snapshot the request: the gateway mutates MaxTokens between attempts
        var snapshot = new LlmProviderRequest
        {
            ModelId = request.ModelId,
            Effort = request.Effort,
            SchemaName = request.SchemaName,
            SchemaJson = request.SchemaJson,
            SystemPrompt = request.SystemPrompt,
            UserPrompt = request.UserPrompt,
            MaxTokens = request.MaxTokens,
            AllowBatch = request.AllowBatch
        };

        lock (_lock)
            Requests.Add(snapshot);

        if (ThrowOnSend != null)
            throw ThrowOnSend;

        var respond = Respond;
        if (respond != null)
            return Task.FromResult(respond(snapshot));

        LlmProviderResponse response;
        lock (_lock)
        {
            if (Responses.Count > 0)
                _last = Responses.Dequeue();

            if (_last == null)
                throw new InvalidOperationException("FakeLlmProvider has no scripted response.");

            response = _last;
        }

        return Task.FromResult(response);
    }

    public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(KeyTestResult);
    }

    public static LlmProviderResponse Ok(string text, int input = 10, int output = 5) =>
        new() { Outcome = LlmOutcome.Succeeded, Text = text, InputTokens = input, OutputTokens = output };

    public static LlmProviderResponse Truncated(int input = 10, int output = 5) =>
        new() { Outcome = LlmOutcome.Truncated, Text = "{\"name\":", InputTokens = input, OutputTokens = output };

    public static LlmProviderResponse Refused(string message) =>
        new() { Outcome = LlmOutcome.Refused, Message = message, InputTokens = 10, OutputTokens = 2 };

    public static LlmProviderResponse Failed(LlmFailureKind kind, string message = "error") =>
        new() { Outcome = LlmOutcome.Failed, FailureKind = kind, Message = message };
}
