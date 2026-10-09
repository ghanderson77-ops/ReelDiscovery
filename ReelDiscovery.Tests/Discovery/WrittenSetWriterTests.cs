using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Discovery;

public partial class WrittenSetWriterTests
{
    private const string CaseNo = "2:24-cv-01234";
    private const string RfpText = "All Documents and Communications Concerning the revised project schedule.";
    private const string RogText = "Identify every meeting at which the revised project schedule was discussed.";
    private const string RogSubparts = "Identify (a) every meeting and (b) every attendee at which the schedule was discussed.";

    [GeneratedRegex(@"^- Slot (\d+) \|", RegexOptions.Multiline)]
    private static partial Regex SlotLineRegex();

    private static readonly Regex SlotLine = SlotLineRegex();

    // ---- helpers ----

    private sealed class Fixture
    {
        public CaseDraft Draft { get; init; } = new();
        public WrittenSetPlan Plan { get; init; } = new();
        public LlmGateway Gateway { get; init; } = null!;
        public FakeLlmProvider Fake { get; init; } = null!;
        public string SystemPrompt { get; init; } = string.Empty;

        // Attempt number (1-based) per batch, keyed by the batch's first slot
        public Dictionary<int, int> Attempts { get; } = new();

        public void Script(Func<List<RequestSlot>, int, LlmProviderResponse> respond)
        {
            Fake.Respond = request =>
            {
                var numbers = SlotLine.Matches(request.UserPrompt).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
                var slots = Plan.Slots.Where(s => numbers.Contains(s.Number)).ToList();
                var key = numbers.First();
                Attempts[key] = Attempts.TryGetValue(key, out var n) ? n + 1 : 1;
                return respond(slots, Attempts[key]);
            };
        }
    }

    private static Fixture CreateFixture(DiscoveryKind kind, int? slotCount = null, bool priced = false)
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var settings = new DiscoverySettings { RfpCount = 25, RogCount = 10, RfaCount = 5 };
        var plan = WrittenDiscoveryPlanner.Plan(draft, kind, settings, null);
        Assert.NotNull(plan);
        Assert.NotEmpty(plan!.Slots);

        if (slotCount is int count)
        {
            // Repeat the planner's slots under consecutive numbers to reach an exact size
            var source = plan.Slots.ToList();
            plan.Slots = Enumerable.Range(1, count).Select(i =>
            {
                var slot = source[(i - 1) % source.Count].Copy();
                slot.Number = i;
                return slot;
            }).ToList();
        }

        var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        var story = CaseTestData.StoryModel();
        if (priced)
        {
            story.InputTokenPricePerMillion = 1_000_000m;
            story.OutputTokenPricePerMillion = 1_000_000m;
        }

