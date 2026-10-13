# Phase 6: Attachments & Non-Relevant Corpus - Pattern Map

**Mapped:** 2026-10-04
**Files analyzed:** 47 (22 new, 25 modified; production + tests)
**Analogs found:** 45 / 47

All paths are relative to the repo root `C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery`. Line numbers come from source read on 2026-10-04.

Conventions that apply to every file below (CLAUDE.md + Phase 5 code): file-scoped `namespace ReelDiscovery.Services.Evidence;`. Usings are alphabetical by group with no blank lines between groups (`System.*`, then third-party such as `MimeKit`, then `ReelDiscovery.*`). Each class carries a `/// <summary>` that cites the decision ids it implements (D-xx, RUN-xx, T-xx-xx). Use `[GeneratedRegex]` and `StableRank`/SHA-256 instead of `Random`/`Guid`/`UtcNow` for anything written into an EML. Catch `OperationCanceledException` before `Exception`. Every swallowed catch carries a comment. No `record` types, no primary constructors and no `#region`.

---

## File Classification

### New production files

| New File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `Services/Evidence/RenderProfile.cs` | config/model | n/a (strategy bag) | `Services/Evidence/ThreadRenderContext.cs` | role-match |
| `Services/Evidence/FillerPrompts.cs` | utility (prompt builder) | transform | `Services/Evidence/EvidencePrompts.cs` | exact |
| `Services/Evidence/FillerRenderer.cs` (filler DTO without `newDetails`; or fold into `ThreadRenderer`) | service (request + DTO) | request-response | `Services/Evidence/ThreadRenderer.cs` | exact |
| `Services/Evidence/FillerAssignments.cs` | utility (pure apportionment) | transform | `Services/Evidence/RenderAssignments.cs` | exact |
| `Services/Evidence/SideWorld.cs` / `SideWorldBuilder.cs` | model + service | request-response (one Volume call + code fallback) | `ThreadRenderer.cs` (DTO/request) + `RenderAssignments.cs` (deterministic fallback) | role-match |
| `Services/Evidence/DecoyScanner.cs` | utility (pure check) | transform | `Services/Evidence/RenderValidator.cs` (`ContainsWord`, `Run`) | exact |
| `Services/Evidence/AttachmentUnitOfWork.cs` | service (unit of work) | request-response + file-I/O | `Services/Evidence/ThreadUnitOfWork.cs` | exact |
| `Services/Evidence/AttachmentRenderer.cs` | service (per-kind DTOs + requests) | request-response | `Services/Evidence/ThreadRenderer.cs` | exact |
| `Services/Evidence/AttachmentPrompts.cs` | utility (prompt builder) | transform | `Services/Evidence/EvidencePrompts.cs` | exact |
| `Services/Evidence/AttachmentNaming.cs` | utility (pure) | transform | `RelevantThreadFiller.FileNameFor` (`Services/Corpus/RelevantThreadFiller.cs:388-407`) | exact |
| `Services/Evidence/AttachmentChains.cs` | utility (pure plan projection) | transform | `RenderAssignments.Compute` (`RenderAssignments.cs:46-76`) | role-match |
| `Services/Evidence/InviteScheduler.cs` | utility (pure) | transform | `LedgerSelector` / `KnowledgeGate` (pure, date-gated) + `RenderAssignments.StableRank` | role-match |
| `Services/Evidence/AttachmentHarvester.cs` | utility | file-I/O (read) | `MailboxWriter.ResolveUnderCase` + RESEARCH "Harvesting" example | partial |
| `Services/Evidence/IMediaGenerator.cs` (+ `OpenAIMediaGenerator` adapter) | service seam | request-response | `Services/Ai/OpenAIMediaClient.cs`, `Services/Ai/ILlmGateway.cs` | role-match |
| `Services/Evidence/VoiceAssignments.cs` | utility (pure) | transform | `OrganizationThemes.ForDomain` (`OrganizationThemes.cs:33-54`) | exact |
| `Services/Evidence/VoicemailComposer.cs` (or a method on `EmailComposer`) | utility (pure composer) | transform | `EmailComposer.Compose` / `PlainTextOf` | exact |
| `Helpers/BulkMailFormatter.cs` | utility (HTML formatter) | transform | `Helpers/HtmlEmailFormatter.cs` `ComposeFragment`/`WrapInTemplate` (lines 625-860) | exact |
| `Models/Evidence/AttachmentRunStatus.cs` | model (enum) | n/a | `Models/Evidence/GenerationState.cs:12` (`ThreadRunStatus`) | exact |
| `Models/Evidence/JournalAttachment.cs` (or new fields on `JournalRecord`) | model | persisted JSONL | `Models/Evidence/JournalRecord.cs` (`JournalMessage`) | exact |

### Modified production files

