using System.Globalization;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

/// <summary>
/// Relabel lifecycle of the answer-key funnel (D-01..D-10, D-20, D-23): write, edit the key file as a user would, write
/// again, read back.
/// </summary>
public class AnswerKeyRelabelTests : IDisposable
{
    private const string Relevant = AnswerKeyBuilder.RelevantLabel;
    private const string NotRelevant = AnswerKeyBuilder.NotRelevantLabel;

    private readonly string _root;
    private readonly string _folder;
    private readonly CaseGuide _guide;
    private readonly CorpusPlan _plan;

    public AnswerKeyRelabelTests()
    {
        // Temp folder only: never a real case folder
        _root = Path.Combine(Path.GetTempPath(), "rd-relabel-" + Guid.NewGuid().ToString("N"));
        _folder = NewFolder("Case");
        var draft = CorpusTestData.RichDraft();
        _guide = draft.Guide!;
        _plan = PlanFixtures.ValidPlan(draft);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---- Fixture helpers ----

    private string NewFolder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string CsvPath(string folder) => Path.Combine(folder, AnswerKeyCsvWriter.FileName);

    private static string XlsxPath(string folder) => Path.Combine(folder, AnswerKeyXlsxWriter.FileName);

    private Task<AnswerKeyWriteResult> Write(string? folder = null, AnswerKeyOverlay? overlay = null, FileWriter? writer = null) =>
        AnswerKeyFiles.WriteAsync(folder ?? _folder, _plan, _guide, overlay, writer);

    private List<AnswerKeyRow> Projection() => AnswerKeyBuilder.Build(_plan, _guide);

    private string Email(string relevance, int index) => Projection()
        .Where(r => r.DocType == AnswerKeyBuilder.DocTypeEmail && r.Relevance == relevance)
        .Select(r => r.DocId)
        .ElementAt(index);

    private string ProjectedRationale(string docId) => Projection().Single(r => r.DocId == docId).Rationale;

    private static AnswerKeyParseResult Parse(string path, bool xlsx)
    {
        var bytes = File.ReadAllBytes(path);
        return xlsx ? AnswerKeyReader.ParseXlsx(bytes) : AnswerKeyReader.ParseCsv(bytes);
    }

    private static AnswerKeyReadRow KeyRow(string path, bool xlsx, string docId) =>
        Parse(path, xlsx).Rows.Single(r => r.DocId == docId);

    // The rows a spreadsheet program would save back: every column from the projection, Relevance and Rationale as they
    // are in the file now, only the rows the file holds
    private List<AnswerKeyRow> RowsOnDisk(string path, bool xlsx)
    {
        var parsed = Parse(path, xlsx);
        Assert.True(parsed.Parsed, parsed.Error);
        var byDocId = parsed.Rows.GroupBy(r => r.DocId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<AnswerKeyRow>();
        foreach (var row in Projection())
        {
            if (!byDocId.TryGetValue(row.DocId, out var onDisk))
                continue;
            row.Relevance = onDisk.Relevance;
            row.Rationale = AnswerKeyReader.StripCsvGuard(onDisk.Rationale);
            rows.Add(row);
        }

        return rows;
    }

    private void EditRows(string folder, bool xlsx, Action<List<AnswerKeyRow>> edit)
    {
        var path = xlsx ? XlsxPath(folder) : CsvPath(folder);
        var rows = RowsOnDisk(path, xlsx);
        edit(rows);
        if (xlsx)
            SaveSharedStringXlsx(folder, rows);
        else
            SaveCsv(folder, rows);
    }

    private static void Edit(List<AnswerKeyRow> rows, string docId, string relevance, string? rationale)
    {
        var row = rows.Single(r => r.DocId == docId);
        row.Relevance = relevance;
        if (rationale != null)
            row.Rationale = rationale;
    }

    // Stand-in for Excel saving the CSV
    private void EditCsv(string docId, string relevance, string? rationale = null, string? folder = null) =>
        EditRows(folder ?? _folder, false, rows => Edit(rows, docId, relevance, rationale));

    // Stand-in for Excel saving the workbook (shared strings)
    private void EditXlsx(string docId, string relevance, string? rationale = null, string? folder = null) =>
        EditRows(folder ?? _folder, true, rows => Edit(rows, docId, relevance, rationale));

    private static void SaveCsv(string folder, List<AnswerKeyRow> rows) =>
        File.WriteAllBytes(CsvPath(folder), AnswerKeyCsvWriter.Build(rows));

    private static AnswerKeyRelabelSet Sidecar(string folder) => new AnswerKeyRelabelStore(folder).Load();

    private static FileWriter FailFor(string extension) => (path, bytes, ct) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? throw new IOException("The process cannot access the file.")
            : CaseStore.WriteAtomicAsync(path, bytes, ct);

    // What Excel writes on save: every cell a shared string, on a sheet named "Answer Key"
    private static void SaveSharedStringXlsx(string folder, List<AnswerKeyRow> rows)
    {
        var strings = new List<SharedStringItem>();
        Cell SharedCell(int column, uint row, string text)
        {
            strings.Add(new SharedStringItem(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            return new Cell
            {
                CellReference = AnswerKeyXlsxWriter.ColumnName(column) + row.ToString(CultureInfo.InvariantCulture),
                DataType = CellValues.SharedString,
                CellValue = new CellValue((strings.Count - 1).ToString(CultureInfo.InvariantCulture))
            };
        }

        var sheetData = new SheetData();
        var values = new List<string[]> { AnswerKeyColumns.Headers };
        values.AddRange(rows.Select(r => r.ToValues()));
        for (var i = 0; i < values.Count; i++)
        {
            var index = (uint)(i + 1);
            var row = new Row { RowIndex = index };
            for (var c = 0; c < values[i].Length; c++)
                row.Append(SharedCell(c + 1, index, values[i][c]));
            sheetData.Append(row);
        }

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

            var keyPart = workbookPart.AddNewPart<WorksheetPart>();
            keyPart.Worksheet = new Worksheet(sheetData);
            keyPart.Worksheet.Save();

            workbookPart.Workbook.Append(new Sheets(
                new Sheet { Id = workbookPart.GetIdOfPart(keyPart), SheetId = 1, Name = AnswerKeyXlsxWriter.SheetNames[0] }));
            workbookPart.Workbook.Save();
        }

        File.WriteAllBytes(XlsxPath(folder), stream.ToArray());
    }

    // Column B of the first Statistics row whose column A reads label ("By Relevance / Category" for the labels)
    private static int StatisticsCount(string path, string label)
    {
        using var doc = SpreadsheetDocument.Open(new MemoryStream(File.ReadAllBytes(path)), false);
        var workbookPart = doc.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Descendants<Sheet>().Single(s => s.Name == "Statistics");
        var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        foreach (var row in part.Worksheet!.Descendants<Row>())
        {
            var cells = row.Elements<Cell>().ToList();
            var first = cells.FirstOrDefault(c => c.CellReference?.Value?.StartsWith('A') == true);
            if (first == null || CellText(first) != label)
                continue;
            var count = cells.First(c => c.CellReference?.Value?.StartsWith('B') == true);
            return int.Parse(count.CellValue!.Text, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException($"No Statistics row '{label}'.");
    }

    private static string CellText(Cell cell) =>
        cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText ?? string.Empty : cell.CellValue?.Text ?? string.Empty;

    // ---- Tests ----

    [Fact]
    public async Task CsvRelabel_SurvivesNextWrite()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, NotRelevant);

        var result = await Write();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Relabels);
        Assert.Equal(NotRelevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
        Assert.Equal(NotRelevant, KeyRow(XlsxPath(_folder), true, docId).Relevance);

        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(_plan.PlanId, relabel.PlanId);
        Assert.Equal(docId, relabel.DocId);
        Assert.Equal(NotRelevant, relabel.Relevance);
        Assert.Equal(AnswerKeyCsvWriter.FileName, relabel.Source);

        // The Statistics sheet counts the relabel (D-01)
        var notRelevant = Projection().Count(r => r.Relevance == NotRelevant) + 1;
        Assert.Equal(notRelevant, StatisticsCount(XlsxPath(_folder), NotRelevant));
    }

    [Fact]
    public async Task XlsxRelabel_SharedStrings_Survives()
    {
        await Write();
        var docId = Email(Relevant, 1);
        EditXlsx(docId, NotRelevant);

        await Write();

        Assert.Equal(NotRelevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
        Assert.Equal(NotRelevant, KeyRow(XlsxPath(_folder), true, docId).Relevance);
        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(docId, relabel.DocId);
        Assert.Equal(AnswerKeyXlsxWriter.FileName, relabel.Source);
    }

    [Fact]
    public async Task OwnWrite_IsNotAnEdit()
    {
        await Write();
        var second = await Write();
        Assert.Equal(0, second.Relabels);
        Assert.Empty(Sidecar(_folder).Relabels);

        // A changed overlay changes the files, but they are still our own writes
        var docId = Email(Relevant, 0);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[docId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusSkipped, Note = "Skipped: test." };
        await Write(overlay: overlay);
        Assert.Empty(Sidecar(_folder).Relabels);

        await Write();
        Assert.Empty(Sidecar(_folder).Relabels);
    }

    [Fact]
    public async Task SetBack_RemovesRelabel()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, NotRelevant);
        await Write();
        Assert.Single(Sidecar(_folder).Relabels);

        EditCsv(docId, Relevant);
        var result = await Write();

        Assert.Equal(0, result.Relabels);
        Assert.Empty(Sidecar(_folder).Relabels);
        Assert.Equal(Relevant, KeyRow(XlsxPath(_folder), true, docId).Relevance);
    }

    [Fact]
    public async Task StaleFile_NotARemoval()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, NotRelevant);

        // The XLSX write fails (Excel holds it): the XLSX on disk keeps the plan label
        var failed = await Write(writer: FailFor(".xlsx"));
        Assert.Equal(AnswerKeyXlsxWriter.FileName, failed.FailedFileName);
        Assert.Single(Sidecar(_folder).Relabels);

        // Excel saves the stale workbook it had open: it never held the relabel, so it is not a removal (Pitfall 2)
        SaveSharedStringXlsx(_folder, Projection());
        var result = await Write();

        Assert.True(result.Succeeded);
        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(NotRelevant, relabel.Relevance);
        Assert.Equal(NotRelevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
        Assert.Equal(NotRelevant, KeyRow(XlsxPath(_folder), true, docId).Relevance);
    }

    [Fact]
    public async Task ConflictingVotes_LaterFileWins()
    {
        var xlsxWins = await Conflict(NewFolder("xlsx-later"), xlsxLater: true);
        Assert.Equal(NotRelevant, xlsxWins.Relevance);
        Assert.Equal("Reviewed by counsel", xlsxWins.Rationale);
        Assert.Equal(AnswerKeyXlsxWriter.FileName, xlsxWins.Source);

        var csvWins = await Conflict(NewFolder("csv-later"), xlsxLater: false);
        Assert.Equal(NotRelevant, csvWins.Relevance);
        Assert.Equal(string.Empty, csvWins.Rationale);
        Assert.Equal(AnswerKeyCsvWriter.FileName, csvWins.Source);
    }

    private async Task<AnswerKeyRelabel> Conflict(string folder, bool xlsxLater)
    {
        await Write(folder);
        var docId = Email(Relevant, 2);
        EditCsv(docId, NotRelevant, string.Empty, folder);
        EditXlsx(docId, NotRelevant, "Reviewed by counsel", folder);

        var t1 = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(CsvPath(folder), t1);
        File.SetLastWriteTimeUtc(XlsxPath(folder), xlsxLater ? t1.AddMinutes(1) : t1.AddMinutes(-1));

        await Write(folder);
        var relabel = Assert.Single(Sidecar(folder).Relabels);
        Assert.Equal(docId, relabel.DocId);
        return relabel;
    }

    [Fact]
    public async Task UnknownDocId_AndAddedRows_Ignored()
    {
        await Write();
        EditRows(_folder, false, rows => rows.Add(new AnswerKeyRow
        {
            DocId = "RD-999999",
            DocType = AnswerKeyBuilder.DocTypeEmail,
            ThreadId = "T-9999",
            Relevance = NotRelevant,
            Rationale = "Added by hand"
        }));

        var result = await Write();

        Assert.Equal(0, result.Relabels);
        Assert.Empty(Sidecar(_folder).Relabels);
        Assert.DoesNotContain(Parse(CsvPath(_folder), false).Rows, r => r.DocId == "RD-999999");
    }

    [Fact]
    public async Task DeletedRow_IsNotARelabel()
    {
        await Write();
        var relabeled = Email(Relevant, 0);
        var deleted = Email(Relevant, 1);
        EditCsv(relabeled, NotRelevant);
        await Write();

        // Removing rows is never a relabel, and never removes one (D-09)
        EditRows(_folder, false, rows => rows.RemoveAll(r => r.DocId == relabeled || r.DocId == deleted));
        await Write();

        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(relabeled, relabel.DocId);
        Assert.Equal(Relevant, KeyRow(CsvPath(_folder), false, deleted).Relevance);
        Assert.Equal(NotRelevant, KeyRow(CsvPath(_folder), false, relabeled).Relevance);
    }

    [Fact]
    public async Task InvalidRelevanceValue_Ignored()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, "Maybe");

