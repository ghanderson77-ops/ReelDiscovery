# Phase 5: Relevant Evidence Generation & Narrative Ledger - Pattern Map

**Mapped:** 2026-10-03
**Files analyzed:** 52 (37 new, 15 modified)
**Analogs found:** 49 / 52. Three files have only partial analogs: `WavePlanner`, `GenerationJournal`, `GenerationSnapshot`. For those, use RESEARCH.md Patterns 1, 7 and 8.

All paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery`. Line numbers were taken on 2026-10-03 at commit `dcbc8e6`.

Namespaces: `ReelDiscovery.Models.Evidence`, `ReelDiscovery.Services.Evidence`, `ReelDiscovery.Tests.Evidence`. Do not reuse `ReelDiscovery.Tests.Generation`, which holds the legacy `EmailGenerator` tests (RESEARCH).

---

## File Classification

### Models (new `Models/Evidence/`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Models/Evidence/LedgerEntry.cs` (+ `LedgerKind` enum) | model | — | `Models/Corpus/PlannedMessage.cs` (enums + class, id comments) | exact |
| `Models/Evidence/NarrativeLedger.cs` (entries + `Normalize`) | model (persisted in case.json) | CRUD | `Models/Corpus/CorpusState.cs` (`Normalize`, `CleanList`) | exact |
| `Models/Evidence/GenerationState.cs` (`CorpusState.Generation`: RunPlanId, Ledger, story file stamp, last outcome) | model | CRUD (autosave) | `Models/Corpus/CorpusState.cs` + `WrittenCorpusFile` | exact |
| `Models/Evidence/JournalRecord.cs` (+ `JournalRecordType`, `ThreadWriteStatus`, `FailureKind` enums) | model (JSONL line) | file-I/O | `Models/Corpus/PlannedMessage.cs` + `CorpusPlan.Normalize` | role-match |
| `Models/Evidence/RenderAssignment.cs` (+ `Subtlety`, `LengthBand`, `PrivilegeMarking`) | model (computed, not persisted) | transform | `Models/Corpus/PlannedMessage.cs` (enum + class) | role-match |
| `Models/Evidence/GenerationWave.cs` | model | transform | `Models/Corpus/PlannedThread.cs` | role-match |
| `Models/Case/CaseStage.cs` (modify: insert `Generation` before `Summary`) | model enum | — | itself (lines 7-15) | exact |
| `Models/Case/CaseDraft.cs` (modify: `CurrentFormatVersion = 3`) | model | CRUD | itself (line 31, lines 141-144) | exact |
| `Models/Corpus/CorpusState.cs` (modify: `Generation` property, `CorpusFileKind.StorySoFar`, Normalize) | model | CRUD | itself (lines 6-11, 60-71) | exact |

### Services: pure core (new `Services/Evidence/`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Services/Evidence/WavePlanner.cs` | utility (pure) | transform | `RelevantThreadFiller.BuildChunks` (lines 422-449) | partial |
| `Services/Evidence/RenderAssignments.cs` | utility (pure, stable hash) | transform | `Helpers/Apportion.cs` + `CaseDraft.ComputeHash` (SHA-256) | role-match |
| `Services/Evidence/KnowledgeGate.cs` | utility (pure) | transform | `RelevantThreadFiller.BuildChunkPrompt` fact window (lines 489-524) + `SkeletonContext.FactDate` | role-match |
| `Services/Evidence/LedgerSelector.cs` | utility (pure filter/rank/cap) | transform | `RelevantThreadFiller.BuildChunkPrompt` earlier facts (`TakeLast(EarlierFactsContext)`) | role-match |
| `Services/Evidence/LedgerMerger.cs` | utility (pure) | transform | `RelevantThreadFiller.ApplyBatch` (lines 208-270: map by seq, drop unknown) + `PlanNumbering` id formatting | role-match |
| `Services/Evidence/EvidencePrompts.cs` | utility (const prompt + builders) | transform | `Services/Corpus/CorpusPlanPrompts.cs` + `RelevantThreadFiller.BuildChunkPrompt` | exact |
| `Services/Evidence/ThreadRenderer.cs` | service (gateway call, nested DTOs, `SchemaTypes`) | request-response | `Services/Corpus/RelevantThreadFiller.cs` | exact |
| `Services/Evidence/RenderRepair.cs` | utility (pure post-processing) | transform | `RelevantThreadFiller.Clean` / `StripReplyPrefix` / `IdTokenPattern` (lines 368-416) | exact |
| `Services/Evidence/RenderValidator.cs` (+ `RenderViolation`) | service (static, never throws) | transform | `Services/Corpus/CorpusPlanValidator.cs` (Run-per-rule isolation) + `CanonicalValueScanner` | exact |
| `Services/Evidence/EmailComposer.cs` (+ `ComposedEmail`) | utility (pure) | transform | `AnswerKeyBuilder.SubjectOf` + `Lookups.Display` (lines 157-166, 215-272), `ThreadingHelper` | role-match |
| `Services/Evidence/OrganizationThemes.cs` | utility (pure) | transform | `Services/ThemeGenerator.cs` (`SanitizeHexColor`, `AllowedFonts`, lines 140-226) + `Models/PresentationTheme.cs` | role-match |
| `Services/Evidence/StorySoFarDocxBuilder.cs` | utility (pure bytes) | transform | `Services/Corpus/ReviewProtocolBuilder.Build` (lines 151-218) | exact |

### Services: I/O shell (new `Services/Evidence/`)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Services/Evidence/MailboxWriter.cs` | service | file-I/O | `Services/EmlFileService.SaveEmailAsEmlAsync` (MimeKit shape, lines 9-113) + `CaseStore.WriteAtomicAsync` + `CorpusPlan.IsSafeEmlPath` | role-match |
| `Services/Evidence/GenerationJournal.cs` | service (append + replay) | file-I/O (append-only) | `Services/Corpus/CorpusPlanStore.cs` (size cap, never-throw load, `Normalize`) | partial |
| `Services/Evidence/GenerationRunState.cs` (+ answer-key overlay) | utility (pure projection) | transform | `Services/Corpus/AnswerKeyBuilder.cs` | role-match |
| `Services/Evidence/ThreadUnitOfWork.cs` | service | request-response + file-I/O | `RelevantThreadFiller.FillAsync` first-pass/re-ask loop (lines 80-141) | role-match |
| `Services/Evidence/GenerationRunner.cs` | service (orchestrator) | batch (waves, bounded parallel) | `RelevantThreadFiller.FillAsync` + `PlanCallRun` | role-match |
| `Services/Evidence/GenerationSnapshot.cs` | utility (lock + counters + bounded queue) | event-driven | `Services/Corpus/PlanCallRun` (`lock (_lock)` fields, lines 8-61), `Models/TokenUsageTracker` | partial |

### Services/Helpers modified

| Modified File | Change | Analog | Match |
|---|---|---|---|
| `Services/Corpus/PlanNumbering.cs` | expose `BuildFolders` as `internal static IReadOnlyDictionary<string,string> CustodianFolders(CaseGuide)` | itself (lines 164-182) | exact |
| `Services/Corpus/AnswerKeyBuilder.cs` | status + subject overlay hook | itself (lines 27-85, 157-166) | exact |
| `Services/Corpus/AnswerKeyXlsxWriter.cs` | Legend Status text (line 64) | itself | exact |
| `Services/Ai/LlmResult.cs` + `LlmGateway.cs` | `InputTokens` / `OutputTokens` summed across attempts | `LlmGateway.GenerateAsync` lines 84-88, `BuildResult` lines 146-168 | exact |
| `Services/Corpus/PlanCallRunner.cs` | `PlanCallOutcome` carries token totals (success and failure) | itself (lines 63-137) | exact |
| `Helpers/HtmlEmailFormatter.cs` | add structured `ComposeHtml(...)`; make `WrapInTemplate` `internal` | itself (lines 167-197, 416-452, 604-611) | exact |
| `Services/ThemeGenerator.cs` | make `SanitizeHexColor` / `AllowedFonts` / `SanitizeFont` `internal static` | itself (lines 140-226) | exact |

### UI

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `UserControls/StepGenerateCorpus.cs` | component (wizard step layout) | event-driven | `UserControls/StepReviewPlan.cs` | exact |
| `UserControls/StepGenerateCorpus.Run.cs` | component (run/cancel/lifecycle) | streaming progress | `UserControls/StepReviewPlan.Run.cs` | exact |
| `UserControls/GenerationUiText.cs` | utility (pure copy) | transform | `UserControls/CorpusUiText.cs` | exact |
| `UserControls/StepCaseSummary.cs` (modify: Mailboxes + Story so far rows) | component | event-driven | itself (`CreateRow` lines 396-424, `OpenCorpusFile` 1081-1107, `OpenCaseFolder` 1109-1127) | exact |
| `Forms/ReplanDialog.cs` / `CorpusUiText.ReplanBody` (modify: extra sentence) | component / copy | request-response | itself (`ReplanDialog.cs` line 32; `CorpusUiText.cs` lines 301-307) | exact |
| `Forms/WizardForm.cs` (modify) | component (shell) | event-driven | itself (lines 282-306, 526, 538-562, 595-597) | exact |
| `UserControls/CaseGuideUiText.cs` (modify `StageLabel`) | utility | transform | itself | exact |