| Modified File | Role | Data Flow | Analog / pattern source | Match Quality |
|---|---|---|---|---|
| `Services/Evidence/ThreadUnitOfWork.cs` | service | request-response | itself (generalize with `RenderProfile`, tier-aware `Cost`) | self |
| `Services/Evidence/GenerationRunner.cs` | service (orchestrator) | batch / event-driven | itself (wave loop lines 170-245 → Stage B/C) | self |
| `Services/Evidence/MailboxWriter.cs` | service | file-I/O | itself (`BuildMime` 65-101, `BoundaryFor` 147-151) | self |
| `Services/Evidence/EmailComposer.cs` | utility | transform | itself (`ComposedEmail`, `Compose`) | self |
| `Services/Evidence/RenderValidator.cs` | utility | transform | itself (`Check` 88-116 → `CheckAttachment`) | self |
| `Services/Evidence/GenerationJournal.cs` | service | file-I/O (JSONL) | itself (`Replay` switch 149-167) | self |
| `Models/Evidence/JournalRecord.cs` | model | persisted | itself (enum line 6, `Normalize` 59-103) | self |
| `Services/Evidence/GenerationRunState.cs` | utility (projection) | transform | itself (`From`, `BuildOverlay`, `ToCounts`, `EstimateRemaining`) | self |
| `Services/Evidence/GenerationReset.cs` | service | file-I/O (delete) | itself (`DeleteAll` 42-84) | self |
| `Services/Evidence/LedgerMerger.cs` | utility | transform | itself (`MaxEntriesPerThread`, `EntriesFor`) | self |
| `Services/Evidence/OrganizationThemes.cs` | utility | transform | itself (`ForDomain` must set `OrganizationName`) | self |
| `Services/Evidence/GenerationSnapshot.cs` + `Models/Evidence/GenerationProgress.cs` | service (live counters) | event-driven | itself (`RecordThread`, `AddEvent`) | self |
| `Services/OfficeDocumentService.cs` | service | transform (bytes) | itself (overloads next to lines 14, 121, 169) | self |
| `Services/CalendarService.cs` | service | transform (bytes) | itself (new overload) | self |
| `Helpers/HtmlEmailFormatter.cs` | utility | transform | itself (`ComposeFragment` optional inline images) | self |
| `Services/Corpus/AnswerKeyBuilder.cs` | utility (projection) | transform | itself (overlay applied lines 82-88; `AttachmentRow` 136-167) | self |
| `Services/Corpus/GenerationCostEstimator.cs` | utility | transform | itself (`Estimate` 42-80) | self |
| `Models/TokenUsageTracker.cs` | model (thread-safe accumulator) | event-driven | itself (`RecordUsage` 23-44) | self |
| `Models/AIModelConfig.cs` / `Models/GenerationConfig.cs` | config | n/a | media unit prices beside the token prices | self |
| `Models/Evidence/GenerationState.cs` | model (case.json cache) | persisted | itself (counts + `Normalize`) | self |
| `UserControls/GenerationUiText.cs` | utility (pure UI copy) | transform | itself (`Tile`, `ActivityText`, `ProgressText`, `BannerBody`, `StateCard`) | self |
| `UserControls/StepGenerateCorpus.cs` + `.Run.cs` | component (WinForms step) | event-driven | itself (`BuildWavesCard` 500-519, `ApplyWaves` 711-742) | self |
| `UserControls/StepCaseSummary.cs` | component | event-driven | itself + `GenerationUiText.MailboxesRow` | self |
| `UserControls/ModelConfigurationDialog.cs` | component (dialog) | request-response | itself (`mainLayout` 48-66, `bottomPanel` 187-203) | self |

### Test files (new / extended)

| Test File | Analog | Match |
|---|---|---|
| `ReelDiscovery.Tests/Evidence/AttachmentTestData.cs` (W0) | `ReelDiscovery.Tests/Evidence/EvidenceTestData.cs` | exact |
| `ReelDiscovery.Tests/Fakes/FakeMediaGenerator.cs` (W0) | `ReelDiscovery.Tests/Fakes/FakeLlmProvider.cs` | exact |
| `AttachmentUnitOfWorkTests`, `FillerPrompts`/`ThreadUnitOfWork` extensions | `ReelDiscovery.Tests/Evidence/ThreadUnitOfWorkTests.cs` | exact |
| `InviteSchedulerTests`, `AttachmentChainsTests`, `AttachmentNamingTests`, `DecoyScannerTests`, `FillerAssignmentsTests`, `SideWorldTests`, `DistributionParityTests` | `ReelDiscovery.Tests/Evidence/RenderAssignmentsTests.cs`, `RenderValidatorTests.cs` | role-match |
| `CalendarServiceTests`, `OfficeDocumentServiceTests` | `ReelDiscovery.Tests/Documents/DocumentRegressionTests.cs` | exact |
| `BulkMailFormatterTests`, `VoicemailComposerTests` | `ReelDiscovery.Tests/Evidence/ComposeHtmlTests.cs`, `EmailComposerTests.cs` | exact |
| `MailboxWriterTests` (extend) | itself | self |
| `CaseSchemaTests` (add types) | `ReelDiscovery.Tests/Case/CaseSchemaTests.cs:11-25` | self |
| `GenerateCorpusRenderTests` / `CaseSummaryRenderTests` / `DialogRenderTests` (extend) | `ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs:169-218` | self |

---

## Pattern Assignments

### `Services/Evidence/AttachmentUnitOfWork.cs` (service, request-response + bytes)

**Analog:** `Services/Evidence/ThreadUnitOfWork.cs`

**Imports** (lines 1-8):
```csharp
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;
```

**Outcome type beside the class** (lines 10-27). Mirror this as `AttachmentOutcomeKind { Written, Warnings, Failed, Skipped, NotRun }` and `AttachmentOutcome { Kind, Bytes, FileName, Violations, FlattenedText, NewDetails, InputTokens, OutputTokens, CostUsd, MediaCostUsd, Reason }`:
```csharp
public enum ThreadOutcomeKind { Written, Warnings, Failed, NotRun }

public class ThreadOutcome
{
    public ThreadOutcomeKind Kind { get; set; }
    public JournalRecord? Record { get; set; }
    public string Reason { get; set; } = string.Empty;
    ...
    // Spend of a NotRun thread, which has no journal line of its own; the runner adds it to RunEnded
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}
```

**Constructor injection** (lines 40-54). Add `IMediaGenerator? media`. It is null when media is unavailable, and then the outcome is Skipped (Pitfall 11):
```csharp
private readonly ILlmGateway _llm;
private readonly GenerationJournal _journal;
private readonly MailboxWriter _mailbox;
private readonly GenerationSnapshot _snapshot;
private readonly AIModelConfig? _storyModel;
```

**Core retry loop with best-attempt selection** (lines 72-146). Copy it for Office and invite kinds, but call `RenderValidator.CheckAttachment` instead of `Check`:
```csharp
for (var attempt = 1; attempt <= MaxAttempts; attempt++)
{
    var outcome = await PlanCallRunner.RunAsync<ThreadRenderer.ThreadRenderDto>(
        _llm, ThreadRenderer.BuildRequest(ctx, attempt, feedback), run).ConfigureAwait(false);
    inputTokens += outcome.InputTokens;
    outputTokens += outcome.OutputTokens;
    attempts = attempt;

    // Left pending: Continue writes it later (D-06)
    if (run.IsFatal) { ... return NotRun ... }

    if (outcome.Value == null)
    {
        if (best != null) break;
        var kind = outcome.Refused ? ThreadFailureKind.Refused
            : outcome.Error.StartsWith(TimeoutPrefix, StringComparison.Ordinal) ? ThreadFailureKind.Timeout : ThreadFailureKind.Provider;
        ...
        return await FailAsync(...);
    }

    var rendered = RenderRepair.Apply(outcome.Value, ctx);
    var violations = RenderValidator.Check(rendered, ctx);
    ...
    if (violations.Count == 0) break;
    if (attempt < MaxAttempts)
    {
        _snapshot.AddEvent(new GenerationEvent { Kind = GenerationEventKind.Retry, ... });
        feedback = violations;   // Same prompt plus the specific problems (D-13)
    }
}
```

