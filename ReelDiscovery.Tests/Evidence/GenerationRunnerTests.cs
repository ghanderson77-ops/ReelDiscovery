using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MimeKit;
using ReelDiscovery.Models;
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

        // Phase 5 cases: relevant threads without attachments, so the run writes exactly the relevant waves (the mixed
        // corpus is covered by GenerationRunnerMixedTests)
        _plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
        foreach (var message in _plan.Threads.SelectMany(t => t.Messages))
            message.Attachments.Clear();
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

    internal static List<string[]> ParseCsv(string text)
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
        var retrySnapshot = new GenerationSnapshot();
        var second = await new GenerationRunner(_gateway).RunAsync(Input, retrySnapshot, Persist);

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        Assert.Equal(new[] { target }, RequestedThreads().Skip(before));

        // The live view no longer counts the fixed failure (WR-01)
        var counts = retrySnapshot.Peek().Counts;
        Assert.Equal(0, counts.FailedThreads);
        Assert.Equal(Relevant.Count, counts.DoneThreads);
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
        Assert.Equal(ended.CostUsd + replay.LastByThread[requested[0]].CostUsd, Replay().SpentCostUsd);
        Assert.Equal(Replay().SpentCostUsd, result.State.CostUsd);
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
    public async Task PersistThrowingCancellation_RunStillEndsCleanly()
    {
        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        Task ThrowingPersist(GenerationCheckpoint checkpoint) =>
            checkpoint.IsFinal ? Task.FromException(new TaskCanceledException("save cancelled")) : Persist(checkpoint);

        var result = await new GenerationRunner(_gateway).RunAsync(Input, snapshot, ThrowingPersist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var view = snapshot.Peek();
        Assert.False(view.Counts.Running);
        Assert.True(view.Counts.Finished);
        Assert.Contains(view.Events, e => e.Kind == GenerationEventKind.RunFinished);
        Assert.Equal(JournalRecordType.RunEnded, JournalRecords().Last().Type);
    }

    [Fact]
    public async Task DuplicateThreadIdInPlan_IsRenderedOnce()
    {
        _fake.Respond = Responder();
        var duplicate = Relevant[0];
        _plan.Threads.Add(duplicate);

        var result = await new GenerationRunner(_gateway).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Single(RequestedThreads(), id => id == duplicate.Id);
        Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.ThreadDone && r.ThreadId == duplicate.Id);
    }

    [Fact]
    public async Task UnreadableJournalMidRun_KeepsLastGoodKeyAndState()
    {
        _fake.Respond = Responder();
        var waves = WavePlanner.Plan(_plan);
        Assert.True(waves.Count > 1);
        FileStream? lockStream = null;
        var runner = new GenerationRunner(_gateway, 1)
        {
            // From wave 2 on the journal is locked (antivirus, sync client): appends and replays both fail
            BeforeWaveHook = i =>
            {
                if (i == 1 && lockStream == null)
                    lockStream = new FileStream(GenerationJournal.PathFor(_folder), FileMode.Open, FileAccess.Read, FileShare.None);
                return Task.CompletedTask;
            }
        };

        GenerationRunResult result;
        try
        {
            result = await runner.RunAsync(Input, new GenerationSnapshot(), Persist);
        }
        finally
        {
            lockStream?.Dispose();
        }

        var firstWave = waves[0].ThreadIds;
        Assert.Equal(firstWave.Count, result.State.Written);

        // The key still lists the first wave as generated rather than every row back to Planned
        var key = KeyStatuses();
        foreach (var id in firstWave)
            foreach (var message in _plan.Threads.First(t => t.Id == id).Messages)
                Assert.Equal("Generated", key[message.DocId]);

        // No checkpoint after the lock persisted an empty run over the first wave's state
        var persisted = Checkpoints();
        Assert.NotEmpty(persisted);
        Assert.All(persisted, c => Assert.Equal(firstWave.Count, c.Checkpoint.State.Written));
        Assert.All(persisted, c => Assert.NotEmpty(c.Checkpoint.Ledger.Entries));
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

/// <summary>
/// Runner over the Phase 6 mixed corpus (AttachmentTestData.MixedCase): attachments inside relevant waves, the voice set,
/// committed-only ledger merges, invite schedules across waves, chain scheduling and honest unjournaled spend (06-19).
/// </summary>
public partial class GenerationRunnerMixedTests : IDisposable
{
    // Every run is guarded so a scheduling bug fails the test instead of hanging the suite
    private static readonly TimeSpan RunGuard = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ChainGuard = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions DtoOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;
    private readonly AIModelConfig _story;
    private readonly AIModelConfig _volume;
    private readonly List<string> _folders = new();

    public GenerationRunnerMixedTests()
    {
        _folder = NewFolder();
        (_draft, _plan) = AttachmentTestData.MixedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();
        _story = _gateway.ResolveModel(LlmTier.Story);
        _volume = _gateway.ResolveModel(LlmTier.Volume);

        // Priced so Story and Volume spend can be told apart (D-29)
        _story.InputTokenPricePerMillion = 3m;
        _story.OutputTokenPricePerMillion = 15m;
        _volume.InputTokenPricePerMillion = 1m;
        _volume.OutputTokenPricePerMillion = 5m;
    }

    public void Dispose()
    {
        foreach (var folder in _folders)
            EvidenceTestData.DeleteFolder(folder);
    }

    [GeneratedRegex(@"Thread id: (T-\d{4})")]
    private static partial Regex ThreadIdLine();

    [GeneratedRegex(@"Attachment id: (\S+)")]
    private static partial Regex AttachmentIdLine();

    [GeneratedRegex(@"^DTSTART[^:\r\n]*:(\S+)", RegexOptions.Multiline)]
    private static partial Regex DtStartLine();

    [GeneratedRegex(@"^DTEND[^:\r\n]*:(\S+)", RegexOptions.Multiline)]
    private static partial Regex DtEndLine();

    private static string RenderSchema => JsonSchemaRegistry.GetSchemaName(typeof(ThreadRenderer.ThreadRenderDto));
    private static string FillerSchema => JsonSchemaRegistry.GetSchemaName(typeof(FillerRenderer.FillerThreadDto));
    private static string VoiceSchema => JsonSchemaRegistry.GetSchemaName(typeof(CastVoiceBuilder.CastVoiceDto));
    private static string SideWorldSchema => JsonSchemaRegistry.GetSchemaName(typeof(SideWorldBuilder.SideWorldDto));

    private CaseGuide Guide => _draft.Guide!;

    private IReadOnlyDictionary<string, string> Folders => PlanNumbering.CustodianFolders(Guide);

    private List<PlannedThread> Relevant => _plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0).ToList();

    private List<PlannedThread> NonRelevant => _plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant && t.Messages.Count > 0).ToList();

    private string NewFolder()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        _folders.Add(folder);
        return folder;
    }

    private GenerationRunInput Input(IMediaGenerator? media = null, string? folder = null) =>
        new() { Draft = _draft, Plan = _plan, CaseFolder = folder ?? _folder, Media = media };

    private Task<GenerationRunResult> Run(GenerationRunner runner, GenerationRunInput input, GenerationSnapshot? snapshot = null,
        CancellationToken ct = default) =>
        runner.RunAsync(input, snapshot ?? new GenerationSnapshot(), null, ct).WaitAsync(RunGuard);

    private static string ThreadOf(LlmProviderRequest request) =>
        ThreadIdLine().Match(request.UserPrompt ?? string.Empty).Groups[1].Value;

    private static string? AttachmentIdOf(LlmProviderRequest request)
    {
        var match = AttachmentIdLine().Match(request.UserPrompt ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    private List<LlmProviderRequest> Requests()
    {
        lock (_fake.Requests)
            return _fake.Requests.ToList();
    }

    private string PromptFor(string attachmentDocId) => Requests().Last(r => AttachmentIdOf(r) == attachmentDocId).UserPrompt!;

    private string RenderPromptFor(string threadId) =>
        Requests().Last(r => r.SchemaName == RenderSchema && ThreadOf(r) == threadId).UserPrompt!;

    /// <summary>
    /// AttachmentResponder (thread renders, attachments, filler and voices) plus deterministic side projects;
    /// <paramref name="intercept"/> may answer first (null = default).
    /// </summary>
    private Func<LlmProviderRequest, LlmProviderResponse> Responder(Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null,
        Func<string, int, string?>? overrideJson = null)
    {
        var inner = AttachmentResponder.Create(_plan, Guide, overrideJson);
        var sideWorld = JsonSerializer.Serialize(SideWorldDto(), DtoOptions);
        return request => intercept?.Invoke(request)
                          ?? (request.SchemaName == SideWorldSchema ? FakeLlmProvider.Ok(sideWorld, 700, 500) : inner(request));
    }

    private SideWorldBuilder.SideWorldDto SideWorldDto()
    {
        var dto = new SideWorldBuilder.SideWorldDto();
        var organizations = SideWorldBuilder.CoveredOrganizations(Guide);
        for (var i = 0; i < organizations.Count; i++)
        {
            dto.Organizations.Add(new SideWorldBuilder.SideWorldOrgDto
            {
                OrganizationId = organizations[i].Id,
                Projects = new()
                {
                    new() { Name = $"Lakeview Clinic Account {i + 1}", Topics = new() { "renewal pricing", "kickoff scheduling" } },
                    new() { Name = $"Northgate Office Fitout {i + 1}", Topics = new() { "furniture delivery", "badge access" } }
                }
            });
        }

        return dto;
    }

    private static LlmProviderResponse Fatal(int input = 0, int output = 0) => new()
    {
        Outcome = LlmOutcome.Failed,
        FailureKind = LlmFailureKind.Auth,
        Message = "invalid key",
        InputTokens = input,
        OutputTokens = output
    };

    private JournalReplay Replay(string? folder = null) => GenerationJournal.Replay(folder ?? _folder, _plan);

    private List<JournalRecord> JournalRecords(string? folder = null)
    {
        var records = new List<JournalRecord>();
        foreach (var line in File.ReadAllLines(GenerationJournal.PathFor(folder ?? _folder)))
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

    // DocID -> (Status, File Name) from answer-key.csv
    private Dictionary<string, (string Status, string FileName)> KeyRows(string? folder = null)
    {
        var text = File.ReadAllText(Path.Combine(folder ?? _folder, AnswerKeyCsvWriter.FileName), Encoding.UTF8);
        var rows = GenerationRunnerTests.ParseCsv(text.TrimStart('﻿'));
        var header = rows[0];
        var doc = Array.IndexOf(header, "DocID");
        var status = Array.IndexOf(header, "Status");
        var name = Array.IndexOf(header, "File Name");
        return rows.Skip(1)
            .Where(r => r.Length > Math.Max(doc, Math.Max(status, name)))
            .ToDictionary(r => r[doc], r => (r[status], r[name]), StringComparer.Ordinal);
    }

    private static List<MimeEntity> DocParts(MimeMessage message) =>
        message.BodyParts.Where(p => p.Headers.Contains(MailboxWriter.DocIdHeader)).ToList();

    private MimeMessage LoadCopy(PlannedMessage message, string? folder = null) =>
        MimeMessage.Load(Path.Combine(folder ?? _folder, MailboxWriter.CopyPathsFor(message, Folders)[0]));

    private static PlannedAttachment Spec(PlannedThread thread, int seq, AttachmentType kind) =>
        thread.Messages.Single(m => m.Seq == seq).Attachments.First(a => a.Kind == kind);

    private static IEnumerable<PlannedAttachment> AttachmentsOf(PlannedThread thread) =>
        thread.Messages.OrderBy(m => m.Seq).SelectMany(m => m.Attachments).Where(a => !string.IsNullOrEmpty(a.DocId));

    private Dictionary<string, int> WaveOf() => WavePlanner.WaveIndexByThread(WavePlanner.Plan(_plan));

    private void Shift(string threadId, TimeSpan by)
    {
        foreach (var message in AttachmentTestData.Thread(_plan, threadId).Messages)
            message.SentAt = message.SentAt.Add(by);
    }

    // Voices the AttachmentResponder picks: the i-th ordered cast member gets ordinal voice (i + 2) % 6
    private CastVoiceSet ResponderVoices()
    {
        var ordered = CastVoiceSet.AllowedVoices.OrderBy(v => v, StringComparer.Ordinal).ToArray();
        var set = new CastVoiceSet();
        var cast = CastVoiceBuilder.OrderedCast(Guide);
        for (var i = 0; i < cast.Count; i++)
            set.Voices.Add(new CastVoice { PersonId = cast[i].Id.Trim(), Voice = ordered[(i + 2) % ordered.Length] });
        set.Normalize();
        return set;
    }

    // Callers of every voicemail spoken in the journaled run (AttachmentDone records only)
    private List<string> SpokenCallers(string? folder = null)
    {
        var replay = Replay(folder);
        var callers = new List<string>();
        foreach (var thread in _plan.Threads)
        {
            foreach (var message in thread.Messages)
            {
                foreach (var attachment in message.Attachments.Where(a => a.Kind == AttachmentType.Voicemail))
                {
                    if (replay.LastByAttachment.TryGetValue(attachment.DocId, out var record) && record.Type == JournalRecordType.AttachmentDone)
                        callers.Add(message.FromId);
                }
            }
        }

        return callers;
    }

    private static (string Start, string End) IcsTimes(MimeMessage message, string inviteDocId)
    {
        var part = (MimePart)DocParts(message).Single(p => p.Headers[MailboxWriter.DocIdHeader] == inviteDocId);
        using var stream = new MemoryStream();
        part.Content!.DecodeTo(stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        return (DtStartLine().Match(text).Groups[1].Value, DtEndLine().Match(text).Groups[1].Value);
    }

    /// <summary>
    /// Wraps the fake so every call yields and waits <paramref name="delayMs"/>: units then truly interleave on the
    /// thread pool, as with a real provider.
    /// </summary>
    private LlmGateway DelayedGateway(Func<LlmProviderRequest, int> delayMs) =>
        new(new DelayedProvider(_fake, delayMs), _story, _volume);

    private sealed class DelayedProvider : ILlmProvider
    {
        private readonly FakeLlmProvider _inner;
        private readonly Func<LlmProviderRequest, int> _delayMs;

        public DelayedProvider(FakeLlmProvider inner, Func<LlmProviderRequest, int> delayMs)
        {
            _inner = inner;
            _delayMs = delayMs;
        }

        public LlmProviderKind Kind => _inner.Kind;
        public int? MaxTokensCeiling => _inner.MaxTokensCeiling;

        public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            await Task.Yield();
            var delay = _delayMs(request);
            if (delay > 0)
                await Task.Delay(delay, CancellationToken.None);
            return await _inner.SendAsync(request, ct);
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) => _inner.TestKeyAsync(modelId, ct);
    }

    // ---------- relevant waves with attachments (D-14, D-18, D-26, review concern 1) ----------

    [Fact]
    public async Task RelevantWaves_EmlsCarryParts_ThreadDoneNestsAttachments_NoStandaloneLines()
    {
        _fake.Respond = Responder();

        var result = await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var replay = Replay();
        foreach (var thread in Relevant)
        {
            var record = replay.LastByThread[thread.Id];
            Assert.Equal(JournalRecordType.ThreadDone, record.Type);
            Assert.Equal(AttachmentsOf(thread).Select(a => a.DocId).OrderBy(d => d), record.Attachments.Select(a => a.AttachmentDocId).OrderBy(d => d));
        }

        Assert.DoesNotContain(JournalRecords(), r => r.Type is JournalRecordType.AttachmentDone or JournalRecordType.AttachmentFailed
            or JournalRecordType.AttachmentSkipped);

        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var eml = LoadCopy(doc.Messages[0]);
        Assert.Equal(AttachmentsOf(doc).Select(a => a.DocId).OrderBy(d => d),
            DocParts(eml).Select(p => p.Headers[MailboxWriter.DocIdHeader]).OrderBy(d => d));
    }

    [Fact]
    public async Task AttachmentDelta_FromEarlierWave_ReachesLaterPrompt_NeverSameWave()
    {
        const string statement = "The pricing backup totals $412,600 for change order seven.";
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var excel = Spec(doc, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder(overrideJson: (docId, _) => docId == excel ? CustomExcel(statement) : null);
        var waveOf = WaveOf();

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Contains(result.Ledger.Entries, e => e.Statement == statement);
        var renders = Requests().Where(r => r.SchemaName == RenderSchema).ToList();
        var seeing = renders.Where(r => r.UserPrompt!.Contains(statement, StringComparison.Ordinal)).Select(ThreadOf).ToList();
        Assert.NotEmpty(seeing);
        Assert.All(seeing, id => Assert.True(waveOf[id] > waveOf[doc.Id], id));
    }

    [Fact]
    public async Task UnjournaledThread_ContributesNoDeltas_SpendInRunEnded()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var docIds = AttachmentsOf(doc).Select(a => a.DocId).ToHashSet();
        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        var runner = new GenerationRunner(_gateway)
        {
            BeforeThreadAppendHook = record => record.ThreadId == doc.Id
                ? Task.FromException(new IOException("journal locked"))
                : Task.CompletedTask
        };

        var result = await Run(runner, Input(), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.False(Replay().LastByThread.ContainsKey(doc.Id));
        Assert.DoesNotContain(result.Ledger.Entries, e => e.SourceThreadId == doc.Id || docIds.Contains(e.SourceDocId));

        // The chain successor never sees text that is not in the journal
        var chain = AttachmentTestData.Thread(_plan, AttachmentTestData.ChainThreadId);
        Assert.Contains(AttachmentPrompts.EarlierDraftNotAvailable, PromptFor(Spec(chain, 1, AttachmentType.Excel).DocId));

        // Thread render (1000/400) and three attachments (800/300 each), all on the Story tier, exactly once
        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(1000 + 3 * 800, ended.InputTokens);
        Assert.Equal(400 + 3 * 300, ended.OutputTokens);
        Assert.Equal(_story.CalculateTotalCost(1000, 400) + 3 * _story.CalculateTotalCost(800, 300), ended.CostUsd);
        Assert.Equal(Replay().SpentCostUsd, snapshot.Peek().Counts.CostUsd);
    }

    private static string CustomExcel(string statement)
    {
        var dto = (AttachmentRenderer.ExcelAttachmentDto)AttachmentResponder.DefaultDto(AttachmentType.Excel, relevant: true);
        dto.NewDetails = new List<ThreadRenderer.LedgerDeltaDto>
        {
            new() { Seq = 0, Kind = LedgerKind.Figure, About = "pricing backup total", Statement = statement, Value = "$412,600", FactIds = new() }
        };
        return JsonSerializer.Serialize(dto, DtoOptions);
    }

    [Fact]
    public async Task Reset_RowsCarryAttachmentCounts_CountsIncludeMedia()
    {
        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        GenerationView? atStart = null;
        var runner = new GenerationRunner(_gateway)
        {
            BeforeWaveHook = i =>
            {
                if (i == 0)
                    atStart = snapshot.Peek();
                return Task.CompletedTask;
            }
        };
        var waves = WavePlanner.Plan(_plan);

        await Run(runner, Input(), snapshot);

        Assert.NotNull(atStart);
        var all = _plan.Threads.SelectMany(AttachmentsOf).ToList();
        Assert.Equal(Relevant.Count, atStart!.Counts.RelevantTotal);
        Assert.Equal(all.Count, atStart.Counts.AttachmentsTotal);
        Assert.Equal(all.Count(a => a.Kind is AttachmentType.Image or AttachmentType.Voicemail), atStart.Counts.MediaPlanned);

        var waveRows = atStart.Waves.Where(w => w.RowKind == StageRowKind.Wave).ToList();
        Assert.Equal(waves.Count, waveRows.Count);
        Assert.Equal(Relevant.SelectMany(AttachmentsOf).Count(), waveRows.Sum(w => w.Attachments));
        var waveOf = WaveOf();
        foreach (var row in waveRows)
            Assert.Equal(Relevant.Where(t => waveOf[t.Id] == row.Index).SelectMany(AttachmentsOf).Count(), row.Attachments);

        var filler = Assert.Single(atStart.Waves, w => w.RowKind == StageRowKind.NonRelevant);
        Assert.Equal(waves.Count + 1, filler.Index);
        Assert.Equal(NonRelevant.Count, filler.Threads);
        Assert.Equal(NonRelevant.SelectMany(AttachmentsOf).Count(), filler.Attachments);
    }

    [Fact]
    public async Task Continue_AttachmentCountersMatchTheJournal()
    {
        _fake.Respond = Responder();
        using var cts = new CancellationTokenSource();
        _fake.OnSend = n =>
        {
            if (n == 12)
                cts.Cancel();
        };

        var first = await Run(new GenerationRunner(_gateway, 1), Input(), ct: cts.Token);
        Assert.Equal(GenerationOutcome.Cancelled, first.Outcome);

        _fake.OnSend = null;
        var snapshot = new GenerationSnapshot();
        var second = await Run(new GenerationRunner(_gateway, 1), Input(), snapshot);

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        var counts = snapshot.Peek().Counts;
        var state = GenerationRunState.From(_plan, Replay());
        Assert.Equal(state.AttachmentsWritten + state.AttachmentsWarnings, counts.AttachmentsDone);
        Assert.Equal(state.AttachmentsFailed, counts.AttachmentsFailed);
        Assert.Equal(state.AttachmentsSkipped, counts.AttachmentsSkipped);
        var rows = snapshot.Peek().Waves;
        Assert.Equal(counts.AttachmentsDone, rows.Sum(r => r.AttachmentsDone));
        Assert.Equal(counts.AttachmentsSkipped, rows.Sum(r => r.AttachmentsSkipped));
    }

    [Fact]
    public async Task Checkpoint_KeyShowsAttachmentStatusesAndChainNames()
    {
        var other = AttachmentTestData.Thread(_plan, AttachmentTestData.OtherOrgDocThreadId);
        var refused = Spec(other, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder(r => AttachmentIdOf(r) == refused ? FakeLlmProvider.Refused("cannot write that document") : null);

        await Run(new GenerationRunner(_gateway), Input());

        var key = KeyRows();
        Assert.Equal(AnswerKeyBuilder.StatusFailed, key[refused].Status);
        var invite = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        Assert.Equal(AnswerKeyBuilder.StatusSkipped, key[Spec(invite, 1, AttachmentType.Image).DocId].Status);
        var voicemail = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantVoicemailThreadId);
        Assert.Equal(AnswerKeyBuilder.StatusSkipped, key[Spec(voicemail, 1, AttachmentType.Voicemail).DocId].Status);
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, key[Spec(doc, 1, AttachmentType.Word).DocId].Status);
        var chain = AttachmentTestData.Thread(_plan, AttachmentTestData.ChainThreadId);
        var v2 = key[Spec(chain, 1, AttachmentType.Excel).DocId];
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, v2.Status);
        Assert.Contains("_v2", v2.FileName);
    }

    // ---------- media (D-06, D-27, D-28, UI-SPEC Decision 2) ----------

    [Fact]
    public async Task MediaStop_EndsOnlyMedia_RunFinishes()
    {
        _fake.Respond = Responder();
        var media = new FakeMediaGenerator { StopAfter = 1 };
        var snapshot = new GenerationSnapshot();

        var result = await Run(new GenerationRunner(_gateway), Input(media), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Single(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.MediaStopped);
        var replay = Replay();
        Assert.All(Relevant, t => Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[t.Id].Type));

        var mediaRecords = replay.LastByAttachment.Values
            .Where(r => r.AttachmentKind is AttachmentType.Image or AttachmentType.Voicemail)
            .ToList();
        Assert.Single(mediaRecords, r => r.Type == JournalRecordType.AttachmentDone);
        Assert.All(mediaRecords.Where(r => r.Type != JournalRecordType.AttachmentDone),
            r => Assert.Equal(JournalRecordType.AttachmentSkipped, r.Type));
    }

    [Fact]
    public async Task NoMedia_EveryMediaItemSkipped_NoVoiceRequest()
    {
        _fake.Respond = Responder();

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.DoesNotContain(Requests(), r => r.SchemaName == VoiceSchema);
        var replay = Replay();
        var media = Relevant.SelectMany(AttachmentsOf).Where(a => a.Kind is AttachmentType.Image or AttachmentType.Voicemail).ToList();
        Assert.NotEmpty(media);
        foreach (var attachment in media)
        {
            var record = replay.LastByAttachment[attachment.DocId];
            Assert.Equal(JournalRecordType.AttachmentSkipped, record.Type);
            Assert.Equal(AttachmentUnitOfWork.NoMediaKeyReason, record.Reason);
        }
    }

    [Fact]
    public async Task Voices_OneCallBeforeFirstRender_UsedForEverySpeech()
    {
        _fake.Respond = Responder();
        var media = new FakeMediaGenerator();

        var result = await Run(new GenerationRunner(_gateway), Input(media));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var requests = Requests();
        var voiceIndex = Assert.Single(requests.Select((r, i) => (r, i)), x => x.r.SchemaName == VoiceSchema).i;
        Assert.True(voiceIndex < requests.FindIndex(r => r.SchemaName == RenderSchema));
        var record = Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.CastVoices);
        Assert.False(record.CastVoices!.FromFallback);
        Assert.True(record.CostUsd > 0m);

        var expected = ResponderVoices();
        Assert.Equal(expected.Voices.Select(v => (v.PersonId, v.Voice)), record.CastVoices.Voices.Select(v => (v.PersonId, v.Voice)));
        var callers = SpokenCallers();
        Assert.NotEmpty(callers);
        Assert.Equal(callers.Select(c => VoiceAssignments.For(c, expected)).OrderBy(v => v),
            media.Requests.Where(r => r.Kind == "speech").Select(r => r.Voice).OrderBy(v => v));
    }

    [Fact]
    public async Task Continue_WithPendingVoicemail_ReusesJournaledVoices()
    {
        _fake.Respond = Responder();
        var media = new FakeMediaGenerator();
        using var cts = new CancellationTokenSource();
        _fake.OnSend = n =>
        {
            if (n == 3)
                cts.Cancel();
        };

        var first = await Run(new GenerationRunner(_gateway, 1), Input(media), ct: cts.Token);
        Assert.Equal(GenerationOutcome.Cancelled, first.Outcome);
        Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.CastVoices);
        Assert.DoesNotContain(media.Requests, r => r.Kind == "speech");

        _fake.OnSend = null;
        var before = Requests().Count;
        var second = await Run(new GenerationRunner(_gateway, 1), Input(media));

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        Assert.DoesNotContain(Requests().Skip(before), r => r.SchemaName == VoiceSchema);
        Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.CastVoices);
        var expected = ResponderVoices();
        var callers = SpokenCallers();
        Assert.NotEmpty(callers);
        Assert.Equal(callers.Select(c => VoiceAssignments.For(c, expected)).OrderBy(v => v),
            media.Requests.Where(r => r.Kind == "speech").Select(r => r.Voice).OrderBy(v => v));
    }

    [Fact]
    public async Task VoiceCallRefused_FallbackVoicesJournaled_RunFinishes()
    {
        _fake.Respond = Responder(r => r.SchemaName == VoiceSchema ? FakeLlmProvider.Refused("no") : null);
        var media = new FakeMediaGenerator();

        var result = await Run(new GenerationRunner(_gateway), Input(media));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var record = Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.CastVoices);
        Assert.True(record.CastVoices!.FromFallback);
        var callers = SpokenCallers();
        Assert.NotEmpty(callers);
        Assert.Equal(callers.Select(VoiceAssignments.Fallback).OrderBy(v => v),
            media.Requests.Where(r => r.Kind == "speech").Select(r => r.Voice).OrderBy(v => v));
    }

    [Fact]
    public async Task VoiceCallAuthFailure_FatalBeforeAnyThread()
    {
        _fake.Respond = Responder(r => r.SchemaName == VoiceSchema ? Fatal() : null);

        var result = await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.Contains("invalid key", result.Message);
        Assert.All(Requests(), r => Assert.Equal(VoiceSchema, r.SchemaName));
        Assert.Empty(Replay().LastByThread);
        Assert.DoesNotContain(JournalRecords(), r => r.Type == JournalRecordType.CastVoices);
    }

    [Fact]
    public async Task VoicesNotSaved_InternalError_NoThreadCall()
    {
        _fake.Respond = Responder();
        var runner = new GenerationRunner(_gateway)
        {
            BeforeRecordAppendHook = record => record.Type == JournalRecordType.CastVoices
                ? Task.FromException(new IOException("disk full"))
                : Task.CompletedTask
        };

        var result = await Run(runner, Input(new FakeMediaGenerator()));

        Assert.Equal(GenerationOutcome.InternalError, result.Outcome);
        Assert.Equal("Could not save the voicemail voices: disk full", result.Message);
        Assert.All(Requests(), r => Assert.Equal(VoiceSchema, r.SchemaName));
        Assert.Empty(Replay().LastByThread);

        // The voice call has no record of its own, so RunEnded carries it
        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(600, ended.InputTokens);
        Assert.Equal(200, ended.OutputTokens);
        Assert.Equal(_volume.CalculateTotalCost(600, 200), ended.CostUsd);
    }

    [Fact]
    public async Task NoVoicemailPlanned_NoVoiceRequest()
    {
        foreach (var message in _plan.Threads.SelectMany(t => t.Messages))
            message.Attachments.RemoveAll(a => a.Kind == AttachmentType.Voicemail);
        _fake.Respond = Responder();

        var result = await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.DoesNotContain(Requests(), r => r.SchemaName == VoiceSchema);
        Assert.DoesNotContain(JournalRecords(), r => r.Type == JournalRecordType.CastVoices);
    }

    // ---------- invites across waves (D-20, review concern 6, checker 06-05) ----------

    // The first invite's thread records the site walk as a Meeting delta, on a day after both invites
    private Func<LlmProviderRequest, LlmProviderResponse> MeetingResponder(string threadId, DateOnly meetingDay, Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null)
    {
        var plain = EvidenceTestData.RenderResponder(_plan, Guide);
        var thread = AttachmentTestData.Thread(_plan, threadId);
        return Responder(request =>
        {
            var first = intercept?.Invoke(request);
            if (first != null)
                return first;
            if (request.SchemaName != RenderSchema || ThreadOf(request) != threadId)
                return null;

            var dto = JsonSerializer.Deserialize<ThreadRenderer.ThreadRenderDto>(plain(request).Text!, DtoOptions)!;
            dto.NewDetails = new List<ThreadRenderer.LedgerDeltaDto>
            {
                new()
                {
                    Seq = 1,
                    Kind = LedgerKind.Meeting,
                    About = "site walk",
                    Statement = "The teams will walk the upper floors together.",
                    Value = meetingDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    FactIds = new List<string>(thread.Messages[0].FactIds)
                }
            };
            return FakeLlmProvider.Ok(JsonSerializer.Serialize(dto, DtoOptions), 1000, 400);
        });
    }

    // Puts the first invite two weeks earlier, so the two invites fall in different waves; the meeting is after both
    private (PlannedThread First, PlannedThread Second, DateOnly MeetingDay) InvitesInTwoWaves()
    {
        Shift(AttachmentTestData.InviteThreadId, TimeSpan.FromDays(-14));
        var first = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var second = AttachmentTestData.Thread(_plan, AttachmentTestData.SecondInviteThreadId);
        var waveOf = WaveOf();
        Assert.True(waveOf[second.Id] > waveOf[first.Id], "precondition: the invites are in different waves");
        var meetingDay = DateOnly.FromDateTime(second.Messages[0].SentAt.DateTime).AddDays(4);
        return (first, second, meetingDay);
    }

    [Fact]
    public async Task Invite_OwnDeltaInWave1_LedgerMatchInWave2_SameSchedule()
    {
        var (first, second, meetingDay) = InvitesInTwoWaves();
        var firstInvite = Spec(first, 1, AttachmentType.CalendarInvite).DocId;
        var secondInvite = Spec(second, 1, AttachmentType.CalendarInvite).DocId;
        _fake.Respond = MeetingResponder(first.Id, meetingDay);

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var replay = Replay();
        var a = replay.LastByAttachment[firstInvite];
        var b = replay.LastByAttachment[secondInvite];
        Assert.Equal(JournalRecordType.AttachmentDone, a.Type);
        Assert.Equal(JournalRecordType.AttachmentDone, b.Type);
        Assert.NotEmpty(a.InviteMeetingKey);
        Assert.Equal(a.InviteMeetingKey, b.InviteMeetingKey);
        Assert.Equal(a.InviteStart, b.InviteStart);
        Assert.Equal(a.InviteEnd, b.InviteEnd);
        Assert.Equal(DateOnly.FromDateTime(a.InviteStart!.Value.DateTime), meetingDay);
        Assert.Equal(IcsTimes(LoadCopy(first.Messages[0]), firstInvite), IcsTimes(LoadCopy(second.Messages[0]), secondInvite));
        var fresh = (a.InviteStart, a.InviteEnd, a.InviteMeetingKey);

        // Continue where only the wave-2 thread is pending: its slot comes from the persisted schedule
        var retryFolder = NewFolder();
        _fake.Respond = MeetingResponder(first.Id, meetingDay,
            r => r.SchemaName == RenderSchema && ThreadOf(r) == second.Id ? FakeLlmProvider.Refused("not now") : null);
        var failed = await Run(new GenerationRunner(_gateway), Input(folder: retryFolder));
        Assert.Equal(JournalRecordType.ThreadFailed, Replay(retryFolder).LastByThread[second.Id].Type);

        _fake.Respond = MeetingResponder(first.Id, meetingDay);
        var before = Requests().Count;
        var retried = await Run(new GenerationRunner(_gateway), Input(folder: retryFolder));

        Assert.Equal(GenerationOutcome.Finished, failed.Outcome);
        Assert.Equal(GenerationOutcome.Finished, retried.Outcome);
        Assert.All(Requests().Skip(before).Where(r => r.SchemaName == RenderSchema), r => Assert.Equal(second.Id, ThreadOf(r)));
        var replayed = Replay(retryFolder);
        var a2 = replayed.LastByAttachment[firstInvite];
        var b2 = replayed.LastByAttachment[secondInvite];
        Assert.Equal(fresh, (a2.InviteStart, a2.InviteEnd, a2.InviteMeetingKey));
        Assert.Equal(fresh, (b2.InviteStart, b2.InviteEnd, b2.InviteMeetingKey));
        Assert.Equal(IcsTimes(LoadCopy(first.Messages[0], retryFolder), firstInvite),
            IcsTimes(LoadCopy(second.Messages[0], retryFolder), secondInvite));
    }

    [Fact]
    public async Task Invite_PersistedSlotFromPreviousRun_IsReused()
    {
        var (first, second, meetingDay) = InvitesInTwoWaves();
        var secondInvite = Spec(second, 1, AttachmentType.CalendarInvite).DocId;
        _fake.Respond = MeetingResponder(first.Id, meetingDay);
        await Run(new GenerationRunner(_gateway), Input());
        var slot = Replay().LastByAttachment[Spec(first, 1, AttachmentType.CalendarInvite).DocId];

        // Hand-moved persisted schedule, and the second invite's thread dropped so Continue writes it again: the next
        // invite for that meeting must take the persisted slot, not derive a new one
        var start = slot.InviteStart!.Value.AddHours(1);
        var end = slot.InviteEnd!.Value.AddHours(1);
        var records = JournalRecords();
        File.Delete(GenerationJournal.PathFor(_folder));
        var journal = new GenerationJournal(_folder);
        foreach (var record in records)
        {
            if (record.Type is JournalRecordType.ThreadDone or JournalRecordType.ThreadFailed && record.ThreadId == second.Id)
                continue;
            if (record.Type == JournalRecordType.ThreadDone && record.ThreadId == first.Id)
            {
                var invite = record.Attachments.Single(a => a.AttachmentKind == AttachmentType.CalendarInvite);
                invite.InviteStart = start;
                invite.InviteEnd = end;
            }

            await journal.AppendAsync(record);
        }

        Assert.Equal(start, Replay().LastByAttachment[Spec(first, 1, AttachmentType.CalendarInvite).DocId].InviteStart);

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var rewritten = Replay().LastByAttachment[secondInvite];
        Assert.Equal(start, rewritten.InviteStart);
        Assert.Equal(end, rewritten.InviteEnd);
    }

    // ---------- chains (D-16, review concern 7, checker 06-18) ----------

    [Fact]
    public async Task Chain_FreshRun_V2InLaterWave_GetsV1Text()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var chain = AttachmentTestData.Thread(_plan, AttachmentTestData.ChainThreadId);
        var waveOf = WaveOf();
        Assert.True(waveOf[chain.Id] > waveOf[doc.Id], "precondition: v2 is written in a later wave");
        var v1 = Spec(doc, 1, AttachmentType.Excel).DocId;
        var v2 = Spec(chain, 1, AttachmentType.Excel).DocId;
        Assert.Equal(v1, AttachmentChains.Compute(_plan, Guide)[v2].PreviousDocId);
        _fake.Respond = Responder();

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var v1Text = Replay().LastByAttachment[v1].FlattenedText;
        Assert.NotEmpty(v1Text);
        var prompt = PromptFor(v2);
        Assert.Contains(v1Text.Trim(), prompt);
        Assert.DoesNotContain(AttachmentPrompts.EarlierDraftNotAvailable, prompt);
    }

    // A = T-0904 (v1 on its root, v3 on its reply-all), B = T-0903 moved into A's wave (v2): one Word chain across two
    // threads of one wave, interleaved by send time
    private (PlannedThread A, PlannedThread B, string V1, string V2, string V3) InterleavedChain()
    {
        var a = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var b = AttachmentTestData.Thread(_plan, AttachmentTestData.OtherOrgDocThreadId);

        // A two days later starts its own wave; B six hours after A's root joins it
        Shift(a.Id, TimeSpan.FromDays(2));
        Shift(b.Id, a.Messages[0].SentAt.AddHours(6) - b.Messages[0].SentAt);

        var bSpec = b.Messages[0].Attachments.Single();
        bSpec.Kind = AttachmentType.Word;
        bSpec.Title = "Site Visit Notes v2";
        bSpec.FileName = "Site Visit Notes v2.docx";
        var replyAll = a.Messages.Single(m => m.Seq == 3);
        replyAll.Attachments.Add(new PlannedAttachment
        {
            Kind = AttachmentType.Word,
            Title = "Site Visit Notes v3",
            FileName = "Site Visit Notes v3.docx",
            Description = "Site Visit Notes v3 for the thread.",
            DocId = replyAll.DocId + ".0001"
        });

        var v1 = Spec(a, 1, AttachmentType.Word).DocId;
        var v2 = bSpec.DocId;
        var v3 = replyAll.Attachments[0].DocId;
        var links = AttachmentChains.Compute(_plan, Guide);
        Assert.Equal(v1, links[v2].PreviousDocId);
        Assert.Equal(v2, links[v3].PreviousDocId);
        var waveOf = WaveOf();
        Assert.Equal(waveOf[a.Id], waveOf[b.Id]);
        var ranks = ChainScheduler.RankThreads(_plan);
        Assert.True(ranks[a.Id] < ranks[b.Id]);
        return (a, b, v1, v2, v3);
    }

    [Fact]
    public async Task Chain_InterleavedAcrossThreadsInOneWave_RunFinishes()
    {
        var (a, b, v1, v2, v3) = InterleavedChain();
        _fake.Respond = Responder();
        var gateway = DelayedGateway(_ => Random.Shared.Next(0, 5));

        var prompts = new List<(string V2, string V3)>();
        foreach (var folder in new[] { _folder, NewFolder() })
        {
            var result = await new GenerationRunner(gateway).RunAsync(Input(folder: folder), new GenerationSnapshot()).WaitAsync(ChainGuard);

            Assert.Equal(GenerationOutcome.Finished, result.Outcome);
            var replay = Replay(folder);
            Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[a.Id].Type);
            Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[b.Id].Type);
            Assert.Contains(replay.LastByAttachment[v1].FlattenedText.Trim(), PromptFor(v2));
            prompts.Add((PromptFor(v2), PromptFor(v3)));
        }

        Assert.Equal(prompts[0], prompts[1]);
    }

    [Fact]
    public async Task Chain_PredecessorStoppedBeforeCommit_SuccessorDoesNotHang()
    {
        var (a, b, _, v2, _) = InterleavedChain();
        _fake.Respond = Responder(r => r.SchemaName == RenderSchema && ThreadOf(r) == a.Id ? Fatal() : null);

        // A's render answers last, so B is already waiting on A's v1 when the run turns fatal
        var gateway = DelayedGateway(r => r.SchemaName == RenderSchema && ThreadOf(r) == a.Id ? 300 : 0);

        var result = await new GenerationRunner(gateway).RunAsync(Input(), new GenerationSnapshot()).WaitAsync(ChainGuard);

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        var replay = Replay();
        Assert.False(replay.LastByThread.ContainsKey(a.Id));
        Assert.Contains(Requests(), r => r.SchemaName == RenderSchema && ThreadOf(r) == b.Id);
        Assert.DoesNotContain(Requests(), r => AttachmentIdOf(r) == v2);
    }

    // ---------- unjournaled spend (D-29, checker 06-19) ----------

    [Fact]
    public async Task NotRunSpend_FatalAfterPaidAttachmentCall_InRunEnded()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var word = Spec(doc, 1, AttachmentType.Word).DocId;
        _fake.Respond = Responder(r => AttachmentIdOf(r) == word ? Fatal(800, 300) : null);
        var snapshot = new GenerationSnapshot();

        var result = await Run(new GenerationRunner(_gateway, 1), Input(), snapshot);

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        var replay = Replay();
        var record = replay.LastByThread[doc.Id];
        Assert.Equal(JournalRecordType.ThreadDone, record.Type);
        Assert.DoesNotContain(record.Attachments, r => r.AttachmentDocId == word);

        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(800, ended.InputTokens);
        Assert.Equal(300, ended.OutputTokens);
        Assert.Equal(_story.CalculateTotalCost(800, 300), ended.CostUsd);
        Assert.Equal(replay.SpentCostUsd, snapshot.Peek().Counts.CostUsd);
    }
}
