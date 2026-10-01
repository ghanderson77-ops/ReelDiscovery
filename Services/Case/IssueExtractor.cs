using System.Text;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Deterministic issue list from the core document's counts/findings (GUIDE-01, D-08).
/// Existing issue ids and details survive re-extraction and reordering.
/// </summary>
public static class IssueExtractor
{
    /// <summary>
    /// Builds a new issue list for the document's counts. Does not assign <see cref="CaseGuide.Issues"/>;
    /// the caller does. New issues get ids from <see cref="IdAllocator"/>.
    /// </summary>
    public static List<CaseIssue> Extract(CoreDocument doc, CaseGuide guide)
    {
        var result = new List<CaseIssue>();
        var counts = doc.Counts?.Where(c => c != null).ToList() ?? new List<CaseCount>();
        var existing = guide.Issues?.Where(i => i != null).ToList() ?? new List<CaseIssue>();
        var claimed = new HashSet<CaseIssue>();

        // Pass 1 and 2 claim matches by title first, so a retitled count can never steal an issue
        // whose title moved to another count.
        var matches = new CaseIssue?[counts.Count];
        var keepDetails = new bool[counts.Count];

        // (1) same CountId and same title
        for (var i = 0; i < counts.Count; i++)
        {
            var title = NormalizeTitle(counts[i].Title);
            var match = existing.FirstOrDefault(x => !claimed.Contains(x)
                && x.CountId == counts[i].Id
                && NormalizeTitle(x.Title) == title);
            if (match != null)
            {
                matches[i] = match;
                keepDetails[i] = true;
                claimed.Add(match);
            }
        }

        // (2) same title anywhere (reordered counts)
        for (var i = 0; i < counts.Count; i++)
        {
            if (matches[i] != null)
                continue;

            var title = NormalizeTitle(counts[i].Title);
            var match = existing.FirstOrDefault(x => !claimed.Contains(x) && NormalizeTitle(x.Title) == title);
            if (match != null)
            {
                matches[i] = match;
                keepDetails[i] = true;
                claimed.Add(match);
            }
        }

        // (3) same CountId, retitled: keep the id, drop details that described the old title
        for (var i = 0; i < counts.Count; i++)
        {
            if (matches[i] != null)
                continue;

            var match = existing.FirstOrDefault(x => !claimed.Contains(x)
                && !string.IsNullOrEmpty(x.CountId)
                && x.CountId == counts[i].Id);
            if (match != null)
            {
                matches[i] = match;
                claimed.Add(match);
            }
        }

        for (var i = 0; i < counts.Count; i++)
        {
            var count = counts[i];
            var old = matches[i];
            var issue = new CaseIssue
            {
                Id = old?.Id ?? IdAllocator.Next(guide, IdAllocator.Issue),
                CountId = count.Id ?? string.Empty,
                Number = count.Number,
                Title = count.Title ?? string.Empty,
                LegalBasis = count.LegalBasis ?? string.Empty,
                Summary = count.Summary ?? string.Empty,
                AgainstPartyIds = count.AgainstPartyIds?.Where(x => x != null).ToList() ?? new List<string>()
            };

            if (old != null && keepDetails[i])
            {
                issue.Description = old.Description ?? string.Empty;
                issue.Elements = old.Elements?.Where(x => x != null).ToList() ?? new List<string>();
                issue.RelevanceCriteria = old.RelevanceCriteria ?? string.Empty;
            }

            result.Add(issue);
        }

        // Unclaimed old issues are dropped; facts that cite them become validator orphans
        return result;
    }

    /// <summary>
    /// "Count II" for pleadings, "Finding 2" for investigation memos.
    /// </summary>
    public static string SourceLabel(CoreDocument doc, CaseIssue issue)
    {
        if (doc.DocumentType == CoreDocumentType.InvestigationMemo)
            return $"Finding {issue.Number}";

        return issue.Number is >= 1 and <= 3999
            ? $"Count {CoreDocumentAssembler.ToRoman(issue.Number)}"
            : $"Count {issue.Number}";
    }

    /// <summary>
    /// Lowercase, punctuation stripped, whitespace collapsed; used to match issues to counts.
    /// </summary>
    internal static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var sb = new StringBuilder(title.Length);
        var pendingSpace = false;
        foreach (var ch in title)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }
}
