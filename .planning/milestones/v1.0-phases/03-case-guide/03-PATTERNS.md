# Phase 3: Case Guide - Pattern Map

**Mapped:** 2026-10-01
**Files analyzed:** 52 (37 new, 15 modified; counting test files)
**Analogs found:** 49 / 52 (3 have no in-repo analog: render harness, CanonicalValueScanner's money regex specifics, atomic OneDrive-safe store beyond `ApiKeyStore`)

> Code truth: `net10.0-windows`, all LLM calls via `ILlmGateway` (CLAUDE.md is partly stale). `InternalsVisibleTo ReelDiscovery.Tests` is set in `ReelDiscovery.csproj:63`, so `internal static` prompt builders and `internal class *Dto` are testable directly (that is how Phase 2 does it).

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Models/Case/CaseDraft.cs` (MOD: FormatVersion, CaseId, CreatedUtc/UpdatedUtc, Stage, Guide, `[JsonIgnore] FolderPath`, `Normalize()`) | model | transform (JSON) | itself (`Models/Case/CaseDraft.cs`) | exact |
| `Models/Case/CaseStage.cs` | model (enum) | — | `Models/Case/CoreDocumentSection.cs` enums (`SectionStatus`) | exact |
| `Models/Case/CaseGuide.cs` | model | transform | `Models/Case/CoreDocument.cs` (root POCO + `FindSection`) | exact |
| `Models/Case/CaseIssue.cs` | model | — | `CaseCount` in `Models/Case/CoreDocument.cs:46-54` | exact |
| `Models/Case/CaseOrganization.cs` | model | — | `CaseParty` in `Models/Case/CoreDocument.cs:32-41` | exact |
| `Models/Case/CastMember.cs` (+ `CastTier`, `CastRole`) | model | — | `CoreDocumentSection` (status + `IsUserEdited`) + `CaseParty` | exact |
| `Models/Case/KeyFact.cs` (+ `FactSignificance`) | model | — | `CoreDocumentSection` + `FactSubsectionPlan` (date strings) | exact |
| `Models/Case/CanonicalValue.cs` (+ `ValueKind`) | model | — | `Models/Case/EntitySubstitution.cs` | exact |
| `Models/Case/GuidePartState.cs` | model | — | `CoreDocumentSection.Status/FailureMessage` + `CoreDocument.SourceHash` | exact |
| `Models/Case/ComplexityPreset.cs` (MOD: `BackgroundPerOrg`, `FactsMin/Max`) | config | — | itself | exact |
| `Services/Case/IdAllocator.cs` | utility (static) | transform | `CoreDocumentGenerator.MapOutline` ID assignment (`party-{i+1}`) | role-match |
| `Services/Case/EmailIdentity.cs` | utility (static) | transform | `Helpers/FileNameHelper.cs` (sanitize) + `NameSubstitution.CorporateSuffix` regex | role-match |
| `Services/Case/IssueExtractor.cs` | utility (static) | transform | `CoreDocumentGenerator.MapOutline` (counts mapping, lines 588-609) | role-match |
| `Services/Case/CaseGuideGenerator.cs` | service (LLM generator) | request-response, batched/parallel | `Services/Case/CoreDocumentGenerator.cs` | exact |
| `Services/Case/CaseGuidePrompts.cs` | utility (static consts/builders) | transform | `CoreDocumentGenerator.SystemPrompt` (line 22) + `BuildOutlinePrompt` (470-523) | exact |
| `Services/Case/CanonicalValueScanner.cs` | utility (static, regex) | transform | `Services/Case/CitationScanner.cs` | exact |
| `Services/Case/CaseGuideValidator.cs` | service (static validator) | transform | `Services/Case/CoreDocumentValidator.cs` | exact |
| `Services/Case/GuideStaleness.cs` | utility (static) | transform (hash) | `CaseDraft.ComputeHash/ComputeSourceHash` + `StepCoreDocument._declinedStaleHash` | role-match |
| `Services/Case/CaseGuideEditing.cs` | utility (static) | CRUD (in-memory) | `Services/Case/CoreDocumentEditing.cs` | exact |
| `Services/Case/CaseStore.cs` | service | file-I/O | `Services/ApiKeyStore.cs` (LOCALAPPDATA folder, tmp + `File.Move(overwrite)`) | role-match |
| `Services/Case/CaseFolderNaming.cs` | utility (static) | transform | `Helpers/FileNameHelper.SanitizeForFileName` | role-match (do NOT reuse as-is: it replaces spaces with `_`) |
| `Services/Case/CaseGuideDocxExporter.cs` | service | file-I/O (OpenXml) | `Services/OfficeDocumentService.CreateWordDocument` (lines 14-119) | role-match (has schema-order bug; do not copy rPr order) |
| `Services/Case/CoreDocumentGenerator.cs` (MOD: `CounselDto.Email` description) | service | — | itself, line 829-831 | exact |
| `Helpers/GridStyle.cs` | utility (UI, static) | — | `StepCoreDocument` issues grid, lines 434-462 | exact (extract verbatim, change padding to 15) |
| `Helpers/TextFieldHost.cs` | utility (UI factory) | — | `StepCoreDocument` guidance box, lines 341-361 | exact |
| `Helpers/Theme.cs` (MOD: `DrawStatusDot`) | utility (UI) | — | `StepCoreDocument.LstSections_DrawItem` dot, lines 843-851 | exact |
| `UserControls/StepCaseGuide.cs` | component (wizard step) | event-driven + request-response | `UserControls/StepCoreDocument.cs` | exact |
| `UserControls/StepCaseSummary.cs` | component (wizard step) | file-I/O (export) | `UserControls/StepCoreDocument.cs` (header/card/status strip) | role-match |
| `UserControls/LinkPicker.cs` | component (UserControl) | event-driven | guidance `TextFieldHost` + `_lstSections` ListBox in StepCoreDocument | partial |
| `UserControls/StepCaseScenario.cs` (MOD: recent-cases card, Open/New Case, theme colors) | component | file-I/O | itself + `StepCoreDocument` Sections card + owner-drawn list | exact |
| `UserControls/StepCoreDocument.cs` (MOD: Next >, guide notice, C9 copy, autosave, 15px insets) | component | — | itself | exact |
| `Forms/ThemedDialog.cs` | component (Form base) | — | `UserControls/EntityReviewDialog.cs` (dialog chrome lines 37-58) | role-match (pre-theme colors; restyle) |
| `Forms/RegenerateTabDialog.cs` | component (dialog) | request-response | `EntityReviewDialog` | role-match |
| `Forms/DeleteReferencesDialog.cs` | component (dialog) | request-response | `EntityReviewDialog` | role-match |
| `Forms/WizardForm.cs` (MOD: 5 steps, Stage→type nav, close C10-C12) | controller (shell) | event-driven | itself | exact |
| `ReelDiscovery.Tests/Case/CaseTestData.cs` (MOD: `SampleGuidedDraft()`, DTO JSON builders) | test fixture | — | itself | exact |
| `ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs` | test | — | `ReelDiscovery.Tests/Case/CoreDocumentGeneratorTests.cs` | exact |
| `ReelDiscovery.Tests/Case/CaseGuideValidatorTests.cs` | test | — | `ReelDiscovery.Tests/Case/CoreDocumentValidatorTests.cs` | exact |
| `ReelDiscovery.Tests/Case/CaseGuideEditingTests.cs` | test | — | `ReelDiscovery.Tests/Case/CoreDocumentEditingTests.cs` | exact |
| `ReelDiscovery.Tests/Case/CaseGuideModelTests.cs` | test | — | `ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs` | exact |
| `ReelDiscovery.Tests/Case/IdAllocatorTests.cs`, `EmailIdentityTests.cs`, `IssueExtractorTests.cs`, `CaseFolderNamingTests.cs`, `CanonicalValueScannerTests.cs`, `GuideStalenessTests.cs` | test | — | `CitationScannerTests.cs`, `CaseDraftTests.cs` | role-match |
| `ReelDiscovery.Tests/Case/CaseStoreTests.cs` | test | file-I/O | `ReelDiscovery.Tests/ApiKeyStoreTests.cs` (temp-folder ctor injection) | role-match |
| `ReelDiscovery.Tests/Case/CaseGuideDocxExporterTests.cs` | test | — | `ReelDiscovery.Tests/Documents/DocumentRegressionTests.cs` | exact (assert zero errors, not pinned baseline) |
| `ReelDiscovery.Tests/Case/CaseGuideUiLogicTests.cs` | test | — | `ReelDiscovery.Tests/Case/CoreDocumentUiLogicTests.cs` | exact |
| `ReelDiscovery.Tests/Case/CaseSchemaTests.cs` (MOD: add `CaseGuideGenerator.SchemaTypes`) | test | — | itself | exact |
| `ReelDiscovery.Tests/Case/WizardFlowTests.cs` (MOD) | test | — | itself | exact |
| `ReelDiscovery.Tests/Case/ComplexityPresetTests.cs` (MOD) | test | — | itself | exact |
| `ReelDiscovery.Tests/Render/RenderHarness.cs` + `ScreenRenderTests.cs` | test (UI render) | — | none committed | **no analog** |

---

## Pattern Assignments

### `Models/Case/CaseDraft.cs` (MOD, model)

**Analog:** itself. Keep the shared `JsonOptions` (lines 31-37) — case.json uses it unchanged:
```csharp
// Shared with Phase 3 case.json; enums as strings
public static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }
};
```
**Computed / non-persisted members use `[JsonIgnore]`** (lines 59-60) — copy for `FolderPath`:
```csharp
[JsonIgnore]
public ComplexityPreset Preset => ComplexityPreset.Get(Complexity);
```
**Serialize / load** (lines 62-66) — `CaseStore` should call these, then `Normalize()`:
```csharp
public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
public static CaseDraft FromJson(string json) =>
    JsonSerializer.Deserialize<CaseDraft>(json, JsonOptions)
    ?? throw new InvalidOperationException("The case draft file is empty.");
