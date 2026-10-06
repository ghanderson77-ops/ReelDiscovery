# Phase 7: Scale, Cost, QA & Cutover - Pattern Map

**Mapped:** 2026-10-06
**Files analyzed:** 52 new or modified (plus the cutover deletion set)
**Analogs found:** 48 / 52

All paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery`. Line numbers are as of commit `4caa548`.

---

## File Classification

### AI layer: usage, batching, caching (PROV-07, RUN-03, RUN-04, RUN-05)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Services/Ai/TokenUsage.cs` (new) | model | transform | `Services/Ai/LlmProviderRequest.cs` (`LlmProviderResponse`) + `Models/MediaPrices.cs` | role-match |
| `Services/Ai/LlmProviderRequest.cs` (mod: `AllowBatch`, `Usage`, `Route`) | model | request-response | itself | exact |
| `Services/Ai/LlmRequest.cs` (mod: `AllowBatch`) | model | request-response | itself | exact |
| `Services/Ai/LlmResult.cs` (mod: `Usage`, `CostUsd`, `BatchSavingsUsd`) | model | request-response | itself | exact |
| `Services/Ai/LlmGateway.cs` (mod) | service | request-response | itself | exact |
| `Services/Ai/AnthropicTextProvider.cs` (mod: `cache_control`, usage) | service | request-response | itself | exact |
| `Services/Ai/OpenAITextProvider.cs` (mod: raw `cache_write_tokens`) | service | request-response | itself | exact |
| `Services/Ai/BatchingLlmProvider.cs` (new) | service (decorator) | batch / event-driven | `Services/Ai/ILlmProvider.cs` contract + `Tests/Fakes/FakeLlmProvider.cs` | role-match |
| `Services/Ai/IBatchTransport.cs` (new) | interface | batch | `Services/Ai/ILlmProvider.cs` | role-match |
| `Services/Ai/AnthropicBatchTransport.cs` (new) | service | batch | `Services/Ai/AnthropicTextProvider.cs` | role-match |
| `Services/Ai/OpenAIBatchTransport.cs` (new) | service | batch / file-I/O | `Services/Ai/OpenAITextProvider.cs` | role-match |
| `Services/Ai/BatchLedger.cs` (new) | store | file-I/O (append-only) | `Services/Evidence/GenerationJournal.cs` | exact |
| `Services/Ai/RequestIdentity.cs` (new) | utility | transform (hash) | `Services/Evidence/RenderAssignments.cs` `StableRank` | role-match |
| `Services/Ai/LlmProviderFactory.cs` (mod: wrap in batching decorator) | utility | factory | itself | exact |

### Cost and pricing (PROV-07, D-16..D-18)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Models/AIModelConfig.cs` (mod: 3 prices + hidden min prefix + `CalculateCost(TokenUsage)`) | model | transform | itself | exact |
| `Models/TokenUsageTracker.cs` (mod: breakdown) | model | accumulate | itself | exact |
| `Services/Corpus/GenerationCostEstimator.cs` (mod: live + batch totals from one `PriceWork`) | service | transform | itself | exact |
| `Services/Corpus/PlanCallRunner.cs` (mod: `PlanCallOutcome.CostUsd`) | service | request-response | itself | exact |
| `Services/Evidence/ThreadUnitOfWork.cs` line 755-756 (mod: cost from gateway) | service | request-response | itself | exact |
| `Services/Evidence/AttachmentUnitOfWork.cs` line 596-601 (mod) | service | request-response | itself | exact |
| `Services/Evidence/GenerationRunState.cs` line 507-515 (mod `EstimateRemaining`) | service | transform | `GenerationCostEstimator.PriceWork` | exact |
| `Services/Evidence/GenerationRunner.cs` (mod: `ModelCost`, batch parallelism, adaptive projection) | service | batch orchestration | itself | exact |

### Journal, reset, regenerate (D-05, D-13, D-15, D-25, IN-06)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Models/Evidence/JournalRecord.cs` (mod: `ThreadInvalidated` appended to enum; optional `CacheWriteTokens`, `CacheReadTokens`, `BatchSavingsUsd`) | model | event log | itself | exact |
| `Services/Evidence/GenerationJournal.cs` (mod: replay `ThreadInvalidated`, cost breakdown sums) | store | file-I/O | itself | exact |
| `Services/Evidence/ThreadInvalidation.cs` (new) | service | file-I/O + event | `MailboxWriter.DeleteCopies` + `GenerationJournal.AppendAsync` | role-match |
| `Services/Evidence/GenerationReset.cs` (mod: `TryDelete` retry, batch files, cancel batches) | service | file-I/O | `Services/Case/CaseStore.cs` `WriteAtomic` retry loop | exact |
| `Services/Evidence/GenerationSnapshot.cs` + `Models/Evidence/GenerationProgress.cs` (mod: batch wait, 5 new event kinds) | model | event-driven | themselves | exact |

### QA services (QA-01, QA-02, QA-03)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Services/Qa/BlindReviewer.cs` + `BlindReviewPrompts.cs` (new, DTO in `SchemaTypes`) | service (generator) | request-response, bounded fan-out | `Services/Evidence/CastVoiceBuilder.cs` + `PlanCallRunner` | exact |
| `Services/Qa/LeakageAudit.cs` (new) | service | transform (batch) | `Services/Corpus/AnswerKeyBuilder.cs` (input rows) + `StableRank` | partial |
| `Services/Qa/LogisticRegression.cs` (new) | utility | transform | none | no analog |
| `Services/Qa/CorpusScans.cs` (new) | service | transform | `Services/Evidence/RenderValidator.cs` `Check`/`CheckAttachment`/`CheckFiller` | exact (reuse) |
| `Services/Qa/QaReportStore.cs` (new) | store | file-I/O | `CaseStore.WriteAtomic` + `GenerationJournal.PathFor` | role-match |
| `Models/WizardState.cs` (mod: `CreateReviewerGateway`, `MediaSettings`, remove legacy fields) | model | factory | `WizardState.CreateGateway` lines 86-94 | exact |

### UI (UI-SPEC S1..S7)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `UserControls/GenerationUiText.cs` (mod) | utility (pure copy) | transform | itself | exact |
| `UserControls/QaUiText.cs` (new) | utility (pure copy) | transform | `UserControls/GenerationUiText.cs` | exact |
| `UserControls/StepGenerateCorpus.cs` + `.Run.cs` (mod: Ready card checkbox, batch wait) | component | event-driven | itself (lines 423-486, 976-1007; Run 977-1003) | exact |
| `UserControls/StepCaseSummary.cs` (mod: Generation cost row, Quality checks group) | component | request-response | itself (`CreateRow` 544-623, items 427-455) | exact |
| `Forms/QaFindingsDialog.cs` (new, sizable grid dialog) | component (dialog) | request-response | `UserControls/ModelConfigurationDialog.cs` (sizable + grid + clamp) + `Forms/ThemedDialog.cs` chrome | role-match |
| `Forms/BlindReviewDialog.cs` (new `ThemedDialog`) | component (dialog) | request-response | `Forms/RegenerateSelectedDialog.cs` | exact |
| `UserControls/ModelConfigurationDialog.cs` (mod: 3 columns, 1180 width, wrap headers, frozen cols) | component (dialog) | CRUD | itself | exact |
| `Forms/WizardForm.cs` (mod: single step list, ticker tooltip) | component (shell) | event-driven | itself | exact |
| `UserControls/StepApiConfiguration.cs` (mod: remove rows 13-14) | component | event-driven | itself | exact |

