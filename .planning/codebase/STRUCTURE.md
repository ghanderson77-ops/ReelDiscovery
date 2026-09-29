# Codebase Structure

**Analysis Date:** 2026-09-29

## Directory Layout

```
ReelDiscovery/                      # Repo root = project root (no .sln committed; single project)
├── Program.cs                      # [STAThread] entry point: disclaimer -> telemetry opt-in -> WizardForm
├── ReelDiscovery.csproj            # net8.0-windows WinExe, NuGet refs, single-file publish, embedded resources
├── README.md                       # User-facing docs (features, quick start, output format)
├── LICENSE
├── .gitignore                      # bin/, obj/, publish/, .vs/, .vscode/, *.user
├── Forms/                          # Top-level windows (shell + startup dialogs)
│   ├── WizardForm.cs               # Wizard shell: owns WizardState, navigation, cost ticker
│   ├── WizardForm.resx
│   ├── DisclaimerDialog.cs         # Must accept to run the app
│   └── TelemetryOptInDialog.cs     # First-run telemetry choice
├── UserControls/                   # Wizard pages (IWizardStep) + step-owned dialogs
│   ├── IWizardStep.cs              # Step contract
│   ├── StepApiConfiguration.cs     # Step 1 (+ .resx)
│   ├── StepTopicInput.cs           # Step 2
│   ├── StepStorylines.cs           # Step 3
│   ├── StepCharacters.cs           # Step 4
│   ├── StepGenerationConfig.cs     # Step 5
│   ├── StepGeneration.cs           # Step 6 (runs EmailGenerator)
│   ├── StepComplete.cs             # Step 7
│   └── ModelConfigurationDialog.cs # Model/pricing editor dialog (+ .resx)
├── Models/                         # POCOs, config, results, session state
├── Services/                       # OpenAI wrapper, AI generators, file-format writers, telemetry
├── Helpers/                        # Static utilities + small reusable UI widgets
├── Properties/                     # Resources.resx + generated Resources.Designer.cs
├── Resources/                      # PNG/ICO/SVG branding; *.png/*.jpg/*.ico embedded as manifest resources
├── .planning/codebase/             # GSD codebase maps (this document)
└── .claude/settings.local.json     # Local Claude Code settings
```

## Directory Purposes

**`Forms/`:**
- Purpose: Top-level `Form` windows not part of the step sequence.
- Contains: Shell form and modal startup dialogs. Namespace `ReelDiscovery.Forms`.
- Key files: `Forms/WizardForm.cs` (step registration in `LoadSteps()`), `Forms/DisclaimerDialog.cs`, `Forms/TelemetryOptInDialog.cs`

**`UserControls/`:**
- Purpose: One `UserControl` per wizard step, each implementing `IWizardStep`; plus dialogs launched from steps.
- Contains: Code-only UI (`InitializeUI()` builds controls), step lifecycle methods, calls into `Services/`. Namespace `ReelDiscovery.UserControls`.
- Key files: `UserControls/IWizardStep.cs`, `UserControls/StepGeneration.cs`, `UserControls/StepGenerationConfig.cs`

**`Models/`:**
- Purpose: Data shapes shared across UI and services.
- Contains: `Storyline.cs`, `Character.cs`, `EmailThread.cs`, `EmailMessage.cs`, `Attachment.cs` (+ `AttachmentType`, `AttachmentComplexity` enums), `PresentationTheme.cs` (`OrganizationTheme`), `GenerationConfig.cs`, `GenerationResult.cs` (+ `GenerationProgress`), `AIModelConfig.cs`, `TokenUsageTracker.cs` (+ `TokenUsageEntry`), `WizardState.cs`. Namespace `ReelDiscovery.Models`.
- Key files: `Models/WizardState.cs`, `Models/EmailMessage.cs`, `Models/GenerationConfig.cs`

**`Services/`:**
- Purpose: Business logic: OpenAI access, AI-driven generation, artifact building, persistence to .eml, telemetry.
- Contains: Instance classes taking `OpenAIService` in the constructor (generators), stateless byte producers, one static service. Namespace `ReelDiscovery.Services`.
- Key files: `Services/OpenAIService.cs`, `Services/EmailGenerator.cs` (pipeline orchestrator, ~2,000 lines), `Services/EmlFileService.cs`, `Services/OfficeDocumentService.cs`

