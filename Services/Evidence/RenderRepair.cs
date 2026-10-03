using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Turns untrusted render output into a RenderedThread (T-05-20): maps messages by planned seq only, strips what code
/// adds itself (quoted history, signatures, privilege banners) and what must never appear (id tokens, HTML, links,
/// stray emphasis), and caps sizes. Repairs are recorded as notes, not violations (Pitfall 5).
/// </summary>
public static partial class RenderRepair
{
    public const int MaxBodyChars = 6_000;
    public const int MaxSignOffChars = 40;
    public const int MaxPeopleNamed = 30;
    public const int MaxNewDetails = 20;

    private const int MaxNameChars = 100;
    private const int MaxDetailChars = 400;
    private const string MobileFooter = "Sent from my mobile";

    internal static RenderedThread Apply(ThreadRenderer.ThreadRenderDto? dto, ThreadRenderContext ctx)
    {
        var thread = ctx.Thread;
        var result = new RenderedThread { ThreadId = thread?.Id ?? string.Empty };
        if (dto == null)
            return result;

        var planned = (thread?.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .GroupBy(m => m.Seq)
            .ToDictionary(g => g.Key, g => g.First());

        // First entry per planned seq wins; unknown seqs and duplicates are dropped
        var bySeq = new Dictionary<int, ThreadRenderer.RenderedMessageDto>();
        foreach (var message in dto.Messages ?? new List<ThreadRenderer.RenderedMessageDto>())
        {
            if (message == null)
            {
                result.RepairNotes.Add("dropped an empty message entry");
                continue;
            }

            if (!planned.ContainsKey(message.Seq))
                result.RepairNotes.Add($"dropped unknown msg {message.Seq}");
            else if (!bySeq.TryAdd(message.Seq, message))
                result.RepairNotes.Add($"dropped duplicate msg {message.Seq}");
        }

        foreach (var seq in planned.Keys.OrderBy(s => s))
        {
            if (!bySeq.TryGetValue(seq, out var message))
                continue;

            var sender = ctx.People?.Find(planned[seq].FromId);
            var signOff = CleanSignOff(message.SignOff, sender);
            var body = StripTrailingSignOff(CleanBody(message.Body, sender, result.RepairNotes, seq), signOff, result.RepairNotes, seq);
            result.Messages.Add(new RenderedMessage
            {
                Seq = seq,
                Body = body,
                SignOff = signOff,
                PeopleNamed = (message.PeopleNamed ?? new List<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => Cap(p.Trim(), MaxNameChars))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxPeopleNamed)
                    .ToList()
            });
        }

        result.NewDetails = (dto.NewDetails ?? new List<ThreadRenderer.LedgerDeltaDto>())
            .Where(d => d != null && (!string.IsNullOrWhiteSpace(d.About) || !string.IsNullOrWhiteSpace(d.Statement)))
            .Take(MaxNewDetails)
            .Select(d => new LedgerDelta
            {
                Seq = d.Seq,
                Kind = d.Kind,
                About = Cap((d.About ?? string.Empty).Trim(), MaxDetailChars),
                Statement = Cap((d.Statement ?? string.Empty).Trim(), MaxDetailChars),
                Value = Cap((d.Value ?? string.Empty).Trim(), MaxDetailChars),
                FactIds = (d.FactIds ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();

        return result;
    }

    /// <summary>
    /// Body repairs in order: line endings, quoted history, privilege banner, trailing name/signature lines, id tokens,
    /// HTML tags, links, single emphasis, whitespace, length cap. One note per repair kind ("msg 2: removed quoted text").
    /// </summary>
    internal static string CleanBody(string? body, PersonInfo? sender, List<string> notes, int seq)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var text = body.Replace("\r\n", "\n").Replace('\r', '\n');

        // Quoted history: code composes quotes itself
        var header = QuoteHeaderPattern().Match(text);
        var lines = (header.Success ? text[..header.Index] : text).Split('\n').ToList();
        var quoted = header.Success | lines.RemoveAll(l => l.TrimStart().StartsWith('>')) > 0;
        if (quoted)
            Note(notes, seq, "removed quoted text");

        // Leading privilege banner: code adds it from the assignment
        var first = lines.FindIndex(l => l.Trim().Length > 0);
        if (first >= 0 && string.Equals(lines[first].Trim().Trim('*').Trim(), HtmlEmailFormatter.PrivilegedBannerText, StringComparison.OrdinalIgnoreCase))
        {
            lines.RemoveAt(first);
            Note(notes, seq, "removed privilege banner");
        }

        // Trailing name and signature lines: code appends the sender's signature
        var signatureLines = SignatureLines(sender);
        var removedSignature = false;
        while (lines.Count > 0)
        {
            var last = lines[^1].Trim();
            if (last.Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
                continue;
            }

            if (!signatureLines.Contains(last.TrimStart('-', ' ')))
                break;
            lines.RemoveAt(lines.Count - 1);
            removedSignature = true;
        }

        if (removedSignature)
            Note(notes, seq, "removed signature");

        text = string.Join("\n", lines);
        text = Replace(IdTokenPattern(), text, string.Empty, notes, seq, "removed id tokens");
        text = Replace(HtmlTagPattern(), text, string.Empty, notes, seq, "removed markup");
        text = Replace(UrlPattern(), text, string.Empty, notes, seq, "removed links");
        text = Replace(UnderscoreEmphasisPattern(), text, "$1", notes, seq, "removed emphasis");
        text = Replace(StarEmphasisPattern(), text, "$1", notes, seq, "removed emphasis");

        text = SpaceRunPattern().Replace(text, " ");
        text = SpaceBeforePunctuationPattern().Replace(text, "$1");
        text = TrailingSpacePattern().Replace(text, string.Empty);
        text = BlankLinesPattern().Replace(text, "\n\n");
        text = text.Trim();

        if (text.Length > MaxBodyChars)
        {
            text = TruncateAtWord(text, MaxBodyChars);
            Note(notes, seq, "shortened body");
        }

        return text;
    }

    // First line only, capped; a sign-off that is just the sender's name is the signature's job (D-16)
    private static string CleanSignOff(string? signOff, PersonInfo? sender)
    {
        if (string.IsNullOrWhiteSpace(signOff))
            return string.Empty;

        var line = signOff.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        line = Cap(line, MaxSignOffChars).Trim();
        if (sender != null && (Same(line, sender.FullName) || Same(line, sender.FirstName)))
            return string.Empty;
        return line;
    }

    // The composer writes the sign-off after the body; a body that already ends with it would show it twice
    private static string StripTrailingSignOff(string body, string signOff, List<string> notes, int seq)
    {
        if (signOff.Length == 0 || body.Length == 0)
            return body;

        var cut = body.LastIndexOf('\n');
        var last = cut >= 0 ? body[(cut + 1)..] : body;
        if (!Same(last.Trim(), signOff))
            return body;

        Note(notes, seq, "removed repeated sign-off");
        return cut >= 0 ? body[..cut].TrimEnd() : string.Empty;
    }

    private static HashSet<string> SignatureLines(PersonInfo? sender)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MobileFooter };
        if (sender == null)
            return set;

        foreach (var name in new[] { sender.FullName, sender.FirstName })
        {
            if (!string.IsNullOrWhiteSpace(name))
                set.Add(name.Trim());
        }

        foreach (var line in (sender.Cast?.Signature ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                set.Add(line.Trim());
        }

        return set;
    }

    private static string Replace(Regex pattern, string text, string replacement, List<string> notes, int seq, string note)
    {
        if (!pattern.IsMatch(text))
            return text;
        Note(notes, seq, note);
        return pattern.Replace(text, replacement);
    }

    private static void Note(List<string> notes, int seq, string text)
    {
        var note = $"msg {seq}: {text}";
        if (!notes.Contains(note))
            notes.Add(note);
    }

    private static string TruncateAtWord(string text, int max)
    {
        var cut = text[..max];
        var space = cut.LastIndexOfAny(new[] { ' ', '\n' });
        if (space > max / 2)
            cut = cut[..space];
        return cut.TrimEnd();
    }

    private static string Cap(string text, int max) => text.Length > max ? text[..max] : text;

    private static bool Same(string a, string b) =>
        !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // "On Tue, Mar 19, 2024 at 9:42 AM, Lisa Chen <lisa@harmon.com> wrote:", "-----Original Message-----", "---------- Forwarded message"
    [GeneratedRegex(@"^(?:On .{5,200} wrote:[ \t]*$|-{2,}\s*Original Message\s*-{2,}|-{5,}\s*Forwarded message)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex QuoteHeaderPattern();

    [GeneratedRegex(@"\b(?:CH|F|ISS|T|V|ORG|EXT)-\d{2,4}\b")]
    private static partial Regex IdTokenPattern();

    [GeneratedRegex(@"<[^>\n]{1,200}>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // _x_ only at word boundaries: never inside identifiers or file names such as Pay_App_7.xlsx
    [GeneratedRegex(@"(?<![\w])_([^_\s][^_\n]{0,100}?)_(?![\w])")]
    private static partial Regex UnderscoreEmphasisPattern();

    // *x* but not **x** (bold is allowed, D-03)
    [GeneratedRegex(@"(?<![\w*])\*([^*\s][^*\n]{0,100}?)\*(?![\w*])")]
    private static partial Regex StarEmphasisPattern();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex SpaceRunPattern();

    // "See F-003, and" -> "See , and" -> "See, and" after id removal
    [GeneratedRegex(@"[ \t]+([,.;:])(?=\s|$)")]
    private static partial Regex SpaceBeforePunctuationPattern();

    [GeneratedRegex(@"[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex TrailingSpacePattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesPattern();
}
