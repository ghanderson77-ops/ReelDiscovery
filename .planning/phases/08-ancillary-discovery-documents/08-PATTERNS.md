# Phase 8: Ancillary Discovery Documents - Pattern Map

**Mapped:** 2026-10-08
**Files analyzed:** 41 (27 new, 14 modified; 15 of the new files are tests)
**Analogs found:** 39 / 41 (`TranscriptLayout` and `CaseNumber` have no close analog)

All line numbers below were read on 2026-10-08 at commit `800ef8f`. Paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery`.

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Models/Discovery/DiscoverySettings.cs` (new) | model (settings) | CRUD (case.json) | `Models/Corpus/CorpusSettings.cs` | exact |
| `Models/Discovery/DiscoveryDocument.cs` (new) | model (record) | CRUD (sidecar) | `Models/Qa/BlindReviewVerdict.cs` | role-match |
| `Models/Discovery/DiscoveryDocumentSet.cs` (new) | model (file root) | file-I/O | `Models/Corpus/CorpusPlan.cs` (FormatVersion + Normalize) via `CorpusPlanStore` | role-match |
| `Models/Discovery/DepositionContent.cs` (new) | model | transform | `Models/Qa/BlindReviewVerdict.cs` (POCO style) | role-match |
| `Models/Discovery/WrittenDiscoveryContent.cs` (new) | model | transform | same as above | role-match |
| `Models/Case/CaseDraft.cs` (mod) | model | CRUD | itself (`Corpus` property + `Normalize`) | exact |
| `Services/Discovery/DiscoveryStore.cs` (new) | service (store) | file-I/O | `Services/Corpus/CorpusPlanStore.cs` | exact |
| `Services/Discovery/DiscoveryGenerator.cs` (new) | service (generator) | request-response, bounded fan-out | `Services/Qa/BlindReviewer.cs` + attempt loop of `Services/Evidence/ThreadUnitOfWork.cs` | exact |
| `Services/Discovery/DiscoveryPrompts.cs` (new) | service (prompts + DTOs) | transform | `Services/Qa/BlindReviewPrompts.cs` + `Services/Case/CaseGuidePrompts.cs` | exact |
| `Services/Discovery/DiscoveryValidator.cs` (new) | service (validator) | transform | `Services/Evidence/RenderValidator.cs` | role-match |
| `Services/Evidence/TextScan.cs` (new, promoted regexes) | utility | transform | `Services/Evidence/RenderValidator.cs` lines 57-99 | exact (move) |
| `Services/Discovery/DepositionPlanner.cs` (new) | service (pure planner) | transform | `Services/Evidence/RenderAssignments.cs` (StableRank-based pure assignment) | role-match |
| `Services/Discovery/WrittenDiscoveryPlanner.cs` (new) | service (pure planner) | transform | `Helpers/Apportion.cs` + `RenderAssignments.cs` | role-match |
| `Services/Discovery/CaseNumber.cs` (new) | utility | transform | `RenderAssignments.StableRank` only | partial |
| `Services/Discovery/CorpusDetector.cs` (new) | service | file-I/O (read) | `StepCaseSummary.LoadQa` (lines 1608-1640) + `CorpusPlanStore.EnsureLoaded` | role-match |
| `Services/Discovery/TranscriptLayout.cs` (new) | utility (pure layout) | transform | none (see No Analog) | none |
| `Services/Discovery/TranscriptDocxBuilder.cs` (new) | service (builder) | transform -> bytes | `Services/Case/CaseGuideDocxExporter.cs` `WordBuilder` | role-match (sibling) |
| `Services/Discovery/DiscoveryDocxBuilder.cs` (new) | service (builder) | transform -> bytes | `Services/Evidence/StorySoFarDocxBuilder.cs` + `CaseGuideDocxExporter.WritePleading` | exact |
| `Services/Discovery/DiscoveryBoilerplate.cs` (new) | utility (code text) | transform | `CoreDocumentAssembler.BuildCaption / BuildSignatureBlock` | role-match |
| `Services/Discovery/DiscoveryStaleness.cs` (new) | utility | transform | `Services/Case/GuideStaleness.cs` | exact |
| `Services/Case/CaseGuideDocxExporter.cs` (mod) | service | transform | itself: `WriteCaption`/`WriteSignatureBlock` private -> internal | exact |
| `Services/Case/CoreDocumentAssembler.cs` (mod) | service | transform | itself: `BuildCaption` overload | exact |
| `Services/Corpus/GenerationCostEstimator.cs` (mod) | service | transform | itself: `Price` (line 342) + `BlindReviewer.Estimate` | exact |
| `Services/Evidence/RenderValidator.cs` (mod) | service | transform | itself; point regexes at `TextScan` | exact |
| `UserControls/StepDiscoveryDocuments.cs` (new) | component (wizard step, layout) | event-driven | `UserControls/StepGenerateCorpus.cs` | exact |
| `UserControls/StepDiscoveryDocuments.Run.cs` (new) | component (run wiring) | event-driven | `UserControls/StepGenerateCorpus.Run.cs` | exact |
| `UserControls/DiscoveryUiText.cs` (new) | utility (copy) | transform | `UserControls/QaUiText.cs` / `GenerationUiText.cs` | exact |
| `Forms/RegenerateDiscoveryDialog.cs` (new) | component (dialog) | request-response | `Forms/RegenerateTabDialog.cs` | exact |
| `Forms/WizardForm.cs` (mod) | shell | event-driven | itself (`StepTypes`, `NavigateToStage`, `IsRunActive`, `OnFormClosing`) | exact |
| `UserControls/StepCaseSummary.cs` (mod) | component | event-driven | itself (`CreateRow`, `items[]`, `OpenMailboxesFolder`, `LoadQa`) | exact |
| `UserControls/GenerationUiText.cs` (mod) | utility (copy) | transform | itself (`CostRow`, `CostItems`) | exact |
| `ReelDiscovery.Tests/Discovery/*Tests.cs` (13 new) | test | — | `Tests/Qa/BlindReviewTests.cs`, `Tests/Case/CaseGuideDocxExporterTests.cs` | exact |
| `ReelDiscovery.Tests/Discovery/DiscoveryTestData.cs` (new) | test fixture | — | `Tests/Case/CaseTestData.cs` (`SampleCounselDraft`, `SampleMemoDraft`, `CreateGateway`) | exact |
| `ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs` (new) | test (render) | — | `Tests/Render/GenerateCorpusRenderTests.cs` | exact |
| `Tests/Render/DialogRenderTests.cs` (mod) | test (render) | — | itself (`RegenerateTabDialog_RendersWithoutClipping`) | exact |
| `Tests/Case/CaseSchemaTests.cs`, `WizardFlowTests.cs`, `CaseDraftSerializationTests.cs`, `Render/WizardShellRenderTests.cs`, `CaseSummary*Tests`, `GenerationResetTests`, `GenerationCostEstimatorTests` (mod) | test | — | themselves | exact |

