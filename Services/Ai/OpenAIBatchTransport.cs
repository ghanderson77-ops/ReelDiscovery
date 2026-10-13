using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Batch;
using OpenAI.Chat;
using OpenAI.Files;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// IBatchTransport over the OpenAI Batch API (/v1/chat/completions, 24h window). Each JSONL line body is serialized by
/// the SDK exactly as the live call and each output line is classified by the live
/// <see cref="OpenAITextProvider.Classify"/>, so batch and live stay identical (D-05).
/// </summary>
public class OpenAIBatchTransport : IBatchTransport
{
    private const string ChatEndpoint = "/v1/chat/completions";

    private const string CapturedCompletionJson =
        "{\"id\":\"capture\",\"object\":\"chat.completion\",\"created\":0,\"model\":\"capture\",\"choices\":[{\"index\":0," +
        "\"message\":{\"role\":\"assistant\",\"content\":\"{}\",\"refusal\":null},\"finish_reason\":\"stop\"}]," +
        "\"usage\":{\"prompt_tokens\":0,\"completion_tokens\":0,\"total_tokens\":0}}";

    private readonly OpenAIFileClient _files;

#pragma warning disable OPENAI001 // BatchClient is [Experimental] in OpenAI 2.14.0
    private readonly BatchClient _batches;
#pragma warning restore OPENAI001

    public OpenAIBatchTransport(string apiKey)
        : this(apiKey, OpenAITextProvider.CreateDefaultOptions())
    {
    }

    internal OpenAIBatchTransport(string apiKey, OpenAIClientOptions options)
    {
        var credential = new ApiKeyCredential(apiKey);
        _files = new OpenAIFileClient(credential, options);
#pragma warning disable OPENAI001 // BatchClient is [Experimental] in OpenAI 2.14.0
        _batches = new BatchClient(credential, options);
#pragma warning restore OPENAI001
    }

    public LlmProviderKind Kind => LlmProviderKind.OpenAI;

    public int MaxRequestsPerBatch => 10_000;

    public long MaxBatchBytes => 150L * 1024 * 1024;

    // Pitfall 6: Tier-1 enqueued-token limit for batch; the decorator splits submissions to stay under it
    public long? MaxEstimatedInputTokens => 2_000_000;

    /// <summary>
    /// Returns the exact Chat Completions request body the live provider sends for <paramref name="r"/>.
    /// </summary>
    internal static BinaryData SerializeBody(LlmProviderRequest r)
    {
        // Parity by construction: the SDK serializes the batch body exactly as the live call (D-05, Pitfall 5)
        var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ChatClient(r.ModelId, new ApiKeyCredential("capture"), new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            RetryPolicy = new ClientRetryPolicy(0)
        });

