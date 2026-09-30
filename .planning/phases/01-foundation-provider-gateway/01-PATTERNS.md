# Phase 1: Foundation & Provider Gateway - Pattern Map

**Mapped:** 2026-09-29
**Files analyzed:** 44 (21 new, 23 modified)
**Analogs found:** 38 / 44 (6 have no in-repo analog: test infra and build pins; use RESEARCH.md "Code Examples")

All line numbers refer to `main` @ a0ef9bc. Paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\`.

## File Classification

### New files

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `global.json` | config | — | none | no analog (RESEARCH "global.json") |
| `ReelDiscovery.slnx` | config | — | none | no analog (`dotnet new sln`) |
| `Services/Ai/LlmTier.cs` | model (enum) | — | `Models/Attachment.cs` (`AttachmentType` enum in same file as type) | role-match |
| `Services/Ai/LlmProviderKind.cs` | model (enum) | — | `Models/Attachment.cs` | role-match |
| `Services/Ai/LlmRequest.cs` | model (DTO) | request-response | `Models/GenerationConfig.cs` (mutable POCO, auto-props, defaults) | role-match |
| `Services/Ai/LlmResult.cs` (`LlmResult<T>`, `LlmOutcome`, `LlmFailureKind`, `LlmCallException`) | model (result) | request-response | `Models/GenerationResult.cs` + `StorylineGenerationResult` (`Services/StorylineGenerator.cs:304-310`) | role-match |
| `Services/Ai/ILlmGateway.cs` | service interface | request-response | `UserControls/IWizardStep.cs` (only interface in repo) | partial |
| `Services/Ai/LlmGateway.cs` | service | request-response | `Services/OpenAIService.cs` `GetJsonCompletionAsync<T>` (lines 115-170) + `TrackUsage` (179-189) | exact (same responsibility, new shape) |
| `Services/Ai/ILlmProvider.cs` | service interface | request-response | `UserControls/IWizardStep.cs` | partial |
| `Services/Ai/LlmProviderRequest.cs` / `LlmProviderResponse` | model (DTO) | request-response | `Models/GenerationConfig.cs` | role-match |
| `Services/Ai/OpenAITextProvider.cs` | service (translator) | request-response | `Services/OpenAIService.cs` lines 22-27, 115-170 | exact |
| `Services/Ai/AnthropicTextProvider.cs` | service (translator) | request-response | `Services/OpenAIService.cs` (structure) + RESEARCH Pattern 3/5 (SDK surface) | role-match |
| `Services/Ai/JsonSchemaRegistry.cs` | utility (static cache) | transform | `Helpers/FileNameHelper.cs` / `Services/TelemetryService.cs` (static class shape) | partial |
| `Services/Ai/OpenAIMediaClient.cs` | service | request-response (binary) | `Services/OpenAIService.cs` lines 191-301 | exact |
| `Services/ApiKeyStore.cs` | service | file-I/O | `Services/TelemetryService.cs` lines 16-68 (settings file in `%LOCALAPPDATA%\ReelDiscovery`) + `StepApiConfiguration.cs` 340-379 (legacy key file) | exact |
| `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` | config (test) | — | `ReelDiscovery.csproj` (property conventions) | partial; template in RESEARCH |
| `ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs` | test utility | request-response | none | no analog (RESEARCH "Fake HTTP") |
| `ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs` | test utility | request-response | none (implements new `ILlmProvider`) | no analog |
| `ReelDiscovery.Tests/Fakes/StrictSchemaValidator.cs` | test utility | transform | none | no analog (RESEARCH "StrictSchemaValidator") |
| `ReelDiscovery.Tests/**/*Tests.cs` (Ai, ApiKeyStore, Concurrency, EmailGeneratorFix, MediaGating, ModelCatalog, DocumentRegression) | test | — | none (no tests exist) | no analog; follow CLAUDE.md naming |

### Modified files

| Modified File | Role | Data Flow | Analog / Pattern Source | Match Quality |
|---------------|------|-----------|-------------------------|---------------|
| `ReelDiscovery.csproj` | config | — | itself (lines 1-51) | exact |
| `Program.cs` | entry point | — | itself (telemetry opt-in block lines 20-26) | exact |
| `Models/WizardState.cs` | model (state) | — | itself (`CreateOpenAIService` lines 47-56) | exact |
| `Models/AIModelConfig.cs` | model (catalog) | — | itself (`GetDefaultModels` lines 23-68) | exact |
| `Models/TokenUsageTracker.cs` | model (thread-safe accumulator) | — | itself (`RecordUsage` 23-43) | exact |
| `Models/GenerationResult.cs` | model (result + progress) | — | `TokenUsageTracker` lock pattern (lines 5, 27-42) | role-match |
| `Models/GenerationConfig.cs` | config model | — | itself (image settings lines 18-20) | exact |
| `Helpers/DateHelper.cs` | utility | — | itself (line 5 `_random`) | exact |
| `Services/StorylineGenerator.cs` | generator | request-response | itself (ctor 8-14, call 233-236, DTOs 263-302) | exact |
| `Services/CharacterGenerator.cs` | generator | request-response | itself (ctor 8-18, `_random` 9, call 319-322) | exact |
| `Services/ThemeGenerator.cs` | generator | request-response | itself (ctor 8-13, prompt 78-92, call 94, DTOs 156-187) | exact |
| `Services/EmailGenerator.cs` | generator/orchestrator | batch + request-response | itself (orchestration 37-408, DTOs 1924-2036) | exact |
| `Services/OpenAIService.cs` | service | — | delete (folded into `OpenAITextProvider` + `OpenAIMediaClient`) | — |
| `Services/TelemetryService.cs` | service | — | itself (`SendGenerationEventAsync(... string model ...)` line 73-98) | exact (caller string change only) |
| `UserControls/StepApiConfiguration.cs` | component (wizard step) | request-response | itself (layout 31-162, test click 213-263, validate 318-338) | exact |
| `UserControls/ModelConfigurationDialog.cs` | component (dialog) | CRUD (grid) | itself (clone 19-28, columns 81-120) | exact |
| `UserControls/StepTopicInput.cs` | component | — | itself (media checkboxes 139-157, save 227-232) | exact |
| `UserControls/StepGenerationConfig.cs` | component | — | itself (images section 225-250, load 529-535, save 592-595) | exact |
| `UserControls/StepStorylines.cs` | component | request-response | itself (line 268 `CreateOpenAIService`) | exact |
| `UserControls/StepCharacters.cs` | component | request-response | same as StepStorylines (lines 299, 323) | exact |
| `UserControls/StepGeneration.cs` | component | streaming (progress) | itself (lines 177-205) | exact |
| `UserControls/StepComplete.cs` | component | — | itself (lines 158, 192 `_state.SelectedModel`) | exact |

---

## Pattern Assignments

### `Services/Ai/OpenAITextProvider.cs` (translator, request-response)

**Analog:** `Services/OpenAIService.cs`

**Imports** (lines 1-8) — keep this ordering (System first, then third-party, then ReelDiscovery; no blank lines; no implicit-using namespaces):
```csharp
using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;
using ReelDiscovery.Models;

