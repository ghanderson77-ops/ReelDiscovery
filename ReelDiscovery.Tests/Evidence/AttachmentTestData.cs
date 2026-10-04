using System.Globalization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Phase 6 Wave 0 fixture: EvidenceTestData.ApprovedCase plus threads T-0901..T-0915 carrying every attachment kind on
/// relevant and non-relevant mail and the adversarial shapes of the cross-AI review (two-version chain, same title from
/// another organization, multi-message attachments, a sibling branch, two invites for one meeting, voicemails, a
/// privileged non-relevant thread and a decoy with MustNotMention). New DocIds are {prefix}-900001.. in SentAt order.
/// </summary>
internal static class AttachmentTestData
{
    public const string RelevantDocThreadId = "T-0901";
    public const string ChainThreadId = "T-0902";
    public const string OtherOrgDocThreadId = "T-0903";
    public const string MultiMessageThreadId = "T-0904";
    public const string SiblingBranchThreadId = "T-0905";
    public const string InviteThreadId = "T-0906";
    public const string SecondInviteThreadId = "T-0907";
    public const string RelevantVoicemailThreadId = "T-0908";
    public const string DailyBusinessThreadId = "T-0909";
    public const string AncillaryThreadId = "T-0910";
    public const string NewsletterThreadId = "T-0911";
    public const string SpamThreadId = "T-0912";
    public const string PersonalThreadId = "T-0913";
    public const string NonRelevantVoicemailThreadId = "T-0914";
    public const string DecoyThreadId = "T-0915";

    public const string NewsletterId = "EXT-901";
    public const string SpamId = "EXT-902";
    public const string PersonalId = "EXT-903";

    public const string PricingBackupTitle = "CO-007 Pricing Backup";

    // Decoy terms: one guide fact title and the amount linked to it
    public const string DecoyFactTitle = "Harmon approves Change Order 7";
    public const string DecoyAmount = "$184,250.00";

    public static (CaseDraft Draft, CorpusPlan Plan) MixedCase()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var guide = draft.Guide!;

        plan.Externals.Add(new ExternalParty
        {
            Id = NewsletterId,
            Name = "Glass & Facade Digest",
            Email = "editor@glassfacadedigest.com",
            Domain = "glassfacadedigest.com",
            Kind = ExternalKind.Newsletter
        });
        plan.Externals.Add(new ExternalParty
        {
            Id = SpamId,
            Name = "Premier Equipment Deals",
            Email = "offers@premierequipdeals.biz",
            Domain = "premierequipdeals.biz",
            Kind = ExternalKind.Spam
        });
        plan.Externals.Add(new ExternalParty
        {
            Id = PersonalId,
            Name = "Jamie Becker",
            Email = "jamie.becker@mailbox.example",
            Domain = "mailbox.example",
            Kind = ExternalKind.Personal
        });

        var threads = new List<PlannedThread>();

        // Harmon project executive sends the hot pricing package (Word + Excel + PowerPoint)
        var relevantDoc = Relevant(RelevantDocThreadId, "Change Order 7 pricing package", "F-003");
        var root = Root(guide, relevantDoc, "CH-004", new[] { "CH-002" }, new[] { "CH-003" }, Day(2024, 3, 20), "F-003");
        root.IsHot = true;
        Attach(root, AttachmentType.Word, "Change Order Pricing Memo");
        Attach(root, AttachmentType.Excel, PricingBackupTitle);
        Attach(root, AttachmentType.PowerPoint, "Schedule Recovery Update");
        threads.Add(relevantDoc);

        // Later Harmon (same organization) revision of the backup: version 2 of the chain
        var chain = Relevant(ChainThreadId, "Revised pricing backup", "F-003");
        Attach(Root(guide, chain, "CH-008", new[] { "CH-004" }, Array.Empty<string>(), Day(2024, 4, 10), "F-003"),
            AttachmentType.Excel, PricingBackupTitle + " v2");
        threads.Add(chain);

        // Brightline (another organization) keeps its own sheet with the same title stem: must not join Harmon's chain
        var otherOrg = Relevant(OtherOrgDocThreadId, "Our pricing backup for CO-007", "F-003");
        Attach(Root(guide, otherOrg, "CH-002", new[] { "CH-001" }, Array.Empty<string>(), Day(2024, 3, 27), "F-003"),
            AttachmentType.Excel, PricingBackupTitle);
        threads.Add(otherOrg);

