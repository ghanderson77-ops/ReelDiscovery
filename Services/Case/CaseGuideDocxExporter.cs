using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models.Case;
using Color = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace ReelDiscovery.Services.Case;

public enum CaseExportKind
{
    CoreDocument,
    Cast,
    KeyFacts
}

/// <summary>
/// Builds the three case guide Word exports (GUIDE-11, D-12) as validator-clean .docx bytes.
/// Pure functions: writing to disk happens in the UI through CaseStore.WriteAtomicAsync.
/// </summary>
public static class CaseGuideDocxExporter
{
    // Page geometry in twips (1 inch = 1440)
    private const int PageShort = 12240;
    private const int PageLong = 15840;
    private const int Margin = 1080;
    private const int PortraitWidth = PageShort - 2 * Margin;   // 10080
    private const int LandscapeWidth = PageLong - 2 * Margin;   // 13680

    private const string FontName = "Calibri";
    private const string HeadingColor = "1F3864";
    private const string SubheadingColor = "2F5496";
    private const string MutedColor = "595959";
    private const string HeaderFill = "F2F4F7";

    public static byte[] Build(CaseDraft draft, CaseExportKind kind) => kind switch
    {
        CaseExportKind.CoreDocument => BuildCoreDocument(draft),
        CaseExportKind.Cast => BuildCast(draft),
        CaseExportKind.KeyFacts => BuildKeyFacts(draft),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown export kind")
    };

    public static string FileNameFor(CaseExportKind kind, string folderName) => kind switch
    {
        CaseExportKind.CoreDocument => $"{folderName} - Core Document.docx",
        CaseExportKind.Cast => $"{folderName} - Cast of Key Characters.docx",
        CaseExportKind.KeyFacts => $"{folderName} - Key Facts & Events.docx",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown export kind")
    };

    /// <summary>
    /// Lower-case noun used in UI error copy ("Could not export the {noun}.").
    /// </summary>
    public static string DocumentNoun(CaseExportKind kind) => kind switch
    {
        CaseExportKind.CoreDocument => "core document",
        CaseExportKind.Cast => "cast",
        CaseExportKind.KeyFacts => "key facts summary",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown export kind")
    };

    public static byte[] BuildCoreDocument(CaseDraft draft)
    {
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Nothing to export yet.");
        var word = new WordBuilder();

        if (CoreDocumentTemplates.IsPleading(doc.DocumentType))
            WritePleading(word, doc);
        else
            WriteMemo(word, doc);

        return word.Save(doc.Title, landscape: false);
    }

    // Mirrors CoreDocumentAssembler.RenderPleading, structured as Word paragraphs
    private static void WritePleading(WordBuilder word, CoreDocument doc)
    {
        WriteCaption(word, CoreDocumentAssembler.BuildCaption(doc));

        var isPetition = doc.DocumentType == CoreDocumentType.Petition;
        var label = CoreDocumentAssembler.PleadingPartyLabel(doc);
        var numberedBySection = CoreDocumentAssembler.Number(doc).ToLookup(p => p.SectionId);
        var factsHeaderWritten = false;
        var causesHeaderWritten = false;

        foreach (var section in doc.Sections)
        {
            // Group headers become Heading1 and their subsections Heading2 so Word's navigation pane nests them
            var nested = false;
            if (section.Kind == SectionKind.FactualAllegations)
            {
                if (!factsHeaderWritten)
                {
                    word.Paragraph(isPetition ? "FACTS" : "FACTUAL ALLEGATIONS", new ParaFormat { Style = "Heading1", Align = JustificationValues.Center });
                    factsHeaderWritten = true;
                }
                nested = true;
            }

            if (section.Kind == SectionKind.Count && isPetition)
            {
                if (!causesHeaderWritten)
                {
                    word.Paragraph("CAUSES OF ACTION", new ParaFormat { Style = "Heading1", Align = JustificationValues.Center });
                    causesHeaderWritten = true;
                }
                nested = true;
            }

            word.Paragraph(section.Heading, nested
                ? new ParaFormat { Style = "Heading2" }
                : new ParaFormat { Style = "Heading1", Align = JustificationValues.Center });

            if (section.Kind == SectionKind.PrayerForRelief)
            {
                WritePrayer(word, section, label);
                continue;
            }

            foreach (var paragraph in numberedBySection[section.Id])
                word.Paragraph($"{paragraph.Number}. {paragraph.Text}", BodyFormat());
        }

        if (doc.DemandsJuryTrial)
        {
            var isComplaint = doc.DocumentType == CoreDocumentType.Complaint;
            word.Paragraph(isComplaint ? "DEMAND FOR JURY TRIAL" : "JURY DEMAND", new ParaFormat { Style = "Heading1", Align = JustificationValues.Center });
            word.Paragraph(isComplaint ? $"{label} demands a trial by jury on all issues so triable." : $"{label} demands a jury trial.", BodyFormat());
        }

        WriteSignatureBlock(word, CoreDocumentAssembler.BuildSignatureBlock(doc));
    }