namespace ReelDiscovery.Services;
```
New files under `Services/Ai/` use `namespace ReelDiscovery.Services.Ai;` (namespace mirrors folder).

**Client construction** (lines 22-27) — keep the 10-minute network timeout; add an injectable options overload for tests (RESEARCH "Fake HTTP": `Transport = new HttpClientPipelineTransport(new HttpClient(handler))`, `RetryPolicy = new ClientRetryPolicy(0)`):
```csharp
private static OpenAIClient CreateClient(string apiKey)
{
    var options = new OpenAIClientOptions();
    options.NetworkTimeout = TimeSpan.FromMinutes(10);
    return new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
}
```

**Message building + call** (lines 125-136) — reuse the System/User message list; REPLACE `CreateJsonObjectFormat()` and the "Respond ONLY with valid JSON" suffix with `ChatResponseFormat.CreateJsonSchemaFormat(name, BinaryData.FromString(schemaJson), jsonSchemaIsStrict: true)` + `MaxOutputTokenCount` + optional `ReasoningEffortLevel` (RESEARCH Pattern 4):
```csharp
var messages = new List<ChatMessage>
{
    new SystemChatMessage(systemPrompt + "\n\nIMPORTANT: Respond ONLY with valid JSON. ..."),  // drop suffix
    new UserChatMessage(userPrompt)
};
var options = new ChatCompletionOptions
{
    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()   // -> CreateJsonSchemaFormat(..., strict: true)
};
var response = await _chatClient.CompleteChatAsync(messages, options, ct);
```

**Error handling** (lines 99-110, 152-166) — DO NOT copy the retry loops (SDK retries 429/5xx). Keep only the filtered-catch shape `catch (ClientResultException ex) when (...)`, but map to `LlmFailureKind` per RESEARCH Pattern 5 (`ex.Status` switch; body `insufficient_quota` -> SpendLimit). `catch (OperationCanceledException) { throw; }` first.

**Usage extraction** (lines 179-189) — read `completion.Usage.InputTokenCount` / `OutputTokenCount` and return them on `LlmProviderResponse`; the gateway records them (not the provider).

**Key test** (lines 47-69) — replace this token-spending chat call and its bare `catch { return false; }` (Pitfall 10) with `OpenAIModelClient.GetModelAsync(modelId, ct)` returning a classified result.

---

### `Services/Ai/AnthropicTextProvider.cs` (translator, request-response)

**Analog:** `Services/OpenAIService.cs` for class shape (private readonly client field, ctor taking key + options, one public async method with `ct` last). SDK specifics come from RESEARCH Pattern 3 and Pattern 5 (verified). Mandatory alias (Pitfall 2):
```csharp
using Anthropic;
using Anthropic.Models.Messages;
using AnthropicMessage = Anthropic.Models.Messages.Message;
```
Rules copied from RESEARCH: list-form `System` (`new List<TextBlockParam> { new(r.SystemPrompt) }`), `OutputConfig.Format = new JsonOutputFormat { Schema = ... }`, never send Temperature/TopP/TopK/Thinking, send `Effort` only when catalog `Effort != null`, pick text blocks via `TryPickText` (skip thinking), classify on `m.StopReason?.Raw()`.

---

### `Services/Ai/LlmGateway.cs` (service, request-response)

**Analog:** `Services/OpenAIService.cs` `GetJsonCompletionAsync<T>` (lines 115-170) and `TrackUsage` (179-189).

**Deserialization pattern** (lines 143-147) — keep case-insensitive (Anthropic may alter enum casing):
```csharp
var json = response.Value.Content[0].Text;
return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
});
```
Hoist the options to a `private static readonly JsonSerializerOptions` field.

**Bounded retry-on-parse-failure shape** (lines 121-169) — keep the `for (attempt < Max...)` loop form ONLY for app-level semantic retries (Invalid: 3 attempts total; Truncated: 1 retry at `MaxOutputTokens * 2` capped at `AIModelConfig.MaxOutputTokens`; Refused/Auth/SpendLimit: 0). RESEARCH Pattern 2 is the table.
```csharp
catch (JsonException)
{
    // JSON parsing failed, retry
    if (attempt == MaxRetries - 1)
        throw;
}
```
→ in the gateway, a parse failure sets `Outcome = Invalid` and continues the loop; after the last attempt, return the result (do not throw). Generators call `GetValueOrThrow()`.

**Usage recording** (lines 179-189) — same null-guarded call, now with provider added:
```csharp
if (_usageTracker != null && _modelConfig != null && usage != null)
{
    _usageTracker.RecordUsage(operation, _modelConfig, usage.InputTokenCount, usage.OutputTokenCount);
}
```

**Tier resolution** — snapshot the selected provider + Story/Volume `AIModelConfig` at construction (created by `WizardState.CreateGateway()`), mirroring how `OpenAIService(string apiKey, AIModelConfig modelConfig, TokenUsageTracker? usageTracker = null)` (lines 37-45) receives its config.

---

### `Services/Ai/OpenAIMediaClient.cs` (service, binary request-response)

**Analog:** `Services/OpenAIService.cs` lines 191-301.

**Null-on-failure contract + 400 handling** (lines 227-231) — keep:
```csharp
catch (ClientResultException ex) when (ex.Status == 400)
{
    // Content policy violation or invalid prompt - don't retry
    return null;
}
```
Drop the 429/503 loops (lines 196, 218-226, 251, 284-292) — SDK retries.

**Image options to replace** (lines 200-207): `"dall-e-3"` → `config.ImageModel` (default `gpt-image-2.5-flare`); `Quality = GeneratedImageQuality.Standard` → `new GeneratedImageQuality(config.ImageQuality)` / `LowQuality`; REMOVE `ResponseFormat = GeneratedImageFormat.Bytes`; add `OutputFileFormat = GeneratedImageFileFormat.Png`. See RESEARCH "Image generation (gpt-image)".

**TTS block to copy verbatim** (lines 245-280, including the `/// <summary>` + `<param>` doc and the voice switch expression):
```csharp
var audioClient = _client.GetAudioClient("tts-1");
var options = new SpeechGenerationOptions { SpeedRatio = 1.0f, ResponseFormat = GeneratedSpeechFormat.Mp3 };
var voiceEnum = voice.ToLowerInvariant() switch
{
    "echo" => GeneratedSpeechVoice.Echo,
    "fable" => GeneratedSpeechVoice.Fable,
    "onyx" => GeneratedSpeechVoice.Onyx,
    "nova" => GeneratedSpeechVoice.Nova,
    "shimmer" => GeneratedSpeechVoice.Shimmer,
    _ => GeneratedSpeechVoice.Alloy
};
var response = await audioClient.GenerateSpeechAsync(text, voiceEnum, options, ct);
```
Signatures to keep (callers at `EmailGenerator.cs:1209` and `1324`): `Task<byte[]?> GenerateImageAsync(string prompt, string? operationName = null, CancellationToken ct = default)` and `Task<byte[]?> GenerateSpeechAsync(string text, string voice = "alloy", string? operationName = null, CancellationToken ct = default)`.

