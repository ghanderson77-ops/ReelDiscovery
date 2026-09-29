<!-- refreshed: 2026-09-29 -->
# Architecture

**Analysis Date:** 2026-09-29

## System Overview

```text
┌──────────────────────────────────────────────────────────────────────────┐
│                     Startup (Program.Main, STA thread)                    │
│  DisclaimerDialog -> TelemetryOptInDialog (first run) -> WizardForm       │
│  `Program.cs`, `Forms/DisclaimerDialog.cs`, `Forms/TelemetryOptInDialog.cs`│
└──────────────────────────────────┬───────────────────────────────────────┘
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                    Wizard Shell  `Forms/WizardForm.cs`                    │
│  Owns the single WizardState, Back/Next/Cancel, header cost ticker        │
├───────────┬───────────┬───────────┬───────────┬───────────┬──────────────┤
│ Step 1    │ Step 2    │ Step 3    │ Step 4    │ Step 5    │ Step 6 / 7   │
│ ApiConfig │ TopicInput│ Storylines│ Characters│ GenConfig │ Generation / │
│           │           │           │           │           │ Complete     │
│ `UserControls/Step*.cs` (all implement `UserControls/IWizardStep.cs`)    │
└─────┬─────┴───────────┴─────┬─────┴─────┬─────┴───────────┴──────┬───────┘
      │ reads/writes          │           │                        │
      ▼                       ▼           ▼                        ▼
┌──────────────────────────────────────────────────────────────────────────┐
│          Shared mutable state  `Models/WizardState.cs`                    │
│  ApiKey, model config, Topic, Storylines, Characters, DomainThemes,       │
│  GenerationConfig, GeneratedThreads, Result, TokenUsageTracker            │
└──────────────────────────────────┬───────────────────────────────────────┘
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                      AI generation services  `Services/`                  │
│  StorylineGenerator  CharacterGenerator  ThemeGenerator  EmailGenerator   │
│            all built on  OpenAIService (chat JSON / DALL-E 3 / TTS-1)     │
└──────────────────────────────────┬───────────────────────────────────────┘
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│             Artifact builders  `Services/` + `Helpers/`                   │
│  OfficeDocumentService (.docx/.xlsx/.pptx)  CalendarService (.ics)        │
│  HtmlEmailFormatter (HTML body)  ThreadingHelper (Message-ID/References)  │
│  EmlFileService (MimeKit .eml writer)  FileNameHelper  DateHelper         │
└──────────────────────────────────┬───────────────────────────────────────┘
                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Output: .eml files on local disk (GenerationConfig.OutputFolder,         │
│  optionally one subfolder per sender).  Settings: %LOCALAPPDATA%\         │
│  ReelDiscovery\ (API key file, settings.json).  Telemetry: Slack webhook  │
└──────────────────────────────────────────────────────────────────────────┘
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

**Overall:** Single-project WinForms wizard with a code-behind UI layer, a shared mutable state object, and stateless-ish service classes that call OpenAI. No DI container, no MVVM/MVP, no persistence layer beyond local files.

**Key Characteristics:**
- Linear 7-step wizard; each step is a `UserControl` implementing `IWizardStep`, all instantiated up-front in `WizardForm.LoadSteps()` and toggled via `Visible`.
- One `WizardState` instance is created by `WizardForm` and pushed into every step through `BindState(WizardState)`. Steps communicate only through this object.
- Services are constructed ad hoc inside step event handlers (`new StorylineGenerator(_state.CreateOpenAIService())`). `OpenAIService` is the only injected dependency (constructor parameter).
- LLM output is always requested as JSON and deserialized into private nested DTO classes inside each generator (`ThreadApiResponse`, `CharacterDto`, `StorylineDto`, etc.), then mapped onto `Models/` types.
- Generation is a multi-pass pipeline: text first (with AI-planned attachment flags on each `EmailMessage`), then attachments/images/calendar/voicemails filled in afterward, then everything written to disk.
- UI is built entirely in code (no `.Designer.cs` files for forms/controls); `InitializeUI()` methods construct layouts with `TableLayoutPanel`/`Panel` and docking.

## Layers

**Presentation (Forms + UserControls):**
- Purpose: Collect input, trigger generation, display progress/results.
- Location: `Forms/`, `UserControls/`
- Contains: `Form` subclasses, `UserControl` wizard steps, modal dialogs.
- Depends on: `Models/`, `Services/`, `Helpers/ButtonHelper.cs`, `Helpers/LoadingOverlay.cs`
- Used by: `Program.cs`

**Domain Models:**
- Purpose: Plain data carriers plus a few computed properties.
- Location: `Models/`
- Contains: POCOs (`Storyline`, `Character`, `EmailThread`, `EmailMessage`, `Attachment`, `OrganizationTheme`), config (`GenerationConfig`, `AIModelConfig`), results (`GenerationResult`, `GenerationProgress`), `WizardState`, `TokenUsageTracker`.
- Depends on: Nothing except `WizardState.CreateOpenAIService()` which references `Services.OpenAIService` (models -> services back-reference).
- Used by: All layers.

**Services:**
- Purpose: AI calls, generation orchestration, file format production.
- Location: `Services/`
- Contains: Generator classes (AI-backed), format writers (OpenXml, iCalendar, MimeKit), static telemetry.
- Depends on: `Models/`, `Helpers/`, NuGet `OpenAI`, `MimeKit`, `DocumentFormat.OpenXml`.
- Used by: `UserControls/` step classes, `Program.cs` (telemetry).

**Helpers:**
- Purpose: Static utilities (dates, file names, threading headers, HTML formatting) and small reusable UI widgets.
- Location: `Helpers/`
- Contains: `static class` utilities and two UI helpers (`ButtonHelper`, `LoadingOverlay`).
- Depends on: `Models/`, embedded resources (disclaimer banner in `HtmlEmailFormatter`).
- Used by: Services and UserControls.

## Data Flow

### Primary Request Path (end-to-end dataset generation)

1. `Program.Main` shows `DisclaimerDialog`, optional `TelemetryOptInDialog`, fires `TelemetryService.SendLaunchEventAsync()` (fire-and-forget), runs `WizardForm` (`Program.cs:9-31`).
2. `WizardForm.LoadSteps` creates all 7 steps, calls `BindState(_state)` and subscribes to `StateChanged` for nav button refresh (`Forms/WizardForm.cs:210-233`).
3. Next click: `ValidateStepAsync()` on current step, then `ShowStep(i+1)` which awaits `OnLeaveStepAsync` / `OnEnterStepAsync` (`Forms/WizardForm.cs:235-311`).
4. Step 1 tests the key via `OpenAIService.TestConnectionAsync` and stores `ApiKey`/`SelectedModel` in state (`UserControls/StepApiConfiguration.cs:230`).
5. Step 3 `OnEnterStepAsync` auto-generates when `Storylines` is empty: `StorylineGenerator.GenerateStorylinesAsync` -> `state.Storylines`, `AISuggestedStartDate/EndDate` (`UserControls/StepStorylines.cs:259-331`).
6. Step 4 runs `CharacterGenerator.GenerateCharactersAsync` -> `state.Characters`, `CompanyDomain`, then `ThemeGenerator.GenerateThemesForDomainsAsync` -> `state.DomainThemes` (`UserControls/StepCharacters.cs:290-340`).
7. Step 6 runs `EmailGenerator.GenerateEmailsAsync(_state, progress, _cts.Token)` (`UserControls/StepGeneration.cs:179-195`).
8. `EmailGenerator` pipeline (`Services/EmailGenerator.cs:37-408`):
   - Distribute email count across storylines (`DistributeEmails`, proportional to `SuggestedEmailCount`).
   - Allocate a date window per storyline (`DateHelper.AllocateDateWindow`).
   - Parallel per storyline (bounded by `SemaphoreSlim(ParallelThreads)`): `GenerateThreadsForStorylineAsync` asks the LLM for batches of up to 15 emails, passing narrative context from prior batches and a phase (BEGINNING/MIDDLE/CONCLUSION); DTOs mapped by `ConvertDtoToEmailMessage` which fixes signatures (`CorrectSignatureBlock`) and renders `BodyHtml` via `HtmlEmailFormatter.ConvertToHtml`; then `ThreadingHelper.SetupThreading`.
   - Parallel pass: documents for emails with `PlannedHasDocument` (`GeneratePlannedDocumentAsync`, with version chains in `_documentChains`).
   - Parallel pass: DALL-E images for `PlannedHasImage` (inline via `cid:` or attached).
   - Parallel pass: calendar detection on a random `CalendarInvitePercentage` sample (`DetectAndAddCalendarInviteAsync` -> `CalendarService`).
   - Parallel pass: TTS voicemails for `PlannedHasVoicemail`.
   - `EmlFileService.SaveAllEmailsAsync` writes every message sequentially, then attachment byte arrays are nulled to free memory.
   - `state.GeneratedThreads` and `state.Result` set.
9. Step 7 shows `state.Result` and calls `TelemetryService.SendGenerationEventAsync` (`UserControls/StepComplete.cs:187`).

### Progress Reporting Flow

1. Step creates `Progress<T>` on the UI thread (`Progress<GenerationProgress>` in `StepGeneration`, `Progress<string>` in Storylines/Characters).
2. Services call `progress.Report(...)` from worker continuations; a shared mutable `GenerationProgress` instance is mutated under `progressLock` and reported.
3. `WizardForm` separately polls `state.UsageTracker` every 1s with a `System.Windows.Forms.Timer`; `StepGeneration` polls every 500ms.

### Token/Cost Tracking Flow

1. `WizardState.CreateOpenAIService()` passes `SelectedModelConfig` and the shared `UsageTracker`.
2. `OpenAIService.TrackUsage` calls `TokenUsageTracker.RecordUsage` after every chat completion (image/TTS calls are not tracked).
3. Tracker is lock-protected; totals read by UI timers.

**State Management:**
- All session state lives in one `WizardState` instance owned by `WizardForm` (`Forms/WizardForm.cs:10`). No persistence between runs except the API key file and telemetry `settings.json` in `%LOCALAPPDATA%\ReelDiscovery\`.
- Steps hold references to UI controls and local flags (`_isLoading`, `_isGenerating`, `_generationComplete`) and raise `StateChanged` so the shell re-queries `CanMoveNext`/`CanMoveBack`.
- `EmailGenerator` holds per-run state (`_documentChains` `ConcurrentDictionary`, `_random`).

## Key Abstractions

**IWizardStep:**
- Purpose: Uniform lifecycle for wizard pages.
- Examples: `UserControls/StepApiConfiguration.cs`, `UserControls/StepGeneration.cs`
- Pattern: Interface on `UserControl`; shell casts `UserControl` to `IWizardStep`. Members: `StepTitle`, `CanMoveNext`, `CanMoveBack`, `NextButtonText`, `StateChanged`, `BindState`, `OnEnterStepAsync`, `OnLeaveStepAsync`, `ValidateStepAsync`.

**WizardState:**
- Purpose: Shared blackboard for the whole wizard, grouped by step in comments.
- Examples: `Models/WizardState.cs`
- Pattern: Mutable POCO with factory method `CreateOpenAIService()`.

**OpenAIService:**
- Purpose: Single gateway to OpenAI.
- Examples: `Services/OpenAIService.cs`
- Pattern: Thin facade; `GetJsonCompletionAsync<T>` forces JSON response format and deserializes case-insensitively; retry loop (`MaxRetries = 4`) with exponential backoff on 429, fixed 5s on 503, retry on `JsonException`; 400 on image/TTS returns `null`. Hard-coded models `dall-e-3` and `tts-1`.

**Generator + private DTO + Result class:**
- Purpose: One class per AI-generated artifact type.
- Examples: `Services/StorylineGenerator.cs` (`StorylineGenerationResult`), `Services/CharacterGenerator.cs` (`CharacterGenerationResult`), `Services/ThemeGenerator.cs`, `Services/EmailGenerator.cs`
- Pattern: Constructor takes `OpenAIService`; public `Generate*Async(..., IProgress<string>?, CancellationToken)`; prompt text built in private methods; private nested DTO classes with `[JsonPropertyName]`; batching when counts exceed a `MaxXPerBatch` constant (15).

**Planned-attachment flags:**
- Purpose: Let the LLM decide which emails carry attachments during text generation, and fill content in later passes.
- Examples: `Models/EmailMessage.cs` (`PlannedHasDocument`, `PlannedDocumentType`, `PlannedHasImage`, `PlannedIsImageInline`, `PlannedHasVoicemail`, ...)
- Pattern: Two-phase generation (plan in text pass, realize in attachment passes).

**OrganizationTheme:**
- Purpose: Per-domain branding applied to HTML email and Office documents.
- Examples: `Models/PresentationTheme.cs` (`OrganizationTheme`, `OrganizationTheme.Default`; `PresentationTheme` is an empty legacy subclass)
- Pattern: Dictionary keyed by domain (case-insensitive) in `WizardState.DomainThemes`.

## Entry Points

**Application:**
- Location: `Program.cs`
- Triggers: `ReelDiscovery.exe` launch (`[STAThread] Main`)
- Responsibilities: `ApplicationConfiguration.Initialize()`, disclaimer gate (exit on decline), telemetry opt-in on first run, launch telemetry, `Application.Run(new WizardForm())`.

**Wizard navigation:**
- Location: `Forms/WizardForm.cs` (`BtnNext_Click`, `BtnBack_Click`, `ShowStep`, public `NavigateToStep`, `EnableNavigation`)
- Triggers: User button clicks; steps may call back into the form.
- Responsibilities: Step lifecycle and navigation gating.

**Generation run:**
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

**What happens:** `Services/EmailGenerator.cs` (~2,000 lines) contains orchestration, prompt construction, DTO mapping, signature correction, document versioning, image/voicemail/calendar generation, and all DTOs. It also contains unreferenced legacy paths (`GenerateAttachmentAsync`, `GenerateImageForEmailAsync`, `GenerateVoicemailForEmailAsync`, `SelectEmailsForAttachments`).
**Why it's wrong:** Hard to navigate and change safely; dead code confuses which path is live.
**Do this instead:** Put new artifact types in their own service (follow `Services/CalendarService.cs` / `Services/OfficeDocumentService.cs` for byte producers, `Services/ThemeGenerator.cs` for AI-backed generators) and call them from the `GenerateEmailsAsync` pipeline. Live paths are the `GeneratePlanned*Async` methods.

### Unsynchronized shared collections in parallel tasks

**What happens:** In `EmailGenerator.GenerateEmailsAsync`, the per-storyline catch block calls `result.Errors.Add(...)` (a `List<string>`) outside `progressLock`, while counters are incremented under the lock.
**Why it's wrong:** Concurrent `List<T>.Add` is not thread-safe if continuations run on the thread pool.
**Do this instead:** Mutate `result` only inside `lock (progressLock)` or use `ConcurrentBag`/`Interlocked`, matching the counter updates in the same method.

### Services constructed inside UI handlers

**What happens:** Steps `new` up `OpenAIService`, generators, and call static `TelemetryService` directly (`UserControls/StepStorylines.cs:268`, `UserControls/StepCharacters.cs:299`, `UserControls/StepGeneration.cs:179`).
**Why it's wrong:** No seam for testing or swapping implementations.
**Do this instead:** Keep using `_state.CreateOpenAIService()` as the single factory (never `new OpenAIService(...)` directly except in the connection test) so usage tracking stays wired; keep UI handlers thin and push logic into `Services/`.

### Secrets and pseudo-secrets in source/plain files

**What happens:** The telemetry Slack webhook URL is a `const` in `Services/TelemetryService.cs`; the OpenAI API key is stored Base64-encoded (not encrypted) by `UserControls/StepApiConfiguration.cs` (`SaveApiKey`).
**Why it's wrong:** Webhook is extractable from the binary/repo; Base64 is not protection.
**Do this instead:** Use DPAPI (`ProtectedData`) for the API key and do not add further secrets as literals.

## Error Handling

**Strategy:** Catch-and-report at the UI boundary; best-effort continuation inside the generation pipeline; silent swallow for non-critical I/O.

**Patterns:**
- UI steps wrap service calls in `try/catch (Exception)` and show the message (plus inner message) in a status label or log (`UserControls/StepStorylines.cs`, `UserControls/StepCharacters.cs`, `UserControls/StepGeneration.cs`).
- `EmailGenerator.GenerateEmailsAsync` never throws: per-storyline failures are appended to `GenerationResult.Errors`; `OperationCanceledException` sets `WasCancelled`; outer exceptions go to `Errors`.
- `OpenAIService` retries transient HTTP errors and throws `InvalidOperationException` after exhausting retries; image/TTS return `null` on failure.
- Settings/telemetry/icon loading use empty `catch { }` blocks (`Services/TelemetryService.cs`, `UserControls/StepApiConfiguration.cs`, `Forms/WizardForm.cs:LoadIcon`).
- Cancellation via `CancellationTokenSource` in `StepGeneration`, checked with `ct.ThrowIfCancellationRequested()` in loops.

## Cross-Cutting Concerns

**Logging:** No logging framework. Generation log is a `RichTextBox` in `UserControls/StepGeneration.cs` (`AppendLog`). No file logs.
**Validation:** Per-step `ValidateStepAsync()` returning `bool`, with `MessageBox` for user feedback; AI output sanitized in `Services/ThemeGenerator.cs` (`SanitizeHexColor`, `SanitizeFont` allowlist) and file names via `Helpers/FileNameHelper.cs`.
**Authentication:** OpenAI API key only, entered in step 1 and passed as `ApiKeyCredential`; persisted Base64 in `%LOCALAPPDATA%\ReelDiscovery\`.
**Cost tracking:** `Models/TokenUsageTracker.cs` + `Models/AIModelConfig.cs` pricing, surfaced in `WizardForm` header.
**Telemetry:** Opt-in, `Services/TelemetryService.cs`, preference in `%LOCALAPPDATA%\ReelDiscovery\settings.json`.

---

*Architecture analysis: 2026-09-29*
