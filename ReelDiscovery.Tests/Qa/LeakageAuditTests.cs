using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Qa;

public class LeakageAuditTests
{
    private static readonly string[] Senders =
        Enumerable.Range(1, 8).Select(i => $"Person {i} <p{i}@acme.com>").ToArray();

    private static readonly DateTimeOffset Start = new(2024, 1, 1, 9, 0, 0, TimeSpan.FromHours(-5));

    // Neutral metadata from index arithmetic only; the caller sets relevance and any leaking field
    private static AnswerKeyRow Email(int i, string threadId, bool relevant, int seqInThread = 0)
    {
        var sent = Start.AddDays(i % 90).AddHours(i % 9).AddMinutes(seqInThread * 37);
        return new AnswerKeyRow
        {
            DocId = $"RD-{i + 1:000000}",
            DocType = AnswerKeyBuilder.DocTypeEmail,
            DateSent = sent.ToString(AnswerKeyBuilder.DateFormat, CultureInfo.InvariantCulture),
            From = Senders[i % Senders.Length],
            To = Senders[(i + 3) % Senders.Length],
            Cc = i % 3 == 0 ? Senders[(i + 5) % Senders.Length] : string.Empty,
            Subject = (i % 4) switch { 0 => "Lunch", 1 => "Status update on the schedule", 2 => "Question", _ => "Follow-up regarding the quarterly site meeting agenda and minutes" },
            ThreadId = threadId,
            FamilyId = $"RD-{i + 1:000000}",
            Relevance = relevant ? AnswerKeyBuilder.RelevantLabel : AnswerKeyBuilder.NotRelevantLabel,
            Privileged = "N",
            Status = AnswerKeyBuilder.StatusPlanned
        };
    }

    private static AnswerKeyRow Child(AnswerKeyRow parent, int k, string fileName) => new()
    {
        DocId = $"{parent.DocId}.{k:0000}",
        DocType = AnswerKeyBuilder.DocTypeAttachment,
        FileName = fileName,
        DateSent = parent.DateSent,
        From = parent.From,
        To = parent.To,
        Cc = parent.Cc,
        Subject = parent.Subject,
        ThreadId = parent.ThreadId,
        FamilyId = parent.DocId,
        ParentDocId = parent.DocId,
        Relevance = parent.Relevance,
        Privileged = parent.Privileged,
        Status = parent.Status
    };

    // One email per thread; relevance from the given rule
    private static List<AnswerKeyRow> Singletons(int count, Func<int, bool> relevant) =>
        Enumerable.Range(0, count).Select(i => Email(i, $"T-{i + 1:0000}", relevant(i))).ToList();

    private static bool Noise(int i) => RenderAssignments.StableRank($"label-{i}") % 2 == 0;

    // ----- verdicts -----

    [Theory]
    [InlineData(0.50, LeakageVerdict.Pass)]
    [InlineData(0.60, LeakageVerdict.Pass)]
    [InlineData(0.6000001, LeakageVerdict.Warning)]
    [InlineData(0.70, LeakageVerdict.Warning)]
    [InlineData(0.7000001, LeakageVerdict.Fail)]
    [InlineData(0.95, LeakageVerdict.Fail)]
    public void VerdictFor_Boundaries(double ba, LeakageVerdict expected)
    {
        Assert.Equal(expected, LeakageAudit.VerdictFor(ba, 40, 40));
    }

    [Theory]
    [InlineData(39, 100)]
    [InlineData(100, 39)]
    public void VerdictFor_BelowMinimum_NotEnoughData(int relevant, int nonRelevant)
    {
        Assert.Equal(LeakageVerdict.NotEnoughData, LeakageAudit.VerdictFor(0.99, relevant, nonRelevant));
    }

    [Fact]
    public void Constants()
    {
        Assert.Equal(0.60, LeakageAudit.PassMaxBalancedAccuracy);
        Assert.Equal(0.70, LeakageAudit.WarningMaxBalancedAccuracy);
        Assert.Equal(40, LeakageAudit.MinEmailsPerClass);
        Assert.Equal(5, LeakageAudit.Folds);
        Assert.Equal(5, LeakageAudit.MinSignalSupport);
    }