---

### `Services/ApiKeyStore.cs` (service, file-I/O)

**Analog A:** `Services/TelemetryService.cs` lines 16-19, 52-68 (path under `%LOCALAPPDATA%\ReelDiscovery`, create dir, write JSON, private nested settings class):
```csharp
private static readonly string SettingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ReelDiscovery",
    "settings.json");
...
var dir = Path.GetDirectoryName(SettingsPath);
if (!string.IsNullOrEmpty(dir))
    Directory.CreateDirectory(dir);
var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(SettingsPath, json);
...
private class TelemetrySettings { public bool TelemetryEnabled { get; set; } }
```
Differences: instance class with `ApiKeyStore(string? folder = null)` for test injection (not static); file name `keys.dat` (must NOT collide with `settings.json`); write temp file then `File.Move(tmp, path, overwrite: true)`; `ProtectedData.Protect/Unprotect(..., Entropy, DataProtectionScope.CurrentUser)`; `CryptographicException` → treat key as absent.

**Analog B — legacy format to migrate** (`UserControls/StepApiConfiguration.cs` lines 9, 340-379). `MigrateLegacyKey()` reads exactly this:
```csharp
private const string SettingsFileName = "reeldiscovery.settings";
...
var folder = Path.Combine(appData, "ReelDiscovery");
...
var encoded = File.ReadAllText(path);
return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
```
Then overwrite with empty text and delete (D-09). Never overwrite an existing DPAPI OpenAI key. Do not use the silent `catch { }` here for migration: RESEARCH says log it (not core, but should be visible).

