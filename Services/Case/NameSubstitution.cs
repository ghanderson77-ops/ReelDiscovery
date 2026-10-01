using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Deterministic real-name to fictional-name substitution (CASE-03) and the post-generation leak scan.
/// Only entries with Apply = true and a non-blank Substitute take part.
/// </summary>
public static partial class NameSubstitution
{
    [GeneratedRegex(@",?\s+(?:Inc\.?|LLC|L\.L\.C\.|Corp\.?|Corporation|Co\.?|Ltd\.?|LP|LLP)$")]
    private static partial Regex CorporateSuffix();

    public static string Apply(string text, IEnumerable<EntitySubstitution> substitutions)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var map = TermMap.Build(substitutions);
        if (map.IsEmpty)
            return text;

        // One pass: replaced text is never re-scanned, so A->B, B->C never chains.
        return map.Pattern.Replace(text, m => map.Lookup(m.Value)?.Target ?? m.Value);
    }

    public static List<string> FindLeaks(string text, IEnumerable<EntitySubstitution> substitutions)
    {
        var leaks = new List<string>();
        if (string.IsNullOrEmpty(text))
            return leaks;

        var map = TermMap.Build(substitutions);
        if (map.IsEmpty)
            return leaks;

        foreach (Match m in map.Pattern.Matches(text))
        {
            var term = map.Lookup(m.Value)?.Term ?? m.Value;
            if (!leaks.Contains(term, StringComparer.Ordinal))
                leaks.Add(term);
        }

        return leaks;
    }

    private static string AliasTarget(EntitySubstitution entry, string original, string substitute, string alias)
    {
        if (entry.Kind is EntityKind.Person or EntityKind.FictionalWorkCharacter)
        {
            var originalTokens = original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var substituteTokens = substitute.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (string.Equals(alias, originalTokens[^1], StringComparison.Ordinal))
                return substituteTokens[^1];

            // First name, or a nickname that shortens it ("Walt" for "Walter"), maps to the substitute's first name
            if (!alias.Any(char.IsWhiteSpace)
                && originalTokens.Length > 1
                && originalTokens[0].StartsWith(alias, StringComparison.Ordinal))
                return substituteTokens[0];

            return substitute;
        }

        // "Apple" for "Apple Inc." -> "Orchard Systems", not "Orchard Systems Inc."
        var stripped = CorporateSuffix().Replace(substitute, string.Empty).Trim();
        return stripped.Length > 0 ? stripped : substitute;
    }

    private class TermEntry
    {
        public string Term { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    private class TermMap
    {
        // Single-token terms (surnames, short company names) are case-sensitive so "White" != "white paper"
        private readonly Dictionary<string, TermEntry> _single = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TermEntry> _multi = new(StringComparer.OrdinalIgnoreCase);

        public Regex Pattern { get; private set; } = null!;
        public bool IsEmpty => _single.Count == 0 && _multi.Count == 0;

        public static TermMap Build(IEnumerable<EntitySubstitution> substitutions)
        {
            var map = new TermMap();

            foreach (var entry in substitutions ?? Enumerable.Empty<EntitySubstitution>())
            {
                if (entry == null || !entry.Apply)
                    continue;

                var original = entry.Original?.Trim() ?? string.Empty;
                var substitute = entry.Substitute?.Trim() ?? string.Empty;
                if (original.Length == 0 || substitute.Length == 0)
                    continue;

                map.Add(original, substitute);

                foreach (var rawAlias in entry.Aliases ?? new List<string>())
                {
                    var alias = rawAlias?.Trim() ?? string.Empty;
                    if (alias.Length == 0 || string.Equals(alias, original, StringComparison.OrdinalIgnoreCase))
                        continue;

                    map.Add(alias, AliasTarget(entry, original, substitute, alias));
                }
            }

            if (!map.IsEmpty)
                map.Pattern = map.BuildPattern();

            return map;
        }

        public TermEntry? Lookup(string value)
        {
            if (!value.Any(char.IsWhiteSpace) && _single.TryGetValue(value, out var single))
                return single;

            return _multi.TryGetValue(value, out var multi) ? multi : null;
        }

        private void Add(string term, string target)
        {
            // First mapping for a term wins
            var dictionary = term.Any(char.IsWhiteSpace) ? _multi : _single;
            if (!dictionary.ContainsKey(term))
                dictionary[term] = new TermEntry { Term = term, Target = target };
        }

        private Regex BuildPattern()
        {
            var alternation = _single.Keys.Select(k => (Term: k, Multi: false))
                .Concat(_multi.Keys.Select(k => (Term: k, Multi: true)))
                .OrderByDescending(t => t.Term.Length)
                .ThenBy(t => t.Term, StringComparer.Ordinal)
                .Select(t => t.Multi ? $"(?i:{Regex.Escape(t.Term)})" : Regex.Escape(t.Term));

            var pattern = $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join("|", alternation)})(?![\p{{L}}\p{{N}}])";

            // Pattern is runtime data so [GeneratedRegex] is impossible; literals are Regex.Escape'd and a match timeout guards ReDoS (Security V5).
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
    }
}
