# Coding Conventions

**Analysis Date:** 2026-09-29

Project: single-project .NET 8 WinForms app (`ReelDiscovery.csproj`, `net8.0-windows`, `Nullable=enable`, `ImplicitUsings=enable`). There is no `.sln`, `.editorconfig`, analyzer package, or formatter config in the repo, so conventions are defined by existing code and must be followed by hand.

## Naming Patterns

**Files:**
- One primary public type per file, file name = type name in PascalCase: `Services/OpenAIService.cs`, `Models/Character.cs`, `Helpers/DateHelper.cs`.
- Small related types may share a file with the primary type (enums, result/progress classes):
  - `Models/Attachment.cs` holds `AttachmentType`, `AttachmentComplexity`, and `Attachment`.
  - `Models/GenerationResult.cs` holds `GenerationResult` and `GenerationProgress`.
  - `Services/CharacterGenerator.cs` ends with public `CharacterGenerationResult`.
  - `Helpers/ButtonHelper.cs` ends with `enum ButtonStyle`.
- Wizard step controls are prefixed `Step`: `UserControls/StepApiConfiguration.cs`, `UserControls/StepTopicInput.cs`, `UserControls/StepStorylines.cs`, `UserControls/StepCharacters.cs`, `UserControls/StepGenerationConfig.cs`, `UserControls/StepGeneration.cs`, `UserControls/StepComplete.cs`.
- Suffix by role: `*Service` (I/O, external APIs: `OpenAIService`, `EmlFileService`, `TelemetryService`, `OfficeDocumentService`, `CalendarService`), `*Generator` (AI-driven content: `EmailGenerator`, `CharacterGenerator`, `StorylineGenerator`, `ThemeGenerator`), `*Helper` (static utilities), `*Dialog` (modal forms: `Forms/DisclaimerDialog.cs`, `Forms/TelemetryOptInDialog.cs`, `UserControls/ModelConfigurationDialog.cs`).

**Functions:**
- PascalCase for all methods.
- Async methods end in `Async` and return `Task`/`Task<T>`: `GetCompletionAsync`, `GenerateCharactersAsync`, `SaveAllEmailsAsync`, `OnEnterStepAsync`.
- Verb-first names: `Generate*`, `Build*Prompt`, `Create*`, `Format*`, `Sanitize*`, `Distribute*`, `Apply*`/`Style*`.
- UI init methods: `InitializeUI()` in step controls, `InitializeComponent()` + `InitializeWizardUI()` in `Forms/WizardForm.cs`.

**Variables:**
- Private instance fields: `_camelCase` (`_openAI`, `_state`, `_random`, `_usageTracker`).
- Private static readonly fields: mixed; both `_httpClient`/`_random` (`Services/TelemetryService.cs`, `Helpers/DateHelper.cs`) and PascalCase `MaleVoices`, `SettingsPath` (`Services/CharacterGenerator.cs`, `Services/TelemetryService.cs`) exist. Prefer `_camelCase` for mutable-instance-like statics (Random, HttpClient) and PascalCase for immutable data/paths.
- Constants: PascalCase (`MaxRetries`, `MaxCharactersPerBatch`, `WebhookUrl`, `SettingsFileName`).
- Locals and parameters: camelCase; `var` used almost universally for locals.
- WinForms control fields use Hungarian-style prefixes after the underscore: `_txtTopic`, `_btnNext`, `_lblProgress`, `_chkImages`, `_numStorylineCount`, `_gridStorylines`, `_progressBar`.
- Cancellation token parameter is named `ct` everywhere (`CancellationToken ct = default`).

**Types:**
- Classes, enums, interfaces in PascalCase; interfaces prefixed `I` (`UserControls/IWizardStep.cs`).
- Acronyms: `OpenAI`, `AI` kept uppercase (`OpenAIService`, `AIModelConfig`, `AISuggestedStartDate`); `Eml`, `Html` in PascalCase (`EmlFileService`, `HtmlEmailFormatter`).
- JSON response DTOs are private nested classes named `*Dto` / `*ApiResponse` inside the consuming generator (e.g. `CharacterApiResponse`, `CharacterDto` in `Services/CharacterGenerator.cs`; `EmailDto`, `SlideDto` in `Services/EmailGenerator.cs`).

## Code Style

**Formatting:**
- No formatter configured. Follow Visual Studio defaults as used throughout:
  - 4-space indentation, Allman braces (opening brace on its own line).
  - File-scoped namespaces: `namespace ReelDiscovery.Services;`
  - Single-statement `if` bodies may omit braces when on the next line (`if (response == null)\n    throw ...;`), and early-return guards may be one-liners (`if (string.IsNullOrEmpty(text)) return "> ";`).
  - Long parameter lists are broken one-per-line with the closing paren on the last parameter line (see `OpenAIService.GetCompletionAsync`).
