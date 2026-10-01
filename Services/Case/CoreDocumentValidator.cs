using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

public enum IssueSeverity
{
    Warning,
    Error
}

public enum IssueCategory
{
    Structure,
    Preset,
    Timeline,
    Jurisdiction,
    Citation,
    RealName,
    Stale
}

public class CoreDocumentIssue
{
    public IssueSeverity Severity { get; set; }
    public IssueCategory Category { get; set; }

    /// <summary>
    /// Section the issue belongs to; "outline" for document-level names, empty for whole-document issues.
    /// </summary>
    public string SectionId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Pure checks over a core document (CASE-02, CASE-05, CASE-06): structure, preset ranges, timeline,
/// jurisdiction consistency, invented case law, leaked real names and staleness. Never throws on bad data.
/// </summary>
public static partial class CoreDocumentValidator
{
    private const string OutlineSectionId = "outline";

    [GeneratedRegex(@"\bparagraphs?\s+\d+", RegexOptions.IgnoreCase, CitationScanner.MatchTimeoutMs)]
    private static partial Regex ParagraphReference();

    // Whole-word state matchers, longest names first so "West Virginia" wins over "Virginia".
    // Names are fixed constants; Regex.Escape plus a match timeout keeps them safe.
    private static readonly (string Name, Regex Pattern)[] StateMatchers = Jurisdiction.UsStates
        .OrderByDescending(s => s.Length)
        .ThenBy(s => s, StringComparer.Ordinal)
        .Select(s => (s, new Regex(@"(?<![A-Za-z])" + Regex.Escape(s) + @"(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
        .ToArray();

    public static List<CoreDocumentIssue> Validate(CaseDraft draft)
    {
        var issues = new List<CoreDocumentIssue>();
        var doc = draft?.CoreDocument;
        if (draft == null || doc == null)
            return issues;

        issues.AddRange(ValidateOutline(doc, draft.Preset));
        issues.AddRange(ValidateStructure(doc, draft.Preset));
        issues.AddRange(ValidateContent(doc, draft.Substitutions));

        // Exact UI-SPEC copy
        if (!string.Equals(doc.SourceHash, draft.ComputeSourceHash(), StringComparison.Ordinal))
            issues.Add(Warning(IssueCategory.Stale, string.Empty, "Document is out of date with the scenario."));

        return issues;
    }

    public static List<CoreDocumentIssue> ValidateOutline(CoreDocument doc, ComplexityPreset preset)
    {
        var issues = new List<CoreDocumentIssue>();
        if (doc == null || preset == null)
            return issues;

        var isPleading = CoreDocumentTemplates.IsPleading(doc.DocumentType);

        // Preset ranges
        AddRangeWarning(issues, doc.Parties.Count, "parties", preset.PartiesMin, preset.PartiesMax, preset);
        if (isPleading)
            AddRangeWarning(issues, doc.Counts.Count, "counts or issues", preset.CountsMin, preset.CountsMax, preset);
        else
            AddRangeWarning(issues, doc.Counts.Count, "findings", preset.FindingsMin, preset.FindingsMax, preset);
        AddRangeWarning(issues, doc.FactPlan.Count, "factual subsections", preset.FactSubsectionsMin, preset.FactSubsectionsMax, preset);

        // Structurally empty documents must not pass Finish (WR-03)
        if (isPleading && doc.Counts.Count == 0)
            issues.Add(Error(IssueCategory.Structure, string.Empty, "The pleading has no causes of action. Regenerate the document."));
        if (doc.FactPlan.Count == 0)
            issues.Add(Error(IssueCategory.Structure, string.Empty, "The document has no factual subsections. Regenerate the document."));

        ValidateTimeline(doc, preset, isPleading, issues);
        ValidateCourt(doc, isPleading, issues);

        return issues;
    }

    public static int CountErrors(IEnumerable<CoreDocumentIssue> issues) =>
        issues.Count(i => i.Severity == IssueSeverity.Error);

    public static int CountWarnings(IEnumerable<CoreDocumentIssue> issues) =>
        issues.Count(i => i.Severity == IssueSeverity.Warning);

    /// <summary>
    /// Errors caused by case citations; the Finish gate names these separately.
    /// </summary>
    public static int CountCitationErrors(IEnumerable<CoreDocumentIssue> issues) =>
        issues.Count(i => i.Severity == IssueSeverity.Error && i.Category == IssueCategory.Citation);

    private static void AddRangeWarning(List<CoreDocumentIssue> issues, int n, string noun, int min, int max, ComplexityPreset preset)
    {
        if (n < min || n > max)
            issues.Add(Warning(IssueCategory.Preset, string.Empty,
                $"The document has {n} {noun}; the {preset.Level} preset expects {ComplexityPreset.FormatRange(min, max)}."));
    }

    private static void ValidateTimeline(CoreDocument doc, ComplexityPreset preset, bool isPleading, List<CoreDocumentIssue> issues)
    {
        var hasStart = CaseDraft.TryParseDate(doc.TimelineStart, out var start);
        var hasEnd = CaseDraft.TryParseDate(doc.TimelineEnd, out var end);

        if (!hasStart)
            issues.Add(Error(IssueCategory.Timeline, string.Empty, "The timeline start date must be a real date in yyyy-MM-dd form."));
        if (!hasEnd)
            issues.Add(Error(IssueCategory.Timeline, string.Empty, "The timeline end date must be a real date in yyyy-MM-dd form."));
        if (!hasStart || !hasEnd)
            return;

        if (end < start)
        {
            issues.Add(Error(IssueCategory.Timeline, string.Empty, "The timeline ends before it starts."));
        }
        else
        {
            var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
            if (months < preset.TimelineMonthsMin || months > preset.TimelineMonthsMax)
                issues.Add(Warning(IssueCategory.Preset, string.Empty,
                    $"The timeline covers {months} months; the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.TimelineMonthsMin, preset.TimelineMonthsMax)} months."));
        }

        var documentDate = isPleading ? doc.FilingDate : doc.Memo?.Date;
        if (CaseDraft.TryParseDate(documentDate, out var filed) && filed < end)
            issues.Add(Warning(IssueCategory.Timeline, string.Empty,
                isPleading ? "The timeline ends after the filing date." : "The timeline ends after the memo date."));

        foreach (var plan in doc.FactPlan)
        {
            var outside = (CaseDraft.TryParseDate(plan.StartDate, out var planStart) && (planStart < start || planStart > end))
                || (CaseDraft.TryParseDate(plan.EndDate, out var planEnd) && (planEnd < start || planEnd > end));

            if (outside)
                issues.Add(Warning(IssueCategory.Timeline, plan.Id,
                    $"The dates for \"{plan.Heading}\" fall outside the case timeline."));
        }
    }

    private static void ValidateCourt(CoreDocument doc, bool isPleading, List<CoreDocumentIssue> issues)
    {
        var jurisdiction = doc.Jurisdiction ?? new Jurisdiction();
        var courtName = jurisdiction.CourtName?.Trim() ?? string.Empty;

        if (courtName.Length == 0)
        {
            if (isPleading)
                issues.Add(Error(IssueCategory.Jurisdiction, string.Empty, "The court name is missing."));

            // The missing-court error covers the contradiction rules
            return;
        }

        // Rule 1: memo forums may be agencies, so only pleadings must name a United States court (N2)
        if (isPleading
            && jurisdiction.CourtSystem == CourtSystem.Federal
            && !courtName.StartsWith("United States", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error(IssueCategory.Jurisdiction, string.Empty, "The court name does not match the federal court system."));
        }

        if (jurisdiction.CourtSystem != CourtSystem.State)
            return;

        // Rule 2
        if (courtName.Contains("United States", StringComparison.OrdinalIgnoreCase))
            issues.Add(Error(IssueCategory.Jurisdiction, string.Empty, "The court name is a federal court but the court system is State."));

        // Rule 3: heuristic only, so a warning; CourtName is not user-editable and must never block Finish (N1)
        var state = jurisdiction.State?.Trim() ?? string.Empty;
        if (state.Length == 0)
            return;

        string? other;
        try
        {
            other = FindOtherState(courtName, state);
        }
        catch (RegexMatchTimeoutException)
        {
            // Heuristic warning only; skip it rather than throw from Validate (WR-04)
            other = null;
        }
        if (other != null)
            issues.Add(Warning(IssueCategory.Jurisdiction, string.Empty, $"The court name mentions {other} but the case is in {state}."));
    }

    private static string? FindOtherState(string courtName, string state)
    {
        var lastComma = courtName.LastIndexOf(',');
        var segment = (lastComma >= 0 ? courtName[(lastComma + 1)..] : courtName).Trim();
        if (segment.Length == 0)
            return null;

        // Ranges where the case's own state appears ("Virginia" inside "West Virginia")
        var ownRanges = new List<(int Start, int End)>();
        var from = 0;
        while (from < segment.Length)
        {
            var index = segment.IndexOf(state, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;
            ownRanges.Add((index, index + state.Length));
            from = index + 1;
        }

        foreach (var (name, pattern) in StateMatchers)
        {
            if (string.Equals(name, state, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (Match m in pattern.Matches(segment))
            {
                var rest = segment[(m.Index + m.Length)..];
                if (rest.StartsWith(" County", StringComparison.OrdinalIgnoreCase)
                    || rest.StartsWith(" Parish", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ownRanges.Any(r => m.Index >= r.Start && m.Index + m.Length <= r.End))
                    continue;

                return name;
            }
        }

        return null;
    }

    private static List<CoreDocumentIssue> ValidateStructure(CoreDocument doc, ComplexityPreset preset)
    {
        var issues = new List<CoreDocumentIssue>();
        var isPleading = CoreDocumentTemplates.IsPleading(doc.DocumentType);

        List<SectionSpec> specs;
        try
        {
            specs = CoreDocumentTemplates.GetSectionSpecs(doc);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Unknown document type from malformed data: report it rather than crash (T-02-15)
            issues.Add(Error(IssueCategory.Structure, string.Empty, "The document type is not recognized."));
            specs = new List<SectionSpec>();
        }

        foreach (var spec in specs)
        {
            if (doc.FindSection(spec.Id) == null)
                issues.Add(Error(IssueCategory.Structure, spec.Id, $"Required section {spec.Heading} is missing."));
        }

        foreach (var section in doc.Sections)
        {
            if (section.Status == SectionStatus.Failed)
                issues.Add(Error(IssueCategory.Structure, section.Id, $"{section.Heading} failed: {section.FailureMessage}"));
            else if (section.Status is SectionStatus.Pending or SectionStatus.Writing)
                issues.Add(Error(IssueCategory.Structure, section.Id, $"{section.Heading} has not been written yet."));
            else if (section.Status == SectionStatus.Ready && section.Paragraphs.Count == 0)
                issues.Add(Error(IssueCategory.Structure, section.Id, $"{section.Heading} is empty. Write it or click Regenerate Section."));
        }

        var allReady = doc.Sections.Count > 0 && doc.Sections.All(s => s.Status == SectionStatus.Ready);
        if (allReady)
        {
            if (isPleading)
            {
                var n = CoreDocumentAssembler.CountNumberedParagraphs(doc);
                if (n < preset.ParagraphsMin || n > preset.ParagraphsMax)
                    issues.Add(Warning(IssueCategory.Preset, string.Empty,
                        $"The document has {n} numbered paragraphs; the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.ParagraphsMin, preset.ParagraphsMax)}."));
            }
            else
            {
                // Memo length target (W4)
                var total = doc.Sections.Sum(s => s.Paragraphs.Count);
                if (total < preset.MemoParagraphsMin || total > preset.MemoParagraphsMax)
                    issues.Add(Warning(IssueCategory.Preset, string.Empty,
                        $"The memo has {total} paragraphs; the {preset.Level} preset expects {ComplexityPreset.FormatRange(preset.MemoParagraphsMin, preset.MemoParagraphsMax)}."));
            }
        }

        if (isPleading)
            ValidateJurisdictionSection(doc, issues);

        return issues;
    }

    private static void ValidateJurisdictionSection(CoreDocument doc, List<CoreDocumentIssue> issues)
    {
        var jurisdiction = doc.Jurisdiction ?? new Jurisdiction();
        var section = doc.Sections.FirstOrDefault(s => s.Kind == SectionKind.JurisdictionVenue);
        var sectionId = section?.Id ?? string.Empty;
        var text = section == null ? string.Empty : string.Join("\n", section.Paragraphs);

        if (jurisdiction.CourtSystem == CourtSystem.Federal)
        {
            if (!text.Contains("1331", StringComparison.Ordinal) && !text.Contains("1332", StringComparison.Ordinal))
                issues.Add(Warning(IssueCategory.Jurisdiction, sectionId,
                    "The jurisdiction section does not state a federal basis (28 U.S.C. § 1331 or § 1332)."));
            return;
        }

        var state = jurisdiction.State?.Trim() ?? string.Empty;
        if (state.Length == 0)
            return;

        var courtName = jurisdiction.CourtName ?? string.Empty;
        if (!courtName.Contains(state, StringComparison.OrdinalIgnoreCase) && !text.Contains(state, StringComparison.OrdinalIgnoreCase))
            issues.Add(Warning(IssueCategory.Jurisdiction, sectionId, $"The court and venue do not mention {state}."));
    }

    private static List<CoreDocumentIssue> ValidateContent(CoreDocument doc, List<EntitySubstitution> substitutions)
    {
        var issues = new List<CoreDocumentIssue>();
        var subs = substitutions ?? new List<EntitySubstitution>();

        var ownPartyNames = doc.Parties
            .SelectMany(p => new[] { p.Name, p.ShortName })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        foreach (var section in doc.Sections)
        {
            var text = string.Join("\n", section.Paragraphs);

            // Regex timeouts must never escape: Validate runs inside UI event handlers (WR-04)
            try
            {
                AddCitations(issues, section.Id, CitationScanner.Scan(text, ownPartyNames));
                AddLeaks(issues, section.Id, NameSubstitution.FindLeaks(text, subs));

                if (ParagraphReference().IsMatch(text))
                    issues.Add(Warning(IssueCategory.Structure, section.Id, "Refers to paragraph numbers, which change when sections are edited."));
            }
            catch (RegexMatchTimeoutException)
            {
                issues.Add(Warning(IssueCategory.Structure, section.Id, TooLargeToScan));
            }
        }

        try
        {
            // Everything rendered outside the sections: caption, title, court, memo header, signature block (WR-02)
            var outlineText = string.Join("\n", OutlineFields(doc).Where(s => !string.IsNullOrWhiteSpace(s)));
            AddLeaks(issues, OutlineSectionId, NameSubstitution.FindLeaks(outlineText, subs));

            // Free-text outline lines that could carry a case citation
            var citationText = string.Join("\n", new[] { doc.Title, doc.Memo?.Re }.Where(s => !string.IsNullOrWhiteSpace(s)));
            AddCitations(issues, OutlineSectionId, CitationScanner.Scan(citationText, ownPartyNames));
        }
        catch (RegexMatchTimeoutException)
        {
            issues.Add(Warning(IssueCategory.Structure, OutlineSectionId, TooLargeToScan));
        }

        return issues;
    }

    internal const string TooLargeToScan = "Section too large to scan for case citations and real names. Shorten it, then check it by hand.";

    private static void AddCitations(List<CoreDocumentIssue> issues, string sectionId, List<CitationFinding> findings)
    {
        foreach (var finding in findings)
        {
            if (finding.Severity == CitationSeverity.Error)
                issues.Add(Error(IssueCategory.Citation, sectionId, $"Case citation: \"{finding.Text}\""));
            else
                issues.Add(Warning(IssueCategory.Citation, sectionId, $"Possible case name or citation phrase: \"{finding.Text}\""));
        }
    }

    private static IEnumerable<string?> OutlineFields(CoreDocument doc)
    {
        foreach (var party in doc.Parties)
        {
            yield return party.Name;
            yield return party.ShortName;
        }

        yield return doc.Title;

        var jurisdiction = doc.Jurisdiction;
        yield return jurisdiction?.CourtName;
        yield return jurisdiction?.District;
        yield return jurisdiction?.VenueCity;

        var counsel = doc.Counsel;
        yield return counsel?.AttorneyName;
        yield return counsel?.FirmName;
        yield return counsel?.BarNumber;
        yield return counsel?.Address;
        yield return counsel?.Email;

        var memo = doc.Memo;
        yield return memo?.To;
        yield return memo?.From;
        yield return memo?.Cc;
        yield return memo?.Re;
        yield return memo?.Organization;
    }

    private static void AddLeaks(List<CoreDocumentIssue> issues, string sectionId, List<string> leaks)
    {
        foreach (var name in leaks)
        {
            // A lone surname or short name may be an ordinary word (I2), so it only warns
            if (name.Any(char.IsWhiteSpace))
                issues.Add(Error(IssueCategory.RealName, sectionId, $"Real name \"{name}\" appears in this section."));
            else
                issues.Add(Warning(IssueCategory.RealName, sectionId, $"Possible real name \"{name}\" appears in this section."));
        }
    }

    private static CoreDocumentIssue Error(IssueCategory category, string sectionId, string message) => new()
    {
        Severity = IssueSeverity.Error,
        Category = category,
        SectionId = sectionId,
        Message = message
    };

    private static CoreDocumentIssue Warning(IssueCategory category, string sectionId, string message) => new()
    {
        Severity = IssueSeverity.Warning,
        Category = category,
        SectionId = sectionId,
        Message = message
    };
}
