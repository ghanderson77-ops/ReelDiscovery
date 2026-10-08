using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// One transcript page: an optional heading (the .docx builder appends the page number) and exactly 25 numbered lines.
/// </summary>
public class TranscriptPage
{
    public int Number { get; set; }
    public string Heading { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = new();
    public bool IsTestimony { get; set; }
}

/// <summary>
/// A 1-based page:line citation.
/// </summary>
public class TranscriptLocation
{
    public int Page { get; set; }
    public int Line { get; set; }
}

/// <summary>
/// A laid-out transcript plus the page:line map of exhibit markings and examination starts.
/// </summary>
public class TranscriptDocument
{
    public List<TranscriptPage> Pages { get; set; } = new();
    public int FirstTestimonyPage { get; set; }
    public int LastTestimonyPage { get; set; }
    public int TestimonyPageCount { get; set; }
    public Dictionary<int, TranscriptLocation> ExhibitMarked { get; set; } = new();
    public List<(string Label, TranscriptLocation At)> Examinations { get; set; } = new();
}

/// <summary>
/// Deterministic "everything is a line" transcript layout (D-23): turns become lines of at most 62 characters, lines
/// become 25-line pages, and every exhibit marking and examination start gets a page:line citation without Word.
/// Indentation follows the federal transcript format (Guide to Judiciary Policy Vol. 6 Ch. 5, section 520.36).
/// </summary>
public static partial class TranscriptLayout
{
    public const int LinesPerPage = 25;

    // Courier New 12 pt at 10 cpi gives 63 characters in the 6.375-inch text width; one character of slack (research A4)
    public const int MaxCharsPerLine = 62;

    // Q/A text starts at column 5 (after "Q.   ")
    public const int QaTextColumn = 5;

    // Colloquy speaker labels start at column 10
    public const int ColloquyLabelIndent = 9;

    // Parentheticals open at column 5
    public const int ParentheticalIndent = 4;

    private const string UnparseableEndTime = "the close of the session";

    // FRCP 30(d)(1): one day of 7 hours
    private const int MaxSessionMinutes = 7 * 60;

    // ~45 seconds of testimony per transcript line
    private const int SecondsPerLine = 45;

    // Index page numbers are right-aligned in this many columns, so placeholder and real numbers lay out identically
    private const int PageNumberWidth = 6;

