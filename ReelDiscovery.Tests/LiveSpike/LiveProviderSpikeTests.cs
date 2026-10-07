using System.ClientModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using OpenAI;
using OpenAI.Chat;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;
using Xunit.Abstractions;

namespace ReelDiscovery.Tests.LiveSpike;

/// <summary>
/// 07-22 opt-in live provider spike (research Open Questions 1-2, assumptions A1/A2/A3/A9). Every test returns
/// immediately (pass, with an output note) unless REELDISCOVERY_LIVE_SPIKE=1, so default suites never call a provider.
/// When enabled, keys come from the DPAPI <see cref="ApiKeyStore"/> and are never written to output, files or logs;
/// only usage JSON, HTTP status, provider error messages and latencies go to <see cref="ITestOutputHelper"/>.
/// </summary>
[Trait("Category", "LiveSpike")]
public class LiveProviderSpikeTests
{
    private const string EnableVariable = "REELDISCOVERY_LIVE_SPIKE";
    private const string OpenAIModel = "gpt-6-luna";
    private const string AnthropicStoryModel = "claude-sonnet-5-5";
    private const int SmallMaxTokens = 256;

    // Tiny strict schema for the OpenAI cache probes and the batch round trips (output cost stays negligible)
    private const string TinySchemaJson =
        "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"}},\"required\":[\"answer\"],\"additionalProperties\":false}";

    private static readonly TimeSpan BatchPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BatchMaxWait = TimeSpan.FromMinutes(90);

    private readonly ITestOutputHelper _output;

    public LiveProviderSpikeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static bool IsEnabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    private bool SkipUnlessEnabled()
    {
        if (IsEnabled)
            return false;

        _output.WriteLine($"Live spike skipped: set {EnableVariable}=1 to call real providers (spends a few cents).");
        return true;
    }

    private static string RequireKey(LlmProviderKind provider)
    {
        // Key value is returned to the caller only; it is never logged or placed in an assertion message
        var key = new ApiKeyStore().GetKey(provider);
        if (string.IsNullOrWhiteSpace(key))
            Assert.Fail($"No {provider} key in the DPAPI key store; enter and test it on the API Configuration step first.");
        return key!;
    }

    // ----- (a) OpenAI implicit caching on Chat Completions -----

    [Fact]
    public async Task OpenAI_ImplicitCaching_ReportsUsage()
    {
        if (SkipUnlessEnabled())
            return;

        var client = new ChatClient(OpenAIModel, new ApiKeyCredential(RequireKey(LlmProviderKind.OpenAI)));
        var system = BuildFixedSystemPrompt();
        _output.WriteLine($"(a) OpenAI {OpenAIModel} implicit mode; fixed system prompt {system.Length} chars");

        var users = new[]
        {
            "Reply with the word alpha.",
            "Reply with the word alpha.",
            "Reply with the word bravo.",
            "Reply with the word charlie."
        };

        for (var i = 0; i < users.Length; i++)
        {
            var request = CreateTinyRequest(OpenAIModel, system, users[i]);
            await SendOpenAIAndLogAsync(client, request, OpenAITextProvider.BuildOptions(request), $"implicit call {i + 1}");
        }
    }

    // ----- (b) OpenAI explicit mode through ChatCompletionOptions.Patch -----

    [Fact]
    public async Task OpenAI_ExplicitCacheMode_ReportsStatusAndUsage()
    {
        if (SkipUnlessEnabled())
            return;

        var client = new ChatClient(OpenAIModel, new ApiKeyCredential(RequireKey(LlmProviderKind.OpenAI)));
        var system = BuildFixedSystemPrompt();
        _output.WriteLine($"(b) OpenAI {OpenAIModel} prompt_cache_options {{\"mode\":\"explicit\"}} via Patch");

        var users = new[]
        {
            "Reply with the word delta.",
            "Reply with the word delta.",
            "Reply with the word echo."
        };

        for (var i = 0; i < users.Length; i++)
        {
            var request = CreateTinyRequest(OpenAIModel, system, users[i]);
            var options = OpenAITextProvider.BuildOptions(request);
#pragma warning disable SCME0001 // JsonPatch is experimental in System.ClientModel; spike-only use
            options.Patch.Set("$.prompt_cache_options"u8, BinaryData.FromString("{\"mode\":\"explicit\"}"));
#pragma warning restore SCME0001
            await SendOpenAIAndLogAsync(client, request, options, $"explicit call {i + 1}");
        }
    }

    // ----- (c) Anthropic Story-tier evidence prefix (A3) -----

    [Fact]
    public async Task Anthropic_EvidencePrefix_ReportsCacheTokens()
    {
        if (SkipUnlessEnabled())
            return;

        var client = new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = RequireKey(LlmProviderKind.Anthropic), Timeout = TimeSpan.FromMinutes(5) });
        var schemaType = typeof(ThreadRenderer.ThreadRenderDto);
        var schemaJson = JsonSchemaRegistry.GetSchemaJson(schemaType);
        _output.WriteLine($"(c) Anthropic {AnthropicStoryModel}: evidence system prompt {EvidencePrompts.SystemPrompt.Length} chars + ThreadRenderDto schema {schemaJson.Length} chars");

        for (var i = 1; i <= 2; i++)
        {
            // Same build path as production (system block with CacheControl, strict output_config)
            var request = new LlmProviderRequest
            {
                ModelId = AnthropicStoryModel,
                SchemaName = JsonSchemaRegistry.GetSchemaName(schemaType),
                SchemaJson = schemaJson,
                SystemPrompt = EvidencePrompts.SystemPrompt,
                UserPrompt = "Spike probe: write a thread with one message (seq 1) from Pat to Lee confirming Tuesday's site walk. No new details.",
                MaxTokens = 512
            };

            var sw = Stopwatch.StartNew();
            try
            {
                var message = await client.Messages.Create(AnthropicTextProvider.BuildParams(request));
                sw.Stop();
                var u = message.Usage;
                _output.WriteLine($"  call {i}: {sw.ElapsedMilliseconds} ms stop={message.StopReason?.Raw()} input_tokens={u.InputTokens} " +
                                  $"cache_creation_input_tokens={u.CacheCreationInputTokens ?? 0} cache_read_input_tokens={u.CacheReadInputTokens ?? 0} " +
                                  $"output_tokens={u.OutputTokens}");
                _output.WriteLine($"  total input (input + creation + read) = {u.InputTokens + (u.CacheCreationInputTokens ?? 0) + (u.CacheReadInputTokens ?? 0)}");
            }
            catch (Anthropic.Exceptions.AnthropicApiException ex)
            {
                var kind = AnthropicTextProvider.MapException(ex);
                _output.WriteLine($"  call {i}: FAILED {AnthropicTextProvider.SafeMessage(kind, ex)}");
            }
        }
    }

    // ----- (d) one 2-request batch per provider through the real transports (A9 / OQ2 latency) -----

    [Fact]
    public async Task OpenAI_Batch_RoundTrip()
    {
        if (SkipUnlessEnabled())
            return;

        var transport = new OpenAIBatchTransport(RequireKey(LlmProviderKind.OpenAI));
        await RunBatchRoundTripAsync(transport, OpenAIModel);
    }

    [Fact]
    public async Task Anthropic_Batch_RoundTrip()
    {
        if (SkipUnlessEnabled())
            return;

        var transport = new AnthropicBatchTransport(RequireKey(LlmProviderKind.Anthropic));
        await RunBatchRoundTripAsync(transport, AnthropicStoryModel);
    }

    private async Task RunBatchRoundTripAsync(IBatchTransport transport, string modelId)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var items = new List<BatchItemRequest>
        {
            new() { CustomId = $"spike-{stamp}-1", Request = CreateTinyRequest(modelId, "You answer in one word.", "Reply with the word one.") },
            new() { CustomId = $"spike-{stamp}-2", Request = CreateTinyRequest(modelId, "You answer in one word.", "Reply with the word two.") }
        };
        foreach (var item in items)
            item.Request.AllowBatch = true;

        var total = Stopwatch.StartNew();
        var submit = await transport.SubmitAsync(items);
        _output.WriteLine($"(d) {transport.Kind} batch submit at {DateTime.UtcNow:O}: succeeded={submit.Succeeded} id={submit.BatchId} message={submit.Message}");
        Assert.True(submit.Succeeded, $"Batch submit failed: {submit.FailureKind} {submit.Message}");

        BatchState? last = null;
        BatchStatusResult status;
        while (true)
        {
            status = await transport.GetStatusAsync(submit.BatchId);
            if (status.State != last || !status.Succeeded)
            {
                _output.WriteLine($"  +{total.Elapsed:hh\\:mm\\:ss} state={status.State} done={status.Done}/{status.Total} failed={status.Failed} ok={status.Succeeded} {status.Message}");
                last = status.State;
            }

            if (status.Succeeded && status.State != BatchState.InProgress)
                break;

            if (total.Elapsed > BatchMaxWait)
            {
                _output.WriteLine($"  gave up after {BatchMaxWait}; cancelling batch");
                await transport.CancelAsync(submit.BatchId);
                Assert.Fail("Batch did not end within the spike wait limit");
            }

            await Task.Delay(BatchPollInterval);
        }

        var results = await transport.GetResultsAsync(submit.BatchId);
        total.Stop();
        _output.WriteLine($"  round trip {total.Elapsed:hh\\:mm\\:ss} (final state {status.State}); results ok={results.Succeeded} items={results.Items.Count} {results.Message}");
        foreach (var item in results.Items)
        {
            var r = item.Response;
            _output.WriteLine($"  {item.CustomId}: {item.Outcome} input={r?.InputTokens} cache_write={r?.CacheWriteTokens} cache_read={r?.CacheReadTokens} " +
                              $"output={r?.OutputTokens} batch={r?.IsBatch} text={r?.Text} {item.Message}");
        }

        await transport.DeleteFilesAsync(submit.InputFileId, status.OutputFileId);
        Assert.True(results.Succeeded, $"Batch results failed: {results.FailureKind} {results.Message}");
    }

    // ----- helpers -----

    private async Task SendOpenAIAndLogAsync(ChatClient client, LlmProviderRequest request, ChatCompletionOptions options, string label)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await client.CompleteChatAsync(OpenAITextProvider.BuildMessages(request), options);
            sw.Stop();
            var raw = result.GetRawResponse();
            _output.WriteLine($"  {label}: HTTP {raw.Status} {sw.ElapsedMilliseconds} ms usage={ExtractUsageJson(raw.Content)} " +
                              $"cache_write_tokens(parsed)={OpenAITextProvider.ReadCacheWriteTokens(raw.Content)}");
        }
        catch (ClientResultException ex)
        {
            sw.Stop();
            var body = OpenAITextProvider.ReadBody(ex);
            var kind = OpenAITextProvider.MapStatus(ex.Status, body);
            _output.WriteLine($"  {label}: HTTP {ex.Status} {sw.ElapsedMilliseconds} ms {OpenAITextProvider.SafeMessage(kind, ex.Status, body)}");
        }
    }

    private static string ExtractUsageJson(BinaryData body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body.ToMemory());
            return doc.RootElement.TryGetProperty("usage", out var usage) ? usage.GetRawText() : "(no usage)";
        }
        catch (JsonException)
        {
            // Spike logging only: an unparsable body is reported, never thrown
            return "(unparsable body)";
        }
    }

    private static LlmProviderRequest CreateTinyRequest(string modelId, string system, string user) => new()
    {
        ModelId = modelId,
        SchemaName = "SpikeAnswer",
        SchemaJson = TinySchemaJson,
        SystemPrompt = system,
        UserPrompt = user,
        MaxTokens = SmallMaxTokens
    };

    /// <summary>
    /// Deterministic ~1,500-token system prompt: byte-identical on every call so the cache prefix can match.
    /// </summary>
    private static string BuildFixedSystemPrompt()
    {
        var sb = new StringBuilder("You are a review assistant for a fictional construction dispute training set. Follow every rule below.\n");
        for (var i = 1; i <= 40; i++)
        {
            sb.Append("Rule ").Append(i).Append(": When a document mentions schedule item ").Append(i)
              .Append(", treat it as routine project correspondence unless it discusses window installation defects, ")
              .Append("change orders, or payment disputes between the subcontractor, the general contractor and the developer.\n");
        }

        sb.Append("Answer only with the JSON object the schema requires.");
        return sb.ToString();
    }
}