- Namespace mirrors folder: `ReelDiscovery.Models`, `ReelDiscovery.Services`, `ReelDiscovery.Helpers`, `ReelDiscovery.UserControls`, `ReelDiscovery.Forms`.
- `this.` is used on control property assignments inside UI code (`this.Padding = ...`, `this.Controls.Add(...)`) but not elsewhere.

**Linting:**
- None (no analyzers, no `.editorconfig`, no `TreatWarningsAsErrors`). Nullable reference types are on; keep builds warning-free by hand.

**Modern C# features in use (use them in new code):**
- Nullable annotations (`string?`, `AIModelConfig?`) and `null!` for fields initialized in `InitializeUI()`: `private TextBox _txtTopic = null!;`
- Target-typed `new()` for fields/collections: `public List<Character> Characters { get; set; } = new();`
- Switch expressions for mapping: `Attachment.MimeType`, `Attachment.Extension` (`Models/Attachment.cs`), voice mapping in `OpenAIService.GenerateSpeechAsync`.
- Range/index slicing: `topic[..50]`, `email.Id.ToString("N")[..6]`.
- `[GeneratedRegex]` source-generated regex with `partial` class (`Helpers/FileNameHelper.cs`) — use this pattern instead of `new Regex(...)`.
- Collection expressions appear in `Services/CharacterGenerator.cs` (`["echo", "onyx", "fable"]`); `new[] { ... }` is more common elsewhere. Either is acceptable.
- `using var` declarations for disposables (`Program.cs`, `Helpers/ButtonHelper.cs`, `Forms/WizardForm.cs`).
- Verbatim interpolated strings (`$@"..."`) for multi-line AI prompts, with `{{`/`}}` escaping JSON braces (`Services/CharacterGenerator.cs`, `Services/EmailGenerator.cs`).
- No `record` types, no primary constructors, no `#region` blocks. Keep models as mutable classes with auto-properties.

## Import Organization

**Order:**
1. `System.*` namespaces (e.g. `System.ClientModel`, `System.Net.Http`, `System.Text.Json`)
2. Third-party packages (`OpenAI`, `OpenAI.Chat`, `MimeKit`, `DocumentFormat.OpenXml.*`)
3. Project namespaces (`ReelDiscovery.Helpers`, `ReelDiscovery.Models`, `ReelDiscovery.Services`, `ReelDiscovery.UserControls`)

- Within each group, alphabetical.
- Rely on implicit usings for `System`, `System.IO`, `System.Linq`, `System.Threading.Tasks`, `System.Windows.Forms`, `System.Drawing` — do not add explicit usings for these.
- No blank lines between using groups.

**Path Aliases:**
- Namespace aliases resolve OpenXml name collisions in `Services/OfficeDocumentService.cs`: `using A = DocumentFormat.OpenXml.Drawing;` and `using P = DocumentFormat.OpenXml.Presentation;`.
- Fully qualified names are used ad hoc to avoid ambiguity: `System.Windows.Forms.Timer` (WinForms vs threading timer), `Services.OpenAIService` inside `Models/WizardState.cs`.

## Error Handling

**Patterns:**
- **Retry with filtered catch in the API layer** (`Services/OpenAIService.cs`): loop `for (attempt < MaxRetries)` with `catch (ClientResultException ex) when (ex.Status == 429)` (exponential backoff `2^(attempt+1)` seconds), `when (ex.Status == 503)` (fixed 5s), and `when (ex.Status == 400)` (return `null`, no retry, for image/speech). After exhausting retries throw `InvalidOperationException` with a descriptive message. Replicate this shape for any new OpenAI call.
- **Null result means failure for optional media**: `GenerateImageAsync` / `GenerateSpeechAsync` return `byte[]?`; callers skip the attachment on `null`.
- **Generators throw on missing AI output**: `if (response == null) throw new InvalidOperationException("Failed to generate characters");` (`Services/CharacterGenerator.cs`).
- **Accumulate, don't abort, during bulk generation** (`Services/EmailGenerator.cs`): per-storyline `try/catch` rethrows `OperationCanceledException` first, then catches `Exception` and appends to `GenerationResult.Errors` (`$"Storyline '{storyline.Title}' failed: {ex.Message}"`). Top-level method catches `OperationCanceledException` → sets `result.WasCancelled = true` and returns the partial result.
- **Always catch `OperationCanceledException` before `Exception`** and let it propagate from inner work.
- **UI boundary** (step controls): wrap async operations in `try/catch/finally`; on error set a status label red (`_lblStatus.ForeColor = Color.Red`) or `AppendLog(..., Color.Red)`, include `ex.InnerException?.Message` when present; `finally` resets busy flags, re-enables buttons, hides `LoadingOverlay`, and fires `StateChanged` (`UserControls/StepStorylines.cs`, `UserControls/StepGeneration.cs`, `UserControls/StepCharacters.cs`).
- **Validation** in `ValidateStepAsync()` uses `MessageBox.Show(..., "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning)` and returns `Task.FromResult(false)`.
- **Silent bare `catch`** is used for non-critical side effects (telemetry, settings/API-key persistence, icon loading, connection test): `Services/TelemetryService.cs`, `UserControls/StepApiConfiguration.cs` (`SaveApiKey`/`LoadApiKey`), `Forms/WizardForm.cs` (`LoadIcon`), `Forms/DisclaimerDialog.cs`, `Helpers/HtmlEmailFormatter.cs`. Always add a comment explaining why the failure is ignored (`// Silently fail - telemetry should never interrupt user experience`). Do not use bare `catch` for core generation logic.