        return new Fixture
        {
            Draft = draft,
            Plan = plan,
            Gateway = new LlmGateway(fake, story, CaseTestData.VolumeModel()),
            Fake = fake,
            SystemPrompt = DiscoveryPrompts.BuildSystemPrompt(draft)
        };
    }

    // Model text for a slot that passes every check: RFAs state the fact's amounts and actor names
    private static string CleanText(Fixture f, RequestSlot slot)
    {
        switch (f.Plan.Kind)
        {
            case DiscoveryKind.Interrogatories:
                return RogText;
            case DiscoveryKind.RequestsForAdmission:
                var guide = f.Draft.Guide ?? new CaseGuide();
                var fact = guide.FindFact(slot.FactId);
                if (slot.IsGenuineness || fact == null)
                    return "the document described is a true and correct copy.";
                var actors = fact.ActorIds.Select(id => guide.FindCast(id)?.FullName).Where(n => !string.IsNullOrWhiteSpace(n));
                var amounts = guide.Values
                    .Where(v => v.Kind == ValueKind.Amount && v.FactIds.Contains(fact.Id))
                    .Select(v => v.Value.Trim());
                var parts = actors.Concat(amounts).ToList();
                return parts.Count == 0 ? "the event occurred as described." : "the event involved " + string.Join(" and ", parts) + ".";
            default:
                return RfpText;
        }
    }

    private static DiscoveryPrompts.RequestSetDto Dto(IEnumerable<(int Slot, string Text)> requests)
    {
        var dto = new DiscoveryPrompts.RequestSetDto();
        foreach (var (slot, text) in requests)
            dto.Requests.Add(new DiscoveryPrompts.RequestDto { Slot = slot, Text = text });
        return dto;
    }

    private static DiscoveryPrompts.RequestSetDto CleanDto(Fixture f, List<RequestSlot> slots) =>
        Dto(slots.Select(s => (s.Number, CleanText(f, s))));

    private static LlmProviderResponse Ok(DiscoveryPrompts.RequestSetDto dto) => FakeLlmProvider.Ok(JsonSerializer.Serialize(dto));

    private static Task<WrittenSetWriteOutcome> Write(Fixture f, PlanCallRun run, Action<int, int>? onPart = null) =>
        new WrittenSetWriter(f.Gateway).WriteAsync(f.Draft, f.Plan, CaseNo, f.SystemPrompt, string.Empty, run, onPart);

    private static PlanCallRun NewRun() => new(CancellationToken.None, 1, "Click Retry.");

    // ---- tests ----

    [Fact]
    public async Task Rfp25_CleanResponse_OneCallWithCompleteContent()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 25);
        f.Script((slots, _) => Ok(CleanDto(f, slots)));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Single(f.Fake.Requests);
        Assert.Equal("claude-story", f.Fake.Requests[0].ModelId);
        Assert.False(f.Fake.Requests[0].AllowBatch);
        Assert.Equal(f.SystemPrompt, f.Fake.Requests[0].SystemPrompt);

        var content = outcome.Content;
        Assert.NotNull(content);
        Assert.Empty(outcome.Warnings);
        Assert.Equal(Enumerable.Range(1, 25), content!.Requests.Select(r => r.Number));
        for (var i = 0; i < 25; i++)
            Assert.Equal(f.Plan.Slots[i].IssueIds, content.Requests[i].IssueIds);
        Assert.All(content.Requests, r => Assert.Equal(RfpText, r.Text));

        Assert.Equal(DiscoveryKind.RequestsForProduction, content.Kind);
        Assert.Equal(f.Plan.Title, content.Title);
        Assert.Equal(CaseNo, content.CaseNumber);
        Assert.Contains(CaseNo, content.CaptionText);
        Assert.NotEmpty(content.PreliminaryStatement);
        Assert.NotEmpty(content.Definitions);
        Assert.NotEmpty(content.Instructions);
        Assert.NotEmpty(content.SignatureText);
        Assert.NotEmpty(content.CertificateText);
        Assert.NotEmpty(content.ServiceDate);
        Assert.Equal("REQUEST FOR PRODUCTION NO.", content.RequestLabel);
        Assert.Equal(f.Plan.Propounding.Id, content.PropoundingPartyId);
        Assert.Equal(f.Plan.Responding.Id, content.RespondingPartyId);
    }

    [Fact]
    public async Task Rfp40_SplitsIntoTwoCallsSharingSystemPrompt()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 40);
        f.Script((slots, _) => Ok(CleanDto(f, slots)));
        var parts = new List<(int, int)>();
        using var run = NewRun();

        var outcome = await Write(f, run, (p, n) => parts.Add((p, n)));

        Assert.Equal(2, f.Fake.Requests.Count);
        Assert.Equal(25, SlotLine.Matches(f.Fake.Requests[0].UserPrompt).Count);
        Assert.Equal(15, SlotLine.Matches(f.Fake.Requests[1].UserPrompt).Count);
        Assert.Equal(f.Fake.Requests[0].SystemPrompt, f.Fake.Requests[1].SystemPrompt);
        Assert.Equal(new[] { (1, 2), (2, 2) }, parts);
        Assert.Equal(Enumerable.Range(1, 40), outcome.Content!.Requests.Select(r => r.Number));
    }

    [Fact]
    public async Task Rfa_DuplicatedPrefixRemoved_KeysCopiedFromSlot()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForAdmission);
        var slot = f.Plan.Slots.First(s => s.Prefix.Length > 0 && !s.IsGenuineness);
        f.Script((slots, _) => Ok(Dto(slots.Select(s =>
            (s.Number, s.Number == slot.Number ? s.Prefix + " " + s.Prefix + " " + CleanText(f, s) : CleanText(f, s))))));
        using var run = NewRun();

        var outcome = await Write(f, run);

        var request = outcome.Content!.Requests.Single(r => r.Number == f.Plan.Slots.IndexOf(slot) + 1);
        Assert.Equal(slot.Prefix + " " + CleanText(f, slot), request.Text);
        Assert.Equal(slot.Prefix, request.Prefix);
        Assert.Equal(slot.FactId, request.FactId);
        Assert.Equal(slot.DocId, request.DocId);
        Assert.Equal(slot.IsGenuineness, request.IsGenuineness);
        Assert.All(outcome.Content.Requests, r => Assert.StartsWith(r.Prefix, r.Text));
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public async Task LeadingRequestLabel_IsStripped()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 6);
        f.Script((slots, _) => Ok(Dto(slots.Select(s => (s.Number,
            s.Number == 4 ? "REQUEST FOR PRODUCTION NO. 4: " + RfpText
            : s.Number == 5 ? "Request No. 5: " + RfpText
            : RfpText)))));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.All(outcome.Content!.Requests, r => Assert.Equal(RfpText, r.Text));
    }

    [Fact]
    public async Task MissingAndExtraSlot_RetriedWithFeedback_ThenClean()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 8);
        f.Script((slots, attempt) =>
        {
            var requests = slots.Select(s => (s.Number, RfpText)).ToList();
            if (attempt == 1)
            {
                requests.RemoveAll(r => r.Number == 3);
                requests.Add((9, RfpText));
            }
            return Ok(Dto(requests));
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(2, f.Fake.Requests.Count);
        Assert.DoesNotContain(DiscoveryValidator.FeedbackHeader, f.Fake.Requests[0].UserPrompt);
        Assert.Contains(DiscoveryValidator.FeedbackHeader, f.Fake.Requests[1].UserPrompt);
        Assert.Empty(outcome.Warnings);
        Assert.Equal(Enumerable.Range(1, 8), outcome.Content!.Requests.Select(r => r.Number));
    }

    [Fact]
    public async Task MissingOnEveryAttempt_BestKept_Renumbered_WarningsKept()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 8);
        f.Script((slots, _) => Ok(Dto(slots.Where(s => s.Number != 3).Select(s => (s.Number, RfpText)))));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(WrittenSetWriter.MaxAttempts, f.Fake.Requests.Count);
        var content = outcome.Content!;
        Assert.Equal(Enumerable.Range(1, 7), content.Requests.Select(r => r.Number));

        // Request 3 now carries slot 4's keys
        Assert.Equal(f.Plan.Slots[3].IssueIds, content.Requests[2].IssueIds);
        Assert.Contains(outcome.Warnings, w => w.Rule == DiscoveryRule.D4MissingRequest && w.Number == 3);
    }

    [Fact]
    public async Task UnknownSlotNumbers_AreDropped()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 4);
        f.Script((slots, _) => Ok(Dto(slots.Select(s => (s.Number, RfpText)).Append((99, "All Documents Concerning anything.")))));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(4, outcome.Content!.Requests.Count);
        Assert.All(outcome.Content.Requests, r => Assert.Equal(RfpText, r.Text));
        Assert.Contains(outcome.Warnings, w => w.Rule == DiscoveryRule.D4ExtraRequest && w.Number == 99);
    }

    [Fact]
    public async Task Rog_SubpartsOnEveryAttempt_KeptWithD7Warnings()
    {
        var f = CreateFixture(DiscoveryKind.Interrogatories);
        f.Script((slots, _) => Ok(Dto(slots.Select(s => (s.Number, RogSubparts)))));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(WrittenSetWriter.MaxAttempts, f.Fake.Requests.Count);
        Assert.NotNull(outcome.Content);
        Assert.Equal(f.Plan.Slots.Count, outcome.Content!.Requests.Count);
        Assert.Equal("INTERROGATORY NO.", outcome.Content.RequestLabel);
        Assert.Equal(f.Plan.Slots.Count, outcome.Warnings.Count(w => w.Rule == DiscoveryRule.D7Subparts));
    }

    [Fact]
    public async Task FatalProviderError_ReturnsNotRunWithCost()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 40, priced: true);
        f.Script((_, _) => new LlmProviderResponse
        {
            Outcome = LlmOutcome.Failed, FailureKind = LlmFailureKind.Auth, Message = "Bad key", InputTokens = 7, OutputTokens = 0
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.True(outcome.NotRun);
        Assert.True(run.IsFatal);
        Assert.Null(outcome.Content);
        Assert.Equal(7m, outcome.Cost.TotalUsd);
        Assert.Single(f.Fake.Requests);
    }

    [Fact]
    public async Task RefusedOnEveryAttempt_FailsWithRefused()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 5, priced: true);
        f.Script((_, _) => FakeLlmProvider.Refused("Not comfortable"));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(WrittenSetWriter.MaxAttempts, f.Fake.Requests.Count);
        Assert.Null(outcome.Content);
        Assert.False(outcome.NotRun);
        Assert.True(outcome.Refused);
        Assert.NotEmpty(outcome.Error);
        Assert.Equal(36m, outcome.Cost.TotalUsd);
    }

    [Fact]
    public async Task UserCancellation_Propagates()
    {
        var f = CreateFixture(DiscoveryKind.RequestsForProduction, 5);
        f.Script((slots, _) => Ok(CleanDto(f, slots)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var run = new PlanCallRun(cts.Token, 1, "Click Retry.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(f, run));
        Assert.Empty(f.Fake.Requests);
    }
}
