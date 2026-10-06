using System.Globalization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Qa;

/// <summary>
/// QA-03 post-hoc scans: V4/V3/V6 re-checks of relevant threads with their as-of-wave ledger, the attachment check,
/// printed case values in non-relevant mail, merged journaled violations and skipped invalidated threads.
/// </summary>
public class CorpusScansTests : IDisposable
{
    private const string Neutral = "Please review the updated schedule and let me know if anything needs to change.";
    private const string VariantAmount = "$184,520";
    private const string VariantText = "uses $184,520 for Change Order 7 amount; the case value is $184,250.00";

    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;

    public CorpusScansTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = EvidenceTestData.ApprovedCase();
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    private CaseGuide Guide => _draft.Guide!;

    private List<PlannedThread> Relevant => _plan.Threads
        .Where(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0)
        .OrderBy(MessageTime.FirstUtc)
        .ThenBy(t => t.Id, StringComparer.Ordinal)
        .ToList();

    private PlannedThread NonRelevant => _plan.Threads.First(t => t.Relevance != ThreadRelevance.Relevant && t.Messages.Count > 0);

    private static PlannedMessage First(PlannedThread thread) => thread.Messages.OrderBy(m => m.Seq).First();

    // Every planned thread written with neutral bodies; bodies[docId] overrides one message
    private JournalReplay Replay(Dictionary<string, string>? bodies = null)
    {
        var replay = new JournalReplay { Exists = true, HasRunStarted = true, JournalPlanId = _plan.PlanId };
        foreach (var thread in _plan.Threads.Where(t => t.Messages.Count > 0))
        {
            replay.LastByThread[thread.Id] = new JournalRecord
            {
                Type = JournalRecordType.ThreadDone,
                PlanId = _plan.PlanId,
                ThreadId = thread.Id,
                Status = ThreadWriteStatus.Written,
                Messages = thread.Messages.OrderBy(m => m.Seq).Select(m => new JournalMessage
                {
                    DocId = m.DocId,
                    Subject = thread.Subject,
                    BodyPlain = bodies != null && bodies.TryGetValue(m.DocId, out var body) ? body : Neutral,
                    SignOff = "Thanks,"
                }).ToList()
            };
        }

        return replay;
    }

    private int WrittenEmails => _plan.Threads.Where(t => t.Messages.Count > 0).Sum(t => t.Messages.Count);

    // A value linked to a new fact dated 30 days after the message, so any earlier email that prints it is ahead of time
    private string AddFutureValue(PlannedMessage message)
    {
        var date = MessageTime.LocalDate(message).AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Guide.Facts.Add(new KeyFact
        {
            Id = "F-099",
            Date = date,
            Title = "Retention payout approved",
            Description = "The owner approved the retention payout.",
            IssueIds = new List<string> { "ISS-01" },
            ActorIds = new List<string> { "CH-001" },
            KnownByIds = new List<string> { "CH-001" },
            Significance = FactSignificance.Supporting
        });
        Guide.Values.Add(new CanonicalValue
        {
            Id = "V-099",
            Kind = ValueKind.Amount,
            Label = "Retention payout",
            Value = "$77,777",
            FactIds = new List<string> { "F-099" }
        });
        return "$77,777";
    }

    [Fact]
    public async Task Run_CleanGeneratedCorpus_HasNoFindings()
    {
        _plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
        foreach (var message in _plan.Threads.SelectMany(t => t.Messages))
            message.Attachments.Clear();
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Respond = EvidenceTestData.RenderResponder(_plan, Guide);
        var result = await new GenerationRunner(gateway)
            .RunAsync(new GenerationRunInput { Draft = _draft, Plan = _plan, CaseFolder = _folder }, new GenerationSnapshot())
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);

        var report = CorpusScans.Run(_draft, _plan, GenerationJournal.Replay(_folder, _plan));

