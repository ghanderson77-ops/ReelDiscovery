# Phase 10: Answer-Key & Batch Safety - Pattern Map

**Mapped:** 2026-10-12
**Files analyzed:** 27 (6 new production, 2 new tests, 19 modified)
**Analogs found:** 26 / 27 (CSV read-back parser has no in-repo analog)

All line numbers below are from the current `main` (commit 67764cb).

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Services/Corpus/AnswerKeyFiles.cs` (NEW funnel) | service (static) | file-I/O, transform | `Services/Evidence/GenerationOutputs.cs` `WriteAnswerKeyAsync` (l.31-67) | exact |
| `Services/Corpus/AnswerKeyReader.cs` (NEW) | utility (static, pure parse) | file-I/O read, transform | XLSX: `ReelDiscovery.Tests/Corpus/AnswerKeyXlsxWriterTests.cs` `Book`/`Text` (l.72, 99-100); CSV: none | partial (XLSX) / none (CSV) |
| `Services/Corpus/AnswerKeyRelabelStore.cs` (NEW sidecar) | store | file-I/O (JSON atomic) | `Services/Qa/QaReportStore.cs` | exact |
| `Models/Corpus/AnswerKeyRelabel*.cs` (NEW sidecar DTOs, may live in store file) | model | — | `Models/Corpus/AnswerKeyRow.cs`, `AnswerKeyOverlayEntry` (`AnswerKeyBuilder.cs:306-328`) | role-match |
| `Models/AttachmentType.cs` (NEW) | model (enum) | — | `Models/Attachment.cs` l.1-11 (moved verbatim) | exact |
| `Models/Attachment.cs` (DELETE) | — | — | — | — |
| `Services/Corpus/CorpusApproval.cs` | service | file-I/O | itself (`BuildFiles` l.35-47, `WriteFilesAsync` l.52-92, `ApproveAsync` l.130) | self |
| `Services/Evidence/GenerationOutputs.cs` | service | file-I/O | itself (becomes thin wrapper over funnel) | self |
| `Services/Evidence/GenerationResult*` / `GenerationFileResult` (add `Notice`/`BackupFileName`) | model | — | `GenerationFileResult` (`GenerationOutputs.cs:13-20`) | self |
| `Services/Evidence/ThreadInvalidation.cs` (KeyNotice) | service | file-I/O | itself l.94-107 | self |
| `Services/Evidence/GenerationRunner.cs` | service (orchestrator) | event-driven/batch | itself l.255-284, 531-538, 1119-1131 | self |
| `Services/Ai/BatchLedger.cs` (provider-filtered count) | store | CRUD read | `PendingBatchCount` l.274-280, `PendingBatchCountAllPlans` l.286-293 | exact |
| `Services/Ai/BatchingLlmProvider.cs` — `ResolveOtherProviderPendingAsync` (static) | service | request-response/batch | `CancelPendingAsync` l.398-416 + `CollectAsync` l.1207-1264 | exact |
| `Services/Ai/BatchingLlmProvider.cs` — `PriceUnusedResults` (static) | service | transform | `PriceOrphans` l.276-328 + `Recover` l.180-214 | exact |
| `Services/Ai/BatchingLlmProvider.cs` — `DrainStaleStagesAsync` (instance) | service | event-driven | `CancelPendingStagesAsync` l.531-575, `DetachAsync` l.786-800, `UntrackLocked` l.971-979 | exact |
| `Models/WizardState.cs` (extract price lookup) | model/factory | — | `CreateRunGateway` l.93-117 | self |
| `UserControls/StepGenerateCorpus.Run.cs` | component (wizard step) | request-response | itself `RunCoreAsync` l.412-450, Start Over l.940-1000, `CancelPendingBatchesAsync` l.1030-1050, `ReadEntryAsync` l.1188-1207 | self |
| `UserControls/StepGenerateCorpus.cs` (banner count l.1309) | component | — | itself | self |
| `UserControls/StepReviewPlan.Run.cs` (approval overlay, l.529) | component | request-response | `ThreadInvalidation.cs:97-98` (overlay build) | role-match |
| `UserControls/GenerationUiText.cs` (`BannerBody`, `CG2StartOver`, refusal text) | utility (pure copy) | transform | itself l.934-943, 984-988 | self |
| `UserControls/QaUiText.cs` (`CQ1RegenerateThread`) | utility (pure copy) | transform | itself l.335-344 | self |
| `UserControls/CorpusUiText.cs` (`ReplanBody`, `SettingsNotice`) | utility (pure copy) | transform | itself l.210-219, 315-324 | self |
| `Forms/ReplanDialog.cs`, `Forms/QaFindingsDialog.cs:407`, `UserControls/StepCorpusSettings.cs:1159`, `Forms/WizardForm.cs:609` | component (callers) | — | existing call sites | self |
| `Models/Evidence/GenerationProgress.cs` (`GenerationEventKind` append) | model (enum) | event-driven | l.36-37 append-only block | exact |
| `ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs` (NEW) | test | — | `ReelDiscovery.Tests/Corpus/AnswerKeyXlsxWriterTests.cs` | role-match |
| `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs` (NEW) | test | — | `ReelDiscovery.Tests/Corpus/CorpusApprovalTests.cs` (temp-folder fixture) | role-match |
| Modified tests: `PlatformTests.cs`, `CorpusApprovalTests.cs`, `BatchingLlmProviderTests.cs:866`, `BatchParityTests.cs:649`, `GenerateCorpusStepTests.cs`, `*UiTextTests.cs`, `BatchLedgerTests` | test | — | themselves | self |

---

## Pattern Assignments

### `Services/Corpus/AnswerKeyFiles.cs` (service, file-I/O funnel) — NEW

**Analog:** `Services/Evidence/GenerationOutputs.cs` (move this loop here; keep per-file independence)

**Imports pattern** (`GenerationOutputs.cs` l.1-8, minus Evidence; Corpus must NOT reference `Services.Evidence` — research D-12 row):
```csharp
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;
```
Note `GenerationFileResult` lives in `Services.Evidence`; the funnel needs its own result type in Corpus (e.g. `AnswerKeyWriteResult` with `Files`, `FailedFileName`, `Error`, `FileOpenElsewhere`, `Notice`/`BackupFileName`) that `GenerationOutputs` maps to `GenerationFileResult`.

**Core write loop to copy** (`GenerationOutputs.cs` l.34-66):
```csharp
writer ??= CaseStore.WriteAtomicAsync;
var result = new GenerationFileResult();

