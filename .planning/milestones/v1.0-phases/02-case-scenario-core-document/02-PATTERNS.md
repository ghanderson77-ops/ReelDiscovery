# Phase 2: Case Scenario & Core Document - Pattern Map

**Mapped:** 2026-09-30
**Files analyzed:** 33 (22 new source/test files + 5 modified files + 6 new model files)
**Analogs found:** 30 / 33 (3 pure-logic files have only partial analogs; see "No Analog Found")

Stack reality check: the repo is now **net10.0-windows** (both csproj files), and all LLM access goes through `ILlmGateway` (`Services/Ai/`). `OpenAIService` is gone. CLAUDE.md text that mentions `.NET 8`, `OpenAIService` or `GetJsonCompletionAsync` is out of date. The analogs below reflect the current code.

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Models/Case/CaseDraft.cs` | model | CRUD (in-memory, JSON round-trip) | `Models/Storyline.cs`, `Models/WizardState.cs` | role-match |
| `Models/Case/ComplexityPreset.cs` | model + static table | transform (lookup) | `Models/AIModelConfig.cs` (`GetDefaultModels()` / `GetTierDefault`) | role-match |
| `Models/Case/Jurisdiction.cs` | model | — | `Models/Storyline.cs` | exact (POCO) |
| `Models/Case/CoreDocument.cs` | model | — | `Models/Storyline.cs` | exact (POCO) |
| `Models/Case/CoreDocumentSection.cs` | model | — | `Models/Storyline.cs` | exact (POCO) |
| `Models/Case/EntitySubstitution.cs` | model | — | `Models/Storyline.cs` | exact (POCO) |
| `Services/Case/RealEntityDetector.cs` | service (LLM generator) | request-response | `Services/StorylineGenerator.cs` | exact |
| `Services/Case/CoreDocumentGenerator.cs` | service (LLM generator, multi-call) | request-response + batch (parallel counts) | `Services/StorylineGenerator.cs` + `Services/EmailGenerator.cs` lines 136-219 (semaphore + outcome catches) + `Services/ThemeGenerator.cs` lines 83-96 (IsFatal check) | exact |
| `Services/Case/CoreDocumentTemplates.cs` | utility (pure) | transform | `Helpers/FileNameHelper.cs` (static class, switch expression) | partial |
| `Services/Case/CoreDocumentAssembler.cs` | utility (pure) | transform | `Helpers/FileNameHelper.cs` | partial |
| `Services/Case/CoreDocumentValidator.cs` | utility (pure) | transform | `Helpers/FileNameHelper.cs` + `UserControls/StepStorylines.cs` lines 204-217 (range validation messages) | partial |
| `Services/Case/CitationScanner.cs` | utility (pure, regex) | transform | `Helpers/FileNameHelper.cs` (`[GeneratedRegex]` partial class) | role-match |
| `Services/Case/NameSubstitution.cs` | utility (pure, regex) | transform | `Helpers/FileNameHelper.cs` | role-match (dynamic regex has no analog) |
| `Services/Case/DocumentTypeResolver.cs` | utility (pure) | transform | `Models/WizardState.cs` lines 62-63 (`static bool CanProceedFromKeys(...)` pure function) | role-match |
| `UserControls/StepCaseScenario.cs` | component (wizard step) | request-response (LLM on Next) | `UserControls/StepTopicInput.cs` (layout) + `UserControls/StepStorylines.cs` (LoadingOverlay + try/catch/finally) | exact |
| `UserControls/StepCoreDocument.cs` | component (wizard step) | streaming progress + CRUD edits | `UserControls/StepGeneration.cs` (cancel/progress) + `UserControls/StepStorylines.cs` (auto-generate on enter, header buttons) | role-match |
| `UserControls/EntityReviewDialog.cs` | component (modal dialog) | CRUD (grid edit) | `UserControls/ModelConfigurationDialog.cs` | exact |
| `UserControls/StepApiConfiguration.cs` (modify) | component | — | itself, lines 56-80 (row list), 131-136 (row add) | exact |
| `Helpers/LoadingOverlay.cs` (modify) | component helper | — | itself, lines 17-57 | exact |
| `Forms/WizardForm.cs` (modify: `BuildSteps(flow)`) | shell | — | itself, lines 210-233 (`LoadSteps`), 283-311 (`BtnNext_Click`) | exact |
| `Models/WizardState.cs` (modify: `Flow`, `Case`) | model | — | itself, lines 102-131 (step-grouped properties) | exact |
| `ReelDiscovery.Tests/Case/CaseTestData.cs` | test helper | — | `ReelDiscovery.Tests/Generation/EmailGeneratorTestSetup.cs` | exact |
| `ReelDiscovery.Tests/Case/CaseSchemaTests.cs` | test | — | `ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs` lines 10-40 | exact |
| `ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs` | test | — | `LegacyGeneratorPortTests.cs` lines 58-152 + `EmailGeneratorOutcomeTests.cs` | exact |
| `ReelDiscovery.Tests/Case/RealEntityDetectorTests.cs` | test | — | `LegacyGeneratorPortTests.cs` lines 106-152 | exact |
| `ReelDiscovery.Tests/Case/CitationScannerTests.cs` | test (pure) | — | `ReelDiscovery.Tests/WizardStateProviderTests.cs` (Theory/InlineData) | role-match |
| `ReelDiscovery.Tests/Case/NameSubstitutionTests.cs` | test (pure) | — | `WizardStateProviderTests.cs` | role-match |
| `ReelDiscovery.Tests/Case/CoreDocumentAssemblerTests.cs` | test (pure) | — | `WizardStateProviderTests.cs` | role-match |
| `ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs` | test (pure) | — | `WizardStateProviderTests.cs` | role-match |
| `ReelDiscovery.Tests/Case/ComplexityPresetTests.cs` | test (pure) | — | `WizardStateProviderTests.cs` | role-match |
| `ReelDiscovery.Tests/Case/DocumentTypeResolverTests.cs` | test (pure) | — | `WizardStateProviderTests.cs` lines 44-56 (matrix Theory) | exact |
| `ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs` | test | file-I/O-free JSON round-trip | none in repo (`ApiKeyStore` serializes but has no round-trip test of a POCO graph) | no analog |
| `ReelDiscovery.Tests/Case/CoreDocumentEditingTests.cs` / `CaseDraftTests.cs` | test (pure) | — | `WizardStateProviderTests.cs` | role-match |

---

## Pattern Assignments

### `Services/Case/RealEntityDetector.cs` and `Services/Case/CoreDocumentGenerator.cs` (LLM service, request-response)

**Analog:** `Services/StorylineGenerator.cs` (310 lines, read in full)

**Imports + namespace** (lines 1-6). New files use `namespace ReelDiscovery.Services.Case;` and add `using ReelDiscovery.Models.Case;`:
```csharp
using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services;
```

**Constructor injection + SchemaTypes registry** (lines 8-21). Every generator exposes `internal static SchemaTypes` so the schema test theory can cover it:
```csharp
public class StorylineGenerator
{
    private readonly ILlmGateway _llm;
    private const int MaxStorylinesPerBatch = 15;

