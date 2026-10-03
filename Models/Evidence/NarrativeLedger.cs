using System.Globalization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// The running record of concrete details the generated evidence has established (D-08); saved in case.json (D-10).
/// </summary>
public class NarrativeLedger
{
    // Hard cap so a hand-edited case.json cannot balloon memory (T-05-02)
    public const int MaxEntries = 20_000;

    public List<LedgerEntry> Entries { get; set; } = new();

    /// <summary>
    /// Repairs a ledger loaded from untrusted case.json; never throws.
    /// </summary>
    public void Normalize()
    {
        Entries = CaseGuide.CleanList(Entries);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<LedgerEntry>();

        foreach (var entry in Entries)
        {
            entry.Id ??= string.Empty;
            entry.Date ??= string.Empty;
            entry.About ??= string.Empty;
            entry.Statement ??= string.Empty;
            entry.Value ??= string.Empty;
            entry.SourceDocId ??= string.Empty;
            entry.SourceThreadId ??= string.Empty;
            entry.KnownByIds = CaseGuide.CleanStrings(entry.KnownByIds);
            entry.FactIds = CaseGuide.CleanStrings(entry.FactIds);
            entry.IssueIds = CaseGuide.CleanStrings(entry.IssueIds);
            if (!Enum.IsDefined(entry.Kind))
                entry.Kind = LedgerKind.Other;
            if (entry.WaveIndex < 0)
                entry.WaveIndex = 0;

            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Statement))
                continue;
            if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                continue;
            if (!seen.Add(entry.Id))
                continue;

            kept.Add(entry);
            if (kept.Count >= MaxEntries)
                break;
        }

        Entries = kept;
    }
}