---

### `Services/Ai/LlmRequest.cs`, `LlmResult.cs`, `LlmProviderRequest.cs` (models)

**Analog:** `Models/GenerationResult.cs` (lines 3-18) and `StorylineGenerationResult` (`Services/StorylineGenerator.cs:304-310`) — mutable class, auto-props, target-typed `new()` / `string.Empty` defaults, small related types in the same file:
```csharp
public class GenerationResult
{
    public int TotalEmailsGenerated { get; set; }
    ...
    public List<string> Errors { get; set; } = new();
    public bool WasCancelled { get; set; }
}

public class GenerationProgress
{ ... }
```
No `record`, no primary constructors (CLAUDE.md). Put `LlmOutcome`, `LlmFailureKind`, `LlmCallException` in `LlmResult.cs` like `GenerationProgress` shares `GenerationResult.cs`. Shape given in RESEARCH Pattern 1.

---

### `Services/Ai/JsonSchemaRegistry.cs` (static utility, transform)

**Analog:** static-class shape from `Services/TelemetryService.cs` (`public static class`, `private static readonly` fields, `/// <summary>` on public members). Use `ConcurrentDictionary<Type, string>` (codebase already uses `System.Collections.Concurrent`, e.g. `EmailGenerator.cs:1,16`). Schema options exactly as RESEARCH "Schema Generation" (`AIJsonUtilities.CreateJsonSchema` with `DisallowAdditionalProperties`, `RequireAllProperties`, `MoveDefaultKeywordToDescription`, `JsonSerializerDefaults.Web`, `JsonStringEnumConverter`).

---

### `Models/WizardState.cs` (state)

**Analog:** itself. Replace lines 5-8, 12 and 47-66:
```csharp
// Step 1 - API Configuration
public string ApiKey { get; set; } = string.Empty;
public string SelectedModel { get; set; } = "gpt-4o-mini";
public bool ConnectionTested { get; set; }
...
public AIModelConfig? SelectedModelConfig => AvailableModelConfigs.FirstOrDefault(m => m.ModelId == SelectedModel);
...
// Helper to create OpenAI service with tracking
public Services.OpenAIService CreateOpenAIService()
{
    var modelConfig = SelectedModelConfig;
    if (modelConfig != null)
        return new Services.OpenAIService(ApiKey, modelConfig, UsageTracker);
    return new Services.OpenAIService(ApiKey, SelectedModel);
}
```
→ `OpenAIApiKey`, `AnthropicApiKey`, `OpenAIKeyTested`, `AnthropicKeyTested`, `TextProvider` (default OpenAI, D-05), `StoryModelId`, `VolumeModelId`, computed `StoryModelConfig`/`VolumeModelConfig`, `IsMediaAvailable`, `MediaUnavailableNotice` (RESEARCH Pattern 7), `CanProceedFromKeys(...)` pure helper, `CreateGateway()` (passes `UsageTracker`), `CreateMediaClient()` (returns `null` when `!IsMediaAvailable`). Keep the "// Step N - ..." grouping comments. Fully qualify `Services.Ai.LlmGateway` as the existing code does with `Services.OpenAIService`. Delete the legacy `AvailableModels` array (lines 58-66) — it is touched by the port.

---

### `Models/AIModelConfig.cs` (catalog)

