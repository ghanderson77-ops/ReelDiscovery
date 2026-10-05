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

        // Wave rows only; the fixture's non-relevant threads add the Non-relevant mail row after them (06-23)
        var waves = GenerationHistory.Waves(plan, state).Where(w => w.RowKind == StageRowKind.Wave).ToList();

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

    // ----- 06-23: Non-relevant mail and Attachments for written threads rows -----

    private static List<PlannedAttachment> AttachmentsOf(PlannedThread thread) =>
        thread.Messages.SelectMany(m => m.Attachments).Where(a => !string.IsNullOrEmpty(a.DocId)).ToList();

    private static List<PlannedThread> NonRelevant(CorpusPlan plan) =>
        plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant && t.Messages.Count > 0).ToList();

    // Every attachment of the thread written, except images and voicemails, which are Skipped
    private static JournalRecord DoneSkippingMedia(CorpusPlan plan, PlannedThread thread) =>
        GenerationRunStateTests.DoneWith(plan, thread, AttachmentsOf(thread)
            .Select(a => GenerationRunStateTests.Attachment(plan, a,
                a.Kind is ReelDiscovery.Models.AttachmentType.Image or ReelDiscovery.Models.AttachmentType.Voicemail
                    ? JournalRecordType.AttachmentSkipped
                    : JournalRecordType.AttachmentDone,
                reason: "no tested OpenAI key this session"))
            .ToArray());

    [Fact]
    public void Waves_MixedCase_WaveRowsWithAttachmentsThenNonRelevantRow()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var daily = AttachmentTestData.Thread(plan, AttachmentTestData.DailyBusinessThreadId);
        var spam = AttachmentTestData.Thread(plan, AttachmentTestData.SpamThreadId);
        var newsletter = AttachmentTestData.Thread(plan, AttachmentTestData.NewsletterThreadId);
        var replay = GenerationRunStateTests.Journal(plan,
            DoneSkippingMedia(plan, daily),
            GenerationRunStateTests.Done(plan, spam, ThreadWriteStatus.Warnings, 0.01m),
            GenerationRunStateTests.Failed(plan, newsletter, 0.01m));
        var state = GenerationRunState.From(plan, replay);
        var planned = WavePlanner.Plan(plan);

        var rows = GenerationHistory.Waves(plan, state);

        // Wave rows in plan order, each counting the attachments of its relevant threads (none done yet)
        var waveRows = rows.Where(r => r.RowKind == StageRowKind.Wave).ToList();
        Assert.Equal(planned.Select(w => w.Index), waveRows.Select(r => r.Index));
        var waveOf = WavePlanner.WaveIndexByThread(planned);
        foreach (var row in waveRows)
        {
            var expected = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && waveOf.TryGetValue(t.Id, out var w) && w == row.Index)
                .Sum(t => AttachmentsOf(t).Count);
            Assert.Equal(expected, row.Attachments);
            Assert.Equal(0, row.AttachmentsDone);
        }

        // Then the Non-relevant mail row with the non-relevant threads and their emails
        var filler = NonRelevant(plan);
        var stage = rows.Single(r => r.RowKind == StageRowKind.NonRelevant);
        Assert.Equal(planned.Count + 1, stage.Index);
        Assert.Same(stage, rows[waveRows.Count]);
        Assert.Equal(filler.Count, stage.Threads);
        Assert.Equal(filler.Sum(t => t.Messages.Count), stage.Emails);
        Assert.Equal(2, stage.Done);
        Assert.Equal(1, stage.Warnings);
        Assert.Equal(1, stage.Failed);
        Assert.Equal(AttachmentsOf(daily).Count, stage.AttachmentsDone);
        Assert.False(stage.Running);

        // The spam thread was written without its banner (no record, as after a Phase 5 run): a repair, so it counts on the
        // Attachments for written threads row only, never also on the Non-relevant row
        var repair = rows.Single(r => r.RowKind == StageRowKind.Attachments);
        Assert.Same(repair, rows[^1]);
        Assert.Equal(planned.Count + 2, repair.Index);
        Assert.Equal(1, repair.Threads);
        Assert.Equal(AttachmentsOf(spam).Count, repair.Attachments);
        Assert.Equal(filler.Sum(t => AttachmentsOf(t).Count) - AttachmentsOf(spam).Count, stage.Attachments);
        Assert.Equal(AttachmentsOf(plan.Threads.First(t => t.Id == spam.Id)).Count + stage.Attachments + waveRows.Sum(r => r.Attachments),
            rows.Sum(r => r.Attachments));
    }

    [Fact]
    public void Waves_NoRepairs_NoAttachmentsRow()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var daily = AttachmentTestData.Thread(plan, AttachmentTestData.DailyBusinessThreadId);
        var state = GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan, DoneSkippingMedia(plan, daily)));

        var rows = GenerationHistory.Waves(plan, state, mediaAvailable: true);

        Assert.DoesNotContain(rows, r => r.RowKind == StageRowKind.Attachments);
        Assert.Equal(AttachmentsOf(daily).Count, rows.Single(r => r.RowKind == StageRowKind.NonRelevant).AttachmentsDone);
    }

    [Fact]
    public void Waves_MediaAvailableMatchesSeeds()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var invite = AttachmentTestData.Thread(plan, AttachmentTestData.InviteThreadId);
        var image = AttachmentsOf(invite).Single(a => a.Kind == ReelDiscovery.Models.AttachmentType.Image);
        // A done thread with a Skipped image and its invite written
        var state = GenerationRunState.From(plan, GenerationRunStateTests.Journal(plan, DoneSkippingMedia(plan, invite)));
        var planned = WavePlanner.Plan(plan);
        var waveOf = WavePlanner.WaveIndexByThread(planned);
        var inviteWave = waveOf[invite.Id];

        // Media available: the skipped image is a repair item, on the Attachments row and not on its wave row
        var withMedia = GenerationHistory.Waves(plan, state, mediaAvailable: true);
        var repair = withMedia.Single(r => r.RowKind == StageRowKind.Attachments);
        Assert.Equal(1, repair.Threads);
        Assert.Equal(1, repair.Attachments);
        Assert.Equal(1, repair.AttachmentsSkipped);
        var wave = withMedia.Single(r => r.RowKind == StageRowKind.Wave && r.Index == inviteWave);
        Assert.Equal(AttachmentsOf(invite).Count - 1, wave.Attachments - OtherAttachmentsInWave(plan, waveOf, inviteWave, invite.Id));
        Assert.Equal(0, wave.AttachmentsSkipped);

        // Media unavailable: no Attachments row, the wave row counts the image as Skipped
        var withoutMedia = GenerationHistory.Waves(plan, state, mediaAvailable: false);
        Assert.DoesNotContain(withoutMedia, r => r.RowKind == StageRowKind.Attachments);
        wave = withoutMedia.Single(r => r.RowKind == StageRowKind.Wave && r.Index == inviteWave);
        Assert.Equal(AttachmentsOf(invite).Count, wave.Attachments - OtherAttachmentsInWave(plan, waveOf, inviteWave, invite.Id));
        Assert.Equal(1, wave.AttachmentsSkipped);

        // For both values every attachment sits on the row of its seed (the runner's rule)
        foreach (var media in new[] { true, false })
        {
            var rows = GenerationHistory.Waves(plan, state, media);
            var seeds = state.AttachmentSeeds(plan, waveOf, planned.Count + 1, planned.Count + 2, media);
            Assert.Equal(seeds[image.DocId].RowIndex, media ? planned.Count + 2 : inviteWave);
            foreach (var row in rows)
            {
                var onRow = seeds.Values.Where(s => s.RowIndex == row.Index).ToList();
                Assert.Equal(onRow.Count, row.Attachments);
                Assert.Equal(onRow.Count(s => s.Status == AttachmentRunStatus.Skipped), row.AttachmentsSkipped);
                Assert.Equal(onRow.Count(s => s.Status is AttachmentRunStatus.Written or AttachmentRunStatus.Warnings), row.AttachmentsDone);
            }

            Assert.Equal(seeds.Count, rows.Sum(r => r.Attachments));
        }
    }

    [Fact]
    public void Waves_NoNonRelevantThreads_NoNonRelevantRow()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
        var state = GenerationRunState.From(plan, GenerationRunStateTests.Replay());

        var rows = GenerationHistory.Waves(plan, state);

        Assert.Equal(WavePlanner.Plan(plan).Count, rows.Count);
        Assert.All(rows, r => Assert.Equal(StageRowKind.Wave, r.RowKind));
    }

    // Attachments of the other relevant threads in the same wave
    private static int OtherAttachmentsInWave(CorpusPlan plan, IReadOnlyDictionary<string, int> waveOf, int wave, string threadId) =>
        plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && t.Id != threadId
                                && waveOf.TryGetValue(t.Id, out var w) && w == wave)
            .Sum(t => AttachmentsOf(t).Count);
}