// CSV fields go through AnswerKeyCsvWriter.Field, which neutralizes formula prefixes (T-05-32)
var rows = AnswerKeyBuilder.Build(plan, guide, overlay);
var files = new List<(CorpusFileKind Kind, string FileName, Func<byte[]> Bytes)>
{
    (CorpusFileKind.AnswerKeyCsv, AnswerKeyCsvWriter.FileName, () => AnswerKeyCsvWriter.Build(rows)),
    (CorpusFileKind.AnswerKeyXlsx, AnswerKeyXlsxWriter.FileName, () => AnswerKeyXlsxWriter.Build(rows, guide))
};

foreach (var (kind, fileName, bytes) in files)
{
    ct.ThrowIfCancellationRequested();
    // Paths = case folder + constant names only; WrittenFiles from case.json never build paths (T-04-68)
    var path = Path.Combine(caseFolder, fileName);
    try
    {
        await writer(path, bytes(), ct).ConfigureAwait(false);
        result.Files.Add(Stamp(kind, fileName, plan.PlanId));
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // Carry on with the next file: a locked CSV must not block the XLSX (Pitfall 8)
        RecordFailure(result, fileName, path, ex);
    }
}
```
Changes for Phase 10: materialize the bytes once into a local before `writer(...)` so the SHA-256 snapshot is taken from exactly the bytes written (Pitfall 1); only update `snap[file]` when the writer returned. Relabels are applied after `AnswerKeyBuilder.Build` (research "Applying relabels after projection"), never inside the builder (D-11).

**Stamp / first-failure helpers** (`GenerationOutputs.cs` l.103-120) — copy verbatim:
```csharp
private static WrittenCorpusFile Stamp(CorpusFileKind kind, string fileName, Guid planId) => new()
{ Kind = kind, FileName = fileName, PlanId = planId, WrittenUtc = DateTime.UtcNow };

// The first failure is the one reported; later failures only keep their file out of result.Files
private static void RecordFailure(GenerationFileResult result, string fileName, string path, Exception ex)
{
    if (result.FailedFileName.Length > 0) return;
    result.FailedFileName = fileName;
    result.Error = ex.Message;
    result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
}
```

**Per-case-folder gate** (Pitfall 3) — analog `Services/Discovery/DiscoveryGenerator.cs` l.103, 111-112:
```csharp
private static readonly ConcurrentDictionary<string, SemaphoreSlim> SidecarLocks = new(StringComparer.OrdinalIgnoreCase);

private static SemaphoreSlim SidecarLock(string folder) =>
    SidecarLocks.GetOrAdd(Path.TrimEndingDirectorySeparator(folder.Trim()), _ => new SemaphoreSlim(1, 1));