### Cutover and folded fixes (D-20..D-26)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Helpers/ThemeSanitizer.cs` (new; moved from `ThemeGenerator`) | utility | transform | `Services/ThemeGenerator.cs` lines 140-156, 199-226 | exact (move) |
| `Helpers/HtmlEmailFormatter.cs` (mod: D-26 cleaning in `BuildEmailTemplate`) | utility | transform | `Helpers/BulkMailFormatter.cs` `SanitizedTheme` lines 251-268 | exact |
| `Helpers/ThreadingHelper.cs`, `Helpers/FileNameHelper.cs` (mod: drop `EmailMessage`/`EmailThread` members) | utility | transform | themselves | exact |
| `Models/GenerationConfig.cs` -> `ImageModel`/`ImageQuality` moved (e.g. `Models/MediaSettings.cs`) | model | config | `Models/MediaPrices.cs` | role-match |
| `Program.cs`, `Services/ApiKeyStore.cs` (mod: drop `MigrateLegacyKey`) | config | startup | themselves | exact |
| `Services/Corpus/NonRelevantSkeletonBuilder.cs` (mod `AssignDailyPrivilege`, D-23) | service | transform | itself lines 206-234 | exact |
| `Services/Evidence/InviteScheduler.cs` (mod `Derive`/`ZoneOf`, D-24) | service | transform | itself lines 359-376 | exact |
| Deleted: `UserControls/Step{TopicInput,Storylines,Characters,GenerationConfig,Generation,Complete}.cs`, `Services/{EmailGenerator,StorylineGenerator,CharacterGenerator,ThemeGenerator,EmlFileService}.cs`, `Models/{WizardFlow,Storyline,Character,EmailThread,EmailMessage,GenerationResult}.cs`, `PresentationTheme` subclass, `Helpers/DateHelper.cs` | - | - | n/a | - |

### Tests

| New/Modified File | Role | Closest Analog | Match Quality |
|---|---|---|---|
| `ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs` | test fake | `ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs` | exact |
| `Tests/Ai/BatchingLlmProviderTests.cs`, `BatchParityTests.cs`, `TokenUsageTests.cs` | unit | `Tests/Ai/LlmGatewayTests.cs` | role-match |
| `Tests/Ai/AnthropicBatchWireTests.cs`, `OpenAIBatchWireTests.cs` | wire | `Tests/Ai/Anthropic/AnthropicWireTests.cs`, `Tests/Ai/OpenAI/OpenAIWireTests.cs` | exact |
| `Tests/Ai/BatchLedgerTests.cs` | unit | `Tests/Evidence/GenerationJournalTests.cs` | exact |
| `Tests/Qa/BlindReviewTests.cs`, `LeakageAuditTests.cs`, `CorpusScansTests.cs` | unit | `Tests/Evidence/CastVoiceTests.cs`, `Tests/Evidence/DistributionParityTests.cs`, `Tests/Evidence/RenderValidatorTests.cs` | role-match |
| `Tests/Evidence/ThreadInvalidationTests.cs` | unit/integration | `Tests/Evidence/GenerationJournalTests.cs`, `Tests/Evidence/AttachmentRepairTests.cs` | role-match |
| `Tests/Scale/FiveThousandRunTests.cs` | integration | `Tests/Evidence/GenerationRunnerTests.cs` + `Tests/Corpus/CorpusPlannerTests.cs` | role-match |
| `Tests/UserControls` copy tests: `QaUiTextTests.cs`; extend `Tests/Evidence/GenerationUiTextTests.cs` | unit | `Tests/Evidence/GenerationUiTextTests.cs` | exact |
| Render: extend `Tests/Render/DialogRenderTests.cs`, `GenerateCorpusRenderTests.cs`, `CaseSummaryRenderTests.cs`, `WizardShellRenderTests.cs` | render | themselves | exact |
| `Tests/Case/CaseSchemaTests.cs` (add `BlindReviewer.SchemaTypes`) | unit | itself lines 11-32 | exact |
| Rewrite/delete: `Tests/Generation/*`, `Tests/Concurrency/ConcurrencyTests.cs`, `Tests/RunSummaryTests.cs`, `Tests/Case/WizardFlowTests.cs`, `Tests/Case/CaseDraftTests.cs`, `Tests/ApiKeyStoreTests.cs` (lines 82-144), `Tests/Documents/DocumentRegressionTests.cs` | - | - | - |

---

## Pattern Assignments

### `Services/Ai/TokenUsage.cs` (model, transform) - new

**Analog:** `Services/Ai/LlmProviderRequest.cs` lines 19-31 (`LlmProviderResponse` carries the raw tokens today) and `Models/MediaPrices.cs` (small mutable class with `Clone`/`Normalize`).

Current response shape to extend (lines 19-31):
```csharp
public class LlmProviderResponse
{
    public LlmOutcome Outcome { get; set; }
    public LlmFailureKind FailureKind { get; set; }
    public string? Text { get; set; }
    public string? Message { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}
```
- Keep `InputTokens`/`OutputTokens` (tests and `FakeLlmProvider` use them) and add `TokenUsage Usage` or add `CacheWriteTokens`/`CacheReadTokens`/`IsBatch` beside them. Per CLAUDE.md: mutable class, auto-properties, no `record`, no primary constructor.
- `Normalize()` style clamp copied from `MediaPrices.Normalize` (`Math.Max(0, ...)`), same as `JournalRecord.Normalize` lines 142-146.

---

### `Services/Ai/LlmGateway.cs` (service, request-response) - modified

**Analog:** itself.

**Request build** (lines 64-73), where `AllowBatch` is set from the first attempt only:
```csharp
var providerRequest = new LlmProviderRequest
{
    ModelId = model.ModelId,
    Effort = model.Effort,
    SchemaName = JsonSchemaRegistry.GetSchemaName(typeof(T)),
    SchemaJson = JsonSchemaRegistry.GetSchemaJson(typeof(T)),
    SystemPrompt = request.SystemPrompt,
    UserPrompt = request.UserPrompt,
    MaxTokens = maxTokens
};
```
**Per-attempt billing** (lines 83-91) - replace token sums and `RecordUsage(op, model, in, out)` with a summed `TokenUsage` and one `model.CalculateCost(usage)`:
```csharp
attempts++;
ct.ThrowIfCancellationRequested();

var response = await _provider.SendAsync(providerRequest, ct);
inputTotal += Math.Max(0, response.InputTokens); outputTotal += Math.Max(0, response.OutputTokens);

// Every attempt is billed, including failed parses and truncations
if (response.InputTokens > 0 || response.OutputTokens > 0)
    _usageTracker?.RecordUsage(request.OperationName, model, response.InputTokens, response.OutputTokens);
```
- Set `providerRequest.AllowBatch = request.AllowBatch && attempts == 1` inside the loop before `SendAsync`, so invalid-JSON and truncation retries (lines 124-137) go live automatically (D-10).
- `BuildResult` (lines 149-175) gains `Usage`/`CostUsd`/`BatchSavingsUsd`; every call site passes the summed values.

---

### `Services/Ai/AnthropicTextProvider.cs` (service) - modified

**Analog:** itself.

**Shared params builder** (lines 220-240) - the batch transport MUST reuse this (Pitfall 5); add the block-level cache marker here only:
```csharp
internal static MessageCreateParams BuildParams(LlmProviderRequest r)
{
    var format = new JsonOutputFormat { Schema = GetSchemaDictionary(r.SchemaJson) };
    var effort = MapEffort(r.Effort);
    var outputConfig = effort != null
        ? new OutputConfig { Format = format, Effort = effort.Value }
        : new OutputConfig { Format = format };

    // Opus 4.7+/5.5 reject non-default sampling and thinking params (400): never set them here.
    return new MessageCreateParams
    {
        Model = r.ModelId,
        MaxTokens = Math.Min(r.MaxTokens, MaxNonStreamingTokens),
        System = new List<TextBlockParam> { new(r.SystemPrompt) },   // -> { CacheControl = new CacheControlEphemeral() }
        Messages = [new() { Role = Role.User, Content = r.UserPrompt }],
        OutputConfig = outputConfig
    };
}
```
- Never set top-level `MessageCreateParams.CacheControl` (RESEARCH anti-pattern). Extract the `OutputConfig` construction into a helper so `Batches.Params` uses the identical object.

