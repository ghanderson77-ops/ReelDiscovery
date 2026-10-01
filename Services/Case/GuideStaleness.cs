using System.Text.Json;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Content-hash staleness for the case guide parts (GUIDE-09, D-20). Each part stores the hash of its
/// upstream inputs when generated; a part is stale when that stored basis no longer matches.
/// </summary>
public static class GuideStaleness
{
    public static readonly GuidePart[] Order = { GuidePart.Issues, GuidePart.Cast, GuidePart.Facts, GuidePart.Values };

    // Component keys in reporting order, with the wording used in stale messages
    private static readonly (string Key, string Label)[] ComponentLabels =
    {
        ("core", "core document"),
        ("issues", "issues"),
        ("cast", "cast"),
        ("facts", "key facts")
    };

    /// <summary>
    /// Hash of the core document content the guide is built from: parties, counts, fact plan, timeline,
    /// filing date and every section's id and paragraphs. Signature-block details are deliberately excluded.
    /// </summary>
    public static string CoreDocumentHash(CoreDocument doc) => Hash(new
    {
        Parties = doc.Parties ?? new(),
        Counts = doc.Counts ?? new(),
        FactPlan = doc.FactPlan ?? new(),
        doc.TimelineStart,
        doc.TimelineEnd,
        doc.FilingDate,
        Sections = (doc.Sections ?? new()).Where(s => s != null).Select(s => new { s.Id, s.Paragraphs })
    });

    /// <summary>
    /// Current basis hash for a part; empty when the draft has no core document or guide.
    /// </summary>
    public static string BasisFor(CaseDraft draft, GuidePart part)
    {
        var components = Components(draft, part);
        if (components.Count == 0)
            return string.Empty;

        return CaseDraft.ComputeHash(string.Join("|", components.Select(c => c.Key + "=" + c.Value)));
    }

    /// <summary>
    /// Records the current basis (and its components) on the part, marking it fresh.
    /// </summary>
    public static void Stamp(CaseDraft draft, GuidePart part)
    {
        if (draft?.Guide == null)
            return;

        var state = draft.Guide.State(part);
        state.BasisHash = BasisFor(draft, part);
        state.BasisComponents = Components(draft, part).ToDictionary(c => c.Key, c => c.Value);
        state.GeneratedUtc = DateTime.UtcNow;
    }

    public static bool IsStale(CaseDraft draft, GuidePart part)
    {
        if (draft?.Guide == null || draft.CoreDocument == null)
            return false;

        var state = draft.Guide.State(part);
        if (state.Status != GuidePartStatus.Ready || string.IsNullOrEmpty(state.BasisHash))
            return false;

        return state.BasisHash != BasisFor(draft, part);
    }

    public static List<GuidePart> StaleParts(CaseDraft draft) =>
        Order.Where(p => IsStale(draft, p)).ToList();

    /// <summary>
    /// Identity of the current stale state ("" when fresh); a Not Now answer is remembered against it.
    /// </summary>
    public static string StaleKey(CaseDraft draft) =>
        string.Join("|", StaleParts(draft).Select(p => p + ":" + BasisFor(draft, p)));

    /// <summary>
    /// Runs a hand edit to <paramref name="editedPart"/>; downstream parts that were fresh are restamped so they stay fresh.
    /// </summary>
    public static void AbsorbHandEdit(CaseDraft draft, GuidePart editedPart, Action edit)
    {
        var fresh = draft?.Guide == null
            ? new List<GuidePart>()
            : Order.SkipWhile(p => p != editedPart).Skip(1)
                .Where(p => draft.Guide.State(p).Status == GuidePartStatus.Ready
                    && draft.Guide.State(p).BasisHash.Length > 0
                    && !IsStale(draft, p))
                .ToList();

        edit();

        // Hand edits are reviewed through NeedsReview, not by regenerating downstream parts (D-20)
        foreach (var part in fresh)
            Stamp(draft!, part);
    }

    public static int NeedsReviewCount(CaseDraft draft) =>
        draft?.Guide?.Facts?.Count(f => f != null && f.NeedsReview) ?? 0;

    public static int ValuesNeedingReviewCount(CaseDraft draft) =>
        draft?.Guide?.Values?.Count(v => v != null && v.NeedsReview) ?? 0;

    /// <summary>
    /// Names the first upstream input that changed since the part was stamped ("core document", "issues",
    /// "cast" or "key facts"); empty when the part is fresh.
    /// </summary>
    public static string StaleReason(CaseDraft draft, GuidePart part)
    {
        if (!IsStale(draft, part))
            return string.Empty;

        var stored = draft.Guide!.State(part).BasisComponents ?? new();
        var current = Components(draft, part).ToDictionary(c => c.Key, c => c.Value);

        foreach (var (key, label) in ComponentLabels)
        {
            if (!current.TryGetValue(key, out var now))
                continue;
            if (!stored.TryGetValue(key, out var then) || then != now)
                return label;
        }

        // Stamped before components were recorded: the core document is the root of every basis
        return "core document";
    }

    private static List<KeyValuePair<string, string>> Components(CaseDraft? draft, GuidePart part)
    {
        var result = new List<KeyValuePair<string, string>>();
        var doc = draft?.CoreDocument;
        var guide = draft?.Guide;
        if (doc == null || guide == null)
            return result;

        result.Add(new("core", CoreDocumentHash(doc)));

        switch (part)
        {
            case GuidePart.Facts:
                result.Add(new("issues", Hash((guide.Issues ?? new()).Where(i => i != null).Select(i => new { i.Id, i.Title }))));
                result.Add(new("cast", Hash((guide.Cast ?? new()).Where(c => c != null)
                    .Select(c => new { c.Id, c.FullName, c.OrganizationId, c.ActiveFrom, c.ActiveTo }))));
                break;
            case GuidePart.Values:
                result.Add(new("facts", Hash((guide.Facts ?? new()).Where(f => f != null)
                    .Select(f => new { f.Id, f.Date, f.Title, f.Description }))));
                break;
        }

        return result;
    }

    private static string Hash(object payload) =>
        CaseDraft.ComputeHash(JsonSerializer.Serialize(payload, CaseDraft.JsonOptions));
}
