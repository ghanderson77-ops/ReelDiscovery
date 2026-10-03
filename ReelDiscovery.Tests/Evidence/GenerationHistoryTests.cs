using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationHistoryTests
{
    private static List<PlannedThread> Relevant(CorpusPlan plan) =>
        plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

    private static JournalRecord At(JournalRecord record, int minutes)
    {
        record.Utc = new DateTime(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);
        return record;
    }

    [Fact]
    public void Events_OneEventPerRecord_InJournalOrder()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var threads = Relevant(plan);
        // Recorded out of dictionary order: the failed thread first, the warnings thread last
        var failed = At(GenerationRunStateTests.Failed(plan, threads[2], 0.05m), 1);
        var written = At(GenerationRunStateTests.Done(plan, threads[0], ThreadWriteStatus.Written, 0.10m), 2);
        var warnings = At(GenerationRunStateTests.Done(plan, threads[1], ThreadWriteStatus.Warnings, 0.20m), 3);
        var replay = GenerationRunStateTests.Replay(written, warnings, failed);
        replay.LastRunEnded = At(new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = plan.PlanId, Outcome = GenerationOutcome.Cancelled }, 4);

        var events = GenerationHistory.Events(plan, replay, RenderAssignments.Compute(plan));

        Assert.Equal(
            new[] { GenerationEventKind.ThreadFailed, GenerationEventKind.ThreadWritten, GenerationEventKind.KeptWithWarnings, GenerationEventKind.RunCancelled },
            events.Select(e => e.Kind));
        Assert.Equal(new[] { threads[2].Id, threads[0].Id, threads[1].Id, "" }, events.Select(e => e.ThreadId));
        Assert.Equal(failed.Utc.ToLocalTime(), events[0].LocalTime);
        Assert.Equal(DateTimeKind.Local, events[0].LocalTime.Kind);
    }

    [Fact]
    public void Events_WrittenCarriesSubjectEmailsDetailsAndLastFile()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var thread = Relevant(plan).First(t => t.Messages.Count > 1);
        var record = GenerationRunStateTests.Done(plan, thread, ThreadWriteStatus.Written, 0.10m);
        record.LedgerDelta.Add(new LedgerDelta { Seq = 2, Kind = LedgerKind.Figure, About = "invoice", Statement = "Invoice total confirmed." });
        var assignments = RenderAssignments.Compute(plan);

        var e = Assert.Single(GenerationHistory.Events(plan, GenerationRunStateTests.Replay(record), assignments));

        var ordered = thread.Messages.OrderBy(m => m.Seq).ToList();
        Assert.Equal(EmailComposer.SubjectFor(thread, ordered[0], assignments[thread.Id].Marking), e.Subject);
        Assert.Equal(record.Messages.Count, e.Emails);
        Assert.Equal(2, e.Details);
        Assert.Equal(ordered[^1].FilePath, e.FilePath);
        Assert.False(string.IsNullOrEmpty(e.FilePath));
    }

    [Fact]
    public void Events_WarningsAndFailedCarryViolationsAndReason()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var threads = Relevant(plan);
        var replay = GenerationRunStateTests.Replay(
            At(GenerationRunStateTests.Done(plan, threads[0], ThreadWriteStatus.Warnings, 0.1m), 1),
            At(GenerationRunStateTests.Failed(plan, threads[1], 0.1m), 2));

        var events = GenerationHistory.Events(plan, replay, RenderAssignments.Compute(plan));

        Assert.Equal(GenerationEventKind.KeptWithWarnings, events[0].Kind);
        Assert.Equal(2, events[0].Violations.Count);
        Assert.Equal(GenerationEventKind.ThreadFailed, events[1].Kind);
        Assert.Equal("The provider returned an error", events[1].Reason);
    }

    [Theory]
    [InlineData(GenerationOutcome.Finished, GenerationEventKind.RunFinished)]
    [InlineData(GenerationOutcome.Cancelled, GenerationEventKind.RunCancelled)]
    [InlineData(GenerationOutcome.Fatal, GenerationEventKind.RunStopped)]
    [InlineData(GenerationOutcome.InternalError, GenerationEventKind.RunStopped)]
    public void Events_RunEndedMapsOutcome(GenerationOutcome outcome, GenerationEventKind expected)
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var threads = Relevant(plan);
        var replay = GenerationRunStateTests.Replay(At(GenerationRunStateTests.Done(plan, threads[0], ThreadWriteStatus.Written, 0.1m), 1));
        replay.LastRunEnded = At(new JournalRecord
        {
            Type = JournalRecordType.RunEnded,
            PlanId = plan.PlanId,
            Outcome = outcome,
            Message = "The API key was rejected"
        }, 2);

        var last = GenerationHistory.Events(plan, replay, RenderAssignments.Compute(plan))[^1];

        Assert.Equal(expected, last.Kind);
        if (expected == GenerationEventKind.RunStopped)
            Assert.Equal("The API key was rejected", last.Reason);
        if (expected == GenerationEventKind.RunCancelled)
        {
            Assert.Equal(1, last.Done);
            Assert.Equal(threads.Count, last.Total);
        }
    }

    [Fact]
    public void Events_NoneOutcomeAndEmptyReplay_GiveNoEvents()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var replay = new JournalReplay { Exists = true };
        replay.LastRunEnded = new JournalRecord { Type = JournalRecordType.RunEnded, Outcome = GenerationOutcome.None };

        Assert.Empty(GenerationHistory.Events(plan, replay, RenderAssignments.Compute(plan)));
    }

    [Fact]
    public void Waves_ProjectStatePerWave()
    {
        var (_, plan, state, threads) = GenerationRunStateTests.Mixed();
        var planned = WavePlanner.Plan(plan);

        var waves = GenerationHistory.Waves(plan, state);

        Assert.Equal(planned.Count, waves.Count);
        Assert.Equal(planned.Select(w => w.Index), waves.Select(w => w.Index));
        Assert.Equal(planned.Select(w => w.ThreadIds.Count), waves.Select(w => w.Threads));
        Assert.Equal(planned.Select(w => w.MessageCount), waves.Select(w => w.Emails));
        Assert.Equal(planned.Select(w => w.Start), waves.Select(w => w.Start));
        Assert.All(waves, w => Assert.False(w.Running));

        // Done counts written and kept-with-warnings threads
        Assert.Equal(2, waves.Sum(w => w.Done));
        Assert.Equal(1, waves.Sum(w => w.Warnings));
        Assert.Equal(1, waves.Sum(w => w.Failed));
        var failedWave = planned.Single(w => w.ThreadIds.Contains(threads[2].Id)).Index;
        Assert.Equal(1, waves.Single(w => w.Index == failedWave).Failed);
    }
}
