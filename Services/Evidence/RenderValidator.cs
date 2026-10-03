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
