using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json.Nodes;
using OpenAI;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Ai.OpenAI;
using ReelDiscovery.Tests.Fakes;
using RoutingHandler = ReelDiscovery.Tests.Ai.AnthropicBatchWireTests.RoutingHandler;

namespace ReelDiscovery.Tests.Ai;

public class OpenAIBatchWireTests
{
    private const string ApiKey = "sk-secret-openai-key-456";

    private const string FileJson =
        "{\"id\":\"file-in\",\"object\":\"file\",\"bytes\":10,\"created_at\":1,\"filename\":\"reeldiscovery-batch.jsonl\"," +
        "\"purpose\":\"batch\",\"status\":\"processed\"}";

    private const string CachedCompletion =
        "{\"id\":\"c1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-6-luna\",\"choices\":[{\"index\":0," +
        "\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"a\\\":1}\",\"refusal\":null},\"finish_reason\":\"stop\"}]," +
        "\"usage\":{\"prompt_tokens\":1000,\"completion_tokens\":5,\"total_tokens\":1005," +
        "\"prompt_tokens_details\":{\"cached_tokens\":400,\"cache_write_tokens\":300}}}";

    private static string BatchJson(string status, string? outputFileId = null, string? errorFileId = null) =>
        "{\"id\":\"batch_1\",\"object\":\"batch\",\"endpoint\":\"/v1/chat/completions\",\"status\":\"" + status + "\"," +
        "\"input_file_id\":\"file-in\",\"completion_window\":\"24h\",\"created_at\":1," +
        "\"output_file_id\":" + (outputFileId == null ? "null" : "\"" + outputFileId + "\"") + "," +
        "\"error_file_id\":" + (errorFileId == null ? "null" : "\"" + errorFileId + "\"") + "," +
        "\"request_counts\":{\"total\":5,\"completed\":3,\"failed\":1}}";

