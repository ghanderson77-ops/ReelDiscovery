using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Turns journaled ledger deltas into stable ledger entries (D-08, CORP-08). The ledger is a pure projection of the
/// deltas: same plan, waves and deltas give the same ledger (Pattern 7).
/// </summary>
public static partial class LedgerMerger
{
    public const int MaxEntriesPerThread = 12;

    private const int MaxAbout = 120;
    private const int MaxStatement = 300;
    private const int MaxValue = 120;

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "MMMM d, yyyy" };
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Entries for one rendered thread: mapped by seq (unknown seqs dropped), text cleaned of id tokens and capped,
    /// FactIds limited to the thread's gated facts, duplicates merged, at most <see cref="MaxEntriesPerThread"/> (T-05-09).
    /// Ids are T-xxxx-Lnn in kept order.
    /// </summary>
    public static List<LedgerEntry> EntriesFor(PlannedThread thread, IReadOnlyList<LedgerDelta> deltas, CaseGuide guide, int waveIndex)
    {
        var kept = new List<LedgerEntry>();
        if (thread == null || deltas == null)
            return kept;

        var bySeq = (thread.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .GroupBy(m => m.Seq)
            .ToDictionary(g => g.Key, g => g.First());

        var allowed = new HashSet<string>(KnowledgeGate.Build(guide, thread).Facts.Select(f => f.Id), StringComparer.Ordinal);
        allowed.UnionWith((thread.FactIds ?? new List<string>()).Select(RelevantThreadFiller.CanonicalId));

        var knownBySeq = new Dictionary<int, List<string>>();
        foreach (var delta in deltas)
        {
            if (delta == null || !bySeq.TryGetValue(delta.Seq, out var message))
                continue;

            var statement = Clean(delta.Statement, MaxStatement);
            if (statement.Length == 0)
                continue;

            var factIds = (delta.FactIds ?? new List<string>())
                .Select(RelevantThreadFiller.CanonicalId)
                .Where(allowed.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (!knownBySeq.TryGetValue(message.Seq, out var knownBy))
            {
                knownBy = KnownBy(thread, message);
                knownBySeq[message.Seq] = knownBy;
            }

            var entry = new LedgerEntry
            {
                Date = MessageTime.LocalDate(message).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Kind = Enum.IsDefined(delta.Kind) ? delta.Kind : LedgerKind.Other,
                About = Clean(delta.About, MaxAbout),
                Statement = statement,
                Value = Clean(delta.Value, MaxValue),
                KnownByIds = new List<string>(knownBy),
                FactIds = factIds,
                IssueIds = IssuesOf(factIds, guide),
                SourceDocId = message.DocId ?? string.Empty,
                SourceThreadId = thread.Id ?? string.Empty,
                WaveIndex = waveIndex
            };

            var duplicate = kept.FirstOrDefault(e => SameDetail(e, entry));
            if (duplicate != null)
            {
                Absorb(duplicate, entry, guide);
                continue;
            }

            if (kept.Count >= MaxEntriesPerThread)
                continue;
            kept.Add(entry);
        }

        for (var i = 0; i < kept.Count; i++)
            kept[i].Id = string.Format(CultureInfo.InvariantCulture, "{0}-L{1:00}", thread.Id, i + 1);
        return kept;
    }

    /// <summary>
    /// The whole ledger from journaled deltas: threads in <paramref name="waveByThread"/> only, ordered by wave, then
    /// first-message UTC, then id; a detail already established by an earlier thread is not repeated, its KnownByIds
    /// absorb the later thread's knowers. Ids are never renumbered.
    /// </summary>
    public static NarrativeLedger Build(CorpusPlan plan, CaseGuide guide, IReadOnlyDictionary<string, int> waveByThread,
        IReadOnlyDictionary<string, List<LedgerDelta>> deltasByThread)
    {
        var ledger = new NarrativeLedger();
        if (plan?.Threads == null || waveByThread == null)
            return ledger;

        var threads = plan.Threads
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id) && waveByThread.ContainsKey(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(t => waveByThread[t.Id])
            .ThenBy(MessageTime.FirstUtc)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        // Same Kind + normalized About -> earlier entries that may hold the same value
        var index = new Dictionary<string, List<LedgerEntry>>(StringComparer.Ordinal);
        foreach (var thread in threads)
        {
            if (deltasByThread == null || !deltasByThread.TryGetValue(thread.Id, out var deltas) || deltas == null)
                continue;

            foreach (var entry in EntriesFor(thread, deltas, guide, waveByThread[thread.Id]))
            {
                var key = DetailKey(entry);
                if (!index.TryGetValue(key, out var candidates))
                {
                    candidates = new List<LedgerEntry>();
                    index[key] = candidates;
                }

                var earlier = candidates.FirstOrDefault(e => SameValue(e.Value, entry.Value));
                if (earlier != null)
                {
                    earlier.KnownByIds = Union(earlier.KnownByIds, entry.KnownByIds);
                    continue;
                }

                if (ledger.Entries.Count >= NarrativeLedger.MaxEntries)
                    return ledger;
                candidates.Add(entry);
                ledger.Entries.Add(entry);
            }
        }

        return ledger;
    }

    /// <summary>
    /// Lower-case letters and digits separated by single spaces; everything else is a separator.
    /// </summary>
    internal static string NormalizeAbout(string text)
    {
        var sb = new StringBuilder((text ?? string.Empty).Length);
        foreach (var c in (text ?? string.Empty).ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return WhitespacePattern().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>
    /// Both empty, equal amounts (CanonicalValueScanner.AmountsEqual, which also compares text case-insensitively),
    /// or the same calendar date written as yyyy-MM-dd or "MMMM d, yyyy".
    /// </summary>
    internal static bool SameValue(string a, string b)
    {
        a = (a ?? string.Empty).Trim();
        b = (b ?? string.Empty).Trim();
        if (a.Length == 0 && b.Length == 0)
            return true;
        if (a.Length == 0 || b.Length == 0)
            return false;
        if (CanonicalValueScanner.AmountsEqual(a, b))
            return true;
        return TryParseDate(a, out var left) && TryParseDate(b, out var right) && left == right;
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormats, EnUs, DateTimeStyles.None, out date);

    // About decides identity; when the model left it empty the statement stands in
    private static string DetailKey(LedgerEntry entry)
    {
        var about = NormalizeAbout(entry.About);
        if (about.Length == 0)
            about = "~" + NormalizeAbout(entry.Statement);
        return ((int)entry.Kind).ToString(CultureInfo.InvariantCulture) + "|" + about;
    }

    private static bool SameDetail(LedgerEntry a, LedgerEntry b) =>
        string.Equals(DetailKey(a), DetailKey(b), StringComparison.Ordinal) && SameValue(a.Value, b.Value);

    private static void Absorb(LedgerEntry target, LedgerEntry source, CaseGuide guide)
    {
        target.KnownByIds = Union(target.KnownByIds, source.KnownByIds);
        target.FactIds = target.FactIds.Concat(source.FactIds).Distinct(StringComparer.Ordinal).ToList();
        target.IssueIds = IssuesOf(target.FactIds, guide);
    }

    private static List<string> Union(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Concat(b).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    // Source participants plus the participants of every later message that quotes or forwards it
    private static List<string> KnownBy(PlannedThread thread, PlannedMessage source)
    {
        var people = new HashSet<string>(Participants(source), StringComparer.Ordinal);
        foreach (var message in thread.Messages.Where(m => m != null && m.Seq != source.Seq))
        {
            if (KnowledgeGate.Ancestors(thread, message.Seq).Contains(source.Seq))
                people.UnionWith(Participants(message));
        }

        return people.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> Participants(PlannedMessage message) =>
        new[] { message.FromId }
            .Concat(message.ToIds ?? new List<string>())
            .Concat(message.CcIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim());

    // Issue ids of the facts, first appearance order (AnswerKeyBuilder.IssuesOf analog)
    private static List<string> IssuesOf(List<string> factIds, CaseGuide guide) =>
        factIds
            .Select(id => (guide?.Facts ?? new List<KeyFact>()).FirstOrDefault(f => f != null && f.Id == id))
            .Where(f => f != null)
            .SelectMany(f => f!.IssueIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // Ledger text is prose: every id token is stripped (ids live only in the structured fields), CR/LF collapsed, length capped
    private static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var stripped = IdTokenPattern().Replace(text, string.Empty);
        var collapsed = WhitespacePattern().Replace(stripped, " ").Trim();
        return collapsed.Length > max ? collapsed[..max].TrimEnd() : collapsed;
    }

    [GeneratedRegex(@"\b(?:CH|F|ISS|T|V|ORG|EXT)-\d{2,4}\b")]
    private static partial Regex IdTokenPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
