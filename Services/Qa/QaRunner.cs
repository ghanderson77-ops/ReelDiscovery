using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Qa;

/// <summary>
/// The automatic QA pass at the end of every run (D-14, D-15, UI-SPEC U33): the QA-03 scans and the QA-02 leakage
/// audit, written to qa-scans.json and qa-leakage.json. Off the UI thread, free (no model call), report-only: it never
/// writes outside the two QA files and never fails or blocks a run or an export.
/// </summary>
public static class QaRunner
{
    /// <summary>
    /// Replays the journal and, when at least one email is written, runs and stores the scans, then the leakage audit.
    /// Returns what was stored (null for a part that did not run). Throws only OperationCanceledException.
    /// <paramref name="overlay"/> should be the one the answer-key projection used; null rebuilds it from the journal.
    /// </summary>
    public static async Task<(ScanReport? Scans, LeakageReport? Leakage)> RunEndOfRunAsync(CaseDraft draft, CorpusPlan plan,
        string caseFolder, AnswerKeyOverlay? overlay, CancellationToken ct = default)
    {
        ScanReport? scans = null;
        LeakageReport? leakage = null;
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (draft == null || plan == null || string.IsNullOrWhiteSpace(caseFolder))
                    return;

                var replay = GenerationJournal.Replay(caseFolder, plan);
                if (!replay.Readable || replay.PlanChanged || !HasWrittenEmail(replay))
                    return;

                var guide = draft.Guide ?? new CaseGuide();
                var store = new QaReportStore(caseFolder);
                overlay ??= GenerationRunState.From(plan, replay).BuildFullOverlay(plan, guide);

                ct.ThrowIfCancellationRequested();
                var report = CorpusScans.Run(draft, plan, replay, overlay);
                report.JournalUtc = JournalWriteUtc(caseFolder);
                ct.ThrowIfCancellationRequested();
                store.SaveScans(report);
                scans = report;

                ct.ThrowIfCancellationRequested();
                var audit = LeakageAudit.Run(plan, guide, overlay);
                ct.ThrowIfCancellationRequested();
                store.SaveLeakage(audit);
                leakage = audit;
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // QA is report-only (D-15); it must never fail a run. A report that could not be written reads as Not run
        }

        return (scans, leakage);
    }

    private static bool HasWrittenEmail(JournalReplay replay) =>
        (replay.LastByThread ?? new Dictionary<string, JournalRecord>()).Values.Any(r =>
            r != null && r.Type == JournalRecordType.ThreadDone
                      && (r.Messages ?? new List<JournalMessage>()).Any(m => m != null && !string.IsNullOrEmpty(m.DocId)));

    private static DateTime? JournalWriteUtc(string caseFolder)
    {
        try
        {
            var path = GenerationJournal.PathFor(caseFolder);
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            // Without a journal time the report shows no staleness hint; the findings are still valid
            return null;
        }
    }
}