        // Attachments on two messages of one thread
        var multi = Relevant(MultiMessageThreadId, "Upper floor site visit", "F-004");
        var multiRoot = Root(guide, multi, "CH-002", new[] { "CH-001" }, Array.Empty<string>(), Day(2024, 4, 23), "F-004");
        Attach(multiRoot, AttachmentType.Word, "Site Visit Notes");
        var multiReply = Reply(guide, multi, 2, 1, MessageKind.Reply, "CH-001", new[] { "CH-002" }, Array.Empty<string>(),
            multiRoot.SentAt.AddHours(3), "F-004");
        Attach(multiReply, AttachmentType.Excel, "Punch List Costs");
        Reply(guide, multi, 3, 2, MessageKind.ReplyAll, "CH-002", new[] { "CH-001" }, new[] { "CH-005" },
            multiReply.SentAt.AddDays(1), "F-004");
        threads.Add(multi);

        // Two replies to the root: branch A (with an attachment) and a private sibling branch with different people
        var sibling = Relevant(SiblingBranchThreadId, "Invoice INV-2024-118 follow-up", "F-005");
        var siblingRoot = Root(guide, sibling, "CH-001", new[] { "CH-002", "CH-005" }, Array.Empty<string>(),
            Day(2024, 5, 14), "F-005");
        Attach(Reply(guide, sibling, 2, 1, MessageKind.Reply, "CH-002", new[] { "CH-001" }, Array.Empty<string>(),
            siblingRoot.SentAt.AddHours(2), "F-005"), AttachmentType.Word, "Branch A Summary");
        Reply(guide, sibling, 3, 1, MessageKind.Reply, "CH-005", new[] { "CH-001" }, Array.Empty<string>(),
            siblingRoot.SentAt.AddHours(4), "F-005");
        threads.Add(sibling);

        // Two invites for one site walk (same participants and facts), both before the meeting
        var invite = Relevant(InviteThreadId, "Site walk on the upper floors", "F-004");
        var inviteRoot = Root(guide, invite, "CH-004", new[] { "CH-002" }, new[] { "CH-001" }, Day(2024, 5, 8), "F-004");
        Attach(inviteRoot, AttachmentType.CalendarInvite, "Site Walk Meeting");
        Attach(inviteRoot, AttachmentType.Image, "Curtain wall mockup photo", inline: true);
        threads.Add(invite);

        var secondInvite = Relevant(SecondInviteThreadId, "Site walk agenda", "F-004");
        Attach(Root(guide, secondInvite, "CH-004", new[] { "CH-002" }, new[] { "CH-001" }, Day(2024, 5, 9), "F-004"),
            AttachmentType.CalendarInvite, "Site Walk Agenda");
        threads.Add(secondInvite);

        var relevantVoicemail = Relevant(RelevantVoicemailThreadId, "Voicemail about the invoice", "F-006");
        Attach(Root(guide, relevantVoicemail, "CH-003", new[] { "CH-004" }, Array.Empty<string>(), Day(2024, 6, 20), "F-006"),
            AttachmentType.Voicemail, "Voicemail from Raymond Harmon");
        threads.Add(relevantVoicemail);

