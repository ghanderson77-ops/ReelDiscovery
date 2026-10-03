using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class RenderRepairTests
{
    private static ThreadRenderContext Context(string factId)
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var thread = PlanFixtures.Thread(plan, factId);
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        return ThreadRenderContext.Create(draft, plan, thread, RenderAssignments.Compute(plan)[thread.Id], people,
            new List<LedgerEntry>(), 1);
    }

    private static PersonInfo Tom(ThreadRenderContext ctx) => ctx.People.Find("CH-002")!;

    private static string Clean(string? body, PersonInfo? sender = null) =>
        RenderRepair.CleanBody(body, sender, new List<string>(), 1);

    private static ThreadRenderer.RenderedMessageDto Message(int seq, string body, string signOff = "Thanks,") =>
        new() { Seq = seq, Body = body, SignOff = signOff, PeopleNamed = new List<string>() };

    [Fact]
    public void Apply_MapsBySeqInPlannedOrder_DropsDuplicatesAndUnknown()
    {
        // F-004: two planned messages
        var ctx = Context("F-004");
        Assert.Equal(2, ctx.Thread.Messages.Count);
        var dto = new ThreadRenderer.ThreadRenderDto
        {
            Messages = new List<ThreadRenderer.RenderedMessageDto>
            {
                Message(2, "First reply text."),
                Message(1, "Opening text."),
                Message(2, "Second copy of the reply."),
                Message(9, "Not planned.")
            }
        };

        var rendered = RenderRepair.Apply(dto, ctx);

        Assert.Equal(ctx.Thread.Id, rendered.ThreadId);
        Assert.Equal(new[] { 1, 2 }, rendered.Messages.Select(m => m.Seq));
        Assert.Equal("First reply text.", rendered.Messages[1].Body);
        Assert.Contains(rendered.RepairNotes, n => n.Contains("duplicate msg 2"));
        Assert.Contains(rendered.RepairNotes, n => n.Contains("unknown msg 9"));
    }

    [Fact]
    public void CleanBody_CutsQuotedHistory()
    {
        Assert.Equal("Sounds good.",
            Clean("Sounds good.\n\nOn Tue, Mar 19, 2024 at 9:42 AM, Lisa Chen <lisa@harmon.com> wrote:\n> earlier"));
        Assert.Equal("Sounds good.", Clean("Sounds good.\r\n> earlier line\r\n> more"));
        Assert.Equal("See below.", Clean("See below.\n\n-----Original Message-----\nFrom: someone\nSent: today"));
        Assert.Equal("Passing this on.", Clean("Passing this on.\n\n---------- Forwarded message ---------\nFrom: someone"));
    }

    [Fact]
    public void CleanBody_StripsTrailingNameAndSignature_KeepsUnrelatedLastLine()
    {
        var ctx = Context("F-003");
        var tom = Tom(ctx);

        Assert.Equal("Sounds good.\n\nThanks,", Clean("Sounds good.\n\nThanks,\nTom", tom));
        Assert.Equal("Sounds good.", Clean("Sounds good.\n\n" + tom.Cast!.Signature, tom));
        Assert.Equal("Sounds good.", Clean("Sounds good.\nTom Becker\nSent from my mobile", tom));
        Assert.Equal("Sounds good.\nThe crew starts Monday.", Clean("Sounds good.\nThe crew starts Monday.", tom));
    }

    [Fact]
    public void CleanBody_RemovesLeadingPrivilegeBanner()
    {
        Assert.Equal("Here is my advice.", Clean(HtmlEmailFormatter.PrivilegedBannerText + "\n\nHere is my advice."));
    }

    [Fact]
    public void CleanBody_StripsIdsTagsUrlsAndSingleEmphasis()
    {
        Assert.Equal("See and", Clean("See F-003 and CH-002"));
        Assert.Equal("hi", Clean("<b>hi</b>"));
        Assert.Equal("Link:", Clean("Link: https://x.example/a"));
        Assert.Equal("a note here", Clean("a _note_ here"));
        Assert.Equal("a word here", Clean("a *word* here"));
        Assert.Equal("a **bold** here", Clean("a **bold** here"));
    }

    [Theory]
    [InlineData("Attached is Pay_App_7.xlsx for review.")]
    [InlineData("Use file_name_v2.xlsx please.")]
    [InlineData("The snake_case_id field.")]
    [InlineData("Values a_b only.")]
    public void CleanBody_LeavesUnderscoresInsideIdentifiers(string body)
    {
        Assert.Equal(body, Clean(body));
    }

    [Fact]
    public void CleanBody_TruncatesLongBodyAtWordBoundary()
    {
        var body = string.Join(" ", Enumerable.Repeat("schedule", 1_000));

        var cleaned = Clean(body);

        Assert.True(cleaned.Length <= RenderRepair.MaxBodyChars);
        Assert.True(cleaned.Length > RenderRepair.MaxBodyChars - 20);
        Assert.EndsWith("schedule", cleaned);
    }

    [Fact]
    public void Apply_CleansSignOffAndPeopleNamed()
    {
        // F-003: msg 1 Lisa Chen, msg 2 Tom Becker, msg 3 Lisa Chen, msg 4 Tom Becker
        var ctx = Context("F-003");
        Assert.Equal("CH-002", ctx.Thread.Messages.Single(m => m.Seq == 2).FromId);
        var people = Enumerable.Range(0, 40).Select(i => $" Person {i} ").Append(" person 1").ToList();
        var dto = new ThreadRenderer.ThreadRenderDto
        {
            Messages = new List<ThreadRenderer.RenderedMessageDto>
            {
                new() { Seq = 1, Body = "Text.", SignOff = "Best regards,\nLisa", PeopleNamed = people },
                new() { Seq = 2, Body = "Text.", SignOff = "Tom Becker", PeopleNamed = new List<string>() },
                new() { Seq = 3, Body = "Text.", SignOff = new string('x', 60), PeopleNamed = new List<string>() },
                new() { Seq = 4, Body = null!, SignOff = null!, PeopleNamed = null! }
            }
        };

        var rendered = RenderRepair.Apply(dto, ctx);

        Assert.Equal("Best regards,", rendered.Messages[0].SignOff);
        Assert.Equal(string.Empty, rendered.Messages[1].SignOff);
        Assert.Equal(40, rendered.Messages[2].SignOff.Length);
        Assert.Equal(string.Empty, rendered.Messages[3].SignOff);
        Assert.Equal(string.Empty, rendered.Messages[3].Body);
        Assert.Empty(rendered.Messages[3].PeopleNamed);

        var named = rendered.Messages[0].PeopleNamed;
        Assert.Equal(30, named.Count);
        Assert.Equal("Person 0", named[0]);
        Assert.Single(named, p => p.Equals("person 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_RemovesTrailingSignOffLineFromBody()
    {
        var ctx = Context("F-003");
        var dto = new ThreadRenderer.ThreadRenderDto
        {
            Messages = new List<ThreadRenderer.RenderedMessageDto> { Message(1, "Please confirm the start date.\n\nThanks,") }
        };

        var rendered = RenderRepair.Apply(dto, ctx);

        Assert.Equal("Please confirm the start date.", rendered.Messages[0].Body);
        Assert.Equal("Thanks,", rendered.Messages[0].SignOff);
    }

    [Fact]
    public void Apply_MapsNewDetailsCapped()
    {
        var ctx = Context("F-003");
        var dto = new ThreadRenderer.ThreadRenderDto
        {
            Messages = new List<ThreadRenderer.RenderedMessageDto> { Message(1, "Text.") },
            NewDetails = Enumerable.Range(1, 25).Select(i => new ThreadRenderer.LedgerDeltaDto
            {
                Seq = 1,
                Kind = LedgerKind.Figure,
                About = $"  item {i} ",
                Statement = $" Statement {i}. ",
                Value = " 2024-03-20 ",
                FactIds = new List<string> { " F-003 " }
            }).ToList()
        };

        var rendered = RenderRepair.Apply(dto, ctx);

        Assert.Equal(20, rendered.NewDetails.Count);
        var detail = rendered.NewDetails[0];
        Assert.Equal(1, detail.Seq);
        Assert.Equal(LedgerKind.Figure, detail.Kind);
        Assert.Equal("item 1", detail.About);
        Assert.Equal("Statement 1.", detail.Statement);
        Assert.Equal("2024-03-20", detail.Value);
        Assert.Equal(new[] { "F-003" }, detail.FactIds);
    }

    [Fact]
    public void Apply_NullDtoAndNullMembersDoNotThrow()
    {
        var ctx = Context("F-003");

        var empty = RenderRepair.Apply(null, ctx);
        Assert.Equal(ctx.Thread.Id, empty.ThreadId);
        Assert.Empty(empty.Messages);

        var partial = RenderRepair.Apply(new ThreadRenderer.ThreadRenderDto { Messages = null!, NewDetails = null! }, ctx);
        Assert.Empty(partial.Messages);
        Assert.Empty(partial.NewDetails);

        Assert.Equal(string.Empty, Clean(null));
    }
}
