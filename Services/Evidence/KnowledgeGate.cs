using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// A canonical value with the date it becomes knowable and the people who can know it (empty = public).
/// </summary>
public class KnowableValue
{
    public CanonicalValue Value { get; set; } = new();

    // Earliest linked fact date; null = not date-gated
    public DateOnly? KnowableFrom { get; set; }

    public HashSet<string> KnownBy { get; set; } = new(StringComparer.Ordinal);

    public bool IsPublic => KnownBy.Count == 0;
}

/// <summary>
/// What a thread's participants could know at the thread's dates (CORP-01).
/// </summary>
public class ThreadKnowledge
{
    public HashSet<string> Participants { get; set; } = new(StringComparer.Ordinal);
    public DateOnly FirstDate { get; set; }
    public DateOnly LastDate { get; set; }

    // Facts known to at least one participant by the last message, plus the thread's own plan facts; date then id order
    public List<KeyFact> Facts { get; set; } = new();

    // Seq -> fact ids the sender knows by that message's date, plus that message's plan facts
    public Dictionary<int, List<string>> MayMentionBySeq { get; set; } = new();

    // Values known to at least one participant by the last message
    public List<KnowableValue> Values { get; set; } = new();

    // Seq -> value ids the sender knows by that message's date (plus values linked to that message's plan facts)
    public Dictionary<int, HashSet<string>> KnownValueIdsBySeq { get; set; } = new();

    // Seq -> root-first seqs the message replies to or forwards
    public Dictionary<int, List<int>> AncestorsBySeq { get; set; } = new();
}

/// <summary>
/// Point-in-time context (CORP-01): never put the complaint, issue RelevanceCriteria or the full value registry into a
/// render prompt (Pitfall 1/2). Facts and values reach a thread only when dated on or before its messages and known to
/// someone on it.
/// </summary>
public static class KnowledgeGate
{
    /// <summary>
    /// People who knew the fact: KnownByIds, else ActorIds, else empty (= public).
    /// </summary>
    public static IReadOnlyCollection<string> KnowersOf(KeyFact fact)
    {
        // RESEARCH A6 fallbacks
        var known = Clean(fact?.KnownByIds);
        if (known.Count > 0)
            return known;
        return Clean(fact?.ActorIds);
    }

    /// <summary>
    /// True when the fact is dated on or before <paramref name="asOf"/> (undated facts are not date-gated) and the
    /// person is one of its knowers (or it is public).
    /// </summary>
    public static bool KnowsFact(KeyFact fact, string personId, DateOnly asOf)
    {
        if (fact == null)
            return false;
        var date = SkeletonContext.FactDate(fact);
        if (date != null && date.Value > asOf)
            return false;
        var knowers = KnowersOf(fact);
        return knowers.Count == 0 || knowers.Contains(personId ?? string.Empty);
    }

    /// <summary>
    /// A value linked to facts becomes knowable at the earliest linked fact date to the union of those facts' knowers;
    /// a value with no resolvable linked facts, or linked to a public fact, is public.
    /// </summary>
    public static KnowableValue Knowability(CanonicalValue value, CaseGuide guide)
    {
        var result = new KnowableValue { Value = value ?? new CanonicalValue() };
        var facts = Clean(value?.FactIds)
            .Select(id => FindFact(guide, id))
            .Where(f => f != null)
            .Select(f => f!)
            .ToList();
        if (facts.Count == 0)
            return result;

        var dates = facts.Select(SkeletonContext.FactDate).Where(d => d != null).Select(d => d!.Value).ToList();
        result.KnowableFrom = dates.Count > 0 ? dates.Min() : null;

        var knowers = facts.Select(KnowersOf).ToList();
        if (knowers.Any(k => k.Count == 0))
            return result;

        foreach (var set in knowers)
            result.KnownBy.UnionWith(set);
        return result;
    }

    public static bool KnowsValue(KnowableValue value, string personId, DateOnly asOf)
    {
        if (value == null)
            return false;
        if (value.KnowableFrom != null && value.KnowableFrom.Value > asOf)
            return false;
        return value.IsPublic || value.KnownBy.Contains(personId ?? string.Empty);
    }

