using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Ai.Anthropic;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Ai;

public class AnthropicBatchWireTests
{
    private const string ApiKey = "sk-ant-secret-key-123";

    private const string CachedMessage =
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\"," +
        "\"content\":[{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"sig\"},{\"type\":\"text\",\"text\":\"{\\\"a\\\":1}\"}]," +
        "\"stop_reason\":\"end_turn\",\"stop_sequence\":null," +
        "\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":50,\"output_tokens\":5}}";

    private static string BatchJson(string status, int processing = 1, int succeeded = 2, int errored = 1) =>
        "{\"id\":\"msgbatch_1\",\"type\":\"message_batch\",\"processing_status\":\"" + status + "\"," +
        "\"request_counts\":{\"processing\":" + processing + ",\"succeeded\":" + succeeded + ",\"errored\":" + errored +
        ",\"canceled\":0,\"expired\":0}," +
        "\"created_at\":\"2026-01-01T00:00:00Z\",\"expires_at\":\"2026-01-02T00:00:00Z\",\"ended_at\":null," +
        "\"archived_at\":null,\"cancel_initiated_at\":null,\"results_url\":null}";

    private static AnthropicBatchTransport CreateTransport(RoutingHandler handler) =>
        new(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = ApiKey,
            HttpClient = new HttpClient(handler),
            MaxRetries = 0
        });

    private static LlmProviderRequest Request(string user) =>
        new()
        {
            ModelId = "claude-opus-5-5",
            Effort = "high",
            SchemaName = AnthropicWireTests.CreateRequest().SchemaName,
            SchemaJson = AnthropicWireTests.CreateRequest().SchemaJson,
            SystemPrompt = "You are a cached system prompt.",
            UserPrompt = user,
            MaxTokens = 50_000
        };

    private static async Task<JsonNode> LiveBodyAsync(LlmProviderRequest request)
    {
        var handler = new FakeHttpHandler { Body = AnthropicWireTests.SuccessBody };
        await AnthropicWireTests.CreateProvider(handler).SendAsync(request);
        return JsonNode.Parse(handler.LastRequestBody!)!;
    }

    [Fact]
    public async Task Submit_ParamsEqualLiveBody_IncludingCacheControl()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/messages/batches", 200, BatchJson("in_progress"));
        var transport = CreateTransport(handler);
        var r1 = Request("first prompt");
        var r2 = Request("second prompt");

        var result = await transport.SubmitAsync(new[]
        {
            new BatchItemRequest { CustomId = "qaaa", Request = r1 },
            new BatchItemRequest { CustomId = "qbbb", Request = r2 }
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("msgbatch_1", result.BatchId);
        Assert.Equal(string.Empty, result.InputFileId);

        var body = JsonNode.Parse(handler.Bodies.Single())!;
        var requests = body["requests"]!.AsArray();
        Assert.Equal(2, requests.Count);
        Assert.Equal("qaaa", requests[0]!["custom_id"]!.GetValue<string>());
        Assert.Equal("qbbb", requests[1]!["custom_id"]!.GetValue<string>());

        var live1 = await LiveBodyAsync(r1);
        var live2 = await LiveBodyAsync(r2);
        Assert.True(JsonNode.DeepEquals(live1, requests[0]!["params"]), requests[0]!["params"]!.ToJsonString() + "\n" + live1.ToJsonString());
        Assert.True(JsonNode.DeepEquals(live2, requests[1]!["params"]));
        Assert.Equal("ephemeral", requests[0]!["params"]!["system"]![0]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal(AnthropicTextProvider.MaxNonStreamingTokens, requests[0]!["params"]!["max_tokens"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("in_progress", BatchState.InProgress)]
    [InlineData("canceling", BatchState.InProgress)]
    [InlineData("ended", BatchState.Ended)]
    public async Task Status_MapsProcessingStatusAndCounts(string status, BatchState expected)
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/messages/batches/msgbatch_1", 200, BatchJson(status, processing: 1, succeeded: 2, errored: 1));

        var result = await CreateTransport(handler).GetStatusAsync("msgbatch_1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(expected, result.State);
        Assert.Equal(4, result.Total);
        Assert.Equal(3, result.Done);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task Results_ClassifiedWithLiveClassifier()
    {
        var lines = new[]
        {
            "{\"custom_id\":\"qok\",\"result\":{\"type\":\"succeeded\",\"message\":" + CachedMessage + "}}",
            "{\"custom_id\":\"qerr\",\"result\":{\"type\":\"errored\",\"error\":{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"bad\"}}}}",
            "{\"custom_id\":\"qexp\",\"result\":{\"type\":\"expired\"}}",
            "{\"custom_id\":\"qcan\",\"result\":{\"type\":\"canceled\"}}"
        };
        var handler = new RoutingHandler();
        handler.Route("/v1/messages/batches/msgbatch_1/results", 200, string.Join("\n", lines) + "\n", "application/x-jsonl");
        handler.Route("/v1/messages/batches/msgbatch_1", 200, BatchJson("ended", processing: 0));

        var result = await CreateTransport(handler).GetResultsAsync("msgbatch_1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(4, result.Items.Count);

        // The same message through the live provider gives the expected classification
        var liveHandler = new FakeHttpHandler { Body = CachedMessage };
        var live = await AnthropicWireTests.CreateProvider(liveHandler).SendAsync(AnthropicWireTests.CreateRequest());

        var ok = result.Items.Single(i => i.CustomId == "qok");
        Assert.Equal(BatchItemOutcome.Succeeded, ok.Outcome);
        Assert.NotNull(ok.Response);
        Assert.True(ok.Response!.IsBatch);
        Assert.False(live.IsBatch);
        Assert.Equal(live.Outcome, ok.Response.Outcome);
        Assert.Equal(live.Text, ok.Response.Text);
        Assert.Equal(live.InputTokens, ok.Response.InputTokens);
        Assert.Equal(100, ok.Response.CacheWriteTokens);
        Assert.Equal(50, ok.Response.CacheReadTokens);
        Assert.Equal(live.OutputTokens, ok.Response.OutputTokens);

        Assert.Equal(BatchItemOutcome.Errored, result.Items.Single(i => i.CustomId == "qerr").Outcome);
        Assert.Equal(BatchItemOutcome.Expired, result.Items.Single(i => i.CustomId == "qexp").Outcome);
        Assert.Equal(BatchItemOutcome.Canceled, result.Items.Single(i => i.CustomId == "qcan").Outcome);
        Assert.All(result.Items.Where(i => i.Outcome != BatchItemOutcome.Succeeded), i => Assert.Null(i.Response));
    }

    [Fact]
    public async Task Submit_401_IsAuthFailureWithoutKey()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/messages/batches", 401,
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");

        var result = await CreateTransport(handler).SubmitAsync(new[]
        {
            new BatchItemRequest { CustomId = "qaaa", Request = Request("p") }
        });

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Auth, result.FailureKind);
        Assert.DoesNotContain(ApiKey, result.Message);
        Assert.Contains("invalid x-api-key", result.Message);
    }

    [Fact]
    public async Task Cancel_404_DoesNotThrow()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/messages/batches/msgbatch_x/cancel", 404,
            "{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\",\"message\":\"not found\"}}");

        await CreateTransport(handler).CancelAsync("msgbatch_x");

        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task Status_NetworkFailure_IsClassifiedNotThrown()
    {
        var handler = new RoutingHandler { ThrowOnSend = new HttpRequestException("boom") };

        var result = await CreateTransport(handler).GetStatusAsync("msgbatch_1");

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Network, result.FailureKind);
    }

    [Fact]
    public async Task DeleteFiles_IsNoOp()
    {
        var handler = new RoutingHandler();
        await CreateTransport(handler).DeleteFilesAsync("file_a", "file_b");
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public void Factory_CreatesAnthropicTransport()
    {
        var transport = LlmProviderFactory.CreateBatchTransport(LlmProviderKind.Anthropic, "k");
        Assert.IsType<AnthropicBatchTransport>(transport);
        Assert.Equal(10_000, transport.MaxRequestsPerBatch);
        Assert.Equal(150L * 1024 * 1024, transport.MaxBatchBytes);
        Assert.Null(transport.MaxEstimatedInputTokens);
    }

    /// <summary>
    /// Routes by exact request path (query ignored); unmatched paths return 404.
    /// </summary>
    internal sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Status, string Body, string ContentType)> _routes = new();

        public List<string> Paths { get; } = new();
        public List<string> Bodies { get; } = new();
        public Exception? ThrowOnSend { get; set; }

        public void Route(string path, int status, string body, string contentType = "application/json") =>
            _routes[path] = (status, body, contentType);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(request.Method + " " + path);
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(ct));

            if (ThrowOnSend != null)
                throw ThrowOnSend;

            var (status, body, contentType) = _routes.TryGetValue(path, out var route)
                ? route
                : (404, "{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\",\"message\":\"no route\"}}", "application/json");

            return new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };
        }
    }
}