---

## Pattern Assignments

### `Services/Discovery/DiscoveryGenerator.cs` (service, bounded fan-out + retries)

**Analog 1:** `Services/Qa/BlindReviewer.cs` (whole file, 365 lines). This is the closest small use of `PlanCallRun`.

**Imports pattern** (lines 1-10): explicit usings sorted alphabetically, with no blank lines between them and a file-scoped namespace.
```csharp
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Qa;
```

**Result class + SchemaTypes + ctor** (lines 12-56):
```csharp
public class BlindReviewRunResult { ... public string FatalMessage { get; set; } = string.Empty; public decimal CostUsd { get; set; } }

public const int MaxParallelCalls = 4;
private const string RetryHint = "Run the blind review again to continue.";
internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(BlindReviewPrompts.BlindReviewDto) };

public BlindReviewer(ILlmGateway reviewer, QaReportStore store)
{
    _reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
    ...
}
// Tests only: 1 makes call order deterministic
internal int MaxParallel { get; set; } = MaxParallelCalls;
```
For discovery, use `MaxParallelCalls = 3` and `RetryHint = "Click Regenerate... to try again."`. Expose `SchemaTypes` with every DTO from `DiscoveryPrompts`.

**Fan-out core** (lines 277-296): this is what D-05 and Pitfall 6 require.
```csharp
using (var run = new PlanCallRun(ct, MaxParallel, RetryHint))
{
    var tasks = todo.Select(doc => ReviewOneAsync(doc, systemPrompt, run, result, gate, () => { ... progress?.Report(value); })).ToList();
    // WhenAll waits for every task, so finished verdicts are on disk before a cancel propagates
    await Task.WhenAll(tasks);
    if (run.IsFatal)
        result.FatalMessage = run.FatalError;
}
```

**One unit: request, call, cost, persist with `CancellationToken.None`** (lines 308-356):
```csharp
var request = new LlmRequest
{
    Tier = LlmTier.Volume,                 // Discovery: LlmTier.Story (D-28)
    OperationName = OperationName,
    SystemPrompt = systemPrompt ?? string.Empty,
    UserPrompt = BlindReviewPrompts.BuildUserPrompt(doc),
    MaxOutputTokens = BlindReviewPrompts.MaxOutputTokens,   // Discovery: 16_000 per chunk
    AllowBatch = false
};
var outcome = await PlanCallRunner.RunAsync<BlindReviewPrompts.BlindReviewDto>(_reviewer, request, run);
var cost = outcome.Cost.TotalUsd;
lock (gate) result.CostUsd += cost;
if (outcome.Value == null) { if (outcome.Error.Length > 0) { lock (gate) { result.Errors++; ... } } return; }
...
// Not cancellable: a verdict that was paid for is always kept (D-13)
await _store.AppendBlindReviewVerdictAsync(verdict, CancellationToken.None);
```

**Estimate** (lines 203-228). This is the cache-aware token approximation for the Ready card and the dialog estimate. Route it through the new `GenerationCostEstimator.PriceDiscovery` so there is one pricing path.
```csharp
var protocolTokens = Tokens(systemPrompt);
var cacheable = model.MinCacheablePrefixTokens > 0 && protocolTokens >= model.MinCacheablePrefixTokens;
...
private static int Tokens(string? text) => ((text?.Length ?? 0) + 3) / 4;
```

**Analog 2 (attempt loop and keep-best):** `Services/Evidence/ThreadUnitOfWork.cs` lines 96-100 and 164-225.
```csharp
public const int MaxAttempts = 3;
...
for (var attempt = 1; attempt <= MaxAttempts; attempt++)
{
    var outcome = await profile.RenderAsync(ctx, attempt, feedback, run).ConfigureAwait(false);
    usage.Add(outcome);
    if (run.IsFatal) { ... return new ThreadOutcome { Kind = ThreadOutcomeKind.NotRun, Reason = run.FatalError, ... }; }
    if (outcome.Rendered == null)
    {
        // An earlier attempt is still the best available text
        if (best != null) break;
        ... return await FailAsync(...);   // refused / timeout / provider
    }
    var violations = profile.Check(rendered, ctx);
    // A composable attempt always beats a structurally broken one; then fewest violations wins and a tie goes to
    // the later attempt (D-14)
    var structurallyBroken = IsStructural(violations);
    var bestStructurallyBroken = IsStructural(bestViolations);
    var better = best == null
        || (bestStructurallyBroken && !structurallyBroken)
        || (bestStructurallyBroken == structurallyBroken && violations.Count <= bestViolations.Count);
    if (better) { best = rendered; bestViolations = violations; }
    if (violations.Count == 0) break;
    ...
}
```
For discovery, "structural" means D1 violations. Timeout detection uses `outcome.Error.StartsWith("The request timed out", StringComparison.Ordinal)` (line 100 `TimeoutPrefix`).

**Feedback header:** reuse the wording style from `Services/Evidence/EvidencePrompts.cs:32` / `AttachmentPrompts.cs:34`:
```csharp
internal const string FeedbackHeader = "Problems in the previous attempt (fix all of them and rewrite the whole thread):";
```
Discovery wording: `"... rewrite the whole part):"`.

**Underlying runner** (`Services/Corpus/PlanCallRunner.cs` lines 90-171): use it as-is. It returns `Value == null` with empty `Error` when the run is fatal or skipped, throws only on user cancel, and copies `Cost` on failure too. Do **not** add new try/catch scaffolding around it.

**Sidecar single-writer lock:** BlindReviewer uses `object gate` for in-memory counters. For the discovery sidecar, add a `SemaphoreSlim(1,1)` around the read-modify-write, as research Pitfall 6 requires. `CaseStore._writeLock` (CaseStore.cs ~line 344 `_writeLock.Release()`) is the precedent.

---

### `Services/Discovery/DiscoveryPrompts.cs` (prompts + strict DTOs)

**Analog:** `Services/Qa/BlindReviewPrompts.cs`

**Class shape** (lines 49-63): `internal static class`, with consts for `OperationName` and `MaxOutputTokens` and byte-stable instruction consts.
```csharp
internal static class BlindReviewPrompts
{
    public const string OperationName = "Quality checks - blind review";
    public const int MaxOutputTokens = 1_000;
    private const string Instructions = "You are a document reviewer ...";
```

