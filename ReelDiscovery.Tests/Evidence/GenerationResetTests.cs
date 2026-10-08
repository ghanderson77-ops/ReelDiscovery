using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
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

    // FileShare.None fails while any other handle is open; antivirus / the search indexer briefly open a just-written EML
    // (with share-delete, so File.Delete is unaffected), which made the exclusive open flaky under full-suite load
    private static async Task<FileStream> OpenExclusiveAsync(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException) when (attempt < 50)
            {
                await Task.Delay(100);
            }
        }
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
    public async Task StartOver_JournalNotReadable_KeepsJournalAndReportsIt()
    {
        // An EML written under an earlier plan: only the journal knows about it
        WriteFile("Mailboxes/x/Inbox/earlier-plan.eml");
        AppendRawRecord(new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            PlanId = Guid.NewGuid(),
            ThreadId = "T-0001",
            Files = new List<string> { "Mailboxes/x/Inbox/earlier-plan.eml" }
        });
        var journal = GenerationJournal.PathFor(_folder);

        StartOverResult result;
        // A reader (antivirus, sync client) that blocks reads but allows delete
        using (new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Write | FileShare.Delete))
        {
            Assert.False(GenerationJournal.TryListAllFiles(_folder, out _));
            result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);
        }

        Assert.True(File.Exists(journal));
        Assert.Contains(result.Errors, e => e.Contains(GenerationReset.JournalKeptMessage));

        // Once readable, Start Over finds the earlier plan's EML and removes the journal
        var retry = await GenerationReset.StartOverAsync(_folder, _plan, Guide);
        Assert.Empty(retry.Errors);
        Assert.False(File.Exists(Path.Combine(_folder, "Mailboxes", "x", "Inbox", "earlier-plan.eml")));
        Assert.False(File.Exists(journal));
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
    public async Task StartOver_OrphanNonRelevantPlannedCopy_IsDeleted()
    {
        // Pitfall 13: a non-relevant thread killed before its journal line leaves its planned copies behind
        var thread = _plan.Threads.First(t => t.Relevance == ThreadRelevance.NotRelevant && t.Messages.Count > 0);
        var paths = thread.Messages.SelectMany(m => MailboxWriter.CopyPathsFor(m, Folders)).Distinct().ToList();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
            WriteFile(path, "partial");

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Empty(result.Errors);
        Assert.Equal(paths.Count, result.FilesDeleted);
        Assert.Empty(EmlFiles());
    }

    [Fact]
    public async Task StartOver_CountsRelevantAndNonRelevantOrphans()
    {
        var relevant = _plan.Threads.First(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0);
        var other = _plan.Threads.First(t => t.Relevance == ThreadRelevance.NotRelevant && t.Messages.Count > 0);
        var paths = new[] { relevant, other }
            .SelectMany(t => t.Messages)
            .SelectMany(m => MailboxWriter.CopyPathsFor(m, Folders))
            .Distinct()
            .ToList();
        foreach (var path in paths)
            WriteFile(path, "partial");

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Equal(paths.Count, result.FilesDeleted);
        Assert.Empty(EmlFiles());
    }

    [Fact]
    public async Task StartOver_UnsafePlannedPath_IsNeverDeleted()
    {
        File.WriteAllText(Path.Combine(_folder, "outside.eml"), "keep");
        File.WriteAllText(Path.Combine(_root, "outside.eml"), "keep");
        var plan = PlanFixtures.Clone(_plan);
        var other = plan.Threads.First(t => t.Relevance == ThreadRelevance.NotRelevant && t.Messages.Count > 0);
        other.Messages[0].FilePath = "Mailboxes/x/Inbox/../../../outside.eml";
        other.Messages[0].CustodianIds = new List<string>();
        if (other.Messages.Count > 1)
            other.Messages[1].FilePath = "../outside.eml";

        var result = await GenerationReset.StartOverAsync(_folder, plan, Guide);

        Assert.Equal(0, result.FilesDeleted);
        Assert.True(File.Exists(Path.Combine(_folder, "outside.eml")));
        Assert.True(File.Exists(Path.Combine(_root, "outside.eml")));
    }

    [Fact]
    public async Task StartOver_AttachmentsRepairedFiles_AreDeleted()
    {
        WriteFile("Mailboxes/x/Inbox/repaired.eml");
        AppendRawRecord(new JournalRecord
        {
            Type = JournalRecordType.AttachmentsRepaired,
            PlanId = _plan.PlanId,
            ThreadId = "T-0001",
            Files = new List<string> { "Mailboxes/x/Inbox/repaired.eml" }
        });

        var result = await GenerationReset.StartOverAsync(_folder, null, null);

        Assert.Equal(1, result.FilesDeleted);
        Assert.Empty(EmlFiles());
        Assert.False(File.Exists(GenerationJournal.PathFor(_folder)));
    }

    [Fact]
    public async Task StartOver_SideWorldAndCastVoices_GoWithTheJournal()
    {
        AppendRawRecord(new JournalRecord
        {
            Type = JournalRecordType.SideWorld,
            PlanId = _plan.PlanId,
            SideWorld = new SideWorld()
        });
        AppendRawRecord(new JournalRecord
        {
            Type = JournalRecordType.CastVoices,
            PlanId = _plan.PlanId,
            CastVoices = new CastVoiceSet()
        });
        var before = GenerationJournal.Replay(_folder, _plan);
        Assert.NotNull(before.LastSideWorld);
        Assert.NotNull(before.LastCastVoices);

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Empty(result.Errors);
        var after = GenerationJournal.Replay(_folder, _plan);
        Assert.Null(after.LastSideWorld);
        Assert.Null(after.LastCastVoices);
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
        using (await OpenExclusiveAsync(locked))
            result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        var error = Assert.Single(result.Errors);
        Assert.Contains(Path.GetFileName(locked), error);
        Assert.Equal(emls.Count - 1, result.FilesDeleted);
        Assert.Equal(new[] { locked }, EmlFiles());
        Assert.False(File.Exists(StoryPath));
    }

    // ----- D-25: transient locks are retried -----

    [Fact]
    public async Task TryDelete_BrieflyLockedFile_RetriesAndDeletes()
    {
        WriteFile("brief.txt");
        var path = Path.Combine(_folder, "brief.txt");
        var result = new StartOverResult();

        var handle = await OpenExclusiveAsync(path);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            handle.Dispose();
        });

        var deleted = await Task.Run(() => GenerationReset.TryDelete(path, "brief.txt", result));
        await release;

        Assert.True(deleted);
        Assert.Empty(result.Errors);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TryDelete_HeldLock_ReportsOneErrorAfterRetries()
    {
        WriteFile("held.txt");
        var path = Path.Combine(_folder, "held.txt");
        var result = new StartOverResult();

        bool deleted;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using (await OpenExclusiveAsync(path))
            deleted = GenerationReset.TryDelete(path, "held.txt", result);
        watch.Stop();

        Assert.False(deleted);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("held.txt: ", error);
        // Waits 100 + 200 + 300 + 400 ms between the 5 attempts
        Assert.True(watch.ElapsedMilliseconds >= 900, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TryDelete_MissingFile_ReturnsFalseWithoutWaiting()
    {
        var result = new StartOverResult();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var deleted = GenerationReset.TryDelete(Path.Combine(_folder, "missing.txt"), "missing.txt", result);

        Assert.False(deleted);
        Assert.Empty(result.Errors);
        Assert.True(watch.ElapsedMilliseconds < 100);
    }

    [Fact]
    public async Task StartOver_DeletesBatchLedgerResultsAndQaReports_OnlyConstantNames()
    {
        WriteFile(BatchLedger.FileName, "{}");
        WriteFile(BatchLedger.ResultsFolderName + "/msgbatch_1.jsonl");
        WriteFile(BatchLedger.ResultsFolderName + "/batch_2.jsonl");
        WriteFile(BatchLedger.ResultsFolderName + "/notes.txt");
        WriteFile(BatchLedger.ResultsFolderName + "/nested/deep.jsonl");
        WriteFile(QaReportStore.ScansFileName, "{}");
        WriteFile(QaReportStore.LeakageFileName, "{}");
        WriteFile(QaReportStore.BlindReviewFileName, "{}");
        WriteFile("qa-other.json", "{}");
        WriteFile("provider-batches.jsonl.bak", "{}");
        var outside = Path.Combine(_root, "elsewhere.jsonl");
        File.WriteAllText(outside, "x");

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Empty(result.Errors);
        Assert.False(File.Exists(Path.Combine(_folder, BatchLedger.FileName)));
        Assert.False(File.Exists(Path.Combine(_folder, BatchLedger.ResultsFolderName, "msgbatch_1.jsonl")));
        Assert.False(File.Exists(Path.Combine(_folder, BatchLedger.ResultsFolderName, "batch_2.jsonl")));
        Assert.False(File.Exists(Path.Combine(_folder, QaReportStore.ScansFileName)));
        Assert.False(File.Exists(Path.Combine(_folder, QaReportStore.LeakageFileName)));
        Assert.False(File.Exists(Path.Combine(_folder, QaReportStore.BlindReviewFileName)));

        // T-07-34: only *.jsonl directly inside the results folder and the constant names
        Assert.True(File.Exists(Path.Combine(_folder, BatchLedger.ResultsFolderName, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(_folder, BatchLedger.ResultsFolderName, "nested", "deep.jsonl")));
        Assert.True(File.Exists(Path.Combine(_folder, "qa-other.json")));
        Assert.True(File.Exists(Path.Combine(_folder, "provider-batches.jsonl.bak")));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task StartOver_EmptyResultsFolder_IsRemoved()
    {
        WriteFile(BatchLedger.ResultsFolderName + "/msgbatch_1.jsonl");

        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Empty(result.Errors);
        Assert.False(Directory.Exists(Path.Combine(_folder, BatchLedger.ResultsFolderName)));
    }

    [Fact]
    public async Task StartOver_EmptyFolder_DoesNothing()
    {
        var result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.Equal(0, result.FilesDeleted);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CRA05_StartOver_UnreadableBatchLedger_IsKept()
    {
        var ledger = new BatchLedger(_folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.OpenAI,
            BatchId = "batch_running",
            PlanId = _plan.PlanId,
            SubmittedUtc = DateTime.UtcNow,
            Items = new List<BatchLedgerItem> { new() { CustomId = "q1", Operation = "Thread", ModelId = "m" } }
        });

        StartOverResult result;

        // Shared for Delete only: the file could be deleted but not read, so its batches are unknown
        using (new FileStream(ledger.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
            result = await GenerationReset.StartOverAsync(_folder, _plan, Guide);

        Assert.True(File.Exists(ledger.FilePath));
        Assert.Contains(result.Errors, e => e.StartsWith(BatchLedger.FileName + ":", StringComparison.Ordinal));
        Assert.Contains("batch_running", new BatchLedger(_folder).Replay().SubmittedById.Keys);
    }
}