    [Fact]
    public void PrivilegeOnlyOnRelevant_Fails_AndIsTopSignal()
    {
        var rows = Singletons(200, i => i % 4 == 0);
        foreach (var row in rows.Where(r => r.Relevance == AnswerKeyBuilder.RelevantLabel))
            row.Privileged = "Y";

        var report = LeakageAudit.RunRows(rows, Guid.Empty);

        Assert.Equal(50, report.RelevantEmails);
        Assert.Equal(150, report.NonRelevantEmails);
        Assert.Equal(LeakageVerdict.Fail, report.Verdict);
        Assert.True(report.BalancedAccuracy > 0.70, $"BA {report.BalancedAccuracy}");
        var top = report.Signals[0];
        Assert.Equal("privilege banner", top.Feature);
        Assert.Equal(LeakageSignalKind.OnlyOnRelevant, top.Kind);
        Assert.Equal(1.0, top.RelevantShare);
        Assert.Equal(0.25, top.BaseRate, 10);
        Assert.Equal(50, top.Support);
    }

    [Fact]
    public void NoiseLabels_Pass()
    {
        var rows = Singletons(300, Noise);
        var report = LeakageAudit.RunRows(rows, Guid.Empty);
        Assert.Equal(LeakageVerdict.Pass, report.Verdict);
        Assert.InRange(report.BalancedAccuracy, 0.0, LeakageAudit.PassMaxBalancedAccuracy);
        Assert.Equal(LeakageAudit.Folds, report.Folds);
    }

    [Fact]
    public void ThirtyNineRelevant_NotEnoughData_EvenWithStrongLeak()
    {
        var rows = Singletons(200, i => i < 39);
        foreach (var row in rows.Where(r => r.Relevance == AnswerKeyBuilder.RelevantLabel))
            row.Privileged = "Y";

        var report = LeakageAudit.RunRows(rows, Guid.Empty);

        Assert.Equal(39, report.RelevantEmails);
        Assert.Equal(LeakageVerdict.NotEnoughData, report.Verdict);
    }

    // ----- folds -----

    [Fact]
    public void Folds_ThreadsShareOneFold()
    {
        var threadIds = new List<string>();
        var labels = new List<int>();
        for (var t = 0; t < 60; t++)
        for (var m = 0; m < 1 + t % 4; m++)
        {
            threadIds.Add($"T-{t:0000}");
            labels.Add(t % 2);
        }

        var (folds, count) = LeakageAudit.AssignFolds(threadIds, labels.ToArray());

        Assert.Equal(LeakageAudit.Folds, count);
        foreach (var group in threadIds.Select((id, i) => (id, i)).GroupBy(x => x.id))
            Assert.Single(group.Select(x => folds[x.i]).Distinct());
        for (var i = 0; i < threadIds.Count; i++)
            Assert.Equal((int)(RenderAssignments.StableRank(threadIds[i]) % (ulong)count), folds[i]);
    }

    [Fact]
    public void Folds_FoldWithoutPositive_ReducesFoldCount_MinTwo()
    {
        // One relevant thread: no fold count above 1 gives every fold a positive, so the count bottoms out at 2
        var threadIds = Enumerable.Range(0, 30).Select(t => $"T-{t:0000}").ToList();
        var labels = threadIds.Select((_, i) => i == 0 ? 1 : 0).ToArray();

        var (folds, count) = LeakageAudit.AssignFolds(threadIds, labels);

        Assert.Equal(2, count);
        Assert.All(folds, f => Assert.InRange(f, 0, 1));
    }

