using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// What "Regenerate thread" did: whether the ThreadInvalidated line was journaled, the EML copies removed, one
/// "{relative}: {reason}" line per copy that could not be removed, and the answer-key write error (empty when fine).
/// </summary>
public class ThreadInvalidationResult
{
    public bool Appended { get; set; }
    public int FilesDeleted { get; set; }
    public List<string> Errors { get; set; } = new();
    public string KeyError { get; set; } = string.Empty;

    /// <summary>Damaged key files backed up during the rewrite (D-10); not a failure.</summary>
    public List<AnswerKeyBackup> KeyBackups { get; set; } = new();
}

/// <summary>
/// "Regenerate thread" (D-13 blind review, D-15 consistency scans): journals a ThreadInvalidated line, deletes the written
/// thread's EML copies and re-projects the answer key so the thread shows Planned until Continue Generating re-renders
/// it. A relevant thread keeps the details it had established (OQ3, D-02); its spend stays counted.
/// </summary>
public static class ThreadInvalidation
{
    public const string NotWrittenMessage = "This thread has not been written yet.";

    /// <summary>
    /// Invalidates one written thread. WR-A10: the ThreadInvalidated line (listing the copies about to be removed) is
    /// journaled first, so a failed append deletes nothing and the thread stays Written with its files. Copies that cannot
    /// be deleted afterwards are reported; the next run overwrites them. Only OperationCanceledException before any write
    /// escapes.
    /// </summary>
    public static async Task<ThreadInvalidationResult> InvalidateAsync(string caseFolder, CorpusPlan plan, CaseGuide guide,
        string threadId, CancellationToken ct = default)
    {
        var result = new ThreadInvalidationResult();
        ct.ThrowIfCancellationRequested();

        var replay = GenerationJournal.Replay(caseFolder, plan);
        if (!replay.Readable)
        {
            result.Errors.Add(replay.Reason.Length > 0 ? replay.Reason : NotWrittenMessage);
            return result;
        }

        var thread = (plan.Threads ?? new List<PlannedThread>())
            .FirstOrDefault(t => t != null && string.Equals(t.Id, threadId, StringComparison.Ordinal));
        if (thread == null
            || string.IsNullOrEmpty(threadId)
            || !replay.LastByThread.TryGetValue(threadId, out var record)
            || record.Type != JournalRecordType.ThreadDone)
        {
            result.Errors.Add(NotWrittenMessage);
            return result;
        }

        // OQ3: thread details plus its committed attachment details, exactly as later waves saw them in the ledger
        var before = GenerationRunState.From(plan, replay);
        var deltas = before.DeltasByThread.TryGetValue(threadId, out var established)
            ? established.ToList()
            : new List<LedgerDelta>();

        // Off the UI thread: the existence checks touch every copy
        var (candidates, existing) = await Task.Run(() => ExistingCopies(caseFolder, guide, thread, record), CancellationToken.None)
            .ConfigureAwait(false);

        // WR-A10: journal before deleting, so the journal never says ThreadDone for a thread whose files are gone
        try
        {
            await new GenerationJournal(caseFolder).AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.ThreadInvalidated,
                PlanId = plan.PlanId,
                ThreadId = threadId,
                LedgerDelta = deltas,
                Files = existing,
                Utc = DateTime.UtcNow
            }, CancellationToken.None).ConfigureAwait(false);
            result.Appended = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing was deleted: the thread stays Written with all its copies (JournalFullException included)
            result.Errors.Add($"{GenerationState.JournalFileName}: {ex.Message}");
            return result;
        }

        // Off the UI thread: lock retries wait up to about a second per file
        await Task.Run(() => DeleteCopies(caseFolder, guide, thread, candidates, existing, result), CancellationToken.None)
            .ConfigureAwait(false);

        // C-Q1: the key is a projection of the journal, so the thread shows Planned right away
        try
        {
            var state = GenerationRunState.From(plan, GenerationJournal.Replay(caseFolder, plan));
            var written = await GenerationOutputs.WriteAnswerKeyAsync(caseFolder, guide, plan, state.BuildFullOverlay(plan, guide))
                .ConfigureAwait(false);
            if (!written.Succeeded)
                result.KeyError = $"{written.FailedFileName}: {written.Error}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The journal line is the durable state; the key is re-projected on the next run or reopen
            result.KeyError = ex.Message;
        }

        return result;
    }

    // Planned copies plus journal-listed files (validated paths only, T-07-48), and the sorted subset that exists now
    private static (List<string> Candidates, List<string> Existing) ExistingCopies(string caseFolder, CaseGuide guide,
        PlannedThread thread, JournalRecord record)
    {
        var folders = PlanNumbering.CustodianFolders(guide);
        var candidates = new List<string>();
        foreach (var message in thread.Messages.Where(m => m != null))
            candidates.AddRange(MailboxWriter.CopyPathsFor(message, folders));
        candidates.AddRange(record.Files);
        candidates = candidates.Distinct(StringComparer.Ordinal).ToList();

        var existing = candidates
            .Where(r => MailboxWriter.ResolveUnderCase(caseFolder, r) is { } full && File.Exists(full))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
        return (candidates, existing);
    }

    // Deletes validated paths only (T-07-48): planned copies through MailboxWriter, journal-listed files through ResolveUnderCase
    private static void DeleteCopies(string caseFolder, CaseGuide guide, PlannedThread thread, List<string> candidates,
        List<string> existing, ThreadInvalidationResult result)
    {
        var folders = PlanNumbering.CustodianFolders(guide);
        var writer = new MailboxWriter(caseFolder, folders);
        var existed = existing.ToHashSet(StringComparer.Ordinal);

        var left = new List<string>();
        foreach (var message in thread.Messages.Where(m => m != null))
            left.AddRange(writer.DeleteCopies(message));

        // Journal-listed files outside the planned copy paths, and locked copies retried with their reason (D-25)
        var stamp = new StartOverResult();
        foreach (var relative in candidates.Where(r => existed.Contains(r)))
        {
            var full = MailboxWriter.ResolveUnderCase(caseFolder, relative);
            if (full != null && File.Exists(full))
                GenerationReset.TryDelete(full, relative, stamp);
        }

        result.Errors.AddRange(stamp.Errors);
        foreach (var relative in left.Distinct(StringComparer.Ordinal))
        {
            if (!stamp.Errors.Any(e => e.StartsWith(relative + ":", StringComparison.Ordinal))
                && MailboxWriter.ResolveUnderCase(caseFolder, relative) is { } full && File.Exists(full))
                result.Errors.Add($"{relative}: The file could not be deleted.");
        }

        result.FilesDeleted = existed
            .Count(r => MailboxWriter.ResolveUnderCase(caseFolder, r) is { } full && !File.Exists(full));
    }
}