```
**Hash + date helpers to reuse everywhere** (lines 84-92):
```csharp
public static string ComputeHash(string text) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
// Culture-independent parsing only (T-02-04)
public static bool TryParseDate(string? value, out DateTime date) =>
    DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
```
Add new properties with target-typed `new()` defaults and `/// <summary>` on non-obvious ones (style of `Revision`, lines 54-57). `Guide` is nullable (`CaseGuide? Guide`) like `CoreDocument? CoreDocument` (line 52).

---

### `Models/Case/CaseGuide.cs`, `CaseIssue.cs`, `CaseOrganization.cs`, `CastMember.cs`, `KeyFact.cs`, `CanonicalValue.cs`, `GuidePartState.cs`, `CaseStage.cs` (models)

**Analog:** `Models/Case/CoreDocument.cs` + `Models/Case/CoreDocumentSection.cs`.

File layout (enums at top of the file of the primary type, file-scoped namespace, no usings needed for plain POCOs) — `CoreDocument.cs` lines 1-41:
```csharp
namespace ReelDiscovery.Models.Case;

public enum PartyRole
{
    Plaintiff,
    Defendant,
    ...
}

public class CaseParty
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public PartyRole Role { get; set; }
    ...
}
```
Dates as strings with a comment (CoreDocument.cs lines 96-99):
```csharp
// Dates are yyyy-MM-dd strings (empty = unknown); parse only via CaseDraft.TryParseDate
public string FilingDate { get; set; } = string.Empty;
```
Lists and lookup helper (lines 101-113) — copy for `CaseGuide.FindCast(id)`, `FindFact(id)`, `AllIds()`:
```csharp
public List<CaseParty> Parties { get; set; } = new();
...
public CoreDocumentSection? FindSection(string id) => Sections.FirstOrDefault(s => s.Id == id);
```
Status + edit flags (CoreDocumentSection.cs lines 23-29, 48-50) — copy for `GuidePartState` (Status/FailureMessage) and for `IsUserEdited` / `NeedsReview` on `CastMember`, `KeyFact`, `CanonicalValue`:
```csharp
public enum SectionStatus { Pending, Writing, Ready, Failed }
...
public SectionStatus Status { get; set; } = SectionStatus.Pending;
public string FailureMessage { get; set; } = string.Empty;
public bool IsUserEdited { get; set; }
```
Phase 3 can reuse `SectionStatus` for `GuidePartState.Status` or declare a parallel `GuidePartStatus` enum with the same 4 values (the UI-SPEC uses Pending/Generating/Ready/Failed wording — pick one and keep it consistent).
Stable-ID doc comment style (CoreDocumentSection.cs lines 34-37):
```csharp
/// <summary>
/// Stable id (e.g. facts-2, count-1); kept when the section is regenerated so Phase 3 can trace facts to sections.
/// </summary>
public string Id { get; set; } = string.Empty;
```
`CaseGuide.IdCounters` = `Dictionary<string, int> IdCounters { get; set; } = new();` (STJ round-trips string-keyed dictionaries). No `record`, no primary constructors.

---

### `Models/Case/ComplexityPreset.cs` (MOD, config)

**Analog:** itself. Add properties next to `CharactersMin/Max` (lines 27-28) and values in each `Get` case (lines 59-97):
```csharp
ComplexityLevel.Standard => new ComplexityPreset
{
    Level = ComplexityLevel.Standard,
    ...
    CharactersMin = 12, CharactersMax = 20,
    ...
},
```
Comment at line 56 must stay true: "every range strictly increases from Simple to Complex" — `ComplexityPresetTests.RangeNames` checks it; add `"Facts"` there. `BackgroundPerOrg` is a single int (3/6/10), not a range.

---

### `Services/Case/CaseGuideGenerator.cs` (service, request-response, batched)

**Analog:** `Services/Case/CoreDocumentGenerator.cs` (880 lines, read in full).

**Imports** (lines 1-9):
```csharp
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;
```
**Class header, constants, ctor, SchemaTypes** (lines 15-34):
```csharp
public class CoreDocumentGenerator
{
    public const int OutlineMaxOutputTokens = 16_000;
    public const int SectionMaxOutputTokens = 12_000;
    private const int MaxParallelSections = 3;

    // Byte-stable for prompt caching: never interpolate run data into this text
    private const string SystemPrompt = "...";

    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(OutlineDto), typeof(SectionDto) };

    public CoreDocumentGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }
```
For Phase 3: `SchemaTypes = { IssueDetailsDto, CastRosterDto, CastDetailsDto, BackgroundStaffDto, FactBatchDto, ValuesDto, (single-entry DTOs) }`; system prompt lives in `CaseGuidePrompts.SystemPrompt`.

