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

    // ---------- V3 canonical value variants ----------

    private static Action<CaseGuide> Values(params CanonicalValue[] values) => guide => guide.Values = values.ToList();

    private static CanonicalValue Value(string id, ValueKind kind, string label, string value, params string[] factIds) => new()
    {
        Id = id,
        Kind = kind,
        Label = label,
        Value = value,
        FactIds = factIds.ToList()
    };

    private static readonly CanonicalValue ChangeOrderAmount = Value("V-101", ValueKind.Amount, "Change Order 7", "$184,250");
    private static readonly CanonicalValue ChangeOrderNumber = Value("V-102", ValueKind.ChangeOrderNumber, "Change order number", "CO-007");

    [Fact]
    public void V3_AmountVariantWithinWindow_IsFlagged()
    {
        var (ctx, _) = Context(editGuide: Values(ChangeOrderAmount, ChangeOrderNumber));

        var violations = Check(With(1, "The revised total on the change order is $184,520 as of today."), ctx);

        var v = Assert.Single(violations);
        Assert.Equal("V3", v.Code);
        Assert.Equal("uses $184,520 for Change Order 7; the case value is $184,250", v.Text);
        Assert.Equal(1, v.Seq);
    }

    [Theory]
    [InlineData("The approved amount is $184,250.00 per the signed form.")]
    [InlineData("The approved amount is $184,250 per the signed form.")]
    [InlineData("Lunch for the crew came to $12,000 over the whole job, believe it or not.")]
    [InlineData("Please reference CO-007 on the invoice.")]
    [InlineData("Purchase order PO-4471 covers the sealant.")]
    [InlineData("Ticket #4471 is still open with the supplier.")]
    public void V3_EqualFormsUnrelatedAmountsAndOtherPrefixes_AreNotFlagged(string body)
    {
        var (ctx, _) = Context(editGuide: Values(ChangeOrderAmount, ChangeOrderNumber));

        Assert.DoesNotContain(Check(With(1, body), ctx), x => x.Code == "V3");
    }

    [Fact]
    public void V3_UnregisteredIdentifierWithCanonicalPrefix_IsFlagged()
    {
        var (ctx, _) = Context(editGuide: Values(ChangeOrderAmount, ChangeOrderNumber));

        var violations = Check(With(1, "Please reference CO-0071 on the invoice."), ctx);

        var v = Assert.Single(violations);
        Assert.Equal("V3", v.Code);
        Assert.Equal("uses CO-0071, which is not a case document number", v.Text);
    }

    // ---------- V4 knowledge ----------

    // F-007 (2024-06-01) is known to CH-001 and CH-005; F-008 (2024-07-04) only to CH-008
    private static readonly CanonicalValue LateFee = Value("V-201", ValueKind.Amount, "Late fee", "$9,850", "F-007");
    private static readonly CanonicalValue Interest = Value("V-202", ValueKind.Amount, "Interest accrued", "$4,615.50", "F-008");
    private static readonly CanonicalValue PublicFee = Value("V-203", ValueKind.Amount, "Permit fee", "$2,000");

    private static List<PlannedMessage> AugustMessages(string firstSender) => new()
    {
        Msg(1, 0, firstSender, new[] { firstSender == "CH-002" ? "CH-004" : "CH-002" }, new DateTime(2024, 8, 5, 10, 0, 0)),
        Msg(2, 1, "CH-002", new[] { firstSender }, new DateTime(2024, 8, 5, 15, 0, 0))
    };

    [Fact]
    public void V4_ValueDatedAfterMessage_IsFlagged()
    {
        var (ctx, _) = Context(editGuide: Values(LateFee));

        var violations = Check(With(1, "Harmon will owe $9,850 in late fees on this."), ctx);

        var v = Assert.Single(violations);
        Assert.Equal("V4", v.Code);
        Assert.Equal("mentions Late fee, dated after this email", v.Text);
        Assert.Equal(1, v.Seq);
    }

    [Fact]
    public void V4_ValueKnownOnlyToOthers_IsFlagged()
    {
        var (ctx, _) = Context(AugustMessages("CH-002"), editGuide: Values(Interest));
        var rendered = Rendered("Interest is up to $4,615.50 on the invoice.", "Noted, thanks for the update.");

        var v = Assert.Single(Check(rendered, ctx));
        Assert.Equal("V4", v.Code);
        Assert.Equal("uses $4,615.50, which Tom does not know yet", v.Text);
    }

    [Fact]
    public void V4_ValueSeenInQuotedAncestor_IsNotFlagged()
    {
        var (ctx, _) = Context(AugustMessages("CH-008"), editGuide: Values(Interest));
        var rendered = Rendered("Interest is up to $4,615.50 on the invoice.", "Understood, $4,615.50 it is. I will tell the team.");

        Assert.Empty(Check(rendered, ctx));
    }

    [Fact]
    public void V4_PublicValue_IsNeverFlagged()
    {
        var (ctx, _) = Context(editGuide: Values(PublicFee));

        Assert.Empty(Check(With(1, "The city charged $2,000 for the street closure permit."), ctx));
    }

    [Fact]
    public void V4_LedgerValueUnknownToSender_IsFlagged()
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry>
        {
            new() { Id = "T-0100-L01", Kind = LedgerKind.Meeting, About = "crane inspection", Value = "2024-04-02", KnownByIds = new List<string> { "CH-001" } }
        };

        var violations = Check(With(1, "Confirming 2024-04-02 for the crane people."), ctx);

        var v = Assert.Single(violations);
        Assert.Equal("V4", v.Code);
        Assert.Equal("uses 2024-04-02, which Lisa does not know yet", v.Text);
    }

    [Fact]
    public void V4_LedgerValueKnownToSender_IsNotFlagged()
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry>
        {
            new() { Id = "T-0100-L01", Kind = LedgerKind.Meeting, About = "crane inspection", Value = "2024-04-02", KnownByIds = new List<string> { "CH-004" } }
        };

        Assert.Empty(Check(With(1, "Confirming 2024-04-02 for the crane people."), ctx));
    }

    // ---------- V6 ledger contradictions ----------

    private static LedgerEntry Retainage() => new()
    {
        Id = "T-0100-L02",
        Kind = LedgerKind.Figure,
        About = "retainage on pay application 7",
        Value = "$18,425",
        KnownByIds = new List<string> { "CH-002", "CH-004" }
    };

    private static LedgerEntry SiteWalk(LedgerKind kind = LedgerKind.Meeting) => new()
    {
        Id = "T-0100-L03",
        Kind = kind,
        About = "site walk with the glazing crew",
        Value = "2024-04-02",
        KnownByIds = new List<string> { "CH-002", "CH-004" }
    };

    [Fact]
    public void V6_DeltaWithDifferentValueForSameDetail_IsFlagged()
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { Retainage() };
        var rendered = With(0, "unused");
        rendered.NewDetails.Add(new LedgerDelta { Seq = 2, Kind = LedgerKind.Figure, About = "Retainage on pay application 7", Value = "$18,524" });

        var v = Assert.Single(Check(rendered, ctx));
        Assert.Equal("V6", v.Code);
        Assert.Equal("gives retainage on pay application 7 as $18,524; earlier emails say $18,425", v.Text);
        Assert.Equal(2, v.Seq);
    }

    [Theory]
    [InlineData(LedgerKind.Figure, "retainage on pay application 7", "$18,425.00")]
    [InlineData(LedgerKind.Figure, "retainage on pay application 8", "$18,524")]
    [InlineData(LedgerKind.Decision, "retainage on pay application 7", "$18,524")]
    [InlineData(LedgerKind.Figure, "retainage on pay application 7", "")]
    public void V6_SameValueOtherSubjectOtherKindOrEmpty_IsNotFlagged(LedgerKind kind, string about, string value)
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { Retainage() };
        var rendered = With(0, "unused");
        rendered.NewDetails.Add(new LedgerDelta { Seq = 2, Kind = kind, About = about, Value = value });

        Assert.Empty(Check(rendered, ctx));
    }

    [Fact]
    public void V6_BodyAmountNearLedgerFigure_IsFlagged()
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { Retainage() };

        var v = Assert.Single(Check(With(2, "Retainage held back is $18,524 on this one."), ctx));
        Assert.Equal("V6", v.Code);
        Assert.Equal("gives retainage on pay application 7 as $18,524; earlier emails say $18,425", v.Text);
    }

    [Theory]
    [InlineData("Retainage held back is $18,425.00 on this one.")]
    [InlineData("The dumpster swap cost $450 this week.")]
    public void V6_BodyAmountEqualOrUnrelated_IsNotFlagged(string body)
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { Retainage() };

        Assert.Empty(Check(With(2, body), ctx));
    }

    [Theory]
    [InlineData("We moved things around, so the site walk is now April 9.")]
    [InlineData("We moved things around, so the site walk is now Apr 9, 2024.")]
    [InlineData("We moved things around, so the site walk is now 2024-04-09.")]
    public void V6_BodyDateContradictingMeeting_IsFlagged(string body)
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { SiteWalk() };

        var v = Assert.Single(Check(With(1, body), ctx));
        Assert.Equal("V6", v.Code);
        Assert.Equal("gives site walk with the glazing crew as 2024-04-09; earlier emails say 2024-04-02", v.Text);
        Assert.Equal(1, v.Seq);
    }

    [Theory]
    [InlineData("As agreed, the site walk is on April 2.")]
    [InlineData("As agreed, the site walk is on 2024-04-02.")]
    [InlineData("The site walk went fine. Invoice review is on April 9.")]
    [InlineData("We moved the site walk from April 2 to April 9.")]
    [InlineData("See you in March for the site walk follow-up.")]
    public void V6_BodyDateMatchingOrUnrelated_IsNotFlagged(string body)
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { SiteWalk() };

        Assert.Empty(Check(With(1, body), ctx));
    }

    [Fact]
    public void V6_BodyDateOnDecisionEntry_IsNotChecked()
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { SiteWalk(LedgerKind.Decision) };

        Assert.Empty(Check(With(1, "We moved things around, so the site walk is now April 9."), ctx));
    }

    [Fact]
    public void V6_CommitmentEntry_IsChecked()
    {
        var (ctx, _) = Context();
        ctx.LedgerView = new List<LedgerEntry> { SiteWalk(LedgerKind.Commitment) };

        Assert.Single(Check(With(1, "We moved things around, so the site walk is now April 9."), ctx), x => x.Code == "V6");
    }
}
