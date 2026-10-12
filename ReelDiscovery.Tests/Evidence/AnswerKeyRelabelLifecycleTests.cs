using System.Text;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// A manual relabel survives every run-side answer-key write (D-02, D-06, D-07, ROADMAP SC1): run checkpoints,
/// Regenerate Thread, Start Over and the reopen/crash-recovery projection. Damaged and locked key files surface as data.
/// </summary>
public class AnswerKeyRelabelLifecycleTests : IDisposable
{
    private static readonly TimeSpan RunGuard = TimeSpan.FromSeconds(60);

    private const string Relevant = AnswerKeyBuilder.RelevantLabel;
    private const string NotRelevant = AnswerKeyBuilder.NotRelevantLabel;
    private const char Bom = (char)0xFEFF;
    private const string CrLf = "\r\n";

    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;

    public AnswerKeyRelabelLifecycleTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = AttachmentTestData.MixedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();

        // Relevant waves with attachments only: no side-world or filler calls needed
        _plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    private CaseGuide Guide => _draft.Guide!;

    private PlannedThread Target => AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);

    private string TargetEmail => Target.Messages[0].DocId;

    private string CsvPath => Path.Combine(_folder, AnswerKeyCsvWriter.FileName);

    private string XlsxPath => Path.Combine(_folder, AnswerKeyXlsxWriter.FileName);

    private async Task FullRunAsync()
    {
        _fake.Respond = AttachmentResponder.Create(_plan, Guide);
        var result = await new GenerationRunner(_gateway)
            .RunAsync(new GenerationRunInput { Draft = _draft, Plan = _plan, CaseFolder = _folder }, new GenerationSnapshot())
            .WaitAsync(RunGuard);
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
    }

    private Task<GenerationFileResult> WriteKey(AnswerKeyOverlay overlay) =>
        GenerationOutputs.WriteAnswerKeyAsync(_folder, Guide, _plan, overlay);

    private AnswerKeyOverlay FullOverlay() =>
        GenerationRunState.From(_plan, GenerationJournal.Replay(_folder, _plan)).BuildFullOverlay(_plan, Guide);

    private string CsvCell(string docId, string column) => CsvCellOf(CsvPath, docId, column);

    private string XlsxRelevance(string docId) =>
        AnswerKeyReader.ParseXlsx(File.ReadAllBytes(XlsxPath)).Rows.Single(r => r.DocId == docId).Relevance;

    private void RelabelCsv(string docId, string relevance) => RelabelCsvFile(CsvPath, docId, relevance);

    private static List<string[]> CsvRows(string path) =>
        GenerationRunnerTests.ParseCsv(File.ReadAllText(path, Encoding.UTF8).TrimStart(Bom));

    /// <summary>One cell of the answer-key CSV row for docId, by header name.</summary>
    internal static string CsvCellOf(string path, string docId, string column)
    {
        var rows = CsvRows(path);
        var doc = Array.IndexOf(rows[0], "DocID");
        var col = Array.IndexOf(rows[0], column);
        return rows.Skip(1).Single(r => r.Length > Math.Max(doc, col) && r[doc] == docId)[col];
    }

    /// <summary>Stand-in for a user editing the Relevance cell in a spreadsheet program and saving the CSV.</summary>
    internal static void RelabelCsvFile(string path, string docId, string relevance)
    {
        var rows = CsvRows(path);
        var doc = Array.IndexOf(rows[0], "DocID");
        var col = Array.IndexOf(rows[0], "Relevance");
        rows.Single(r => r.Length > Math.Max(doc, col) && r[doc] == docId)[col] = relevance;

        var text = new StringBuilder().Append(Bom);
        foreach (var row in rows)
            text.Append(string.Join(",", row.Select(f => "\"" + f.Replace("\"", "\"\"") + "\""))).Append(CrLf);
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    private void AssertRelabelKept(string docId)
    {
        Assert.Equal(NotRelevant, CsvCell(docId, "Relevance"));
        Assert.Equal(NotRelevant, XlsxRelevance(docId));
    }

    [Fact]
    public async Task Relabel_SurvivesCheckpoint()
    {
        await FullRunAsync();
        var docId = TargetEmail;
        Assert.Equal(Relevant, CsvCell(docId, "Relevance"));
        RelabelCsv(docId, NotRelevant);

        // A checkpoint write carries the run overlay for the emails written so far
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[docId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusGenerated };
        var result = await WriteKey(overlay);

        Assert.True(result.Succeeded, result.Error);
        AssertRelabelKept(docId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, CsvCell(docId, "Status"));

        // Continue: the next checkpoint keeps it too
        await WriteKey(FullOverlay());
        AssertRelabelKept(docId);
    }

    [Fact]
    public async Task Relabel_SurvivesRegenerateThread()
    {
        await FullRunAsync();
        var docId = TargetEmail;
        RelabelCsv(docId, NotRelevant);

        var result = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, Target.Id);

        Assert.True(result.Appended);
        Assert.Equal(string.Empty, result.KeyError);
        Assert.Empty(result.KeyBackups);
        AssertRelabelKept(docId);
        Assert.All(Target.Messages, m => Assert.Equal(AnswerKeyBuilder.StatusPlanned, CsvCell(m.DocId, "Status")));
    }

    [Fact]
    public async Task Relabel_SurvivesStartOver()
    {
        await FullRunAsync();
        var docId = TargetEmail;
        RelabelCsv(docId, NotRelevant);

        await GenerationReset.StartOverAsync(_folder, _plan, Guide);
        var result = await WriteKey(new AnswerKeyOverlay());

        Assert.True(result.Succeeded, result.Error);
        AssertRelabelKept(docId);
        Assert.Equal(AnswerKeyBuilder.StatusPlanned, CsvCell(docId, "Status"));
        Assert.True(File.Exists(new AnswerKeyRelabelStore(_folder).FilePath));
    }

    [Fact]
    public async Task Relabel_SurvivesReopenProjection()
    {
        await FullRunAsync();
        var docId = TargetEmail;
        RelabelCsv(docId, NotRelevant);

        // Reopen and crash recovery re-project the key from the journal
        var result = await WriteKey(FullOverlay());

        Assert.True(result.Succeeded, result.Error);
        AssertRelabelKept(docId);
        Assert.StartsWith(AnswerKeyBuilder.StatusGenerated, CsvCell(docId, "Status"));
    }

    [Fact]
    public async Task DamagedKey_BackupReported_NotAnError()
    {
        await WriteKey(new AnswerKeyOverlay());
        File.WriteAllBytes(CsvPath, Encoding.UTF8.GetBytes("this is not an answer key\r\nat all\r\n"));

        var result = await WriteKey(new AnswerKeyOverlay());

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.FailedFileName);
        var backup = Assert.Single(result.Backups);
        Assert.Equal(AnswerKeyCsvWriter.FileName, backup.FileName);
        Assert.True(File.Exists(Path.Combine(_folder, backup.BackupFileName)));
    }

    [Fact]
    public async Task DamagedKey_DuringRegenerateThread_ReportedAsBackup()
    {
        await FullRunAsync();
        File.WriteAllBytes(CsvPath, Encoding.UTF8.GetBytes("this is not an answer key\r\nat all\r\n"));

        var result = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, Target.Id);

        // Pitfall 9: a backup is a notice, never a KeyError (that would trigger the leave-retry loop)
        Assert.Equal(string.Empty, result.KeyError);
        var backup = Assert.Single(result.KeyBackups);
        Assert.Equal(AnswerKeyCsvWriter.FileName, backup.FileName);
    }

    [Fact]
    public async Task LockedKey_ReportedOpenElsewhere()
    {
        await WriteKey(new AnswerKeyOverlay());

        GenerationFileResult result;
        using (new FileStream(CsvPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await WriteKey(new AnswerKeyOverlay());

        Assert.False(result.Succeeded);
        Assert.Equal(AnswerKeyCsvWriter.FileName, result.FailedFileName);
        Assert.True(result.FileOpenElsewhere);
        var written = Assert.Single(result.Files);
        Assert.Equal(CorpusFileKind.AnswerKeyXlsx, written.Kind);
    }
}
