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

    // IN-06: a thread line refused at the journal stop line ends the run as a fatal stop; the journal stays readable
    [Fact]
    public async Task JournalAtStopLine_StopsRunFatal_JournalStaysReadable()
    {
        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();

        // Room for RunStarted and RunEnded only: the first ThreadDone line (message bodies) crosses the 90% stop line
        var probeFolder = Path.Combine(_folder, "probe");
        Directory.CreateDirectory(probeFolder);
        var probe = new GenerationJournal(probeFolder);
        await probe.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.RunStarted, PlanId = _plan.PlanId, Utc = DateTime.UtcNow, Provider = _gateway.Provider.ToString()
        });
        // 07-05: RunEnded is fully itemized (CostDetail + media), so the probe carries the same fields
        await probe.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.RunEnded,
            PlanId = _plan.PlanId,
            Utc = DateTime.UtcNow,
            Outcome = GenerationOutcome.Fatal,
            Message = GenerationJournal.FullMessage,
            InputTokens = 999_999,
            OutputTokens = 999_999,
            CostUsd = 99.999999m,
            MediaCostUsd = 99.999999m,
            CostDetail = new CostBreakdown
            {
                InputUsd = 99.999999m, CacheWriteUsd = 99.999999m, CacheReadUsd = 99.999999m, OutputUsd = 99.999999m,
                BatchSavingsUsd = 99.999999m
            }
        });
        var controlBytes = new FileInfo(GenerationJournal.PathFor(probeFolder)).Length;
        Directory.Delete(probeFolder, recursive: true);
        GenerationJournal.SizeCapOverride = controlBytes + 64;
        GenerationRunResult result;
        try
        {
            result = await new GenerationRunner(_gateway, 1).RunAsync(Input, snapshot, Persist);
        }
        finally
        {
            GenerationJournal.SizeCapOverride = 0;
        }

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.Equal(GenerationJournal.FullMessage, result.Message);
        Assert.Single(RequestedThreads().Distinct());
        var replay = Replay();
        Assert.True(replay.Readable);
        Assert.Empty(replay.LastByThread);
        Assert.Equal(new[] { JournalRecordType.RunStarted, JournalRecordType.RunEnded }, JournalRecords().Select(r => r.Type));
        Assert.Contains(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.NotSaved);
        Assert.Empty(Directory.Exists(Path.Combine(_folder, "Mailboxes"))
            ? Directory.GetFiles(Path.Combine(_folder, "Mailboxes"), "*.eml", SearchOption.AllDirectories)
            : Array.Empty<string>());
    }

    [Fact]
    public async Task JournalAlreadyPastStopLine_StopsBeforeAnyCall()
    {
        _fake.Respond = Responder();
        var journal = new GenerationJournal(_folder);
        await journal.AppendAsync(new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = _plan.PlanId, Utc = DateTime.UtcNow });

        GenerationJournal.SizeCapOverride = new FileInfo(GenerationJournal.PathFor(_folder)).Length + 10;
        GenerationRunResult result;
        try
        {
            result = await new GenerationRunner(_gateway, 1).RunAsync(Input, new GenerationSnapshot(), Persist);
        }
        finally
        {
            GenerationJournal.SizeCapOverride = 0;
        }

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.Equal(GenerationJournal.FullMessage, result.Message);
        Assert.Empty(RequestedThreads());
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

        // WR-02: a NotSaved row explains the pending thread, and the wave row does not count it as failed (as the tiles do not)
        var events = snapshot.Peek().Events;
        Assert.Contains(events, e => e.Kind == GenerationEventKind.NotSaved && e.ThreadId == doc.Id);
        Assert.All(events.Where(e => e.Kind == GenerationEventKind.WaveFinished), e => Assert.Equal(0, e.Failed));
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

    // ---------- non-relevant stage (D-21, D-25, NOISE-01..05, OUT-01) ----------

    [GeneratedRegex(@"^(Document|Spreadsheet|Presentation) \d+\.")]
    private static partial Regex CodeDefaultName();

    private int SideWorldRecords(string? folder = null) => JournalRecords(folder).Count(r => r.Type == JournalRecordType.SideWorld);

    private List<string> JournaledProjectNames() =>
        JournalRecords().Last(r => r.Type == JournalRecordType.SideWorld).SideWorld!.Organizations
            .SelectMany(o => o.Projects).Select(p => p.Name).ToList();

    [Fact]
    public async Task FullRun_EveryFillerCallAfterEveryRelevantRender()
    {
        _fake.Respond = Responder();

        var result = await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var requests = Requests();
        var renders = requests.Select((r, i) => (r, i)).Where(x => x.r.SchemaName == RenderSchema).Select(x => x.i).ToList();
        var filler = requests.Select((r, i) => (r, i)).Where(x => x.r.SchemaName == FillerSchema).Select(x => x.i).ToList();
        Assert.NotEmpty(renders);
        Assert.Equal(NonRelevant.Count, filler.Count);
        Assert.True(renders.Max() < filler.Min(), "a filler call started before the last relevant wave ended");

        var replay = Replay();
        Assert.All(_plan.Threads.Where(t => t.Messages.Count > 0),
            t => Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[t.Id].Type));
    }

    [Fact]
    public async Task SideWorld_OneCallBeforeFiller_Journaled_StageEventsAndRow()
    {
        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        var waves = WavePlanner.Plan(_plan);

        var result = await Run(new GenerationRunner(_gateway), Input(), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var requests = Requests();
        var sideIndex = Assert.Single(requests.Select((r, i) => (r, i)), x => x.r.SchemaName == SideWorldSchema).i;
        Assert.True(sideIndex < requests.FindIndex(r => r.SchemaName == FillerSchema));
        var record = Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.SideWorld);
        Assert.False(record.SideWorld!.FromFallback);
        Assert.Equal(_volume.CalculateTotalCost(700, 500), record.CostUsd);

        var view = snapshot.Peek();
        var ready = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.SideProjectsReady);
        Assert.Equal(SideWorldBuilder.CoveredOrganizations(Guide).Count, ready.Organizations);
        Assert.Equal(2 * ready.Organizations, ready.Projects);
        var started = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.NonRelevantStarted);
        Assert.Equal(NonRelevant.Count, started.Threads);
        var finished = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.NonRelevantFinished);
        Assert.Equal(NonRelevant.Count, finished.Written + finished.Warnings);
        Assert.Equal(0, finished.Failed);
        Assert.True(view.Events.IndexOf(ready) < view.Events.IndexOf(started));

        var row = Assert.Single(view.Waves, w => w.RowKind == StageRowKind.NonRelevant);
        Assert.Equal(waves.Count + 1, row.Index);
        Assert.Equal(NonRelevant.Count, row.Done);
        Assert.False(row.Running);
        Assert.Equal(NonRelevant.Count, view.Counts.NonRelevantDone);
    }

    [Fact]
    public async Task DailyBusinessPrompts_NameTheJournaledSideProjects()
    {
        _fake.Respond = Responder();

        await Run(new GenerationRunner(_gateway), Input());

        var names = JournaledProjectNames();
        Assert.Contains(names, n => n.StartsWith("Lakeview Clinic Account", StringComparison.Ordinal));
        var daily = NonRelevant.Where(t => t.Category == NonRelevantCategory.DailyBusiness).Select(t => t.Id).ToHashSet();
        var prompts = Requests().Where(r => r.SchemaName == FillerSchema && daily.Contains(ThreadOf(r))).ToList();
        Assert.Equal(daily.Count, prompts.Count);
        Assert.All(prompts, p => Assert.Contains(names, n => p.UserPrompt!.Contains(n, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NonRelevant_EveryCopyOnDisk_ExternalSendersInboxOnly()
    {
        _fake.Respond = Responder();

        await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        var external = _plan.Externals.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var message in NonRelevant.SelectMany(t => t.Messages))
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

            // Every participating custodian mailbox gets its copy (OUT-01)
            foreach (var custodian in message.CustodianIds)
                Assert.Contains(paths, p => p.Contains($"/{Folders[custodian]}/", StringComparison.Ordinal));

            if (external.Contains(message.FromId))
                Assert.All(paths, p => Assert.Contains($"/{PlanNumbering.InboxFolder}/", p));
        }
    }

    [Fact]
    public async Task SecondRun_OnFinishedFolder_MakesNoRequests()
    {
        _fake.Respond = Responder();
        var media = new FakeMediaGenerator();
        var first = await Run(new GenerationRunner(_gateway), Input(media));
        Assert.Equal(GenerationOutcome.Finished, first.Outcome);
        var before = Requests().Count;
        var mediaBefore = media.Requests.Count;

        var second = await Run(new GenerationRunner(_gateway), Input(media));

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        Assert.Equal(before, Requests().Count);
        Assert.Equal(mediaBefore, media.Requests.Count);
        Assert.Equal(1, SideWorldRecords());
    }

    [Fact]
    public async Task CancelDuringNonRelevant_ContinueReplaysSideWorld_WritesOnlyTheRest()
    {
        using var cts = new CancellationTokenSource();
        var inner = Responder();
        var fillerCalls = 0;
        _fake.Respond = request =>
        {
            if (request.SchemaName == FillerSchema && Interlocked.Increment(ref fillerCalls) == 3)
                cts.Cancel();
            return inner(request);
        };

        var first = await Run(new GenerationRunner(_gateway, 1), Input(), ct: cts.Token);

        Assert.Equal(GenerationOutcome.Cancelled, first.Outcome);
        Assert.Equal(1, SideWorldRecords());
        var replay = Replay();
        Assert.All(Relevant, t => Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[t.Id].Type));
        var doneFiller = NonRelevant.Where(t => replay.LastByThread.ContainsKey(t.Id)).Select(t => t.Id).ToList();
        Assert.NotEmpty(doneFiller);
        Assert.True(doneFiller.Count < NonRelevant.Count);

        _fake.Respond = Responder();
        var before = Requests().Count;
        var second = await Run(new GenerationRunner(_gateway, 1), Input());

        Assert.Equal(GenerationOutcome.Finished, second.Outcome);
        var resent = Requests().Skip(before).ToList();
        Assert.DoesNotContain(resent, r => r.SchemaName == SideWorldSchema || r.SchemaName == RenderSchema);
        Assert.Equal(NonRelevant.Select(t => t.Id).Except(doneFiller).OrderBy(i => i),
            resent.Where(r => r.SchemaName == FillerSchema).Select(ThreadOf).OrderBy(i => i));
        Assert.Equal(1, SideWorldRecords());
        Assert.All(NonRelevant, t => Assert.Equal(JournalRecordType.ThreadDone, Replay().LastByThread[t.Id].Type));
    }

    [Fact]
    public async Task FillerAuthFailure_Fatal_KeepsDoneThreads_NoFurtherFillerCalls()
    {
        var inner = Responder();
        var fillerCalls = 0;
        _fake.Respond = request => request.SchemaName == FillerSchema && Interlocked.Increment(ref fillerCalls) == 2
            ? Fatal()
            : inner(request);
        var snapshot = new GenerationSnapshot();

        var result = await Run(new GenerationRunner(_gateway, 1), Input(), snapshot);

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.Contains("invalid key", result.Message);
        Assert.Equal(2, Requests().Count(r => r.SchemaName == FillerSchema));
        var replay = Replay();
        Assert.All(Relevant, t => Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[t.Id].Type));
        var first = Requests().First(r => r.SchemaName == FillerSchema);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[ThreadOf(first)].Type);
        Assert.Single(NonRelevant, t => replay.LastByThread.ContainsKey(t.Id));
        Assert.DoesNotContain(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.NonRelevantFinished);
        Assert.False(snapshot.Peek().Waves.Single(w => w.RowKind == StageRowKind.NonRelevant).Running);
    }

    [Fact]
    public async Task SideWorldFatal_NoFillerAttempted()
    {
        _fake.Respond = Responder(r => r.SchemaName == SideWorldSchema ? Fatal() : null);

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.DoesNotContain(Requests(), r => r.SchemaName == FillerSchema);
        Assert.Equal(0, SideWorldRecords());
        var replay = Replay();
        Assert.All(Relevant, t => Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[t.Id].Type));
    }

    [Fact]
    public async Task SideWorldNotSaved_InternalError_NoFillerAttempted()
    {
        _fake.Respond = Responder();
        var runner = new GenerationRunner(_gateway)
        {
            BeforeRecordAppendHook = record => record.Type == JournalRecordType.SideWorld
                ? Task.FromException(new IOException("disk full"))
                : Task.CompletedTask
        };

        var result = await Run(runner, Input());

        Assert.Equal(GenerationOutcome.InternalError, result.Outcome);
        Assert.Equal("Could not save the side projects: disk full", result.Message);
        Assert.DoesNotContain(Requests(), r => r.SchemaName == FillerSchema);
        Assert.Equal(0, SideWorldRecords());

        // The side-world call has no record of its own, so RunEnded carries it
        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(700, ended.InputTokens);
        Assert.Equal(500, ended.OutputTokens);
    }

    [Fact]
    public async Task Filler_WritesNoLedgerDeltas_LedgerEqualsRelevantOnlyRun()
    {
        _fake.Respond = Responder();

        var mixed = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, mixed.Outcome);
        var replay = Replay();
        foreach (var thread in NonRelevant)
        {
            var record = replay.LastByThread[thread.Id];
            Assert.Equal(JournalRecordType.ThreadDone, record.Type);
            Assert.Empty(record.LedgerDelta);
            Assert.All(record.Attachments, a => Assert.Empty(a.LedgerDelta));
        }

        // D-13: filler prompts never carry the ledger
        Assert.All(Requests().Where(r => r.SchemaName == FillerSchema),
            r => Assert.DoesNotContain("Earlier details from previous emails", r.UserPrompt));

        var relevantOnly = PlanFixtures.Clone(_plan);
        relevantOnly.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
        var folder = NewFolder();
        var alone = await Run(new GenerationRunner(_gateway),
            new GenerationRunInput { Draft = _draft, Plan = relevantOnly, CaseFolder = folder });

        Assert.Equal(GenerationOutcome.Finished, alone.Outcome);
        Assert.NotEmpty(alone.Ledger.Entries);
        Assert.Equal(JsonSerializer.Serialize(alone.Ledger.Entries, DtoOptions), JsonSerializer.Serialize(mixed.Ledger.Entries, DtoOptions));
    }

    [Fact]
    public async Task AnswerKey_NonRelevantRowsGenerated_NoCodeDefaultNames()
    {
        _fake.Respond = Responder();

        await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        var key = KeyRows();
        foreach (var message in NonRelevant.SelectMany(t => t.Messages))
        {
            Assert.Equal(AnswerKeyBuilder.StatusGenerated, key[message.DocId].Status);
            foreach (var attachment in message.Attachments)
            {
                Assert.Equal(AnswerKeyBuilder.StatusGenerated, key[attachment.DocId].Status);
                Assert.DoesNotMatch(CodeDefaultName(), key[attachment.DocId].FileName);
            }
        }

        var daily = AttachmentTestData.Thread(_plan, AttachmentTestData.DailyBusinessThreadId);
        Assert.Equal("Word_notes_1.docx", key[daily.Messages[0].Attachments[0].DocId].FileName);
    }
}

