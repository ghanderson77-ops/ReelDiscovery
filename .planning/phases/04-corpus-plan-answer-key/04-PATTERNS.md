# Phase 4: Corpus Plan & Answer Key - Pattern Map

**Mapped:** 2026-10-02
**Files analyzed:** 49 (38 new, 11 modified)
**Analogs found:** 46 / 49 (3 have partial or no analog: `BusinessCalendar`, `ThreadShapes`, `AnswerKeyCsvWriter`. Use the RESEARCH.md Code Examples for them)

All paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery`. Line numbers were taken on 2026-10-02 at commit `a96c68d`.

## File Classification

### Models (new `Models/Corpus/`, namespace `ReelDiscovery.Models.Corpus`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Models/Corpus/CorpusSettings.cs` | model (persisted settings + `ComputeHash` + `Normalize`) | transform | `Models/Case/CaseDraft.cs` (Normalize, ComputeHash, TryParseDate) | role-match |
| `Models/Corpus/CorpusState.cs` | model (lives in `CaseDraft.Corpus`: settings, plan ref, approval, dismissed hashes, written files) | CRUD (autosave) | `Models/Case/GuidePartState.cs` + `CaseGuide.DismissedStaleKey` | exact |
| `Models/Corpus/CorpusPlan.cs` | model (plan root, own file) | file-I/O | `Models/Case/CaseGuide.cs` (lists + Normalize + CleanList) | role-match |
| `Models/Corpus/PlannedThread.cs` (+ `ThreadRelevance`, `NonRelevantCategory` enums) | model | — | `Models/Case/KeyFact.cs` (enum + class in one file, id doc comment) | exact |
| `Models/Corpus/PlannedMessage.cs` (+ `MessageKind`, `RelevanceBasis`) | model | — | `Models/Case/KeyFact.cs`, `Models/Case/CastMember.cs` | exact |
| `Models/Corpus/PlannedAttachment.cs` | model (reuses `Models.AttachmentType`) | — | `Models/Case/CanonicalValue.cs` | role-match |
| `Models/Corpus/ExternalParty.cs` | model (EXT-001 senders) | — | `Models/Case/CastMember.cs` | role-match |
| `Models/Case/CaseStage.cs` (modify) | model enum | — | itself | exact |
| `Models/Case/CaseDraft.cs` (modify: `Corpus` property, `CurrentFormatVersion = 2`, Normalize) | model | CRUD | itself | exact |
| `Models/Case/CaseGuide.cs` (modify: `ComputeHash` content projection) | model | transform | `Services/Case/GuideStaleness.cs` `CoreDocumentHash` / `Hash(object)` | exact |

### Services (new `Services/Corpus/`, namespace `ReelDiscovery.Services.Corpus`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Services/Corpus/CorpusPlanner.cs` | service (LLM orchestration, nested strict DTOs, `SchemaTypes`) | request-response, batch (parallel chunks) | `Services/Case/CaseGuideGenerator.cs` (`GenerateFactsAsync`, `RunUnitAsync`, `UnitRun`, DTOs) | exact |
| `Services/Corpus/CorpusPlanPrompts.cs` | utility (byte-stable system prompt + builders) | transform | `Services/Case/CaseGuidePrompts.cs` | exact |
| `Services/Corpus/BudgetAllocator.cs` | utility (pure) | transform | `CaseGuideGenerator.Distribute` (lines 1655-1669) | role-match |
| `Helpers/Apportion.cs` (extract) | utility (pure static) | transform | `CaseGuideGenerator.Distribute` | exact |
| `Services/Corpus/ThreadSkeletonBuilder.cs` | utility (pure, injected `Random`) | transform | `CaseGuideEditing.WidenActiveRanges` style pure guide walkers; `CaseGuideValidator.Context` lookups | partial |
| `Services/Corpus/ThreadShapes.cs` | utility (parameter table + samplers) | transform | none (closest: constant tables in `Services/Case/UsTimeZones.cs`) | partial |
| `Services/Corpus/BusinessCalendar.cs` | utility (pure; holidays, TZ resolve) | transform | `Services/Case/UsTimeZones.cs` | role-match |
| `Services/Corpus/CorpusPlanValidator.cs` (+ `PlanFinding`) | service (static, never throws) | transform | `Services/Case/CaseGuideValidator.cs` | exact |
| `Services/Corpus/PlanSummary.cs` | utility (pure projection) | transform | `StepCaseSummary` static line helpers (lines 56-92) | role-match |
| `Services/Corpus/GenerationCostEstimator.cs` | utility | transform | `Models/AIModelConfig.CalculateTotalCost` (line 53) | role-match |
| `Services/Corpus/DocIdPrefix.cs` | utility | transform | `Services/Case/CaseFolderNaming.cs` (`CaseTitle`, `DefaultFolderName`, `Sanitize`) | exact |
| `Services/Corpus/CorpusPlanStore.cs` | service (corpus-plan.json load/save) | file-I/O | `Services/Case/CaseStore.cs` (`Load`, `WriteAtomicAsync`, `PeekFormatVersion`) | exact |
| `Services/Corpus/AnswerKeyBuilder.cs` | utility (pure plan → rows) | transform | `CaseGuideDocxExporter.BuildKeyFacts` (pure projection of guide) | role-match |
| `Services/Corpus/AnswerKeyCsvWriter.cs` | utility (pure bytes) | file-I/O (bytes only) | none in repo; RESEARCH "CSV field encoding" | no analog |
| `Services/Corpus/AnswerKeyXlsxWriter.cs` | utility (pure bytes) | file-I/O (bytes only) | `Services/OfficeDocumentService.CreateExcelDocument` (lines 121-167) + `CaseGuideDocxExporter` pure-bytes shape | role-match |
| `Services/Corpus/ReviewProtocolBuilder.cs` | service (structure + 1 Story call + docx bytes) | request-response + transform | `CaseGuideDocxExporter.BuildKeyFacts` + `WordBuilder`; LLM call from `CaseGuideGenerator.EnrichIssuesAsync` | exact |
| `Services/Case/CaseGuideDocxExporter.cs` (modify: `WordBuilder`, `ParaFormat`, `Cell` → `internal`, or move to `Services/Case/WordBuilder.cs`) | utility | transform | itself | exact |

### UI

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `UserControls/StepCorpusSettings.cs` | component (wizard step, form inputs, autosave) | event-driven | `UserControls/StepCaseGuide.cs` (header, root TLP), `StepCaseSummary.cs` (scroll host), `Forms/RegenerateTabDialog.cs` (CheckBox + TextFieldHost) | role-match |
| `UserControls/StepReviewPlan.cs` | component (wizard step: header, banner, tiles, body, Checks, status strip) | event-driven | `UserControls/StepCaseGuide.cs` (InitializeUI lines 103-557) | exact |
| `UserControls/StepReviewPlan.Run.cs` | component (run/cancel/autosave/lifecycle/Next gate) | streaming progress, request-response | `UserControls/StepCaseGuide.Run.cs` | exact |
| `UserControls/MonthHistogram.cs` | component (custom-painted `Control`) | transform (paint) | `Helpers/SlimProgressBar.cs` | role-match |
| `Helpers/PercentSlider.cs` | component (custom-painted, input) | event-driven | `Helpers/SlimProgressBar.cs` + `Helpers/ThemedButton.cs` | role-match |
| `UserControls/StatTile.cs` | component (`CardPanel` + 3 labels) | — | `StepCaseSummary.CreateRow` (lines 372-460) | role-match |
| `UserControls/CorpusUiText.cs` | utility (pure copy) | transform | `UserControls/CaseGuideUiText.cs` | exact |
| `Forms/ReplanDialog.cs` | component (`ThemedDialog`) | request-response | `Forms/RegenerateTabDialog.cs` | exact |
| `UserControls/StepCaseSummary.cs` (modify: Corpus group, 3 rows, Open CSV/Excel/Word) | component | event-driven | itself (`CreateRow`, `OpenExport`) | exact |
| `Forms/WizardForm.cs` (modify: `StepTypesFor`, `StepTypeForStage`, `IsRunActive`, `StopActiveRun`, C-P2) | component (shell) | event-driven | itself | exact |
| `UserControls/CaseGuideUiText.cs` (modify: `StageLabel`) | utility | transform | itself (lines 506-514) | exact |