    [Fact]
    public void Folds_EveryFoldHasBothClassesWhenPossible()
    {
        var threadIds = Enumerable.Range(0, 12).Select(t => $"T-{t:0000}").ToList();
        var labels = threadIds.Select((_, i) => i % 4 == 0 ? 1 : 0).ToArray();

        var (folds, count) = LeakageAudit.AssignFolds(threadIds, labels);

        Assert.InRange(count, 2, LeakageAudit.Folds);
        for (var f = 0; f < count; f++)
        {
            var inFold = Enumerable.Range(0, labels.Length).Where(i => folds[i] == f).ToList();
            if (count > 2)
            {
                Assert.Contains(inFold, i => labels[i] == 1);
                Assert.Contains(inFold, i => labels[i] == 0);
            }
        }
    }

    // ----- rows and features -----

    [Fact]
    public void AttachmentRows_AreNotAuditRows_KindsBecomeParentFeatures()
    {
        var rows = Singletons(200, i => i % 4 == 0);
        var children = rows.Where(r => r.Relevance == AnswerKeyBuilder.RelevantLabel)
            .Select(r => Child(r, 1, "Budget.xlsx")).ToList();
        rows.AddRange(children);

        var report = LeakageAudit.RunRows(rows, Guid.Empty);

        Assert.Equal(50, report.RelevantEmails);
        Assert.Equal(150, report.NonRelevantEmails);
        var signal = Assert.Single(report.Signals, s => s.Feature == "Excel attachment");
        Assert.Equal(LeakageSignalKind.OnlyOnRelevant, signal.Kind);
        Assert.Equal(50, signal.Support);
    }

    [Fact]
    public void RecipientOnlyOnRelevant_IsSignal()
    {
        var rows = Singletons(200, Noise);
        var relevant = rows.Where(r => r.Relevance == AnswerKeyBuilder.RelevantLabel).Take(6).ToList();
        foreach (var row in relevant)
            row.Cc = string.IsNullOrEmpty(row.Cc) ? "Pat Leak <pat@leak.com>" : row.Cc + "; Pat Leak <pat@leak.com>";

        var report = LeakageAudit.RunRows(rows, Guid.Empty);

        var signal = Assert.Single(report.Signals, s => s.Feature == "recipient pat@leak.com");
        Assert.Equal(LeakageSignalKind.OnlyOnRelevant, signal.Kind);
        Assert.Equal(6, signal.Support);
    }

    [Fact]
    public void SupportBelowFive_IsNotASignal()
    {
        var rows = Singletons(200, Noise);
        foreach (var row in rows.Where(r => r.Relevance == AnswerKeyBuilder.RelevantLabel).Take(4))
            row.To = "Pat Leak <pat@leak.com>";

        var report = LeakageAudit.RunRows(rows, Guid.Empty);

        Assert.DoesNotContain(report.Signals, s => s.Feature.Contains("pat@leak.com", StringComparison.Ordinal));
    }

    [Fact]
    public void NeverOnRelevant_Signal()
    {
        var rows = Singletons(200, Noise);
        foreach (var row in rows.Where(r => r.Relevance == AnswerKeyBuilder.NotRelevantLabel).Take(8))
            row.Subject = EmailComposer.PrivilegedSubjectTag + row.Subject;

        var report = LeakageAudit.RunRows(rows, Guid.Empty);

        var signal = Assert.Single(report.Signals, s => s.Feature == "privileged subject tag");
        Assert.Equal(LeakageSignalKind.NeverOnRelevant, signal.Kind);
        Assert.Equal(0.0, signal.RelevantShare);
    }

    [Fact]
    public void GeneratedOnly_KeepsGeneratedRows_FallsBackWhenNone()
    {
        var rows = Singletons(200, Noise);
        for (var i = 0; i < 120; i++)
            rows[i].Status = i % 2 == 0 ? AnswerKeyBuilder.StatusGenerated : AnswerKeyBuilder.StatusGeneratedWithWarnings;

        var generated = LeakageAudit.RunRows(rows, Guid.Empty);
        Assert.Equal(120, generated.RelevantEmails + generated.NonRelevantEmails);

        var all = LeakageAudit.RunRows(rows, Guid.Empty, generatedOnly: false);
        Assert.Equal(200, all.RelevantEmails + all.NonRelevantEmails);

        foreach (var row in rows)
            row.Status = AnswerKeyBuilder.StatusPlanned;
        var fallback = LeakageAudit.RunRows(rows, Guid.Empty);
        Assert.Equal(200, fallback.RelevantEmails + fallback.NonRelevantEmails);
    }

