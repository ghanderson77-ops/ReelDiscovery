using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Pure projection of the plan (KEY-01..03, D-27..D-32); the CSV/XLSX writers serialize these rows.
/// </summary>
public static class AnswerKeyBuilder
{
    public const string StatusPlanned = "Planned";
    public const string StatusGenerated = "Generated";
    public const string StatusGeneratedWithWarnings = "Generated - with warnings";
    public const string StatusFailed = "Failed";
    public const string StatusSkipped = "Skipped";
    public const string DocTypeEmail = "Email";
    public const string DocTypeAttachment = "Attachment";
    public const string RelevantLabel = "Relevant";
    public const string NotRelevantLabel = "Not Relevant";
    public const string DateFormat = "yyyy-MM-dd HH:mm zzz";

    private const string Yes = "Y";
    private const string No = "N";
    private const string Separator = "; ";

    /// <summary>
    /// One row per planned email and per attachment, sorted by DocID (ordinal); never mutates the plan or the guide.
    /// The optional overlay sets Status and Subject on email rows; attachment rows copy the parent subject but stay Planned.
    /// </summary>
    public static List<AnswerKeyRow> Build(CorpusPlan plan, CaseGuide guide, AnswerKeyOverlay? overlay = null)
    {
        var lookups = new Lookups(plan, guide);
        var rows = new List<AnswerKeyRow>();

        foreach (var thread in plan.Threads)
        {
            var messages = thread.Messages.OrderBy(m => m.Seq).ToList();
            var bySeq = new Dictionary<int, PlannedMessage>();
            foreach (var message in messages)
                bySeq.TryAdd(message.Seq, message);

            var threadRationale = ThreadRationale(thread, lookups);

            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                var row = EmailRow(thread, message, lookups);

                if (thread.Relevance == ThreadRelevance.Relevant)
                {
                    row.Relevance = RelevantLabel;
                    var isRoot = message.ReplyToSeq <= 0 || i == 0;
                    if (message.FactIds.Count > 0 || isRoot)
                    {
                        // Own evidence: codes from the message's facts (thread codes when it names none)
                        var factIds = message.FactIds.Count > 0 ? message.FactIds : thread.FactIds;
                        row.FactIds = Join(factIds);
                        row.IssueCodes = Join(IssuesOf(factIds, thread, lookups));
                        row.Rationale = !string.IsNullOrWhiteSpace(thread.Rationale)
                            ? thread.Rationale.Trim()
                            : EvidenceRationale(factIds, thread, lookups);
                    }
                    else
                    {
                        // Quoted: family-level coding inherited from the thread
                        var parent = bySeq.TryGetValue(message.ReplyToSeq, out var p) && p != message ? p : messages[i - 1];
                        row.FactIds = Join(thread.FactIds);
                        row.IssueCodes = Join(thread.IssueIds);
                        row.Rationale = $"Quotes {parent.DocId}, which is relevant: {threadRationale}";
                    }
                }
                else
                {
                    row.Relevance = NotRelevantLabel;
                    row.NonRelevantCategory = CategoryLabel(thread.Category);
                    row.NearMiss = thread.Category == NonRelevantCategory.Decoy ? Yes : No;
                    row.Rationale = NonRelevantRationale(thread);
                }

                // Run state (D-05): applied before attachment rows so they copy the rendered subject
                if (overlay != null && overlay.ByDocId.TryGetValue(row.DocId, out var o) && o != null)
                {
                    row.Status = string.IsNullOrWhiteSpace(o.Status) ? StatusPlanned : o.Status;
                    if (!string.IsNullOrWhiteSpace(o.Subject))
                        row.Subject = o.Subject;
                }

                rows.Add(row);

                foreach (var attachment in message.Attachments)
                    rows.Add(AttachmentRow(thread, row, attachment));
            }
        }

