using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusPlanValidatorTests
{
    private static readonly TimeZoneInfo Chicago = BusinessCalendar.ResolveId("America/Chicago")!;
    private static readonly TimeZoneInfo Phoenix = BusinessCalendar.ResolveId("America/Phoenix")!;

    private static (CaseDraft Draft, CorpusPlan Plan) Fixture()
    {
        var draft = CorpusTestData.RichDraft();
        return (draft, PlanFixtures.Clone(PlanFixtures.ValidPlan(draft)));
    }

    private static List<PlanFinding> Run(CaseDraft draft, CorpusPlan plan, bool mediaAvailable = true)
    {
        var findings = CorpusPlanValidator.Validate(draft, plan, mediaAvailable);
        Assert.All(findings, f => Assert.Equal(IssueSeverity.Warning, f.Severity));
        Assert.All(findings, f => Assert.False(string.IsNullOrWhiteSpace(f.FixHint)));
        return findings;
    }

    private static PlanFinding One(List<PlanFinding> findings, PlanArea area, string entityId, string message)
    {
        var finding = Assert.Single(findings);
        Assert.Equal(message, finding.Message);
        Assert.Equal(area, finding.Area);
        Assert.Equal(entityId, finding.EntityId);
        Assert.Equal(IssueSeverity.Warning, finding.Severity);
        Assert.False(string.IsNullOrWhiteSpace(finding.FixHint));
        return finding;
    }

    private static PlannedThread ThreadOn(CorpusPlan plan, DateOnly day) =>
        plan.Threads.Single(t => DateOnly.FromDateTime(t.Messages[0].SentAt.DateTime) == day);

    private static PlannedThread FactThread(CorpusPlan plan, string factId) => plan.Threads.Single(t => t.FactIds.Contains(factId));

    private static void Restamp(CaseDraft draft, CorpusPlan plan) => plan.GuideHash = draft.Guide!.ComputeHash();

    [Fact]
    public void Good_IsClean()
    {
        var (draft, plan) = Fixture();
        Assert.Empty(CorpusPlanValidator.Validate(draft, plan, mediaAvailable: true));
        Assert.Equal(20, plan.EmailCount);
    }

    [Fact]
    public void NullSafe_NullDraftOrPlan()
    {
        Assert.Empty(CorpusPlanValidator.Validate(null, null, false));
        Assert.Empty(CorpusPlanValidator.Validate(CorpusTestData.RichDraft(), null, false));
        Assert.Empty(CorpusPlanValidator.Validate(new CaseDraft(), new CorpusPlan(), false));
    }

    [Fact]
    public void NullSafe_NullHeavyJsonAfterNormalize()
    {
        const string json = """
            {"Settings":null,"Threads":[null,{"Id":null,"IssueIds":null,"FactIds":null,"LinkedThreadIds":null,
            "Messages":[null,{"Id":null,"DocId":null,"FromId":null,"ToIds":null,"CcIds":null,"FactIds":null,
            "Attachments":[null,{"DocId":null,"FileName":null}]}]}],"Externals":[null,{"Id":null}],"Notes":[null,"n"]}
            """;
        var plan = JsonSerializer.Deserialize<CorpusPlan>(json, CaseDraft.JsonOptions)!;
        plan.Normalize();

        var findings = CorpusPlanValidator.Validate(CorpusTestData.RichDraft(), plan, false);

        Assert.NotEmpty(findings);
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("A check could not run", StringComparison.Ordinal));
    }

    [Fact]
    public void NullSafe_EmptyPlanDoesNotThrow()
    {
        var findings = CorpusPlanValidator.Validate(CorpusTestData.RichDraft(), new CorpusPlan { Settings = null! }, true);
        Assert.Contains(findings, f => f.Message == "The plan is out of date with the case guide.");
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("A check could not run", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule01_EmailCount()
    {
        var (draft, plan) = Fixture();
        plan.Settings.TotalEmails = 21;

        One(Run(draft, plan), PlanArea.Plan, "", "The plan has 20 emails; the settings ask for 21.");
    }

    [Fact]
    public void Rule02_RelevantRatio()
    {
        var (draft, plan) = Fixture();
        plan.Settings.RelevantPercent = 50;

        var finding = One(Run(draft, plan), PlanArea.Plan, "", "The plan has 8 relevant emails (40%); the settings ask for 10 (50%).");
        Assert.Contains("raise % relevant", finding.FixHint);
    }

    [Fact]
    public void Rule03_InactiveParticipant()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 5, 14));
        thread.Messages[0].SentAt = BusinessCalendar.At(Chicago, new DateOnly(2024, 6, 18), new TimeOnly(10, 0));

        var finding = One(Run(draft, plan), PlanArea.Custodian, "CH-009",
            "Owen Reilly (CH-009) is on an email dated 2024-06-18 but is only active 2024-05-01 to 2024-06-15.");
        Assert.Equal("Custodian · CH-009", finding.Where);
    }

    [Fact]
    public void Rule04_FactWithoutEvidence()
    {
        var (draft, plan) = Fixture();
        draft.Guide!.Facts.Add(new KeyFact
        {
            Id = "F-009",
            Date = "2024-06-25",
            Title = "Extra fact",
            IssueIds = new List<string> { "ISS-01" },
            ActorIds = new List<string> { "CH-001" },
            KnownByIds = new List<string> { "CH-001" }
        });
        Restamp(draft, plan);

        var finding = One(Run(draft, plan), PlanArea.Facts, "F-009", "No planned email is evidence of F-009 (Extra fact).");
        Assert.Equal("Key Facts · F-009", finding.Where);
    }

    [Fact]
    public void Rule05_OutsideWindow()
    {
        var (draft, plan) = Fixture();
        plan.Settings.WindowEnd = "2024-07-01";
        var thread = FactThread(plan, "F-008");

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has an email dated 2024-07-05, outside the date window (2023-12-15 to 2024-07-01).");
    }

    [Fact]
    public void Rule06_OutsideBusinessHours()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 2, 20));
        thread.Messages[0].SentAt = BusinessCalendar.At(Chicago, new DateOnly(2024, 2, 20), new TimeOnly(18, 0));

        var finding = One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has an email sent outside business hours (2024-02-20 18:00 {Chicago.Id}).");
        Assert.Equal($"Thread {thread.Id}", finding.Where);
    }

    [Fact]
    public void Rule06_Holiday()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 6, 11));
        thread.Messages[0].SentAt = BusinessCalendar.At(Phoenix, new DateOnly(2024, 7, 4), new TimeOnly(10, 0));

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has an email sent outside business hours (2024-07-04 10:00 {Phoenix.Id}).");
    }

    [Fact]
    public void Rule06_ExternalSenderUsesDefaultZone()
    {
        var (draft, plan) = Fixture();
        var eastern = BusinessCalendar.ResolveId(UsTimeZones.Default)!;
        var thread = ThreadOn(plan, new DateOnly(2024, 3, 12));
        thread.Messages[0].SentAt = BusinessCalendar.At(eastern, new DateOnly(2024, 3, 12), new TimeOnly(8, 30));

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has an email sent outside business hours (2024-03-12 08:30 {eastern.Id}).");
    }

    [Fact]
    public void Rule07_RepliesOutOfOrder()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 4, 9));
        thread.Messages[0].ReplyToSeq = 1;

        One(Run(draft, plan), PlanArea.Thread, thread.Id, $"Thread {thread.Id} has replies out of date order.");
    }

    [Fact]
    public void Rule08_UnknownPerson()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 4, 9));
        thread.Messages[0].ToIds = new List<string> { "CH-099" };

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} refers to CH-099, which is not in the case guide or the plan.");
    }

    [Fact]
    public void Rule08_UnknownLinkedThread()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 4, 9));
        thread.LinkedThreadIds.Add("T-9999");

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} refers to T-9999, which is not in the case guide or the plan.");
    }

    [Fact]
    public void Rule09_HotOnNonRelevant()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 4, 9));
        thread.Messages[0].IsHot = true;

        One(Run(draft, plan), PlanArea.Thread, thread.Id, $"Thread {thread.Id} has inconsistent labels: hot but not relevant.");
    }

    [Fact]
    public void Rule09_RelevantWithoutFacts()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-004");
        thread.FactIds.Clear();

        One(Run(draft, plan), PlanArea.Thread, thread.Id, $"Thread {thread.Id} has inconsistent labels: relevant with no key facts.");
    }

    [Fact]
    public void Rule09_HotWithoutHotFact()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-002");
        thread.Messages[0].IsHot = true;

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has inconsistent labels: {thread.Messages[0].Id} is hot but carries no hot fact.");
    }

    [Fact]
    public void Rule10_FactBeforeItHappened()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-004");
        thread.Messages[0].SentAt = BusinessCalendar.At(Chicago, new DateOnly(2024, 4, 19), new TimeOnly(10, 0));

        One(Run(draft, plan), PlanArea.Facts, "F-004", $"Thread {thread.Id} mentions F-004 before it happened (2024-04-22).");
    }

    [Fact]
    public void Rule11_ParticipantDidNotKnow()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-002");
        thread.Messages[0].ToIds = new List<string> { "CH-003" };

        One(Run(draft, plan), PlanArea.Thread, thread.Id, $"Raymond Harmon is on thread {thread.Id} but did not know about F-002.");
    }

    [Fact]
    public void Rule11_NoteToSelfIsSkipped()
    {
        var (draft, plan) = Fixture();
        draft.Guide!.FindFact("F-008")!.KnownByIds.Clear();
        draft.Guide.FindFact("F-008")!.ActorIds.Clear();
        Restamp(draft, plan);

        Assert.Empty(Run(draft, plan));
    }

    [Fact]
    public void Rule12_PrivilegedWithoutCounsel()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-002");
        thread.IsPrivileged = true;

        One(Run(draft, plan), PlanArea.Thread, thread.Id, $"Privileged thread {thread.Id} has no counsel.");
    }

    [Fact]
    public void Rule12_PrivilegedWithOtherSide()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-001");
        thread.Messages[0].CcIds = new List<string> { "CH-003" };

        One(Run(draft, plan), PlanArea.Thread, thread.Id, $"Privileged thread {thread.Id} includes Raymond Harmon from the other side.");
    }

    [Fact]
    public void Rule12_OutsideCounselForClientIsFine()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-001");
        draft.Guide!.FindFact("F-001")!.KnownByIds.Add("CH-006");
        Restamp(draft, plan);
        thread.Messages[0].CcIds = new List<string> { "CH-006" };

        Assert.Empty(Run(draft, plan));
    }

    [Fact]
    public void Rule13_MediaWithoutMedia()
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-003");
        thread.Messages[0].Attachments[0].Kind = AttachmentType.Image;

        One(Run(draft, plan, mediaAvailable: false), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has an attachment the settings do not allow (Image).");
        Assert.Empty(Run(draft, plan, mediaAvailable: true));
    }

    [Theory]
    [InlineData("..\\evil.docx")]
    [InlineData("a|b.docx")]
    [InlineData("")]
    public void Rule13_BadFileName(string fileName)
    {
        var (draft, plan) = Fixture();
        var thread = FactThread(plan, "F-003");
        thread.Messages[0].Attachments[0].FileName = fileName;

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} has an attachment the settings do not allow ({(fileName.Length == 0 ? "no file name" : fileName)}).");
    }

    [Fact]
    public void Rule14_DuplicateDocId()
    {
        var (draft, plan) = Fixture();
        var first = plan.Threads[0].Messages[0];
        var second = plan.Threads[1].Messages[0];
        second.DocId = first.DocId;

        One(Run(draft, plan), PlanArea.Plan, "", $"DocIDs are not unique and in order ({first.DocId} is used twice).");
    }

    [Fact]
    public void Rule14_AttachmentDocId()
    {
        var (draft, plan) = Fixture();
        var message = FactThread(plan, "F-003").Messages[0];
        message.Attachments[0].DocId = message.DocId + ".0002";

        One(Run(draft, plan), PlanArea.Plan, "",
            $"DocIDs are not unique and in order (attachment {message.DocId}.0002 does not follow {message.DocId}).");
    }

    [Fact]
    public void Rule15_Mix()
    {
        var (draft, plan) = Fixture();
        plan.Settings.DecoyPercent = 30;
        plan.Settings.DailyBusinessPercent = 30;

        var findings = Run(draft, plan);

        Assert.Contains(findings, f => f.Message == "Custodians' daily business is 50% of other email; the settings ask for 30%.");
        Assert.Contains(findings, f => f.Message == "Near-miss decoys is 8.3% of other email; the settings ask for 30%.");
        Assert.All(findings, f => Assert.Equal(PlanArea.Plan, f.Area));
    }

    [Fact]
    public void Rule16_DepthCap()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 4, 9));
        var root = thread.Messages[0];
        for (var seq = 2; seq <= 13; seq++)
        {
            thread.Messages.Add(new PlannedMessage
            {
                Id = $"{thread.Id}-M{seq:00}",
                Seq = seq,
                ReplyToSeq = seq - 1,
                Kind = MessageKind.Reply,
                FromId = seq % 2 == 0 ? "CH-003" : "CH-004",
                ToIds = new List<string> { seq % 2 == 0 ? "CH-004" : "CH-003" },
                SentAt = root.SentAt.AddMinutes(20 * (seq - 1))
            });
        }

        var findings = Run(draft, plan);

        Assert.Contains(findings, f => f.Message == $"Thread {thread.Id} has 13 emails (more than 12)." && f.EntityId == thread.Id);
    }

    [Fact]
    public void Rule17_FallbackContent()
    {
        var (draft, plan) = Fixture();
        var thread = ThreadOn(plan, new DateOnly(2024, 4, 9));
        thread.UsesFallbackContent = true;

        One(Run(draft, plan), PlanArea.Thread, thread.Id,
            $"Thread {thread.Id} uses a placeholder subject because the model did not plan it.");
    }

    [Fact]
    public void Rule17_PlanNotes()
    {
        var (draft, plan) = Fixture();
        plan.Notes.Add("Two decoy slots were filled from templates.");

        var finding = One(Run(draft, plan), PlanArea.Plan, "", "Two decoy slots were filled from templates.");
        Assert.Equal("Click Re-plan if this matters for your demo.", finding.FixHint);
        Assert.Equal("Plan", finding.Where);
    }

    [Fact]
    public void Rule18_Leakage_SenderHour()
    {
        var (draft, plan) = Fixture();
        foreach (var thread in plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant))
        {
            var message = thread.Messages[0];
            message.SentAt = message.SentAt.AddHours(6);
        }

        One(Run(draft, plan), PlanArea.Plan, "",
            "Relevant email differs from everyday email in sender hour (16 vs 10), which can give the labels away.");
    }

    [Fact]
    public void Leakage_ZeroDenominator()
    {
        var (draft, plan) = Fixture();
        ThreadOn(plan, new DateOnly(2024, 5, 14)).Messages[0].Attachments.Clear();

        Assert.Empty(Run(draft, plan));
    }

    [Fact]
    public void Rule19_Stale()
    {
        var (draft, plan) = Fixture();
        plan.GuideHash = "old";

        var finding = One(Run(draft, plan), PlanArea.Plan, "", "The plan is out of date with the case guide.");
        Assert.Contains("Keep This Plan", finding.FixHint);
    }

    [Fact]
    public void Rule19_DismissedStaleIsQuiet()
    {
        var (draft, plan) = Fixture();
        draft.Guide!.Facts[0].Title += " (edited)";
        draft.Corpus = new CorpusState { DismissedStaleGuideHash = draft.Guide.ComputeHash() };

        Assert.Empty(Run(draft, plan));
    }

    [Fact]
    public void AllWarnings_MessyPlan()
    {
        var (draft, plan) = Fixture();
        plan.Settings.TotalEmails = 500;
        plan.GuideHash = "x";
        plan.Threads[0].IsPrivileged = true;
        plan.Threads[1].Messages[0].ToIds.Add("EXT-404");
        plan.Threads[2].UsesFallbackContent = true;
        plan.Threads[3].Messages[0].SentAt = plan.Threads[3].Messages[0].SentAt.AddHours(12);

        var findings = CorpusPlanValidator.Validate(draft, plan, false);

        Assert.True(findings.Count >= 5);
        Assert.All(findings, f => Assert.Equal(IssueSeverity.Warning, f.Severity));
        Assert.All(findings, f => Assert.False(string.IsNullOrWhiteSpace(f.FixHint)));
    }

    [Fact]
    public void BrokenRule_OthersStillReport()
    {
        var (draft, plan) = Fixture();
        plan.Threads[0].Messages[0].ToIds = null!;
        plan.GuideHash = "x";

        var findings = CorpusPlanValidator.Validate(draft, plan, true);

        var broken = findings.Where(f => f.Message.StartsWith("A check could not run: ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(broken);
        Assert.All(broken, f => Assert.EndsWith(".", f.Message));
        Assert.All(broken, f => Assert.Equal("Click Re-plan.", f.FixHint));
        Assert.Contains(findings, f => f.Message == "The plan is out of date with the case guide.");
    }
}