**Analog:** itself (lines 3-20 properties, 23-68 presets). Add `Provider` (`LlmProviderKind`), `DefaultForTier` (`LlmTier?`), `Effort` (`string?`), `MaxOutputTokens` (int). Replace the preset list with RESEARCH "Model Catalog Defaults" entries using the same object-initializer style:
```csharp
new AIModelConfig
{
    ModelId = "gpt-4o",
    DisplayName = "GPT-4o",
    InputTokenPricePerMillion = 2.50m,
    OutputTokenPricePerMillion = 10.00m,
    IsDefault = true
},
```
Note `CalculateInputCost(int tokens)` (line 11): Anthropic usage is `long`; cast or widen the parameter.

---

### `Models/TokenUsageTracker.cs` (thread-safe accumulator)

**Analog:** itself. Extend `RecordUsage` (lines 23-43) and `TokenUsageEntry` (103-111) with `Provider`; keep the `lock (_lock)` body pattern:
```csharp
public void RecordUsage(string operation, AIModelConfig model, int inputTokens, int outputTokens)
{
    var cost = model.CalculateTotalCost(inputTokens, outputTokens);
    lock (_lock)
    {
        TotalInputTokens += inputTokens;
        ...
        _entries.Add(new TokenUsageEntry { Timestamp = DateTime.Now, Operation = operation, ModelId = model.ModelId, ... });
    }
}
```
Provider can come from `model.Provider` so the signature need not change.

---

### `Models/GenerationResult.cs` (C4 error sink, C5 progress snapshot)

**Analog:** `Models/TokenUsageTracker.cs` lines 5 and 27-42 (private `_lock` object + `lock` around mutation). Add to `GenerationResult`:
```csharp
private readonly object _errorsLock = new();
public void AddError(string message) { lock (_errorsLock) { Errors.Add(message); } }
```
Keep `public List<string> Errors` for `StepGeneration.cs:199-204`. Add `public GenerationProgress Clone() => (GenerationProgress)MemberwiseClone();` to `GenerationProgress`.

---

### `Models/GenerationConfig.cs`

**Analog:** itself, image settings block (lines 18-20) — add in the same style with trailing `//` comment:
```csharp
// Image generation settings
public bool IncludeImages { get; set; } = false;
public int ImagePercentage { get; set; } = 10; // Percentage of emails ...
```
→ `public string ImageModel { get; set; } = "gpt-image-2.5-flare";` and `public string ImageQuality { get; set; } = "low";` (Pitfall 6).

---

### Generators: `Services/StorylineGenerator.cs`, `CharacterGenerator.cs`, `ThemeGenerator.cs` (generator, request-response)

**Analog:** `Services/ThemeGenerator.cs` (smallest complete example).

**Constructor to change** (ThemeGenerator lines 8-13; Storyline 8-14; Character 8, 15-18):
```csharp
private readonly OpenAIService _openAI;
public ThemeGenerator(OpenAIService openAI) { _openAI = openAI; }
```
→ `private readonly ILlmGateway _llm; public ThemeGenerator(ILlmGateway llm) { _llm = llm; }`

**Call site to change** (ThemeGenerator 94; StorylineGenerator 233-236; CharacterGenerator 319-322):
```csharp
var response = await _openAI.GetJsonCompletionAsync<StorylineApiResponse>(systemPrompt, userPrompt, "Storyline Generation", ct);
if (response == null)
    throw new InvalidOperationException("Failed to generate storylines");
```
→
```csharp
var result = await _llm.GenerateAsync<StorylineApiResponse>(new LlmRequest
{
    Tier = LlmTier.Story,                  // D-14: Story for storylines/characters, Volume for themes/emails
    OperationName = "Storyline Generation",
    SystemPrompt = systemPrompt,           // stable prefix
    UserPrompt = userPrompt,               // variable suffix
    MaxOutputTokens = 32_000               // Story calls (Opus 5.5 thinking budget, RESEARCH Open Q2)
}, ct);
var response = result.GetValueOrThrow();   // keeps the "throw on missing output" convention
```

**Prompt JSON template to REMOVE** (ThemeGenerator 78-92, the `Respond with JSON: {{ ... }}` block and "Respond with valid JSON only." at 57; same pattern in Storyline ~215-231 and Character ~300-317). Move field semantics into `[Description("...")]` on DTO properties (`using System.ComponentModel;`), e.g. `"RRGGBB (hex without #, main brand color)"` becomes the description on `PrimaryColor`.

**DTOs** (ThemeGenerator 156-187; Storyline 263-302; Character nested DTOs): change `private class` → `internal class` (still nested; Pitfall 7). Keep `[JsonPropertyName]`. Storyline `DateRangeDto? SuggestedDateRange` (line 270) → non-null `= new()` and adjust the null check at 252 to check for empty strings.

**Post-validation stays in C#** — ThemeGenerator's `SanitizeHexColor` (138-154) and `AllowedFonts`/`SanitizeFont` (190-216) are the model for clamps elsewhere.

**Random** — CharacterGenerator line 9 `private readonly Random _random = new();` → delete; uses at 31, 35, 62, 66, 72 → `Random.Shared` (C3).

---

### `Services/EmailGenerator.cs` (orchestrator, batch)