### Tests (`ReelDiscovery.Tests/Corpus/`, namespace `ReelDiscovery.Tests.Corpus`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Corpus/CorpusTestData.cs` | test fixture | — | `Case/CaseTestData.cs` (`SampleGuidedDraft`, `CreateGateway`, `SyncProgress`) | exact |
| `Corpus/CorpusSettingsTests.cs`, `BudgetAllocatorTests.cs`, `ThreadSkeletonTests.cs`, `ThreadShapesTests.cs`, `BusinessCalendarTests.cs`, `PlanSummaryTests.cs`, `ReviewPlanLogicTests.cs`, `CorpusUiTextTests.cs` | test (pure unit) | — | `Case/GuideStalenessTests.cs`, `Case/CaseGuideUiLogicTests.cs`, `Case/IdAllocatorTests.cs` | exact |
| `Corpus/CorpusPlannerTests.cs` | test (FakeLlmProvider) | — | `Case/CaseGuideGeneratorTests.cs` | exact |
| `Corpus/CorpusPlanValidatorTests.cs` | test | — | `Case/CaseGuideValidatorTests.cs` | exact |
| `Corpus/CorpusPlanStoreTests.cs` | test (temp folder) | — | `Case/CaseStoreTests.cs` | exact |
| `Corpus/AnswerKeyBuilderTests.cs`, `AnswerKeyCsvWriterTests.cs` | test | — | `Case/CaseGuideDocxExporterTests.cs` | role-match |
| `Corpus/AnswerKeyXlsxWriterTests.cs`, `ReviewProtocolTests.cs` | test (`OpenXmlValidator`) | — | `Case/CaseGuideDocxExporterTests.cs` (lines 11-14) | exact |
| `Corpus/CorpusPromptInvariantTests.cs` | test | — | `Case/CaseGuidePromptInvariantTests.cs` | exact |
| `Render/CorpusRenderTests.cs` | test (render, 144 DPI) | — | `Render/CaseSummaryRenderTests.cs` | exact |
| `Fakes/FakeLlmProvider.cs` (modify: lock + `Respond` func) | test fake | — | itself | exact |
| `Render/RenderHarness.cs` (modify: `WizardMin = new(1000, 540)`) | test harness | — | itself (lines 23-24) | exact |
| `Case/CaseSchemaTests.cs`, `Case/WizardFlowTests.cs`, `Case/CaseGuideModelTests.cs` (extend) | test | — | themselves | exact |

---

## Pattern Assignments

### `Models/Corpus/*.cs` (models)

**Analog:** `Models/Case/KeyFact.cs`, `Models/Case/CastMember.cs`, `Models/Case/CaseGuide.cs`

**File shape** (`KeyFact.cs` lines 1-45): enum(s) first, then one mutable class, `string.Empty` / `new()` defaults, doc comments only on non-obvious members, date fields as `yyyy-MM-dd` strings with the parse comment:
```csharp
namespace ReelDiscovery.Models.Case;

public enum FactSignificance
{
    Hot,
    Key,
    Supporting
}

public class KeyFact
{
    /// <summary>
    /// Stable id in the form F-001; allocated by code (IdAllocator), never by the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    // yyyy-MM-dd string (empty = unknown); parse only via CaseDraft.TryParseDate
    public string Date { get; set; } = string.Empty;
    ...
    public List<string> IssueIds { get; set; } = new();
```
Use the same doc-comment wording for plan ids: "Stable id in the form T-0001; formatted locally by the planner, never by the model and never via IdAllocator" (RESEARCH Pitfall 2).

**Computed display property** (`CastMember.cs` lines 37-38) when a model needs one, excluded from JSON:
```csharp
[JsonIgnore]
public string FullName => string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
```

**Normalize pattern for untrusted JSON** (`CaseGuide.cs` lines 72-155, 161-168). `CorpusPlan.Normalize()` and `CorpusSettings.Normalize()` must follow it, and reuse the `internal` helpers `CaseGuide.CleanList` / `CleanStrings`:
```csharp
public void Normalize()
{
    Issues = CleanList(Issues);
    ...
    IdCounters ??= new();
    DismissedStaleKey ??= string.Empty;
    foreach (var fact in Facts)
    {
        fact.Id ??= string.Empty;
        fact.Date ??= string.Empty;
        fact.IssueIds = CleanStrings(fact.IssueIds);
        ...
    }
}

internal static List<T> CleanList<T>(List<T>? list) where T : class
{
    list ??= new();
    list.RemoveAll(x => x == null);
    return list;
}
```
Additions for `CorpusSettings.Normalize()`: clamp `TotalEmails` to 50..5,000, `RelevantPercent` to 5..60, renormalize the four mix shares with `Apportion` when the sum is not 100, and clamp override ints (RESEARCH Pattern 1).

**Stale-dismiss state analog** for `CorpusState` (`CaseGuide.cs` lines 27-30, `GuidePartState.cs` lines 26-36):
```csharp
/// <summary>
/// GuideStaleness.StaleKey the user answered Not Now for; the banner stays hidden until the key changes.
/// </summary>
public string DismissedStaleKey { get; set; } = string.Empty;
...
public string BasisHash { get; set; } = string.Empty;
public DateTime? GeneratedUtc { get; set; }
```
`CorpusState` fields: `Settings`, `PlanId` (Guid), `PlanGuideHash`, `PlanSettingsHash`, `Approved`, `ApprovedUtc`, `DismissedStaleGuideHash`, `DismissedSettingsHash`, `WrittenFiles`.

**Hash pattern** for `CorpusSettings.ComputeHash()` (`CaseDraft.cs` lines 156-160):
```csharp
public static string ComputeHash(string text) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

public string ComputeSourceHash() =>
    ComputeHash(string.Join("|", EffectiveScenario, Complexity, DocumentPreference, CourtPreference, StatePreference));
```

**Shared split function** (Pitfall 4). Put `CorpusSettings.RelevantCount(total, percent)` on the model as a `public static int`. The UI live-counts line and `BudgetAllocator` must both call it.

---

### `Models/Case/CaseStage.cs` (modify)

**Analog:** itself (lines 1-12). Insert before `Summary`. Values are stored as names (`JsonStringEnumConverter` in `CaseDraft.JsonOptions`, line 37), so old case.json files stay valid:
```csharp
public enum CaseStage
{
    Described,
    CoreDocument,
    CaseGuide,
    CorpusSettings,
    ReviewPlan,
    Summary
}
```
`CaseDraft.AdvanceStage` (lines 87-91) compares ordinals (`if (stage > Stage)`), so inserting before `Summary` keeps the forward-only rule.

### `Models/Case/CaseDraft.cs` (modify)

- Line 30: `public const int CurrentFormatVersion = 1;` → `2`. `CaseStore.Load` (line 213) already refuses newer versions through `PeekFormatVersion`.
- Add `public CorpusState? Corpus { get; set; }` next to `public CaseGuide? Guide { get; set; }` (line 70).
- `Normalize()` (lines 96-138): add `Corpus?.Normalize();` after `Guide?.Normalize();` (line 132).

### `Models/Case/CaseGuide.cs` (modify `ComputeHash`)

**Current (line 67), hashes UI state (Pitfall 1):**
```csharp
public string ComputeHash() => CaseDraft.ComputeHash(JsonSerializer.Serialize(this, CaseDraft.JsonOptions));
```
**Copy the projection style from** `Services/Case/GuideStaleness.cs` lines 27-36 and 161-162:
```csharp
public static string CoreDocumentHash(CoreDocument doc) => Hash(new
{
    Parties = doc.Parties ?? new(),
    ...
    Sections = (doc.Sections ?? new()).Where(s => s != null).Select(s => new { s.Id, s.Paragraphs })
});

private static string Hash(object payload) =>
    CaseDraft.ComputeHash(JsonSerializer.Serialize(payload, CaseDraft.JsonOptions));
```
Project only content fields: issues (id, title, criteria, elements), organizations, cast, facts, values. Exclude `IdCounters`, `*State`, `DismissedStaleKey`, `NeedsReview`, `ReviewReason`, `IsUserEdited`, `DetailsPending`, `MappingNotes`. Extend `CaseGuideModelTests` (existing `ComputeHash_ChangesOnEdit` at line 140) with "UI-state change does not change the hash" cases.

---

### `Services/Corpus/CorpusPlanner.cs` (service, parallel LLM chunks)

**Analog:** `Services/Case/CaseGuideGenerator.cs`