**Single gateway call** (lines 43-52):
```csharp
var result = await _llm.GenerateAsync<OutlineDto>(new LlmRequest
{
    Tier = LlmTier.Story,
    OperationName = "Core Document - Outline",
    SystemPrompt = SystemPrompt,
    UserPrompt = BuildOutlinePrompt(draft),
    MaxOutputTokens = OutlineMaxOutputTokens
}, ct);

return MapOutline(result.GetValueOrThrow(), draft);
```
OperationName convention for guide: `"Case Guide - Cast roster"`, `"Case Guide - Facts ({plan.Heading})"`.

**Bounded parallel batch with fatal-stop linked CTS** (lines 96-131) — copy for cast detail batches, background-per-org and fact-per-section calls. Note Phase 3 difference (RESEARCH Pattern 5): tasks return DTOs; merge into `guide.*` lists **after** `Task.WhenAll`, on the calling thread:
```csharp
using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
...
using var semaphore = new SemaphoreSlim(MaxParallelSections, MaxParallelSections);
var tasks = parallel.Select(async section =>
{
    await semaphore.WaitAsync(ct);
    try
    {
        // A fatal error in another call stopped the run: leave this section Pending
        if (runCts.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            return;
        }

        progress?.Report(DescribeProgress(doc, section));
        await WriteOneAsync(doc, preset, section, leadText, result, runCts, ct);
    }
    finally
    {
        semaphore.Release();
    }
}).ToList();

await Task.WhenAll(tasks);
```
**Per-unit catch ladder** (lines 193-245) — the exact outcome-classification shape to copy per part (Issues/Cast/Facts/Values → `GuidePartState`):
```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // User cancel: nothing written for this section
    section.Status = SectionStatus.Pending;
    throw;
}
catch (OperationCanceledException) when (runCts.IsCancellationRequested)
{
    // Fatal stop from another call
    section.Status = SectionStatus.Pending;
}
catch (OperationCanceledException)
{
    // Run not cancelled: the request timed out
    MarkFailed(section, result, "The request timed out. Click Retry Section.");
}
catch (LlmCallException ex) when (ex.Result.IsFatal)
{
    // Bad key or exhausted spend limit: stop all further calls, keep completed sections
    section.Status = SectionStatus.Pending;
    lock (result)
    {
        if (!result.IsFatal)
            result.FatalError = ex.Result.Message ?? ex.Message;
    }

    runCts.Cancel();
}
catch (LlmCallException ex)
{
    // Refused / truncated / invalid / non-fatal failure: mark this section, keep going
    MarkFailed(section, result, DescribeOutcome(ex.Result));
}
catch (Exception ex)
{
    MarkFailed(section, result, ex.Message);
}
```
**Outcome → message** (lines 255-261):
```csharp
private static string DescribeOutcome(LlmResult result) => result.Outcome switch
{
    LlmOutcome.Refused => $"The model declined: {result.Message}",
    LlmOutcome.Truncated => "The response was cut off. Click Retry Section.",
    LlmOutcome.Invalid => "The response could not be read. Click Retry Section.",
    _ => $"{result.FailureKind}: {result.Message}"
};
```
**Single-entry regenerate contract** (lines 151-170): look up by ID, throw `ArgumentException` for unknown, call, then mutate in place; failures propagate and leave old text untouched:
```csharp
public async Task RegenerateSectionAsync(CaseDraft draft, string sectionId, string guidance, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(draft);
    var doc = draft.CoreDocument;
    var section = doc?.FindSection(sectionId);
    if (doc == null || section == null)
        throw new ArgumentException($"Unknown section '{sectionId}'.", nameof(sectionId));
    ...
    section.Paragraphs = paragraphs;
    section.Status = SectionStatus.Ready;
    section.FailureMessage = string.Empty;
    section.IsUserEdited = false;
    draft.Revision++;
}
```
**Resume skips hand-edited work** (lines 83-87) — same rule for guide regeneration:
```csharp
// Hand-written text is never overwritten by a resume or retry (CR-02); only Regenerate Section/All replace it
var todo = doc.Sections
    .Where(s => s.Status is SectionStatus.Pending or SectionStatus.Failed)
    .Where(s => !(s.IsUserEdited && s.Paragraphs.Count > 0))
    .ToList();
```
**Revision bump in finally** (lines 137-142):
```csharp
finally
{
    if (result.SectionsWritten > 0)
        draft.Revision++;
}
```
**Prompt guidance + current-text appendix** (lines 383-396):
```csharp
if (!string.IsNullOrWhiteSpace(guidance))
{
    sb.AppendLine();
    sb.AppendLine();
    sb.Append("Guidance from the user: " + guidance.Trim());
}
```
**Mapping with `Clean` + name-resolution of references** (lines 588-609, 664) — model for resolving `organizationRef`, `issueIds`, `actorIds` (drop unknowns, record `MappingNotes`):
```csharp
// Unresolved names are dropped
AgainstPartyIds = doc.Parties
    .Where(p => against.Any(a =>
        string.Equals(a, p.ShortName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a, p.Name, StringComparison.OrdinalIgnoreCase)))
    .Select(p => p.Id)
    .ToList(),
...
private static string Clean(string value) => (value ?? string.Empty).Trim();
```
**Fictional phone rule** (lines 633-634) — reuse for code-built signatures:
```csharp
// Fictional 555-01xx range reserved for fiction; never a real number (Pitfall 12)
Phone = $"(555) 555-01{Random.Shared.Next(0, 100).ToString("D2", CultureInfo.InvariantCulture)}",
```
**Strict DTO style** (lines 666-866) — nested `internal class`, every property `[JsonPropertyName]` + `[Description]`, non-nullable with defaults, enums typed (become string enums), dates as `"YYYY-MM-DD"` strings:
```csharp
// API Response DTOs (strict schemas: no nullable properties, dates as YYYY-MM-DD strings)
internal class FactSubsectionDto
{
    [JsonPropertyName("heading")]
    [Description("Subsection heading")]
    public string Heading { get; set; } = string.Empty;

    [JsonPropertyName("startDate")]
    [Description("YYYY-MM-DD")]
    public string StartDate { get; set; } = string.Empty;
    ...
}
```
**Result object** (lines 869-880) — copy for `CaseGuideGenerationResult` (add per-part counts):
```csharp
public class CoreDocumentGenerationResult
{
    public int SectionsWritten { get; set; }
    public int SectionsFailed { get; set; }
    public string FatalError { get; set; } = string.Empty;
    public bool IsFatal => FatalError.Length > 0;
}
```

---

### `Services/Case/CaseGuidePrompts.cs` (utility, static)

**Analog:** `CoreDocumentGenerator.SystemPrompt` (line 22) and `BuildOutlinePrompt` (lines 470-523).

The system prompt must be a `const string` (byte-stable). Copy the D-01 wording verbatim from line 22:
> "Keep every person, company and organization named in the scenario exactly as the scenario names them, including characters and companies from films, TV series, books or games, which are already fictional; never rename them. Invent names only for people, companies, law firms and counsel the scenario does not name, and make those invented names fictional. Never use the name of a real-world person, company, law firm or judge unless the user approved it."
and the D-02 sentence: "Take a firm position ... Do not hedge, describe findings as unproven or unconfirmed".

