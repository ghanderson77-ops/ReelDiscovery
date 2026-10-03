using System.Text;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationJournalTests : IDisposable
{
    private readonly string _folder;
    private readonly CorpusPlan _plan;

    public GenerationJournalTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "rd-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _plan = new CorpusPlan
        {
            PlanId = Guid.NewGuid(),
            Threads = new List<PlannedThread>
            {
                new() { Id = "T-0003", Relevance = ThreadRelevance.Relevant },
                new() { Id = "T-0005", Relevance = ThreadRelevance.Relevant },
                new() { Id = "T-0007", Relevance = ThreadRelevance.Relevant }
            }
        };
    }

    public void Dispose()
    {
        GenerationJournal.SizeCapOverride = 0;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private JournalRecord Started() => new() { Type = JournalRecordType.RunStarted, PlanId = _plan.PlanId, Utc = DateTime.UtcNow };

    private JournalRecord Done(string threadId, Guid? planId = null, params string[] files) => new()
    {
        Type = JournalRecordType.ThreadDone,
        PlanId = planId ?? _plan.PlanId,
        ThreadId = threadId,
        Status = ThreadWriteStatus.Written,
        Attempts = 1,
        Files = files.Length == 0 ? new List<string> { $"Mailboxes/pat.lee/Sent Items/{threadId}.eml" } : files.ToList()
    };

    private JournalRecord Failed(string threadId) => new()
    {
        Type = JournalRecordType.ThreadFailed,
        PlanId = _plan.PlanId,
        ThreadId = threadId,
        FailureKind = ThreadFailureKind.Refused,
        Reason = "The model declined."
    };

    private string JournalPath => GenerationJournal.PathFor(_folder);

    [Fact]
    public void PathFor_CaseFolderPlusConstant()
    {
        Assert.Equal(Path.Combine(_folder, GenerationState.JournalFileName), GenerationJournal.PathFor(_folder));
        Assert.Equal(JournalPath, new GenerationJournal(_folder).FilePath);
    }

    [Fact]
    public async Task AppendThenReplay_KeepsRecords()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Started());
        await journal.AppendAsync(Done("T-0003"));
        await journal.AppendAsync(Failed("T-0005"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Exists);
        Assert.True(replay.Readable);
        Assert.False(replay.PlanChanged);
        Assert.True(replay.HasRunStarted);
        Assert.Equal(2, replay.LastByThread.Count);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0003"].Type);
        Assert.Equal(JournalRecordType.ThreadFailed, replay.LastByThread["T-0005"].Type);
        Assert.Equal(0, replay.SkippedLines);
        Assert.NotEqual(default, replay.LastWriteUtc);
    }

    [Fact]
    public async Task Replay_LaterRecordWins()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Failed("T-0003"));
        await journal.AppendAsync(Done("T-0003"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread["T-0003"].Type);
    }

    [Fact]
    public async Task Replay_RunEnded_KeepsLast()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Cancelled });
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = _plan.PlanId, Outcome = GenerationOutcome.Finished });

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.NotNull(replay.LastRunEnded);
        Assert.Equal(GenerationOutcome.Finished, replay.LastRunEnded!.Outcome);
        Assert.False(replay.HasRunStarted);
    }

    [Fact]
    public async Task ParallelAppends_OneParseableLineEach()
    {
        var journal = new GenerationJournal(_folder);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => journal.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            PlanId = _plan.PlanId,
            ThreadId = $"T-{i:0000}",
            Reason = new string('x', 2000)
        }))));

        var lines = File.ReadAllLines(JournalPath);
        Assert.Equal(50, lines.Length);
        Assert.All(lines, line => Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<JournalRecord>(line,
            ReelDiscovery.Models.Case.CaseDraft.JsonOptions)));
    }

    [Fact]
    public async Task Replay_TornLastLineAndBlankLine_Skipped()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "\n");
        await journal.AppendAsync(Done("T-0005"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"ThreadDone\",\"PlanId\":\"" + _plan.PlanId + "\",\"Thread");

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Readable);
        Assert.Equal(2, replay.SkippedLines);
        Assert.Equal(2, replay.LastByThread.Count);
        Assert.Equal("Mailboxes/pat.lee/Sent Items/T-0005.eml", Assert.Single(replay.LastByThread["T-0005"].Files));
    }

    [Fact]
    public async Task Append_AfterTornLastLine_StartsFreshLine()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"ThreadDone\",\"PlanId\":\"" + _plan.PlanId + "\",\"Thread");
        await journal.AppendAsync(Done("T-0005"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(1, replay.SkippedLines);
        Assert.Equal(2, replay.LastByThread.Count);
        Assert.True(replay.LastByThread.ContainsKey("T-0005"));
    }

    [Fact]
    public async Task Replay_UnknownTypeUnknownThreadForeignPlan_Dropped()
    {
        var journal = new GenerationJournal(_folder);
        var foreign = Guid.NewGuid();
        await journal.AppendAsync(Done("T-0003"));
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":\"Exploded\",\"PlanId\":\"" + _plan.PlanId + "\",\"ThreadId\":\"T-0005\"}\n");
        await File.AppendAllTextAsync(JournalPath, "{\"Type\":42,\"PlanId\":\"" + _plan.PlanId + "\",\"ThreadId\":\"T-0005\"}\n");
        await journal.AppendAsync(Done("T-9999"));
        await journal.AppendAsync(Done("T-0007", foreign));
        await journal.AppendAsync(Done("T-0008", foreign));
        await journal.AppendAsync(Done("T-0008", foreign));
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.ThreadFailed, PlanId = foreign, ThreadId = "T-0009" });

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Readable);
        Assert.Equal(new[] { "T-0003" }, replay.LastByThread.Keys.ToArray());
        Assert.True(replay.PlanChanged);
        Assert.Equal(foreign, replay.JournalPlanId);
        Assert.Equal(2, replay.ForeignThreadCount);
    }

    [Fact]
    public async Task Replay_UnsafeFiles_RemovedByNormalize()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003", null, "../x.eml", "Mailboxes/pat.lee/Inbox/PL-000001.eml", "C:\\Windows\\x.eml"));

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.Equal(new[] { "Mailboxes/pat.lee/Inbox/PL-000001.eml" }, replay.LastByThread["T-0003"].Files);
    }

    [Fact]
    public async Task Replay_Oversize_ReasonNoThrow()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003"));
        await journal.AppendAsync(Done("T-0005"));
        GenerationJournal.SizeCapOverride = 100;

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Exists);
        Assert.False(replay.Readable);
        Assert.Equal("The saved run is larger than 200 MB.", replay.Reason);
        Assert.Empty(replay.LastByThread);
    }

    [Fact]
    public void Replay_Missing_NotExists()
    {
        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.False(replay.Exists);
        Assert.Equal(string.Empty, replay.Reason);
        Assert.False(GenerationJournal.Exists(_folder));
    }

    [Fact]
    public async Task Replay_Locked_ReasonCouldNotBeRead()
    {
        await new GenerationJournal(_folder).AppendAsync(Done("T-0003"));

        using (new FileStream(JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var replay = GenerationJournal.Replay(_folder, _plan);

            Assert.True(replay.Exists);
            Assert.StartsWith("The saved run could not be read:", replay.Reason);
            Assert.EndsWith(".", replay.Reason);
        }
    }

    [Fact]
    public async Task Replay_GarbageOnly_SkipsEveryLine()
    {
        await File.WriteAllTextAsync(JournalPath, "not json\nnull\n[1,2]\n", Encoding.UTF8);

        var replay = GenerationJournal.Replay(_folder, _plan);

        Assert.True(replay.Readable);
        Assert.Equal(3, replay.SkippedLines);
        Assert.Empty(replay.LastByThread);
    }

    [Fact]
    public async Task ListAllFiles_EveryThreadDone_AnyPlan_SafeDistinct()
    {
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(Done("T-0003", null, "Mailboxes/a/Inbox/PL-000001.eml", "../x.eml"));
        await journal.AppendAsync(Done("T-0003", null, "Mailboxes/a/Inbox/PL-000001.eml"));
        await journal.AppendAsync(Done("T-0004", Guid.NewGuid(), "Mailboxes/b/Sent Items/PL-000002.eml"));
        await journal.AppendAsync(Failed("T-0005"));

        var files = GenerationJournal.ListAllFiles(_folder);

        Assert.Equal(new[] { "Mailboxes/a/Inbox/PL-000001.eml", "Mailboxes/b/Sent Items/PL-000002.eml" }, files);
    }

    [Fact]
    public void ListAllFiles_Missing_Empty()
    {
        Assert.Empty(GenerationJournal.ListAllFiles(_folder));
    }

    [Fact]
    public async Task Delete_RemovesJournal_NoOpWhenMissing()
    {
        await new GenerationJournal(_folder).AppendAsync(Started());
        Assert.True(GenerationJournal.Exists(_folder));

        GenerationJournal.Delete(_folder);
        Assert.False(File.Exists(JournalPath));

        GenerationJournal.Delete(_folder);
        Assert.False(GenerationJournal.Exists(_folder));
    }
}
