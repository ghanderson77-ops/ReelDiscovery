using System.Text.RegularExpressions;

namespace ReelDiscovery.Services.Case;

public enum CitationSeverity
{
    Warning,
    Error
}

public class CitationFinding
{
    public CitationSeverity Severity { get; set; }

    /// <summary>
    /// One of "Reporter", "Database", "CaseName", "Signal".
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public int Index { get; set; }
}

/// <summary>
/// Deterministic check for invented case law (CASE-06). Reporter and database citations are errors;
/// bare case names and citation signals are warnings. Statutes, regulations and court rules are never flagged.
/// </summary>
public static partial class CitationScanner
{
    /// <summary>
    /// Match timeout for the scanning patterns; callers treat RegexMatchTimeoutException as "too large to scan".
    /// </summary>
    public const int MatchTimeoutMs = 1000;

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "inc", "llc", "corp", "corporation", "co", "ltd", "lp", "llp", "pc", "pa", "the", "et", "al", "company"
    };

    // volume + reporter + first page, e.g. "531 U.S. 98", "123 F.3d 456", "45 Cal. Rptr. 3d 12".
    // "U.S." must be followed by whitespace and a page number, so "28 U.S.C. § 1332" can never match.
    [GeneratedRegex(@"\b\d{1,4}\s+(?:U\.\s?S\.|S\.\s?Ct\.|L\.\s?Ed\.(?:\s?2d)?|F\.\s?Supp\.(?:\s?[23]d)?|F\.\s?App'x|Fed\.\s?Appx\.?|F\.(?:\s?(?:2d|3d|4th))?|B\.\s?R\.|Fed\.\s?Cl\.|A\.(?:\s?[23]d)?|P\.(?:\s?[23]d)?|N\.E\.(?:\s?[23]d)?|N\.W\.(?:\s?2d)?|S\.E\.(?:\s?2d)?|S\.W\.(?:\s?[23]d)?|So\.(?:\s?[23]d)?|Cal\.\s?Rptr\.(?:\s?[23]d)?|Cal\.\s?App\.\s?(?:2d|3d|4th|5th)|Cal\.\s?(?:2d|3d|4th|5th)|N\.Y\.S\.(?:\s?[23]d)?|N\.Y\.\s?[23]d|A\.D\.\s?[23]d|Ill\.\s?Dec\.|Ill\.\s?App\.(?:\s?[23]d)?)\s+\d{1,5}\b", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex ReporterCitation();

    // "2019 WL 1234567", "2020 U.S. Dist. LEXIS 12345"
    [GeneratedRegex(@"\b(?:19|20)\d{2}\s+(?:WL|U\.S\.\s?(?:Dist\.|App\.)\s?LEXIS|[A-Z][A-Za-z.]*\s?LEXIS)\s+\d+\b", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex DatabaseCitation();

    // "Smith v. Jones" (warning unless it is this document's own caption).
    // Backtracks over long runs of capitalized words, so the timeout matters most here (WR-04).
    [GeneratedRegex(@"\b([A-Z][\w.&'\-]*(?:\s+(?:[A-Z][\w.&'\-]*|of|the|and|&))*)\s+v\.\s+([A-Z][\w.&'\-]*(?:\s+(?:[A-Z][\w.&'\-]*|of|the|and|&))*)", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex CaseName();

    [GeneratedRegex(@"\bId\.\s+at\s+\d+|\bsupra\b|\bSee,?\s+e\.g\.,", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex CitationSignal();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    public static List<CitationFinding> Scan(string text, IReadOnlyCollection<string> ownPartyNames)
    {
        var findings = new List<CitationFinding>();
        if (string.IsNullOrWhiteSpace(text))
            return findings;

        foreach (Match m in ReporterCitation().Matches(text))
            findings.Add(Finding(CitationSeverity.Error, "Reporter", m));

        foreach (Match m in DatabaseCitation().Matches(text))
            findings.Add(Finding(CitationSeverity.Error, "Database", m));

        foreach (Match m in CaseName().Matches(text))
        {
            if (!IsOwnCaption(m.Groups[1].Value, m.Groups[2].Value, ownPartyNames))
                findings.Add(Finding(CitationSeverity.Warning, "CaseName", m));
        }

        foreach (Match m in CitationSignal().Matches(text))
            findings.Add(Finding(CitationSeverity.Warning, "Signal", m));

        return findings.OrderBy(f => f.Index).ToList();
    }

    public static bool HasErrors(IEnumerable<CitationFinding> findings) => findings.Any(f => f.Severity == CitationSeverity.Error);

    private static CitationFinding Finding(CitationSeverity severity, string kind, Match m) => new()
    {
        Severity = severity,
        Kind = kind,
        Text = m.Value,
        Index = m.Index
    };

    private static string Normalize(string s)
    {
        var spaced = NonAlphanumeric().Replace(s.ToLowerInvariant(), " ");
        var tokens = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !NoiseTokens.Contains(t));
        return string.Join(" ", tokens);
    }

    // Pitfall 4: the document's own caption must never block Finish.
    private static bool IsOwnCaption(string left, string right, IReadOnlyCollection<string> parties)
    {
        var partyTokens = (parties ?? Array.Empty<string>())
            .Select(Tokens)
            .Where(t => t.Count > 0)
            .ToList();

        return SideMatches(Tokens(left), partyTokens) && SideMatches(Tokens(right), partyTokens);
    }

    private static HashSet<string> Tokens(string s) =>
        new(Normalize(s ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    // Whole-token comparison (WR-10): "Ace" must not match inside "Grace", nor "Gold" inside "Goldberg"
    private static bool SideMatches(HashSet<string> side, List<HashSet<string>> partyTokens)
    {
        if (side.Count == 0)
            return true;

        return partyTokens.Any(p => side.IsSubsetOf(p) || p.IsSubsetOf(side));
    }
}