    private const int ExhibitIndexPrefix = 6;

    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"( *)([^ ]+)", RegexOptions.None, 1000)]
    private static partial Regex Token();

    /// <summary>
    /// Greedy word wrap to <paramref name="width"/> columns. Leading spaces on the first line are kept (they carry the
    /// federal indentation); continuation lines start with <paramref name="continuationIndent"/> spaces. Tokens wider
    /// than the remaining width are hard-split. Control characters and lone surrogates are dropped first.
    /// </summary>
    public static List<string> Wrap(string text, int width, int continuationIndent)
    {
        var lines = new List<string>();
        if (width <= 0)
            width = MaxCharsPerLine;
        if (continuationIndent < 0 || continuationIndent >= width)
            continuationIndent = 0;
        var indent = new string(' ', continuationIndent);

        var clean = CaseGuideDocxExporter.StripInvalidXmlChars(text);
        var sb = new StringBuilder(clean.Length);
        foreach (var c in clean)
            sb.Append(char.IsWhiteSpace(c) ? ' ' : c);

        var current = string.Empty;
        var lineHasWord = false;
        foreach (Match match in Token().Matches(sb.ToString()))
        {
            var sep = match.Groups[1].Value;
            var word = match.Groups[2].Value;

            // The first line keeps its leading spaces; a fresh continuation line drops the separator
            var joiner = lineHasWord || lines.Count == 0 ? sep : string.Empty;
            if (current.Length + joiner.Length + word.Length <= width)
            {
                current += joiner + word;
                lineHasWord = true;
                continue;
            }

            if (lineHasWord && continuationIndent + word.Length <= width)
            {
                lines.Add(current);
                current = indent + word;
                continue;
            }

            // Hard split: the token is wider than a whole line
            if (current.Length + joiner.Length >= width)
            {
                if (current.Trim().Length > 0)
                    lines.Add(current);
                current = indent;
                joiner = string.Empty;
            }

            current += joiner;
            var rest = word;
            while (rest.Length > 0)
            {
                var room = width - current.Length;
                if (room <= 0)
                {
                    lines.Add(current);
                    current = indent;
                    room = width - current.Length;
                }

                var take = Math.Min(room, rest.Length);
                current += rest[..take];
                rest = rest[take..];
                lineHasWord = true;
            }
        }

        if (lineHasWord)
            lines.Add(current);

        for (var i = 0; i < lines.Count; i++)
            lines[i] = lines[i].TrimEnd();
        return lines;
    }

    /// <summary>
    /// Colloquy label: "THE WITNESS" / "THE REPORTER" / "THE VIDEOGRAPHER" by role, otherwise "MS. QUIST" from the
    /// honorific and last name, otherwise the upper-cased full name.
    /// </summary>
    public static string SpeakerLabel(DepositionContent content, string speakerId)
    {
        var speaker = content?.Speakers?.FirstOrDefault(s => s != null && string.Equals(s.SpeakerId, speakerId, StringComparison.Ordinal));
        if (speaker == null)
            return "COUNSEL";

        switch (speaker.Role)
        {
            case SpeakerRole.Witness:
                return "THE WITNESS";
            case SpeakerRole.Reporter:
                return "THE REPORTER";
            case SpeakerRole.Videographer:
                return "THE VIDEOGRAPHER";
        }

        var name = Clean(speaker.FullName);
        var honorific = Clean(speaker.Honorific).TrimEnd('.');
        if (name.Length == 0)
            return "COUNSEL";
        if (honorific.Length == 0)
            return name.ToUpperInvariant();
        return $"{honorific.ToUpperInvariant()}. {LastName(name).ToUpperInvariant()}";
    }

    /// <summary>
    /// Lines for one turn: "Q.   text", "A.   text", 9 spaces + "LABEL:  text" for colloquy and objections (continuing
    /// at column 1), and 4 spaces + "(text)" for parentheticals (continuing at column 6).
    /// </summary>
    public static List<string> FormatTurn(DepositionTurn turn, DepositionContent content)
    {
        var text = Clean(turn?.Text);
        return turn?.Kind switch
        {
            TurnKind.Q => Wrap("Q.   " + text, MaxCharsPerLine, 0),
            TurnKind.A => Wrap("A.   " + text, MaxCharsPerLine, 0),
            TurnKind.Parenthetical => Parenthetical(text.Trim('(', ')', ' ')),
            _ => Colloquy(SpeakerLabel(content, turn?.SpeakerId ?? string.Empty), text)
        };
    }

    /// <summary>
    /// End time for "(Whereupon ... concluded at ...)": start + 45 s per testimony line, rounded up to 5 minutes and
    /// capped at start + 7 hours (FRCP 30(d)(1)). An unparseable start returns "the close of the session".
    /// </summary>
    public static string ComputeEndTime(string startTime, int testimonyLines)
    {
        if (!TryParseTime(startTime, out var start))
            return UnparseableEndTime;

        var end = start.AddSeconds((double)Math.Max(0, testimonyLines) * SecondsPerLine);
        var minutes = (int)Math.Ceiling(end.TimeOfDay.TotalMinutes / 5.0) * 5;
        end = end.Date.AddMinutes(minutes);
        var cap = start.AddMinutes(MaxSessionMinutes);
        if (end > cap)
            end = cap;
        return FormatTime(end);
    }

    /// <summary>
    /// Testimony lines before page padding (used by DepositionWriter to trim overflow at a turn boundary).
    /// </summary>
    public static int CountTestimonyLines(DepositionContent content) => Testimony(content).Lines.Count;

    /// <summary>
    /// Lays out caption, appearances, index, testimony, signature page, errata sheet and reporter's certificate, each
    /// starting on a new page and padded to 25 lines.
    /// </summary>
    public static TranscriptDocument Build(DepositionContent content)
    {
        content ??= new DepositionContent();
        var testimony = Testimony(content);
        var appearances = Appearances(content);
        var backMatter = new List<List<string>> { SignaturePage(content), Errata(content), Certificate(content) };

        // Index page numbers are fixed-width, so the index page count is known before testimony is placed
        var placeholderIndex = Index(content, new Dictionary<int, TranscriptLocation>(), new List<(string, TranscriptLocation)>());
        var frontPages = 1 + PageCount(appearances) + PageCount(placeholderIndex);
        var firstTestimonyPage = frontPages + 1;
        var testimonyPages = Math.Max(1, PageCount(testimony.Lines));
        var totalPages = frontPages + testimonyPages + backMatter.Sum(PageCount);

        var doc = new TranscriptDocument
        {
            FirstTestimonyPage = firstTestimonyPage,
            LastTestimonyPage = firstTestimonyPage + testimonyPages - 1,
            TestimonyPageCount = testimonyPages
        };

        foreach (var (number, lineIndex) in testimony.Markers)
            doc.ExhibitMarked[number] = Locate(firstTestimonyPage, lineIndex);
        foreach (var (label, lineIndex) in testimony.Examinations)
            doc.Examinations.Add((label, Locate(firstTestimonyPage, lineIndex)));

        var index = Index(content, doc.ExhibitMarked, doc.Examinations);

        AddSection(doc, Caption(content, totalPages), string.Empty, false);
        AddSection(doc, appearances, string.Empty, false);
        AddSection(doc, index, string.Empty, false);
        var heading = $"{LastName(Clean(content.WitnessName)).ToUpperInvariant()} - EXAMINATION";
        AddSection(doc, testimony.Lines, heading, true);
        foreach (var section in backMatter)
            AddSection(doc, section, string.Empty, false);

        return doc;
    }

    private sealed class TestimonyLayout
    {
        public List<string> Lines { get; } = new();
        public List<(int Number, int LineIndex)> Markers { get; } = new();
        public List<(string Label, int LineIndex)> Examinations { get; } = new();
    }

    private static TestimonyLayout Testimony(DepositionContent content)
    {
        var layout = new TestimonyLayout();
        var lines = layout.Lines;
        var noun = content.IsMemo ? "interview" : "deposition";
        var witness = Clean(content.WitnessName).ToUpperInvariant();
        var examiningLabel = ExaminingLabel(content);

        if (Clean(content.VideographerName).Length > 0)
        {
            var date = CaseGuideDocxExporter.LongDate(content.Date);
            lines.AddRange(Colloquy("THE VIDEOGRAPHER", $"We are on the record at {Clean(content.StartTime)} on {Clean(date)}."));
            lines.Add(string.Empty);
        }

        lines.AddRange(Wrap(new string(' ', 10) + $"{witness}, having been first duly sworn, was examined and testified as follows:", MaxCharsPerLine, 0));
        lines.Add(string.Empty);
        layout.Examinations.Add(($"BY {examiningLabel}", lines.Count));
        lines.Add(Center("EXAMINATION"));
        lines.Add($"BY {examiningLabel}:");

        var marked = new HashSet<int>();
        foreach (var turn in content.Turns ?? new List<DepositionTurn>())
        {
            if (turn == null)
                continue;
            if (turn.ExhibitNumber > 0 && marked.Add(turn.ExhibitNumber))
            {
                var marker = content.IsMemo
                    ? $"Exhibit {turn.ExhibitNumber} was marked for identification."
                    : $"Deposition Exhibit {turn.ExhibitNumber} was marked for identification.";
                layout.Markers.Add((turn.ExhibitNumber, lines.Count));
                lines.AddRange(Parenthetical(marker));
            }

            lines.AddRange(FormatTurn(turn, content));
        }

        lines.Add(string.Empty);
        var endTime = Clean(content.EndTime);
        if (endTime.Length == 0)
            endTime = ComputeEndTime(content.StartTime, lines.Count);
        lines.AddRange(Parenthetical($"Whereupon the {noun} concluded at {endTime}."));
        return layout;
    }

    private static List<string> Caption(DepositionContent content, int totalPages)
    {
        var title = content.IsMemo ? "EXAMINATION OF" : "DEPOSITION OF";
        var tail = new List<string> { string.Empty };
        var caseNumber = Clean(content.CaseNumber);
        if (caseNumber.Length > 0)
        {
            tail.AddRange(Centered($"Case No. {caseNumber}"));
            tail.Add(string.Empty);
        }

        tail.AddRange(Centered($"{title} {Clean(content.WitnessName).ToUpperInvariant()}"));
        tail.AddRange(Centered("EXCERPT"));
        tail.AddRange(Centered(string.Create(CultureInfo.InvariantCulture, $"Pages 1 - {totalPages}")));
        tail.Add(string.Empty);
        tail.AddRange(Centered(CaseGuideDocxExporter.LongDate(content.Date)));
        tail.AddRange(Centered(content.StartTime));
        tail.AddRange(Centered(content.Location));
        if (content.Remote)
            tail.AddRange(Centered("(Taken remotely by videoconference)"));
        tail.Add(string.Empty);
        tail.AddRange(Centered($"Reported by: {Clean(content.ReporterName)}, {Clean(content.ReporterCsr)}"));

        // Caption lines are truncated so the caption page never overflows page 1
        var captionLines = (content.CaptionLines ?? new List<string>())
            .SelectMany(l => Wrap(l, MaxCharsPerLine, 0))
            .Take(Math.Max(0, LinesPerPage - tail.Count))
            .ToList();
        return captionLines.Concat(tail).Take(LinesPerPage).ToList();
    }

    private static List<string> Appearances(DepositionContent content)
    {
        var lines = new List<string> { "APPEARANCES:", string.Empty };
        var speakers = (content.Speakers ?? new List<DepositionSpeaker>()).Where(s => s != null).ToList();
        foreach (var speaker in speakers.Where(s => s.Role is SpeakerRole.Examining or SpeakerRole.Defending or SpeakerRole.Appearance))
        {
            var label = Clean(speaker.PartyLabel);
            if (label.Length > 0)
                lines.AddRange(Wrap(label + ":", MaxCharsPerLine, 0));
            foreach (var detail in new[] { speaker.FullName, speaker.FirmName, speaker.Email })
            {
                if (Clean(detail).Length > 0)
                    lines.AddRange(Wrap("     " + Clean(detail), MaxCharsPerLine, 5));
            }
            lines.Add(string.Empty);
        }

        var videographer = Clean(content.VideographerName);
        if (videographer.Length > 0)
        {
            lines.Add("ALSO PRESENT:");
            lines.AddRange(Wrap($"     {videographer}, Videographer", MaxCharsPerLine, 5));
        }

        return lines;
    }

    private static List<string> Index(DepositionContent content, Dictionary<int, TranscriptLocation> marked,
        List<(string Label, TranscriptLocation At)> examinations)
    {
        var lines = new List<string>
        {
            Center("INDEX"),
            string.Empty,
            $"WITNESS: {Clean(content.WitnessName).ToUpperInvariant()}",
            string.Empty,
            RightAlign("EXAMINATION", "PAGE"),
        };

        if (examinations.Count == 0)
            lines.Add(Dotted($"  BY {ExaminingLabel(content)}", string.Empty));
        foreach (var (label, at) in examinations)
            lines.Add(Dotted("  " + label, at.Page.ToString(CultureInfo.InvariantCulture)));

        lines.Add(string.Empty);
        lines.Add("EXHIBITS");
        lines.Add(RightAlign("NO.   DESCRIPTION", "PAGE"));
        foreach (var exhibit in (content.Exhibits ?? new List<DepositionExhibit>()).Where(e => e != null).OrderBy(e => e.Number))
        {
            var page = marked.TryGetValue(exhibit.Number, out var at) ? at.Page.ToString(CultureInfo.InvariantCulture) : "--";
            var description = Wrap(Clean(exhibit.Description), MaxCharsPerLine - ExhibitIndexPrefix - PageNumberWidth, 0);
            if (description.Count == 0)
                description.Add(string.Empty);
            for (var i = 0; i < description.Count; i++)
            {
                var prefix = i == 0
                    ? exhibit.Number.ToString(CultureInfo.InvariantCulture).PadLeft(3).PadRight(ExhibitIndexPrefix)
                    : new string(' ', ExhibitIndexPrefix);
                var line = prefix + description[i];
                lines.Add(i == description.Count - 1 ? RightAlign(line, page) : line);
            }
        }

        return lines;
    }

    private static List<string> SignaturePage(DepositionContent content)
    {
        var noun = content.IsMemo ? "examination" : "deposition";
        var witness = Clean(content.WitnessName).ToUpperInvariant();
        var lines = new List<string> { Center("WITNESS SIGNATURE"), string.Empty };
        lines.AddRange(Wrap(
            $"     I, {witness}, have read the foregoing transcript of my {noun} taken on " +
            $"{CaseGuideDocxExporter.LongDate(content.Date)}, and it is true and correct, subject to the changes, if any, " +
            "noted on the attached errata sheet.", MaxCharsPerLine, 0));
        lines.Add(string.Empty);
        lines.Add(string.Empty);
        lines.Add("     ______________________________");
        lines.AddRange(Wrap("     " + witness, MaxCharsPerLine, 5));
        lines.Add(string.Empty);
        lines.Add("     Date: ________________________");
        return lines;
    }

    private static List<string> Errata(DepositionContent content)
    {
        var lines = new List<string> { Center("ERRATA SHEET"), string.Empty };
        lines.AddRange(Wrap($"Witness: {Clean(content.WitnessName)}", MaxCharsPerLine, 9));
        lines.AddRange(Wrap($"Date taken: {CaseGuideDocxExporter.LongDate(content.Date)}", MaxCharsPerLine, 12));
        lines.Add(string.Empty);
        lines.Add("PAGE   LINE   CHANGE                       REASON");
        for (var i = 0; i < 10; i++)
            lines.Add("____   ____   __________________________   ______________");
        lines.Add(string.Empty);
        lines.Add("Signature: ____________________   Date: ____________");
        return lines;
    }

    private static List<string> Certificate(DepositionContent content)
    {
        var reporter = Clean(content.ReporterName);
        var csr = Clean(content.ReporterCsr);
        var noun = content.IsMemo ? "examination" : "deposition";
        var lines = new List<string> { Center("REPORTER'S CERTIFICATE"), string.Empty };
        lines.AddRange(Wrap(
            $"     I, {reporter}, {csr}, certify that the witness in the foregoing {noun}, " +
            $"{Clean(content.WitnessName)}, was duly sworn by me before testifying; that the testimony was taken down " +
            "stenographically by me and transcribed under my direction; and that the foregoing transcript is a true " +
            "record of the testimony given.", MaxCharsPerLine, 0));
        lines.Add(string.Empty);
        lines.AddRange(Wrap(
            "     I further certify that I am neither counsel for, related to, nor employed by any of the parties, and " +
            "that I have no interest in the outcome of the action.", MaxCharsPerLine, 0));
        lines.Add(string.Empty);
        lines.AddRange(Wrap($"     Dated: {CaseGuideDocxExporter.LongDate(content.Date)}", MaxCharsPerLine, 5));
        lines.Add(string.Empty);
        lines.Add("     ______________________________");
        lines.AddRange(Wrap($"     {reporter}, {csr}", MaxCharsPerLine, 5));
        return lines;
    }

    private static void AddSection(TranscriptDocument doc, List<string> lines, string heading, bool testimony)
    {
        var count = Math.Max(1, PageCount(lines));
        for (var p = 0; p < count; p++)
        {
            var page = new TranscriptPage
            {
                Number = doc.Pages.Count + 1,
                Heading = heading,
                IsTestimony = testimony
            };
            page.Lines.AddRange(lines.Skip(p * LinesPerPage).Take(LinesPerPage));
            while (page.Lines.Count < LinesPerPage)
                page.Lines.Add(string.Empty);
            doc.Pages.Add(page);
        }
    }

    private static int PageCount(List<string> lines) => Math.Max(1, (lines.Count + LinesPerPage - 1) / LinesPerPage);

    private static TranscriptLocation Locate(int firstPage, int lineIndex) => new()
    {
        Page = firstPage + lineIndex / LinesPerPage,
        Line = lineIndex % LinesPerPage + 1
    };

    private static List<string> Colloquy(string label, string text) =>
        Wrap(new string(' ', ColloquyLabelIndent) + label + ":  " + text, MaxCharsPerLine, 0);

    private static List<string> Parenthetical(string text) =>
        Wrap(new string(' ', ParentheticalIndent) + "(" + text + ")", MaxCharsPerLine, ParentheticalIndent + 1);

    private static string ExaminingLabel(DepositionContent content)
    {
        var examining = content.Speakers?.FirstOrDefault(s => s != null && s.Role == SpeakerRole.Examining);
        return examining != null ? SpeakerLabel(content, examining.SpeakerId) : "COUNSEL";
    }

    private static string Center(string text)
    {
        var clean = Clean(text);
        if (clean.Length >= MaxCharsPerLine)
            return clean[..MaxCharsPerLine];
        return new string(' ', (MaxCharsPerLine - clean.Length) / 2) + clean;
    }

    private static IEnumerable<string> Centered(string? text) =>
        Wrap(Clean(text), MaxCharsPerLine, 0).Select(Center);

    // Left text, then the right text right-aligned at column 62
    private static string RightAlign(string left, string right)
    {
        var maxLeft = MaxCharsPerLine - PageNumberWidth;
        if (left.Length > maxLeft)
            left = left[..maxLeft];
        return left.PadRight(maxLeft) + right.PadLeft(PageNumberWidth);
    }

    // "  BY MS. QUIST ..................................     5"
    private static string Dotted(string left, string page)
    {
        var maxLeft = MaxCharsPerLine - PageNumberWidth;
        if (left.Length > maxLeft - 2)
            left = left[..(maxLeft - 2)];
        var dots = maxLeft - left.Length - 1;
        return left + " " + new string('.', Math.Max(0, dots)) + page.PadLeft(PageNumberWidth);
    }

    private static string Clean(string? text) =>
        Whitespace().Replace(CaseGuideDocxExporter.StripInvalidXmlChars(text), " ").Trim();

    private static string LastName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private static bool TryParseTime(string? value, out DateTime time)
    {
        var normalized = Clean(value)
            .Replace("a.m.", "AM", StringComparison.OrdinalIgnoreCase)
            .Replace("p.m.", "PM", StringComparison.OrdinalIgnoreCase);
        return DateTime.TryParseExact(normalized, new[] { "h:mm tt", "hh:mm tt", "h:mmtt", "H:mm" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static string FormatTime(DateTime time) =>
        time.ToString("h:mm", CultureInfo.InvariantCulture) + (time.Hour < 12 ? " a.m." : " p.m.");
}