**Imports** (lines 1-7):
```csharp
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;
```
(Planner adds `using ReelDiscovery.Models.Corpus;` and uses namespace `ReelDiscovery.Services.Corpus`.)

**Constants, ctor, SchemaTypes** (lines 14-43):
```csharp
public class CaseGuideGenerator
{
    public const int MaxParallelCalls = 3;
    public const int FactsMaxOutputTokens = 16_000;
    ...
    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[]
    {
        typeof(IssueDetailsDto), typeof(CastRosterDto), ...
    };

    public CaseGuideGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }
```
Planner ctor: `CorpusPlanner(ILlmGateway llm, Random? random = null)` with `_random = random ?? Random.Shared` (RESEARCH Pattern 3). `SchemaTypes` = `ThreadPlanBatchDto`, `SubjectSeedBatchDto`, `ReviewProtocolProseDto`, `GuidanceWeightsDto`.

**Parallel chunk fan-out** (lines 1530-1548): build every prompt before any call, report progress in `onStart`, and send only Story-tier requests:
```csharp
using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
using var semaphore = new SemaphoreSlim(MaxParallelCalls, MaxParallelCalls);
var run = new UnitRun(runCts, semaphore, ct, RetryHint("Key Facts"));

// Tasks return DTOs only; prompts are built here, before any merge
var tasks = units.Select((unit, i) => RunUnitAsync<FactBatchDto>(new LlmRequest
{
    Tier = LlmTier.Story,
    OperationName = $"Case Guide - Facts ({unit.Plan.Heading})",
    SystemPrompt = CaseGuidePrompts.SystemPrompt,
    UserPrompt = CaseGuidePrompts.BuildFactsPrompt(draft, unit.Plan, unit.From, unit.To, unit.Target, Array.Empty<KeyFact>()),
    MaxOutputTokens = FactsMaxOutputTokens
}, run, () => progress?.Report(new GuideProgress
{
    Part = GuidePart.Facts,
    Message = $"Writing the key facts: {unit.Plan.Heading} ({i + 1} of {units.Count})...",
    Done = i,
    Total = units.Count
}))).ToList();
```
`OperationName` should read like `"Corpus Plan - Relevant threads (chunk 3 of 17)"`, so the cost ticker labels usage.

**Merge on the calling thread after `WhenAll`** (lines 1550-1572). The difference here: the planner builds a new plan object and never mutates the draft, so a cancel just rethrows and drops the new plan:
```csharp
try
{
    try
    {
        await Task.WhenAll(tasks);
    }
    finally
    {
        // Merge once on the calling thread: generator tests have no SynchronizationContext, so lambdas run on pool threads (RESEARCH Pattern 5)
        changed = MergeFacts(guide, units, tasks);
    }
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    ...
    throw;
}
...
var outcomes = tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToList();
result.WasRefused = outcomes.Any(o => o.Refused);
if (run.IsFatal) { ... result.FatalError = run.FatalError; }
```

**Bounded call + outcome classification. Copy verbatim** (lines 1385-1495: `RunUnitAsync<T>`, `UnitOutcome<T>`, `UnitRun`). Or extract them to an `internal` shared helper in `Services/Ai`. The key catch ladder:
```csharp
catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested) { throw; }
catch (OperationCanceledException) when (run.Cts.IsCancellationRequested) { return new UnitOutcome<T>(); }   // fatal stop from another call
catch (OperationCanceledException) { return new UnitOutcome<T> { Error = $"The request timed out. {run.RetryHint}" }; }
catch (LlmCallException ex) when (ex.Result.IsFatal) { run.SetFatal(ex.Result.Message ?? ex.Message); return new UnitOutcome<T>(); }
catch (LlmCallException ex) { return new UnitOutcome<T> { Error = DescribeOutcome(ex.Result, run.RetryHint), Refused = ex.Result.Outcome == LlmOutcome.Refused }; }
catch (Exception ex) { return new UnitOutcome<T> { Error = ex.Message }; }
finally { run.Semaphore.Release(); }
```

**Strict DTO style** (lines 3114-3154). Nested `internal class`, `[JsonPropertyName]` + `[Description]` on every property, no nullables, and dates as `"YYYY-MM-DD"` strings:
```csharp
internal class FactBatchDto
{
    [JsonPropertyName("facts")]
    [Description("Dated facts for this period, in date order")]
    public List<FactDto> Facts { get; set; } = new();
}

internal class FactDto
{
    [JsonPropertyName("existingId")]
    [Description("ID of the existing fact this revises, e.g. F-012; empty string for a new fact")]
    public string ExistingId { get; set; } = string.Empty;
    ...
}
```
The planner's DTO bodies are already spelled out in RESEARCH "LLM chunk DTOs (strict-schema safe)". Copy them using this attribute layout (one attribute per line).

**Progress and result classes** (lines 3280-3309) are the models for `PlanProgress` and `CorpusPlanResult`. Put them at the bottom of the same file, outside the class:
```csharp
public class GuideProgress
{
    public GuidePart Part { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Done { get; set; }
    public int Total { get; set; }
}

public class GuidePartResult
{
    public bool Succeeded { get; set; }
    public string FailureMessage { get; set; } = string.Empty;
    public string FatalError { get; set; } = string.Empty;
    public bool IsFatal => FatalError.Length > 0;
    public bool WasRefused { get; set; }
    public List<string> Notes { get; set; } = new();
}
```
`CorpusPlanResult` adds `CorpusPlan? Plan`, `int RefusedChunks`, `int TotalChunks` (for the "declined k of n" outcome line). `PlanProgress` replaces `Part` with a `PlanStage` enum (Allocating, PlanningRelevant, PlanningOther, WritingProtocol, Validating).

**Mapping LLM ids back** uses `IdAllocator.Normalize(raw, prefix)` (`Services/Case/IdAllocator.cs` lines 50-57) to canonicalize model-supplied `CH-`/`F-`/`ISS-` ids. **Never call `IdAllocator.Next`** (it writes `guide.IdCounters`, lines 29-44).

---

### `Services/Corpus/CorpusPlanPrompts.cs` (prompt builders)

**Analog:** `Services/Case/CaseGuidePrompts.cs` lines 1-40

```csharp
using System.Text;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Shared system prompt and user-prompt builders for every case guide call (D-01, D-02, D-03).
/// Prompts carry no JSON-format instructions: the strict schema defines the response shape.
/// </summary>
public static class CaseGuidePrompts
{
    // Byte-stable for prompt caching: never interpolate run data into this text (D-01, D-02)
    public const string SystemPrompt = "...";
```
- Copy the `const` + byte-stable comment. **Do not copy the wording** "make those invented names fictional" (Pitfall 8, D-02). Keep "Keep each person ... exactly as named, including characters and companies from films, TV series, books or games" and "Take a firm position ... Do not hedge".
- Reuse `CaseGuidePrompts.AppendNameRules(sb, draft)` (`internal static`, line 25) in every relevant-chunk and protocol user prompt.
- Builders: `BuildRelevantChunkPrompt`, `BuildSubjectSeedPrompt` (no facts for non-decoy slots), `BuildDecoySeedPrompt` (must-not-mention list), `BuildProtocolPrompt` (guide only, no plan data), `BuildGuidanceWeightsPrompt`.

---

### `Helpers/Apportion.cs` + `Services/Corpus/BudgetAllocator.cs` (pure)

**Analog:** `CaseGuideGenerator.Distribute` (lines 1655-1669):
```csharp
// Largest remainder: integer shares of total proportional to the weights (equal shares when every weight is 0)
private static List<int> Distribute(int total, List<int> weights)
{
    if (weights.Sum() <= 0)
        weights = weights.Select(_ => 1).ToList();

    var sum = weights.Sum();
    var exact = weights.Select(w => (double)total * w / sum).ToList();
    var shares = exact.Select(e => (int)Math.Floor(e)).ToList();
    var left = total - shares.Sum();
    foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => exact[i] - shares[i]).ThenBy(i => i).Take(left))
        shares[i]++;

    return shares;
}
```
Extract it as `public static class Apportion` with a per-item `min` (RESEARCH "Largest remainder with a per-item minimum"). Optionally switch `CaseGuideGenerator` to it, keeping the same tie-break (`ThenBy(i => i)`).

---

### `Services/Corpus/BusinessCalendar.cs` (pure)