**Usage normalization point** in `Classify` (lines 252-257) - Anthropic `input_tokens` is exclusive; add `m.Usage.CacheCreationInputTokens` / `CacheReadInputTokens` here:
```csharp
var response = new LlmProviderResponse
{
    Text = text.ToString(),
    InputTokens = (int)Math.Min(m.Usage.InputTokens, int.MaxValue),
    OutputTokens = (int)Math.Min(m.Usage.OutputTokens, int.MaxValue)
};
```
`Classify(AnthropicMessage)` is `internal static` and is the single classifier both live and batch call (D-05 parity).

---

### `Services/Ai/OpenAITextProvider.cs` (service) - modified

**Analog:** itself. Shared builders `BuildOptions` (lines 111-134) and `BuildMessages` (lines 136-140) must be reused to serialize batch JSONL `body` (wire-parity test). `Classify(ChatCompletion)` (lines 142-180) is the single classifier for live and batch output lines. `prompt_tokens` is inclusive; read `CachedTokenCount` from `c.Usage.InputTokenDetails` and `cache_write_tokens` from raw JSON (`ClientResult.GetRawResponse().Content`) at line 51:
```csharp
var chatClient = _chatClients.GetOrAdd(request.ModelId, id => _client.GetChatClient(id));
ChatCompletion completion = (await chatClient.CompleteChatAsync(BuildMessages(request), BuildOptions(request), ct)).Value;
return Classify(completion);
```
Experimental API suppression pattern (lines 126-130) - copy for `BatchClient`:
```csharp
#pragma warning disable OPENAI001
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(r.Effort);
#pragma warning restore OPENAI001
```

---

### `Services/Ai/BatchingLlmProvider.cs` (decorator, batch) - new

**Analog:** the `ILlmProvider` contract (`Services/Ai/ILlmProvider.cs` lines 3-24) and the thread-safe request queue in `ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs` lines 43-80.

Contract it must keep (never throws except caller cancellation):
```csharp
/// Sends one structured-output request. Never throws for API/HTTP failures (including transport timeouts):
/// they are mapped to Outcome = Failed with a FailureKind. Throws OperationCanceledException only when
/// <paramref name="ct"/> is cancelled.
Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default);
```
- `Kind`, `MaxTokensCeiling`, `TestKeyAsync` delegate to the inner live provider.
- Lock pattern for the pending queue: `private readonly object _lock = new();` as in `FakeLlmProvider` lines 11, 45-47, 65-66; invoke callbacks outside the lock (line 49 comment).
- Cancellation filter: copy `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }` from `AnthropicTextProvider` lines 62-65.
- Skeleton is RESEARCH.md Pattern 1 example (AllowBatch false -> `_live.SendAsync`; ledger attach; else enqueue; errored/expired -> live).
- Wire-up point: `Services/Ai/LlmProviderFactory.cs` lines 8-13 (wrap result when batch mode is on) or `WizardState.CreateGateway` lines 86-94.

---

### `Services/Ai/IBatchTransport.cs`, `AnthropicBatchTransport.cs`, `OpenAIBatchTransport.cs` (service, batch) - new

**Analog:** `Services/Ai/AnthropicTextProvider.cs` / `OpenAITextProvider.cs`.

**Constructor and test seam** (Anthropic lines 29-42) - copy so wire tests can inject `FakeHttpHandler`:
```csharp
public AnthropicTextProvider(string apiKey)
    : this(new Anthropic.Core.ClientOptions
    {
        ApiKey = apiKey,
        HttpClient = CreateHttpClient(new HttpClientHandler()),
        Timeout = TimeSpan.FromMinutes(10)
    })
{
}

internal AnthropicTextProvider(Anthropic.Core.ClientOptions options)
{
    _client = new AnthropicClient(options);
}
```
OpenAI equivalent: lines 20-29 plus `CreateDefaultOptions()` lines 39-43 (`QuotaAwareRetryPolicy`).

**Error mapping** - reuse `AnthropicTextProvider.MapException` (lines 129-164, `internal static`) and `OpenAITextProvider.MapStatus` (lines 185-207, `internal static`); reuse the `SafeMessage`/`ExtractErrorMessage` pattern (Anthropic lines 170-211) so provider error bodies are reduced to `error.message` and no header/key leaks (ASVS V7).

**Catch ladder** (Anthropic lines 57-83) - same order for submit/poll/results:
```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
catch (OperationCanceledException) { return Failed(LlmFailureKind.Network, "Request to Anthropic timed out"); }
catch (AnthropicApiException ex) { var kind = MapException(ex); return Failed(kind, SafeMessage(kind, ex)); }
catch (AnthropicIOException ex) { return Failed(LlmFailureKind.Network, "Network error contacting Anthropic: " + ex.Message); }
catch (HttpRequestException ex) { return Failed(LlmFailureKind.Network, "Network error contacting Anthropic: " + ex.Message); }
```
SDK call shapes for batches: RESEARCH.md "Code Examples" (Anthropic `client.Messages.Batches.Create/Retrieve/ResultsStreaming`; OpenAI `UploadFileAsync(..., FileUploadPurpose.Batch)`, `BatchClient.CreateBatchAsync`, `ModelReaderWriter.Read<ChatCompletion>`).

---

### `Services/Ai/BatchLedger.cs` (store, append-only file-I/O) - new

**Analog:** `Services/Evidence/GenerationJournal.cs` (exact technique).

**Path rule** (lines 108-112):
```csharp
// Case folder + constant name only, never a path from data (T-04-26)
public static string PathFor(string caseFolder) => Path.Combine(caseFolder, GenerationState.JournalFileName);

public static bool Exists(string caseFolder) =>
    !string.IsNullOrWhiteSpace(caseFolder) && File.Exists(PathFor(caseFolder));
```
Constant file name lives next to `GenerationState.JournalFileName` (`Models/Evidence/GenerationState.cs` line 20): add `provider-batches.jsonl`.

**Append + flush + torn-line repair** (lines 119-154):
```csharp
var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, LineOptions) + "\n");

await _gate.WaitAsync(ct).ConfigureAwait(false);
try
{
    await using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
    ...
    if (stream.Length > 0)
    {
        stream.Seek(-1, SeekOrigin.End);
        if (stream.ReadByte() != '\n')
            stream.WriteByte((byte)'\n');
    }

    stream.Seek(0, SeekOrigin.End);
    await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    stream.Flush(flushToDisk: true);
}
finally
{
    _gate.Release();
}
```
**Serializer options** (line 97): `new(CaseDraft.JsonOptions) { WriteIndented = false }`.
**Defensive replay** (lines 384-430 `ReadRecords`): skip blank, `JsonException`, `NotSupportedException`/`InvalidOperationException`/`ArgumentException`, null, undefined enum. Outer catch list (lines 265-266): `IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException`.
**Results file** `batch-results/{sanitizedBatchId}.jsonl`: write with `CaseStore.WriteAtomic` (`Services/Case/CaseStore.cs` lines 383-415); sanitize provider ids to `^[A-Za-z0-9_-]{1,64}$` else hash (RESEARCH Security).

---

### `Services/Ai/RequestIdentity.cs` (utility, hash) - new

**Analog:** `Services/Evidence/RenderAssignments.cs` lines 35-41:
```csharp
/// Stable 64-bit rank of an id: the first 8 bytes (big-endian) of its UTF-8 SHA-256.
public static ulong StableRank(string id)
{
    // Never string.GetHashCode(): randomized per process, would change on every Continue
    return BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(id ?? string.Empty)));
}
```
Use `SHA256.HashData` over the canonical request (model, effort, schema name and JSON, system, user, max tokens, separator-joined), then `"q" + Convert.ToHexString(hash)[..40].ToLowerInvariant()` for `custom_id` (Anthropic regex).

---

### `Models/AIModelConfig.cs` (model) - modified