**Byte-stable system prompt built from structured guide data** (lines 71-90):
```csharp
public static string BuildSystemPrompt(CaseGuide guide, ReviewProtocolProse protocol)
{
    guide ??= new CaseGuide();
    var sb = new StringBuilder();
    sb.Append(Instructions).Append('\n').Append('\n');
    AppendSection(sb, "Purpose", protocol.Purpose);
    var keyPeople = guide.Cast.Where(c => c != null && c.Tier == CastTier.Key && !CorpusPlanPrompts.IsCounsel(c)).ToList();
    if (keyPeople.Count > 0)
    {
        sb.Append("Key people (name | title | organization):\n");
        foreach (var member in keyPeople)
            sb.Append("- ").Append(member.FullName).Append(" | ").Append(member.Title?.Trim()).Append(" | ")
                .Append(guide.FindOrganization(member.OrganizationId)?.Name?.Trim()).Append('\n');
```
Note `CorpusPlanPrompts.IsCounsel(c)`. Reuse it for the D-12 eligibility filter.

**Strict DTO** (lines 232-245): nested `internal class` with `[JsonPropertyName]` + `[Description]`, no nullable fields.
```csharp
internal class BlindReviewDto
{
    [JsonPropertyName("relevant")]
    [Description("True when the document meets the criteria of at least one issue code")]
    public bool Relevant { get; set; }
    [JsonPropertyName("issueCodes")]
    [Description("Issue code ids exactly as listed; empty when not relevant")]
    public List<string> IssueCodes { get; set; } = new();
    ...
```
Usings for DTO files: `System.ComponentModel`, `System.Text`, `System.Text.Json.Serialization` (lines 1-3).

**Name rules (D-02):** `Services/Case/CaseGuidePrompts.cs`
- line 13: the system-prompt wording "Keep each person, company and organization named ... exactly as named, including characters and companies from films, TV series, books or games ... never rename them. ... Take a firm position". Copy its spirit into the discovery system prompt. **Do not** copy the clause "for a fictional e-discovery training dataset"/"make those invented names fictional", because Pitfall 5 and the prompt-invariant test forbid "fictional".
- lines 26-49: `AppendNameRules(sb, draft, includeScenario: false)` is internal. Call it with `includeScenario: false` so no raw scenario or complaint text enters the prompt (P-Ancillary). **Caveat:** line 37 emits the literal string "Use these fictional names exactly:" when substitutions exist. The planner must either accept this in the invariant test (substitutions only) or add a discovery-specific variant.

---

### `Services/Discovery/DiscoveryStore.cs` (sidecar file-I/O)

**Analog:** `Services/Corpus/CorpusPlanStore.cs` (whole file, 182 lines)

**Load result type** (lines 11-24):
```csharp
public class PlanLoadResult
{
    public CorpusPlan? Plan { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool Succeeded => Plan != null;
}
```

**Constants, test seam, JSON options, path from constant only** (lines 34-53):
```csharp
public const long MaxPlanFileBytes = 100L * 1024 * 1024;     // Discovery: 20 MB
private static readonly AsyncLocal<long> _sizeCapOverride = new();
internal static long SizeCapOverride { get => _sizeCapOverride.Value; set => _sizeCapOverride.Value = value; }
private static readonly JsonSerializerOptions PlanJsonOptions = new(CaseDraft.JsonOptions) { WriteIndented = false };
/// Plan path built only from the case folder and the constant file name, never from case.json content (T-04-26).
public static string PlanPath(string caseFolder) => Path.Combine(caseFolder, CorpusState.PlanFileName);
public static byte[] Serialize(CorpusPlan plan) => JsonSerializer.SerializeToUtf8Bytes(plan, PlanJsonOptions);
```

**Atomic save** (lines 59-70): `await CaseStore.WriteAtomicAsync(path, Serialize(plan), ct).ConfigureAwait(false);`

**Never-throws load with size cap, newer-version refusal and id check** (lines 107-140, 163-181):
```csharp
var cap = SizeCapOverride > 0 ? SizeCapOverride : MaxPlanFileBytes;
if (new FileInfo(path).Length > cap) return Fail($"The plan file is larger than {MaxPlanFileBytes / (1024 * 1024)} MB.");
var json = File.ReadAllText(path);
if (PeekFormatVersion(json) > CorpusPlan.CurrentFormatVersion) return Fail("The plan was saved by a newer version of ReelDiscovery.");
var plan = JsonSerializer.Deserialize<CorpusPlan>(json, PlanJsonOptions);
plan.Normalize();
if (plan.PlanId != expectedPlanId) return new PlanLoadResult { Reason = "The saved plan does not match this case.", IdMismatch = true };
...
catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException
                               or UnauthorizedAccessException or NotSupportedException or ArgumentException
                               or OverflowException)
```
Replace `PlanId` with `CaseId`. Copy `PeekFormatVersion` verbatim (lines 163-181).

**Discovery folder + file-name guard:** `Services/Evidence/StorySoFarDocxBuilder.cs` lines 16-38.
```csharp
public static bool IsStoryFileName(string? name)
{
    if (string.IsNullOrWhiteSpace(name) || name.Length <= FileNameSuffix.Length) return false;
    if (!name.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase)) return false;
    if (name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
    return Path.GetFileName(name) == name && !Path.IsPathRooted(name);
}
```
Build `IsDiscoveryFileName` with a `.docx` suffix check, plus a `StartsWith(fullRoot)` check after `Path.GetFullPath(Path.Combine(caseFolder, "Discovery", name))`. File names use `CaseFolderNaming.Sanitize` (CaseFolderNaming.cs:72) and `MaxLength = 80` (line 11).

---

### `Models/Discovery/DiscoverySettings.cs` + `Models/Case/CaseDraft.cs` (settings model, migration)

**Analog:** `Models/Corpus/CorpusSettings.cs`

**Range constants + mutable auto-properties** (lines 27-45):
```csharp
public class CorpusSettings
{
    public const int MinEmails = 50;
    public const int MaxEmails = 5_000;
    ...
    public int TotalEmails { get; set; } = 1_000;
```
Discovery: `MinPages = 10`, `MaxPages = 60`, `MinRfp = 5`, `MaxRfp = 80`, `MaxInterrogatories = 25`, `MaxRfa = 50`, `MaxWitnesses = 6`, plus `List<string> WitnessIds`, the party ids and `bool UserTouched`.

**Normalize clamps untrusted input** (lines 210-238):
```csharp
/// Repairs settings loaded from untrusted case.json: clamps every number to its range and sanitizes strings (T-04-01).
public void Normalize()
{
    TotalEmails = Math.Clamp(TotalEmails, MinEmails, MaxEmails);
    ...
    WindowStart ??= string.Empty;
```
Use `CaseGuide.CleanStrings(...)` for id lists (as `CaseDraft.Normalize` line 112 does), then `.Distinct(StringComparer.Ordinal).Take(MaxWitnesses)`.

