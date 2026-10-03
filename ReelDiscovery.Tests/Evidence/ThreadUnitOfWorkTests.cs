using System.Text.Json;
using System.Text.Json.Serialization;
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

public class ThreadUnitOfWorkTests : IDisposable
{
    private const string RetryHint = "Click Continue Generating.";
    private const string FeedbackHeader = "Problems in the previous attempt";

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
    private readonly GenerationJournal _journal;
    private readonly GenerationSnapshot _snapshot;
    private readonly AIModelConfig _story;

    public ThreadUnitOfWorkTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = EvidenceTestData.ApprovedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();
        _story = _gateway.ResolveModel(LlmTier.Story);
        _journal = new GenerationJournal(_folder);
        _snapshot = new GenerationSnapshot();
        _snapshot.Reset(new GenerationCounts { TotalThreads = 8, WaveCount = 1 },
            new[] { new WaveProgress { Index = 1, Threads = 8 } });
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    private IReadOnlyDictionary<string, string> Folders => PlanNumbering.CustodianFolders(_draft.Guide!);

    private ThreadUnitOfWork Unit() =>
        new(_gateway, _journal, new MailboxWriter(_folder, Folders), _snapshot, _story);

    private ThreadRenderContext Context(string factId)
    {
        var thread = PlanFixtures.Thread(_plan, factId);
        var people = PeopleDirectory.Create(_draft.Guide!, _plan);
        var assignment = RenderAssignments.Compute(_plan)[thread.Id];
        return ThreadRenderContext.Create(_draft, _plan, thread, assignment, people, new List<LedgerEntry>(), 1);
    }

    private static PlanCallRun Run(CancellationTokenSource cts) => new(cts.Token, 1, RetryHint);

    private List<string> JournalLines() =>
        File.Exists(GenerationJournal.PathFor(_folder))
            ? File.ReadAllLines(GenerationJournal.PathFor(_folder)).Where(l => l.Length > 0).ToList()
            : new List<string>();

    private JournalRecord SingleRecord(PlannedThread thread)
    {
        Assert.Single(JournalLines());
        var replay = GenerationJournal.Replay(_folder, _plan);
        return replay.LastByThread[thread.Id];
    }

    private List<string> EmlFiles() =>
        Directory.Exists(Path.Combine(_folder, "Mailboxes"))
            ? Directory.GetFiles(Path.Combine(_folder, "Mailboxes"), "*.eml", SearchOption.AllDirectories).ToList()
            : new List<string>();

    /// <summary>
    /// Wraps the clean responder: <paramref name="mutate"/> gets the 1-based call number and the clean DTO; it may edit the
    /// DTO in place (returns null) or return a replacement response.
    /// </summary>
    private Func<LlmProviderRequest, LlmProviderResponse> Mutating(Func<int, ThreadRenderer.ThreadRenderDto, LlmProviderResponse?> mutate)
    {
        var inner = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        var call = 0;
        return request =>
        {
            var response = inner(request);
            var n = Interlocked.Increment(ref call);
            var dto = JsonSerializer.Deserialize<ThreadRenderer.ThreadRenderDto>(response.Text!, DtoOptions)!;
            return mutate(n, dto) ?? FakeLlmProvider.Ok(JsonSerializer.Serialize(dto, DtoOptions), 1000, 400);
        };
    }

    private static void NameStrangers(ThreadRenderer.ThreadRenderDto dto, params string[] names) =>
        dto.Messages[0].PeopleNamed = names.ToList();

    private void AssertFailedCost(JournalRecord record, int input, int output)
    {
        Assert.Equal(input, record.InputTokens);
        Assert.Equal(output, record.OutputTokens);
        Assert.Equal(_story.CalculateTotalCost(input, output), record.CostUsd);
    }

    [Fact]
    public async Task CleanThread_IsWrittenAndJournaled()
    {
        _fake.Respond = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        Assert.Single(_fake.Requests);
        var expected = ctx.Thread.Messages.SelectMany(m => MailboxWriter.CopyPathsFor(m, Folders)).Distinct().ToList();
        Assert.NotEmpty(expected);
        foreach (var relative in expected)
            Assert.True(File.Exists(Path.Combine(_folder, relative)), relative);

        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadDone, record.Type);
        Assert.Equal(ThreadWriteStatus.Written, record.Status);
        Assert.Equal(1, record.Attempts);
        Assert.Equal(expected.OrderBy(p => p), record.Files.OrderBy(p => p));
        Assert.Equal(ctx.Thread.Messages.Count, record.Messages.Count);
        Assert.Equal(ctx.Thread.Messages.OrderBy(m => m.Seq).Select(m => m.DocId), record.Messages.Select(m => m.DocId));
        Assert.All(record.Messages, m =>
        {
            Assert.NotEmpty(m.MessageId);
            Assert.NotEmpty(m.Subject);
            Assert.NotEmpty(m.BodyPlain);
            Assert.Equal("Thanks,", m.SignOff);
        });
        Assert.Contains(record.LedgerDelta, d => d.About.StartsWith("site walk", StringComparison.Ordinal));
        Assert.True(record.InputTokens > 0 && record.OutputTokens > 0);
        Assert.Equal(_story.CalculateTotalCost(record.InputTokens, record.OutputTokens), record.CostUsd);
        Assert.Equal(expected.Count, outcome.Files);
        Assert.Equal(ctx.Thread.Messages.Count, outcome.Emails);