**Never-throws error handling** (lines 229-243). Copy it exactly. An attachment failure must not escape into the thread's `Task.WhenAll` (RUN-02, D-26):
```csharp
catch (OperationCanceledException)
{
    throw;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    return await FailAsync(...);
}
catch (Exception ex)
{
    // A MimeKit ParseException (malformed address) or a composer bug must fail only this thread, never escape Task.WhenAll and abort the run (RUN-02)
    return await FailAsync(...);
}
```

**Journal-failure tolerance in FailAsync** (lines 268-275):
```csharp
try
{
    await _journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
}
catch (Exception)
{
    // Without a journal line the thread stays pending and Continue retries it; the run itself goes on (RUN-02)
}
```

**Cost** (line 296). Make it tier-aware (Pitfall 14). Add a separate `MediaCostUsd` for images and TTS:
```csharp
private decimal Cost(int inputTokens, int outputTokens) => _storyModel?.CalculateTotalCost(inputTokens, outputTokens) ?? 0m;
```

**Media path.** Image and voicemail calls do not go through `PlanCallRunner`. Use a dedicated `SemaphoreSlim(2)`. A null result maps to `AttachmentFailed` (refused or timeout). A `ClientResultException` with Status 401, 403 or 429 maps to "media stopped": set a run-level flag, return Skipped, and never call `run.SetFatal` (UI-SPEC Decision 2). Exception-classification analog: `OpenAIMediaClient.cs:57-66`.

---

### `Services/Evidence/AttachmentRenderer.cs` (service, per-kind strict DTOs)

**Analog:** `Services/Evidence/ThreadRenderer.cs`

**Imports** (lines 1-6):
```csharp
using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;
```

**Schema registry + request builder** (lines 14-30). Use one `BuildRequest` per kind. The tier comes from the profile: Story for relevant and decoy attachments, Volume for filler attachments:
```csharp
public const int MaxOutputTokens = 12_000;

internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(ThreadRenderDto) };

internal static LlmRequest BuildRequest(ThreadRenderContext ctx, int attempt, IReadOnlyList<RenderViolation>? feedback) => new()
{
    Tier = LlmTier.Story,
    OperationName = $"Generate Corpus - {ctx.Thread.Id}" + (attempt > 1 ? $" attempt {attempt}" : string.Empty),
    SystemPrompt = EvidencePrompts.SystemPrompt,
    UserPrompt = EvidencePrompts.BuildThreadPrompt(ctx, feedback),
    MaxOutputTokens = MaxOutputTokens
};
```

**Strict DTO style** (lines 32-89). Use `internal class` with `[JsonPropertyName]` + `[Description]`, no nullable members, collections initialized with `new()`, and enums as strings. Reuse `ThreadRenderer.LedgerDeltaDto` for `newDetails` on relevant attachments (D-18). Filler variants omit it (D-13):
```csharp
// ----- strict-schema DTOs: no nullable members, no recursion, no dates/people/labels decided by the model (D-01, D-02) -----

internal class ThreadRenderDto
{
    [JsonPropertyName("messages")]
    [Description("One entry per message in the request, by seq, in order")]
    public List<RenderedMessageDto> Messages { get; set; } = new();

    [JsonPropertyName("newDetails")]
    [Description("New specific details this thread introduces that later emails may refer back to; do not repeat earlier details")]
    public List<LedgerDeltaDto> NewDetails { get; set; } = new();
}
```
Register every new DTO in `SchemaTypes` and add a loop to `CaseSchemaTests.CaseSchemaTypes()` (see Tests below).

---

### `Services/Evidence/AttachmentPrompts.cs` and `Services/Evidence/FillerPrompts.cs` (prompt builders)

**Analog:** `Services/Evidence/EvidencePrompts.cs`

**Imports** (lines 1-8):
```csharp
using System.Globalization;
using System.Text;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Evidence;
```

**Byte-stable `const` system prompt** (line 17-18). Each new prompt class gets its own `const` with no interpolation. Keep the "Keep each person ... exactly as named in the guide, including characters ... from films, TV series" clause verbatim (D-03):
```csharp
// Byte-stable for prompt caching (Phase 7): never interpolate run data into this text (D-01, D-03)
public const string SystemPrompt = "You write the email messages of an e-discovery training dataset ...";
```

**Section order (a)-(j)** (lines 47-141). For filler, reuse (a) `CaseGuidePrompts.AppendNameRules(sb, ctx.Draft, includeScenario: false)`, (b) organizations, (c) `PersonLine` and (g)-(j). Replace (d)/(e)/(f) with "Ongoing work" from the side world, or with the decoy Angle and must-not-mention list:
```csharp
// (g) The thread
sb.AppendLine($"Thread id: {thread.Id}");
sb.AppendLine($"Subject: \"{thread.Subject.Trim()}\"");
sb.AppendLine($"Purpose: {thread.Purpose.Trim()}");
if (thread.IsPrivileged)
    sb.AppendLine(CounselText);
...
// (j) Retry feedback last: the prefix above stays cacheable (D-13)
if (feedback != null && feedback.Count > 0)
{
    sb.AppendLine();
    sb.AppendLine(FeedbackHeader);
    foreach (var violation in feedback.Where(v => v != null))
        sb.AppendLine(violation.Seq > 0 ? $"- msg {violation.Seq}: {violation.Text}" : $"- thread: {violation.Text}");
}
```

**Message line with length band** (lines 156-200). Filler must use the same `RenderAssignments.Spec(band)` `length: min-max words` token so distributions match (NOISE-05, D-07):
```csharp
var band = RenderAssignments.Spec(assignment?.Length ?? LengthBand.Medium);
...
sb.Append($" | {MessageTime.Local(message).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
sb.Append($" | length: {band.MinWords}-{band.MaxWords} words");
```

**Attachment knowledge gating (Pitfall 4).** Build value and fact lists from `ctx.Knowledge.MayMentionBySeq[parent.Seq]` (pattern at line 175) and `KnownValueIdsBySeq[parent.Seq]`. Do not use `knowledge.LastDate` (line 77).

`PersonLine` (lines 203-223), `KnownTo` (226-233), `Names` (235-236) and `OrderedParticipants` (239-253) are `private static`. Promote them to `internal static` instead of duplicating them.

---

### `Services/Evidence/FillerAssignments.cs` (pure apportionment)

**Analog:** `Services/Evidence/RenderAssignments.cs`