    [Fact]
    public void SignalsCappedAtTen_SortedByScore()
    {
        var rows = Singletons(300, Noise);
        var report = LeakageAudit.RunRows(rows, Guid.Empty);
        Assert.InRange(report.Signals.Count, 0, 10);
        var scores = report.Signals.Select(s => Math.Abs(s.Lift) * s.Support).ToList();
        Assert.Equal(scores.OrderByDescending(s => s).ToList(), scores);
        Assert.All(report.Signals, s => Assert.True(s.Support >= LeakageAudit.MinSignalSupport));
    }

    // ----- real planner (D-23 before/after) -----

    // The default 8% privileged share gives this 250-email plan only 2 privileged relevant emails, below
    // MinSignalSupport, so the D-23 before/after proof uses 20% (9 privileged relevant emails)
    private const int PrivilegeProofPercent = 20;

    private static async Task<(CaseDraft Draft, CorpusPlan Plan)> RealPlanAsync(int? privilegedPercent = null)
    {
        var draft = CorpusPlannerTests.Draft(250);
        draft.Corpus!.Settings.PrivilegedPercent = privilegedPercent;
        var (planner, fake) = CorpusPlannerTests.Planner(2106);
        fake.Respond = CorpusPlannerTests.Responder();
        var result = await planner.PlanAsync(draft, mediaAvailable: true, string.Empty);
        Assert.True(result.Succeeded, result.FailureMessage);
        return (draft, result.Plan!);
    }

    [Fact]
    public async Task RealPlanner_PreD23_ReportsPrivilegeBannerOnlyOnRelevant()
    {
        var (draft, plan) = await RealPlanAsync(PrivilegeProofPercent);
        // Test seam: the pre-D-23 rule left non-relevant mail without privileged threads in small corpora
        foreach (var thread in plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant))
            thread.IsPrivileged = false;

        var report = LeakageAudit.Run(plan, draft.Guide!);

        Assert.Contains(report.Signals,
            s => s.Feature == "privilege banner" && s.Kind == LeakageSignalKind.OnlyOnRelevant);
        Assert.Equal(plan.PlanId, report.PlanId);
    }

    [Fact]
    public async Task RealPlanner_PostD23_NoPrivilegeBannerOnlyOnRelevant()
    {
        var (draft, plan) = await RealPlanAsync(PrivilegeProofPercent);
        Assert.True(plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && t.IsPrivileged)
            .Sum(t => t.Messages.Count) >= LeakageAudit.MinSignalSupport);

        var report = LeakageAudit.Run(plan, draft.Guide!);

        Assert.DoesNotContain(report.Signals,
            s => s.Feature == "privilege banner" && s.Kind == LeakageSignalKind.OnlyOnRelevant);
        Assert.Equal(plan.EmailCount, report.RelevantEmails + report.NonRelevantEmails);
    }

    [Fact]
    public async Task RealPlanner_TwoRuns_IdenticalReports()
    {
        var (draft, plan) = await RealPlanAsync();

        var a = LeakageAudit.Run(plan, draft.Guide!);
        var b = LeakageAudit.Run(plan, draft.Guide!);

        Assert.Equal(a.Verdict, b.Verdict);
        Assert.Equal(a.BalancedAccuracy, b.BalancedAccuracy);
        Assert.Equal(a.Auc, b.Auc);
        Assert.Equal(a.Folds, b.Folds);
        Assert.Equal(a.Signals.Select(s => (s.Feature, s.Kind, s.Support, s.Lift)),
            b.Signals.Select(s => (s.Feature, s.Kind, s.Support, s.Lift)));
    }
}
