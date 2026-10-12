using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Raw bytes of one answer-key file, read with sharing so a file Excel holds open is still read (D-03).
/// </summary>
public class AnswerKeyFileRead
{
    public bool Exists { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public DateTime LastWriteUtc { get; set; }

    /// <summary>IOException or UnauthorizedAccessException while the file exists (e.g. locked without read sharing).</summary>
    public Exception? ReadError { get; set; }

    /// <summary>Larger than <see cref="AnswerKeyReader.MaxFileBytes"/>; not read into memory.</summary>
    public bool TooLarge { get; set; }
}

/// <summary>
/// The columns of one answer-key row that the relabel round-trip needs; values are as found in the file.
/// </summary>
public class AnswerKeyReadRow
{
    public string DocId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string Relevance { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
}

public class AnswerKeyParseResult
{
    public bool Parsed { get; set; }
    public string Error { get; set; } = string.Empty;
    public List<AnswerKeyReadRow> Rows { get; set; } = new();
}

/// <summary>
/// Reads DocID, Thread ID, Relevance and Rationale back from answer-key.csv and the "Answer Key" sheet of
/// answer-key.xlsx, in every variant Excel saves (D-03). Parsing never throws: a damaged file is Parsed = false.
/// </summary>
public static class AnswerKeyReader
{
    public const long MaxFileBytes = 100L * 1024 * 1024;

    private const string MissingColumnsError = "The DocID or Relevance column is missing.";

    // Excel's last column is XFD; longer letter runs are not a cell reference
    private const int MaxColumnLetters = 3;

    private static readonly char[] SeparatorCandidates = { ',', ';', '\t' };

    private static string DocIdHeader => AnswerKeyColumns.Headers[0];
    private static string ThreadIdHeader => AnswerKeyColumns.Headers[11];
    private static string RelevanceHeader => AnswerKeyColumns.Headers[14];
    private static string RationaleHeader => AnswerKeyColumns.Headers[21];

    /// <summary>
    /// Reads the whole file into memory with FileShare.ReadWrite | FileShare.Delete. Never throws for I/O failures:
    /// a missing file is Exists = false, a failed read is ReadError, an oversized file is TooLarge.
    /// </summary>
    public static AnswerKeyFileRead ReadShared(string path)
    {
        if (!File.Exists(path))
            return new AnswerKeyFileRead { Exists = false };

        var read = new AnswerKeyFileRead { Exists = true };
        try
        {
            var info = new FileInfo(path);
            read.LastWriteUtc = info.LastWriteTimeUtc;
            if (info.Length > MaxFileBytes)
            {
                read.TooLarge = true;
                return read;
            }

            // Excel keeps the file open; FileShare.ReadWrite lets us read it anyway (D-03). Never the path-based read
            // APIs: they open with FileShare.Read
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            read.Bytes = memory.ToArray();
            return read;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted between the existence check and the open: same as never there
            return new AnswerKeyFileRead { Exists = false };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            read.ReadError = ex;
            read.Bytes = Array.Empty<byte>();
            return read;
        }
    }

    /// <summary>
    /// "Relevant" or "Not Relevant" (trimmed, case-insensitive, one leading CSV guard apostrophe stripped); any other
    /// value is null and ignored (D-05).
    /// </summary>
    public static string? NormalizeRelevance(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value.StartsWith('\''))
            value = value[1..].Trim();

        if (string.Equals(value, AnswerKeyBuilder.RelevantLabel, StringComparison.OrdinalIgnoreCase))
            return AnswerKeyBuilder.RelevantLabel;
        if (string.Equals(value, AnswerKeyBuilder.NotRelevantLabel, StringComparison.OrdinalIgnoreCase))
            return AnswerKeyBuilder.NotRelevantLabel;
        return null;
    }

    /// <summary>
    /// Removes the ' that AnswerKeyCsvWriter put in front of a formula lead; any other leading ' is the user's text.
    /// </summary>
    public static string StripCsvGuard(string? value)
    {
        var text = value ?? string.Empty;
        return AnswerKeyCsvWriter.IsGuarded(text) ? text[1..] : text;
    }