**Imports** (lines 1-6):
```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
```

**Reuse the shared bands and shares** (lines 17-30). Do not copy the numbers:
```csharp
// Shared with Phase 6 filler so relevant and non-relevant lengths match (NOISE-05, D-21)
public static readonly IReadOnlyList<LengthBandSpec> LengthBands = ...
public const double LabeledShare = 0.85;
public const double SubjectTagShare = 0.40;
```

**Salted stable ranking** (lines 78-82). Use different salts (for example `":filler-length"` and `":filler-privilege"`). Never re-apportion relevant and non-relevant together (anti-pattern):
```csharp
private static List<PlannedMessage> Ranked(IEnumerable<PlannedMessage> messages, string salt) =>
    messages
        .OrderBy(m => StableRank(m.Id + salt))
        .ThenBy(m => m.Id, StringComparer.Ordinal)
        .ToList();
```

**Length apportionment** (lines 128-152, `AssignLength`) and **privilege marking** (lines 157-176, `AssignPrivilege`). Copy both shapes over the non-relevant business population (DailyBusiness + Ancillary + Decoy). Exclude NewsletterSpamPersonal (Pitfall 3).

**Entry filter** (lines 48-51). Use `t.Relevance != ThreadRelevance.Relevant` instead of `WavePlanner.IsGeneratable`.

---

### `Services/Evidence/AttachmentChains.cs`, `AttachmentNaming.cs`, `InviteScheduler.cs`, `VoiceAssignments.cs` (pure utilities)

**Analog for naming:** `Services/Corpus/RelevantThreadFiller.cs:384-407`. Call it rather than copy it (it is `internal static`):
```csharp
internal static string FileNameFor(string title, AttachmentType kind)
{
    var stem = FileNameHelper.SanitizeForFileName(title).Trim('.', '_', ' ');
    if (stem.Length > MaxFileNameStem)
        stem = stem[..MaxFileNameStem].TrimEnd('.', '_', ' ');
    return stem.Length == 0 ? string.Empty : stem + Extension(kind);
}
```
`AttachmentNaming.FinalName` = `FileNameFor(stem + chainSuffix, kind)`. If that is empty, fall back to a deterministic code name that is not "Document N" (Pitfall 1). The same function feeds the EML part name, the journal `FileName` and the key overlay.

**Analog for deterministic choice:** `Services/Evidence/OrganizationThemes.cs:33-54` (hash, then modulo palette). Use it for `VoiceAssignments.For(personId)` over the 6 tts-1 voices and for `InviteScheduler` slot choice:
```csharp
// Deterministic, no LLM call, stable across runs and machines (UI-SPEC S5, U12)
var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
var index = (int)(BinaryPrimitives.ReadUInt64BigEndian(hash) % PaletteCount);
```
Alternatively, `RenderAssignments.StableRank(id + salt) % n`.

**Analog for pure plan projection (chains):** `RenderAssignments.Compute` (lines 46-76). Filter the plan, order by ordinal id, return `Dictionary<string, T>(StringComparer.Ordinal)`. Group by normalized title stem plus `Kind`, then order by parent `SentAt`, then `DocId`.

**Legacy reference only (D-16):** `Services/EmailGenerator.cs:18, 61, 1360-1413, 1578-1580` (`_documentChains`, `DocumentChainState`, version labels). Do not copy its `Random.Shared.Next(100) < 30` selection. It is non-deterministic.

---

### `Services/Evidence/DecoyScanner.cs` (pure check)

**Analog:** `Services/Evidence/RenderValidator.cs`

**Whole-word matcher to reuse** (lines 573-594, `internal static bool ContainsWord(string text, string term)`). For amounts, use `CanonicalValueScanner.Scan` / `AmountsEqual` (`Services/Case/CanonicalValueScanner.cs:40, 65`).

**Violation factory and rule isolation** (lines 118-129, 568):
```csharp
internal static void Run(List<RenderViolation> violations, Func<IEnumerable<RenderViolation>> rule)
{
    try
    {
        // Materialize inside the try so a lazy rule that fails part-way adds nothing
        var found = rule()?.Where(v => v != null).ToList() ?? new List<RenderViolation>();
        violations.AddRange(found);
    }
    catch (Exception)
    {
        // One broken rule must not hide the others or throw into the run; a skipped rule is not a violation (Pitfall 5)
    }
}
private static RenderViolation V(string code, int seq, string text) => new() { Code = code, Seq = seq, Text = text };
```
The full scan body is in RESEARCH "Decoy must-not-mention scan". The violation text must match the UI-SPEC: `mentions "{term}", which this email must not mention`.

---

### `Services/Evidence/RenderValidator.cs` (modify: add `CheckAttachment`)

**Self pattern** (lines 88-116). The new entry point uses the same `CheckContext` try, the same `Run(...)` calls and the same dedupe. Include only V2, V3, V4, V6 and V7:
```csharp
CheckContext c;
try { c = new CheckContext(rendered, ctx); }
catch (Exception)
{
    // Without a usable snapshot no rule can run; an unchecked thread is not a violation (Pitfall 5)
    return violations;
}
Run(violations, () => CheckPeople(c));
Run(violations, () => CheckCanonicalVariants(c));
Run(violations, () => CheckKnowledge(c));
Run(violations, () => CheckLedger(c));
Run(violations, () => CheckLedgerDates(c));
Run(violations, () => CheckLegalWording(c));
var seen = new HashSet<string>(StringComparer.Ordinal);
return violations.Where(v => seen.Add(v.Code + "|" + v.Seq.ToString(Inv) + "|" + v.Text)).ToList();
```
`CheckContext.Planned` (lines 631-636) is built from `ctx.Thread.Messages`. For attachments, pass a context whose thread holds only the parent message, or add a constructor overload. Extend `SeenText` (lines 706-716) so the parent body counts as seen text. For Excel, restrict V3 to labelled cells (Pitfall 5).

---

### `Services/Evidence/ThreadUnitOfWork.cs` (modify: profile-driven)