## Logging

**Framework:** None. No `ILogger`, `Debug.WriteLine`, `Console`, or `Trace` calls exist.

**Patterns:**
- User-visible run log: `StepGeneration.AppendLog(string message, Color? color = null)` writes timestamped lines to a `RichTextBox` and marshals with `InvokeRequired`/`Invoke` (`UserControls/StepGeneration.cs`). Colors: Cyan = section start, Green = success, Yellow = cancellation, Orange/Red = errors.
- Progress reporting from services uses `IProgress<T>`: `IProgress<string>` for simple steps (`CharacterGenerator`, `StorylineGenerator`), `IProgress<GenerationProgress>` for email generation. Update shared progress objects under a `lock` and report after the lock.
- Errors surface via `GenerationResult.Errors` (list of strings) rather than a log sink.
- Remote telemetry is opt-in only via `Services/TelemetryService.cs` (`SendLaunchEventAsync`, `SendGenerationEventAsync`); fire-and-forget with `_ = TelemetryService.SendLaunchEventAsync();`.

## Comments

**When to Comment:**
- Short `//` comments above logical blocks describing intent ("// Rate limited - exponential backoff", "// Generate unique Message-ID").
- Inline row-index comments on `TableLayoutPanel` row styles (`// 0: Topic label`) in step controls — keep this when adding rows.
- Explain every swallowed exception.
- Comment non-obvious numeric heuristics (`// ~65% internal`, `// 24px padding (12 each side)`).

**JSDoc/TSDoc (XML doc):**
- `/// <summary>` used selectively on public helpers, non-obvious model properties (`Models/Attachment.cs`, `Models/Character.VoiceId`), and public service methods (`TelemetryService`, `OpenAIService.GenerateSpeechAsync` with `<param>`/`<returns>`). Not required on every member; add it for public APIs whose behavior or allowed values are not obvious from the name.

## Function Design

**Size:** No enforced limit. Existing large methods (`EmailGenerator.GenerateEmailsAsync`, prompt builders) run 100–370 lines; prefer extracting `Build*Prompt`, `Distribute*`, and `Generate*ForEmailAsync` helpers as `EmailGenerator` does.

**Parameters:**
- Optional trailing parameters: `IProgress<T>? progress = null, CancellationToken ct = default` — `ct` is always last.
- Optional `string? operationName = null` on `OpenAIService` calls labels token usage; always pass a human-readable name (e.g. `"Character Generation"`).
- Services receive dependencies through constructors (`new CharacterGenerator(openAI)`); `OpenAIService` is created via `WizardState.CreateOpenAIService()` so usage tracking is attached.

**Return Values:**
- AI generators return result objects (`CharacterGenerationResult`, `GenerationResult`) rather than tuples; tuples are used only for small internal helpers (`DateHelper.AllocateDateWindow` returns `(start, end)`).
- `Task.CompletedTask` / `Task.FromResult(...)` for synchronous implementations of async interface members (`IWizardStep`).

## Module Design

**Exports:**
- Everything is `public` except `Program` (`internal static`) and nested DTOs/settings (`private class`). There is no deliberate internal API surface.
- Stateless utilities are `public static class` (`Helpers/*`, `Services/TelemetryService.cs`); stateful services are instance classes without interfaces (no DI container).
- UI is built entirely in code (no designer files for steps). Each step is `public class StepX : UserControl, IWizardStep` implementing `StepTitle`, `CanMoveNext`, `CanMoveBack`, `NextButtonText`, `StateChanged`, `BindState`, `OnEnterStepAsync`, `OnLeaveStepAsync`, `ValidateStepAsync`. Controls are created in `InitializeUI()` using object initializers and a `TableLayoutPanel` root docked `Fill`.
- Shared state flows through the single mutable `Models/WizardState.cs` instance passed to `BindState`; steps read it in `OnEnterStepAsync` and write it in `ValidateStepAsync`.
- Style buttons via `Helpers/ButtonHelper.CreateButton(text, minWidth, height, ButtonStyle.Primary)`; use `Segoe UI` fonts.
- Thread safety: shared counters use `Interlocked`, collections use `ConcurrentBag`/`ConcurrentDictionary`, trackers use `lock (_lock)` (`Models/TokenUsageTracker.cs`). Note `System.Random` instances (`EmailGenerator._random`, `DateHelper._random`) are shared across parallel tasks — use `Random.Shared` in new code.

**Barrel Files:** Not applicable (C#). Namespaces map 1:1 to folders.

---

*Convention analysis: 2026-09-29*