**Analog:** `Services/Case/UsTimeZones.cs` (static class, constant table, `Default` fallback, lines 6-118)
```csharp
public static class UsTimeZones
{
    // Phase 4 schedules emails in each organization's local business hours (PLAN-04)
    public const string Default = Eastern;
    ...
    public static string ForLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return Default;
        ...
        return Default;
    }
```
Resolution order: `org.TimeZoneId` → `UsTimeZones.ForLocation(org.Location)` → `UsTimeZones.Default` → `TimeZoneInfo.Utc`. Each lookup is wrapped in `catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)` with a comment, and results are cached per id. The holiday table and `At(...)` are in RESEARCH "Holiday table with observed dates" and "Sender-local business instant". There is no repo analog for those.

---

### `Services/Corpus/CorpusPlanValidator.cs` (static, never throws)

**Analog:** `Services/Case/CaseGuideValidator.cs`

**Finding model** (lines 64-112). `PlanFinding` keeps `Severity` (always `IssueSeverity.Warning`, D-24), `EntityId`, `Message`, `FixHint` and a `Where` label, plus an `Area` enum (`Facts`, `Issues`, `Custodian`, `Thread`, `Plan`) so double-click can switch tables. Drop `QuickFix` / `FixTargetId` / `FixValue` (the plan is read-only).

**Entry point + per-rule isolation** (lines 157-190, 215-230):
```csharp
public static List<GuideFinding> Validate(CaseDraft? draft)
{
    var findings = new List<GuideFinding>();
    if (draft?.CoreDocument == null || draft.Guide == null)
        return findings;

    Context ctx;
    try
    {
        ctx = new Context(draft);
    }
    catch (Exception ex)
    {
        // Malformed data must never crash the step that shows the checks
        findings.Add(CouldNotRun(ex));
        return findings;
    }

    Run(findings, ctx, CheckIds);
    Run(findings, ctx, CheckFacts);
    ...
    return findings;
}

private static void Run(List<GuideFinding> findings, Context ctx, Action<Context, List<GuideFinding>> rule)
{
    try
    {
        rule(ctx, findings);
    }
    catch (Exception ex)
    {
        // One broken rule must not hide the others or throw into the UI (T-03-26)
        findings.Add(CouldNotRun(ex));
    }
}
```
Signature becomes `Validate(CaseDraft? draft, CorpusPlan? plan)`. Use one `Check*` method per RESEARCH Pattern 7 rule.

**Finding factories + fluent `Fix`** (lines 937-955):
```csharp
private static GuideFinding Warning(GuideArea area, string entityId, string message) => new()
{
    Severity = IssueSeverity.Warning,
    Area = area,
    EntityId = entityId,
    Message = message
};

private static GuideFinding Fix(this GuideFinding finding, string hint, ...)
{
    finding.FixHint = hint;
    ...
    return finding;
}
```
(The class is `public static partial class`, which is needed both for the extension method and for `[GeneratedRegex]`, lines 138 and 153-155.)

**Context snapshot** (lines 960-1002): null-cleaned lists plus `Dictionary<string, CastMember> CastById`, `OrganizationById`, `HashSet<string> FactIds`, with "first entry wins" `TryAdd`. Extend it with `ThreadById`, `MessagesByThread`, `ExternalById`, and resolved `TimeZoneInfo` per org.

---

### `Services/Corpus/CorpusPlanStore.cs` (file-I/O)

**Analog:** `Services/Case/CaseStore.cs`

**Load with size cap, version peek, Normalize, user-facing failure, never throws** (lines 194-230):
```csharp
public CaseLoadResult Load(string caseJsonPath)
{
    var fileName = SafeFileName(caseJsonPath);
    try
    {
        var fullPath = Path.GetFullPath(caseJsonPath);
        if (!File.Exists(fullPath)) { ... return Fail(CaseLoadError.Missing, ...); }

        // Size cap before reading anything (T-03-12)
        if (new FileInfo(fullPath).Length > MaxCaseFileBytes)
            return Fail(CaseLoadError.TooLarge, $"Could not open {fileName}: the file is larger than 50 MB. ...");

        var json = File.ReadAllText(fullPath);
        if (PeekFormatVersion(json) > CaseDraft.CurrentFormatVersion)
            return Fail(CaseLoadError.NewerVersion, "...");

        var draft = CaseDraft.FromJson(json);
        draft.Normalize();
        ...
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException
                                   or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        var reason = ex.Message.Trim().TrimEnd('.');
        return Fail(CaseLoadError.Corrupt, $"Could not open {fileName}: {reason}. ...");
    }
}
```
Add a `plan.PlanId == state.PlanId` check (Pitfall 11) and a 100 MB cap. A failure means "no plan" plus a reason for the "saved plan could not be opened" state card.

**Atomic write** (lines 347-380). Call `CaseStore.WriteAtomicAsync(path, bytes, ct)` directly; do not reimplement it. Order: write `corpus-plan.json` first, then save `case.json` (which holds `PlanId`). Serialize with `CaseDraft.JsonOptions` (lines 33-38: `WriteIndented`, case-insensitive, `JsonStringEnumConverter`). Consider `WriteIndented = false` for the 3-6 MB plan file.

---

### `Services/Corpus/DocIdPrefix.cs`

**Analog:** `Services/Case/CaseFolderNaming.cs` lines 26-108. Read `CaseFolderNaming.CaseTitle(draft)` / `PleadingParties` for the producing party, and prefer `CaseOrganization.ShortName` (`Models/Case/CaseOrganization.cs` line 20). Copy the char-by-char `StringBuilder` filtering style from `Sanitize` (lines 72-108), keeping only A-Z, 2-4 letters, with fallback `"RD"`.

---

### `Services/Corpus/AnswerKeyXlsxWriter.cs` (pure bytes)

**Analog:** `Services/OfficeDocumentService.CreateExcelDocument` (lines 121-167). This is the package-creation skeleton:
```csharp
using var stream = new MemoryStream();
using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
{
    var workbookPart = doc.AddWorkbookPart();
    workbookPart.Workbook = new Workbook();

    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    worksheetPart.Worksheet = new Worksheet(new SheetData());

    var sheets = workbookPart.Workbook.AppendChild(new Sheets());
    var sheet = new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1,
        Name = title.Length > 31 ? title[..31] : title
    };
    sheets.Append(sheet);
    ...
    worksheetPart.Worksheet.Save();
    workbookPart.Workbook.Save();
}
return stream.ToArray();
```
**Do not modify `OfficeDocumentService`** (it is pinned by `Documents/DocumentRegressionTests.cs`). Write a new `public static class` in `Services/Corpus` and add, from the RESEARCH "XLSX sheet with frozen header and autofilter" prototype: `WorkbookStylesPart` (index 0 default), `SheetViews` → `Columns` → `SheetData` → `AutoFilter` element order, `_xlnm._FilterDatabase` defined names, `CellValues.InlineString` only, `CellValues.Number` on Statistics, and never `CellFormula`.

**Text sanitizing:** reuse `CaseGuideDocxExporter.StripInvalidXmlChars` (`internal static`, lines 476-500) for every cell, and cap cells at 32,767 characters.

**Pure-bytes contract** (`CaseGuideDocxExporter` doc comment, lines 18-21): "Pure functions: writing to disk happens in the UI through CaseStore.WriteAtomicAsync." Apply the same to `AnswerKeyCsvWriter` (`byte[] Build(IReadOnlyList<AnswerKeyRow>)`, UTF-8 BOM, CRLF, OWASP guard per RESEARCH "CSV field encoding").

---

### `Services/Corpus/AnswerKeyBuilder.cs` (pure projection)

**Analog:** `CaseGuideDocxExporter.BuildKeyFacts` (lines 333-386): deterministic order, undated items last, ids break ties, all culture-invariant:
```csharp
// Undated facts sort last; ids break ties
var facts = guide.Facts
    .OrderBy(f => CaseDraft.TryParseDate(f.Date, out var d) ? d : DateTime.MaxValue)
    .ThenBy(f => f.Id, StringComparer.Ordinal)
    .ToList();
```
For DocIDs and rows: sort by `SentAt.UtcDateTime`, then thread id, then `Seq`, with `StringComparer.Ordinal`. Format numbers with `CultureInfo.InvariantCulture` (`IdAllocator.Format`, line 69-70: `number.ToString(new string('0', width), CultureInfo.InvariantCulture)`). Subjects come from `Helpers/ThreadingHelper.AddReplyPrefix` / `AddForwardPrefix` (lines 36, 46). `AnswerKeyRow` is a mutable class with one property per column (no records).