```
Research recommends keying by `Path.GetFullPath(Path.TrimEndingDirectorySeparator(folder))`. Hold across harvest -> CSV write -> XLSX write -> sidecar save; `try/finally Release()` like `QaReportStore.AppendLinesAsync` l.147-168.

**D-10 backup write:** use the `FileWriter` delegate (`CorpusApproval.cs:8`, default `CaseStore.WriteAtomicAsync`) with the bytes already read; name = constant stem + invariant timestamp (`answer-key (unreadable yyyyMMdd-HHmmss).csv`). Report via a new `Notice` field, NOT `KeyError` (Pitfall 9).

---

### `Services/Corpus/AnswerKeyRelabelStore.cs` (store, JSON sidecar) — NEW

**Analog:** `Services/Qa/QaReportStore.cs` (240 lines; copy structure)

**Imports + constants + ctor** (l.1-36):
```csharp
using System.Text.Json;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Qa;   // -> ReelDiscovery.Services.Corpus

public class QaReportStore
{
    public const string ScansFileName = "qa-scans.json";        // -> RelabelsFileName = "answer-key-relabels.json"
    private const int MaxMoveAttempts = 5;
    private readonly string _caseFolder;

    public QaReportStore(string caseFolder) { _caseFolder = caseFolder ?? string.Empty; }

    // Case folder + constant name only, never a path from data (T-07-40)
    public string ScansPath => Path.Combine(_caseFolder, ScansFileName);
```

**Tolerant read** (l.205-219) — missing/damaged sidecar reads as empty, never throws:
```csharp
private static T? ReadJson<T>(string path) where T : class
{
    try
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), CaseDraft.JsonOptions);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
    {
        // A report is a convenience view; a missing or damaged one shows Not run
        return null;
    }
}
```

**Atomic write** (l.171-203): temp file `path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp"`, `FileMode.CreateNew` + `FileOptions.WriteThrough`, `Flush(flushToDisk: true)`, then `File.Move(tmp, path, overwrite: true)` retried up to `MaxMoveAttempts` with `Thread.Sleep(100)` on `IOException/UnauthorizedAccessException`; on failure `TryDeleteTemp(tmp); throw;`. Copy `TryDeleteTemp` (l.221-232) verbatim. The funnel catches the save failure (sidecar failure tolerated, research diagram).

**DTO style:** mutable classes with auto-properties, `= new()` defaults, `Dictionary<..>(StringComparer.Ordinal)` for DocID keys (as `AnswerKeyOverlay.ByDocId`, `AnswerKeyBuilder.cs:327`). Serialize with `CaseDraft.JsonOptions`. No records.

---

### `Services/Corpus/AnswerKeyReader.cs` (utility, parse) — NEW

**XLSX analog:** `ReelDiscovery.Tests/Corpus/AnswerKeyXlsxWriterTests.cs`
```csharp
// l.72: open from a MemoryStream, read-only
public Book(byte[] bytes) => Doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);

// l.99-100: inline-only today; production reader must also handle SharedString and str (research Pattern 3)
private static string Text(Cell cell) =>
    cell.DataType?.Value == CellValues.InlineString ? cell.InlineString!.Text!.Text : cell.CellValue?.Text ?? string.Empty;
```
Imports to mirror `AnswerKeyXlsxWriter.cs` l.1-4 (`System.Globalization`, `DocumentFormat.OpenXml`, `DocumentFormat.OpenXml.Packaging`, `DocumentFormat.OpenXml.Spreadsheet`). Sheet name from `AnswerKeyXlsxWriter.SheetNames[0]` ("Answer Key", l.21); headers from `AnswerKeyColumns.Headers` (`Models/Corpus/AnswerKeyRow.cs` l.45-70: "DocID" index 0, "Relevance" index 14, "Rationale" index 21) — resolve by header name, not index. Column letter helper exists: `AnswerKeyXlsxWriter.ColumnName(int)` (l.120).

**Shared-read** (research Pattern 2): `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)` -> copy to `MemoryStream` -> bytes reused for SHA-256, parse and D-10 backup. Never `SpreadsheetDocument.Open(path, false)` / `File.ReadAllBytes`.

