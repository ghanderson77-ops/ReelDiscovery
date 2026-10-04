using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class DecoyScannerTests
{
    private static PlannedThread Decoy(params string[] terms) => new()
    {
        Id = "T-0700",
        Relevance = ThreadRelevance.NotRelevant,
        Category = NonRelevantCategory.Decoy,
        DecoyAngle = "Talks about facades in general.",
        MustNotMention = terms.ToList()
    };

    private static RenderedThread Rendered(params string[] bodies) => new()
    {
        ThreadId = "T-0700",
        Messages = bodies.Select((b, i) => new RenderedMessage { Seq = i + 1, Body = b }).ToList()
    };

    private static string Mentions(string term) => $"mentions \"{term}\", which this email must not mention";

    [Fact]
    public void WholePhrase_CaseInsensitive_OneViolation()
    {
        var thread = Decoy("Curtain Wall Failure", "$1,250,000");

        var found = DecoyScanner.Scan(Rendered("Thanks.", "We discussed the curtain wall failure yesterday"), thread);

        var violation = Assert.Single(found);
        Assert.Equal(DecoyScanner.Code, violation.Code);
        Assert.Equal("D1", violation.Code);
        Assert.Equal(2, violation.Seq);
        Assert.Equal(Mentions("Curtain Wall Failure"), violation.Text);
    }

    [Fact]
    public void Amount_MatchesAcrossFormats()
    {
        var found = DecoyScanner.Scan(Rendered("the total came to $1.25M"), Decoy("$1,250,000"));

        var violation = Assert.Single(found);
        Assert.Equal(Mentions("$1,250,000"), violation.Text);
        Assert.Equal(1, violation.Seq);
    }

    [Fact]
    public void Amount_DifferentValue_NoHit()
    {
        Assert.Empty(DecoyScanner.Scan(Rendered("the total came to $1,250,001"), Decoy("$1,250,000")));
    }

    [Fact]
    public void Identifier_WholeWordOnly()
    {
        Assert.Empty(DecoyScanner.Scan(Rendered("See CO-0071 for the log format."), Decoy("CO-007")));
        Assert.Single(DecoyScanner.Scan(Rendered("See CO-007 for the log format."), Decoy("CO-007")));
    }

    [Fact]
    public void OneViolationPerSeqAndTerm()
    {
        var found = DecoyScanner.Scan(Rendered("CO-007 and again CO-007", "CO-007"), Decoy("CO-007", " co-007 "));

        Assert.Equal(new[] { 1, 2 }, found.Select(v => v.Seq));
    }

    [Fact]
    public void BlankTerms_AndNullList_Ignored()
    {
        var thread = Decoy("", "   ");
        Assert.Empty(DecoyScanner.Scan(Rendered("anything at all"), thread));

        thread.MustNotMention = null!;
        Assert.Empty(DecoyScanner.Scan(Rendered("anything at all"), thread));
        Assert.Empty(DecoyScanner.ScanText("anything at all", 1, thread));
    }

    [Fact]
    public void ScanText_SameShape()
    {
        var found = DecoyScanner.ScanText("Attachment notes: curtain wall failure photos", 3, Decoy("Curtain Wall Failure"));

        var violation = Assert.Single(found);
        Assert.Equal("D1", violation.Code);
        Assert.Equal(3, violation.Seq);
        Assert.Equal(Mentions("Curtain Wall Failure"), violation.Text);
        Assert.Empty(DecoyScanner.ScanText(null, 3, Decoy("Curtain Wall Failure")));
    }

    [Fact]
    public void NonDecoy_Empty()
    {
        var thread = Decoy("Curtain Wall Failure");
        thread.Category = NonRelevantCategory.DailyBusiness;

        Assert.Empty(DecoyScanner.Scan(Rendered("curtain wall failure"), thread));
        Assert.Empty(DecoyScanner.ScanText("curtain wall failure", 1, thread));
    }

    [Fact]
    public void NullInputs_NeverThrow()
    {
        Assert.Empty(DecoyScanner.Scan(null!, Decoy("x")));
        Assert.Empty(DecoyScanner.Scan(Rendered("x"), null!));
        var rendered = new RenderedThread { Messages = new List<RenderedMessage> { null!, new() { Seq = 2, Body = null! } } };
        Assert.Empty(DecoyScanner.Scan(rendered, Decoy("x")));
    }

    [Fact]
    public void PathologicalInput_ReturnsWhatItFound()
    {
        var huge = string.Concat(Enumerable.Repeat("$1,000,000,000 PO-1 No. 12-34-56 ", 70_000)) + " curtain wall failure";

        var found = DecoyScanner.Scan(Rendered(huge), Decoy("$9", "Curtain Wall Failure"));

        Assert.Contains(found, v => v.Text == Mentions("Curtain Wall Failure"));
    }
}
