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
    private sealed class ParaFormat
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
        public bool BottomBorder { get; set; }
    }

    /// <summary>
    /// Minimal schema-ordered WordprocessingML builder (Pitfall 10): styles, paragraphs, tables and page sections.
    /// </summary>
    private sealed class WordBuilder
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

        // Order follows CT_PPrBase: pStyle, keepNext, pBdr, tabs, spacing, ind, jc
        private static ParagraphProperties BuildParagraphProperties(ParaFormat format)
        {
            var properties = new ParagraphProperties();
            if (format.Style != null)
                properties.Append(new ParagraphStyleId { Val = format.Style });
            if (format.KeepNext)
                properties.Append(new KeepNext());
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
    private sealed class Cell
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
