using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class TranscriptDocxBuilderTests
{
    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);

    private static string? StyleOf(Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    [Fact]
    public void Build_ValidatesClean()
    {
        var bytes = TranscriptDocxBuilder.Build(TranscriptTestData.Sample());

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(bytes);

        Assert.True(errors.Count == 0, DocxTestHelper.ErrorsText(errors));
        Assert.Empty(errors);
        Assert.NotEmpty(paragraphs);
    }

    [Fact]
    public void Build_OnePageBreakPerPageAfterFirst_And25NumberedLinesPerPage()
    {
        var content = TranscriptTestData.Sample();
        var layout = TranscriptLayout.Build(content);

        using var doc = Open(TranscriptDocxBuilder.Build(content));
        var body = doc.MainDocumentPart!.Document!.Body!;

        Assert.Equal(layout.Pages.Count - 1, body.Descendants<PageBreakBefore>().Count());

        var lines = body.Elements<Paragraph>().Where(p => StyleOf(p) == "TranscriptLine").ToList();
        Assert.Equal(layout.Pages.Count * 25, lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var text = DocxTestHelper.ParagraphText(lines[i]);
            var expected = layout.Pages[i / 25].Lines[i % 25];
            Assert.Equal($"\t{i % 25 + 1}\t{expected}", text);
        }

        var headings = body.Elements<Paragraph>().Where(p => StyleOf(p) == "TranscriptHeading").ToList();
        Assert.Equal(layout.Pages.Count, headings.Count);
        Assert.Null(headings[0].ParagraphProperties!.PageBreakBefore);
        Assert.All(headings.Skip(1), h => Assert.NotNull(h.ParagraphProperties!.PageBreakBefore));
        var testimony = layout.Pages[layout.FirstTestimonyPage - 1];
        Assert.Equal($"{testimony.Heading}\t{testimony.Number}", DocxTestHelper.ParagraphText(headings[testimony.Number - 1]));
    }

    [Fact]
    public void Build_CourierNew12_ExactSpacingOnEveryParagraph()
    {
        using var doc = Open(TranscriptDocxBuilder.Build(TranscriptTestData.Sample()));

        var defaults = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.DocDefaults!;
        var fonts = defaults.Descendants<RunFonts>().First();
        Assert.Equal("Courier New", fonts.Ascii?.Value);
        Assert.Equal("Courier New", fonts.HighAnsi?.Value);
        Assert.Equal("Courier New", fonts.ComplexScript?.Value);
        Assert.Equal("Courier New", fonts.EastAsia?.Value);
        Assert.Equal("24", defaults.Descendants<FontSize>().First().Val?.Value);

        foreach (var paragraph in doc.MainDocumentPart.Document!.Body!.Elements<Paragraph>())
        {
            var spacing = paragraph.ParagraphProperties?.SpacingBetweenLines;
            Assert.NotNull(spacing);
            Assert.Equal("480", spacing!.Line?.Value);
            Assert.Equal(LineSpacingRuleValues.Exact, spacing.LineRule?.Value);
            Assert.Equal("0", spacing.Before?.Value);
            Assert.Equal("0", spacing.After?.Value);
        }
    }

    [Fact]
    public void Build_SectionGeometry_NoWordLineNumbering()
    {
        using var doc = Open(TranscriptDocxBuilder.Build(TranscriptTestData.Sample()));
        var body = doc.MainDocumentPart!.Document!.Body!;

        var section = Assert.IsType<SectionProperties>(body.LastChild);
        var size = section.GetFirstChild<PageSize>()!;
        var margin = section.GetFirstChild<PageMargin>()!;
        Assert.Equal(12240U, size.Width!.Value);
        Assert.Equal(15840U, size.Height!.Value);
        Assert.Equal(1080, margin.Top!.Value);
        Assert.Equal(1080, margin.Bottom!.Value);
        Assert.Equal(1440U, margin.Left!.Value);
        Assert.Equal(540U, margin.Right!.Value);
        Assert.Empty(doc.MainDocumentPart.Document.Descendants<LineNumberType>());
        Assert.Empty(doc.MainDocumentPart.Document.Descendants<FieldCode>());
    }

    [Fact]
    public void Build_PackageProperties()
    {
        var content = TranscriptTestData.Sample();

        using var doc = Open(TranscriptDocxBuilder.Build(content));

        Assert.Equal(content.DocumentTitle, doc.PackageProperties.Title);
        Assert.Equal("ReelDiscovery", doc.PackageProperties.Creator);
    }

    [Fact]
    public void Build_ControlCharactersAndLoneSurrogates_StillValidate()
    {
        var content = TranscriptTestData.Sample();
        content.Turns[1].Text = "Bad\u0001 answer \uDC00 text";
        content.DocumentTitle = "Deposition\u0001 of \uD800Michael Scott";
        content.Exhibits[0].Description = "Email\u0003 re submittal";

        var (errors, _) = DocxTestHelper.OpenAndValidate(TranscriptDocxBuilder.Build(content));

        Assert.True(errors.Count == 0, DocxTestHelper.ErrorsText(errors));
    }

    [Fact]
    public void Build_LayoutOverload_RendersHeadingsWithStrippedText()
    {
        var layout = TranscriptLayout.Build(TranscriptTestData.Sample());
        layout.Pages[0].Lines[0] = "raw\u0001line";
        layout.Pages[0].Heading = "head\u0002ing";

        var (errors, paragraphs) = DocxTestHelper.OpenAndValidate(TranscriptDocxBuilder.Build(layout, "Title\u0001"));

        Assert.True(errors.Count == 0, DocxTestHelper.ErrorsText(errors));
        Assert.Contains("\t1\trawline", paragraphs);
        Assert.Contains("heading\t1", paragraphs);
    }
}