Hook points in the current file:
- Line 74-75: `PlanCallRunner.RunAsync<ThreadRenderer.ThreadRenderDto>(_llm, ThreadRenderer.BuildRequest(...))`. Use `profile.BuildRequest` and the profile's DTO type. The filler DTO has no `newDetails`.
- Line 111-112: `RenderRepair.Apply` + `RenderValidator.Check`. Use `profile.Check` (structure + V8 for filler; structure + V8 + `DecoyScanner` for decoys).
- Line 160: `EmailComposer.Compose(...)`. Run `AttachmentUnitOfWork` for each planned attachment on accepted messages here, before the writes.
- Lines 163-175: write loop. `ComposedEmail` now carries attachment parts.
- Lines 179-202: `ThreadDone` record. After the append, add one `Attachment{Done|Failed|Skipped}` record per planned attachment (RESEARCH Pattern 12 order).
- Line 197: `LedgerDelta = best.NewDetails`. This must be empty for profiles with `UsesLedger == false` (D-13).
- Line 296: `Cost` must resolve the profile tier's model (`_llm.ResolveModel(profile.Tier)`).

---

### `Services/Evidence/GenerationRunner.cs` (modify: Stage B filler, Stage C attachment repair)

**Wave loop to mirror for Stage B** (lines 170-245). Stage B is one "stage" with no ledger and no merge:
```csharp
var pending = wave.ThreadIds.Where(id => !state.IsDone(id) && threads.ContainsKey(id))
    .Distinct(StringComparer.Ordinal).ToList();
...
snapshot.SetStage(GenerationStage.Writing, wave.Index, waves.Count, wave.Start, wave.End, pending.Count);
...
var results = await Task.WhenAll(tasks).ConfigureAwait(false);

// Merged after the wave completes: same-wave threads never see each other's details (D-11)
foreach (var result in results)
{
    if (result.Kind is ThreadOutcomeKind.Written or ThreadOutcomeKind.Warnings && result.Record != null)
        deltas[result.Record.ThreadId] = result.Record.LedgerDelta.ToList();
}
```
For relevant waves, also append attachment deltas (from `AttachmentDone`) to `deltas[threadId]` *after* the thread's own deltas (Pattern 8). Add new `GenerationStage` values for side projects, non-relevant and missing attachments.

**Per-thread wrapper with throttled projection** (lines 296-305): reuse `RunThreadAsync` for filler.

**Fatal stop check** (lines 220-225): only the text provider sets `run.IsFatal`. A media stop is a separate flag.

**Shared run context** (lines 533-589, `RunContext`). Add `VolumeModel`, `MediaStopped` (under `_lock`), `SideWorld` and `FillerAssignments`. The `AddUnjournaledSpend` pattern (lines 574-582) also covers media spend.

**Run end never throws** (lines 247-285). Keep the structure. `FinishSnapshot` (lines 500-527) gains attachment counts.

---

### `Services/Evidence/MailboxWriter.cs` (modify: multipart/mixed + related)

**Current body build** (lines 87-99). Replace it with the `BodyBuilder` + `LinkedResources` + `Attachments` version (RESEARCH Pattern 5) and recurse boundaries:
```csharp
// OUT-02: ties every custodian copy back to the answer key row
message.Headers.Add(DocIdHeader, email.Planned.DocId);

// D-16: multipart/alternative, plain text first so clients prefer the HTML part
var body = new BodyBuilder { TextBody = email.PlainText, HtmlBody = email.Html }.ToMessageBody();

// Boundary derived from the message identity so rewriting the same email gives byte-identical files
if (body is Multipart multipart)
    multipart.Boundary = BoundaryFor(email);
message.Body = body;

// Long or non-ASCII lines get a transfer encoding, keeping every line within RFC 5322 limits
message.Prepare(EncodingConstraint.SevenBit);
```
**Boundary function to generalize** (lines 147-151). Add a `path` argument so nested multiparts get distinct, deterministic boundaries (RESEARCH "Deterministic boundary" example):
```csharp
private static string BoundaryFor(ComposedEmail email)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{email.Planned.DocId}|{email.MessageId}"));
    return "=-rd-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
}
```
**Path-safe atomic writes** (lines 121-145). Unchanged. `WriteAsync` already writes every custodian copy, so Stage C rewrites reuse it.

---

### `Services/Evidence/AttachmentHarvester.cs` (file-I/O read)

**Analog:** `MailboxWriter.ResolveUnderCase` (lines 107-115) for path safety. Only read journal-listed `Files` that pass `CorpusPlan.IsSafeEmlPath`. Core body: RESEARCH "Harvesting succeeded attachment parts" (`MimeMessage.Load`, `BodyParts.OfType<MimePart>()`, header `MailboxWriter.DocIdHeader`). Cap part size (V12). IO failures return an empty map, so the item is regenerated. Copy the filtered-catch shape from `GenerationReset.TryDelete` (lines 97-101):
```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
```

---

### `Services/Evidence/EmailComposer.cs` (modify) and voicemail composition

**`ComposedEmail` model** (lines 23-59). Add `List<ComposedAttachment> Attachments` (FileName, Bytes, MimeType, DocId, ContentId, IsInline, Title) and an internal `QuoteFragment` without `cid:` images (Pitfall 10). Follow the existing `[JsonIgnore] internal string HtmlFragment` (lines 56-58).

**Header sanitizing** (lines 77-78, 91). Reuse `ControlCharRegex()` for voicemail subjects and bulk sender names:
```csharp
[GeneratedRegex(@"\p{Cc}")]
private static partial Regex ControlCharRegex();
...
var root = ControlCharRegex().Replace(thread?.Subject ?? string.Empty, string.Empty).Trim();
```
**Deterministic id** (lines 106-111, `MessageIdFor`). Use the same SHA-256 shape for inline `ContentId` (`hash(DocId)@domain`) and ICS UID.

**Per-message composition** (lines 180-242). The voicemail override replaces `From` (voicemail-system address), `Subject` (`Voicemail from {Caller} ({m:ss})`), body (code template, UI-SPEC S5) and `Signature = string.Empty`. Everything else (MessageId, Date, To/Cc, theme) stays from the plan. The bulk-mail branch swaps `HtmlEmailFormatter.ComposeHtml` (line 233) for `BulkMailFormatter.Compose(...)` by `ExternalKind`.

**Theme per sender domain** (line 230): `var theme = OrganizationThemes.ForDomain(domain);`

---

### `Helpers/BulkMailFormatter.cs` (HTML formatter)

**Analog:** `Helpers/HtmlEmailFormatter.cs`