        var view = _snapshot.Drain();
        Assert.Equal(1, view.Counts.DoneThreads);
        var written = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.ThreadWritten);
        var root = ctx.Thread.Messages.OrderBy(m => m.Seq).First();
        Assert.Equal(EmailComposer.SubjectFor(ctx.Thread, root, ctx.Assignment.Marking), written.Subject);
        Assert.Equal(ctx.Thread.Messages.OrderBy(m => m.Seq).Last().FilePath, written.FilePath);
        Assert.Equal(ctx.Thread.Id, written.ThreadId);
    }

    [Fact]
    public async Task ViolationThenClean_RetriesWithFeedback()
    {
        _fake.Respond = Mutating((n, dto) =>
        {
            if (n == 1)
                NameStrangers(dto, "Harlan Voss");
            return null;
        });
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        Assert.Equal(2, _fake.Requests.Count);
        var second = _fake.Requests[1].UserPrompt!;
        var block = second[second.IndexOf(FeedbackHeader, StringComparison.Ordinal)..];
        Assert.Contains("Harlan Voss", block);
        Assert.StartsWith(_fake.Requests[0].UserPrompt!.TrimEnd(), second);

        var events = _snapshot.Drain().Events;
        var retry = events.FindIndex(e => e.Kind == GenerationEventKind.Retry);
        var done = events.FindIndex(e => e.Kind == GenerationEventKind.ThreadWritten);
        Assert.True(retry >= 0 && done > retry);
        Assert.Equal(1, events[retry].Attempt);
        Assert.Contains(events[retry].Violations, v => v.Contains("Harlan Voss"));
        Assert.Equal(2, SingleRecord(ctx.Thread).Attempts);
    }

    [Fact]
    public async Task AlwaysViolating_KeepsFewestViolations_AsWarnings()
    {
        _fake.Respond = Mutating((n, dto) =>
        {
            switch (n)
            {
                case 1:
                    NameStrangers(dto, "Harlan Voss", "Mirela Tansey");
                    break;
                case 2:
                    NameStrangers(dto, "Harlan Voss", "Mirela Tansey", "Corbin Ashdale");
                    break;
                default:
                    NameStrangers(dto, "Corbin Ashdale");
                    dto.Messages[0].Body += " Marker3.";
                    break;
            }
            return null;
        });
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(3, _fake.Requests.Count);
        Assert.Equal(ThreadOutcomeKind.Warnings, outcome.Kind);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(ThreadWriteStatus.Warnings, record.Status);
        Assert.Equal(3, record.Attempts);
        var violation = Assert.Single(record.Violations);
        Assert.Contains("Corbin Ashdale", violation);
        Assert.StartsWith("msg 1: ", violation);
        Assert.Contains("Marker3", record.Messages[0].BodyPlain);

        var view = _snapshot.Drain();
        Assert.Equal(1, view.Counts.WarningThreads);
        var kept = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.KeptWithWarnings);
        Assert.Contains(kept.Violations, v => v.Contains("Corbin Ashdale"));
    }

    [Fact]
    public async Task Refusal_FailsWithoutReask()
    {
        _fake.Respond = _ => FakeLlmProvider.Refused("cannot help with that");
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Failed, outcome.Kind);
        Assert.Single(_fake.Requests);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadFailed, record.Type);
        Assert.Equal(ThreadFailureKind.Refused, record.FailureKind);
        Assert.StartsWith("The model declined", record.Reason);
        AssertFailedCost(record, 10, 2);
        Assert.Empty(EmlFiles());

        var view = _snapshot.Drain();
        Assert.Equal(1, view.Counts.FailedThreads);
        Assert.Contains(view.Events, e => e.Kind == GenerationEventKind.ThreadFailed);
    }

    [Fact]
    public async Task AuthFailure_StopsRun_LeavesThreadPending()
    {
        _fake.Respond = _ => FakeLlmProvider.Failed(LlmFailureKind.Auth, "invalid key");
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.True(run.IsFatal);
        Assert.Equal(ThreadOutcomeKind.NotRun, outcome.Kind);
        Assert.Empty(JournalLines());
        Assert.Empty(EmlFiles());
    }

    [Fact]
    public async Task FatalAfterAnAttempt_NotRunCarriesTheSpend()
    {
        _fake.Respond = Mutating((n, dto) =>
        {
            if (n == 1)
            {
                NameStrangers(dto, "Odell Prewitt");
                return null;
            }
            return FakeLlmProvider.Failed(LlmFailureKind.Auth, "invalid key");
        });
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.NotRun, outcome.Kind);
        Assert.Empty(JournalLines());
        Assert.True(outcome.InputTokens >= 1000 && outcome.OutputTokens >= 400);
        Assert.Equal(_story.CalculateTotalCost(outcome.InputTokens, outcome.OutputTokens), outcome.CostUsd);
        Assert.Equal(outcome.CostUsd, _snapshot.Drain().Counts.CostUsd);
    }

    [Fact]
    public async Task ViolationThenTransportFailure_KeepsFirstAttemptAsWarnings()
    {
        _fake.Respond = Mutating((n, dto) =>
        {
            if (n == 1)
            {
                NameStrangers(dto, "Harlan Voss");
                return null;
            }
            return FakeLlmProvider.Failed(LlmFailureKind.Transient, "connection reset");
        });
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Warnings, outcome.Kind);
        Assert.Equal(2, _fake.Requests.Count);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadDone, record.Type);
        Assert.Equal(ThreadWriteStatus.Warnings, record.Status);
        Assert.Contains(record.Violations, v => v.Contains("Harlan Voss"));
    }

    [Fact]
    public async Task MissingMessageOnEveryAttempt_FailsAsProvider()
    {
        _fake.Respond = Mutating((_, dto) =>
        {
            dto.Messages.RemoveAt(dto.Messages.Count - 1);
            return null;
        });
        var ctx = Context("F-003");
        Assert.Equal(4, ctx.Thread.Messages.Count);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Failed, outcome.Kind);
        Assert.Equal(3, _fake.Requests.Count);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadFailed, record.Type);
        Assert.Equal(ThreadFailureKind.Provider, record.FailureKind);
        Assert.Contains("has 3 messages; the plan has 4", record.Reason);
        AssertFailedCost(record, 3000, 1200);
        Assert.Empty(EmlFiles());
    }

    [Fact]
    public async Task ComposableAttempt_BeatsStructurallyBrokenAttempts_WithFewerViolations()
    {
        // Attempts 1 and 3 drop a message (structural, few violations); attempt 2 is complete but names many strangers
        _fake.Respond = Mutating((n, dto) =>
        {
            if (n == 2)
                NameStrangers(dto, "Odell Prewitt", "Sabine Okafor", "Tobias Wrenfield", "Lucinda Marrow", "Anselm Crayle", "Petra Holloway");
            else
                dto.Messages.RemoveAt(dto.Messages.Count - 1);
            return null;
        });
        var ctx = Context("F-003");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(3, _fake.Requests.Count);
        Assert.Equal(ThreadOutcomeKind.Warnings, outcome.Kind);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadDone, record.Type);
        Assert.Equal(ThreadWriteStatus.Warnings, record.Status);
        Assert.Equal(ctx.Thread.Messages.Count, record.Messages.Count);
        Assert.Contains(record.Violations, v => v.Contains("Odell Prewitt"));
        Assert.DoesNotContain(record.Violations, v => v.Contains("the plan has"));
    }

    [Fact]
    public async Task BlockedTargetPath_FailsAsWrite_WithoutThrowing()
    {
        _fake.Respond = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        var ctx = Context("F-003");
        var target = MailboxWriter.CopyPathsFor(ctx.Thread.Messages.OrderBy(m => m.Seq).First(), Folders)[0];
        Directory.CreateDirectory(Path.Combine(_folder, target));
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Failed, outcome.Kind);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadFailed, record.Type);
        Assert.Equal(ThreadFailureKind.Write, record.FailureKind);
        Assert.StartsWith("Could not write", record.Reason);
        AssertFailedCost(record, 1000, 400);
        Assert.False(run.IsFatal);
    }

    [Fact]
    public async Task UserCancelDuringCall_Propagates_WithoutJournal()
    {
        _fake.Respond = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        using var cts = new CancellationTokenSource();
        _fake.OnSend = _ => cts.Cancel();
        var ctx = Context("F-003");
        using var run = Run(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Unit().RunAsync(ctx, run));

        Assert.Empty(JournalLines());
    }

    [Fact]
    public async Task InvalidExternalAddress_FailsOnlyThisThread()
    {
        _plan.Externals.First(e => e.Id == EvidenceTestData.VendorId).Email = "not an address <<";
        _fake.Respond = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        var ctx = Context("F-004");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(ThreadOutcomeKind.Failed, outcome.Kind);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadFailed, record.Type);
        Assert.Equal(ThreadFailureKind.Provider, record.FailureKind);
        Assert.StartsWith("Could not build thread", record.Reason);
        AssertFailedCost(record, 1000, 400);
        Assert.Equal(1, _snapshot.Drain().Counts.FailedThreads);
    }
}