**Analog:** itself. Price properties (lines 9-10) and cost helpers (lines 47-54):
```csharp
public decimal CalculateInputCost(int tokens) =>
    tokens * InputTokenPricePerMillion / 1_000_000m;

public decimal CalculateOutputCost(int tokens) =>
    tokens * OutputTokenPricePerMillion / 1_000_000m;

public decimal CalculateTotalCost(int inputTokens, int outputTokens) =>
    CalculateInputCost(inputTokens) + CalculateOutputCost(outputTokens);
```
- Add `CacheWriteTokenPricePerMillion`, `CacheReadTokenPricePerMillion`, `BatchDiscountPercent`, hidden `MinCacheablePrefixTokens`, and `CalculateCost(TokenUsage)` (RESEARCH Pattern 4 formula). Keep `CalculateTotalCost` for the estimator's per-message heuristics.
- Fill defaults in `GetDefaultModels()` (lines 62-130) from the RESEARCH Pattern 4 price table; each entry is an object initializer, add the new properties there.
- `ModelConfigurationDialog` clones models field by field (dialog lines 42-52); the clone must copy the new properties too.

---

### `Models/TokenUsageTracker.cs` (model, accumulate) - modified

**Analog:** itself. Lock pattern and entry shape (lines 23-44):
```csharp
public void RecordUsage(string operation, AIModelConfig model, int inputTokens, int outputTokens)
{
    var cost = model.CalculateTotalCost(inputTokens, outputTokens);

    lock (_lock)
    {
        TotalInputTokens += inputTokens;
        TotalOutputTokens += outputTokens;
        TotalCost += cost;
        _entries.Add(new TokenUsageEntry { ... Cost = cost });
    }
}
```
Add an overload taking `TokenUsage` + precomputed cost, and per-category totals (input $, cache write $, cache read $, output $, batch savings $, media $) under the same `_lock`; `RecordMediaUsage` (lines 49-66) feeds the media total. `WizardForm.UpdateCostDisplay` reads these for the S6 tooltip.

---

### `Services/Corpus/GenerationCostEstimator.cs` (service) - modified

**Analog:** itself. The single pricing path `PriceWork` (lines 168-246). Extend it, do not add a second function (D-17). Per-group pricing lines to parameterize (lines 222-228):
```csharp
RelevantCost = storyMessages * story.CalculateTotalCost(RelevantInputPerMessage, RelevantOutputPerMessage) * QaOverhead,
OtherCost = volumeMessages * volume.CalculateTotalCost(OtherInputPerMessage, OtherOutputPerMessage) * QaOverhead
...
var storyAttachments = storyDocuments * story.CalculateTotalCost(DocumentInput, DocumentOutput) * QaOverhead
                       + storySmall * story.CalculateTotalCost(SmallAttachmentInput, SmallAttachmentOutput) * QaOverhead;
```
- Route each through a private `Price(model, inTokens, outTokens, bool batch)` that applies the cache term only when an estimated prefix is at least `model.MinCacheablePrefixTokens` (Pitfall 7), and the batch discount when `batch`. Media stays live (`MediaCost` lines 234-240).
- Return both totals on `CostEstimate` (lines 11-38): add `BatchTotal`, `BatchStoryCost`, `BatchVolumeCost`.
- Header constants block (lines 82-109) style: `// [ASSUMED] heuristic` comments on every new constant.
- `GenerationRunState.EstimateRemaining` (`Services/Evidence/GenerationRunState.cs` lines 507-515) calls `CalculateTotalCost` directly; route it through `PriceWork` (anti-pattern in RESEARCH).

---

### `Services/Corpus/PlanCallRunner.cs` (service) - modified

**Analog:** itself. `PlanCallOutcome<T>` (lines 63-75) gains `CostUsd`/`Usage`; every return in `RunAsync` (lines 101-139) copies them from `result` / `ex.Result`:
```csharp
var result = await llm.GenerateAsync<T>(request, run.Cts.Token);
var value = result.GetValueOrThrow();
return new PlanCallOutcome<T> { Value = value, InputTokens = result.InputTokens, OutputTokens = result.OutputTokens };
```
This is also the bounded fan-out the blind reviewer uses (see below).

---

### `Services/Evidence/GenerationRunner.cs` (service, orchestration) - modified

**Analog:** itself.

**Knobs** (lines 61-73) - batch mode lifts unit parallelism only; keep `MaxConcurrentRepairs`:
```csharp
public const int MaxParallelThreads = 3;
public const int FillerChunkSize = 50;
public const int MaxConcurrentRepairs = 4;
public static readonly TimeSpan KeyProjectionInterval = TimeSpan.FromSeconds(2);
```
Constructor (lines 81-85) takes `maxParallel`; the batch run passes a large value.

**Debounced projection** (lines 837-861) - make the interval adaptive (`max(2 s, 10 x last duration)`, cap 60 s); keep the double-checked `ProjectionGate.WaitAsync(0)` and the commented swallow:
```csharp
if (DateTime.UtcNow - ctx.ReadLastProjection() < KeyProjectionInterval)
    return;
if (!await ctx.ProjectionGate.WaitAsync(0).ConfigureAwait(false))
    return;
try { ... await CheckpointCoreAsync(ctx, final: false, GenerationOutcome.None, string.Empty).ConfigureAwait(false); }
catch (Exception)
{
    // A projection is a convenience view of the journal; the thread is already on disk and the wave end re-projects
}
finally { ctx.ProjectionGate.Release(); }
```
**Cost helper to remove** (lines 1095-1096): `ModelCost(model, in, out)`; callers at lines 265 and 537 take `CostUsd` from the outcome instead.
**Test seams** (lines 87-106): new batch seams follow the `internal Func<...>? XxxHook { get; set; }` pattern with a one-line comment.

---

### `Models/Evidence/JournalRecord.cs` + `Services/Evidence/GenerationJournal.cs` - modified

**Analog:** themselves.

Enum is append-only (`Models/Evidence/JournalRecord.cs` lines 8-11):
```csharp
public enum JournalRecordType
{
    RunStarted, ThreadDone, ThreadFailed, RunEnded, AttachmentDone, AttachmentFailed, AttachmentSkipped, SideWorld, CastVoices, AttachmentsRepaired
}
```
Append `ThreadInvalidated` at the END. Older builds drop unknown types (`GenerationJournal.ReadRecords` lines 424-426).

Optional fields: add after `CostUsd` (record line ~63) with a `//` comment, and clamp in `Normalize()` beside lines 142-146:
```csharp
InputTokens = Math.Max(0, InputTokens);
OutputTokens = Math.Max(0, OutputTokens);
CostUsd = Math.Max(0m, CostUsd);
MediaCostUsd = Math.Max(0m, MediaCostUsd);
```
Replay switch (`GenerationJournal.cs` lines 199-259) - add a case for `ThreadInvalidated` that removes the thread from `LastByThread` and its owned attachments from `LastByAttachment` (copy the owner loop at lines 218-219), but keeps its spend. Add breakdown sums (`CacheWriteTokens`, `CacheReadTokens`, `BatchSavingsUsd`) where `SpentCostUsd` accumulates, so Case Summary R1 survives restarts. `JournalReplay` (lines 12-66) gets the new totals as auto-properties with `//` comments.

---

### `Services/Evidence/ThreadInvalidation.cs` (service) - new

**Analog:** `Services/Evidence/MailboxWriter.cs` `DeleteCopies` (lines 157-182) for EML removal, and `GenerationJournal.AppendAsync` for the record:
```csharp
public List<string> DeleteCopies(PlannedMessage message)
{
    var left = new List<string>();
    if (message == null)
        return left;

    foreach (var relative in CopyPathsFor(message, _custodianFolders))
    {
        var full = ResolveUnderCase(_caseFolder, relative);
        if (full == null)
            continue;
        try
        {
            if (File.Exists(full))
                File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked copy is reported to the caller, which names it in the failure reason
            left.Add(relative);
        }
    }
    return left;
}
```
Return a result class like `StartOverResult` (`GenerationReset.cs` lines 11-15: `FilesDeleted` + `List<string> Errors`) so the dialog footer can show the S3 "could not be deleted" line. The relevant late-render rule reuses `JournalRecord.LateRepair` (record line ~83: "deltas stay out of the ledger").