**Substitution lines in the user prompt** (lines 501-517) — copy into a shared `AppendNameRules(StringBuilder, CaseDraft)` helper used by every guide prompt:
```csharp
var applied = draft.Substitutions
    .Where(s => s.Apply && !string.IsNullOrWhiteSpace(s.Original) && !string.IsNullOrWhiteSpace(s.Substitute))
    .ToList();
if (applied.Count > 0)
{
    sb.AppendLine($"Use these fictional names exactly: {string.Join(", ", applied.Select(s => s.Substitute.Trim()))}");
    sb.AppendLine($"Never use these real names: {string.Join(", ", applied.Select(s => s.Original.Trim()))}");
}

// CASE-03 override (WR-01): names the user confirmed keeping. Stated here, not in the byte-stable system prompt.
var kept = draft.Substitutions
    .Where(s => !s.Apply && !string.IsNullOrWhiteSpace(s.Original))
    .Select(s => s.Original.Trim())
    .Distinct(StringComparer.Ordinal)
    .ToList();
if (kept.Count > 0)
    sb.AppendLine($"The user has approved keeping these names exactly as written; use them unchanged as an exception to the fictional-names rule: {string.Join(", ", kept)}");
```
Party/count listing format (lines 358-368) — reuse for issue / cast-roster / fact-section context lines. Builders are `internal static string Build*Prompt(...)` so tests can assert on them (as `Outline_PromptCarriesPresetNumbers` does).

---

### `Services/Case/CaseGuideValidator.cs` (static validator)

**Analog:** `Services/Case/CoreDocumentValidator.cs`.

**Finding type + severity enums** (lines 6-34) — copy as `GuideFinding` with `GuideArea` (Issues/Cast/Facts/Values/Guide) and `EntityId` instead of `SectionId`. Note: do **not** name it "Issue" (UI-SPEC Pitfall 11):
```csharp
public enum IssueSeverity { Warning, Error }
...
public class CoreDocumentIssue
{
    public IssueSeverity Severity { get; set; }
    public IssueCategory Category { get; set; }
    public string SectionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
```
**Never-throw entry** (lines 56-72):
```csharp
public static List<CoreDocumentIssue> Validate(CaseDraft draft)
{
    var issues = new List<CoreDocumentIssue>();
    var doc = draft?.CoreDocument;
    if (draft == null || doc == null)
        return issues;

    issues.AddRange(ValidateOutline(doc, draft.Preset));
    ...
    return issues;
}
```
**Counters** (lines 102-106):
```csharp
public static int CountErrors(IEnumerable<CoreDocumentIssue> issues) =>
    issues.Count(i => i.Severity == IssueSeverity.Error);
public static int CountWarnings(IEnumerable<CoreDocumentIssue> issues) =>
    issues.Count(i => i.Severity == IssueSeverity.Warning);
```
**Preset range warning** (lines 114-119) — reuse for key cast / background per org / facts:
```csharp
private static void AddRangeWarning(List<CoreDocumentIssue> issues, int n, string noun, int min, int max, ComplexityPreset preset)
{
    if (n < min || n > max)
        issues.Add(Warning(IssueCategory.Preset, string.Empty,
            $"The document has {n} {noun}; the {preset.Level} preset expects {ComplexityPreset.FormatRange(min, max)}."));
}
```
**Date checks** (lines 123-136):
```csharp
var hasStart = CaseDraft.TryParseDate(doc.TimelineStart, out var start);
if (!hasStart)
    issues.Add(Error(IssueCategory.Timeline, string.Empty, "The timeline start date must be a real date in yyyy-MM-dd form."));
```
**Real-name leak scan with regex-timeout guard** (lines 351, 356-359):
```csharp
AddLeaks(issues, section.Id, NameSubstitution.FindLeaks(text, subs));
...
catch (RegexMatchTimeoutException)
{
    issues.Add(Warning(IssueCategory.Structure, section.Id, TooLargeToScan));
}
```
**Factory helpers** (lines 435-449):
```csharp
private static CoreDocumentIssue Error(IssueCategory category, string sectionId, string message) => new()
{
    Severity = IssueSeverity.Error,
    Category = category,
    SectionId = sectionId,
    Message = message
};
```
Null-safety: use `(fact.ActorIds ?? new())` and `.Where(x => x != null)` per RESEARCH validator skeleton (case.json is untrusted input).

---

### `Services/Case/CaseGuideEditing.cs` (pure CRUD helpers)

**Analog:** `Services/Case/CoreDocumentEditing.cs` (55 lines).

Shape: `public static (partial) class`, methods take `CaseDraft draft` + ID, return `bool` (false = unknown entity or no change, Revision untouched), set `IsUserEdited = true`, bump `draft.Revision`:
```csharp
public static bool ApplyEdit(CaseDraft draft, string sectionId, string editorText)
{
    var section = draft?.CoreDocument?.FindSection(sectionId);
    if (draft == null || section == null)
        return false;

    var paragraphs = SplitParagraphs(editorText);
    if (paragraphs.SequenceEqual(section.Paragraphs))
        return false;

    section.Paragraphs = paragraphs;
    section.IsUserEdited = true;

    // The user wrote a failed or unwritten section by hand: it counts as written (CR-02)
    if ((section.Status is SectionStatus.Failed or SectionStatus.Pending) && paragraphs.Count > 0)
    {
        section.Status = SectionStatus.Ready;
        section.FailureMessage = string.Empty;
    }

    draft.Revision++;
    return true;
}
```
`ApplyCastEdit` returns affected fact IDs (`List<string>`) instead of `bool` (RESEARCH Pattern 8). Name-replacement regex: `Regex.Escape` + word boundaries + timeout (see Shared: Regex).

---

### `Services/Case/CanonicalValueScanner.cs` (static regex scanner)

**Analog:** `Services/Case/CitationScanner.cs`.

Finding class + `static partial class` + shared timeout constant + `[GeneratedRegex(..., MatchTimeoutMs)]` (lines 11-47):
```csharp
public static partial class CitationScanner
{
    /// <summary>
    /// Match timeout for the scanning patterns; callers treat RegexMatchTimeoutException as "too large to scan".
    /// </summary>
    public const int MatchTimeoutMs = 1000;

    // "2019 WL 1234567", "2020 U.S. Dist. LEXIS 12345"
    [GeneratedRegex(@"\b(?:19|20)\d{2}\s+(?:WL|...)\s+\d+\b", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex DatabaseCitation();
```
Reuse `CitationScanner.MatchTimeoutMs` (as `CoreDocumentValidator` does at line 44) or declare its own. Each pattern gets a one-line example comment.

---

### `Services/Case/IdAllocator.cs`, `EmailIdentity.cs`, `IssueExtractor.cs`, `GuideStaleness.cs`, `CaseFolderNaming.cs` (pure static utilities)

**Analogs:** ID format from `CoreDocumentGenerator.MapOutline` (`Id = $"party-{i + 1}"`, line 578); hashing from `CaseDraft.ComputeHash`; sanitizing from `Helpers/FileNameHelper.cs`.

`FileNameHelper.SanitizeForFileName` (lines 35-54) — reuse the invalid-char filter only:
```csharp
var invalid = Path.GetInvalidFileNameChars();
var sanitized = new string(input.Where(c => !invalid.Contains(c)).ToArray());
```
**Do not** call it directly for folder names: it replaces spaces with `_` (line 45) and returns `"unnamed"`; the case folder must read `Dunder Mifflin v. Staplex`. Add trailing-dot/space trim, reserved device names, 80-char cap, ` (2)` suffixes in `CaseFolderNaming`.

Legal-suffix regex to extend for `EmailIdentity` — `NameSubstitution.cs` lines 12-13:
```csharp
[GeneratedRegex(@",?\s+(?:Inc\.?|LLC|L\.L\.C\.|Corp\.?|Corporation|Co\.?|Ltd\.?|LP|LLP)$")]
private static partial Regex CorporateSuffix();
```
Staleness hash: `CaseDraft.ComputeHash(JsonSerializer.Serialize(new { ... }, CaseDraft.JsonOptions))` (RESEARCH Pattern 8). Dismissed-hash idea = `StepCoreDocument._declinedStaleHash` (line 62, used lines 1324-1336).