    /// <summary>
    /// RFC 4180 CSV in UTF-8 (with or without BOM), UTF-16 with BOM or ANSI; ',' ';' or tab separated, optional
    /// "sep=" first line, quoted multi-line fields. Quoted text is never trimmed.
    /// </summary>
    public static AnswerKeyParseResult ParseCsv(byte[] bytes)
    {
        try
        {
            if (bytes == null || bytes.Length == 0)
                return Fail("The file is empty.");

            var text = Decode(bytes);
            var position = 0;
            char separator;
            if (TrySepLine(text, ref position, out var declared))
            {
                separator = declared;
            }
            else
            {
                separator = '\0';
                foreach (var candidate in SeparatorCandidates)
                {
                    var probe = 0;
                    var first = ReadRecord(text, ref probe, candidate);
                    if (first != null && HasRequiredColumns(first))
                    {
                        separator = candidate;
                        break;
                    }
                }

                if (separator == '\0')
                    return Fail(MissingColumnsError);
            }

            var records = new List<List<string>>();
            List<string>? record;
            while ((record = ReadRecord(text, ref position, separator)) != null)
                records.Add(record);

            return FromTable(records);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Damaged files are backed up by the funnel (D-10); never throw
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// The "Answer Key" sheet (or the first sheet with DocID and Relevance headers) of a workbook: inline strings (our
    /// writer), shared strings (Excel save) and formula strings. Phonetic runs are not part of the text.
    /// </summary>
    public static AnswerKeyParseResult ParseXlsx(byte[] bytes)
    {
        try
        {
            if (bytes == null || bytes.Length == 0)
                return Fail("The file is empty.");

            using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes, writable: false), false);
            var workbookPart = doc.WorkbookPart;
            var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? new List<Sheet>();
            if (workbookPart == null || sheets.Count == 0)
                return Fail("The workbook has no sheets.");

            var shared = workbookPart.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ToList()
                         ?? new List<SharedStringItem>();

            // Named sheet first, then any other sheet whose header row has DocID and Relevance
            var ordered = sheets
                .OrderBy(s => string.Equals(s.Name?.Value, AnswerKeyXlsxWriter.SheetNames[0],
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();

            foreach (var sheet in ordered)
            {
                var id = sheet.Id?.Value;
                if (string.IsNullOrEmpty(id) || workbookPart.GetPartById(id) is not WorksheetPart part)
                    continue;

                var table = ReadGrid(part, shared);
                if (table.Count > 0 && HasRequiredColumns(table[0]))
                    return FromTable(table);
            }

            return Fail(MissingColumnsError);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Damaged files are backed up by the funnel (D-10); never throw
            return Fail(ex.Message);
        }
    }

    private static AnswerKeyParseResult Fail(string error) => new() { Parsed = false, Error = error };

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Excel "CSV (Comma delimited)" saves in the ANSI code page; Latin1 decodes every byte and keeps
            // DocID/Relevance (A2, A4)
            return Encoding.Latin1.GetString(bytes);
        }
    }

    // Excel writes "sep=;" as the first line when the user's list separator differs from the file's
    private static bool TrySepLine(string text, ref int position, out char separator)
    {
        separator = '\0';
        var end = text.IndexOfAny(new[] { '\r', '\n' });
        var line = end < 0 ? text : text[..end];
        if (line.Length != 5 || !line.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
            return false;

        separator = line[4];
        position = end < 0 ? text.Length : end + 1;
        if (end >= 0 && text[end] == '\r' && position < text.Length && text[position] == '\n')
            position++;
        return true;
    }

    // One RFC 4180 record starting at position; null at the end of the text
    private static List<string>? ReadRecord(string text, ref int position, char separator)
    {
        if (position >= text.Length)
            return null;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStart = true;

        while (position < text.Length)
        {
            var c = text[position];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (position + 1 < text.Length && text[position + 1] == '"')
                    {
                        field.Append('"');
                        position += 2;
                        continue;
                    }

                    inQuotes = false;
                    position++;
                    continue;
                }

                // CR/LF inside quotes are part of the field
                field.Append(c);
                position++;
                continue;
            }

            if (c == '"' && fieldStart)
            {
                inQuotes = true;
                fieldStart = false;
                position++;
                continue;
            }

            if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
                fieldStart = true;
                position++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                position++;
                if (c == '\r' && position < text.Length && text[position] == '\n')
                    position++;
                fields.Add(field.ToString());
                return fields;
            }

