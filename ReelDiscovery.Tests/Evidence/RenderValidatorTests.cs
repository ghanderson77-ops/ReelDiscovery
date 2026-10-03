using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class RenderValidatorTests
{
    // Cast in the fixture: CH-001 Maria Delgado, CH-002 Tom Becker, CH-003 Raymond Harmon, CH-004 Lisa Chen,
    // CH-005 Karen Walsh, CH-007 Evan Price, CH-008 Priya Nair; EXT-002 Dana Whitfield (vendor)
    private const string Zone = "America/Chicago";

    // ---------- fixture helpers ----------

    private static PlannedMessage Msg(int seq, int replyTo, string from, string[] to, DateTime local, params string[] cc) => new()
    {
        Id = $"M-{seq}",
        Seq = seq,
        ReplyToSeq = replyTo,
        Kind = replyTo == 0 ? MessageKind.New : MessageKind.Reply,
        FromId = from,
        ToIds = to.ToList(),
        CcIds = cc.ToList(),
        SentAt = new DateTimeOffset(local, TimeSpan.FromHours(-5)),
        TimeZoneId = Zone
    };

    // Default 4-message thread: Lisa -> Tom, Tom -> Lisa, Lisa -> Tom cc Raymond, Tom -> Maria
    private static List<PlannedMessage> DefaultMessages() => new()
    {
        Msg(1, 0, "CH-004", new[] { "CH-002" }, new DateTime(2024, 5, 10, 10, 0, 0)),
        Msg(2, 1, "CH-002", new[] { "CH-004" }, new DateTime(2024, 5, 10, 14, 0, 0)),
        Msg(3, 2, "CH-004", new[] { "CH-002" }, new DateTime(2024, 5, 13, 9, 0, 0), "CH-003"),
        Msg(4, 3, "CH-002", new[] { "CH-001" }, new DateTime(2024, 5, 14, 9, 0, 0))
    };

    private static (ThreadRenderContext Ctx, CaseDraft Draft) Context(List<PlannedMessage>? messages = null, bool privileged = false,
        LengthBand band = LengthBand.Medium, Action<CaseGuide>? editGuide = null)
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        editGuide?.Invoke(draft.Guide!);
        var thread = new PlannedThread
        {
            Id = "T-0900",
            Relevance = ThreadRelevance.Relevant,
            IsPrivileged = privileged,
            Subject = "Upper floor windows",
            Messages = messages ?? DefaultMessages()
        };
        var assignment = new ThreadAssignment
        {
            ThreadId = thread.Id,
            Messages = thread.Messages.Select(m => new MessageAssignment { Seq = m.Seq, Length = band }).ToList()
        };
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var ctx = ThreadRenderContext.Create(draft, plan, thread, assignment, people, new List<LedgerEntry>(), 2);
        return (ctx, draft);
    }

    private static RenderedThread Rendered(params string[] bodies) => new()
    {
        ThreadId = "T-0900",
        Messages = bodies.Select((b, i) => new RenderedMessage { Seq = i + 1, Body = b, SignOff = "Thanks," }).ToList()
    };

    // Fills the default thread with neutral bodies, replacing message seq with the given body
    private static RenderedThread With(int seq, string body)
    {
        var bodies = Enumerable.Range(1, 4)
            .Select(i => i == seq ? body : "Got it, I will check the schedule and come back to you later today with the details.")
            .ToArray();
        return Rendered(bodies);
    }

    private static string Words(int count) =>
        string.Join(" ", Enumerable.Range(0, count).Select(i => i % 2 == 0 ? "schedule" : "update"));

    private static List<RenderViolation> Check(RenderedThread rendered, ThreadRenderContext ctx) =>
        RenderValidator.Check(rendered, ctx);

    // ---------- V1 / V5 structure ----------

    [Fact]
    public void V1_MissingMessage_ReportsCount()
    {
        var (ctx, _) = Context();
        var rendered = With(0, "unused");
        rendered.Messages.RemoveAt(3);

        var violations = Check(rendered, ctx);

        var v = Assert.Single(violations, x => x.Code == "V1");
        Assert.Equal("has 3 messages; the plan has 4", v.Text);
        Assert.Equal(0, v.Seq);
    }

    [Fact]
    public void V1_EmptyBody_ReportsEmptyMessage()
    {
        var (ctx, _) = Context();

        var violations = Check(With(2, "   "), ctx);

        var v = Assert.Single(violations, x => x.Code == "V1");
        Assert.Equal("message 2 is empty", v.Text);
        Assert.Equal(2, v.Seq);
    }

    [Fact]
    public void V5_BodyEmptiedByQuoteRemoval_ReportsV5Only()
    {
        var (ctx, _) = Context();
        var rendered = With(2, string.Empty);
        rendered.RepairNotes.Add("msg 2: removed quoted text");

        var violations = Check(rendered, ctx);

        var v = Assert.Single(violations, x => x.Code == "V5");
        Assert.Equal("message 2 is empty after removing quoted text", v.Text);
        Assert.DoesNotContain(violations, x => x.Code == "V1");
    }

    [Fact]
    public void CleanThread_HasNoViolations()
    {
        var (ctx, _) = Context();

        Assert.Empty(Check(With(0, "unused"), ctx));
    }

    // ---------- V2 people ----------

    [Fact]
    public void V2_PeopleNamedOutsideCast_IsFlagged()
    {
        var (ctx, _) = Context();
        var rendered = With(0, "unused");
        rendered.Messages[0].PeopleNamed = new List<string> { "Tom Becker", "Gerald Fontaine" };

        var violations = Check(rendered, ctx);

        var v = Assert.Single(violations, x => x.Code == "V2");
        Assert.Equal("names \"Gerald Fontaine\", who is not in the cast", v.Text);
        Assert.Equal(1, v.Seq);
    }

    [Fact]
    public void V2_PeopleNamedKnownForms_AreAccepted()
    {
        var (ctx, _) = Context();
        var rendered = With(0, "unused");
        rendered.Messages[0].PeopleNamed = new List<string> { "Tom", "becker", "Lisa Chen", "Dana Whitfield", "Mr. Becker", "CH-003" };

        Assert.DoesNotContain(Check(rendered, ctx), x => x.Code == "V2");
    }

    [Fact]
    public void V2_CastFirstNameWithUnknownLastName_IsFlagged()
    {
        var (ctx, _) = Context();

        var violations = Check(With(1, "I spoke with Maria Ortiz about the upper floor panels this morning."), ctx);

        var v = Assert.Single(violations, x => x.Code == "V2");
        Assert.Equal("names \"Maria Ortiz\", who is not in the cast", v.Text);
    }

    [Fact]
    public void V2_HonorificWithUnknownLastName_IsFlagged()
    {
        var (ctx, _) = Context();

        var violations = Check(With(1, "Mr. Kowalski asked for the revised shop drawings by Friday."), ctx);

        var v = Assert.Single(violations, x => x.Code == "V2");
        Assert.Equal("names \"Mr. Kowalski\", who is not in the cast", v.Text);
    }

    [Fact]
    public void V2_GreetingCastMemberNotOnEmail_IsFlagged()
    {
        var (ctx, _) = Context();

        var violations = Check(With(1, "Hi Raymond,\nThe panels arrive next week."), ctx);

        var v = Assert.Single(violations, x => x.Code == "V2");
        Assert.Equal("greets \"Raymond\", who is not on this email", v.Text);
        Assert.Equal(1, v.Seq);
    }

    [Fact]
    public void V2_GreetingCcRecipient_IsAccepted()
    {
        var (ctx, _) = Context();

        // Message 3 copies Raymond
        Assert.DoesNotContain(Check(With(3, "Hi Raymond,\nLooping you in on the panels."), ctx), x => x.Code == "V2");
    }

    [Theory]
    [InlineData("Hi team,\nThe panels arrive next week.")]
    [InlineData("Hi all,\nQuick update on the schedule.")]
    [InlineData("Hello everyone,\nPlease see the revised sequence.")]
    [InlineData("Morning folks,\nCrew is on site at seven.")]
    [InlineData("Hi Tom,\nCan you confirm the crane time?")]
    [InlineData("Good morning Tom,\nCan you confirm the crane time?")]
    [InlineData("See you in March when the curtain wall is closed up.")]
    [InlineData("Lakeview Tower is still waiting on the north elevation glass.")]
    [InlineData("Let's talk Monday. Thursday works too.")]
    [InlineData("Maria wants the field report before the meeting.")]
    [InlineData("Dana Whitfield said the extrusions ship from Phoenix.")]
    [InlineData("Mr. Becker will walk the floor with the inspector.")]
    [InlineData("Lisa Chen approved the sequence. Tom Becker will confirm.")]
    [InlineData("Hi Unknownname,\nPlease send the delivery ticket.")]
    [InlineData("Thanks Tom\nThe crew starts Monday.")]
    public void V2_RealisticNegatives_AreNotFlagged(string body)
    {
        var (ctx, _) = Context();

        Assert.DoesNotContain(Check(With(1, body), ctx), x => x.Code == "V2");
    }

    // ---------- V7 legal wording ----------

    [Theory]
    [InlineData("The plaintiff wants the panels replaced.", "plaintiff")]
    [InlineData("Harmon is the defendant here, apparently.", "defendant")]
    [InlineData("They allegedly skipped the inspection.", "allegedly")]
    [InlineData("If this turns into a lawsuit we need the logs.", "lawsuit")]
    [InlineData("Is there a cause of action here?", "cause of action")]
    [InlineData("Count II covers the late payment.", "Count II")]
    [InlineData("Just to be clear, this is fictional.", "this is fictional")]
    [InlineData("Read the complaint for breach of contract and unjust enrichment first.", "Complaint for Breach of Contract and Unjust Enrichment")]
    public void V7_LegalWording_InNonPrivilegedThread_IsFlagged(string body, string term)
    {
        var (ctx, _) = Context();

        var violations = Check(With(1, body), ctx);

        var v = Assert.Single(violations, x => x.Code == "V7");
        Assert.Equal($"uses legal wording (\"{term}\")", v.Text);
        Assert.Equal(1, v.Seq);
    }

    [Fact]
    public void V7_LegalWording_InPrivilegedThread_IsNotFlagged()
    {
        var (ctx, _) = Context(privileged: true);

        Assert.DoesNotContain(Check(With(1, "The plaintiff will rely on Count II and the alleged delay."), ctx), x => x.Code == "V7");
    }

    [Theory]
    [InlineData("Leave the samples on the counter in the trailer.")]
    [InlineData("Our accountant will look at the pay application.")]
    [InlineData("A truck was illegally parked at the loading dock.")]
    [InlineData("I lost count of the change requests this month.")]
    [InlineData("The count is 42 panels on the north side.")]
    public void V7_SubstringsAndOrdinaryWords_AreNotFlagged(string body)
    {
        var (ctx, _) = Context();

        Assert.DoesNotContain(Check(With(1, body), ctx), x => x.Code == "V7");
    }

    // ---------- V8 length ----------

    [Fact]
    public void V8_ShortBandBodyFarTooLong_IsFlagged()
    {
        var (ctx, _) = Context(band: LengthBand.Short);

        var violations = Check(With(1, Words(130)), ctx);

        var v = Assert.Single(violations, x => x.Code == "V8");
        Assert.Equal("message 1 is 130 words; target is 8-60", v.Text);
    }

    [Fact]
    public void V8_LongBandBodyFarTooShort_IsFlagged()
    {
        var (ctx, _) = Context(band: LengthBand.Long);
        var rendered = Rendered(Words(200), Words(200), Words(70), Words(200));

        var v = Assert.Single(Check(rendered, ctx), x => x.Code == "V8");
        Assert.Equal("message 3 is 70 words; target is 160-320", v.Text);
    }

    [Fact]
    public void V8_MediumBandModeratelyLong_IsNotFlagged()
    {
        var (ctx, _) = Context(band: LengthBand.Medium);
        var rendered = Rendered(Words(150), Words(150), Words(150), Words(30));

        Assert.DoesNotContain(Check(rendered, ctx), x => x.Code == "V8");
    }

    // ---------- never throws ----------

    [Fact]
    public void Check_NullInputs_ReturnsEmpty()
    {
        var (ctx, _) = Context();

        Assert.Empty(RenderValidator.Check(null!, ctx));
        Assert.Empty(RenderValidator.Check(Rendered("x"), null!));
    }

    [Fact]
    public void Check_NullGuideListsAndPeople_StillRunsOtherRules()
    {
        var (ctx, _) = Context();
        ctx.Guide.Cast = null!;
        ctx.Guide.Values = null!;
        ctx.Guide.Facts = null!;
        ctx.People = null!;
        ctx.LedgerView = null!;
        var rendered = With(1, "The plaintiff wants an answer.");
        rendered.Messages.RemoveAt(3);

        var violations = Check(rendered, ctx);

        Assert.Contains(violations, x => x.Code == "V1");
        Assert.Contains(violations, x => x.Code == "V7");
    }

    [Fact]
    public void Run_RuleThatThrows_IsSkippedAndOthersKept()
    {
        var violations = new List<RenderViolation>();

        RenderValidator.Run(violations, () => new[] { new RenderViolation { Code = "V1", Text = "a" } });
        RenderValidator.Run(violations, () => throw new InvalidOperationException("boom"));
        RenderValidator.Run(violations, () => ThrowingRule());
        RenderValidator.Run(violations, () => new[] { new RenderViolation { Code = "V8", Text = "b" } });

        Assert.Equal(new[] { "V1", "V8" }, violations.Select(v => v.Code));
    }

    private static IEnumerable<RenderViolation> ThrowingRule()
    {
        yield return new RenderViolation { Code = "V3", Text = "partial" };
        throw new InvalidOperationException("late failure");
    }
}