---

### `Services/Evidence/GenerationReset.cs` (service, file-I/O) - modified (D-25 + batch cleanup)

**Analog for retry:** `Services/Case/CaseStore.cs` lines 398-410 (`MaxMoveAttempts = 5`, line 45):
```csharp
for (var attempt = 1; ; attempt++)
{
    try
    {
        File.Move(tmp, path, overwrite: true);
        return;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MaxMoveAttempts)
    {
        // Transient lock (OneDrive, antivirus, search indexer)
        Thread.Sleep(100 * attempt);
    }
}
```
Apply it inside `TryDelete` (lines 89-104). `DeleteAll` runs on `Task.Run` (line 38), so `Thread.Sleep` is acceptable. Keep the final `catch ... when (IOException or UnauthorizedAccessException)` that adds `$"{displayPath}: {ex.Message}"` to `Errors`. Add constant-name deletes for `provider-batches.jsonl`, `batch-results/`, `qa-*.json(l)` after the journal delete (line 78), the same way `storyName` is handled (lines 83-85).

---

### `Services/Qa/BlindReviewer.cs` + `BlindReviewPrompts.cs` (generator, bounded fan-out) - new

**Analog:** `Services/Evidence/CastVoiceBuilder.cs` (whole file, 160 lines).

**Header + schema registration** (lines 24-33):
```csharp
internal static class CastVoiceBuilder
{
    public const string OperationName = "Generate Corpus - voicemail voices";
    public const int MaxOutputTokens = 8_000;
    public const string SystemPrompt = "...";   // byte-stable: this is the cached prefix

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(CastVoiceDto) };
```
**Request build** (lines 43-71): `StringBuilder` user prompt, `LlmRequest { Tier = LlmTier.Volume, OperationName, SystemPrompt, UserPrompt, MaxOutputTokens }`. For blind review the review protocol goes into `SystemPrompt` (cacheable, identical every call); the document goes into `UserPrompt`.
**Call through the bounded runner** (lines 76-87):
```csharp
var outcome = await PlanCallRunner.RunAsync<CastVoiceDto>(llm, BuildRequest(draft, guide), run);
var result = new CastVoiceOutcome { InputTokens = outcome.InputTokens, OutputTokens = outcome.OutputTokens };
if (run.IsFatal && outcome.Value == null)
    return result;
```
**Strict DTOs** (lines 140-159):
```csharp
internal class CastVoiceItemDto
{
    [JsonPropertyName("personId")]
    [Description("Person id exactly as given, for example CH-001")]
    public string PersonId { get; set; } = string.Empty;
```
Register in `ReelDiscovery.Tests/Case/CaseSchemaTests.cs` lines 11-32 (`foreach (var type in BlindReviewer.SchemaTypes) data.Add(nameof(BlindReviewer), type);`).
- Protocol content: `Services/Corpus/ReviewProtocolBuilder.cs` (issues, relevance criteria, privilege rule).
- Deterministic sample order: `RenderAssignments.StableRank` (no `Random`).
- Per-verdict append to `qa-blind-review.jsonl`: same technique as `BatchLedger`/`GenerationJournal.AppendAsync`.
- Reviewer gateway: copy `WizardState.CreateGateway` (lines 86-94) into `CreateReviewerGateway`, choosing the provider whose key is tested (`OpenAIKeyTested`/`AnthropicKeyTested`, lines 18-19) and that is not `TextProvider`; Volume tier default via `AIModelConfig.GetTierDefault` (line 58-59).
```csharp
public ILlmGateway CreateGateway()
{
    var key = GetApiKey(TextProvider);
    var story = StoryModelConfig
        ?? throw new InvalidOperationException($"No Story model selected for {TextProvider}");
    var volume = VolumeModelConfig
        ?? throw new InvalidOperationException($"No Volume model selected for {TextProvider}");
    return new LlmGateway(LlmProviderFactory.Create(TextProvider, key), story, volume, UsageTracker);
}
```
Note: `LlmGateway` ctor (lines 34-39) throws when story/volume provider differs from `provider.Kind`, so the reviewer gateway must pass that provider's own tier defaults for both tiers.

---

### `Services/Qa/CorpusScans.cs` (service, transform) - new

**Analog:** `Services/Evidence/RenderValidator.cs` - reuse, do not re-implement.

Entry points (lines 112, 147, 257): `Check(RenderedThread, ThreadRenderContext)`, `CheckAttachment(AttachmentCheckInput, ThreadRenderContext)`, `CheckFiller(RenderedThread, ThreadRenderContext)`. Never-throw shape (lines 112-139):
```csharp
var violations = new List<RenderViolation>();
if (rendered == null || ctx == null)
    return violations;

CheckContext c;
try { c = new CheckContext(rendered, ctx); }
catch (Exception)
{
    // Without a usable snapshot no rule can run; an unchecked thread is not a violation (Pitfall 5)
    return violations;
}
Run(violations, () => CheckCanonicalVariants(c));   // V3
Run(violations, () => CheckKnowledge(c));           // V4
Run(violations, () => CheckLedger(c));              // V6
```
Filter to V3/V4/V6 codes after calling `Check`. Filler canonical-value check: `Services/Case/CanonicalValueScanner.cs` `Scan(string?)` (line 42) + `IsRegistered(ScannedValue, IEnumerable<CanonicalValue>)` (line 79). Context rebuild: same code `GenerationRunner` uses to build `ThreadRenderContext` from the replayed journal (`GenerationJournal.Replay`).

---

### `Services/Qa/LeakageAudit.cs` (service, transform) - new (partial analog)

- Input rows: `Services/Corpus/AnswerKeyBuilder.Build(plan, guide, overlay)` (line 33) returns `List<AnswerKeyRow>`; fields available (`Models/Corpus/AnswerKeyRow.cs` lines 8-30): `From`, `To`, `Cc`, `DateSent`, `Subject`, `ThreadId`, `FamilyId`, `ParentDocId`, `Relevance`, `Privileged`, `NearMiss`, `DocType`. Exclude attachment rows (`ParentDocId` non-empty).
- Fold assignment: `RenderAssignments.StableRank(threadId) % 5`.
- Test fixtures: `ReelDiscovery.Tests/Evidence/DistributionParityTests.cs` (synthetic plan + real-planner plan).
- `LogisticRegression.cs`: no analog; follow RESEARCH Pattern 8.

---

### `Services/Qa/QaReportStore.cs` (store) - new

**Analog:** `CaseStore.WriteAtomic` (lines 383-415) for the JSON write, `GenerationJournal.PathFor` (line 109) for the constant-name path rule, `CaseDraft.JsonOptions` for serialization. Read path never throws (copy `GenerationJournal.Replay` outer catch, lines 265-280).

---

### `UserControls/QaUiText.cs` (pure copy) - new; `GenerationUiText.cs` - modified

**Analog:** `UserControls/GenerationUiText.cs`.

