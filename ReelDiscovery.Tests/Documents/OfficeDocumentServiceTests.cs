using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using ReelDiscovery.Models;
using ReelDiscovery.Services;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ReelDiscovery.Tests.Documents;

public class OfficeDocumentServiceTests
{
    private readonly OfficeDocumentService _office = new();

    private static readonly DocumentMetadata Meta = new()
    {
        Author = "Michael Scott",
        Created = new DateTimeOffset(2024, 3, 4, 9, 30, 0, TimeSpan.FromHours(-5)),
        Modified = new DateTimeOffset(2024, 3, 4, 11, 15, 0, TimeSpan.FromHours(-5))
    };

    private static OrganizationTheme Theme() => new()
    {
        OrganizationName = "Dunder Mifflin",
        PrimaryColor = "1F3A5F",
        SecondaryColor = "4A6FA5",
        AccentColor = "C9A227",
        HeadingFont = "Georgia",
        BodyFont = "Arial"
    };

    private static List<WordSection> Sections() => new()
    {
        new WordSection { Heading = "Background", Paragraphs = new List<string> { "The branch ordered paper in bulk.", "Deliveries were late." } },
        new WordSection { Heading = "Findings", Paragraphs = new List<string> { "Invoices did not match purchase orders." } },
        new WordSection { Heading = "Next Steps", Paragraphs = new List<string> { "Reconcile the ledger by Friday." } }
    };

    private static SimpleTable Table() => new()
    {
        Headers = new List<string> { "Item", "Qty", "Unit", "Cost" },
        Rows = new List<List<string>>
        {
            new() { "Copy paper", "40", "case", "$1,240.00" },
            new() { "Toner", "6", "box", "$480.00" },
            new() { "Envelopes", "12", "box", "$96.00" }
        }
    };

    private byte[] BuildWord() => _office.CreateWordDocument("Supply Review", Sections(), Table(), Theme(), Meta);

    private static List<SlideContent> Slides() => new()
    {
        new SlideContent { Title = "Agenda", Bullets = new List<string> { "Q1 results", "Open issues" } },
        new SlideContent { Title = "Q1 Results", Bullets = new List<string> { "Sales up 4%", "Returns flat" } },
        new SlideContent { Title = "Open Issues", Bullets = new List<string> { "Late deliveries" } },
        new SlideContent { Title = "Next Steps", Bullets = new List<string> { "Vendor review", "Budget sign-off" } }
    };

    private byte[] BuildDeck() => _office.CreatePowerPointDocument("Quarterly Review", "Dunder Mifflin | March 2024", Slides(), Theme(), Meta);

    private byte[] BuildWorkbook(List<List<string>> rows) =>
        _office.CreateExcelDocument("Cost Summary", "Costs", new List<string> { "Label", "Value" }, rows, Theme(), Meta);

    [Fact]
    public void Word_ValidatesAndContainsAllText()
    {
        var bytes = BuildWord();
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);