**`Helpers/`:**
- Purpose: Stateless helpers and reusable UI bits.
- Contains: `DateHelper.cs`, `FileNameHelper.cs`, `ThreadingHelper.cs`, `HtmlEmailFormatter.cs` (static), `ButtonHelper.cs` (+ `ButtonStyle` enum), `LoadingOverlay.cs` (Panel subclass). Namespace `ReelDiscovery.Helpers`.
- Key files: `Helpers/HtmlEmailFormatter.cs`, `Helpers/ThreadingHelper.cs`

**`Resources/`:**
- Purpose: Branding images and app icon.
- Contains: `QD_Logo_Color_236x256.ico` (window icon, loaded as `ReelDiscovery.Resources.QD_Logo_Color_236x256.ico`), `quikdata_disclaimer_banner_yellow_svglogo_with_url.png` (embedded into HTML emails by `HtmlEmailFormatter`), header/logo PNGs, `QD_Full_Color.svg`.

**`Properties/`:**
- Purpose: Standard ResX resources.
- Contains: `Resources.resx`, auto-generated `Resources.Designer.cs` (do not hand-edit).

## Key File Locations

**Entry Points:**
- `Program.cs`: Process entry, startup dialogs, `Application.Run(new WizardForm())`
- `Forms/WizardForm.cs`: Wizard shell; `LoadSteps()` defines step order
- `UserControls/StepGeneration.cs`: `StartGenerationAsync()` kicks off the generation pipeline on step entry

**Configuration:**
- `ReelDiscovery.csproj`: Target framework, NuGet packages (`OpenAI` 2.1.0, `MimeKit` 4.18.1, `DocumentFormat.OpenXml` 3.0.2), publish settings, embedded resource globs
- `Models/GenerationConfig.cs`: All generation knobs and their defaults
- `Models/AIModelConfig.cs`: Default model list and token pricing (`GetDefaultModels()`)
- Runtime settings (outside repo): `%LOCALAPPDATA%\ReelDiscovery\settings.json` (telemetry) and the API key file written by `UserControls/StepApiConfiguration.cs`

**Core Logic:**
- `Services/EmailGenerator.cs`: Thread generation, attachment/image/calendar/voicemail passes, save
- `Services/StorylineGenerator.cs`, `Services/CharacterGenerator.cs`, `Services/ThemeGenerator.cs`: Earlier AI steps
- `Services/OpenAIService.cs`: All OpenAI calls, retries, token tracking
- `Services/EmlFileService.cs`: MimeKit .eml writing, per-sender folders
- `Services/OfficeDocumentService.cs`: .docx/.xlsx/.pptx creation
- `Helpers/HtmlEmailFormatter.cs`: HTML body rendering
- `Helpers/ThreadingHelper.cs`: Message-ID / In-Reply-To / References

**Testing:**
- Not detected. No test project or test files exist.

## Naming Conventions

**Files:**
- One primary public type per file, file name = type name, PascalCase: `EmailGenerator.cs`, `WizardState.cs`.
- Exception: `Models/PresentationTheme.cs` holds `OrganizationTheme` (main type) and legacy `PresentationTheme`; companion enums/result types share the file of their main type (`Attachment.cs` has `AttachmentType`; `GenerationResult.cs` has `GenerationProgress`; `StorylineGenerator.cs` has `StorylineGenerationResult`).
- Wizard pages: `Step{Name}.cs` in `UserControls/`.
- AI-backed services: `{Thing}Generator.cs`; non-AI services: `{Thing}Service.cs`; utilities: `{Thing}Helper.cs`; dialogs: `{Name}Dialog.cs`.
- Interfaces: `I{Name}.cs` (`IWizardStep.cs`).

**Directories:**
- PascalCase plural or role names matching namespace segment: `Forms`, `UserControls`, `Models`, `Services`, `Helpers`. Namespace = `ReelDiscovery.{Directory}` using file-scoped `namespace` declarations.

