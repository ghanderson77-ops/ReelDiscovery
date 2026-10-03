using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;

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
/// Start Over: removes the generated relevant emails, the run journal and the story-so-far document so the next run
/// starts clean. The plan, case.json and the answer key are left alone (the step re-projects the key).
/// </summary>
public static class GenerationReset
{
    /// <summary>
    /// Deletes every EML listed by the journal (any plan) and every planned relevant copy path, then the journal and the
    /// story file. Files that cannot be deleted are reported in Errors; only OperationCanceledException escapes.
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
        var candidates = new List<string>(GenerationJournal.ListAllFiles(caseFolder));

        // Planned copies catch files of a thread killed before its journal line was written
        if (plan != null && guide != null)
        {
            var folders = PlanNumbering.CustodianFolders(guide);
            foreach (var thread in (plan.Threads ?? new List<PlannedThread>())
                         .Where(t => t != null && t.Relevance == ThreadRelevance.Relevant))
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
        TryDelete(GenerationJournal.PathFor(caseFolder), GenerationState.JournalFileName, result);

        // Name = verified case folder name + constant suffix, never a name read from case.json (T-05-33)
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(caseFolder));
        var storyName = StorySoFarDocxBuilder.FileNameFor(folderName);
        TryDelete(Path.Combine(caseFolder, storyName), storyName, result);
    }

    // True when an existing file was deleted; a missing file is not an error
    private static bool TryDelete(string fullPath, string displayPath, StartOverResult result)
    {
        try
        {
            if (!File.Exists(fullPath))
                return false;

            File.Delete(fullPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"{displayPath}: {ex.Message}");
            return false;
        }
    }
}