**Allowlist + encode-first** (lines 634-658):
```csharp
public static string ComposeFragment(string body, string signOff, string signature, QuotedPart? quoted,
    OrganizationTheme theme, bool privilegedBanner, bool allowTable = true)
{
    // Never touch [ThreadStatic] _currentTheme: composition runs on pool threads
    var t = theme ?? OrganizationTheme.Default;
    var primary = "#" + (Services.ThemeGenerator.SanitizeHexColor(t.PrimaryColor) ?? "2B579A");
    var secondary = "#" + (Services.ThemeGenerator.SanitizeHexColor(t.SecondaryColor) ?? "5B9BD5");
    var fontStack = GetFontStack(Services.ThemeGenerator.SanitizeFont(t.BodyFont) ?? "Segoe UI");
    var html = new System.Text.StringBuilder();
    ...
}
```
**Bold-run cap and encoding** (lines 787-797, 842): `ComposeInline` + `Encode(text) => System.Net.WebUtility.HtmlEncode(text)`. Make `GetFontStack`, `Encode` and `ComposeInline` `internal` and reuse them. Do not duplicate them.

**Disclaimer banner wrapper** (lines 853-860). Every bulk variant must still go through `WrapInTemplate` (Pattern 11: dropping the banner is a signal):
```csharp
internal static string WrapInTemplate(string content, OrganizationTheme? theme)
```
Declare it as a `public static partial class` with `[GeneratedRegex]` for any parsing (`**Heading**` lines), as in lines 10 and 615-620.

---

### `Helpers/HtmlEmailFormatter.cs` (modify: inline images)

Add an optional `IReadOnlyList<(string ContentId, string Title)>? inlineImages = null` parameter to `ComposeFragment`/`ComposeHtml` (lines 625-658). Emit `<img src="cid:{encoded}" alt="{Encode(title)}" style="max-width:100%">` after `AppendComposedBody` and before `AppendComposedSignature` (line 650-651). Quoted parents (lines 814-837) receive the image-free fragment.

---

### `Services/OfficeDocumentService.cs` (modify: overloads)

**Existing signatures to keep** (legacy + `DocumentRegressionTests`):
- line 14: `public byte[] CreateWordDocument(string title, string content, OrganizationTheme? theme = null)`
- line 121: `public byte[] CreateExcelDocument(string title, List<string> headers, List<List<string>> rows)`
- line 169: `public byte[] CreatePowerPointDocument(string title, List<(string slideTitle, string content)> slides, OrganizationTheme? theme = null)`

**Defects the new overloads fix:**
- Lines 41-46 and 95-99: `RunProperties` children are in the wrong schema order (`Bold, FontSize, Color, RunFonts`). The new code must emit `RunFonts, Bold, Color, FontSize`. The old path keeps its pinned 3 errors.
- Line 137: `Name = title.Length > 31 ? title[..31] : title`. Sanitize `: \ / ? * [ ]`, trim apostrophes, and never leave the name empty.
- Line 226: `CreateTitleSlide(..., title, "Generated Email Dataset", t)`. Pass the subtitle `{Org} | {Month yyyy}` (Pitfall 7).
- Lines 296-304 (`CreateCell`): add a numeric variant (`CellValues.Number` + `CellValue(decimal)`; see RESEARCH "Numeric Excel cell").
- Line 31: the header renders only when `t.OrganizationName` is set. Fix this in `OrganizationThemes`/caller (Pitfall 8).
- Set `PackageProperties` Creator/LastModifiedBy/Created/Modified. Never write "ReelDiscovery".

Disposal pattern to keep (lines 18-19, 116-118): `using var stream = new MemoryStream(); using (var doc = ...Create(stream, ..., true)) { ... } return stream.ToArray();`

---

### `Services/CalendarService.cs` (modify: deterministic overload)

**Current method to leave in place for legacy** (lines 10-50). The new overload takes `DateTimeOffset start/end`, `string uid`, `DateTimeOffset dtStamp`, and attendees from the plan. Fix these lines:
- line 20 `Guid.NewGuid()` and line 21 `DateTime.UtcNow`: non-deterministic.
- line 37/41 `CN={EscapeIcsText(name)}`: must DQUOTE-quote and strip `\p{Cc}`. The `mailto:` value is unsanitized.
- lines 52-55 `dt.ToUniversalTime()` on Unspecified kind: use `DateTimeOffset.UtcDateTime`.
- No 75-octet folding. Use CRLF line endings (`AppendLine` gives `\r\n` on Windows, but be explicit).
`EscapeIcsText` (lines 57-68) is the base for TEXT escaping.

---

### `Services/Evidence/IMediaGenerator.cs` (seam)

**Wrapped client:** `Services/Ai/OpenAIMediaClient.cs`. Signatures to mirror (lines 36, 77-81):
```csharp
public async Task<byte[]?> GenerateImageAsync(string prompt, string? operationName = null, CancellationToken ct = default)
public async Task<byte[]?> GenerateSpeechAsync(string text, string voice = "alloy", string? operationName = null, CancellationToken ct = default)
```
Null on 400 or timeout (lines 57-66, 107-116). 401/403/429 propagate as `ClientResultException`. The adapter or the caller classifies them as "media stopped". Creation follows `Models/WizardState.cs:95-96`:
```csharp
public OpenAIMediaClient? CreateMediaClient() =>
    IsMediaAvailable ? new OpenAIMediaClient(OpenAIApiKey, Config.ImageModel, Config.ImageQuality) : null;
```

---

### `Models/Evidence/JournalRecord.cs` + `Services/Evidence/GenerationJournal.cs` (modify)

**Enum to extend** (`JournalRecord.cs:6`). Append new values at the end only. String serialization makes older builds skip the line:
```csharp
public enum JournalRecordType { RunStarted, ThreadDone, ThreadFailed, RunEnded }
```
**Nested payload pattern** (`JournalRecord.cs:15-22`, `JournalMessage`). Copy it for `JournalAttachment` or add flat fields under a `// AttachmentDone / AttachmentFailed / AttachmentSkipped` comment block like lines 33-54.

**Normalize** (lines 59-103). Every new string gets `??= string.Empty` plus a length cap. Every list goes through `CaseGuide.CleanStrings` / `CleanList`. New `Files` entries go through `CorpusPlan.IsSafeEmlPath` (lines 66-68). Clamp numbers with `Math.Max(0, ...)`.

**Replay switch** (`GenerationJournal.cs:149-167`). Add `LastByAttachment[docId]` and keep summing spend for every record:
```csharp
case JournalRecordType.ThreadDone:
case JournalRecordType.ThreadFailed:
    // Later lines win: a retried thread's newest outcome replaces the earlier one, but its spend stays counted
    if (threadIds.Contains(record.ThreadId))
    {
        replay.LastByThread[record.ThreadId] = record;
        replay.SpentCostUsd += Math.Max(0m, record.CostUsd);
    }
    break;
```
Also add `LastSideWorld`. Clear the new dictionaries in the catch block (lines 173-181).

