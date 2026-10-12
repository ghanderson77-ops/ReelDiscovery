using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class AnswerKeyReaderTests : IDisposable
{
    private const string MultiLineRationale = "First line, with a comma\r\nSecond line \"quoted\"  ";
    private const string PhoneticText = "PHONETIC-LEAK";

    private readonly string _folder;

    public AnswerKeyReaderTests()
    {
        // Temp folder only: never a real case folder
        _folder = Path.Combine(Path.GetTempPath(), "rd-akread-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static CaseGuide Guide() => new()
    {
        Issues =
        {
            new CaseIssue { Id = "ISS-01", Title = "Breach of Contract", RelevanceCriteria = "Documents about Change Order 7 approval." }
        },
        Facts =
        {
            new KeyFact { Id = "F-001", Date = "2024-03-18", Title = "Chen approves Change Order 7" }
        }
    };

    private static AnswerKeyRow Row(string docId, string threadId, bool relevant, string rationale) => new()
    {
        DocId = docId,
        DocType = "Email",
        FilePath = $"Mailboxes/x/Inbox/{docId}.eml",
        FileName = $"{docId}.eml",
        Custodian = "Lisa Chen",
        AllCustodians = "Lisa Chen",
        DateSent = "2024-03-18 09:15 -05:00",
        From = "Lisa Chen",
        To = "Someone",
        Subject = "Status update",
        ThreadId = threadId,
        FamilyId = docId,
        Relevance = relevant ? "Relevant" : "Not Relevant",
        Hot = "N",
        Privileged = "N",
        NearMiss = "N",
        Rationale = rationale,
        Status = "Planned"
    };

    private static List<AnswerKeyRow> Rows() => new()
    {
        Row("BR-000001", "T-0001", true, MultiLineRationale),
        Row("BR-000001.0001", "T-0001", true, "=SUM(A1)"),
        Row("BR-000002", "T-0002", false, "Routine.")
    };

    private static void AssertRows(List<AnswerKeyRow> expected, AnswerKeyParseResult result)
    {
        Assert.True(result.Parsed, result.Error);
        Assert.Equal(expected.Count, result.Rows.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].DocId, result.Rows[i].DocId);
            Assert.Equal(expected[i].ThreadId, result.Rows[i].ThreadId);
            Assert.Equal(expected[i].Relevance, result.Rows[i].Relevance);
            Assert.Equal(expected[i].Rationale, AnswerKeyReader.StripCsvGuard(result.Rows[i].Rationale));
        }
    }

    [Fact]
    public void ParseCsv_RoundTripsWriterOutput()
    {
        var rows = Rows();

        var result = AnswerKeyReader.ParseCsv(AnswerKeyCsvWriter.Build(rows));

        AssertRows(rows, result);
        Assert.Equal(MultiLineRationale, result.Rows[0].Rationale);
        Assert.Equal("'=SUM(A1)", result.Rows[1].Rationale);
    }

    [Fact]
    public void ParseCsv_AnsiSemicolonWithoutBom_DecodesLatin1()
    {
        var text = "DocID;Thread ID;Relevance;Rationale\r\nBR-000001;T-0001;Not Relevant;Café au lait\r\n";

        var result = AnswerKeyReader.ParseCsv(Encoding.Latin1.GetBytes(text));

        Assert.True(result.Parsed, result.Error);
        var row = Assert.Single(result.Rows);
        Assert.Equal("BR-000001", row.DocId);
        Assert.Equal("T-0001", row.ThreadId);
        Assert.Equal("Not Relevant", row.Relevance);
        Assert.Equal("Café au lait", row.Rationale);
    }

    [Fact]
    public void ParseCsv_SepLine_SelectsSeparator()
    {
        var text = "sep=;\r\nDocID;Relevance;Rationale\r\nBR-000001;Relevant;a, b\r\n";

        var result = AnswerKeyReader.ParseCsv(Encoding.UTF8.GetBytes(text));

        Assert.True(result.Parsed, result.Error);
        var row = Assert.Single(result.Rows);
        Assert.Equal("Relevant", row.Relevance);
        Assert.Equal("a, b", row.Rationale);
        Assert.Equal(string.Empty, row.ThreadId);
    }

    [Fact]
    public void ParseCsv_TabSeparated_Works()
    {
        var text = "DocID\tThread ID\tRelevance\tRationale\nBR-000001\tT-0001\tRelevant\tWhy, indeed\nBR-000002\tT-0002\tNot Relevant\t\n";

        var result = AnswerKeyReader.ParseCsv(Encoding.UTF8.GetBytes(text));

        Assert.True(result.Parsed, result.Error);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Why, indeed", result.Rows[0].Rationale);
        Assert.Equal("Not Relevant", result.Rows[1].Relevance);
    }

    [Fact]
    public void ParseCsv_SkipsRowsWithoutDocId()
    {
        var text = "DocID,Relevance\r\n,Relevant\r\n\r\nBR-000002,Not Relevant\r\n";

        var result = AnswerKeyReader.ParseCsv(Encoding.UTF8.GetBytes(text));

        Assert.True(result.Parsed, result.Error);
        Assert.Equal("BR-000002", Assert.Single(result.Rows).DocId);
    }

    [Fact]
    public void ParseCsv_MissingHeader_NotParsed()
    {
        var result = AnswerKeyReader.ParseCsv(Encoding.UTF8.GetBytes("Name,Value\r\nA,B\r\n"));

        Assert.False(result.Parsed);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void ParseCsv_Empty_NotParsed()
    {
        Assert.False(AnswerKeyReader.ParseCsv(Array.Empty<byte>()).Parsed);
    }

    [Fact]
    public void ParseXlsx_InlineStrings_RoundTripsWriterOutput()
    {
        var rows = Rows();

        var result = AnswerKeyReader.ParseXlsx(AnswerKeyXlsxWriter.Build(rows, Guide()));

        AssertRows(rows, result);
        Assert.Equal("=SUM(A1)", result.Rows[1].Rationale);
    }

    [Fact]
    public void ParseXlsx_SharedStrings_ReadsRowsWithoutPhoneticText()
    {
        var result = AnswerKeyReader.ParseXlsx(SharedStringWorkbook());

        Assert.True(result.Parsed, result.Error);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("BR-000001", result.Rows[0].DocId);
        Assert.Equal("T-0001", result.Rows[0].ThreadId);
        Assert.Equal("Not Relevant", result.Rows[0].Relevance);
        Assert.Equal("Base rationale", result.Rows[0].Rationale);
        Assert.DoesNotContain(PhoneticText, result.Rows[0].Rationale);
        Assert.Equal("BR-000002", result.Rows[1].DocId);
        Assert.Equal("Relevant", result.Rows[1].Relevance);
        Assert.Equal(string.Empty, result.Rows[1].Rationale);
    }

    [Fact]
    public void ParseXlsx_RandomBytes_NotParsed()
    {
        var bytes = new byte[2048];
        new Random(42).NextBytes(bytes);

        var result = AnswerKeyReader.ParseXlsx(bytes);

        Assert.False(result.Parsed);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void ParseCsv_RandomBytes_NeverThrows()
    {
        var bytes = new byte[2048];
        new Random(7).NextBytes(bytes);

        var result = AnswerKeyReader.ParseCsv(bytes);

        Assert.False(result.Parsed);
    }

    [Theory]
    [InlineData(" not relevant ", "Not Relevant")]
    [InlineData("'Relevant", "Relevant")]
    [InlineData("RELEVANT", "Relevant")]
    [InlineData("Maybe", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeRelevance_AcceptsOnlyCanonicalLabels(string? raw, string? expected)
    {
        Assert.Equal(expected, AnswerKeyReader.NormalizeRelevance(raw));
    }

    [Theory]
    [InlineData("'=SUM(A1)", "=SUM(A1)")]
    [InlineData("'hello", "'hello")]
    [InlineData("plain", "plain")]
    [InlineData(null, "")]
    public void StripCsvGuard_RemovesOnlyWriterGuard(string? value, string expected)
    {
        Assert.Equal(expected, AnswerKeyReader.StripCsvGuard(value));
    }

    [Fact]
    public void ReadShared_FileHeldOpenElsewhere_StillReads()
    {
        var path = Path.Combine(_folder, AnswerKeyCsvWriter.FileName);
        var bytes = AnswerKeyCsvWriter.Build(Rows());
        File.WriteAllBytes(path, bytes);

        AnswerKeyFileRead read;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            read = AnswerKeyReader.ReadShared(path);

        Assert.True(read.Exists);
        Assert.Null(read.ReadError);
        Assert.False(read.TooLarge);
        Assert.Equal(bytes, read.Bytes);
        Assert.NotEqual(default, read.LastWriteUtc);
    }

    [Fact]
    public void ReadShared_MissingFile_DoesNotExist()
    {
        var read = AnswerKeyReader.ReadShared(Path.Combine(_folder, "missing.csv"));

        Assert.False(read.Exists);
        Assert.Null(read.ReadError);
        Assert.Empty(read.Bytes);
    }

    // What Excel writes on save: shared strings, plus a phonetic run on one item that must not leak into the text
    private static byte[] SharedStringWorkbook()
    {
        var strings = new List<SharedStringItem>();
        uint Add(SharedStringItem item)
        {
            strings.Add(item);
            return (uint)(strings.Count - 1);
        }

        uint AddText(string text) => Add(new SharedStringItem(new Text(text)));

        Cell SharedCell(string column, uint row, uint index) => new()
        {
            CellReference = column + row,
            DataType = CellValues.SharedString,
            CellValue = new CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        var sheetData = new SheetData();
        var header = new DocumentFormat.OpenXml.Spreadsheet.Row { RowIndex = 1 };
        for (var i = 0; i < AnswerKeyColumns.Headers.Length; i++)
            header.Append(SharedCell(AnswerKeyXlsxWriter.ColumnName(i + 1), 1, AddText(AnswerKeyColumns.Headers[i])));
        sheetData.Append(header);

        var phonetic = Add(new SharedStringItem(
            new Text("Base rationale"),
            new PhoneticRun(new Text(PhoneticText)) { BaseTextStartIndex = 0, EndingBaseIndex = 4 },
            new PhoneticProperties { FontId = 0 }));

        // Sparse rows: only the columns the reader needs, addressed by CellReference
        var row2 = new DocumentFormat.OpenXml.Spreadsheet.Row { RowIndex = 2 };
        row2.Append(SharedCell("A", 2, AddText("BR-000001")));
        row2.Append(SharedCell("L", 2, AddText("T-0001")));
        row2.Append(SharedCell("O", 2, AddText("Not Relevant")));
        row2.Append(SharedCell("V", 2, phonetic));
        sheetData.Append(row2);

        var row3 = new DocumentFormat.OpenXml.Spreadsheet.Row { RowIndex = 3 };
        row3.Append(SharedCell("A", 3, AddText("BR-000002")));
        row3.Append(new Cell
        {
            CellReference = "O3",
            DataType = CellValues.String,
            CellFormula = new CellFormula("\"Relevant\""),
            CellValue = new CellValue("Relevant")
        });
        sheetData.Append(row3);

        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedPart.SharedStringTable = new SharedStringTable(strings.Cast<OpenXmlElement>())
            {
                Count = (uint)strings.Count,
                UniqueCount = (uint)strings.Count
            };
            sharedPart.SharedStringTable.Save();

            // An unrelated first sheet: the reader picks the sheet by name
            var otherPart = workbookPart.AddNewPart<WorksheetPart>();
            otherPart.Worksheet = new Worksheet(new SheetData());
            otherPart.Worksheet.Save();

            var keyPart = workbookPart.AddNewPart<WorksheetPart>();
            keyPart.Worksheet = new Worksheet(sheetData);
            keyPart.Worksheet.Save();

            workbookPart.Workbook.Append(new Sheets(
                new Sheet { Id = workbookPart.GetIdOfPart(otherPart), SheetId = 1, Name = "Notes" },
                new Sheet { Id = workbookPart.GetIdOfPart(keyPart), SheetId = 2, Name = AnswerKeyXlsxWriter.SheetNames[0] }));
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }
}
