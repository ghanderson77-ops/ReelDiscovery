using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using static ReelDiscovery.Services.Case.CaseGuideDocxExporter;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// The internal "story so far" document (CORP-11, D-10): the narrative ledger grouped by month, plus the threads kept with
/// warnings. Never part of the produced corpus.
/// </summary>
public static class StorySoFarDocxBuilder
{
    public const string FileNameSuffix = " - Story So Far.docx";

    private const string Dot = " · ";

    // Month headings and entry dates are English whatever the Windows culture
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static string FileNameFor(string folderName) => $"{folderName}{FileNameSuffix}";

    /// <summary>
    /// True for a bare story file name ("... - Story So Far.docx"): no folder part, no "..", no invalid characters.
    /// Guards a name read from case.json before it is combined with the case folder (T-05-33).
    /// </summary>
    public static bool IsStoryFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length <= FileNameSuffix.Length)
            return false;
        if (!name.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return Path.GetFileName(name) == name && !Path.IsPathRooted(name);
    }

    /// <summary>
    /// Deterministic, validator-clean .docx bytes. Text passes through WordBuilder, which strips invalid XML characters.
    /// </summary>
    public static byte[] Build(CaseDraft draft, CorpusPlan plan, NarrativeLedger ledger, GenerationRunState state,
        PeopleDirectory people)
    {
        var title = $"{CaseFolderNaming.CaseTitle(draft)} - Story So Far";
        var word = new WordBuilder();
        word.Paragraph(title, new ParaFormat { Style = "Heading1" });

        var entries = (ledger?.Entries ?? new List<LedgerEntry>())
            .Where(e => e != null && e.ParsedDate.HasValue && !string.IsNullOrWhiteSpace(e.Statement))
            .OrderBy(e => e.ParsedDate!.Value)
            .ThenBy(e => e.Id ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
        {
            word.Paragraph("No story details yet.");
        }
        else
        {
            // The ledger is built from relevant threads only (D-13)
            var threads = state.RelevantWritten + state.RelevantWarnings;
            var first = entries[0].ParsedDate!.Value;
            var last = entries[^1].ParsedDate!.Value;
            word.Paragraph($"Internal narrative ledger written by ReelDiscovery from {Plural(threads, "generated thread")}, " +
                           $"{ShortDate(first)} to {ShortDate(last)}. Not part of the produced corpus.");

            foreach (var month in entries.GroupBy(e => (e.ParsedDate!.Value.Year, e.ParsedDate!.Value.Month)))
            {
                var heading = new DateOnly(month.Key.Year, month.Key.Month, 1).ToString("MMMM yyyy", English);
                word.Paragraph(heading, new ParaFormat { Style = "Heading2" });

                foreach (var entry in month)
                {
                    var value = StripInvalidXmlChars(entry.Value).Trim();
                    var suffix = value.Length > 0 ? $" ({value})" : string.Empty;
                    word.Paragraph(entry.Statement.Trim() + suffix,
                        new ParaFormat { BoldLead = $"{ShortDate(entry.ParsedDate!.Value)}{Dot}{entry.Kind} " });

                    var detail = DetailLine(entry, people);
                    if (detail.Length > 0)
                        word.Paragraph(detail, new ParaFormat { SizeHalfPoints = 18 });
                }
            }
        }

        var warned = state.StatusByThread
            .Where(p => p.Value == ThreadRunStatus.Warnings)
            .Select(p => p.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (warned.Count > 0)
        {
            var subjects = (plan?.Threads ?? new List<PlannedThread>())
                .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
                .GroupBy(t => t.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (g.First().Subject ?? string.Empty).Trim(), StringComparer.Ordinal);

            word.Paragraph("Threads kept with warnings", new ParaFormat { Style = "Heading2" });
            foreach (var threadId in warned)
            {
                var subject = subjects.TryGetValue(threadId, out var s) ? s : string.Empty;
                var violations = state.Records.TryGetValue(threadId, out var record)
                    ? string.Join("; ", record.Violations.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()))
                    : string.Empty;
                word.Paragraph(violations, new ParaFormat { BoldLead = $"{threadId}{Dot}{subject} " });
            }
        }

        return word.Save(title, landscape: false);
    }

    // "Known to {names} · Facts {ids} · Issues {ids} · Source {DocID}"; empty clauses dropped
    private static string DetailLine(LedgerEntry entry, PeopleDirectory people)
    {
        var clauses = new List<string>();
        var names = entry.KnownByIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => people.NameOf(id.Trim())).ToList();
        if (names.Count > 0)
            clauses.Add("Known to " + string.Join(", ", names));
        var facts = entry.FactIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (facts.Count > 0)
            clauses.Add("Facts " + string.Join(", ", facts));
        var issues = entry.IssueIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (issues.Count > 0)
            clauses.Add("Issues " + string.Join(", ", issues));
        if (!string.IsNullOrWhiteSpace(entry.SourceDocId))
            clauses.Add("Source " + entry.SourceDocId.Trim());
        return string.Join(Dot, clauses);
    }

    private static string ShortDate(DateOnly date) => date.ToString("MMM d, yyyy", English);

    private static string Plural(int n, string singular) =>
        n == 1 ? $"1 {singular}" : $"{n.ToString("N0", English)} {singular}s";
}
