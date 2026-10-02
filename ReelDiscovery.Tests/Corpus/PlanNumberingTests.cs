using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class PlanNumberingTests
{
    private static readonly TimeSpan Central = TimeSpan.FromHours(-5);

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0, TimeSpan? offset = null) =>
        new(2024, month, day, hour, minute, 0, offset ?? Central);

    private static PlannedMessage Message(int seq, string fromId, DateTimeOffset sentAt, string[] to, string[]? cc = null,
        MessageKind kind = MessageKind.New, int replyToSeq = 0) => new()
    {
        Seq = seq,
        ReplyToSeq = replyToSeq,
        Kind = kind,
        FromId = fromId,
        ToIds = to.ToList(),
        CcIds = (cc ?? Array.Empty<string>()).ToList(),
        SentAt = sentAt
    };

    /// <summary>
    /// Three threads that arrive out of time order: X (2024-03-20), Y (2024-03-18, relevant) and Z (2024-03-19), with
    /// messages of X and Y interleaving in time.
    /// </summary>
    private static (CaseDraft Draft, CorpusPlan Plan) ThreeThreadPlan()
    {
        var draft = CorpusTestData.RichDraft();
        var plan = new CorpusPlan { DocIdPrefix = "HB" };
        plan.Externals.Add(new ExternalParty { Id = "EXT-001", Name = "Dunder Digest", Email = "news@dunderdigest.com", Domain = "dunderdigest.com" });

        var x = new PlannedThread
        {
            Id = "X",
            Relevance = ThreadRelevance.NotRelevant,
            Category = NonRelevantCategory.NewsletterSpamPersonal,
            Subject = "Weekly digest",
            LinkedThreadIds = new List<string> { "Y", "MISSING" }
        };
        x.Messages.Add(Message(1, "EXT-001", At(3, 20, 9), new[] { "CH-001" }));
        x.Messages.Add(Message(2, "CH-001", At(3, 20, 11), new[] { "EXT-001" }, kind: MessageKind.Reply, replyToSeq: 1));

        var y = new PlannedThread
        {
            Id = "Y",
            Relevance = ThreadRelevance.Relevant,
            IssueIds = new List<string> { "ISS-01" },
            FactIds = new List<string> { "F-003" },
            Subject = "Change Order 7 sign-off",
            LinkedThreadIds = new List<string> { "Z" }
        };
        y.Messages.Add(Message(1, "CH-001", At(3, 18, 10), new[] { "CH-002" }, new[] { "CH-005" }));
        y.Messages[0].Attachments.Add(new PlannedAttachment { FileName = "CO7.docx" });
        y.Messages[0].Attachments.Add(new PlannedAttachment { FileName = "CO7.xlsx" });
        // Lands after X's first message: relevant and non-relevant interleave by time
        y.Messages.Add(Message(2, "CH-002", At(3, 20, 10), new[] { "CH-001" }, kind: MessageKind.Reply, replyToSeq: 1));

        var z = new PlannedThread
        {
            Id = "Z",
            Relevance = ThreadRelevance.NotRelevant,
            Category = NonRelevantCategory.DailyBusiness,
            Subject = "Crane schedule"
        };
        // 08:00 Phoenix (UTC-7) = 15:00 UTC on 2024-03-19
        z.Messages.Add(Message(1, "CH-003", At(3, 19, 8, 0, TimeSpan.FromHours(-7)), new[] { "CH-004" }));

        plan.Threads.AddRange(new[] { x, y, z });
        return (draft, plan);
    }

    [Fact]
    public void DocIdPrefix_Derive_PleadingUsesDefendantInitialsFromFullName()
    {
        // Harmon Builders, Inc. has the single-word short name "Harmon": initials of the full name minus Inc
        Assert.Equal("HB", DocIdPrefix.Derive(CorpusTestData.RichDraft()));
    }

    [Fact]
    public void DocIdPrefix_Derive_UsesMultiWordShortName()
    {
        var draft = CorpusTestData.RichDraft();
        var defendant = draft.CoreDocument!.Parties.Single(p => p.Role == PartyRole.Defendant);
        defendant.Name = "Dunder Mifflin Paper Company, Inc.";
        defendant.ShortName = "Dunder Mifflin";
        draft.Guide!.FindOrganization("ORG-02")!.ShortName = "Dunder Mifflin";

        Assert.Equal("DM", DocIdPrefix.Derive(draft));
    }

    [Fact]
    public void DocIdPrefix_Derive_MemoUsesOrganization()
    {
        var draft = new CaseDraft
        {
            CoreDocument = new CoreDocument
            {
                DocumentType = CoreDocumentType.InvestigationMemo,
                Memo = new MemoHeader { Organization = "Acme Paper Company of Scranton" }
            }
        };

        Assert.Equal("APS", DocIdPrefix.Derive(draft));
    }

    [Fact]
    public void DocIdPrefix_Derive_FallsBackToRd()
    {
        Assert.Equal("RD", DocIdPrefix.Derive(new CaseDraft()));
        Assert.Equal("RD", DocIdPrefix.Derive(new CaseDraft { CoreDocument = new CoreDocument() }));
    }

    [Fact]
    public void DocIdPrefix_Derive_DropsNonAsciiLetters()
    {
        var draft = new CaseDraft
        {
            CoreDocument = new CoreDocument
            {
                DocumentType = CoreDocumentType.InvestigationMemo,
                Memo = new MemoHeader { Organization = "Ölwerk Éclair Bakery" }
            }
        };

        // Non-ASCII letters are dropped from each word before taking initials
        var prefix = DocIdPrefix.Derive(draft);
        Assert.Equal("LCB", prefix);
        Assert.Matches("^[A-Z]{2,4}$", prefix);
    }

    [Fact]
    public void DocIdPrefix_Derive_SingleWordUsesFirstTwoLetters()
    {
        var draft = new CaseDraft
        {
            CoreDocument = new CoreDocument
            {
                DocumentType = CoreDocumentType.InvestigationMemo,
                Memo = new MemoHeader { Organization = "Initech" }
            }
        };

        Assert.Equal("IN", DocIdPrefix.Derive(draft));
    }

    [Fact]
    public void DocIdPrefix_Derive_CapsAtFourLetters()
    {
        var draft = new CaseDraft
        {
            CoreDocument = new CoreDocument
            {
                DocumentType = CoreDocumentType.InvestigationMemo,
                Memo = new MemoHeader { Organization = "Alpha Beta Gamma Delta Epsilon" }
            }
        };

        Assert.Equal("ABGD", DocIdPrefix.Derive(draft));
    }

    [Fact]
    public void DocIdPrefix_Effective_OverrideWins()
    {
        var draft = CorpusTestData.RichDraft();
        Assert.Equal("QD", DocIdPrefix.Effective(new CorpusSettings { DocIdPrefix = "QD" }, draft));
        Assert.Equal("HB", DocIdPrefix.Effective(new CorpusSettings { DocIdPrefix = "" }, draft));
        Assert.Equal("QD", DocIdPrefix.Effective(new CorpusSettings { DocIdPrefix = "q-d" }, draft));
    }

    [Fact]
    public void Assign_ThreadIdsFollowFirstSentAt()
    {
        var (draft, plan) = ThreeThreadPlan();
        PlanNumbering.Assign(plan, draft.Guide!);

        Assert.Equal(new[] { "T-0001", "T-0002", "T-0003" }, plan.Threads.Select(t => t.Id));
        Assert.Equal("Change Order 7 sign-off", plan.Threads[0].Subject);
        Assert.Equal("Crane schedule", plan.Threads[1].Subject);
        Assert.Equal("Weekly digest", plan.Threads[2].Subject);
        Assert.Equal(new[] { "T-0001-M01", "T-0001-M02" }, plan.Threads[0].Messages.Select(m => m.Id));
    }

    [Fact]
    public void Assign_DocIdsChronologicalAndInterleaved()
    {
        var (draft, plan) = ThreeThreadPlan();
        PlanNumbering.Assign(plan, draft.Guide!);

        var ordered = plan.AllMessages().OrderBy(m => m.DocId, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "HB-000001", "HB-000002", "HB-000003", "HB-000004", "HB-000005" }, ordered.Select(m => m.DocId));
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i - 1].SentAt.UtcDateTime <= ordered[i].SentAt.UtcDateTime);

        var relevant = plan.Threads.Single(t => t.Relevance == ThreadRelevance.Relevant);
        var digest = plan.Threads.Single(t => t.Subject == "Weekly digest");
        // Y msg 1, Z, X msg 1, Y msg 2, X msg 2
        Assert.Equal("HB-000001", relevant.Messages[0].DocId);
        Assert.Equal("HB-000003", digest.Messages[0].DocId);
        Assert.Equal("HB-000004", relevant.Messages[1].DocId);
        Assert.Equal("HB-000005", digest.Messages[1].DocId);
    }

    [Fact]
    public void Assign_RemapsLinksAndDropsUnknown()
    {
        var (draft, plan) = ThreeThreadPlan();
        PlanNumbering.Assign(plan, draft.Guide!);

        var digest = plan.Threads.Single(t => t.Subject == "Weekly digest");
        Assert.Equal(new[] { "T-0001" }, digest.LinkedThreadIds);
        Assert.Equal(new[] { "T-0002" }, plan.Threads[0].LinkedThreadIds);
    }

    [Fact]
    public void Assign_AttachmentDocIdsFollowParent()
    {
        var (draft, plan) = ThreeThreadPlan();
        PlanNumbering.Assign(plan, draft.Guide!);

        var root = plan.Threads[0].Messages[0];
        Assert.Equal(new[] { "HB-000001.0001", "HB-000001.0002" }, root.Attachments.Select(a => a.DocId));
    }

    [Fact]
    public void Assign_CustodiansAndPaths()
    {
        var (draft, plan) = ThreeThreadPlan();
        PlanNumbering.Assign(plan, draft.Guide!);

        var root = plan.Threads[0].Messages[0];
        Assert.Equal(new[] { "CH-001", "CH-002", "CH-005" }, root.CustodianIds);
        Assert.Equal("Mailboxes/maria.delgado/Sent Items/HB-000001.eml", root.FilePath);
        Assert.Equal("CH-001", plan.Threads[0].OwnerCustodianId);

        var digest = plan.Threads.Single(t => t.Subject == "Weekly digest");
        var inbound = digest.Messages[0];
        Assert.Equal(new[] { "CH-001" }, inbound.CustodianIds);
        Assert.Equal("Mailboxes/maria.delgado/Inbox/HB-000003.eml", inbound.FilePath);
        Assert.Equal("CH-001", digest.OwnerCustodianId);

        foreach (var message in plan.AllMessages())
        {
            Assert.False(Path.IsPathRooted(message.FilePath));
            Assert.DoesNotContain("..", message.FilePath);
            Assert.DoesNotContain("\\", message.FilePath);
            Assert.DoesNotContain(":", message.FilePath);
            Assert.DoesNotContain("Relevant", message.FilePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CustodianFolder_SanitizesLocalPart()
    {
        var empty = new Dictionary<string, string>();
        Assert.Equal("evil", PlanNumbering.CustodianFolder(new CastMember { Id = "CH-001", Email = "../evil@x.com" }, empty));
        Assert.Equal("CH-009", PlanNumbering.CustodianFolder(new CastMember { Id = "CH-009", Email = "" }, empty));
        Assert.Equal("CH-009", PlanNumbering.CustodianFolder(new CastMember { Id = "CH-009", Email = "...@x.com" }, empty));
        Assert.Equal("ab.b", PlanNumbering.CustodianFolder(new CastMember { Id = "CH-002", Email = "A:B\\..\\.b@x.com" }, empty));

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var email in new[] { "../evil@x.com", "con@x.com", "a<b>|c?*\"@x.com", "  .hidden.@x.com", "émile.z@x.com" })
        {
            var folder = PlanNumbering.CustodianFolder(new CastMember { Id = "CH-003", Email = email }, empty);
            Assert.NotEmpty(folder);
            Assert.DoesNotContain("..", folder);
            Assert.False(folder.StartsWith('.') || folder.EndsWith('.'));
            Assert.True(folder.IndexOfAny(invalid) < 0, folder);
            Assert.NotEqual("con", folder, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CustodianFolder_DuplicateLocalPartGetsSuffix()
    {
        var (draft, plan) = ThreeThreadPlan();
        var guide = draft.Guide!;
        guide.FindCast("CH-002")!.Email = "maria.delgado@harmon.com";
        PlanNumbering.Assign(plan, guide);

        var reply = plan.Threads[0].Messages[1];
        Assert.Equal("CH-002", reply.FromId);
        Assert.Equal("Mailboxes/maria.delgado-CH-002/Sent Items/HB-000004.eml", reply.FilePath);
        Assert.Equal("Mailboxes/maria.delgado/Sent Items/HB-000001.eml", plan.Threads[0].Messages[0].FilePath);

        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["maria.delgado"] = "CH-001" };
        Assert.Equal("maria.delgado", PlanNumbering.CustodianFolder(guide.FindCast("CH-001")!, taken));
        Assert.Equal("maria.delgado-CH-002", PlanNumbering.CustodianFolder(guide.FindCast("CH-002")!, taken));
    }

    [Fact]
    public void Assign_IsIdempotent()
    {
        var (draft, plan) = ThreeThreadPlan();
        PlanNumbering.Assign(plan, draft.Guide!);
        var first = Snapshot(plan);

        PlanNumbering.Assign(plan, draft.Guide!);
        Assert.Equal(first, Snapshot(plan));
    }

    [Fact]
    public void Assign_DoesNotTouchGuideCounters()
    {
        var (draft, plan) = ThreeThreadPlan();
        var before = new Dictionary<string, int>(draft.Guide!.IdCounters);
        var hash = draft.Guide.ComputeHash();
        PlanNumbering.Assign(plan, draft.Guide);

        Assert.Equal(before, draft.Guide.IdCounters);
        Assert.Equal(hash, draft.Guide.ComputeHash());
    }

    [Fact]
    public void Assign_EmptyPrefixFallsBackToRd()
    {
        var (draft, plan) = ThreeThreadPlan();
        plan.DocIdPrefix = "";
        PlanNumbering.Assign(plan, draft.Guide!);

        Assert.Equal("RD-000001", plan.Threads[0].Messages[0].DocId);
    }

    private static List<string> Snapshot(CorpusPlan plan) =>
        plan.Threads.SelectMany(t => new[] { t.Id, t.OwnerCustodianId, string.Join(",", t.LinkedThreadIds) }
            .Concat(t.Messages.SelectMany(m => new[] { m.Id, m.DocId, m.FilePath, string.Join(",", m.CustodianIds) }
                .Concat(m.Attachments.Select(a => a.DocId))))).ToList();
}