        await Write();

        Assert.Empty(Sidecar(_folder).Relabels);
        Assert.Equal(Relevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
    }

    [Fact]
    public async Task GuardedValue_Accepted()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, "'Not Relevant");

        await Write();

        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(NotRelevant, relabel.Relevance);
        Assert.Equal(NotRelevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
    }

    [Fact]
    public async Task CustomRationale_KeptOnRelabeledRowOnly()
    {
        await Write();
        var custom = Email(Relevant, 0);
        var notRelabeled = Email(Relevant, 1);
        var emptyCell = Email(Relevant, 2);
        EditRows(_folder, false, rows =>
        {
            Edit(rows, custom, NotRelevant, "Counsel: not responsive");
            Edit(rows, notRelabeled, Relevant, "Edited without a relabel");
            Edit(rows, emptyCell, NotRelevant, string.Empty);
        });

        await Write();

        var csv = CsvPath(_folder);
        Assert.Equal("Counsel: not responsive", KeyRow(csv, false, custom).Rationale);
        Assert.Equal(ProjectedRationale(notRelabeled), KeyRow(csv, false, notRelabeled).Rationale);
        Assert.Equal(ProjectedRationale(emptyCell), KeyRow(csv, false, emptyCell).Rationale);
        Assert.Equal(NotRelevant, KeyRow(csv, false, emptyCell).Relevance);
        var relabels = Sidecar(_folder).Relabels.ToDictionary(r => r.DocId);
        Assert.Equal(2, relabels.Count);
        Assert.Equal(string.Empty, relabels[emptyCell].Rationale);

        // A Rationale-only edit on a row that is already relabeled is captured (D-23)
        EditXlsx(custom, NotRelevant, "Second thoughts");
        await Write();

        var updated = Sidecar(_folder).Relabels.Single(r => r.DocId == custom);
        Assert.Equal("Second thoughts", updated.Rationale);
        Assert.Equal(AnswerKeyXlsxWriter.FileName, updated.Source);
        Assert.Equal("Second thoughts", KeyRow(csv, false, custom).Rationale);
    }

    [Fact]
    public async Task AppendedRationale_IsUserEdit()
    {
        // CR-02: appending to the projected rationale is the most natural edit; it must not read as "unchanged"
        await Write();
        var docId = Email(Relevant, 0);
        var appended = ProjectedRationale(docId) + " Reviewer: actually only marginal, see 3/14 email.";
        EditCsv(docId, NotRelevant, appended);

        await Write();

        Assert.Equal(appended, Sidecar(_folder).Relabels.Single(r => r.DocId == docId).Rationale);
        Assert.Equal(appended, KeyRow(CsvPath(_folder), false, docId).Rationale);
        Assert.Equal(appended, KeyRow(XlsxPath(_folder), true, docId).Rationale);

        // A Rationale-only append on the relabeled row is captured too (D-23)
        var again = appended + " Second look.";
        EditXlsx(docId, NotRelevant, again);
        await Write();
        Assert.Equal(again, Sidecar(_folder).Relabels.Single(r => r.DocId == docId).Rationale);
    }

    [Fact]
    public async Task RunNoteRationale_NotAnEdit()
    {
        // Pitfall 6 with CR-02: the projected rationale plus the run note the file was written with is unchanged, even
        // when the next write's overlay has no note (or another one)
        var docId = Email(Relevant, 0);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[docId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusSkipped, Note = "Skipped: test." };
        await Write(overlay: overlay);
        Assert.Equal(ProjectedRationale(docId) + " Skipped: test.", KeyRow(CsvPath(_folder), false, docId).Rationale);

        EditXlsx(docId, NotRelevant);
        await Write();

        var relabel = Sidecar(_folder).Relabels.Single(r => r.DocId == docId);
        Assert.Equal(NotRelevant, relabel.Relevance);
        Assert.Equal(string.Empty, relabel.Rationale);
        Assert.Equal(ProjectedRationale(docId), KeyRow(CsvPath(_folder), false, docId).Rationale);
    }

    [Fact]
    public async Task MultiLineRationale_LineEndings_NotAnEdit()
    {
        // XLSX stores CRLF in a cell as LF: an untouched multi-line rationale must not read as a user edit
        var projected = Email(Relevant, 0);
        _plan.Threads.Single(t => t.Messages.Any(m => m.DocId == projected)).Rationale = "Line one\r\nLine two";
        await Write();

        EditXlsx(projected, NotRelevant);
        await Write();
        Assert.Equal(string.Empty, Sidecar(_folder).Relabels.Single(r => r.DocId == projected).Rationale);

        var customDoc = Email(Relevant, 1);
        EditCsv(customDoc, NotRelevant, "Counsel line one\r\nCounsel line two");
        await Write();
        var custom = Sidecar(_folder).Relabels.Single(r => r.DocId == customDoc);
        Assert.Equal("Counsel line one\nCounsel line two", custom.Rationale);
        Assert.Equal(AnswerKeyCsvWriter.FileName, custom.Source);

        // An unrelated edit saved from Excel carries both rows back with LF only
        EditXlsx(Email(Relevant, 2), NotRelevant);
        await Write();

        var relabels = Sidecar(_folder).Relabels.ToDictionary(r => r.DocId);
        Assert.Equal(3, relabels.Count);
        Assert.Equal(string.Empty, relabels[projected].Rationale);
        Assert.Equal("Counsel line one\nCounsel line two", relabels[customDoc].Rationale);
        Assert.Equal(AnswerKeyCsvWriter.FileName, relabels[customDoc].Source);
    }

    [Fact]
    public async Task OtherPlanId_Pruned()
    {
        var otherPlan = Guid.NewGuid();
        var docId = Email(Relevant, 0);

        // A key file whose snapshot belongs to another plan, with an edit in it
        var rows = Projection();
        Edit(rows, docId, NotRelevant, null);
        SaveCsv(_folder, rows);
        var seeded = new AnswerKeyRelabelSet();
        seeded.Relabels.Add(new AnswerKeyRelabel
        {
            PlanId = otherPlan,
            DocId = Email(Relevant, 1),
            Relevance = NotRelevant,
            Source = AnswerKeyCsvWriter.FileName
        });
        seeded.Files[AnswerKeyCsvWriter.FileName] = new AnswerKeyFileSnapshot { PlanId = otherPlan, Sha256 = "00" };
        new AnswerKeyRelabelStore(_folder).Save(seeded);

        var result = await Write();

        Assert.Equal(0, result.Relabels);
        var saved = Sidecar(_folder);
        Assert.Empty(saved.Relabels);
        Assert.All(saved.Files.Values, s => Assert.Equal(_plan.PlanId, s.PlanId));
        Assert.Equal(Relevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
        Assert.Equal(Relevant, KeyRow(CsvPath(_folder), false, Email(Relevant, 1)).Relevance);
    }

    [Fact]
    public async Task OtherPlanFile_NotOverwritten_KeepsItsSnapshot()
    {
        // WR-01: a re-plan approval writes the CSV but the XLSX is locked. The old plan's XLSX stays on disk, so its
        // snapshot must stay too; otherwise the next harvest takes it by legacy ownership and imports its labels
        var otherPlan = Guid.NewGuid();
        var docId = Email(Relevant, 0);
        var rows = Projection();
        Edit(rows, docId, NotRelevant, null);
        SaveCsv(_folder, rows);
        File.WriteAllBytes(XlsxPath(_folder), AnswerKeyXlsxWriter.Build(rows, _guide));
        var seeded = new AnswerKeyRelabelSet();
        seeded.Files[AnswerKeyCsvWriter.FileName] = new AnswerKeyFileSnapshot { PlanId = otherPlan, Sha256 = "00" };
        seeded.Files[AnswerKeyXlsxWriter.FileName] = new AnswerKeyFileSnapshot { PlanId = otherPlan, Sha256 = "00" };
        new AnswerKeyRelabelStore(_folder).Save(seeded);

        var first = await Write(writer: FailFor(".xlsx"));

        Assert.False(first.Succeeded);
        Assert.Equal(AnswerKeyXlsxWriter.FileName, first.FailedFileName);
        var saved = Sidecar(_folder);
        Assert.Equal(_plan.PlanId, saved.Files[AnswerKeyCsvWriter.FileName].PlanId);
        Assert.Equal(otherPlan, saved.Files[AnswerKeyXlsxWriter.FileName].PlanId);

        // The lock is gone: the old plan's label in the XLSX is still not a relabel of this plan
        var second = await Write();

        Assert.True(second.Succeeded);
        Assert.Equal(0, second.Relabels);
        Assert.Empty(Sidecar(_folder).Relabels);
        Assert.Equal(Relevant, KeyRow(CsvPath(_folder), false, docId).Relevance);
        Assert.Equal(Relevant, KeyRow(XlsxPath(_folder), true, docId).Relevance);
        Assert.All(Sidecar(_folder).Files.Values, s => Assert.Equal(_plan.PlanId, s.PlanId));
    }

    [Fact]
    public async Task OtherPlanSnapshot_FileGone_Pruned()
    {
        // WR-01: a snapshot whose file no longer exists says nothing; it is dropped
        var seeded = new AnswerKeyRelabelSet();
        seeded.Files[AnswerKeyXlsxWriter.FileName] = new AnswerKeyFileSnapshot { PlanId = Guid.NewGuid(), Sha256 = "00" };
        new AnswerKeyRelabelStore(_folder).Save(seeded);

        await Write(writer: FailFor(".xlsx"));

        Assert.False(File.Exists(XlsxPath(_folder)));
        Assert.False(Sidecar(_folder).Files.ContainsKey(AnswerKeyXlsxWriter.FileName));
    }

    [Fact]
    public async Task LegacyFile_NoSidecar()
    {
        // Written before Phase 10: plain projection, no sidecar
        var docId = Email(Relevant, 0);
        var rows = Projection();
        Edit(rows, docId, NotRelevant, null);
        SaveCsv(_folder, rows);

        await Write();

        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(docId, relabel.DocId);
        Assert.Equal(AnswerKeyCsvWriter.FileName, relabel.Source);

        // A legacy file whose DocIDs do not match the plan belongs to another plan
        var other = NewFolder("other");
        var mismatched = Projection();
        mismatched.RemoveAt(mismatched.Count - 1);
        Edit(mismatched, docId, NotRelevant, null);
        SaveCsv(other, mismatched);

        await Write(other);

        Assert.Empty(Sidecar(other).Relabels);
        Assert.Equal(Relevant, KeyRow(CsvPath(other), false, docId).Relevance);
    }

    [Fact]
    public async Task DamagedCsv_BackedUp_NewFileWritten()
    {
        await Write();
        var garbage = Encoding.UTF8.GetBytes("this is not an answer key\r\nat all\r\n");
        File.WriteAllBytes(CsvPath(_folder), garbage);

        var result = await Write();

        Assert.True(result.Succeeded);
        var backup = Assert.Single(result.Backups);
        Assert.Equal(AnswerKeyCsvWriter.FileName, backup.FileName);
        Assert.StartsWith("answer-key (unreadable ", backup.BackupFileName);
        Assert.EndsWith(").csv", backup.BackupFileName);
        Assert.Equal(garbage, File.ReadAllBytes(Path.Combine(_folder, backup.BackupFileName)));
        Assert.True(Parse(CsvPath(_folder), false).Parsed);
    }

    [Fact]
    public async Task LockedOnRead_NotWritten()
    {
        await Write();
        var before = File.ReadAllBytes(CsvPath(_folder));
        var docId = Email(Relevant, 0);
        EditXlsx(docId, NotRelevant);

        AnswerKeyWriteResult result;
        using (new FileStream(CsvPath(_folder), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await Write();

        Assert.Equal(AnswerKeyCsvWriter.FileName, result.FailedFileName);
        Assert.True(result.FileOpenElsewhere);
        var written = Assert.Single(result.Files);
        Assert.Equal(CorpusFileKind.AnswerKeyXlsx, written.Kind);
        Assert.Equal(before, File.ReadAllBytes(CsvPath(_folder)));
        Assert.Equal(NotRelevant, KeyRow(XlsxPath(_folder), true, docId).Relevance);
    }

    [Fact]
    public async Task PeekRelabels_DryHarvest()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, NotRelevant);

        var paths = new[] { CsvPath(_folder), XlsxPath(_folder), new AnswerKeyRelabelStore(_folder).FilePath };
        var before = paths.Select(p => (Bytes: File.ReadAllBytes(p), Time: File.GetLastWriteTimeUtc(p))).ToList();

        var peeked = AnswerKeyFiles.PeekRelabels(_folder, _plan, _guide);

        var relabel = Assert.Single(peeked.Values);
        Assert.Equal(docId, relabel.DocId);
        Assert.Equal(NotRelevant, relabel.Relevance);
        for (var i = 0; i < paths.Length; i++)
        {
            Assert.Equal(before[i].Bytes, File.ReadAllBytes(paths[i]));
            Assert.Equal(before[i].Time, File.GetLastWriteTimeUtc(paths[i]));
        }

        Assert.Empty(Sidecar(_folder).Relabels);
    }

    [Fact]
    public async Task Concurrency_SerializedPerFolder()
    {
        await Write();
        var docId = Email(Relevant, 0);
        EditCsv(docId, NotRelevant);

        var results = await Task.WhenAll(Task.Run(() => Write()), Task.Run(() => Write()));

        Assert.All(results, r => Assert.True(r.Succeeded));
        using (JsonDocument.Parse(File.ReadAllText(new AnswerKeyRelabelStore(_folder).FilePath)))
        {
            // Valid JSON
        }

        var relabel = Assert.Single(Sidecar(_folder).Relabels);
        Assert.Equal(docId, relabel.DocId);
    }

    [Fact]
    public void Apply_RelabelsOnlyTheDocId()
    {
        var docId = Email(Relevant, 0);
        var relabels = new Dictionary<string, AnswerKeyRelabel>
        {
            [docId] = new() { PlanId = _plan.PlanId, DocId = docId, Relevance = NotRelevant, Rationale = "Custom" }
        };

        var rows = AnswerKeyFiles.Apply(Projection(), relabels);

        var changed = rows.Where(r => r.Relevance != Projection().Single(p => p.DocId == r.DocId).Relevance).ToList();
        var row = Assert.Single(changed);
        Assert.Equal(docId, row.DocId);
        Assert.Equal("Custom", row.Rationale);
    }

    [Fact]
    public void BuildBytes_NoRelabels_EqualsPlanProjection()
    {
        // KEY-03: without relabels the funnel writes exactly the builder's projection
        var (csv, _) = AnswerKeyFiles.BuildBytes(_plan, _guide, null, null);

        Assert.Equal(AnswerKeyCsvWriter.Build(Projection()), csv);
    }
}