        // Privileged daily business: counsel and the president on routine matters, a code-default Word file name
        var daily = Other(DailyBusinessThreadId, NonRelevantCategory.DailyBusiness, "Insurance certificate renewal");
        daily.IsPrivileged = true;
        var dailyRoot = Root(guide, daily, "CH-005", new[] { "CH-001" }, new[] { "CH-002" }, Day(2024, 2, 21));
        dailyRoot.Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Word, FileName = "Document 1.docx", Title = string.Empty });
        var dailyReply = Reply(guide, daily, 2, 1, MessageKind.Reply, "CH-001", new[] { "CH-005" }, new[] { "CH-002" },
            dailyRoot.SentAt.AddHours(2));
        Reply(guide, daily, 3, 2, MessageKind.ReplyAll, "CH-005", new[] { "CH-001" }, new[] { "CH-002" },
            dailyReply.SentAt.AddHours(1));
        threads.Add(daily);

        var ancillary = Other(AncillaryThreadId, NonRelevantCategory.Ancillary, "Vendor onboarding forms");
        Root(guide, ancillary, "CH-008", new[] { "CH-004" }, Array.Empty<string>(), Day(2024, 3, 6)).Attachments
            .Add(new PlannedAttachment { Kind = AttachmentType.Excel, FileName = "Spreadsheet 1.xlsx", Title = string.Empty });
        threads.Add(ancillary);

        var newsletter = Other(NewsletterThreadId, NonRelevantCategory.NewsletterSpamPersonal, "This month in facade design");
        Attach(Root(guide, newsletter, NewsletterId, new[] { "CH-001" }, Array.Empty<string>(), Day(2024, 4, 2)),
            AttachmentType.Image, "Newsletter banner", inline: true);
        threads.Add(newsletter);

        var spam = Other(SpamThreadId, NonRelevantCategory.NewsletterSpamPersonal, "Limited time lift rental offer");
        Attach(Root(guide, spam, SpamId, new[] { "CH-003" }, Array.Empty<string>(), Day(2024, 4, 16)),
            AttachmentType.Image, "Offer banner", inline: true);
        threads.Add(spam);

        var personal = Other(PersonalThreadId, NonRelevantCategory.NewsletterSpamPersonal, "Dinner on Saturday");
        Root(guide, personal, PersonalId, new[] { "CH-002" }, Array.Empty<string>(), Day(2024, 5, 21));
        threads.Add(personal);

        var nonRelevantVoicemail = Other(NonRelevantVoicemailThreadId, NonRelevantCategory.DailyBusiness, "Crew start time");
        Attach(Root(guide, nonRelevantVoicemail, "CH-009", new[] { "CH-002" }, Array.Empty<string>(), Day(2024, 5, 28)),
            AttachmentType.Voicemail, "Voicemail from Owen Reilly");
        threads.Add(nonRelevantVoicemail);

        var decoy = Other(DecoyThreadId, NonRelevantCategory.Decoy, "Change order log cleanup");
        decoy.DecoyAngle = "Mentions change orders in general, but only about tidying the log format, never the disputed one.";
        decoy.MustNotMention = new List<string> { DecoyFactTitle, DecoyAmount };
        decoy.IssueIds = new List<string> { "ISS-01" };
        Root(guide, decoy, "CH-004", new[] { "CH-002" }, Array.Empty<string>(), Day(2024, 4, 4));
        threads.Add(decoy);

        Number(plan, guide, threads);
        plan.Threads.AddRange(threads);
        draft.Corpus!.Plan = plan;
        return (draft, plan);
    }

    public static PlannedThread Thread(CorpusPlan plan, string threadId) => plan.Threads.Single(t => t.Id == threadId);

    public static IEnumerable<PlannedAttachment> AttachmentsOf(CorpusPlan plan, string threadId) =>
        Thread(plan, threadId).Messages.OrderBy(m => m.Seq).SelectMany(m => m.Attachments);

    /// <summary>
    /// The site-walk Meeting ledger entry both invites refer to. Date is the source email's local date (first invite minus
    /// 2 days); Value is the meeting day (first invite + 5 days), so a scheduler that reads Date instead of Value is caught.
    /// </summary>
    public static LedgerEntry SiteWalkMeeting(CorpusPlan plan)
    {
        var root = Thread(plan, InviteThreadId).Messages.Single(m => m.Seq == 1);
        var local = DateOnly.FromDateTime(root.SentAt.DateTime);
        return new LedgerEntry
        {
            Id = $"{InviteThreadId}-L01",
            Date = local.AddDays(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Kind = LedgerKind.Meeting,
            About = "site walk",
            Statement = "The teams will walk the upper floors together.",
            Value = local.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            KnownByIds = new[] { root.FromId }.Concat(root.ToIds).Concat(root.CcIds).Distinct(StringComparer.Ordinal).ToList(),
            FactIds = new List<string>(root.FactIds),
            SourceDocId = root.DocId,
            SourceThreadId = InviteThreadId,
            WaveIndex = 1
        };
    }

    private static DateOnly Day(int year, int month, int day) => new(year, month, day);

    private static PlannedThread Relevant(string id, string subject, string factId) => new()
    {
        Id = id,
        Relevance = ThreadRelevance.Relevant,
        Category = NonRelevantCategory.None,
        FactIds = new List<string> { factId },
        IssueIds = new List<string> { "ISS-01" },
        Subject = subject,
        Purpose = $"Evidence for {factId}."
    };

    private static PlannedThread Other(string id, NonRelevantCategory category, string subject) => new()
    {
        Id = id,
        Relevance = ThreadRelevance.NotRelevant,
        Category = category,
        Subject = subject,
        Purpose = "Routine mail unrelated to the dispute."
    };

    private static PlannedMessage Root(CaseGuide guide, PlannedThread thread, string from, string[] to, string[] cc,
        DateOnly day, string factId = "")
    {
        var zone = ZoneFor(guide, from, to);
        var message = Message(thread, 1, 0, MessageKind.New, from, to, cc, BusinessCalendar.At(zone, day, new TimeOnly(10, 0)),
            zone, factId);
        return message;
    }

    private static PlannedMessage Reply(CaseGuide guide, PlannedThread thread, int seq, int replyToSeq, MessageKind kind,
        string from, string[] to, string[] cc, DateTimeOffset sentAt, string factId = "")
    {
        var zone = ZoneFor(guide, from, to);
        return Message(thread, seq, replyToSeq, kind, from, to, cc, TimeZoneInfo.ConvertTime(sentAt, zone), zone, factId);
    }

    private static PlannedMessage Message(PlannedThread thread, int seq, int replyToSeq, MessageKind kind, string from,
        string[] to, string[] cc, DateTimeOffset sentAt, TimeZoneInfo zone, string factId)
    {
        var relevant = thread.Relevance == ThreadRelevance.Relevant;
        var message = new PlannedMessage
        {
            Id = $"{thread.Id}-M{seq:00}",
            Seq = seq,
            ReplyToSeq = replyToSeq,
            Kind = kind,
            FromId = from,
            ToIds = to.ToList(),
            CcIds = cc.ToList(),
            SentAt = sentAt,
            TimeZoneId = zone.Id,
            FactIds = factId.Length > 0 ? new List<string> { factId } : new List<string>(),
            Basis = relevant ? RelevanceBasis.Own : RelevanceBasis.None
        };
        thread.Messages.Add(message);
        return message;
    }

    private static void Attach(PlannedMessage message, AttachmentType kind, string title, bool inline = false)
    {
        var extension = kind switch
        {
            AttachmentType.Word => ".docx",
            AttachmentType.Excel => ".xlsx",
            AttachmentType.PowerPoint => ".pptx",
            AttachmentType.Image => ".png",
            AttachmentType.CalendarInvite => ".ics",
            _ => ".mp3"
        };
        message.Attachments.Add(new PlannedAttachment
        {
            Kind = kind,
            Title = title,
            FileName = title + extension,
            Description = $"{title} for the thread.",
            IsInline = inline
        });
    }

    // Cast sender: their organization's zone; external sender: the first cast recipient's zone
    private static TimeZoneInfo ZoneFor(CaseGuide guide, string from, string[] to)
    {
        var member = guide.FindCast(from) ?? to.Select(guide.FindCast).FirstOrDefault(m => m != null);
        return member == null ? TimeZoneInfo.Utc : BusinessCalendar.Resolve(guide.FindOrganization(member.OrganizationId));
    }

    // PlanNumbering formats: {prefix}-9xxxxx in SentAt order, {DocId}.0001 attachments, sender-copy path, cast custodians
    private static void Number(CorpusPlan plan, CaseGuide guide, List<PlannedThread> threads)
    {
        var prefix = string.IsNullOrEmpty(plan.DocIdPrefix) ? PlanFixtures.Prefix : plan.DocIdPrefix;
        var folders = PlanNumbering.CustodianFolders(guide);
        var messages = threads.SelectMany(t => t.Messages).OrderBy(m => m.SentAt.UtcDateTime).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            message.DocId = string.Format(CultureInfo.InvariantCulture, "{0}-{1:000000}", prefix, 900001 + i);
            for (var k = 0; k < message.Attachments.Count; k++)
                message.Attachments[k].DocId = string.Format(CultureInfo.InvariantCulture, "{0}.{1:0000}", message.DocId, k + 1);

            message.CustodianIds = new[] { message.FromId }
                .Concat(message.ToIds)
                .Concat(message.CcIds)
                .Where(id => guide.FindCast(id) != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var owner = guide.FindCast(message.FromId) != null ? message.FromId : message.CustodianIds.FirstOrDefault();
            var box = owner == message.FromId ? PlanNumbering.SentFolder : PlanNumbering.InboxFolder;
            var folder = owner != null && folders.TryGetValue(owner, out var name) ? name : PlanNumbering.UnassignedFolder;
            message.FilePath = $"{PlanNumbering.MailboxesFolder}/{folder}/{box}/{message.DocId}.eml";
        }

        foreach (var thread in threads)
        {
            var root = thread.Messages.First();
            thread.OwnerCustodianId = guide.FindCast(root.FromId) != null ? root.FromId : root.CustodianIds.FirstOrDefault() ?? string.Empty;
        }
    }
}

