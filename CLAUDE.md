<!-- GSD:project-start source:PROJECT.md -->
## Project

**ReelDiscovery**

ReelDiscovery is a Windows desktop app (.NET 8 WinForms wizard) that generates realistic, internally consistent eDiscovery sample data sets. The user describes a real-life-style dispute or investigation (e.g. "construction defect dispute between a window subcontractor, a general contractor and the developer on a midrise commercial project", or "correspondence inspired by Breaking Bad evidencing a criminal conspiracy"). The app first writes the legal "case guide" — a comprehensive Complaint / Petition / Investigation Memo plus an internal Cast of Key Characters and Summary of Key Facts & Events — and then generates a large email corpus (threads + attachments) that serves as the evidence of the alleged conduct, mixed with a user-controlled share of non-relevant material, plus an answer key describing which documents are relevant and why.

It is for people who need believable, coherent eDiscovery demo/training/test corpora — especially for demonstrating review workflows and automated relevance classification.

**Core Value:** Every relevant email in a multi-thousand-document corpus must stay faithful to one consistent case story (the core pleading and its key-facts timeline) — the corpus reads as the actual evidence behind the allegations, not a pile of disconnected emails.

### Constraints

- **Tech stack**: C# / .NET 8 WinForms, Windows x64, single-file self-contained publish — existing app and distribution model
- **Libraries**: MimeKit (EML), DocumentFormat.OpenXml (Office) — reuse existing builders
- **AI**: OpenAI remains supported; Claude added — provider abstraction needed
- **Scale**: ~5K emails per run — requires incremental persistence, bounded context per call, and cost control (Batch APIs)
- **Consistency**: generation must be grounded in structured case-guide data (cast, dated facts) rather than free-form context
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# (language version implied by the .NET 8 SDK default, C# 12) - All application code: `Program.cs`, `Forms/`, `UserControls/`, `Services/`, `Models/`, `Helpers/`
- XML (.resx) - WinForms resources: `Properties/Resources.resx`, `Forms/WizardForm.resx`, `UserControls/StepApiConfiguration.resx`, `UserControls/ModelConfigurationDialog.resx`
- Hand-written iCalendar (RFC 5545) text - generated in `Services/CalendarService.cs`
- HTML - email bodies built in `Helpers/HtmlEmailFormatter.cs`
## Runtime
- .NET 8 (`<TargetFramework>net8.0-windows</TargetFramework>`) - Windows-only
- Windows Forms (`<UseWindowsForms>true</UseWindowsForms>`), `<OutputType>WinExe</OutputType>`
- Target: Windows 10/11 x64 (`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`)
- No `global.json` present - builds with whatever SDK is installed (dev machine has 9.0.103 and 10.0.401; both can target net8.0)
- NuGet via `PackageReference` in `ReelDiscovery.csproj`
- Lockfile: missing (no `packages.lock.json`)
- No `.sln` file; the project is built directly from `ReelDiscovery.csproj`
- No `Directory.Build.props` / `Directory.Packages.props` (no central package management)
## Frameworks
- Windows Forms (.NET 8) - UI. The app is a multi-step wizard: `Forms/WizardForm.cs` hosts `UserControls/Step*.cs` controls implementing `UserControls/IWizardStep.cs`. UI is built in code (not the Designer) for most controls.
- `ApplicationConfiguration.Initialize()` bootstraps high-DPI/visual styles in `Program.cs`
- Not detected - no test project, no xUnit/NUnit/MSTest references
- .NET SDK (`Microsoft.NET.Sdk`)
- `ResXFileCodeGenerator` for `Properties/Resources.Designer.cs`
- Single-file self-contained publish configured in `ReelDiscovery.csproj`:
## Key Dependencies
- `OpenAI` 2.1.0 - Official OpenAI .NET SDK. All AI calls go through `Services/OpenAIService.cs` (chat completions, JSON-mode completions, DALL-E 3 images, TTS-1 speech). Uses `System.ClientModel` (`ApiKeyCredential`, `ClientResultException`) which comes transitively.
- `MimeKit` 4.18.1 - Builds and writes `.eml` files (`MimeMessage`, `MailboxAddress`, multipart bodies, attachments) in `Services/EmlFileService.cs`
- `DocumentFormat.OpenXml` 3.0.2 - Generates `.docx`, `.xlsx`, `.pptx` attachments in-memory in `Services/OfficeDocumentService.cs` (uses aliases `A = DocumentFormat.OpenXml.Drawing`, `P = DocumentFormat.OpenXml.Presentation`)
- `System.Text.Json` - JSON deserialization of LLM responses (`PropertyNameCaseInsensitive = true`) and settings files; `[JsonPropertyName]` attributes in models
- `System.Net.Http.HttpClient` - Telemetry POSTs in `Services/TelemetryService.cs`
- `System.Threading` (`SemaphoreSlim`, `Task.WhenAll`) - Parallel API calls in `Services/EmailGenerator.cs`
- `System.Collections.Concurrent` - Thread-safe collections during generation
- `System.Text.RegularExpressions` - Post-processing of generated text
## Configuration
- No environment variables, `appsettings.json`, or `IConfiguration` usage. All configuration is entered through the wizard UI and held in `Models/WizardState.cs` / `Models/GenerationConfig.cs`.
- OpenAI API key entered in `UserControls/StepApiConfiguration.cs`, persisted Base64-encoded (not encrypted) to `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings`
- Telemetry opt-in flag persisted as JSON to `%LOCALAPPDATA%\ReelDiscovery\settings.json` by `Services/TelemetryService.cs`
- Model catalog and per-million-token pricing hardcoded in `Models/AIModelConfig.cs` (`GetDefaultModels()`); editable at runtime via `UserControls/ModelConfigurationDialog.cs`
- Generation defaults in `Models/GenerationConfig.cs` (e.g. `TotalEmailCount = 50`, `ParallelThreads = 3`, `AttachmentPercentage = 20`)
- `.gitignore` lists `telemetry.config` as intended home for the webhook URL, but no code reads such a file
- `ReelDiscovery.csproj` - sole build config (target framework, publish settings, package references, embedded resources)
- Embedded resources: `Resources/**/*.png`, `*.jpg`, `*.ico` are embedded and loaded via `Assembly.GetManifestResourceStream("ReelDiscovery.Resources.<file>")` (e.g. `Forms/DisclaimerDialog.cs`, `Forms/WizardForm.cs`)
- `.claude/settings.local.json` - local Claude Code permissions (allows `dotnet build *`)
## Platform Requirements
- Windows with .NET SDK 8.0+ (Windows Desktop workload)
- `dotnet build` / `dotnet run` from project root
- OpenAI API key for any functional testing
- Distributed as a single self-contained `ReelDiscovery.exe` (win-x64) via `dotnet publish`; no installer, no runtime prerequisite
- Requires outbound HTTPS to `api.openai.com` (and optionally `hooks.slack.com` for telemetry)
- Output written to a user-chosen local folder (`FolderBrowserDialog` in `UserControls/StepGenerationConfig.cs`)
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming Patterns
- One primary public type per file, file name = type name in PascalCase: `Services/OpenAIService.cs`, `Models/Character.cs`, `Helpers/DateHelper.cs`.
- Small related types may share a file with the primary type (enums, result/progress classes):
- Wizard step controls are prefixed `Step`: `UserControls/StepApiConfiguration.cs`, `UserControls/StepTopicInput.cs`, `UserControls/StepStorylines.cs`, `UserControls/StepCharacters.cs`, `UserControls/StepGenerationConfig.cs`, `UserControls/StepGeneration.cs`, `UserControls/StepComplete.cs`.
- Suffix by role: `*Service` (I/O, external APIs: `OpenAIService`, `EmlFileService`, `TelemetryService`, `OfficeDocumentService`, `CalendarService`), `*Generator` (AI-driven content: `EmailGenerator`, `CharacterGenerator`, `StorylineGenerator`, `ThemeGenerator`), `*Helper` (static utilities), `*Dialog` (modal forms: `Forms/DisclaimerDialog.cs`, `Forms/TelemetryOptInDialog.cs`, `UserControls/ModelConfigurationDialog.cs`).
- PascalCase for all methods.
- Async methods end in `Async` and return `Task`/`Task<T>`: `GetCompletionAsync`, `GenerateCharactersAsync`, `SaveAllEmailsAsync`, `OnEnterStepAsync`.
- Verb-first names: `Generate*`, `Build*Prompt`, `Create*`, `Format*`, `Sanitize*`, `Distribute*`, `Apply*`/`Style*`.
- UI init methods: `InitializeUI()` in step controls, `InitializeComponent()` + `InitializeWizardUI()` in `Forms/WizardForm.cs`.
- Private instance fields: `_camelCase` (`_openAI`, `_state`, `_random`, `_usageTracker`).
- Private static readonly fields: mixed; both `_httpClient`/`_random` (`Services/TelemetryService.cs`, `Helpers/DateHelper.cs`) and PascalCase `MaleVoices`, `SettingsPath` (`Services/CharacterGenerator.cs`, `Services/TelemetryService.cs`) exist. Prefer `_camelCase` for mutable-instance-like statics (Random, HttpClient) and PascalCase for immutable data/paths.
- Constants: PascalCase (`MaxRetries`, `MaxCharactersPerBatch`, `WebhookUrl`, `SettingsFileName`).
- Locals and parameters: camelCase; `var` used almost universally for locals.
- WinForms control fields use Hungarian-style prefixes after the underscore: `_txtTopic`, `_btnNext`, `_lblProgress`, `_chkImages`, `_numStorylineCount`, `_gridStorylines`, `_progressBar`.
- Cancellation token parameter is named `ct` everywhere (`CancellationToken ct = default`).
- Classes, enums, interfaces in PascalCase; interfaces prefixed `I` (`UserControls/IWizardStep.cs`).
- Acronyms: `OpenAI`, `AI` kept uppercase (`OpenAIService`, `AIModelConfig`, `AISuggestedStartDate`); `Eml`, `Html` in PascalCase (`EmlFileService`, `HtmlEmailFormatter`).
- JSON response DTOs are private nested classes named `*Dto` / `*ApiResponse` inside the consuming generator (e.g. `CharacterApiResponse`, `CharacterDto` in `Services/CharacterGenerator.cs`; `EmailDto`, `SlideDto` in `Services/EmailGenerator.cs`).
## Code Style
- No formatter configured. Follow Visual Studio defaults as used throughout:
- Namespace mirrors folder: `ReelDiscovery.Models`, `ReelDiscovery.Services`, `ReelDiscovery.Helpers`, `ReelDiscovery.UserControls`, `ReelDiscovery.Forms`.
- `this.` is used on control property assignments inside UI code (`this.Padding = ...`, `this.Controls.Add(...)`) but not elsewhere.
- None (no analyzers, no `.editorconfig`, no `TreatWarningsAsErrors`). Nullable reference types are on; keep builds warning-free by hand.
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
- Within each group, alphabetical.
- Rely on implicit usings for `System`, `System.IO`, `System.Linq`, `System.Threading.Tasks`, `System.Windows.Forms`, `System.Drawing` — do not add explicit usings for these.
- No blank lines between using groups.
- Namespace aliases resolve OpenXml name collisions in `Services/OfficeDocumentService.cs`: `using A = DocumentFormat.OpenXml.Drawing;` and `using P = DocumentFormat.OpenXml.Presentation;`.
- Fully qualified names are used ad hoc to avoid ambiguity: `System.Windows.Forms.Timer` (WinForms vs threading timer), `Services.OpenAIService` inside `Models/WizardState.cs`.
## Error Handling
- **Retry with filtered catch in the API layer** (`Services/OpenAIService.cs`): loop `for (attempt < MaxRetries)` with `catch (ClientResultException ex) when (ex.Status == 429)` (exponential backoff `2^(attempt+1)` seconds), `when (ex.Status == 503)` (fixed 5s), and `when (ex.Status == 400)` (return `null`, no retry, for image/speech). After exhausting retries throw `InvalidOperationException` with a descriptive message. Replicate this shape for any new OpenAI call.
- **Null result means failure for optional media**: `GenerateImageAsync` / `GenerateSpeechAsync` return `byte[]?`; callers skip the attachment on `null`.
- **Generators throw on missing AI output**: `if (response == null) throw new InvalidOperationException("Failed to generate characters");` (`Services/CharacterGenerator.cs`).
- **Accumulate, don't abort, during bulk generation** (`Services/EmailGenerator.cs`): per-storyline `try/catch` rethrows `OperationCanceledException` first, then catches `Exception` and appends to `GenerationResult.Errors` (`$"Storyline '{storyline.Title}' failed: {ex.Message}"`). Top-level method catches `OperationCanceledException` → sets `result.WasCancelled = true` and returns the partial result.
- **Always catch `OperationCanceledException` before `Exception`** and let it propagate from inner work.
- **UI boundary** (step controls): wrap async operations in `try/catch/finally`; on error set a status label red (`_lblStatus.ForeColor = Color.Red`) or `AppendLog(..., Color.Red)`, include `ex.InnerException?.Message` when present; `finally` resets busy flags, re-enables buttons, hides `LoadingOverlay`, and fires `StateChanged` (`UserControls/StepStorylines.cs`, `UserControls/StepGeneration.cs`, `UserControls/StepCharacters.cs`).
- **Validation** in `ValidateStepAsync()` uses `MessageBox.Show(..., "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning)` and returns `Task.FromResult(false)`.
- **Silent bare `catch`** is used for non-critical side effects (telemetry, settings/API-key persistence, icon loading, connection test): `Services/TelemetryService.cs`, `UserControls/StepApiConfiguration.cs` (`SaveApiKey`/`LoadApiKey`), `Forms/WizardForm.cs` (`LoadIcon`), `Forms/DisclaimerDialog.cs`, `Helpers/HtmlEmailFormatter.cs`. Always add a comment explaining why the failure is ignored (`// Silently fail - telemetry should never interrupt user experience`). Do not use bare `catch` for core generation logic.
## Logging
- User-visible run log: `StepGeneration.AppendLog(string message, Color? color = null)` writes timestamped lines to a `RichTextBox` and marshals with `InvokeRequired`/`Invoke` (`UserControls/StepGeneration.cs`). Colors: Cyan = section start, Green = success, Yellow = cancellation, Orange/Red = errors.
- Progress reporting from services uses `IProgress<T>`: `IProgress<string>` for simple steps (`CharacterGenerator`, `StorylineGenerator`), `IProgress<GenerationProgress>` for email generation. Update shared progress objects under a `lock` and report after the lock.
- Errors surface via `GenerationResult.Errors` (list of strings) rather than a log sink.
- Remote telemetry is opt-in only via `Services/TelemetryService.cs` (`SendLaunchEventAsync`, `SendGenerationEventAsync`); fire-and-forget with `_ = TelemetryService.SendLaunchEventAsync();`.
## Comments
- Short `//` comments above logical blocks describing intent ("// Rate limited - exponential backoff", "// Generate unique Message-ID").
- Inline row-index comments on `TableLayoutPanel` row styles (`// 0: Topic label`) in step controls — keep this when adding rows.
- Explain every swallowed exception.
- Comment non-obvious numeric heuristics (`// ~65% internal`, `// 24px padding (12 each side)`).
- `/// <summary>` used selectively on public helpers, non-obvious model properties (`Models/Attachment.cs`, `Models/Character.VoiceId`), and public service methods (`TelemetryService`, `OpenAIService.GenerateSpeechAsync` with `<param>`/`<returns>`). Not required on every member; add it for public APIs whose behavior or allowed values are not obvious from the name.
## Function Design
- Optional trailing parameters: `IProgress<T>? progress = null, CancellationToken ct = default` — `ct` is always last.
- Optional `string? operationName = null` on `OpenAIService` calls labels token usage; always pass a human-readable name (e.g. `"Character Generation"`).
- Services receive dependencies through constructors (`new CharacterGenerator(openAI)`); `OpenAIService` is created via `WizardState.CreateOpenAIService()` so usage tracking is attached.
- AI generators return result objects (`CharacterGenerationResult`, `GenerationResult`) rather than tuples; tuples are used only for small internal helpers (`DateHelper.AllocateDateWindow` returns `(start, end)`).
- `Task.CompletedTask` / `Task.FromResult(...)` for synchronous implementations of async interface members (`IWizardStep`).
## Module Design
- Everything is `public` except `Program` (`internal static`) and nested DTOs/settings (`private class`). There is no deliberate internal API surface.
- Stateless utilities are `public static class` (`Helpers/*`, `Services/TelemetryService.cs`); stateful services are instance classes without interfaces (no DI container).
- UI is built entirely in code (no designer files for steps). Each step is `public class StepX : UserControl, IWizardStep` implementing `StepTitle`, `CanMoveNext`, `CanMoveBack`, `NextButtonText`, `StateChanged`, `BindState`, `OnEnterStepAsync`, `OnLeaveStepAsync`, `ValidateStepAsync`. Controls are created in `InitializeUI()` using object initializers and a `TableLayoutPanel` root docked `Fill`.
- Shared state flows through the single mutable `Models/WizardState.cs` instance passed to `BindState`; steps read it in `OnEnterStepAsync` and write it in `ValidateStepAsync`.
- Style buttons via `Helpers/ButtonHelper.CreateButton(text, minWidth, height, ButtonStyle.Primary)`; use `Segoe UI` fonts.
- Thread safety: shared counters use `Interlocked`, collections use `ConcurrentBag`/`ConcurrentDictionary`, trackers use `lock (_lock)` (`Models/TokenUsageTracker.cs`). Note `System.Random` instances (`EmailGenerator._random`, `DateHelper._random`) are shared across parallel tasks — use `Random.Shared` in new code.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## System Overview
```text
```
## Component Responsibilities
| Component | Responsibility | File |
|-----------|----------------|------|
| Program | App bootstrap; disclaimer gate; telemetry opt-in; launch event; runs WizardForm | `Program.cs` |
| WizardForm | Hosts step UserControls, navigation, validation gating, live token/cost display | `Forms/WizardForm.cs` |
| IWizardStep | Contract every wizard page implements (title, nav flags, lifecycle hooks) | `UserControls/IWizardStep.cs` |
| StepApiConfiguration | API key entry, connection test, model selection, key persistence (Base64 file) | `UserControls/StepApiConfiguration.cs` |
| ModelConfigurationDialog | Edit model list and token pricing | `UserControls/ModelConfigurationDialog.cs` |
| StepTopicInput | Topic, extra instructions, storyline count, media-type hints | `UserControls/StepTopicInput.cs` |
| StepStorylines | Calls StorylineGenerator, editable grid of storylines, AI date range | `UserControls/StepStorylines.cs` |
| StepCharacters | Calls CharacterGenerator + ThemeGenerator, editable character grid | `UserControls/StepCharacters.cs` |
| StepGenerationConfig | Email count, dates, attachment/image/voicemail/calendar percentages, output folder, parallelism, telemetry toggle | `UserControls/StepGenerationConfig.cs` |
| StepGeneration | Runs EmailGenerator with progress, log, cancellation | `UserControls/StepGeneration.cs` |
| StepComplete | Result summary, open output folder, sends telemetry event | `UserControls/StepComplete.cs` |
| WizardState | Single in-memory bag of all user inputs and generated data | `Models/WizardState.cs` |
| OpenAIService | Wrapper over OpenAI SDK: text, JSON-typed completion, image, speech; retry/backoff; token tracking | `Services/OpenAIService.cs` |
| StorylineGenerator | Topic -> storylines + suggested date range (batched at 15) | `Services/StorylineGenerator.cs` |
| CharacterGenerator | Storylines -> characters, company domain, TTS voice assignment (batched) | `Services/CharacterGenerator.cs` |
| ThemeGenerator | Per-email-domain color/font branding (sanitized hex + font allowlist) | `Services/ThemeGenerator.cs` |
| EmailGenerator | Orchestrates the full generation pipeline (threads, attachments, images, calendar, voicemail, save) | `Services/EmailGenerator.cs` |
| OfficeDocumentService | Builds Word/Excel/PowerPoint bytes with OpenXml | `Services/OfficeDocumentService.cs` |
| CalendarService | Builds .ics bytes | `Services/CalendarService.cs` |
| EmlFileService | Converts EmailMessage to MimeMessage and writes .eml | `Services/EmlFileService.cs` |
| TelemetryService | Static opt-in telemetry (settings.json + Slack webhook POST) | `Services/TelemetryService.cs` |
| TokenUsageTracker | Thread-safe token/cost accumulator | `Models/TokenUsageTracker.cs` |
| HtmlEmailFormatter | Plain text -> themed HTML email (quotes, forwards, lists, signature, disclaimer banner) | `Helpers/HtmlEmailFormatter.cs` |
| ThreadingHelper | Message-ID generation, In-Reply-To/References, RE:/FW: prefixes, quoting | `Helpers/ThreadingHelper.cs` |
## Pattern Overview
- Linear 7-step wizard; each step is a `UserControl` implementing `IWizardStep`, all instantiated up-front in `WizardForm.LoadSteps()` and toggled via `Visible`.
- One `WizardState` instance is created by `WizardForm` and pushed into every step through `BindState(WizardState)`. Steps communicate only through this object.
- Services are constructed ad hoc inside step event handlers (`new StorylineGenerator(_state.CreateOpenAIService())`). `OpenAIService` is the only injected dependency (constructor parameter).
- LLM output is always requested as JSON and deserialized into private nested DTO classes inside each generator (`ThreadApiResponse`, `CharacterDto`, `StorylineDto`, etc.), then mapped onto `Models/` types.
- Generation is a multi-pass pipeline: text first (with AI-planned attachment flags on each `EmailMessage`), then attachments/images/calendar/voicemails filled in afterward, then everything written to disk.
- UI is built entirely in code (no `.Designer.cs` files for forms/controls); `InitializeUI()` methods construct layouts with `TableLayoutPanel`/`Panel` and docking.
## Layers
- Purpose: Collect input, trigger generation, display progress/results.
- Location: `Forms/`, `UserControls/`
- Contains: `Form` subclasses, `UserControl` wizard steps, modal dialogs.
- Depends on: `Models/`, `Services/`, `Helpers/ButtonHelper.cs`, `Helpers/LoadingOverlay.cs`
- Used by: `Program.cs`
- Purpose: Plain data carriers plus a few computed properties.
- Location: `Models/`
- Contains: POCOs (`Storyline`, `Character`, `EmailThread`, `EmailMessage`, `Attachment`, `OrganizationTheme`), config (`GenerationConfig`, `AIModelConfig`), results (`GenerationResult`, `GenerationProgress`), `WizardState`, `TokenUsageTracker`.
- Depends on: Nothing except `WizardState.CreateOpenAIService()` which references `Services.OpenAIService` (models -> services back-reference).
- Used by: All layers.
- Purpose: AI calls, generation orchestration, file format production.
- Location: `Services/`
- Contains: Generator classes (AI-backed), format writers (OpenXml, iCalendar, MimeKit), static telemetry.
- Depends on: `Models/`, `Helpers/`, NuGet `OpenAI`, `MimeKit`, `DocumentFormat.OpenXml`.
- Used by: `UserControls/` step classes, `Program.cs` (telemetry).
- Purpose: Static utilities (dates, file names, threading headers, HTML formatting) and small reusable UI widgets.
- Location: `Helpers/`
- Contains: `static class` utilities and two UI helpers (`ButtonHelper`, `LoadingOverlay`).
- Depends on: `Models/`, embedded resources (disclaimer banner in `HtmlEmailFormatter`).
- Used by: Services and UserControls.
## Data Flow
### Primary Request Path (end-to-end dataset generation)
### Progress Reporting Flow
### Token/Cost Tracking Flow
- All session state lives in one `WizardState` instance owned by `WizardForm` (`Forms/WizardForm.cs:10`). No persistence between runs except the API key file and telemetry `settings.json` in `%LOCALAPPDATA%\ReelDiscovery\`.
- Steps hold references to UI controls and local flags (`_isLoading`, `_isGenerating`, `_generationComplete`) and raise `StateChanged` so the shell re-queries `CanMoveNext`/`CanMoveBack`.
- `EmailGenerator` holds per-run state (`_documentChains` `ConcurrentDictionary`, `_random`).
## Key Abstractions
- Purpose: Uniform lifecycle for wizard pages.
- Examples: `UserControls/StepApiConfiguration.cs`, `UserControls/StepGeneration.cs`
- Pattern: Interface on `UserControl`; shell casts `UserControl` to `IWizardStep`. Members: `StepTitle`, `CanMoveNext`, `CanMoveBack`, `NextButtonText`, `StateChanged`, `BindState`, `OnEnterStepAsync`, `OnLeaveStepAsync`, `ValidateStepAsync`.
- Purpose: Shared blackboard for the whole wizard, grouped by step in comments.
- Examples: `Models/WizardState.cs`
- Pattern: Mutable POCO with factory method `CreateOpenAIService()`.
- Purpose: Single gateway to OpenAI.
- Examples: `Services/OpenAIService.cs`
- Pattern: Thin facade; `GetJsonCompletionAsync<T>` forces JSON response format and deserializes case-insensitively; retry loop (`MaxRetries = 4`) with exponential backoff on 429, fixed 5s on 503, retry on `JsonException`; 400 on image/TTS returns `null`. Hard-coded models `dall-e-3` and `tts-1`.
- Purpose: One class per AI-generated artifact type.
- Examples: `Services/StorylineGenerator.cs` (`StorylineGenerationResult`), `Services/CharacterGenerator.cs` (`CharacterGenerationResult`), `Services/ThemeGenerator.cs`, `Services/EmailGenerator.cs`
- Pattern: Constructor takes `OpenAIService`; public `Generate*Async(..., IProgress<string>?, CancellationToken)`; prompt text built in private methods; private nested DTO classes with `[JsonPropertyName]`; batching when counts exceed a `MaxXPerBatch` constant (15).
- Purpose: Let the LLM decide which emails carry attachments during text generation, and fill content in later passes.
- Examples: `Models/EmailMessage.cs` (`PlannedHasDocument`, `PlannedDocumentType`, `PlannedHasImage`, `PlannedIsImageInline`, `PlannedHasVoicemail`, ...)
- Pattern: Two-phase generation (plan in text pass, realize in attachment passes).
- Purpose: Per-domain branding applied to HTML email and Office documents.
- Examples: `Models/PresentationTheme.cs` (`OrganizationTheme`, `OrganizationTheme.Default`; `PresentationTheme` is an empty legacy subclass)
- Pattern: Dictionary keyed by domain (case-insensitive) in `WizardState.DomainThemes`.
## Entry Points
- Location: `Program.cs`
- Triggers: `ReelDiscovery.exe` launch (`[STAThread] Main`)
- Responsibilities: `ApplicationConfiguration.Initialize()`, disclaimer gate (exit on decline), telemetry opt-in on first run, launch telemetry, `Application.Run(new WizardForm())`.
- Location: `Forms/WizardForm.cs` (`BtnNext_Click`, `BtnBack_Click`, `ShowStep`, public `NavigateToStep`, `EnableNavigation`)
- Triggers: User button clicks; steps may call back into the form.
- Responsibilities: Step lifecycle and navigation gating.
- Location: `UserControls/StepGeneration.cs` (`StartGenerationAsync`)
- Triggers: Entering step 6.
- Responsibilities: Create `EmailGenerator`, wire progress/cancel, log results.
## Architectural Constraints
- **Threading:** WinForms STA UI thread. All service work is `async`/`await` without `ConfigureAwait(false)`, so continuations generally resume on the UI thread's `SynchronizationContext`; concurrency inside `EmailGenerator` comes from `Task.WhenAll` over many tasks gated by a single `SemaphoreSlim(ParallelThreads)`. UI updates from progress callbacks use `InvokeRequired`/`Invoke`.
- **Global state:** `Services/TelemetryService.cs` is a `static class` with a static `HttpClient` and hard-coded webhook constant. `Helpers/DateHelper.cs` has a static shared `Random`. `WizardState` is the single per-session shared mutable object.
- **Circular imports:** `Models/WizardState.cs` references `Services.OpenAIService`, and `Services/*` reference `Models/*` (model <-> service coupling). `EmailGenerator` private methods accept the whole `WizardState`.
- **Memory:** All threads, messages and attachment `byte[]` payloads are held in memory until the final `SaveAllEmailsAsync`; content is nulled only after saving.
- **Single project:** No test project, no class library split; everything compiles into `ReelDiscovery.csproj` (net8.0-windows, single-file self-contained win-x64 publish).
- **Hard-coded AI models:** Image (`dall-e-3`) and TTS (`tts-1`) model IDs are literals in `Services/OpenAIService.cs`; chat model comes from `AIModelConfig` list in `Models/AIModelConfig.cs`.
## Anti-Patterns
### God class for generation
### Unsynchronized shared collections in parallel tasks
### Services constructed inside UI handlers
### Secrets and pseudo-secrets in source/plain files
## Error Handling
- UI steps wrap service calls in `try/catch (Exception)` and show the message (plus inner message) in a status label or log (`UserControls/StepStorylines.cs`, `UserControls/StepCharacters.cs`, `UserControls/StepGeneration.cs`).
- `EmailGenerator.GenerateEmailsAsync` never throws: per-storyline failures are appended to `GenerationResult.Errors`; `OperationCanceledException` sets `WasCancelled`; outer exceptions go to `Errors`.
- `OpenAIService` retries transient HTTP errors and throws `InvalidOperationException` after exhausting retries; image/TTS return `null` on failure.
- Settings/telemetry/icon loading use empty `catch { }` blocks (`Services/TelemetryService.cs`, `UserControls/StepApiConfiguration.cs`, `Forms/WizardForm.cs:LoadIcon`).
- Cancellation via `CancellationTokenSource` in `StepGeneration`, checked with `ct.ThrowIfCancellationRequested()` in loops.
## Cross-Cutting Concerns
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
