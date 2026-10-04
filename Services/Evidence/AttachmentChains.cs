using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// One attachment's place in a document version chain.
/// </summary>
public class AttachmentChainLink
{
    // "{orgKey}|{Kind}|{stem}"
    public string ChainId { get; set; } = string.Empty;

    // 1-based rank in the chain (parent SentAt, then DocId)
    public int Version { get; set; }

    public int Count { get; set; }

    // DocId of version n-1; empty for version 1
    public string PreviousDocId { get; set; } = string.Empty;

    // Cast OrganizationId, "ext:{domain}", "unknown" or "from:{FromId}" (no guide)
    public string OrganizationKey { get; set; } = string.Empty;

    // Version 1's title without version tokens; every version is named from it
    public string BaseTitle { get; set; } = string.Empty;
}

/// <summary>
/// Document version chains of relevant Word/Excel/PowerPoint attachments (D-16), a pure function of the plan: chain order
/// is parent SentAt then DocId, so version numbers never depend on completion order (Pitfall 15). Chains are scoped by
/// sender organization so unrelated organizations' documents never link (review concern 7).
/// </summary>
public static partial class AttachmentChains
{
    private static readonly AttachmentType[] ChainKinds = { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint };

    /// <summary>
    /// Links keyed by attachment DocId; attachments without a same-organization, same-kind, same-stem sibling get no entry.
    /// With no guide every sender is its own scope ("from:{FromId}"), a fallback for callers without a case guide.
    /// </summary>
    public static Dictionary<string, AttachmentChainLink> Compute(CorpusPlan plan, CaseGuide? guide)
    {
        var result = new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
        if (plan?.Threads == null)
            return result;

        var people = guide != null ? PeopleDirectory.Create(guide, plan) : null;
        var items = new List<(string Org, AttachmentType Kind, string Stem, PlannedMessage Parent, PlannedAttachment Spec)>();
        foreach (var thread in plan.Threads.Where(t => t != null && t.Relevance == ThreadRelevance.Relevant))
        {
            foreach (var message in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null))
            {
                foreach (var spec in (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null))
                {
                    if (!ChainKinds.Contains(spec.Kind) || string.IsNullOrEmpty(spec.DocId))
                        continue;
                    var stem = NormalizeStem(spec.Title);
                    if (stem.Length == 0)
                        continue;
                    items.Add((OrganizationKey(people, message.FromId), spec.Kind, stem, message, spec));
                }
            }
        }

        var groups = items.GroupBy(i => (i.Org, i.Kind, i.Stem));
        foreach (var group in groups)
        {
            var ordered = group
                .GroupBy(i => i.Spec.DocId, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(i => i.Parent.SentAt.UtcDateTime)
                .ThenBy(i => i.Spec.DocId, StringComparer.Ordinal)
                .ToList();
            if (ordered.Count < 2)
                continue;

            var chainId = $"{group.Key.Org}|{group.Key.Kind}|{group.Key.Stem}";
            var baseTitle = ChainTitle(ordered[0].Spec.Title);
            for (var i = 0; i < ordered.Count; i++)
            {
                result[ordered[i].Spec.DocId] = new AttachmentChainLink
                {
                    ChainId = chainId,
                    Version = i + 1,
                    Count = ordered.Count,
                    PreviousDocId = i > 0 ? ordered[i - 1].Spec.DocId : string.Empty,
                    OrganizationKey = group.Key.Org,
                    BaseTitle = baseTitle
                };
            }
        }

        return result;
    }

    /// <summary>
    /// Sender scope: cast OrganizationId, "ext:{domain}" for externals, "unknown" for unresolved ids, "from:{FromId}"
    /// when there is no directory.
    /// </summary>
    internal static string OrganizationKey(PeopleDirectory? people, string? fromId)
    {
        if (people == null)
            return "from:" + (fromId ?? string.Empty).Trim();

        var person = people.Find(fromId);
        if (person == null)
            return "unknown";
        if (person.IsExternal)
            return person.Domain.Length > 0 ? "ext:" + person.Domain.ToLowerInvariant() : "unknown";
        return person.OrganizationId.Length > 0 ? person.OrganizationId : "unknown";
    }

    /// <summary>
    /// Lower-case title without version tokens (v2, version 3, final, draft, rev 2, revised, updated, copy), dates and
    /// punctuation; words separated by single spaces.
    /// </summary>
    internal static string NormalizeStem(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var text = title.ToLowerInvariant().Replace('_', ' ');
        text = DatePattern().Replace(text, " ");
        text = VersionTokenPattern().Replace(text, " ");
        text = NonWordPattern().Replace(text, " ");
        return WhitespacePattern().Replace(text, " ").Trim();
    }

    /// <summary>
    /// The display title without version tokens and dates (case and punctuation kept), e.g. "CO-007 Pricing Backup v2" ->
    /// "CO-007 Pricing Backup"; the original title when nothing usable remains.
    /// </summary>
    internal static string ChainTitle(string? title)
    {
        var original = (title ?? string.Empty).Trim();
        var text = original.Replace('_', ' ');
        text = DatePattern().Replace(text, " ");
        text = VersionTokenPattern().Replace(text, " ");
        text = EmptyParensPattern().Replace(text, " ");
        text = WhitespacePattern().Replace(text, " ").Trim(' ', '-', '.', ',');
        return text.Length > 0 ? text : original;
    }

    [GeneratedRegex(@"\b(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}/\d{1,2}/\d{2,4})\b")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b(?:v\d+|version\s*\d+|final|draft|rev(?:ision)?\s*\d*|revised|updated|copy)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionTokenPattern();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex NonWordPattern();

    [GeneratedRegex(@"[\(\[]\s*[\)\]]")]
    private static partial Regex EmptyParensPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