### Tests (`ReelDiscovery.Tests/Evidence/`)

| New/Modified File | Role | Closest Analog | Match |
|---|---|---|---|
| `Evidence/EvidenceTestData.cs` | fixture | `Corpus/PlanFixtures.cs` + `Corpus/CorpusTestData.cs` | exact |
| `Evidence/WavePlannerTests`, `RenderAssignmentsTests`, `KnowledgeGateTests`, `LedgerSelectorTests`, `LedgerMergerTests`, `RenderValidatorTests`, `EmailComposerTests`, `GenerationUiTextTests`, `AnswerKeyOverlayTests`, `GenerationStateTests` | pure unit | `Corpus/AnswerKeyBuilderTests.cs`, `Corpus/CorpusUiTextTests.cs` | exact |
| `Evidence/ThreadUnitOfWorkTests`, `GenerationRunnerTests` | integration (FakeLlmProvider) | `Corpus/RelevantThreadFillerTests.cs`, `Corpus/PlanCallRunnerTests.cs` | exact |
| `Evidence/MailboxWriterTests`, `GenerationJournalTests` | temp folder | `Corpus/CorpusPlanStoreTests.cs` (lines 9-25) | exact |
| `Evidence/StorySoFarDocxTests` | OpenXmlValidator | `Corpus/ReviewProtocolTests.cs` | exact |
| `Evidence/GenerationSnapshotTests` | concurrency | `ReelDiscovery.Tests/Concurrency/*` | role-match |
| `Render/GenerateCorpusRenderTests.cs` | render 144 DPI | `Render/CorpusReviewPlanRenderTests.cs` | exact |
| `Case/CaseSchemaTests.cs`, `Case/WizardFlowTests.cs` (extend) | test | themselves | exact |

---

## Pattern Assignments

### `Models/Evidence/*.cs` (models)

**Analogs:** `Models/Corpus/PlannedMessage.cs`, `Models/Corpus/CorpusState.cs`, `Models/Corpus/CorpusPlan.cs`

