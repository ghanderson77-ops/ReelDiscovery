using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>A damaged key file copied aside before it was rewritten (D-10).</summary>
public class AnswerKeyBackup
{
    public string FileName { get; set; } = string.Empty;
    public string BackupFileName { get; set; } = string.Empty;
}

/// <summary>Outcome of one funnel write: files written, the first failure, backups and the relabels applied.</summary>
public class AnswerKeyWriteResult
{
    public List<WrittenCorpusFile> Files { get; set; } = new();
    public string FailedFileName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool FileOpenElsewhere { get; set; }
    public List<AnswerKeyBackup> Backups { get; set; } = new();
    public int Relabels { get; set; }
    public bool Succeeded => FailedFileName.Length == 0;
}

/// <summary>
/// The one answer-key writer (D-02): harvests user relabels from the key files on disk, keeps them in
/// answer-key-relabels.json, re-applies them to the plan projection and writes answer-key.csv and answer-key.xlsx.
/// AnswerKeyBuilder stays a pure plan projection (KEY-03, D-11).
/// </summary>
public static class AnswerKeyFiles
{
    public static Task<AnswerKeyWriteResult> WriteAsync(string caseFolder, CorpusPlan plan, CaseGuide guide,
        AnswerKeyOverlay? overlay, FileWriter? writer = null, bool stopAtFirstFailure = false, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public static Dictionary<string, AnswerKeyRelabel> PeekRelabels(string caseFolder, CorpusPlan plan, CaseGuide guide) =>
        throw new NotImplementedException();

    public static (byte[] Csv, byte[] Xlsx) BuildBytes(CorpusPlan plan, CaseGuide guide, AnswerKeyOverlay? overlay,
        IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels) =>
        throw new NotImplementedException();

    public static List<AnswerKeyRow> Apply(List<AnswerKeyRow> rows, IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels) =>
        throw new NotImplementedException();
}
