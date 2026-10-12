using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Outcome of re-projecting generation files: the files written and the first failure, if any.
/// </summary>
public class GenerationFileResult
{
    public List<WrittenCorpusFile> Files { get; set; } = new();
    public string FailedFileName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool FileOpenElsewhere { get; set; }

    /// <summary>Damaged key files copied aside before they were rewritten (D-10); reported as a notice, never as KeyError.</summary>
    public List<AnswerKeyBackup> Backups { get; set; } = new();

    public bool Succeeded => FailedFileName.Length == 0;
}

/// <summary>
/// Rewrites the answer key and the story-so-far document from run state (D-05, D-10). Each file is written on its own,
/// so a file open in Excel or Word never stops the others (Pitfall 8). Never touches the draft.
/// </summary>
public static class GenerationOutputs
{
    /// <summary>
    /// Writes answer-key.csv and answer-key.xlsx from the plan with the run-state overlay and the user's manual relabels
    /// (D-01, D-02).
    /// </summary>
    public static async Task<GenerationFileResult> WriteAnswerKeyAsync(string caseFolder, CaseGuide guide, CorpusPlan plan,
        AnswerKeyOverlay overlay, FileWriter? writer = null, CancellationToken ct = default)
    {
        // D-02: the one funnel harvests manual relabels, re-applies them and writes both files (each on its own, Pitfall 8)
        var written = await AnswerKeyFiles.WriteAsync(caseFolder, plan, guide, overlay, writer, stopAtFirstFailure: false, ct)
            .ConfigureAwait(false);
        return new GenerationFileResult
        {
            Files = written.Files,
            FailedFileName = written.FailedFileName,
            Error = written.Error,
            FileOpenElsewhere = written.FileOpenElsewhere,
            Backups = written.Backups
        };
    }

    /// <summary>
    /// Writes "{folder name} - Story So Far.docx" into the case folder.
    /// </summary>
    public static async Task<GenerationFileResult> WriteStorySoFarAsync(string caseFolder, CaseDraft draft, CorpusPlan plan,
        NarrativeLedger ledger, GenerationRunState state, PeopleDirectory people, FileWriter? writer = null,
        CancellationToken ct = default)
    {
        writer ??= CaseStore.WriteAtomicAsync;
        var result = new GenerationFileResult();

        // Name = verified case folder name + constant suffix, never a name read from case.json (T-05-33)
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(caseFolder));
        var fileName = StorySoFarDocxBuilder.FileNameFor(folderName);
        var bytes = StorySoFarDocxBuilder.Build(draft, plan, ledger, state, people);

        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(caseFolder, fileName);
        try
        {
            await writer(path, bytes, ct).ConfigureAwait(false);
            result.Files.Add(Stamp(CorpusFileKind.StorySoFar, fileName, plan.PlanId));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecordFailure(result, fileName, path, ex);
        }

        return result;
    }

    private static WrittenCorpusFile Stamp(CorpusFileKind kind, string fileName, Guid planId) => new()
    {
        Kind = kind,
        FileName = fileName,
        PlanId = planId,
        WrittenUtc = DateTime.UtcNow
    };

    // The first failure is the one reported; later failures only keep their file out of result.Files
    private static void RecordFailure(GenerationFileResult result, string fileName, string path, Exception ex)
    {
        if (result.FailedFileName.Length > 0)
            return;

        result.FailedFileName = fileName;
        result.Error = ex.Message;
        result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
    }
}
