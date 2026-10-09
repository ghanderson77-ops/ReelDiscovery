using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Progress of a discovery run: the document being written, its part, and how many documents are finished.
/// </summary>
public class DiscoveryProgress
{
    // 1-based position of the document in the run
    public int Index { get; set; }
    public int Total { get; set; }
    public string Key { get; set; } = string.Empty;

    // 1-based part (chunk or call) of the document and its part count; 0 before the first part
    public int Part { get; set; }
    public int PartCount { get; set; }

    public int Finished { get; set; }
    public bool Stopping { get; set; }

    // True on the report sent after the document's record (and .docx) is saved
    public bool Done { get; set; }
}

/// <summary>
/// Outcome of a discovery run; never an exception.
/// </summary>
public class DiscoveryRunResult
{
    public int Total { get; set; }

    // Documents written and saved as .docx
    public int Saved { get; set; }

    // Saved documents that kept validator warnings
    public int Warnings { get; set; }

    // Documents whose model calls produced no usable content
    public int Failed { get; set; }

    // Documents with content whose .docx could not be written (open in Word, write error)
    public int NotSaved { get; set; }

    public bool Cancelled { get; set; }

    // Non-empty when a fatal provider error (bad key, spend limit) or an unreadable sidecar stopped the run
    public string FatalMessage { get; set; } = string.Empty;

    public decimal CostUsd { get; set; }

    // Keys of the documents whose record was written in this run
    public List<string> FinishedKeys { get; set; } = new();
}

/// <summary>
/// Outcome of Save Again (rewriting a .docx from stored content, no model call).
/// </summary>
public class SaveAgainResult
{
    public bool Saved { get; set; }

    // The file is still open in another program
    public bool StillOpen { get; set; }

    public string Error { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

/// <summary>
/// Runs discovery generation (D-05, D-27, D-28, D-29): one unit of work per document under a bounded
/// <see cref="PlanCallRun"/>; each finished document is written as .docx into Discovery\ and then recorded in
/// discovery-documents.json by a single writer. One failed document never stops the others; a fatal provider error or a
/// user cancel keeps every finished document. Never throws.
/// </summary>
public sealed class DiscoveryGenerator
{
    // Plain-word failure reasons shown as the card's {reason} text (UI-SPEC)
    public const string ReasonNoContent = "the model returned no usable content";
    public const string ReasonTimeout = "the request timed out";
    public const string ReasonRefused = "the provider refused the request";

    // Research Pattern 7
    public const int MaxParallelCalls = 3;

    private const string WriteFailedPrefix = "the Word file could not be written: ";
    private const string RetryHint = "Click Regenerate... to try again.";

    private readonly ILlmGateway _llm;

    // Single writer for the sidecar: parallel documents finishing together never lose an update (T-08-44)
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public DiscoveryGenerator(ILlmGateway llm)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    internal static IReadOnlyList<Type> SchemaTypes => DiscoveryPrompts.SchemaTypes;

    // Tests use 1 for a deterministic call order
    internal int MaxParallel { get; set; } = MaxParallelCalls;

    public static string ReasonWriteFailed(string message) => $"{WriteFailedPrefix}{(message ?? string.Empty).Trim().TrimEnd('.')}";

    /// <summary>
    /// True when <paramref name="reason"/> records a .docx write failure (content kept, Save Again rewrites it) rather
    /// than a model failure.
    /// </summary>
    public static bool IsWriteFailure(string? reason) =>
        !string.IsNullOrEmpty(reason) && reason.StartsWith(WriteFailedPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Writes every work item. <paramref name="guidance"/> maps a document key to the user's regenerate guidance.
    /// Existing content is replaced only by a successful write; a failed regenerate keeps the previous version (U54).
    /// </summary>
    public async Task<DiscoveryRunResult> RunAsync(CaseDraft draft, DiscoveryCorpus? corpus, IReadOnlyList<DiscoveryWorkItem> items,
        IReadOnlyDictionary<string, string>? guidance = null, IProgress<DiscoveryProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new DiscoveryRunResult();
        var work = (items ?? Array.Empty<DiscoveryWorkItem>()).Where(i => i != null).ToList();
        result.Total = work.Count;

        try
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.FolderPath))
            {
                result.FatalMessage = "The case has no folder to save discovery documents in.";
                return result;
            }

            var folder = draft.FolderPath;

            // Never overwrite a sidecar this build cannot read (newer version, other case, corrupt) (T-08-04, T-08-46)
            var load = DiscoveryStore.Load(folder, draft.CaseId);
            if (!load.Succeeded && !load.Missing)
            {
                result.FatalMessage = load.Reason;
                return result;
            }

            if (work.Count == 0)
                return result;

            var set = load.Set ?? new DiscoveryDocumentSet();
            set.CaseId = draft.CaseId;

            // Assigned once and persisted (D-24)
            if (set.CaseNumber.Length == 0)
                set.CaseNumber = CaseNumber.For(draft);

            Directory.CreateDirectory(DiscoveryStore.FolderPath(folder));

            // Built once: byte-identical for every call of every document (D-06)
            var systemPrompt = DiscoveryPrompts.BuildSystemPrompt(draft);
            var context = new RunContext(draft, corpus, folder, set, systemPrompt, work.Count, progress, result);

            using (var run = new PlanCallRun(ct, MaxParallel, RetryHint))
            {
                var tasks = work.Select((item, index) =>
                {
                    var text = guidance != null && guidance.TryGetValue(item.Key, out var g) ? g ?? string.Empty : string.Empty;
                    return RunOneAsync(item, index + 1, text, run, context);
                }).ToList();

                try
                {
                    // WhenAll waits for every task, so finished documents are on disk before a cancel propagates
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    result.Cancelled = true;
                }

                if (run.IsFatal)
                    result.FatalMessage = run.FatalError;
            }
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The Discovery folder could not be created or the case folder is unusable
            result.FatalMessage = $"The discovery documents could not be saved: {ex.Message.Trim().TrimEnd('.')}.";
        }

        return result;
    }