/// <summary>
/// Attachment repair inside the runner (06-20 Task 2, D-26, review concerns 5 and MEDIUM, checkers 06-18/06-19/06-20):
/// Retry Failed and Continue add attachments to written threads in story order, without changing finished text.
/// </summary>
public partial class GenerationRunnerMixedTests
{
    private const string RepairStatement = "The repaired pricing backup totals $412,600 for change order seven.";

    private int RepairRow => WavePlanner.Plan(_plan).Count + 2;

    private static Func<LlmProviderRequest, LlmProviderResponse?> RefuseIds(IEnumerable<string> docIds)
    {
        var set = new HashSet<string>(docIds, StringComparer.Ordinal);
        return r => AttachmentIdOf(r) is { } id && set.Contains(id) ? FakeLlmProvider.Refused("cannot write that document") : null;
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.Ordinal));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, target, StringComparison.Ordinal), overwrite: true);
    }

    private Dictionary<string, byte[]> AllEmls(string? folder = null)
    {
        var root = folder ?? _folder;
        return Directory.GetFiles(Path.Combine(root, PlanNumbering.MailboxesFolder), "*.eml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
    }

    private string ThreadOfDoc(string docId) =>
        _plan.Threads.First(t => t.Messages.Any(m => m.Attachments.Any(a => a.DocId == docId))).Id;

    // DocID -> (From, Subject) from answer-key.csv
    private Dictionary<string, (string From, string Subject)> KeyHeaders(string? folder = null)
    {
        var text = File.ReadAllText(Path.Combine(folder ?? _folder, AnswerKeyCsvWriter.FileName), Encoding.UTF8);
        var rows = GenerationRunnerTests.ParseCsv(text.TrimStart('﻿'));
        var header = rows[0];
        var doc = Array.IndexOf(header, "DocID");
        var from = Array.IndexOf(header, "From");
        var subject = Array.IndexOf(header, "Subject");
        return rows.Skip(1)
            .Where(r => r.Length > Math.Max(doc, Math.Max(from, subject)))
            .ToDictionary(r => r[doc], r => (r[from], r[subject]), StringComparer.Ordinal);
    }

    private static PlannedAttachment AddAttachment(PlannedMessage message, AttachmentType kind, string title)
    {
        var extension = kind == AttachmentType.Word ? ".docx" : kind == AttachmentType.Excel ? ".xlsx" : ".pptx";
        var spec = new PlannedAttachment
        {
            Kind = kind,
            Title = title,
            FileName = title + extension,
            Description = title + " for the thread.",
            DocId = message.DocId + ".00" + (11 + message.Attachments.Count)
        };
        message.Attachments.Add(spec);
        return spec;
    }

    // Moves a thread so its first message is at the given time
    private void MoveTo(string threadId, DateTimeOffset firstSent)
    {
        var thread = AttachmentTestData.Thread(_plan, threadId);
        Shift(threadId, firstSent - thread.Messages.Min(m => m.SentAt));
    }

    private AttachmentType KindOf(string docId) =>
        _plan.Threads.SelectMany(AttachmentsOf).First(a => a.DocId == docId).Kind;

    // A late repair that obeys the prompt: the default document without new details (no V6 retry)
    private static string NoNewDetails(AttachmentType kind)
    {
        var dto = AttachmentResponder.DefaultDto(kind, relevant: true);
        switch (dto)
        {
            case AttachmentRenderer.WordAttachmentDto word:
                word.NewDetails = new List<ThreadRenderer.LedgerDeltaDto>();
                break;
            case AttachmentRenderer.ExcelAttachmentDto excel:
                excel.NewDetails = new List<ThreadRenderer.LedgerDeltaDto>();
                break;
            case AttachmentRenderer.PowerPointAttachmentDto slides:
                slides.NewDetails = new List<ThreadRenderer.LedgerDeltaDto>();
                break;
        }

        return JsonSerializer.Serialize(dto, dto.GetType(), DtoOptions);
    }

    private DateTimeOffset AfterLastThread(int days) =>
        _plan.Threads.SelectMany(t => t.Messages).Max(m => m.SentAt).AddDays(days);

    // ---------- Retry Failed and Continue (D-26, RUN-02) ----------

    [Fact]
    public async Task RetryFailed_RefusedExcel_OneAttachmentCall_NoThreadCall_KeyGenerated()
    {
        var other = AttachmentTestData.Thread(_plan, AttachmentTestData.OtherOrgDocThreadId);
        var refused = Spec(other, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder(RefuseIds(new[] { refused }));
        await Run(new GenerationRunner(_gateway), Input());
        Assert.Equal(AnswerKeyBuilder.StatusFailed, KeyRows()[refused].Status);

        _fake.Respond = Responder(overrideJson: (docId, _) => docId == refused ? NoNewDetails(AttachmentType.Excel) : null);
        var before = Requests().Count;
        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var calls = Requests().Skip(before).ToList();
        Assert.Equal(refused, AttachmentIdOf(Assert.Single(calls)));
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, KeyRows()[refused].Status);
        Assert.Contains(refused, DocParts(LoadCopy(other.Messages[0])).Select(p => p.Headers[MailboxWriter.DocIdHeader]));
    }

    [Fact]
    public async Task Continue_EarlierWaveRepair_FeedsLaterWave()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var excel = Spec(doc, 1, AttachmentType.Excel).DocId;
        var waveOf = WaveOf();
        var later = Relevant.Where(t => waveOf[t.Id] > waveOf[doc.Id]).Select(t => t.Id).ToHashSet();
        Assert.NotEmpty(later);

        // Run 1: the Excel fails and every later wave is left pending
        _fake.Respond = Responder(r => AttachmentIdOf(r) == excel || (r.SchemaName == RenderSchema && later.Contains(ThreadOf(r)))
            ? FakeLlmProvider.Refused("not now")
            : null);
        await Run(new GenerationRunner(_gateway), Input());
        Assert.All(later, id => Assert.Equal(JournalRecordType.ThreadFailed, Replay().LastByThread[id].Type));

        _fake.Respond = Responder(overrideJson: (docId, _) => docId == excel ? CustomExcel(RepairStatement) : null);
        var before = Requests().Count;
        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var calls = Requests().Skip(before).ToList();
        var repairIndex = calls.FindIndex(r => AttachmentIdOf(r) == excel);
        var firstLater = calls.FindIndex(r => r.SchemaName == RenderSchema && later.Contains(ThreadOf(r)));
        Assert.True(repairIndex >= 0 && firstLater > repairIndex, $"repair {repairIndex}, first later render {firstLater}");
        var repairLine = JournalRecords().Single(r => r.Type == JournalRecordType.AttachmentsRepaired);
        Assert.False(repairLine.LateRepair);
        Assert.Contains(calls.Where(r => r.SchemaName == RenderSchema && later.Contains(ThreadOf(r))),
            r => r.UserPrompt!.Contains(RepairStatement, StringComparison.Ordinal));
        Assert.Contains(result.Ledger.Entries, e => e.Statement == RepairStatement);
    }

    [Fact]
    public async Task Continue_RepairsInWaveOrder_SeeEarlierRepairs()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var chain = AttachmentTestData.Thread(_plan, AttachmentTestData.ChainThreadId);

        // Nothing relevant is written after the doc thread except the chain thread, so neither repair is late
        var docStart = doc.Messages.Min(m => m.SentAt);
        _plan.Threads.RemoveAll(t => t.Relevance == ThreadRelevance.Relevant && t.Id != chain.Id
                                     && t.Messages.Count > 0 && t.Messages.Min(m => m.SentAt) > docStart);
        var waveOf = WaveOf();
        Assert.True(waveOf[chain.Id] > waveOf[doc.Id], "precondition: the chain thread is in a later wave");
        Assert.Equal(waveOf.Values.Max(), waveOf[chain.Id]);
        var v1 = Spec(doc, 1, AttachmentType.Excel).DocId;
        var v2 = Spec(chain, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder(RefuseIds(new[] { v1, v2 }));
        await Run(new GenerationRunner(_gateway), Input());

        _fake.Respond = Responder(overrideJson: (docId, _) => docId == v1 ? CustomExcel(RepairStatement) : null);
        var before = Requests().Count;
        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var calls = Requests().Skip(before).ToList();
        Assert.Equal(new[] { v1, v2 }, calls.Select(AttachmentIdOf).Where(id => id == v1 || id == v2).Distinct());
        var v2Prompt = calls.Last(r => AttachmentIdOf(r) == v2).UserPrompt!;
        Assert.Contains(RepairStatement, v2Prompt);

        // The wave-2 entry (the chain thread's own detail) never reaches the earlier repair
        var v1Prompt = calls.Last(r => AttachmentIdOf(r) == v1).UserPrompt!;
        Assert.DoesNotContain("site walk 902", v1Prompt);
        Assert.All(JournalRecords().Where(r => r.Type == JournalRecordType.AttachmentsRepaired), r => Assert.False(r.LateRepair));
    }

    [Fact]
    public async Task RetryFailed_LateRepair_KeepsFinishedText()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = doc.Messages[0];
        var excel = Spec(doc, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder(RefuseIds(new[] { excel }));
        var first = await Run(new GenerationRunner(_gateway), Input());
        var ledger = first.Ledger.Entries.Select(e => (e.Id, e.Statement, e.Value)).ToList();
        var docCopies = MailboxWriter.CopyPathsFor(root, Folders).Select(p => p.Replace('/', Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = AllEmls().Where(e => !docCopies.Contains(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);

        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        var before = Requests().Count;
        var result = await Run(new GenerationRunner(_gateway), Input(), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var line = JournalRecords().Single(r => r.Type == JournalRecordType.AttachmentsRepaired);
        Assert.True(line.LateRepair);
        Assert.True(Assert.Single(line.Attachments).LateRepair);
        var calls = Requests().Skip(before).ToList();
        Assert.All(calls, r => Assert.Contains(AttachmentPrompts.LateRepairText, r.UserPrompt));
        Assert.True(calls.Count > 1, "the new detail is retried");
        Assert.Contains(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.AttachmentRetry
                                                     && e.Violations.Any(v => v.Contains("adds a new detail after later emails were written")));
        Assert.Equal(ledger, result.Ledger.Entries.Select(e => (e.Id, e.Statement, e.Value)).ToList());
        var after = AllEmls();
        foreach (var (path, bytes) in others)
            Assert.Equal(bytes, after[path]);
        Assert.All(MailboxWriter.CopyPathsFor(root, Folders), c => Assert.Contains(excel,
            DocParts(MimeMessage.Load(Path.Combine(_folder, c))).Select(p => p.Headers[MailboxWriter.DocIdHeader])));
    }

    [Fact]
    public async Task SkippedMedia_StillNoMedia_NoCalls_ThenMediaAvailable_GeneratedAndRewritten()
    {
        _fake.Respond = Responder();
        await Run(new GenerationRunner(_gateway), Input());
        bool IsMedia(PlannedAttachment a) => a.Kind is AttachmentType.Image or AttachmentType.Voicemail;
        var media = _plan.Threads.SelectMany(AttachmentsOf).Where(IsMedia).ToList();
        Assert.All(media, a => Assert.Equal(JournalRecordType.AttachmentSkipped, Replay().LastByAttachment[a.DocId].Type));

        var before = Requests().Count;
        var still = await Run(new GenerationRunner(_gateway), Input());
        Assert.Equal(GenerationOutcome.Finished, still.Outcome);
        Assert.Equal(before, Requests().Count);
        Assert.All(media, a => Assert.Equal(JournalRecordType.AttachmentSkipped, Replay().LastByAttachment[a.DocId].Type));

        var generator = new FakeMediaGenerator();
        var now = await Run(new GenerationRunner(_gateway), Input(generator));

        Assert.Equal(GenerationOutcome.Finished, now.Outcome);
        Assert.Equal(media.Count, generator.Requests.Count);
        var replay = Replay();
        Assert.All(media, a => Assert.Equal(JournalRecordType.AttachmentDone, replay.LastByAttachment[a.DocId].Type));
        foreach (var message in _plan.Threads.SelectMany(t => t.Messages).Where(m => m.Attachments.Any(IsMedia)))
        {
            foreach (var copy in MailboxWriter.CopyPathsFor(message, Folders))
            {
                var ids = DocParts(MimeMessage.Load(Path.Combine(_folder, copy))).Select(p => p.Headers[MailboxWriter.DocIdHeader]).ToList();
                Assert.All(message.Attachments.Where(IsMedia), a => Assert.Contains(a.DocId, ids));
            }
        }
    }

    // ---------- Phase 5 runs (Existing Run State) ----------

    // A Phase 5 journal: relevant threads only, no attachment records, no journaled From, EMLs without parts
    private async Task WritePhase5Run()
    {
        var stripped = JsonSerializer.Deserialize<CorpusPlan>(JsonSerializer.Serialize(_plan, CaseDraft.JsonOptions), CaseDraft.JsonOptions)!;
        stripped.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
        foreach (var message in stripped.Threads.SelectMany(t => t.Messages))
            message.Attachments.Clear();
        _fake.Respond = Responder();
        var result = await new GenerationRunner(_gateway)
            .RunAsync(new GenerationRunInput { Draft = _draft, Plan = stripped, CaseFolder = _folder }, new GenerationSnapshot())
            .WaitAsync(RunGuard);
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);

        var records = JournalRecords().Where(r => r.Type is JournalRecordType.ThreadDone or JournalRecordType.RunStarted or JournalRecordType.RunEnded).ToList();
        File.Delete(GenerationJournal.PathFor(_folder));
        var journal = new GenerationJournal(_folder);
        foreach (var record in records)
        {
            foreach (var message in record.Messages)
            {
                message.From = string.Empty;
                message.AttachmentTitles.Clear();
            }

            await journal.AppendAsync(record);
        }
    }

    [Fact]
    public async Task Phase5Upgrade_OneContinue_AddsAttachmentsInWaveOrder_WritesNonRelevant()
    {
        await WritePhase5Run();
        Assert.All(Relevant.SelectMany(t => t.Messages), m => Assert.Empty(DocParts(LoadCopy(m))));
        var waveOf = WaveOf();
        var relevantIds = Relevant.Select(t => t.Id).ToHashSet();
        _fake.Respond = Responder();
        var before = Requests().Count;

        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var calls = Requests().Skip(before).ToList();
        Assert.DoesNotContain(calls, r => r.SchemaName == RenderSchema);
        var repairWaves = calls.Select(AttachmentIdOf).OfType<string>().Where(id => relevantIds.Contains(ThreadOfDoc(id)))
            .Select(id => waveOf[ThreadOfDoc(id)]).ToList();
        Assert.NotEmpty(repairWaves);
        Assert.Equal(repairWaves.OrderBy(w => w), repairWaves);
        Assert.Contains(calls, r => r.SchemaName == SideWorldSchema);
        Assert.Equal(NonRelevant.Count, calls.Count(r => r.SchemaName == FillerSchema));

        var state = GenerationRunState.From(_plan, Replay());
        Assert.Equal(state.TotalThreads, state.Written + state.Warnings);
        foreach (var attachment in Relevant.SelectMany(AttachmentsOf).Where(a => a.Kind is not (AttachmentType.Image or AttachmentType.Voicemail)))
        {
            Assert.True(state.StatusByAttachment[attachment.DocId] is AttachmentRunStatus.Written or AttachmentRunStatus.Warnings, attachment.DocId);
            var parent = Relevant.SelectMany(t => t.Messages).Single(m => m.Attachments.Contains(attachment));
            Assert.Contains(attachment.DocId, DocParts(LoadCopy(parent)).Select(p => p.Headers[MailboxWriter.DocIdHeader]));
        }
    }

    [Fact]
    public async Task Phase5Upgrade_VoicemailThread_AudioAdded_TextAsWritten_KeyMatchesEml()
    {
        await WritePhase5Run();
        var voicemail = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantVoicemailThreadId);
        var message = voicemail.Messages.Single();
        var copies = MailboxWriter.CopyPathsFor(message, Folders);
        var before = copies.ToDictionary(c => c, c => MimeMessage.Load(Path.Combine(_folder, c)), StringComparer.Ordinal);
        _fake.Respond = Responder();

        var result = await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        foreach (var (copy, original) in before)
        {
            var eml = MimeMessage.Load(Path.Combine(_folder, copy));
            Assert.Equal(original.From.ToString(), eml.From.ToString());
            Assert.Equal(original.Subject, eml.Subject);
            Assert.Equal(original.TextBody, eml.TextBody);
            Assert.Equal(original.HtmlBody, eml.HtmlBody);
            Assert.Single(DocParts(eml), p => p.ContentType.MimeType == "audio/mpeg");
            Assert.DoesNotContain("Voice Mail", eml.From.ToString());
        }

        var key = KeyHeaders()[message.DocId];
        var first = MimeMessage.Load(Path.Combine(_folder, copies[0]));
        Assert.Equal(first.Subject, key.Subject);
        Assert.Contains(first.From.Mailboxes.Single().Address, key.From);
        Assert.Contains(first.From.Mailboxes.Single().Name!, key.From);
    }

    // ---------- snapshot, stage and events (UI-SPEC S1) ----------

    [Fact]
    public async Task Snapshot_RepairRowOnlyWhenRepairsPending_StageAttachments_StartedEvent()
    {
        var other = AttachmentTestData.Thread(_plan, AttachmentTestData.OtherOrgDocThreadId);
        var refused = Spec(other, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder(RefuseIds(new[] { refused }));
        GenerationView? fresh = null;
        var firstSnapshot = new GenerationSnapshot();
        var firstRunner = new GenerationRunner(_gateway)
        {
            BeforeWaveHook = i =>
            {
                if (i == 0)
                    fresh = firstSnapshot.Peek();
                return Task.CompletedTask;
            }
        };
        await Run(firstRunner, Input(), firstSnapshot);
        Assert.DoesNotContain(fresh!.Waves, w => w.RowKind == StageRowKind.Attachments);

        _fake.Respond = Responder();
        var snapshot = new GenerationSnapshot();
        GenerationView? atStart = null;
        GenerationStage? stage = null;
        var runner = new GenerationRunner(_gateway)
        {
            BeforeWaveHook = i =>
            {
                if (i == 0)
                    atStart = snapshot.Peek();
                return Task.CompletedTask;
            },
            RepairSlotHook = (_, delta) =>
            {
                if (delta > 0)
                    stage = snapshot.Peek().Stage;
            }
        };
        await Run(runner, Input(), snapshot);

        var row = Assert.Single(atStart!.Waves, w => w.RowKind == StageRowKind.Attachments);
        Assert.Equal(RepairRow, row.Index);
        Assert.Equal(1, row.Attachments);
        Assert.Equal(1, row.Threads);
        Assert.Equal(1, row.AttachmentsFailed);
        Assert.Equal(GenerationStage.Attachments, stage);
        var view = snapshot.Peek();
        var end = view.Waves.Single(w => w.RowKind == StageRowKind.Attachments);
        Assert.Equal(1, end.AttachmentsDone);
        Assert.Equal(0, end.AttachmentsFailed);
        Assert.Equal(atStart.Waves.Where(w => w.RowKind != StageRowKind.Attachments).Sum(w => w.AttachmentsDone),
            view.Waves.Where(w => w.RowKind != StageRowKind.Attachments).Sum(w => w.AttachmentsDone));
        Assert.Equal(view.Counts.AttachmentsDone, view.Waves.Sum(w => w.AttachmentsDone));
        var started = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.AttachmentRepairStarted);
        Assert.Equal(1, started.Attachments);
        Assert.Equal(1, started.Threads);
        Assert.Single(view.Events, e => e.Kind == GenerationEventKind.AttachmentsAdded);

        GenerationView? done = null;
        var third = new GenerationSnapshot();
        var thirdRunner = new GenerationRunner(_gateway)
        {
            BeforeWaveHook = i =>
            {
                if (i == 0)
                    done = third.Peek();
                return Task.CompletedTask;
            }
        };
        await Run(thirdRunner, Input(), third);
        Assert.DoesNotContain(done!.Waves, w => w.RowKind == StageRowKind.Attachments);
    }

    // ---------- bounded repair work and deadlock-free chains (review MEDIUM, checkers 06-18 / 06-20) ----------

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
                return;
        }
    }

    private static List<AttachmentRepairGroup> Reversed(List<AttachmentRepairGroup> groups)
    {
        groups.Reverse();
        return groups;
    }

    [Fact]
    public async Task Repairs_AtMostFourAtOnce()
    {
        var names = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliet" };
        var ids = Relevant.OrderBy(MessageTime.FirstUtc).Take(names.Length).Select(t => t.Id).ToList();
        var start = AfterLastThread(30);
        var docIds = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            MoveTo(ids[i], start.AddHours(i));
            docIds.Add(AddAttachment(AttachmentTestData.Thread(_plan, ids[i]).Messages.OrderBy(m => m.Seq).First(), AttachmentType.Word,
                "Field Report " + names[i]).DocId);
        }

        var waveOf = WaveOf();
        Assert.Single(ids.Select(id => waveOf[id]).Distinct());
        _fake.Respond = Responder(RefuseIds(docIds));
        await Run(new GenerationRunner(_gateway), Input());
        Assert.Equal(10, GenerationRunState.From(_plan, Replay()).RepairGroups(_plan, false).Count);

        _fake.Respond = Responder();
        var active = 0;
        var max = 0;
        var runner = new GenerationRunner(DelayedGateway(_ => 25))
        {
            RepairSlotHook = (_, delta) => InterlockedMax(ref max, Interlocked.Add(ref active, delta))
        };
        var result = await runner.RunAsync(Input(), new GenerationSnapshot()).WaitAsync(RunGuard);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.InRange(max, 2, GenerationRunner.MaxConcurrentRepairs);
        Assert.Equal(0, active);
        Assert.All(docIds, d => Assert.Equal(JournalRecordType.AttachmentDone, Replay().LastByAttachment[d].Type));
    }

    [Fact]
    public async Task Repair_FiveChainLinksSameWave_NoSlotDeadlock()
    {
        // Five single-message Harmon threads, one link each, ranked in chain order in one wave
        var ids = new[] { "T-0002", "T-0019", "T-0020", AttachmentTestData.ChainThreadId, AttachmentTestData.SecondInviteThreadId };
        var start = AfterLastThread(30);
        var links = new List<string>();
        for (var i = 0; i < ids.Length; i++)
        {
            MoveTo(ids[i], start.AddHours(2 * i));
            links.Add(AddAttachment(AttachmentTestData.Thread(_plan, ids[i]).Messages.Single(), AttachmentType.PowerPoint, "Daily Log").DocId);
        }

        var chains = AttachmentChains.Compute(_plan, Guide);
        for (var k = 1; k < links.Count; k++)
            Assert.Equal(links[k - 1], chains[links[k]].PreviousDocId);
        var ranks = ChainScheduler.RankThreads(_plan);
        for (var k = 1; k < ids.Length; k++)
            Assert.True(ranks[ids[k - 1]] < ranks[ids[k]], "precondition: ranks follow chain order");
        var waveOf = WaveOf();
        Assert.Single(ids.Select(id => waveOf[id]).Distinct());

        _fake.Respond = Responder(RefuseIds(links));
        await Run(new GenerationRunner(_gateway), Input());

        _fake.Respond = Responder();
        var before = Requests().Count;
        var active = 0;
        var max = 0;
        var runner = new GenerationRunner(DelayedGateway(_ => Random.Shared.Next(0, 5)))
        {
            RepairOrderHook = Reversed,
            RepairSlotHook = (_, delta) => InterlockedMax(ref max, Interlocked.Add(ref active, delta))
        };
        var result = await runner.RunAsync(Input(), new GenerationSnapshot()).WaitAsync(ChainGuard);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.InRange(max, 1, GenerationRunner.MaxConcurrentRepairs);
        var replay = Replay();
        var calls = Requests().Skip(before).ToList();
        for (var k = 1; k < links.Count; k++)
        {
            var previous = replay.LastByAttachment[links[k - 1]].FlattenedText.Trim();
            Assert.NotEmpty(previous);
            Assert.Contains(previous, calls.Last(r => AttachmentIdOf(r) == links[k]).UserPrompt);
        }
    }

    // Copies the source run, repairs it with the given slots and launch order, and returns the repair prompts by DocId
    private async Task<(List<(string DocId, string Prompt)> Prompts, JournalReplay Replay)> RepairMatrixRun(string source, int? slots,
        bool reversed, IReadOnlyCollection<string> docIds)
    {
        var folder = NewFolder();
        CopyFolder(source, folder);
        _fake.Respond = Responder(overrideJson: (docId, _) => docIds.Contains(docId) ? NoNewDetails(KindOf(docId)) : null);
        var before = Requests().Count;
        var runner = new GenerationRunner(DelayedGateway(_ => Random.Shared.Next(0, 5)))
        {
            RepairSlotsOverride = slots,
            RepairOrderHook = reversed ? Reversed : null
        };

        var result = await runner.RunAsync(Input(folder: folder), new GenerationSnapshot()).WaitAsync(ChainGuard);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var replay = Replay(folder);
        Assert.All(docIds, d => Assert.Equal(JournalRecordType.AttachmentDone, replay.LastByAttachment[d].Type));
        var prompts = Requests().Skip(before)
            .Select(r => (DocId: AttachmentIdOf(r) ?? string.Empty, Prompt: r.UserPrompt ?? string.Empty))
            .Where(p => docIds.Contains(p.DocId))
            .OrderBy(p => p.DocId, StringComparer.Ordinal)
            .ToList();
        return (prompts, replay);
    }

    [Fact]
    public async Task Repair_InterleavedChainsTwoThreads_NoDeadlock()
    {
        var (a, b, v1, v2, v3) = InterleavedChain();
        _fake.Respond = Responder(RefuseIds(new[] { v1, v2, v3 }));
        await Run(new GenerationRunner(_gateway), Input());

        var runs = new List<List<(string DocId, string Prompt)>>();
        foreach (var slots in new int?[] { null, 1 })
        {
            foreach (var reversed in new[] { false, true })
            {
                var (prompts, replay) = await RepairMatrixRun(_folder, slots, reversed, new[] { v1, v2, v3 });
                Assert.Contains(replay.LastByAttachment[v1].FlattenedText.Trim(), prompts.Single(p => p.DocId == v2).Prompt);
                Assert.Contains(AttachmentPrompts.EarlierDraftNotAvailable, prompts.Single(p => p.DocId == v3).Prompt);
                runs.Add(prompts);
            }
        }

        Assert.True(ChainScheduler.RankThreads(_plan)[a.Id] < ChainScheduler.RankThreads(_plan)[b.Id]);
        Assert.All(runs, r => Assert.Equal(runs[0], r));
    }

    [Fact]
    public async Task Repair_CrossingChainsTwoThreads_NoDeadlock()
    {
        // X: A msg 1 -> B; Y: B -> A msg 3 (A = T-0904, B = T-0903 moved six hours after A's root)
        var a = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var b = AttachmentTestData.Thread(_plan, AttachmentTestData.OtherOrgDocThreadId);
        Shift(a.Id, TimeSpan.FromDays(2));
        Shift(b.Id, a.Messages[0].SentAt.AddHours(6) - b.Messages[0].SentAt);
        var x2 = b.Messages[0].Attachments.Single();
        x2.Kind = AttachmentType.Word;
        x2.Title = "Site Visit Notes v2";
        x2.FileName = "Site Visit Notes v2.docx";
        var x1 = Spec(a, 1, AttachmentType.Word).DocId;
        var y1 = AddAttachment(b.Messages[0], AttachmentType.PowerPoint, "Crew Plan").DocId;
        var y2 = AddAttachment(a.Messages.Single(m => m.Seq == 3), AttachmentType.PowerPoint, "Crew Plan v2").DocId;
        var links = AttachmentChains.Compute(_plan, Guide);
        Assert.Equal(x1, links[x2.DocId].PreviousDocId);
        Assert.Equal(y1, links[y2].PreviousDocId);
        Assert.Equal(WaveOf()[a.Id], WaveOf()[b.Id]);
        Assert.True(ChainScheduler.RankThreads(_plan)[a.Id] < ChainScheduler.RankThreads(_plan)[b.Id]);
        var docIds = new[] { x1, x2.DocId, y1, y2 };
        _fake.Respond = Responder(RefuseIds(docIds));
        await Run(new GenerationRunner(_gateway), Input());

        var runs = new List<List<(string DocId, string Prompt)>>();
        foreach (var slots in new int?[] { null, 1 })
        {
            foreach (var reversed in new[] { false, true })
            {
                var (prompts, replay) = await RepairMatrixRun(_folder, slots, reversed, docIds);
                Assert.Contains(replay.LastByAttachment[x1].FlattenedText.Trim(), prompts.Single(p => p.DocId == x2.DocId).Prompt);
                Assert.Contains(AttachmentPrompts.EarlierDraftNotAvailable, prompts.Single(p => p.DocId == y2).Prompt);
                runs.Add(prompts);
            }
        }

        Assert.All(runs, r => Assert.Equal(runs[0], r));
    }

    // ---------- spend, Stage B, fatal and cancel ----------

    [Fact]
    public async Task RepairFatal_AfterPaidCall_SpendInRunEndedOnce()
    {
        var doc = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var excel = Spec(doc, 1, AttachmentType.Excel).DocId;
        var slides = Spec(doc, 1, AttachmentType.PowerPoint).DocId;
        _fake.Respond = Responder(RefuseIds(new[] { excel, slides }));
        var snapshot = new GenerationSnapshot();
        await Run(new GenerationRunner(_gateway), Input());
        var spentBefore = Replay().SpentCostUsd;

        _fake.Respond = Responder(r => AttachmentIdOf(r) == excel ? Fatal(800, 300) : null);
        var before = Requests().Count;
        var result = await Run(new GenerationRunner(_gateway, 1), Input(), snapshot);

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        Assert.DoesNotContain(Requests().Skip(before), r => AttachmentIdOf(r) == slides);
        var ended = JournalRecords().Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.Equal(800, ended.InputTokens);
        Assert.Equal(300, ended.OutputTokens);
        Assert.Equal(_story.CalculateTotalCost(800, 300), ended.CostUsd);
        Assert.Equal(spentBefore + _story.CalculateTotalCost(800, 300), Replay().SpentCostUsd);
        Assert.Equal(Replay().SpentCostUsd, snapshot.Peek().Counts.CostUsd);
        Assert.DoesNotContain(JournalRecords(), r => r.Type == JournalRecordType.AttachmentsRepaired);
    }

    [Fact]
    public async Task NonRelevantRepairs_RunInStageB_AfterSideWorld()
    {
        var daily = AttachmentTestData.Thread(_plan, AttachmentTestData.DailyBusinessThreadId);
        var ancillary = AttachmentTestData.Thread(_plan, AttachmentTestData.AncillaryThreadId);
        var word = daily.Messages[0].Attachments[0].DocId;
        var excel = ancillary.Messages[0].Attachments[0].DocId;
        _fake.Respond = Responder(RefuseIds(new[] { word, excel }));
        await Run(new GenerationRunner(_gateway), Input());
        Assert.Equal(1, SideWorldRecords());

        _fake.Respond = Responder();
        var before = Requests().Count;
        var result = await Run(new GenerationRunner(_gateway), Input());

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Equal(new[] { excel, word }.OrderBy(d => d), Requests().Skip(before).Select(AttachmentIdOf).OrderBy(d => d));
        Assert.Equal(1, SideWorldRecords());
        var repaired = JournalRecords().Where(r => r.Type == JournalRecordType.AttachmentsRepaired).Select(r => r.ThreadId).ToList();
        Assert.Equal(new[] { daily.Id, ancillary.Id }.OrderBy(i => i), repaired.OrderBy(i => i));
        var key = KeyRows();
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, key[word].Status);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, key[excel].Status);
    }

    [Fact]
    public async Task NonRelevantRepairs_FatalSkipsRest_CancelKeepsCommitted()
    {
        var daily = AttachmentTestData.Thread(_plan, AttachmentTestData.DailyBusinessThreadId);
        var ancillary = AttachmentTestData.Thread(_plan, AttachmentTestData.AncillaryThreadId);
        var word = daily.Messages[0].Attachments[0].DocId;
        var excel = ancillary.Messages[0].Attachments[0].DocId;
        _fake.Respond = Responder(RefuseIds(new[] { word, excel }));
        await Run(new GenerationRunner(_gateway), Input());
        var source = NewFolder();
        CopyFolder(_folder, source);

        // RepairGroups order: first send time, then id
        var first = new[] { daily, ancillary }.OrderBy(t => t.Messages.Min(m => m.SentAt)).ThenBy(t => t.Id, StringComparer.Ordinal).First();
        var firstDoc = first.Id == daily.Id ? word : excel;
        var secondDoc = firstDoc == word ? excel : word;

        // Fatal on the first repair: the second never starts
        _fake.Respond = Responder(r => AttachmentIdOf(r) == firstDoc ? Fatal(800, 300) : null);
        var before = Requests().Count;
        var fatal = await Run(new GenerationRunner(_gateway, 1) { RepairSlotsOverride = 1 }, Input());
        Assert.Equal(GenerationOutcome.Fatal, fatal.Outcome);
        Assert.DoesNotContain(Requests().Skip(before), r => AttachmentIdOf(r) == secondDoc);

        // Cancel while the second repair's call is sent: the first stays journaled
        var folder = NewFolder();
        CopyFolder(source, folder);
        _fake.Respond = Responder();
        using var cts = new CancellationTokenSource();
        var start = Requests().Count;
        _fake.OnSend = index =>
        {
            if (index == start + 1)
                cts.Cancel();
        };
        var cancelled = await Run(new GenerationRunner(_gateway, 1) { RepairSlotsOverride = 1 }, Input(folder: folder), ct: cts.Token);
        _fake.OnSend = null;

        Assert.Equal(GenerationOutcome.Cancelled, cancelled.Outcome);
        var replay = Replay(folder);
        Assert.Equal(JournalRecordType.AttachmentDone, replay.LastByAttachment[firstDoc].Type);
        Assert.Equal(JournalRecordType.AttachmentFailed, replay.LastByAttachment[secondDoc].Type);
    }
}

