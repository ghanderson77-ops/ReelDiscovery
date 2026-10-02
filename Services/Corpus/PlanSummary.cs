using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Emails sent in one calendar month (sender-local date), split by relevance.
/// </summary>
public class MonthBucket
{
    // First day of the month
    public DateOnly Month { get; set; }
    public int Relevant { get; set; }
    public int NonRelevant { get; set; }
    public int Total => Relevant + NonRelevant;
}

public class IssueCountRow
{
    public string IssueId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Relevant { get; set; }

    // Near-miss decoys seeded from this issue (D-14)
    public int NotRelevant { get; set; }
    public int Total { get; set; }
}

public class FactCountRow
{
    public string FactId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Threads { get; set; }
    public int Emails { get; set; }
    public int Hot { get; set; }
}

public class CustodianCountRow
{
    public string CastId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public int Relevant { get; set; }
    public int NotRelevant { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Pure projection of a corpus plan for the Review Plan tiles, month histogram and count tables (D-22, PLAN-07).
/// </summary>
public class PlanSummary
{
    // Window spans beyond this are treated as typos; the histogram follows the messages instead
    private const int MaxWindowMonths = 600;

    public int Emails { get; set; }
    public int Threads { get; set; }
    public int Custodians { get; set; }
    public int Relevant { get; set; }
    public int NonRelevant { get; set; }

    /// <summary>
    /// Relevant share of emails in percent, one decimal (AwayFromZero); 0 for an empty plan.
    /// </summary>
    public decimal RelevantPercent { get; set; }

    // IsHot emails in relevant threads
    public int HotEmails { get; set; }

    // Facts marked Hot in the case guide
    public int HotFacts { get; set; }

    public int PrivilegedThreads { get; set; }
    public int PrivilegedEmails { get; set; }
    public int Attachments { get; set; }
    public int EmailsWithAttachments { get; set; }

    /// <summary>
    /// Share of emails carrying at least one attachment, in percent, one decimal.
    /// </summary>
    public decimal AttachmentPercent { get; set; }

    public Dictionary<AttachmentType, int> AttachmentsByType { get; set; } = new();
    public List<MonthBucket> Months { get; set; } = new();
    public List<IssueCountRow> ByIssue { get; set; } = new();
    public List<FactCountRow> ByFact { get; set; } = new();
    public List<CustodianCountRow> ByCustodian { get; set; } = new();

    public static PlanSummary From(CorpusPlan plan, CaseGuide guide)
    {
        var summary = new PlanSummary();
        var threads = (plan?.Threads ?? new List<PlannedThread>()).Where(t => t != null).ToList();
        var cast = (guide?.Cast ?? new List<CastMember>()).Where(c => c != null && !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());

        var entries = threads
            .SelectMany(t => (t.Messages ?? new List<PlannedMessage>()).Where(m => m != null).Select(m => (Thread: t, Message: m)))
            .ToList();

        summary.Threads = threads.Count;
        summary.Emails = entries.Count;
        summary.Relevant = entries.Count(e => IsRelevant(e.Thread));
        summary.NonRelevant = summary.Emails - summary.Relevant;
        summary.RelevantPercent = Percent(summary.Relevant, summary.Emails);
        summary.HotEmails = entries.Count(e => IsRelevant(e.Thread) && e.Message.IsHot);
        summary.HotFacts = (guide?.Facts ?? new List<KeyFact>()).Count(f => f != null && f.Significance == FactSignificance.Hot);
        summary.PrivilegedThreads = threads.Count(t => t.IsPrivileged);
        summary.PrivilegedEmails = entries.Count(e => e.Thread.IsPrivileged);

        foreach (var (_, message) in entries)
        {
            var attachments = (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null).ToList();
            if (attachments.Count > 0)
                summary.EmailsWithAttachments++;
            foreach (var attachment in attachments)
            {
                summary.Attachments++;
                summary.AttachmentsByType[attachment.Kind] = summary.AttachmentsByType.GetValueOrDefault(attachment.Kind) + 1;
            }
        }

        summary.AttachmentPercent = Percent(summary.EmailsWithAttachments, summary.Emails);

        var custodianIds = entries.SelectMany(e => Ids(e.Message.CustodianIds)).Distinct();
        summary.Custodians = cast.Count > 0 ? custodianIds.Count(cast.ContainsKey) : custodianIds.Count();

        summary.Months = BuildMonths(plan?.Settings, entries);
        summary.ByIssue = BuildIssues(guide, threads, entries);
        summary.ByFact = BuildFacts(guide, threads, entries);
        summary.ByCustodian = BuildCustodians(guide, cast, entries);
        return summary;
    }

    private static bool IsRelevant(PlannedThread thread) => thread.Relevance == ThreadRelevance.Relevant;

    private static IEnumerable<string> Ids(List<string>? ids) =>
        (ids ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id));

    private static decimal Percent(int part, int whole) =>
        whole == 0 ? 0m : Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero);

    private static DateOnly MonthOf(PlannedMessage message)
    {
        var tz = BusinessCalendar.ResolveId(message.TimeZoneId);
        var day = tz == null ? DateOnly.FromDateTime(message.SentAt.DateTime) : BusinessCalendar.LocalDate(message.SentAt, tz);
        return new DateOnly(day.Year, day.Month, 1);
    }

    private static int MonthIndex(DateOnly month) => month.Year * 12 + month.Month - 1;