**Analog:** itself. Key excerpts to modify:

**Ctor / fields** (lines 10-26): `OpenAIService _openAI` → `ILlmGateway _llm` + `OpenAIMediaClient? _media`; delete `_random` (line 13, C1).

**Per-item isolation template = the existing storyline catch** (lines 120-140). Copy this into the attachment (159-207), image (224-257), calendar (278-291) and voicemail (307-338) lambdas, which today have only `try/finally`:
```csharp
catch (OperationCanceledException)
{
    throw; // Let cancellation propagate
}
catch (Exception ex)
{
    // Per-storyline error handling: log and continue with others
    result.Errors.Add($"Storyline '{storyline.Title}' failed: {ex.Message}");   // -> result.AddError(...)
    ...
}
finally
{
    semaphore.Release();
}
```
Add `catch (LlmCallException ex) when (ex.Result.IsFatal)` → cancel a linked CTS (fatal Auth/SpendLimit) before the generic catch. Move `SaveAllEmailsAsync` (359) so it also runs on fatal/cancel (B2).

**Progress under lock** (lines 93-98) — the correct pattern; apply everywhere and report a snapshot:
```csharp
lock (progressLock)
{
    progressData.CurrentStoryline = storyline.Title;
    progressData.CurrentOperation = $"Processing storyline: {storyline.Title}";
}
progress.Report(progressData);          // -> snapshot taken inside the lock, Report(snapshot) after
```
Lines that mutate outside the lock today: 147, 152, 156, 220-222, 267, 304, 348, 355 (C5).

**Media gating guard** (lines 214, 298, 483-488): `state.Config.IncludeImages` / `IncludeVoicemails` → `&& _media != null`. Image call at 1209 and TTS at 1324 → `_media.GenerateImageAsync` / `_media.GenerateSpeechAsync`.

**Text call sites** (531, 1318, 1484, 1543, 1609, 1781) — same transform as generators with `Tier = LlmTier.Volume`; remove the `Respond with JSON:` templates (e.g. 1478-1482, 1771-1777).

**Bug fixes** (exact code):
- B1 lines 926-929 `thread.Messages[e.ReplyToIndex]` → `internal static EmailMessage? ResolveReferencedEmail(IReadOnlyList<EmailMessage> messages, int batchOffset, int replyToIndex)`.
- B3 line 1796 `new DateTime(..., hour, minute, 0)` → `Math.Clamp(hour, 0, 23)`, `Math.Clamp(minute, 0, 59)`.
- B4 line 896 `.Where(c => c.Id != fromChar.Id).First()` → `.FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar`.
- B5 line 549 `emailsGenerated += response.Emails.Count` → trim `response.Emails` to `batchSize` before the loop at 542.
- C6 lines 1146/1170-1172 `chainState.VersionNumber++` → reserve the version under `lock (chainState)` before generating.

**DTOs** (1924-2036): `private class` → `internal class`; `EmailDto.CcEmails` `List<string>?` → `List<string> = new()` (and drop the `?? new List<string>()` at line 899); delete `ExcelDocResponseRaw` (2007-2017) and use `ExcelDocResponse` (1994-2005, already `List<List<string>>`); delete dead `ImageSuggestionResponse` (1736) and dead methods that call `_openAI`.

---

### `Helpers/DateHelper.cs`

**Analog:** itself. Delete line 5 `private static readonly Random _random = new();`; replace uses (29, 38, 61, 71, 102) with `Random.Shared` (C2). Same edit in `EmailGenerator` (273, 885, 1100, 1136, 1144, 1176, 1350, 1355, 1364, 1410).

---

### `UserControls/StepApiConfiguration.cs` (wizard step, rebuilt)

**Analog:** itself. Preserve these patterns while rebuilding per D-04..D-09:

**Fields / step contract** (lines 10-24): `null!` control fields with Hungarian names; `CanMoveNext` computed. New names: `_txtOpenAIKey`, `_txtAnthropicKey`, `_btnTestOpenAI`, `_btnTestAnthropic`, `_btnClearOpenAI`, `_btnClearAnthropic`, `_lblOpenAIStatus`, `_lblAnthropicStatus`, `_lblMediaStatus`, `_cboProvider`, `_cboStoryModel`, `_cboVolumeModel`. `CanMoveNext => _state.CanProceedFromKeys(...)`. Delete `SettingsFileName` (line 9) and `SaveApiKey`/`LoadApiKey`/`GetSettingsPath` (340-379).

**TableLayoutPanel with row-index comments** (lines 35-51) — keep the comment on every row style:
```csharp
var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(5) };
mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));  // API Key
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Model + button
```

**Key textbox** (lines 63-71) — replicate for Anthropic, keep `UseSystemPasswordChar = true` (ASVS V8):
```csharp
_txtApiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Font = new Font("Consolas", 11F), Margin = new Padding(0, 8, 0, 8) };
_txtApiKey.TextChanged += (s, e) => _connectionTested = false;
```

