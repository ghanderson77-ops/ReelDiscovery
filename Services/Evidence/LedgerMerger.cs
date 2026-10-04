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

    // D-18: per attachment, so one attachment's repair never pushes another's details out (review concern 7)
    public const int MaxEntriesPerAttachment = 3;

    private const int MaxAbout = 120;
    private const int MaxStatement = 300;
    private const int MaxValue = 120;

    private static readonly string[] DateFormats = { "yyyy-MM-dd", "MMMM d, yyyy" };
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Entries for one rendered thread: mapped by seq (unknown seqs dropped), text cleaned of id tokens and capped,
    /// FactIds limited to the thread's gated facts, duplicates merged, at most <see cref="MaxEntriesPerThread"/> (T-05-09).
    /// Ids are T-xxxx-Lnn in kept order. Attachment details (D-18) follow every thread entry, grouped by AttachmentDocId in
    /// ordinal order, at most <see cref="MaxEntriesPerAttachment"/> each, with ids {AttachmentDocId}-Lnn in the group's own
    /// order; one that repeats a thread detail is absorbed into it (KnownByIds only), never across attachments.
    /// </summary>
    public static List<LedgerEntry> EntriesFor(PlannedThread thread, IReadOnlyList<LedgerDelta> deltas, CaseGuide guide, int waveIndex)
    {
        var (own, attachments) = Partition(thread, deltas, guide, waveIndex);
        own.AddRange(attachments);
        return own;
    }

    /// <summary>
    /// The whole ledger from journaled deltas: threads in <paramref name="waveByThread"/> only, ordered by wave, then
    /// first-message UTC, then id; a detail already established by an earlier entry is not repeated, its KnownByIds
    /// absorb the later entry's knowers. Every thread's own entries come first, then attachment entries in the same thread
    /// order. Ids are never renumbered.
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

        var own = new List<List<LedgerEntry>>();
        var attachments = new List<List<LedgerEntry>>();
        foreach (var thread in threads)
        {
            if (deltasByThread == null || !deltasByThread.TryGetValue(thread.Id, out var deltas) || deltas == null)
                continue;

            var (threadEntries, attachmentEntries) = Partition(thread, deltas, guide, waveByThread[thread.Id]);
            own.Add(threadEntries);
            attachments.Add(attachmentEntries);
        }

        // Same Kind + normalized About -> earlier entries that may hold the same value
        var index = new Dictionary<string, List<LedgerEntry>>(StringComparer.Ordinal);
        var fromAttachment = new HashSet<LedgerEntry>(ReferenceEqualityComparer.Instance);

        // Thread entries first across the ledger: a late attachment can never displace or absorb an entry later emails already cite
        foreach (var entry in own.SelectMany(e => e))
        {
            if (!Place(ledger, index, entry, e => true))
                return ledger;
        }

        foreach (var entry in attachments.SelectMany(e => e))
        {
            // Within one thread, attachments never absorb each other (same rule as EntriesFor)
            if (!Place(ledger, index, entry, e => !(fromAttachment.Contains(e)
                    && string.Equals(e.SourceThreadId, entry.SourceThreadId, StringComparison.Ordinal))))
                return ledger;
            fromAttachment.Add(entry);
        }

        return ledger;
    }

    // Absorbs the entry's knowers into an earlier equal entry the filter allows, else appends it; false when the ledger is full
    private static bool Place(NarrativeLedger ledger, Dictionary<string, List<LedgerEntry>> index, LedgerEntry entry,
        Func<LedgerEntry, bool> mayAbsorb)
    {
        var key = DetailKey(entry);
        if (!index.TryGetValue(key, out var candidates))
        {
            candidates = new List<LedgerEntry>();
            index[key] = candidates;
        }

        var earlier = candidates.FirstOrDefault(e => SameDetail(e, entry) && mayAbsorb(e));
        if (earlier != null)
        {
            AbsorbKnowers(earlier, entry);
            return true;
        }

        if (ledger.Entries.Count >= NarrativeLedger.MaxEntries)
            return false;
        candidates.Add(entry);
        ledger.Entries.Add(entry);
        return true;
    }

    // Thread entries (Ids T-xxxx-Lnn) and attachment entries (Ids {AttachmentDocId}-Lnn), each in kept order
    private static (List<LedgerEntry> Own, List<LedgerEntry> Attachments) Partition(PlannedThread thread,
        IReadOnlyList<LedgerDelta> deltas, CaseGuide guide, int waveIndex)
    {
        var kept = new List<LedgerEntry>();
        var attachmentEntries = new List<LedgerEntry>();
        if (thread == null || deltas == null)
            return (kept, attachmentEntries);

        var bySeq = (thread.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .GroupBy(m => m.Seq)
            .ToDictionary(g => g.Key, g => g.First());

        var allowed = new HashSet<string>(KnowledgeGate.Build(guide, thread).Facts.Select(f => f.Id), StringComparer.Ordinal);
        allowed.UnionWith((thread.FactIds ?? new List<string>()).Select(RelevantThreadFiller.CanonicalId));

        var knownBySeq = new Dictionary<int, List<string>>();
        var groups = new SortedDictionary<string, List<LedgerEntry>>(StringComparer.Ordinal);
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

            List<string> knownBy;
            string sourceDocId;
            if (delta.FromAttachment)
            {
                // An attachment travels with its own email only: the parent's participants know it
                knownBy = Participants(message).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
                var docId = (delta.AttachmentDocId ?? string.Empty).Trim();
                sourceDocId = docId.Length > 0 ? docId : message.DocId ?? string.Empty;
            }
            else
            {
                if (!knownBySeq.TryGetValue(message.Seq, out var known))
                {
                    known = KnownBy(thread, message);
                    knownBySeq[message.Seq] = known;
                }

                knownBy = known;
                sourceDocId = message.DocId ?? string.Empty;
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
                SourceDocId = sourceDocId,
                SourceThreadId = thread.Id ?? string.Empty,
                WaveIndex = waveIndex
            };

            if (delta.FromAttachment)
            {
                var key = AttachmentGroupKey(thread, delta);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new List<LedgerEntry>();
                    groups[key] = group;
                }

                group.Add(entry);
                continue;
            }

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

        // Each group sees the kept thread entries and its own entries only, so no attachment changes another's entries
        foreach (var (key, candidates) in groups)
        {
            var group = new List<LedgerEntry>();
            foreach (var entry in candidates)
            {
                var threadEntry = kept.FirstOrDefault(e => SameDetail(e, entry));
                if (threadEntry != null)
                {
                    // Checker 06-05: a later attachment never changes a thread entry's facts, so meeting keys taken before it stay valid
                    AbsorbKnowers(threadEntry, entry);
                    continue;
                }

                var duplicate = group.FirstOrDefault(e => SameDetail(e, entry));
                if (duplicate != null)
                {
                    Absorb(duplicate, entry, guide);
                    continue;
                }

                if (group.Count >= MaxEntriesPerAttachment)
                    continue;
                group.Add(entry);
            }

            for (var i = 0; i < group.Count; i++)
                group[i].Id = string.Format(CultureInfo.InvariantCulture, "{0}-L{1:00}", key, i + 1);
            attachmentEntries.AddRange(group);
        }

        return (kept, attachmentEntries);
    }

    // The attachment's DocId; legacy deltas without one share {thread.Id}-A
    private static string AttachmentGroupKey(PlannedThread thread, LedgerDelta delta)
    {
        var docId = (delta.AttachmentDocId ?? string.Empty).Trim();
        return docId.Length > 0 ? docId : (thread.Id ?? string.Empty) + "-A";
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

    /// <summary>
    /// The date formats SameValue accepts (yyyy-MM-dd, "MMMM d, yyyy", en-US); InviteScheduler reads meeting dates with it.
    /// </summary>
    internal static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormats, EnUs, DateTimeStyles.None, out date);

    /// <summary>
    /// Identity of a detail: Kind plus normalized About; when the model left About empty the statement stands in.
    /// </summary>
    internal static string DetailKey(LedgerEntry entry)
    {
        var about = NormalizeAbout(entry.About);
        if (about.Length == 0)
            about = "~" + NormalizeAbout(entry.Statement);
        return ((int)entry.Kind).ToString(CultureInfo.InvariantCulture) + "|" + about;
    }

    /// <summary>
    /// The duplicate rule EntriesFor and both Build passes use (Build indexes on DetailKey first). InviteScheduler
    /// keys meetings with it, so a key never disagrees with the ledger (checker 06-05).
    /// </summary>
    internal static bool SameDetail(LedgerEntry a, LedgerEntry b) =>
        string.Equals(DetailKey(a), DetailKey(b), StringComparison.Ordinal) && SameValue(a.Value, b.Value);

    // Knowers only: FactIds, IssueIds, About, Value and SourceDocId of the target stay as they were
    private static void AbsorbKnowers(LedgerEntry target, LedgerEntry source) =>
        target.KnownByIds = Union(target.KnownByIds, source.KnownByIds);

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