        Assert.Empty(report.Findings);
        Assert.Equal(Relevant.Sum(t => t.Messages.Count), report.ScannedEmails);
        Assert.Equal(_plan.PlanId, report.PlanId);
    }

    [Fact]
    public void Run_NeutralCorpus_ScansEveryWrittenEmail()
    {
        var report = CorpusScans.Run(_draft, _plan, Replay());

        Assert.Empty(report.Findings);
        Assert.Equal(WrittenEmails, report.ScannedEmails);
    }

    [Fact]
    public void Run_RelevantBodyWithFutureFact_IsOneTimelineFinding()
    {
        var thread = Relevant.First();
        var message = First(thread);
        var value = AddFutureValue(message);

        var report = CorpusScans.Run(_draft, _plan,
            Replay(new() { [message.DocId] = $"We expect {value} on the retention payout next month." }));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ScanCheck.Timeline, finding.Check);
        Assert.Equal(message.DocId, finding.DocId);
        Assert.Equal(thread.Id, finding.ThreadId);
        Assert.True(finding.Relevant);
        Assert.Equal("mentions Retention payout, dated after this email", finding.Text);
        Assert.Equal(MessageTime.Local(message).DateTime, finding.DateSent);
        Assert.False(string.IsNullOrWhiteSpace(finding.Subject));
    }

    [Fact]
    public void Run_RelevantBodyWithWrongAmountVariant_IsOneValueFinding()
    {
        var message = First(Relevant.First());

        var report = CorpusScans.Run(_draft, _plan,
            Replay(new() { [message.DocId] = $"The revised total on the change order is {VariantAmount} as of today." }));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ScanCheck.Value, finding.Check);
        Assert.Equal(message.DocId, finding.DocId);
        Assert.Equal(VariantText, finding.Text);
    }

    // An earlier-wave thread A and a later-wave thread B that shares A's first sender and starts after A's first email
    private (PlannedThread Earlier, PlannedThread Later) WavePair()
    {
        var waveOf = WavePlanner.WaveIndexByThread(WavePlanner.Plan(_plan));
        foreach (var a in Relevant)
        {
            var root = First(a);
            foreach (var b in Relevant)
            {
                if (waveOf[b.Id] > waveOf[a.Id] && MessageTime.FirstLocalDate(b) > MessageTime.LocalDate(root)
                    && KnowledgeGate.Build(Guide, b).Participants.Contains(root.FromId))
                    return (a, b);
            }
        }

        throw new InvalidOperationException("Fixture has no earlier/later wave pair");
    }

    private static LedgerDelta Retainage(string value) => new()
    {
        Seq = 1,
        Kind = LedgerKind.Figure,
        About = "retainage release",
        Statement = $"The retainage release is {value}.",
        Value = value
    };

    // The later wave sees the earlier wave's ledger; the earlier wave never sees a later one, so the finding is always on
    // the later thread whichever of the two values came first
    [Theory]
    [InlineData("$50,000", "$61,000")]
    [InlineData("$61,000", "$50,000")]
    public void Run_LaterWaveContradictingEarlierLedger_IsStoryDetailFinding_EarlierWaveIsNot(string first, string second)
    {
        var (earlier, later) = WavePair();
        var replay = Replay();
        replay.LastByThread[earlier.Id].LedgerDelta.Add(Retainage(first));
        replay.LastByThread[later.Id].LedgerDelta.Add(Retainage(second));

        var report = CorpusScans.Run(_draft, _plan, replay);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ScanCheck.StoryDetail, finding.Check);
        Assert.Equal(later.Id, finding.ThreadId);
        Assert.Equal(First(later).DocId, finding.DocId);
        Assert.Equal($"gives retainage release as {second}; earlier emails say {first}", finding.Text);
        Assert.DoesNotContain(report.Findings, f => f.ThreadId == earlier.Id);
    }

    [Fact]
    public void Run_AttachmentTextWithFutureFact_IsAttachmentFindingOnParent()
    {
        var message = First(Relevant.First());
        var value = AddFutureValue(message);
        var replay = Replay();
        replay.LastByAttachment["ATT-QA-1"] = new JournalRecord
        {
            Type = JournalRecordType.AttachmentDone,
            AttachmentDocId = "ATT-QA-1",
            ParentDocId = message.DocId,
            AttachmentKind = AttachmentType.Word,
            Title = "Payout memo",
            FlattenedText = $"Payout memo\nThe retention payout of {value} is approved."
        };

        var report = CorpusScans.Run(_draft, _plan, replay);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ScanCheck.Attachment, finding.Check);
        Assert.Equal(message.DocId, finding.DocId);
        Assert.Equal("Payout memo: mentions Retention payout, dated after this email", finding.Text);
    }

    [Fact]
    public void Run_NonRelevantBodyPrintingCaseAmount_IsNonRelevantValueFinding()
    {
        var thread = NonRelevant;
        var message = First(thread);

        var report = CorpusScans.Run(_draft, _plan,
            Replay(new() { [message.DocId] = "Odd coincidence, but the catering quote came to $184,250 for the gala." }));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ScanCheck.Value, finding.Check);
        Assert.False(finding.Relevant);
        Assert.Equal(message.DocId, finding.DocId);
        Assert.Equal("prints the case value $184,250", finding.Text);
    }

    [Fact]
    public void Run_WarningsThreadWithJournaledV3_IsMergedNotDuplicated()
    {
        var thread = Relevant.First();
        var message = First(thread);
        var replay = Replay(new() { [message.DocId] = $"The revised total on the change order is {VariantAmount} as of today." });
        var record = replay.LastByThread[thread.Id];
        record.Status = ThreadWriteStatus.Warnings;
        record.Violations = new List<string>
        {
            $"msg {message.Seq}: {VariantText}",
            $"msg {message.Seq}: uses legal wording (\"plaintiff\")"
        };

        var report = CorpusScans.Run(_draft, _plan, replay);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ScanCheck.Value, finding.Check);
        Assert.Equal(VariantText, finding.Text);
    }

    [Fact]
    public void Run_JournaledViolationTheRecheckNoLongerFinds_IsStillReported()
    {
        var thread = Relevant.First();
        var message = First(thread);
        var replay = Replay();
        var record = replay.LastByThread[thread.Id];
        record.Status = ThreadWriteStatus.Warnings;
        record.Violations = new List<string> { $"msg {message.Seq}: mentions Late fee, dated after this email" };

        var finding = Assert.Single(CorpusScans.Run(_draft, _plan, replay).Findings);

        Assert.Equal(ScanCheck.Timeline, finding.Check);
        Assert.Equal(message.DocId, finding.DocId);
    }

    [Fact]
    public void Run_InvalidatedThread_IsSkipped()
    {
        var thread = Relevant.First();
        var message = First(thread);
        var replay = Replay(new() { [message.DocId] = $"The revised total on the change order is {VariantAmount} as of today." });
        replay.InvalidatedThreadIds.Add(thread.Id);

        var report = CorpusScans.Run(_draft, _plan, replay);

        Assert.Empty(report.Findings);
        Assert.Equal(WrittenEmails - thread.Messages.Count, report.ScannedEmails);
    }

    [Fact]
    public void Run_Findings_AreOrderedByDateThenDocId_AndRepeatable()
    {
        var bodies = Relevant.Select(First)
            .ToDictionary(m => m.DocId, _ => $"The revised total on the change order is {VariantAmount} as of today.");
        bodies[First(NonRelevant).DocId] = "The catering quote came to $184,250 for the gala.";

        var first = CorpusScans.Run(_draft, _plan, Replay(bodies));
        var second = CorpusScans.Run(_draft, _plan, Replay(bodies));

        Assert.True(first.Findings.Count >= 3);
        var expected = first.Findings.OrderBy(f => f.DateSent).ThenBy(f => f.DocId, StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Select(f => f.DocId), first.Findings.Select(f => f.DocId));
        Assert.Equal(first.Findings.Select(f => f.DocId + f.Text), second.Findings.Select(f => f.DocId + f.Text));
    }

    [Fact]
    public void CorpusScans_TakesNoModelGateway()
    {
        var parameters = typeof(CorpusScans).GetMethods()
            .Where(m => m.DeclaringType == typeof(CorpusScans))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(parameters, t => typeof(ILlmGateway).IsAssignableFrom(t) || t == typeof(FakeLlmProvider));
    }
}
