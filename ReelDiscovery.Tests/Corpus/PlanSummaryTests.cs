using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class PlanSummaryTests
{
    private static readonly TimeSpan Chicago = TimeSpan.FromHours(-6);

    private static PlannedMessage Msg(int seq, string from, string to, DateTimeOffset sentAt, string tz = "America/Chicago",
        string[]? facts = null, bool hot = false, params AttachmentType[] attachments)
    {
        var custodians = new List<string>();
        if (from.StartsWith("CH-"))
            custodians.Add(from);
        if (to.StartsWith("CH-"))
            custodians.Add(to);

        return new PlannedMessage
        {
            Id = $"M{seq:00}",
            Seq = seq,
            FromId = from,
            ToIds = new List<string> { to },
            SentAt = sentAt,
            TimeZoneId = tz,
            FactIds = (facts ?? Array.Empty<string>()).ToList(),
            Basis = facts == null ? RelevanceBasis.Quoted : RelevanceBasis.Own,
            IsHot = hot,
            CustodianIds = custodians,
            Attachments = attachments.Select((k, i) => new PlannedAttachment { Kind = k, FileName = $"a{i}" }).ToList()
        };
    }

    private static DateTimeOffset At(int y, int m, int d, int h = 10) => new(y, m, d, h, 0, 0, Chicago);

    /// <summary>
    /// 3 relevant threads (6 emails) and 2 non-relevant threads (DailyBusiness 3, Decoy 1 with ISS-01).
    /// </summary>
    private static CorpusPlan InlinePlan()
    {
        var plan = new CorpusPlan
        {
            PlanId = Guid.NewGuid(),
            Settings = new CorpusSettings { WindowStart = "2023-12-15", WindowEnd = "2024-08-20" }
        };

        plan.Threads.Add(new PlannedThread
        {
            Id = "T-0001",
            Relevance = ThreadRelevance.Relevant,
            IssueIds = new List<string> { "ISS-01" },
            FactIds = new List<string> { "F-001" },
            Messages = new List<PlannedMessage>
            {
                Msg(1, "CH-001", "CH-003", At(2024, 1, 15), facts: new[] { "F-001" }),
                Msg(2, "CH-003", "CH-001", At(2024, 1, 16), "America/Phoenix", new[] { "F-001" })
            }
        });
        plan.Threads.Add(new PlannedThread
        {
            Id = "T-0002",
            Relevance = ThreadRelevance.Relevant,
            IsPrivileged = true,
            IssueIds = new List<string> { "ISS-01" },
            FactIds = new List<string> { "F-003" },
            Messages = new List<PlannedMessage>
            {
                Msg(1, "CH-004", "CH-005", At(2024, 3, 18), "America/Phoenix", new[] { "F-003" }, true,
                    AttachmentType.Word, AttachmentType.CalendarInvite),
                // 2024-04-01 03:30 UTC is still March 31 in Chicago
                Msg(2, "CH-005", "CH-004", new DateTimeOffset(2024, 4, 1, 3, 30, 0, TimeSpan.Zero))
            }
        });
        plan.Threads.Add(new PlannedThread
        {
            Id = "T-0003",
            Relevance = ThreadRelevance.Relevant,
            IssueIds = new List<string> { "ISS-01", "ISS-02" },
            FactIds = new List<string> { "F-005" },
            Messages = new List<PlannedMessage>
            {
                Msg(1, "CH-001", "CH-004", At(2024, 5, 10), facts: new[] { "F-005" }),
                Msg(2, "CH-004", "CH-001", At(2024, 5, 13), "America/Phoenix", new[] { "F-005", "F-006" })
            }
        });
        plan.Threads.Add(new PlannedThread
        {
            Id = "T-0004",
            Relevance = ThreadRelevance.NotRelevant,
            Category = NonRelevantCategory.DailyBusiness,
            Messages = new List<PlannedMessage>
            {
                Msg(1, "CH-008", "CH-004", At(2024, 6, 3), "America/Phoenix"),
                Msg(2, "CH-008", "CH-004", At(2024, 6, 4), "America/Phoenix"),
                Msg(3, "CH-008", "CH-004", At(2024, 6, 5), "America/Phoenix")
            }
        });
        plan.Threads.Add(new PlannedThread
        {
            Id = "T-0005",
            Relevance = ThreadRelevance.NotRelevant,
            Category = NonRelevantCategory.Decoy,
            IssueIds = new List<string> { "ISS-01" },
            Messages = new List<PlannedMessage> { Msg(1, "EXT-001", "CH-002", At(2024, 8, 5), "America/New_York") }
        });

        return plan;
    }

    private static PlanSummary Summary() => PlanSummary.From(InlinePlan(), CorpusTestData.RichDraft().Guide!);

    [Fact]
    public void Scalars()
    {
        var s = Summary();

        Assert.Equal(10, s.Emails);
        Assert.Equal(5, s.Threads);
        Assert.Equal(6, s.Relevant);
        Assert.Equal(4, s.NonRelevant);
        Assert.Equal(60.0m, s.RelevantPercent);
        Assert.Equal(1, s.HotEmails);
        Assert.Equal(1, s.HotFacts);
        Assert.Equal(1, s.PrivilegedThreads);
        Assert.Equal(2, s.PrivilegedEmails);
        Assert.Equal(2, s.Attachments);
        Assert.Equal(1, s.EmailsWithAttachments);
        Assert.Equal(10.0m, s.AttachmentPercent);
        Assert.Equal(1, s.AttachmentsByType[AttachmentType.Word]);
        Assert.Equal(1, s.AttachmentsByType[AttachmentType.CalendarInvite]);
        Assert.False(s.AttachmentsByType.ContainsKey(AttachmentType.Excel));
        Assert.Equal(6, s.Custodians);
    }

    [Fact]
    public void RelevantPercent_RoundsToOneDecimal()
    {
        var plan = InlinePlan();
        plan.Threads[3].Messages.Add(Msg(4, "CH-008", "CH-004", At(2024, 6, 6), "America/Phoenix"));
        plan.Threads[3].Messages.Add(Msg(5, "CH-008", "CH-004", At(2024, 6, 7), "America/Phoenix"));
        plan.Threads[3].Messages.Add(Msg(6, "CH-008", "CH-004", At(2024, 6, 10), "America/Phoenix"));

        var s = PlanSummary.From(plan, CorpusTestData.RichDraft().Guide!);
        Assert.Equal(13, s.Emails);
        Assert.Equal(46.2m, s.RelevantPercent);
    }

    [Fact]
    public void Months_CoverWholeWindow_SenderLocal()
    {
        var s = Summary();

        Assert.Equal(9, s.Months.Count);
        Assert.Equal(new DateOnly(2023, 12, 1), s.Months[0].Month);
        Assert.Equal(new DateOnly(2024, 8, 1), s.Months[^1].Month);
        Assert.Equal(s.Emails, s.Months.Sum(m => m.Total));

        int R(int month) => s.Months.Single(m => m.Month == new DateOnly(2024, month, 1)).Relevant;
        int N(int month) => s.Months.Single(m => m.Month == new DateOnly(2024, month, 1)).NonRelevant;
        Assert.Equal(2, R(1));
        Assert.Equal(0, R(2));
        Assert.Equal(2, R(3));
        Assert.Equal(0, R(4));
        Assert.Equal(2, R(5));
        Assert.Equal(3, N(6));
        Assert.Equal(0, s.Months.Single(m => m.Month == new DateOnly(2024, 7, 1)).Total);
        Assert.Equal(1, N(8));
        Assert.Equal(0, s.Months[0].Total);
    }

    [Fact]
    public void Months_ExtendToMessagesOutsideWindow()
    {
        var plan = InlinePlan();
        plan.Settings.WindowStart = "2024-02-01";
        plan.Settings.WindowEnd = "bad";

        var s = PlanSummary.From(plan, CorpusTestData.RichDraft().Guide!);
        Assert.Equal(new DateOnly(2024, 1, 1), s.Months[0].Month);
        Assert.Equal(new DateOnly(2024, 8, 1), s.Months[^1].Month);
        Assert.Equal(s.Emails, s.Months.Sum(m => m.Total));
    }

    [Fact]
    public void ByIssue()
    {
        var s = Summary();

        Assert.Equal(new[] { "ISS-01", "ISS-02" }, s.ByIssue.Select(r => r.IssueId));
        var iss1 = s.ByIssue[0];
        Assert.Equal(6, iss1.Relevant);
        Assert.Equal(1, iss1.NotRelevant);
        Assert.Equal(7, iss1.Total);
        Assert.False(string.IsNullOrEmpty(iss1.Title));
        var iss2 = s.ByIssue[1];
        Assert.Equal("Prompt payment violation", iss2.Title);
        Assert.Equal(2, iss2.Relevant);
        Assert.Equal(0, iss2.NotRelevant);
        Assert.Equal(2, iss2.Total);
    }

    [Fact]
    public void ByFact()
    {
        var s = Summary();

        Assert.Equal(8, s.ByFact.Count);
        Assert.Equal(s.ByFact.Select(r => r.Date).OrderBy(d => d, StringComparer.Ordinal), s.ByFact.Select(r => r.Date));

        FactCountRow F(string id) => s.ByFact.Single(r => r.FactId == id);
        Assert.Equal((1, 2, 0), (F("F-001").Threads, F("F-001").Emails, F("F-001").Hot));
        Assert.Equal((0, 0, 0), (F("F-002").Threads, F("F-002").Emails, F("F-002").Hot));
        Assert.Equal((1, 1, 1), (F("F-003").Threads, F("F-003").Emails, F("F-003").Hot));
        Assert.Equal((1, 2, 0), (F("F-005").Threads, F("F-005").Emails, F("F-005").Hot));
        Assert.Equal((0, 1, 0), (F("F-006").Threads, F("F-006").Emails, F("F-006").Hot));
        Assert.Equal("2024-03-18", F("F-003").Date);
        Assert.Equal("Harmon approves Change Order 7", F("F-003").Title);
    }

    [Fact]
    public void ByCustodian()
    {
        var s = Summary();

        Assert.Equal(new[] { "CH-008", "CH-004", "CH-001", "CH-005", "CH-003", "CH-002" }, s.ByCustodian.Select(r => r.CastId));
        var nair = s.ByCustodian[0];
        Assert.Equal("Priya Nair", nair.Name);
        Assert.Equal((0, 3, 3), (nair.Relevant, nair.NotRelevant, nair.Total));
        Assert.False(string.IsNullOrEmpty(nair.Email));
        Assert.False(string.IsNullOrEmpty(nair.Organization));
        var becker = s.ByCustodian.Single(r => r.CastId == "CH-002");
        Assert.Equal((0, 1, 1), (becker.Relevant, becker.NotRelevant, becker.Total));
        Assert.Equal(s.Emails, s.ByCustodian.Sum(r => r.Total));
    }

    [Fact]
    public void EmptyPlan_NoThrow()
    {
        var s = PlanSummary.From(new CorpusPlan { Settings = new CorpusSettings() }, new CaseGuide());

        Assert.Equal(0, s.Emails);
        Assert.Equal(0m, s.RelevantPercent);
        Assert.Equal(0m, s.AttachmentPercent);
        Assert.Empty(s.Months);
        Assert.Empty(s.ByFact);
    }

    private static CorpusPlan CostPlan()
    {
        var relevant = new PlannedThread { Relevance = ThreadRelevance.Relevant };
        for (var i = 0; i < 1_000; i++)
            relevant.Messages.Add(new PlannedMessage { Seq = i + 1 });
        var other = new PlannedThread { Relevance = ThreadRelevance.NotRelevant, Category = NonRelevantCategory.DailyBusiness };
        for (var i = 0; i < 4_000; i++)
            other.Messages.Add(new PlannedMessage { Seq = i + 1 });

        var kinds = new[] { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint };
        for (var i = 0; i < 800; i++)
            other.Messages[i].Attachments.Add(new PlannedAttachment { Kind = kinds[i % 3] });
        var small = new[] { AttachmentType.CalendarInvite, AttachmentType.Image, AttachmentType.Voicemail };
        for (var i = 0; i < 200; i++)
            relevant.Messages[i].Attachments.Add(new PlannedAttachment { Kind = small[i % 3] });

        return new CorpusPlan { Threads = new List<PlannedThread> { relevant, other } };
    }

    [Fact]
    public void Cost_Estimate()
    {
        var model = new AIModelConfig { DisplayName = "Test Model", InputTokenPricePerMillion = 4m, OutputTokenPricePerMillion = 20m };

        var estimate = GenerationCostEstimator.Estimate(CostPlan(), model);

        Assert.Equal(1_000 * model.CalculateTotalCost(4_000, 450) * 1.15m, estimate.RelevantCost);
        Assert.Equal(4_000 * model.CalculateTotalCost(1_500, 300) * 1.15m, estimate.OtherCost);
        Assert.Equal((800 * model.CalculateTotalCost(3_000, 1_500) + 200 * model.CalculateTotalCost(800, 200)) * 1.15m,
            estimate.AttachmentCost);
        Assert.Equal(estimate.RelevantCost + estimate.OtherCost + estimate.AttachmentCost, estimate.Total);
        Assert.Equal(28.75m, estimate.RelevantCost);
        Assert.Equal(124.246m, estimate.Total);
        Assert.Equal("Test Model", estimate.ModelName);
    }

    [Fact]
    public void Cost_NullModel_Zero()
    {
        var estimate = GenerationCostEstimator.Estimate(CostPlan(), null);

        Assert.Equal(0m, estimate.Total);
        Assert.Equal(string.Empty, estimate.ModelName);
    }
}