public class AttachmentTestDataTests
{
    [Fact]
    public void MixedCase_CoversEveryAttachmentKindAndCategory()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var kinds = plan.AllMessages().SelectMany(m => m.Attachments).Select(a => a.Kind).ToHashSet();
        var categories = plan.Threads.Select(t => t.Category).ToHashSet();

        Assert.All(Enum.GetValues<AttachmentType>(), kind => Assert.Contains(kind, kinds));
        Assert.All(Enum.GetValues<NonRelevantCategory>().Where(c => c != NonRelevantCategory.None),
            category => Assert.Contains(category, categories));

        var relevantKinds = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant)
            .SelectMany(t => t.Messages).SelectMany(m => m.Attachments).Select(a => a.Kind).ToHashSet();
        Assert.All(Enum.GetValues<AttachmentType>(), kind => Assert.Contains(kind, relevantKinds));
    }

    [Fact]
    public void MixedCase_SamePricingTitle_FromDifferentOrganizations()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;

        var senders = plan.AllMessages()
            .Where(m => m.Attachments.Any(a => a.Title == AttachmentTestData.PricingBackupTitle))
            .Select(m => guide.FindCast(m.FromId)!.OrganizationId)
            .ToList();

        Assert.Equal(2, senders.Count);
        Assert.NotEqual(senders[0], senders[1]);

        var chainSender = AttachmentTestData.Thread(plan, AttachmentTestData.ChainThreadId).Messages[0];
        var firstSender = AttachmentTestData.Thread(plan, AttachmentTestData.RelevantDocThreadId).Messages[0];
        Assert.Equal(guide.FindCast(firstSender.FromId)!.OrganizationId, guide.FindCast(chainSender.FromId)!.OrganizationId);
        Assert.True(chainSender.SentAt > firstSender.SentAt);
    }

    [Fact]
    public void MixedCase_AdversarialShapes()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;

        var multi = AttachmentTestData.Thread(plan, AttachmentTestData.MultiMessageThreadId);
        Assert.Equal(3, multi.Messages.Count);
        Assert.Equal(2, multi.Messages.Count(m => m.Attachments.Count > 0));

        var sibling = AttachmentTestData.Thread(plan, AttachmentTestData.SiblingBranchThreadId);
        Assert.Equal(new[] { 0, 1, 1 }, sibling.Messages.OrderBy(m => m.Seq).Select(m => m.ReplyToSeq));
        Assert.NotEqual(sibling.Messages[1].FromId, sibling.Messages[2].FromId);

        var first = AttachmentTestData.Thread(plan, AttachmentTestData.InviteThreadId).Messages[0];
        var second = AttachmentTestData.Thread(plan, AttachmentTestData.SecondInviteThreadId).Messages[0];
        var meeting = AttachmentTestData.SiteWalkMeeting(plan);
        var meetingDay = DateOnly.ParseExact(meeting.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.True(second.SentAt > first.SentAt);
        Assert.True(DateOnly.FromDateTime(second.SentAt.DateTime) < meetingDay);
        Assert.Equal(meeting.ParsedDate!.Value.AddDays(7), meetingDay);
        Assert.Equal(first.FactIds, second.FactIds);
        Assert.Equal(new[] { first.FromId }.Concat(first.ToIds).Concat(first.CcIds),
            new[] { second.FromId }.Concat(second.ToIds).Concat(second.CcIds));
        Assert.Contains(first.Attachments, a => a.Kind == AttachmentType.Image && a.IsInline);

        var daily = AttachmentTestData.Thread(plan, AttachmentTestData.DailyBusinessThreadId);
        Assert.True(daily.IsPrivileged);
        Assert.Equal(ThreadRelevance.NotRelevant, daily.Relevance);
        Assert.Equal(3, daily.Messages.Count);
        Assert.Contains(daily.Messages[0].Attachments, a => a.FileName == "Document 1.docx" && a.Title.Length == 0);

        var decoy = AttachmentTestData.Thread(plan, AttachmentTestData.DecoyThreadId);
        Assert.NotEmpty(decoy.DecoyAngle);
        Assert.Contains(decoy.MustNotMention, term => guide.Facts.Any(f => f.Title == term));
        Assert.Contains(AttachmentTestData.DecoyAmount, decoy.MustNotMention);

        Assert.Equal(new[] { ExternalKind.Newsletter, ExternalKind.Spam, ExternalKind.Personal },
            new[] { AttachmentTestData.NewsletterId, AttachmentTestData.SpamId, AttachmentTestData.PersonalId }
                .Select(id => plan.Externals.Single(e => e.Id == id).Kind));
        Assert.Contains(AttachmentTestData.AttachmentsOf(plan, AttachmentTestData.SpamThreadId), a => a.IsInline);
        Assert.Contains(AttachmentTestData.AttachmentsOf(plan, AttachmentTestData.NewsletterThreadId), a => a.IsInline);
        Assert.Single(AttachmentTestData.Thread(plan, AttachmentTestData.NonRelevantVoicemailThreadId).Messages);
    }

    [Fact]
    public void MixedCase_IdsResolveAndFormatsMatchPlanNumbering()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;
        var externals = plan.Externals.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var added = plan.Threads.Where(t => t.Id.StartsWith("T-09", StringComparison.Ordinal)).ToList();

        Assert.Equal(15, added.Count);
        Assert.Equal(plan.Threads.Count, plan.Threads.Select(t => t.Id).Distinct().Count());
        foreach (var message in added.SelectMany(t => t.Messages))
        {
            Assert.All(new[] { message.FromId }.Concat(message.ToIds).Concat(message.CcIds),
                id => Assert.True(guide.FindCast(id) != null || externals.Contains(id), id));
            Assert.Matches($@"^{plan.DocIdPrefix}-9\d{{5}}$", message.DocId);
            Assert.True(CorpusPlan.IsSafeEmlPath(message.FilePath), message.FilePath);
            Assert.EndsWith($"/{message.DocId}.eml", message.FilePath);
            Assert.NotEmpty(message.CustodianIds);

            // Every cast participant is active on the local send date
            var day = DateOnly.FromDateTime(message.SentAt.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var member in message.CustodianIds.Select(id => guide.FindCast(id)!))
                Assert.True(string.CompareOrdinal(member.ActiveFrom, day) <= 0 && string.CompareOrdinal(day, member.ActiveTo) <= 0,
                    $"{member.Id} inactive on {day}");
            for (var k = 0; k < message.Attachments.Count; k++)
                Assert.Equal($"{message.DocId}.{k + 1:0000}", message.Attachments[k].DocId);
        }

        var docIds = plan.AllMessages().Select(m => m.DocId).ToList();
        Assert.Equal(docIds.Count, docIds.Distinct().Count());
    }

    [Fact]
    public async Task MixedCase_ReplayAcceptsOwnedAttachmentsOnly()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            var multi = AttachmentTestData.Thread(plan, AttachmentTestData.MultiMessageThreadId);
            var reply = multi.Messages.Single(m => m.Seq == 2);
            var chainAttachment = AttachmentTestData.AttachmentsOf(plan, AttachmentTestData.ChainThreadId).Single();
            var done = new JournalRecord
            {
                Type = JournalRecordType.ThreadDone,
                PlanId = plan.PlanId,
                ThreadId = multi.Id,
                Attachments = new List<JournalRecord>
                {
                    new()
                    {
                        Type = JournalRecordType.AttachmentDone,
                        AttachmentDocId = reply.Attachments[0].DocId,
                        ParentDocId = reply.DocId,
                        AttachmentKind = AttachmentType.Excel
                    },
                    new()
                    {
                        Type = JournalRecordType.AttachmentDone,
                        AttachmentDocId = chainAttachment.DocId,
                        ParentDocId = chainAttachment.DocId[..chainAttachment.DocId.LastIndexOf('.')],
                        AttachmentKind = AttachmentType.Excel
                    }
                }
            };
            await new GenerationJournal(folder).AppendAsync(done);

            var replay = GenerationJournal.Replay(folder, plan);

            Assert.Equal(new[] { reply.Attachments[0].DocId }, replay.LastByAttachment.Keys.ToArray());
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }

    [Fact]
    public void MixedCase_SurvivesNormalize()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var before = PlanFixtures.Clone(plan);

        plan.Normalize();

        Assert.Equal(before.Threads.Count, plan.Threads.Count);
        Assert.Equal(before.AttachmentCount, plan.AttachmentCount);
        Assert.Equal(before.AllMessages().Select(m => m.FilePath), plan.AllMessages().Select(m => m.FilePath));
        Assert.Equal(before.AllMessages().SelectMany(m => m.Attachments).Select(a => a.FileName),
            plan.AllMessages().SelectMany(m => m.Attachments).Select(a => a.FileName));
        Assert.Equal(before.Externals.Count, plan.Externals.Count);
    }
}
