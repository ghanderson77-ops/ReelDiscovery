using System.Globalization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Code-owned entity ids (ISS-01, ORG-01, CH-001, F-001, V-001). The model never assigns ids.
/// </summary>
public static class IdAllocator
{
    public const string Issue = "ISS";
    public const string Organization = "ORG";
    public const string Character = "CH";
    public const string Fact = "F";
    public const string Value = "V";

    // Longest digit run accepted from model or user text
    private const int MaxDigits = 6;

    /// <summary>
    /// Zero-padding width for a prefix: ISS and ORG use 2 digits, everything else 3.
    /// </summary>
    public static int WidthFor(string prefix) =>
        prefix is Issue or Organization ? 2 : 3;

    /// <summary>
    /// Allocates the next id for a prefix and records it in <see cref="CaseGuide.IdCounters"/>.
    /// </summary>
    public static string Next(CaseGuide guide, string prefix)
    {
        guide.IdCounters ??= new();

        var max = 0;
        foreach (var id in SafeIds(guide))
        {
            if (TryParseNumber(id, prefix, out var n) && n > max)
                max = n;
        }

        // Counter only grows: deleted IDs may still be cited by exports and later phases
        var next = Math.Max(max, guide.IdCounters.GetValueOrDefault(prefix)) + 1;
        guide.IdCounters[prefix] = next;
        return Format(prefix, next);
    }

    /// <summary>
    /// Canonicalizes a model- or user-supplied id (" ch-7 " -> "CH-007"); returns null unless it is
    /// exactly the prefix, a dash and 1-6 digits.
    /// </summary>
    public static string? Normalize(string? raw, string prefix)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim().ToUpperInvariant();
        return TryParseNumber(text, prefix, out var n) ? Format(prefix, n) : null;
    }

    // CaseGuide.AllIds without assuming Normalize() already repaired null lists
    private static IEnumerable<string> SafeIds(CaseGuide guide) =>
        (guide.Issues?.Select(x => x?.Id) ?? Enumerable.Empty<string?>())
            .Concat(guide.Organizations?.Select(x => x?.Id) ?? Enumerable.Empty<string?>())
            .Concat(guide.Cast?.Select(x => x?.Id) ?? Enumerable.Empty<string?>())
            .Concat(guide.Facts?.Select(x => x?.Id) ?? Enumerable.Empty<string?>())
            .Concat(guide.Values?.Select(x => x?.Id) ?? Enumerable.Empty<string?>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!);

    private static string Format(string prefix, int number) =>
        prefix + "-" + number.ToString(new string('0', WidthFor(prefix)), CultureInfo.InvariantCulture);

    private static bool TryParseNumber(string? id, string prefix, out int number)
    {
        number = 0;
        if (string.IsNullOrEmpty(id))
            return false;

        var head = prefix + "-";
        if (!id.StartsWith(head, StringComparison.Ordinal))
            return false;

        var digits = id[head.Length..];
        if (digits.Length == 0 || digits.Length > MaxDigits || !digits.All(char.IsAsciiDigit))
            return false;

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
