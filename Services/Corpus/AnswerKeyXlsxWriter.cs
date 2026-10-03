using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Font = DocumentFormat.OpenXml.Spreadsheet.Font;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Builds answer-key.xlsx: "Answer Key" (frozen header, autofilter, sized columns), "Statistics" and "Legend" (KEY-01, KEY-04, D-33).
/// Pure: returns bytes only. Text is written as inline strings (no formulas), stripped of XML-invalid characters and capped.
/// </summary>
public static class AnswerKeyXlsxWriter
{
    public const string FileName = "answer-key.xlsx";
    public const int MaxCellLength = 32_767;

    public static readonly string[] SheetNames = { "Answer Key", "Statistics", "Legend" };

    public const string NoCustodian = "(No custodian)";
    public const string Undated = "(Undated)";

    // Style indexes into the stylesheet built in BuildStylesheet
    private const uint StyleHeader = 1;
    private const uint StyleBody = 2;
    private const uint StyleSection = 3;

    // Widths in AnswerKeyColumns.Headers order
    private static readonly double[] AnswerKeyWidths =
    {
        16, 11, 44, 24, 22, 36, 20, 32, 40, 40, 45, 10, 16, 16, 13, 16, 18, 6, 10, 14, 22, 60, 10
    };

    private static readonly double[] StatisticsWidths = { 32, 40, 16, 12 };
    private static readonly double[] LegendWidths = { 24, 60, 80 };

    private static readonly string[] ColumnDefinitions =
    {
        "DocID: Unique document number, assigned in chronological order; attachments add a .0001 suffix to the parent DocID.",
        "Doc Type: Email or Attachment.",
        "File Path: Location of the custodian's copy of the email; attachments live inside the parent email.",
        "File Name: Name of the email file or of the attachment.",
        "Custodian: Cast member whose mailbox holds this copy.",
        "All Custodians: Every cast member who sent or received the email.",
        "Date Sent: Sender-local date and time with UTC offset.",
        "From: Sender of the email.",
        "To: Primary recipients.",
        "CC: Copied recipients.",
        "Subject: Email subject line.",
        "Thread ID: Conversation the email belongs to.",
        "Family ID: DocID of the parent email; the same on the email and its attachments.",
        "Parent DocID: DocID of the parent email for attachments; blank on emails.",
        "Relevance: Relevant or Not Relevant to the issues in this matter.",
        "Issue Codes: Issues the document is relevant to (see Issue Codes).",
        "Fact IDs: Key facts the document evidences (see Key Facts).",
        "Hot: Y when the document is a hot document that directly evidences a key fact.",
        "Privileged: Y when the document is an attorney-client communication or attorney work product.",
        "Near-Miss Decoy: Y when the document looks related to the matter but is not relevant.",
        "Non-Relevant Category: Kind of non-relevant material.",
        "Rationale: Why the document was coded this way.",
        "Status: Planned, Generated, Generated - with warnings, Failed or Skipped. Planned rows are not generated yet; with warnings means the email was kept after failing a consistency check 3 times."
    };

