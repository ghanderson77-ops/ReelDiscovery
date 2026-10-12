using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class AnswerKeyXlsxWriterTests
{
    private const string FormulaSubject = "=cmd|' /C calc'!A0";

    private static CaseGuide Guide() => new()
    {
        Issues =
        {
            new CaseIssue { Id = "ISS-01", Title = "Breach of Contract", RelevanceCriteria = "Documents about Change Order 7 approval." },
            new CaseIssue { Id = "ISS-02", Title = "Negligent Misrepresentation", RelevanceCriteria = "Statements about window performance." }
        },
        Facts =
        {
            new KeyFact { Id = "F-001", Date = "2024-03-18", Title = "Chen approves Change Order 7" },
            new KeyFact { Id = "F-002", Date = "2024-04-02", Title = "Harmon denies the approval" }
        }
    };

    private static AnswerKeyRow Row(string docId, string custodian, string date, bool relevant, string issues = "",
        string category = "", bool hot = false, bool privileged = false, bool nearMiss = false, string docType = "Email",
        string subject = "Status update", string rationale = "Routine.") => new()
    {
        DocId = docId,
        DocType = docType,
        FilePath = $"Mailboxes/x/Inbox/{docId}.eml",
        FileName = $"{docId}.eml",
        Custodian = custodian,
        AllCustodians = custodian,
        DateSent = date,
        From = custodian,
        To = "Someone",
        Subject = subject,
        ThreadId = "T-0001",
        FamilyId = docId,
        Relevance = relevant ? "Relevant" : "Not Relevant",
        IssueCodes = issues,
        FactIds = relevant ? "F-001" : string.Empty,
        Hot = hot ? "Y" : "N",
        Privileged = privileged ? "Y" : "N",
        NearMiss = nearMiss ? "Y" : "N",
        NonRelevantCategory = category,
        Rationale = rationale,
        Status = "Planned"
    };

    private static List<AnswerKeyRow> Rows() => new()
    {
        Row("BR-000001", "Lisa Chen", "2024-03-18 09:15 -05:00", true, "ISS-01", hot: true, subject: FormulaSubject),
        Row("BR-000001.0001", "Lisa Chen", "2024-03-18 09:15 -05:00", true, "ISS-01", hot: true, docType: "Attachment",
            rationale: "Bad\u0001char"),
        Row("BR-000002", "Dan Harmon", "2024-03-20 10:00 -05:00", true, "ISS-01; ISS-02", privileged: true,
            rationale: new string('x', 40_000)),
        Row("BR-000003", "Dan Harmon", "2024-04-02 11:00 -05:00", false, category: "Daily business"),
        Row("BR-000004", "Lisa Chen", "2024-04-03 12:00 -05:00", false, category: "Decoy", nearMiss: true),
        Row("BR-000005", "Lisa Chen", "2024-04-05 08:00 -05:00", false, category: "Newsletter/spam/personal"),
        Row("BR-000006", "Pat Ruiz", "2024-04-06 08:00 -05:00", false, category: "Ancillary"),
        Row("BR-000007", "", "2024-04-07 08:00 -05:00", false, category: "Newsletter/spam/personal")
    };

    private sealed class Book : IDisposable
    {
        public SpreadsheetDocument Doc { get; }
        public Book(byte[] bytes) => Doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        public void Dispose() => Doc.Dispose();

        public List<Sheet> Sheets => Doc.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().ToList();

        public Worksheet Sheet(string name)
        {
            var sheet = Sheets.Single(s => s.Name == name);
            return ((WorksheetPart)Doc.WorkbookPart!.GetPartById(sheet.Id!)).Worksheet!;
        }

        // Row index -> cell values by column letter
        public SortedDictionary<uint, Dictionary<string, Cell>> Grid(string name)
        {
            var grid = new SortedDictionary<uint, Dictionary<string, Cell>>();
            foreach (var row in Sheet(name).GetFirstChild<SheetData>()!.Elements<Row>())
            {
                var cells = new Dictionary<string, Cell>();
                foreach (var cell in row.Elements<Cell>())
                    cells[new string(cell.CellReference!.Value!.TakeWhile(char.IsLetter).ToArray())] = cell;
                grid[row.RowIndex!.Value] = cells;
            }

            return grid;
        }
    }

    private static string Text(Cell cell) =>
        cell.DataType?.Value == CellValues.InlineString ? cell.InlineString!.Text!.Text : cell.CellValue?.Text ?? string.Empty;

    // Data rows under a section title (title row, header row, then rows until a gap in row indices)
    private static List<Dictionary<string, Cell>> Section(SortedDictionary<uint, Dictionary<string, Cell>> grid, string title)
    {
        var keys = grid.Keys.ToList();
        var start = keys.FindIndex(k => grid[k].TryGetValue("A", out var a) && Text(a) == title);
        Assert.True(start >= 0, $"Section '{title}' not found");
        var result = new List<Dictionary<string, Cell>>();
        var previous = keys[start + 1];
        for (var i = start + 2; i < keys.Count && keys[i] == previous + 1; i++)
        {
            result.Add(grid[keys[i]]);
            previous = keys[i];
        }

        return result;
    }

    private static int Number(Cell cell)
    {
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        return int.Parse(cell.CellValue!.Text);
    }

    [Fact]
    public void Build_ValidatesWithZeroErrors()
    {
        using var book = new Book(AnswerKeyXlsxWriter.Build(Rows(), Guide()));

        var errors = new OpenXmlValidator().Validate(book.Doc).ToList();
        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
    }

    [Fact]
    public void Build_EmptyRows_ValidatesWithZeroErrors()
    {
        using var book = new Book(AnswerKeyXlsxWriter.Build(new List<AnswerKeyRow>(), new CaseGuide()));

        Assert.Empty(new OpenXmlValidator().Validate(book.Doc).Select(e => e.Description));
    }

    [Fact]
    public void Build_SheetNamesInOrder()
    {
        using var book = new Book(AnswerKeyXlsxWriter.Build(Rows(), Guide()));

        Assert.Equal(new[] { "Answer Key", "Statistics", "Legend" }, book.Sheets.Select(s => s.Name!.Value));
        Assert.Equal(AnswerKeyXlsxWriter.SheetNames, book.Sheets.Select(s => s.Name!.Value));
        Assert.Equal("answer-key.xlsx", AnswerKeyXlsxWriter.FileName);
    }

    [Fact]
    public void AnswerKey_HeaderFrozenFilteredAndSized()
    {
        var rows = Rows();
        using var book = new Book(AnswerKeyXlsxWriter.Build(rows, Guide()));
        var grid = book.Grid("Answer Key");
        var sheet = book.Sheet("Answer Key");

        Assert.Equal(AnswerKeyColumns.Headers, grid[1].OrderBy(c => c.Key.Length).ThenBy(c => c.Key).Select(c => Text(c.Value)));
        Assert.Equal(rows.Count, grid.Count - 1);

        var pane = sheet.Descendants<Pane>().Single();
        Assert.Equal(1d, pane.VerticalSplit!.Value);
        Assert.Equal("A2", pane.TopLeftCell!.Value);
        Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);

        Assert.Equal($"A1:W{rows.Count + 1}", sheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
        var defined = book.Doc.WorkbookPart!.Workbook!.DefinedNames!.Elements<DefinedName>().Single();
        Assert.Equal("_xlnm._FilterDatabase", defined.Name!.Value);
        Assert.Equal(0u, defined.LocalSheetId!.Value);
        Assert.True(defined.Hidden!.Value);
        Assert.Equal($"'Answer Key'!$A$1:$W${rows.Count + 1}", defined.Text);

        var columns = sheet.GetFirstChild<Columns>()!.Elements<Column>().ToList();
        var covered = columns.SelectMany(c => Enumerable.Range((int)c.Min!.Value, (int)(c.Max!.Value - c.Min!.Value + 1))).ToList();
        Assert.Equal(Enumerable.Range(1, 23), covered);
        Assert.All(columns, c => Assert.True(c.CustomWidth!.Value));
    }

    [Fact]
    public void AnswerKey_InlineStringsOnly_NoFormulas_Sanitized()
    {
        using var book = new Book(AnswerKeyXlsxWriter.Build(Rows(), Guide()));

        foreach (var part in book.Doc.WorkbookPart!.WorksheetParts)
            Assert.Empty(part.Worksheet!.Descendants<CellFormula>());

        var grid = book.Grid("Answer Key");
        Assert.All(grid.Values.SelectMany(r => r.Values), c => Assert.Equal(CellValues.InlineString, c.DataType!.Value));

        // Column K = Subject, V = Rationale
        Assert.Equal(FormulaSubject, Text(grid[2]["K"]));
        Assert.Equal("Badchar", Text(grid[3]["V"]));
        Assert.Equal(AnswerKeyXlsxWriter.MaxCellLength, Text(grid[4]["V"]).Length);
        Assert.Equal(32_767, AnswerKeyXlsxWriter.MaxCellLength);
    }

    [Fact]
    public void Statistics_ByCustodian_SumsToRowCounts()
    {
        var rows = Rows();
        using var book = new Book(AnswerKeyXlsxWriter.Build(rows, Guide()));
        var section = Section(book.Grid("Statistics"), "By Custodian");

        var total = section.Single(r => Text(r["A"]) == "Total");
        Assert.Equal(rows.Count, Number(total["D"]));
        Assert.Equal(rows.Count(r => r.Relevance == "Relevant"), Number(total["B"]));

        foreach (var line in section.Where(r => Text(r["A"]) != "Total"))
        {
            var name = Text(line["A"]);
            var expected = rows.Count(r => (r.Custodian.Length == 0 ? "(No custodian)" : r.Custodian) == name);
            Assert.Equal(expected, Number(line["B"]) + Number(line["C"]));
            Assert.Equal(expected, Number(line["D"]));
        }

        Assert.Equal(4, section.Count(r => Text(r["A"]) != "Total"));
        Assert.Equal(rows.Count, section.Where(r => Text(r["A"]) != "Total").Sum(r => Number(r["D"])));
        var chen = section.Single(r => Text(r["A"]) == "Lisa Chen");
        Assert.Equal(2, Number(chen["B"]));
        Assert.Equal(2, Number(chen["C"]));
    }

    [Fact]
    public void Statistics_ByRelevance_CountsCategoriesAndFlags()
    {
        using var book = new Book(AnswerKeyXlsxWriter.Build(Rows(), Guide()));
        var section = Section(book.Grid("Statistics"), "By Relevance / Category");
        var counts = section.ToDictionary(r => Text(r["A"]), r => Number(r["B"]));

        Assert.Equal(3, counts["Relevant"]);
        Assert.Equal(5, counts["Not Relevant"]);
        Assert.Equal(1, counts["Daily business"]);
        Assert.Equal(1, counts["Ancillary"]);
        Assert.Equal(2, counts["Newsletter/spam/personal"]);
        Assert.Equal(1, counts["Decoy"]);
        Assert.Equal(2, counts["Hot"]);
        Assert.Equal(1, counts["Privileged"]);
        Assert.Equal(1, counts["Near-miss"]);
    }

    [Fact]
    public void Statistics_ByMonth_KeysFromDateSent()
    {
        var rows = Rows();
        using var book = new Book(AnswerKeyXlsxWriter.Build(rows, Guide()));
        var section = Section(book.Grid("Statistics"), "By Month");

        Assert.Equal(new[] { "2024-03", "2024-04" }, section.Select(r => Text(r["A"])));
        var march = section[0];
        Assert.Equal(3, Number(march["B"]));
        Assert.Equal(0, Number(march["C"]));
        Assert.Equal(3, Number(march["D"]));
        Assert.Equal(rows.Count, section.Sum(r => Number(r["D"])));
    }

    [Fact]
    public void Statistics_ByIssue_ListsEveryIssue()
    {
        using var book = new Book(AnswerKeyXlsxWriter.Build(Rows(), Guide()));
        var section = Section(book.Grid("Statistics"), "By Issue");

        Assert.Equal(new[] { "ISS-01", "ISS-02" }, section.Select(r => Text(r["A"])));
        Assert.Equal("Breach of Contract", Text(section[0]["B"]));
        Assert.Equal(3, Number(section[0]["C"]));
        Assert.Equal(1, Number(section[1]["C"]));
    }

    [Fact]
    public void Legend_ListsIssuesFactsColumnsAndCategories()
    {
        var guide = Guide();
        using var book = new Book(AnswerKeyXlsxWriter.Build(Rows(), guide));
        var grid = book.Grid("Legend");

        var issues = Section(grid, "Issue Codes");
        Assert.Equal(guide.Issues.Select(i => i.Id), issues.Select(r => Text(r["A"])));
        Assert.Equal(guide.Issues.Select(i => i.Title), issues.Select(r => Text(r["B"])));
        Assert.Equal(guide.Issues.Select(i => i.RelevanceCriteria), issues.Select(r => Text(r["C"])));

        var facts = Section(grid, "Key Facts");
        Assert.Equal(guide.Facts.Select(f => f.Id), facts.Select(r => Text(r["A"])));
        Assert.Equal(guide.Facts.Select(f => f.Date), facts.Select(r => Text(r["B"])));
        Assert.Equal(guide.Facts.Select(f => f.Title), facts.Select(r => Text(r["C"])));

        var columns = Section(grid, "Column Definitions");
        Assert.Equal(AnswerKeyColumns.Headers, columns.Select(r => Text(r["A"])));
        Assert.All(columns, r => Assert.False(string.IsNullOrWhiteSpace(Text(r["B"]))));

        // 10 D-05: the Legend tells the user which column to edit to relabel and what survives a rewrite
        var relevance = columns.Single(r => Text(r["A"]) == "Relevance");
        Assert.Equal("Relevance: Relevant or Not Relevant to the issues in this matter. To relabel a document, edit this column; the relabel is kept when the answer key is rewritten. Edits to other columns are replaced.",
            Text(relevance["B"]));

        var categories = Section(grid, "Category Definitions").Select(r => Text(r["A"])).ToList();
        foreach (var label in new[] { "Daily business", "Ancillary", "Newsletter/spam/personal", "Decoy" })
            Assert.Contains(label, categories);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(23, "W")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    public void ColumnName_MapsIndex(int index, string expected)
    {
        Assert.Equal(expected, AnswerKeyXlsxWriter.ColumnName(index));
    }
}
