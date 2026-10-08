using System.Globalization;
using System.Text.RegularExpressions;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Name, date and identifier scanners shared by RenderValidator and DiscoveryValidator.
/// </summary>
internal static partial class TextScan
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Capitalized words that follow a first name without being a surname ("Tom Monday", "Lisa Thanks")
    internal static readonly HashSet<string> NotSurnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        "The", "This", "That", "These", "Those", "Please", "Thanks", "Thank", "And", "But", "Also", "Can", "Could", "Will", "Would",
        "Let", "Just", "Here", "There", "Team", "Everyone", "All"
    };

    internal static readonly string[] Honorifics = { "mr.", "mr", "ms.", "ms", "mrs.", "mrs", "dr.", "dr" };

    // Overlapping "First Last" pairs on one line (a newline never joins two names)
    [GeneratedRegex(@"\b([A-Z][a-z]+)(?=[ \t]+([A-Z][a-z]+)\b)", RegexOptions.None, 1000)]
    internal static partial Regex NameBigram();

    [GeneratedRegex(@"\b(?:Mr|Ms|Mrs|Dr)\.?[ \t]+([A-Z][a-z]+)\b", RegexOptions.None, 1000)]
    internal static partial Regex HonorificName();

    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    internal static partial Regex Whitespace();

    // Leading letters of a canonical identifier: "CO-007" -> "CO-", "INV-2024-118" -> "INV-"
    [GeneratedRegex(@"^([A-Za-z]+-)", RegexOptions.None, 1000)]
    internal static partial Regex IdentifierPrefix();

    [GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.None, 1000)]
    internal static partial Regex IsoDate();

    // en-US "April 9", "Apr 9, 2024", "Sept. 3rd 2024" (capitalized month names only, so "may" the verb never matches)
    [GeneratedRegex(@"\b(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?[ \t]+(\d{1,2})(?:st|nd|rd|th)?\b(?:,?[ \t]+(\d{4})\b)?", RegexOptions.None, 1000)]
    internal static partial Regex MonthDate();

    private static readonly string[] MonthPrefixes = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    // Valid dates in order of appearance; a month-day date without a year takes the message's year
    internal static List<DateOnly> DatesIn(string sentence, int defaultYear) =>
        DateMatchesIn(sentence, defaultYear).Select(f => f.Date).ToList();

    // Valid dates with the matched text, in order of appearance (DiscoveryValidator reports the text)
    internal static List<(string Text, DateOnly Date)> DateMatchesIn(string sentence, int defaultYear)
    {
        var found = new List<(int Index, string Text, DateOnly Date)>();
        foreach (Match m in IsoDate().Matches(sentence))
        {
            if (TryDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var d))
                found.Add((m.Index, m.Value, d));
        }

        foreach (Match m in MonthDate().Matches(sentence))
        {
            var month = Array.IndexOf(MonthPrefixes, m.Groups[1].Value[..3].ToLowerInvariant()) + 1;
            var year = m.Groups[3].Success ? m.Groups[3].Value : defaultYear.ToString(Inv);
            if (month > 0 && TryDate(year, month.ToString(Inv), m.Groups[2].Value, out var d))
                found.Add((m.Index, m.Value, d));
        }

        return found.OrderBy(f => f.Index).Select(f => (f.Text, f.Date)).ToList();
    }

    internal static bool TryDate(string year, string month, string day, out DateOnly date)
    {
        date = default;
        if (!int.TryParse(year, NumberStyles.None, Inv, out var y) || !int.TryParse(month, NumberStyles.None, Inv, out var mo)
            || !int.TryParse(day, NumberStyles.None, Inv, out var d))
            return false;
        if (y < 1 || y > 9999 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo))
            return false;
        date = new DateOnly(y, mo, d);
        return true;
    }
}