**Buttons** (lines 94-97, 114-117): `ButtonHelper.CreateButton("Test Connection", 160, 38, ButtonStyle.Primary)`; `ButtonStyle.Default` for Configure/Clear.

**Async test click with status label** (lines 213-263) — copy the structure: empty-key guard → disable button → gray "Testing..." → `try` (call key test; Green success; persist via `ApiKeyStore.SetKey`) / `catch (Exception ex)` Red / `finally` re-enable + `StateChanged?.Invoke(this, EventArgs.Empty)`. Status text must come from the classified key-test result (Auth / model not available / Network), not a bare bool.

**Model dropdown refresh** (lines 191-211) — reuse for both tier dropdowns, filtered by `m.Provider == selectedProvider`, falling back to `DefaultForTier` instead of `IsDefault`:
```csharp
var toSelect = _state.AvailableModelConfigs.FirstOrDefault(m => m.ModelId == currentSelection)
    ?? _state.AvailableModelConfigs.FirstOrDefault(m => m.IsDefault)
    ?? _state.AvailableModelConfigs.FirstOrDefault();
```

**Configure Models dialog launch** (lines 181-189) — keep `using var dialog = new ModelConfigurationDialog(...)`.

**Validation** (lines 318-338) — keep `MessageBox.Show(..., "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); return Task.FromResult(false);`, but require only the selected provider's tested key (D-06). Update the disclaimer text (150-151) to mention both providers.

---

### `UserControls/ModelConfigurationDialog.cs` (dialog, grid CRUD)

**Analog:** itself. Add new properties to the clone (lines 21-28) — every new `AIModelConfig` property (`Provider`, `DefaultForTier`, `Effort`, `MaxOutputTokens`) must be copied or it will be lost on OK:
```csharp
Models = models.Select(m => new AIModelConfig
{
    ModelId = m.ModelId,
    DisplayName = m.DisplayName,
    InputTokenPricePerMillion = m.InputTokenPricePerMillion,
    OutputTokenPricePerMillion = m.OutputTokenPricePerMillion,
    IsDefault = m.IsDefault
}).ToList();
```
Add a Provider column in the same style as lines 81-120 (use `DataGridViewComboBoxColumn` with `DataSource = Enum.GetValues<LlmProviderKind>()` and `DataPropertyName = "Provider"`), plus an Effort text column. Widen `this.Size` (line 36) if needed. Also check the Reset Defaults handler uses `AIModelConfig.GetDefaultModels()`.

---

### `UserControls/StepTopicInput.cs` / `StepGenerationConfig.cs` (media gating, PROV-06)