---

### `Services/Evidence/GenerationRunState.cs` (modify: projection)

- `From` (lines 45-113): add a non-relevant loop alongside `RelevantThreads` with separate counters, plus an attachment loop over `replay.LastByAttachment` with status per DocId. Union `AttachmentDone` deltas into `DeltasByThread` (line 74) for relevant threads.
- `BuildOverlay` (lines 119-147): cover all threads. Overlay attachment rows (Status, `FileName` from the journal, voicemail `From`/`Subject`). The status mapping switch at lines 125-131 gains `StatusSkipped`. Non-relevant subjects use `FillerAssignments` markings.
- `ToCounts` (lines 149-166) and `EstimateRemaining` (lines 172-180): add Volume/Story/media inputs. Keep the formula shape `remaining * model.CalculateTotalCost(...) * GenerationCostEstimator.QaOverhead`.
- `GenerationStateApplier.Apply` (lines 193-222): copy the new counts into `GenerationState`. Clear on Start Over (lines 227-235).

---

### `Services/Evidence/GenerationReset.cs` (modify)

Lines 51-57 filter to `t.Relevance == ThreadRelevance.Relevant`. Drop the filter so every planned thread's `CopyPathsFor` is a candidate (Pitfall 13). If the side world is a file, delete it beside the story file (lines 80-83) and keep the constant-name rule (T-05-33).

---

### `Services/Evidence/LedgerMerger.cs` (modify)

Line 18 `MaxEntriesPerThread = 12`, enforced at lines 90-91. Add a separate attachment allowance (for example `MaxAttachmentEntriesPerThread = 6`) applied to deltas tagged as attachment-sourced. `EntriesFor` maps by `delta.Seq` (lines 49-50), so attachment deltas must carry `Seq = parent.Seq`.

---

### `Services/Corpus/AnswerKeyBuilder.cs` (modify)

- Overlay is applied only to email rows (lines 82-88). `AttachmentRow` (lines 136-167) hard-codes `Status = StatusPlanned` and uses `attachment.FileName`. Apply `overlay.ByDocId[attachment.DocId]` there as well: Status, FileName, and `Skipped: {reason}` in the description/rationale.
- `AnswerKeyOverlayEntry` (lines 288-292): add `FileName`, `From`, `Subject` (voicemail), `Note`.
- `StatusSkipped` already exists (line 17).

---

### `Services/Corpus/GenerationCostEstimator.cs` (modify)

`Estimate(CorpusPlan plan, AIModelConfig? storyModel)` (lines 42-80) prices everything on Story. Add an overload `Estimate(plan, storyModel, volumeModel, mediaPrices, mediaAvailable)` that splits relevant + decoy (Story) from DailyBusiness/Ancillary/Bulk (Volume), and documents vs small attachments per parent tier, plus `images * pricePerImage + ttsChars * pricePer1M / 1_000_000`. Keep the existing constants (lines 23-37). Extend `CostEstimate` (lines 10-17) with `VolumeCost`, `MediaCost` and `VolumeModelName`.

---

### `Models/TokenUsageTracker.cs` (modify)

Add `RecordMediaUsage(string operation, decimal cost)` with the same lock shape as `RecordUsage` (lines 23-44): increment `TotalCost` under `_lock` and append a `TokenUsageEntry` with zero tokens.

---

### `UserControls/GenerationUiText.cs` (modify: all copy)

- `GenerationTileKind` (lines 9-18) becomes `Relevant, NonRelevant, Attachments, Emails, Warnings, Failed, Cost`.
- `Tile` (lines 178-248): keep the `threadsVariant` 0/1/2 DPI fallback shape (lines 188-194) for tiles 1-3. Keep `Tone = c.X > 0 ? TileTone.Warning : TileTone.Normal` (lines 228, 237).
- `ActivityText` (lines 269-307): a switch expression of `(text, tone)` tuples per `GenerationEventKind`. Add the new kinds from UI-SPEC S1 with the same `Plural(...)`, `Colon(first)` and `N(...)` helpers.
- `ProgressText` (lines 311-325) and `OutcomeText` (lines 327-378): add the stage and clause variants. The dropped-zero-clause pattern is at lines 343-351.
- `ScopeNotice` (lines 164-171), `BannerBody` (lines 452-456), `StateCard` (lines 413-445), `CG1-CG3` (lines 460-470), `StartedOverText` (lines 386-387) and `MailboxesRow` (lines 474+): rewrite to the UI-SPEC templates.

---

### `UserControls/StepGenerateCorpus.cs` (+ `.Run.cs`)

**Card + grid construction** (lines 500-519). Rename it to Stages and rename the `Threads` column to `Done`. Keep the row-index comments:
```csharp
layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header (caption | hint)
layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Waves grid
layout.Controls.Add(NewCardHeader(inset, "Waves", WavesHint), 0, 0);

_gridWaves = NewGrid();
GridStyle.AddDotColumn(_gridWaves, this);
AddRightColumn(_gridWaves, "Wave", "Wave", 56);
AddFillColumn(_gridWaves, "Dates", "Dates", 120);
AddRightColumn(_gridWaves, "Threads", "Threads", 80);
AddRightColumn(_gridWaves, "Warnings", "Warnings", 80);
AddRightColumn(_gridWaves, "Failed", "Failed", 64);
```
**In-place row update** (lines 711-742, `ApplyWaves`). Append the "Non-relevant mail" and "Attachments for written threads" rows after the wave rows using the same `DotTag` / `SetCell` / `ToolTipText` code. The tile kinds array is at lines 38-44. Test accessors are at lines 1054-1060 (`internal DataGridView WavesGrid`, `internal IReadOnlyList<StatTile> Tiles`).

---

### `UserControls/ModelConfigurationDialog.cs` (modify: media prices row)

**Layout to extend** (lines 48-57). Set `RowCount` to 4 and insert an AutoSize row before the bottom buttons, with row-index comments:
```csharp
var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
```
**FlowLayoutPanel pattern** (lines 187-192, `bottomPanel`). The media row uses `FlowDirection.LeftToRight` and `WrapContents = true`. Move `mainLayout.Controls.Add(bottomPanel, 0, 2)` (line 203) to row 3. Header text is at line 62. Reset Defaults handler is at line 268 (also reset the two `NumericUpDown`s).

---

