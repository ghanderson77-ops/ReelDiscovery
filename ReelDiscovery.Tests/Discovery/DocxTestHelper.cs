using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// Shared .docx open/validate helper for every Phase 8 document test.
/// </summary>
internal static class DocxTestHelper
{
    public static (List<ValidationErrorInfo> Errors, List<string> Paragraphs) OpenAndValidate(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(ParagraphText).ToList();
        return (errors, paragraphs);
    }

    // Text with Break/TabChar elements rendered as newlines/tabs
    public static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants().Select(e => e switch
        {
            Text t => t.Text,
            Break => "\n",
            TabChar => "\t",
            _ => string.Empty
        }));

    public static string ErrorsText(IEnumerable<ValidationErrorInfo> errors) =>
        string.Join("\n", errors.Select(e => e.Description + " @ " + e.Path?.XPath));
}
