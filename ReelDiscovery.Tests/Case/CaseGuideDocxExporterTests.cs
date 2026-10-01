using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseGuideDocxExporterTests
{
    private static (List<ValidationErrorInfo> Errors, string Text) OpenAndValidate(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(ParagraphText);
        return (errors, string.Join("\n", paragraphs));
    }

    // Text with Break/TabChar elements rendered as newlines/tabs
    private static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants().Select(e => e switch
        {
            Text t => t.Text,
            Break => "\n",
            TabChar => "\t",
            _ => string.Empty
        }));

    [Fact]
    public void CoreDocument_Complaint_OpensAndValidates()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var doc = draft.CoreDocument!;

        var (errors, text) = OpenAndValidate(CaseGuideDocxExporter.BuildCoreDocument(draft));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains(doc.Jurisdiction.CourtName.ToUpperInvariant(), text);
        foreach (var section in doc.Sections)
            Assert.Contains(section.Heading, text);

        var first = CoreDocumentAssembler.Number(doc)[0];
        Assert.Contains($"1. {first.Text}", text);
        Assert.Contains(doc.Counsel.AttorneyName, text);
        Assert.Contains("A. Brightline asks the Court", text);
        Assert.Contains("FACTUAL ALLEGATIONS", text);
        Assert.Contains("DEMAND FOR JURY TRIAL", text);
    }

    [Fact]
    public void CoreDocument_Memo_OpensAndValidates()
    {
        var draft = CaseTestData.SampleMemoDraft();

        var (errors, text) = OpenAndValidate(CaseGuideDocxExporter.BuildCoreDocument(draft));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        // Label and value are tab-aligned in Word
        Assert.Contains("RE:\t" + draft.CoreDocument!.Memo.Re, text);
        Assert.Contains("EXECUTIVE SUMMARY", text);
        Assert.DoesNotContain("1. The task force", text);
    }

    [Fact]
    public void Styles_Present()
    {
        var bytes = CaseGuideDocxExporter.BuildCoreDocument(CaseTestData.SampleComplaintDraft());

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var styleIds = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<Style>().Select(s => s.StyleId?.Value).ToList();

        Assert.Contains("Title", styleIds);
        Assert.Contains("Heading1", styleIds);
        Assert.Contains("Heading2", styleIds);
        Assert.Contains("GuideTable", styleIds);
    }

    [Fact]
    public void StripInvalidXmlChars_RemovesControlCharacters()
    {
        Assert.Equal("abcd\te\nf", CaseGuideDocxExporter.StripInvalidXmlChars("a\u0001b\u000Bc￾d\te\nf"));
        Assert.Equal(string.Empty, CaseGuideDocxExporter.StripInvalidXmlChars(null));
        // Valid surrogate pair kept, lone surrogate dropped
        Assert.Equal("x\U0001F600y", CaseGuideDocxExporter.StripInvalidXmlChars("x\U0001F600\uD800y"));
    }

    [Fact]
    public void ControlChars_StillValid()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Sections[0].Paragraphs[0] = "Bad\u0001chars\u0008 here.";

        var (errors, text) = OpenAndValidate(CaseGuideDocxExporter.BuildCoreDocument(draft));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains("1. Badchars here.", text);
    }

    [Theory]
    [InlineData(CaseExportKind.CoreDocument, "Brightline v. Harmon - Core Document.docx")]
    [InlineData(CaseExportKind.Cast, "Brightline v. Harmon - Cast of Key Characters.docx")]
    [InlineData(CaseExportKind.KeyFacts, "Brightline v. Harmon - Key Facts & Events.docx")]
    public void FileNameFor_UsesFolderName(CaseExportKind kind, string expected)
    {
        Assert.Equal(expected, CaseGuideDocxExporter.FileNameFor(kind, "Brightline v. Harmon"));
    }

    [Theory]
    [InlineData(CaseExportKind.CoreDocument, "core document")]
    [InlineData(CaseExportKind.Cast, "cast")]
    [InlineData(CaseExportKind.KeyFacts, "key facts summary")]
    public void DocumentNoun_ForErrorCopy(CaseExportKind kind, string expected)
    {
        Assert.Equal(expected, CaseGuideDocxExporter.DocumentNoun(kind));
    }

    private static List<Table> Tables(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        return doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Select(t => (Table)t.CloneNode(true)).ToList();
    }

    private static int DataRows(Table table) => table.Elements<TableRow>().Count() - 1;

    private static List<string> ColumnText(Table table, int column) =>
        table.Elements<TableRow>().Skip(1).Select(r => r.Elements<TableCell>().ElementAt(column).InnerText).ToList();

    private static bool HasLandscapeSection(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<PageSize>()
            .Any(p => p.Orient != null && p.Orient.Value == PageOrientationValues.Landscape);
    }

    [Fact]
    public void Cast_OpensAndValidates()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var bytes = CaseGuideDocxExporter.BuildCast(draft);

        var (errors, text) = OpenAndValidate(bytes);

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        foreach (var heading in new[] { "Cast of Key Characters", "Organizations", "Key Characters", "Character Profiles", "Background Staff" })
            Assert.Contains(heading, text);
        Assert.Contains("maria.delgado@brightline.com", text);
        Assert.Contains("7 key and 2 background characters across 3 organizations.", text);

        var tables = Tables(bytes);
        Assert.Equal(3, DataRows(tables[0]));
        Assert.Equal(7, DataRows(tables[1]));
        Assert.Equal(2, tables.Skip(2).Sum(DataRows));
        Assert.Contains("Outside counsel", ColumnText(tables[1], 6));
        Assert.True(HasLandscapeSection(bytes));
    }

    [Fact]
    public void Cast_ProfilesIncludeVoiceAndSignature()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var maria = draft.Guide!.FindCast("CH-001")!;

        var (_, text) = OpenAndValidate(CaseGuideDocxExporter.BuildCast(draft));

        Assert.Contains("Maria Delgado (CH-001)", text);
        Assert.Contains(maria.Involvement, text);
        Assert.Contains(maria.VoiceNote, text);
        Assert.Contains(maria.Signature, text);
        Assert.Contains("October 1, 2023 to December 31, 2024", text);
    }

    [Fact]
    public void KeyFacts_OpensAndValidates()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        // Shuffle so the export has to sort by date
        draft.Guide!.Facts.Reverse();
        var bytes = CaseGuideDocxExporter.BuildKeyFacts(draft);

        var (errors, text) = OpenAndValidate(bytes);

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains("Key Facts & Events", text);
        Assert.Contains("Index by Issue", text);

        var table = Tables(bytes)[0];
        Assert.Equal(new[] { "F-001", "F-002", "F-003", "F-004", "F-005", "F-006" }, ColumnText(table, 0));
        Assert.Equal("January 15, 2024", ColumnText(table, 1)[0]);
        Assert.Equal("ISS-01", ColumnText(table, 3)[0]);
        Assert.Equal("Maria Delgado, Raymond Harmon", ColumnText(table, 4)[0]);
        Assert.Contains("Karen Walsh", ColumnText(table, 5)[0]);
        Assert.DoesNotContain("CH-0", string.Join(" ", ColumnText(table, 4)));

        var issue = draft.Guide.Issues[0];
        var indexStart = text.IndexOf("Index by Issue", StringComparison.Ordinal);
        var issueAt = text.IndexOf($"{issue.Id} {issue.Title}", indexStart, StringComparison.Ordinal);
        Assert.True(issueAt > indexStart);
        Assert.True(text.IndexOf("F-006", issueAt, StringComparison.Ordinal) > issueAt);
        Assert.True(HasLandscapeSection(bytes));
    }

    [Fact]
    public void KeyFacts_OrphanIdsRenderRaw()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.Facts[0].ActorIds = new List<string> { "CH-999" };
        draft.Guide.Facts[0].IssueIds = new List<string> { "ISS-77" };

        var (errors, text) = OpenAndValidate(CaseGuideDocxExporter.BuildKeyFacts(draft));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains("CH-999", text);
        Assert.Contains("ISS-77", text);
    }

    [Fact]
    public void EmptyGuide_StillValid()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = new CaseGuide();

        var (castErrors, castText) = OpenAndValidate(CaseGuideDocxExporter.BuildCast(draft));
        var (factErrors, factText) = OpenAndValidate(CaseGuideDocxExporter.BuildKeyFacts(draft));

        Assert.Empty(castErrors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Empty(factErrors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains("Organizations\n(none)", castText);
        Assert.Contains("Key Characters\n(none)", castText);
        Assert.Contains("Background Staff\n(none)", castText);
        Assert.Contains("(none)", factText);
    }

    [Fact]
    public void MissingParts_Throw()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = null;

        Assert.Throws<InvalidOperationException>(() => CaseGuideDocxExporter.BuildCast(draft));
        Assert.Throws<InvalidOperationException>(() => CaseGuideDocxExporter.BuildKeyFacts(draft));
        Assert.NotEmpty(CaseGuideDocxExporter.BuildCoreDocument(draft));
    }

    [Theory]
    [InlineData(CaseExportKind.CoreDocument, "COMPLAINT FOR BREACH")]
    [InlineData(CaseExportKind.Cast, "Character Profiles")]
    [InlineData(CaseExportKind.KeyFacts, "Index by Issue")]
    public void Build_DispatchesByKind(CaseExportKind kind, string marker)
    {
        var (errors, text) = OpenAndValidate(CaseGuideDocxExporter.Build(CaseTestData.SampleGuidedDraft(), kind));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains(marker, text);
    }

    [Theory]
    [InlineData("2024-03-03", "March 3, 2024")]
    [InlineData("bad", "bad")]
    [InlineData("", "")]
    public void LongDate_FormatsIsoDates(string input, string expected)
    {
        Assert.Equal(expected, CaseGuideDocxExporter.LongDate(input));
    }
}