---

### `Services/Case/CaseStore.cs` (service, file-I/O)

**Analog:** `Services/ApiKeyStore.cs`.

**Injectable root folder for tests** (lines 20-27) — copy for both the Cases root and the recent.json folder:
```csharp
private readonly string _folder;

public ApiKeyStore(string? folder = null)
{
    _folder = folder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ReelDiscovery");
}
```
**Atomic replace** (lines 157-164) — baseline; Phase 3 hardens it per RESEARCH "Atomic write" (unique tmp name, WriteThrough + Flush(true), retry 5x on `IOException`/`UnauthorizedAccessException`, delete tmp on final failure):
```csharp
private void SaveDto(KeyFileDto dto)
{
    Directory.CreateDirectory(_folder);
    var tmp = KeysFilePath + ".tmp";
    File.WriteAllText(tmp, JsonSerializer.Serialize(dto));
    // Atomic replace so a crash mid-write never leaves a truncated keys.dat
    File.Move(tmp, KeysFilePath, overwrite: true);
}
```
**Corrupt-file tolerance for recent.json** (lines 146-154) — fine for recent.json (non-critical, commented), but **not** for case.json load (must surface the UI-SPEC load-error copy):
```csharp
catch (JsonException)
{
    // Corrupt key file: treat all keys as absent so the user can re-enter them
    return new KeyFileDto();
}
```
Private nested DTO for recent.json entries with `[JsonPropertyName]` (lines 166-173).

---

### `Services/Case/CaseGuideDocxExporter.cs` (service, OpenXml)

**Analog:** `Services/OfficeDocumentService.CreateWordDocument` (lines 14-119).

Reuse: package creation into `MemoryStream`, `Body` + final `SectionProperties`, newline → `Break`, `Space = Preserve` (lines 18-23, 102-118):
```csharp
using var stream = new MemoryStream();
using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
{
    var mainPart = doc.AddMainDocumentPart();
    mainPart.Document = new Document();
    var body = mainPart.Document.AppendChild(new Body());
    ...
    for (int i = 0; i < lines.Length; i++)
    {
        run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        if (i < lines.Length - 1)
            run.AppendChild(new Break());
    }
    ...
    body.AppendChild(sectionProps);
    mainPart.Document.Save();
}
return stream.ToArray();
```
**Do NOT copy** the `RunProperties` child order at lines 41-46 / 95-99 (`Bold, FontSize, Color, RunFonts`) — it is the pinned schema violation in `DocumentRegressionTests.Word_OpensAndValidates`. Correct order: `RunFonts, Bold, ..., Color, FontSize, FontSizeComplexScript`. Use the RESEARCH "Word table (schema-ordered)" excerpt for tables. Imports: only `DocumentFormat.OpenXml`, `.Packaging`, `.Wordprocessing` (no Spreadsheet/Presentation, so no aliasing needed). Numbering/caption text: call `CoreDocumentAssembler.Number`, `BuildCaption`, `BuildMemoHeader`, `BuildSignatureBlock` (public statics, `CoreDocumentAssembler.cs` lines 73, 120, 156, 178).

---

### `Helpers/GridStyle.cs` (new static UI helper)

**Analog:** `StepCoreDocument` issues grid, lines 434-462 — extract verbatim, except padding `pad - 4` (=12) becomes **15** per UI-SPEC Content x:
```csharp
_gridIssues = new DataGridView
{
    Dock = DockStyle.Fill,
    ReadOnly = true,
    AutoGenerateColumns = false,
    AllowUserToAddRows = false,
    AllowUserToDeleteRows = false,
    AllowUserToResizeRows = false,
    RowHeadersVisible = false,
    MultiSelect = false,
    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    BackgroundColor = Theme.Surface,
    BorderStyle = BorderStyle.None,
    CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
    GridColor = Theme.Border,
    EnableHeadersVisualStyles = false,
    ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
    Font = Theme.BodyFont()
};
_gridIssues.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceMuted;
_gridIssues.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
_gridIssues.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceMuted;
_gridIssues.ColumnHeadersDefaultCellStyle.Font = Theme.SmallFont();
_gridIssues.ColumnHeadersDefaultCellStyle.Padding = new Padding(pad - 4, LogicalToDeviceUnits(6), 0, LogicalToDeviceUnits(6));
_gridIssues.DefaultCellStyle.Padding = new Padding(pad - 4, 0, 0, 0);
_gridIssues.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
_gridIssues.DefaultCellStyle.SelectionForeColor = Theme.Text;
_gridIssues.RowTemplate.Height = LogicalToDeviceUnits(32);
```
Signature `Apply(DataGridView grid, Control owner)` — `owner.LogicalToDeviceUnits(n)` replaces the bare call (it is a public `Control` method). `ReadOnly` should not be forced (Cast/Facts/Values grids edit cells).

### `Helpers/TextFieldHost.cs` (new factory)

**Analog:** `StepCoreDocument` guidance box, lines 341-361:
```csharp
// Guidance box: borderless TextBox inside a rounded border matching the buttons' height
_txtGuidance = new TextBox
{
    Dock = DockStyle.Fill,
    BorderStyle = BorderStyle.None,
    Font = Theme.BodyFont(),
    ForeColor = Theme.Text,
    BackColor = Theme.Surface,
    PlaceholderText = "Optional guidance for Regenerate Section, e.g. add the March 3 site meeting"
};
var guidanceHost = new CardPanel
{
    Dock = DockStyle.Fill,
    Radius = Theme.Radius,
    BorderColor = Theme.BorderStrong,
    Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, LogicalToDeviceUnits(Theme.SmallGap), 0)
};
var guidanceVertical = Math.Max(LogicalToDeviceUnits(4), (_btnApply.Height - _txtGuidance.PreferredHeight) / 2);
guidanceHost.Padding = new Padding(LogicalToDeviceUnits(10), guidanceVertical, LogicalToDeviceUnits(10), 0);
guidanceHost.Height = _btnApply.Height;
guidanceHost.Controls.Add(_txtGuidance);
```
Invalid state = `host.BorderColor = Theme.Danger` (`CardPanel.BorderColor` setter invalidates, `CardPanel.cs` lines 27-36).

### `Helpers/Theme.cs` (MOD: `DrawStatusDot`)

**Analog:** dot drawing in `StepCoreDocument.LstSections_DrawItem`, lines 843-851:
```csharp
var dot = LogicalToDeviceUnits(8);
var dotRect = new Rectangle(bounds.X + pad, bounds.Y + (bounds.Height - dot) / 2, dot, dot);
g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
using (var dotBrush = new SolidBrush(StatusDotColor(section, sectionIssues)))
{
    g.FillEllipse(dotBrush, dotRect);
}
g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
```
Inset becomes `owner.LogicalToDeviceUnits(15)` (not `CardPadding` 16). `Theme.cs` already has `using System.Drawing.Drawing2D;`. Palette stays unchanged.

---

### `UserControls/StepCaseGuide.cs` (wizard step, event-driven generation)

**Analog:** `UserControls/StepCoreDocument.cs` (1397 lines; read 1-900 and 940-1397).