    /// <summary>
    /// Three-sheet workbook bytes for the answer key; never mutates rows or guide.
    /// </summary>
    public static byte[] Build(IReadOnlyList<AnswerKeyRow> rows, CaseGuide guide)
    {
        var list = rows.Where(r => r != null).ToList();
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var lastColumn = ColumnName(AnswerKeyColumns.Headers.Length);
            var lastRow = list.Count + 1;

            var answerKey = AddSheet(workbookPart, BuildAnswerKeySheet(list, lastColumn, lastRow));
            var statistics = AddSheet(workbookPart, BuildStatisticsSheet(list, guide));
            var legend = AddSheet(workbookPart, BuildLegendSheet(guide));

            workbookPart.Workbook.Append(new BookViews(new WorkbookView()));
            var sheets = new Sheets();
            var parts = new[] { answerKey, statistics, legend };
            for (var i = 0; i < parts.Length; i++)
            {
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(parts[i]),
                    SheetId = (uint)(i + 1),
                    Name = SheetNames[i]
                });
            }

            workbookPart.Workbook.Append(sheets);
            workbookPart.Workbook.Append(new DefinedNames(
                new DefinedName($"'{SheetNames[0]}'!$A$1:${lastColumn}${lastRow}")
                {
                    Name = "_xlnm._FilterDatabase",
                    LocalSheetId = 0,
                    Hidden = true
                }));
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// 1 -> A, 23 -> W, 27 -> AA.
    /// </summary>
    public static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            name = (char)('A' + remainder) + name;
            index = (index - 1) / 26;
        }

        return name;
    }

    private static WorksheetPart AddSheet(WorkbookPart workbookPart, Worksheet worksheet)
    {
        var part = workbookPart.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        part.Worksheet.Save();
        return part;
    }

    private static Worksheet BuildAnswerKeySheet(List<AnswerKeyRow> rows, string lastColumn, int lastRow)
    {
        var sheet = new SheetBuilder();
        sheet.TextRow(AnswerKeyColumns.Headers, StyleHeader);
        foreach (var row in rows)
            sheet.TextRow(row.ToValues(), StyleBody);

        var view = new SheetView(
            new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
            new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } })
        {
            WorkbookViewId = 0,
            TabSelected = true
        };

        return new Worksheet(
            new SheetViews(view),
            BuildColumns(AnswerKeyWidths),
            sheet.Data,
            new AutoFilter { Reference = $"A1:{lastColumn}{lastRow}" });
    }

    private static Worksheet BuildStatisticsSheet(List<AnswerKeyRow> rows, CaseGuide guide)
    {
        var sheet = new SheetBuilder();

        // By custodian: relevant + not relevant = total per custodian, grand total = row count
        sheet.SectionTitle("By Custodian");
        sheet.TextRow(new[] { "Custodian", "Relevant", "Not Relevant", "Total" }, StyleHeader);
        var byCustodian = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Custodian) ? NoCustodian : r.Custodian.Trim(), StringComparer.Ordinal)
            .OrderBy(g => g.Key == NoCustodian ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in byCustodian)
        {
            var relevant = group.Count(IsRelevant);
            sheet.MixedRow(group.Key, relevant, group.Count() - relevant, group.Count());
        }

        var totalRelevant = rows.Count(IsRelevant);
        sheet.MixedRow("Total", totalRelevant, rows.Count - totalRelevant, rows.Count);
        sheet.Blank();

        // By issue: documents whose issue codes contain the issue id
        sheet.SectionTitle("By Issue");
        sheet.TextRow(new[] { "Issue", "Title", "Relevant Documents" }, StyleHeader);
        foreach (var issue in guide.Issues.Where(i => i != null && !string.IsNullOrWhiteSpace(i.Id)))
        {
            var count = rows.Count(r => SplitCodes(r.IssueCodes).Contains(issue.Id, StringComparer.Ordinal));
            sheet.MixedRow(new[] { issue.Id, issue.Title }, count);
        }

        sheet.Blank();

        // By relevance / category
        sheet.SectionTitle("By Relevance / Category");
        sheet.TextRow(new[] { "Category", "Documents" }, StyleHeader);
        sheet.MixedRow(AnswerKeyBuilder.RelevantLabel, totalRelevant);
        sheet.MixedRow(AnswerKeyBuilder.NotRelevantLabel, rows.Count - totalRelevant);
        foreach (var label in CategoryLabels())
        {
            sheet.MixedRow(label, rows.Count(r => !IsRelevant(r)
                && string.Equals(r.NonRelevantCategory?.Trim(), label, StringComparison.OrdinalIgnoreCase)));
        }

        sheet.MixedRow("Hot", rows.Count(r => IsYes(r.Hot)));
        sheet.MixedRow("Privileged", rows.Count(r => IsYes(r.Privileged)));
        sheet.MixedRow("Near-miss", rows.Count(r => IsYes(r.NearMiss)));
        sheet.Blank();

        // By month: yyyy-MM from Date Sent
        sheet.SectionTitle("By Month");
        sheet.TextRow(new[] { "Month", "Relevant", "Not Relevant", "Total" }, StyleHeader);
        var byMonth = rows
            .GroupBy(r => MonthKey(r.DateSent), StringComparer.Ordinal)
            .OrderBy(g => g.Key == Undated ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in byMonth)
        {
            var relevant = group.Count(IsRelevant);
            sheet.MixedRow(group.Key, relevant, group.Count() - relevant, group.Count());
        }

        return new Worksheet(
            new SheetViews(new SheetView { WorkbookViewId = 0 }),
            BuildColumns(StatisticsWidths),
            sheet.Data);
    }

    private static Worksheet BuildLegendSheet(CaseGuide guide)
    {
        var sheet = new SheetBuilder();

        sheet.SectionTitle("Issue Codes");
        sheet.TextRow(new[] { "Issue", "Title", "Relevance Criteria" }, StyleHeader);
        foreach (var issue in guide.Issues.Where(i => i != null))
            sheet.TextRow(new[] { issue.Id, issue.Title, issue.RelevanceCriteria }, StyleBody);
        sheet.Blank();

        sheet.SectionTitle("Key Facts");
        sheet.TextRow(new[] { "Fact", "Date", "Title" }, StyleHeader);
        foreach (var fact in guide.Facts.Where(f => f != null))
            sheet.TextRow(new[] { fact.Id, fact.Date, fact.Title }, StyleBody);
        sheet.Blank();

        sheet.SectionTitle("Column Definitions");
        sheet.TextRow(new[] { "Column", "Definition" }, StyleHeader);
        for (var i = 0; i < AnswerKeyColumns.Headers.Length; i++)
        {
            var definition = i < ColumnDefinitions.Length ? ColumnDefinitions[i] : AnswerKeyColumns.Headers[i] + ".";
            sheet.TextRow(new[] { AnswerKeyColumns.Headers[i], definition }, StyleBody);
        }

        sheet.Blank();

        sheet.SectionTitle("Category Definitions");
        sheet.TextRow(new[] { "Category", "Definition" }, StyleHeader);
        sheet.TextRow(new[] { AnswerKeyBuilder.RelevantLabel, "Evidences one or more key facts or issues in this matter, including quoted replies and attachments in a relevant family." }, StyleBody);
        sheet.TextRow(new[] { "Daily business", "Routine business correspondence unrelated to the issues in this matter." }, StyleBody);
        sheet.TextRow(new[] { "Ancillary", "Administrative staff correspondence unrelated to the issues in this matter." }, StyleBody);
        sheet.TextRow(new[] { "Newsletter/spam/personal", "Newsletters, promotional email and personal messages." }, StyleBody);
        sheet.TextRow(new[] { "Decoy", "Near-miss documents that mention the people, project or subject matter but not the alleged conduct." }, StyleBody);
        sheet.TextRow(new[] { "Hot", "A relevant document that directly evidences a key fact; review first." }, StyleBody);
        sheet.TextRow(new[] { "Privileged", "Attorney-client communication or attorney work product; withhold or redact." }, StyleBody);

        return new Worksheet(
            new SheetViews(new SheetView { WorkbookViewId = 0 }),
            BuildColumns(LegendWidths),
            sheet.Data);
    }

    private static Columns BuildColumns(double[] widths)
    {
        var columns = new Columns();
        for (var i = 0; i < widths.Length; i++)
        {
            columns.Append(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = widths[i], CustomWidth = true });
        }

        return columns;
    }

    private static Stylesheet BuildStylesheet()
    {
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
        { Count = 2 };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE8F2FC" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid }))
        { Count = 3 };

        var borders = new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder()))
        { Count = 1 };

        var cellStyleFormats = new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 })
        { Count = 1 };

        var cellFormats = new CellFormats(
            // 0: default
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            // 1: header (bold, light fill)
            new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 2, BorderId = 0, FormatId = 0, ApplyFont = true, ApplyFill = true },
            // 2: body (wrap, top-aligned)
            new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top })
            { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyAlignment = true },
            // 3: section title (bold)
            new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true })
        { Count = 4 };

        var cellStyles = new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 };

        return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
    }

    private static bool IsRelevant(AnswerKeyRow row) =>
        string.Equals(row.Relevance?.Trim(), AnswerKeyBuilder.RelevantLabel, StringComparison.OrdinalIgnoreCase);

    private static bool IsYes(string? value) => string.Equals(value?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitCodes(string? codes) =>
        (codes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> CategoryLabels() =>
        Enum.GetValues<NonRelevantCategory>()
            .Select(AnswerKeyBuilder.CategoryLabel)
            .Where(label => label.Length > 0);

    // "2024-03-18 09:15 -05:00" -> "2024-03"; anything else is Undated
    private static string MonthKey(string? dateSent)
    {
        var text = dateSent?.Trim() ?? string.Empty;
        if (text.Length >= 7 && DateTime.TryParseExact(text[..7], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return text[..7];
        return Undated;
    }

    /// <summary>
    /// Strips XML-invalid characters and caps at MaxCellLength without splitting a surrogate pair.
    /// </summary>
    private static string Clean(string? text)
    {
        var clean = CaseGuideDocxExporter.StripInvalidXmlChars(text);
        if (clean.Length <= MaxCellLength)
            return clean;
        var length = char.IsHighSurrogate(clean[MaxCellLength - 1]) ? MaxCellLength - 1 : MaxCellLength;
        return clean[..length];
    }

    /// <summary>
    /// Appends rows with consecutive indexes; Blank() skips one row index to separate sections.
    /// </summary>
    private sealed class SheetBuilder
    {
        private uint _rowIndex;

        public SheetData Data { get; } = new();

        public void Blank() => _rowIndex++;

        public void SectionTitle(string title) => TextRow(new[] { title }, StyleSection);

        public void TextRow(IReadOnlyList<string> values, uint style)
        {
            var row = NewRow();
            for (var i = 0; i < values.Count; i++)
                row.Append(TextCell(ColumnName(i + 1) + row.RowIndex!.Value, values[i], style));
        }

        public void MixedRow(string label, params int[] numbers) => MixedRow(new[] { label }, numbers);

        public void MixedRow(IReadOnlyList<string> labels, params int[] numbers)
        {
            var row = NewRow();
            var column = 1;
            foreach (var label in labels)
                row.Append(TextCell(ColumnName(column++) + row.RowIndex!.Value, label, StyleBody));
            foreach (var number in numbers)
            {
                row.Append(new Cell
                {
                    CellReference = ColumnName(column++) + row.RowIndex!.Value,
                    DataType = CellValues.Number,
                    CellValue = new CellValue(number)
                });
            }
        }

        private Row NewRow()
        {
            _rowIndex++;
            var row = new Row { RowIndex = _rowIndex };
            Data.Append(row);
            return row;
        }

        private static Cell TextCell(string reference, string? text, uint style) => new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            StyleIndex = style,
            InlineString = new InlineString(new Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve })
        };
    }
}