**CaseDraft integration** (`Models/Case/CaseDraft.cs`):
- line 31: `public const int CurrentFormatVersion = 3;` changes to `4`.
- lines 73-76: model the new property on `Corpus`:
```csharp
/// Corpus settings, plan reference and approval state (D-04); the plan itself lives in corpus-plan.json.
public CorpusState? Corpus { get; set; }
```
- lines 138-145: add `Discovery?.Normalize();` next to `Corpus?.Normalize();`. The existing upgrade block already bumps `FormatVersion` on load.
- Test: `Tests/Case/CaseDraftSerializationTests.cs` lines 9-14 (`FormatVersion_IsThree`) changes to 4. Copy the `Corpus_RoundTrips_PlanNotSerialized` shape (line 17+) for a `Discovery_RoundTrips` test.

---

### `Models/Discovery/DiscoveryDocument.cs` / `DiscoveryDocumentSet.cs` (records)

**Analog:** `Models/Qa/BlindReviewVerdict.cs` (whole file, 39 lines). It is a flat POCO with `string.Empty` / `new()` defaults, `decimal CostUsd`, `DateTime Utc`, and a nullable stamp with a `/// <summary>` that explains staleness semantics. Apply the same shape to `GuideHash`, `CoreHash`, `CaptionHash`, `CorpusPlanId?`, `CostUsd`, `CostDetail` (`CostBreakdown`), `Warnings`, `Status` and `GeneratedUtc`. The root set gets `FormatVersion` / `CurrentFormatVersion`, like `CorpusPlan`.

---

### `Services/Discovery/DiscoveryStaleness.cs`

**Analog:** `Services/Case/GuideStaleness.cs`

**Content hash of an anonymous projection** (lines 23-36):
```csharp
/// Hash of the core document content ... Signature-block details are deliberately excluded.
public static string CoreDocumentHash(CoreDocument doc) => Hash(new
{
    Parties = doc.Parties ?? new(),
    Counts = doc.Counts ?? new(),
    ...
    Sections = (doc.Sections ?? new()).Where(s => s != null).Select(s => new { s.Id, s.Paragraphs })
});
```
Reuse `GuideStaleness.CoreDocumentHash` directly. Add a `CaptionHash` with the same `Hash(new { doc.Jurisdiction, doc.Counsel, doc.DocumentType, doc.MatterKind, doc.MemoPerspective, doc.Title })` pattern. Use `CaseDraft.ComputeHash(string)` (CaseDraft.cs:166) for SHA-256 hex, and `draft.Guide.ComputeHash()` for the guide.

Labels for `{what}` follow `ComponentLabels` (lines 15-21): `("core", "core document")`.

---

### `Services/Discovery/DepositionPlanner.cs`, `WrittenDiscoveryPlanner.cs`, `CaseNumber.cs` (pure planners)

**Determinism:** `Services/Evidence/RenderAssignments.cs` lines 34-41.
```csharp
/// Stable 64-bit rank of an id: the first 8 bytes (big-endian) of its UTF-8 SHA-256.
public static ulong StableRank(string id)
{
    // Never string.GetHashCode(): randomized per process, would change on every Continue
    return BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(id ?? string.Empty)));
}
```

**Slots:** `Helpers/Apportion.cs` lines 13-33 (`Apportion.Largest(int total, IReadOnlyList<double> weights, int min = 0)`). It returns `min` per item when the total cannot cover the minimums, so the caller raises the count to `issues.Count` first (research Pattern 5).

**Business days:** `Services/Corpus/BusinessCalendar.cs:33` `IsBusinessDay(DateOnly)` and `:39` `NextBusinessDay(DateOnly)`.

**Knowledge slice:** `Services/Evidence/KnowledgeGate.cs`
- `:57` `KnowersOf(KeyFact)`
- `:70` `KnowsFact(KeyFact, string personId, DateOnly asOf)`
- `:85` `Knowability(CanonicalValue, CaseGuide)`
- `:108` `KnowsValue(KnowableValue, string personId, DateOnly asOf)`

**Party side grouping:** `Services/Case/CoreDocumentAssembler.cs` lines 131-132.
```csharp
var plaintiffs = doc.Parties.Where(p => IsPlaintiffSide(p.Role)).ToList();
var defendants = doc.Parties.Where(p => p.Role is PartyRole.Defendant or PartyRole.ThirdParty or PartyRole.Subject).ToList();
```

**Counsel rules to rely on:** `CaseGuideGenerator.cs:572-603` (`CounselGaps`) per the research. Planners must still degrade without throwing.

**Dates:** parse only through `CaseDraft.TryParseDate` (CaseDraft.cs:173). Format with `CaseGuideDocxExporter.LongDate` (line 468, internal).

---

### `Services/Discovery/CorpusDetector.cs` (read-only corpus probe)

**Analog:** `UserControls/StepCaseSummary.cs` `LoadQa` (lines 1608-1640) and `ReprojectAfterRegenerateAsync` (lines 2249-2254).
```csharp
var qa = new QaSnapshot { Plan = plan };
if (string.IsNullOrEmpty(folder) || plan == null) return qa;
try
{
    var replay = GenerationJournal.Replay(folder, plan);
    ...
    if (replay.Readable && !replay.PlanChanged)
        qa.Generated = BlindReviewer.GeneratedEmailDocIds(replay);
    if (qa.HasEmails)
    {
        qa.Overlay = GenerationRunState.From(plan, replay).BuildFullOverlay(plan, guide);
        foreach (var row in AnswerKeyBuilder.Build(plan, guide, qa.Overlay).Where(r => r.DocType == AnswerKeyBuilder.DocTypeEmail)) ...
```
```csharp
var replay = GenerationJournal.Replay(folder, plan);
var runState = GenerationRunState.From(plan, replay);
var merged = LedgerMerger.Build(plan, guide, WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan)), runState.DeltasByThread);
```
Plan loading: `CorpusPlanStore.EnsureLoaded(draft)` (CorpusPlanStore.cs:146-159). People resolution: `PeopleDirectory.Create(guide, plan)` (PeopleDirectory.cs:53). It works with `new CorpusPlan()` when there is no corpus.

---

### `Services/Discovery/DiscoveryValidator.cs` + `Services/Evidence/TextScan.cs`

**Analog:** `Services/Evidence/RenderValidator.cs`

**Regexes to promote into `internal static partial class TextScan`** (lines 57-99; all `[GeneratedRegex(..., RegexOptions.None, 1000)]` private partials today):
```csharp
private static readonly HashSet<string> NotSurnames = new(StringComparer.OrdinalIgnoreCase) { ... };   // line 57
[GeneratedRegex(@"\b([A-Z][a-z]+)(?=[ \t]+([A-Z][a-z]+)\b)", RegexOptions.None, 1000)]          // NameBigram, line 70
[GeneratedRegex(@"\b(?:Mr|Ms|Mrs|Dr)\.?[ \t]+([A-Z][a-z]+)\b", RegexOptions.None, 1000)]          // HonorificName, line 73
[GeneratedRegex(@"\s+", RegexOptions.None, 1000)]                                                  // Whitespace, line 83
[GeneratedRegex(@"^([A-Za-z]+-)", RegexOptions.None, 1000)]                                        // IdentifierPrefix, line 87
[GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.None, 1000)]                          // IsoDate, line 94
[GeneratedRegex(@"\b(Jan(?:uary)?|...|Dec(?:ember)?)\.?[ \t]+(\d{1,2})(?:st|nd|rd|th)?\b(?:,?[ \t]+(\d{4})\b)?", ...)] // MonthDate, line 98
```
Keep `RenderValidator` compiling by pointing its call sites at `TextScan.*`. Existing RenderValidator tests are the regression net.

