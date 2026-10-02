using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

public delegate Task FileWriter(string path, byte[] bytes, CancellationToken ct);

public delegate Task PlanSaver(string caseFolder, CorpusPlan plan, CancellationToken ct);

public class ApprovalResult
{
    public bool Succeeded { get; set; }
    public List<WrittenCorpusFile> Files { get; set; } = new();
    public string FailedFileName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool FileOpenElsewhere { get; set; }
}

/// <summary>
/// Review Plan state transitions (D-04, D-18, D-25, D-26) without UI: answer-key/protocol writes and approval.
/// </summary>
public static class CorpusApproval
{
    /// <summary>
    /// The three approval files in write order: answer-key.csv, answer-key.xlsx, '{folder} - Review Protocol.docx'.
    /// </summary>
    public static List<(CorpusFileKind Kind, string FileName, byte[] Bytes)> BuildFiles(CaseDraft draft, CorpusPlan plan,
        string folderName)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var rows = AnswerKeyBuilder.Build(plan, guide);
        return new List<(CorpusFileKind Kind, string FileName, byte[] Bytes)>
        {
            (CorpusFileKind.AnswerKeyCsv, AnswerKeyCsvWriter.FileName, AnswerKeyCsvWriter.Build(rows)),
            (CorpusFileKind.AnswerKeyXlsx, AnswerKeyXlsxWriter.FileName, AnswerKeyXlsxWriter.Build(rows, guide)),
            (CorpusFileKind.ReviewProtocol, ReviewProtocolBuilder.FileNameFor(folderName),
                ReviewProtocolBuilder.Build(draft, plan.Protocol ?? new ReviewProtocolProse()))
        };
    }

    /// <summary>
    /// Writes the three files into the case folder, stopping at the first failure. Never touches the draft.
    /// </summary>
    public static async Task<ApprovalResult> WriteFilesAsync(string caseFolder, CaseDraft draft, CorpusPlan plan,
        FileWriter? writer = null, CancellationToken ct = default)
    {
        writer ??= CaseStore.WriteAtomicAsync;
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(caseFolder));
        var result = new ApprovalResult();

        foreach (var (kind, fileName, bytes) in BuildFiles(draft, plan, folderName))
        {
            ct.ThrowIfCancellationRequested();
            // Paths = case folder + constant names only; WrittenFiles from case.json never build paths (T-04-68)
            var path = Path.Combine(caseFolder, fileName);
            try
            {
                await writer(path, bytes, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Succeeded = false;
                result.FailedFileName = fileName;
                result.Error = ex.Message;
                result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
                return result;
            }

            result.Files.Add(new WrittenCorpusFile
            {
                Kind = kind,
                FileName = fileName,
                PlanId = plan.PlanId,
                WrittenUtc = DateTime.UtcNow
            });
        }

        result.Succeeded = true;
        return result;
    }

    /// <summary>
    /// Records approval after all three files were written; the written file list replaces the previous one.
    /// </summary>
    public static void RecordApproval(CaseDraft draft, CorpusPlan plan, IReadOnlyList<WrittenCorpusFile> files, DateTime utcNow)
    {
        draft.Corpus ??= new CorpusState();
        draft.Corpus.Approved = true;
        draft.Corpus.ApprovedUtc = utcNow;
        draft.Corpus.WrittenFiles = files.ToList();
        draft.Corpus.DismissedStaleGuideHash = string.Empty;
        // No-op once the plan was first saved (D-04 decision); kept so approval always lands at ReviewPlan
        draft.AdvanceStage(CaseStage.ReviewPlan);
    }

    /// <summary>
    /// Writes the answer key and review protocol, then records approval only if every write succeeded (D-26, T-04-70).
    /// The caller saves case.json afterwards.
    /// </summary>
    public static async Task<ApprovalResult> ApproveAsync(string caseFolder, CaseDraft draft, CorpusPlan plan, FileWriter? writer = null, CancellationToken ct = default)
    {
        var result = await WriteFilesAsync(caseFolder, draft, plan, writer, ct);
        if (result.Succeeded)
            RecordApproval(draft, plan, result.Files, DateTime.UtcNow);
        return result;
    }
}