    /// <summary>
    /// Seqs the message replies to or forwards, root first; stops at a missing seq and never loops on cycles.
    /// </summary>
    public static List<int> Ancestors(PlannedThread thread, int seq)
    {
        var bySeq = BySeq(thread);
        var chain = new List<int>();
        if (!bySeq.TryGetValue(seq, out var current))
            return chain;

        var visited = new HashSet<int> { seq };
        while (current.ReplyToSeq != 0 && visited.Add(current.ReplyToSeq) && bySeq.TryGetValue(current.ReplyToSeq, out var parent))
        {
            chain.Add(parent.Seq);
            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    public static ThreadKnowledge Build(CaseGuide guide, PlannedThread thread)
    {
        var knowledge = new ThreadKnowledge();
        var messages = (thread?.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .OrderBy(m => m.Seq)
            .ToList();

        foreach (var message in messages)
        {
            knowledge.Participants.UnionWith(Clean(new[] { message.FromId }));
            knowledge.Participants.UnionWith(Clean(message.ToIds));
            knowledge.Participants.UnionWith(Clean(message.CcIds));
        }

        knowledge.FirstDate = thread != null ? MessageTime.FirstLocalDate(Safe(thread, messages)) : DateOnly.MaxValue;
        knowledge.LastDate = thread != null ? MessageTime.LastLocalDate(Safe(thread, messages)) : DateOnly.MinValue;

        var allFacts = (guide?.Facts ?? new List<KeyFact>())
            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id))
            .OrderBy(f => SkeletonContext.FactDate(f) ?? DateOnly.MinValue)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        // Plan facts of the thread and of every message are always in context
        var planFacts = new HashSet<string>(Clean(thread?.FactIds), StringComparer.Ordinal);
        foreach (var message in messages)
            planFacts.UnionWith(Clean(message.FactIds));

        knowledge.Facts = allFacts
            .Where(f => planFacts.Contains(f.Id) || knowledge.Participants.Any(p => KnowsFact(f, p, knowledge.LastDate)))
            .ToList();

        var values = (guide?.Values ?? new List<CanonicalValue>())
            .Where(v => v != null && !string.IsNullOrWhiteSpace(v.Id))
            .Select(v => Knowability(v, guide!))
            .ToList();
        knowledge.Values = values
            .Where(v => KnownByAny(v, knowledge.Participants, knowledge.LastDate))
            .ToList();

        foreach (var message in messages)
        {
            if (knowledge.MayMentionBySeq.ContainsKey(message.Seq))
                continue;

            var date = MessageTime.LocalDate(message);
            var own = new HashSet<string>(Clean(message.FactIds), StringComparer.Ordinal);
            knowledge.MayMentionBySeq[message.Seq] = allFacts
                .Where(f => own.Contains(f.Id) || KnowsFact(f, message.FromId ?? string.Empty, date))
                .Select(f => f.Id)
                .ToList();

            knowledge.KnownValueIdsBySeq[message.Seq] = values
                .Where(v => KnowsValue(v, message.FromId ?? string.Empty, date) || Clean(v.Value.FactIds).Any(own.Contains))
                .Select(v => v.Value.Id)
                .ToHashSet(StringComparer.Ordinal);

            knowledge.AncestorsBySeq[message.Seq] = Ancestors(thread!, message.Seq);
        }

        return knowledge;
    }

    private static bool KnownByAny(KnowableValue value, IEnumerable<string> people, DateOnly asOf)
    {
        if (value.KnowableFrom != null && value.KnowableFrom.Value > asOf)
            return false;
        return value.IsPublic || people.Any(value.KnownBy.Contains);
    }

    // Messages with nulls removed, so MessageTime never sees a null entry
    private static PlannedThread Safe(PlannedThread thread, List<PlannedMessage> messages) =>
        new() { Id = thread.Id, Messages = messages };

    private static Dictionary<int, PlannedMessage> BySeq(PlannedThread thread) =>
        (thread?.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .GroupBy(m => m.Seq)
            .ToDictionary(g => g.Key, g => g.First());

    private static KeyFact? FindFact(CaseGuide guide, string id) =>
        (guide?.Facts ?? new List<KeyFact>()).FirstOrDefault(f => f != null && string.Equals(f.Id, id, StringComparison.Ordinal));

    private static List<string> Clean(IEnumerable<string?>? ids) =>
        (ids ?? Enumerable.Empty<string?>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