**Reusable public/internal helpers:**
- `RenderValidator.VariantWindow = 0.15m` (line 42)
- variant check shape (line 407): `.Where(a => Math.Abs(amount - a.Amount) <= a.Amount * VariantWindow)`
- `internal static bool ContainsWord(string text, string term)` (line 745)
- `CanonicalValueScanner.Scan(string?)` (line 42) and `AmountsEqual(string, string)` (line 67)

**Violation records:** RenderValidator returns `List<RenderViolation>`. Mirror this as `List<DiscoveryViolation>` with a rule code (D1..D8) so `DiscoveryUiText.ViolationText` can map them.

---

### `Services/Discovery/DiscoveryDocxBuilder.cs` (written discovery .docx)

**Analog 1:** `Services/Evidence/StorySoFarDocxBuilder.cs` (lines 1-8, 43-111). It is a public static builder in another namespace that reuses the internal WordBuilder.
```csharp
using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using static ReelDiscovery.Services.Case.CaseGuideDocxExporter;
...
var word = new WordBuilder();
word.Paragraph(title, new ParaFormat { Style = "Heading1" });
...
word.Paragraph(entry.Statement.Trim() + suffix, new ParaFormat { BoldLead = $"{ShortDate(...)}{Dot}{entry.Kind} " });
...
return word.Save(title, landscape: false);
```
English culture for dates: `private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");` (line 21).

**Analog 2 (caption, headings, numbered body, signature):** `Services/Case/CaseGuideDocxExporter.cs` `WritePleading` (lines 78-134).
```csharp
WriteCaption(word, CoreDocumentAssembler.BuildCaption(doc));
...
word.Paragraph("FACTUAL ALLEGATIONS", new ParaFormat { Style = "Heading1", Align = JustificationValues.Center });
...
word.Paragraph($"{paragraph.Number}. {paragraph.Text}", BodyFormat());
...
WriteSignatureBlock(word, CoreDocumentAssembler.BuildSignatureBlock(doc));
```
- `BodyFormat()` = `new() { FirstLine = 720, Align = JustificationValues.Both }` (line 452). `Compact()` = `SpaceAfter = 0` (line 454).
- Lettered list format from `WritePrayer` (lines 136-147): `new ParaFormat { IndentLeft = 1080, Hanging = 360, Align = JustificationValues.Both }`.
- **Required modification:** `WriteCaption` (line 149) and `WriteSignatureBlock` (line 192) are `private static`. Change them to `internal static`.
- `WriteCaption` already turns `"v.   Case No. ____"` into a right tab (lines 179-185), so the new `BuildCaption(doc, caseNumber, title)` overload only has to replace the underscores and the title line.

**Caption overload source:** `Services/Case/CoreDocumentAssembler.cs` `BuildCaption` lines 120-154. The `____________` literal is on line 140, and the title plus jury suffix is on lines 148-151. Make the existing method delegate to the new overload so `CaseGuideDocxExporterTests` stays green. The signature-block shape is on lines 178-200. It already skips an empty `BarNumber` (line 191).

---

### `Services/Discovery/TranscriptDocxBuilder.cs` (transcript .docx, sibling builder)

**Analog:** `CaseGuideDocxExporter.WordBuilder` (lines 527-836). Copy its structure, not its styles.
- `Save` (lines 590-611): `WordprocessingDocument.Create(stream, ...)`, `AddMainDocumentPart`, `StyleDefinitionsPart`, body elements, **final `SectionProperties` as the last body child**, `PackageProperties.Title = StripInvalidXmlChars(title)`, `Creator = "ReelDiscovery"`.
- `SectionProps` (lines 613-626): the `PageSize` + `PageMargin` shape. Use transcript geometry instead (Top/Bottom 1080, Left 1440, Right 540).
- `BuildParagraphProperties` order comment (line 642): `// Order follows CT_PPrBase: pStyle, keepNext, keepLines, pBdr, tabs, spacing, ind, jc`. Insert `PageBreakBefore` after keepLines and before pBdr/tabs.
- `ParagraphStyle(...)` (lines 817-833): CT_Style child order `name, basedOn, next, uiPriority, qFormat, pPr, rPr`.
- `BuildStyles` `DocDefaults` (lines 732-740): replace Calibri `22` with Courier New `24` and `LineRule = Exact`, `Line = "480"`.
- Run text: `new Text(segment) { Space = SpaceProcessingModeValues.Preserve }` (line 704). Always pass text through `CaseGuideDocxExporter.StripInvalidXmlChars` (line 476, internal).
- Numbers to strings: `Twips(int)` uses `CultureInfo.InvariantCulture` (line 835).
- The code skeleton for the line paragraph and page heading is in RESEARCH "Code Examples" (lines 775-819).

---

### `Services/Corpus/GenerationCostEstimator.cs` (mod: `PriceDiscovery`)

**Pattern:** constants at the top with `[ASSUMED]` comments (lines 97-127), then reuse of the private `Price` (lines 337-351):
```csharp
// [ASSUMED] heuristic (RESEARCH A9); recalibrate from real usage in Phases 5-7
public const int DocumentInput = 3_000;
...
public const decimal QaOverhead = 1.15m;
...
private static decimal Price(AIModelConfig model, int inputTokens, int outputTokens, bool batch)
```
Add `DepositionChunkInput = 7_000`, `DepositionChunkOutput = 4_500`, `WrittenSetInput = 6_000` and `OutputPerRequest = 150` (research A3). Then add `public static decimal PriceDiscovery(..., AIModelConfig? storyModel)`, which returns 0 when the model is null (as `Estimate` does, line 131) and computes `count * Price(story, in, out, false) * QaOverhead`.

---

### `UserControls/StepDiscoveryDocuments.cs` (wizard step layout)

**Analog:** `UserControls/StepGenerateCorpus.cs`

**Imports + class header** (lines 1-5, 38): `using ReelDiscovery.Helpers; using ReelDiscovery.Models; ...`, `public partial class StepDiscoveryDocuments : UserControl`. The `IWizardStep` half lives in the `.Run.cs` partial (StepGenerateCorpus.Run.cs:22 `public partial class StepGenerateCorpus : IWizardStep`).

