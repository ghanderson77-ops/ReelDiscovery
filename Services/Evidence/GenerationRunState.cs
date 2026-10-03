using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Relevant-thread run state projected from the plan and the replayed journal (D-05, RUN-06). Pure: the same plan and
/// journal always give the same state, after a finished, cancelled or crashed run.
/// </summary>
public class GenerationRunState
{
    public Dictionary<string, ThreadRunStatus> StatusByThread { get; set; } = new(StringComparer.Ordinal);

    // Last ThreadDone / ThreadFailed record per relevant thread
    public Dictionary<string, JournalRecord> Records { get; set; } = new(StringComparer.Ordinal);

    // Ledger deltas of done threads only (input to LedgerMerger)
    public Dictionary<string, List<LedgerDelta>> DeltasByThread { get; set; } = new(StringComparer.Ordinal);

    public int TotalThreads { get; set; }
    public int Written { get; set; }
    public int Warnings { get; set; }
    public int Failed { get; set; }
    public int EmailsWritten { get; set; }

    // Planned messages of Failed threads
    public int EmailsFailed { get; set; }

    public int FilesWritten { get; set; }
    public int Mailboxes { get; set; }
    public decimal CostUsd { get; set; }
    public bool HasRun { get; set; }
    public bool PlanChanged { get; set; }
    public string UnreadableReason { get; set; } = string.Empty;

    public int Pending => TotalThreads - Written - Warnings - Failed;

    public bool IsDone(string threadId) =>
        StatusByThread.TryGetValue(threadId ?? string.Empty, out var status)
        && status is ThreadRunStatus.Written or ThreadRunStatus.Warnings;

    public static GenerationRunState From(CorpusPlan plan, JournalReplay replay)
    {
        var state = new GenerationRunState
        {
            HasRun = replay.Exists && (replay.HasRunStarted || replay.LastByThread.Count > 0),
            PlanChanged = replay.PlanChanged,
            UnreadableReason = replay.Reason ?? string.Empty
        };

        var files = new HashSet<string>(StringComparer.Ordinal);
        var mailboxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var thread in RelevantThreads(plan))
        {
            if (state.StatusByThread.ContainsKey(thread.Id))
                continue;

            state.TotalThreads++;
            var status = ThreadRunStatus.Pending;

            if (replay.LastByThread.TryGetValue(thread.Id, out var record) && record != null)
            {
                state.Records[thread.Id] = record;
                state.CostUsd += Math.Max(0m, record.CostUsd);

                if (record.Type == JournalRecordType.ThreadDone)
                {
                    status = record.Status == ThreadWriteStatus.Written ? ThreadRunStatus.Written : ThreadRunStatus.Warnings;
                    state.EmailsWritten += record.Messages.Count;
                    state.DeltasByThread[thread.Id] = record.LedgerDelta.ToList();

                    foreach (var file in record.Files.Where(f => !string.IsNullOrWhiteSpace(f)))
                    {
                        files.Add(file);
                        // "Mailboxes/{folder}/{box}/{name}.eml": the custodian folder is the second segment
                        var segments = file.Split('/', '\\');
                        if (segments.Length > 1 && segments[1].Length > 0)
                            mailboxes.Add(segments[1]);
                    }
                }
                else if (record.Type == JournalRecordType.ThreadFailed)
                {
                    status = ThreadRunStatus.Failed;
                    state.EmailsFailed += thread.Messages.Count(m => m != null);
                }
            }

            state.StatusByThread[thread.Id] = status;
            switch (status)
            {
                case ThreadRunStatus.Written:
                    state.Written++;
                    break;
                case ThreadRunStatus.Warnings:
                    state.Warnings++;
                    break;
                case ThreadRunStatus.Failed:
                    state.Failed++;
                    break;
            }
        }

        state.FilesWritten = files.Count;
        state.Mailboxes = mailboxes.Count;

