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

    [Theory]
    [InlineData("2024-03-03", "March 3, 2024")]
    [InlineData("bad", "bad")]
    [InlineData("", "")]
    public void LongDate_FormatsIsoDates(string input, string expected)
    {
        Assert.Equal(expected, CaseGuideDocxExporter.LongDate(input));
    }
}