            field.Append(c);
            fieldStart = false;
            position++;
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static int ColumnOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool HasRequiredColumns(IReadOnlyList<string> header) =>
        ColumnOf(header, DocIdHeader) >= 0 && ColumnOf(header, RelevanceHeader) >= 0;

    // First record is the header; columns are found by name, never by position
    private static AnswerKeyParseResult FromTable(List<List<string>> table)
    {
        if (table.Count == 0)
            return Fail("The file is empty.");

        var header = table[0];
        var docId = ColumnOf(header, DocIdHeader);
        var relevance = ColumnOf(header, RelevanceHeader);
        if (docId < 0 || relevance < 0)
            return Fail(MissingColumnsError);
        var threadId = ColumnOf(header, ThreadIdHeader);
        var rationale = ColumnOf(header, RationaleHeader);

        var result = new AnswerKeyParseResult { Parsed = true };
        for (var i = 1; i < table.Count; i++)
        {
            var record = table[i];
            var id = Cell(record, docId).Trim();
            if (id.Length == 0)
                continue;

            var text = Cell(record, rationale);
            if (text.Length > AnswerKeyXlsxWriter.MaxCellLength)
                text = text[..AnswerKeyXlsxWriter.MaxCellLength];

            result.Rows.Add(new AnswerKeyReadRow
            {
                DocId = id,
                ThreadId = Cell(record, threadId),
                Relevance = Cell(record, relevance),
                Rationale = text
            });
        }

        return result;
    }

    private static string Cell(IReadOnlyList<string> record, int index) =>
        index >= 0 && index < record.Count ? record[index] ?? string.Empty : string.Empty;

    // Rows in document order; each row's cells placed by the column letters of CellReference
    private static List<List<string>> ReadGrid(WorksheetPart part, List<SharedStringItem> shared)
    {
        var table = new List<List<string>>();
        var sheetData = part.Worksheet?.GetFirstChild<SheetData>();
        if (sheetData == null)
            return table;

        foreach (var row in sheetData.Elements<Row>())
        {
            var values = new List<string>();
            var next = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                var column = ColumnIndex(cell.CellReference?.Value);
                if (column < 0)
                    column = next;
                while (values.Count <= column)
                    values.Add(string.Empty);
                values[column] = CellText(cell, shared);
                next = column + 1;
            }

            table.Add(values);
        }

        return table;
    }

    // "A1" -> 0, "Z9" -> 25, "AA2" -> 26; -1 when there is no usable reference
    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return -1;

        var index = 0;
        var letters = 0;
        foreach (var c in reference)
        {
            var upper = char.ToUpperInvariant(c);
            if (upper < 'A' || upper > 'Z')
                break;
            if (++letters > MaxColumnLetters)
                return -1;
            index = index * 26 + (upper - 'A' + 1);
        }

        return letters == 0 ? -1 : index - 1;
    }

    private static string CellText(Cell cell, List<SharedStringItem> shared)
    {
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString)
        {
            return int.TryParse(cell.CellValue?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                   && i >= 0 && i < shared.Count
                ? TextOf(shared[i])
                : string.Empty;
        }

        if (type == CellValues.InlineString)
            return cell.InlineString == null ? string.Empty : TextOf(cell.InlineString);

        // Formula strings ("str"), numbers and booleans keep their cached value
        return cell.CellValue?.Text ?? string.Empty;
    }

    // Plain and rich-text runs; phonetic (furigana) runs are a reading aid, not cell text
    private static string TextOf(OpenXmlElement element) =>
        string.Concat(element.Descendants<Text>()
            .Where(t => !t.Ancestors<PhoneticRun>().Any())
            .Select(t => t.Text));
}