        // Replaced failures and fatal-stop calls were paid for too; the last record per thread alone under-counts
        state.CostUsd = Math.Max(state.CostUsd, replay.SpentCostUsd);
        return state;
    }

    /// <summary>
    /// Answer-key status and rendered subject per relevant email DocID. The subject comes from the same function the EML
    /// writer uses, so key and EML subjects always match (Pitfall 2). Attachments are not overlaid and stay Planned.
    /// </summary>
    public AnswerKeyOverlay BuildOverlay(CorpusPlan plan, IReadOnlyDictionary<string, ThreadAssignment> assignments)
    {
        var overlay = new AnswerKeyOverlay();
        foreach (var thread in RelevantThreads(plan))
        {
            var status = StatusByThread.TryGetValue(thread.Id, out var s) ? s : ThreadRunStatus.Pending;
            var label = status switch
            {
                ThreadRunStatus.Written => AnswerKeyBuilder.StatusGenerated,
                ThreadRunStatus.Warnings => AnswerKeyBuilder.StatusGeneratedWithWarnings,
                ThreadRunStatus.Failed => AnswerKeyBuilder.StatusFailed,
                _ => AnswerKeyBuilder.StatusPlanned
            };
            var marking = assignments.TryGetValue(thread.Id, out var assignment) && assignment != null
                ? assignment.Marking
                : PrivilegeMarking.None;

            foreach (var message in thread.Messages.Where(m => m != null && !string.IsNullOrEmpty(m.DocId)))
            {
                overlay.ByDocId[message.DocId] = new AnswerKeyOverlayEntry
                {
                    Status = label,
                    Subject = EmailComposer.SubjectFor(thread, message, marking)
                };
            }
        }

        return overlay;
    }

    public GenerationCounts ToCounts(CorpusPlan plan, int waveCount, int ledgerEntries, decimal remainingCost, bool running,
        bool finished) => new()
    {
        TotalThreads = TotalThreads,
        DoneThreads = Written + Warnings,
        WarningThreads = Warnings,
        FailedThreads = Failed,
        WaveCount = waveCount,
        EmailsPlanned = RelevantThreads(plan).Sum(t => t.Messages.Count(m => m != null)),
        EmailsWritten = EmailsWritten,
        FilesWritten = FilesWritten,
        Mailboxes = Mailboxes,
        LedgerEntries = ledgerEntries,
        CostUsd = CostUsd,
        RemainingCostUsd = remainingCost,
        Running = running,
        Finished = finished
    };

    /// <summary>
    /// Rough cost of the relevant messages still to write (Pending and Failed threads, which Continue retries), on the
    /// same per-message heuristic as the plan estimate. A null model gives 0.
    /// </summary>
    public static decimal EstimateRemaining(CorpusPlan plan, GenerationRunState state, AIModelConfig? model)
    {
        if (model == null)
            return 0m;

        var remaining = RelevantThreads(plan).Where(t => !state.IsDone(t.Id)).Sum(t => t.Messages.Count(m => m != null));
        return remaining * model.CalculateTotalCost(GenerationCostEstimator.RelevantInputPerMessage,
            GenerationCostEstimator.RelevantOutputPerMessage) * GenerationCostEstimator.QaOverhead;
    }

    private static IEnumerable<PlannedThread> RelevantThreads(CorpusPlan plan) =>
        (plan?.Threads ?? new List<PlannedThread>())
        .Where(t => t != null && t.Relevance == ThreadRelevance.Relevant && !string.IsNullOrEmpty(t.Id));
}

/// <summary>
/// Applies a run checkpoint to the CaseDraft: ledger and cached counts into Corpus.Generation (case.json, D-10).
/// </summary>
public static class GenerationStateApplier
{
    // UI thread only: CaseDraft is never mutated from workers (RESEARCH Pattern 8)
    public static void Apply(CaseDraft draft, CorpusPlan plan, GenerationRunState state, NarrativeLedger ledger,
        IReadOnlyList<WrittenCorpusFile> files, GenerationOutcome outcome, string message, DateTime utcNow, bool final)
    {
        draft.Corpus ??= new CorpusState();
        var generation = draft.Corpus.Generation ??= new GenerationState();

        generation.RunPlanId = plan.PlanId;
        generation.Ledger = ledger ?? new NarrativeLedger();
        generation.ThreadsTotal = state.TotalThreads;
        generation.ThreadsWritten = state.Written;
        generation.ThreadsWithWarnings = state.Warnings;
        generation.ThreadsFailed = state.Failed;
        generation.EmailsWritten = state.EmailsWritten;
        generation.EmailsFailed = state.EmailsFailed;
        generation.FilesWritten = state.FilesWritten;
        generation.Mailboxes = state.Mailboxes;
        generation.CostUsd = state.CostUsd;
        generation.ProjectedUtc = utcNow;

        // Re-stamp the files this checkpoint rewrote (same Kind replaced); others stay as recorded
        CorpusApproval.RecordPartialWrites(draft, files ?? new List<WrittenCorpusFile>());

        if (!final)
            return;

        generation.LastOutcome = outcome;
        generation.LastMessage = message ?? string.Empty;
        generation.LastRunEndedUtc = utcNow;
        draft.AdvanceStage(CaseStage.Generation);
    }

    /// <summary>
    /// Start Over: drops the generation state and the story-so-far file record. The caller deletes files and the journal.
    /// </summary>
    public static void Clear(CaseDraft draft)
    {
        var corpus = draft.Corpus;
        if (corpus == null)
            return;

        corpus.Generation = null;
        corpus.WrittenFiles = corpus.WrittenFiles.Where(f => f != null && f.Kind != CorpusFileKind.StorySoFar).ToList();
    }
}
