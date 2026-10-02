using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class AnswerKeyBuilderTests
{
    private const string Rationale = "Lisa Chen approves Change Order 7 in writing, the approval Harmon later denies.";

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        new(2024, month, day, hour, minute, 0, TimeSpan.FromHours(-5));

    private static PlannedMessage Message(int seq, string fromId, DateTimeOffset sentAt, string[] to, string[]? cc = null,
        MessageKind kind = MessageKind.New, int replyToSeq = 0, string[]? factIds = null, bool hot = false) => new()
    {
        Seq = seq,
        ReplyToSeq = replyToSeq,
        Kind = kind,
        FromId = fromId,
        ToIds = to.ToList(),
        CcIds = (cc ?? Array.Empty<string>()).ToList(),
        SentAt = sentAt,
        FactIds = (factIds ?? Array.Empty<string>()).ToList(),
        IsHot = hot
    };

    private static PlannedThread NonRelevant(NonRelevantCategory category, string subject, PlannedMessage message, string decoyAngle = "")
    {
        var thread = new PlannedThread
        {
            Relevance = ThreadRelevance.NotRelevant,
            Category = category,
            Subject = subject,
            DecoyAngle = decoyAngle
        };
        thread.Messages.Add(message);
        return thread;
    }

    /// <summary>
    /// A relevant thread (hot root with an attachment, a quoted reply, a forward), a privileged relevant thread with a blank
    /// rationale, and one non-relevant thread per category plus an angle-less decoy; numbered by PlanNumbering.
    /// </summary>
    private static (CaseDraft Draft, CorpusPlan Plan) BuildPlan()
    {
        var draft = CorpusTestData.RichDraft();
        var plan = new CorpusPlan { DocIdPrefix = "HB" };
        plan.Externals.Add(new ExternalParty { Id = "EXT-001", Name = "Dunder Digest", Email = "news@dunderdigest.com", Domain = "dunderdigest.com" });

        var relevant = new PlannedThread
        {
            Id = "A",
            Relevance = ThreadRelevance.Relevant,
            IssueIds = new List<string> { "ISS-01" },
            FactIds = new List<string> { "F-003" },
            Subject = "Change Order 7 sign-off",
            Rationale = Rationale
        };
        relevant.Messages.Add(Message(1, "CH-004", At(3, 18, 10, 14), new[] { "CH-002" }, new[] { "CH-003" },
            factIds: new[] { "F-003" }, hot: true));
        relevant.Messages[0].Attachments.Add(new PlannedAttachment { FileName = "CO-007 Approval.docx", Kind = AttachmentType.Word });
        relevant.Messages.Add(Message(2, "CH-002", At(3, 18, 11), new[] { "CH-004" }, kind: MessageKind.Reply, replyToSeq: 1));
        relevant.Messages.Add(Message(3, "CH-002", At(3, 18, 12), new[] { "CH-001" }, kind: MessageKind.Forward, replyToSeq: 2));

        var privileged = new PlannedThread
        {
            Id = "B",
            Relevance = ThreadRelevance.Relevant,
            IsPrivileged = true,
            IssueIds = new List<string> { "ISS-02" },
            FactIds = new List<string> { "F-007" },
            Subject = "Late payment notice",
            Rationale = ""
        };
        privileged.Messages.Add(Message(1, "CH-001", At(6, 3, 9), new[] { "CH-005" }, factIds: new[] { "F-007" }));
        privileged.Messages[0].Attachments.Add(new PlannedAttachment { FileName = "Notice.pdf" });
        privileged.Messages.Add(Message(2, "CH-005", At(6, 3, 10), new[] { "CH-001" }, kind: MessageKind.Reply, replyToSeq: 1));

        plan.Threads.Add(relevant);
        plan.Threads.Add(privileged);
        plan.Threads.Add(NonRelevant(NonRelevantCategory.DailyBusiness, "Crane schedule", Message(1, "CH-002", At(4, 1, 9), new[] { "CH-009" })));
        plan.Threads.Add(NonRelevant(NonRelevantCategory.Ancillary, "Parking passes", Message(1, "CH-008", At(4, 2, 9), new[] { "CH-004" })));
        var newsletter = NonRelevant(NonRelevantCategory.NewsletterSpamPersonal, "This week in glazing",
            Message(1, "EXT-001", At(4, 3, 9), new[] { "CH-001", "CH-002" }));
        newsletter.Messages[0].Attachments.Add(new PlannedAttachment { FileName = "digest.pdf" });
        plan.Threads.Add(newsletter);
        plan.Threads.Add(NonRelevant(NonRelevantCategory.Decoy, "Lakeview Tower lobby glass", Message(1, "CH-004", At(4, 4, 9), new[] { "CH-002" }),
            "the lobby glass warranty"));
        plan.Threads.Add(NonRelevant(NonRelevantCategory.Decoy, "Lakeview Tower punch list", Message(1, "CH-004", At(4, 5, 9), new[] { "CH-002" })));

        PlanNumbering.Assign(plan, draft.Guide!);
        return (draft, plan);
    }

    private static AnswerKeyRow Row(List<AnswerKeyRow> rows, string subject, string docType = "Email") =>
        rows.Single(r => r.Subject == subject && r.DocType == docType);

    [Fact]
    public void RowCount_SortAndHeaders()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);

        Assert.Equal(plan.EmailCount + plan.AttachmentCount, rows.Count);
        Assert.Equal(rows.Select(r => r.DocId).OrderBy(d => d, StringComparer.Ordinal), rows.Select(r => r.DocId));
        Assert.Equal(23, AnswerKeyColumns.Headers.Length);
        Assert.All(rows, r => Assert.Equal(AnswerKeyColumns.Headers.Length, r.ToValues().Length));
        Assert.All(rows, r => Assert.Equal(AnswerKeyBuilder.StatusPlanned, r.Status));
    }

    [Fact]
    public void EmailRow_Columns()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var root = plan.Threads.Single(t => t.Subject == "Change Order 7 sign-off").Messages[0];
        var row = Row(rows, "Change Order 7 sign-off");

        Assert.Equal(root.DocId, row.DocId);
        Assert.Equal("Email", row.DocType);
        Assert.Equal($"{root.DocId}.eml", row.FileName);
        Assert.Equal(root.FilePath, row.FilePath);
        Assert.Equal("2024-03-18 10:14 -05:00", row.DateSent);
        Assert.Equal("Lisa Chen <lisa.chen@harmon.com>", row.From);
        Assert.Equal("Tom Becker <tom.becker@brightline.com>", row.To);
        Assert.Equal("Raymond Harmon <raymond.harmon@harmon.com>", row.Cc);
        Assert.Equal("Lisa Chen", row.Custodian);
        Assert.Equal("Lisa Chen; Tom Becker; Raymond Harmon", row.AllCustodians);
        Assert.Equal("T-0001", row.ThreadId);
        Assert.Equal(row.DocId, row.FamilyId);
        Assert.Equal("", row.ParentDocId);
        Assert.Equal("Planned", row.Status);
    }

    [Fact]
    public void Subjects_ReplyAndForwardPrefixes()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);

        Assert.Single(rows, r => r.Subject == "Change Order 7 sign-off" && r.DocType == "Email");
        Assert.Single(rows, r => r.Subject == "RE: Change Order 7 sign-off");
        Assert.Single(rows, r => r.Subject == "FW: Change Order 7 sign-off");
    }

    [Fact]
    public void RelevantRoot_OwnCodesAndRationale()
    {
        var (draft, plan) = BuildPlan();
        var row = Row(AnswerKeyBuilder.Build(plan, draft.Guide!), "Change Order 7 sign-off");

        Assert.Equal("Relevant", row.Relevance);
        Assert.Equal("ISS-01", row.IssueCodes);
        Assert.Equal("F-003", row.FactIds);
        Assert.Equal(Rationale, row.Rationale);
        Assert.Equal("Y", row.Hot);
        Assert.Equal("N", row.Privileged);
        Assert.Equal("N", row.NearMiss);
        Assert.Equal("", row.NonRelevantCategory);
    }

    [Fact]
    public void BlankThreadRationale_UsesFactAndIssueTemplate()
    {
        var (draft, plan) = BuildPlan();
        var row = Row(AnswerKeyBuilder.Build(plan, draft.Guide!), "Late payment notice");

        Assert.Equal("Evidence of Brightline sends notice of late payment (F-007) under Prompt payment violation (ISS-02).", row.Rationale);
        Assert.Equal("ISS-02", row.IssueCodes);
    }

    [Fact]
    public void QuotedReply_InheritsThreadCodes()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var root = Row(rows, "Change Order 7 sign-off");
        var reply = Row(rows, "RE: Change Order 7 sign-off");
        var forward = Row(rows, "FW: Change Order 7 sign-off");

        Assert.Equal("Relevant", reply.Relevance);
        Assert.Equal("ISS-01", reply.IssueCodes);
        Assert.Equal("F-003", reply.FactIds);
        Assert.Equal($"Quotes {root.DocId}, which is relevant: {Rationale}", reply.Rationale);
        Assert.Equal("N", reply.Hot);
        Assert.Equal($"Quotes {reply.DocId}, which is relevant: {Rationale}", forward.Rationale);
    }

    [Fact]
    public void Attachment_InheritsFamily()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var parent = Row(rows, "Change Order 7 sign-off");
        var attachment = Row(rows, "Change Order 7 sign-off", "Attachment");

        Assert.Equal($"{parent.DocId}.0001", attachment.DocId);
        Assert.Equal("CO-007 Approval.docx", attachment.FileName);
        Assert.Equal(parent.FilePath, attachment.FilePath);
        Assert.Equal(parent.DocId, attachment.FamilyId);
        Assert.Equal(parent.DocId, attachment.ParentDocId);
        Assert.Equal("Relevant", attachment.Relevance);
        Assert.Equal("Y", attachment.Hot);
        Assert.Equal("N", attachment.Privileged);
        Assert.Equal("ISS-01", attachment.IssueCodes);
        Assert.Equal("F-003", attachment.FactIds);
        Assert.Equal($"Attachment to relevant email {parent.DocId}; family coded Relevant.", attachment.Rationale);
        Assert.Equal(parent.Custodian, attachment.Custodian);
        Assert.Equal(parent.DateSent, attachment.DateSent);
    }

    [Fact]
    public void Privileged_FlagsEveryRowOfThread()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var threadId = plan.Threads.Single(t => t.IsPrivileged).Id;

        var family = rows.Where(r => r.ThreadId == threadId).ToList();
        Assert.Equal(3, family.Count);
        Assert.All(family, r => Assert.Equal("Y", r.Privileged));
        Assert.All(rows.Where(r => r.ThreadId != threadId), r => Assert.Equal("N", r.Privileged));
        Assert.Single(rows, r => r.Hot == "Y" && r.DocType == "Email");
    }

    [Theory]
    [InlineData("Crane schedule", "Daily business", "N", "Routine business correspondence unrelated to the issues in this matter.")]
    [InlineData("Parking passes", "Ancillary", "N", "Administrative staff correspondence unrelated to the issues in this matter.")]
    [InlineData("This week in glazing", "Newsletter/spam/personal", "N", "Newsletter, promotional or personal email unrelated to the issues in this matter.")]
    [InlineData("Lakeview Tower lobby glass", "Decoy", "Y", "Near-miss: concerns the lobby glass warranty, not the alleged conduct.")]
    [InlineData("Lakeview Tower punch list", "Decoy", "Y", "Near-miss: shares project names and people with the matter but is not about the alleged conduct.")]
    public void NonRelevant_Templates(string subject, string category, string nearMiss, string rationale)
    {
        var (draft, plan) = BuildPlan();
        var row = Row(AnswerKeyBuilder.Build(plan, draft.Guide!), subject);

        Assert.Equal("Not Relevant", row.Relevance);
        Assert.Equal("", row.IssueCodes);
        Assert.Equal("", row.FactIds);
        Assert.Equal(category, row.NonRelevantCategory);
        Assert.Equal(nearMiss, row.NearMiss);
        Assert.Equal(rationale, row.Rationale);
        Assert.Equal("N", row.Hot);
        Assert.Equal("N", row.Privileged);
    }

    [Fact]
    public void ExternalSender_CustodianIsFirstCastRecipient()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var row = Row(rows, "This week in glazing");
        var attachment = Row(rows, "This week in glazing", "Attachment");

        Assert.Equal("Dunder Digest <news@dunderdigest.com>", row.From);
        Assert.Equal("Maria Delgado", row.Custodian);
        Assert.Equal("Maria Delgado; Tom Becker", row.AllCustodians);
        Assert.StartsWith("Mailboxes/maria.delgado/Inbox/", row.FilePath);
        Assert.Equal("Not Relevant", attachment.Relevance);
        Assert.Equal("Newsletter/spam/personal", attachment.NonRelevantCategory);
        Assert.Contains(row.DocId, attachment.Rationale);
    }

    [Fact]
    public void Invariant_RowRelevanceMatchesThread()
    {
        var (draft, plan) = BuildPlan();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var byThread = plan.Threads.ToDictionary(t => t.Id);

        foreach (var row in rows)
        {
            var expected = byThread[row.ThreadId].Relevance == ThreadRelevance.Relevant ? "Relevant" : "Not Relevant";
            Assert.Equal(expected, row.Relevance);
            Assert.Contains(row.Hot, new[] { "Y", "N" });
            Assert.Contains(row.Privileged, new[] { "Y", "N" });
            Assert.Contains(row.NearMiss, new[] { "Y", "N" });
        }
    }

    [Fact]
    public void Build_IsPureAndDeterministic()
    {
        var (draft, plan) = BuildPlan();
        var hash = draft.Guide!.ComputeHash();
        var first = AnswerKeyBuilder.Build(plan, draft.Guide).Select(r => string.Join("|", r.ToValues())).ToList();
        var second = AnswerKeyBuilder.Build(plan, draft.Guide).Select(r => string.Join("|", r.ToValues())).ToList();

        Assert.Equal(first, second);
        Assert.Equal(hash, draft.Guide.ComputeHash());
    }

    [Fact]
    public void CategoryLabel_Values()
    {
        Assert.Equal("Daily business", AnswerKeyBuilder.CategoryLabel(NonRelevantCategory.DailyBusiness));
        Assert.Equal("Ancillary", AnswerKeyBuilder.CategoryLabel(NonRelevantCategory.Ancillary));
        Assert.Equal("Newsletter/spam/personal", AnswerKeyBuilder.CategoryLabel(NonRelevantCategory.NewsletterSpamPersonal));
        Assert.Equal("Decoy", AnswerKeyBuilder.CategoryLabel(NonRelevantCategory.Decoy));
        Assert.Equal("", AnswerKeyBuilder.CategoryLabel(NonRelevantCategory.None));
    }
}