Class header and culture fields (lines 197-207):
```csharp
/// All Generate Corpus and Case Summary copy with counts, dates or conditions (05- and 06-UI-SPEC), pure and
/// unit-tested so the generation step stays layout-only (03-06 precedent). Every string is plain text and may carry
/// model-written subjects or violation texts; labels showing it set UseMnemonic = false (T-04-33, T-05-15).
public static class GenerationUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
```
Helpers (lines 857, 895): `private static string Plural(int n, string singular, string plural) => CaseGuideUiText.Plural(n, singular, plural);` and `private static string N(int value) => value.ToString("N0", Invariant);`.
Row content DTO (lines 138-159): `SummaryTone` + `SummaryRowContent { Line2, Line3, Line3Tone, DotTone, Line3Tooltip, ShowPrimary, ShowSecondary }` - reuse for R1-R4 rows.
Ready-card estimate builder to change (lines 647-676), dropped-clause idiom:
```csharp
var mediaClause = mediaPlanned ? $" · images and voicemail ≈{Money(mediaCost)}" : "";
var estimate = string.Equals(input.StoryModel, input.VolumeModel, StringComparison.Ordinal)
    ? $"Estimated cost ≈{Money(total)} on {input.StoryModel}, ±50%{mediaClause}"
    : $"Estimated cost ≈{Money(total)}, ±50%: {input.StoryModel} ≈{Money(input.StoryCost)} · ...";
```
Confirmation tuple pattern (lines 735-749): `public static (string Title, string Text) CG1CancelGeneration(...)` - add batch clauses here and C-Q1..C-Q3 in `QaUiText` the same way. `GenerationReadyInput` (around line 100-123) gains `BatchStoryCost`, `BatchVolumeCost`, `BatchChecked`, `PlannedEmails`.

---

### `UserControls/StepGenerateCorpus.cs` / `.Run.cs` (component) - modified

**Analog:** itself.

Ready-card controls (StepGenerateCorpus.cs lines 452-482) - the batch checkbox and hint slot in between `_lblStateEstimate` and `_lblStateMediaNotice`:
```csharp
_lblStateEstimate = new Label
{
    AutoSize = true,
    UseMnemonic = false,
    MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
    Font = Theme.SmallFont(),
    ForeColor = Theme.TextMuted,
    BackColor = Theme.Surface,
    Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20)),
    Visible = false
};
...
_stateContent.Controls.Add(_lblStateEstimate);
_stateContent.Controls.Add(_lblStateMediaNotice);
_stateContent.Controls.Add(_btnStateAction);
```
Checkbox style: copy `Forms/RegenerateSelectedDialog.cs` lines 57-67 (`AutoSize = true, FlatStyle = FlatStyle.System, Font = Theme.BodyFont(), ForeColor = Theme.Text, UseMnemonic = false`), with `BackColor = Theme.Surface`.
"Last line gets 20 px" margin logic (lines 983-992) - extend to the checkbox and hint:
```csharp
var last = LogicalToDeviceUnits(20);
_lblStateBody.Margin = new Padding(0, 0, 0, estimate.Length > 0 || notice.Length > 0 ? LogicalToDeviceUnits(Theme.SmallGap) : last);
_lblStateEstimate.Margin = new Padding(0, 0, 0, notice.Length > 0 ? LogicalToDeviceUnits(4) : last);
```
Estimate source (Run.cs lines 977-1003): `GenerationCostEstimator.Estimate(plan, _state.StoryModelConfig, _state.VolumeModelConfig, _state.MediaPrices, _state.IsMediaAvailable, Draft?.Guide)` - read the new batch totals from the same call. Tooltip on the estimate: the step already owns `_toolTip` (disposed in Run.cs line 1044). Stage rows: `GenerationUiText.StageRowText(WaveProgress)` (line 366) returns `(Dates, Done, Tooltip)` - add the batch-wait branch there, data from `WaveProgress` (`Models/Evidence/GenerationProgress.cs` lines 98-118) plus new batch fields. New activity events: add to `GenerationEventKind` (lines 8-28, append at end) and map in `GenerationUiText.ActivityText` (line 389).

---

### `UserControls/StepCaseSummary.cs` (component) - modified

**Analog:** itself.

Row creation (lines 404-414):
```csharp
_rowMailboxes = CreateRow("Mailboxes", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open Mailboxes Folder", "Generate Emails" });
_rowStory = CreateRow("Story so far", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open in Word", "Export Story So Far" });
```
New: `CreateRow("Generation cost", hasLine3: true, hasButtons: false, ...)`, `CreateRow("Consistency scans", true, true, ..., new[] { "View Findings" })`, `CreateRow("Leakage audit", true, true, ..., new[] { "View Report", "Run Audit" })`, `CreateRow("Blind review", true, true, ..., new[] { "View Findings", "Run Blind Review...", "Cancel Review" })` (Cancel Review restyled Danger via `ButtonHelper.StyleButton`).
Group list (lines 427-442) - insert `(null, "Quality checks")` + the three rows between `_rowStory` and `(null, "Case guide")`; the divider loop (lines 443-456) needs no change.
Apply pattern (lines 880-884, 923-929):
```csharp
ApplySummaryRow(_rowMailboxes, mailboxes);
_toolTip.SetToolTip(_rowMailboxes.Line3!, mailboxes.Line3Tooltip);
_btnOpenMailboxes.Visible = mailboxes.ShowPrimary;
```
Busy gating: add new row buttons to the `UpdateButtons` enumeration (lines 968-970). Status strip: `SetStatus(text, color)` (lines 989-993) for "Running the leakage audit..." and blind-review progress.

---

### `Forms/BlindReviewDialog.cs` (ThemedDialog) - new

**Analog:** `Forms/RegenerateSelectedDialog.cs` (whole file, 79 lines).
```csharp
public class RegenerateSelectedDialog : ThemedDialog
{
    public RegenerateSelectedDialog(int selectedCount, int editedCount)
        : base(CaseGuideUiText.RegenerateSelectedTitle)
    {
        ...
        InitializeUI();
    }

    private void InitializeUI()
    {
        AddHeadline(CaseGuideUiText.RegenerateSelectedHeadline(_selectedCount));
        AddBody(CaseGuideUiText.RegenerateSelectedBody(_editedCount));

        var caption = CreateWrappingLabel("Guidance (optional)", Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);
        ...
        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnRegenerate = ButtonHelper.CreateButton("Regenerate Selected", 0, LDU(34), ButtonStyle.Primary);
        _btnRegenerate.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnRegenerate);

        FinishLayout();
    }
}
```
Base API (`Forms/ThemedDialog.cs`): `AddHeadline` (63), `AddBody` (71), `CreateWrappingLabel` (82), `AddContent` (167), `AddButtons(cancel, others)` (178, cancel gets focus), `FinishLayout` (211), `LDU` (61), `ContentWidth` (59). Sample row: `FlowLayoutPanel` LeftToRight + `NumericUpDown` + labels as in `ModelConfigurationDialog` media row (lines 219-255, `CreatePriceLabel` 293-301). All copy from `QaUiText`.

---

### `Forms/QaFindingsDialog.cs` (sizable grid dialog) - new

**Analog:** `UserControls/ModelConfigurationDialog.cs` for sizable + grid + working-area clamp; `Forms/ThemedDialog.cs` constructor for Theme chrome.

Logical sizes and scaling (ModelConfigurationDialog lines 24-26, 59-69):
```csharp
internal static readonly Size LogicalSize = new(980, 480);
internal static readonly Size LogicalMinimumSize = new(820, 360);
...
this.Size = new Size(LogicalToDeviceUnits(LogicalSize.Width), LogicalToDeviceUnits(LogicalSize.Height));
this.MinimumSize = new Size(LogicalToDeviceUnits(LogicalMinimumSize.Width), LogicalToDeviceUnits(LogicalMinimumSize.Height));
this.StartPosition = FormStartPosition.CenterParent;
this.FormBorderStyle = FormBorderStyle.Sizable;
this.MaximizeBox = false;
this.MinimizeBox = false;
this.ShowInTaskbar = false;
```
Clamp (lines 283-291):
```csharp
protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);
    // Keep the scaled window on screen on small displays at high scaling
    var area = Screen.FromControl(this).WorkingArea;
    MinimumSize = new Size(Math.Min(area.Width, MinimumSize.Width), Math.Min(area.Height, MinimumSize.Height));
    Size = new Size(Math.Min(area.Width, Width), Math.Min(area.Height, Height));
}
```
Theme chrome to copy before children (ThemedDialog lines 20-36): `SuspendLayout; AutoScaleDimensions = new SizeF(7F, 15F); AutoScaleMode = Font; Font = Theme.BodyFont(); ResumeLayout(false); PerformAutoScale();` then `BackColor = Theme.Background; Padding = new Padding(LDU(24))`. Root `TableLayoutPanel` with row-index comments (ModelConfigurationDialog lines 71-83 style). Grid: `Helpers/GridStyle.Apply(grid, owner)` (line 15) and `GridStyle.AddDotColumn(grid, owner)` (line 54). Test accessors pattern (lines 445-450: `internal DataGridView ModelsGrid => _gridModels;`). Open Email: same shell rule as the Activity double-click in `StepGenerateCorpus`.