**CSV:** no in-repo CSV reader exists. Must invert `AnswerKeyCsvWriter` (`Services/Corpus/AnswerKeyCsvWriter.cs`):
```csharp
// l.15: guard chars; reader strips ONE leading ' only when followed by one of these
private static readonly char[] FormulaLeads = { '=', '+', '-', '@', '\t', '\r', '\n', '＝', '＋', '－', '＠' };
// l.45-46: UTF-8 BOM + body, CRLF lines, RFC 4180 quoting with doubled quotes
```
`FormulaLeads` is private — expose it as `internal`/`public static readonly` (or a `public static bool IsGuarded(string)` helper) rather than duplicating. Encoding/delimiter rules per research Pattern 4. Normalize relevance to `AnswerKeyBuilder.RelevantLabel` / `NotRelevantLabel` (`AnswerKeyBuilder.cs:20-21`).

---

### `Services/Corpus/CorpusApproval.cs` (service, modify)

**Current BuildFiles** (l.35-47) — keep public signature; add optional trailing `AnswerKeyOverlay? overlay = null, IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels = null`:
```csharp
public static List<(CorpusFileKind Kind, string FileName, byte[] Bytes)> BuildFiles(CaseDraft draft, CorpusPlan plan,
    string folderName)
{
    var guide = draft.Guide ?? new CaseGuide();
    var rows = AnswerKeyBuilder.Build(plan, guide);
    ...
```
**WriteFilesAsync** (l.52-92) keeps stop-at-first-failure semantics: route the two key files through the funnel, then write the protocol only if both succeeded; failure branch shape (l.72-79):
```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    result.Succeeded = false;
    result.FailedFileName = fileName;
    result.Error = ex.Message;
    result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
    return result;
}
```
`ApproveAsync(string caseFolder, CaseDraft draft, CorpusPlan plan, FileWriter? writer = null, CancellationToken ct = default)` (l.130): add overlay as an overload or before `writer` (ct stays last); update named-arg test call sites.

---

### `Services/Evidence/GenerationOutputs.cs` (modify) and `ThreadInvalidation.cs` (modify)

`WriteAnswerKeyAsync` (l.31-67) becomes a wrapper: `AnswerKeyFiles.WriteAsync(...)` -> map to `GenerationFileResult`. Signature unchanged so writers 1, 2, 4, 5 compile untouched (`GenerationRunner.cs:1041-1042`, `ThreadInvalidation.cs:98`, `StepGenerateCorpus.Run.cs:1204`, `:1097`).

**Overlay build to reuse for D-12** (`ThreadInvalidation.cs` l.97-101):
```csharp
var state = GenerationRunState.From(plan, GenerationJournal.Replay(caseFolder, plan));
var written = await GenerationOutputs.WriteAnswerKeyAsync(caseFolder, guide, plan, state.BuildFullOverlay(plan, guide))
    .ConfigureAwait(false);
if (!written.Succeeded)
    result.KeyError = $"{written.FailedFileName}: {written.Error}";
```
Add `result.KeyNotice` for the D-10 backup notice beside `KeyError` (research Pitfall 9).

---

### `Services/Evidence/GenerationRunner.cs` (modify, D-17)

**Insert drain after RunStarted** (l.274-284), inside the `try`, only when `batch != null`:
```csharp
await journal.AppendAsync(new JournalRecord
{
    Type = JournalRecordType.RunStarted,
    Utc = DateTime.UtcNow,
    PlanId = plan.PlanId,
    Provider = _llm.Provider.ToString()
}, CancellationToken.None).ConfigureAwait(false);
// NEW: if (batch != null) await batch.DrainStaleStagesAsync(l => l.StartsWith("wave ", StringComparison.Ordinal), ct)...
```
Stage-label predicate copy from `CloseStaleWaveBatchesAsync` (l.1123): `label.StartsWith("wave ", StringComparison.Ordinal)`; label helper `WaveStageLabel(int)` l.1117.

**Delete** l.536 call and l.1119-1131 definition; keep `orphans = AddOrphanSpend(ctx, batch);` (l.537). After drain update `snapshot.SetPendingProviderBatches(batch.PendingBatchCount)` as the deleted method did (l.1125).

**Activity events** (D-13 logging): `snapshot.AddEvent(new GenerationEvent { Kind = ..., LocalTime = DateTime.Now, ... })` pattern at l.247-253; new kinds appended at the end of `GenerationEventKind` (`Models/Evidence/GenerationProgress.cs` l.36-37, comment "append-only: earlier members keep their values").

---

### `Services/Ai/BatchLedger.cs` (modify, D-15)

