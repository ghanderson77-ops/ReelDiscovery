using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Deterministic organization domains and person addresses for the cast.
/// </summary>
public static partial class EmailIdentity
{
    // Code derives every address so one organization never has two domains (D-14); no mail is ever sent
    // Compared with periods removed and upper-cased, so "L.L.C." matches "LLC" and "N.A." matches "NA"
    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "LLC", "INC", "INCORPORATED", "CORP", "CORPORATION", "CO", "COMPANY", "LLP", "LP",
        "PC", "PLLC", "LTD", "LIMITED", "PLC", "NA"
    };

    private static readonly string[] ReservedSuffixes = { ".example", ".test", ".invalid", ".localhost" };

    private static readonly HashSet<string> ReservedDomains = new(StringComparer.Ordinal)
    {
        "example.com", "example.net", "example.org", "localhost"
    };

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._'+-]*@[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}$", RegexOptions.IgnoreCase, CitationScanner.MatchTimeoutMs)]
    private static partial Regex WellFormedEmail();

    /// <summary>
    /// Folded domain stem for an organization (without TLD); may be empty when the name has no letters or digits.
    /// </summary>
    public static string BaseName(CaseOrganization org)
    {
        var source = !string.IsNullOrWhiteSpace(org.ShortName) ? org.ShortName : org.Name ?? string.Empty;

        if (org.Kind == OrganizationKind.Individual)
        {
            var person = Tokens(!string.IsNullOrWhiteSpace(org.Name) ? org.Name : source);
            var last = person.Count > 0 ? Fold(person[^1]) : string.Empty;
            return last.Length > 0 ? last + "family" : string.Empty;
        }

        var tokens = StripName(source);

        if (org.Kind == OrganizationKind.LawFirm)
        {
            // Firm names are surname lists: "Thistle & Bracken", "Okafor, Lindqvist and Marsh" -> first two surnames
            var surnames = tokens
                .Where(t => !t.Equals("and", StringComparison.OrdinalIgnoreCase))
                .Select(t => Fold(t))
                .Where(t => t.Length > 0)
                .Take(2);
            return string.Concat(surnames);
        }

        return string.Concat(tokens.Select(t => Fold(t)));
    }

    /// <summary>
    /// Realistic unique domain: .gov for agencies, .com otherwise. Collisions append "law"/"group", then 2, 3...
    /// The result is added to <paramref name="takenDomains"/>; reserved test domains are never returned.
    /// </summary>
    public static string DomainFor(CaseOrganization org, ISet<string> takenDomains)
    {
        var tld = org.Kind == OrganizationKind.Agency ? ".gov" : ".com";
        var stem = BaseName(org);
        if (stem.Length == 0)
            stem = "org";

        var result = stem + tld;
        if (!IsAvailable(result, takenDomains))
        {
            result = stem + (org.Kind == OrganizationKind.LawFirm ? "law" : "group") + tld;
            for (var n = 2; !IsAvailable(result, takenDomains); n++)
                result = stem + n.ToString(CultureInfo.InvariantCulture) + tld;
        }

        takenDomains.Add(result);
        return result;
    }

    /// <summary>
    /// first.last@domain (folded, hyphens kept); collisions append 2, 3... to the local part.
    /// The result is added to <paramref name="takenAddresses"/>.
    /// </summary>
    public static string AddressFor(string firstName, string lastName, string domain, ISet<string> takenAddresses)
    {
        var first = Fold(firstName, keepHyphen: true);
        var last = Fold(lastName, keepHyphen: true);
        var local = first.Length > 0 && last.Length > 0 ? first + "." + last : first + last;
        if (local.Length == 0)
            local = "contact";

        var host = (domain ?? string.Empty).Trim().ToLowerInvariant();
        var result = local + "@" + host;
        for (var n = 2; takenAddresses.Contains(result); n++)
            result = local + n.ToString(CultureInfo.InvariantCulture) + "@" + host;

        takenAddresses.Add(result);
        return result;
    }

    /// <summary>
    /// Accent-folded lowercase text restricted to [a-z0-9] (plus '-' when <paramref name="keepHyphen"/>),
    /// so names can never inject separators, quotes or header characters.
    /// </summary>
    public static string Fold(string text, bool keepHyphen = false)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(ch);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9') || (keepHyphen && lower == '-'))
                sb.Append(lower);
        }

        return keepHyphen ? sb.ToString().Trim('-') : sb.ToString();
    }

    /// <summary>
    /// True for RFC 2606/6761 reserved names (example.com/.net/.org, *.example, *.test, *.invalid, *.localhost, localhost).
    /// Accepts an email address or a bare domain.
    /// </summary>
    public static bool IsReservedDomain(string? domainOrEmail)
    {
        var domain = domainOrEmail != null && domainOrEmail.Contains('@')
            ? DomainOf(domainOrEmail)
            : (domainOrEmail ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

        if (domain.Length == 0)
            return false;

        return ReservedDomains.Contains(domain) || ReservedSuffixes.Any(s => domain.EndsWith(s, StringComparison.Ordinal));
    }

    public static bool IsWellFormed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            return WellFormedEmail().IsMatch(email);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lower-cased text after the last '@', or empty when there is none.
    /// </summary>
    public static string DomainOf(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return string.Empty;

        var at = email.LastIndexOf('@');
        return at < 0 ? string.Empty : email[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
    }

    // Leading words that make a phrase a description of someone ("A friend", "My mom", "The Smith family")
    private static readonly HashSet<string> NonNameLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "my", "your", "our", "his", "her", "their", "some", "someone"
    };

    // Relationship and role words a model gives instead of a name ("A neighbor", "Book Club")
    private static readonly HashSet<string> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "friend", "neighbor", "neighbour", "mom", "mum", "dad", "mother", "father", "brother", "sister", "cousin", "aunt",
        "uncle", "grandma", "grandpa", "family", "buddy", "pal", "roommate", "coworker", "colleague", "contact", "member",
        "team", "club", "group", "sender", "person", "someone", "unknown", "anonymous", "customer", "user"
    };

    // Lower-case surname particles allowed between the first and last name ("Luis de la Cruz")
    private static readonly HashSet<string> NameParticles = new(StringComparer.Ordinal)
    {
        "de", "del", "della", "da", "di", "du", "la", "le", "van", "von", "der", "den", "bin", "al"
    };

    public const int MaxPersonNameTokens = 5;

    /// <summary>
    /// True when <paramref name="name"/> reads as a person's first and last name: 2 to 5 tokens, each a capitalized word
    /// (hyphens and apostrophes allowed, e.g. O'Brien, Smith-Jones), with initials ("J.") and lower-case particles
    /// ("de", "van") only between the first and last token. Roles and descriptions such as "A friend", "My mom",
    /// "The Smith family" or "Book Club" are refused.
    /// </summary>
    public static bool LooksLikePersonName(string? name)
    {
        var tokens = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || tokens.Length > MaxPersonNameTokens || NonNameLeadWords.Contains(tokens[0]))
            return false;

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (RoleWords.Contains(token.Trim('\'', '’', '-')))
                return false;

            var middle = i > 0 && i < tokens.Length - 1;
            if (NameWordPattern().IsMatch(token))
                continue;
            if (middle && (InitialPattern().IsMatch(token) || NameParticles.Contains(token)))
                continue;
            return false;
        }

        return true;
    }

    // "Dana", "O'Neil", "Mary-Kate", "McAllister": an upper-case letter, then letters with single inner ' or - joins
    [GeneratedRegex(@"^\p{Lu}\p{Ll}*(?:\p{Lu}\p{Ll}+)?(?:['’-]\p{L}\p{Ll}*)*$", RegexOptions.None, CitationScanner.MatchTimeoutMs)]
    private static partial Regex NameWordPattern();

    // "J." middle initial
    [GeneratedRegex(@"^\p{Lu}\.$", RegexOptions.None, CitationScanner.MatchTimeoutMs)]
    private static partial Regex InitialPattern();

    private static bool IsAvailable(string domain, ISet<string> takenDomains) =>
        !takenDomains.Contains(domain) && !IsReservedDomain(domain);

    // Whitespace/comma/ampersand tokens with leading "The" and trailing legal suffixes removed
    private static List<string> StripName(string name)
    {
        var tokens = Tokens(name);

        if (tokens.Count > 1 && tokens[0].Equals("the", StringComparison.OrdinalIgnoreCase))
            tokens.RemoveAt(0);

        while (tokens.Count > 1 && LegalSuffixes.Contains(tokens[^1].Replace(".", string.Empty).ToUpperInvariant()))
            tokens.RemoveAt(tokens.Count - 1);

        return tokens;
    }

    private static List<string> Tokens(string name) =>
        (name ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
