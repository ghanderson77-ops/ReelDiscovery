using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

public enum ScannedKind
{
    Amount,
    Identifier
}

public class ScannedValue
{
    public ScannedKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Deterministic scan for dollar amounts and document identifiers (GUIDE-06 cross-check) so the validator can
/// report values the text uses but the canonical value registry does not hold.
/// </summary>
public static partial class CanonicalValueScanner
{
    // "$184,250.00", "$12,000", "$7.5 million", "$ 500"; the grouped form is tried first so commas are kept
    [GeneratedRegex(@"\$\s?\d{1,3}(?:,\d{3})+(?:\.\d{2})?(?:\s(?:million|billion|thousand)\b)?|\$\s?\d+(?:\.\d+)?(?:\s(?:million|billion|thousand)\b)?", RegexOptions.IgnoreCase, CitationScanner.MatchTimeoutMs)]
    private static partial Regex Amount();

    // "CO-007", "INV-2024-118", "PO-4471", "SOW-3", "ACCT-99812"
    [GeneratedRegex(@"\b(?:CO|INV|PO|SOW|CN|AC|ACCT)-\d[\w-]*\b", RegexOptions.IgnoreCase, CitationScanner.MatchTimeoutMs)]
    private static partial Regex PrefixedIdentifier();

    // "No. 2023-17", "#4471" (at least two digits, so "Count No. 2" and "#1" are ignored)
    [GeneratedRegex(@"(?:\bNo\.\s?|#)\d[\d-]{1,}\b", RegexOptions.IgnoreCase, CitationScanner.MatchTimeoutMs)]
    private static partial Regex NumberedIdentifier();

    /// <summary>
    /// Amounts and identifiers in order of appearance, distinct by text (case-insensitive). Never throws.
    /// </summary>
    public static List<ScannedValue> Scan(string? text)
    {
        var result = new List<ScannedValue>();
        if (string.IsNullOrEmpty(text))
            return result;

        var found = new List<(int Index, ScannedValue Value)>();
        Collect(found, Amount(), text, ScannedKind.Amount);
        Collect(found, PrefixedIdentifier(), text, ScannedKind.Identifier);
        Collect(found, NumberedIdentifier(), text, ScannedKind.Identifier);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, value) in found.OrderBy(f => f.Index))
        {
            if (seen.Add(value.Text))
                result.Add(value);
        }

        return result;
    }

    /// <summary>
    /// True when both texts are the same dollar amount ("$184,250" == "$184,250.00", "$7.5 million" == "$7,500,000").
    /// Texts that are not amounts compare as trimmed, case-insensitive strings.
    /// </summary>
    public static bool AmountsEqual(string a, string b)
    {
        if (TryParseAmount(a, out var left) && TryParseAmount(b, out var right))
            return left == right;

        return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Amounts match any Amount-kind value by <see cref="AmountsEqual"/>; identifiers match any value whose
    /// text contains them or is contained by them (case-insensitive).
    /// </summary>
    public static bool IsRegistered(ScannedValue scanned, IEnumerable<CanonicalValue> values)
    {
        if (scanned == null || string.IsNullOrWhiteSpace(scanned.Text) || values == null)
            return false;

        foreach (var value in values)
        {
            var text = value?.Value?.Trim() ?? string.Empty;
            if (text.Length == 0)
                continue;

            if (scanned.Kind == ScannedKind.Amount)
            {
                if (value!.Kind == ValueKind.Amount && AmountsEqual(text, scanned.Text))
                    return true;
            }
            else if (text.Contains(scanned.Text, StringComparison.OrdinalIgnoreCase)
                || scanned.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Collect(List<(int, ScannedValue)> found, Regex pattern, string text, ScannedKind kind)
    {
        try
        {
            foreach (Match m in pattern.Matches(text))
                found.Add((m.Index, new ScannedValue { Kind = kind, Text = m.Value }));
        }
        catch (RegexMatchTimeoutException)
        {
            // Too large to scan fully; the validator reports what it found
        }
    }

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        var s = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (!s.StartsWith('$'))
            return false;

        decimal multiplier = 1;
        foreach (var (word, factor) in new[] { ("thousand", 1_000m), ("million", 1_000_000m), ("billion", 1_000_000_000m) })
        {
            if (s.EndsWith(word, StringComparison.Ordinal))
            {
                multiplier = factor;
                s = s[..^word.Length];
                break;
            }
        }

        s = s.Replace("$", string.Empty).Replace(",", string.Empty).Replace(" ", string.Empty);
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            return false;

        try
        {
            amount = number * multiplier;
            return true;
        }
        catch (OverflowException)
        {
            // Absurdly large amount: treat as not comparable rather than throw
            return false;
        }
    }
}
