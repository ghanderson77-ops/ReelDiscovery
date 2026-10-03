using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// The slice of the narrative ledger a thread may see (D-09, CORP-09): earlier waves only, dated before the thread
/// starts, known to someone on it, ranked by relevance and capped so the prompt stays bounded (T-05-11).
/// </summary>
public static class LedgerSelector
{
    // D-09 allows 25-40; A3
    public const int MaxLedgerEntries = 30;

    /// <summary>
    /// Visible entries ranked by shared FactIds, then shared IssueIds, then recency (then Id), capped at
    /// <paramref name="cap"/> and returned in chronological order (Date, then Id).
    /// </summary>
    public static List<LedgerEntry> Select(IReadOnlyList<LedgerEntry> ledger, PlannedThread thread, int waveIndex,
        IReadOnlyCollection<string> participants, int cap = MaxLedgerEntries)
    {
        if (ledger == null || thread == null || cap <= 0)
            return new List<LedgerEntry>();

        var people = new HashSet<string>(participants ?? Array.Empty<string>(), StringComparer.Ordinal);
        var firstDate = MessageTime.FirstLocalDate(thread);

        var threadFacts = new HashSet<string>(thread.FactIds ?? new List<string>(), StringComparer.Ordinal);
        foreach (var message in thread.Messages ?? new List<PlannedMessage>())
            if (message?.FactIds != null)
                threadFacts.UnionWith(message.FactIds);
        var threadIssues = new HashSet<string>(thread.IssueIds ?? new List<string>(), StringComparer.Ordinal);

        return ledger
            .Where(e => e != null && e.WaveIndex < waveIndex)
            .Select(e => (Entry: e, Date: e.ParsedDate))
            .Where(x => x.Date != null && x.Date.Value < firstDate)
            .Where(x => (x.Entry.KnownByIds ?? new List<string>()).Any(people.Contains))
            .OrderByDescending(x => Shared(x.Entry.FactIds, threadFacts))
            .ThenByDescending(x => Shared(x.Entry.IssueIds, threadIssues))
            .ThenByDescending(x => x.Date)
            .ThenBy(x => x.Entry.Id, StringComparer.Ordinal)
            .Take(cap)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Entry.Id, StringComparer.Ordinal)
            .Select(x => x.Entry)
            .ToList();
    }

    private static int Shared(List<string>? ids, HashSet<string> set) =>
        (ids ?? new List<string>()).Distinct(StringComparer.Ordinal).Count(set.Contains);
}