---

### `UserControls/ModelConfigurationDialog.cs` (CRUD dialog) - modified

**Analog:** itself. Price column pattern (lines 141-157):
```csharp
_gridModels.Columns.Add(new DataGridViewTextBoxColumn
{
    Name = "OutputPrice",
    HeaderText = "Output $/M",
    DataPropertyName = "OutputTokenPricePerMillion",
    Width = LogicalToDeviceUnits(90),
    DefaultCellStyle = new DataGridViewCellStyle { Format = "F2" }
});
```
Insert `CacheWritePrice`, `CacheReadPrice`, `BatchDiscount` after it with UI-SPEC S5 widths/formats; narrow the others; set `ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True`, `ColumnHeadersHeightSizeMode = AutoSize`, `Frozen = true` on Provider and ModelId; `LogicalSize` -> `new(1180, 480)`. Extend the header label text (line 88). Clone block (lines 42-52) and `ResetDefaults` (lines 377-383) must carry the new properties.
**Divergence to resolve:** UI-SPEC S5 says bad entries revert "No MessageBox", but the current `GridModels_DataError` (lines 318-324) shows a MessageBox. Planner should decide whether to change the handler (affects existing behavior) or keep it.
Render test analog: `ReelDiscovery.Tests/Render/DialogRenderTests.cs` lines 240-272 (`RenderHarness.RenderForm(() => new ModelConfigurationDialog(...), "dialog-model-config-...", form => {...}, new ClippingOptions { AllowGridHorizontalScroll = true })`).

---

### `Forms/WizardForm.cs` (shell) - modified (S6, S7, D-20, D-21)

**Analog:** itself.
- Remove `_builtFlow` (line 15), `state.Flow = WizardFlow.CaseDriven` (line 51), the flow ternary in `StepTypesFor` (lines 282-293 -> one array, keep the case list lines 285-288), the rebuild in `BtnNext_Click` (lines 444-446), the legacy MessageBox branch in `BtnCancel_Click` (lines 461-472), the `flow` parameter of `DecideClose` (line 503-506) and the `Flow` checks at lines 522 and 589-593. `BuildSteps(WizardFlow)` (lines 342-361) becomes parameterless (or folds into `LoadSteps`, lines 321-328).
- Ticker tooltip: `UpdateCostDisplay` (lines 122-134) is the 1 s timer callback; set the tooltip only when the text changes:
```csharp
var tracker = _state.UsageTracker;
var totalTokens = tracker.TotalInputTokens + tracker.TotalOutputTokens;
if (totalTokens > 0)
    _lblCostTracker.Text = $"Tokens: {totalTokens:N0} | Cost: ${tracker.TotalCost:F4}";
else
    _lblCostTracker.Text = "API Cost: $0.0000";
```
Label is created at lines 180-188; add a `ToolTip` field owned by the form and dispose it with the form.

---

### `UserControls/StepApiConfiguration.cs` (component) - modified (S7)

**Analog:** itself. Remove fields lines 36-38, row styles lines 86-87 (`// 13: Workflow header`, `// 14: Workflow radios`), construction lines 207-244, handler lines 246-252, bind lines 649-650, validate line 702; `RowCount = 15` (line 64) -> 13. Keep the remaining row-index comments accurate (CLAUDE.md rule). Re-capture `WizardShellRenderTests` baseline.

---

### `Program.cs` + `Services/ApiKeyStore.cs` (startup) - modified (D-22)

Delete `Program.cs` lines 20-31 (the `MigrateLegacyKey()` try/catch block). Delete `ApiKeyStore.LegacySettingsPath` (line 31), `MigrateLegacyKey()` (line 106 onward) and its tests (`ReelDiscovery.Tests/ApiKeyStoreTests.cs` lines ~78-144). Add a `PlatformTests` assertion that `Program` no longer references it (reflection test style already in `ReelDiscovery.Tests/PlatformTests.cs` / `TelemetryRemovalTests.cs`).

---

### `Helpers/ThemeSanitizer.cs` (new) + `Helpers/HtmlEmailFormatter.cs` (D-26)

**Source to move:** `Services/ThemeGenerator.cs` lines 140-156 (`SanitizeHexColor`), 199-207 (`AllowedFonts`), 212-226 (`SanitizeFont`). Make a `public static class ThemeSanitizer` in `ReelDiscovery.Helpers`; update callers `Helpers/BulkMailFormatter.cs` lines 259-274, `Helpers/HtmlEmailFormatter.cs` lines 649-651, `Services/Evidence/OrganizationThemes.cs` lines 47-51, and `PeopleDirectoryTests`.

**Pattern to apply in `BuildEmailTemplate`** (`HtmlEmailFormatter.cs` lines 17-28 today use raw `t.PrimaryColor`, `t.BodyFont`): copy `BulkMailFormatter.SanitizedTheme` (lines 251-268):
```csharp
private static OrganizationTheme SanitizedTheme(OrganizationTheme theme)
{
    var d = OrganizationTheme.Default;
    return new OrganizationTheme
    {
        Domain = theme.Domain,
        OrganizationName = theme.OrganizationName,
        ThemeName = theme.ThemeName,
        PrimaryColor = ThemeGenerator.SanitizeHexColor(theme.PrimaryColor) ?? d.PrimaryColor,
        ...
        HeadingFont = ThemeGenerator.SanitizeFont(theme.HeadingFont) ?? d.HeadingFont,
        BodyFont = ThemeGenerator.SanitizeFont(theme.BodyFont) ?? d.BodyFont
    };
}
```
**Golden-HTML risk:** `SanitizeHexColor` upper-cases (line 155: `return color.ToUpperInvariant();`). A valid lowercase theme color would change output. The Phase 5 golden (`ReelDiscovery.Tests/Evidence/Golden`, `ComposeHtmlTests`) must stay byte-identical, so either keep the original value when it is already valid or confirm the golden themes are uppercase. Move `SanitizedTheme` itself into `ThemeSanitizer` so both formatters share it.

---

### `Services/Corpus/NonRelevantSkeletonBuilder.cs` (D-23)

**Analog:** itself, lines 206-234:
```csharp
var target = (int)Math.Round(specs.Count * PrivilegedDailyShare, MidpointRounding.AwayFromZero);
if (target == 0 && specs.Count >= MinDailyThreadsForPrivileged)
    target = 1;
Shuffle(candidates, ctx.Random);
foreach (var (spec, counsel) in candidates.Take(Math.Min(target, candidates.Count)))
{
    spec.IsPrivileged = true;
    if (counsel != spec.Sender)
        spec.Forced.Insert(0, counsel);
}
```
The small-corpus gap is the `MinDailyThreadsForPrivileged` floor and the 2% share. Test in `ReelDiscovery.Tests/Corpus/NonRelevantSkeletonTests.cs` and through `LeakageAuditTests` on the 250-email fixture.

### `Services/Evidence/InviteScheduler.cs` (D-24)