Copy `PendingBatchCount` (l.274-280) into a provider overload:
```csharp
/// <summary>
/// Number of this plan's batches that were submitted but neither collected nor cancelled. Never throws; an unreadable
/// ledger counts as 0 (Start Over keeps it, CR-A05).
/// </summary>
public static int PendingBatchCount(string caseFolder, Guid planId)
{
    if (string.IsNullOrWhiteSpace(caseFolder))
        return 0;

    return new BatchLedger(caseFolder).Replay().Pending(planId).Count();
}
```
New: `PendingBatchCount(string caseFolder, Guid planId, LlmProviderKind provider)` -> `.Pending(planId).Count(r => r.Provider == provider)`; plus an "other providers" count/distinct-kinds helper for the banner. `BatchLedgerState.Pending` at l.82-86.

---

### `Services/Ai/BatchingLlmProvider.cs` (modify: three additions, one deletion candidate)

**A. `ResolveOtherProviderPendingAsync` (static, D-13/D-14/D-22).** Shape from `CancelPendingAsync` (l.398-416):
```csharp
public static async Task<List<string>> CancelPendingAsync(BatchLedger ledger,
    Func<LlmProviderKind, IBatchTransport?> transportFor, Guid planId, Func<BatchLedgerRecord, bool>? include = null,
    bool closeOnCancelFailure = false, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(ledger);
    ArgumentNullException.ThrowIfNull(transportFor);

    var state = ReadForCancel(ledger);                 // throws IOException on unreadable ledger (CR-A05) -> refuse run
    var pending = state.Pending(planId).Where(r => include == null || include(r)).ToList();
    ...
```
Collect path: extract the classification block of `CollectAsync` (l.1209-1220) into a static helper and reuse:
```csharp
// T-07-23: only ids this batch was submitted with are delivered; foreign or repeated ids are ignored
var byId = new Dictionary<string, BatchItemResult>(StringComparer.Ordinal);
foreach (var item in results.Items)
    if (item != null && batch.Contains(item.CustomId) && !byId.ContainsKey(item.CustomId))
        byId[item.CustomId] = item;

var ordered = batch.ItemIds.Select(id => byId.TryGetValue(id, out var found)
    ? found
    : new BatchItemResult { CustomId = id, Outcome = BatchItemOutcome.Expired, Message = "Missing from the batch results" })
    .ToList();
```
then `ledger.SaveResults(...)` and append `Collected` (l.1240-1252) with `Provider = record.Provider` (not `_transport.Kind`), `PlanId = record.PlanId`, `OutputFileId = status.OutputFileId ?? string.Empty`. Status mapping: terminal = Ended/Expired/Cancelled (comment l.1167), `BatchState.Failed` mirrors `FailAtProviderAsync`. Must run before `CreateRunGateway` (Pitfall 11: `Recover` runs in ctor l.157).

**B. `PriceUnusedResults` (static, D-16).** Pricing math copy from `PriceOrphans` (l.296-312):
```csharp
// CR-A04: the batch's own provider and model; no catalog price means unbilled, never a recorded $0
var model = _priceLookup(orphan.Provider, orphan.ModelId);
if (model == null) { priced = false; break; }

var usage = orphan.Result.Response!.ToUsage();
usage.IsBatch = true;
usage.Normalize();
var itemCost = model.CalculateCost(usage);
```
Candidate selection copy from `Recover` (l.180-213) but across ALL plans (Start Over deletes the whole ledger): collected batches not in `OrphansBilledIds`, `_ledger.TryReadResults` succeeded (skip cache-less, CX-05), `batch.Contains(id) && !state.IsConsumed(batchId, id)`, `IsUsable(result)`. Return `OrphanBill` (l.65-84). Make `OrphanOperation` (l.384, currently `private const`) `public const`.

**C. `DrainStaleStagesAsync` (instance, D-17/D-21).** Selection under lock copies `CancelPendingStagesAsync` l.537-543 but WITHOUT setting `Closed`:
```csharp
lock (_lock)
{
    closing = _batches.Values.Where(b => !b.Closed && stageMatch(b.StageLabel ?? string.Empty)).ToList();
}
```
Cancel via `_transport.CancelAsync(b.BatchId, ct)` directly (no `CancelPendingAsync`, no `Cancelled` ledger line — Pitfall 10). Bounded wait copies `DetachAsync` l.800:
```csharp
await Task.WhenAll(flushes).WaitAsync(_timings.DrainTimeout).ConfigureAwait(false);
```
Add `public TaskCompletionSource Ended { get; } = NewSignal();` to `TrackedBatch` (l.1683-1740) and `tracked.Ended.TrySetResult()` in `UntrackLocked` (l.971-979). `NewSignal()` helper at l.981 (`RunContinuationsAsynchronously`). Catch `TimeoutException` with a comment (D-17 step 4).

