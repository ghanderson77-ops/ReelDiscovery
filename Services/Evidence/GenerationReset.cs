using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Qa;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// What Start Over removed: the number of EML copies deleted and one line per file that could not be deleted.
/// </summary>
public class StartOverResult
{
    public int FilesDeleted { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Start Over: removes the generated relevant and non-relevant emails, the run journal and the story-so-far document so the next run
/// starts clean. The side world and cast voices live in the journal and go with it, so the next run asks for them again.
/// The plan, case.json and the answer key are left alone (the step re-projects the key).
/// </summary>
public static class GenerationReset
{
    public const string JournalKeptMessage =
        "The saved run could not be read in full, so it was kept. Emails it lists may still be in Mailboxes; try Start Over again.";

    // D-25: attempts per file before a lock is reported (waits 100 ms x attempt in between)
    private const int MaxDeleteAttempts = 5;

    /// <summary>
    /// Deletes every EML listed by the journal (any plan) and every planned copy path of every thread (relevant and non-relevant), then the journal (only when
    /// it was read in full) and the story file. Files that cannot be deleted are reported in Errors; only OperationCanceledException escapes.
    /// </summary>
    public static async Task<StartOverResult> StartOverAsync(string caseFolder, CorpusPlan? plan, CaseGuide? guide, CancellationToken ct = default)
    {
        var result = new StartOverResult();
        if (string.IsNullOrWhiteSpace(caseFolder))
            return result;

        // Off the UI thread: thousands of deletes must not freeze the window
        await Task.Run(() => DeleteAll(caseFolder, plan, guide, result, ct), ct).ConfigureAwait(false);
        return result;
    }

    // Deletes only validated paths under Mailboxes plus two constant-name files (UI-SPEC U6, V12)
    private static void DeleteAll(string caseFolder, CorpusPlan? plan, CaseGuide? guide, StartOverResult result,
        CancellationToken ct)
    {
        var complete = GenerationJournal.TryListAllFiles(caseFolder, out var listed);
        var candidates = new List<string>(listed);

        // Planned copies catch files of a thread killed before its journal line was written
        if (plan != null && guide != null)
        {
            var folders = PlanNumbering.CustodianFolders(guide);

            // Pitfall 13: non-relevant threads killed before their journal line leave planned copies too
            foreach (var thread in (plan.Threads ?? new List<PlannedThread>()).Where(t => t != null))
            {
                foreach (var message in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null))
                    candidates.AddRange(MailboxWriter.CopyPathsFor(message, folders));
            }
        }

        foreach (var relative in candidates.Distinct(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var full = MailboxWriter.ResolveUnderCase(caseFolder, relative);
            if (full == null)
                continue;

            if (TryDelete(full, relative, result))
                result.FilesDeleted++;
        }

        ct.ThrowIfCancellationRequested();

        // The journal is the only record of EMLs written under an earlier plan: keep it until it could be read in full,
        // so a later Start Over can still find and remove them
        if (complete)
            TryDelete(GenerationJournal.PathFor(caseFolder), GenerationState.JournalFileName, result);
        else
            result.Errors.Add($"{GenerationState.JournalFileName}: {JournalKeptMessage}");

        // Name = verified case folder name + constant suffix, never a name read from case.json (T-05-33)
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(caseFolder));
        var storyName = StorySoFarDocxBuilder.FileNameFor(folderName);
        TryDelete(Path.Combine(caseFolder, storyName), storyName, result);

        // Constant names only (T-04-26); provider batches are cancelled by the step before this runs. CR-A05: a ledger
        // that could not be read may still list running batches, so it (and their cached results) is kept
        ct.ThrowIfCancellationRequested();
        if (new BatchLedger(caseFolder).Replay().Readable)
        {
            TryDelete(Path.Combine(caseFolder, BatchLedger.FileName), BatchLedger.FileName, result);
            DeleteBatchResults(caseFolder, result);
        }
        else
        {
            result.Errors.Add($"{BatchLedger.FileName}: {BatchLedger.UnreadableMessage}");
        }
        TryDelete(Path.Combine(caseFolder, QaReportStore.ScansFileName), QaReportStore.ScansFileName, result);
        TryDelete(Path.Combine(caseFolder, QaReportStore.LeakageFileName), QaReportStore.LeakageFileName, result);
        TryDelete(Path.Combine(caseFolder, QaReportStore.BlindReviewFileName), QaReportStore.BlindReviewFileName, result);
    }

    // T-07-34: only *.jsonl directly inside the constant results folder; the folder goes when nothing else is left in it
    private static void DeleteBatchResults(string caseFolder, StartOverResult result)
    {
        var folder = Path.Combine(caseFolder, BatchLedger.ResultsFolderName);
        if (!Directory.Exists(folder))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*.jsonl", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{BatchLedger.ResultsFolderName}: {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            // GetFiles' "*.jsonl" also matches longer extensions on Windows (8.3 rule); keep the exact one only
            if (!string.Equals(Path.GetExtension(file), ".jsonl", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileName(file);
            TryDelete(file, $"{BatchLedger.ResultsFolderName}/{name}", result);
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind is harmless; the next Start Over tries again
        }
    }

    // True when an existing file was deleted; a missing file is not an error. Runs inside DeleteAll's Task.Run, so the
    // retry waits never block the UI thread (T-07-09: at most 5 attempts, about 1 s per file)
    internal static bool TryDelete(string fullPath, string displayPath, StartOverResult result)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!File.Exists(fullPath))
                    return false;

                File.Delete(fullPath);
                return true;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxDeleteAttempts)
            {
                // D-25: transient lock (Defender, search indexer, OneDrive), same as CaseStore.WriteAtomic
                Thread.Sleep(100 * attempt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors.Add($"{displayPath}: {ex.Message}");
                return false;
            }
        }
    }
}
