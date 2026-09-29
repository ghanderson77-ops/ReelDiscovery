# External Integrations

**Analysis Date:** 2026-09-29

## APIs & External Services

**AI / LLM - OpenAI (the only functional external dependency):**
- All access is centralized in `Services/OpenAIService.cs`. Construct it via `WizardState.CreateOpenAIService()` in `Models/WizardState.cs` so token usage is tracked; do not create `OpenAIClient` elsewhere.
  - SDK/Client: `OpenAI` 2.1.0 NuGet (`OpenAIClient`, `ChatClient`, `ImageClient`, `AudioClient`)
  - Auth: user-supplied API key (`ApiKeyCredential`), entered in `UserControls/StepApiConfiguration.cs`. No env var.
  - Client options: `NetworkTimeout = 10 minutes` (`CreateClient` in `Services/OpenAIService.cs`)
- Endpoints used:
  - **Chat Completions** - `GetCompletionAsync` (plain text) and `GetJsonCompletionAsync<T>` (JSON mode via `ChatResponseFormat.CreateJsonObjectFormat()`, deserialized with `System.Text.Json`). Model is user-selected; default `gpt-4o-mini` (`Models/WizardState.cs`). Presets in `Models/AIModelConfig.cs`: `gpt-4o`, `gpt-4o-mini`, `gpt-4-turbo`, `gpt-3.5-turbo`, `o1-preview`, `o1-mini`.
  - **Connection test** - `TestConnectionAsync` sends a tiny chat request (`MaxOutputTokenCount = 10`)
  - **Images** - `GenerateImageAsync` uses hardcoded model `dall-e-3`, 1024x1024, standard quality, bytes response
  - **Text-to-Speech** - `GenerateSpeechAsync` uses hardcoded model `tts-1`, MP3 output, voices alloy/echo/fable/onyx/nova/shimmer
- Callers: `Services/ThemeGenerator.cs`, `Services/StorylineGenerator.cs`, `Services/CharacterGenerator.cs`, `Services/EmailGenerator.cs`
- Retry policy (duplicated in each method, `MaxRetries = 4`):
  - HTTP 429 -> exponential backoff `2^(attempt+1)` seconds
  - HTTP 503 -> fixed 5 s delay
  - HTTP 400 (images/TTS) -> return `null`, no retry
  - `JsonException` (JSON mode) -> retry, rethrow on last attempt
- Concurrency: `Services/EmailGenerator.cs` throttles parallel calls with `SemaphoreSlim(GenerationConfig.ParallelThreads)` (default 3) and `Task.WhenAll`
- Cost tracking: `Models/TokenUsageTracker.cs` records `ChatTokenUsage` input/output tokens per operation and prices them with hardcoded rates from `Models/AIModelConfig.cs`. Image and TTS calls are not tracked.

**Telemetry - Slack Incoming Webhook (opt-in):**
- `Services/TelemetryService.cs` POSTs `{ "text": ... }` JSON to a Slack incoming-webhook URL
  - Client: static `HttpClient` with 5 s timeout
  - Auth: the webhook URL itself (a bearer-style secret) is hardcoded as a `const` in `Services/TelemetryService.cs` and committed to git. Treat it as a leaked secret; see CONCERNS.
  - Events: `SendLaunchEventAsync` (called from `Program.cs`), `SendGenerationEventAsync` (called from `UserControls/StepComplete.cs`)
  - Payload: app version, OS platform, topic (truncated to 50 chars), email/thread/attachment counts, model, duration
  - Gated by `IsTelemetryEnabled()`; opt-in dialog `Forms/TelemetryOptInDialog.cs` on first run; toggle in `UserControls/StepGenerationConfig.cs`
  - All failures swallowed silently

**Outbound links (browser launch only, no API):**
- `https://www.quikdata.com` opened via `Process.Start` from footer link in `Forms/WizardForm.cs`
- `https://platform.openai.com/api-keys` shown as help text in `UserControls/StepApiConfiguration.cs`
- `UserControls/StepComplete.cs` opens the output folder via `Process.Start`

## Data Storage

**Databases:**
- None

**File Storage:**
- Local filesystem only
  - Generated output: user-selected folder (`GenerationConfig.OutputFolder`); `.eml` files written by `Services/EmlFileService.cs`, optionally organized into per-sender subfolders (`OrganizeBySender`)
  - Attachments embedded inside `.eml` files: `.docx`/`.xlsx`/`.pptx` (`Services/OfficeDocumentService.cs`), `.ics` (`Services/CalendarService.cs`), `.png` images and `.mp3` voicemails (from OpenAI)
  - App settings: `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings` (Base64 API key) and `%LOCALAPPDATA%\ReelDiscovery\settings.json` (telemetry flag)

**Caching:**
- None (in-memory only during a generation run, e.g. document version chains in `Services/EmailGenerator.cs`)

## Authentication & Identity

**Auth Provider:**
- None for end users. The only credential is the user's own OpenAI API key.
  - Implementation: key typed into a `TextBox`, validated via `TestConnectionAsync`, stored with Base64 "obfuscation" (explicitly commented as not secure) in `UserControls/StepApiConfiguration.cs` (`SaveApiKey` / `LoadApiKey`). Windows DPAPI (`ProtectedData`) is not used.

## Monitoring & Observability

**Error Tracking:**
- None (no Sentry/App Insights). Errors surface via `MessageBox` in the UI or are swallowed.

**Logs:**
- No logging framework. Progress is reported to the UI through `IProgress<T>` callbacks during generation. Opt-in Slack telemetry is the only remote signal.

## CI/CD & Deployment

**Hosting:**
- Desktop app; distributed as a self-contained single-file `ReelDiscovery.exe` (README references GitHub Releases)

**CI Pipeline:**
- None (no `.github/workflows`, no Azure Pipelines config)

## Environment Configuration

**Required env vars:**
- None. The OpenAI API key is supplied through the UI at runtime.

**Secrets location:**
- OpenAI API key: `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings` on the user's machine (Base64, not encrypted)
- Slack webhook URL: hardcoded in `Services/TelemetryService.cs` (committed). `.gitignore` reserves `telemetry.config` for it, but no code loads that file.

## Webhooks & Callbacks

**Incoming:**
- None

**Outgoing:**
- Slack incoming webhook (telemetry, opt-in) - `Services/TelemetryService.cs` `SendSlackMessageAsync`

## Output Format Contracts (consumers of generated data)

- `.eml` (RFC 5322 / MIME via MimeKit) with `Message-ID`, `In-Reply-To`, `References` threading headers and HTML-only bodies (`Helpers/HtmlEmailFormatter.cs`); fallback Message-IDs use `@generated.local`
- Intended import targets (per `README.md`): Outlook, Relativity, Nuix, Concordance and other e-discovery platforms
- `.ics` calendar invites (`METHOD:REQUEST`, `PRODID:-//ReelDiscovery//Email Generator//EN`)

---

*Integration audit: 2026-09-29*