**Imports** (lines 1-8):
```csharp
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;
```
**Fields + IWizardStep surface** (lines 21-76): `null!` control fields, `_cts`, `_isGenerating`, `StepTitle`, `CanMoveNext => !_isGenerating && ...`, `CanMoveBack => !_isGenerating`, `NextButtonText`, `StateChanged`, ctor calls `InitializeUI()`.

**Root layout with row-index comments** (lines 78-100):
```csharp
this.Padding = new Padding(0);
this.BackColor = Theme.Background;
var gap = LogicalToDeviceUnits(Theme.Gap);
var pad = LogicalToDeviceUnits(Theme.CardPadding);
var buttonHeight = LogicalToDeviceUnits(34);

var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(0), BackColor = Theme.Background };
mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                            // 0: Header
mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                        // 1: Sections + editor
_issuesRow = new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(184));
mainLayout.RowStyles.Add(_issuesRow);                                                 // 2: Issues card
mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                            // 3: Status strip
```
StepCaseGuide has 6 rows (header, banner, tabs, body, Checks 152, status) — keep the `_checksRow` RowStyle reference to toggle Absolute 152 / AutoSize like `_issuesRow.Height` (line 794).

**Header** (lines 103-163): title `TitleFont` `AutoSize=false` + `Height = PreferredHeight` + `AutoEllipsis`; subtitle `BodyFont`/`TextMuted` top margin 2; right `FlowLayoutPanel` anchored Right with `SetRowSpan(headerButtons, 2)`.

**Segmented tabs** = the Section/Full toggle (lines 252-269), extended to 4 toggles:
```csharp
var toggle = new FlowLayoutPanel
{
    FlowDirection = FlowDirection.LeftToRight,
    WrapContents = false,
    AutoSize = true,
    AutoSizeMode = AutoSizeMode.GrowAndShrink,
    ...
};
_btnShowSection = (ThemedButton)ButtonHelper.CreateButton("Section", 0, buttonHeight, ButtonStyle.Toggle);
_btnShowSection.Margin = new Padding(0, 0, LogicalToDeviceUnits(4), 0);
_btnShowSection.Selected = true;
```
Selection switch (lines 548-559): set `Selected` on each toggle, swap `Visible` on hosts.

**Grid card + detail card split** (lines 168-220): `_bodyLayout` 2 columns (`Percent 30/70` → use 60/40), cards `CardPanel { Padding = new Padding(1) }` with margin `gap / 2`; card header TLP padding `(pad, 14, pad, 10)` → change left/right to 15 (Content x). Detail card `CardPanel { Padding = new Padding(pad) }`.

**Detail toolbar divider** (lines 225, 318):
```csharp
editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(1))); // 3: Divider
...
editorLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0) }, 0, 3);
```
**State card** (lines 371-413) + centering (lines 562-567, wired 544-545) — copy exactly for writing / waiting / failed / not-written / no-guide states:
```csharp
_emptyPanel = new CardPanel { Dock = DockStyle.Fill, Visible = false };
_emptyContent = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Surface };
_lblEmptyHeading = new Label { ..., Font = Theme.HeadingFont(), Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Theme.SmallGap)) };
_lblEmptyBody = new Label { AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(520), 0), Font = Theme.BodyFont(), ForeColor = Theme.TextMuted, Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20)) };
_emptyBusyBar = new SlimProgressBar { Width = LogicalToDeviceUnits(360), Height = LogicalToDeviceUnits(6), Marquee = true, Visible = false, ... };
...
private void CenterEmptyContent()
{
    _emptyContent.Location = new Point(
        Math.Max(0, (_emptyPanel.Width - _emptyContent.Width) / 2),
        Math.Max(0, (_emptyPanel.Height - _emptyContent.Height) / 2));
}
```
**Checks card** = Issues card (lines 417-485), caption text "Checks", caption margin `(pad - 1, 12, pad, 8)` (= 15 inset), no-findings label `Theme.Success`; double-click navigation = `GridIssues_CellDoubleClick` (lines 1237-1260) using `row.Tag`.

**Status strip** (lines 487-523) — copy verbatim (label `MinimumSize` height = cancel button height, `SlimProgressBar` 220x6, `Cancel Generation` Danger).

**Handlers wired last** (lines 527-545):
```csharp
// Wire handlers only after every control exists (handlers read other controls)
_lstSections.DrawItem += LstSections_DrawItem;
...
editorCard.SizeChanged += (s, e) =>
    _lblSectionHint.MaximumSize = new Size(Math.Max(0, editorCard.Width - 2 * pad), 0);
```
**Pure display helpers as `public static` on the step** (lines 569-697: `SectionSuffix`, `StatusDotColor`, `IssuesCaption`, `EmptyStateBody`, `HasUnfinishedSections`, `ReadyStatus`) — Phase 3 equivalents: `TabCaption`, `PartNoun`, `StaleBannerText`, `StateCardText`, `ChecksCaption`, `DisplayPath`, `NextGateDecision`, `Plural`; colors must be `Theme.*` (note `SectionSuffixColor`/`ReadyStatus` still use `Color.Red/DarkOrange/Green` — do **not** copy those colors).

**Owner-drawn row text measure + draw** (lines 853-873) — for recent-cases list and any owner-drawn list:
```csharp
const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
statusWidth = TextRenderer.MeasureText(g, status, small, Size.Empty, flags & ~TextFormatFlags.EndEllipsis).Width + LogicalToDeviceUnits(4);
```
Selection bar (lines 830-838): `AccentSoft` fill + 3 px `Accent` bar.

**Rebind-only-when-changed + suppress selection** (lines 718-739) — pattern for preserving selection on grid rebuild (combine with RESEARCH "Grid rebuild that keeps sort and selection").

**Dispose-safe status + cancel on dispose** (lines 1001-1008, 1030-1046):
```csharp
// No-op once the step is disposed: generation continuations can resume after the window closed (WR-07)
private void SetStatus(string text, Color color)
{
    if (IsDisposed)
        return;
    _lblStatus.Text = text;
    _lblStatus.ForeColor = color;
}
...
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        // Closing the wizard or switching flows stops in-flight generation so no further calls are billed (WR-07)
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException)
        {
            // The run already finished and released its token source; nothing to cancel
        }
    }
    base.Dispose(disposing);
}
```
**SetBusy** (lines 1048-1057) and **run method** (lines 1059-1163) — the UI catch ladder to copy for `RunGuideAsync`; after each part also call the autosave (`CaseStore`) and never in a bare catch:
```csharp
_cts = new CancellationTokenSource();
SetBusy(true);
RefreshAll();
try
{
    var generator = new CoreDocumentGenerator(_state.CreateGateway());
    var progress = new Progress<string>(msg =>
    {
        if (IsDisposed)
            return;
        SetStatus(msg, Accent);
        ...
        RefreshAll();
    });
    var result = await generator.GenerateAsync(_state.Case, progress, _cts.Token);
    // The window may have closed during the run (WR-07)
    if (IsDisposed)
        return;
    if (result.IsFatal) { SetStatus(FatalText(result.FatalError), Color.Red); ... }
    ...
}
catch (OperationCanceledException) { SetStatus(CancelledText, Color.DarkOrange); ... }
catch (LlmCallException ex) when (ex.Result.Outcome == LlmOutcome.Refused) { ... }
catch (LlmCallException ex) when (ex.Result.IsFatal) { ... }
catch (Exception ex) { SetStatus(ErrorText(ex), Color.Red); }
finally
{
    _cts?.Dispose();
    _cts = null;
    if (!IsDisposed)
    {
        SetBusy(false);
        RefreshAll();
        ReloadEditorAfterRun();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
```
Replace `Color.Red` → `Theme.Danger`, `Color.DarkOrange` → `Theme.Warning`, `Color.Green` → `Theme.Success`; `Accent` is already `Theme.Accent`.