        return rows.OrderBy(r => r.DocId, StringComparer.Ordinal).ToList();
    }

    public static string CategoryLabel(NonRelevantCategory category) => category switch
    {
        NonRelevantCategory.DailyBusiness => "Daily business",
        NonRelevantCategory.Ancillary => "Ancillary",
        NonRelevantCategory.NewsletterSpamPersonal => "Newsletter/spam/personal",
        NonRelevantCategory.Decoy => "Decoy",
        _ => string.Empty
    };

    private static AnswerKeyRow EmailRow(PlannedThread thread, PlannedMessage message, Lookups lookups)
    {
        var custodianId = message.CustodianIds.FirstOrDefault() ?? string.Empty;
        return new AnswerKeyRow
        {
            DocId = message.DocId,
            DocType = DocTypeEmail,
            FilePath = message.FilePath,
            FileName = $"{message.DocId}.eml",
            Custodian = custodianId.Length == 0 ? string.Empty : lookups.NameOf(custodianId),
            AllCustodians = string.Join(Separator, message.CustodianIds.Select(lookups.NameOf)),
            DateSent = message.SentAt.ToString(DateFormat, CultureInfo.InvariantCulture),
            From = lookups.Display(message.FromId),
            To = string.Join(Separator, message.ToIds.Select(lookups.Display)),
            Cc = string.Join(Separator, message.CcIds.Select(lookups.Display)),
            Subject = SubjectOf(thread, message),
            ThreadId = thread.Id,
            FamilyId = message.DocId,
            ParentDocId = string.Empty,
            Hot = thread.Relevance == ThreadRelevance.Relevant && message.IsHot ? Yes : No,
            Privileged = thread.IsPrivileged ? Yes : No,
            NearMiss = No,
            Status = StatusPlanned
        };
    }

    // Attachments live inside the parent EML: same path, dates and people; relevance and codes follow the family (D-31)
    private static AnswerKeyRow AttachmentRow(PlannedThread thread, AnswerKeyRow parent, PlannedAttachment attachment)
    {
        var relevant = thread.Relevance == ThreadRelevance.Relevant;
        return new AnswerKeyRow
        {
            DocId = attachment.DocId,
            DocType = DocTypeAttachment,
            FilePath = parent.FilePath,
            FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? attachment.DocId : attachment.FileName.Trim(),
            Custodian = parent.Custodian,
            AllCustodians = parent.AllCustodians,
            DateSent = parent.DateSent,
            From = parent.From,
            To = parent.To,
            Cc = parent.Cc,
            Subject = parent.Subject,
            ThreadId = parent.ThreadId,
            FamilyId = parent.DocId,
            ParentDocId = parent.DocId,
            Relevance = parent.Relevance,
            IssueCodes = relevant ? Join(thread.IssueIds) : string.Empty,
            FactIds = relevant ? Join(thread.FactIds) : string.Empty,
            Hot = parent.Hot,
            Privileged = parent.Privileged,
            NearMiss = parent.NearMiss,
            NonRelevantCategory = parent.NonRelevantCategory,
            Rationale = relevant
                ? $"Attachment to relevant email {parent.DocId}; family coded Relevant."
                : $"Attachment to non-relevant email {parent.DocId}; family coded Not Relevant.",
            Status = StatusPlanned
        };
    }

    // Prefix once from the root subject (no cumulative RE: RE:)
    private static string SubjectOf(PlannedThread thread, PlannedMessage message)
    {
        var subject = thread.Subject.Trim();
        return message.Kind switch
        {
            MessageKind.Reply or MessageKind.ReplyAll => ThreadingHelper.AddReplyPrefix(subject),
            MessageKind.Forward => ThreadingHelper.AddForwardPrefix(subject),
            _ => subject
        };
    }

    private static string ThreadRationale(PlannedThread thread, Lookups lookups) =>
        !string.IsNullOrWhiteSpace(thread.Rationale)
            ? thread.Rationale.Trim()
            : EvidenceRationale(thread.FactIds, thread, lookups);

    private static string EvidenceRationale(List<string> factIds, PlannedThread thread, Lookups lookups)
    {
        var fact = factIds.Select(lookups.Fact).FirstOrDefault(f => f != null);
        var issueId = IssuesOf(factIds, thread, lookups).FirstOrDefault();
        var issue = lookups.Issue(issueId);

        if (fact != null && issue != null)
            return $"Evidence of {fact.Title.Trim()} ({fact.Id}) under {issue.Title.Trim()} ({issue.Id}).";
        if (fact != null)
            return $"Evidence of {fact.Title.Trim()} ({fact.Id}).";
        if (issue != null)
            return $"Evidence under {issue.Title.Trim()} ({issue.Id}).";
        return "Evidence of the conduct alleged in this matter.";
    }

    // Issue ids of the facts (first appearance order); the thread's issues when the facts carry none
    private static List<string> IssuesOf(List<string> factIds, PlannedThread thread, Lookups lookups)
    {
        var issues = factIds
            .Select(lookups.Fact)
            .Where(f => f != null)
            .SelectMany(f => f!.IssueIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return issues.Count > 0 ? issues : thread.IssueIds.ToList();
    }

    private static string NonRelevantRationale(PlannedThread thread) => thread.Category switch
    {
        NonRelevantCategory.DailyBusiness => "Routine business correspondence unrelated to the issues in this matter.",
        NonRelevantCategory.Ancillary => "Administrative staff correspondence unrelated to the issues in this matter.",
        NonRelevantCategory.NewsletterSpamPersonal => "Newsletter, promotional or personal email unrelated to the issues in this matter.",
        NonRelevantCategory.Decoy => string.IsNullOrWhiteSpace(thread.DecoyAngle)
            ? "Near-miss: shares project names and people with the matter but is not about the alleged conduct."
            : $"Near-miss: concerns {thread.DecoyAngle.Trim().TrimEnd('.')}, not the alleged conduct.",
        _ => "Correspondence unrelated to the issues in this matter."
    };

    private static string Join(IEnumerable<string> values) =>
        string.Join(Separator, values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal));

    private class Lookups
    {
        private readonly Dictionary<string, CastMember> _cast = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExternalParty> _externals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, KeyFact> _facts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CaseIssue> _issues = new(StringComparer.Ordinal);

        public Lookups(CorpusPlan plan, CaseGuide guide)
        {
            // First entry wins on duplicate ids
            foreach (var member in guide.Cast.Where(c => c != null && !string.IsNullOrEmpty(c.Id)))
                _cast.TryAdd(member.Id, member);
            foreach (var external in plan.Externals.Where(e => e != null && !string.IsNullOrEmpty(e.Id)))
                _externals.TryAdd(external.Id, external);
            foreach (var fact in guide.Facts.Where(f => f != null && !string.IsNullOrEmpty(f.Id)))
                _facts.TryAdd(fact.Id, fact);
            foreach (var issue in guide.Issues.Where(i => i != null && !string.IsNullOrEmpty(i.Id)))
                _issues.TryAdd(issue.Id, issue);
        }

        public KeyFact? Fact(string? id) => id != null && _facts.TryGetValue(id, out var fact) ? fact : null;

        public CaseIssue? Issue(string? id) => id != null && _issues.TryGetValue(id, out var issue) ? issue : null;

        public string NameOf(string id)
        {
            if (_cast.TryGetValue(id, out var member) && member.FullName.Length > 0)
                return member.FullName;
            if (_externals.TryGetValue(id, out var external) && !string.IsNullOrWhiteSpace(external.Name))
                return external.Name.Trim();
            return id;
        }

        // "Name <email>"; the id when the person is unknown
        public string Display(string id)
        {
            string name;
            string email;
            if (_cast.TryGetValue(id, out var member))
            {
                name = member.FullName;
                email = member.Email?.Trim() ?? string.Empty;
            }
            else if (_externals.TryGetValue(id, out var external))
            {
                name = external.Name?.Trim() ?? string.Empty;
                email = external.Email?.Trim() ?? string.Empty;
            }
            else
            {
                return id;
            }

            if (name.Length == 0)
                return email.Length == 0 ? id : email;
            return email.Length == 0 ? name : $"{name} <{email}>";
        }
    }
}

public class AnswerKeyOverlayEntry
{
    public string Status { get; set; } = AnswerKeyBuilder.StatusPlanned;
    public string Subject { get; set; } = string.Empty;
}

/// <summary>
/// Run-state projection onto the planned key (D-05): status and rendered subject per email DocID.
/// </summary>
public class AnswerKeyOverlay
{
    public Dictionary<string, AnswerKeyOverlayEntry> ByDocId { get; set; } = new(StringComparer.Ordinal);
}