    private static OpenAIBatchTransport CreateTransport(RoutingHandler handler) =>
        new(ApiKey, new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            RetryPolicy = new ClientRetryPolicy(0)
        });

    private static async Task<string> LiveBodyAsync(LlmProviderRequest request)
    {
        var handler = new FakeHttpHandler { Body = OpenAIWireTests.SuccessBody };
        await OpenAIWireTests.CreateProvider(handler).SendAsync(request);
        return handler.LastRequestBody!;
    }

    private static LlmProviderRequest Request(string user, string? effort = null)
    {
        var r = OpenAIWireTests.CreateRequest(effort);
        r.UserPrompt = user;
        return r;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    [InlineData("high")]
    public async Task SerializeBody_IsByteIdenticalToLiveBody(string? effort)
    {
        var request = Request("Produce a \"quoted\" ünïcode value.", effort);

        var batch = OpenAIBatchTransport.SerializeBody(request).ToArray();
        var live = Encoding.UTF8.GetBytes(await LiveBodyAsync(request));

        Assert.Equal(live, batch);
    }

    [Fact]
    public async Task Submit_UploadsJsonlLinesAndCreates24hBatch()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/files", 200, FileJson);
        handler.Route("/v1/batches", 200, BatchJson("validating"));
        var r1 = Request("first");
        var r2 = Request("second", effort: "low");

        var result = await CreateTransport(handler).SubmitAsync(new[]
        {
            new BatchItemRequest { CustomId = "qaaa", Request = r1 },
            new BatchItemRequest { CustomId = "qbbb", Request = r2 }
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("batch_1", result.BatchId);
        Assert.Equal("file-in", result.InputFileId);
        Assert.Equal(new[] { "POST /v1/files", "POST /v1/batches" }, handler.Paths);

        // Multipart upload: the JSONL lines are the parts' lines that start with {"custom_id"
        var upload = handler.Bodies[0];
        Assert.Contains("name=purpose", upload);
        Assert.Contains("batch", upload);
        var lines = upload.Split('\n').Where(l => l.StartsWith("{\"custom_id\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);

        var live = new[] { await LiveBodyAsync(r1), await LiveBodyAsync(r2) };
        var ids = new[] { "qaaa", "qbbb" };
        for (var i = 0; i < 2; i++)
        {
            var line = JsonNode.Parse(lines[i])!;
            Assert.Equal(ids[i], line["custom_id"]!.GetValue<string>());
            Assert.Equal("POST", line["method"]!.GetValue<string>());
            Assert.Equal("/v1/chat/completions", line["url"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(live[i]), line["body"]));
            // Raw bytes of the body are embedded verbatim
            Assert.Contains("\"body\":" + live[i] + "}", lines[i]);
        }

        var create = JsonNode.Parse(handler.Bodies[1])!;
        Assert.Equal("file-in", create["input_file_id"]!.GetValue<string>());
        Assert.Equal("/v1/chat/completions", create["endpoint"]!.GetValue<string>());
        Assert.Equal("24h", create["completion_window"]!.GetValue<string>());
        Assert.DoesNotContain(ApiKey, string.Join("", handler.Bodies));
    }

    [Theory]
    [InlineData("validating", BatchState.InProgress)]
    [InlineData("in_progress", BatchState.InProgress)]
    [InlineData("finalizing", BatchState.InProgress)]
    [InlineData("cancelling", BatchState.InProgress)]
    [InlineData("completed", BatchState.Ended)]
    [InlineData("failed", BatchState.Failed)]
    [InlineData("expired", BatchState.Expired)]
    [InlineData("cancelled", BatchState.Cancelled)]
    public async Task Status_MapsProviderStatusCountsAndFiles(string status, BatchState expected)
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/batches/batch_1", 200, BatchJson(status, "file-out", "file-err"));

        var result = await CreateTransport(handler).GetStatusAsync("batch_1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(expected, result.State);
        Assert.Equal(5, result.Total);
        Assert.Equal(4, result.Done);
        Assert.Equal(1, result.Failed);
        Assert.Equal("file-out", result.OutputFileId);
        Assert.Equal("file-err", result.ErrorFileId);
    }

    [Fact]
    public async Task Results_ClassifiedWithLiveClassifier_ErrorsAndGarbageHandled()
    {
        var output = string.Join("\n", new[]
        {
            "{\"id\":\"r1\",\"custom_id\":\"qok\",\"response\":{\"status_code\":200,\"request_id\":\"x\",\"body\":" + CachedCompletion + "},\"error\":null}",
            "{\"id\":\"r2\",\"custom_id\":\"q400\",\"response\":{\"status_code\":400,\"body\":{\"error\":{\"message\":\"bad schema\",\"type\":\"invalid_request_error\"}}},\"error\":null}",
            "this is not json {",
            "{\"custom_id\":\"qbad\",\"response\":{\"status_code\":200,\"body\":\"not an object\"}}",
            ""
        });
        var errors = "{\"id\":\"r3\",\"custom_id\":\"qerr\",\"response\":null,\"error\":{\"code\":\"x\",\"message\":\"line failed\"}}\n";

        var handler = new RoutingHandler();
        handler.Route("/v1/batches/batch_1", 200, BatchJson("expired", "file-out", "file-err"));
        handler.Route("/v1/files/file-out/content", 200, output, "application/octet-stream");
        handler.Route("/v1/files/file-err/content", 200, errors, "application/octet-stream");

        var result = await CreateTransport(handler).GetResultsAsync("batch_1");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(new[] { "qok", "q400", "qerr" }, result.Items.Select(i => i.CustomId));

        var liveHandler = new FakeHttpHandler { Body = CachedCompletion };
        var live = await OpenAIWireTests.CreateProvider(liveHandler).SendAsync(OpenAIWireTests.CreateRequest());

        var ok = result.Items[0];
        Assert.Equal(BatchItemOutcome.Succeeded, ok.Outcome);
        Assert.True(ok.Response!.IsBatch);
        Assert.Equal(live.Outcome, ok.Response.Outcome);
        Assert.Equal(live.Text, ok.Response.Text);
        Assert.Equal(300, ok.Response.CacheWriteTokens);
        Assert.Equal(400, ok.Response.CacheReadTokens);
        Assert.Equal(300, ok.Response.InputTokens);
        Assert.Equal(live.InputTokens, ok.Response.InputTokens);
        Assert.Equal(live.CacheWriteTokens, ok.Response.CacheWriteTokens);
        Assert.Equal(live.OutputTokens, ok.Response.OutputTokens);

        Assert.Equal(BatchItemOutcome.Errored, result.Items[1].Outcome);
        Assert.Contains("bad schema", result.Items[1].Message);
        Assert.Null(result.Items[1].Response);
        Assert.Equal(BatchItemOutcome.Errored, result.Items[2].Outcome);
        Assert.Contains("line failed", result.Items[2].Message);
    }

    [Fact]
    public async Task Results_NoFiles_ReturnsEmpty()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/batches/batch_1", 200, BatchJson("failed"));

        var result = await CreateTransport(handler).GetResultsAsync("batch_1");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Submit_InsufficientQuota_IsSpendLimitWithoutKey()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/files", 429,
            "{\"error\":{\"message\":\"You exceeded your current quota\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}");

        var result = await CreateTransport(handler).SubmitAsync(new[]
        {
            new BatchItemRequest { CustomId = "qaaa", Request = Request("p") }
        });

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.SpendLimit, result.FailureKind);
        Assert.Equal(LlmFailureKind.SpendLimit, OpenAITextProvider.MapStatus(429, "insufficient_quota"));
        Assert.Contains("current quota", result.Message);
        Assert.DoesNotContain(ApiKey, result.Message);
    }

    [Fact]
    public async Task Status_401_IsAuth()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/batches/batch_1", 401, "{\"error\":{\"message\":\"Incorrect API key provided\"}}");

        var result = await CreateTransport(handler).GetStatusAsync("batch_1");

        Assert.False(result.Succeeded);
        Assert.Equal(LlmFailureKind.Auth, result.FailureKind);
        Assert.DoesNotContain(ApiKey, result.Message);
    }

    [Fact]
    public async Task Cancel_UnknownBatch_DoesNotThrow()
    {
        var handler = new RoutingHandler();

        await CreateTransport(handler).CancelAsync("batch_missing");

        Assert.NotEmpty(handler.Paths);
    }

    [Fact]
    public async Task Cancel_PostsCancel()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/batches/batch_1", 200, BatchJson("in_progress"));
        handler.Route("/v1/batches/batch_1/cancel", 200, BatchJson("cancelling"));

        await CreateTransport(handler).CancelAsync("batch_1");

        Assert.Contains("POST /v1/batches/batch_1/cancel", handler.Paths);
    }

    [Fact]
    public async Task DeleteFiles_BestEffort()
    {
        var handler = new RoutingHandler();
        handler.Route("/v1/files/file-out", 200, "{\"id\":\"file-out\",\"object\":\"file\",\"deleted\":true}");

        // file-in has no route (404) and must not stop file-out being deleted
        await CreateTransport(handler).DeleteFilesAsync("file-in", "file-out");

        Assert.Equal(new[] { "DELETE /v1/files/file-in", "DELETE /v1/files/file-out" }, handler.Paths);
    }

    [Fact]
    public void Factory_CreatesOpenAITransport()
    {
        var transport = LlmProviderFactory.CreateBatchTransport(LlmProviderKind.OpenAI, "k");
        Assert.IsType<OpenAIBatchTransport>(transport);
        Assert.Equal(10_000, transport.MaxRequestsPerBatch);
        Assert.Equal(150L * 1024 * 1024, transport.MaxBatchBytes);
        Assert.Equal(2_000_000, transport.MaxEstimatedInputTokens);
    }
}