**Single-entry regenerate with C3 confirm** (lines 1165-1235) — confirmation `MessageBox` with `MessageBoxDefaultButton.Button2`; on success clear guidance; on `LlmCallException` keep old text and report.

**Cancel confirm C2** (lines 1262-1278), **Regenerate-all confirm** (lines 1280-1298).

**Lifecycle** (lines 1302-1396): `BindState` stores state; `OnEnterStepAsync` auto-runs when no data (`if (doc == null) { await RunGenerationAsync(...); return; }`), else refresh + stale check with declined-hash memory; `OnLeaveStepAsync` flushes pending edit; `ValidateStepAsync` = Errors block (OK), Warnings ask (Yes/No, `Button2`):
```csharp
var warnings = CoreDocumentValidator.CountWarnings(_issues);
if (warnings > 0)
{
    var answer = MessageBox.Show(
        $"{warnings} warnings remain (possible case names or citation phrases). Continue anyway?",
        "Review Warnings",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question,
        MessageBoxDefaultButton.Button2);
    return Task.FromResult(answer == DialogResult.Yes);
}
```
StepCaseGuide must NOT auto-run when a partial guide exists (UI-SPEC U13) — differs from the analog.

---

### `UserControls/StepCaseSummary.cs` (wizard step, export)

**Analog:** `StepCoreDocument` header (lines 103-163), card padding conventions, status strip (lines 487-523, label only). Contents card rows = owner TLPs with dot (`Theme.DrawStatusDot`) | text | buttons; dividers as 1 px `Theme.Border` panels (line 318). `Open Case Folder`: `Process.Start("explorer.exe", quotedDir)` only after `Directory.Exists` (RESEARCH Security). `IWizardStep` surface as StepCoreDocument, `NextButtonText => "Finish"`.

### `UserControls/LinkPicker.cs` (UserControl)

**Analog (partial):** `TextFieldHost` filter (guidance box above) + `CardPanel` border host with borderless list (`_lstSections`, lines 203-214: `BorderStyle.None`, `IntegralHeight = false`). Height computed after handle exists from `ItemHeight` (UI-SPEC DPI rule 7). `ItemCheck` → `BeginInvoke` (Pitfall 7).

### `UserControls/StepCaseScenario.cs` (MOD)

**Analog:** itself. Root TLP lines 52-69 become 2 columns; keep row comments:
```csharp
mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0: Scenario label
mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 1: Scenario input
...
```
**Repopulate inputs from draft** = existing `OnEnterStepAsync` body (lines 390-415) — extract into a `LoadInputs(CaseDraft)` method and call it after Open Case before navigating (Pitfall 5), because `OnLeaveStepAsync` → `StoreInputs` (lines 417-440) would otherwise overwrite the loaded scenario.
Restyle colors at lines 121-130, 309, 382, 462, 495, 505, 518, 545 (`Color.DimGray/Green/DarkOrange/Red`, `new Font("Segoe UI", 9F)`, `Color.FromArgb(0,120,215)`) → `Theme.TextMuted/Success/Warning/Danger/Accent`, `Theme.SmallFont()`.
Recent-cases card = StepCoreDocument Sections card (lines 181-216) with owner-drawn `ListBox` (`ItemHeight = LogicalToDeviceUnits(52)`).

### `UserControls/StepCoreDocument.cs` (MOD)

Targeted edits:
- line 68-69 `NextButtonText => "Finish"` → `"Next >"` (comment already says Phase 3 changes it).
- line 1356 `"Generate the core document before finishing."` → `"...before continuing."`; lines 1365, 1376 `"click Finish again"` → `"click Next again"`.
- line 1288 Regenerate-all copy → C9 (adds guide sentence) when `_state.Case.Guide != null`.
- Content-x fixes: line 192 header padding `pad` → 15 left/right; lines 458-459 grid padding `pad - 4` → 15 (or switch to `GridStyle.Apply`); line 841/845 dot inset `pad` → 15.
- Header row 2 notice (SmallFont, TextMuted) when `Guide != null` (headerLayout lines 103-115 gets a third AutoSize row).
- Autosave call after `RunGenerationAsync` part completion, after `RegenerateSelectedSectionAsync`, and in `ApplyPendingEdit` (line 950) when `ApplyEdit` returns true.

---

### `Forms/ThemedDialog.cs`, `RegenerateTabDialog.cs`, `DeleteReferencesDialog.cs`

**Analog:** `UserControls/EntityReviewDialog.cs` lines 37-58 (dialog chrome + AutoSize rows with comments):
```csharp
this.Text = "Real Names Found";
this.FormBorderStyle = FormBorderStyle.FixedDialog;
this.StartPosition = FormStartPosition.CenterParent;
// Pixel sizes are scaled to the display DPI; fonts in points already scale
this.ClientSize = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(440));
this.MinimizeBox = false;
this.MaximizeBox = false;
this.ShowInTaskbar = false;

var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
// Banner and button rows size to their content so text is never clipped at high DPI
mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0: Warning banner
```
Differences required by UI-SPEC: `BackColor = Theme.Background`, `Padding = 24`, `AutoScaleMode.Font` + `AutoScaleDimensions = (7F, 15F)` (as `WizardForm.InitializeComponent`, lines 82-83), width 520 logical, **content-sized height** from root TLP `PreferredSize` (not a fixed `ClientSize`), buttons via `ButtonHelper.CreateButton(..., LogicalToDeviceUnits(34), ...)`, `CancelButton` = safe choice. EntityReviewDialog's banner colors (line 13, 68 `Color.FromArgb(255,243,205)`, `FixedSingle`) are pre-theme — do not copy. Result exposure pattern: public property set before `DialogResult.OK` (`Entities`, line 19; caller `using var dlg = ...; if (dlg.ShowDialog(this) != DialogResult.OK)` in `StepCaseScenario.cs` lines 482-487).

---

### `Forms/WizardForm.cs` (MOD, shell)

**Analog:** itself.
- `StepTypesFor` (lines 221-228): add `typeof(StepCaseGuide), typeof(StepCaseSummary)` after `StepCoreDocument`.
- Stage → step **type** navigation: reuse `NavigateToStep(int)` (lines 380-386) via `_steps.FindIndex(s => s.GetType() == targetType)`.
- `FlushCurrentCaseStep` (lines 406-414) — extend the type pattern to `StepCaseGuide or StepCaseSummary`, then save via `CaseStore` (sync flush for close).
- `OnFormClosing` (lines 416-438) + `DiscardCaseMessage` / `HasUnsavedCase` (lines 395-403): replace with C10/C11/C12 logic; keep the `_closeConfirmed` guard and `e.Cancel = true; base.OnFormClosing(e); return;` shape. `WizardFlowTests.FinishPath_ValidatesBeforeClose` asserts `"Close Case"` and `e.Cancel = true` strings in OnFormClosing — C11 title is still `Close Case`, so keep it reachable there.
- `UpdateNavigationButtons` (lines 312-316) already sets "Finish" on the last step — StepCaseSummary is last, fine.

---

### Tests

