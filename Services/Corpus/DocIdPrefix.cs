using System.Text;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// DocID prefix from the producing party (D-30): defendant/subject for pleadings, the memo organization for memos.
/// </summary>
public static class DocIdPrefix
{
    public const string Fallback = "RD";
    public const int MaxLength = 4;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inc", "LLC", "LLP", "Corp", "Corporation", "Co", "Company", "Ltd", "LP", "PC", "The", "of", "and", "&"
    };

    /// <summary>
    /// The settings override when it has letters left after sanitizing; otherwise Derive(draft).
    /// </summary>
    public static string Effective(CorpusSettings settings, CaseDraft draft)
    {
        var custom = CorpusSettings.SanitizePrefix(settings?.DocIdPrefix);
        return custom.Length > 0 ? custom : Derive(draft);
    }

    /// <summary>
    /// 2-4 uppercase A-Z letters from the producing party's name; "RD" when nothing usable is found.
    /// </summary>
    public static string Derive(CaseDraft draft)
    {
        var doc = draft?.CoreDocument;
        if (doc == null)
            return Fallback;

        var parties = doc.Parties?.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList() ?? new List<CaseParty>();
        string shortText;
        string fullName;

        if (doc.DocumentType == CoreDocumentType.InvestigationMemo && !string.IsNullOrWhiteSpace(doc.Memo?.Organization))
        {
            fullName = doc.Memo.Organization.Trim();
            var org = draft!.Guide?.Organizations?.FirstOrDefault(o =>
                o != null && string.Equals(o.Name?.Trim(), fullName, StringComparison.OrdinalIgnoreCase));
            shortText = string.IsNullOrWhiteSpace(org?.ShortName) ? fullName : org.ShortName;
        }
        else
        {
            var party = doc.DocumentType == CoreDocumentType.InvestigationMemo
                ? parties.FirstOrDefault()
                : parties.FirstOrDefault(p => p.Role == PartyRole.Defendant)
                    ?? parties.FirstOrDefault(p => p.Role == PartyRole.Subject)
                    ?? parties.LastOrDefault();
            if (party == null)
                return Fallback;

            fullName = party.Name.Trim();
            var org = draft!.Guide?.Organizations?.FirstOrDefault(o =>
                o != null && !string.IsNullOrEmpty(o.PartyId) && o.PartyId == party.Id);
            shortText = !string.IsNullOrWhiteSpace(org?.ShortName) ? org.ShortName
                : !string.IsNullOrWhiteSpace(party.ShortName) ? party.ShortName
                : fullName;
        }

        var words = SignificantWords(shortText);
        if (words.Count < 2)
            words = SignificantWords(fullName);

        if (words.Count == 0)
            return Fallback;

        string result;
        if (words.Count == 1)
        {
            result = words[0].Length >= 2 ? words[0][..2] : words[0];
        }
        else
        {
            var initials = new StringBuilder();
            foreach (var word in words)
            {
                initials.Append(word[0]);
                if (initials.Length == MaxLength)
                    break;
            }

            result = initials.ToString();
        }

        return result.Length >= 2 ? result : Fallback;
    }

    // Upper-cased A-Z-only words with stopwords removed (non-ASCII letters, digits and punctuation dropped)
    private static List<string> SignificantWords(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var raw in text.Split(new[] { ' ', ',', '.', '\t', '/', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Stopwords.Contains(raw))
                continue;

            var builder = new StringBuilder();
            foreach (var c in raw.ToUpperInvariant())
            {
                if (c is >= 'A' and <= 'Z')
                    builder.Append(c);
            }

            if (builder.Length == 0)
                continue;

            var word = builder.ToString();
            if (!Stopwords.Contains(word))
                result.Add(word);
        }

        return result;
    }
}