## Shared Patterns

### Never-throws unit of work + journal = truth
**Source:** `Services/Evidence/ThreadUnitOfWork.cs:60-291`
**Apply to:** `AttachmentUnitOfWork`, the filler path, and Stage C repair.
Order: render, check, retry, compose, write all copies with `CancellationToken.None` (a thread that passed checks always completes, lines 162-175), then journal, then snapshot. Failures journal with spend. A journal append failure is swallowed with a comment.

### Slot-limited, fatal-aware LLM calls
**Source:** `Services/Corpus/PlanCallRunner.cs:8-155` (`PlanCallRun` + `PlanCallRunner.RunAsync<T>(llm, request, run)`)
**Apply to:** filler threads, attachment text calls and the side-world call. **Not** media.

### Deterministic identity for anything in an EML
**Source:** `RenderAssignments.StableRank` (`RenderAssignments.cs:37-41`), `EmailComposer.MessageIdFor` (lines 106-111), `MailboxWriter.BoundaryFor` (lines 147-151), `OrganizationThemes.ForDomain` (lines 39-41)
**Apply to:** Content-IDs, nested boundaries, ICS UID/DTSTAMP, voice choice, invite slots, chain order, filler assignments and side-world fallback names.
```csharp
// Never string.GetHashCode(): randomized per process, would change on every Continue
return BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(id ?? string.Empty)));
```

### Untrusted model text: encode and strip control characters
**Source:** `EmailComposer.ControlCharRegex` (lines 77-78), `HtmlEmailFormatter.Encode`/`ComposeInline` (lines 787-797, 842), `RelevantThreadFiller.FileNameFor` (lines 388-394), `MailboxWriter.ResolveUnderCase` (lines 107-115)
**Apply to:** `BulkMailFormatter`, `CalendarService` overload, `AttachmentNaming`, voicemail subject/From, `AttachmentHarvester`, and Office builders (sheet names).

### Strict-schema DTOs + schema test registration
**Source:** `ThreadRenderer.cs:17, 32-89`; `ReelDiscovery.Tests/Case/CaseSchemaTests.cs:11-25`
**Apply to:** every new DTO (Word/Excel/PowerPoint/Invite/filler/side-world). Add `foreach (var type in AttachmentRenderer.SchemaTypes) data.Add(nameof(AttachmentRenderer), type);` and the same for filler/side-world.

### Persisted JSON repair
**Source:** `JournalRecord.Normalize` (`JournalRecord.cs:59-103`), `GenerationState.Normalize` (`Models/Evidence/GenerationState.cs:52`)
**Apply to:** new journal fields, the side-world record, and new case.json counts (defaults 0 so old files load).

### Snapshot-only progress (UI never touched from workers)
**Source:** `ThreadUnitOfWork.cs:204-219` (`_snapshot.RecordThread`, `_snapshot.AddEvent(new GenerationEvent {...})`); `GenerationRunner.RunContext` lock pattern (lines 533-589)
**Apply to:** attachment events (retry, warnings, failed, skipped, media stopped), stage changes and counts.

### Pure UI copy in `GenerationUiText`
**Source:** `UserControls/GenerationUiText.cs:147-155` (class doc: pure, unit-tested, `UseMnemonic = false` on labels showing model text)
**Apply to:** every new string in S1-S4. The step stays layout-only.

---

## Tests: patterns to copy

| New/extended test | Copy from | Key excerpt |
|---|---|---|
| `AttachmentUnitOfWorkTests` | `Evidence/ThreadUnitOfWorkTests.cs:15-106` | `IDisposable` fixture: `EvidenceTestData.TempCaseFolder()`, `EvidenceTestData.ApprovedCase()`, `CorpusTestData.CreateGateway()`, `new GenerationJournal(_folder)`, `_snapshot.Reset(...)`; `Mutating(...)` wrapper over `EvidenceTestData.RenderResponder` (lines 85-96); `AssertFailedCost` (101-106) |
| `FakeMediaGenerator` | `Fakes/FakeLlmProvider.cs` | `sealed`, `_lock`, `Requests` list, `Respond` callback, `ThrowOnSend` (lines 29, 41, 68-69) for scripted bytes / null / `ClientResultException(401)` |
| `CalendarServiceTests`, `OfficeDocumentServiceTests` | `Documents/DocumentRegressionTests.cs:1-40` | `WordprocessingDocument.Open(new MemoryStream(bytes), false)` + `new OpenXmlValidator().Validate(doc)`. New overloads assert **0** errors |
| `MailboxWriterTests` (extend) | `Evidence/MailboxWriterTests.cs:9-61` | Temp folder fixture, `Composed()` builder; add attachments + inline image; assert byte-identical rewrite |
| `CaseSchemaTests` | `Case/CaseSchemaTests.cs:11-37` | add `SchemaTypes` loops |
| Render tests | `Render/GenerateCorpusRenderTests.cs:17-19, 169-218` | `[Trait("Category","Render")]`, `[Collection(RenderCollection.Name)]`, `RenderHarness.RunSta`, sizes `Large` + `WizardMin`, `Assert.Empty(result.Clipping)`, no grid HScroll, `step.LoadForRender(...)` |
| `GenerationUiTextTests` | `Evidence/GenerationUiTextTests.cs` | n = 0, 1, 2 and 1,000 plus dropped-clause cases |

---

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| `Services/Evidence/AttachmentHarvester.cs` | utility | file-I/O read of MIME | No code reads EMLs back today. Use the RESEARCH "Harvesting" example (`MimeMessage.Load`, `BodyParts.OfType<MimePart>()`, `Content.DecodeTo`) with `ResolveUnderCase` path safety |
| `Services/Evidence/SideWorldBuilder.cs` (validation part) | service | request-response + pure validation | No existing "invent names that avoid case terms" logic. Combine the `ThreadRenderer` DTO/request shape with `RenderValidator.ContainsWord` checks against fact titles, canonical values and org/project names. Use deterministic code templates as the fallback (RESEARCH Open Question 6) |

## Metadata

**Analog search scope:** `Services/Evidence`, `Services/Corpus`, `Services/Ai`, `Services/` (Office, Calendar, legacy EmailGenerator), `Helpers/`, `Models/Evidence`, `Models/Corpus`, `Models/`, `UserControls/`, `ReelDiscovery.Tests/{Evidence,Fakes,Render,Case,Documents}`
**Files scanned:** about 45 (24 read in full or by targeted range)
**Pattern extraction date:** 2026-10-04
