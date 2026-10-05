using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// AttachmentUnitOfWork (06-17): identified outcomes, Office and invite text calls with checks, retries and best-attempt
/// selection (D-14, D-17), code-built invites (D-20), and no completion reporting before the caller's durable writes
/// (review concern 1). The unit writes no files.
/// </summary>
public class AttachmentUnitOfWorkTests
{
    private const string RetryHint = "Click Continue Generating.";
    private const string FeedbackHeader = "Problems in the previous attempt";
    private const string Stranger = "Gerald Fontaine";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;
    private readonly GenerationSnapshot _snapshot;

    // Priced so story and volume spend can be told apart
    private readonly AIModelConfig _story = new() { ModelId = "claude-story", InputTokenPricePerMillion = 3m, OutputTokenPricePerMillion = 15m };
    private readonly AIModelConfig _volume = new() { ModelId = "claude-volume", InputTokenPricePerMillion = 1m, OutputTokenPricePerMillion = 5m };

    public AttachmentUnitOfWorkTests()
    {
        (_gateway, _fake) = CorpusTestData.CreateGateway();
        _snapshot = new GenerationSnapshot();
        _snapshot.Reset(new GenerationCounts { TotalThreads = 15, WaveCount = 1 },
            new[] { new WaveProgress { Index = 1, Threads = 15 } });
    }

    private AttachmentUnitOfWork Unit(IMediaGenerator? media = null, MediaRunGate? gate = null) =>
        new(_gateway, _snapshot, media, gate ?? new MediaRunGate(), _story, _volume);

    private static PlanCallRun Run(CancellationTokenSource cts) => new(cts.Token, 1, RetryHint);

    internal static AttachmentRenderContext Ctx(string threadId, AttachmentType kind, int parentSeq = 1)
    {
        var ctx = AttachmentRendererTests.Context(threadId, kind, parentSeq);
        ctx.AcceptedThread = new RenderedThread
        {
            ThreadId = ctx.Thread.Thread.Id,
            Messages = ctx.Thread.Thread.Messages
                .OrderBy(m => m.Seq)
                .Select(m => new RenderedMessage { Seq = m.Seq, Body = "Attached is the document we discussed.", SignOff = "Thanks," })
                .ToList()
        };
        ctx.IsDecoy = ctx.Thread.Thread.Category == NonRelevantCategory.Decoy;
        return ctx;
    }

    private void Respond(AttachmentRenderContext ctx, Func<string, int, string?>? overrideJson = null) =>
        _fake.Respond = AttachmentResponder.Create(ctx.Thread.Plan, ctx.Thread.Guide, overrideJson);

    private static string Json<T>(AttachmentType kind, bool relevant, Action<T> edit) where T : class
    {
        var dto = (T)AttachmentResponder.DefaultDto(kind, relevant);
        edit(dto);
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static string WordWithStranger() =>
        Json<AttachmentRenderer.WordAttachmentDto>(AttachmentType.Word, true, d => d.PeopleNamed = new List<string> { Stranger });

    private void AssertNothingReported()
    {
        var view = _snapshot.Peek();
        Assert.Equal(0, view.Counts.AttachmentsDone);
        Assert.Equal(0, view.Counts.AttachmentsFailed);
        Assert.Equal(0, view.Counts.AttachmentsSkipped);
        Assert.DoesNotContain(view.Events, e => e.Kind is GenerationEventKind.AttachmentFailed
            or GenerationEventKind.AttachmentKeptWithWarnings or GenerationEventKind.AttachmentSkipped);
    }

    private static string WordText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        return doc.MainDocumentPart!.Document.Body!.InnerText;
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // ---------- Office documents ----------

    [Fact]
    public async Task CleanWord_OnHotMessage_IsWritten_WithBrandedBytes_AndParentDeltas()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Written, outcome.Kind);
        Assert.Empty(outcome.Violations);
        Assert.Equal(1, outcome.Attempts);
        Assert.Single(_fake.Requests);
        Assert.Equal("claude-story", _fake.Requests[0].ModelId);