**Fields** (lines 60-130): Hungarian control prefixes with `null!` (`private Label _lblStatus = null!;`, `private SlimProgressBar _progress = null!;`, `private readonly ToolTip _toolTip = new();`), and flags `_isGenerating`.

**IWizardStep props** (lines 131-141):
```csharp
public string StepTitle => "Generate Corpus";
public string NextButtonText => "Next >";
public bool CanMoveNext => !_isGenerating && !_isResetting;
public bool CanMoveBack => !_isGenerating && !_isResetting;
public bool IsGenerating => _isGenerating;
public event EventHandler? StateChanged;
public StepGenerateCorpus() { InitializeUI(); WireRunHandlers(); }
private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
```

**InitializeUI root + row-index comments + wire-after-build** (lines 155-219):
```csharp
this.Padding = new Padding(0);
this.BackColor = Theme.Background;
var gap = LogicalToDeviceUnits(Theme.Gap);
var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
_buttonHeight = LogicalToDeviceUnits(34);
_cornerInset = LogicalToDeviceUnits(8);
_root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0), BackColor = Theme.Background };
_root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
_root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header
_root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: Notice line
...
_root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 4: Body
...
// Wire handlers only after every control exists (handlers read other controls)
```

**Header (title/subtitle + right button flow)** (lines 221-289). Copy it verbatim and swap in the `Open Discovery Folder` (Default) button.

**Notice line** (lines 291-305): `AutoSize = true`, `SmallFont`, `TextMuted`, bottom margin `gap`, wrapped by a `SizeNotice()` on `_root.SizeChanged`.

**Cards** (lines 525-589, 592-641): `NewFullBleedCard()`, `NewCardLayout()` and `NewCardHeader(inset, caption, hint)` (caption `LabelFont`, hint `SmallFont` right-aligned `AutoEllipsis`, min height 28). Grid helpers: `NewGrid()` (643-659, `GridStyle.Apply`, vertical scroll, no column resize, cell tooltips), `AddTextColumn` / `AddFillColumn` / `AddRightColumn` (661-701, `NotSortable`, right-aligned header padding), and `GridStyle.AddDotColumn(grid, this)` (line 602).

**Status / progress / Cancel** (lines 703-744):
```csharp
_btnCancel = ButtonHelper.CreateButton("Cancel Generation", 0, _buttonHeight, ButtonStyle.Danger);
_lblStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.BodyFont(), ForeColor = Theme.TextMuted };
_progress = new SlimProgressBar { Width = LogicalToDeviceUnits(220), Height = LogicalToDeviceUnits(6), Anchor = AnchorStyles.Left | AnchorStyles.Right, Visible = false };
```
UI-SPEC places these in Card 3's footer rather than in a root strip. Keep the control construction and move the parent.

**Checkbox style** (from `Forms/RegenerateTabDialog.cs` lines 65-75): `AutoSize = true, FlatStyle = FlatStyle.System, Font = Theme.BodyFont(), ForeColor = Theme.Text, UseMnemonic = false`.

**Text fields:** `TextFieldHost.Create(this, textBox, lines: 1)` (RegenerateTabDialog.cs:55). Invalid state: `TextFieldHost.SetInvalid(host, true)` (UI-SPEC). Combo host: `GuideTabPanel.HostCombo` (UI-SPEC Design System row).

**Render seam:** `GenerateCorpusRenderState` enum + `LoadForRender` (lines 10-27) and the internal accessors at lines 1380-1398 (`internal CardPanel WavesCard => _wavesCard;`, `internal SlimProgressBar ProgressBar => _progress;`). Add `DiscoveryRenderState` and the matching accessors.

---

### `UserControls/StepDiscoveryDocuments.Run.cs` (lifecycle + run)

**Analog:** `UserControls/StepGenerateCorpus.Run.cs`

**Test seams** (lines 41-55):
```csharp
internal Func<string, string, MessageBoxIcon, bool> Confirm = null!;
internal Func<ILlmGateway>? GatewayFactory;
```
**Bind/enter** (lines 109-129): `BindState` stores `_state`. `OnEnterStepAsync` returns early while `_isGenerating`, and resets view state when `Draft.CaseId != _boundCaseId`.

**Next gate** (line 247): `public Task<bool> ValidateStepAsync() => Task.FromResult(!_isGenerating);`. Discovery also saves the selections first and never blocks.

**Save** (lines 252-273): `_ = SaveAsync();`, which calls `_state.CaseStore.SaveAsync(Draft)`. On failure: `SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger)`.

**Confirm wiring** (lines 277-281):
```csharp
Confirm = (title, text, icon) => MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, icon,
    MessageBoxDefaultButton.Button2) == DialogResult.Yes;
```

**Run skeleton** (lines 405-470): `_ = Handle;`, a new `CancellationTokenSource`, `_isGenerating = true`, chrome on, `RaiseStateChanged()`, `await Task.Run(() => runner.RunAsync(..., token))`, then catch `OperationCanceledException` first, then `Exception` (UI boundary, `SetStatus(..., Theme.Danger)`). The `finally` block disposes the CTS, guards `if (!IsDisposed)`, resets `_isGenerating` and chrome, and calls `RaiseStateChanged()`.

**Cancel-and-wait for close:** `StepCaseSummary.CancelAndWaitForReview` (StepCaseSummary.cs:2097-2111), which uses a `Stopwatch` loop with `Application.DoEvents()` / `Thread.Sleep(15)`. Name it `CancelAndWaitForRun(TimeSpan)` to match the other steps.

**Leave-during-run confirm:** `StepCaseSummary.ConfirmLeave` (lines 2081-2092).

**Open folder / open file / file-lock** (StepCaseSummary.cs):
- `OpenFile` / `OpenFolder` test seams (lines 1312-1323)
- `OpenMailboxesFolder` (lines 1328-1345)
- `OpenExport` with `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })` and the `catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)` message `$"Could not open {fileName}: {ex.Message.Trim().TrimEnd('.')}."` (lines 1230-1253)
- `IsFileOpenElsewhere` (lines 1201-1228; sharing violation `0x80070020`, lock violation `0x80070021`, plus a probe `FileStream`). UI-SPEC names `FileLockDetector`. Check whether it already exists as a helper; if not, promote this method.

---

### `UserControls/DiscoveryUiText.cs` (pure copy)