    /// <summary>
    /// Rewrites a document's .docx from the content stored in the sidecar, with no model call (UI-SPEC U47).
    /// </summary>
    public static async Task<SaveAgainResult> SaveAgainAsync(CaseDraft draft, string key, CancellationToken ct = default)
    {
        var result = new SaveAgainResult();
        if (ct.IsCancellationRequested)
        {
            result.Error = "The save was cancelled.";
            return result;
        }

        if (draft == null || string.IsNullOrWhiteSpace(draft.FolderPath))
        {
            result.Error = "The case has no folder.";
            return result;
        }

        var folder = draft.FolderPath;
        var load = DiscoveryStore.Load(folder, draft.CaseId);
        if (!load.Succeeded)
        {
            result.Error = load.Reason.Length > 0 ? load.Reason : "No discovery documents have been generated for this case.";
            return result;
        }

        var set = load.Set!;
        var document = set.Find(key ?? string.Empty);
        if (document == null || !document.HasContent)
        {
            result.Error = "This document has no saved content to write.";
            return result;
        }

        result.FileName = document.FileName;
        var path = DiscoveryStore.DocxPath(folder, document.FileName);
        if (path == null)
        {
            result.Error = ReasonWriteFailed("invalid file name");
            return result;
        }

        try
        {
            var bytes = BuildBytes(document);
            Directory.CreateDirectory(DiscoveryStore.FolderPath(folder));
            await CaseStore.WriteAtomicAsync(path, bytes, CancellationToken.None);
        }
        catch (Exception ex) when (FileLockDetector.IsFileOpenElsewhere(ex, path))
        {
            result.StillOpen = true;
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Error = ReasonWriteFailed(ex.Message);
            return result;
        }

        document.FileSaved = true;
        if (IsWriteFailure(document.FailureReason))
            document.FailureReason = string.Empty;

        try
        {
            // The file is already written; the record must follow it
            await DiscoveryStore.SaveAsync(folder, set, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Error = $"The Word file was saved, but the discovery documents file could not be updated: {ex.Message.Trim().TrimEnd('.')}.";
        }

        result.Saved = true;
        return result;
    }

    private async Task RunOneAsync(DiscoveryWorkItem item, int index, string guidance, PlanCallRun run, RunContext context)
    {
        Report(context, item, index, 0, 0);
        void OnPart(int part, int count) => Report(context, item, index, part, count);

        DepositionContent? deposition = null;
        WrittenDiscoveryContent? written = null;
        List<DiscoveryViolation> warnings;
        CostBreakdown cost;
        bool notRun;
        bool refused;
        bool timedOut;
        List<string> notes;

        // User cancel propagates from the writers (OperationCanceledException)
        if (item.Kind == DiscoveryKind.Deposition && item.Deposition != null)
        {
            var outcome = await new DepositionWriter(_llm).WriteAsync(context.Draft, item.Deposition, context.Set.CaseNumber,
                context.SystemPrompt, guidance, run, OnPart);
            deposition = outcome.Content;
            (warnings, cost, notRun, refused, timedOut) = (outcome.Warnings, outcome.Cost, outcome.NotRun, outcome.Refused, outcome.TimedOut);
            notes = item.Deposition.Notes;
        }
        else if (item.Kind != DiscoveryKind.Deposition && item.Written != null)
        {
            var outcome = await new WrittenSetWriter(_llm).WriteAsync(context.Draft, item.Written, context.Set.CaseNumber,
                context.SystemPrompt, guidance, run, OnPart);
            written = outcome.Content;
            (warnings, cost, notRun, refused, timedOut) = (outcome.Warnings, outcome.Cost, outcome.NotRun, outcome.Refused, outcome.TimedOut);
            notes = item.Written.Notes;
        }
        else
        {
            // A work item without a plan cannot be written
            (warnings, cost, notRun, refused, timedOut) = (new List<DiscoveryViolation>(), new CostBreakdown(), false, false, false);
            notes = new List<string>();
        }

        if (notRun)
        {
            if (cost.TotalUsd > 0)
            {
                await MutateAsync(context, set =>
                {
                    // Paid spend is never lost: it goes to the existing record or the unattributed total (T-08-47)
                    var existing = set.Find(item.Key);
                    if (existing != null)
                    {
                        existing.CostUsd += cost.TotalUsd;
                        existing.CostDetail.Add(cost);
                    }
                    else
                    {
                        set.UnattributedCostUsd += cost.TotalUsd;
                    }
                    context.Result.CostUsd += cost.TotalUsd;
                });
            }
            return;
        }

        if (deposition == null && written == null)
        {
            var reason = timedOut ? ReasonTimeout : refused ? ReasonRefused : ReasonNoContent;
            await MutateAsync(context, set =>
            {
                var existing = set.Find(item.Key);
                if (existing != null && existing.HasContent)
                {
                    // A failed regenerate keeps the previous version and its file (UI-SPEC U54)
                    existing.FailureReason = reason;
                    existing.CostUsd += cost.TotalUsd;
                    existing.CostDetail.Add(cost);
                }
                else
                {
                    var detail = existing?.CostDetail?.Clone() ?? new CostBreakdown();
                    detail.Add(cost);
                    set.Upsert(new DiscoveryDocument
                    {
                        Key = item.Key,
                        Kind = item.Kind,
                        WitnessId = item.WitnessId,
                        Title = item.ShortTitle,
                        FileName = item.FileName,
                        Status = DiscoveryStatus.Failed,
                        FailureReason = reason,
                        PlannerNotes = new List<string>(notes),
                        CostUsd = (existing?.CostUsd ?? 0m) + cost.TotalUsd,
                        CostDetail = detail
                    });
                }
                context.Result.CostUsd += cost.TotalUsd;
                context.Result.Failed++;
                context.Result.FinishedKeys.Add(item.Key);
            });
            Finish(context, item, index);
            return;
        }

        // The content was paid for: it is kept in the sidecar whether or not the .docx can be written
        var record = new DiscoveryDocument
        {
            Key = item.Key,
            Kind = item.Kind,
            WitnessId = item.WitnessId,
            Title = item.ShortTitle,
            FileName = item.FileName,
            Status = warnings.Count > 0 ? DiscoveryStatus.GeneratedWithWarnings : DiscoveryStatus.Generated,
            Warnings = warnings,
            PlannerNotes = new List<string>(notes),
            GeneratedUtc = DateTime.UtcNow,
            Deposition = deposition,
            Written = written,
            TestimonyPages = deposition != null ? TranscriptLayout.Build(deposition).TestimonyPageCount : 0,
            RequestCount = written?.Requests.Count ?? 0
        };
        DiscoveryStaleness.Stamp(record, context.Draft, context.Corpus);

        var fileSaved = false;
        var failureReason = string.Empty;
        var path = DiscoveryStore.DocxPath(context.Folder, item.FileName);
        if (path == null)
        {
            // Only validated names under Discovery\ become paths (T-08-43)
            failureReason = ReasonWriteFailed("invalid file name");
        }
        else
        {
            try
            {
                var bytes = BuildBytes(record);

                // Not cancellable: content that was paid for is always written (Pitfall 6)
                await CaseStore.WriteAtomicAsync(path, bytes, CancellationToken.None);
                fileSaved = true;
            }
            catch (Exception ex) when (FileLockDetector.IsFileOpenElsewhere(ex, path))
            {
                // Open in Word: shown as Not saved, Save Again rewrites it from the stored content (U47)
                fileSaved = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                fileSaved = false;
                failureReason = ReasonWriteFailed(ex.Message);
            }
        }

        record.FileSaved = fileSaved;
        record.FailureReason = failureReason;

        await MutateAsync(context, set =>
        {
            var existing = set.Find(item.Key);
            var detail = existing?.CostDetail?.Clone() ?? new CostBreakdown();
            detail.Add(cost);
            record.CostUsd = (existing?.CostUsd ?? 0m) + cost.TotalUsd;
            record.CostDetail = detail;
            set.Upsert(record);

            // The deponent's exhibit numbers follow the plan, which reuses saved assignments on regenerate (D-21)
            if (item.Kind == DiscoveryKind.Deposition && item.Deposition != null)
            {
                set.ExhibitAssignments.RemoveAll(a => string.Equals(a.WitnessId, item.WitnessId, StringComparison.Ordinal));
                set.ExhibitAssignments.AddRange(item.Deposition.Assignments.Where(a => a != null).Select(Copy));
                set.ExhibitAssignments.Sort((a, b) => a.Number.CompareTo(b.Number));
            }

            context.Result.CostUsd += cost.TotalUsd;
            if (fileSaved)
            {
                context.Result.Saved++;
                if (warnings.Count > 0)
                    context.Result.Warnings++;
            }
            else
            {
                context.Result.NotSaved++;
            }
            context.Result.FinishedKeys.Add(item.Key);
        });
        Finish(context, item, index);
    }

    /// <summary>
    /// Applies one change to the in-memory set and persists it, under the single-writer lock. Not cancellable: a document
    /// that was paid for is always kept (BlindReviewer precedent).
    /// </summary>
    private async Task MutateAsync(RunContext context, Action<DiscoveryDocumentSet> change)
    {
        await _writeLock.WaitAsync(CancellationToken.None);
        try
        {
            change(context.Set);
            await DiscoveryStore.SaveAsync(context.Folder, context.Set, CancellationToken.None);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static byte[] BuildBytes(DiscoveryDocument document) =>
        document.Deposition != null
            ? TranscriptDocxBuilder.Build(document.Deposition)
            : DiscoveryDocxBuilder.Build(document.Written!);

    private static ExhibitAssignment Copy(ExhibitAssignment a) => new()
    {
        Number = a.Number,
        WitnessId = a.WitnessId,
        SourceKey = a.SourceKey,
        Description = a.Description,
        DocId = a.DocId,
        Date = a.Date,
        IssueId = a.IssueId
    };

    private static void Finish(RunContext context, DiscoveryWorkItem item, int index)
    {
        var finished = Interlocked.Increment(ref context.Finished);
        Send(context, new DiscoveryProgress { Index = index, Total = context.Total, Key = item.Key, Finished = finished, Done = true });
    }

    private static void Report(RunContext context, DiscoveryWorkItem item, int index, int part, int partCount) =>
        Send(context, new DiscoveryProgress
        {
            Index = index,
            Total = context.Total,
            Key = item.Key,
            Part = part,
            PartCount = partCount,
            Finished = Volatile.Read(ref context.Finished)
        });

    private static void Send(RunContext context, DiscoveryProgress value)
    {
        try
        {
            context.Progress?.Report(value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failing progress display must never abort a document that is being paid for
        }
    }

    private sealed class RunContext
    {
        // Updated with Interlocked
        public int Finished;

        public RunContext(CaseDraft draft, DiscoveryCorpus? corpus, string folder, DiscoveryDocumentSet set, string systemPrompt,
            int total, IProgress<DiscoveryProgress>? progress, DiscoveryRunResult result)
        {
            Draft = draft;
            Corpus = corpus;
            Folder = folder;
            Set = set;
            SystemPrompt = systemPrompt;
            Total = total;
            Progress = progress;
            Result = result;
        }

        public CaseDraft Draft { get; }
        public DiscoveryCorpus? Corpus { get; }
        public string Folder { get; }
        public DiscoveryDocumentSet Set { get; }
        public string SystemPrompt { get; }
        public int Total { get; }
        public IProgress<DiscoveryProgress>? Progress { get; }
        public DiscoveryRunResult Result { get; }
    }
}
