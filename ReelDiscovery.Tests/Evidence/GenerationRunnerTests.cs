using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimeKit;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

public partial class GenerationRunnerTests : IDisposable
{
    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;
    private readonly object _lock = new();
    private readonly List<(GenerationCheckpoint Checkpoint, Dictionary<string, string> Key)> _checkpoints = new();

    public GenerationRunnerTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = EvidenceTestData.ApprovedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    [GeneratedRegex(@"Thread id: (T-\d{4})")]
    private static partial Regex ThreadIdLine();

    private IReadOnlyDictionary<string, string> Folders => PlanNumbering.CustodianFolders(_draft.Guide!);

    private List<PlannedThread> Relevant =>
        _plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0).ToList();

    private GenerationRunInput Input => new() { Draft = _draft, Plan = _plan, CaseFolder = _folder };

    private Func<LlmProviderRequest, LlmProviderResponse> Responder() => EvidenceTestData.RenderResponder(_plan, _draft.Guide!);

    private Task Persist(GenerationCheckpoint checkpoint)
    {
        var key = KeyStatuses();
        lock (_lock)
            _checkpoints.Add((checkpoint, key));
        return Task.CompletedTask;
    }

    private List<(GenerationCheckpoint Checkpoint, Dictionary<string, string> Key)> Checkpoints()
    {
        lock (_lock)
            return _checkpoints.ToList();
    }

    private static string ThreadOf(LlmProviderRequest request) =>
        ThreadIdLine().Match(request.UserPrompt ?? string.Empty).Groups[1].Value;

    private List<string> RequestedThreads()
    {
        lock (_fake.Requests)
            return _fake.Requests.Select(ThreadOf).ToList();
    }

    private static string DetailOf(string threadId) => $"site walk {int.Parse(threadId[2..])} -";

    private JournalReplay Replay() => GenerationJournal.Replay(_folder, _plan);

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
                // Torn test line
            }
        }

        return records;
    }

    private string StoryPath => Path.Combine(_folder, StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(_folder)));

    // DocID -> Status from answer-key.csv; empty when the key does not exist yet
    private Dictionary<string, string> KeyStatuses()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(_folder, AnswerKeyCsvWriter.FileName);
        if (!File.Exists(path))
            return map;

        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            return map;
        }

        var rows = ParseCsv(text.TrimStart('﻿'));
        var header = rows[0];
        var docCol = Array.IndexOf(header, "DocID");
        var statusCol = Array.IndexOf(header, "Status");
        foreach (var row in rows.Skip(1).Where(r => r.Length > Math.Max(docCol, statusCol)))
            map[row[docCol]] = row[statusCol];
        return map;
    }

    private static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r')
            {
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                rows.Add(fields.ToArray());
                fields.Clear();
            }
            else
                field.Append(c);
        }

        return rows;
    }

    private List<string> CopyPaths(PlannedThread thread) =>
        thread.Messages.SelectMany(m => MailboxWriter.CopyPathsFor(m, Folders)).Distinct().ToList();

    // Moves every message of a thread by whole days so tests can put two threads in one wave
    private void Shift(string threadId, int days)
    {
        foreach (var message in _plan.Threads.First(t => t.Id == threadId).Messages)
            message.SentAt = message.SentAt.AddDays(days);
    }

    // T-0001 + T-0002 share wave 1, T-0007 + T-0011 share wave 3 (the fixture otherwise has one thread per wave)
    private void MergeWaves()
    {
        Shift("T-0002", -27);
        Shift("T-0011", -33);
    }

    private void AssertThreadOnDisk(PlannedThread thread)
    {
        foreach (var message in thread.Messages)
        {
            var paths = MailboxWriter.CopyPathsFor(message, Folders);
            Assert.NotEmpty(paths);
            foreach (var relative in paths)
            {
                var full = Path.Combine(_folder, relative);
                Assert.True(File.Exists(full), relative);
                using var mime = MimeMessage.Load(full);
                Assert.Equal(message.DocId, mime.Headers[MailboxWriter.DocIdHeader]);
            }
        }
    }

    [Fact]
    public async Task FullRun_WritesEveryCopy_KeyStoryJournalAndCheckpoints()
    {
        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        var waves = WavePlanner.Plan(_plan);

        var result = await new GenerationRunner(_gateway).RunAsync(Input, snapshot, Persist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var replay = Replay();
        foreach (var thread in Relevant)
        {
            var record = replay.LastByThread[thread.Id];
            Assert.Equal(JournalRecordType.ThreadDone, record.Type);
            Assert.Equal(ThreadWriteStatus.Written, record.Status);
            AssertThreadOnDisk(thread);
        }

        var key = KeyStatuses();
        var relevantDocs = Relevant.SelectMany(t => t.Messages).Select(m => m.DocId).ToHashSet();
        foreach (var thread in _plan.Threads)
        {
            foreach (var message in thread.Messages)
            {
                Assert.Equal(relevantDocs.Contains(message.DocId) ? "Generated" : "Planned", key[message.DocId]);
                foreach (var attachment in message.Attachments.Where(a => !string.IsNullOrEmpty(a.DocId)))
                    Assert.Equal("Planned", key[attachment.DocId]);
            }
        }

        Assert.True(File.Exists(StoryPath));

        var checkpoints = Checkpoints();
        Assert.True(checkpoints.Count(c => !c.Checkpoint.IsFinal) >= waves.Count);
        var final = Assert.Single(checkpoints, c => c.Checkpoint.IsFinal).Checkpoint;
        Assert.Equal(GenerationOutcome.Finished, final.Outcome);
        Assert.Contains(final.Files, f => f.Kind == CorpusFileKind.StorySoFar);
        Assert.Contains(final.Files, f => f.Kind == CorpusFileKind.AnswerKeyCsv);

        var view = snapshot.Peek();
        Assert.Equal(Relevant.Count, view.Counts.DoneThreads);
        Assert.False(view.Counts.Running);
        Assert.True(view.Counts.Finished);
        Assert.Contains(view.Events, e => e.Kind == GenerationEventKind.RunStarted);
        Assert.Contains(view.Events, e => e.Kind == GenerationEventKind.RunFinished);

        var last = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, last.Type);
        Assert.Equal(GenerationOutcome.Finished, last.Outcome);
    }

    [Fact]
    public async Task Ledger_LaterWavesSeeEarlierDetails_SameWaveNeverDoes()
    {
        MergeWaves();
        _fake.Respond = Responder();
        var waves = WavePlanner.Plan(_plan);
        var waveOf = WavePlanner.WaveIndexByThread(waves);
        Assert.Contains(waves, w => w.ThreadIds.Count > 1);

        var result = await new GenerationRunner(_gateway).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var prompts = new Dictionary<string, string>(StringComparer.Ordinal);
        lock (_fake.Requests)
            foreach (var request in _fake.Requests)
                prompts[ThreadOf(request)] = request.UserPrompt;

        // Never a same-wave or later-wave detail
        foreach (var thread in Relevant)
        {
            foreach (var other in Relevant.Where(o => o.Id != thread.Id && waveOf[o.Id] >= waveOf[thread.Id]))
                Assert.DoesNotContain(DetailOf(other.Id), prompts[thread.Id]);
        }

        // Every earlier-wave detail the selector shows a participant is in the prompt
        var final = Assert.Single(Checkpoints(), c => c.Checkpoint.IsFinal).Checkpoint;
        var people = PeopleDirectory.Create(_draft.Guide!, _plan);
        var assignments = RenderAssignments.Compute(_plan);
        var visible = 0;
        foreach (var thread in Relevant)
        {
            var ctx = ThreadRenderContext.Create(_draft, _plan, thread, assignments[thread.Id], people, final.Ledger.Entries,
                waveOf[thread.Id]);
            foreach (var entry in ctx.LedgerView)
            {
                Assert.Contains(DetailOf(entry.SourceThreadId), prompts[thread.Id]);
                visible++;
            }
        }

        Assert.True(visible > 0, "expected at least one later-wave thread to see an earlier detail");

        // One unique responder detail per thread
        Assert.Equal(Relevant.Count, final.Ledger.Entries.Count);
        Assert.Equal(Relevant.Count, final.Ledger.Entries.Select(e => e.SourceThreadId).Distinct().Count());
    }

    [Fact]
    public async Task Cancel_KeepsFinishedThreads_ContinueWritesOnlyTheRest()
    {
        var inner = Responder();
        _fake.Respond = inner;
        using var cts = new CancellationTokenSource();
        _fake.OnSend = n =>
        {
            if (n == 3)
                cts.Cancel();
        };
        var snapshot = new GenerationSnapshot();

        var first = await new GenerationRunner(_gateway, 1).RunAsync(Input, snapshot, Persist, cts.Token);

        Assert.Equal(GenerationOutcome.Cancelled, first.Outcome);
        var done = RequestedThreads();
        Assert.Equal(3, done.Count);
        var replay = Replay();
        Assert.Equal(done.OrderBy(i => i), replay.LastByThread.Keys.OrderBy(i => i));
        Assert.All(replay.LastByThread.Values, r => Assert.Equal(JournalRecordType.ThreadDone, r.Type));
        foreach (var thread in Relevant.Where(t => done.Contains(t.Id)))
            AssertThreadOnDisk(thread);
        foreach (var thread in Relevant.Where(t => !done.Contains(t.Id)))
            Assert.All(CopyPaths(thread), p => Assert.False(File.Exists(Path.Combine(_folder, p)), p));

        var key = KeyStatuses();
        foreach (var thread in Relevant)
            foreach (var message in thread.Messages)
                Assert.Equal(done.Contains(thread.Id) ? "Generated" : "Planned", key[message.DocId]);

        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(GenerationOutcome.Cancelled, ended.Outcome);
        Assert.Contains(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.RunCancelled);

        var bytes = Relevant.Where(t => done.Contains(t.Id)).SelectMany(CopyPaths)
            .ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(_folder, p)));

        // Continue
        _fake.OnSend = null;
        var continued = new GenerationSnapshot();
        var second = await new GenerationRunner(_gateway, 1).RunAsync(Input, continued, Persist);

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        var resent = RequestedThreads().Skip(3).ToList();
        Assert.Equal(Relevant.Select(t => t.Id).Except(done).OrderBy(i => i), resent.OrderBy(i => i));
        Assert.Contains(continued.Peek().Events, e => e.Kind == GenerationEventKind.RunContinued);
        foreach (var (path, original) in bytes)
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(_folder, path)));
        foreach (var thread in Relevant)
            AssertThreadOnDisk(thread);
    }

    [Fact]
    public async Task RefusedThread_IsRecordedAndRunContinues_RetryWritesOnlyIt()
    {
        var inner = Responder();
        var target = Relevant[2].Id;
        _fake.Respond = request => ThreadOf(request) == target ? FakeLlmProvider.Refused("cannot help") : inner(request);

        var first = await new GenerationRunner(_gateway).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, first.Outcome);
        var replay = Replay();
        Assert.Equal(JournalRecordType.ThreadFailed, replay.LastByThread[target].Type);
        foreach (var thread in Relevant.Where(t => t.Id != target))
            Assert.Equal(ThreadWriteStatus.Written, replay.LastByThread[thread.Id].Status);
        var key = KeyStatuses();
        foreach (var message in Relevant.First(t => t.Id == target).Messages)
            Assert.Equal("Failed", key[message.DocId]);

        var before = RequestedThreads().Count;
        _fake.Respond = inner;
        var second = await new GenerationRunner(_gateway).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        Assert.Equal(new[] { target }, RequestedThreads().Skip(before));
        Assert.Equal(ThreadWriteStatus.Written, Replay().LastByThread[target].Status);
        Assert.Equal(JournalRecordType.ThreadDone, Replay().LastByThread[target].Type);
    }

    [Fact]
    public async Task AuthFailure_StopsRun_KeepsEarlierThreads()
    {
        var inner = Responder();
        var calls = 0;
        _fake.Respond = request => Interlocked.Increment(ref calls) == 2
            ? FakeLlmProvider.Failed(LlmFailureKind.Auth, "invalid key")
            : inner(request);
        var snapshot = new GenerationSnapshot();

        var result = await new GenerationRunner(_gateway, 1).RunAsync(Input, snapshot, Persist);

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.Contains("invalid key", result.Message);
        var requested = RequestedThreads();
        Assert.Equal(2, requested.Count);
        var replay = Replay();
        Assert.Equal(new[] { requested[0] }, replay.LastByThread.Keys);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[requested[0]].Type);
        AssertThreadOnDisk(Relevant.First(t => t.Id == requested[0]));

        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(GenerationOutcome.Fatal, ended.Outcome);
        Assert.Contains(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.RunStopped && e.Reason.Contains("invalid key"));
        Assert.False(snapshot.Peek().Counts.Finished);
    }

    [Fact]
    public async Task KilledRun_TornJournalAndOrphanFiles_ContinueRewritesThread()
    {
        _fake.Respond = Responder();
        using var cts = new CancellationTokenSource();
        _fake.OnSend = n =>
        {
            if (n == 2)
                cts.Cancel();
        };
        await new GenerationRunner(_gateway, 1).RunAsync(Input, new GenerationSnapshot(), Persist, cts.Token);
        var done = RequestedThreads();
        var orphan = Relevant.First(t => !done.Contains(t.Id));

        // Kill: a torn last line and junk EMLs of a thread that was never journaled
        File.AppendAllText(GenerationJournal.PathFor(_folder), "{\"Type\":\"ThreadDone\",\"PlanId\":\"");
        foreach (var relative in CopyPaths(orphan))
        {
            var full = Path.Combine(_folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "partial");
        }

        _fake.OnSend = null;
        var result = await new GenerationRunner(_gateway, 1).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Contains(orphan.Id, RequestedThreads().Skip(done.Count));
        Assert.Equal(JournalRecordType.ThreadDone, Replay().LastByThread[orphan.Id].Type);
        AssertThreadOnDisk(orphan);
        Assert.All(Relevant, t => Assert.True(Replay().LastByThread.ContainsKey(t.Id)));
    }

    [Fact]
    public async Task PlanChanged_ReturnsNone_WithoutRequests()
    {
        await new GenerationJournal(_folder).AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.RunStarted,
            Utc = DateTime.UtcNow,
            PlanId = Guid.NewGuid()
        });
        _fake.Respond = Responder();

        var result = await new GenerationRunner(_gateway).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.None, result.Outcome);
        Assert.NotEmpty(result.Message);
        Assert.Empty(_fake.Requests);
        Assert.Empty(Checkpoints());
    }

    [Fact]
    public async Task ThreadFinishing_ReprojectsKeyDuringTheWave()
    {
        MergeWaves();
        var inner = Responder();
        var calls = 0;
        _fake.Respond = request =>
        {
            // First thread finishes past the 2 s debounce; the second is still running when it projects
            var n = Interlocked.Increment(ref calls);
            if (n == 1)
                Thread.Sleep(2500);
            else if (n == 2)
                Thread.Sleep(1500);
            return inner(request);
        };
        var waves = WavePlanner.Plan(_plan);
        var wave1 = waves[0].ThreadIds;
        Assert.Equal(2, wave1.Count);

        var result = await new GenerationRunner(_gateway, 1).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var checkpoints = Checkpoints();
        var midWave = checkpoints.Where(c => !c.Checkpoint.IsFinal
                                             && c.Checkpoint.State.Written == 1
                                             && wave1.Count(c.Checkpoint.State.IsDone) == 1)
            .ToList();
        Assert.NotEmpty(midWave);

        var first = wave1.Single(midWave[0].Checkpoint.State.IsDone);
        var second = wave1.Single(id => id != first);
        var key = midWave[0].Key;
        foreach (var message in _plan.Threads.First(t => t.Id == first).Messages)
            Assert.Equal("Generated", key[message.DocId]);
        foreach (var message in _plan.Threads.First(t => t.Id == second).Messages)
            Assert.Equal("Planned", key[message.DocId]);
        Assert.True(checkpoints.Count(c => !c.Checkpoint.IsFinal) > waves.Count);
    }

    [Fact]
    public async Task ExceptionEscapingAWave_StillEndsCleanly()
    {
        _fake.Respond = Responder();
        var waves = WavePlanner.Plan(_plan);
        var runner = new GenerationRunner(_gateway)
        {
            BeforeWaveHook = i => i == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask
        };

        var result = await runner.RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.InternalError, result.Outcome);
        Assert.Equal("boom", result.Message);
        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(GenerationOutcome.InternalError, ended.Outcome);
        Assert.Equal("boom", ended.Message);

        var final = Assert.Single(Checkpoints(), c => c.Checkpoint.IsFinal);
        Assert.Equal(GenerationOutcome.InternalError, final.Checkpoint.Outcome);
        Assert.True(File.Exists(StoryPath));

        var replay = Replay();
        foreach (var id in waves[0].ThreadIds)
        {
            Assert.Equal(ThreadWriteStatus.Written, replay.LastByThread[id].Status);
            AssertThreadOnDisk(_plan.Threads.First(t => t.Id == id));
            foreach (var message in _plan.Threads.First(t => t.Id == id).Messages)
                Assert.Equal("Generated", final.Key[message.DocId]);
        }

        Assert.Equal(waves[0].ThreadIds.Count, replay.LastByThread.Count);
    }

    [Fact]
    public async Task RunAsync_OffUiThread_NeverPostsToCapturedContext()
    {
        _fake.Respond = Responder();
        var runner = new GenerationRunner(_gateway);
        var previous = SynchronizationContext.Current;
        Task<GenerationRunResult> task;
        try
        {
            // Started under a context whose Post throws, as the step does from the UI thread
            SynchronizationContext.SetSynchronizationContext(new ThrowingContext());
            task = Task.Run(() => runner.RunAsync(Input, new GenerationSnapshot(), Persist));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var result = await task;
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
    }

    private sealed class ThrowingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("UI context captured");

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException("UI context captured");
    }
}