**Analog:** `StepGenerationConfig.cs` lines 237-240 (checkbox driving the dependent control's `Enabled`):
```csharp
_chkImages.CheckedChanged += (s, e) =>
{
    _numImagePercent.Enabled = _chkImages.Checked;
};
```
In `OnEnterStepAsync` (StepGenerationConfig ~529-535; StepTopicInput load), when `!_state.IsMediaAvailable`, set `_chkImages.Enabled = _chkVoicemails.Enabled = false`, uncheck both, disable the percent spinners, and show `_state.MediaUnavailableNotice` in an Orange label. In `ValidateStepAsync` (StepGenerationConfig 592-595; StepTopicInput 227-232) force `Config.IncludeImages/IncludeVoicemails = false` when unavailable. Rename the "AI-Generated Images (DALL-E)" header (line 226) and "uses DALL-E API" text (231).

---

### `UserControls/StepStorylines.cs`, `StepCharacters.cs`, `StepGeneration.cs`, `StepComplete.cs` (call-site swaps)

**Analog:** `StepStorylines.cs` lines 267-269:
```csharp
var openAI = _state.CreateOpenAIService();
var generator = new StorylineGenerator(openAI);
```
→ `var generator = new StorylineGenerator(_state.CreateGateway());`. Same at `StepCharacters.cs:299, 323` and `StepGeneration.cs:179-180` (`new EmailGenerator(_state.CreateGateway(), _state.CreateMediaClient())`).

**Log colors for outcomes** (`StepGeneration.cs:192-205`): reuse `AppendLog(..., Color.Orange)` for refusals/truncations and `Color.Red` for failures (Claude's discretion on outcome UX).

**StepComplete** lines 158 and 192 `_state.SelectedModel` → `$"{provider}: {story} / {volume}"` string; `TelemetryService.SendGenerationEventAsync` signature (line 73-79) unchanged (`string model`).

---

### `ReelDiscovery.csproj`, `Program.cs`

**csproj:** edit in place (lines 5, 27-31) and append the three `ItemGroup`s from RESEARCH "App csproj additions" (`Compile/EmbeddedResource/None Remove="ReelDiscovery.Tests/**"` with forward slashes, `InternalsVisibleTo`). Keep the publish block (lines 11-16) unconditional.

**Program.cs:** insert migration after the telemetry opt-in block (lines 20-26), before `Application.Run(new WizardForm())` (line 31). Follow the existing one-block-per-concern-with-comment style:
```csharp
// Show telemetry opt-in dialog on first run
if (!TelemetryService.HasMadeTelemetryChoice())
{ ... }
```
→ `// Migrate legacy Base64 API key file to DPAPI store` + `try { new ApiKeyStore().MigrateLegacyKey(); } catch (Exception ex) { /* surface, not swallow */ }`.

---

## Shared Patterns

### Error handling order (all services and generators)
**Source:** `Services/EmailGenerator.cs` lines 120-136 and 396-407
```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex) { result.Errors.Add(...); }
...
catch (OperationCanceledException) { result.WasCancelled = true; result.ElapsedTime = DateTime.Now - startTime; return result; }
```
Apply to: every parallel lambda in `EmailGenerator`, the gateway, both providers. Generators never catch SDK exceptions (only `LlmCallException`).

### Silent catch only for non-critical side effects, always commented
**Source:** `Services/TelemetryService.cs` lines 64-67, 94-97
```csharp
catch
{
    // Silently fail - telemetry should never interrupt user experience
}
```
Apply to: nothing new in core paths. `ApiKeyStore` decrypt failures return `null` via a typed `catch (CryptographicException)` with a comment.

### Thread-safe mutation
**Source:** `Models/TokenUsageTracker.cs` lines 5, 16-19, 27-42 (`private readonly object _lock = new();` + `lock (_lock)`); `EmailGenerator.cs:113` (`Interlocked.Add`)
Apply to: `GenerationResult.AddError`, progress snapshots, document-chain version reservation, `JsonSchemaRegistry` (use `ConcurrentDictionary`). `Random.Shared` in all new/touched code.

### UI step async boundary
**Source:** `UserControls/StepApiConfiguration.cs` lines 222-262 (disable → try → Green/Red status label → finally re-enable + `StateChanged`)
Apply to: both key Test buttons, Clear buttons.

### JSON DTO conventions
**Source:** `Services/ThemeGenerator.cs` lines 156-187
```csharp
private class ThemeDto
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;
```
Apply to: all DTOs, now `internal`, non-null lists/objects with `= new()`, `[Description]` for semantics, no DataAnnotations constraints.

### Prompt strings
**Source:** `Services/ThemeGenerator.cs` lines 39-57, 61-92 (`@"..."` system prompt, `$@"..."` user prompt with `{{ }}` escaping). Keep the verbatim-interpolated style for behavioural instructions; delete only the embedded JSON shape blocks.

### Service construction
**Source:** `Models/WizardState.cs` lines 47-56 (factory on state attaches `UsageTracker`). Apply to `CreateGateway()` / `CreateMediaClient()`; steps never `new` a provider directly.

## No Analog Found

| File | Role | Data Flow | Reason / Use Instead |
|------|------|-----------|----------------------|
| `global.json` | config | — | No SDK pin today. RESEARCH "global.json" (10.0.401, `latestFeature`). |
| `ReelDiscovery.slnx` | config | — | No solution file today. `dotnet new sln` on SDK 10 + add both projects. |
| `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` | test config | — | No test project. RESEARCH "Test csproj (verified passing)". |
| `ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs` | test utility | request-response | RESEARCH "Fake HTTP for both SDKs". `sealed class` is fine; only records and primary constructors are banned. |
| `ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs`, `StrictSchemaValidator.cs` | test utility | transform | RESEARCH "StrictSchemaValidator" rules. |
| `ReelDiscovery.Tests/**/*Tests.cs` | test | — | No tests exist. Name classes `{Area}Tests`, methods `Method_Condition_Expected`; filters in RESEARCH "Phase Requirements → Test Map" rely on these class names (`ApiKeyStoreTests`, `OutcomeClassificationTests`, `ConcurrencyTests`, `EmailGeneratorFixTests`, `MediaGatingTests`, `ModelCatalogTests`, `LlmGatewayTests`, `KeyTestTests`, `OpenAIMediaClientTests`, `DocumentRegressionTests`). |

## Metadata

**Analog search scope:** `Services/`, `Models/`, `Helpers/`, `UserControls/`, `Program.cs`, `ReelDiscovery.csproj`
**Files scanned:** 20 read or excerpted (OpenAIService, WizardState, AIModelConfig, TokenUsageTracker, GenerationResult, GenerationConfig, TelemetryService, StepApiConfiguration, ThemeGenerator, StorylineGenerator, CharacterGenerator, EmailGenerator (targeted ranges), DateHelper, ModelConfigurationDialog, StepStorylines, StepGeneration, StepTopicInput, StepGenerationConfig, StepComplete, Program)
**Pattern extraction date:** 2026-09-29
