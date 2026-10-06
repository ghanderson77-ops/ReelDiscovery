using System.Text;
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

/// <summary>
/// "Regenerate thread" (D-13/D-15): EML removal, the ThreadInvalidated journal line, replay semantics (pending again,
/// spend kept, established details kept) and the re-projected answer key.
/// </summary>
public class ThreadInvalidationTests : IDisposable
{
    private static readonly TimeSpan RunGuard = TimeSpan.FromSeconds(60);

    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;

    public ThreadInvalidationTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = AttachmentTestData.MixedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();

        // Relevant waves with attachments only: no side-world or filler calls needed
        _plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    private CaseGuide Guide => _draft.Guide!;

    private IReadOnlyDictionary<string, string> Folders => PlanNumbering.CustodianFolders(Guide);

    private PlannedThread Target => AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);

    private List<string> CopiesOf(PlannedThread thread) =>
        thread.Messages.SelectMany(m => MailboxWriter.CopyPathsFor(m, Folders)).Distinct().ToList();

    private string Full(string relative) => Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));

    private async Task FullRunAsync()
    {
        _fake.Respond = AttachmentResponder.Create(_plan, Guide);
        var result = await new GenerationRunner(_gateway)
            .RunAsync(new GenerationRunInput { Draft = _draft, Plan = _plan, CaseFolder = _folder }, new GenerationSnapshot())
            .WaitAsync(RunGuard);
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
    }

    private List<JournalRecord> JournalRecords()
    {
        var records = new List<JournalRecord>();
        foreach (var line in File.ReadAllLines(GenerationJournal.PathFor(_folder)))
        {
            try
            {
                var record = JsonSerializer.Deserialize<JournalRecord>(line, CaseDraft.JsonOptions);
                if (record != null)
                    records.Add(record);
            }
            catch (JsonException)
            {
                // Unknown record type (older build) or torn line
            }
        }

        return records;
    }

    private void AppendRaw(string json) => File.AppendAllText(GenerationJournal.PathFor(_folder), json + "\n");

    private static string Serialize(JournalRecord record) =>
        JsonSerializer.Serialize(record, new JsonSerializerOptions(CaseDraft.JsonOptions) { WriteIndented = false });

    private Dictionary<string, string> KeyStatuses()
    {
        var text = File.ReadAllText(Path.Combine(_folder, AnswerKeyCsvWriter.FileName), Encoding.UTF8);
        var rows = GenerationRunnerTests.ParseCsv(text.TrimStart('﻿'));
        var doc = Array.IndexOf(rows[0], "DocID");
        var status = Array.IndexOf(rows[0], "Status");
        return rows.Skip(1)
            .Where(r => r.Length > Math.Max(doc, status))
            .ToDictionary(r => r[doc], r => r[status], StringComparer.Ordinal);
    }

    private static List<string> Statements(IEnumerable<LedgerDelta> deltas) => deltas.Select(d => d.Statement).ToList();

    [Fact]
    public async Task Invalidate_WrittenRelevantThread_DeletesCopies_JournalsAndReplaysPending()
    {
        await FullRunAsync();
        var target = Target;
        var attachments = target.Messages.SelectMany(m => m.Attachments).Select(a => a.DocId).ToList();
        Assert.NotEmpty(attachments);
        var copies = CopiesOf(target);
        Assert.All(copies, c => Assert.True(File.Exists(Full(c)), c));

        var before = GenerationJournal.Replay(_folder, _plan);
        var beforeState = GenerationRunState.From(_plan, before);
        var established = Statements(beforeState.DeltasByThread[target.Id]);
        Assert.NotEmpty(established);
        Assert.All(attachments, a => Assert.True(before.LastByAttachment.ContainsKey(a), a));
        var xlsx = Path.Combine(_folder, AnswerKeyXlsxWriter.FileName);
        var xlsxBefore = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(xlsx, xlsxBefore);

        var result = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, target.Id);

        Assert.True(result.Appended);
        Assert.Empty(result.Errors);
        Assert.Equal(string.Empty, result.KeyError);
        Assert.Equal(copies.Count, result.FilesDeleted);
        Assert.All(copies, c => Assert.False(File.Exists(Full(c)), c));

        // Other threads keep their copies
        var other = _plan.Threads.First(t => t.Id != target.Id && t.Messages.Count > 0);
        Assert.All(CopiesOf(other), c => Assert.True(File.Exists(Full(c)), c));

        var line = JournalRecords().Last();
        Assert.Equal(JournalRecordType.ThreadInvalidated, line.Type);
        Assert.Equal(target.Id, line.ThreadId);
        Assert.Equal(_plan.PlanId, line.PlanId);
        Assert.Equal(established, Statements(line.LedgerDelta));
        Assert.Equal(copies.OrderBy(c => c, StringComparer.Ordinal), line.Files);

        var replay = GenerationJournal.Replay(_folder, _plan);
        Assert.False(replay.LastByThread.ContainsKey(target.Id));
        Assert.All(attachments, a => Assert.False(replay.LastByAttachment.ContainsKey(a), a));
        Assert.Contains(target.Id, replay.InvalidatedThreadIds);
        Assert.Equal(established, Statements(replay.InvalidatedDeltas[target.Id]));
        Assert.Equal(before.SpentCostUsd, replay.SpentCostUsd);

        var state = GenerationRunState.From(_plan, replay);
        Assert.False(state.IsDone(target.Id));
        Assert.Equal(ThreadRunStatus.Pending, state.StatusByThread[target.Id]);
        Assert.Equal(established, Statements(state.DeltasByThread[target.Id]));
        Assert.Equal(beforeState.CostUsd, state.CostUsd);

        // C-Q1: the on-disk key shows the thread Planned again
        var key = KeyStatuses();
        Assert.All(target.Messages, m => Assert.Equal(AnswerKeyBuilder.StatusPlanned, key[m.DocId]));
        Assert.All(attachments, a => Assert.Equal(AnswerKeyBuilder.StatusPlanned, key[a]));
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, key[other.Messages[0].DocId]);
        Assert.True(File.GetLastWriteTimeUtc(xlsx) > xlsxBefore);
    }

    [Fact]
    public async Task LaterThreadDone_ClearsQueuedState_KeepsDeltas()
    {
        await FullRunAsync();
        var target = Target;
        var done = GenerationJournal.Replay(_folder, _plan).LastByThread[target.Id];
        await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, target.Id);

        AppendRaw(Serialize(done));

        var replay = GenerationJournal.Replay(_folder, _plan);
        Assert.DoesNotContain(target.Id, replay.InvalidatedThreadIds);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[target.Id].Type);
        Assert.True(GenerationRunState.From(_plan, replay).IsDone(target.Id));
    }

    [Fact]
    public async Task LockedCopy_IsReported_AndLineStillAppended()
    {
        await FullRunAsync();
        var target = Target;
        var locked = CopiesOf(target)[0];

        ThreadInvalidationResult result;
        using (new FileStream(Full(locked), FileMode.Open, FileAccess.Read, FileShare.None))
            result = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, target.Id);

        Assert.True(result.Appended);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith(locked + ": ", error, StringComparison.Ordinal);
        Assert.Equal(JournalRecordType.ThreadInvalidated, JournalRecords().Last().Type);
        Assert.DoesNotContain(locked, JournalRecords().Last().Files);
        Assert.True(File.Exists(Full(locked)));
        Assert.False(GenerationRunState.From(_plan, GenerationJournal.Replay(_folder, _plan)).IsDone(target.Id));
    }

    [Fact]
    public async Task UnknownOrUnwrittenThread_NotAppended_NothingDeleted()
    {
        await FullRunAsync();
        var lines = File.ReadAllLines(GenerationJournal.PathFor(_folder)).Length;
        var emls = Directory.GetFiles(Path.Combine(_folder, "Mailboxes"), "*.eml", SearchOption.AllDirectories).Length;

        var unknown = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, "T-9999");
        Assert.False(unknown.Appended);
        Assert.Contains(ThreadInvalidation.NotWrittenMessage, unknown.Errors);

        await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, Target.Id);
        lines++;
        emls -= CopiesOf(Target).Count;

        // Already invalidated: not written any more
        var again = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, Target.Id);
        Assert.False(again.Appended);
        Assert.Contains(ThreadInvalidation.NotWrittenMessage, again.Errors);
        Assert.Equal(0, again.FilesDeleted);

        Assert.Equal(lines, File.ReadAllLines(GenerationJournal.PathFor(_folder)).Length);
        Assert.Equal(emls, Directory.GetFiles(Path.Combine(_folder, "Mailboxes"), "*.eml", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task OlderBuild_SkipsUnknownLine_ReplaysTheRest()
    {
        await FullRunAsync();
        var target = Target;
        var line = Serialize(new JournalRecord
        {
            Type = JournalRecordType.ThreadInvalidated,
            PlanId = _plan.PlanId,
            ThreadId = target.Id
        });

        // An older build has no ThreadInvalidated name: simulate with a name this build does not define either
        AppendRaw(line.Replace("\"ThreadInvalidated\"", "\"SomeFutureRecord\"", StringComparison.Ordinal));

        var replay = GenerationJournal.Replay(_folder, _plan);
        Assert.True(replay.Readable);
        Assert.True(replay.SkippedLines >= 1);
        Assert.True(GenerationRunState.From(_plan, replay).IsDone(target.Id));
        Assert.DoesNotContain(target.Id, replay.InvalidatedThreadIds);
    }

    [Fact]
    public async Task LockedKeyFile_KeyErrorSet_LineStillAppended()
    {
        await FullRunAsync();
        var csv = Path.Combine(_folder, AnswerKeyCsvWriter.FileName);

        ThreadInvalidationResult result;
        using (new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await ThreadInvalidation.InvalidateAsync(_folder, _plan, Guide, Target.Id);

        Assert.True(result.Appended);
        Assert.NotEqual(string.Empty, result.KeyError);
        Assert.Equal(JournalRecordType.ThreadInvalidated, JournalRecords().Last().Type);
    }
}