**Analog:** `UserControls/QaUiText.cs` (lines 1-80, 330-350) and `GenerationUiText.cs`.
```csharp
using System.Globalization;
namespace ReelDiscovery.UserControls;
/// All QA copy (07-UI-SPEC ...), pure and unit-tested like GenerationUiText. Strings may carry
/// model-written subjects ...; labels showing them set UseMnemonic = false (T-07-58).
public static class QaUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public const string ...Line2 = "...";
    public static (string Title, string Text) CQ3LeaveDuringReview() =>
        ("Stop Blind Review?", "The blind review is still running. Leave and stop it? Reviewed emails are kept.");
```
- Plural: `CaseGuideUiText.Plural(int n, string singular, string plural)` (CaseGuideUiText.cs:126) gives `"{n:N0} {word}"`. Wrap it as `GenerationUiText` does at line 1129.
- Summary-row return type: `SummaryRowContent` (GenerationUiText.cs:160-173) with `Line2`, `Line3`, `Line3Tone`, `DotTone`, `Line3Tooltip`, `ShowPrimary`, `ShowSecondary`. `SummaryTone` = Muted/Success/Warning/Danger.
- Confirmations: `CD1CancelGeneration()`, `CD2CloseDuringRun()` returning `(string Title, string Text)`.
- Tests analog: `ReelDiscovery.Tests/Qa/QaUiTextTests.cs`.

---

### `Forms/RegenerateDiscoveryDialog.cs`

