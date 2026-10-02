using System.Text;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Serializes answer-key rows to RFC 4180 CSV (CRLF, UTF-8 with BOM) with the OWASP CSV Injection guard on every field.
/// Pure: returns bytes only; the UI writes them via CaseStore.WriteAtomicAsync (KEY-01).
/// </summary>
public static class AnswerKeyCsvWriter
{
    public const string FileName = "answer-key.csv";

    // Source: OWASP CSV Injection; leading characters spreadsheets treat as a formula (incl. full-width variants)
    private static readonly char[] FormulaLeads = { '=', '+', '-', '@', '\t', '\r', '\n', '＝', '＋', '－', '＠' };

    private static readonly char[] QuoteTriggers = { ',', '"', '\r', '\n' };

    /// <summary>
    /// One CSV field: formula leads get a ' prefix and quotes; commas, quotes, CR and LF force quotes with doubled quotes.
    /// </summary>
    public static string Field(string? value)
    {
        var text = value ?? string.Empty;
        var guarded = text.Length > 0 && Array.IndexOf(FormulaLeads, text[0]) >= 0;
        if (guarded)
            text = "'" + text;
        var needsQuotes = guarded || text.IndexOfAny(QuoteTriggers) >= 0;
        return needsQuotes ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    /// <summary>
    /// Header line plus one line per row, every line terminated by "\r\n"; bytes are the UTF-8 BOM followed by the body.
    /// </summary>
    public static byte[] Build(IReadOnlyList<AnswerKeyRow> rows)
    {
        var sb = new StringBuilder();
        AppendLine(sb, AnswerKeyColumns.Headers);
        foreach (var row in rows)
        {
            if (row != null)
                AppendLine(sb, row.ToValues());
        }

        var preamble = new UTF8Encoding(true).GetPreamble();
        var body = new UTF8Encoding(false).GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static void AppendLine(StringBuilder sb, IEnumerable<string> values)
    {
        sb.Append(string.Join(",", values.Select(Field)));
        sb.Append("\r\n");
    }
}
