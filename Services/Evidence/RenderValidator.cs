using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Deterministic consistency checks of one rendered thread (D-12, no LLM judge). The result decides whether a thread is
/// accepted, retried with the violation texts as feedback, or kept with warnings (D-13, D-14). Static and never throws:
/// each rule runs in isolation, so a rule that fails is skipped rather than reported.
/// </summary>
public static partial class RenderValidator
{
    // RESEARCH A7: tune on the first live run
    public const decimal VariantWindow = 0.15m;
    public const double OverLengthFactor = 2.0;
    public const double UnderLengthFactor = 0.5;

    public static readonly string[] LegalTerms =
    {
        "plaintiff", "plaintiffs", "defendant", "defendants", "allegedly", "alleged", "lawsuit", "cause of action", "this is fictional"
    };

    public static readonly string[] GroupGreetingWords =
    {
        "team", "all", "everyone", "everybody", "folks", "guys", "both", "there", "y'all", "gang", "group"
    };

    // Capitalized words that follow a first name without being a surname ("Tom Monday", "Lisa Thanks")
    private static readonly HashSet<string> NotSurnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        "The", "This", "That", "These", "Those", "Please", "Thanks", "Thank", "And", "But", "Also", "Can", "Could", "Will", "Would",
        "Let", "Just", "Here", "There", "Team", "Everyone", "All"
    };

    private static readonly string[] Honorifics = { "mr.", "mr", "ms.", "ms", "mrs.", "mrs", "dr.", "dr" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Overlapping "First Last" pairs on one line (a newline never joins two names)
    [GeneratedRegex(@"\b([A-Z][a-z]+)(?=[ \t]+([A-Z][a-z]+)\b)", RegexOptions.None, 1000)]
    private static partial Regex NameBigram();

    [GeneratedRegex(@"\b(?:Mr|Ms|Mrs|Dr)\.?[ \t]+([A-Z][a-z]+)\b", RegexOptions.None, 1000)]
    private static partial Regex HonorificName();

    [GeneratedRegex(@"^(?:Hi|Hello|Hey|Dear|Morning|Good morning)[ \t]+([A-Z][a-z]+)\b", RegexOptions.None, 1000)]
    private static partial Regex GreetingName();

    // "Count II", "Count IV" (case-sensitive so "count" and "counter" never match)
    [GeneratedRegex(@"\bCount [IVX]+\b", RegexOptions.None, 1000)]
    private static partial Regex CountNumber();

    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    private static partial Regex Whitespace();

    // Leading letters of a canonical identifier: "CO-007" -> "CO-", "INV-2024-118" -> "INV-"
    [GeneratedRegex(@"^([A-Za-z]+-)", RegexOptions.None, 1000)]
    private static partial Regex IdentifierPrefix();

    // Sentence ends: terminal punctuation before a capitalized word ("Apr. 9" stays whole), or a line break
    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z""'(])|\n+", RegexOptions.None, 1000)]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.None, 1000)]
    private static partial Regex IsoDate();

    // en-US "April 9", "Apr 9, 2024", "Sept. 3rd 2024" (capitalized month names only, so "may" the verb never matches)
    [GeneratedRegex(@"\b(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sept?(?:ember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?[ \t]+(\d{1,2})(?:st|nd|rd|th)?\b(?:,?[ \t]+(\d{4})\b)?", RegexOptions.None, 1000)]
    private static partial Regex MonthDate();

    // Function words that end the head phrase of a ledger About ("site walk | with the glazing crew")
    private static readonly HashSet<string> AboutStopWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "with", "for", "on", "at", "about", "of", "to", "from", "by", "in", "and", "regarding", "between",
        "re", "our", "their", "his", "her", "its", "this", "that", "into", "over", "after", "before"
    };

    /// <summary>
    /// Runs V1-V8 against the rendered thread. Violations are distinct (code, seq, text) in rule order; empty when either
    /// input is missing.
    /// </summary>
    public static List<RenderViolation> Check(RenderedThread rendered, ThreadRenderContext ctx)
    {
        var violations = new List<RenderViolation>();
        if (rendered == null || ctx == null)
            return violations;

        CheckContext c;
        try
        {
            c = new CheckContext(rendered, ctx);
        }
        catch (Exception)
        {
            // Without a usable snapshot no rule can run; an unchecked thread is not a violation (Pitfall 5)
            return violations;
        }

        Run(violations, () => CheckStructure(c));
        Run(violations, () => CheckPeople(c));
        Run(violations, () => CheckCanonicalVariants(c));
        Run(violations, () => CheckKnowledge(c));
        Run(violations, () => CheckLedger(c));
        Run(violations, () => CheckLedgerDates(c));
        Run(violations, () => CheckLegalWording(c));
        Run(violations, () => CheckLength(c));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return violations.Where(v => seen.Add(v.Code + "|" + v.Seq.ToString(Inv) + "|" + v.Text)).ToList();
    }

    internal static void Run(List<RenderViolation> violations, Func<IEnumerable<RenderViolation>> rule)
    {
        try
        {
            // Materialize inside the try so a lazy rule that fails part-way adds nothing
            var found = rule()?.Where(v => v != null).ToList() ?? new List<RenderViolation>();
            violations.AddRange(found);
        }
        catch (Exception)
        {
            // One broken rule must not hide the others or throw into the run; a skipped rule is not a violation (Pitfall 5)
        }
    }

    // ---------- V1 + V5: message count and empty bodies ----------

    private static IEnumerable<RenderViolation> CheckStructure(CheckContext c)
    {
        var result = new List<RenderViolation>();
        var renderedCount = c.Planned.Count(p => c.RenderedBySeq.ContainsKey(p.Seq));
        if (renderedCount != c.Planned.Count)
            result.Add(V("V1", 0, $"has {renderedCount.ToString(Inv)} messages; the plan has {c.Planned.Count.ToString(Inv)}"));

        foreach (var planned in c.Planned)
        {
            if (!c.RenderedBySeq.TryGetValue(planned.Seq, out var message) || !string.IsNullOrWhiteSpace(message.Body))
                continue;

            var seq = planned.Seq.ToString(Inv);
            var quoteNote = $"msg {seq}: removed quoted text";
            if (c.RepairNotes.Contains(quoteNote))
                result.Add(V("V5", planned.Seq, $"message {seq} is empty after removing quoted text"));
            else
                result.Add(V("V1", planned.Seq, $"message {seq} is empty"));
        }

        return result;
    }

    // ---------- V2: unknown people ----------

    private static IEnumerable<RenderViolation> CheckPeople(CheckContext c)
    {
        var result = new List<RenderViolation>();
        foreach (var (planned, message) in c.Pairs())
        {
            var seq = planned.Seq;

            // (a) Names the model reports
            foreach (var name in message.PeopleNamed ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(name) && !c.IsKnownName(name))
                    result.Add(NotInCast(seq, name.Trim()));
            }

            var body = message.Body ?? string.Empty;

            // (b) "Maria Ortiz": a known first name followed by a surname nobody in the case has
            foreach (Match m in NameBigram().Matches(body))
            {
                var first = m.Groups[1].Value;
                var second = m.Groups[2].Value;
                if (!c.FirstNames.Contains(first) || NotSurnames.Contains(second))
                    continue;
                if (c.FullNames.Contains(first + " " + second) || c.FirstNames.Contains(second) || c.LastNames.Contains(second))
                    continue;
                result.Add(NotInCast(seq, first + " " + second));
            }

            // (c) "Mr. Kowalski" with no such last name
            foreach (Match m in HonorificName().Matches(body))
            {
                if (!c.LastNames.Contains(m.Groups[1].Value) && !c.FirstNames.Contains(m.Groups[1].Value))
                    result.Add(NotInCast(seq, Whitespace().Replace(m.Value, " ")));
            }

            // (d) Greeting a known person who is not on this email; group words and non-person names are ignored
            var firstLine = body.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            var greeting = GreetingName().Match(firstLine);
            if (greeting.Success)
            {
                var name = greeting.Groups[1].Value;
                if (!GroupGreetingWords.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && c.FirstNames.Contains(name)
                    && !c.RecipientFirstNames(planned).Contains(name))
                {
                    result.Add(V("V2", seq, $"greets \"{name}\", who is not on this email"));
                }
            }
        }

        return result;
    }

    private static RenderViolation NotInCast(int seq, string name) => V("V2", seq, $"names \"{name}\", who is not in the cast");

    // ---------- V3: canonical value variants ----------

    private static IEnumerable<RenderViolation> CheckCanonicalVariants(CheckContext c)
    {
        var result = new List<RenderViolation>();
        var amounts = c.CanonicalAmounts();
        var prefixes = c.Values
            .Where(v => v.Kind != ValueKind.Amount)
            .Select(v => IdentifierPrefix().Match(v.Value.Trim()))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (planned, message) in c.Pairs())
        {
            foreach (var scanned in CanonicalValueScanner.Scan(message.Body))
            {
                if (scanned.Kind == ScannedKind.Amount)
                {
                    if (!CanonicalValueScanner.TryParseAmount(scanned.Text, out var amount)
                        || amounts.Any(a => CanonicalValueScanner.AmountsEqual(a.Value.Value, scanned.Text)))
                        continue;

                    // The closest canonical amount inside the window, reported once per scanned text
                    var closest = amounts
                        .Where(a => Math.Abs(amount - a.Amount) <= a.Amount * VariantWindow)
                        .OrderBy(a => Math.Abs(amount - a.Amount))
                        .Select(a => a.Value)
                        .FirstOrDefault();
                    if (closest != null)
                    {
                        var label = string.IsNullOrWhiteSpace(closest.Label) ? "this amount" : closest.Label.Trim();
                        result.Add(V("V3", planned.Seq, $"uses {scanned.Text} for {label}; the case value is {closest.Value.Trim()}"));
                    }
                }
                else if (prefixes.Any(p => scanned.Text.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && !c.IsRegisteredIdentifier(scanned.Text))
                {
                    result.Add(V("V3", planned.Seq, $"uses {scanned.Text}, which is not a case document number"));
                }
            }
        }

        return result;
    }

    // ---------- V4: future or unknown knowledge ----------

    private static IEnumerable<RenderViolation> CheckKnowledge(CheckContext c)
    {
        var result = new List<RenderViolation>();

        // Narrow on purpose (Pitfall 5): only amounts and digit-bearing values (numbers, dates, identifiers) are knowledge
        // markers; names such as the project name are shared context everyone on the job uses
        var values = c.Values.Where(v => IsSpecific(v.Value)).ToList();
        var ledger = c.Ledger.Where(e => IsSpecific(e.Value) && (e.KnownByIds?.Count ?? 0) > 0).ToList();

        foreach (var (planned, message) in c.Pairs())
        {
            var body = message.Body ?? string.Empty;
            var sender = planned.FromId ?? string.Empty;
            var date = MessageTime.LocalDate(planned);
            var seen = c.SeenText(planned.Seq);
            var ownFacts = new HashSet<string>(planned.FactIds ?? new List<string>(), StringComparer.Ordinal);

            foreach (var value in values)
            {
                if (!Mentions(body, value.Value))
                    continue;

                // Values linked to this message's own plan facts were given to it in the prompt
                if ((value.FactIds ?? new List<string>()).Any(ownFacts.Contains))
                    continue;

                var knowable = KnowledgeGate.Knowability(value, c.Guide);
                if (KnowledgeGate.KnowsValue(knowable, sender, date) || Mentions(seen, value.Value))
                    continue;

                if (knowable.KnowableFrom != null && knowable.KnowableFrom.Value > date)
                {
                    var label = string.IsNullOrWhiteSpace(value.Label) ? value.Value.Trim() : value.Label.Trim();
                    result.Add(V("V4", planned.Seq, $"mentions {label}, dated after this email"));
                }
                else
                {
                    result.Add(DoesNotKnow(c, planned, value.Value));
                }
            }

            foreach (var entry in ledger)
            {
                if (Mentions(body, entry.Value) && !entry.KnownByIds.Contains(sender, StringComparer.Ordinal) && !Mentions(seen, entry.Value))
                    result.Add(DoesNotKnow(c, planned, entry.Value));
            }
        }

        return result;
    }

    private static RenderViolation DoesNotKnow(CheckContext c, PlannedMessage planned, string value) =>
        V("V4", planned.Seq, $"uses {value.Trim()}, which {c.FirstNameOf(planned.FromId)} does not know yet");

    private static bool IsSpecific(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (CanonicalValueScanner.TryParseAmount(text, out _))
            return true;
        return text.Length >= 4 && text.Any(char.IsDigit);
    }

    /// <summary>
    /// Amounts compare by value against the amounts scanned from the text; anything else is a whole-token,
    /// case-insensitive match (so "CO-007" is not found inside "CO-0071").
    /// </summary>
    private static bool Mentions(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (CanonicalValueScanner.TryParseAmount(trimmed, out _))
        {
            return CanonicalValueScanner.Scan(text)
                .Any(s => s.Kind == ScannedKind.Amount && CanonicalValueScanner.AmountsEqual(s.Text, trimmed));
        }

        return ContainsWord(text, trimmed);
    }

    // ---------- V6: ledger contradictions (details and amounts) ----------

    private static IEnumerable<RenderViolation> CheckLedger(CheckContext c)
    {
        var result = new List<RenderViolation>();
        var visible = c.Ledger.Where(e => !string.IsNullOrWhiteSpace(e.Value) && LedgerMerger.NormalizeAbout(e.About).Length > 0).ToList();

        // A new detail giving a different value for the same kind and subject as an earlier entry
        foreach (var delta in c.Rendered.NewDetails ?? new List<LedgerDelta>())
        {
            if (delta == null || string.IsNullOrWhiteSpace(delta.Value))
                continue;

            var about = LedgerMerger.NormalizeAbout(delta.About);
            if (about.Length == 0)
                continue;

            foreach (var entry in visible)
            {
                if (entry.Kind == delta.Kind
                    && string.Equals(LedgerMerger.NormalizeAbout(entry.About), about, StringComparison.Ordinal)
                    && !LedgerMerger.SameValue(entry.Value, delta.Value))
                {
                    result.Add(Contradiction(delta.Seq, entry, delta.Value));
                }
            }
        }

        // Body amounts close to (but not equal to) a known ledger figure, unless they are another case or ledger amount
        var figures = visible
            .Where(e => e.Kind == LedgerKind.Figure)
            .Select(e => (Entry: e, Ok: CanonicalValueScanner.TryParseAmount(e.Value.Trim(), out var amount), Amount: amount))
            .Where(f => f.Ok && f.Amount > 0)
            .ToList();
        if (figures.Count == 0)
            return result;

        var canonical = c.CanonicalAmounts();
        foreach (var (planned, message) in c.Pairs())
        {
            foreach (var scanned in CanonicalValueScanner.Scan(message.Body).Where(s => s.Kind == ScannedKind.Amount))
            {
                if (!CanonicalValueScanner.TryParseAmount(scanned.Text, out var amount)
                    || canonical.Any(a => CanonicalValueScanner.AmountsEqual(a.Value.Value, scanned.Text))
                    || c.Ledger.Any(e => CanonicalValueScanner.AmountsEqual(e.Value ?? string.Empty, scanned.Text)))
                    continue;

                var closest = figures
                    .Where(f => Math.Abs(amount - f.Amount) <= f.Amount * VariantWindow)
                    .OrderBy(f => Math.Abs(amount - f.Amount))
                    .Select(f => f.Entry)
                    .FirstOrDefault();
                if (closest != null)
                    result.Add(Contradiction(planned.Seq, closest, scanned.Text));
            }
        }

        return result;
    }

    private static RenderViolation Contradiction(int seq, LedgerEntry entry, string newValue)
    {
        var about = string.IsNullOrWhiteSpace(entry.About) ? entry.Statement.Trim() : entry.About.Trim();
        return V("V6", seq, $"gives {about} as {newValue.Trim()}; earlier emails say {entry.Value.Trim()}");
    }

    // ---------- V6: body dates against earlier meetings and commitments ----------

    private static IEnumerable<RenderViolation> CheckLedgerDates(CheckContext c)
    {
        // Narrow on purpose (Pitfall 5): only dates stated next to the same subject as an earlier meeting/commitment; other date contradictions are left to the Phase 7 QA scans
        var result = new List<RenderViolation>();
        var dated = c.Ledger
            .Where(e => e.Kind is LedgerKind.Meeting or LedgerKind.Commitment)
            .Select(e => (Entry: e, Ok: DateOnly.TryParseExact(e.Value?.Trim(), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d), Date: d))
            .Where(e => e.Ok)
            .Select(e => (e.Entry, e.Date, Subject: SubjectMatcher.From(e.Entry.About)))
            .Where(e => e.Subject != null)
            .ToList();
        if (dated.Count == 0)
            return result;

        foreach (var (planned, message) in c.Pairs())
        {
            var year = MessageTime.LocalDate(planned).Year;
            var sentences = SentenceBreak().Split(message.Body ?? string.Empty);
            foreach (var (entry, date, subject) in dated)
            {
                foreach (var sentence in sentences)
                {
                    if (!subject!.Matches(LedgerMerger.NormalizeAbout(sentence)))
                        continue;

                    var dates = DatesIn(sentence, year);
                    if (dates.Count == 0 || dates.Contains(date))
                        continue;

                    result.Add(Contradiction(planned.Seq, entry, dates[0].ToString("yyyy-MM-dd", Inv)));
                    break;
                }
            }
        }

        return result;
    }

    // Valid dates in order of appearance; a month-day date without a year takes the message's year
    private static List<DateOnly> DatesIn(string sentence, int defaultYear)
    {
        var found = new List<(int Index, DateOnly Date)>();
        foreach (Match m in IsoDate().Matches(sentence))
        {
            if (TryDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var d))
                found.Add((m.Index, d));
        }

        foreach (Match m in MonthDate().Matches(sentence))
        {
            var month = Array.IndexOf(MonthPrefixes, m.Groups[1].Value[..3].ToLowerInvariant()) + 1;
            var year = m.Groups[3].Success ? m.Groups[3].Value : defaultYear.ToString(Inv);
            if (month > 0 && TryDate(year, month.ToString(Inv), m.Groups[2].Value, out var d))
                found.Add((m.Index, d));
        }

        return found.OrderBy(f => f.Index).Select(f => f.Date).ToList();
    }

    private static readonly string[] MonthPrefixes = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    private static bool TryDate(string year, string month, string day, out DateOnly date)
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

    /// <summary>
    /// Decides whether a normalized sentence talks about a ledger subject: its head phrase ("site walk" of "site walk with
    /// the glazing crew") when that has two or more words, else every significant word (four letters or more).
    /// </summary>
    private sealed class SubjectMatcher
    {
        private string _head = string.Empty;
        private List<string> _significant = new();

        public static SubjectMatcher? From(string? about)
        {
            var words = LedgerMerger.NormalizeAbout(about ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var head = words.SkipWhile(AboutStopWords.Contains).TakeWhile(w => !AboutStopWords.Contains(w)).ToList();
            var significant = words.Where(w => w.Length >= 4 && !AboutStopWords.Contains(w)).Distinct(StringComparer.Ordinal).ToList();
            if (head.Count < 2 && significant.Count == 0)
                return null;
            return new SubjectMatcher { _head = head.Count >= 2 ? string.Join(' ', head) : string.Empty, _significant = significant };
        }

        public bool Matches(string normalizedSentence)
        {
            var padded = " " + normalizedSentence + " ";
            if (_head.Length > 0)
                return padded.Contains(" " + _head + " ", StringComparison.Ordinal);
            return _significant.All(w => padded.Contains(" " + w + " ", StringComparison.Ordinal));
        }
    }

    // ---------- V7: legal framing outside privileged threads ----------

    private static IEnumerable<RenderViolation> CheckLegalWording(CheckContext c)
    {
        var result = new List<RenderViolation>();
        if (c.Ctx.Thread?.IsPrivileged == true)
            return result;

        var title = c.Ctx.Draft?.CoreDocument?.Title?.Trim() ?? string.Empty;
        foreach (var (planned, message) in c.Pairs())
        {
            var body = message.Body ?? string.Empty;
            foreach (var term in LegalTerms)
            {
                if (ContainsWord(body, term))
                    result.Add(LegalWording(planned.Seq, term));
            }

            foreach (Match m in CountNumber().Matches(body))
                result.Add(LegalWording(planned.Seq, m.Value));

            if (title.Length >= 8 && body.Contains(title, StringComparison.OrdinalIgnoreCase))
                result.Add(LegalWording(planned.Seq, title));
        }

        return result;
    }

    private static RenderViolation LegalWording(int seq, string term) => V("V7", seq, $"uses legal wording (\"{term}\")");

    // ---------- V8: gross length misses ----------

    private static IEnumerable<RenderViolation> CheckLength(CheckContext c)
    {
        var result = new List<RenderViolation>();
        foreach (var (planned, message) in c.Pairs())
        {
            if (string.IsNullOrWhiteSpace(message.Body))
                continue;

            var band = c.Ctx.Assignment?.For(planned.Seq)?.Length ?? LengthBand.Medium;
            var spec = RenderAssignments.Spec(band);
            var words = message.Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            var tooLong = words > spec.MaxWords * OverLengthFactor;
            var tooShort = band == LengthBand.Long && words < spec.MinWords * UnderLengthFactor;
            if (tooLong || tooShort)
            {
                result.Add(V("V8", planned.Seq,
                    $"message {planned.Seq.ToString(Inv)} is {words.ToString(Inv)} words; target is {spec.MinWords.ToString(Inv)}-{spec.MaxWords.ToString(Inv)}"));
            }
        }

        return result;
    }

    // ---------- shared helpers ----------

    private static RenderViolation V(string code, int seq, string text) => new() { Code = code, Seq = seq, Text = text };

    /// <summary>
    /// Case-insensitive whole-word (or whole-phrase) match: the characters around the match are not letters or digits.
    /// </summary>
    internal static bool ContainsWord(string text, string term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return false;

        var start = 0;
        while (start <= text.Length - term.Length)
        {
            var index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + term.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
                return true;
            start = index + 1;
        }

        return false;
    }

    // Whole-token identifier match: letters, digits and hyphens around the match end the token
    private static bool ContainsIdentifier(string text, string identifier)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(identifier))
            return false;

        var start = 0;
        while (start <= text.Length - identifier.Length)
        {
            var index = text.IndexOf(identifier, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var end = index + identifier.Length;
            var before = index == 0 || !(char.IsLetterOrDigit(text[index - 1]) || text[index - 1] == '-');
            var after = end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '-');
            if (before && after)
                return true;
            start = index + 1;
        }

        return false;
    }

    /// <summary>
    /// Read-only snapshot of everything the rules need, built once per Check with null-safe defaults.
    /// </summary>
    private sealed class CheckContext
    {
        public CheckContext(RenderedThread rendered, ThreadRenderContext ctx)
        {
            Rendered = rendered;
            Ctx = ctx;
            Guide = ctx.Guide ?? new CaseGuide();

            Planned = (ctx.Thread?.Messages ?? new List<PlannedMessage>())
                .Where(m => m != null)
                .GroupBy(m => m.Seq)
                .Select(g => g.First())
                .OrderBy(m => m.Seq)
                .ToList();

            RenderedBySeq = new Dictionary<int, RenderedMessage>();
            foreach (var message in rendered.Messages ?? new List<RenderedMessage>())
            {
                if (message != null)
                    RenderedBySeq.TryAdd(message.Seq, message);
            }

            RepairNotes = new HashSet<string>(rendered.RepairNotes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            People = ctx.People ?? PeopleDirectory.Create(Guide, ctx.Plan ?? new CorpusPlan());
            foreach (var person in People.All)
            {
                if (!string.IsNullOrWhiteSpace(person.FirstName))
                    FirstNames.Add(person.FirstName.Trim());
                if (!string.IsNullOrWhiteSpace(person.LastName))
                    LastNames.Add(person.LastName.Trim());
                if (!string.IsNullOrWhiteSpace(person.FullName))
                    FullNames.Add(Whitespace().Replace(person.FullName.Trim(), " "));
            }

            Values = (Guide.Values ?? new List<CanonicalValue>()).Where(v => v != null && !string.IsNullOrWhiteSpace(v.Value)).ToList();
            Ledger = (ctx.LedgerView ?? new List<LedgerEntry>()).Where(e => e != null).ToList();
        }

        public RenderedThread Rendered { get; }
        public ThreadRenderContext Ctx { get; }
        public CaseGuide Guide { get; }
        public List<PlannedMessage> Planned { get; }
        public Dictionary<int, RenderedMessage> RenderedBySeq { get; }
        public HashSet<string> RepairNotes { get; }
        public PeopleDirectory People { get; }
        public HashSet<string> FirstNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LastNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FullNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<CanonicalValue> Values { get; }
        public List<LedgerEntry> Ledger { get; }

        // Planned messages that have a rendered counterpart, in seq order
        public IEnumerable<(PlannedMessage Planned, RenderedMessage Message)> Pairs()
        {
            foreach (var planned in Planned)
            {
                if (RenderedBySeq.TryGetValue(planned.Seq, out var message))
                    yield return (planned, message);
            }
        }

        // Amount-kind canonical values with a positive parsed amount
        public List<(CanonicalValue Value, decimal Amount)> CanonicalAmounts() =>
            Values
                .Where(v => v.Kind == ValueKind.Amount)
                .Select(v => (Value: v, Ok: CanonicalValueScanner.TryParseAmount(v.Value.Trim(), out var amount), Amount: amount))
                .Where(a => a.Ok && a.Amount > 0)
                .Select(a => (a.Value, a.Amount))
                .ToList();

        /// <summary>
        /// Stricter than CanonicalValueScanner.IsRegistered (whose containment test accepts "CO-0071" because it contains
        /// "CO-007"): the identifier equals a non-amount canonical or ledger value, or appears in one as a whole token.
        /// </summary>
        public bool IsRegisteredIdentifier(string identifier)
        {
            var texts = Values.Where(v => v.Kind != ValueKind.Amount).Select(v => v.Value)
                .Concat(Ledger.Select(e => e.Value ?? string.Empty));
            return texts.Any(t => string.Equals(t.Trim(), identifier, StringComparison.OrdinalIgnoreCase) || ContainsIdentifier(t, identifier));
        }

        // Rendered bodies of the messages this one replies to or forwards, plus the thread subject
        public string SeenText(int seq)
        {
            var parts = new List<string> { Ctx.Thread?.Subject ?? string.Empty };
            foreach (var ancestor in KnowledgeGate.Ancestors(Ctx.Thread!, seq))
            {
                if (RenderedBySeq.TryGetValue(ancestor, out var message))
                    parts.Add(message.Body ?? string.Empty);
            }

            return string.Join("\n", parts);
        }

        public HashSet<string> RecipientFirstNames(PlannedMessage planned)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in (planned.ToIds ?? new List<string>()).Concat(planned.CcIds ?? new List<string>()))
            {
                var first = People.Find(id)?.FirstName;
                if (!string.IsNullOrWhiteSpace(first))
                    names.Add(first.Trim());
            }

            return names;
        }

        public string FirstNameOf(string? id)
        {
            var person = People.Find(id);
            if (person == null)
                return id ?? string.Empty;
            if (person.FirstName.Length > 0)
                return person.FirstName;
            return person.FullName.Length > 0 ? person.FullName : person.Id;
        }

        /// <summary>
        /// A person id, a known first, last or full name, optionally after an honorific or with a possessive "'s".
        /// </summary>
        public bool IsKnownName(string name)
        {
            var text = Whitespace().Replace(name.Trim(), " ");
            if (People.Find(text) != null)
                return true;

            if (text.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
                text = text[..^2];

            var space = text.IndexOf(' ');
            if (space > 0 && Honorifics.Contains(text[..space], StringComparer.OrdinalIgnoreCase))
                text = text[(space + 1)..].Trim();

            return text.Length > 0
                && (FullNames.Contains(text) || FirstNames.Contains(text) || LastNames.Contains(text)
                    || People.KnownNameTokens.Contains(text, StringComparer.OrdinalIgnoreCase));
        }
    }
}
