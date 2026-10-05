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

public partial class ThreadUnitOfWorkTests : IDisposable
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
    private readonly AIModelConfig _volume;

    public ThreadUnitOfWorkTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();

        // ApprovedCase plus the Phase 6 attachment and non-relevant threads (T-0901..T-0915)
        (_draft, _plan) = AttachmentTestData.MixedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();
        _story = _gateway.ResolveModel(LlmTier.Story);
        _volume = _gateway.ResolveModel(LlmTier.Volume);

        // Priced so Story and Volume spend can be told apart (D-29)
        _story.InputTokenPricePerMillion = 3m;
        _story.OutputTokenPricePerMillion = 15m;
        _volume.InputTokenPricePerMillion = 1m;
        _volume.OutputTokenPricePerMillion = 5m;
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

    // ---------- render profiles (NOISE-05, D-12 strict, D-13, D-23, D-29) ----------

    private ThreadRenderContext ThreadCtx(string threadId, int wave = 1, IReadOnlyList<LedgerEntry>? ledger = null)
    {
        var thread = AttachmentTestData.Thread(_plan, threadId);
        var people = PeopleDirectory.Create(_draft.Guide!, _plan);
        var assignment = thread.Relevance == ThreadRelevance.Relevant
            ? RenderAssignments.Compute(_plan)[thread.Id]
            : FillerAssignments.Compute(_plan)[thread.Id];
        return ThreadRenderContext.Create(_draft, _plan, thread, assignment, people, ledger ?? new List<LedgerEntry>(), wave);
    }

    private static string FillerSchema => JsonSchemaRegistry.GetSchemaName(typeof(FillerRenderer.FillerThreadDto));

    private string FillerJson(string threadId, Action<FillerRenderer.FillerThreadDto> edit)
    {
        var thread = AttachmentTestData.Thread(_plan, threadId);
        FillerAssignments.Compute(_plan).TryGetValue(threadId, out var assignment);
        var dto = AttachmentResponder.FillerDto(thread, assignment);
        edit(dto);
        return JsonSerializer.Serialize(dto, DtoOptions);
    }

    [Fact]
    public async Task Filler_DailyBusiness_VolumeTier_StructureOnly_NoLengthRetry()
    {
        var ctx = ThreadCtx(AttachmentTestData.DailyBusinessThreadId);
        ctx.Assignment.Messages.Single(m => m.Seq == 1).Length = LengthBand.Short;
        _fake.Respond = AttachmentResponder.Create(_plan, _draft.Guide!, (id, attempt) =>
            id == ctx.Thread.Id && attempt == 1
                ? FillerJson(id, d => d.Messages[0].Body = "Harlan Voss called. " + EvidenceTestData.Filler(400))
                : null);
        var profile = RenderProfile.For(ctx.Thread, _gateway, null);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, profile, run);

        Assert.Equal(RenderProfile.FillerName, profile.Name);
        Assert.Equal(LlmTier.Volume, profile.Tier);
        Assert.False(profile.UsesLedger);
        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        var request = Assert.Single(_fake.Requests);
        Assert.Equal(FillerSchema, request.SchemaName);
        Assert.Equal(FillerPrompts.SystemPrompt, request.SystemPrompt);
        Assert.Equal("claude-volume", request.ModelId);

        var record = SingleRecord(ctx.Thread);
        Assert.Equal(ThreadWriteStatus.Written, record.Status);
        Assert.Empty(record.Violations);
        Assert.Empty(record.LedgerDelta);
        Assert.Equal(_volume.CalculateTotalCost(1000, 400), record.CostUsd);
        Assert.NotEqual(_story.CalculateTotalCost(1000, 400), record.CostUsd);
        Assert.Contains("Harlan Voss", record.Messages[0].BodyPlain);

        // Journal messages carry the rendered From and the filler titles
        var root = ctx.Thread.Messages.Single(m => m.Seq == 1);
        var sender = ctx.People.Find(root.FromId)!;
        Assert.Equal($"{sender.FullName} <{sender.Email}>", record.Messages[0].From);
        Assert.All(record.Messages, m => Assert.Contains("@", m.From));
        Assert.Equal(new[] { "Word notes 1" }, record.Messages[0].AttachmentTitles);
        Assert.Empty(record.Messages[1].AttachmentTitles);

        var view = _snapshot.Drain();
        Assert.Equal(1, view.Counts.NonRelevantDone);
        Assert.Equal(0, view.Counts.RelevantDone);
        Assert.False(Assert.Single(view.Events, e => e.Kind == GenerationEventKind.ThreadWritten).Relevant);
    }

    [Fact]
    public async Task Filler_MissingMessage_IsV1_AndRetried()
    {
        var ctx = ThreadCtx(AttachmentTestData.DailyBusinessThreadId);
        _fake.Respond = AttachmentResponder.Create(_plan, _draft.Guide!, (id, attempt) =>
            id == ctx.Thread.Id && attempt == 1 ? FillerJson(id, d => d.Messages.RemoveAt(d.Messages.Count - 1)) : null);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, RenderProfile.For(ctx.Thread, _gateway, null), run);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        Assert.Equal(2, _fake.Requests.Count);
        Assert.Contains(FeedbackHeader, _fake.Requests[1].UserPrompt);
        var retry = Assert.Single(_snapshot.Drain().Events, e => e.Kind == GenerationEventKind.Retry);
        Assert.Contains(retry.Violations, v => v.Contains("the plan has 3", StringComparison.Ordinal));
        Assert.Equal(_volume.CalculateTotalCost(2000, 800), SingleRecord(ctx.Thread).CostUsd);
    }

    [Fact]
    public async Task Decoy_MustNotMentionHit_RetriedWithD1Feedback()
    {
        var ctx = ThreadCtx(AttachmentTestData.DecoyThreadId);
        _fake.Respond = AttachmentResponder.Create(_plan, _draft.Guide!, (id, attempt) =>
            id == ctx.Thread.Id && attempt == 1
                ? FillerJson(id, d => d.Messages[0].Body = $"About {AttachmentTestData.DecoyFactTitle}, the log needs tidying.")
                : null);
        var profile = RenderProfile.For(ctx.Thread, _gateway, null);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, profile, run);

        Assert.Equal(RenderProfile.DecoyName, profile.Name);
        Assert.Equal(LlmTier.Story, profile.Tier);
        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        Assert.Equal(2, _fake.Requests.Count);
        Assert.All(_fake.Requests, r => Assert.Equal("claude-story", r.ModelId));
        var second = _fake.Requests[1].UserPrompt!;
        var block = second[second.IndexOf(FeedbackHeader, StringComparison.Ordinal)..];
        Assert.Contains(AttachmentTestData.DecoyFactTitle, block);
        Assert.Empty(SingleRecord(ctx.Thread).LedgerDelta);
    }

    [Fact]
    public async Task Decoy_HitOnEveryAttempt_KeptWithWarnings()
    {
        var ctx = ThreadCtx(AttachmentTestData.DecoyThreadId);
        _fake.Respond = AttachmentResponder.Create(_plan, _draft.Guide!, (id, _) =>
            id == ctx.Thread.Id
                ? FillerJson(id, d => d.Messages[0].Body = $"The {AttachmentTestData.DecoyAmount} figure is in the log.")
                : null);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, RenderProfile.For(ctx.Thread, _gateway, null), run);

        Assert.Equal(ThreadOutcomeKind.Warnings, outcome.Kind);
        Assert.Equal(3, _fake.Requests.Count);
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(ThreadWriteStatus.Warnings, record.Status);
        Assert.Contains(record.Violations, v => v.Contains(AttachmentTestData.DecoyAmount, StringComparison.Ordinal));
        Assert.Equal(_story.CalculateTotalCost(3000, 1200), record.CostUsd);
    }

    [Fact]
    public async Task Bulk_Newsletter_VolumeTier_WritesNewsletterHtml()
    {
        var ctx = ThreadCtx(AttachmentTestData.NewsletterThreadId);
        _fake.Respond = AttachmentResponder.Create(_plan, _draft.Guide!);
        var profile = RenderProfile.For(ctx.Thread, _gateway, null);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, profile, run);

        Assert.Equal(LlmTier.Volume, profile.Tier);
        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        Assert.Equal("claude-volume", Assert.Single(_fake.Requests).ModelId);
        var eml = Path.Combine(_folder, MailboxWriter.CopyPathsFor(ctx.Thread.Messages[0], Folders)[0]);
        var message = MimeKit.MimeMessage.Load(eml);
        Assert.Contains("You are receiving this because you subscribed to", message.HtmlBody);
    }

    [Fact]
    public void Profile_For_RelevantUsesStoryAndLedger()
    {
        var profile = RenderProfile.For(AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId), _gateway, null);

        Assert.Equal(RenderProfile.RelevantName, profile.Name);
        Assert.Equal(LlmTier.Story, profile.Tier);
        Assert.True(profile.UsesLedger);
        Assert.Same(_story, profile.Model);
    }

    // ---------- attachment step and commit boundary (D-14, D-16, D-20, D-26, review concerns 1, 4, 6, 7) ----------

    private static readonly TimeSpan ChainGuard = TimeSpan.FromSeconds(10);

    [System.Text.RegularExpressions.GeneratedRegex(@"Attachment id: (\S+)")]
    private static partial System.Text.RegularExpressions.Regex AttachmentIdLine();

    private static string? AttachmentIdOf(LlmProviderRequest request)
    {
        var match = AttachmentIdLine().Match(request.UserPrompt ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    private AttachmentUnitOfWork AttachmentUnit(IMediaGenerator? media) =>
        new(_gateway, _snapshot, media, new MediaRunGate(), _story, _volume);

    private ThreadUnitOfWork FullUnit(IMediaGenerator? media = null, string? folder = null)
    {
        var target = folder ?? _folder;
        var journal = target == _folder ? _journal : new GenerationJournal(target);
        return new ThreadUnitOfWork(_gateway, journal, new MailboxWriter(target, Folders), _snapshot, _story, AttachmentUnit(media));
    }

    private ChainScheduler Chains(IReadOnlyDictionary<string, AttachmentChainLink>? links = null,
        IReadOnlyDictionary<string, string>? committed = null) =>
        new(links ?? AttachmentChains.Compute(_plan, _draft.Guide), committed ?? new Dictionary<string, string>(),
            ChainScheduler.ThreadOfAttachment(_plan), ChainScheduler.RankThreads(_plan));

    private static ThreadAttachmentContext Step(ChainScheduler chains, params string[] threadIds)
    {
        chains.BeginStep(threadIds);
        return new ThreadAttachmentContext { Chains = chains, RowIndex = 1 };
    }

    private Task<ThreadOutcome> RunThread(ThreadUnitOfWork unit, string threadId, ThreadAttachmentContext attachments, PlanCallRun run,
        ThreadRenderContext? ctx = null)
    {
        var render = ctx ?? ThreadCtx(threadId);
        return unit.RunAsync(render, RenderProfile.For(render.Thread, _gateway, null), run, attachments);
    }

    /// <summary>
    /// The attachment-aware clean responder; <paramref name="intercept"/> may answer a request first (null = default).
    /// </summary>
    private Func<LlmProviderRequest, LlmProviderResponse> Responder(Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null,
        Func<string, int, string?>? overrideJson = null)
    {
        var inner = AttachmentResponder.Create(_plan, _draft.Guide!, overrideJson);
        return request => intercept?.Invoke(request) ?? inner(request);
    }

    private static Func<LlmProviderRequest, LlmProviderResponse?> RefuseAttachment(string docId) =>
        request => AttachmentIdOf(request) == docId ? FakeLlmProvider.Refused("cannot write that document") : null;

    private string PromptFor(string docId) => _fake.Requests.Last(r => AttachmentIdOf(r) == docId).UserPrompt!;

    private static List<MimeKit.MimeEntity> DocParts(MimeKit.MimeMessage message) =>
        message.BodyParts.Where(p => p.Headers.Contains(MailboxWriter.DocIdHeader)).ToList();

    private static PlannedAttachment Spec(PlannedThread thread, int seq, AttachmentType kind) =>
        thread.Messages.Single(m => m.Seq == seq).Attachments.First(a => a.Kind == kind);

    private static Dictionary<string, AttachmentChainLink> ManualChain(params (string Chain, string[] DocIds)[] chains)
    {
        var links = new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
        foreach (var (chain, docIds) in chains)
        {
            for (var i = 0; i < docIds.Length; i++)
            {
                links[docIds[i]] = new AttachmentChainLink
                {
                    ChainId = chain,
                    Version = i + 1,
                    Count = docIds.Length,
                    PreviousDocId = i > 0 ? docIds[i - 1] : string.Empty,
                    OrganizationKey = "test",
                    BaseTitle = chain
                };
            }
        }

        return links;
    }

    [Fact]
    public async Task Attachments_WordExcelPowerPoint_OneThreadDoneLine_ByteIdenticalCopies()
    {
        _fake.Respond = Responder();
        var ctx = ThreadCtx(AttachmentTestData.RelevantDocThreadId);
        var chains = Chains();
        var attachments = Step(chains, ctx.Thread.Id);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await FullUnit().RunAsync(ctx, RenderProfile.For(ctx.Thread, _gateway, null), run, attachments);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        var root = ctx.Thread.Messages.Single(m => m.Seq == 1);
        var names = AttachmentNaming.NamesFor(root, null, chains.Links);
        var copies = MailboxWriter.CopyPathsFor(root, Folders);
        Assert.True(copies.Count > 1);
        var bytes = copies.Select(c => File.ReadAllBytes(Path.Combine(_folder, c))).ToList();
        Assert.All(bytes, b => Assert.Equal(bytes[0], b));

        var eml = MimeKit.MimeMessage.Load(Path.Combine(_folder, copies[0]));
        var parts = DocParts(eml);
        Assert.Equal(root.Attachments.Select(a => a.DocId), parts.Select(p => p.Headers[MailboxWriter.DocIdHeader]));
        Assert.Equal(root.Attachments.Select(a => names[a.DocId]), parts.Select(p => ((MimeKit.MimePart)p).FileName));

        // Exactly one journal line: the ThreadDone with every attachment nested in plan order
        var record = SingleRecord(ctx.Thread);
        Assert.Equal(JournalRecordType.ThreadDone, record.Type);
        Assert.Equal(root.Attachments.Select(a => a.DocId), record.Attachments.Select(a => a.AttachmentDocId));
        Assert.All(record.Attachments, a =>
        {
            Assert.Equal(JournalRecordType.AttachmentDone, a.Type);
            Assert.Equal(root.DocId, a.ParentDocId);
            Assert.Equal(record.Utc, a.Utc);
            Assert.Equal(copies.OrderBy(c => c), a.Files.OrderBy(c => c));
        });
        Assert.Equal(record.Attachments.Count, outcome.AttachmentRecords.Count);

        // Relevant attachment deltas come back for the ledger, marked with their source
        Assert.NotEmpty(outcome.AttachmentDeltas);
        Assert.All(outcome.AttachmentDeltas, d =>
        {
            Assert.True(d.FromAttachment);
            Assert.Contains(d.AttachmentDocId, root.Attachments.Select(a => a.DocId));
        });

        var view = _snapshot.Drain();
        Assert.Equal(3, view.Counts.AttachmentsDone);
        Assert.Equal(3, Assert.Single(view.Events, e => e.Kind == GenerationEventKind.ThreadWritten).Attachments);
        Assert.Equal(0m, outcome.NotRunCostUsd);
    }

    [Fact]
    public async Task Attachments_SnapshotAndEvents_OnlyAfterTheAppend()
    {
        var root = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId).Messages.Single(m => m.Seq == 1);
        var excel = root.Attachments.Single(a => a.Kind == AttachmentType.Excel).DocId;
        _fake.Respond = Responder(RefuseAttachment(excel));
        var ctx = ThreadCtx(AttachmentTestData.RelevantDocThreadId);
        var attachments = Step(Chains(), ctx.Thread.Id);
        var unit = FullUnit();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        unit.BeforeAppendHook = async _ =>
        {
            reached.TrySetResult();
            await release.Task;
        };
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var task = Task.Run(() => unit.RunAsync(ctx, RenderProfile.For(ctx.Thread, _gateway, null), run, attachments));
        await reached.Task.WaitAsync(ChainGuard);

        // Every copy is on disk, but nothing is reported before the line is flushed
        Assert.True(File.Exists(Path.Combine(_folder, MailboxWriter.CopyPathsFor(root, Folders)[0])));
        var pending = _snapshot.Peek();
        Assert.Equal(0, pending.Counts.AttachmentsDone);
        Assert.Equal(0, pending.Counts.AttachmentsFailed);
        Assert.DoesNotContain(pending.Events, e => e.Kind is GenerationEventKind.AttachmentFailed
            or GenerationEventKind.AttachmentKeptWithWarnings or GenerationEventKind.AttachmentSkipped or GenerationEventKind.ThreadWritten);
        Assert.Empty(JournalLines());

        release.SetResult();
        var outcome = await task.WaitAsync(ChainGuard);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        var view = _snapshot.Drain();
        Assert.Equal(2, view.Counts.AttachmentsDone);
        Assert.Equal(1, view.Counts.AttachmentsFailed);
        var failed = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.AttachmentFailed);
        Assert.Equal(AttachmentUnitOfWork.RefusedReason, failed.Reason);
        Assert.EndsWith(".xlsx", failed.FileName);
    }

    [Fact]
    public async Task Attachments_RefusedExcel_EmailWrittenWithoutIt_FailedNestedInThreadDone()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var excel = root.Attachments.Single(a => a.Kind == AttachmentType.Excel).DocId;

        // Reference run without the failure in its own folder
        var clean = Path.Combine(_folder, "clean");
        Directory.CreateDirectory(clean);
        _fake.Respond = Responder();
        using (var cleanCts = new CancellationTokenSource())
        using (var cleanRun = Run(cleanCts))
            await RunThread(FullUnit(folder: clean), thread.Id, Step(Chains(), thread.Id), cleanRun);
        var cleanRecord = GenerationJournal.Replay(clean, _plan).LastByThread[thread.Id];

        _fake.Respond = Responder(RefuseAttachment(excel));
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await RunThread(FullUnit(), thread.Id, Step(Chains(), thread.Id), run);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        var eml = MimeKit.MimeMessage.Load(Path.Combine(_folder, MailboxWriter.CopyPathsFor(root, Folders)[0]));
        Assert.Equal(root.Attachments.Where(a => a.DocId != excel).Select(a => a.DocId),
            DocParts(eml).Select(p => p.Headers[MailboxWriter.DocIdHeader]));

        var record = SingleRecord(thread);
        Assert.Equal(new[] { JournalRecordType.AttachmentDone, JournalRecordType.AttachmentFailed, JournalRecordType.AttachmentDone },
            record.Attachments.Select(a => a.Type));
        var failed = record.Attachments.Single(a => a.Type == JournalRecordType.AttachmentFailed);
        Assert.Equal(excel, failed.AttachmentDocId);
        Assert.Equal(AttachmentType.Excel, failed.AttachmentKind);
        Assert.Equal(root.DocId, failed.ParentDocId);
        Assert.Equal(ThreadFailureKind.Refused, failed.FailureKind);
        Assert.Empty(failed.Files);

        // D-26: the bodies are exactly what the run without the failure wrote
        Assert.Equal(cleanRecord.Messages.Select(m => m.BodyPlain), record.Messages.Select(m => m.BodyPlain));
    }

    [Fact]
    public async Task Attachments_InlineImage_MediaOk_HasCid_NoMedia_Skipped()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var image = root.Attachments.Single(a => a.Kind == AttachmentType.Image);
        _fake.Respond = Responder();
        var media = new FakeMediaGenerator();
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var withMedia = await RunThread(FullUnit(media), thread.Id, Step(Chains(), thread.Id), run);

        Assert.Equal(ThreadOutcomeKind.Written, withMedia.Kind);
        var eml = MimeKit.MimeMessage.Load(Path.Combine(_folder, MailboxWriter.CopyPathsFor(root, Folders)[0]));
        var sender = ThreadCtx(thread.Id).People.Find(root.FromId)!;
        var cid = AttachmentNaming.ContentIdFor(image.DocId, sender.Domain);
        Assert.Contains("cid:" + cid, eml.HtmlBody);
        Assert.Contains(DocParts(eml), p => p.Headers[MailboxWriter.DocIdHeader] == image.DocId);

        var bare = Path.Combine(_folder, "bare");
        Directory.CreateDirectory(bare);
        var withoutMedia = await RunThread(FullUnit(folder: bare), thread.Id, Step(Chains(), thread.Id), run);

        Assert.Equal(ThreadOutcomeKind.Written, withoutMedia.Kind);
        var skipped = withoutMedia.Record!.Attachments.Single(a => a.AttachmentDocId == image.DocId);
        Assert.Equal(JournalRecordType.AttachmentSkipped, skipped.Type);
        Assert.Equal(AttachmentUnitOfWork.NoMediaKeyReason, skipped.Reason);
        var bareEml = MimeKit.MimeMessage.Load(Path.Combine(bare, MailboxWriter.CopyPathsFor(root, Folders)[0]));
        Assert.DoesNotContain(DocParts(bareEml), p => p.Headers[MailboxWriter.DocIdHeader] == image.DocId);
        Assert.DoesNotContain("cid:", bareEml.HtmlBody);
    }

    [Fact]
    public async Task Chain_SameStep_LowerRankedPredecessor_PromptHasItsText_EveryRun()
    {
        var first = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var second = AttachmentTestData.Thread(_plan, AttachmentTestData.ChainThreadId);
        var v1 = Spec(first, 1, AttachmentType.Excel).DocId;
        var v2 = Spec(second, 1, AttachmentType.Excel).DocId;
        var probe = Chains();
        Assert.Equal(v1, probe.Links[v2].PreviousDocId);
        Assert.True(probe.Compare(first.Id, second.Id) < 0);
        _fake.OnSend = _ => Thread.Sleep(Random.Shared.Next(0, 4));

        for (var i = 0; i < 20; i++)
        {
            var folder = Path.Combine(_folder, $"run{i}");
            Directory.CreateDirectory(folder);
            _fake.Requests.Clear();
            _fake.Respond = Responder();
            var unit = FullUnit(folder: folder);
            var attachments = Step(Chains(), first.Id, second.Id);
            using var cts = new CancellationTokenSource();
            using var run = new PlanCallRun(cts.Token, 2, RetryHint);

            var launch = i % 2 == 0 ? new[] { second.Id, first.Id } : new[] { first.Id, second.Id };
            var tasks = launch.Select(id => Task.Run(() => RunThread(unit, id, attachments, run))).ToList();
            await Task.WhenAll(tasks).WaitAsync(ChainGuard);

            var v1Text = GenerationJournal.Replay(folder, _plan).LastByAttachment[v1].FlattenedText;
            Assert.NotEmpty(v1Text);
            Assert.Contains(v1Text.Trim(), PromptFor(v2));
        }

        // v1 failed: the successor is told the earlier draft is not available
        var failedFolder = Path.Combine(_folder, "failed");
        Directory.CreateDirectory(failedFolder);
        _fake.Requests.Clear();
        _fake.Respond = Responder(RefuseAttachment(v1));
        var failUnit = FullUnit(folder: failedFolder);
        var step = Step(Chains(), first.Id, second.Id);
        using (var cts = new CancellationTokenSource())
        using (var run = new PlanCallRun(cts.Token, 2, RetryHint))
            await Task.WhenAll(RunThread(failUnit, second.Id, step, run), RunThread(failUnit, first.Id, step, run)).WaitAsync(ChainGuard);
        Assert.Contains(AttachmentPrompts.EarlierDraftNotAvailable, PromptFor(v2));
    }

    private async Task<string> RunTwoThreadChain(string a, string b, Dictionary<string, AttachmentChainLink> links,
        IReadOnlyDictionary<string, string>? committed, bool aFirst, int iteration, IEnumerable<string> promptDocIds)
    {
        var folder = Path.Combine(_folder, $"{(aFirst ? "a" : "b")}{iteration}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        _fake.Requests.Clear();
        _fake.Respond = Responder();
        var unit = FullUnit(folder: folder);
        var attachments = Step(Chains(links, committed), a, b);

        // One provider slot shared by both threads: a chain wait made while holding it would deadlock
        using var cts = new CancellationTokenSource();
        using var run = new PlanCallRun(cts.Token, 1, RetryHint);
        var order = aFirst ? new[] { a, b } : new[] { b, a };
        var tasks = order.Select(id => Task.Run(() => RunThread(unit, id, attachments, run))).ToList();
        var outcomes = await Task.WhenAll(tasks).WaitAsync(ChainGuard);

        Assert.All(outcomes, o => Assert.Equal(ThreadOutcomeKind.Written, o.Kind));
        var replay = GenerationJournal.Replay(folder, _plan);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[a].Type);
        Assert.Equal(JournalRecordType.ThreadDone, replay.LastByThread[b].Type);
        return string.Join("\n----\n", promptDocIds.Select(PromptFor));
    }

    [Fact]
    public async Task Chain_InterleavedAcrossTwoThreads_NoDeadlock()
    {
        // A = T-0901 (rank lower): v1 Word and v3 Excel; B = T-0904: v2 Word
        var a = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var b = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var v1 = Spec(a, 1, AttachmentType.Word).DocId;
        var v2 = Spec(b, 1, AttachmentType.Word).DocId;
        var v3 = Spec(a, 1, AttachmentType.Excel).DocId;
        var links = ManualChain(("Interleaved Notes", new[] { v1, v2, v3 }));
        Assert.True(Chains(links).Compare(a.Id, b.Id) < 0);
        _fake.OnSend = _ => Thread.Sleep(Random.Shared.Next(0, 4));

        string? expected = null;
        for (var i = 0; i < 20; i++)
        {
            var prompts = await RunTwoThreadChain(a.Id, b.Id, links, null, aFirst: i % 2 == 0, i, new[] { v2, v3 });
            expected ??= prompts;
            Assert.Equal(expected, prompts);
        }

        var v1Text = GenerationJournal.Replay(Directory.GetDirectories(_folder)[0], _plan).LastByAttachment[v1].FlattenedText;
        var v2Prompt = expected!.Split("\n----\n")[0];
        var v3Prompt = expected.Split("\n----\n")[1];
        Assert.Contains(v1Text.Trim(), v2Prompt);
        Assert.Contains(AttachmentPrompts.EarlierDraftNotAvailable, v3Prompt);
    }

    [Fact]
    public async Task Chain_CrossingChainsTwoThreads_NoDeadlock()
    {
        // Chain X: x1 (A Word) -> x2 (B Word); chain Y: y1 (B Excel) -> y2 (A Excel); rank(A) < rank(B)
        var a = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var b = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var x1 = Spec(a, 1, AttachmentType.Word).DocId;
        var x2 = Spec(b, 1, AttachmentType.Word).DocId;
        var y1 = Spec(b, 2, AttachmentType.Excel).DocId;
        var y2 = Spec(a, 1, AttachmentType.Excel).DocId;
        var links = ManualChain(("Chain X", new[] { x1, x2 }), ("Chain Y", new[] { y1, y2 }));
        _fake.OnSend = _ => Thread.Sleep(Random.Shared.Next(0, 4));

        foreach (var seeded in new[] { true, false })
        {
            var committed = seeded
                ? new Dictionary<string, string> { [y1] = "Seeded y1 sheet from an earlier run" }
                : null;
            string? expected = null;
            for (var i = 0; i < 20; i++)
            {
                var prompts = await RunTwoThreadChain(a.Id, b.Id, links, committed, aFirst: i % 2 == 0, i, new[] { x2, y2 });
                expected ??= prompts;
                Assert.Equal(expected, prompts);
            }

            var x2Prompt = expected!.Split("\n----\n")[0];
            var y2Prompt = expected.Split("\n----\n")[1];
            Assert.Contains("The earlier version (revise it;", x2Prompt);
            Assert.DoesNotContain(AttachmentPrompts.EarlierDraftNotAvailable, x2Prompt);
            if (seeded)
                Assert.Contains("Seeded y1 sheet from an earlier run", y2Prompt);
            else
                Assert.Contains(AttachmentPrompts.EarlierDraftNotAvailable, y2Prompt);
        }
    }

    [Fact]
    public async Task Chain_InOneThread_UsesOwnEarlierText_NoSelfWait()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var v1 = Spec(thread, 1, AttachmentType.Word).DocId;
        var v2 = Spec(thread, 2, AttachmentType.Excel).DocId;
        _fake.Respond = Responder();
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await RunThread(FullUnit(), thread.Id, Step(Chains(ManualChain(("Visit Notes", new[] { v1, v2 }))), thread.Id), run)
            .WaitAsync(ChainGuard);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        var record = SingleRecord(thread);
        Assert.Equal(new[] { v1, v2 }, record.Attachments.Select(a => a.AttachmentDocId));
        Assert.Contains(record.Attachments[0].FlattenedText.Trim(), PromptFor(v2));
    }

    // A Meeting delta on the invite's root: the site walk five days after the email
    private string RenderWithMeeting(string threadId, int attempt)
    {
        var inner = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        var thread = AttachmentTestData.Thread(_plan, threadId);
        var request = new LlmProviderRequest { UserPrompt = $"Thread id: {threadId}" };
        var dto = JsonSerializer.Deserialize<ThreadRenderer.ThreadRenderDto>(inner(request).Text!, DtoOptions)!;
        var meetingDay = DateOnly.FromDateTime(AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId).Messages[0].SentAt.DateTime)
            .AddDays(5);
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
        return JsonSerializer.Serialize(dto, DtoOptions);
    }

    private Func<LlmProviderRequest, LlmProviderResponse> MeetingResponder(params string[] threadIds)
    {
        var inner = Responder();
        var renderSchema = JsonSchemaRegistry.GetSchemaName(typeof(ThreadRenderer.ThreadRenderDto));
        return request =>
        {
            var id = threadIds.FirstOrDefault(t => (request.UserPrompt ?? string.Empty).Contains($"Thread id: {t}", StringComparison.Ordinal));
            return id != null && request.SchemaName == renderSchema ? FakeLlmProvider.Ok(RenderWithMeeting(id, 1), 1000, 400) : inner(request);
        };
    }

    [Fact]
    public async Task Invite_OwnDeltaThenLedger_ReusesRegisteredSlot()
    {
        var a = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var b = AttachmentTestData.Thread(_plan, AttachmentTestData.SecondInviteThreadId);
        _fake.Respond = MeetingResponder(a.Id);
        var chains = Chains();
        var attachments = Step(chains, a.Id);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var first = await RunThread(FullUnit(new FakeMediaGenerator()), a.Id, attachments, run);

        var inviteA = first.Record!.Attachments.Single(r => r.AttachmentKind == AttachmentType.CalendarInvite);
        Assert.Equal(JournalRecordType.AttachmentDone, inviteA.Type);
        Assert.NotEmpty(inviteA.InviteMeetingKey);
        var registered = Assert.Single(attachments.InviteSlots);
        Assert.Equal(inviteA.InviteMeetingKey, registered.Key);
        Assert.Equal(inviteA.InviteStart, registered.Value.Start);

        // B runs in a later wave and sees A's meeting through the ledger
        var waves = new Dictionary<string, int> { [a.Id] = 1, [b.Id] = 2 };
        var ledger = LedgerMerger.Build(_plan, _draft.Guide!, waves,
            new Dictionary<string, List<LedgerDelta>> { [a.Id] = first.Record.LedgerDelta.ToList() });
        Assert.Contains(ledger.Entries, e => e.Kind == LedgerKind.Meeting);
        var next = new ThreadAttachmentContext { Chains = chains, RowIndex = 2 };
        foreach (var (key, slot) in attachments.InviteSlots)
            next.InviteSlots.TryAdd(key, slot);
        chains.BeginStep(new[] { b.Id });
        _fake.Respond = Responder();

        var second = await RunThread(FullUnit(), b.Id, next, run, ThreadCtx(b.Id, 2, ledger.Entries));

        var inviteB = second.Record!.Attachments.Single(r => r.AttachmentKind == AttachmentType.CalendarInvite);
        Assert.Equal(inviteA.InviteMeetingKey, inviteB.InviteMeetingKey);
        Assert.Equal(inviteA.InviteStart, inviteB.InviteStart);
        Assert.Equal(inviteA.InviteEnd, inviteB.InviteEnd);
    }

    [Fact]
    public async Task Invite_MeetingAbsorbedIntoEarlierThreadEntry_SameSchedule()
    {
        // 06-08 edge: an earlier-ranked thread C of the same wave states the same meeting, so LedgerMerger.Build keeps
        // C's entry and absorbs A's; B (a later wave) then sees C's entry, not the key A registered under
        var a = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var b = AttachmentTestData.Thread(_plan, AttachmentTestData.SecondInviteThreadId);
        var c = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        _fake.Respond = MeetingResponder(a.Id);
        var chains = Chains();
        var attachments = Step(chains, a.Id);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var first = await RunThread(FullUnit(new FakeMediaGenerator()), a.Id, attachments, run);
        var inviteA = first.Record!.Attachments.Single(r => r.AttachmentKind == AttachmentType.CalendarInvite);

        var cDelta = first.Record.LedgerDelta.Select(d => new LedgerDelta
        {
            Seq = 1,
            Kind = d.Kind,
            About = d.About,
            Statement = d.Statement,
            Value = d.Value,
            FactIds = d.FactIds.ToList()
        }).ToList();
        var waves = new Dictionary<string, int> { [c.Id] = 1, [a.Id] = 1, [b.Id] = 2 };
        var ledger = LedgerMerger.Build(_plan, _draft.Guide!, waves,
            new Dictionary<string, List<LedgerDelta>> { [a.Id] = first.Record.LedgerDelta.ToList(), [c.Id] = cDelta });
        var meeting = Assert.Single(ledger.Entries, e => e.Kind == LedgerKind.Meeting);
        Assert.Equal(c.Id, meeting.SourceThreadId);

        var next = new ThreadAttachmentContext { Chains = chains, RowIndex = 2 };
        foreach (var (key, slot) in attachments.InviteSlots)
            next.InviteSlots.TryAdd(key, slot);
        chains.BeginStep(new[] { b.Id });
        _fake.Respond = Responder();

        var second = await RunThread(FullUnit(), b.Id, next, run, ThreadCtx(b.Id, 2, ledger.Entries));

        // The content seed gives the same schedule even when the ledger exposes the other thread's key
        var inviteB = second.Record!.Attachments.Single(r => r.AttachmentKind == AttachmentType.CalendarInvite);
        Assert.Equal(inviteA.InviteStart, inviteB.InviteStart);
        Assert.Equal(inviteA.InviteEnd, inviteB.InviteEnd);
        Assert.NotEmpty(inviteB.InviteMeetingKey);
        // Documented edge (06-08): the key B sees is C's canonical key, not the one A registered; the schedule still matches
        Assert.StartsWith($"mtg:{a.Id}:", inviteA.InviteMeetingKey);
        Assert.StartsWith($"mtg:{c.Id}:", inviteB.InviteMeetingKey);
        Assert.True(attachments.InviteSlots.ContainsKey(inviteA.InviteMeetingKey));
    }

    [Fact]
    public void Invite_NonRelevant_UsesPlanSlot_WithoutMeetingKey()
    {
        // The invite thread's shape run as filler: the slot comes from the plan only and has no meeting key
        var fillerThread = PlanFixtures.Clone(_plan).Threads.Single(t => t.Id == AttachmentTestData.InviteThreadId);
        fillerThread.Relevance = ThreadRelevance.NotRelevant;
        fillerThread.Category = NonRelevantCategory.DailyBusiness;
        var ctx = ThreadCtx(AttachmentTestData.InviteThreadId);
        ctx.Thread = fillerThread;
        var parent = fillerThread.Messages.Single(m => m.Seq == 1);
        var invite = parent.Attachments.Single(a => a.Kind == AttachmentType.CalendarInvite);
        var accepted = new RenderedThread
        {
            ThreadId = fillerThread.Id,
            Messages = fillerThread.Messages.Select(m => new RenderedMessage { Seq = m.Seq, Body = "Agenda attached." }).ToList()
        };

        var actx = ThreadUnitOfWork.CreateAttachmentContext(ctx, RenderProfile.For(fillerThread, _gateway, null), parent, invite,
            accepted, new List<LedgerDelta>(), Step(Chains(), fillerThread.Id), null, null);

        var expected = InviteScheduler.ForNonRelevant(parent, invite);
        Assert.NotNull(actx.Slot);
        Assert.Equal(expected.Start, actx.Slot!.Start);
        Assert.Equal(expected.End, actx.Slot.End);
        Assert.Empty(actx.Slot.MeetingKey);
        Assert.Equal(LlmTier.Volume, actx.Tier);
        Assert.False(actx.Relevant);
    }

    [Fact]
    public void CreateAttachmentContext_AcceptedThread_AndAncestorDeltasOnly()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.SiblingBranchThreadId);
        var ctx = ThreadCtx(thread.Id);
        var parent = thread.Messages.Single(m => m.Seq == 2);
        var spec = parent.Attachments.Single();
        var accepted = new RenderedThread
        {
            ThreadId = thread.Id,
            Messages = thread.Messages.Select(m => new RenderedMessage { Seq = m.Seq, Body = $"Body of message {m.Seq}." }).ToList()
        };
        var deltas = new List<LedgerDelta>
        {
            new() { Seq = 1, Kind = LedgerKind.Figure, About = "root detail", Value = "$1,000" },
            new() { Seq = 2, Kind = LedgerKind.Figure, About = "parent detail", Value = "$2,000" },
            new() { Seq = 3, Kind = LedgerKind.Figure, About = "sibling detail", Value = "$3,000" }
        };
        var attachments = Step(Chains(), thread.Id);

        var actx = ThreadUnitOfWork.CreateAttachmentContext(ctx, RenderProfile.Relevant(_gateway), parent, spec, accepted, deltas,
            attachments, "earlier text", null);

        Assert.Same(accepted, actx.AcceptedThread);
        Assert.Equal("Body of message 2.", actx.ParentBody);
        Assert.Equal(new[] { "root detail", "parent detail" }, actx.VisibleThreadDeltas.Select(d => d.About));
        Assert.Equal("earlier text", actx.PriorVersionText);
        Assert.Equal(AttachmentNaming.NamesFor(parent, null, attachments.Chains.Links)[spec.DocId], actx.FileName);
        Assert.Equal(LlmTier.Story, actx.Tier);
        Assert.True(actx.Relevant);

        // D-13: filler attachments see no thread deltas
        var filler = ThreadUnitOfWork.CreateAttachmentContext(ctx, RenderProfile.For(
            AttachmentTestData.Thread(_plan, AttachmentTestData.DailyBusinessThreadId), _gateway, null), parent, spec, accepted, deltas,
            attachments, null, null);
        Assert.Empty(filler.VisibleThreadDeltas);
        Assert.Equal(string.Empty, filler.PriorVersionText);
    }

    [Fact]
    public async Task Attachments_NonRelevant_NoDeltas_FillerTitlesName()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.DailyBusinessThreadId);
        _fake.Respond = Responder();
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await RunThread(FullUnit(), thread.Id, Step(Chains(), thread.Id), run);

        Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
        Assert.Empty(outcome.AttachmentDeltas);
        var record = SingleRecord(thread);
        Assert.Empty(record.LedgerDelta);
        var nested = Assert.Single(record.Attachments);
        Assert.Equal(JournalRecordType.AttachmentDone, nested.Type);
        Assert.Empty(nested.LedgerDelta);
        Assert.Equal("Word notes 1", nested.Title);
        var root = thread.Messages.Single(m => m.Seq == 1);
        Assert.Equal(AttachmentNaming.NamesFor(root, new[] { "Word notes 1" }, Chains().Links)[root.Attachments[0].DocId], nested.FileName);
        Assert.StartsWith("Word_notes_1", nested.FileName);
        Assert.Equal(_volume.CalculateTotalCost(800, 300), nested.CostUsd);
        Assert.Equal(1, Assert.Single(_snapshot.Drain().Events, e => e.Kind == GenerationEventKind.ThreadWritten).Attachments);
    }

    // ---------- failure injection at the commit boundary ----------
    // Review concern 1: every write/journal boundary has a test; a successful paid part is either committed with its thread or never reported

    private static AttachmentRunStatus AttachmentStatus(GenerationRunState state, string docId) =>
        state.StatusByAttachment.TryGetValue(docId, out var status) ? status : AttachmentRunStatus.Pending;

    private static ThreadRunStatus ThreadStatus(GenerationRunState state, string threadId) =>
        state.StatusByThread.TryGetValue(threadId, out var status) ? status : ThreadRunStatus.Pending;

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, target));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, target), overwrite: true);
    }

    [Fact]
    public async Task Commit_CancelAfterFirstAttachment_KeepsThreadAndPart()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var (word, excel, slides) = (root.Attachments[0].DocId, root.Attachments[1].DocId, root.Attachments[2].DocId);
        _fake.Respond = Responder();
        using var cts = new CancellationTokenSource();

        // Call 0 renders the thread, call 1 the Word file; Cancel arrives while the Excel call is being answered
        _fake.OnSend = index =>
        {
            if (index == 2)
                cts.Cancel();
        };
        using var run = Run(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunThread(FullUnit(), thread.Id, Step(Chains(), thread.Id), run));

        var record = SingleRecord(thread);
        Assert.Equal(JournalRecordType.ThreadDone, record.Type);
        var nested = Assert.Single(record.Attachments);
        Assert.Equal(word, nested.AttachmentDocId);
        Assert.Equal(JournalRecordType.AttachmentDone, nested.Type);
        Assert.DoesNotContain(_fake.Requests, r => AttachmentIdOf(r) == slides);

        foreach (var copy in MailboxWriter.CopyPathsFor(root, Folders))
        {
            var eml = MimeKit.MimeMessage.Load(Path.Combine(_folder, copy));
            Assert.Equal(new[] { word }, DocParts(eml).Select(p => p.Headers[MailboxWriter.DocIdHeader]));
        }

        var state = GenerationRunState.From(_plan, GenerationJournal.Replay(_folder, _plan));
        Assert.Equal(ThreadRunStatus.Written, ThreadStatus(state, thread.Id));
        Assert.Equal(AttachmentRunStatus.Written, AttachmentStatus(state, word));
        Assert.Equal(AttachmentRunStatus.Pending, AttachmentStatus(state, excel));
        Assert.Equal(AttachmentRunStatus.Pending, AttachmentStatus(state, slides));

        var view = _snapshot.Drain();
        Assert.Equal(1, view.Counts.DoneThreads);
        Assert.Equal(1, view.Counts.AttachmentsDone);
    }

    [Fact]
    public async Task Commit_SecondCopyWriteFails_NothingReportedWritten()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var copies = MailboxWriter.CopyPathsFor(root, Folders);
        Assert.True(copies.Count > 1);
        var blocker = Path.Combine(_folder, copies[1]);
        Directory.CreateDirectory(blocker);
        _fake.Respond = Responder();
        var media = new FakeMediaGenerator();
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await RunThread(FullUnit(media), thread.Id, Step(Chains(), thread.Id), run);

        // Thread 1000/400 and the invite 800/300 on Story, plus one image
        var text = _story.CalculateTotalCost(1000, 400) + _story.CalculateTotalCost(800, 300);
        Assert.Equal(ThreadOutcomeKind.Failed, outcome.Kind);
        Assert.False(outcome.Unjournaled);
        var record = SingleRecord(thread);
        Assert.Equal(JournalRecordType.ThreadFailed, record.Type);
        Assert.Equal(ThreadFailureKind.Write, record.FailureKind);
        Assert.Equal(text, record.CostUsd);
        Assert.Equal(0.006m, record.MediaCostUsd);
        Assert.Equal(1800, record.InputTokens);
        Assert.Equal(700, record.OutputTokens);
        Assert.Empty(record.Attachments);
        Assert.Equal(text + 0.006m, outcome.CostUsd);
        Assert.Equal(1800, outcome.InputTokens);
        Assert.Equal(700, outcome.OutputTokens);
        Assert.Empty(outcome.AttachmentDeltas);
        Assert.Empty(outcome.AttachmentRecords);
        Assert.Equal(0m, outcome.NotRunCostUsd);
        Assert.Equal(0, outcome.NotRunInputTokens);
        Assert.Equal(0, outcome.NotRunOutputTokens);

        var view = _snapshot.Drain();
        Assert.Equal(0, view.Counts.AttachmentsDone);
        Assert.Equal(0, view.Counts.AttachmentsFailed);
        Assert.Equal(1, view.Counts.FailedThreads);
        Assert.Equal(text + 0.006m, view.Counts.CostUsd);
        Assert.Equal(0.006m, view.Counts.MediaCostUsd);
        Assert.DoesNotContain(view.Events, e => e.Kind is GenerationEventKind.AttachmentFailed or GenerationEventKind.AttachmentSkipped
            or GenerationEventKind.AttachmentKeptWithWarnings or GenerationEventKind.ThreadWritten);

        var replay = GenerationJournal.Replay(_folder, _plan);
        Assert.Equal(text + 0.006m, replay.SpentCostUsd);
        var state = GenerationRunState.From(_plan, replay);
        Assert.Equal(ThreadRunStatus.Failed, ThreadStatus(state, thread.Id));
        Assert.All(root.Attachments, a => Assert.Equal(AttachmentRunStatus.Pending, AttachmentStatus(state, a.DocId)));

        // A rerun on the same deterministic paths overwrites the partial copy and commits
        Directory.Delete(blocker);
        var rerun = await RunThread(FullUnit(media), thread.Id, Step(Chains(), thread.Id), run);

        Assert.Equal(ThreadOutcomeKind.Written, rerun.Kind);
        var bytes = copies.Select(c => File.ReadAllBytes(Path.Combine(_folder, c))).ToList();
        Assert.All(bytes, b => Assert.Equal(bytes[0], b));
        var after = GenerationRunState.From(_plan, GenerationJournal.Replay(_folder, _plan));
        Assert.Equal(ThreadRunStatus.Written, ThreadStatus(after, thread.Id));
        Assert.All(root.Attachments, a => Assert.Contains(AttachmentStatus(after, a.DocId),
            new[] { AttachmentRunStatus.Written, AttachmentRunStatus.Warnings }));
    }

    [Fact]
    public async Task Commit_ThreadDoneAppendFails_SpendReturnedUnjournaled()
    {
        var first = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var second = AttachmentTestData.Thread(_plan, AttachmentTestData.ChainThreadId);
        var v2 = Spec(second, 1, AttachmentType.Excel).DocId;
        _fake.Respond = Responder();

        // A directory where the journal file should be: every append throws
        Directory.CreateDirectory(GenerationJournal.PathFor(_folder));
        var chains = Chains();
        var attachments = Step(chains, first.Id, second.Id);
        var successor = chains.PriorTextAsync(v2, CancellationToken.None);
        Assert.False(successor.IsCompleted);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await RunThread(FullUnit(), first.Id, attachments, run);

        var spend = _story.CalculateTotalCost(1000, 400) + 3 * _story.CalculateTotalCost(800, 300);
        Assert.Equal(ThreadOutcomeKind.Failed, outcome.Kind);
        Assert.True(outcome.Unjournaled);
        Assert.StartsWith("Could not save the run journal", outcome.Reason);
        Assert.Equal(spend, outcome.CostUsd);
        Assert.Equal(1000 + 3 * 800, outcome.InputTokens);
        Assert.Equal(400 + 3 * 300, outcome.OutputTokens);
        Assert.Empty(outcome.AttachmentDeltas);
        Assert.Equal(0m, outcome.NotRunCostUsd);
        Assert.Equal(0, outcome.NotRunInputTokens);
        Assert.Equal(0, outcome.NotRunOutputTokens);

        // No successor hangs, and none sees the unjournaled text
        Assert.Null(await successor.WaitAsync(ChainGuard));

        var view = _snapshot.Drain();
        Assert.Equal(0, view.Counts.DoneThreads);
        Assert.Equal(0, view.Counts.FailedThreads);
        Assert.Equal(0, view.Counts.AttachmentsDone);
        Assert.Equal(spend, view.Counts.CostUsd);
        Assert.DoesNotContain(view.Events, e => e.Kind is GenerationEventKind.ThreadWritten or GenerationEventKind.ThreadFailed);
    }

    [Fact]
    public async Task Commit_KillBetweenWritesAndAppend_ReplayIsConsistent()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        _fake.Respond = Responder();
        var killed = _folder + "-killed";
        var unit = FullUnit();

        // The kill: a snapshot of the case folder after every EML copy and before the ThreadDone line
        unit.BeforeAppendHook = _ =>
        {
            CopyFolder(_folder, killed);
            return Task.CompletedTask;
        };
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);
        try
        {
            await RunThread(unit, thread.Id, Step(Chains(), thread.Id), run);

            Assert.All(MailboxWriter.CopyPathsFor(root, Folders), c => Assert.True(File.Exists(Path.Combine(killed, c)), c));
            var state = GenerationRunState.From(_plan, GenerationJournal.Replay(killed, _plan));
            Assert.Equal(ThreadRunStatus.Pending, ThreadStatus(state, thread.Id));
            Assert.All(root.Attachments, a => Assert.Equal(AttachmentRunStatus.Pending, AttachmentStatus(state, a.DocId)));

            // Continue on the killed folder rewrites the same paths and commits
            var resumed = await RunThread(FullUnit(folder: killed), thread.Id, Step(Chains(), thread.Id), run);

            Assert.Equal(ThreadOutcomeKind.Written, resumed.Kind);
            var after = GenerationRunState.From(_plan, GenerationJournal.Replay(killed, _plan));
            Assert.Equal(ThreadRunStatus.Written, ThreadStatus(after, thread.Id));
            Assert.All(root.Attachments, a => Assert.Equal(AttachmentRunStatus.Written, AttachmentStatus(after, a.DocId)));
            Assert.Single(File.ReadAllLines(GenerationJournal.PathFor(killed)), l => l.Length > 0);
        }
        finally
        {
            EvidenceTestData.DeleteFolder(killed);
        }
    }

    [Fact]
    public async Task Commit_FatalDuringAttachments_CommitsFinishedParts()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var (word, excel, slides) = (root.Attachments[0].DocId, root.Attachments[1].DocId, root.Attachments[2].DocId);

        // (a) Auth failure on the second attachment call: no more calls, the first part is committed
        _fake.Respond = Responder(r => AttachmentIdOf(r) == excel ? FakeLlmProvider.Failed(LlmFailureKind.Auth, "invalid key") : null);
        using (var cts = new CancellationTokenSource())
        using (var run = Run(cts))
        {
            var outcome = await RunThread(FullUnit(), thread.Id, Step(Chains(), thread.Id), run);

            Assert.True(run.IsFatal);
            Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
            Assert.Equal(new[] { word }, outcome.Record!.Attachments.Select(a => a.AttachmentDocId));
            Assert.DoesNotContain(_fake.Requests, r => AttachmentIdOf(r) == slides);
            var eml = MimeKit.MimeMessage.Load(Path.Combine(_folder, MailboxWriter.CopyPathsFor(root, Folders)[0]));
            Assert.Equal(new[] { word }, DocParts(eml).Select(p => p.Headers[MailboxWriter.DocIdHeader]));
            var state = GenerationRunState.From(_plan, GenerationJournal.Replay(_folder, _plan));
            Assert.Equal(AttachmentRunStatus.Pending, AttachmentStatus(state, excel));
            Assert.Equal(AttachmentRunStatus.Pending, AttachmentStatus(state, slides));
        }

        // (b) The second call is paid, then the run turns fatal: its spend comes back as NotRun spend
        var paid = Path.Combine(_folder, "paid");
        Directory.CreateDirectory(paid);
        _snapshot.Drain();
        var before = _snapshot.Peek().Counts.CostUsd;
        using (var cts = new CancellationTokenSource())
        using (var run = Run(cts))
        {
            var inner = Responder();
            _fake.Respond = request =>
            {
                var response = inner(request);
                if (AttachmentIdOf(request) == excel)
                    run.SetFatal("The API key was rejected.");
                return response;
            };

            var outcome = await RunThread(FullUnit(folder: paid), thread.Id, Step(Chains(), thread.Id), run);

            var excelCost = _story.CalculateTotalCost(800, 300);
            Assert.Equal(ThreadOutcomeKind.Written, outcome.Kind);
            Assert.Equal(new[] { word }, outcome.Record!.Attachments.Select(a => a.AttachmentDocId));
            Assert.Equal(excelCost, outcome.NotRunCostUsd);
            Assert.Equal(800, outcome.NotRunInputTokens);
            Assert.Equal(300, outcome.NotRunOutputTokens);

            var committed = _story.CalculateTotalCost(1000, 400) + _story.CalculateTotalCost(800, 300);
            Assert.Equal(before + committed + excelCost, _snapshot.Peek().Counts.CostUsd);
            Assert.Equal(committed, GenerationJournal.Replay(paid, _plan).SpentCostUsd);
        }
    }
}