---

### `Services/Corpus/ReviewProtocolBuilder.cs` + `WordBuilder` visibility change

**Analog:** `Services/Case/CaseGuideDocxExporter.cs`

**Exporter shape** (lines 22-51, 333-386): `public static class`, a `FileNameFor` helper, `Build*` returning `byte[]` via `new WordBuilder()` ... `word.Save(title, landscape: false)`:
```csharp
public static string FileNameFor(CaseExportKind kind, string folderName) => kind switch
{
    CaseExportKind.CoreDocument => $"{folderName} - Core Document.docx",
    ...
};
...
word.Paragraph("Key Facts & Events", new ParaFormat { Style = "Title" });
word.Paragraph(doc.Title, new ParaFormat { Style = "Subtitle" });
word.Paragraph("Chronology", new ParaFormat { Style = "Heading1" });
word.Table(
    new[] { "ID", "Date", "Fact", "Issues", "Who did it", "Who knew" },
    facts.Select(f => (IReadOnlyList<Cell>)new Cell[] { f.Id, LongDate(f.Date), new Cell(f.Description, f.Title.Length > 0 ? f.Title + ": " : null), ... }),
    new[] { 800, 1700, 6180, 1000, 2000, 2000 });
word.EndSection(landscape: true);
...
return word.Save("Key Facts & Events", landscape: false);
```
Protocol file name: `$"{folderName} - Review Protocol.docx"` (UI-SPEC S2).

**Visibility change:** `ParaFormat` (line 505), `WordBuilder` (line 527) and `Cell` (line 841) are `private sealed class` nested types. Change them to `internal sealed class` (still nested, referenced as `CaseGuideDocxExporter.WordBuilder`) or move them to `Services/Case/WordBuilder.cs`. Keep `CaseGuideDocxExporterTests` green.

**LLM prose call:** one `_llm.GenerateAsync<ReviewProtocolProseDto>(new LlmRequest { Tier = LlmTier.Story, OperationName = "Corpus Plan - Review protocol", SystemPrompt = CorpusPlanPrompts.SystemPrompt, UserPrompt = ..., MaxOutputTokens = 8_000 }, ct)` then `.GetValueOrThrow()` (pattern at `CaseGuideGenerator.cs` line 1402). It runs as the last planning stage and falls back to criteria-only prose plus a plan note.

---

### `UserControls/StepReviewPlan.cs` (wizard step layout)

**Analog:** `UserControls/StepCaseGuide.cs` lines 1-557

**Class header + IWizardStep members** (lines 14, 86-101):
```csharp
public partial class StepCaseGuide : UserControl, IWizardStep, IGuideHost
{
    ...
    public string StepTitle => "Case Guide";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isGenerating;
    public bool CanMoveBack => !_isGenerating;
    public bool IsGenerating => _isGenerating;
    public event EventHandler? StateChanged;

    public StepCaseGuide()
    {
        InitializeUI();
    }
```
`StepReviewPlan` is `public partial class StepReviewPlan : UserControl, IWizardStep` with `public bool IsPlanning` (used by `WizardForm.IsRunActive`).

**Fields with Hungarian prefixes + `null!`** (lines 27-66): `_root`, `_lblTitle`, `_lblSubtitle`, `_banner`, `_bannerRow`, `_lblBannerHeading`, `_lblBannerBody`, `_bannerButtons`, `_stateCard`, `_stateContent`, `_lblStateHeading`, `_lblStateBody`, `_btnStateAction`, `_checksCard`, `_checksRow`, `_lblChecks`, `_lblNoChecks`, `_gridChecks`, `_lblStatus`, `_progress`, `_btnCancel`.

**Root TLP with row-index comments** (lines 105-132). Rows match UI-SPEC S2 exactly (Header, Banner, Tiles, Body, Checks, Status):
```csharp
// Content aligns with the wizard's page margin (the shell supplies it)
this.Padding = new Padding(0);
this.BackColor = Theme.Background;
var gap = LogicalToDeviceUnits(Theme.Gap);
var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
var pad = LogicalToDeviceUnits(Theme.CardPadding);
var inset = LogicalToDeviceUnits(15);
var buttonHeight = LogicalToDeviceUnits(34);

_root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0), BackColor = Theme.Background };
_root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
_root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                                 // 0: Header
// Sized in SizeBanner from the page width: the wrapping text makes the card's own preferred size unusable
_bannerRow = new RowStyle(SizeType.Absolute, 0);
_root.RowStyles.Add(_bannerRow);                                                      // 1: Banner
_root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                                 // 2: Tabs row
_root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                             // 3: Body
_checksRow = new RowStyle(SizeType.AutoSize);
_root.RowStyles.Add(_checksRow);                                                      // 4: Checks card
_root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                                 // 5: Status strip
```

**Header (title + subtitle + right-side button flow)** lines 134-193. Copy as-is; replace `_btnResume` with `_btnReplan` (Default) + `_btnApprove` (Primary) + approval badge.

**Banner card** lines 195-263 (`CardPanel { BorderColor = Theme.Warning }`, heading `LabelFont`/`Warning`, body `BodyFont`/`TextMuted`, buttons `FlowLayoutPanel` anchored right, RowSpan 2), plus `SizeBanner()` (line 810) for the absolute row height. Buttons: `Re-plan...` (Primary) and `Keep This Plan` (Default).

**Segmented toggles** (lines 278-294) for the `Issue` / `Key fact` / `Custodian` switch (compact height 28):
```csharp
var toggle = (ThemedButton)ButtonHelper.CreateButton(CaseGuideUiText.PartNoun(part, true), 0, buttonHeight, ButtonStyle.Toggle);
toggle.Margin = new Padding(0, 0, part == GuidePart.Values ? 0 : LogicalToDeviceUnits(4), 0);
toggle.Selected = part == _selectedTab;
```

**State card** lines 322-363 (centered `FlowLayoutPanel TopDown`, heading `HeadingFont`, body max width 520, bottom 20, Primary action) + `CenterStateContent()` on `Resize`/`SizeChanged` (lines 555-556). Add the stage checklist TLP (width 360) in place of `_stateBar`.

**Checks card** lines 366-480. Copy the caption line (margin `inset, 12, inset, smallGap`), `_lblNoChecks` (`Success`), and grid host (bottom margin 6, `GridStyle.Apply(_gridChecks, this)`). Drop the Severity column and Fix/Fix All buttons; add `How to fix` (Fill). The column definition style is:
```csharp
_gridChecks.Columns.Add(new DataGridViewTextBoxColumn
{
    Name = "Where",
    HeaderText = "Where",
    Width = LogicalToDeviceUnits(176),
    SortMode = DataGridViewColumnSortMode.NotSortable
});
```

**Status strip** lines 482-521 (status label `MinimumSize` height = button height, `SlimProgressBar` 220x6 with margins `gap, 0, gap, 0`, `Cancel Planning` Danger, bar and cancel hidden until a run).

**Wire handlers last** (lines 523-556):
```csharp
this.Controls.Add(_root);

// Wire handlers only after every control exists (handlers read other controls)
_btnStateAction.Click += async (s, e) => await ...;
_btnCancel.Click += BtnCancel_Click;
_gridChecks.CellDoubleClick += (s, e) => NavigateToFinding(e.RowIndex);
_gridChecks.KeyDown += GridChecks_KeyDown;
_root.SizeChanged += (s, e) => { SizeBanner(); UpdateChecksLayout(); };
_stateCard.Resize += (s, e) => CenterStateContent();
```

**SetStatus is a no-op after dispose** (lines 680-688):
```csharp
// No-op once the step is disposed: generation continuations can resume after the window closed (WR-07)
public void SetStatus(string text, Color color)
{
    if (IsDisposed)
        return;
    _lblStatus.Text = text;
    _lblStatus.ForeColor = color;
}
```

**Pure static decision helpers on the step** (lines 559-627, e.g. `TabStateFor`, `VisibleFindings`) are the model for `ReviewPlanEntryDecision`. Better yet, put it in `CorpusUiText` so it is unit-tested without controls.