**Members:**
- Private fields `_camelCase` (`_state`, `_openAI`, `_btnNext`, `_lblStatus`); UI control fields prefixed by control type (`_btn`, `_lbl`, `_txt`, `_chk`, `_grid`).
- Async methods end in `Async`; LLM DTOs are private nested classes suffixed `Dto` / `Response`.
- Constants PascalCase (`MaxEmailsPerBatch`, `MaxRetries`).

## Where to Add New Code

**New wizard step:**
- Implementation: `UserControls/Step{Name}.cs` as `public class Step{Name} : UserControl, IWizardStep`; build UI in a private `InitializeUI()`; store state in `WizardState`.
- Register: add to `LoadSteps()` in `Forms/WizardForm.cs` at the correct index (order = display order). Check any hard-coded step indices used with `NavigateToStep`.
- New state fields: `Models/WizardState.cs`, under a `// Step N - ...` comment group.

**New AI-generated artifact / attachment type:**
- Generator/service: `Services/{Thing}Generator.cs` (AI) or `Services/{Thing}Service.cs` (byte producer returning `byte[]`, like `Services/CalendarService.cs`).
- Enum value: `AttachmentType` in `Models/Attachment.cs`.
- Planning flag: `Planned*` properties on `Models/EmailMessage.cs` and the prompt text in `EmailGenerator.BuildBatchAttachmentInstructions` / `EmailDto`.
- Pipeline pass: add a parallel pass in `EmailGenerator.GenerateEmailsAsync` (use the existing `semaphore` and `progressLock` pattern) before the "Saving EML files" stage.
- Counters: `Models/GenerationResult.cs`; toggles/percentages: `Models/GenerationConfig.cs` + UI in `UserControls/StepGenerationConfig.cs`.

**New OpenAI capability:**
- Add a method to `Services/OpenAIService.cs` following the retry-loop pattern (429 backoff, 503 delay, `MaxRetries`), call `TrackUsage` when token usage is available.
- Obtain the service via `WizardState.CreateOpenAIService()` so cost tracking is wired.

**New model / pricing preset:**
- `Models/AIModelConfig.cs` `GetDefaultModels()`.

**New dialog:**
- Top-level/startup: `Forms/{Name}Dialog.cs`. Launched from a step: `UserControls/{Name}Dialog.cs` (existing precedent: `ModelConfigurationDialog`).

**Utilities:**
- Shared stateless helpers: `Helpers/{Name}Helper.cs` as `public static class`.
- Reusable UI widgets: `Helpers/` (precedent: `LoadingOverlay`, `ButtonHelper`). Use `ButtonHelper.CreateButton(text, width, height, ButtonStyle.X)` for buttons.

**Embedded assets:**
- Put images/icons in `Resources/`; `*.png`, `*.jpg`, `*.ico` are auto-embedded. Load with `Assembly.GetManifestResourceStream("ReelDiscovery.Resources.{FileName}")`. SVGs are not embedded by the current globs.

**Tests:**
- No location exists. If added, create a sibling test project (e.g., `ReelDiscovery.Tests/`) outside this project folder so it is not compiled into the WinExe; `Helpers/` and non-AI `Services/` are the most testable targets.

## Special Directories

**`bin/`, `obj/`:**
- Purpose: Build output and intermediate files.
- Generated: Yes
- Committed: No (gitignored)

**`publish/`:**
- Purpose: `dotnet publish` single-file output.
- Generated: Yes
- Committed: No (gitignored)

**`.vs/`:**
- Purpose: Visual Studio local state.
- Generated: Yes
- Committed: No

**`.planning/`:**
- Purpose: GSD planning artifacts, including `.planning/codebase/` maps.
- Generated: By tooling
- Committed: Managed by the GSD orchestrator

**`Properties/Resources.Designer.cs`:**
- Purpose: ResX code-gen output.
- Generated: Yes (by `ResXFileCodeGenerator`)
- Committed: Yes

**Output folder (runtime, outside repo):**
- Purpose: Generated `.eml` files, named by `Helpers/FileNameHelper.GenerateEmlFileName`, optionally in per-sender subfolders (`GenerationConfig.OrganizeBySender`).
- Generated: Yes, at user-chosen `GenerationConfig.OutputFolder`
- Committed: No

---

*Structure analysis: 2026-09-29*
