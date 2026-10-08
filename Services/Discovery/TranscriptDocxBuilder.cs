using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models.Discovery;
using static ReelDiscovery.Services.Case.CaseGuideDocxExporter;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Renders a laid-out transcript as .docx bytes (D-23). A sibling of CaseGuideDocxExporter.WordBuilder, which hard-codes
/// Calibri and auto spacing: here every page is one heading paragraph plus 25 numbered line paragraphs at exact 24 pt
/// spacing in Courier New 12 pt, with an explicit page break before every page after the first. Word line numbering
/// and PAGE fields are never used, so the page:line map from TranscriptLayout is exactly what Word shows.
/// </summary>
public static class TranscriptDocxBuilder
{
    private const string FontName = "Courier New";
    private const string LineStyleId = "TranscriptLine";
    private const string HeadingStyleId = "TranscriptHeading";

    // Exact 24 pt ("double-spaced" 12 pt) in twentieths of a point
    private const int LineTwips = 480;

    // Letter page in twips
    private const uint PageWidth = 12240;
    private const uint PageHeight = 15840;

    // Top/bottom 0.75", left 1.0", right 3/8" (federal format). Body height = 15840 - 2 x 1080 = 13680, and one page
    // needs 26 x 480 = 12480 (heading + 25 lines), leaving 1200 twips of slack so Word never breaks before line 25.
    private const int MarginTop = 1080;
    private const int MarginBottom = 1080;
    private const uint MarginLeft = 1440;
    private const uint MarginRight = 540;
    private const uint HeaderFooter = 540;

    // Line number right-aligned at 600; text starts at 1080 (1.75" from the page edge)
    private const int LineNumberTab = 600;
    private const int TextColumn = 1080;

    // Page heading: page number right-aligned at the right edge of the text area (12240 - 1440 - 540 - 540 = 9720)
    private const int HeadingRightTab = 9720;

    public static byte[] Build(DepositionContent content) =>
        Build(TranscriptLayout.Build(content), content?.DocumentTitle ?? string.Empty);

    public static byte[] Build(TranscriptDocument transcript, string title)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = BuildStyles();

            var body = new Body();
            var first = true;
            foreach (var page in transcript?.Pages ?? new List<TranscriptPage>())
            {
                body.Append(PageHeading(page.Number, page.Heading, first));
                first = false;
                for (var i = 0; i < page.Lines.Count; i++)
                    body.Append(TranscriptLine(i + 1, page.Lines[i]));
            }

            // Final section properties must be the last child of the body
            body.Append(SectionProps());
            main.Document = new Document(body);

            package.PackageProperties.Title = StripInvalidXmlChars(title);
            package.PackageProperties.Creator = "ReelDiscovery";
        }

        return stream.ToArray();
    }

    // CT_PPrBase order: pStyle, pageBreakBefore, tabs, spacing
    private static Paragraph PageHeading(int pageNumber, string heading, bool firstPage)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = HeadingStyleId });
        if (!firstPage)
            properties.Append(new PageBreakBefore());
        properties.Append(new Tabs(new TabStop { Val = TabStopValues.Right, Position = HeadingRightTab }));
        properties.Append(ExactSpacing());

        var run = new Run(
            new Text(StripInvalidXmlChars(heading)) { Space = SpaceProcessingModeValues.Preserve },
            new TabChar(),
            new Text(Number(pageNumber)));
        return new Paragraph(properties, run);
    }

    // CT_PPrBase order: pStyle, tabs, spacing, ind
    private static Paragraph TranscriptLine(int lineNumber, string text)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId { Val = LineStyleId },
            new Tabs(
                new TabStop { Val = TabStopValues.Right, Position = LineNumberTab },
                new TabStop { Val = TabStopValues.Left, Position = TextColumn }),
            ExactSpacing(),
            new Indentation { Left = Number(TextColumn), Hanging = Number(TextColumn) });

        var run = new Run(
            new TabChar(),
            new Text(Number(lineNumber)),
            new TabChar(),
            new Text(StripInvalidXmlChars(text)) { Space = SpaceProcessingModeValues.Preserve });
        return new Paragraph(properties, run);
    }

    private static SpacingBetweenLines ExactSpacing() => new()
    {
        Before = "0",
        After = "0",
        Line = Number(LineTwips),
        LineRule = LineSpacingRuleValues.Exact
    };

    // CT_SectPr order: pgSz, pgMar; no lnNumType (D-23)
    private static SectionProperties SectionProps() => new(
        new PageSize { Width = PageWidth, Height = PageHeight },
        new PageMargin
        {
            Top = MarginTop,
            Right = MarginRight,
            Bottom = MarginBottom,
            Left = MarginLeft,
            Header = HeaderFooter,
            Footer = HeaderFooter,
            Gutter = 0U
        });

    private static Styles BuildStyles()
    {
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    CourierNew(),
                    new FontSize { Val = "24" },
                    new FontSizeComplexScript { Val = "24" },
                    new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(ExactSpacing()))));

        styles.Append(ParagraphStyle(LineStyleId, "Transcript Line", isDefault: true));
        styles.Append(ParagraphStyle(HeadingStyleId, "Transcript Heading", isDefault: false));
        return styles;
    }

    private static Style ParagraphStyle(string id, string name, bool isDefault)
    {
        // CT_Style order: name, basedOn, next, uiPriority, qFormat, pPr, rPr
        var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
        if (isDefault)
            style.Default = true;
        style.Append(new StyleName { Val = name });
        if (!isDefault)
            style.Append(new BasedOn { Val = LineStyleId });
        style.Append(new NextParagraphStyle { Val = LineStyleId });
        style.Append(new PrimaryStyle());
        style.Append(new StyleParagraphProperties(ExactSpacing()));
        style.Append(new StyleRunProperties(
            CourierNew(),
            new FontSize { Val = "24" },
            new FontSizeComplexScript { Val = "24" }));
        return style;
    }

    private static RunFonts CourierNew() =>
        new() { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName, EastAsia = FontName };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