**Count grids (three `DataGridView`s):** `GridStyle.Apply(grid, this)` (`Helpers/GridStyle.cs` lines 15-49). For right-aligned numeric columns, override per column `DefaultCellStyle.Alignment = MiddleRight`, `Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0)`, and header padding `(0, LDU(6), LDU(15) - GridStyle.HeaderTextMargin, LDU(6))`. Sort numerics with `SortCompare` on the int value.

---

### `UserControls/StepReviewPlan.Run.cs` (run, cancel, autosave, lifecycle)

**Analog:** `UserControls/StepCaseGuide.Run.cs`

**Imports** (lines 1-8):
```csharp
using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
```
(+ `ReelDiscovery.Models.Corpus`, `ReelDiscovery.Services.Corpus`.)

**Fields + autosave** (lines 16-45):
```csharp
private CancellationTokenSource? _cts;
// The run in flight (whole guide, tab or entry), so closing can wait for it to merge its finished units (WR-01)
private Task? _activeRun;
private Guid _boundCaseId = Guid.Empty;

private void QueueSave() { _ = SaveAsync(); }

private async Task SaveAsync()
{
    if (_state == null || !CaseStore.CanSave(Draft))
        return;
    var ok = await _state.CaseStore.SaveAsync(Draft);
    if (IsDisposed)
        return;
    RefreshHeader();
    if (!ok)
        SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
}
```

**Generator creation through the gateway** (line 64): `private CorpusPlanner CreatePlanner() => new CorpusPlanner(_state.CreateGateway());`

**Run core: progress → status/bar, outcome ladder, finally restores** (lines 290-387):
```csharp
_cts = new CancellationTokenSource();
_progress.Value = 0;
SetStatus(string.Empty, Theme.Accent);
SetBusy(true);
try
{
    var generator = CreateGenerator();
    var progress = new Progress<GuideProgress>(p =>
    {
        if (IsDisposed)
            return;
        SetStatus(p.Message, Theme.Accent);
        if (p.Total > 0) { _progress.Marquee = false; _progress.Maximum = p.Total; _progress.Value = Math.Min(p.Total, p.Done); }
        else { _progress.Marquee = true; }
        RefreshAll();
    });
    var result = await work(generator, progress, partCompleted, _cts.Token);
    // The window may have closed during the run (WR-07)
    if (IsDisposed)
        return;
    ...
}
catch (OperationCanceledException) { SetStatus(CaseGuideUiText.CancelledText, Theme.Warning); }
catch (LlmCallException ex) when (ex.Result.Outcome == LlmOutcome.Refused) { ... Theme.Danger }
catch (LlmCallException ex) when (ex.Result.IsFatal) { SetStatus(CaseGuideUiText.FatalText(ex.Result.Message ?? ex.Message), Theme.Danger); }
catch (Exception ex) { SetStatus(ErrorText(ex), Theme.Danger); }
finally
{
    _cts?.Dispose();
    _cts = null;
    if (!IsDisposed)
    {
        _isGenerating = false;
        SetBusy(false);
        RefreshAll();
        await SaveAsync();
        if (!IsDisposed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
```
Phase 4 difference (D-18): on success **assign `Draft.Corpus` plan reference on the UI thread, then write `corpus-plan.json`, then `SaveAsync()`**. On cancel or failure, assign nothing, so the previous plan is restored by construction. There is no per-part `partCompleted` autosave.

**Track run + close-time cancel/wait** (lines 252-288: `TrackRun`, `CancelAndWaitForRun`). Copy verbatim; `WizardForm.StopActiveRun` calls it.

**Cancel confirm** (lines 486-498):
```csharp
var (title, text) = CaseGuideUiText.C2CancelGeneration();
var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
if (answer == DialogResult.Yes)
{
    _btnCancel.Enabled = false;
    _cts?.Cancel();
}
```
Use `CorpusUiText.C1CancelPlanning(hasPreviousPlan)` for the C-P1 copy.

**Dismiss banner** (lines 500-509) → `Keep This Plan`:
```csharp
Draft.Guide.DismissedStaleKey = GuideStaleness.StaleKey(Draft);
RefreshAll();
QueueSave();
```

**Lifecycle** (lines 513-576): `OnEnterStepAsync` handles the case switch (`_boundCaseId`), `PendingNotice`, `Draft.AdvanceStage(CaseStage.ReviewPlan)`, and save, then applies the entry decision. Note that `AdvanceStage(ReviewPlan)` happens on **Approve** per UI-SPEC; on enter, advance to `CorpusSettings` at most. `ValidateStepAsync` returns `Task.FromResult(...)` and uses `MessageBox.Show(FindForm(), msg, title, MessageBoxButtons.OK, MessageBoxIcon.Warning)` for M-P1 and `YesNo, Question` for M-P2.

**Approve write + file-open detection:** copy `StepCaseSummary.ExportAsync` / `IsFileOpenElsewhere` (`StepCaseSummary.cs` lines 708-777):
```csharp
var folder = _state.CaseStore.EnsureFolder(Draft);
// Built on the UI thread so the export is a consistent snapshot of the draft
var bytes = CaseGuideDocxExporter.Build(Draft, kind);
var path = Path.Combine(folder, fileName);
await CaseStore.WriteAtomicAsync(path, bytes);
...
catch (Exception ex) when (IsFileOpenElsewhere(ex, fileName)) { ... "Close {file name}, then click Approve Plan again." }
catch (Exception ex)
{
    // UI boundary: any export failure (I/O, access, missing guide, OpenXml) is shown, never thrown
    ...
}
```
Move `IsFileOpenElsewhere` into a small shared `internal static` helper rather than duplicating it.

