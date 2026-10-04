using System.Globalization;
using System.Text;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// One name for EML part, journal and answer key (Pitfall 1): every attachment's final file name and Content-ID come from
/// here, never "Document N" / "Photo N" placeholders. D-02 narrow exception: the final name may differ from the plan's
/// FileName (version suffix, filler title, placeholder replacement); kind, DocID, parent, inline flag, title and
/// description stay plan-owned, and the final name is journaled on the attachment record and the key shows it (review 06-05).
/// </summary>
public static class AttachmentNaming
{
    private const int DescriptionWords = 8;

    /// <summary>
    /// The attachment's title: plan Title, else the filler title, else the first words of the Description, else
    /// "{kind noun} {sender-local date}".
    /// </summary>
    public static string TitleFor(PlannedAttachment spec, PlannedMessage parent, string? fillerTitle)
    {
        if (!string.IsNullOrWhiteSpace(spec?.Title))
            return spec.Title.Trim();
        if (!string.IsNullOrWhiteSpace(fillerTitle))
            return fillerTitle.Trim();

        var words = (spec?.Description ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Take(DescriptionWords)
            .ToList();
        if (words.Count > 0)
            return string.Join(" ", words).TrimEnd('.', ',', ';', ':');

        return FallbackTitle(spec?.Kind ?? AttachmentType.Word, parent);
    }

    /// <summary>
    /// Final file names of a message's attachments keyed by attachment DocId in plan order; filler title i pairs with
    /// attachment i. Chain members are named from the chain's base title plus "_v{n}" for n > 1; voicemails are named
    /// from the sender-local send time; same names on one message get " (2)", " (3)"; every name passes
    /// <see cref="CorpusPlan.IsSafeFileName"/>.
    /// </summary>
    public static Dictionary<string, string> NamesFor(PlannedMessage message, IReadOnlyList<string>? fillerTitles,
        IReadOnlyDictionary<string, AttachmentChainLink> chains)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (message?.Attachments == null)
            return names;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < message.Attachments.Count; i++)
        {
            var spec = message.Attachments[i];
            if (spec == null || string.IsNullOrEmpty(spec.DocId) || names.ContainsKey(spec.DocId))
                continue;

            var filler = fillerTitles != null && i < fillerTitles.Count ? fillerTitles[i] : null;
            var name = BaseName(spec, message, filler, chains);
            names[spec.DocId] = Unique(name, used);
        }

        return names;
    }

    /// <summary>
    /// "{16 hex}@{domain}": a stable hash of the attachment DocId at the sender domain restricted to [a-z0-9.-]
    /// (T-06-15); a blank domain uses <see cref="EmailComposer.FallbackDomain"/>.
    /// </summary>
    public static string ContentIdFor(string docId, string? domain)
    {
        var rank = RenderAssignments.StableRank("cid:" + (docId ?? string.Empty));
        return rank.ToString("x16", CultureInfo.InvariantCulture) + "@" + CleanDomain(domain);
    }

    private static string BaseName(PlannedAttachment spec, PlannedMessage parent, string? filler,
        IReadOnlyDictionary<string, AttachmentChainLink>? chains)
    {
        string stem;
        if (spec.Kind == AttachmentType.Voicemail)
        {
            var local = MessageTime.Local(parent);
            stem = "Voicemail " + local.ToString("yyyy-MM-dd h-mm tt", CultureInfo.InvariantCulture);
        }
        else if (chains != null && chains.TryGetValue(spec.DocId, out var link) && link.BaseTitle.Length > 0)
        {
            stem = link.Version > 1
                ? link.BaseTitle + "_v" + link.Version.ToString(CultureInfo.InvariantCulture)
                : link.BaseTitle;
        }
        else
        {
            stem = TitleFor(spec, parent, filler);
        }

        var name = Safe(RelevantThreadFiller.FileNameFor(stem, spec.Kind));
        if (name.Length == 0)
            name = Safe(RelevantThreadFiller.FileNameFor(FallbackTitle(spec.Kind, parent), spec.Kind));
        return name.Length > 0 ? name : "Attachment" + ExtensionOf(spec.Kind);
    }

    // Final guard: ".." collapsed; anything still unsafe is dropped so the caller falls back to the kind + date name
    private static string Safe(string name)
    {
        while (name.Contains("..", StringComparison.Ordinal))
            name = name.Replace("..", ".", StringComparison.Ordinal);
        name = name.Trim();
        return CorpusPlan.IsSafeFileName(name) && !name.StartsWith('.') ? name : string.Empty;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        for (var n = 2; ; n++)
        {
            var candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1}){2}", stem, n, extension);
            if (used.Add(candidate))
                return candidate;
        }
    }

    private static string FallbackTitle(AttachmentType kind, PlannedMessage parent)
    {
        var date = parent != null ? MessageTime.Local(parent).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "undated";
        return KindNoun(kind) + " " + date;
    }

    // Nouns that never form the "Document N" placeholder shape
    private static string KindNoun(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => "Notes",
        AttachmentType.Excel => "Worksheet",
        AttachmentType.PowerPoint => "Slides",
        AttachmentType.Image => "Photo",
        AttachmentType.CalendarInvite => "Meeting",
        AttachmentType.Voicemail => "Voicemail",
        _ => "Attachment"
    };

    private static string ExtensionOf(AttachmentType kind)
    {
        var sample = RelevantThreadFiller.FileNameFor("x", kind);
        return sample.Length > 1 ? sample[1..] : ".bin";
    }

    private static string CleanDomain(string? domain)
    {
        var sb = new StringBuilder();
        foreach (var c in (domain ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')
                sb.Append(c);
        }

        var clean = sb.ToString().Trim('.', '-');
        return clean.Length > 0 ? clean : EmailComposer.FallbackDomain;
    }
}