    private static List<MonthBucket> BuildMonths(CorpusSettings? settings, List<(PlannedThread Thread, PlannedMessage Message)> entries)
    {
        var counts = new Dictionary<DateOnly, MonthBucket>();
        foreach (var (thread, message) in entries)
        {
            var month = MonthOf(message);
            if (!counts.TryGetValue(month, out var bucket))
                counts[month] = bucket = new MonthBucket { Month = month };
            if (IsRelevant(thread))
                bucket.Relevant++;
            else
                bucket.NonRelevant++;
        }

        DateOnly? first = counts.Count > 0 ? counts.Keys.Min() : null;
        DateOnly? last = counts.Count > 0 ? counts.Keys.Max() : null;

        // The window from the settings, widened to every planned message so bucket sums equal the email count
        DateOnly? windowStart = CaseDraft.TryParseDate(settings?.WindowStart, out var ws) ? new DateOnly(ws.Year, ws.Month, 1) : null;
        DateOnly? windowEnd = CaseDraft.TryParseDate(settings?.WindowEnd, out var we) ? new DateOnly(we.Year, we.Month, 1) : null;
        if (windowStart != null && windowEnd != null && MonthIndex(windowEnd.Value) - MonthIndex(windowStart.Value) > MaxWindowMonths)
            windowStart = windowEnd = null;

        if (windowStart != null && (first == null || windowStart < first))
            first = windowStart;
        if (windowEnd != null && (last == null || windowEnd > last))
            last = windowEnd;

        if (first == null || last == null || last < first || MonthIndex(last.Value) - MonthIndex(first.Value) > MaxWindowMonths)
        {
            // Nothing sensible to span: list the populated months only
            return counts.Values.OrderBy(b => b.Month).ToList();
        }

        var months = new List<MonthBucket>();
        for (var month = first.Value; month <= last.Value; month = month.AddMonths(1))
            months.Add(counts.TryGetValue(month, out var bucket) ? bucket : new MonthBucket { Month = month });
        return months;
    }

    private static List<IssueCountRow> BuildIssues(CaseGuide? guide, List<PlannedThread> threads,
        List<(PlannedThread Thread, PlannedMessage Message)> entries)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var issue in (guide?.Issues ?? new List<CaseIssue>()).Where(i => i != null && !string.IsNullOrEmpty(i.Id)))
            titles.TryAdd(issue.Id, issue.Title ?? string.Empty);
        foreach (var id in threads.SelectMany(t => Ids(t.IssueIds)))
            titles.TryAdd(id, string.Empty);

        var rows = titles.Keys.ToDictionary(id => id, id => new IssueCountRow { IssueId = id, Title = titles[id] });
        foreach (var (thread, _) in entries)
        {
            foreach (var id in Ids(thread.IssueIds).Distinct())
            {
                if (IsRelevant(thread))
                    rows[id].Relevant++;
                else
                    rows[id].NotRelevant++;
            }
        }

        foreach (var row in rows.Values)
            row.Total = row.Relevant + row.NotRelevant;

        return rows.Values.OrderBy(r => r.IssueId, StringComparer.Ordinal).ToList();
    }

    private static List<FactCountRow> BuildFacts(CaseGuide? guide, List<PlannedThread> threads,
        List<(PlannedThread Thread, PlannedMessage Message)> entries)
    {
        var rows = new List<(DateTime Date, FactCountRow Row)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in (guide?.Facts ?? new List<KeyFact>()).Where(f => f != null && !string.IsNullOrEmpty(f.Id)))
        {
            if (!CaseDraft.TryParseDate(fact.Date, out var date) || !seen.Add(fact.Id))
                continue;

            var row = new FactCountRow { FactId = fact.Id, Date = fact.Date, Title = fact.Title ?? string.Empty };
            row.Threads = threads.Count(t => IsRelevant(t) && Ids(t.FactIds).Contains(fact.Id));
            foreach (var (thread, message) in entries)
            {
                if (!IsRelevant(thread) || !Ids(message.FactIds).Contains(fact.Id))
                    continue;
                row.Emails++;
                if (message.IsHot)
                    row.Hot++;
            }

            rows.Add((date, row));
        }

        return rows.OrderBy(r => r.Date).ThenBy(r => r.Row.FactId, StringComparer.Ordinal).Select(r => r.Row).ToList();
    }

    private static List<CustodianCountRow> BuildCustodians(CaseGuide? guide, Dictionary<string, CastMember> cast,
        List<(PlannedThread Thread, PlannedMessage Message)> entries)
    {
        var rows = new Dictionary<string, CustodianCountRow>(StringComparer.Ordinal);
        foreach (var (thread, message) in entries)
        {
            var id = RowCustodian(message, cast);
            if (id == null)
                continue;

            if (!rows.TryGetValue(id, out var row))
            {
                var member = cast[id];
                rows[id] = row = new CustodianCountRow
                {
                    CastId = id,
                    Name = member.FullName,
                    Email = member.Email ?? string.Empty,
                    Organization = guide?.FindOrganization(member.OrganizationId)?.Name ?? string.Empty
                };
            }

            if (IsRelevant(thread))
                row.Relevant++;
            else
                row.NotRelevant++;
        }

        foreach (var row in rows.Values)
            row.Total = row.Relevant + row.NotRelevant;

        return rows.Values
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CastId, StringComparer.Ordinal)
            .ToList();
    }

    // The sender when it is a cast member, else the first cast recipient (To, then CC)
    private static string? RowCustodian(PlannedMessage message, Dictionary<string, CastMember> cast)
    {
        if (!string.IsNullOrEmpty(message.FromId) && cast.ContainsKey(message.FromId))
            return message.FromId;
        return Ids(message.ToIds).Concat(Ids(message.CcIds)).FirstOrDefault(cast.ContainsKey);
    }
}