    /// <summary>
    /// Top-level response types sent as strict JSON schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(StorylineApiResponse) };

    public StorylineGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }
```

**Public method signature** (lines 23-31). Optional `IProgress<string>? progress = null`, `CancellationToken ct = default` last:
```csharp
public async Task<StorylineGenerationResult> GenerateStorylinesAsync(
    string topic,
    ...
    IProgress<string>? progress = null,
    CancellationToken ct = default)
```

**Loop + cancellation + progress** (lines 85-93). Use the same shape for sequential fact subsections:
```csharp
for (int batch = 0; batch < batchCount; batch++)
{
    ct.ThrowIfCancellationRequested();
    ...
    progress?.Report($"Generating storylines (batch {batch + 1} of {batchCount})...");
```

**Gateway call** (lines 218-226). Copy exactly. Set `Tier = LlmTier.Story` and give `OperationName` a readable name (`"Core Document - Outline"`, `"Core Document - Count II"`, `"Real Name Check"`). Research sets `MaxOutputTokens = 12_000` per section:
```csharp
var llmResult = await _llm.GenerateAsync<StorylineApiResponse>(new LlmRequest
{
    Tier = LlmTier.Story,
    OperationName = "Storyline Generation",
    SystemPrompt = systemPrompt,
    UserPrompt = userPrompt,
    MaxOutputTokens = 32_000
}, ct);
var response = llmResult.GetValueOrThrow();
```
`GetValueOrThrow()` (`Services/Ai/LlmResult.cs` lines 51-57) throws `LlmCallException` carrying `.Result` (Outcome, FailureKind, Message, `IsFatal`).

**Prompt style** (lines 137-216). Verbatim `@"..."` system prompt as a constant-like local (keep it byte-stable for Phase 7 caching: move it to a `private const string` or a static `CoreDocumentPrompts.System`). `$@"..."` user prompt. **No "Respond with JSON" / "valid JSON" text.** The tests assert this (see `AssertNoJsonInstructions` below).

**Date parsing** (lines 241-248). The analog uses culture-dependent `DateTime.TryParse`. **Do not copy that.** Use `DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)` (RESEARCH anti-pattern).

**DTO pattern** (lines 253-301). DTOs are **`internal class`** nested inside the generator (not `private`). They must be `internal` because `SchemaTypes`/tests reference them by type. Each property has `[JsonPropertyName]` + `[Description]`. Strings default to `string.Empty` (no nullables), lists to `new()`, and nested objects to `new()`. Dates are `string` with "YYYY-MM-DD, or empty string" in the description:
```csharp
// API Response DTOs
internal class StorylineApiResponse
{
    [JsonPropertyName("storylines")]
    [Description("The generated storylines")]
    public List<StorylineDto> Storylines { get; set; } = new();

    [JsonPropertyName("suggestedDateRange")]
    [Description("Suggested date range for the emails. Leave startDate and endDate as empty strings if a date range was not requested.")]
    public DateRangeDto SuggestedDateRange { get; set; } = new();
}

