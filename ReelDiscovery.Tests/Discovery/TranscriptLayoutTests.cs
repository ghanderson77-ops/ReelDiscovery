using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class TranscriptLayoutTests
{
    private static int PageIndexContaining(TranscriptDocument doc, string text) =>
        doc.Pages.FindIndex(p => p.Lines.Any(l => l.Contains(text, StringComparison.Ordinal)));

    private static string PageText(TranscriptPage page) => string.Join("\n", page.Lines);

    // Testimony text with line breaks folded to single spaces (sentences may wrap across lines)
    private static string TestimonyText(TranscriptDocument doc) =>
        string.Join(" ", doc.Pages.Where(p => p.IsTestimony).SelectMany(p => p.Lines).Select(l => l.Trim()).Where(l => l.Length > 0));

    private static string LineAt(TranscriptDocument doc, TranscriptLocation at) => doc.Pages[at.Page - 1].Lines[at.Line - 1];

    [Fact]
    public void Wrap_LongQuestion_StaysWithinWidth_AndHardSplitsOversizeToken()
    {
        var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => "word" + i));
        var token = new string('x', 45) + new string('y', 45);
        var text = "Q.   " + words[..300] + " " + token + " end";

        var lines = TranscriptLayout.Wrap(text, 62, continuationIndent: 0);

        Assert.True(lines.Count > 5);
        Assert.All(lines, l => Assert.True(l.Length <= 62, l));
        Assert.StartsWith("Q.   ", lines[0]);
        Assert.All(lines.Skip(1), l => Assert.False(l.StartsWith(' '), l));
        Assert.Contains(token, string.Concat(lines).Replace(" ", string.Empty));
    }

    [Fact]
    public void Wrap_ContinuationIndent_PrefixesContinuationLines()
    {
        var lines = TranscriptLayout.Wrap("    (" + string.Join(" ", Enumerable.Repeat("discussion", 20)) + ")", 62, continuationIndent: 5);

        Assert.StartsWith("    (", lines[0]);
        Assert.All(lines.Skip(1), l => Assert.StartsWith("     d", l));
        Assert.All(lines, l => Assert.True(l.Length <= 62));
    }

    [Fact]
    public void FormatTurn_QAndA_UseColumnFiveText()
    {
        var content = TranscriptTestData.Sample();

        var q = TranscriptLayout.FormatTurn(new DepositionTurn { Kind = TurnKind.Q, SpeakerId = "CH-011", Text = "Where were you?" }, content);
        var a = TranscriptLayout.FormatTurn(new DepositionTurn { Kind = TurnKind.A, SpeakerId = "CH-013", Text = "At the site." }, content);

        Assert.Equal("Q.   Where were you?", Assert.Single(q));
        Assert.Equal("A.   At the site.", Assert.Single(a));
    }

    [Fact]
    public void FormatTurn_ColloquyAndObjection_UseLabelAtColumnTen()
    {
        var content = TranscriptTestData.Sample();

        var colloquy = TranscriptLayout.FormatTurn(new DepositionTurn { Kind = TurnKind.Colloquy, SpeakerId = "CH-011", Text = "Let's take a short break." }, content);
        var objection = TranscriptLayout.FormatTurn(new DepositionTurn { Kind = TurnKind.Objection, SpeakerId = "CH-012", Text = "Objection, form." }, content);

        Assert.StartsWith(new string(' ', 9) + "MS. QUIST:  ", colloquy[0]);
        Assert.Equal(new string(' ', 9) + "MR. HALE:  Objection, form.", Assert.Single(objection));
    }

    [Fact]
    public void FormatTurn_Parenthetical_OpensAtColumnFive_ContinuesIndented()
    {
        var content = TranscriptTestData.Sample();
        var text = string.Join(" ", Enumerable.Repeat("Discussion held off the record between counsel", 3));

        var lines = TranscriptLayout.FormatTurn(new DepositionTurn { Kind = TurnKind.Parenthetical, Text = text }, content);

        Assert.StartsWith("    (Discussion", lines[0]);
        Assert.EndsWith(")", lines[^1]);
        Assert.True(lines.Count > 1);
        Assert.All(lines.Skip(1), l => Assert.StartsWith(new string(' ', 5), l));
    }

    [Fact]
    public void SpeakerLabel_RolesAndFallbacks()
    {
        var content = TranscriptTestData.Sample();
        content.Speakers.Add(new DepositionSpeaker { SpeakerId = "X", FullName = "Pat Lee", Role = SpeakerRole.Appearance });

        Assert.Equal("MS. QUIST", TranscriptLayout.SpeakerLabel(content, "CH-011"));
        Assert.Equal("THE WITNESS", TranscriptLayout.SpeakerLabel(content, "CH-013"));
        Assert.Equal("THE REPORTER", TranscriptLayout.SpeakerLabel(content, "REP"));
        Assert.Equal("THE VIDEOGRAPHER", TranscriptLayout.SpeakerLabel(content, "VID"));
        Assert.Equal("PAT LEE", TranscriptLayout.SpeakerLabel(content, "X"));
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(400, false)]
    [InlineData(120, true)]
    public void EveryPage_HasExactly25Lines_NoneOver62(int turns, bool memo)
    {
        var doc = TranscriptLayout.Build(TranscriptTestData.Sample(turns, memo));

        Assert.NotEmpty(doc.Pages);
        foreach (var page in doc.Pages)
        {
            Assert.Equal(TranscriptLayout.LinesPerPage, page.Lines.Count);
            Assert.All(page.Lines, l => Assert.True(l.Length <= TranscriptLayout.MaxCharsPerLine, $"p{page.Number}: {l}"));
        }

        Assert.Equal(Enumerable.Range(1, doc.Pages.Count), doc.Pages.Select(p => p.Number));
    }

    [Fact]
    public void CaptionPage_CarriesCaseIdentity()
    {
        var content = TranscriptTestData.Sample();
        var doc = TranscriptLayout.Build(content);
        var caption = PageText(doc.Pages[0]);

        Assert.Contains("3:25-cv-04821", caption);
        Assert.Contains("DEPOSITION OF MICHAEL SCOTT", caption);
        Assert.Contains("EXCERPT", caption);
        Assert.Contains($"Pages 1 - {doc.Pages.Count}", caption);
        Assert.Contains("September 16, 2025", caption);
        Assert.Contains("9:30 a.m.", caption);
        Assert.Contains("Hale & Ward LLP", caption);
        Assert.Contains("Janet Moore, CSR No. 13021", caption);
        Assert.Contains("UNITED STATES DISTRICT COURT", caption);
        Assert.False(doc.Pages[0].IsTestimony);
    }

    [Fact]
    public void CaptionPage_NeverOverflows()
    {
        var content = TranscriptTestData.Sample();
        content.CaptionLines = Enumerable.Range(0, 40).Select(i => "CAPTION LINE " + i).ToList();

        var doc = TranscriptLayout.Build(content);

        Assert.Contains("DEPOSITION OF MICHAEL SCOTT", PageText(doc.Pages[0]));
        Assert.Contains("CSR No. 13021", PageText(doc.Pages[0]));
        Assert.DoesNotContain("CAPTION LINE", PageText(doc.Pages[1]));
    }

    [Fact]
    public void AppearancesAndIndex_ListCounselExaminationAndExhibits()
    {
        var doc = TranscriptLayout.Build(TranscriptTestData.Sample());

        var appearances = PageIndexContaining(doc, "APPEARANCES:");
        Assert.True(appearances > 0);
        var appearanceText = PageText(doc.Pages[appearances]);
        Assert.Contains("Ingrid Quist", appearanceText);
        Assert.Contains("Daniel Hale", appearanceText);
        Assert.Contains("iquist@quistpartners.com", appearanceText);
        Assert.Contains("ALSO PRESENT:", appearanceText);
        Assert.Contains("Carl Brooks", appearanceText);

        var index = PageIndexContaining(doc, "INDEX");
        Assert.True(index > appearances);
        var indexLines = doc.Pages[index].Lines;
        var exam = Assert.Single(doc.Examinations);
        Assert.Contains(indexLines, l => l.Contains("BY MS. QUIST") && l.TrimEnd().EndsWith(" " + exam.At.Page));
        Assert.Contains(indexLines, l => l.Contains("EXAMINATION"));

        foreach (var exhibit in TranscriptTestData.Sample().Exhibits)
        {
            var marked = doc.ExhibitMarked[exhibit.Number];
            var start = indexLines.FindIndex(l => l.TrimStart().StartsWith(exhibit.Number + " ") && l.Contains(exhibit.Description[..20]));
            Assert.True(start >= 0, "exhibit " + exhibit.Number);
            var block = indexLines.Skip(start).TakeWhile((l, i) => i == 0 || l.StartsWith("      ")).ToList();
            var lastLine = block[^1].TrimEnd();
            var page = marked.Page.ToString();
            Assert.EndsWith(" " + page, lastLine);
            block[^1] = lastLine[..^page.Length];
            Assert.Contains(exhibit.Description, string.Join(" ", block.Select(l => l.Trim())));
        }
    }

    [Fact]
    public void ExhibitMarker_PrecedesIntroducingTurn()
    {
        var content = TranscriptTestData.Sample();
        var doc = TranscriptLayout.Build(content);

        Assert.Equal(new[] { 1, 2, 3 }, doc.ExhibitMarked.Keys.OrderBy(k => k));
        foreach (var (number, at) in doc.ExhibitMarked)
        {
            Assert.Equal($"(Deposition Exhibit {number} was marked for identification.)", LineAt(doc, at).Trim());
            Assert.True(doc.Pages[at.Page - 1].IsTestimony);

            // The next line (possibly on the next page) opens the introducing question
            var next = at.Line < 25 ? doc.Pages[at.Page - 1].Lines[at.Line] : doc.Pages[at.Page].Lines[0];
            Assert.StartsWith("Q.   I am handing you what has been marked as", next);
        }

        Assert.True(doc.ExhibitMarked[1].Page < doc.ExhibitMarked[3].Page);
    }

    [Fact]
    public void ExhibitMarker_MemoWording()
    {
        var doc = TranscriptLayout.Build(TranscriptTestData.Sample(memo: true));

        Assert.Equal("(Exhibit 2 was marked for identification.)", LineAt(doc, doc.ExhibitMarked[2]).Trim());
        Assert.DoesNotContain("deposition", TestimonyText(doc), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(Whereupon the interview concluded at", TestimonyText(doc));
    }

    [Fact]
    public void Testimony_OpensWithOathAndExamination_EndsWithConcludedLine_PaddedPage()
    {
        var content = TranscriptTestData.Sample();
        var doc = TranscriptLayout.Build(content);
        var text = TestimonyText(doc);

        Assert.Contains("We are on the record at 9:30 a.m. on September 16, 2025.", text);
        Assert.Contains("MICHAEL SCOTT, having been first duly sworn, was examined and testified as follows:", text);
        Assert.Contains("EXAMINATION BY MS. QUIST:", text);

        var first = doc.Pages[doc.FirstTestimonyPage - 1];
        Assert.Contains(first.Lines, l => l.Trim() == "EXAMINATION" && l.StartsWith("  "));
        Assert.Contains(first.Lines, l => l == "BY MS. QUIST:");
        Assert.Equal("SCOTT - EXAMINATION", first.Heading);

        var last = doc.Pages[doc.LastTestimonyPage - 1];
        var lastText = last.Lines.Where(l => l.Length > 0).ToList();
        Assert.StartsWith("(Whereupon the deposition concluded at ", lastText[^1].Trim());
        Assert.Equal(string.Empty, last.Lines[^1]);

        Assert.Equal(doc.LastTestimonyPage - doc.FirstTestimonyPage + 1, doc.TestimonyPageCount);
        Assert.Equal(doc.TestimonyPageCount, doc.Pages.Count(p => p.IsTestimony));
        Assert.True(doc.TestimonyPageCount >= 8);
        Assert.All(doc.Pages.Where(p => !p.IsTestimony), p => Assert.Equal(string.Empty, p.Heading));
    }

    [Fact]
    public void BackMatter_SignatureErrataCertificate_InOrder()
    {
        var doc = TranscriptLayout.Build(TranscriptTestData.Sample());

        var signature = PageIndexContaining(doc, "I, MICHAEL SCOTT, have read");
        var errata = PageIndexContaining(doc, "ERRATA SHEET");
        var certificate = PageIndexContaining(doc, "REPORTER'S CERTIFICATE");

        Assert.True(signature + 1 > doc.LastTestimonyPage);
        Assert.True(errata > signature);
        Assert.True(certificate > errata);
        Assert.Equal(doc.Pages.Count - 1, certificate);

        var errataText = PageText(doc.Pages[errata]);
        foreach (var column in new[] { "PAGE", "LINE", "CHANGE", "REASON" })
            Assert.Contains(column, errataText);

        var certificateText = string.Join(" ", doc.Pages[certificate].Lines.Select(l => l.Trim()));
        Assert.Contains("duly sworn", certificateText);
        Assert.Contains("CSR No. 13021", certificateText);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var a = TranscriptLayout.Build(TranscriptTestData.Sample());
        var b = TranscriptLayout.Build(TranscriptTestData.Sample());

        Assert.Equal(a.Pages.Count, b.Pages.Count);
        for (var i = 0; i < a.Pages.Count; i++)
        {
            Assert.Equal(a.Pages[i].Heading, b.Pages[i].Heading);
            Assert.Equal(a.Pages[i].Lines, b.Pages[i].Lines);
        }
    }

    [Fact]
    public void CountTestimonyLines_MatchesUnpaddedTestimony()
    {
        var content = TranscriptTestData.Sample();
        var doc = TranscriptLayout.Build(content);
        var lines = TranscriptLayout.CountTestimonyLines(content);

        Assert.Equal((lines + TranscriptLayout.LinesPerPage - 1) / TranscriptLayout.LinesPerPage, doc.TestimonyPageCount);
        Assert.True(TranscriptLayout.CountTestimonyLines(TranscriptTestData.Sample(400)) > lines);
    }

    [Theory]
    [InlineData("9:30 a.m.", 100, "10:45 a.m.")]
    [InlineData("9:30 a.m.", 625, "4:30 p.m.")]
    [InlineData("1:02 p.m.", 40, "1:35 p.m.")]
    public void ComputeEndTime_RoundsAndCaps(string start, int lines, string expected)
    {
        Assert.Equal(expected, TranscriptLayout.ComputeEndTime(start, lines));
    }

    [Fact]
    public void ComputeEndTime_UnparseableStart()
    {
        Assert.Equal("the close of the session", TranscriptLayout.ComputeEndTime("morning", 100));
    }

    [Fact]
    public void Build_StripsControlCharactersAndLoneSurrogates()
    {
        var content = TranscriptTestData.Sample();
        content.Turns[3].Text = "bad\u0001text\uD800 here\u0007";
        content.WitnessName = "Michael\u0002 Scott";

        var doc = TranscriptLayout.Build(content);

        foreach (var line in doc.Pages.SelectMany(p => p.Lines.Append(p.Heading)))
            Assert.DoesNotContain(line, c => (c < 0x20) || char.IsSurrogate(c));
        Assert.Contains("badtext here", TestimonyText(doc));
    }
}