        client.CompleteChat(OpenAITextProvider.BuildMessages(r), OpenAITextProvider.BuildOptions(r));
        return BinaryData.FromBytes(handler.Captured ?? throw new InvalidOperationException("Chat request body was not captured"));
    }

    /// <summary>
    /// Builds the batch input file: one {"custom_id","method","url","body"} line per item.
    /// </summary>
    internal static byte[] BuildJsonl(IReadOnlyList<BatchItemRequest> items)
    {
        using var stream = new MemoryStream();
        foreach (var item in items)
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("custom_id", item.CustomId);
                writer.WriteString("method", "POST");
                writer.WriteString("url", ChatEndpoint);
                writer.WritePropertyName("body");
                writer.WriteRawValue(SerializeBody(item.Request).ToMemory().Span, skipInputValidation: true);
                writer.WriteEndObject();
            }

            stream.WriteByte((byte)'\n');
        }

        return stream.ToArray();
    }

    public async Task<BatchSubmitResult> SubmitAsync(IReadOnlyList<BatchItemRequest> items, CancellationToken ct = default)
    {
        string inputFileId = string.Empty;
        try
        {
            // T-07-29: the JSONL is built in memory and uploaded; it is never written to disk
            var jsonl = BuildJsonl(items);
            using var stream = new MemoryStream(jsonl, writable: false);
            var file = (await _files.UploadFileAsync(stream, "reeldiscovery-batch.jsonl", FileUploadPurpose.Batch, ct)).Value;
            inputFileId = file.Id;

#pragma warning disable OPENAI001 // BatchClient is [Experimental] in OpenAI 2.14.0
            var operation = await _batches.CreateBatchAsync(
                BinaryContent.Create(BinaryData.FromObjectAsJson(new
                {
                    input_file_id = file.Id,
                    endpoint = ChatEndpoint,
                    completion_window = "24h"
                })),
                waitUntilCompleted: false,
                new RequestOptions { CancellationToken = ct });
            var batchId = operation.BatchId;
#pragma warning restore OPENAI001

            return new BatchSubmitResult { Succeeded = true, BatchId = batchId, InputFileId = file.Id };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (Exception ex)
        {
            var (kind, message) = MapFailure(ex);
            // The input file id (if uploaded) lets the caller clean it up
            return new BatchSubmitResult { FailureKind = kind, Message = message, InputFileId = inputFileId };
        }
    }

    public async Task<BatchStatusResult> GetStatusAsync(string batchId, CancellationToken ct = default)
    {
        try
        {
            using var doc = await GetBatchJsonAsync(batchId, ct);
            return ParseStatus(doc.RootElement);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (Exception ex)
        {
            var (kind, message) = MapFailure(ex);
            return new BatchStatusResult { FailureKind = kind, Message = message, NotFound = IsNotFound(ex) };
        }
    }

    public async Task<BatchResultsResult> GetResultsAsync(string batchId, CancellationToken ct = default)
    {
        try
        {
            BatchStatusResult status;
            using (var doc = await GetBatchJsonAsync(batchId, ct))
            {
                status = ParseStatus(doc.RootElement);
            }

            var result = new BatchResultsResult { Succeeded = true };
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Expired/cancelled batches still carry partial output; ids in neither file stay absent (decorator marks them)
            foreach (var fileId in new[] { status.OutputFileId, status.ErrorFileId })
            {
                if (string.IsNullOrEmpty(fileId))
                    continue;

                var content = (await _files.DownloadFileAsync(fileId, ct)).Value;
                ParseResultLines(content.ToString(), result.Items, seen);
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (Exception ex)
        {
            var (kind, message) = MapFailure(ex);
            return new BatchResultsResult { FailureKind = kind, Message = message, NotFound = IsNotFound(ex) };
        }
    }

    public async Task CancelAsync(string batchId, CancellationToken ct = default)
    {
        try
        {
#pragma warning disable OPENAI001 // BatchClient is [Experimental] in OpenAI 2.14.0
            // 2.14.0 exposes cancel on the operation only: rehydrate it from the id, then cancel
            var operation = await CreateBatchOperation.RehydrateAsync(_batches, batchId, ct);
            // An ended batch (completed, failed, expired, cancelled) has nothing left to cancel
            if (operation.HasCompleted)
                return;

            await operation.CancelAsync(new RequestOptions { CancellationToken = ct });
#pragma warning restore OPENAI001
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancelled (user cancel or fatal run stop)
        }
        catch (ClientResultException ex) when (ex.Status is 404 or 409)
        {
            // An unknown or already-ended batch needs no action: it can no longer bill
        }
        catch (Exception ex)
        {
            // CR-B01: a cancel that did not take effect must reach Start Over, which then keeps the ledger
            var (kind, message) = MapFailure(ex);
            throw new BatchCancelFailedException(batchId, kind, message);
        }
    }

    public async Task DeleteFilesAsync(string? inputFileId, string? outputFileId, CancellationToken ct = default)
    {
        foreach (var fileId in new[] { inputFileId, outputFileId })
        {
            if (string.IsNullOrEmpty(fileId))
                continue;

            try
            {
                await _files.DeleteFileAsync(fileId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Caller cancelled (user cancel or fatal run stop)
            }
            catch (Exception)
            {
                // best-effort cleanup; files expire after 30 days
            }
        }
    }

    internal static BatchState MapStatus(string? status) => status switch
    {
        "completed" => BatchState.Ended,
        "failed" => BatchState.Failed,
        "expired" => BatchState.Expired,
        "cancelled" => BatchState.Cancelled,
        // validating, in_progress, finalizing, cancelling and anything newer stay pollable
        _ => BatchState.InProgress
    };

    private async Task<JsonDocument> GetBatchJsonAsync(string batchId, CancellationToken ct)
    {
#pragma warning disable OPENAI001 // BatchClient is [Experimental] in OpenAI 2.14.0
        var response = await _batches.GetBatchAsync(batchId, new RequestOptions { CancellationToken = ct });
#pragma warning restore OPENAI001
        return JsonDocument.Parse(response.GetRawResponse().Content.ToMemory());
    }

    private static BatchStatusResult ParseStatus(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new BatchStatusResult { FailureKind = LlmFailureKind.Unknown, Message = "Unexpected batch status response" };

        var status = GetString(root, "status");
        var result = new BatchStatusResult
        {
            Succeeded = true,
            State = MapStatus(status),
            OutputFileId = GetString(root, "output_file_id") ?? string.Empty,
            ErrorFileId = GetString(root, "error_file_id") ?? string.Empty
        };

        if (root.TryGetProperty("request_counts", out var counts) && counts.ValueKind == JsonValueKind.Object)
        {
            var completed = GetInt(counts, "completed");
            result.Failed = GetInt(counts, "failed");
            result.Total = GetInt(counts, "total");
            result.Done = completed + result.Failed;
        }

        if (result.State == BatchState.Failed)
            result.Message = "Batch failed: " + (FirstBatchError(root) ?? "no detail");

        return result;
    }

    private static string? FirstBatchError(JsonElement root)
    {
        if (root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Object
            && errors.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in data.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && GetString(entry, "message") is { } message)
                    return message;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses output/error file lines (T-07-30: untrusted). Lines that are not valid JSON objects with a custom_id are
    /// skipped; the first line per custom_id wins.
    /// </summary>
    internal static void ParseResultLines(string content, List<BatchItemResult> items, HashSet<string> seen)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var item = ParseResultLine(line);
            if (item != null && seen.Add(item.CustomId))
                items.Add(item);
        }
    }

    private static BatchItemResult? ParseResultLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || GetString(root, "custom_id") is not { Length: > 0 } customId)
                return null;

            if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
            {
                var statusCode = GetInt(response, "status_code");
                var hasBody = response.TryGetProperty("body", out var body);
                var bodyJson = hasBody ? body.GetRawText() : string.Empty;

                if (statusCode == (int)HttpStatusCode.OK)
                {
                    // A 200 without a completion object is malformed: skip it like a garbage line
                    if (!hasBody || body.ValueKind != JsonValueKind.Object)
                        return null;

                    var raw = BinaryData.FromString(bodyJson);
                    var completion = ModelReaderWriter.Read<ChatCompletion>(raw);
                    if (completion == null)
                        return null;

                    var classified = OpenAITextProvider.Classify(completion, raw);
                    classified.IsBatch = true;
                    return new BatchItemResult { CustomId = customId, Outcome = BatchItemOutcome.Succeeded, Response = classified };
                }

                var kind = OpenAITextProvider.MapStatus(statusCode, bodyJson);
                return new BatchItemResult
                {
                    CustomId = customId,
                    Outcome = BatchItemOutcome.Errored,
                    Message = OpenAITextProvider.SafeMessage(kind, statusCode, bodyJson)
                };
            }

            // Error-file line without a response: report only the provider's error.message
            var detail = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? GetString(error, "message")
                : null;
            return new BatchItemResult
            {
                CustomId = customId,
                Outcome = BatchItemOutcome.Errored,
                Message = detail != null ? "Batch request errored: " + detail : "Batch request errored"
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WR-A02: garbage, a torn line or a body the SDK cannot read (any exception type) in an untrusted provider
            // file skips only this line; the decorator re-runs missing ids live instead of failing the whole batch
            return null;
        }
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)
            ? Math.Max(0, n)
            : 0;

    // CR-01: a deleted batch, a key of another org or project, or output files past their retention window
    private static bool IsNotFound(Exception ex) => ex is ClientResultException { Status: 404 };

    private static (LlmFailureKind Kind, string Message) MapFailure(Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                // Caller token not cancelled: the SDK/HttpClient network timeout fired. Classify, never throw (CR-01)
                return (LlmFailureKind.Network, "Request to OpenAI timed out");
            case ClientResultException cre:
                var body = OpenAITextProvider.ReadBody(cre);
                var kind = OpenAITextProvider.MapStatus(cre.Status, body);
                return (kind, OpenAITextProvider.SafeMessage(kind, cre.Status, body));
            case HttpRequestException http:
                return (LlmFailureKind.Network, "Network error contacting OpenAI: " + http.Message);
            case JsonException:
                return (LlmFailureKind.Unknown, "Unexpected OpenAI batch response");
            default:
                // Unexpected SDK failure: report the type only so no payload text leaks
                return (LlmFailureKind.Unknown, $"OpenAI batch error ({ex.GetType().Name})");
        }
    }

    /// <summary>
    /// In-process handler that records the serialized chat request and answers with a canned completion. It never
    /// reaches the network (T-07-32).
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public byte[]? Captured { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                using var buffer = new MemoryStream();
                request.Content.CopyTo(buffer, null, cancellationToken);
                Captured = buffer.ToArray();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CapturedCompletionJson, Encoding.UTF8, "application/json")
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
