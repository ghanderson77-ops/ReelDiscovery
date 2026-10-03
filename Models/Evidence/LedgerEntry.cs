using System.Globalization;
using System.Text.Json.Serialization;

namespace ReelDiscovery.Models.Evidence;

public enum LedgerKind { Commitment, Figure, Meeting, Decision, Person, Document, Other }

/// <summary>
/// One concrete detail a written email established (D-08); later threads see earlier-wave entries so the story stays consistent.
/// </summary>
public class LedgerEntry
{
    // "T-0042-L03" (code-owned, never from the model)
    public string Id { get; set; } = string.Empty;

    // yyyy-MM-dd, sender-local date of the source message
    public string Date { get; set; } = string.Empty;

    public LedgerKind Kind { get; set; }

    // Who or what the detail is about
    public string About { get; set; } = string.Empty;

    // One-line statement of the detail
    public string Statement { get; set; } = string.Empty;

    // Exact value (amount, date, name) later emails must repeat verbatim
    public string Value { get; set; } = string.Empty;

    // CH-xxx / EXT-xxx people who saw the source email
    public List<string> KnownByIds { get; set; } = new();
    public List<string> FactIds { get; set; } = new();
    public List<string> IssueIds { get; set; } = new();

    public string SourceDocId { get; set; } = string.Empty;
    public string SourceThreadId { get; set; } = string.Empty;

    // 1-based wave of the source thread; LedgerSelector shows only earlier waves (D-11)
    public int WaveIndex { get; set; }

    [JsonIgnore]
    public DateOnly? ParsedDate =>
        DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