internal class DateRangeDto
{
    [JsonPropertyName("startDate")]
    [Description("Start date as YYYY-MM-DD, or empty string")]
    public string StartDate { get; set; } = string.Empty;
    ...
}
```
Enums in DTOs are allowed. Both schema generation (`JsonSchemaRegistry.cs` line 59) and deserialization (`LlmGateway.cs` lines 17-21) register `JsonStringEnumConverter`, so `MatterKind`/entity `kind` can be C# enums serialized as strings.

**Result class** (lines 304-310). A public result class sits after the generator in the same file:
```csharp
public class StorylineGenerationResult
{
    public List<Storyline> Storylines { get; set; } = new();
    public DateTime? SuggestedStartDate { get; set; }
    ...
}
```

**Parallel counts with outcome handling:** `Services/EmailGenerator.cs` lines 139-219. Use `SemaphoreSlim(3)` and the catch ordering (cancel, then fatal `LlmCallException`, then non-fatal `LlmCallException`, then `Exception`):
```csharp
using var semaphore = new SemaphoreSlim(parallelism, parallelism);
...
await semaphore.WaitAsync(runToken);
try
{
    runToken.ThrowIfCancellationRequested();
    ...
}
catch (OperationCanceledException) when (runCts.IsCancellationRequested)
{
    throw; // User cancel or fatal run stop
}
catch (OperationCanceledException)
{
    // Run not cancelled: a request timed out. Skip this storyline and keep going (CR-01)
    result.AddError($"Storyline '{storyline.Title}' skipped (request timed out)");
}
catch (LlmCallException ex) when (ex.Result.IsFatal)
{
    // Bad key or exhausted spend limit: stop the whole run (completed threads are still saved)
    SetFatalError(result, ex.Message);
    runCts.Cancel();
}
catch (LlmCallException ex)
{
    // Refused / truncated / invalid / non-fatal failure: skip this storyline, no blind retry
    result.AddError($"Storyline '{storyline.Title}' skipped ({DescribeOutcome(ex.Result)}): {ex.Result.Message}");
}
catch (Exception ex)
{
    result.AddError($"Storyline '{storyline.Title}' failed: {ex.Message}");
}
finally
{
    // semaphore.Release() (see file line ~219)
```
For Phase 2, the "skip" branch maps to `section.Status = Failed; section.FailureMessage = ...`. The fatal branch maps to "stop generation, keep completed sections". `Refused` on the **outline** call should propagate so the UI shows the Refused empty state.

**Explicit IsFatal check without throwing on non-fatal:** `Services/ThemeGenerator.cs` lines 92-96:
```csharp
// Bad key / spend limit must stop the run; other failures fall back to default themes
if (llmResult.IsFatal)
    throw new LlmCallException(llmResult);

var response = llmResult.Outcome == LlmOutcome.Succeeded ? llmResult.Value : null;
```
Use this in `GenerateSectionAsync` when you need to inspect `Outcome` (Truncated vs Refused) before deciding to mark the section Failed.

**Citation retry (new, content-level):** there is no analog. After `GetValueOrThrow()`, run `CitationScanner.Scan`. If Error findings exist, issue the **same** `LlmRequest` once more with the violation list appended to `UserPrompt`. Do not wrap transport retries (the gateway already does Invalid x3 / Truncated x1; see `LlmGateway.cs` lines 14-15, 121-137).

---

### Pure utilities: `CitationScanner.cs`, `NameSubstitution.cs`, `CoreDocumentAssembler.cs`, `CoreDocumentValidator.cs`, `CoreDocumentTemplates.cs`, `DocumentTypeResolver.cs`

**Analog:** `Helpers/FileNameHelper.cs` (69 lines, read in full)

**Static partial class + `[GeneratedRegex]`** (lines 1-6, 67-68):
```csharp
using System.Text.RegularExpressions;
using ReelDiscovery.Models;

namespace ReelDiscovery.Helpers;

public static partial class FileNameHelper
{
    ...
    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();
}
```
`CitationScanner` copies this directly, with the four regexes given in RESEARCH "Code Examples". A second in-repo example is `Services/Ai/JsonSchemaRegistry.cs` lines 15, 80-81 (`public static partial class`, `[GeneratedRegex("[^A-Za-z0-9_-]")]`).

**Switch expression mapping** (lines 24-30). Use it for `CoreDocumentTemplates.GetSections(CoreDocumentType)` and `DocumentTypeResolver`:
```csharp
var typeName = attachment.Type switch
{
    AttachmentType.Word => "Document",
    AttachmentType.Excel => "Spreadsheet",
    AttachmentType.PowerPoint => "Presentation",
    _ => "File"
};
```

**Pure static decision function** (`Models/WizardState.cs` lines 62-63). This is the shape for `DocumentTypeResolver.Resolve(matterKind, preference, courtSystem, usesPetitionTerminology)`:
```csharp
public static bool CanProceedFromKeys(LlmProviderKind provider, bool openAITested, bool anthropicTested) =>
    provider == LlmProviderKind.OpenAI ? openAITested : anthropicTested;
```

**NameSubstitution dynamic regex:** no `[GeneratedRegex]` is possible because the pattern is built from data. Use `new Regex(pattern, options, TimeSpan.FromMilliseconds(...))` with `Regex.Escape`d literals. This is the one justified exception to the CLAUDE.md `[GeneratedRegex]` rule. Add a comment explaining why (the pattern is runtime data, with a match timeout per Security V5).

**Validator messages:** follow the user-facing wording style in `UserControls/StepStorylines.cs` lines 209-214 (a full sentence that names the allowed range):
```csharp
$"Est. Emails must be a whole number from 0 to {Storyline.MaxSuggestedEmailCount:N0}."
```
Return a `List<CoreDocumentIssue>` (Severity, SectionId, Message) instead of showing UI. The validator must stay pure and testable.

---

### Models: `Models/Case/*.cs`

**Analog:** `Models/Storyline.cs` (full file) and `Models/AIModelConfig.cs` lines 1-30

```csharp
namespace ReelDiscovery.Models;

public class Storyline
{
    /// <summary>
    /// Upper bound for a user-edited "Est. Emails" value; keeps the email distribution math far from int overflow.
    /// </summary>
    public const int MaxSuggestedEmailCount = 10_000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public List<Guid> InvolvedCharacterIds { get; set; } = new();
    ...
    public override string ToString() => Title;
}
```
- Namespace: `ReelDiscovery.Models.Case`. The `Services/Ai` → `ReelDiscovery.Services.Ai` subfolder precedent applies.
- Constants such as `MaxScenarioLength = 8_000` go on `CaseDraft` as a `public const`, the same way as `Storyline.MaxSuggestedEmailCount`.
- Mutable class, auto-properties, `string.Empty` / `new()` defaults, no records, no primary constructors.
- `/// <summary>` only on non-obvious members (for example, `Revision` bumps on every edit, and Phase 3 keys stale-marking off it).
- `ToString()` override on `CoreDocumentSection` returns `Heading`. This helps the owner-drawn `ListBox`.
- `ComplexityPreset.Get(level)` static table: copy the `AIModelConfig.GetDefaultModels()` / `GetTierDefault(list, provider, tier)` static-factory shape (`Models/AIModelConfig.cs`). Use `int Min/Max` pairs (for example, `PartiesMin`, `PartiesMax`) so the UI summary, prompt and validator all read one source.
- Nullable enum precedent: `public LlmTier? DefaultForTier { get; set; }` (AIModelConfig). Nullable is fine in **models**, but never in LLM DTOs.

**WizardState additions** (`Models/WizardState.cs` lines 102-131). Group them with a step comment in the same style:
```csharp
// Step 2 - Topic Input
public string Topic { get; set; } = string.Empty;
...
// Step 5 - Generation Config
public GenerationConfig Config { get; set; } = new();
```
Add `// Workflow` `public WizardFlow Flow { get; set; } = WizardFlow.CaseDriven;` and `// Case-driven flow` `public CaseDraft Case { get; set; } = new();`. Gateway creation stays `CreateGateway()` (lines 83-91). Do not add a new factory.

---

### `UserControls/StepCaseScenario.cs` (wizard step, request-response on Next)

**Layout analog:** `UserControls/StepTopicInput.cs` (269 lines, read in full)

**Class header + IWizardStep members** (lines 1-26):
```csharp
using ReelDiscovery.Models;

namespace ReelDiscovery.UserControls;

public class StepTopicInput : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private TextBox _txtTopic = null!;
    ...
    public string StepTitle => "Topic Selection";
    public bool CanMoveNext => !string.IsNullOrWhiteSpace(_txtTopic?.Text);
    public bool CanMoveBack => true;
    public string NextButtonText => "Generate Storylines >";

    public event EventHandler? StateChanged;

    public StepTopicInput()
    {
        InitializeUI();
    }
```
Step title `Describe the Case`. `CanMoveNext => !string.IsNullOrWhiteSpace(_txtScenario?.Text) && !_isChecking`.

**Root layout with row-index comments** (lines 30-48). Change `Padding` to 8 per UI-SPEC and use the S1 row table (28/100%/24/28/32/48/28/32/24):
```csharp
this.Padding = new Padding(20);

var mainLayout = new TableLayoutPanel
{
    Dock = DockStyle.Fill,
    ColumnCount = 1,
    RowCount = 9,
    Padding = new Padding(10)
};

mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 0: Topic label
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 1: Topic input
```

**Bold label** (lines 51-58), **multiline TextBox** (lines 81-89; add `WordWrap`, `AcceptsReturn`, `MaxLength = 0`), **TextChanged → StateChanged** (line 67):
```csharp
var lblTopic = new Label
{
    Text = "Topic (Movie, TV Show, Book, or Subject):",
    Dock = DockStyle.Fill,
    TextAlign = ContentAlignment.BottomLeft,
    Font = new Font(this.Font, FontStyle.Bold)
};
...
_txtTopic.TextChanged += (s, e) => StateChanged?.Invoke(this, EventArgs.Empty);
```

**FlowLayoutPanel of options** (lines 123-138). Use it for the Simple/Standard/Complex radios and the Document/Court/State combos. Use UI-SPEC margins `new Padding(0, 4, 16, 0)` and drop the emoji text:
```csharp
var mediaPanel = new FlowLayoutPanel
{
    Dock = DockStyle.Fill,
    FlowDirection = FlowDirection.LeftToRight,
    WrapContents = true
};
_chkDocuments = new CheckBox { Text = "...", AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9.5F), Margin = new Padding(0, 5, 20, 0) };
```

**DropDownList combo factory:** `UserControls/StepApiConfiguration.cs` lines 228-234:
```csharp
private ComboBox CreateComboBox() => new()
{
    Width = 260,
    DropDownStyle = ComboBoxStyle.DropDownList,
    Font = new Font(this.Font.FontFamily, 10F),
    Margin = new Padding(0, 5, 0, 0)
};
```

**Enter / Validate lifecycle** (lines 197-230, 237-268). Restore from state in `OnEnterStepAsync`. Validate and write to state in `ValidateStepAsync`. Clear downstream data on change (lines 259-265):
```csharp
public Task<bool> ValidateStepAsync()
{
    if (string.IsNullOrWhiteSpace(_txtTopic.Text))
    {
        MessageBox.Show("Please enter a topic.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return Task.FromResult(false);
    }

    _state.Topic = _txtTopic.Text.Trim();
    ...
    return Task.FromResult(true);
}
```
`StepCaseScenario.ValidateStepAsync` is truly `async` (it runs the detector). It returns `false` on dialog cancel or on detector error.

**Async LLM call under LoadingOverlay:** `UserControls/StepStorylines.cs` lines 280-339. Copy this block for the entity check. Use `LoadingOverlay.LoadingType.EntityCheck` and show it over the scenario TextBox:
```csharp
private async Task GenerateStorylinesAsync()
{
    _isLoading = true;
    _btnRegenerate.Enabled = false;
    _lblStatus.Text = "";
    _loadingOverlay.Show(_gridStorylines);
    StateChanged?.Invoke(this, EventArgs.Empty);

    try
    {
        var generator = new StorylineGenerator(_state.CreateGateway());
        var progress = new Progress<string>(status =>
        {
            _lblStatus.Text = status;
            _lblStatus.ForeColor = Color.Blue;
        });
        var result = await generator.GenerateStorylinesAsync(..., progress);
        ...
        _lblStatus.ForeColor = Color.Green;
    }
    catch (Exception ex)
    {
        var errorMsg = ex.InnerException != null
            ? $"{ex.Message} ({ex.InnerException.Message})"
            : ex.Message;
        _lblStatus.Text = $"Error: {errorMsg}";
        _lblStatus.ForeColor = Color.Red;
    }
    finally
    {
        _isLoading = false;
        _btnRegenerate.Enabled = true;
        _loadingOverlay.Hide();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
```
UI-SPEC overrides: progress/in-progress color is Accent `Color.FromArgb(0, 120, 215)`, not `Color.Blue`. Error copy is `Could not check the scenario for real names: {message}. Click Next to try again.`

---

### `UserControls/EntityReviewDialog.cs` (modal Form, grid CRUD)

**Analog:** `UserControls/ModelConfigurationDialog.cs` (325 lines; lines 1-120 and 160-325 read)

**Class + cloned input exposed as result property** (lines 7-35). The dialog edits a copy, and the caller reads the property after `DialogResult.OK`:
```csharp
public class ModelConfigurationDialog : Form
{
    private DataGridView _gridModels = null!;
    ...
    public List<AIModelConfig> Models { get; private set; }

    public ModelConfigurationDialog(List<AIModelConfig> models)
    {
        // Clone the models to avoid modifying the original until OK is clicked
        Models = models.Select(m => new AIModelConfig { ... }).ToList();
        InitializeUI();
    }
```

**Form chrome** (lines 39-46). UI-SPEC changes it to `FixedDialog`, `ClientSize = new Size(720, 440)`, title `Real Names Found`:
```csharp
this.Text = "Configure AI Models";
this.Size = new Size(980, 480);
this.StartPosition = FormStartPosition.CenterParent;
this.FormBorderStyle = FormBorderStyle.Sizable;
this.MaximizeBox = false;
this.MinimizeBox = false;
this.ShowInTaskbar = false;
```

**Grid construction** (lines 73-102). Add a `DataGridViewCheckBoxColumn` first (width 128), then read-only text columns and an editable `Substitute` column with `AutoSizeMode = Fill`:
```csharp
_gridModels = new DataGridView
{
    Dock = DockStyle.Fill,
    AutoGenerateColumns = false,
    AllowUserToAddRows = false,
    AllowUserToDeleteRows = false,
    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    MultiSelect = false,
    RowHeadersVisible = false,
    BackgroundColor = SystemColors.Window,
    DataSource = _bindingSource
};
```

**Immediate commit of checkbox/combo edits** (lines 210-217). This is needed so unchecking a row greys the Substitute cell right away. Match on `DataGridViewCheckBoxCell`:
```csharp
private void GridModels_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
{
    // Commit combo box changes immediately
    if (_gridModels.IsCurrentCellDirty && _gridModels.CurrentCell is DataGridViewComboBoxCell)
    {
        _gridModels.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }
}
```

**Footer (RightToLeft) + Accept/Cancel buttons** (lines 186-207):
```csharp
var bottomPanel = new FlowLayoutPanel
{
    Dock = DockStyle.Fill,
    FlowDirection = FlowDirection.RightToLeft,
    Padding = new Padding(0, 5, 0, 0)
};

_btnCancel = ButtonHelper.CreateButton("Cancel", 80, 30, ButtonStyle.Default);
_btnCancel.DialogResult = DialogResult.Cancel;
bottomPanel.Controls.Add(_btnCancel);

_btnOk = ButtonHelper.CreateButton("OK", 80, 30, ButtonStyle.Primary);
_btnOk.Margin = new Padding(0, 0, 10, 0);
_btnOk.Click += BtnOk_Click;
bottomPanel.Controls.Add(_btnOk);
...
this.AcceptButton = _btnOk;
this.CancelButton = _btnCancel;
```
Labels become `Apply Substitutes` (Primary, 140x32) and `Back to Scenario` (Default, 140x32). The 8px gap means `Margin = new Padding(0, 0, 8, 0)`.

**OK-click validation, then close** (lines 279-324). This is the place for the blank-substitute check and confirm C1 (Keep Real Names?):
```csharp
private void BtnOk_Click(object? sender, EventArgs e)
{
    foreach (var model in Models)
    {
        if (string.IsNullOrWhiteSpace(model.ModelId))
        {
            MessageBox.Show("All models must have a Model ID.", "Validation Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
    }
    ...
    this.DialogResult = DialogResult.OK;
    this.Close();
}
```
Caller pattern: `using var dlg = new EntityReviewDialog(entities); if (dlg.ShowDialog(this) != DialogResult.OK) return false;`. The `using var` convention comes from CLAUDE.md.

---

### `UserControls/StepCoreDocument.cs` (wizard step, inline progress + edit)

**Cancel/progress analog:** `UserControls/StepGeneration.cs` (lines 100-287 read)

**Fields + nav gating** (lines 16-23):
```csharp
private CancellationTokenSource? _cts;
private bool _isGenerating = false;
public bool CanMoveBack => !_isGenerating;
```

**Danger cancel button + confirm** (lines 105-125). UI-SPEC copy: C5 `Cancel Generation`, `Stop generating? Completed sections will be kept.`, default button No:
```csharp
_btnCancel = ButtonHelper.CreateButton("Cancel", 100, 35, ButtonStyle.Danger);
...
private void BtnCancel_Click(object? sender, EventArgs e)
{
    if (_isGenerating && _cts != null)
    {
        if (MessageBox.Show("Are you sure you want to cancel the generation?",
            "Confirm Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _cts.Cancel();
            AppendLog("Cancellation requested...", Color.Yellow);
        }
    }
}
```

**Run skeleton** (lines 160-287). `_isGenerating = true` + new CTS → try { generator call with `_cts.Token` } → `catch (OperationCanceledException)` → `catch (Exception)` → `finally { _isGenerating = false; ...; _cts?.Dispose(); _cts = null; StateChanged?.Invoke(...) }`:
```csharp
catch (OperationCanceledException)
{
    AppendLog("Generation cancelled.", Color.Yellow);
    _lblProgress.Text = "Generation cancelled.";
}
catch (Exception ex)
{
    AppendLog($"Error: {ex.Message}", Color.Red);
    _lblProgress.Text = "Generation failed.";
}
finally
{
    _isGenerating = false;
    _btnCancel.Enabled = false;
    ...
    _cts?.Dispose();
    _cts = null;
    StateChanged?.Invoke(this, EventArgs.Empty);
}
```
Add `catch (LlmCallException ex) when (ex.Result.Outcome == LlmOutcome.Refused)` **before** `catch (Exception)` to show the Refused empty state. Put the fatal branch there too (`ex.Result.IsFatal` → fatal copy).

**Cross-thread UI update** (lines 127-133, 190-200). Progress callbacks go through `Progress<T>` (captures UI context). Keep the `InvokeRequired` guard for safety:
```csharp
if (InvokeRequired)
{
    Invoke(() => AppendLog(message, color));
    return;
}
```
Cancel-generation color: UI-SPEC says DarkOrange status (`Generation cancelled. Completed sections were kept.`), not Yellow (Yellow is for the dark RichTextBox log only).

**Header with right-anchored button:** `UserControls/StepStorylines.cs` lines 51-78 (Panel + Label + `Anchor = Right|Top` button, repositioned in `Resize`):
```csharp
var headerPanel = new Panel { Dock = DockStyle.Fill };
...
_btnRegenerate = ButtonHelper.CreateButton("Regenerate", 110, 32, ButtonStyle.Primary);
_btnRegenerate.Anchor = AnchorStyles.Right | AnchorStyles.Top;
headerPanel.Resize += (s, e) =>
{
    _btnRegenerate.Location = new Point(headerPanel.Width - 120, 0);
};
```
UI-SPEC: `Regenerate All` is **Default** style here (only `Regenerate Section`/`Generate Document` are Primary).

**Issues grid:** copy the `DataGridView` init from `StepStorylines.cs` lines 81-124 (`AutoGenerateColumns = false`, `RowHeadersVisible = false`, `BackgroundColor = SystemColors.Window`, fixed-width + `Fill` columns). Add `ReadOnly = true` and a `CellDoubleClick` handler.

**Auto-generate on enter:** `StepStorylines.cs` lines 352-371 (`if (_state.Storylines.Count == 0) await GenerateStorylinesAsync();`). **Diverge per research Pitfall 6:** generate only when `CoreDocument == null`. On a `SourceHash` mismatch, show confirm C4. Never regenerate silently.

**Validation MessageBoxes** (Next gating): `StepStorylines.cs` lines 379-388 shape. Use the UI-SPEC titles `Case Citations Found` (OK, Warning) and `Review Warnings` (YesNo, Question, `MessageBoxDefaultButton.Button2`).

**No analogs exist** for owner-drawn `ListBox`, `SplitContainer` or `TabControl` in the repo. Use UI-SPEC S3 values directly.

---

### `UserControls/StepApiConfiguration.cs` (modify: flow selector rows)

**Analog:** itself, lines 56-80 and 131-136, 186-200

Add two rows **at the bottom** (after row 12, Disclaimer) or before the Percent-100 instructions row. Bump `RowCount = 13` → `15` and keep the comments continuous:
```csharp
mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 11: Instructions
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // 12: Disclaimer
// + 13: Workflow header (28), 14: Workflow radios (32)
```
Header label: reuse the existing `CreateSectionHeader(text)` factory (lines 203-209). Span both columns with `mainLayout.SetColumnSpan(ctrl, 2)` (the pattern at lines 184, 198). The layout has `MinimumSize = new Size(0, 520)` (line 63). Raise it by 60 so the new rows stay reachable via `AutoScroll`. Radio `CheckedChanged` writes `_state.Flow`. It is also written again in `ValidateStepAsync` (lines 616-632) per the "write in Validate" convention.

---

### `Helpers/LoadingOverlay.cs` (modify: `EntityCheck`)

**Analog:** itself, lines 17-57

Add a message array and an enum value. Replace the binary ternary on line 57 with a switch expression:
```csharp
private static readonly string[] EntityCheckMessages = new[]
{
    "Checking for real names",
    "Looking for real people and companies",
    "Preparing fictional substitutes"
};

public enum LoadingType
{
    Storylines,
    Characters,
    EntityCheck
}

_messages = type switch
{
    LoadingType.Storylines => StorylineMessages,
    LoadingType.Characters => CharacterMessages,
    LoadingType.EntityCheck => EntityCheckMessages,
    _ => StorylineMessages
};
```
Existing line 57 to replace: `_messages = type == LoadingType.Storylines ? StorylineMessages : CharacterMessages;`

---

### `Forms/WizardForm.cs` (modify: `BuildSteps(flow)`)

**Analog:** itself

**Current fixed list** (lines 210-233). Refactor into `BuildSteps(WizardFlow flow)`. Keep index 0 (`StepApiConfiguration`), and remove + `Dispose()` the other steps (both from `_steps` and from `_contentPanel.Controls`). Then add the flow's list and repeat the bind/wire loop:
```csharp
foreach (var step in _steps)
{
    if (step is IWizardStep wizardStep)
    {
        wizardStep.BindState(_state);
        wizardStep.StateChanged += (s, e) => UpdateNavigationButtons();
    }
    step.Dock = DockStyle.Fill;
    step.Visible = false;
    _contentPanel.Controls.Add(step);
}
```
Case flow: `new StepApiConfiguration()` (kept), `new StepCaseScenario()`, `new StepCoreDocument()`. Legacy: `StepTopicInput` … `StepComplete`.

**Hook point** (lines 283-311, `BtnNext_Click`). After `ValidateStepAsync()` succeeds on index 0, call `BuildSteps(_state.Flow)` if the flow changed, then `ShowStep(1)`. `ShowStep` (line 256) already renders `Step {index + 1} of {_steps.Count}`.

**Finish confirm C6** (lines 285-290). This is the current last-step close. Add the MessageBox before `this.Close()` when `_state.Flow == WizardFlow.CaseDriven`. The `BtnCancel_Click` confirm (lines 321-328) is the in-file model:
```csharp
if (_currentStepIndex >= _steps.Count - 1)
{
    // On last step, close the form
    this.Close();
    return;
}
```
Note: `ValidateStepAsync` is **not** called on the last step today (the Finish path returns early). `StepCoreDocument`'s citation-error gating on Finish needs the Finish path to call `ValidateStepAsync()` first. The planner must change this branch.

---

### Tests: `ReelDiscovery.Tests/Case/*`

**Generator tests analog:** `ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs` (224 lines, read in full)

**Usings + namespace** (lines 1-6). The new namespace is `ReelDiscovery.Tests.Case`, which matches the research filter `FullyQualifiedName~ReelDiscovery.Tests.Case`:
```csharp
using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Generation;
```

**Schema strictness theory** (lines 10-32). Copy it for `CaseSchemaTests` over `RealEntityDetector.SchemaTypes` + `CoreDocumentGenerator.SchemaTypes`:
```csharp
public static TheoryData<string, Type> AllSchemaTypes()
{
    var data = new TheoryData<string, Type>();
    foreach (var type in StorylineGenerator.SchemaTypes)
        data.Add(nameof(StorylineGenerator), type);
    ...
    return data;
}

[Theory]
[MemberData(nameof(AllSchemaTypes))]
public void AllSchemaTypes_AreStrict(string generator, Type type)
{
    var schema = JsonSchemaRegistry.GetSchemaJson(type);
    var errors = StrictSchemaValidator.Validate(schema);
    Assert.True(errors.Count == 0, $"{generator}.{type.Name}: {string.Join("; ", errors)}");
    Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{generator}.{type.Name} exceeds 16 union types");
}
```

**Gateway + fake construction (Anthropic proves provider-agnostic)** (lines 42-70):
```csharp
private static AIModelConfig StoryModel() => new()
{
    ModelId = "claude-story",
    DisplayName = "Story",
    Provider = LlmProviderKind.Anthropic,
    MaxOutputTokens = 64_000
};
...
private static (LlmGateway Gateway, FakeLlmProvider Fake) CreateGateway()
{
    // Anthropic fake proves the generators are provider-agnostic
    var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
    return (new LlmGateway(fake, StoryModel(), VolumeModel()), fake);
}

private static void AssertNoJsonInstructions(LlmProviderRequest request)
{
    var prompts = request.SystemPrompt + request.UserPrompt;
    Assert.DoesNotContain("Respond with JSON", prompts);
    Assert.DoesNotContain("valid JSON", prompts);
}
```
Move these into `CaseTestData` (internal static, modeled on `EmailGeneratorTestSetup.cs` lines 10-28, which exposes `StoryModel()`, `VolumeModel()`, `Gateway(fake)`, and a `SyncProgress<T>` at lines 93-99). Canned JSON goes in `"""` raw-string constants (lines 72-99).

**Round-trip + tier + prompt-capture assertions** (lines 106-125):
```csharp
var (gateway, fake) = CreateGateway();
fake.Responses.Enqueue(FakeLlmProvider.Ok(StorylineJson));

var result = await new StorylineGenerator(gateway).GenerateStorylinesAsync("Game of Thrones", "", storylineCount: 2);
...
var request = Assert.Single(fake.Requests);
Assert.Equal("claude-story", request.ModelId);
AssertNoJsonInstructions(request);
```
For the citation retry: enqueue `Ok(sectionWithCitation)` then `Ok(cleanSection)`. Assert `fake.Requests.Count == 2` and `fake.Requests[1].UserPrompt` contains the violation text. For the outline prompt: assert `fake.Requests[0].UserPrompt` contains the preset numbers.

**Refused / fatal assertions** (lines 141-152, 213-223):
```csharp
fake.Responses.Enqueue(FakeLlmProvider.Refused("I can't help with that"));
var ex = await Assert.ThrowsAsync<LlmCallException>(() => ...);
Assert.Equal(LlmOutcome.Refused, ex.Result.Outcome);
Assert.Single(fake.Requests); // no generator-level retry
...
fake.Responses.Enqueue(FakeLlmProvider.Failed(LlmFailureKind.SpendLimit, "credit balance too low"));
```
`FakeLlmProvider` (`ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs`) helpers: `Ok(text)`, `Truncated()`, `Refused(msg)`, `Failed(kind, msg)`, plus `Requests` capture and `OnSend` for deterministic mid-run cancel (lines 30-34). When the queue empties, the **last response repeats** (line 56-57). With parallel count calls, script enough responses or rely on the repeat.

**Section-failed-but-others-kept analog:** `ReelDiscovery.Tests/Generation/EmailGeneratorOutcomeTests.cs` lines 11-80 (Refused skips one unit and continues; SpendLimit stops and keeps completed work).

**Pure-logic tests analog:** `ReelDiscovery.Tests/WizardStateProviderTests.cs` (full file). Plain `[Fact]`s and a `[Theory]` + `[InlineData]` matrix (lines 44-56). Use it for `DocumentTypeResolverTests` and the positive/negative corpus in `CitationScannerTests`:
```csharp
[Theory]
[InlineData(LlmProviderKind.OpenAI, true, false, true)]
[InlineData(LlmProviderKind.OpenAI, false, true, false)]
public void CanProceedFromKeys_Matrix(LlmProviderKind provider, bool openAITested, bool anthropicTested, bool expected)
{
    Assert.Equal(expected, WizardState.CanProceedFromKeys(provider, openAITested, anthropicTested));
}
```
Test project: `using Xunit` is global (`ReelDiscovery.Tests.csproj` `<Using Include="Xunit" />`), and `InternalsVisibleTo ReelDiscovery.Tests` is set in `ReelDiscovery.csproj` line 63, so `internal` DTOs/`SchemaTypes` are visible. The main csproj excludes `ReelDiscovery.Tests/**` (lines 57-59), so new test files need no csproj edit.

---

## Shared Patterns

### LLM access (all Services/Case generators)
**Source:** `Models/WizardState.cs` lines 83-91 (`CreateGateway()`), `Services/Ai/ILlmGateway.cs`
**Apply to:** `RealEntityDetector`, `CoreDocumentGenerator` (constructor takes `ILlmGateway`); UI creates them as `new CoreDocumentGenerator(_state.CreateGateway())` (`StepStorylines.cs` line 290 precedent).
```csharp
public ILlmGateway CreateGateway()
{
    var key = GetApiKey(TextProvider);
    var story = StoryModelConfig ?? throw new InvalidOperationException($"No Story model selected for {TextProvider}");
    ...
    return new LlmGateway(LlmProviderFactory.Create(TextProvider, key), story, volume, UsageTracker);
}
```
Usage is recorded to `UsageTracker` automatically, so the header cost ticker (`WizardForm.cs` lines 52-74) updates with no extra code.

### Outcome / error handling
**Source:** `Services/Ai/LlmResult.cs` lines 24-75; `Services/EmailGenerator.cs` lines 184-217; `Services/ThemeGenerator.cs` lines 92-96
**Apply to:** generator services and `StepCoreDocument`
- Catch order: `OperationCanceledException` (when run cancelled) → `LlmCallException when IsFatal` → `LlmCallException` → `Exception`.
- `IsFatal` = Auth or SpendLimit. It stops the run and keeps completed work.
- No generator-level transport retries. The only new retry is the one-shot citation regeneration.

### UI async boundary
**Source:** `UserControls/StepStorylines.cs` lines 280-339; `UserControls/StepGeneration.cs` lines 160-287
**Apply to:** `StepCaseScenario` (entity check), `StepCoreDocument` (generate / regenerate)
- Set busy flag → disable inputs → `StateChanged` → try/catch → `finally` reset + `StateChanged`.
- Error text includes `ex.InnerException?.Message` when present. Status `ForeColor = Color.Red`.

### Validation MessageBox
**Source:** `UserControls/StepTopicInput.cs` line 241; `StepStorylines.cs` line 383
**Apply to:** all new steps and the dialog
```csharp
MessageBox.Show("Please enter a topic.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
return Task.FromResult(false);
```
Confirmations use `MessageBoxDefaultButton.Button2` (UI-SPEC C1-C6). The existing code does not pass this argument, so add it explicitly.

### Buttons
**Source:** `Helpers/ButtonHelper.cs` line 54 `CreateButton(string text, int minWidth, int height, ButtonStyle style = ButtonStyle.Default)`; `ButtonStyle { Default, Primary, Secondary, Danger }` (lines 75-81)
**Apply to:** every new button (height 32 in new UI per UI-SPEC). Do not hand-set FlatStyle, colors or fonts.

### Regex
**Source:** `Helpers/FileNameHelper.cs` lines 6, 67-68; `Services/Ai/JsonSchemaRegistry.cs` lines 80-81
**Apply to:** `CitationScanner`, paragraph-number stripping (`^\s*\d+\.\s+`), the `paragraphs? \d+` validator warning, and blank-line paragraph splitting (`\r?\n\s*\r?\n`) in `CoreDocumentSection`/editor helper. The one exception is the data-built `NameSubstitution` regex (`new Regex` + timeout).

### JSON serialization of CaseDraft
**Source:** `Services/Ai/LlmGateway.cs` lines 17-21 (options with `JsonStringEnumConverter`, case-insensitive)
**Apply to:** `CaseDraftSerializationTests` (and the optional Save/Open draft). Define one `static readonly JsonSerializerOptions` (for example `CaseDraft.JsonOptions`: `WriteIndented = true`, `JsonStringEnumConverter`) that Phase 3's case.json will reuse. Do not use `ApiKeyStore`'s default-options serializer, because it would write enums as numbers.

---

## No Analog Found

| File / Concern | Role | Data Flow | Reason |
|---|---|---|---|
| `Services/Case/NameSubstitution.cs` (runtime alternation regex, single-pass dictionary replace, case-sensitivity split) | utility | transform | No dynamic-regex code exists. Use RESEARCH Pattern 3 and add a timeout |
| `Services/Case/CoreDocumentAssembler.cs` (numbering, incorporation, caption, prayer lettering) | utility | transform | No document-assembly logic in the repo. Use RESEARCH Pattern 2 and the "Code-owned incorporation paragraph" example. `NumberedParagraph` is a class, not a record |
| `ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs` | test | JSON round-trip | No POCO round-trip test exists. Serialize → deserialize → assert every field, including enums and nested sections |
| Owner-drawn `ListBox`, `SplitContainer`, `TabControl` in `StepCoreDocument` | component | — | Not used anywhere in the repo. Follow UI-SPEC S3 exactly |
| Citation-violation content retry | service | request-response | The gateway retries only transport/parse outcomes. This is new logic in `CoreDocumentGenerator` |

## Metadata

**Analog search scope:** `Services/`, `Services/Ai/`, `Models/`, `UserControls/`, `Forms/`, `Helpers/`, `ReelDiscovery.Tests/` (all subfolders)
**Files scanned:** 23 read (StorylineGenerator, LlmResult, LlmRequest, ILlmGateway, LlmGateway, JsonSchemaRegistry, WizardState, Storyline, AIModelConfig (partial), WizardForm, StepStorylines, StepTopicInput, StepGeneration (partial), StepApiConfiguration (partial), ModelConfigurationDialog, IWizardStep, LoadingOverlay, FileNameHelper, ThemeGenerator (partial), EmailGenerator (partial), LegacyGeneratorPortTests, EmailGeneratorOutcomeTests (partial), EmailGeneratorTestSetup (partial), WizardStateProviderTests, FakeLlmProvider, StrictSchemaValidator, test csproj)
**Pattern extraction date:** 2026-09-30
