using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationResetTests : IDisposable
{
    private readonly string _root;
    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;

    public GenerationResetTests()
    {
        // Case folder inside a scratch root so "outside the case folder" files can be checked too
        _root = EvidenceTestData.TempCaseFolder();
        _folder = Path.Combine(_root, "Window Dispute");
        Directory.CreateDirectory(_folder);
        (_draft, _plan) = EvidenceTestData.ApprovedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_root);

    private CaseGuide Guide => _draft.Guide!;

    private IReadOnlyDictionary<string, string> Folders => PlanNumbering.CustodianFolders(Guide);

    private string StoryPath => Path.Combine(_folder, StorySoFarDocxBuilder.FileNameFor("Window Dispute"));

    private List<string> EmlFiles() =>
        Directory.Exists(Path.Combine(_folder, "Mailboxes"))
            ? Directory.GetFiles(Path.Combine(_folder, "Mailboxes"), "*.eml", SearchOption.AllDirectories).ToList()
            : new List<string>();

    private async Task FullRunAsync()
    {
        _fake.Respond = EvidenceTestData.RenderResponder(_plan, Guide);
        var result = await new GenerationRunner(_gateway).RunAsync(
            new GenerationRunInput { Draft = _draft, Plan = _plan, CaseFolder = _folder }, new GenerationSnapshot());
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
    }

    private void WriteFile(string relative, string text = "x")
    {
        var full = Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private void AppendRawRecord(JournalRecord record) =>
        File.AppendAllText(GenerationJournal.PathFor(_folder),
            JsonSerializer.Serialize(record, new JsonSerializerOptions(CaseDraft.JsonOptions) { WriteIndented = false }) + "\n");

    [Fact]
    public async Task StartOver_AfterFullRun_DeletesGeneratedOutputOnly()
    {
        await FullRunAsync();
        File.WriteAllText(Path.Combine(_folder, "corpus-plan.json"), "{}");
        File.WriteAllText(Path.Combine(_folder, "case.json"), "{}");
        File.WriteAllText(Path.Combine(_folder, "notes.eml"), "keep");
        File.WriteAllText(Path.Combine(_root, "neighbor.eml"), "keep");
        var listed = GenerationJournal.ListAllFiles(_folder);
        var emls = EmlFiles();
        Assert.NotEmpty(emls);
        Assert.True(File.Exists(StoryPath));

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Empty(result.Errors);
        Assert.Equal(listed.Count, result.FilesDeleted);
        Assert.Equal(emls.Count, result.FilesDeleted);
        Assert.Empty(EmlFiles());
        Assert.False(File.Exists(GenerationJournal.PathFor(_folder)));
        Assert.False(File.Exists(StoryPath));
        Assert.True(File.Exists(Path.Combine(_folder, "corpus-plan.json")));
        Assert.True(File.Exists(Path.Combine(_folder, "case.json")));
        Assert.True(File.Exists(Path.Combine(_folder, AnswerKeyCsvWriter.FileName)));
        Assert.True(File.Exists(Path.Combine(_folder, AnswerKeyXlsxWriter.FileName)));
        Assert.True(File.Exists(Path.Combine(_folder, "notes.eml")));
        Assert.True(File.Exists(Path.Combine(_root, "neighbor.eml")));
    }

    [Fact]
    public async Task StartOver_TraversalPathInJournal_NeverLeavesMailboxes()
    {
        File.WriteAllText(Path.Combine(_folder, "outside.eml"), "keep");
        File.WriteAllText(Path.Combine(_root, "outside.eml"), "keep");
        WriteFile("Mailboxes/x/Inbox/real.eml");
        AppendRawRecord(new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            PlanId = _plan.PlanId,
            ThreadId = "T-0001",
            Files = new List<string>
            {
                "Mailboxes/x/Inbox/../../../outside.eml",
                "Mailboxes/x/Inbox/../../../../outside.eml",
                "Mailboxes/x/Inbox/real.eml"
            }
        });

        var result = await GenerationReset.StartOverAsync(_folder, null, null);

        Assert.Equal(1, result.FilesDeleted);
        Assert.True(File.Exists(Path.Combine(_folder, "outside.eml")));
        Assert.True(File.Exists(Path.Combine(_root, "outside.eml")));
        Assert.False(File.Exists(Path.Combine(_folder, "Mailboxes", "x", "Inbox", "real.eml")));
        Assert.False(File.Exists(GenerationJournal.PathFor(_folder)));
    }

    [Fact]
    public async Task StartOver_OrphanPlannedCopyWithoutJournal_IsDeleted()
    {
        var thread = _plan.Threads.First(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0);
        var paths = thread.Messages.SelectMany(m => MailboxWriter.CopyPathsFor(m, Folders)).Distinct().ToList();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
            WriteFile(path, "partial");

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Equal(paths.Count, result.FilesDeleted);
        Assert.Empty(EmlFiles());
    }

    [Fact]
    public async Task StartOver_JournalForAnotherPlan_StillDeletesListedFiles()
    {
        WriteFile("Mailboxes/old/Sent Items/OLD-000001.eml");
        AppendRawRecord(new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = Guid.NewGuid() });
        AppendRawRecord(new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            PlanId = Guid.NewGuid(),
            ThreadId = "T-0099",
            Files = new List<string> { "Mailboxes/old/Sent Items/OLD-000001.eml" }
        });

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Equal(1, result.FilesDeleted);
        Assert.Empty(EmlFiles());
        Assert.False(File.Exists(GenerationJournal.PathFor(_folder)));
    }

    [Fact]
    public async Task StartOver_LockedFile_IsReported_RestStillDeleted()
    {
        await FullRunAsync();
        var emls = EmlFiles();
        var locked = emls[0];

        StartOverResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        var error = Assert.Single(result.Errors);
        Assert.Contains(Path.GetFileName(locked), error);
        Assert.Equal(emls.Count - 1, result.FilesDeleted);
        Assert.Equal(new[] { locked }, EmlFiles());
        Assert.False(File.Exists(StoryPath));
    }

    [Fact]
    public async Task StartOver_EmptyFolder_DoesNothing()
    {
        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Equal(0, result.FilesDeleted);
        Assert.Empty(result.Errors);
    }
}