**Deletion candidate:** `CancelPendingStagesAsync` (l.525-575) + `Reopen` (l.577-597) lose their only production caller; research recommends deleting them with tests `BatchingLlmProviderTests` l.1196, l.1243 (PLAT-05 spirit). Update the `TrackedBatch.Closed` doc comment (l.1707) accordingly.

---

### `Models/WizardState.cs` (modify, D-16 shared price lookup)

Extract the lambda at l.107-108 into a public method used by both `CreateRunGateway` and Start Over:
```csharp
Func<LlmProviderKind, string, AIModelConfig?> price = (kind, id) =>
    AvailableModelConfigs.FirstOrDefault(m => m.ModelId == id && m.Provider == kind);
```
-> `public AIModelConfig? PriceBatchModel(LlmProviderKind kind, string modelId)`.

---

### `UserControls/StepGenerateCorpus.Run.cs` (component, modify)

**D-13/D-14/D-22 seam** — between the approval check (l.431-438) and gateway creation (l.440-449):
```csharp
string folder;
ILlmGateway gateway;
BatchingLlmProvider? batch;
var useBatch = CaseUseBatch;
try
{
    folder = _state.CaseStore.EnsureFolder(Draft);
    // NEW: resolve other-provider pending batches here (off UI thread via Task.Run), refuse on Blocked / failure
    (gateway, batch) = RunGatewayFactory?.Invoke(useBatch)
        ?? (GatewayFactory != null ? (GatewayFactory(), null) : _state.CreateRunGateway(useBatch, folder, plan.PlanId));
}
```
Refusal pattern copy (l.423-424 / 434-437): `SetStatus(GenerationUiText.RunRefused...Text, Theme.Warning); return;`.

**transportFor default** — reuse/extract from `CancelPendingBatchesAsync` (l.1032-1036) so run start and Start Over share it:
```csharp
var transportFor = BatchTransportFactory ?? (kind =>
{
    var key = _state.GetApiKey(kind);
    return string.IsNullOrWhiteSpace(key) ? null : LlmProviderFactory.CreateBatchTransport(kind, key);
});
```
and the off-UI-thread call shape (l.1040-1041): `await Task.Run(() => BatchingLlmProvider.X(new BatchLedger(folder), transportFor, ...CancellationToken.None))`.

**D-16 seam** — Start Over (l.963-971):
```csharp
var cancelFailed = false;
if (pending > 0)
    cancelFailed = !await CancelPendingBatchesAsync(folder);
if (IsDisposed)
    return;
// NEW (only when !cancelFailed and ledger Readable): price unused results -> _state.UsageTracker.RecordUsage(BatchingLlmProvider.OrphanOperation, ...)
var reset = await GenerationReset.StartOverAsync(folder, plan, Draft.Guide, keepBatchLedger: cancelFailed);
```
`RecordUsage` call shape: `BatchingLlmProvider.cs:379` `tracker.RecordUsage(OrphanOperation, line.Model, line.Usage, line.Cost);`.

**D-07 count** — fold relabel count into the existing tuple (l.940-947):
```csharp
var (files, pending, cited) = await Task.Run(() =>
    (GenerationJournal.ListAllFiles(folder).Count, BatchLedger.PendingBatchCountAllPlans(folder),
        StartOverCitesDiscovery(folder, caseId)));
...
var (title, text) = GenerationUiText.CG2StartOver(files, pending > 0, cited);
```
**Writer 3** (l.982) `new AnswerKeyOverlay()` stays; it now goes through the funnel automatically via `GenerationOutputs`.

**D-15 call sites** of `BatchLedger.PendingBatchCount`: l.531 (`await Task.Run(...)`), l.1193 (`ReadEntryAsync`, static — pass `_state.TextProvider` in as a parameter), and `StepGenerateCorpus.cs:1309`. Start Over keeps `PendingBatchCountAllPlans` (WR-A05).

---

### `UserControls/StepReviewPlan.Run.cs` (component, modify D-12)

Call site l.529: `var result = await CorpusApproval.ApproveAsync(folder, Draft!, plan);`. Compute the overlay off the UI thread before it, using the ThreadInvalidation pattern above (`GenerationRunState.From(plan, GenerationJournal.Replay(folder, plan)).BuildFullOverlay(plan, guide)`), only when replay is `Readable && !PlanChanged` and the PlanId matches the sidecar/WrittenFiles. Services/Corpus stays free of Services/Evidence.

---

### UiText (pure copy) — `GenerationUiText.cs`, `QaUiText.cs`, `CorpusUiText.cs`

