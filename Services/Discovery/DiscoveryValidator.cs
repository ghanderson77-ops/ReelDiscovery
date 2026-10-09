using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// What one discovery check needs: the case, the document kind and the part or slots the model was asked for.
/// </summary>
public class DiscoveryCheckContext
{
    public CaseDraft Draft { get; set; } = new();
    public DiscoveryKind Kind { get; set; }

    // Deposition part number (1-based) and its target turn count; 0 turns = no length check
    public int Part { get; set; }
    public int TargetTurns { get; set; }
    public List<int> ExpectedExhibits { get; set; } = new();
    public DateOnly? DepositionDate { get; set; }

    // Code-provided people (reporter, videographer) and document numbers (exhibit DocIds) that are not in the guide
    public HashSet<string> ExtraNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AllowedIdentifiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Written discovery: the request numbers asked for, the fact each RFA slot proves and the genuineness RFAs
    public HashSet<int> ExpectedSlots { get; set; } = new();
    public Dictionary<int, string> SlotFactIds { get; set; } = new();
    public HashSet<int> GenuinenessSlots { get; set; } = new();
}

/// <summary>
/// Deterministic D-26 checks of generated discovery text (no LLM judge), mirroring RenderValidator: the violations decide
/// whether a part is accepted or retried with <see cref="FeedbackText"/>, and <see cref="IsBetter"/> keeps the best
/// attempt. Static and never throws on null or empty input.
/// </summary>
public static partial class DiscoveryValidator
{
    public const string FeedbackHeader = "Problems in the previous attempt (fix all of them and rewrite the whole part):";

    // D1: a chunk shorter than half its target turn count is retried (research Pattern 8)
    public const double MinTurnShare = 0.5;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // "(a)", "(ii)", "(2)" not glued to a word, so "person(s)" and "Section 3(1)" never count as subparts
    [GeneratedRegex(@"(?<![A-Za-z0-9])\(\s*(?:[a-z]|[ivx]{1,4}|\d{1,2})\s*\)", RegexOptions.None, 1000)]
    private static partial Regex SubpartMarker();

    // A line that starts "a. ", "b) ", "1. " or "2) "
    [GeneratedRegex(@"(?m)^\s*(?:[a-z]|\d{1,2})[.)]\s", RegexOptions.None, 1000)]
    private static partial Regex SubpartLine();