**Generator tests** — analog `CoreDocumentGeneratorTests.cs`: raw-string JSON constants (lines 13-42), `NewDraft()` helper (79-84), gateway factory enqueuing canned responses (340-349), assertions on `fake.Requests` (MaxTokens, ModelId `"claude-story"`, `CaseTestData.AssertNoJsonInstructions`) (372-377), `CaseTestData.SyncProgress<string>` for progress (384-392):
```csharp
private static (LlmGateway Gateway, FakeLlmProvider Fake) CivilGateway(params LlmProviderResponse[] sectionResponses)
{
    var (gateway, fake) = CaseTestData.CreateGateway();
    fake.Responses.Enqueue(FakeLlmProvider.Ok(CivilOutlineJson));
    foreach (var response in sectionResponses)
        fake.Responses.Enqueue(response);
    ...
}
```
Failure scripting: `FakeLlmProvider.Refused(msg)`, `.Truncated()`, `.Failed(LlmFailureKind.X)`, `OnSend` for deterministic mid-run cancel (`Fakes/FakeLlmProvider.cs` lines 34, 71-81). Parallel calls dequeue in nondeterministic order — script identical responses for parallel batches, or key assertions on content not order.

**Schema strictness** — `CaseSchemaTests.cs` lines 9-17: add
```csharp
foreach (var type in CaseGuideGenerator.SchemaTypes)
    data.Add(nameof(CaseGuideGenerator), type);
```
**Serialization / model** — `CaseDraftSerializationTests.cs` lines 7-55 (round trip via `ToJson`/`FromJson`, string-enum assertions `Assert.Contains("\"Complaint\"", json)`). Add "no ApiKey/provider/model in JSON" assertion.

**UI logic** — `CoreDocumentUiLogicTests.cs` lines 1-48: header comment "Covers only the static, UI-free helpers of StepX; the UserControl is never instantiated", `[Theory]/[InlineData]` over pure helpers.

**Docx** — `DocumentRegressionTests.cs` lines 14-30: `WordprocessingDocument.Open(new MemoryStream(bytes), false)` + `new OpenXmlValidator().Validate(doc).ToList()`; Phase 3 asserts `Assert.Empty(errors)` (like `Excel_OpensAndValidates`, line 46), with the error descriptions in the message.

**Wizard flow** — `WizardFlowTests.cs` lines 11-17 (step list), 46-68 (source-text check), 70-83 (`HasUnsavedCase`) — update to 5 steps and new close semantics; `FindRepoRoot()` helper (85-94) reusable for any source-text test.

**Fixture** — `CaseTestData.cs`: `internal static class`, `CreateGateway()` (lines 30-35, Anthropic fake), `SampleComplaintDraft()` (52+). Add `SampleGuidedDraft()` here, built on `SampleComplaintDraft()`.

**Store tests** — `ApiKeyStoreTests.cs` (ctor takes a temp folder); create `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))` and delete in `Dispose`.

---

## Shared Patterns

### Gateway call + outcome handling
**Source:** `Services/Case/CoreDocumentGenerator.cs` lines 43-52 (call), 193-245 (service ladder), `UserControls/StepCoreDocument.cs` lines 1129-1162 (UI ladder)
**Apply to:** `CaseGuideGenerator`, `StepCaseGuide`
Order is always: `OperationCanceledException` (user) → `OperationCanceledException` (fatal-stop) → `OperationCanceledException` (timeout) → `LlmCallException when IsFatal` → `LlmCallException` → `Exception`.

### Theme / DPI
**Source:** `Helpers/Theme.cs` (tokens + font factories), `ButtonHelper.CreateButton(text, 0, LogicalToDeviceUnits(34), style)` (`ButtonHelper.cs` lines 20-26), `CardPanel` (`Radius` logical, `BorderColor`), `SlimProgressBar`
**Apply to:** every new UI file
Never set `Width/Height/Font` on buttons after `CreateButton`; `ThemedButton.OnTextChanged` re-fits (ThemedButton.cs lines 71-77). Fonts only from `Theme.*Font()`. No `Color.Red/DarkOrange/Green/DimGray/SystemColors` in new code.

### Init order
**Source:** `StepCoreDocument.cs` line 527 comment
**Apply to:** all new UserControls/dialogs
Create every control in `InitializeUI()`, wire all events at the end.

### WR-07 disposal safety
**Source:** `StepCoreDocument.cs` lines 1001-1008, 1030-1046, 1109-1111, 1153-1161; `WizardForm.BtnNext_Click` lines 331-337
**Apply to:** StepCaseGuide, StepCaseSummary (export), StepCaseScenario (open case)
Check `IsDisposed` after every `await` and in every progress callback; cancel `_cts` in `Dispose`.

### Regex
**Source:** `CitationScanner.cs` lines 30-33, 42-55; `CoreDocumentValidator.cs` lines 44-54, 356-359; `CoreDocumentEditing.cs` lines 12-13
**Apply to:** `CanonicalValueScanner`, `EmailIdentity`, `CaseGuideEditing` (rename), `CaseFolderNaming`, `CaseGuideValidator`
`[GeneratedRegex(pattern, options, CitationScanner.MatchTimeoutMs)]` in a `static partial class`; dynamic patterns use `new Regex(@"(?<![A-Za-z])" + Regex.Escape(name) + ..., ..., TimeSpan.FromSeconds(1))`; catch `RegexMatchTimeoutException` and degrade to a Warning, never throw from `Validate`.

### Confirmations / validation messages
**Source:** `StepCoreDocument.cs` lines 1178-1185, 1267-1272, 1346-1396
**Apply to:** all C1-C12, M1-M3
`MessageBox.Show(copy, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning|Question, MessageBoxDefaultButton.Button2)`; Next gate returns `Task.FromResult(false)` on block.

### Hash-based staleness with "declined" memory
**Source:** `CaseDraft.ComputeHash` (line 84), `StepCoreDocument` lines 62, 1323-1337
**Apply to:** `GuideStaleness`, StepCaseGuide banner `Not Now`

### Silent catch only for non-critical work (with comment)
**Source:** `WizardForm.LoadIcon` lines 49-52; `ApiKeyStore` lines 48-57
**Apply to:** recent.json read only. **Never** for case.json save/load (must surface "Not saved: ..." / load-error copy).

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| `ReelDiscovery.Tests/Render/RenderHarness.cs` + `ScreenRenderTests.cs` | test (UI render) | — | No render harness committed (RESEARCH verified). Use RESEARCH Wave 0 recipe: STA thread, P/Invoke `SetProcessDpiAwarenessContext(-4)`, skip unless `DeviceDpi == 144`, host in Form with shell padding, `DrawToBitmap`, `[Trait("Category","Render")]` |
| `Services/Case/CaseStore.cs` retry/coalescing parts | service | file-I/O | `ApiKeyStore` covers only tmp+move; OneDrive retry loop, `SemaphoreSlim(1)` single writer and latest-snapshot coalescing come from RESEARCH "Atomic write" + Pattern 9 |
| `Services/Case/CanonicalValueScanner.cs` pattern content | utility | transform | Structure copies `CitationScanner`, but no money / `No.`/`#`/`CO-`/`INV-` patterns exist yet |

## Metadata

**Analog search scope:** `Models/Case`, `Services/Case`, `Services/Ai`, `Services/ApiKeyStore.cs`, `Services/OfficeDocumentService.cs`, `Helpers/`, `UserControls/`, `Forms/`, `ReelDiscovery.Tests/{Case,Fakes,Documents}`
**Files scanned:** 34 (read in full or targeted ranges)
**Pattern extraction date:** 2026-10-01
