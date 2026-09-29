# Technology Stack

**Analysis Date:** 2026-09-29

## Languages

**Primary:**
- C# (language version implied by the .NET 8 SDK default, C# 12) - All application code: `Program.cs`, `Forms/`, `UserControls/`, `Services/`, `Models/`, `Helpers/`
  - Nullable reference types enabled (`<Nullable>enable</Nullable>` in `ReelDiscovery.csproj`)
  - Implicit usings enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
  - File-scoped namespaces used throughout (e.g. `namespace ReelDiscovery.Services;`)

**Secondary:**
- XML (.resx) - WinForms resources: `Properties/Resources.resx`, `Forms/WizardForm.resx`, `UserControls/StepApiConfiguration.resx`, `UserControls/ModelConfigurationDialog.resx`
- Hand-written iCalendar (RFC 5545) text - generated in `Services/CalendarService.cs`
- HTML - email bodies built in `Helpers/HtmlEmailFormatter.cs`

## Runtime

**Environment:**
- .NET 8 (`<TargetFramework>net8.0-windows</TargetFramework>`) - Windows-only
- Windows Forms (`<UseWindowsForms>true</UseWindowsForms>`), `<OutputType>WinExe</OutputType>`
- Target: Windows 10/11 x64 (`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`)
- No `global.json` present - builds with whatever SDK is installed (dev machine has 9.0.103 and 10.0.401; both can target net8.0)

**Package Manager:**
- NuGet via `PackageReference` in `ReelDiscovery.csproj`
- Lockfile: missing (no `packages.lock.json`)
- No `.sln` file; the project is built directly from `ReelDiscovery.csproj`
- No `Directory.Build.props` / `Directory.Packages.props` (no central package management)

## Frameworks

**Core:**
- Windows Forms (.NET 8) - UI. The app is a multi-step wizard: `Forms/WizardForm.cs` hosts `UserControls/Step*.cs` controls implementing `UserControls/IWizardStep.cs`. UI is built in code (not the Designer) for most controls.
- `ApplicationConfiguration.Initialize()` bootstraps high-DPI/visual styles in `Program.cs`

**Testing:**
- Not detected - no test project, no xUnit/NUnit/MSTest references

**Build/Dev:**
- .NET SDK (`Microsoft.NET.Sdk`)
- `ResXFileCodeGenerator` for `Properties/Resources.Designer.cs`
- Single-file self-contained publish configured in `ReelDiscovery.csproj`:
  - `PublishSingleFile=true`, `SelfContained=true`, `EnableCompressionInSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`

## Key Dependencies

**Critical:**
- `OpenAI` 2.1.0 - Official OpenAI .NET SDK. All AI calls go through `Services/OpenAIService.cs` (chat completions, JSON-mode completions, DALL-E 3 images, TTS-1 speech). Uses `System.ClientModel` (`ApiKeyCredential`, `ClientResultException`) which comes transitively.
- `MimeKit` 4.18.1 - Builds and writes `.eml` files (`MimeMessage`, `MailboxAddress`, multipart bodies, attachments) in `Services/EmlFileService.cs`
- `DocumentFormat.OpenXml` 3.0.2 - Generates `.docx`, `.xlsx`, `.pptx` attachments in-memory in `Services/OfficeDocumentService.cs` (uses aliases `A = DocumentFormat.OpenXml.Drawing`, `P = DocumentFormat.OpenXml.Presentation`)

**Infrastructure (BCL, no extra packages):**
- `System.Text.Json` - JSON deserialization of LLM responses (`PropertyNameCaseInsensitive = true`) and settings files; `[JsonPropertyName]` attributes in models
- `System.Net.Http.HttpClient` - Telemetry POSTs in `Services/TelemetryService.cs`
- `System.Threading` (`SemaphoreSlim`, `Task.WhenAll`) - Parallel API calls in `Services/EmailGenerator.cs`
- `System.Collections.Concurrent` - Thread-safe collections during generation
- `System.Text.RegularExpressions` - Post-processing of generated text

## Configuration

**Environment:**
- No environment variables, `appsettings.json`, or `IConfiguration` usage. All configuration is entered through the wizard UI and held in `Models/WizardState.cs` / `Models/GenerationConfig.cs`.
- OpenAI API key entered in `UserControls/StepApiConfiguration.cs`, persisted Base64-encoded (not encrypted) to `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings`
- Telemetry opt-in flag persisted as JSON to `%LOCALAPPDATA%\ReelDiscovery\settings.json` by `Services/TelemetryService.cs`
- Model catalog and per-million-token pricing hardcoded in `Models/AIModelConfig.cs` (`GetDefaultModels()`); editable at runtime via `UserControls/ModelConfigurationDialog.cs`
- Generation defaults in `Models/GenerationConfig.cs` (e.g. `TotalEmailCount = 50`, `ParallelThreads = 3`, `AttachmentPercentage = 20`)
- `.gitignore` lists `telemetry.config` as intended home for the webhook URL, but no code reads such a file

**Build:**
- `ReelDiscovery.csproj` - sole build config (target framework, publish settings, package references, embedded resources)
- Embedded resources: `Resources/**/*.png`, `*.jpg`, `*.ico` are embedded and loaded via `Assembly.GetManifestResourceStream("ReelDiscovery.Resources.<file>")` (e.g. `Forms/DisclaimerDialog.cs`, `Forms/WizardForm.cs`)
- `.claude/settings.local.json` - local Claude Code permissions (allows `dotnet build *`)

## Platform Requirements

**Development:**
- Windows with .NET SDK 8.0+ (Windows Desktop workload)
- `dotnet build` / `dotnet run` from project root
- OpenAI API key for any functional testing

**Production:**
- Distributed as a single self-contained `ReelDiscovery.exe` (win-x64) via `dotnet publish`; no installer, no runtime prerequisite
- Requires outbound HTTPS to `api.openai.com` (and optionally `hooks.slack.com` for telemetry)
- Output written to a user-chosen local folder (`FolderBrowserDialog` in `UserControls/StepGenerationConfig.cs`)

---

*Stack analysis: 2026-09-29*