**File shape** (`PlannedMessage.cs` lines 1-67): enums first, then one mutable class, `string.Empty` / `new()` defaults, and short `//` comments that give each id's shape:
```csharp
namespace ReelDiscovery.Models.Corpus;

public enum MessageKind
{
    New,
    Reply,
    ReplyAll,
    Forward
}

/// <summary>
/// Why a message is relevant: Own = carries fact evidence; Quoted = inherits via quoted or forwarded text.
/// </summary>
public enum RelevanceBasis { None, Own, Quoted }

public class PlannedMessage
{
    // "T-0042-M03" (thread-relative, opaque)
    public string Id { get; set; } = string.Empty;

    // "DM-000317", assigned chronologically after scheduling
    public string DocId { get; set; } = string.Empty;
    ...
    // Sender-local offset; System.Text.Json writes ISO 8601 with offset
    public DateTimeOffset SentAt { get; set; }
```
`LedgerEntry` follows this with `// "T-0042-L03" (code-owned, never from the model)` on `Id`, `// yyyy-MM-dd from the source message (sender-local)` on `Date`, plus `Kind`, `About`, `Statement`, `Value`, `KnownByIds`, `FactIds`, `IssueIds`, `SourceDocId`, `SourceThreadId`, and `WaveIndex` (for LedgerSelector's earlier-waves filter). Do not use records or primary constructors.

**Normalize for untrusted JSON** (`CorpusState.cs` lines 57-71, `CorpusPlan.cs` lines 61-96, 123-160). `NarrativeLedger.Normalize()`, `GenerationState.Normalize()` and the journal replay must all follow this pattern:
```csharp
/// <summary>
/// Repairs state loaded from untrusted case.json; never throws.
/// </summary>
public void Normalize()
{
    Settings ??= new();
    Settings.Normalize();
    PlanGuideHash ??= string.Empty;
    ...
    WrittenFiles = CaseGuide.CleanList(WrittenFiles);
    foreach (var file in WrittenFiles)
        file.FileName ??= string.Empty;
}
```
Journal file paths use the WR-10 guard from `CorpusPlan.NormalizeThread` line 149:
```csharp
// Only "Mailboxes/{folder}/{box}/{name}.eml" survives, so no loaded path can leave the output folder (WR-10)
message.FilePath = IsSafeEmlPath(message.FilePath) ? message.FilePath : string.Empty;
```

**Computed / non-serialized members** (`CorpusState.cs` lines 50-55):
```csharp
// Loaded from corpus-plan.json by CorpusPlanStore; never serialized into case.json
[JsonIgnore]
public CorpusPlan? Plan { get; set; }

[JsonIgnore]
public bool HasPlan => PlanId != Guid.Empty;
```

**Stamped written-file pattern for the story-so-far docx** (`CorpusState.cs` lines 6-22). Add `StorySoFar` to the existing enum and reuse `WrittenCorpusFile`:
```csharp
public enum CorpusFileKind
{
    AnswerKeyCsv,
    AnswerKeyXlsx,
    ReviewProtocol
    // + StorySoFar
}

public class WrittenCorpusFile
{
    public CorpusFileKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public DateTime WrittenUtc { get; set; }
}
```

### `Models/Corpus/CorpusState.cs` (modify)
Add `public GenerationState? Generation { get; set; }` with a doc comment like the one on `Plan`. Then add `Generation?.Normalize();` to `Normalize()` after `WrittenFiles` (line 68). Do **not** `[JsonIgnore]` it: the ledger is meant to live in case.json (RESEARCH A8).

### `Models/Case/CaseStage.cs` (modify)
Insert `Generation` between `ReviewPlan` and `Summary` (lines 13-14). The doc comment (lines 3-6) already states that values are stored by name, so this is safe. `CaseDraft.AdvanceStage` (lines 93-97) compares ordinals.

### `Models/Case/CaseDraft.cs` (modify)
Line 31: `CurrentFormatVersion = 2` → `3` (RESEARCH A11). The upgrade-on-load comment at lines 141-144 already covers the bump. Nothing else changes here.

---

### `Services/Evidence/ThreadRenderer.cs` (service, strict DTO call)

**Analog:** `Services/Corpus/RelevantThreadFiller.cs`

**Imports** (lines 1-13):
```csharp
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;
```
(Change to `namespace ReelDiscovery.Services.Evidence;` and add `using ReelDiscovery.Models.Evidence; using ReelDiscovery.Services.Corpus;`.)

**Constants, ctor, SchemaTypes** (lines 20-42):
```csharp
public partial class RelevantThreadFiller
{
    public const int ChunkMaxOutputTokens = 16_000;
    public const int MaxParallelCalls = 3;
    private readonly ILlmGateway _llm;
    private readonly int _maxParallel;

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(ThreadPlanBatchDto) };

    public RelevantThreadFiller(ILlmGateway llm, int maxParallel = MaxParallelCalls)
    {
        _llm = llm;
        _maxParallel = Math.Max(1, maxParallel);
    }
```
ThreadRenderer: `MaxOutputTokens = 12_000` (RESEARCH Pattern 4) and `SchemaTypes = { typeof(ThreadRenderDto) }`.

**Request builder** (lines 171-178). Copy the shape exactly. OperationName labels the cost ticker:
```csharp
private static LlmRequest ChunkRequest(string prompt, int index, int total, bool retry) => new()
{
    Tier = LlmTier.Story,
    OperationName = $"Corpus Plan - Relevant threads (chunk {index + 1} of {total})" + (retry ? " retry" : string.Empty),
    SystemPrompt = CorpusPlanPrompts.SystemPrompt,
    UserPrompt = prompt,
    MaxOutputTokens = ChunkMaxOutputTokens
};
```
→ `OperationName = $"Generate Corpus - {thread.Id}" + (attempt > 1 ? $" attempt {attempt}" : "")`, `SystemPrompt = EvidencePrompts.SystemPrompt`, Story tier (D-19).

**Strict DTO style** (lines 590-643). Use nested `internal class`, put one attribute per line, use no nullables, and give every property `[JsonPropertyName]` + `[Description]`:
```csharp
// ----- LLM chunk DTOs (strict-schema safe: no nullable members, no dates, people, counts or labels) -----

internal class ThreadPlanBatchDto
{
    [JsonPropertyName("threads")]
    [Description("One entry per thread listed in the request, using its threadId")]
    public List<ThreadPlanDto> Threads { get; set; } = new();
}

internal class MessagePlanDto
{
    [JsonPropertyName("seq")]
    [Description("Message number from the request")]
    public int Seq { get; set; }
    ...
}
```
The bodies of `ThreadRenderDto` / `RenderedMessageDto` / `LedgerDeltaDto` are in RESEARCH Pattern 4. Give the `seq` and `kind` fields `[Description]`s too (RESEARCH omitted them).

**Map strictly by seq, drop unknowns** (lines 236-245):
```csharp
var bySeq = thread.Messages.GroupBy(m => m.Seq).ToDictionary(g => g.Key, g => g.First());
var seenSeq = new HashSet<int>();
foreach (var messageDto in dto.Messages ?? new List<MessagePlanDto>())
{
    if (messageDto == null || !bySeq.TryGetValue(messageDto.Seq, out var message) || !seenSeq.Add(messageDto.Seq))
        continue;
```
Put this mapping in `RenderRepair`. A duplicate or missing seq then surfaces as violation V1 in `RenderValidator`.

---

### `Services/Evidence/EvidencePrompts.cs` (byte-stable system prompt + builders)

**Analog:** `Services/Corpus/CorpusPlanPrompts.cs` (lines 1-64) and `RelevantThreadFiller.BuildChunkPrompt` (lines 456-567)

**Const prompt** (`CorpusPlanPrompts.cs` lines 7-14):
```csharp
/// <summary>
/// Shared system prompt and guide-context helpers for every corpus-planning call (D-01, D-02, Pitfall 8).
/// Prompts carry no format instructions: the strict schema defines the response shape.
/// </summary>
public static class CorpusPlanPrompts
{
    // Byte-stable for prompt caching: never interpolate run data into this text (D-01, D-02)
    public const string SystemPrompt = "You plan and describe the email evidence for an e-discovery training dataset built from a finished case guide. Keep each person, company and organization exactly as named in the guide, including characters and companies from films, TV series, books or games. Take a firm position ... Do not hedge. ... no legal terms such as alleged, plaintiff, defendant or liability. Use the exact names, dates and values you are given and never invent people or IDs.";
```
Reuse the keep-names sentence word for word (D-03). Never write "fictional names". Add the fictional-training-dataset framing for refusals (RESEARCH Pitfall 10), the no-signature/no-quote/no-legal-framing rules, and the allowed markup (`**bold**`, `- ` bullets, one pipe table).

**Prompt layout: stable prefix first** (`BuildChunkPrompt` lines 468-487). Reuse the internal helpers and do not reimplement them:
```csharp
var sb = new StringBuilder();
CaseGuidePrompts.AppendNameRules(sb, draft);
sb.AppendLine();
CorpusPlanPrompts.AppendCaseSynopsis(sb, draft);   // DO NOT use in render prompts: it prints issue RelevanceCriteria (Pitfall 2)
...
sb.AppendLine("People in these threads:");
CorpusPlanPrompts.AppendCast(sb, guide, participants);
```
**Warning:** `AppendCaseSynopsis` prints `RelevanceCriteria` and `AppendValues` (lines 56-64) prints **every** value (RESEARCH Pitfall 1). EvidencePrompts needs a case-title-only line plus a gated value list built from `KnowledgeGate`. `AppendCast` is safe, but extend each line with `VoiceNote` + `SignatureStyle` (D-20).

**Message line format** (lines 557-567). Copy it and add `subtlety`, `length`, `may mention: F-...`:
```csharp
// "T-0003 msg 2 | ReplyAll | from CH-002 Tom Becker to CH-004 Lisa Chen; CH-003 Raymond Harmon | cc - | 2024-03-19 09:42 America/Phoenix | carries the facts: yes | attachments: Word"
private static string MessageLine(string threadId, PlannedMessage message, IReadOnlyDictionary<string, CastMember> castById)
{
    var to = message.ToIds.Count > 0 ? string.Join("; ", message.ToIds.Select(id => Person(castById, id))) : "-";
    ...
    var when = LocalTime(message).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
```
**Sender-local time** (lines 578-585). Reuse it, or move it to a shared helper:
```csharp
private static DateTimeOffset LocalTime(PlannedMessage message)
{
    var tz = BusinessCalendar.ResolveId(message.TimeZoneId);
    return tz != null ? TimeZoneInfo.ConvertTime(message.SentAt, tz) : message.SentAt;
}
```
The retry feedback block goes at the **end** of the user prompt (after the task text), so the prefix stays cacheable (Phase 7).

---

### `Services/Evidence/KnowledgeGate.cs`, `LedgerSelector.cs`, `WavePlanner.cs` (pure)

**Analog:** `RelevantThreadFiller.BuildChunkPrompt` fact window (lines 489-508) and `BuildChunks` (lines 422-449)

**Dated-fact filtering, culture-invariant, ids break ties:**
```csharp
var dated = guide.Facts
    .Select(f => (Fact: f, Date: SkeletonContext.FactDate(f)))
    .Where(x => x.Date != null)
    .OrderBy(x => x.Date)
    .ThenBy(x => x.Fact.Id, StringComparer.Ordinal)
    .ToList();
...
var before = dated
    .Where(x => x.Date < firstInWindow && !inWindowIds.Contains(x.Fact.Id))
    .TakeLast(EarlierFactsContext)
    .ToList();
```
`SkeletonContext.FactDate` (`Services/Corpus/SkeletonContext.cs` lines 72-73) returns `DateOnly?`, with null for undated facts (RESEARCH A6: undated means not date-gated).

**Ordering threads by first message** (`BuildChunks` lines 424-427, `FirstSentUtc` lines 587-588). This is the WavePlanner seed:
```csharp
var ordered = relevant
    .OrderBy(FirstSentUtc)
    .ThenBy(t => t.Id, StringComparer.Ordinal)
    .ToList();
...
private static DateTime FirstSentUtc(PlannedThread thread) =>
    thread.Messages.Count > 0 ? thread.Messages.Min(m => m.SentAt.UtcDateTime) : DateTime.MaxValue;
```
The greedy accumulate-then-close loop in `BuildChunks` (lines 429-448) is the shape for WavePlanner's 7/14-day window. Always pass the **whole plan's** relevant threads (Pitfall 3). Keep constants `public const` for tests (`MaxThreadsPerChunk` style). Ledger cap: `public const int MaxLedgerEntries = 30;`.

---

### `Services/Evidence/RenderAssignments.cs` (pure, stable)

**Analogs:** `Helpers/Apportion.cs` (whole file) and `CaseDraft.ComputeHash` (SHA-256 idiom)

```csharp
/// <summary>
/// Splits <paramref name="total"/> into integer shares proportional to <paramref name="weights"/>, each at least
/// <paramref name="min"/>. ... Ties go to the lower index.
/// </summary>
public static List<int> Largest(int total, IReadOnlyList<double> weights, int min = 0)
```
Call it as `Apportion.Largest(ownCount, new[] { 0.60, 0.30, 0.10 })` (subtlety), `new[] { 0.55, 0.35, 0.10 }` (length), and `new[] { 0.85, 0.15 }` (privileged labeling). Rank with `SHA256.HashData(Encoding.UTF8.GetBytes(id))` + `BinaryPrimitives.ReadUInt64BigEndian` (RESEARCH Pattern 2). **Never** use `string.GetHashCode()` or `Random`. Make the length band table `public static readonly` so Phase 6 can reuse it (NOISE-05).

---

### `Services/Evidence/LedgerMerger.cs` (pure)

**Analogs:** `RelevantThreadFiller.ApplyBatch` (lines 208-270), `PlanNumbering.Assign` id formatting (lines 49, 67, 90)

**Id formatting, invariant culture, code-owned:**
```csharp
message.Id = string.Format(CultureInfo.InvariantCulture, "{0}-M{1:00}", thread.Id, message.Seq);
```
→ `string.Format(CultureInfo.InvariantCulture, "{0}-L{1:00}", thread.Id, n)`.

**Strip unknown id tokens from model text** (lines 353-376, 409-410). Reuse it for `About`/`Statement`/`Value`:
```csharp
private static string Clean(string? text, HashSet<string> knownTokens)
{
    if (string.IsNullOrWhiteSpace(text))
        return string.Empty;
    var stripped = IdTokenPattern().Replace(text, m => knownTokens.Contains(m.Value) ? m.Value : string.Empty);
    return WhitespacePattern().Replace(stripped, " ").Trim();
}

[GeneratedRegex(@"\b(?:CH|F|ISS|T|V|ORG)-\d{2,4}\b")]
private static partial Regex IdTokenPattern();
```
Keep model `FactIds` only when they are in the thread's gated fact set (normalize with `RelevantThreadFiller.CanonicalId`, line 368). Derive `IssueIds` from the facts the way `AnswerKeyBuilder.IssuesOf` does (lines 189-199):
```csharp
var issues = factIds
    .Select(lookups.Fact)
    .Where(f => f != null)
    .SelectMany(f => f!.IssueIds)
    .Where(id => !string.IsNullOrWhiteSpace(id))
    .Distinct(StringComparer.Ordinal)
    .ToList();
```

---

### `Services/Evidence/RenderRepair.cs` (pure)

**Analog:** `RelevantThreadFiller` text helpers (lines 368-416)
```csharp
private static string StripReplyPrefix(string subject)
{
    var cleaned = ReplyPrefixPattern().Replace(subject, string.Empty).Trim();
    return cleaned.Length > 0 ? cleaned : subject;
}

[GeneratedRegex(@"^(?:(?:RE|FW|FWD)\s*:\s*)+", RegexOptions.IgnoreCase)]
private static partial Regex ReplyPrefixPattern();
```
Repairs need a `public static partial class` with `[GeneratedRegex]` for: id tokens, `^>` quote lines, `On ... wrote:` blocks, `-----Original Message-----` / forward headers, trailing lines equal to the sender's name or signature, `*`/`_` italics, URLs/HTML tags, more than 2 bold runs, more than 6 bullets, and CR/LF in subjects. Never use `new Regex(...)` (CLAUDE.md).

---

### `Services/Evidence/RenderValidator.cs` (static, never throws)

**Analog:** `Services/Corpus/CorpusPlanValidator.cs` (same Run-per-rule isolation as `CaseGuideValidator`, see 04-PATTERNS "Never-throwing validator")

Shape to copy (04-PATTERNS lines 436-474; same structure in `CorpusPlanValidator.Validate`):
```csharp
public static List<GuideFinding> Validate(CaseDraft? draft)
{
    var findings = new List<GuideFinding>();
    ...
    Context ctx;
    try { ctx = new Context(draft); }
    catch (Exception ex)
    {
        // Malformed data must never crash the step that shows the checks
        findings.Add(CouldNotRun(ex));
        return findings;
    }
    Run(findings, ctx, CheckIds);
    ...
}

private static void Run(List<GuideFinding> findings, Context ctx, Action<Context, List<GuideFinding>> rule)
{
    try { rule(ctx, findings); }
    catch (Exception ex)
    {
        // One broken rule must not hide the others or throw into the UI (T-03-26)
        findings.Add(CouldNotRun(ex));
    }
}
```
Use `Check(RenderedThread rendered, RenderContext ctx) → List<RenderViolation>` with one `CheckV1..V8` method per row of the RESEARCH "Code Checks" table. For the variant and amount checks (V3, V6), reuse `CanonicalValueScanner` (`Services/Case/CanonicalValueScanner.cs`). It never throws and handles regex timeouts:
```csharp
public static List<ScannedValue> Scan(string? text)                    // lines 40-59
public static bool AmountsEqual(string a, string b)                     // lines 65-71: "$7.5 million" == "$7,500,000"
public static bool IsRegistered(ScannedValue scanned, IEnumerable<CanonicalValue> values)  // lines 77-101
```
The ±15% window needs the parsed decimal. `TryParseAmount` (lines 116-148) is `private`, so make it `internal static` rather than duplicating it. Violation text is plain English (UI-SPEC "Violation texts"), formatted with `CultureInfo.InvariantCulture`.

---

### `Services/Evidence/EmailComposer.cs` (pure)

**Analogs:** `AnswerKeyBuilder.SubjectOf` + `Lookups` (lines 157-166, 215-272), `ThreadingHelper.cs`, `SignatureBuilder.cs`

**Subject: one pure function shared with the answer-key overlay** (`AnswerKeyBuilder.cs` lines 156-166):
```csharp
// Prefix once from the root subject (no cumulative RE: RE:)
private static string SubjectOf(PlannedThread thread, PlannedMessage message)
{
    var subject = thread.Subject.Trim();
    return message.Kind switch
    {
        MessageKind.Reply or MessageKind.ReplyAll => ThreadingHelper.AddReplyPrefix(subject),
        MessageKind.Forward => ThreadingHelper.AddForwardPrefix(subject),
        _ => subject
    };
}
```
Create `public static string EmailComposer.SubjectFor(PlannedThread, PlannedMessage, PrivilegeMarking)`, which applies the `Privileged & Confidential - ` tag to the root subject before prefixing. Have `AnswerKeyBuilder` call it through the overlay (RESEARCH Pitfall 2).

**Name / address lookups** (`AnswerKeyBuilder.Lookups.Display`, lines 249-271). Cast first, then plan externals, with the id as fallback. Copy this into a composer `People` lookup that also returns the domain (`CaseOrganization.Domain`, otherwise the email host).

**Signature** (`Services/Case/SignatureBuilder.cs` lines 11-25). `CastMember.Signature` is already built. Append it verbatim. For the Short band with `NameOnly`/`MobileFooter`, use the first name, matching the `SignatureBuilder` switch:
```csharp
SignatureStyle.NameOnly => firstName,
SignatureStyle.MobileFooter => firstName + "\n\nSent from my mobile",
```

**Quoting: do NOT call `ThreadingHelper.FormatQuotedReply`** (lines 80-85). It formats dates with the current culture and takes the legacy `EmailMessage`. Reuse only `QuoteText` (lines 112-117) and copy the header text with an explicit culture:
```csharp
var header = $"On {originalEmail.SentDate:ddd, MMM d, yyyy} at {originalEmail.SentDate:h:mm tt}, {originalEmail.From.FullName} <{originalEmail.From.Email}> wrote:";
```
→ `string.Format(CultureInfo.GetCultureInfo("en-US"), "On {0:ddd, MMM d, yyyy} at {0:h:mm tt}, {1} <{2}> wrote:", local, name, addr)`. The forward block wording is in `FormatForwardedContent` (lines 97-105).

**Message-ID:** do not use `ThreadingHelper.GenerateMessageId` (line 7-11, random). Use `Convert.ToHexString(SHA256.HashData(...)).ToLowerInvariant()[..24] + "@" + domain` (RESEARCH Pattern 6).

---

### `Helpers/HtmlEmailFormatter.cs` (modify: structured overload)

**Analog:** itself
- `ConvertToHtml` (lines 167-197) shows the overall flow: main content, then quoted/forwarded, then `WrapInTemplate`.
- `FormatInlineElements` (lines 416-452) is the part **not** to reuse on model text. It turns `*x*` into bold, `_x_` into italics and autolinks URLs. Copy only its first line, `System.Net.WebUtility.HtmlEncode(text)`, then apply the allowlisted `**bold**` replacement with a `[GeneratedRegex]`.
- `WrapInTemplate` (lines 604-611) is `private static`. Change it to `internal static` and call it from the new `ComposeHtml(string body, string signOff, string signature, QuotedPart? quoted, OrganizationTheme theme, bool privilegedBanner)`.
- `_currentTheme` is `[ThreadStatic]` (lines 158-160). The new overload must take the theme as a parameter and never touch that static, because composition runs on pool threads.
- Leave `ConvertToHtml` unchanged for the legacy flow (RESEARCH "Deprecated").

---

### `Services/Evidence/OrganizationThemes.cs` (pure)

**Analogs:** `Models/PresentationTheme.cs` (`OrganizationTheme`, `Default`, lines 6-75), `Services/ThemeGenerator.cs` lines 140-226

```csharp
public static OrganizationTheme Default => new()
{
    ThemeName = "Professional Blue",
    PrimaryColor = "2B579A",
    SecondaryColor = "5B9BD5",
    AccentColor = "ED7D31",
    HeadingFont = "Segoe UI Semibold",
    BodyFont = "Segoe UI"
};
```
Build 8 fixed palettes as `private static readonly OrganizationTheme[]`-style factories (always return `new`). The index comes from a stable SHA-256 of the lower-cased domain. Validate the colors and fonts through `ThemeGenerator.SanitizeHexColor` / `SanitizeFont` / `AllowedFonts`, made `internal static`:
```csharp
private static string? SanitizeHexColor(string? color)   // lines 140-156
private static readonly HashSet<string> AllowedFonts = new(StringComparer.OrdinalIgnoreCase) { "Georgia", ... "Tahoma" };  // lines 200-207
```

---

### `Services/Evidence/MailboxWriter.cs` (file-I/O)

**Analogs:** `Services/EmlFileService.cs` (MimeKit header shape, lines 14-58, 103), `CaseStore.WriteAtomicAsync` (`Services/Case/CaseStore.cs` lines 348-381), `CorpusPlan.IsSafeEmlPath` (`Models/Corpus/CorpusPlan.cs` lines 102-121), `PlanNumbering` folder constants (lines 14-19)

**MimeKit build** (EmlFileService lines 14-58). Copy the order but fix the defects RESEARCH found:
```csharp
var message = new MimeMessage();
message.MessageId = messageId.Trim('<', '>');
message.Date = new DateTimeOffset(email.SentDate);          // DEFECT: machine-local offset -> use planned.SentAt
message.Subject = email.Subject;
message.From.Add(new MailboxAddress(email.From.FullName, email.From.Email));
foreach (var to in email.To) message.To.Add(new MailboxAddress(to.FullName, to.Email));
foreach (var cc in email.Cc) message.Cc.Add(new MailboxAddress(cc.FullName, cc.Email));
if (!string.IsNullOrEmpty(email.InReplyTo)) message.InReplyTo = email.InReplyTo.Trim('<', '>');
foreach (var reference in email.References) message.References.Add(reference.Trim('<', '>'));
var builder = new BodyBuilder();
builder.HtmlBody = email.BodyHtml;                           // DEFECT: HTML only -> set TextBody AND HtmlBody (multipart/alternative)
message.Body = builder.ToMessageBody();
await using var stream = File.Create(filePath);              // DEFECT: non-atomic -> MemoryStream + CaseStore.WriteAtomicAsync
```
Add `message.Headers.Add("X-ReelDiscovery-DocID", planned.DocId);` (OUT-02; RESEARCH Code Examples).

**Atomic write:** `CaseStore.WriteAtomicAsync` creates its temp file with `FileMode.CreateNew` **in the target folder** (line 354). It does not create the directory, so call `Directory.CreateDirectory(Path.GetDirectoryName(full)!)` first. Use `.ConfigureAwait(false)` everywhere in this file (it runs off the UI thread, RESEARCH Pattern 8; `CorpusPlanStore.SaveAsync` lines 63-67 is the precedent).

**Paths:** sender copy = `planned.FilePath`. Other copies use `$"{PlanNumbering.MailboxesFolder}/{folders[id]}/{PlanNumbering.InboxFolder}/{planned.DocId}.eml"`, where `folders = PlanNumbering.CustodianFolders(guide)` (newly exposed). Reject anything that fails `CorpusPlan.IsSafeEmlPath`, and add the full-path prefix check:
```csharp
public static bool IsSafeEmlPath(string? path)
{
    if (string.IsNullOrEmpty(path) || path.Contains('\\') || Path.IsPathRooted(path))
        return false;
    var segments = path.Split('/');
    return segments.Length == 4 && segments[0] == MailboxesRoot && segments.All(IsSafeFileName)
        && segments[3].EndsWith(".eml", StringComparison.OrdinalIgnoreCase);
}
```

### `Services/Corpus/PlanNumbering.cs` (modify)
Expose the existing private method (lines 163-182) without changing it. Use `internal static IReadOnlyDictionary<string, string> CustodianFolders(CaseGuide guide) => BuildFolders(guide);`, or rename it, so Inbox paths use exactly the folder names the planned sender paths use.

---

### `Services/Evidence/GenerationJournal.cs` (append + replay)

**Analog (replay side):** `Services/Corpus/CorpusPlanStore.cs` `LoadFile` (lines 105-138). Partial: no JSONL exists in the repo.
```csharp
private static PlanLoadResult LoadFile(string path, Guid expectedPlanId)
{
    try
    {
        if (!File.Exists(path)) return Fail(...);
        // Size cap before reading anything
        var cap = SizeCapOverride > 0 ? SizeCapOverride : MaxPlanFileBytes;
        if (new FileInfo(path).Length > cap) return Fail($"The plan file is larger than ...");
        ...
        plan.Normalize();
        if (plan.PlanId != expectedPlanId)
            return new PlanLoadResult { Reason = "The saved plan does not match this case.", IdMismatch = true };
        return new PlanLoadResult { Plan = plan };
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException
                                   or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                   or OverflowException)
    {
        var reason = ex.Message.Trim().TrimEnd('.');
        return Fail($"The plan file could not be read: {reason}.");
    }
}
```
Copy these pieces: `internal static long SizeCapOverride;` (tests, line 37), the path built from folder + constant only (`PlanPath`, line 44), and a result class with `Reason` that never throws (`PlanLoadResult`, lines 11-24). Parse **per line** inside its own `try/catch (JsonException)`. Skip torn, blank and unknown lines with a comment (RESEARCH Pattern 7). A foreign `PlanId` maps to a "plan changed" result, the same way `IdMismatch` does.

**Serializer options** (`CorpusPlanStore.cs` line 39): `new(CaseDraft.JsonOptions) { WriteIndented = false }`. Compact output gives one line per record, and enums are written as strings.

**Append side (no analog):** a `SemaphoreSlim(1,1)` plus `new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)`, a single `WriteAsync` of line + `\n`, then `Flush(flushToDisk: true)`. The `FileStream` + `Flush(flushToDisk: true)` usage comes from `CaseStore.WriteAtomicAsync` lines 354-358.

---

### `Services/Evidence/GenerationRunState.cs` + AnswerKeyBuilder overlay (pure projection)

**Analog:** `Services/Corpus/AnswerKeyBuilder.cs`
- Status constant (line 13): `public const string StatusPlanned = "Planned";`. Add `StatusGenerated = "Generated"`, `StatusGeneratedWithWarnings = "Generated - with warnings"`, `StatusFailed = "Failed"` and `StatusSkipped = "Skipped"` beside it.
- Rows are built in `Build(plan, guide)` (lines 27-85), and `Status = StatusPlanned` is set at lines 118 and 152. Add an optional overlay parameter, for example `Build(CorpusPlan plan, CaseGuide guide, AnswerKeyOverlay? overlay = null)`, where the overlay maps DocId → (status, subject). Apply it to **email rows only**. Attachment rows stay Planned (CONTEXT), but they copy the overlaid parent subject.
- Writers stay unchanged. Re-run `CorpusApproval.BuildFiles`-style writes (`CorpusApproval.cs` lines 35-47) for CSV and XLSX only, not the protocol, and do each write independently (RESEARCH Pitfall 8). Use the `WriteFilesAsync` catch shape (lines 64-79) with `FileLockDetector.IsFileOpenElsewhere(ex, path)`:
```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    result.FailedFileName = fileName;
    result.Error = ex.Message;
    result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
    ...
}
```
- `AnswerKeyXlsxWriter.cs` line 64: replace `"Status: Planned until the document is generated."` with text that lists the four statuses.

---

### `Services/Evidence/ThreadUnitOfWork.cs` + `GenerationRunner.cs` (orchestration)

**Analogs:** `Services/Corpus/PlanCallRunner.cs` (whole file), `RelevantThreadFiller.FillAsync` (lines 50-169)

**Bounded fan-out + fatal check + apply after WhenAll** (`FillAsync` lines 80-108):
```csharp
using var run = new PlanCallRun(ct, _maxParallel, "Click Retry Planning.");

// First pass: one call per chunk; tasks only return DTOs and never touch the threads
var firstTasks = new List<Task<PlanCallOutcome<ThreadPlanBatchDto>>>(n);
for (var i = 0; i < n; i++)
{
    var index = i;
    var request = ChunkRequest(prompts[index], index, n, retry: false);
    firstTasks.Add(PlanCallRunner.RunAsync<ThreadPlanBatchDto>(_llm, request, run,
        onStart: () => progress?.Report(new PlanProgress { ... })));
}

// A user cancel propagates from WhenAll before anything is applied to the threads
var first = await Task.WhenAll(firstTasks);

if (run.IsFatal)
{
    result.FatalError = run.FatalError;
    return result;
}
```
Phase 5 difference: create **one `PlanCallRun` per run** (fatal stops everything) and **one `Task.WhenAll` per wave**. Each task is a whole `ThreadUnitOfWork` (up to 3 `PlanCallRunner.RunAsync` calls, then compose, write and journal), not a bare call. The ledger is merged on the runner after the wave's `WhenAll` (D-11). The retry hint string becomes `"Click Continue Generating."`.

**Outcome classification is already done** (`PlanCallRunner.RunAsync` lines 77-137). Do not write a new catch ladder:
```csharp
catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested) { throw; }
catch (OperationCanceledException) when (run.Cts.IsCancellationRequested) { return new PlanCallOutcome<T>(); }  // fatal from another call
catch (OperationCanceledException) { return new PlanCallOutcome<T> { Error = $"The request timed out. {run.RetryHint}" }; }
catch (LlmCallException ex) when (ex.Result.IsFatal) { run.SetFatal(ex.Result.Message ?? ex.Message); return new PlanCallOutcome<T>(); }
catch (LlmCallException ex) { var refused = ex.Result.Outcome == LlmOutcome.Refused; return new PlanCallOutcome<T> { Error = DescribeOutcome(ex.Result, run.RetryHint), Refused = refused, RefusalMessage = ... }; }
```
Mapping in the unit of work: `run.IsFatal` → `NotRun` (left pending, no journal record). `Value == null && Refused` → `ThreadFailed{Kind=Refused}`. Other null → `ThreadFailed{Kind=Timeout|Provider}`. A write exception → `ThreadFailed{Kind=Write}`, which is logged and the run continues (RUN-02). Catch `OperationCanceledException` before `Exception` (CLAUDE.md).

**Token usage per thread (modify `PlanCallRunner` + `LlmGateway`).** `PlanCallRunner.RunAsync` line 95 calls `.GetValueOrThrow()` and drops the `LlmResult`, so callers cannot see token counts. Required changes:
1. `LlmResult` (`Services/Ai/LlmResult.cs` lines 24-45): add `public int InputTokens { get; set; }` and `public int OutputTokens { get; set; }`.
2. `LlmGateway.GenerateAsync` (lines 79-88): accumulate `response.InputTokens` / `OutputTokens` into locals on every attempt (all attempts are billed, per the comment at line 86), then set them in `BuildResult` (lines 146-168).
3. `PlanCallOutcome<T>` (lines 63-69): add `InputTokens`, `OutputTokens`. Set them from the result on success and from `ex.Result` in the `LlmCallException` catches.
4. Price with `model.CalculateTotalCost(in, out)` (same call as `GenerationCostEstimator.Estimate` line 73). Resolve the model through `ILlmGateway.ResolveModel(LlmTier.Story)` (`LlmGateway.cs` line 49).

**Remaining-cost estimate for the Cost tile:** reuse `GenerationCostEstimator.RelevantInputPerMessage` / `RelevantOutputPerMessage` (lines 23-24) × the remaining relevant messages × `QaOverhead`.

---

### `Services/Evidence/GenerationSnapshot.cs` (lock + counters + bounded queue)

**Analog (partial):** `PlanCallRun` locked state (`PlanCallRunner.cs` lines 8-54)
```csharp
private readonly object _lock = new();
private string _fatalError = string.Empty;

public string FatalError
{
    get
    {
        lock (_lock)
            return _fatalError;
    }
}
```
Workers call `Record*(...)` methods under `lock (_lock)`. The UI timer calls `Drain()`, which returns an immutable copy of the counters plus queued events. Cap the queue at 500 (UI-SPEC Activity cap). The cost ticker's polling of `TokenUsageTracker` (`Forms/WizardForm.cs` lines 112-120) is the precedent for a UI timer reading a locked tracker:
```csharp
_costUpdateTimer = new System.Windows.Forms.Timer
{
    Interval = 1000 // Update every second
};
_costUpdateTimer.Tick += (s, e) => UpdateCostDisplay();
_costUpdateTimer.Start();
```
(The step uses `Interval = 250`, and both `Stop()` and `Dispose()` must run in `Dispose`, as in lines 640-641.)

---

### `Services/Evidence/StorySoFarDocxBuilder.cs` (pure bytes)

**Analog:** `Services/Corpus/ReviewProtocolBuilder.cs`

**Imports + static using of the nested Word types** (lines 1-10):
```csharp
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using static ReelDiscovery.Services.Case.CaseGuideDocxExporter;
```
`WordBuilder`, `ParaFormat` and `Cell` are already `internal sealed` (CaseGuideDocxExporter.cs lines 505, 527, 841), so no visibility change is needed.

**File name helper + Build** (lines 114-116, 151-158, 218):
```csharp
public const string FileNameSuffix = " - Review Protocol.docx";
public static string FileNameFor(string folderName) => $"{folderName}{FileNameSuffix}";

public static byte[] Build(CaseDraft draft, ReviewProtocolProse prose)
{
    var guide = draft.Guide ?? new CaseGuide();
    var word = new CaseGuideDocxExporter.WordBuilder();
    word.Paragraph(DocumentTitle, new ParaFormat { Style = "Title" });
    word.Paragraph(CaseFolderNaming.CaseTitle(draft), new ParaFormat { Style = "Subtitle" });
    ...
    word.Paragraph(issue.RelevanceCriteria, new ParaFormat { BoldLead = "Relevance criteria: " });
    ...
    return word.Save(DocumentTitle, landscape: false);
}
```
Story so far: `FileNameSuffix = " - Story So Far.docx"`, Heading1 for the title, Heading2 per month (`MMMM yyyy`, invariant/en-US), and one `ParaFormat { BoldLead = "{MMM d, yyyy} · {Kind} " }` paragraph per entry. The second line uses `SizeHalfPoints` for smaller text (`ParaFormat` line 511). Also copy the `IsProtocolFileName`/`ResolveFileName` guard pattern (lines 118+) for a bare-name check.

---

### `UserControls/StepGenerateCorpus.cs` (wizard step layout)

**Analog:** `UserControls/StepReviewPlan.cs`

**Render-state enum + class header + IWizardStep members** (lines 13-32, 116-133):
```csharp
/// <summary>
/// Which plan state LoadForRender shows (render tests only; no run is started).
/// </summary>
internal enum ReviewPlanRenderState { Planning, Ready, Stale, ApprovedSettingsChanged, NoPlan, Failed }

public partial class StepReviewPlan : UserControl, IWizardStep
{
    ...
    public string StepTitle => "Review Plan";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isPlanning && !_isApproving;
    public bool CanMoveBack => !_isPlanning && !_isApproving;

    /// <summary>
    /// True while a planning run is active (closing asks C-P2).
    /// </summary>
    public bool IsPlanning => _isPlanning;

    public event EventHandler? StateChanged;

    public StepReviewPlan() { InitializeUI(); }

    private CaseDraft Draft => _state.Case;
```
→ `StepGenerateCorpus`, `StepTitle => "Generate Corpus"`, `public bool IsGenerating => _isGenerating;`, and `GenerateCorpusRenderState { Ready, Running, Partial, FinishedClean, FinishedIssues, NotApproved, PlanChanged, Unreadable }`.

**Fields** (lines 52-114). Copy the Hungarian `_lbl`/`_btn`/`_grid` names with `= null!`: `_root`, `_lblTitle`, `_lblSubtitle`, `_headerButtons`, `_btnStartOver`, `_btnRetryFailed`, `_lblNotice`, `_banner`, `_bannerRow`, `_lblBannerHeading`, `_lblBannerBody`, `_btnContinue`, `_tilesRow`, `_tiles`, `_bodyLayout`, `_wavesCard`, `_gridWaves`, `_activityCard`, `_gridActivity`, `_stateCard`, `_stateContent`, `_lblStateHeading`, `_lblStateBody`, `_btnStateAction`, `_lblStatus`, `_progress`, `_btnCancel`, `_refreshTimer`.

**InitializeUI preamble + root TLP with row-index comments** (lines 139-178). UI-SPEC S1 has a different row set:
```csharp
// Content aligns with the wizard's page margin (the shell supplies it)
this.Padding = new Padding(0);
this.BackColor = Theme.Background;
var gap = LogicalToDeviceUnits(Theme.Gap);
var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
var pad = LogicalToDeviceUnits(Theme.CardPadding);
var inset = LogicalToDeviceUnits(15);
var buttonHeight = LogicalToDeviceUnits(34);
_cornerInset = LogicalToDeviceUnits(8);

_root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0), BackColor = Theme.Background };
_root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
_root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header
...
BuildHeader(gap, smallGap, buttonHeight);
BuildBanner(gap, smallGap, pad, buttonHeight);
BuildTiles(gap);
BuildBody(gap, smallGap, inset, buttonHeight);
BuildStatusStrip(gap, buttonHeight);
this.Controls.Add(_root);

// Wire handlers only after every control exists (handlers read other controls)
...
_root.SizeChanged += (s, e) => { SizeBanner(); ... };
_stateCard.Resize += (s, e) => CenterStateContent();
_stateContent.SizeChanged += (s, e) => CenterStateContent();
```
Rows: `// 0: Header`, `// 1: Notice line`, `// 2: Partial-run banner` (Absolute, sized in `SizeBanner`, see lines 161-163 and 1113), `// 3: Tiles`, `// 4: Body (Waves + Activity, or the state card)`, `// 5: Status strip`.

**Header / banner / state card:** copy `BuildHeader` (line 205+), `BuildBanner` (lines 283-347), `ShowStateCard` / `SetStateCardShown` / `CenterStateContent` (lines 1050-1088) and `SizeBanner` (line 1113) as-is, then change the copy source to `GenerationUiText`.

**Tiles row: copy verbatim** (lines 349-378):
```csharp
_tilesRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, AutoSize = true,
    AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, gap), Padding = new Padding(0),
    BackColor = Theme.Background, Visible = false };
_tilesRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Tiles (Dock Fill, so one height)
var half = LogicalToDeviceUnits(Theme.SmallGap) / 2;
for (var i = 0; i < 7; i++)
{
    _tilesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
    var tile = new StatTile { Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : half, 0, i == 6 ? 0 : half, 0) };
    _tiles.Add(tile);
    _tilesRow.Controls.Add(tile, i, 0);
}
```
Fill tiles with `_tiles[i].SetContent(GenerationUiText.Tile(kind, snapshot))` (`StatTile.SetContent(TileContent)`, `StatTile.cs` line 110; same call as lines 894-896). The Warnings/Failed value color (UI-SPEC) needs a value-color setter on `StatTile`, or a `ValueColor` field on `TileContent`.

**Two cards with header + grid:** `NewCardLayout`, `NewCardHeader`, `CardCaption` (lines 487-519), the counts-card body (`BuildCountsCard` ~lines 580-665), `NewCountsGrid` (lines 667-676), `AddTextColumn` / `AddFillColumn` / `AddNumberColumn` (lines 678-721). For the right-aligned numerics (UI-SPEC rule 15):
```csharp
private DataGridViewTextBoxColumn AddNumberColumn(DataGridView grid, string name, string header, int width)
{
    var column = AddTextColumn(grid, name, header, width);
    column.ValueType = typeof(int);
    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    column.DefaultCellStyle.Format = "N0";
    column.DefaultCellStyle.FormatProvider = Invariant;
    column.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
    column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
    column.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
    return column;
}
```
Use `SortMode = NotSortable` here (UI-SPEC: no sorting), unlike `AddTextColumn`'s `Automatic`.

**Status-dot column:** `GridStyle.AddDotColumn(grid, owner)` (`Helpers/GridStyle.cs` lines 54-70) plus `CellPainting`, copied from `GuideTabPanel.Grid_CellPainting` (lines 878-891):
```csharp
private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
{
    if (e.RowIndex < 0 || e.Graphics == null)
        return;
    var dot = Grid.Columns[GridStyle.DotColumnName];
    if (dot == null || e.ColumnIndex != dot.Index)
        return;
    e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
    if (Grid.Rows[e.RowIndex].Tag is string id)
        Theme.DrawStatusDot(e.Graphics, e.CellBounds, DotColor(id), this);
    e.Handled = true;
}
```
Note: `AddDotColumn` is 28 logical px wide, but UI-SPEC says 24. Use `AddDotColumn` and then set the width, or keep 28 and record the deviation. The dot paints at inset 15 (comment line 66), so check at 144 DPI.

**Status strip: copy verbatim** (lines 840-881), with button text `"Cancel Generation"`.

**SetStatus no-op after dispose** (lines 1258-1265) and **render access** (`LoadForRender` lines 1272-1320, `internal` accessors lines 1322-1333). Add `internal void LoadForRender(WizardState state, GenerateCorpusRenderState render, GenerationSnapshot? snapshot = null, string reason = "")`, which seeds from a snapshot and never starts a run.

---

### `UserControls/StepGenerateCorpus.Run.cs` (run, cancel, lifecycle)

**Analog:** `UserControls/StepReviewPlan.Run.cs`

**Imports** (lines 1-8) + `using ReelDiscovery.Models.Evidence; using ReelDiscovery.Services.Evidence;`.

**Autosave** (lines 25-49). Copy verbatim:
```csharp
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

**Entry decision** (lines 58-133). Make the same `switch` over a pure `GenerationUiText.EntryDecision(GenerationEntryInput)`. Use `CorpusPlanStore.EnsureLoaded(Draft)` (line 82) and `CorpusApproval.IsGuideStale` for gating, and copy the `_boundCaseId` case-switch reset (lines 68-73) and the `PendingNotice` handling (lines 75-79). Never auto-start (UI-SPEC rule 5).

**TrackRun + CancelAndWaitForRun** (lines 194-232). Copy verbatim. `WizardForm.StopActiveRun` calls it.

**Run core** (lines 234-366). Keep the same skeleton: `_cts = new CancellationTokenSource()`, `_isGenerating = true`, show bar + Cancel, `StateChanged`, then `try / catch (OperationCanceledException) / catch (LlmCallException ex) when (ex.Result.IsFatal) / catch (Exception ex) / finally` with `if (IsDisposed) return;` after every await. **Phase 5 differences (RESEARCH Pattern 8):**
- Start the engine with `_activeRun = Task.Run(() => runner.RunAsync(input, _snapshot, PersistOnUiThreadAsync, _cts.Token));`, not a direct `await planner.PlanAsync(...)`. Replace `Progress<PlanProgress>` with the 250 ms timer draining `_snapshot`.
- Wave-end persistence that mutates `Draft` (`Corpus.Generation.Ledger`, WrittenFiles stamps) is marshalled with `BeginInvoke` + `TaskCompletionSource`, then calls `SaveAsync()` on the UI thread.
- Cancel no longer means "discard everything". The journal keeps finished threads, so after cancel show the partial-run banner and status `Cancelled. {done} of {total} threads are saved...`.

**Cancel confirm** (lines 412-439). Copy it, including the "run moved on while the dialog was open" re-check and the `ObjectDisposedException` comment:
```csharp
var (title, text) = CorpusUiText.CP1CancelPlanning(Draft.Corpus?.Plan != null);
var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
```
→ `GenerationUiText.CG1CancelGeneration(done)`. For Start Over (C-G2) use `MessageBoxIcon.Warning`, still with `Button2`.

**Next gate** (lines 147-170): just `return Task.FromResult(!_isGenerating)` (UI-SPEC U7). **Dispose** (lines 555-571): copy verbatim and also stop and dispose the refresh timer.

**Opening an EML from an Activity row:** copy `StepCaseSummary.OpenCorpusFile` (lines 1081-1107). Build the path from the verified case folder + plan `FilePath` (`/` → `\`), check that `Path.GetFullPath(...)` starts with the case folder, then `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`, catching `Win32Exception or InvalidOperationException or IOException`.

---

### `UserControls/GenerationUiText.cs` (pure copy)

**Analog:** `UserControls/CorpusUiText.cs`

**POCO content with public fields + enums** (lines 45-121):
```csharp
public enum TileKind { Emails, Threads, Relevant, Hot, Privileged, Attachments, Cost }

public class TileContent
{
    public string Caption = "";
    public string Value = "";
    public string SubLine = "";
    public string Tooltip = "";
}

public enum ReviewPlanEntry { KeepRun, AutoPlan, ..., UnreadableCard }

public class ReviewPlanEntryInput
{
    public bool Running;
    public bool HasPlan;
    // Non-empty when the saved plan could not be loaded
    public string UnreadableReason = "";
    ...
}
```
Use a distinct enum name such as `GenerationTileKind` (`TileKind` already exists in this namespace) and reuse `TileContent`. Add `GenerationEntry` / `GenerationEntryInput` / `GenerationStateCard`.

**Class doc + invariant culture** (lines 123-129):
```csharp
/// <summary>
/// All Phase 4 copy with counts, dates or conditions (04-UI-SPEC), pure and unit-tested so the corpus steps stay
/// layout-only (03-06 precedent). Every string is plain text; labels showing it set UseMnemonic = false (T-04-33).
/// </summary>
public static class CorpusUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
```
**First-match decision function** (lines 365-381). This is the template for `EntryDecision` (UI-SPEC rules 1-7). **Tile switch** (lines 428-439) shows the `Tile(kind, ...)` shape. **Confirm tuples:** `CP1CancelPlanning` (line 628) and `CP2ClosePlanning` (line 634) return `(string Title, string Text)`. Reuse `CaseGuideUiText.Plural`, `SubtitleText`, `DisplayPath` and `SaveFailedText` rather than duplicating them (04-PATTERNS).

### `UserControls/CorpusUiText.cs` + `Forms/ReplanDialog.cs` (modify, S2)
`ReplanBody(bool approved)` (lines 301-307) gains a parameter such as `int generatedThreads` and appends the S2 sentence when it is > 0. `ReplanDialog` (line 17 ctor, line 32 `AddBody`) passes it through. The caller is `StepReviewPlan.Run.cs` line 449 (`new ReplanDialog(Draft.Corpus.Approved)`).

---

### `UserControls/StepCaseSummary.cs` (modify, S3)

**Analog:** itself
- Rows are created at lines 402-404 with custom captions: `CreateRow("Answer key", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open CSV", "Open in Excel" })`. Add `_rowMailboxes = CreateRow("Mailboxes", true, true, buttonHeight, new[] { "Open Mailboxes Folder", "Generate Emails" })` and `_rowStory = CreateRow("Story so far", true, true, buttonHeight, new[] { "Open in Word", "Export Story So Far" })`.
- Insert them into the `items` array (lines 413-424) after `(_rowProtocol, "")`.
- Line text goes in `RefreshCorpusRows` (line 731+), which calls `GenerationUiText.SummaryRowText`. The dot uses `SetDot(row, DotColor(...))` (line 791 pattern).
- The Mailboxes folder opens like `OpenCaseFolder` (lines 1109-1127) through `explorer.exe` + `Path.GetFullPath(Path.Combine(folder, PlanNumbering.MailboxesFolder))`. The story docx opens like `OpenCorpusFile` (lines 1081-1107) and shows `MissingFileText` when the file is gone.
- `Generate Emails` navigates by type: `((WizardForm)FindForm()).NavigateToStage(CaseStage.Generation)` (`WizardForm.cs` lines 311-318).
- Extend `LoadForRender` (line 873) to seed generation states for render state 7.

---

### `Forms/WizardForm.cs` (modify, S4)
- `StepTypesFor` (lines 282-288): insert `typeof(StepGenerateCorpus)` between `typeof(StepReviewPlan)` and `typeof(StepCaseSummary)`.
- `StepTypeForStage` (lines 298-306): add `CaseStage.Generation => typeof(StepGenerateCorpus),`.
- Flush list (line 526): add `or StepGenerateCorpus` to the pattern.
- `IsRunActive` (lines 538-545): add `or StepGenerateCorpus { IsGenerating: true }`.
- `StopActiveRun` (lines 549-562): add a branch like the `StepReviewPlan` one (line 557-559):
```csharp
else if (step is StepReviewPlan review)
    // A planning run is cancelled and awaited, so no further calls are billed (T-04-61)
    WaitWhileStopping(() => review.CancelAndWaitForRun(TimeSpan.FromSeconds(10)));
```
  Comment for the new branch: in-flight threads are discarded, and finished ones are already journaled.
- `OnFormClosing` (lines 595-597): extend the ternary into a switch, so `StepGenerateCorpus` uses `GenerationUiText.CG3CloseDuringRun()`.

### `UserControls/CaseGuideUiText.cs` (modify)
`StageLabel(CaseStage)`: add `CaseStage.Generation => "Generating emails",` before the `Summary` arm.

---

## Shared Patterns

### Strict-schema Story calls
**Source:** `RelevantThreadFiller.cs` lines 171-178 (request), 590-643 (DTOs); `PlanCallRunner.cs` lines 77-137
**Apply to:** `ThreadRenderer`
- `new LlmRequest { Tier = LlmTier.Story, OperationName = "...", SystemPrompt = <const>, UserPrompt = ..., MaxOutputTokens = ... }`
- Calls go through `PlanCallRunner.RunAsync<T>(llm, request, run, onStart)`, never `_llm.GenerateAsync` directly inside the run (fatal/timeout/refusal classification).
- Register the DTO in `CaseSchemaTests.CaseSchemaTypes()` (`ReelDiscovery.Tests/Case/CaseSchemaTests.cs` lines 10-22):
```csharp
foreach (var type in CorpusPlanner.SchemaTypes)
    data.Add(nameof(CorpusPlanner), type);
// + foreach (var type in ThreadRenderer.SchemaTypes) data.Add(nameof(ThreadRenderer), type);
```
- The enum `LedgerKind` in the DTO is fine: the gateway deserializes with `JsonStringEnumConverter` (`LlmGateway.cs` lines 17-21).

### Treat model output as untrusted
**Source:** `RelevantThreadFiller.ApplyBatch` / `Clean` (lines 208-270, 370-376), `CorpusPlan.Normalize` (lines 61-160)
**Apply to:** `RenderRepair`, `LedgerMerger`, `GenerationJournal` replay, `EmailComposer` (HTML-encode every model string, strip CR/LF from subjects)

### Atomic writes, path safety, ConfigureAwait
**Source:** `CaseStore.WriteAtomicAsync` (lines 348-381), `CorpusPlan.IsSafeEmlPath` (lines 102-121), `CorpusPlanStore.PlanPath` (line 44: folder + constant only), `CorpusPlanStore.SaveAsync` (`.ConfigureAwait(false)`, lines 63-67)
**Apply to:** `MailboxWriter`, `GenerationJournal`, the key re-projection, `StorySoFarDocxBuilder` callers, and Start Over deletion (journal-listed paths only, re-validated)

### Fatal stop / refusal / cancel semantics
**Source:** `PlanCallRun.SetFatal` (lines 45-54), `PlanCallRunner` catch ladder (lines 98-131), `StepReviewPlan.Run.cs` lines 320-351
**Apply to:** `ThreadUnitOfWork`, `GenerationRunner`, `StepGenerateCorpus.Run.cs`
- Catch `OperationCanceledException` before `Exception`. A user cancel throws. A fatal error from another call returns empty (the thread is left pending). Refusals are recorded and not re-asked (D-06).

### UI-thread discipline
**Source:** `StepReviewPlan.Run.cs` (`if (IsDisposed) return;` after every await, lines 43, 272, 284, 297), `SetStatus` (lines 1258-1265), Dispose (lines 555-571)
**Apply to:** `StepGenerateCorpus*`. Workers write only to `GenerationSnapshot`. `Draft` is only mutated on the UI thread.

### Theme primitives
**Source:** `Helpers/Theme.cs` (`DrawStatusDot` line 64), `GridStyle.Apply` / `AddDotColumn` (`GridStyle.cs` lines 15-70), `CardPanel`, `StatTile`, `SlimProgressBar`, `ButtonHelper.CreateButton(text, 0, buttonHeight, ButtonStyle.X)`
**Apply to:** every new control. All sizes go through `LogicalToDeviceUnits`, fonts only come from `Theme.*Font()`, and there is no `Color.Red` or colored RichTextBox (the legacy `StepGeneration.AppendLog` is **not** the model).

### Culture
**Source:** `AnswerKeyBuilder.DateFormat` + `CultureInfo.InvariantCulture` (lines 18, 107), `PlanNumbering` id formatting (line 49)
**Apply to:** ids, journal, key, prompts. Use `CultureInfo.GetCultureInfo("en-US")` only for human-visible email text (quote headers, docx month names; RESEARCH Pitfall 6).

### Test scaffolding
**Source:**
- Gateway + fake: `CaseTestData.CreateGateway()` (used by `PlanCallRunnerTests.cs` line 31) and `FakeLlmProvider` (`Fakes/FakeLlmProvider.cs`). `Respond` (lines 37-41, 71-73) answers by prompt content for parallel tests, and `OnSend` (lines 31-35, 50) cancels deterministically mid-run. Response factories `Ok(text, input, output)` / `Refused` / `Failed(kind)` are at lines 96-106.
- Plan fixture: `PlanFixtures.ValidPlan(CorpusTestData.RichDraft())` (`Corpus/PlanFixtures.cs` line 37: 8 relevant one-message threads with one privileged and one hot). `EvidenceTestData` must add multi-message, forward, external-sender and multi-custodian threads. Clone with `PlanFixtures.Clone` (line 175).
- Prompt parsing in fakes: `CorpusTestData.ThreadIdsIn` / `ThreadSeqsIn` (`Corpus/CorpusTestData.cs` lines 112-136), plus the `[GeneratedRegex]` message-line parser in `CorpusReviewPlanRenderTests.cs` lines 24-26.
- Temp folders: `CorpusPlanStoreTests` ctor/Dispose (lines 13-25), including the `SizeCapOverride = 0` reset.
- Render: `CorpusReviewPlanRenderTests.cs` (`[Trait("Category","Render")]`, `[Collection(RenderCollection.Name)]`, `RenderBoth(...)` helper, `Lazy<CorpusPlan>` fixtures, lines 20-88).
- Docx validity: `OpenXmlValidator` as in `Corpus/ReviewProtocolTests.cs`.
- Wizard flow: `Case/WizardFlowTests.cs`. Update the case-driven step array and add `[InlineData(CaseStage.Generation, typeof(StepGenerateCorpus))]`.

---

## No Analog Found

| File | Role | Data Flow | Reason / what to use instead |
|---|---|---|---|
| `Services/Evidence/GenerationJournal.cs` (append half) | service | append-only file-I/O | No JSONL or append-mode writer exists. Use RESEARCH Pattern 7 (SemaphoreSlim(1), `FileMode.Append`, `Flush(flushToDisk: true)`, one write per line). The replay half copies `CorpusPlanStore.LoadFile` |
| `Services/Evidence/WavePlanner.cs` (window rules) | utility | transform | Only the ordering and the greedy loop have an analog (`RelevantThreadFiller.BuildChunks`). The 7/14-day windows and the split above 12 at the largest gap come from RESEARCH Pattern 1 |
| `Services/Evidence/GenerationSnapshot.cs` (bounded event queue + drain) | utility | event-driven | Only the locked-field style exists (`PlanCallRun`). Queue cap 500 and drain-on-timer come from UI-SPEC S1 / RESEARCH Pattern 8 |

## Metadata

**Analog search scope:** `Models/{Case,Corpus}`, `Services/{Ai,Case,Corpus}`, `Services/EmlFileService.cs`, `Services/ThemeGenerator.cs`, `Helpers/`, `UserControls/`, `Forms/`, `ReelDiscovery.Tests/{Case,Corpus,Fakes,Render}`
**Files scanned:** ~40 (17 read in full, the rest by targeted ranges/grep)
**Pattern extraction date:** 2026-10-03