        var errors = new OpenXmlValidator().Validate(doc).ToList();
        Assert.True(errors.Count == 0, string.Join(" || ", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));

        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Dunder Mifflin", text);
        Assert.Contains("Supply Review", text);
        foreach (var section in Sections())
        {
            Assert.Contains(section.Heading, text);
            foreach (var p in section.Paragraphs)
                Assert.Contains(p, text);
        }
        foreach (var h in Table().Headers)
            Assert.Contains(h, text);
        foreach (var cell in Table().Rows.SelectMany(r => r))
            Assert.Contains(cell, text);
        Assert.Single(doc.MainDocumentPart.Document.Body.Descendants<W.Table>());
    }

    [Fact]
    public void Word_HeadingRunPropertiesInSchemaOrder()
    {
        var bytes = BuildWord();
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);

        var headingRun = doc.MainDocumentPart!.Document!.Body!.Descendants<W.Run>()
            .First(r => r.InnerText == "Background");
        var names = headingRun.RunProperties!.ChildElements.Select(e => e.LocalName).ToList();

        Assert.Equal(new[] { "rFonts", "b", "color", "sz" }, names);
    }

    [Fact]
    public void Excel_AmountsAndPlainNumbersAreNumeric()
    {
        var bytes = BuildWorkbook(new List<List<string>>
        {
            new() { "Line item", "$412,500" },
            new() { "Total", "1250000" },
            new() { "Note", "n/a" }
        });

        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();
        Assert.True(errors.Count == 0, string.Join(" || ", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));

        var cells = CellsByRef(doc);
        Assert.Equal(CellValues.Number, cells["B2"].DataType!.Value);
        Assert.Equal("412500", cells["B2"].CellValue!.Text);
        Assert.Equal(CellValues.Number, cells["B3"].DataType!.Value);
        Assert.Equal("1250000", cells["B3"].CellValue!.Text);
        Assert.Equal(CellValues.String, cells["B4"].DataType!.Value);
        Assert.Equal("n/a", cells["B4"].CellValue!.Text);

        // Currency style on the "$" cell
        var stylesheet = doc.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
        var formats = stylesheet.CellFormats!.Elements<CellFormat>().ToList();
        var styleIndex = (int)cells["B2"].StyleIndex!.Value;
        Assert.Equal(164u, formats[styleIndex].NumberFormatId!.Value);
        Assert.Contains(stylesheet.NumberingFormats!.Elements<NumberingFormat>(), n => n.NumberFormatId!.Value == 164 && n.FormatCode!.Value!.Contains("#,##0.00"));

        // Header row is bold-styled
        var headerStyle = (int)cells["A1"].StyleIndex!.Value;
        var font = stylesheet.Fonts!.Elements<Font>().ElementAt((int)formats[headerStyle].FontId!.Value);
        Assert.NotNull(font.Bold);
    }

    [Theory]
    [InlineData("007")]
    [InlineData("00123")]
    [InlineData("02134")]
    [InlineData("CO-007")]
    [InlineData("1-800-555-0100")]
    public void Excel_IdentifiersStayText(string value)
    {
        var bytes = BuildWorkbook(new List<List<string>> { new() { "Ref", value } });
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);

        var cell = CellsByRef(doc)["B2"];
        Assert.Equal(CellValues.String, cell.DataType!.Value);
        Assert.Equal(value, cell.CellValue!.Text);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("0.5", "0.5")]
    [InlineData("12.75", "12.75")]
    public void Excel_PlainDecimalsAreNumbers(string value, string expected)
    {
        var bytes = BuildWorkbook(new List<List<string>> { new() { "Qty", value } });
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);

        var cell = CellsByRef(doc)["B2"];
        Assert.Equal(CellValues.Number, cell.DataType!.Value);
        Assert.Equal(expected, cell.CellValue!.Text);
    }

    [Fact]
    public void Excel_SheetNameIsSanitized()
    {
        var bytes = _office.CreateExcelDocument("Title", "Q1: Costs/Backup [draft]?", new List<string> { "A" }, new List<List<string>>(), Theme(), Meta);
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);

        var sheet = doc.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Single();
        Assert.Equal("Q1 Costs Backup draft", sheet.Name!.Value);
    }

    [Theory]
    [InlineData("Q1: Costs/Backup [draft]?", "Q1 Costs Backup draft")]
    [InlineData("'quoted'", "quoted")]
    [InlineData("", "Sheet1")]
    [InlineData(null, "Sheet1")]
    [InlineData(":\\/?*[]", "Sheet1")]
    [InlineData("''' '''", "Sheet1")]
    public void SafeSheetName_Cases(string? input, string expected)
    {
        Assert.Equal(expected, OfficeDocumentService.SafeSheetName(input));
    }

    [Fact]
    public void SafeSheetName_CutsTo31()
    {
        var name = new string('a', 40);
        Assert.Equal(new string('a', 31), OfficeDocumentService.SafeSheetName(name));
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz abc d efgh")] // 31st char is a space
    [InlineData("abcdefghijklmnopqrstuvwxyzabcd'efgh")]   // 31st char is an apostrophe
    public void SafeSheetName_TrimsAfterCut(string input)
    {
        var result = OfficeDocumentService.SafeSheetName(input);
        Assert.True(result.Length <= 31);
        Assert.False(result.EndsWith(' '));
        Assert.False(result.EndsWith('\''));
        Assert.NotEmpty(result);
    }

    [Fact]
    public void PowerPoint_ValidatesWithSubtitleAndNoGeneratorLabel()
    {
        var bytes = BuildDeck();
        using var doc = PresentationDocument.Open(new MemoryStream(bytes), false);

        var errors = new OpenXmlValidator().Validate(doc).ToList();
        Assert.True(errors.Count == 0, string.Join(" || ", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));

        var slideParts = doc.PresentationPart!.SlideParts.ToList();
        Assert.Equal(5, slideParts.Count);

        var texts = slideParts.SelectMany(s => s.Slide!.Descendants<DocumentFormat.OpenXml.Drawing.Text>()).Select(t => t.Text).ToList();
        Assert.DoesNotContain("Generated Email Dataset", texts);
        Assert.Contains("Dunder Mifflin | March 2024", texts);
        Assert.Contains("Quarterly Review", texts);
        Assert.Contains("Late deliveries", texts);
    }

    [Fact]
    public void AllOverloads_SetPackageMetadata()
    {
        AssertMeta(WordprocessingDocument.Open(new MemoryStream(BuildWord()), false));
        AssertMeta(SpreadsheetDocument.Open(new MemoryStream(BuildWorkbook(new List<List<string>> { new() { "a", "1" } })), false));
        AssertMeta(PresentationDocument.Open(new MemoryStream(BuildDeck()), false));
    }

    [Fact]
    public void AllOverloads_NeverMentionReelDiscovery()
    {
        foreach (var bytes in new[] { BuildWord(), BuildWorkbook(new List<List<string>> { new() { "a", "1" } }), BuildDeck() })
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var content = reader.ReadToEnd();
                Assert.DoesNotContain("ReelDiscovery", content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static void AssertMeta(OpenXmlPackage package)
    {
        using (package)
        {
            var props = package.PackageProperties;
            Assert.Equal("Michael Scott", props.Creator);
            Assert.Equal("Michael Scott", props.LastModifiedBy);
            Assert.Equal(Meta.Created.UtcDateTime, props.Created!.Value.ToUniversalTime());
            Assert.Equal(Meta.Modified.UtcDateTime, props.Modified!.Value.ToUniversalTime());
        }
    }

    private static Dictionary<string, Cell> CellsByRef(SpreadsheetDocument doc)
    {
        var sheetPart = doc.WorkbookPart!.WorksheetParts.Single();
        return sheetPart.Worksheet!.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!);
    }
}