// ---------- PROV-07: gateway cost reaches the journal unchanged (07-05) ----------

public partial class GenerationRunnerMixedTests
{
    // Every successful response also reports cache reads (and the first one a cache write)
    private Func<LlmProviderRequest, LlmProviderResponse> Cached(Func<LlmProviderRequest, LlmProviderResponse> inner)
    {
        var calls = 0;
        return request =>
        {
            var response = inner(request);
            if (response.Outcome != LlmOutcome.Succeeded)
                return response;

            response.CacheReadTokens = 400;
            if (Interlocked.Increment(ref calls) == 1)
                response.CacheWriteTokens = 120;
            return response;
        };
    }

    private void PriceCache()
    {
        _story.CacheWriteTokenPricePerMillion = 3.75m;
        _story.CacheReadTokenPricePerMillion = 0.30m;
        _volume.CacheWriteTokenPricePerMillion = 1.25m;
        _volume.CacheReadTokenPricePerMillion = 0.10m;
    }

    private static void AssertItemsAddUp(JournalReplay replay) =>
        Assert.Equal(replay.SpentCostUsd, replay.SpentDetail.TotalUsd + replay.MediaSpentCostUsd + replay.UnsplitSpentUsd);

    [Fact]
    public async Task CacheReads_ReachThreadDoneCostDetail_SnapshotEqualsJournal()
    {
        PriceCache();
        _fake.Respond = Cached(Responder());
        var snapshot = new GenerationSnapshot();

        var result = await Run(new GenerationRunner(_gateway), Input(new FakeMediaGenerator()), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var records = JournalRecords();
        var started = Assert.Single(records, r => r.Type == JournalRecordType.RunStarted);
        Assert.Equal(_gateway.Provider.ToString(), started.Provider);

        var done = records.Where(r => r.Type == JournalRecordType.ThreadDone).ToList();
        Assert.NotEmpty(done);
        foreach (var record in done)
        {
            Assert.NotNull(record.CostDetail);
            Assert.True(record.CostDetail!.CacheReadUsd > 0m);
            Assert.True(record.CacheReadTokens > 0);
            Assert.Equal(record.CostDetail.TotalUsd, record.CostUsd);
            foreach (var attachment in record.Attachments)
            {
                Assert.NotNull(attachment.CostDetail);
                Assert.Equal(attachment.CostDetail!.TotalUsd, attachment.CostUsd);
            }
        }

        foreach (var own in records.Where(r => r.Type is JournalRecordType.SideWorld or JournalRecordType.CastVoices))
        {
            Assert.NotNull(own.CostDetail);
            Assert.True(own.CostDetail!.CacheReadUsd > 0m);
            Assert.Equal(own.CostDetail.TotalUsd, own.CostUsd);
        }

        var ended = records.Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.NotNull(ended.CostDetail);
        Assert.Equal(ended.CostDetail!.TotalUsd, ended.CostUsd);

        var replay = Replay();
        Assert.Equal(0m, replay.UnsplitSpentUsd);
        Assert.True(replay.SpentDetail.CacheReadUsd > 0m);
        Assert.True(replay.SpentDetail.CacheWriteUsd > 0m);
        AssertItemsAddUp(replay);
        Assert.Equal(replay.SpentCostUsd, snapshot.Peek().Counts.CostUsd);
    }

    [Fact]
    public async Task NotRunThreadAndVoiceCast_RunEndedFullyItemized()
    {
        // The voice call is paid and journaled; the first thread render is paid, then a fatal stop leaves it NotRun
        PriceCache();
        var renders = 0;
        _fake.Respond = Cached(Responder(r =>
            r.SchemaName == RenderSchema && Interlocked.Increment(ref renders) == 1 ? Fatal(900, 350) : null));
        var snapshot = new GenerationSnapshot();

        var result = await Run(new GenerationRunner(_gateway, 1), Input(new FakeMediaGenerator()), snapshot);

        Assert.Equal(GenerationOutcome.Fatal, result.Outcome);
        var records = JournalRecords();
        var voices = Assert.Single(records, r => r.Type == JournalRecordType.CastVoices);
        Assert.NotNull(voices.CostDetail);
        Assert.Equal(voices.CostDetail!.TotalUsd, voices.CostUsd);
        Assert.True(voices.CostUsd > 0m);

        var ended = records.Last();
        Assert.Equal(JournalRecordType.RunEnded, ended.Type);
        Assert.NotNull(ended.CostDetail);
        Assert.Equal(ended.CostDetail!.TotalUsd, ended.CostUsd);
        Assert.Equal(900, ended.InputTokens);
        Assert.Equal(350, ended.OutputTokens);
        Assert.Equal(_story.CalculateTotalCost(900, 350), ended.CostUsd);

        var replay = Replay();
        Assert.Equal(0m, replay.UnsplitSpentUsd);
        AssertItemsAddUp(replay);
        Assert.Equal(replay.SpentCostUsd, snapshot.Peek().Counts.CostUsd);
    }
}

public partial class GenerationRunnerTests
{
    [Fact]
    public async Task RunStarted_RecordsTheWritingProvider()
    {
        var fake = new FakeLlmProvider(LlmProviderKind.OpenAI) { Respond = Responder() };
        var story = ReelDiscovery.Tests.Case.CaseTestData.StoryModel();
        var volume = ReelDiscovery.Tests.Case.CaseTestData.VolumeModel();
        story.Provider = LlmProviderKind.OpenAI;
        volume.Provider = LlmProviderKind.OpenAI;

        var result = await new GenerationRunner(new LlmGateway(fake, story, volume), 1).RunAsync(Input, new GenerationSnapshot(), Persist);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var started = Assert.Single(JournalRecords(), r => r.Type == JournalRecordType.RunStarted);
        Assert.Equal("OpenAI", started.Provider);
        Assert.Equal("OpenAI", Replay().WriterProvider);
    }
}
