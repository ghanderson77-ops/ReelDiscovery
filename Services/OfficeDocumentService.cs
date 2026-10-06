using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services;

public partial class OfficeDocumentService
{
    public byte[] CreateWordDocument(string title, string content, OrganizationTheme? theme = null)
    {
        var t = theme ?? OrganizationTheme.Default;

        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Add page margins
            var sectionProps = new SectionProperties(
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }
            );

            // Add header with organization branding (colored bar with org name)
            if (!string.IsNullOrEmpty(t.OrganizationName))
            {
                var headerPara = body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Fill = t.PrimaryColor },
                        new SpacingBetweenLines { After = "200" },
                        new Indentation { Left = "-115", Right = "-115" }
                    )
                ));
                var headerRun = headerPara.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                        new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "24" },
                        new DocumentFormat.OpenXml.Wordprocessing.Color { Val = t.TextLight },
                        new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = t.HeadingFont, HighAnsi = t.HeadingFont }
                    ),
                    new DocumentFormat.OpenXml.Wordprocessing.Text($"  {t.OrganizationName}")
                ));

                // Add accent line under header
                var accentPara = body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Fill = t.AccentColor },
                        new SpacingBetweenLines { After = "400", Line = "60", LineRule = LineSpacingRuleValues.Exact },
                        new Indentation { Left = "-115", Right = "-115" }
                    )
                ));
            }

            // Add document title with themed color
            var titlePara = body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { After = "200" }
                )
            ));
            var titleRun = titlePara.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run());
            titleRun.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "36" },
                new DocumentFormat.OpenXml.Wordprocessing.Color { Val = t.PrimaryColor },
                new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = t.HeadingFont, HighAnsi = t.HeadingFont }
            ));
            titleRun.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(title));

            // Add subtle divider line
            var dividerPara = body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new ParagraphBorders(
                        new DocumentFormat.OpenXml.Wordprocessing.BottomBorder { Val = BorderValues.Single, Size = 6, Color = t.SecondaryColor }
                    ),
                    new SpacingBetweenLines { After = "300" }
                )
            ));

            // Add content paragraphs with better typography
            var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var para in paragraphs)
            {
                var p = body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines { After = "200", Line = "276", LineRule = LineSpacingRuleValues.Auto }
                    )
                ));
                var run = p.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "22" },
                        new DocumentFormat.OpenXml.Wordprocessing.Color { Val = t.TextDark },
                        new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = t.BodyFont, HighAnsi = t.BodyFont }
                    )
                ));

                // Handle line breaks within paragraphs
                var lines = para.Trim().Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
                    if (i < lines.Length - 1)
                    {
                        run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Break());
                    }
                }
            }

            body.AppendChild(sectionProps);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public byte[] CreateExcelDocument(string title, List<string> headers, List<List<string>> rows)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = title.Length > 31 ? title[..31] : title
            };
            sheets.Append(sheet);

            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;

            // Add header row
            var headerRow = new Row { RowIndex = 1 };
            for (int i = 0; i < headers.Count; i++)
            {
                headerRow.Append(CreateCell(GetColumnName(i + 1), 1, headers[i]));
            }
            sheetData.Append(headerRow);

            // Add data rows
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var dataRow = new Row { RowIndex = (uint)(rowIndex + 2) };
                for (int colIndex = 0; colIndex < rows[rowIndex].Count; colIndex++)
                {
                    dataRow.Append(CreateCell(GetColumnName(colIndex + 1), (uint)(rowIndex + 2), rows[rowIndex][colIndex]));
                }
                sheetData.Append(dataRow);
            }

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    public byte[] CreatePowerPointDocument(string title, List<(string slideTitle, string content)> slides, OrganizationTheme? theme = null)
    {
        // Use provided theme or default colors
        var t = theme ?? OrganizationTheme.Default;
        return BuildPresentation(title, "Generated Email Dataset", slides, false, t, null);
    }

    private static byte[] BuildPresentation(
        string title,
        string subtitle,
        IEnumerable<(string slideTitle, string content)> slides,
        bool bullets,
        OrganizationTheme t,
        DocumentMetadata? meta)
    {
        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            // Create presentation part with proper structure
            var presentationPart = doc.AddPresentationPart();

            // Create slide master part first
            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();

            // Create theme part (required) - link to presentation AND slide master
            var themePart = presentationPart.AddNewPart<ThemePart>();
            themePart.Theme = CreateModernTheme(t);
            themePart.Theme.Save();

            // Also add theme to slide master
            slideMasterPart.AddPart(themePart);

            // Create slide layout part
            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
            slideLayoutPart.SlideLayout = CreateSlideLayout();
            slideLayoutPart.SlideLayout.Save();

            // Create slide master
            slideMasterPart.SlideMaster = CreateSlideMaster(slideMasterPart.GetIdOfPart(slideLayoutPart));
            slideMasterPart.SlideMaster.Save();

            // Build the presentation with all required elements in correct order
            var presentation = new P.Presentation();
            presentation.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
            presentation.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            presentation.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");

            // Add slide master id list
            presentation.AppendChild(new P.SlideMasterIdList(
                new P.SlideMasterId { Id = 2147483648, RelationshipId = presentationPart.GetIdOfPart(slideMasterPart) }
            ));

            // Add slide id list
            var slideIdList = presentation.AppendChild(new P.SlideIdList());

            // Add slide size and notes size
            presentation.AppendChild(new P.SlideSize { Cx = 9144000, Cy = 6858000, Type = P.SlideSizeValues.Screen4x3 });
            presentation.AppendChild(new P.NotesSize { Cx = 6858000, Cy = 9144000 });

            // Add default text style
            presentation.AppendChild(new P.DefaultTextStyle());

            presentationPart.Presentation = presentation;

            uint slideId = 256;

            // Create title slide (special formatting)
            CreateTitleSlide(presentationPart, slideLayoutPart, slideIdList, slideId++, title, subtitle, t);

            // Create content slides
            foreach (var (slideTitle, content) in slides)
            {
                CreateContentSlide(presentationPart, slideLayoutPart, slideIdList, slideId++, slideTitle, content, t, bullets);
            }

            presentationPart.Presentation.Save();

            if (meta != null)
                ApplyMetadata(doc, meta);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Branded Word attachment (D-19): organization header, title, headed sections and at most one simple table.
    /// Validator-clean; the legacy overload keeps its pinned output.
    /// </summary>
    public byte[] CreateWordDocument(string title, IReadOnlyList<WordSection> sections, SimpleTable? table, OrganizationTheme theme, DocumentMetadata meta)
    {
        var t = theme ?? OrganizationTheme.Default;

        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new W.Document();
            var body = mainPart.Document.AppendChild(new W.Body());

            // Organization header bar plus accent line (same visual as the legacy header)
            if (!string.IsNullOrWhiteSpace(t.OrganizationName))
            {
                body.AppendChild(new W.Paragraph(
                    new W.ParagraphProperties(
                        new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = t.PrimaryColor },
                        new W.SpacingBetweenLines { After = "200" },
                        new W.Indentation { Left = "-115", Right = "-115" }),
                    new W.Run(
                        WordRunProperties(t.HeadingFont, true, t.TextLight, "24"),
                        new W.Text($"  {Clean(t.OrganizationName)}") { Space = SpaceProcessingModeValues.Preserve })));

                body.AppendChild(new W.Paragraph(
                    new W.ParagraphProperties(
                        new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = t.AccentColor },
                        new W.SpacingBetweenLines { After = "400", Line = "60", LineRule = W.LineSpacingRuleValues.Exact },
                        new W.Indentation { Left = "-115", Right = "-115" })));
            }

            // Title
            body.AppendChild(new W.Paragraph(
                new W.ParagraphProperties(new W.SpacingBetweenLines { After = "200" }),
                new W.Run(WordRunProperties(t.HeadingFont, true, t.PrimaryColor, "36"), new W.Text(Clean(title)))));

            // Divider
            body.AppendChild(new W.Paragraph(
                new W.ParagraphProperties(
                    new W.ParagraphBorders(new W.BottomBorder { Val = W.BorderValues.Single, Size = 6, Color = t.SecondaryColor }),
                    new W.SpacingBetweenLines { After = "300" })));

            foreach (var section in sections ?? Array.Empty<WordSection>())
            {
                if (section == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(section.Heading))
                {
                    body.AppendChild(new W.Paragraph(
                        new W.ParagraphProperties(new W.KeepNext(), new W.SpacingBetweenLines { Before = "240", After = "120" }),
                        new W.Run(WordRunProperties(t.HeadingFont, true, t.PrimaryColor, "26"), new W.Text(Clean(section.Heading)))));
                }

                foreach (var paragraph in section.Paragraphs ?? new List<string>())
                    AppendBodyParagraph(body, paragraph, t);
            }

            if (table?.Headers.Count > 0)
                body.AppendChild(BuildWordTable(table, t));

            body.AppendChild(new W.SectionProperties(
                new W.PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }));
            mainPart.Document.Save();

            ApplyMetadata(doc, meta);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// One-sheet workbook (D-19) with a bold theme-colored header row and real numeric cells.
    /// A cell is numeric only for a "$" amount or a plain decimal without a leading zero.
    /// </summary>
    public byte[] CreateExcelDocument(string title, string sheetName, List<string> headers, List<List<string>> rows, OrganizationTheme theme, DocumentMetadata meta)
    {
        var t = theme ?? OrganizationTheme.Default;
        headers ??= new List<string>();
        rows ??= new List<List<string>>();

        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet(t);
            stylesPart.Stylesheet.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var worksheet = new S.Worksheet();
            var columnCount = Math.Max(headers.Count, rows.Count == 0 ? 0 : rows.Max(r => r?.Count ?? 0));
            if (columnCount > 0)
                worksheet.AppendChild(new S.Columns(new S.Column { Min = 1, Max = (uint)columnCount, Width = 20, CustomWidth = true }));
            var sheetData = worksheet.AppendChild(new S.SheetData());
            worksheetPart.Worksheet = worksheet;

            var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            sheets.Append(new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = SafeSheetName(string.IsNullOrWhiteSpace(sheetName) ? title : sheetName)
            });

            var headerRow = new S.Row { RowIndex = 1 };
            for (int i = 0; i < headers.Count; i++)
            {
                headerRow.Append(new S.Cell
                {
                    CellReference = $"{GetColumnName(i + 1)}1",
                    DataType = S.CellValues.String,
                    CellValue = new S.CellValue(Clean(headers[i])),
                    StyleIndex = HeaderStyleIndex
                });
            }
            sheetData.Append(headerRow);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var rowNumber = (uint)(rowIndex + 2);
                var dataRow = new S.Row { RowIndex = rowNumber };
                var values = rows[rowIndex] ?? new List<string>();
                for (int colIndex = 0; colIndex < values.Count; colIndex++)
                    dataRow.Append(CreateTypedCell($"{GetColumnName(colIndex + 1)}{rowNumber}", values[colIndex]));
                sheetData.Append(dataRow);
            }

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();

            ApplyMetadata(doc, meta);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Branded deck (D-19): title slide with the caller's subtitle (e.g. "{Org} | {Month yyyy}"), then one bullet slide per entry.
    /// </summary>
    public byte[] CreatePowerPointDocument(string title, string subtitle, IReadOnlyList<SlideContent> slides, OrganizationTheme theme, DocumentMetadata meta)
    {
        var t = theme ?? OrganizationTheme.Default;
        var content = (slides ?? Array.Empty<SlideContent>())
            .Where(s => s != null)
            .Select(s => (Clean(s.Title), string.Join("\n", (s.Bullets ?? new List<string>()).Select(b => Clean(b).Replace("\n", " ").Trim()).Where(b => b.Length > 0))))
            .ToList();

        return BuildPresentation(Clean(title), Clean(subtitle), content, true, t, meta);
    }

    /// <summary>
    /// Excel-safe sheet name: no : \ / ? * [ ], collapsed whitespace, no leading/trailing space or apostrophe, at most 31 chars.
    /// </summary>
    internal static string SafeSheetName(string? name)
    {
        var cleaned = SheetNameForbidden().Replace(name ?? string.Empty, " ");
        cleaned = Whitespace().Replace(cleaned, " ").Trim(' ', '\'');
        if (cleaned.Length > 31)
            cleaned = cleaned[..31];

        // The cut can expose a trailing space or apostrophe
        cleaned = cleaned.Trim(' ', '\'');
        return cleaned.Length == 0 ? "Sheet1" : cleaned;
    }

    private const uint HeaderStyleIndex = 1;
    private const uint CurrencyStyleIndex = 2;
    private const uint CurrencyFormatId = 164;

    private static S.Cell CreateTypedCell(string reference, string? raw)
    {
        var text = Clean(raw);
        var trimmed = text.Trim();
        var cell = new S.Cell { CellReference = reference };

        if (trimmed.StartsWith('$') && CanonicalValueScanner.TryParseAmount(trimmed, out var amount))
        {
            cell.DataType = S.CellValues.Number;
            cell.CellValue = new S.CellValue(amount);
            cell.StyleIndex = CurrencyStyleIndex;
        }
        // No leading zeros: identifiers and ZIP-like codes stay text (review 06-03)
        else if (PlainNumber().IsMatch(trimmed)
            && decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            cell.DataType = S.CellValues.Number;
            cell.CellValue = new S.CellValue(number);
        }
        else
        {
            cell.DataType = S.CellValues.String;
            cell.CellValue = new S.CellValue(text);
        }

        return cell;
    }

    private static S.Stylesheet BuildStylesheet(OrganizationTheme t)
    {
        // Schema order: numFmts, fonts, fills, borders, cellStyleXfs, cellXfs
        return new S.Stylesheet(
            new S.NumberingFormats(
                new S.NumberingFormat { NumberFormatId = CurrencyFormatId, FormatCode = "\"$\"#,##0.00" }) { Count = 1 },
            new S.Fonts(
                new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" }),
                new S.Font(new S.Bold(), new S.FontSize { Val = 11 }, new S.Color { Rgb = "FF" + t.TextLight }, new S.FontName { Val = "Calibri" })) { Count = 2 },
            new S.Fills(
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }),
                new S.Fill(new S.PatternFill(
                    new S.ForegroundColor { Rgb = "FF" + t.PrimaryColor },
                    new S.BackgroundColor { Indexed = 64 }) { PatternType = S.PatternValues.Solid })) { Count = 3 },
            new S.Borders(
                new S.Border(new S.LeftBorder(), new S.RightBorder(), new S.TopBorder(), new S.BottomBorder(), new S.DiagonalBorder())) { Count = 1 },
            new S.CellStyleFormats(
                new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new S.CellFormats(
                new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new S.CellFormat { NumberFormatId = 0, FontId = 1, FillId = 2, BorderId = 0, FormatId = 0, ApplyFont = true, ApplyFill = true },
                new S.CellFormat { NumberFormatId = CurrencyFormatId, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true }) { Count = 3 });
    }

    // Schema order RunFonts, Bold, Color, FontSize; the legacy path keeps its pinned order for DocumentRegressionTests
    private static W.RunProperties WordRunProperties(string font, bool bold, string color, string size)
    {
        var props = new W.RunProperties(new W.RunFonts { Ascii = font, HighAnsi = font });
        if (bold)
            props.Append(new W.Bold());
        props.Append(new W.Color { Val = color }, new W.FontSize { Val = size });
        return props;
    }

    private static void AppendBodyParagraph(W.Body body, string? paragraph, OrganizationTheme t)
    {
        var text = Clean(paragraph).Trim();
        if (text.Length == 0)
            return;

        var run = new W.Run(WordRunProperties(t.BodyFont, false, t.TextDark, "22"));
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            run.AppendChild(new W.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            if (i < lines.Length - 1)
                run.AppendChild(new W.Break());
        }

        body.AppendChild(new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "200", Line = "276", LineRule = W.LineSpacingRuleValues.Auto }),
            run));
    }

    private static W.Table BuildWordTable(SimpleTable table, OrganizationTheme t)
    {
        var columnCount = Math.Max(table.Headers.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r?.Count ?? 0));
        const int tableWidthTwips = 9360; // 6.5in text width at 1in margins
        var columnWidth = (tableWidthTwips / columnCount).ToString(CultureInfo.InvariantCulture);

        var wordTable = new W.Table(
            new W.TableProperties(
                new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
                new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = t.SecondaryColor },
                    new W.LeftBorder { Val = W.BorderValues.Single, Size = 4, Color = t.SecondaryColor },
                    new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = t.SecondaryColor },
                    new W.RightBorder { Val = W.BorderValues.Single, Size = 4, Color = t.SecondaryColor },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = t.SecondaryColor },
                    new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4, Color = t.SecondaryColor })),
            new W.TableGrid(Enumerable.Range(0, columnCount).Select(_ => new W.GridColumn { Width = columnWidth })));

        wordTable.Append(BuildWordTableRow(table.Headers, columnCount, columnWidth, t, header: true));
        foreach (var row in table.Rows)
            wordTable.Append(BuildWordTableRow(row ?? new List<string>(), columnCount, columnWidth, t, header: false));

        return wordTable;
    }

    private static W.TableRow BuildWordTableRow(List<string> values, int columnCount, string columnWidth, OrganizationTheme t, bool header)
    {
        var row = new W.TableRow();
        for (int i = 0; i < columnCount; i++)
        {
            var value = i < values.Count ? Clean(values[i]).Replace("\n", " ").Trim() : string.Empty;
            var cellProps = new W.TableCellProperties(new W.TableCellWidth { Width = columnWidth, Type = W.TableWidthUnitValues.Dxa });
            if (header)
                cellProps.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = t.PrimaryColor });

            row.Append(new W.TableCell(
                cellProps,
                new W.Paragraph(
                    new W.ParagraphProperties(new W.SpacingBetweenLines { Before = "40", After = "40" }),
                    new W.Run(
                        WordRunProperties(header ? t.HeadingFont : t.BodyFont, header, header ? t.TextLight : t.TextDark, "20"),
                        new W.Text(value) { Space = SpaceProcessingModeValues.Preserve }))));
        }

        return row;
    }

    // Sender as author, dates just before the email; no Application/Company part is ever written (T-06-09)
    private static void ApplyMetadata(OpenXmlPackage doc, DocumentMetadata? meta)
    {
        if (meta == null)
            return;

        doc.PackageProperties.Creator = meta.Author ?? string.Empty;
        doc.PackageProperties.LastModifiedBy = meta.Author ?? string.Empty;
        doc.PackageProperties.Created = meta.Created.UtcDateTime;
        doc.PackageProperties.Modified = meta.Modified.UtcDateTime;
    }

    // Model text is untrusted: drop control characters and unpaired surrogates XML cannot carry, keep line breaks and tabs
    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var sb = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (char.IsHighSurrogate(c))
            {
                // Keep a full pair only; a lone surrogate makes the OpenXml writer throw (IN-01)
                if (i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1]))
                {
                    sb.Append(c).Append(normalized[i + 1]);
                    i++;
                }
                continue;
            }

            if (char.IsLowSurrogate(c))
                continue;

            if (c == '\n' || c == '\t' || !char.IsControl(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[:\\/?*\[\]]")]
    private static partial Regex SheetNameForbidden();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^-?(0|[1-9]\d{0,14})(\.\d+)?$")]
    private static partial Regex PlainNumber();

    private static A.Theme CreateModernTheme(OrganizationTheme t)
    {
        return new A.Theme(
            new A.ThemeElements(
                new A.ColorScheme(
                    new A.Dark1Color(new A.RgbColorModelHex { Val = t.TextDark }),
                    new A.Light1Color(new A.RgbColorModelHex { Val = t.TextLight }),
                    new A.Dark2Color(new A.RgbColorModelHex { Val = t.PrimaryColor }),
                    new A.Light2Color(new A.RgbColorModelHex { Val = t.BackgroundLight }),
                    new A.Accent1Color(new A.RgbColorModelHex { Val = t.SecondaryColor }),
                    new A.Accent2Color(new A.RgbColorModelHex { Val = t.AccentColor }),
                    new A.Accent3Color(new A.RgbColorModelHex { Val = "70AD47" }),  // Green
                    new A.Accent4Color(new A.RgbColorModelHex { Val = "7030A0" }),  // Purple
                    new A.Accent5Color(new A.RgbColorModelHex { Val = "00B0F0" }),  // Cyan
                    new A.Accent6Color(new A.RgbColorModelHex { Val = "FFC000" }),  // Gold
                    new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }),
                    new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" })
                ) { Name = t.ThemeName },
                new A.FontScheme(
                    new A.MajorFont(
                        new A.LatinFont { Typeface = t.HeadingFont },
                        new A.EastAsianFont { Typeface = "" },
                        new A.ComplexScriptFont { Typeface = "" }
                    ),
                    new A.MinorFont(
                        new A.LatinFont { Typeface = t.BodyFont },
                        new A.EastAsianFont { Typeface = "" },
                        new A.ComplexScriptFont { Typeface = "" }
                    )
                ) { Name = t.ThemeName },
                new A.FormatScheme(
                    new A.FillStyleList(
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })
                    ),
                    new A.LineStyleList(
                        new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 9525 },
                        new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 25400 },
                        new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 38100 }
                    ),
                    new A.EffectStyleList(
                        new A.EffectStyle(new A.EffectList()),
                        new A.EffectStyle(new A.EffectList()),
                        new A.EffectStyle(new A.EffectList())
                    ),
                    new A.BackgroundFillStyleList(
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })
                    )
                ) { Name = t.ThemeName }
            )
        ) { Name = $"{t.ThemeName} Theme" };
    }

    private static Cell CreateCell(string columnName, uint rowIndex, string value)
    {
        return new Cell
        {
            CellReference = $"{columnName}{rowIndex}",
            CellValue = new CellValue(value),
            DataType = CellValues.String
        };
    }

    private static string GetColumnName(int columnNumber)
    {
        string columnName = string.Empty;
        while (columnNumber > 0)
        {
            int modulo = (columnNumber - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            columnNumber = (columnNumber - modulo) / 26;
        }
        return columnName;
    }

    private static P.SlideMaster CreateSlideMaster(string slideLayoutRelId)
    {
        return new P.SlideMaster(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()))),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId { Id = 2147483649, RelationshipId = slideLayoutRelId }
            ));
    }

    private static P.SlideLayout CreateSlideLayout()
    {
        return new P.SlideLayout(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()))))
        { Type = P.SlideLayoutValues.Title };
    }

    private static void CreateTitleSlide(
        PresentationPart presentationPart,
        SlideLayoutPart slideLayoutPart,
        P.SlideIdList slideIdList,
        uint slideId,
        string title,
        string subtitle,
        OrganizationTheme t)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();

        // Title slide: Full themed background with centered white text
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.Background(
                    new P.BackgroundProperties(
                        new A.SolidFill(new A.RgbColorModelHex { Val = t.PrimaryColor }))),
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    // Main title - centered, large, white
                    CreateStyledTextShape(2, "Title", title, 457200, 2200000, 8229600, 1500000, 4800, t.TextLight, true, t.HeadingFont),
                    // Subtitle - centered, smaller, light gray
                    CreateStyledTextShape(3, "Subtitle", subtitle, 457200, 3800000, 8229600, 600000, 2400, "E0E0E0", false, t.BodyFont))),
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slidePart.AddPart(slideLayoutPart);
        slidePart.Slide.Save();

        slideIdList.Append(new P.SlideId
        {
            Id = slideId,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static void CreateContentSlide(
        PresentationPart presentationPart,
        SlideLayoutPart slideLayoutPart,
        P.SlideIdList slideIdList,
        uint slideId,
        string title,
        string content,
        OrganizationTheme t,
        bool bullets = false)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();

        // Content slide: Themed header bar at top, white background for content
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    // Header bar (themed rectangle at top)
                    CreateHeaderBar(2, 0, 0, 9144000, 1000000, t.PrimaryColor),
                    // Title text on the header bar (white)
                    CreateStyledTextShape(3, "Title", title, 300000, 250000, 8544000, 600000, 3200, t.TextLight, true, t.HeadingFont),
                    // Accent line under header
                    CreateAccentLine(4, 0, 1000000, 9144000, 40000, t.AccentColor),
                    // Content area (dark text on white background)
                    CreateContentTextShape(5, "Content", content, 300000, 1200000, 8544000, 5400000, 1800, t.TextDark, t.BodyFont, bullets))),
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slidePart.AddPart(slideLayoutPart);
        slidePart.Slide.Save();

        slideIdList.Append(new P.SlideId
        {
            Id = slideId,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static P.Shape CreateHeaderBar(uint id, long x, long y, long width, long height, string colorHex)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Header Bar" },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = colorHex }),
                new A.Outline(new A.NoFill())));
    }

    private static P.Shape CreateAccentLine(uint id, long x, long y, long width, long height, string colorHex)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Accent Line" },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = colorHex }),
                new A.Outline(new A.NoFill())));
    }

    private static P.Shape CreateStyledTextShape(uint id, string name, string text, long x, long y, long width, long height, int fontSize, string colorHex, bool bold, string fontName)
    {
        var runProps = new A.RunProperties
        {
            Language = "en-US",
            FontSize = fontSize,
            Bold = bold,
            Dirty = false
        };
        runProps.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = colorHex }));
        runProps.AppendChild(new A.LatinFont { Typeface = fontName });

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            new P.TextBody(
                new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Center },
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center },
                    new A.Run(runProps, new A.Text(text)),
                    new A.EndParagraphRunProperties { Language = "en-US" })));
    }

    private static P.Shape CreateContentTextShape(uint id, string name, string text, long x, long y, long width, long height, int fontSize, string textColorHex, string fontName, bool bullets = false)
    {
        var runProps = new A.RunProperties
        {
            Language = "en-US",
            FontSize = fontSize,
            Dirty = false
        };
        runProps.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = textColorHex }));
        runProps.AppendChild(new A.LatinFont { Typeface = fontName });

        // Split content into paragraphs for better formatting
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        var textBody = new P.TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Top },
            new A.ListStyle());

        foreach (var para in paragraphs)
        {
            var lines = para.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                var lineRunProps = new A.RunProperties
                {
                    Language = "en-US",
                    FontSize = fontSize,
                    Dirty = false
                };
                lineRunProps.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = textColorHex }));
                lineRunProps.AppendChild(new A.LatinFont { Typeface = fontName });

                var paragraphProps = new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left };
                if (bullets)
                {
                    // Hanging-indent bullet; schema order buFont before buChar
                    paragraphProps.LeftMargin = 285750;
                    paragraphProps.Indent = -285750;
                    paragraphProps.Append(new A.BulletFont { Typeface = "Arial" }, new A.CharacterBullet { Char = "\u2022" });
                }

                textBody.AppendChild(new A.Paragraph(
                    paragraphProps,
                    new A.Run(lineRunProps, new A.Text(line.Trim())),
                    new A.EndParagraphRunProperties { Language = "en-US" }));
            }
            // Add spacing between paragraphs
            textBody.AppendChild(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" }));
        }

        // txBody needs at least one paragraph; an empty slide body would otherwise fail validation
        if (!textBody.Elements<A.Paragraph>().Any())
            textBody.AppendChild(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" }));

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            textBody);
    }
}