        var part = Assert.IsType<ComposedAttachment>(outcome.Part);
        Assert.Equal(ctx.FileName, part.FileName);
        Assert.Equal(ctx.Spec.DocId, part.DocId);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", part.MimeType);
        var sender = ctx.Thread.People.Find(ctx.Parent.FromId)!;
        Assert.False(string.IsNullOrWhiteSpace(sender.OrganizationName));
        Assert.Contains(sender.OrganizationName, WordText(part.Bytes));

        Assert.NotEmpty(outcome.NewDetails);
        Assert.All(outcome.NewDetails, d =>
        {
            Assert.True(d.FromAttachment);
            Assert.Equal(ctx.Parent.Seq, d.Seq);
            Assert.Equal(ctx.Spec.DocId, d.AttachmentDocId);
        });
        Assert.Equal(_story.CalculateTotalCost(800, 300), outcome.CostUsd);
        Assert.Equal(0m, outcome.MediaCostUsd);

        var record = outcome.ToRecord(ctx.Thread.Plan.PlanId)!;
        Assert.Equal(JournalRecordType.AttachmentDone, record.Type);
        Assert.Equal(ThreadWriteStatus.Written, record.Status);
        Assert.Equal(ctx.Thread.Plan.PlanId, record.PlanId);
        Assert.Equal(ctx.Thread.Thread.Id, record.ThreadId);
        Assert.Equal(ctx.Spec.DocId, record.AttachmentDocId);
        Assert.Equal(ctx.Parent.DocId, record.ParentDocId);
        Assert.Equal(AttachmentType.Word, record.AttachmentKind);
        Assert.Equal(ctx.FileName, record.FileName);
        Assert.False(string.IsNullOrWhiteSpace(record.FlattenedText));
        Assert.Equal(outcome.NewDetails.Count, record.LedgerDelta.Count);
        Assert.Empty(record.Files);
        AssertNothingReported();
    }

    [Fact]
    public async Task Identity_IsTheSame_ForWrittenFailedAndNotRun()
    {
        var written = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Excel);
        Respond(written);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);
        var ok = (await Unit().RunAsync(written, run)).ToRecord(written.Thread.Plan.PlanId)!;

        var refusedCtx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Excel);
        _fake.Respond = _ => FakeLlmProvider.Refused("cannot help with that");
        var refused = await Unit().RunAsync(refusedCtx, run);
        Assert.Equal(AttachmentOutcomeKind.Failed, refused.Kind);
        Assert.Null(refused.Part);
        var failed = refused.ToRecord(refusedCtx.Thread.Plan.PlanId)!;

        Assert.Equal(JournalRecordType.AttachmentFailed, failed.Type);
        Assert.Equal(ok.AttachmentDocId, failed.AttachmentDocId);
        Assert.Equal(ok.ParentDocId, failed.ParentDocId);
        Assert.Equal(ok.AttachmentKind, failed.AttachmentKind);
        Assert.Equal(ok.ThreadId, failed.ThreadId);
        Assert.Equal(ok.FileName, failed.FileName);
        Assert.Equal(ok.Title, failed.Title);
        Assert.False(string.IsNullOrWhiteSpace(failed.Title));

        // Fatal after one billed attempt: identity and spend kept, no journal record
        var fatalCtx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Excel);
        var inner = AttachmentResponder.Create(fatalCtx.Thread.Plan, fatalCtx.Thread.Guide,
            (_, _) => Json<AttachmentRenderer.ExcelAttachmentDto>(AttachmentType.Excel, true, d => d.PeopleNamed = new List<string> { Stranger }));
        var calls = 0;
        _fake.Respond = request => Interlocked.Increment(ref calls) == 1
            ? inner(request)
            : FakeLlmProvider.Failed(LlmFailureKind.Auth, "invalid key");
        using var fatalCts = new CancellationTokenSource();
        using var fatalRun = Run(fatalCts);

        var notRun = await Unit().RunAsync(fatalCtx, fatalRun);

        Assert.True(fatalRun.IsFatal);
        Assert.Equal(AttachmentOutcomeKind.NotRun, notRun.Kind);
        Assert.Null(notRun.ToRecord(fatalCtx.Thread.Plan.PlanId));
        Assert.Equal(ok.AttachmentDocId, notRun.DocId);
        Assert.Equal(ok.ParentDocId, notRun.ParentDocId);
        Assert.Equal(ok.FileName, notRun.FileName);
        Assert.True(notRun.InputTokens >= 800 && notRun.OutputTokens >= 300);
        Assert.Equal(_story.CalculateTotalCost(notRun.InputTokens, notRun.OutputTokens), notRun.CostUsd);
        AssertNothingReported();
    }

    [Fact]
    public async Task UnknownPerson_RetriesWithFeedback_ThenClean()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        Respond(ctx, (_, attempt) => attempt == 1 ? WordWithStranger() : null);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Written, outcome.Kind);
        Assert.Equal(2, outcome.Attempts);
        Assert.Equal(2, _fake.Requests.Count);
        Assert.DoesNotContain(FeedbackHeader, _fake.Requests[0].UserPrompt);
        Assert.Contains(FeedbackHeader, _fake.Requests[1].UserPrompt);
        Assert.EndsWith($"- names \"{Stranger}\", who is not in the cast", _fake.Requests[1].UserPrompt!.TrimEnd());

        var retry = Assert.Single(_snapshot.Peek().Events, e => e.Kind == GenerationEventKind.AttachmentRetry);
        Assert.Equal(ctx.FileName, retry.FileName);
        Assert.Equal(1, retry.Attempt);
        Assert.Contains(retry.Violations, v => v.Contains(Stranger));
        AssertNothingReported();
    }

    [Fact]
    public async Task NewDetailContradictingVisibleThreadDelta_IsV6Feedback()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        ctx.AcceptedThread!.NewDetails.Add(new LedgerDelta
        {
            Seq = ctx.Parent.Seq,
            Kind = LedgerKind.Figure,
            About = "line item subtotal",
            Statement = "The line items total $70,000.",
            Value = "$70,000"
        });
        ctx.VisibleThreadDeltas = ctx.AcceptedThread.NewDetails.ToList();
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.True(_fake.Requests.Count >= 2);
        Assert.Contains("gives line item subtotal as $78,000; earlier emails say $70,000", _fake.Requests[1].UserPrompt);
        Assert.Equal(AttachmentOutcomeKind.Warnings, outcome.Kind);
    }

    [Fact]
    public async Task AlwaysViolating_ThreeAttempts_KeptWithWarnings()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        Respond(ctx, (_, _) => WordWithStranger());
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentUnitOfWork.MaxAttempts, _fake.Requests.Count);
        Assert.Equal(AttachmentOutcomeKind.Warnings, outcome.Kind);
        Assert.NotNull(outcome.Part);
        Assert.Contains(outcome.Violations, v => v.Contains(Stranger));
        Assert.Equal(2, _snapshot.Peek().Events.Count(e => e.Kind == GenerationEventKind.AttachmentRetry));
        var record = outcome.ToRecord(ctx.Thread.Plan.PlanId)!;
        Assert.Equal(JournalRecordType.AttachmentDone, record.Type);
        Assert.Equal(ThreadWriteStatus.Warnings, record.Status);
        AssertNothingReported();
    }

    [Fact]
    public async Task Refusal_FailsWithoutReask()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        _fake.Respond = _ => FakeLlmProvider.Refused("cannot help with that");
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Failed, outcome.Kind);
        Assert.Equal("the model declined to write it", outcome.Reason);
        Assert.Equal(ThreadFailureKind.Refused, outcome.FailureKind);
        Assert.Single(_fake.Requests);
        AssertNothingReported();
    }

    [Fact]
    public async Task Timeout_FailsWithPlainReason()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        _fake.ThrowOnSend = new OperationCanceledException();
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Failed, outcome.Kind);
        Assert.Equal("the request timed out", outcome.Reason);
        Assert.Equal(ThreadFailureKind.Timeout, outcome.FailureKind);
        Assert.Single(_fake.Requests);
    }

    public static TheoryData<AttachmentType, string> UnusableAnswers() => new()
    {
        { AttachmentType.Word, "{\"title\":\"Memo\",\"sections\":[],\"table\":{\"headers\":[],\"rows\":[]},\"peopleNamed\":[],\"newDetails\":[]}" },
        { AttachmentType.Excel, "{\"title\":\"Sheet\",\"sheetName\":\"S\",\"headers\":[\"Item\"],\"rows\":[],\"notes\":\"\",\"peopleNamed\":[],\"newDetails\":[]}" },
        { AttachmentType.PowerPoint, "{\"title\":\"Deck\",\"slides\":[{\"title\":\"\",\"bullets\":[]}],\"peopleNamed\":[],\"newDetails\":[]}" }
    };

    [Theory]
    [MemberData(nameof(UnusableAnswers))]
    public async Task UnusableContent_IsRetried_ThenFailed(AttachmentType kind, string json)
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, kind);
        Respond(ctx, (_, _) => json);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentUnitOfWork.MaxAttempts, _fake.Requests.Count);
        Assert.Contains("the document was empty", _fake.Requests[1].UserPrompt);
        Assert.Equal(AttachmentOutcomeKind.Failed, outcome.Kind);
        Assert.Equal("the model returned no usable content", outcome.Reason);
        Assert.Null(outcome.Part);
        AssertNothingReported();
    }

    [Fact]
    public async Task FillerExcel_UsesVolumeTier_NoChecks_NoDetails()
    {
        var ctx = Ctx(AttachmentTestData.DailyBusinessThreadId, AttachmentType.Excel);
        ctx.FileName = "Supplies.xlsx";
        Assert.False(ctx.Relevant);
        Assert.Equal(LlmTier.Volume, ctx.Tier);

        // A stranger in filler is not checked: filler only needs usable content
        Respond(ctx, (_, _) => Json<AttachmentRenderer.FillerExcelDto>(AttachmentType.Excel, false,
            d => d.Rows.Add(new List<string> { $"Lunch with {Stranger}", "$40" })));
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Written, outcome.Kind);
        Assert.Single(_fake.Requests);
        Assert.Equal("claude-volume", _fake.Requests[0].ModelId);
        Assert.Empty(outcome.NewDetails);
        Assert.Equal(_volume.CalculateTotalCost(800, 300), outcome.CostUsd);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", outcome.Part!.MimeType);
        Assert.False(outcome.Relevant);
    }

    [Fact]
    public async Task DecoyWord_MentioningForbiddenTerm_KeptWithD1Warning()
    {
        var ctx = Ctx(AttachmentTestData.DecoyThreadId, AttachmentType.Word);
        Assert.True(ctx.IsDecoy);
        Respond(ctx, (_, _) => Json<AttachmentRenderer.FillerWordDto>(AttachmentType.Word, false,
            d => d.Sections[0].Paragraphs.Add($"See also {AttachmentTestData.DecoyFactTitle} in the log.")));
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentUnitOfWork.MaxAttempts, _fake.Requests.Count);
        Assert.Equal(AttachmentOutcomeKind.Warnings, outcome.Kind);
        Assert.Contains(outcome.Violations,
            v => v.Contains($"mentions \"{AttachmentTestData.DecoyFactTitle}\", which this email must not mention"));
    }

    [Fact]
    public async Task PowerPoint_SubtitleIsOrganizationAndMonth()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.PowerPoint);
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Written, outcome.Kind);
        var sender = ctx.Thread.People.Find(ctx.Parent.FromId)!;
        var expected = $"{sender.OrganizationName} | {MessageTime.Local(ctx.Parent).ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        using var stream = new MemoryStream(outcome.Part!.Bytes);
        using var deck = PresentationDocument.Open(stream, false);
        var text = string.Join("\n", deck.PresentationPart!.SlideParts.Select(s => s.Slide.InnerText));
        Assert.Contains(expected, text);
    }

    // ---------- invites (D-20) ----------

    [Fact]
    public async Task Invite_IsBuiltByCode_FromTheSlot()
    {
        var ctx = Ctx(AttachmentTestData.InviteThreadId, AttachmentType.CalendarInvite);
        var start = new DateTimeOffset(2024, 5, 13, 10, 0, 0, TimeSpan.FromHours(-5));
        ctx.Slot = new InviteSlot
        {
            Start = start,
            End = start.AddHours(1),
            Source = InviteSource.Ledger,
            MatchedId = "T-0906-L01",
            MeetingKey = "mtg:T-0906:site-walk"
        };
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Written, outcome.Kind);
        Assert.Equal("text/calendar", outcome.Part!.MimeType);
        var ics = Encoding.UTF8.GetString(outcome.Part.Bytes).Replace("\r\n ", string.Empty);
        Assert.Contains($"DTSTART:{Utc(start)}", ics);
        Assert.Contains($"DTEND:{Utc(start.AddHours(1))}", ics);
        Assert.Contains("SUMMARY:Project Meeting", ics);
        var sender = ctx.Thread.People.Find(ctx.Parent.FromId)!;
        Assert.Contains($"mailto:{sender.Email}", ics.Split("\r\n").Single(l => l.StartsWith("ORGANIZER", StringComparison.Ordinal)));
        var invited = ctx.Parent.ToIds.Concat(ctx.Parent.CcIds).Distinct().Count();
        Assert.Equal(invited, ics.Split("\r\n").Count(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal)));

        Assert.Equal("mtg:T-0906:site-walk", outcome.InviteMeetingKey);
        Assert.Equal(start, outcome.InviteStart);
        Assert.Equal(start.AddHours(1), outcome.InviteEnd);
        var record = outcome.ToRecord(ctx.Thread.Plan.PlanId)!;
        Assert.Equal("mtg:T-0906:site-walk", record.InviteMeetingKey);
        Assert.Equal(start, record.InviteStart);
    }

    [Fact]
    public async Task UntiedInvite_KeptWithWarning_NotRetried()
    {
        var ctx = Ctx(AttachmentTestData.InviteThreadId, AttachmentType.CalendarInvite);
        var start = ctx.Parent.SentAt.AddDays(2);
        ctx.Slot = new InviteSlot
        {
            Start = start,
            End = start.AddMinutes(30),
            Source = InviteSource.Fallback,
            Warning = true,
            WarningText = InviteScheduler.NotTiedWarning
        };
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);

        var outcome = await Unit().RunAsync(ctx, run);

        Assert.Single(_fake.Requests);
        Assert.Equal(AttachmentOutcomeKind.Warnings, outcome.Kind);
        Assert.Contains(outcome.Violations, v => v.Contains("invite not tied to a known meeting"));
        Assert.NotNull(outcome.Part);
        Assert.Empty(_snapshot.Peek().Events.Where(e => e.Kind == GenerationEventKind.AttachmentRetry));
    }

    // ---------- failures that never escape ----------

    [Fact]
    public async Task BuilderException_IsFailed_ForThisAttachmentOnly()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);
        var unit = Unit();
        unit.BeforeBuild = _ => throw new InvalidOperationException("boom");

        var outcome = await unit.RunAsync(ctx, run);

        Assert.Equal(AttachmentOutcomeKind.Failed, outcome.Kind);
        Assert.StartsWith("the document could not be built", outcome.Reason);
        Assert.Contains("boom", outcome.Reason);
        Assert.Null(outcome.Part);
        Assert.Equal(_story.CalculateTotalCost(800, 300), outcome.CostUsd);
        Assert.Equal(ctx.Spec.DocId, outcome.ToRecord(ctx.Thread.Plan.PlanId)!.AttachmentDocId);
    }

    [Fact]
    public async Task UserCancel_DuringCall_Propagates()
    {
        var ctx = Ctx(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        Respond(ctx);
        using var cts = new CancellationTokenSource();
        using var run = Run(cts);
        _fake.OnSend = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Unit().RunAsync(ctx, run));
        AssertNothingReported();
    }
}