Precedent for optional trailing parameters (`GenerationUiText.CG2StartOver`, l.984-988):
```csharp
public static (string Title, string Text) CG2StartOver(int files, bool batchesPending = false, bool citedByDiscovery = false) =>
    ("Start Over",
        $"Delete the {Plural(files, ...)} ...? This cannot be undone. The corpus plan and case guide are kept."
        + (batchesPending ? " Pending provider batches are cancelled." : "")
        + (citedByDiscovery ? " Discovery documents that cite these emails will show Out of date." : ""));
```
- D-07: append `bool relabelsKept = false` -> `+ (relabelsKept ? " Relabels you made in the answer key are kept." : "")`.
- D-06: `QaUiText.CQ1RegenerateThread(string subject, int emlFiles, int attachments, bool relevant, bool citedByDiscovery = false)` (l.335-344) — append `bool relabelsKept = false`, clause "Relabels you made for its emails in the answer key are kept."
- D-08: `CorpusUiText.ReplanBody(bool approved, int generatedThreads = 0)` (l.315-324) and `SettingsNotice(bool hasPlan, bool approved, DateTime? approvedLocal)` unapproved branch (l.214-215) — append `int relabels = 0`, clause "{N} manual relabels in the answer key will be dropped." using `CaseGuideUiText.Plural` / `Plural` like l.322.
- D-15: `BannerBody(..., bool batchOn = false, int pendingBatches = 0)` (l.934-943) — append `int otherProviderBatches = 0, string otherProvider = "", bool otherProviderBlocked = false`. Existing clause style:
```csharp
var batchClause = pendingBatches > 0
    ? $" {Plural(pendingBatches, "provider batch", "provider batches")} submitted earlier will be collected first, without paying again."
    : batchOn ? " Batch pricing stays on for this case." : "";
```
- D-14/D-22 refusal text: `public const string`/static method beside `RunRefusedGuideChangedText` / `RunRefusedNotApprovedText`.
- Callers to update: `Forms/QaFindingsDialog.cs:407`, `Forms/ReplanDialog.cs:35` (`ReplanDialog(approved, generatedThreads)` ctor l.19), `UserControls/StepCorpusSettings.cs:1159`, `Forms/WizardForm.cs:609` (`CG3CloseDuringRun(s.PendingProviderBatches > 0)` — semantics change to current-provider only, no signature change).

---

### `Models/AttachmentType.cs` (NEW) / `Models/Attachment.cs` (DELETE)

Move exactly `Models/Attachment.cs` l.1-11:
```csharp
namespace ReelDiscovery.Models;

public enum AttachmentType
{
    Word,
    Excel,
    PowerPoint,
    Image,
    CalendarInvite,
    Voicemail
}
```
Delete the file (removes `AttachmentComplexity` l.13-17 and `class Attachment` l.19-74). Member order must stay (serialized enum).

---

## Test Pattern Assignments

### `ReelDiscovery.Tests/PlatformTests.cs` (modify)
- Add to the `[Theory] LegacyType_IsDeleted` list (l.86-111):
```csharp
[InlineData("ReelDiscovery.Models.Attachment")]
[InlineData("ReelDiscovery.Models.AttachmentComplexity")]
```
- Funnel source-scan guard: copy `OnlyServicesAiReferencesProviderSdks` (l.32-59) — `FindGlobalJson()` root, `Directory.GetFiles(root, "*.cs", AllDirectories)` filtered by `IsExcluded` (l.136-140), vacuous-pass guard, then assert `AnswerKeyCsvWriter.Build(` / `AnswerKeyXlsxWriter.Build(` appear only in `Services/Corpus/AnswerKeyFiles.cs` (and the writers themselves / `CorpusApproval.cs` if it keeps building bytes).

### `ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs` (NEW)
Analog `Corpus/AnswerKeyXlsxWriterTests.cs`: usings l.1-6 (`DocumentFormat.OpenXml.Packaging`, `DocumentFormat.OpenXml.Spreadsheet`, `ReelDiscovery.Models.Case`, `ReelDiscovery.Models.Corpus`, `ReelDiscovery.Services.Corpus`), namespace `ReelDiscovery.Tests.Corpus`, `Guide()` / `Row(...)` builders (l.14-45). Build the shared-string workbook with `SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true)` as the writer does (`AnswerKeyXlsxWriter.cs` l.73-115) but with a `SharedStringTablePart` and `CellValues.SharedString` cells.