**Dispose cancels the run** (lines 759-787):
```csharp
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

---

### `UserControls/StepCorpusSettings.cs` (form step)

**Analogs:** `StepCaseGuide.cs` (header lines 134-193, same `InitializeUI` preamble), `StepCaseSummary.cs` (scroll host lines 286-319), `Forms/RegenerateTabDialog.cs` (CheckBox lines 65-76).

**Scroll host** (`StepCaseSummary.cs` lines 286-296), plus a `ClientSizeChanged` width sync so no horizontal scrollbar ever appears:
```csharp
var rowsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Margin = new Padding(0) };
var rowsLayout = new TableLayoutPanel
{
    Dock = DockStyle.Top,
    ColumnCount = 1,
    AutoSize = true,
    AutoSizeMode = AutoSizeMode.GrowAndShrink,
    BackColor = Theme.Surface,
    Margin = new Padding(0)
};
```
(Use `Theme.Background` for the settings host; the cards are `Theme.Surface`.)

**Checkbox on a card** (`RegenerateTabDialog.cs` lines 65-76). Switch `BackColor` to `Theme.Surface` inside cards:
```csharp
_chkReplaceEdited = new CheckBox
{
    Text = ...,
    AutoSize = true,
    FlatStyle = FlatStyle.System,
    Font = Theme.BodyFont(),
    ForeColor = Theme.Text,
    BackColor = Theme.Background,
    UseMnemonic = false,
    Margin = new Padding(0, LDU(12), 0, 0)
};
```

**Number/date fields:** `TextFieldHost.Create(this, textBox)` (`Helpers/TextFieldHost.cs` lines 17-60) and `TextFieldHost.SetInvalid(host, invalid)` (lines 62-65) for Danger borders. Commit on `Leave`/Enter; never on every keystroke.

**Media availability:** effective toggles = saved choice && (`Office`/`Calendar` || `_state.IsMediaAvailable`) (`Models/WizardState.cs` line 46). The notice copy differs from `WizardState.MediaUnavailableNotice` (line 48-49); use the UI-SPEC text from `CorpusUiText`.

**Validation gate:** `ValidateStepAsync` → `MessageBox.Show(FindForm(), text, "Check the Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning)` + `Task.FromResult(false)` (CLAUDE.md validation convention; same as `StepCaseGuide.Run.cs` line 587).

---

### `UserControls/MonthHistogram.cs` + `Helpers/PercentSlider.cs` (custom-painted)

**Analog:** `Helpers/SlimProgressBar.cs` (lines 1-110)
```csharp
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ReelDiscovery.Helpers;

public class SlimProgressBar : Control
{
    public SlimProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        ...
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, _maximum);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = Height / 2;
        using (var trackPath = Theme.RoundedRect(track, radius))
        using (var trackBrush = new SolidBrush(Theme.Border))
        {
            g.FillPath(trackBrush, trackPath);
        }
        ...
    }
}
```
- Every public settable property gets `[DesignerSerializationVisibility(Hidden)]` (the WinForms analyzer requires it) and `Invalidate()`.
- Lengths come from `LogicalToDeviceUnits(n)` at paint time; repaint on `OnDpiChangedAfterParent`. Measure text with `TextRenderer.MeasureText(..., TextFormatFlags.NoPadding)`.
- Dots: `Theme.DrawStatusDot(g, cell, color, owner)` (`Helpers/Theme.cs` lines 64-76).
- `PercentSlider` focus/hover/pressed colors: `Theme.AccentHover` / `AccentPressed` / `AccentSoft` (`Theme.cs` lines 20-23). Keyboard via `IsInputKey` + `OnKeyDown`, and raise `ValueChanged` with `EventHandler? ValueChanged`.
- `MonthHistogram` puts `NiceAxisMax` and `HistogramLabelStep` as `public static` pure methods (tested in `CorpusUiTextTests` or `PlanSummaryTests`) and uses a `ToolTip` for hover.

---

### `UserControls/StatTile.cs`

**Analog:** `StepCaseSummary.CreateRow` / `RowLabel` (lines 372-460). Uses a `CardPanel` with padding (16, 12), a 1-column AutoSize TLP with three `RowLabel`-style labels (`AutoEllipsis = true`, `UseMnemonic = false`, `Height = PreferredHeight`):
```csharp
private static Label RowLabel(Font font, Color color, int top)
{
    var label = new Label
    {
        AutoSize = false,
        AutoEllipsis = true,
        UseMnemonic = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, top, 0, 0),
        Font = font,
        ForeColor = color
    };
    label.Height = label.PreferredHeight;
    return label;
}
```
Fonts: caption `Theme.SmallFont()`/`TextMuted`, value `Theme.TitleFont()`/`Text`, sub-line `SmallFont`/`TextMuted`. Dispose the `ToolTip` in `Dispose` (`StepCaseSummary.cs` lines 889-894).

---

### `UserControls/CorpusUiText.cs` (pure copy)

**Analog:** `UserControls/CaseGuideUiText.cs`

**Header + POCO content classes** (lines 1-55, 107-129). Use public fields, not properties, for UI content POCOs, matching `StateCardContent` / `BannerContent` / `NextGateResult`:
```csharp
using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

public class BannerContent
{
    public BannerVariant Variant;
    public string Heading = "";
    public string Body = "";
    public string PrimaryText = "";
    public string SecondaryText = "";
}

/// <summary>
/// Every Case Guide / Case Summary / Describe the Case string that carries a count, a part name or a
/// state (03-UI-SPEC Copywriting Contract). Pure functions: no controls, no colors.
/// </summary>
public static class CaseGuideUiText
{
    public const string NoChecksText = "...";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Plural(int n, string singular, string plural)
    {
        return n.ToString("N0", Invariant) + " " + (n == 1 ? singular : plural);
    }
```
Reuse `CaseGuideUiText.Plural`, `SubtitleText`, `DisplayPath`, `SaveFailedText` (lines 126, 496, 480, 665). Do not duplicate them. Decision helpers return a result POCO with a `Kind` enum like `NextGateDecision` (lines 273-299). Confirmation helpers return `(string Title, string Text)` tuples like `C2CancelGeneration()` (line 717).

### `UserControls/CaseGuideUiText.cs` (modify `StageLabel`, lines 506-514)
```csharp
public static string StageLabel(CaseStage stage)
{
    return stage switch
    {
        CaseStage.CaseGuide => "Case guide",
        CaseStage.CorpusSettings => "Corpus settings",
        CaseStage.ReviewPlan => "Plan review",
        CaseStage.Summary => "Complete",
        _ => "Core document"
    };
}
```

---

### `Forms/ReplanDialog.cs`

**Analog:** `Forms/RegenerateTabDialog.cs` (lines 1-87). This is a near-exact template:
```csharp
public class RegenerateTabDialog : ThemedDialog
{
    private TextBox _txtGuidance = null!;
    private Button _btnCancel = null!;
    private Button _btnRegenerate = null!;

    public RegenerateTabDialog(GuidePart part, int editedCount)
        : base(CaseGuideUiText.RegenerateTabTitle(ValidatePart(part)))
    {
        ...
        InitializeUI();
    }

    public string Guidance => _txtGuidance.Text.Trim();

    private void InitializeUI()
    {
        AddHeadline(...);
        AddBody(...);

        var caption = CreateWrappingLabel("Guidance (optional)", Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);

        _txtGuidance = new TextBox { PlaceholderText = ... };
        var guidanceHost = TextFieldHost.Create(this, _txtGuidance, lines: 3);
        // Three lines of optional guidance: ... an always-visible stock scrollbar in an empty field reads as unthemed chrome
        _txtGuidance.ScrollBars = ScrollBars.None;
        guidanceHost.Dock = DockStyle.Fill;
        guidanceHost.Margin = Padding.Empty;
        AddContent(guidanceHost);

        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnRegenerate = ButtonHelper.CreateButton(..., 0, LDU(34), ButtonStyle.Primary);
        _btnRegenerate.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnRegenerate);

        FinishLayout();
    }
}
```
Differences: ctor `ReplanDialog(bool approved)`; add the hint label after the field (`SmallFont`, `TextMuted`, top 8). After `AddButtons` (which sets `ActiveControl = cancel`, `ThemedDialog.cs` line 204), set `this.ActiveControl = _txtGuidance;` (UI-SPEC U16). Caller pattern: `StepCaseGuide.Run.cs` lines 107-113 (`using (var dialog = ...) { if (dialog.ShowDialog(FindForm()) != DialogResult.OK || IsDisposed) return; guidance = dialog.Guidance; }`).

---

### `UserControls/StepCaseSummary.cs` (modify)

**Analog:** itself
- Rows are built at lines 298-317 (`CreateRow(title, hasLine3, hasButtons, buttonHeight)` + `Divider()` between rows). Add the `Corpus plan`, `Answer key` and `Review protocol` rows and two group-heading labels (`SmallFont`, `TextMuted`, padding 15, 12, 15, 4). The rows host is already `AutoScroll = true` (line 286); add the width sync.
- `CreateRow` currently hard-codes `Open in Word` + `Export to Word` buttons (lines 431-437). Add an overload or parameter for custom button captions (`Open CSV`, `Open in Excel`, `Open in Word`, `Review Plan`, `Plan Corpus`).
- Open files via `OpenExport` (lines 779-802). The path comes from `CaseFolder` + a known file name, never from case.json:
```csharp
// Opens the .docx this step just wrote; the path is built from the verified case folder
Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
...
catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
```
- `Review Plan` / `Plan Corpus` buttons navigate with `((WizardForm)FindForm()).NavigateToStage(CaseStage.ReviewPlan / CorpusSettings)` (`WizardForm.cs` lines 308-315).
- Pure line helpers live as `public static` methods at the top (lines 56-92: `CoreDocumentLine`, `CastLine`, `ExportedAt`). Put the corpus row text in `CorpusUiText.SummaryRowText` and keep only calls here.
- Render entry point: `internal void LoadForRender(WizardState state)` (lines 600-604). Add the same to `StepCorpusSettings` and `StepReviewPlan` (plus a way to inject a plan/run state for render states 5-11).

### `Forms/WizardForm.cs` (modify)

- `StepTypesFor` (lines 282-292): insert `typeof(StepCorpusSettings), typeof(StepReviewPlan)` between `StepCaseGuide` and `StepCaseSummary`.
- `StepTypeForStage` (lines 297-303): add `CaseStage.CorpusSettings => typeof(StepCorpusSettings)` and `CaseStage.ReviewPlan => typeof(StepReviewPlan)`. Keep the `_ => typeof(StepCaseScenario)` fallback (T-03-60).
- `IsRunActive` (lines 534-540): add `or StepReviewPlan { IsPlanning: true }`.
- `StopActiveRun` (lines 544-568): add a `StepReviewPlan` branch calling `CancelAndWaitForRun` (or simply rely on `Dispose`, since a cancelled plan is discarded anyway).
- `OnFormClosing` (lines 581-595): use C-P2 copy from `CorpusUiText` when the active step is `StepReviewPlan`.
- Reopen without auto-plan (UI-SPEC S4): the step needs a flag such as `_state.OpenedFromDisk` or a `PendingNotice`-like one-shot to choose the `No corpus plan yet` card instead of auto-planning.

---

## Shared Patterns

### Strict-schema LLM calls (Story tier)
**Source:** `Services/Case/CaseGuideGenerator.cs` lines 1389-1495 (`RunUnitAsync`, `UnitRun`), lines 3114-3154 (DTO attributes)
**Apply to:** `CorpusPlanner` (relevant chunks, subject seeds, decoy seeds, guidance weights), `ReviewProtocolBuilder`
- `new LlmRequest { Tier = LlmTier.Story, OperationName = "...", SystemPrompt = <byte-stable const>, UserPrompt = ..., MaxOutputTokens = ... }`
- `(await _llm.GenerateAsync<T>(request, ct)).GetValueOrThrow()`
- Register every top-level DTO in `SchemaTypes` and add it to `CaseSchemaTests.CaseSchemaTypes()` (lines 9-19):
```csharp
foreach (var type in CaseGuideGenerator.SchemaTypes)
    data.Add(nameof(CaseGuideGenerator), type);
```

### Error handling at the service and UI boundaries
**Source:** `StepCaseGuide.Run.cs` lines 354-386; `CaseStore.SaveAsync` lines 120-132
**Apply to:** `StepReviewPlan.Run.cs`, `StepCorpusSettings`, `CorpusPlanner`
- `OperationCanceledException` is caught before `Exception`; `LlmCallException` is filtered by `Outcome == Refused` and `IsFatal`.
- Every swallowed exception gets a comment explaining why.
- `if (IsDisposed) return;` after every `await` in UI code (WR-07).

### Autosave + "Not saved" surfacing
**Source:** `StepCaseGuide.Run.cs` lines 24-45; `CaseStore.SaveAsync` lines 101-133
**Apply to:** both new steps (every commit → `QueueSave()`; subtitle via `CaseGuideUiText.SubtitleText`)

### Atomic file writes in the case folder
**Source:** `CaseStore.WriteAtomicAsync` (lines 347-380), `StepCaseSummary.ExportAsync` (lines 708-738)
**Apply to:** `corpus-plan.json`, `answer-key.csv`, `answer-key.xlsx`, `{folder} - Review Protocol.docx`

### Never-throwing validator with fix hints
**Source:** `CaseGuideValidator.Validate` / `Run` / `Warning` / `Fix` (lines 157-230, 937-955)
**Apply to:** `CorpusPlanValidator`

### Staleness via stored basis hash + dismissed key
**Source:** `GuideStaleness.StaleKey` (lines 82-83), `DismissStaleBanner` (`StepCaseGuide.Run.cs` lines 500-509), `CaseGuide.DismissedStaleKey`
**Apply to:** `CorpusState` (`PlanGuideHash` vs `Guide.ComputeHash()`, `PlanSettingsHash` vs `Settings.ComputeHash()`), the Review Plan banner, and the Case Summary "out of date" lines

### UI theme primitives
**Source:** `Helpers/Theme.cs`, `CardPanel.cs`, `GridStyle.cs`, `TextFieldHost.cs`, `ButtonHelper.CreateButton(text, 0, LDU(34), ButtonStyle.X)`
**Apply to:** every new control. All sizes go through `LogicalToDeviceUnits`, fonts only come from `Theme.*Font()`, and there is no `Color.Red` / `SystemColors`. Cards inside cards use `Padding = 1` with a 15 inset.

### Pure, culture-invariant text and dates
**Source:** `CaseDraft.TryParseDate` (lines 162-164), `CaseGuideDocxExporter.LongDate` (lines 468-471), `CaseGuideUiText.Plural` (line 126)
**Apply to:** `CorpusSettings` window dates, `AnswerKeyBuilder` (`yyyy-MM-dd HH:mm zzz`, `CultureInfo.InvariantCulture`), `CorpusUiText`, `PlanSummary` month keys

### Test scaffolding
**Source:** `Case/CaseTestData.cs` (`CreateGateway()` lines 30-35 uses an Anthropic fake to prove provider-agnosticism; `SyncProgress<T>` lines 44-50; `SampleGuidedDraft()` line 204, `SampleCounselDraft()` line 314), `Case/CaseGuideGeneratorTests.cs` lines 38-44:
```csharp
private static (LlmGateway Gateway, FakeLlmProvider Fake) Gateway(params LlmProviderResponse[] responses)
{
    var (gateway, fake) = CaseTestData.CreateGateway();
    foreach (var response in responses)
        fake.Responses.Enqueue(response);
    return (gateway, fake);
}
```
**FakeLlmProvider change** (`Fakes/FakeLlmProvider.cs` lines 36-63): wrap `Requests.Add` and `Responses.Dequeue` in `lock (_lock)` and add `public Func<LlmProviderRequest, LlmProviderResponse>? Respond { get; set; }`, checked before the queue (Pitfall 12). Keep `OnSend`, `ThrowOnSend` and the `Ok`/`Refused`/`Failed` factories unchanged.

**OpenXml validation in tests** (`Case/CaseGuideDocxExporterTests.cs` lines 1-14):
```csharp
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
...
var errors = new OpenXmlValidator().Validate(doc).ToList();
```
For XLSX, open with `SpreadsheetDocument.Open(new MemoryStream(bytes), false)`.

**Prompt invariants** (`Case/CaseGuidePromptInvariantTests.cs`; `CaseGuideGeneratorTests.SystemPrompt_KeepsNames` lines 63-70):
```csharp
Assert.Contains("including characters and companies from films, TV series, books or games", CaseGuidePrompts.SystemPrompt);
Assert.False(Regex.IsMatch(CaseGuidePrompts.SystemPrompt, @"every (person|character|company)[^.]*fictional", RegexOptions.IgnoreCase));
```
`CorpusPromptInvariantTests` should assert the corpus system prompt contains no "fictional" at all, is byte-identical across requests, and passes `CaseTestData.AssertNoJsonInstructions(request)` (lines 37-42).

**Render tests** (`Render/CaseSummaryRenderTests.cs` lines 1-130): `[Trait("Category", "Render")]`, `[Collection(RenderCollection.Name)]`, `RenderHarness.RunSta(() => { foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin }) ... })`, a temp-root `CaseStore` fixture (`SummaryFixture`, lines 108-129), `LoadForRender`, and `AssertRendered` (PNG exists, DPI ≥ 120, `Assert.Empty(result.Clipping)`). Dialogs use `RenderHarness.RenderForm(() => new ReplanDialog(approved), "replan-dialog-...")`.

**RenderHarness change** (lines 23-24): add `public static readonly Size WizardMin = new(1000, 540);`.

**Wizard flow tests** (`Case/WizardFlowTests.cs` lines 11-33): update the `CaseDriven_StepTypes` array and add `[InlineData(CaseStage.CorpusSettings, typeof(StepCorpusSettings))]` and `[InlineData(CaseStage.ReviewPlan, typeof(StepReviewPlan))]`.

---

## No Analog Found

| File | Role | Data Flow | Reason / what to use instead |
|------|------|-----------|------------------------------|
| `Services/Corpus/BusinessCalendar.cs` (holiday + business-time arithmetic part) | utility | transform | Nothing in the repo does calendar arithmetic. Use RESEARCH "Holiday table with observed dates" and "Sender-local business instant". Only the TZ fallback chain has an analog (`UsTimeZones`) |
| `Services/Corpus/ThreadShapes.cs` | utility | transform | No sampling tables exist. Use RESEARCH Pattern 6. Inject `Random` (never a shared static, per CLAUDE.md). Expose the table as `public static readonly` arrays so `ThreadShapesTests` can check frequencies |
| `Services/Corpus/AnswerKeyCsvWriter.cs` | utility | bytes | No CSV code in the repo. Use RESEARCH "CSV field encoding (RFC 4180 + OWASP)" verbatim, return `byte[]` (BOM + UTF-8 body) and write through `CaseStore.WriteAtomicAsync` |

## Metadata

**Analog search scope:** `Models/`, `Models/Case/`, `Services/`, `Services/Case/`, `Services/Ai/`, `UserControls/`, `Forms/`, `Helpers/`, `ReelDiscovery.Tests/{Case,Fakes,Render}/`
**Files scanned:** ~45 read or grepped (11 read in full, the rest by targeted ranges)
**Pattern extraction date:** 2026-10-02