    private static void WritePrayer(WordBuilder word, CoreDocumentSection section, string label)
    {
        word.Paragraph($"WHEREFORE, {label} respectfully requests that the Court enter judgment in its favor and award the following relief:", BodyFormat());

        var items = section.Paragraphs.Select(CoreDocumentAssembler.StripLeadingNumber).Where(p => p.Length > 0).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            // Same lettering rule as CoreDocumentAssembler.AppendPrayer
            var marker = i < 26 ? $"{(char)('A' + i)}." : $"{i + 1}.";
            word.Paragraph($"{marker} {items[i]}", new ParaFormat { IndentLeft = 1080, Hanging = 360, Align = JustificationValues.Both });
        }
    }

    private static void WriteCaption(WordBuilder word, string caption)
    {
        var lines = SplitLines(caption);
        var titleIndex = lines.FindLastIndex(l => l.Trim().Length > 0);
        var inCourtBlock = true;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (i == titleIndex)
            {
                word.Paragraph(trimmed, new ParaFormat { Style = "Title", Align = JustificationValues.Center, SpaceBefore = 240 });
                break;
            }

            if (trimmed.Length == 0)
            {
                inCourtBlock = false;
                word.Paragraph(string.Empty, Compact());
                continue;
            }

            if (inCourtBlock)
            {
                word.Paragraph(trimmed, new ParaFormat { Bold = true, Align = JustificationValues.Center, SpaceAfter = 0 });
                continue;
            }

            // "v.   Case No. ____": the assembler pads with spaces; Word gets a right tab stop instead
            var gap = trimmed.IndexOf("   ", StringComparison.Ordinal);
            if (trimmed.StartsWith("v.", StringComparison.Ordinal) && gap > 0)
            {
                word.Paragraph(trimmed[..gap] + "\t" + trimmed[gap..].Trim(), new ParaFormat { SpaceAfter = 0, RightTab = PortraitWidth });
                continue;
            }

            var indented = line.StartsWith(' ');
            word.Paragraph(trimmed, new ParaFormat { SpaceAfter = 0, IndentLeft = indented ? 720 : 0 });
        }
    }

    private static void WriteSignatureBlock(WordBuilder word, string block)
    {
        var lines = SplitLines(block);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        word.Paragraph(string.Empty, Compact());
        for (var i = 0; i < lines.Count; i++)
        {
            // The dated line sits at the left margin; the signature itself on the right half of the page
            var indent = i == 0 ? 0 : PortraitWidth / 2;
            word.Paragraph(lines[i].Trim(), new ParaFormat { IndentLeft = indent, SpaceAfter = 0, KeepNext = i < lines.Count - 1 });
        }
    }

    // Mirrors CoreDocumentAssembler.RenderMemo: unnumbered paragraphs under each heading
    private static void WriteMemo(WordBuilder word, CoreDocument doc)
    {
        foreach (var line in SplitLines(CoreDocumentAssembler.BuildMemoHeader(doc)))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            if (trimmed.All(c => c == '-'))
            {
                // Plain-text rule becomes a real bottom border
                word.Paragraph(string.Empty, new ParaFormat { SpaceAfter = 240, BottomBorder = true });
                continue;
            }

            if (trimmed == "MEMORANDUM")
            {
                word.Paragraph(trimmed, new ParaFormat { Style = "Title", Align = JustificationValues.Center, SpaceBefore = 120 });
                continue;
            }

            var colon = trimmed.IndexOf(": ", StringComparison.Ordinal);
            if (colon is > 0 and <= 5 && trimmed[..colon].All(char.IsUpper))
            {
                // TO:/FROM:/CC:/DATE:/RE: as bold label + tab-aligned value
                word.Paragraph(trimmed[(colon + 2)..], new ParaFormat
                {
                    BoldLead = trimmed[..(colon + 1)] + "\t",
                    IndentLeft = 1080,
                    Hanging = 1080,
                    SpaceAfter = 60
                });
                continue;
            }

            // Privilege / sensitivity banner
            word.Paragraph(trimmed, new ParaFormat { Bold = true, Align = JustificationValues.Center, Color = "C00000", SizeHalfPoints = 18 });
        }

        foreach (var section in doc.Sections)
        {
            word.Paragraph(section.Heading, new ParaFormat { Style = "Heading1" });
            foreach (var paragraph in section.Paragraphs.Select(CoreDocumentAssembler.StripLeadingNumber).Where(p => p.Length > 0))
                word.Paragraph(paragraph, new ParaFormat { Align = JustificationValues.Both });
        }
    }

    public static byte[] BuildCast(CaseDraft draft)
    {
        var (doc, guide) = RequireGuide(draft);
        var word = new WordBuilder();

        var key = guide.Cast.Where(c => c.Tier == CastTier.Key).ToList();
        var background = guide.Cast.Where(c => c.Tier == CastTier.Background).ToList();

        word.Paragraph("Cast of Key Characters", new ParaFormat { Style = "Title" });
        word.Paragraph(doc.Title, new ParaFormat { Style = "Subtitle" });
        // Title block shares the first landscape page with the wide reference tables
        word.Paragraph($"{key.Count} key and {background.Count} background characters across {guide.Organizations.Count} organizations.");

        word.Paragraph("Organizations", new ParaFormat { Style = "Heading1" });
        if (guide.Organizations.Count == 0)
            word.Paragraph(NoneText);
        else
            word.Table(
                new[] { "ID", "Organization", "Type", "Domain", "Location", "Represents" },
                guide.Organizations.Select(o => (IReadOnlyList<Cell>)new Cell[]
                {
                    o.Id, o.Name, OrganizationKindText(o.Kind), o.Domain, o.Location,
                    string.Join(", ", o.RepresentsOrgIds.Select(id => OrganizationName(guide, id)))
                }),
                new[] { 900, 3600, 1300, 2400, 2400, 3080 });

        word.Paragraph("Key Characters", new ParaFormat { Style = "Heading1" });
        if (key.Count == 0)
            word.Paragraph(NoneText);
        else
            word.Table(
                new[] { "ID", "Name", "Title", "Organization", "Email", "Active", "Role" },
                key.Select(c => (IReadOnlyList<Cell>)new Cell[]
                {
                    c.Id, c.FullName, c.Title, OrganizationName(guide, c.OrganizationId), c.Email,
                    ActiveRange(c, ShortDate), RoleText(c.Role)
                }),
                new[] { 900, 2000, 2100, 2800, 3080, 1500, 1300 });
        word.EndSection(landscape: true);

        word.Paragraph("Character Profiles", new ParaFormat { Style = "Heading1" });
        if (key.Count == 0)
            word.Paragraph(NoneText);
        foreach (var member in key)
        {
            word.Paragraph($"{member.FullName} ({member.Id})", new ParaFormat { Style = "Heading2" });
            ProfileLine(word, "Title", member.Title);
            ProfileLine(word, "Organization", OrganizationName(guide, member.OrganizationId));
            ProfileLine(word, "Email", member.Email);
            ProfileLine(word, "Active", ActiveRange(member, LongDate));
            ProfileLine(word, "Key involvement", member.Involvement);
            ProfileLine(word, "Writing voice", member.VoiceNote);
            word.Paragraph(string.Empty, new ParaFormat { BoldLead = "Signature:", SpaceAfter = 40, KeepNext = true });
            word.Paragraph(member.Signature, new ParaFormat { IndentLeft = 360, Color = MutedColor, SpaceAfter = 200, KeepLines = true });
        }

        word.Paragraph("Background Staff", new ParaFormat { Style = "Heading1" });
        if (background.Count == 0)
            word.Paragraph(NoneText);

        // Organization order first, then any staff whose organization id no longer resolves
        var groups = background
            .GroupBy(c => c.OrganizationId)
            .OrderBy(g => OrganizationOrder(guide, g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var groupName = group.Key.Length == 0 ? "(no organization)" : OrganizationName(guide, group.Key);
            word.Paragraph(groupName, new ParaFormat { Style = "Heading2" });
            word.Table(
                new[] { "ID", "Name", "Title", "Department", "Email" },
                group.Select(c => (IReadOnlyList<Cell>)new Cell[] { c.Id, c.FullName, c.Title, c.Department, c.Email }),
                new[] { 900, 2200, 2700, 1900, 2380 });
        }

        return word.Save("Cast of Key Characters", landscape: false);
    }

    public static byte[] BuildKeyFacts(CaseDraft draft)
    {
        var (doc, guide) = RequireGuide(draft);
        var word = new WordBuilder();

        // Undated facts sort last; ids break ties
        var facts = guide.Facts
            .OrderBy(f => CaseDraft.TryParseDate(f.Date, out var d) ? d : DateTime.MaxValue)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        word.Paragraph("Key Facts & Events", new ParaFormat { Style = "Title" });
        word.Paragraph(doc.Title, new ParaFormat { Style = "Subtitle" });
        var dated = facts.Where(f => CaseDraft.TryParseDate(f.Date, out _)).ToList();
        word.Paragraph(dated.Count > 0
            ? $"{facts.Count} key facts from {LongDate(dated[0].Date)} to {LongDate(dated[^1].Date)}."
            : $"{facts.Count} key facts.");

        word.Paragraph("Chronology", new ParaFormat { Style = "Heading1" });
        if (facts.Count == 0)
            word.Paragraph(NoneText);
        else
            word.Table(
                new[] { "ID", "Date", "Fact", "Issues", "Who did it", "Who knew" },
                facts.Select(f => (IReadOnlyList<Cell>)new Cell[]
                {
                    f.Id,
                    LongDate(f.Date),
                    new Cell(f.Description, f.Title.Length > 0 ? f.Title + ": " : null),
                    string.Join(", ", f.IssueIds),
                    CastNames(guide, f.ActorIds),
                    CastNames(guide, f.KnownByIds)
                }),
                new[] { 800, 1700, 6180, 1000, 2000, 2000 });
        word.EndSection(landscape: true);

        word.Paragraph("Index by Issue", new ParaFormat { Style = "Heading1" });
        if (guide.Issues.Count == 0)
            word.Paragraph(NoneText);
        foreach (var issue in guide.Issues)
        {
            word.Paragraph($"{issue.Id} {issue.Title}".Trim(), new ParaFormat { Style = "Heading2" });
            var linked = facts.Where(f => f.IssueIds.Contains(issue.Id, StringComparer.Ordinal)).ToList();
            if (linked.Count == 0)
                word.Paragraph("(no facts)", new ParaFormat { Color = MutedColor });
            foreach (var fact in linked)
            {
                var date = fact.Date.Length > 0 ? $" ({LongDate(fact.Date)})" : string.Empty;
                word.Paragraph(fact.Title, new ParaFormat { BoldLead = $"{fact.Id}{date} ", IndentLeft = 360, SpaceAfter = 60 });
            }
        }

        return word.Save("Key Facts & Events", landscape: false);
    }

    private const string NoneText = "(none)";

    private static (CoreDocument Doc, CaseGuide Guide) RequireGuide(CaseDraft draft)
    {
        if (draft.CoreDocument == null || draft.Guide == null)
            throw new InvalidOperationException("Nothing to export yet.");
        return (draft.CoreDocument, draft.Guide);
    }

    private static void ProfileLine(WordBuilder word, string label, string value) =>
        // KeepNext chains each profile to its signature so a profile never splits across pages
        word.Paragraph(value, new ParaFormat { BoldLead = label + ": ", SpaceAfter = 60, KeepNext = true });

    // Unknown ids render as the raw id (T-03-19): exports never throw on orphans
    private static string OrganizationName(CaseGuide guide, string id)
    {
        var organization = guide.FindOrganization(id);
        return organization == null || organization.Name.Length == 0 ? id : organization.Name;
    }

    private static int OrganizationOrder(CaseGuide guide, string id)
    {
        var index = guide.Organizations.FindIndex(o => o.Id == id);
        return index < 0 ? int.MaxValue : index;
    }

    private static string CastNames(CaseGuide guide, IEnumerable<string> ids) =>
        string.Join(", ", ids.Select(id =>
        {
            var member = guide.FindCast(id);
            return member == null || member.FullName.Length == 0 ? id : member.FullName;
        }));

    private static string ActiveRange(CastMember member, Func<string, string> format)
    {
        if (member.ActiveFrom.Length == 0 && member.ActiveTo.Length == 0)
            return string.Empty;
        var from = member.ActiveFrom.Length > 0 ? format(member.ActiveFrom) : "unknown";
        var to = member.ActiveTo.Length > 0 ? format(member.ActiveTo) : "present";
        return $"{from} to {to}";
    }

    private static string ShortDate(string yyyyMmDd) =>
        CaseDraft.TryParseDate(yyyyMmDd, out var date)
            ? date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
            : yyyyMmDd;

    private static string RoleText(CastRole role) => role switch
    {
        CastRole.Party => "Party",
        CastRole.InHouseCounsel => "In-house counsel",
        CastRole.OutsideCounsel => "Outside counsel",
        _ => "Staff"
    };

    private static string OrganizationKindText(OrganizationKind kind) => kind switch
    {
        OrganizationKind.Party => "Party",
        OrganizationKind.LawFirm => "Law firm",
        OrganizationKind.Agency => "Agency",
        OrganizationKind.ThirdParty => "Third party",
        _ => "Individual"
    };

    private static ParaFormat BodyFormat() => new() { FirstLine = 720, Align = JustificationValues.Both };

    private static ParaFormat Compact() => new() { SpaceAfter = 0 };

    // Assembler text uses AppendLine, so drop the empty entry after the final newline
    private static List<string> SplitLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>
    /// "2024-03-03" -> "March 3, 2024"; anything unparseable is returned unchanged.
    /// </summary>
    internal static string LongDate(string yyyyMmDd) =>
        CaseDraft.TryParseDate(yyyyMmDd, out var date)
            ? date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
            : yyyyMmDd ?? string.Empty;

    /// <summary>
    /// Drops characters XML 1.0 cannot carry (Pitfall 9): controls other than tab/CR/LF, U+FFFE/U+FFFF and lone surrogates.
    /// </summary>
    internal static string StripInvalidXmlChars(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c).Append(text[i + 1]);
                    i++;
                }
                continue;
            }

            if (c is '\t' or '\n' or '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Paragraph formatting options; unset values inherit from the paragraph style.
    /// </summary>
    internal sealed class ParaFormat
    {
        public string? Style { get; set; }
        public bool Bold { get; set; }
        public string? BoldLead { get; set; }
        public string? Color { get; set; }
        public int? SizeHalfPoints { get; set; }
        public JustificationValues? Align { get; set; }
        public int IndentLeft { get; set; }
        public int FirstLine { get; set; }
        public int Hanging { get; set; }
        public int? SpaceBefore { get; set; }
        public int? SpaceAfter { get; set; }
        public int? RightTab { get; set; }
        public bool KeepNext { get; set; }
        public bool KeepLines { get; set; }
        public bool BottomBorder { get; set; }
    }

    /// <summary>
    /// Minimal schema-ordered WordprocessingML builder (Pitfall 10): styles, paragraphs, tables and page sections.
    /// </summary>
    internal sealed class WordBuilder
    {
        private readonly List<OpenXmlElement> _body = new();

        public void Paragraph(string? text, ParaFormat? format = null) => _body.Add(BuildParagraph(text, format ?? new ParaFormat()));

        /// <summary>
        /// Ends the current page section (portrait or landscape); content added afterwards starts a new section.
        /// </summary>
        public void EndSection(bool landscape) =>
            _body.Add(new Paragraph(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "0" },
                SectionProps(landscape))));

        public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<Cell>> rows, IReadOnlyList<int> widthsTwips)
        {
            var table = new Table(
                new TableProperties(
                    new TableStyle { Val = "GuideTable" },
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                        new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "D9D9D9" }),
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    new TableCellMarginDefault(
                        new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                        new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                        new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                        new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa })),
                new TableGrid(widthsTwips.Select(w => new GridColumn { Width = Twips(w) })));

            // Header row repeats on each page
            var header = new TableRow(new TableRowProperties(new TableHeader(), new CantSplit()));
            foreach (var (text, width) in headers.Zip(widthsTwips))
            {
                header.Append(new TableCell(
                    new TableCellProperties(
                        new TableCellWidth { Width = Twips(width), Type = TableWidthUnitValues.Dxa },
                        new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = HeaderFill }),
                    BuildParagraph(text, new ParaFormat { Style = "TableText", Bold = true })));
            }
            table.Append(header);

            foreach (var row in rows)
            {
                var tableRow = new TableRow(new TableRowProperties(new CantSplit()));
                for (var i = 0; i < widthsTwips.Count; i++)
                {
                    var cell = i < row.Count ? row[i] : new Cell(string.Empty);
                    // Every cell needs at least one paragraph
                    tableRow.Append(new TableCell(
                        new TableCellProperties(new TableCellWidth { Width = Twips(widthsTwips[i]), Type = TableWidthUnitValues.Dxa }),
                        BuildParagraph(cell.Text, new ParaFormat { Style = "TableText", BoldLead = cell.BoldLead })));
                }
                table.Append(tableRow);
            }

            _body.Add(table);
            // Spacer so consecutive blocks never fuse
            Paragraph(string.Empty, new ParaFormat { SpaceAfter = 0 });
        }

        public byte[] Save(string? title, bool landscape)
        {
            using var stream = new MemoryStream();
            using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = package.AddMainDocumentPart();
                var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
                stylesPart.Styles = BuildStyles();

                var body = new Body();
                foreach (var element in _body)
                    body.Append(element);
                // Final section properties must be the last child of the body
                body.Append(SectionProps(landscape));
                main.Document = new Document(body);

                package.PackageProperties.Title = StripInvalidXmlChars(title);
                package.PackageProperties.Creator = "ReelDiscovery";
            }

            return stream.ToArray();
        }

        private static SectionProperties SectionProps(bool landscape) => new(
            landscape
                ? new PageSize { Width = (UInt32Value)(uint)PageLong, Height = (UInt32Value)(uint)PageShort, Orient = PageOrientationValues.Landscape }
                : new PageSize { Width = (UInt32Value)(uint)PageShort, Height = (UInt32Value)(uint)PageLong },
            new PageMargin
            {
                Top = Margin,
                Right = (UInt32Value)(uint)Margin,
                Bottom = Margin,
                Left = (UInt32Value)(uint)Margin,
                Header = 720U,
                Footer = 720U,
                Gutter = 0U
            });

        private static Paragraph BuildParagraph(string? text, ParaFormat format)
        {
            var paragraph = new Paragraph();
            var properties = BuildParagraphProperties(format);
            if (properties.HasChildren)
                paragraph.Append(properties);

            if (!string.IsNullOrEmpty(format.BoldLead))
                AppendRuns(paragraph, format.BoldLead, format, bold: true);

            AppendRuns(paragraph, text, format, format.Bold);
            return paragraph;
        }

        // Order follows CT_PPrBase: pStyle, keepNext, keepLines, pBdr, tabs, spacing, ind, jc
        private static ParagraphProperties BuildParagraphProperties(ParaFormat format)
        {
            var properties = new ParagraphProperties();
            if (format.Style != null)
                properties.Append(new ParagraphStyleId { Val = format.Style });
            if (format.KeepNext)
                properties.Append(new KeepNext());
            if (format.KeepLines)
                properties.Append(new KeepLines());
            if (format.BottomBorder)
                properties.Append(new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Space = 1, Color = "808080" }));
            if (format.RightTab != null)
                properties.Append(new Tabs(new TabStop { Val = TabStopValues.Right, Position = format.RightTab.Value }));
            if (format.SpaceBefore != null || format.SpaceAfter != null)
            {
                var spacing = new SpacingBetweenLines();
                if (format.SpaceBefore != null)
                    spacing.Before = Twips(format.SpaceBefore.Value);
                if (format.SpaceAfter != null)
                    spacing.After = Twips(format.SpaceAfter.Value);
                properties.Append(spacing);
            }
            if (format.IndentLeft != 0 || format.FirstLine != 0 || format.Hanging != 0)
            {
                var indent = new Indentation();
                if (format.IndentLeft != 0)
                    indent.Left = Twips(format.IndentLeft);
                if (format.Hanging != 0)
                    indent.Hanging = Twips(format.Hanging);
                else if (format.FirstLine != 0)
                    indent.FirstLine = Twips(format.FirstLine);
                properties.Append(indent);
            }
            if (format.Align != null)
                properties.Append(new Justification { Val = format.Align.Value });
            return properties;
        }

        private static void AppendRuns(Paragraph paragraph, string? text, ParaFormat format, bool bold)
        {
            var clean = StripInvalidXmlChars(text).Replace("\r\n", "\n").Replace('\r', '\n');
            if (clean.Length == 0)
                return;

            var run = new Run();
            var runProperties = BuildRunProperties(format, bold);
            if (runProperties != null)
                run.Append(runProperties);

            var lines = clean.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    run.Append(new Break());

                var segments = lines[i].Split('\t');
                for (var j = 0; j < segments.Length; j++)
                {
                    if (j > 0)
                        run.Append(new TabChar());
                    if (segments[j].Length > 0)
                        run.Append(new Text(segments[j]) { Space = SpaceProcessingModeValues.Preserve });
                }
            }

            paragraph.Append(run);
        }

        // Pitfall 10: rFonts, b, color, sz, szCs
        private static RunProperties? BuildRunProperties(ParaFormat format, bool bold)
        {
            if (!bold && format.Color == null && format.SizeHalfPoints == null)
                return null;

            var properties = new RunProperties();
            if (bold)
                properties.Append(new Bold());
            if (format.Color != null)
                properties.Append(new Color { Val = format.Color });
            if (format.SizeHalfPoints != null)
            {
                properties.Append(new FontSize { Val = Twips(format.SizeHalfPoints.Value) });
                properties.Append(new FontSizeComplexScript { Val = Twips(format.SizeHalfPoints.Value) });
            }
            return properties;
        }

        private static Styles BuildStyles()
        {
            var styles = new Styles(
                new DocDefaults(
                    new RunPropertiesDefault(new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName, EastAsia = FontName },
                        new FontSize { Val = "22" },
                        new FontSizeComplexScript { Val = "22" },
                        new Languages { Val = "en-US" })),
                    new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                        new SpacingBetweenLines { After = "160", Line = "264", LineRule = LineSpacingRuleValues.Auto }))));

            styles.Append(ParagraphStyle("Normal", "Normal", null, isDefault: true,
                null,
                new StyleRunProperties(
                    new RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName, EastAsia = FontName },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" })));

            styles.Append(ParagraphStyle("Title", "Title", "Normal", isDefault: false,
                new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "240" }),
                new StyleRunProperties(
                    new RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName, EastAsia = FontName },
                    new Bold(),
                    new Color { Val = HeadingColor },
                    new FontSize { Val = "32" },
                    new FontSizeComplexScript { Val = "32" })));

            styles.Append(ParagraphStyle("Subtitle", "Subtitle", "Normal", isDefault: false,
                new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "240" }),
                new StyleRunProperties(
                    new Color { Val = MutedColor },
                    new FontSize { Val = "24" },
                    new FontSizeComplexScript { Val = "24" })));

            styles.Append(ParagraphStyle("Heading1", "heading 1", "Normal", isDefault: false,
                new StyleParagraphProperties(
                    new KeepNext(),
                    new KeepLines(),
                    new SpacingBetweenLines { Before = "240", After = "120" },
                    new OutlineLevel { Val = 0 }),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = HeadingColor },
                    new FontSize { Val = "26" },
                    new FontSizeComplexScript { Val = "26" })));

            styles.Append(ParagraphStyle("Heading2", "heading 2", "Normal", isDefault: false,
                new StyleParagraphProperties(
                    new KeepNext(),
                    new KeepLines(),
                    new SpacingBetweenLines { Before = "200", After = "80" },
                    new OutlineLevel { Val = 1 }),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = SubheadingColor },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" })));

            styles.Append(ParagraphStyle("TableText", "Table Text", "Normal", isDefault: false,
                new StyleParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                new StyleRunProperties(
                    new FontSize { Val = "19" },
                    new FontSizeComplexScript { Val = "19" })));

            styles.Append(new Style(
                new StyleName { Val = "Guide Table" },
                new UIPriority { Val = 59 },
                new StyleTableProperties(
                    new TableStyleRowBandSize { Val = 1 },
                    new TableStyleColumnBandSize { Val = 1 },
                    new TableCellMarginDefault(
                        new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                        new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                        new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                        new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa })))
            {
                Type = StyleValues.Table,
                StyleId = "GuideTable"
            });

            return styles;
        }

        private static Style ParagraphStyle(string id, string name, string? basedOn, bool isDefault,
            StyleParagraphProperties? paragraphProperties, StyleRunProperties runProperties)
        {
            // CT_Style order: name, basedOn, next, uiPriority, qFormat, pPr, rPr
            var style = new Style { Type = StyleValues.Paragraph, StyleId = id };
            if (isDefault)
                style.Default = true;
            style.Append(new StyleName { Val = name });
            if (basedOn != null)
                style.Append(new BasedOn { Val = basedOn });
            style.Append(new NextParagraphStyle { Val = "Normal" });
            style.Append(new PrimaryStyle());
            if (paragraphProperties != null)
                style.Append(paragraphProperties);
            style.Append(runProperties);
            return style;
        }

        private static string Twips(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Table cell text with an optional bold lead-in ("Title: " before the description).
    /// </summary>
    internal sealed class Cell
    {
        public Cell(string? text, string? boldLead = null)
        {
            Text = text ?? string.Empty;
            BoldLead = boldLead;
        }

        public string Text { get; }
        public string? BoldLead { get; }

        public static implicit operator Cell(string? text) => new(text);
    }
}