**Analog:** itself, lines 359-376:
```csharp
private static (DateTimeOffset Start, DateTimeOffset End) Derive(TimeZoneInfo tz, DateOnly date, TimeOnly? stated, string seed)
{
    var rank = RenderAssignments.StableRank(seed + ":invite-slot");
    var time = stated ?? BusinessCalendar.DayStart.AddMinutes(SlotStepMinutes * (int)(rank % SlotSteps));
    var minutes = (rank / SlotSteps) % 2 == 0 ? 30 : 60;
    var start = BusinessCalendar.At(tz, date, time);
    return (start, start.AddMinutes(minutes));
}
private static TimeZoneInfo ZoneOf(PlannedMessage parent) => BusinessCalendar.ResolveId(parent?.TimeZoneId) ?? TimeZoneInfo.Utc;
```
The per-sender `tz` passed to `Derive` is the cause; matched meetings (keyed by `MeetingKey`, `Resolve` lines 94-129) need one canonical zone (e.g. the first participant by StableRank or the organizer) so `KnownSlots` resolves to the same instant. Test in `ReelDiscovery.Tests/Evidence/InviteSchedulerTests.cs`.

---

## Shared Patterns

### Never-throw provider and unit-of-work error ladder
**Source:** `Services/Ai/AnthropicTextProvider.cs` lines 57-83; `Services/Corpus/PlanCallRunner.cs` lines 105-140.
**Apply to:** `BatchingLlmProvider`, both batch transports, `BlindReviewer`, `ThreadInvalidation`, `CorpusScans`, `LeakageAudit`.
```csharp
catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested) { throw; }
catch (OperationCanceledException) when (run.Cts.IsCancellationRequested) { return new PlanCallOutcome<T>(); } // Fatal stop from another call
catch (OperationCanceledException) { return new PlanCallOutcome<T> { Error = $"The request timed out. {run.RetryHint}" }; }
catch (LlmCallException ex) when (ex.Result.IsFatal) { run.SetFatal(ex.Result.Message ?? ex.Message); ... }
```
Always catch `OperationCanceledException` before `Exception`; every swallowed exception gets a `//` reason.

### Append-only JSONL with flush and torn-line repair
**Source:** `Services/Evidence/GenerationJournal.cs` lines 119-154 (write) and 384-430 (read).
**Apply to:** `BatchLedger` (`provider-batches.jsonl`), blind-review verdict log (`qa-blind-review.jsonl`).

### Atomic write and transient-lock retry
**Source:** `Services/Case/CaseStore.cs` lines 383-415 (`WriteAtomic`, `MaxMoveAttempts = 5`, `100 * attempt` ms).
**Apply to:** batch results files, `QaReportStore`, `GenerationReset.TryDelete` (D-25).

### Constant-name, case-folder-relative paths
**Source:** `GenerationJournal.PathFor` line 108-109 (`// Case folder + constant name only, never a path from data (T-04-26)`); `MailboxWriter.ResolveUnderCase`; `JournalRecord.Normalize` lines 116-118 (`Files.RemoveAll(f => !CorpusPlan.IsSafeEmlPath(f))`).
**Apply to:** every new file in the case folder; provider batch/file ids sanitized before use in a file name.

### Deterministic ordering
**Source:** `RenderAssignments.StableRank` (`Services/Evidence/RenderAssignments.cs` lines 37-41).
**Apply to:** request identity, blind-review sample order, leakage CV folds, D-24 canonical zone choice. Never `Random`/`Guid`/`GetHashCode` for anything persisted or written.

### Single cost function
**Source:** `AIModelConfig.CalculateTotalCost` (lines 53-54) today, recomputed at `ThreadUnitOfWork.cs` 755-756, `AttachmentUnitOfWork.cs` 597-601, `GenerationRunner.cs` 1095-1096, `TokenUsageTracker.cs` 25, `GenerationRunState.cs` 513.
**Apply to:** replace all five with `CostUsd` from `LlmResult`/`PlanCallOutcome` (live) and `PriceWork` (estimates).

### Strict schema DTO registration
**Source:** `internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(XDto) };` (`CastVoiceBuilder.cs` line 33, `FillerRenderer.cs` line 19) + `ReelDiscovery.Tests/Case/CaseSchemaTests.cs` lines 11-32.
**Apply to:** blind-review DTO.

### Pure UI copy + DPI-safe controls
**Source:** `UserControls/GenerationUiText.cs` (pure static class, `Plural`, `N`, `SummaryRowContent`, tuple confirmations); labels with `UseMnemonic = false`, `MaximumSize = new Size(LogicalToDeviceUnits(520), 0)`; buttons via `ButtonHelper.CreateButton(text, 0, height, ButtonStyle.X)`; fonts only from `Theme.*Font()`; colors only `Theme.*`.
**Apply to:** `QaUiText`, all S1-S7 UI. Copy tests follow `ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs` (incl. the banned-word scan lines 32-39: "evidence", "noise", "filler").

### Render verification at 144 DPI
**Source:** `ReelDiscovery.Tests/Render/RenderHarness.cs`, `ClippingDetector.cs`, example `DialogRenderTests.cs` lines 240-272 (`[Trait("Category","Render")]`, assert `result.DeviceDpi >= RenderHarness.MinimumDpi`, `Assert.Empty(result.Clipping)`).
**Apply to:** every changed screen in UI-SPEC render list 1-10.

### Test fakes
**Source:** `ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs` (scripted queue, `Respond` func, `OnSend` hook, request snapshots under `_lock`); `FakeHttpHandler` (captures request bodies for wire tests, used in `AnthropicWireTests.CreateProvider` lines 36-42).
**Apply to:** `FakeBatchTransport` (scripted submit/poll/results, injectable errored/expired items, batch-level failure), batch wire tests.

---

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| `Services/Qa/LogisticRegression.cs` | utility | transform | No numeric/ML code in the repo. Follow RESEARCH Pattern 8 (L2 logistic regression, grouped 5-fold CV, balanced accuracy). |
| Batch polling loop inside `BatchingLlmProvider` | service | event-driven (timer poll) | No provider-side polling exists. Closest timing idiom is the 1 s `System.Windows.Forms.Timer` in `WizardForm.InitializeCostTracker` (UI only). Use `Task.Delay(interval, ct)` in a loop and report through `GenerationSnapshot` (thread-safe, `lock (_lock)`). |
| OpenAI file upload / batch protocol JSON | service | file-I/O | No existing use of `OpenAIFileClient`/`BatchClient`. Use RESEARCH "Code Examples"; parse with `JsonDocument` defensively like `ExtractErrorMessage` (OpenAITextProvider lines 235-258). |
| Quiet-period flush timer | service | batching | No debouncer exists outside UI. Closest idea: `MaybeProjectAsync` time check + gate (GenerationRunner 837-861). |

---

## Notes for the Planner

1. `GridModels_DataError` currently shows a MessageBox (ModelConfigurationDialog lines 318-324); UI-SPEC S5 says "cell reverts, no MessageBox". Decide explicitly.
2. D-26: `SanitizeHexColor` upper-cases valid colors; check golden themes before routing `BuildEmailTemplate` through it.
3. `LlmGateway` refuses mixed-provider tier models (lines 34-39); the reviewer gateway must use the reviewer provider's own Story/Volume defaults.
4. `FakeLlmProvider` snapshots requests field by field (lines 54-63); new `LlmProviderRequest` properties (`AllowBatch`) must be added to that snapshot or tests will miss them.
5. Cutover shared-helper splits are listed under "Cutover"; legacy-only references found by grep: `Helpers/FileNameHelper.cs` lines 8-18, `Helpers/ThreadingHelper.cs` lines 13, 80, 90, `Models/WizardState.cs` lines 58-59, 97, 105, 115, 133-161.

## Metadata

**Analog search scope:** `Services/Ai`, `Services/Evidence`, `Services/Corpus`, `Services/Case`, `Models`, `Models/Evidence`, `Models/Corpus`, `UserControls`, `Forms`, `Helpers`, `Program.cs`, `ReelDiscovery.Tests/{Ai,Fakes,Case,Evidence,Corpus,Render}`
**Files scanned:** ~45 read or grepped
**Pattern extraction date:** 2026-10-06