### `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs` (NEW)
Analog `Corpus/CorpusApprovalTests.cs` l.9-39: `IDisposable` temp folder (`Path.GetTempPath()` + `"rd-..." + Guid`), `Dispose` deletes recursively, `Setup()` via `CorpusTestData.RichDraft()` + `PlanFixtures.ValidPlan(draft)`. Writer failures injected through the `FileWriter` delegate (`CorpusApproval.cs:8`).

### `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs` (revise l.866-892)
Keep the first three assertions (provider ignores foreign records); replace the final "fresh submission" assertion (l.888-891) with a companion that runs `ResolveOtherProviderPendingAsync` first and asserts the Anthropic record is Cancelled/Collected before the OpenAI submission. Ledger seeding shape l.868-879.

### `ReelDiscovery.Tests/Evidence/BatchParityTests.cs` (rewrite l.646-707)
Keep the session-1 setup (l.651-689). Session-2 assertions change: `cancelFails=false` -> ledger has `CollectedIds` containing `staleBatch`, NOT `CancelledIds`; live render not sent. `cancelFails=true` -> set `DrainTimeout` small on the test `BatchTimings` (the `Fast()` helper l.89-95 does not set it); pending stays 1; no Cancelled line.

### `ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs` (extend)
Harness/RunSta/Pump pattern and ledger seed at l.880-910; `step.BatchTransportFactory = _ => transport;` (l.903) becomes kind-aware with two `FakeBatchTransport { Kind = ... }`; key-missing = factory returns null for that kind; failure = factory throws (l.980 precedent).

---

## Shared Patterns

### Case-folder file paths (T-04-68 / T-07-40)
**Source:** `GenerationOutputs.cs` l.48-49, `QaReportStore.cs` l.33-36
**Apply to:** funnel, sidecar store, backup file
```csharp
// Paths = case folder + constant names only; WrittenFiles from case.json never build paths (T-04-68)
var path = Path.Combine(caseFolder, fileName);
```

### Cancellation-first, filtered IO catch
**Source:** `GenerationOutputs.cs` l.55-63, `CorpusApproval.cs` l.68-79
**Apply to:** every new write/read path in Corpus and Ai
```csharp
catch (OperationCanceledException) { throw; }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* comment why */ }
```

### Ledger unreadable = refuse (CR-A05)
**Source:** `BatchingLlmProvider.ReadForCancel` l.446-453
**Apply to:** `ResolveOtherProviderPendingAsync`, `PriceUnusedResults` (price only when `Readable`, Pitfall 13)
```csharp
var state = ledger.Replay();
if (!state.Readable)
    throw new IOException(BatchLedger.UnreadableMessage);
```

### Orphan pricing (CR-A04 / WR-A09)
**Source:** `BatchingLlmProvider.PriceOrphans` l.296-312 and tracker record l.375-380
**Apply to:** D-13 foreign collected results (automatic via Recover), D-16 Start Over pricing, D-17 leftovers

### Background off-UI-thread + IsDisposed check
**Source:** `StepGenerateCorpus.Run.cs` l.940-947, 531-533
**Apply to:** relabel counts for D-06/07/08, resolver call, Start Over pricing
```csharp
var x = await Task.Run(() => ...);
if (IsDisposed || _isGenerating || _isResetting)
    return;
```

### Status reporting (never throw to UI)
**Source:** `StepGenerateCorpus.Run.cs` l.987-996 (`SetStatus(..., Theme.Warning/Danger/Success)`, `ShowFileError(key)`), `ThreadInvalidation.cs` l.100-107 (`KeyError`)
**Apply to:** D-10 backup notice (separate `Notice`/`KeyNotice`, not `KeyError`), D-14 refusal.

### Pure UiText with optional trailing params
**Source:** `GenerationUiText.CG2StartOver` l.984-988
**Apply to:** all copy in D-06/07/08/14/15. Existing tests and callers compile unchanged.

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| CSV read-back half of `Services/Corpus/AnswerKeyReader.cs` | utility | file-I/O parse | No CSV parser exists in the codebase (only the writer). Use research Pattern 4 (`TextFieldParser` with `TrimWhiteSpace = false`, per-line `MalformedLineException` skip, or a small RFC 4180 state machine); encoding detection BOM -> strict UTF-8 -> 1252/Latin1; delimiter from `sep=` or header sniffing. |

## Metadata

**Analog search scope:** `Services/Corpus`, `Services/Evidence`, `Services/Ai`, `Services/Qa`, `Services/Discovery`, `Models`, `UserControls`, `Forms`, `ReelDiscovery.Tests/{Corpus,Ai,Evidence}`, `ReelDiscovery.Tests/PlatformTests.cs`
**Files scanned:** ~30
**Pattern extraction date:** 2026-10-12