**Analog:** `Forms/RegenerateTabDialog.cs` (whole file, 87 lines). Copy the structure:
```csharp
public class RegenerateTabDialog : ThemedDialog
{
    private TextBox _txtGuidance = null!;
    private Button _btnCancel = null!;
    private Button _btnRegenerate = null!;
    public RegenerateTabDialog(GuidePart part, int editedCount) : base(CaseGuideUiText.RegenerateTabTitle(...)) { ...; InitializeUI(); }
    public string Guidance => _txtGuidance.Text.Trim();
    private void InitializeUI()
    {
        AddHeadline(...); AddBody(...);
        var caption = CreateWrappingLabel("Guidance (optional)", Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);
        _txtGuidance = new TextBox { PlaceholderText = ... };
        var guidanceHost = TextFieldHost.Create(this, _txtGuidance, lines: 3);
        _txtGuidance.ScrollBars = ScrollBars.None;
        guidanceHost.Dock = DockStyle.Fill; guidanceHost.Margin = Padding.Empty;
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
Add the settings line, the file-open warning (`CreateWrappingLabel(..., Theme.SmallFont(), Theme.Warning)`, top `LDU(12)`) and the estimate line between the guidance field and the buttons. Truncate the guidance to 1,000 characters in code.

---

### `Forms/WizardForm.cs` (mod)

- **StepTypes** (lines 301-305): insert `typeof(StepDiscoveryDocuments)` between `StepGenerateCorpus` and `StepCaseSummary`. `StepTypeForStage` (310-319) is unchanged (D-09).
- **NavigateToStep(Type)**: model it on `NavigateToStage` (lines 324-331). The `NavigateToStep(int)` overload already exists (461-467). Add:
```csharp
// Map by step type, never by index: Phase 4 inserts steps before the summary
var index = _steps.FindIndex(s => s.GetType() == type);
if (index > 0) ShowStep(index);
```
- **Leave check** (lines 362-365): currently `_steps[_currentStepIndex] is StepCaseSummary summary && !summary.ConfirmLeave()`. Research recommends generalizing this, for example with a small internal interface `IConfirmLeave` implemented by both steps.
- **FlushCurrentCaseStep** (lines 509-515): add `StepDiscoveryDocuments` to the `is (... or ...)` list.
- **IsRunActive** (lines 528-530): add `or StepDiscoveryDocuments { IsGenerating: true }`.
- **StopActiveRun** (lines 540-551): add an `else if (step is StepDiscoveryDocuments discovery) WaitWhileStopping(() => discovery.CancelAndWaitForRun(TimeSpan.FromSeconds(10)));` branch.
- **OnFormClosing switch** (lines 588-595): add `StepDiscoveryDocuments => DiscoveryUiText.CD2CloseDuringRun(),`.

---

### `UserControls/StepCaseSummary.cs` (mod)

- **Row creation** (lines 417-432): `_rowDiscovery = CreateRow("Discovery documents", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open Discovery Folder", "Discovery Documents" });`
- **Group order** (lines 453-474): insert `(null, "Discovery"), (_rowDiscovery, "")` between the Quality checks rows and `(null, "Case guide")`. Update the comment on line 453.
- **Handlers** (lines 527-556, after every control exists): `_btnDiscoveryDocs.Click += (s, e) => (FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments));`. This mirrors line 533 `NavigateToStage(CaseStage.CorpusSettings)`. Open folder mirrors `OpenMailboxesFolder` (1328-1345).
- **Cost row without a plan** (Pitfall 9): `LoadQa` (1608-1613) returns before computing `qa.Cost` when `plan == null`. Move the `CostRow(...)` computation so it adds sidecar discovery spend even with no plan.

---

### `UserControls/GenerationUiText.cs` (mod)

`CostRow` (lines 1082-1105) and `CostItems` (lines 1108-1123). Add a `decimal discovery` parameter and insert `("discovery documents", "Discovery documents", "", discovery)` after the `images and voicemail` tuple and before `not itemized` (line 1119/1120). `anySpend`/`total` must include discovery spend. Update every caller (StepCaseSummary.cs:1541, 1620) and `GenerationUiTextTests`.

---

### Tests

**DTO strictness:** `Tests/Case/CaseSchemaTests.cs` lines 12-36. Add:
```csharp
foreach (var type in DiscoveryGenerator.SchemaTypes)
    data.Add(nameof(DiscoveryGenerator), type);
```

**Step order:** `Tests/Case/WizardFlowTests.cs` lines 22-24 (`CaseSteps` array) and 97-102 (`Assert.Equal(8, types.Count)`, `IndexOf(typeof(StepCaseSummary)) == 7`). Change these to 9 steps, Discovery = 7, Summary = 8.

**.docx validation:** `Tests/Case/CaseGuideDocxExporterTests.cs` lines 11-27 (`OpenAndValidate` + `ParagraphText`). Copy them, or promote them to a shared test helper. Assertion style is on line 37: `Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));`.

**Generator with fake provider:** `Tests/Qa/BlindReviewTests.cs`
- usings (lines 1-14) include `ReelDiscovery.Tests.Fakes`, `ReelDiscovery.Tests.Evidence` and `ReelDiscovery.Tests.Corpus`
- in-test plan fixture builder (lines 23-72)
- `CaseTestData.CreateGateway()` returns `(LlmGateway Gateway, FakeLlmProvider Fake)` (CaseTestData.cs:30)
- the fake queues responses through `Fake.Responses.Enqueue(FakeLlmProvider.Ok(json))`, with `Truncated()`, `Refused(msg)` and `Failed(kind, msg)` (FakeLlmProvider.cs:23, 97-106); requests are inspected through `Fake.Requests`

**Fixtures:** `CaseTestData.SampleCounselDraft()` (line 314), `SampleGuidedDraft()` (204), `SampleMemoDraft()` (410), `SampleComplaintDraft()` (52).

**Render:** `Tests/Render/GenerateCorpusRenderTests.cs` lines 1-67 (`[Trait("Category","Render")]`, `[Collection(RenderCollection.Name)]`, `RenderBoth(name, state, view, (step, labels) => {...}, primary: 1)` asserting `ThemedButton.Variant` and colors). Dialog render: `Tests/Render/DialogRenderTests.cs` lines 32-50 (`RenderHarness.RunSta`, `RenderHarness.RenderForm(() => new RegenerateTabDialog(...), name, form => {...})`, `AssertRendered(result, height, workingHeight)`).

**Serialization:** `Tests/Case/CaseDraftSerializationTests.cs` lines 9-14 (version) and 17+ (round trip).

---

## Shared Patterns

### Never-throws unit of work + fatal stop (D-05)
**Source:** `Services/Corpus/PlanCallRunner.cs` lines 90-171; consumer `Services/Qa/BlindReviewer.cs` lines 281-356.
**Apply to:** `DiscoveryGenerator`.
The catch order is user cancel (rethrow), then the fatal token (empty outcome), then timeout, then `LlmCallException` fatal (`run.SetFatal`), then `LlmCallException`, then `Exception`. Persist paid work with `CancellationToken.None`, and `await Task.WhenAll` before cancel propagates.

### Atomic writes
**Source:** `Services/Case/CaseStore.cs:349` `public static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct = default)`.
**Apply to:** the `.docx` in `Discovery\` and `discovery-documents.json`. Write the .docx first, then the sidecar (CorpusPlanStore's "plan file before case.json" ordering rule, lines 26-31).

### Untrusted-file loading
**Source:** `CorpusPlanStore.LoadFile` / `PeekFormatVersion` (lines 107-181); `CaseDraft.Normalize` (lines 102-148); `CorpusSettings.Normalize` (lines 213-238).
**Apply to:** `DiscoveryStore`, `DiscoverySettings`, `DiscoveryDocumentSet`.

### Path safety (ASVS V12)
**Source:**
- `StorySoFarDocxBuilder.IsStoryFileName` (lines 29-38)
- `StepCaseSummary.OpenCaseFolder` (lines 1287-1305): `Process.Start("explorer.exe", "\"" + Path.GetFullPath(folder) + "\"")`, with the comment `// Never shell-execute a path read from case.json (ASVS V12)`
- `OpenMailboxesFolder` (lines 1328-1345)

**Apply to:** Open in Word, Open Discovery Folder (step and Summary), and names read back from the sidecar.

### Determinism
**Source:** `RenderAssignments.StableRank` (RenderAssignments.cs:37-41).
**Apply to:** case number, deposition dates, reporter and videographer picks, Partner/Associate choice, and ranking tie-breaks. Never use `Random`, `Guid.NewGuid()` or `GetHashCode()`.

### Strict-schema DTOs
**Source:** `BlindReviewPrompts.BlindReviewDto` (lines 232-245); registration in `CaseSchemaTests` (lines 12-36).
**Apply to:** every `DiscoveryPrompts` DTO. They must be flat, with 16 or fewer union types and no nullables.

### Prompt name rules (D-02)
**Source:** `CaseGuidePrompts.SystemPrompt` (line 13) and `AppendNameRules(sb, draft, includeScenario: false)` (lines 26-49).
**Apply to:** `DiscoveryPrompts`. Do not include the raw complaint or scenario text, and never instruct "fictional" (except the substitution line noted above).

### UI construction and DPI
**Source:** `StepGenerateCorpus.InitializeUI` (lines 155-219) for `LogicalToDeviceUnits`, row-index comments and handlers wired last; `CardPanel` helpers (525-589); `ButtonHelper.CreateButton(text, 0, _buttonHeight, ButtonStyle.X)`.
**Apply to:** `StepDiscoveryDocuments`, the Case Summary row and `RegenerateDiscoveryDialog` (which uses `LDU(n)`).

### UI error boundary
**Source:** `StepGenerateCorpus.Run.cs` lines 448-470 (catch `OperationCanceledException`, then `Exception`; `SetStatus(..., Theme.Danger)`; `finally` resets flags and calls `RaiseStateChanged()`), and `StepCaseSummary.ExportOneAsync` lines 1160-1199 (`catch (Exception ex) when (IsFileOpenElsewhere(ex, fileName))` before the generic catch).
**Apply to:** Generate, Regenerate, Save Again and Open in Word.

### Pure copy helper
**Source:** `QaUiText` / `GenerationUiText` (`SummaryRowContent`, `(string Title, string Text)` confirmations, `CaseGuideUiText.Plural`).
**Apply to:** `DiscoveryUiText`, with `DiscoveryUiTextTests` modeled on `QaUiTextTests`.

---

## No Analog Found

| File | Role | Data Flow | Reason / What to use instead |
|------|------|-----------|------------------------------|
| `Services/Discovery/TranscriptLayout.cs` | utility (pure layout engine) | transform | Nothing in the repo wraps text to fixed columns or paginates. Use the RESEARCH Pattern 4 rules and the "Layout engine core" example (RESEARCH lines 821-837): `LinesPerPage = 25`, `MaxCharsPerLine = 62`, Q/A col 5, colloquy col 10, parenthetical col 5, greedy wrap, pad the last page. Normalize whitespace with the promoted `TextScan.Whitespace` regex and strip characters with `CaseGuideDocxExporter.StripInvalidXmlChars` |
| `Services/Discovery/CaseNumber.cs` | utility | transform | No case-number derivation exists (the core document keeps `____________`, CoreDocumentAssembler.cs:140). Use RESEARCH Pattern 2 formats with `RenderAssignments.StableRank(CaseId.ToString("N"))`, and persist the result in the sidecar |

Partial notes:
- `DiscoveryBoilerplate.cs`: no definitions or instructions boilerplate exists. Use `CoreDocumentAssembler.BuildSignatureBlock` (lines 178-200) for the `StringBuilder` + `AppendLine` text-template style. Use `CourtSystem.State` vs `Federal` (CoreDocumentAssembler.cs:127) to pick rule citations.
- `FileLockDetector` is named in the UI-SPEC, but only `StepCaseSummary.IsFileOpenElsewhere` (private, lines 1201-1228) was found. The planner should verify whether a `FileLockDetector` helper exists; if not, promote that method into `Helpers/`.

## Metadata

**Analog search scope:** `Services/{Ai,Case,Corpus,Evidence,Qa}`, `Models/{Case,Corpus,Qa}`, `UserControls/`, `Forms/`, `Helpers/`, `ReelDiscovery.Tests/{Case,Qa,Render,Fakes}`
**Files scanned:** about 40 (26 read in part or in full)
**Pattern extraction date:** 2026-10-08
