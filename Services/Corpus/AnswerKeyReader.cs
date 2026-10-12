namespace ReelDiscovery.Services.Corpus;

public class AnswerKeyFileRead
{
    public bool Exists { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public DateTime LastWriteUtc { get; set; }
    public Exception? ReadError { get; set; }
    public bool TooLarge { get; set; }
}

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

public static class AnswerKeyReader
{
    public const long MaxFileBytes = 100L * 1024 * 1024;

    public static AnswerKeyFileRead ReadShared(string path) => throw new NotImplementedException();

    public static AnswerKeyParseResult ParseCsv(byte[] bytes) => throw new NotImplementedException();

    public static AnswerKeyParseResult ParseXlsx(byte[] bytes) => throw new NotImplementedException();

    public static string? NormalizeRelevance(string? raw) => throw new NotImplementedException();

    public static string StripCsvGuard(string? value) => throw new NotImplementedException();
}