    /// <summary>
    /// D1 (length, exhibits), D2, D3, D6 and D8 over one deposition chunk.
    /// </summary>
    public static List<DiscoveryViolation> CheckDepositionChunk(IReadOnlyList<DepositionTurn> turns, IReadOnlyList<string> peopleNamed,
        DiscoveryCheckContext ctx)
    {
        var result = new List<DiscoveryViolation>();
        if (ctx == null)
            return result;

        var list = (turns ?? Array.Empty<DepositionTurn>()).Where(t => t != null).ToList();
        var text = string.Join("\n", list.Select(t => t.Text ?? string.Empty));
        var names = NameSets.From(ctx);

        // D1: length and assigned exhibits
        if (ctx.TargetTurns > 0 && list.Count < ctx.TargetTurns * MinTurnShare)
            result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D1ShortPart, Number = ctx.Part, Expected = ctx.TargetTurns, Actual = list.Count });

        foreach (var exhibit in (ctx.ExpectedExhibits ?? new List<int>()).Distinct())
        {
            var shown = list.Any(t => t.ExhibitNumber == exhibit)
                || RenderValidator.ContainsWord(text, "Exhibit " + exhibit.ToString(Inv));
            if (!shown)
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D1ExhibitNotShown, Number = exhibit });
        }

        foreach (var name in peopleNamed ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && !names.IsKnownName(name))
                result.Add(UnknownPerson(ctx.Part, TextScan.Whitespace().Replace(name.Trim(), " ")));
        }

        result.AddRange(CheckPeople(text, ctx.Part, names));
        result.AddRange(CheckValues(text, ctx.Part, ctx));
        result.AddRange(CheckDates(text, ctx.Part, ctx));
        result.AddRange(CheckRealNames(text, ctx.Part, ctx));
        return Distinct(result);
    }

    /// <summary>
    /// D4, D2, D3 and D8 over numbered written discovery requests; D5 for admissions and D7 for interrogatories. For D5
    /// the caller passes each slot's code-owned prefix joined to the model text. <paramref name="peopleNamed"/> is the
    /// model's own list of people it named: each unknown one is a D2 on the slot whose text mentions it (0 when none does).
    /// </summary>
    public static List<DiscoveryViolation> CheckRequests(IReadOnlyList<(int Slot, string Text)> requests, DiscoveryCheckContext ctx,
        IReadOnlyList<string>? peopleNamed = null)
    {
        var result = new List<DiscoveryViolation>();
        if (ctx == null)
            return result;

        var list = requests ?? Array.Empty<(int Slot, string Text)>();
        var expected = ctx.ExpectedSlots ?? new HashSet<int>();

        // D4: every slot exactly once
        var counts = list.GroupBy(r => r.Slot).ToDictionary(g => g.Key, g => g.Count());
        foreach (var slot in expected.Where(s => !counts.ContainsKey(s)).OrderBy(s => s))
            result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D4MissingRequest, Number = slot, Expected = 1, Actual = 0 });
        foreach (var (slot, count) in counts.OrderBy(c => c.Key))
        {
            if (!expected.Contains(slot) || count > 1)
            {
                // Expected = 1 marks a duplicate of a requested slot; 0 a slot nobody asked for
                result.Add(new DiscoveryViolation
                {
                    Rule = DiscoveryRule.D4ExtraRequest, Number = slot, Expected = expected.Contains(slot) ? 1 : 0, Actual = count
                });
            }
        }

        var names = NameSets.From(ctx);

        // The NameBigram/HonorificName scans miss invented names whose first name is not in the cast (as depositions do)
        foreach (var name in peopleNamed ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name) || names.IsKnownName(name))
                continue;
            var clean = TextScan.Whitespace().Replace(name.Trim(), " ");
            var slot = list.Where(r => !string.IsNullOrEmpty(r.Text) && RenderValidator.ContainsWord(r.Text, clean))
                .Select(r => r.Slot)
                .DefaultIfEmpty(0)
                .First();
            result.Add(UnknownPerson(slot, clean));
        }

        foreach (var (slot, raw) in list)
        {
            var text = raw ?? string.Empty;
            if (text.Length == 0)
                continue;

            result.AddRange(CheckPeople(text, slot, names));
            result.AddRange(CheckValues(text, slot, ctx));
            result.AddRange(CheckRealNames(text, slot, ctx));

            if (ctx.Kind == DiscoveryKind.RequestsForAdmission)
                result.AddRange(CheckFactDetail(text, slot, ctx));
            if (ctx.Kind == DiscoveryKind.Interrogatories && HasSubparts(text))
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D7Subparts, Number = slot });
        }

        return Distinct(result);
    }

    /// <summary>
    /// Keep-best (Phase 5 D-14): any attempt beats none, a non-structural attempt beats a structural one, then fewer
    /// violations wins and a tie goes to the later attempt.
    /// </summary>
    public static bool IsBetter(IReadOnlyList<DiscoveryViolation> candidate, IReadOnlyList<DiscoveryViolation>? best)
    {
        if (best == null)
            return true;

        var candidateList = candidate ?? Array.Empty<DiscoveryViolation>();
        var candidateStructural = candidateList.Any(v => v != null && v.IsStructural);
        var bestStructural = best.Any(v => v != null && v.IsStructural);
        if (bestStructural != candidateStructural)
            return bestStructural;
        return candidateList.Count <= best.Count;
    }

    /// <summary>
    /// Model-facing instruction that fixes one violation.
    /// </summary>
    public static string FeedbackLine(DiscoveryViolation v)
    {
        if (v == null)
            return string.Empty;

        var n = v.Number.ToString(Inv);
        return v.Rule switch
        {
            DiscoveryRule.D1ShortPart => $"Part {n} has {v.Actual.ToString(Inv)} turns; write about {v.Expected.ToString(Inv)}.",
            DiscoveryRule.D1ExhibitNotShown => $"Show Exhibit {n} to the witness in this part.",
            DiscoveryRule.D2UnknownPerson => $"\"{v.Value}\" is not in the cast; use only the people listed.",
            DiscoveryRule.D3AmountVariant => $"\"{v.Value}\" is close to the case amount {v.Canonical}; use the exact amount.",
            DiscoveryRule.D3UnknownIdentifier => $"\"{v.Value}\" is not a case document number; use only the listed identifiers.",
            DiscoveryRule.D4MissingRequest => $"Request {n} is missing; return every slot exactly once.",
            DiscoveryRule.D4ExtraRequest => v.Expected > 0
                ? $"Request {n} appears {v.Actual.ToString(Inv)} times; return every slot exactly once."
                : $"Request {n} was not asked for; return only the listed slots.",
            DiscoveryRule.D5MissingFactDetail => CanonicalValueScanner.TryParseAmount(v.Value, out _)
                ? $"Admission {n} must state {v.Value}."
                : $"Admission {n} must name {v.Value}.",
            DiscoveryRule.D6AfterDepositionDate => $"{v.Value} is after the deposition date; nothing after it can be mentioned.",
            DiscoveryRule.D7Subparts => $"Interrogatory {n} has lettered or numbered parts; ask one question.",
            // The real name (Value) never goes back to the model: it may be a configured alias it never wrote (T-08-32)
            DiscoveryRule.D8RealEntityName => $"Part or request {n} uses a real-world name; use \"{v.Canonical}\" instead.",
            _ => v.Value
        };
    }

    /// <summary>
    /// Retry feedback: the header, then one "- " line per violation.
    /// </summary>
    public static string FeedbackText(IReadOnlyList<DiscoveryViolation> violations)
    {
        var lines = (violations ?? Array.Empty<DiscoveryViolation>())
            .Where(v => v != null)
            .Select(v => "- " + FeedbackLine(v));
        return string.Join("\n", new[] { FeedbackHeader }.Concat(lines));
    }

    // ---------- D2: unknown people (mirrors RenderValidator V2) ----------

    private static IEnumerable<DiscoveryViolation> CheckPeople(string text, int number, NameSets names)
    {
        var result = new List<DiscoveryViolation>();
        if (names.FirstNames.Count == 0 && names.LastNames.Count == 0)
            return result;

        // "Michael Jordan": a known first name followed by a surname nobody in the case has
        foreach (Match m in TextScan.NameBigram().Matches(text))
        {
            var first = m.Groups[1].Value;
            var second = m.Groups[2].Value;
            if (!names.FirstNames.Contains(first) || TextScan.NotSurnames.Contains(second))
                continue;
            if (names.FullNames.Contains(first + " " + second) || names.FirstNames.Contains(second) || names.LastNames.Contains(second))
                continue;
            result.Add(UnknownPerson(number, first + " " + second));
        }

        // "Mr. Halbrook" with no such name
        foreach (Match m in TextScan.HonorificName().Matches(text))
        {
            var surname = m.Groups[1].Value;
            if (!names.LastNames.Contains(surname) && !names.FirstNames.Contains(surname))
                result.Add(UnknownPerson(number, TextScan.Whitespace().Replace(m.Value, " ")));
        }

        return result;
    }

    private static DiscoveryViolation UnknownPerson(int number, string name) =>
        new() { Rule = DiscoveryRule.D2UnknownPerson, Number = number, Value = name };

    // ---------- D3: canonical value variants (mirrors RenderValidator V3) ----------

    private static IEnumerable<DiscoveryViolation> CheckValues(string text, int number, DiscoveryCheckContext ctx)
    {
        var result = new List<DiscoveryViolation>();
        var values = (ctx.Draft?.Guide?.Values ?? new List<CanonicalValue>())
            .Where(v => v != null && !string.IsNullOrWhiteSpace(v.Value))
            .ToList();
        if (values.Count == 0)
            return result;

        var amounts = values
            .Where(v => v.Kind == ValueKind.Amount)
            .Select(v => (Value: v, Ok: CanonicalValueScanner.TryParseAmount(v.Value.Trim(), out var amount), Amount: amount))
            .Where(a => a.Ok && a.Amount > 0)
            .Select(a => (a.Value, a.Amount))
            .ToList();
        var prefixes = values
            .Where(v => v.Kind != ValueKind.Amount)
            .Select(v => TextScan.IdentifierPrefix().Match(v.Value.Trim()))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var identifiers = values.Where(v => v.Kind != ValueKind.Amount).ToList();

        foreach (var scanned in CanonicalValueScanner.Scan(text))
        {
            if (scanned.Kind == ScannedKind.Amount)
            {
                if (!CanonicalValueScanner.TryParseAmount(scanned.Text, out var amount)
                    || amounts.Any(a => CanonicalValueScanner.AmountsEqual(a.Value.Value, scanned.Text)))
                    continue;

                var closest = amounts
                    .Where(a => Math.Abs(amount - a.Amount) <= a.Amount * RenderValidator.VariantWindow)
                    .OrderBy(a => Math.Abs(amount - a.Amount))
                    .Select(a => a.Value)
                    .FirstOrDefault();
                if (closest != null)
                {
                    result.Add(new DiscoveryViolation
                    {
                        Rule = DiscoveryRule.D3AmountVariant, Number = number, Value = scanned.Text, Canonical = closest.Value.Trim()
                    });
                }
            }
            else if (prefixes.Any(p => scanned.Text.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                && !CanonicalValueScanner.IsRegistered(scanned, identifiers)
                && !(ctx.AllowedIdentifiers?.Contains(scanned.Text) ?? false))
            {
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D3UnknownIdentifier, Number = number, Value = scanned.Text });
            }
        }

        return result;
    }

    // ---------- D5: RFA fidelity ----------

    // A fact-keyed admission states every canonical amount of its fact and names each of the fact's actors (full, first or
    // last name); genuineness admissions prove a document, not a fact, so they are skipped
    private static IEnumerable<DiscoveryViolation> CheckFactDetail(string text, int slot, DiscoveryCheckContext ctx)
    {
        var result = new List<DiscoveryViolation>();
        var guide = ctx.Draft?.Guide;
        if (guide == null || (ctx.GenuinenessSlots?.Contains(slot) ?? false)
            || ctx.SlotFactIds == null || !ctx.SlotFactIds.TryGetValue(slot, out var factId))
            return result;

        var fact = guide.FindFact(factId);
        if (fact == null)
            return result;

        var scannedAmounts = CanonicalValueScanner.Scan(text).Where(s => s.Kind == ScannedKind.Amount).ToList();
        foreach (var value in (guide.Values ?? new List<CanonicalValue>())
                     .Where(v => v != null && v.Kind == ValueKind.Amount && !string.IsNullOrWhiteSpace(v.Value)
                         && (v.FactIds ?? new List<string>()).Contains(fact.Id, StringComparer.OrdinalIgnoreCase)))
        {
            if (!scannedAmounts.Any(s => CanonicalValueScanner.AmountsEqual(value.Value, s.Text)))
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D5MissingFactDetail, Number = slot, Value = value.Value.Trim(), Canonical = fact.Id });
        }

        foreach (var actorId in (fact.ActorIds ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var actor = guide.FindCast(actorId);
            if (actor == null || actor.FullName.Length == 0)
                continue;

            var named = new[] { actor.FullName, actor.FirstName, actor.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Any(s => RenderValidator.ContainsWord(text, s.Trim()));
            if (!named)
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D5MissingFactDetail, Number = slot, Value = actor.FullName, Canonical = fact.Id });
        }

        return result;
    }

    // ---------- D6: after the deposition date ----------

    private static IEnumerable<DiscoveryViolation> CheckDates(string text, int number, DiscoveryCheckContext ctx)
    {
        var result = new List<DiscoveryViolation>();
        if (ctx.DepositionDate is not { } depositionDate)
            return result;

        // Year-less dates are ambiguous; year 1 keeps them below every deposition date so they are never flagged. Over-strict
        // checks become warnings to tune (research Pitfall 7), so only dated mentions are checked
        foreach (var (match, date) in TextScan.DateMatchesIn(text, defaultYear: 1))
        {
            if (date > depositionDate)
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D6AfterDepositionDate, Number = number, Value = match });
        }

        return result;
    }

    // ---------- D7: interrogatory subparts ----------

    // Two or more "(a)"-style markers, two or more "a. "/"1. " lines, or one marker joined to a second clause by " and ("
    private static bool HasSubparts(string text)
    {
        var markers = SubpartMarker().Matches(text).Count;
        return markers >= 2
            || SubpartLine().Matches(text).Count >= 2
            || (markers == 1 && text.Contains(" and (", StringComparison.Ordinal));
    }

    // ---------- D8: real entity names ----------

    // The first original or alias of each applied substitution that still appears
    private static IEnumerable<DiscoveryViolation> CheckRealNames(string text, int number, DiscoveryCheckContext ctx)
    {
        var result = new List<DiscoveryViolation>();
        foreach (var sub in ctx.Draft?.Substitutions ?? new List<EntitySubstitution>())
        {
            if (sub == null || !sub.Apply || string.IsNullOrWhiteSpace(sub.Substitute))
                continue;

            var found = new[] { sub.Original }.Concat(sub.Aliases ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .FirstOrDefault(t => RenderValidator.ContainsWord(text, t));
            if (found != null)
                result.Add(new DiscoveryViolation { Rule = DiscoveryRule.D8RealEntityName, Number = number, Value = found, Canonical = sub.Substitute.Trim() });
        }

        return result;
    }

    // ---------- shared helpers ----------

    // Distinct by (Rule, Number, Value), first occurrence kept
    private static List<DiscoveryViolation> Distinct(List<DiscoveryViolation> violations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return violations.Where(v => seen.Add(v.Rule + "|" + v.Number.ToString(Inv) + "|" + v.Value)).ToList();
    }

    /// <summary>
    /// Known first, last and full names: the cast, the code-provided extra names (whole and by token) and organization
    /// names and short names (whole and by token, so "Brightline" or "Harmon" never reads as an unknown surname).
    /// </summary>
    private sealed class NameSets
    {
        public HashSet<string> FirstNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LastNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FullNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static NameSets From(DiscoveryCheckContext ctx)
        {
            var sets = new NameSets();
            var guide = ctx.Draft?.Guide;
            foreach (var member in guide?.Cast ?? new List<CastMember>())
            {
                if (member == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(member.FirstName))
                    sets.FirstNames.Add(member.FirstName.Trim());
                if (!string.IsNullOrWhiteSpace(member.LastName))
                    sets.LastNames.Add(member.LastName.Trim());
                if (member.FullName.Length > 0)
                    sets.FullNames.Add(TextScan.Whitespace().Replace(member.FullName, " "));
            }

            foreach (var name in ctx.ExtraNames ?? new HashSet<string>())
                sets.AddWhole(name, firstTokenIsFirstName: true);

            foreach (var org in guide?.Organizations ?? new List<CaseOrganization>())
            {
                if (org == null)
                    continue;
                sets.AddWhole(org.Name, firstTokenIsFirstName: false);
                sets.AddWhole(org.ShortName, firstTokenIsFirstName: false);
            }

            return sets;
        }

        // Every token is a known surname-position word; the first token of a person's name is also a known first name
        private void AddWhole(string? name, bool firstTokenIsFirstName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var text = TextScan.Whitespace().Replace(name.Trim(), " ");
            FullNames.Add(text);
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim(',', '.')).Where(t => t.Length > 0).ToList();
            for (var i = 0; i < tokens.Count; i++)
            {
                LastNames.Add(tokens[i]);
                if (i == 0 && firstTokenIsFirstName)
                    FirstNames.Add(tokens[i]);
            }
        }

        /// <summary>
        /// A known first, last or full name, optionally after an honorific or with a possessive "'s".
        /// </summary>
        public bool IsKnownName(string name)
        {
            var text = TextScan.Whitespace().Replace(name.Trim(), " ");
            if (text.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
                text = text[..^2];

            var space = text.IndexOf(' ');
            if (space > 0 && TextScan.Honorifics.Contains(text[..space], StringComparer.OrdinalIgnoreCase))
                text = text[(space + 1)..].Trim();

            return text.Length > 0 && (FullNames.Contains(text) || FirstNames.Contains(text) || LastNames.Contains(text));
        }
    }
}
