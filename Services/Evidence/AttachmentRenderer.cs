using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Mapped, clamped text of one attachment: the input of the Office/ICS builders and of the attachment checks.
/// </summary>
public class AttachmentContent
{
    public AttachmentType Kind { get; set; }
    public string Title { get; set; } = string.Empty;

    // Word
    public List<WordSection> Sections { get; set; } = new();
    public SimpleTable? Table { get; set; }

    // Excel
    public string SheetName { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public string Notes { get; set; } = string.Empty;

    // PowerPoint
    public List<SlideContent> Slides { get; set; } = new();

    // Calendar invite (D-20: date, time and attendees are code-owned)
    public string Agenda { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    public List<string> PeopleNamed { get; set; } = new();

    // Relevant only (D-18): Seq = parent seq, FromAttachment = true
    public List<LedgerDelta> NewDetails { get; set; } = new();
}

/// <summary>
/// The per-attachment text call (ATT-01, ATT-02, ATT-04): one strict-schema request per Word, Excel, PowerPoint or invite
/// attachment. Model output is untrusted: every list and string is clamped and control characters are stripped before
/// any bytes are built (T-06-32, T-06-35).
/// </summary>
public static partial class AttachmentRenderer
{
    // A 3-page memo with a table is about 3K output tokens; the gateway doubles once on truncation
    public const int MaxOutputTokens = 8_000;

    public const int MaxSections = 6;
    public const int MaxParagraphs = 8;
    public const int MaxColumns = 8;
    public const int MaxRows = 60;
    public const int MaxSlides = 12;
    public const int MaxBullets = 8;
    public const int MaxChars = 2_000;

    private const string RowSeparator = " | ";

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[]
    {
        typeof(WordAttachmentDto), typeof(ExcelAttachmentDto), typeof(PowerPointAttachmentDto), typeof(InviteAttachmentDto),
        typeof(FillerWordDto), typeof(FillerExcelDto), typeof(FillerPowerPointDto)
    };

    [GeneratedRegex(@"[\r\n\t]+")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"\p{Cc}")]
    private static partial Regex ControlChars();

    /// <summary>
    /// The request for one attempt: relevant attachments use the case system prompt, filler the mundane one; attempts after
    /// the first carry the previous attempt's violations at the end of the user prompt (D-13).
    /// </summary>
    internal static LlmRequest BuildRequest(AttachmentRenderContext ctx, int attempt, IReadOnlyList<RenderViolation>? feedback) => new()
    {
        Tier = ctx.Tier,
        OperationName = $"Generate Corpus - {ctx.Spec.DocId}" + (attempt > 1 ? $" attempt {attempt}" : string.Empty),
        SystemPrompt = ctx.Relevant ? AttachmentPrompts.SystemPrompt : AttachmentPrompts.FillerSystemPrompt,
        UserPrompt = AttachmentPrompts.Build(ctx, feedback),
        MaxOutputTokens = MaxOutputTokens
    };

    /// <summary>
    /// The DTO the call asks for: relevant Office kinds return newDetails, filler kinds never can (D-13); invites have one
    /// shape for both.
    /// </summary>
    internal static Type DtoTypeFor(AttachmentType kind, bool relevant) => kind switch
    {
        AttachmentType.Word => relevant ? typeof(WordAttachmentDto) : typeof(FillerWordDto),
        AttachmentType.Excel => relevant ? typeof(ExcelAttachmentDto) : typeof(FillerExcelDto),
        AttachmentType.PowerPoint => relevant ? typeof(PowerPointAttachmentDto) : typeof(FillerPowerPointDto),
        AttachmentType.CalendarInvite => typeof(InviteAttachmentDto),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No text call for this attachment kind")
    };

    /// <summary>
    /// Maps a model DTO to clamped content. Ledger deltas are kept only for relevant attachments and always take the
    /// parent's seq (the model's seq is ignored).
    /// </summary>
    internal static AttachmentContent Map(object dto, AttachmentRenderContext ctx)
    {
        var content = dto switch
        {
            WordAttachmentDto w => Word(w.Title, w.Sections, w.Table, w.PeopleNamed, w.NewDetails, ctx),
            FillerWordDto w => Word(w.Title, w.Sections, w.Table, null, null, ctx),
            ExcelAttachmentDto e => Excel(e.Title, e.SheetName, e.Headers, e.Rows, e.Notes, e.PeopleNamed, e.NewDetails, ctx),
            FillerExcelDto e => Excel(e.Title, e.SheetName, e.Headers, e.Rows, e.Notes, null, null, ctx),
            PowerPointAttachmentDto p => PowerPoint(p.Title, p.Slides, p.PeopleNamed, p.NewDetails, ctx),
            FillerPowerPointDto p => PowerPoint(p.Title, p.Slides, null, null, ctx),
            InviteAttachmentDto i => new AttachmentContent
            {
                Kind = AttachmentType.CalendarInvite,
                Title = Clean(i.Title),
                Agenda = Clean(i.Agenda),
                Location = Clean(i.Location)
            },
            _ => throw new ArgumentException($"Unknown attachment DTO {dto?.GetType().Name ?? "null"}", nameof(dto))
        };
        return content;
    }

    /// <summary>
    /// Deterministic text of the whole attachment for checks and chains: Word = title, headings, paragraphs and table
    /// cells; Excel = title, header row and every row joined by " | ", then notes; PowerPoint = title, slide titles and
    /// bullets; invite = title, agenda, location. One item per line, empty items skipped.
    /// </summary>
    internal static string Flatten(AttachmentContent content)
    {
        var lines = new List<string> { content.Title };
        switch (content.Kind)
        {
            case AttachmentType.Word:
                foreach (var section in content.Sections)
                {
                    lines.Add(section.Heading);
                    lines.AddRange(section.Paragraphs);
                }

                if (content.Table != null)
                {
                    lines.AddRange(content.Table.Headers);
                    lines.AddRange(content.Table.Rows.SelectMany(r => r));
                }

                break;
            case AttachmentType.Excel:
                lines.Add(JoinRow(content.Headers));
                lines.AddRange(content.Rows.Select(JoinRow));
                lines.Add(content.Notes);
                break;
            case AttachmentType.PowerPoint:
                foreach (var slide in content.Slides)
                {
                    lines.Add(slide.Title);
                    lines.AddRange(slide.Bullets);
                }

                break;
            case AttachmentType.CalendarInvite:
                lines.Add(content.Agenda);
                lines.Add(content.Location);
                break;
        }

        return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    /// <summary>
    /// Text the canonical-variant checks run on (Pitfall 5): for Excel only rows whose first cell names a canonical label
    /// or a visible ledger subject (whole word), plus notes, so line items never trip V3; other kinds are all prose.
    /// </summary>
    internal static string VariantText(AttachmentContent content, IEnumerable<string> labels)
    {
        if (content.Kind != AttachmentType.Excel)
            return Flatten(content);

        var terms = (labels ?? Enumerable.Empty<string>())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();
        var lines = content.Rows
            .Where(r => r.Count > 0 && terms.Any(t => RenderValidator.ContainsWord(r[0], t)))
            .Select(JoinRow)
            .ToList();
        lines.Add(content.Notes);
        return string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    /// <summary>
    /// True when the content is a real document of its kind; an unusable answer is retried like a failed check.
    /// </summary>
    internal static bool IsUsable(AttachmentContent content)
    {
        // Review 06-11: a title-only or empty-table answer is no document
        if (content == null)
            return false;
        return content.Kind switch
        {
            AttachmentType.Word => content.Sections.Any(s => s.Paragraphs.Any(p => p.Length > 0)),
            AttachmentType.Excel => content.Headers.Any(h => h.Length > 0) && content.Rows.Any(r => r.Any(c => c.Length > 0)),
            AttachmentType.PowerPoint => content.Slides.Any(s => s.Title.Length > 0 || s.Bullets.Any(b => b.Length > 0)),
            AttachmentType.CalendarInvite => content.Title.Length > 0,
            _ => false
        };
    }

    private static AttachmentContent Word(string title, List<WordSectionDto>? sections, SimpleTableDto? table,
        List<string>? peopleNamed, List<ThreadRenderer.LedgerDeltaDto>? newDetails, AttachmentRenderContext ctx)
    {
        var content = new AttachmentContent
        {
            Kind = AttachmentType.Word,
            Title = Clean(title),
            Sections = (sections ?? new List<WordSectionDto>())
                .Where(s => s != null)
                .Select(s => new WordSection { Heading = Clean(s.Heading), Paragraphs = CleanList(s.Paragraphs, MaxParagraphs) })
                .Where(s => s.Heading.Length > 0 || s.Paragraphs.Count > 0)
                .Take(MaxSections)
                .ToList()
        };

        var headers = (table?.Headers ?? new List<string>()).Select(Clean).Take(MaxColumns).ToList();
        if (headers.Any(h => h.Length > 0))
            content.Table = new SimpleTable { Headers = headers, Rows = CleanRows(table!.Rows, headers.Count) };

        AddPeopleAndDetails(content, peopleNamed, newDetails, ctx);
        return content;
    }

    private static AttachmentContent Excel(string title, string sheetName, List<string>? headers, List<List<string>>? rows, string notes,
        List<string>? peopleNamed, List<ThreadRenderer.LedgerDeltaDto>? newDetails, AttachmentRenderContext ctx)
    {
        var cleanHeaders = (headers ?? new List<string>()).Select(Clean).Take(MaxColumns).ToList();
        if (!cleanHeaders.Any(h => h.Length > 0))
            cleanHeaders = new List<string>();
        var cleanTitle = Clean(title);
        var sheet = Clean(sheetName);

        var content = new AttachmentContent
        {
            Kind = AttachmentType.Excel,
            Title = cleanTitle,
            SheetName = sheet.Length > 0 ? sheet : cleanTitle,
            Headers = cleanHeaders,
            Rows = cleanHeaders.Count > 0 ? CleanRows(rows, cleanHeaders.Count) : new List<List<string>>(),
            Notes = Clean(notes)
        };

        AddPeopleAndDetails(content, peopleNamed, newDetails, ctx);
        return content;
    }

    private static AttachmentContent PowerPoint(string title, List<SlideDto>? slides, List<string>? peopleNamed,
        List<ThreadRenderer.LedgerDeltaDto>? newDetails, AttachmentRenderContext ctx)
    {
        var content = new AttachmentContent
        {
            Kind = AttachmentType.PowerPoint,
            Title = Clean(title),
            Slides = (slides ?? new List<SlideDto>())
                .Where(s => s != null)
                .Select(s => new SlideContent { Title = Clean(s.Title), Bullets = CleanList(s.Bullets, MaxBullets) })
                .Where(s => s.Title.Length > 0 || s.Bullets.Count > 0)
                .Take(MaxSlides)
                .ToList()
        };

        AddPeopleAndDetails(content, peopleNamed, newDetails, ctx);
        return content;
    }

    private static void AddPeopleAndDetails(AttachmentContent content, List<string>? peopleNamed,
        List<ThreadRenderer.LedgerDeltaDto>? newDetails, AttachmentRenderContext ctx)
    {
        content.PeopleNamed = (peopleNamed ?? new List<string>())
            .Select(Clean)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(RenderRepair.MaxPeopleNamed)
            .ToList();

        // Filler can never write the ledger (D-13); relevant deltas belong to the parent message (D-18)
        if (newDetails == null || ctx?.Relevant != true)
            return;

        var seq = ctx.Parent?.Seq ?? 0;
        var docId = ctx.Spec?.DocId ?? string.Empty;
        content.NewDetails = newDetails
            .Where(d => d != null && (!string.IsNullOrWhiteSpace(d.About) || !string.IsNullOrWhiteSpace(d.Statement)))
            .Take(RenderRepair.MaxNewDetails)
            .Select(d => new LedgerDelta
            {
                Seq = seq,
                Kind = d.Kind,
                About = Clean(d.About),
                Statement = Clean(d.Statement),
                Value = Clean(d.Value),
                FactIds = (d.FactIds ?? new List<string>())
                    .Select(Clean)
                    .Where(id => id.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                FromAttachment = true,
                AttachmentDocId = docId
            })
            .ToList();
    }

    // Rows padded or cut to the column count; fully empty rows dropped; at most MaxRows
    private static List<List<string>> CleanRows(List<List<string>>? rows, int columns) =>
        (rows ?? new List<List<string>>())
            .Where(r => r != null)
            .Select(r => Enumerable.Range(0, columns).Select(i => i < r.Count ? Clean(r[i]) : string.Empty).ToList())
            .Where(r => r.Any(c => c.Length > 0))
            .Take(MaxRows)
            .ToList();

    private static List<string> CleanList(List<string>? items, int max) =>
        (items ?? new List<string>()).Select(Clean).Where(s => s.Length > 0).Take(max).ToList();

    // Line breaks and tabs become one space, other control characters are removed, then trim and cut (T-06-35)
    internal static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var text = ControlChars().Replace(LineBreaks().Replace(value, " "), string.Empty).Trim();
        if (text.Length <= MaxChars)
            return text;
        // Never end on a high surrogate: a lone surrogate makes the OpenXml writer throw (IN-01)
        var cut = char.IsHighSurrogate(text[MaxChars - 1]) ? MaxChars - 1 : MaxChars;
        return text[..cut].TrimEnd();
    }

    private static string JoinRow(List<string> row) =>
        row.Any(c => c.Length > 0) ? string.Join(RowSeparator, row) : string.Empty;

    // ----- strict-schema DTOs (D-01): no nullable members, no recursion; sizes bounded in prose and clamped in code -----

    internal class WordAttachmentDto
    {
        [JsonPropertyName("title")]
        [Description("Document title as it appears at the top")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("sections")]
        [Description("Headed sections in order")]
        public List<WordSectionDto> Sections { get; set; } = new();

        [JsonPropertyName("table")]
        [Description("One optional table; empty headers means no table")]
        public SimpleTableDto Table { get; set; } = new();

        [JsonPropertyName("peopleNamed")]
        [Description("Full or first names of every person the document mentions")]
        public List<string> PeopleNamed { get; set; } = new();

        [JsonPropertyName("newDetails")]
        [Description("Each new figure, date, total or commitment the document introduces; use 0 for seq")]
        public List<ThreadRenderer.LedgerDeltaDto> NewDetails { get; set; } = new();
    }

    internal class FillerWordDto
    {
        [JsonPropertyName("title")]
        [Description("Document title as it appears at the top")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("sections")]
        [Description("Headed sections in order")]
        public List<WordSectionDto> Sections { get; set; } = new();

        [JsonPropertyName("table")]
        [Description("One optional table; empty headers means no table")]
        public SimpleTableDto Table { get; set; } = new();
    }

    internal class WordSectionDto
    {
        [JsonPropertyName("heading")]
        [Description("Section heading")]
        public string Heading { get; set; } = string.Empty;

        [JsonPropertyName("paragraphs")]
        [Description("Paragraphs of the section in order")]
        public List<string> Paragraphs { get; set; } = new();
    }

    internal class SimpleTableDto
    {
        [JsonPropertyName("headers")]
        [Description("Column headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        [Description("Rows of cells, one cell per header")]
        public List<List<string>> Rows { get; set; } = new();
    }

    internal class ExcelAttachmentDto
    {
        [JsonPropertyName("title")]
        [Description("Workbook title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("sheetName")]
        [Description("Short worksheet name")]
        public string SheetName { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        [Description("Column headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        [Description("Rows of cells, one cell per header; amounts as text such as $1,250.00")]
        public List<List<string>> Rows { get; set; } = new();

        [JsonPropertyName("notes")]
        [Description("A short note under the table, or empty")]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("peopleNamed")]
        [Description("Full or first names of every person the workbook mentions")]
        public List<string> PeopleNamed { get; set; } = new();

        [JsonPropertyName("newDetails")]
        [Description("Each new figure, date, total or commitment the workbook introduces; use 0 for seq")]
        public List<ThreadRenderer.LedgerDeltaDto> NewDetails { get; set; } = new();
    }

    internal class FillerExcelDto
    {
        [JsonPropertyName("title")]
        [Description("Workbook title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("sheetName")]
        [Description("Short worksheet name")]
        public string SheetName { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        [Description("Column headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        [Description("Rows of cells, one cell per header")]
        public List<List<string>> Rows { get; set; } = new();

        [JsonPropertyName("notes")]
        [Description("A short note under the table, or empty")]
        public string Notes { get; set; } = string.Empty;
    }

    internal class PowerPointAttachmentDto
    {
        [JsonPropertyName("title")]
        [Description("Deck title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("slides")]
        [Description("Content slides in order")]
        public List<SlideDto> Slides { get; set; } = new();

        [JsonPropertyName("peopleNamed")]
        [Description("Full or first names of every person the deck mentions")]
        public List<string> PeopleNamed { get; set; } = new();

        [JsonPropertyName("newDetails")]
        [Description("Each new figure, date, total or commitment the deck introduces; use 0 for seq")]
        public List<ThreadRenderer.LedgerDeltaDto> NewDetails { get; set; } = new();
    }

    internal class FillerPowerPointDto
    {
        [JsonPropertyName("title")]
        [Description("Deck title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("slides")]
        [Description("Content slides in order")]
        public List<SlideDto> Slides { get; set; } = new();
    }

    internal class SlideDto
    {
        [JsonPropertyName("title")]
        [Description("Slide title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("bullets")]
        [Description("Short bullet lines")]
        public List<string> Bullets { get; set; } = new();
    }

    internal class InviteAttachmentDto
    {
        [JsonPropertyName("title")]
        [Description("Meeting title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("agenda")]
        [Description("A short agenda")]
        public string Agenda { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        [Description("Meeting place, room or call details without links")]
        public string Location { get; set; } = string.Empty;
    }
}
