using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationSnapshotTests
{
    private static GenerationSnapshot Create(int waves = 3)
    {
        var snapshot = new GenerationSnapshot();
        var rows = Enumerable.Range(1, waves).Select(i => new WaveProgress { Index = i, Threads = 4 });
        snapshot.Reset(new GenerationCounts { TotalThreads = 10, WaveCount = waves }, rows);
        return snapshot;
    }

    [Fact]
    public void Reset_SetsCountsAndWaves_WithNoEvents()
    {
        var view = Create().Drain();

        Assert.Equal(10, view.Counts.TotalThreads);
        Assert.Equal(3, view.Waves.Count);
        Assert.Empty(view.Events);
    }

    [Fact]
    public void RecordThread_Written_UpdatesCountsAndWave()
    {
        var snapshot = Create();
        snapshot.RecordThread(2, ThreadRunStatus.Written, 3, 7, 0.12m);
        var view = snapshot.Drain();

        Assert.Equal(1, view.Counts.DoneThreads);
        Assert.Equal(3, view.Counts.EmailsWritten);
        Assert.Equal(7, view.Counts.FilesWritten);
        Assert.Equal(0.12m, view.Counts.CostUsd);
        Assert.Equal(1, view.Waves.Single(w => w.Index == 2).Done);
        Assert.Equal(0, view.Waves.Single(w => w.Index == 1).Done);
    }

    [Fact]
    public void RecordThread_WarningsAndFailed_UpdateTheirCounters()
    {
        var snapshot = Create();
        snapshot.RecordThread(2, ThreadRunStatus.Warnings, 2, 4, 0.05m);
        snapshot.RecordThread(3, ThreadRunStatus.Failed, 0, 0, 0.01m);
        var view = snapshot.Drain();

        Assert.Equal(1, view.Counts.DoneThreads);
        Assert.Equal(1, view.Counts.WarningThreads);
        Assert.Equal(1, view.Counts.FailedThreads);
        Assert.Equal(0.06m, view.Counts.CostUsd);
        var wave2 = view.Waves.Single(w => w.Index == 2);
        Assert.Equal(1, wave2.Done);
        Assert.Equal(1, wave2.Warnings);
        var wave3 = view.Waves.Single(w => w.Index == 3);
        Assert.Equal(1, wave3.Failed);
        Assert.Equal(0, wave3.Done);
    }

    [Fact]
    public void RecordThread_RetriedFailedThread_LeavesFailedCountOnce()
    {
        var snapshot = new GenerationSnapshot();
        snapshot.Reset(new GenerationCounts { TotalThreads = 3, FailedThreads = 2 },
            new[] { new WaveProgress { Index = 1, Threads = 3, Failed = 2 } });

        // One earlier failure now written, the other fails again
        snapshot.RecordThread(1, ThreadRunStatus.Written, 2, 2, 0m, ThreadRunStatus.Failed);
        snapshot.RecordThread(1, ThreadRunStatus.Failed, 0, 0, 0m, ThreadRunStatus.Failed);
        var view = snapshot.Drain();

        Assert.Equal(1, view.Counts.DoneThreads);
        Assert.Equal(1, view.Counts.FailedThreads);
        var wave = view.Waves.Single();
        Assert.Equal(1, wave.Done);
        Assert.Equal(1, wave.Failed);
        Assert.True(view.Counts.DoneThreads + view.Counts.FailedThreads <= view.Counts.TotalThreads);
    }

    [Fact]
    public async Task RecordThread_FromParallelTasks_CountsExactly()
    {
        var snapshot = Create();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                snapshot.RecordThread(1, ThreadRunStatus.Written, 1, 1, 0m);
        }));
        await Task.WhenAll(tasks);

        var view = snapshot.Drain();
        Assert.Equal(8000, view.Counts.DoneThreads);
        Assert.Equal(8000, view.Waves.Single(w => w.Index == 1).Done);
    }

    [Fact]
    public void AddEvent_KeepsNewest500_AndDrainEmptiesQueue()
    {
        var snapshot = Create();
        for (var i = 0; i < 700; i++)
            snapshot.AddEvent(new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, ThreadId = $"T{i}" });

        var first = snapshot.Drain();
        Assert.Equal(GenerationSnapshot.MaxQueuedEvents, first.Events.Count);
        Assert.Equal("T200", first.Events[0].ThreadId);
        Assert.Equal("T699", first.Events[^1].ThreadId);

        Assert.Empty(snapshot.Drain().Events);
    }

    [Fact]
    public void Peek_DoesNotDequeueEvents()
    {
        var snapshot = Create();
        snapshot.AddEvent(new GenerationEvent { Kind = GenerationEventKind.Retry });

        Assert.Single(snapshot.Peek().Events);
        Assert.Single(snapshot.Drain().Events);
    }

    [Fact]
    public void Drain_ReturnsCopies()
    {
        var snapshot = Create();
        var view = snapshot.Drain();
        view.Counts.DoneThreads = 99;
        view.Waves[0].Done = 42;

        var next = snapshot.Drain();
        Assert.Equal(0, next.Counts.DoneThreads);
        Assert.Equal(0, next.Waves[0].Done);
    }

    [Fact]
    public void SetStage_AndWaveRunning_AreReflected()
    {
        var snapshot = Create();
        var start = new DateOnly(2024, 3, 1);
        var end = new DateOnly(2024, 3, 31);
        snapshot.SetStage(GenerationStage.Writing, 2, 5, start, end, 3);
        snapshot.SetWaveRunning(2, true);
        snapshot.SetLedgerEntries(12);
        snapshot.SetRemainingCost(1.5m);
        snapshot.SetFinished(true, false);
        var view = snapshot.Drain();

        Assert.Equal(GenerationStage.Writing, view.Stage);
        Assert.Equal(2, view.StageWave);
        Assert.Equal(5, view.StageWaveCount);
        Assert.Equal(start, view.StageWaveStart);
        Assert.Equal(end, view.StageWaveEnd);
        Assert.Equal(3, view.StageCount);
        Assert.Equal(new[] { false, true, false }, view.Waves.Select(w => w.Running).ToArray());
        Assert.Equal(12, view.Counts.LedgerEntries);
        Assert.Equal(1.5m, view.Counts.RemainingCostUsd);
        Assert.True(view.Counts.Running);
        Assert.False(view.Counts.Finished);
    }

    private static GenerationSnapshot CreateWithAttachments(int total = 20,
        IReadOnlyDictionary<string, AttachmentSeed>? seeds = null, GenerationCounts? counts = null,
        IEnumerable<WaveProgress>? rows = null)
    {
        var snapshot = new GenerationSnapshot();
        rows ??= new[]
        {
            new WaveProgress { Index = 1 },
            new WaveProgress { Index = 2 },
            new WaveProgress { Index = 3 },
            new WaveProgress { Index = 99, RowKind = StageRowKind.Attachments }
        };
        counts ??= new GenerationCounts();
        counts.AttachmentsTotal = total;
        snapshot.Reset(counts, rows, seeds);
        return snapshot;
    }

    [Fact]
    public void RecordThread_RelevantFlag_SplitsRelevantAndNonRelevant()
    {
        var snapshot = Create();
        snapshot.RecordThread(1, ThreadRunStatus.Written, 1, 1, 0m);
        snapshot.RecordThread(1, ThreadRunStatus.Warnings, 1, 1, 0m, relevant: false);
        snapshot.RecordThread(1, ThreadRunStatus.Written, 1, 1, 0m, relevant: false);
        var view = snapshot.Drain();

        Assert.Equal(3, view.Counts.DoneThreads);
        Assert.Equal(1, view.Counts.RelevantDone);
        Assert.Equal(2, view.Counts.NonRelevantDone);
    }

    [Fact]
    public void RecordThread_PreviousStatusCorrection_KeepsSplit()
    {
        var snapshot = new GenerationSnapshot();
        snapshot.Reset(new GenerationCounts { TotalThreads = 2, FailedThreads = 1, NonRelevantTotal = 2 },
            new[] { new WaveProgress { Index = 99, RowKind = StageRowKind.NonRelevant, Failed = 1 } });

        snapshot.RecordThread(99, ThreadRunStatus.Written, 1, 1, 0m, ThreadRunStatus.Failed, relevant: false);
        var view = snapshot.Drain();

        Assert.Equal(0, view.Counts.FailedThreads);
        Assert.Equal(1, view.Counts.DoneThreads);
        Assert.Equal(1, view.Counts.NonRelevantDone);
        Assert.Equal(0, view.Counts.RelevantDone);
    }

    [Fact]
    public void RecordThread_NonRelevantRow_UpdatedLikeWaveRow()
    {
        var snapshot = new GenerationSnapshot();
        snapshot.Reset(new GenerationCounts(), new[]
        {
            new WaveProgress { Index = 1 },
            new WaveProgress { Index = 99, RowKind = StageRowKind.NonRelevant, Threads = 5 }
        });

        snapshot.RecordThread(99, ThreadRunStatus.Warnings, 2, 2, 0m, relevant: false);
        snapshot.RecordThread(99, ThreadRunStatus.Failed, 0, 0, 0m, relevant: false);
        var row = snapshot.Drain().Waves.Single(w => w.Index == 99);

        Assert.Equal(StageRowKind.NonRelevant, row.RowKind);
        Assert.Equal(1, row.Done);
        Assert.Equal(1, row.Warnings);
        Assert.Equal(1, row.Failed);
    }

    [Fact]
    public void RecordAttachment_SeededFailedThenWritten_MovesToDone()
    {
        var seeds = new Dictionary<string, AttachmentSeed>
        {
            ["A.0001"] = new() { RowIndex = 2, Status = AttachmentRunStatus.Failed }
        };
        var rows = new[]
        {
            new WaveProgress { Index = 1 },
            new WaveProgress { Index = 2, Attachments = 1, AttachmentsFailed = 1 }
        };
        var snapshot = CreateWithAttachments(1, seeds, new GenerationCounts { AttachmentsFailed = 1, CostUsd = 1m }, rows);

        snapshot.RecordAttachment("A.0001", 2, AttachmentRunStatus.Written, 0.01m, 0.006m);
        var view = snapshot.Drain();

        Assert.Equal(0, view.Counts.AttachmentsFailed);
        Assert.Equal(1, view.Counts.AttachmentsDone);
        var row = view.Waves.Single(w => w.Index == 2);
        Assert.Equal(0, row.AttachmentsFailed);
        Assert.Equal(1, row.AttachmentsDone);
        Assert.Equal(1.016m, view.Counts.CostUsd);
        Assert.Equal(0.006m, view.Counts.MediaCostUsd);
    }

    [Fact]
    public void RecordAttachment_RepeatedFailure_CountsOnce_AddsBothCosts()
    {
        var snapshot = CreateWithAttachments();
        snapshot.RecordAttachment("B.0001", 3, AttachmentRunStatus.Failed, 0.01m);
        snapshot.RecordAttachment("B.0001", 3, AttachmentRunStatus.Failed, 0.02m, 0.005m);
        var view = snapshot.Drain();

        Assert.Equal(1, view.Counts.AttachmentsFailed);
        Assert.Equal(1, view.Waves.Single(w => w.Index == 3).AttachmentsFailed);
        Assert.Equal(0.035m, view.Counts.CostUsd);
        Assert.Equal(0.005m, view.Counts.MediaCostUsd);
    }

    [Fact]
    public void RecordAttachment_SkippedThenWritten_AndWarningsCountAsDone()
    {
        var snapshot = CreateWithAttachments();
        snapshot.RecordAttachment("C.0001", 1, AttachmentRunStatus.Skipped, 0m);
        var skipped = snapshot.Peek();
        Assert.Equal(1, skipped.Counts.AttachmentsSkipped);
        Assert.Equal(1, skipped.Waves.Single(w => w.Index == 1).AttachmentsSkipped);

        snapshot.RecordAttachment("C.0001", 1, AttachmentRunStatus.Written, 0m);
        snapshot.RecordAttachment("C.0002", 1, AttachmentRunStatus.Warnings, 0m);
        var view = snapshot.Drain();

        Assert.Equal(0, view.Counts.AttachmentsSkipped);
        Assert.Equal(2, view.Counts.AttachmentsDone);
        Assert.Equal(1, view.Counts.AttachmentsWarnings);
        var row = view.Waves.Single(w => w.Index == 1);
        Assert.Equal(0, row.AttachmentsSkipped);
        Assert.Equal(2, row.AttachmentsDone);
        Assert.Equal(1, row.AttachmentWarnings);
    }

    [Fact]
    public void RecordAttachment_WarningsThenWritten_DropsWarning()
    {
        var snapshot = CreateWithAttachments();
        snapshot.RecordAttachment("C.0003", 1, AttachmentRunStatus.Warnings, 0m);
        snapshot.RecordAttachment("C.0003", 1, AttachmentRunStatus.Written, 0m);
        var view = snapshot.Drain();

        Assert.Equal(1, view.Counts.AttachmentsDone);
        Assert.Equal(0, view.Counts.AttachmentsWarnings);
    }

    [Fact]
    public void RecordAttachment_MovedRow_DecrementsOldRowAndIncrementsNewRow()
    {
        var seeds = new Dictionary<string, AttachmentSeed>
        {
            ["D.0001"] = new() { RowIndex = 1, Status = AttachmentRunStatus.Failed }
        };
        var rows = new[]
        {
            new WaveProgress { Index = 1, AttachmentsFailed = 1 },
            new WaveProgress { Index = 99, RowKind = StageRowKind.Attachments }
        };
        var snapshot = CreateWithAttachments(1, seeds, new GenerationCounts { AttachmentsFailed = 1 }, rows);

        snapshot.RecordAttachment("D.0001", 99, AttachmentRunStatus.Written, 0m);
        var view = snapshot.Drain();

        Assert.Equal(0, view.Waves.Single(w => w.Index == 1).AttachmentsFailed);
        Assert.Equal(1, view.Waves.Single(w => w.Index == 99).AttachmentsDone);
        Assert.Equal(0, view.Counts.AttachmentsFailed);
        Assert.Equal(1, view.Counts.AttachmentsDone);
    }

    [Fact]
    public void RecordAttachment_RandomTransitions_NeverExceedTotalOrGoNegative()
    {
        var random = new Random(7);
        var statuses = Enum.GetValues<AttachmentRunStatus>();
        var rowIndexes = new[] { 1, 2, 3, 99 };

        for (var sequence = 0; sequence < 1000; sequence++)
        {
            var snapshot = CreateWithAttachments(20);
            var steps = random.Next(1, 60);
            for (var step = 0; step < steps; step++)
            {
                var docId = $"E.{random.Next(20):D4}";
                snapshot.RecordAttachment(docId, rowIndexes[random.Next(rowIndexes.Length)],
                    statuses[random.Next(statuses.Length)], 0m);
            }

            var view = snapshot.Drain();
            var c = view.Counts;
            Assert.True(c.AttachmentsDone + c.AttachmentsFailed + c.AttachmentsSkipped <= c.AttachmentsTotal);
            Assert.True(c.AttachmentsDone >= 0 && c.AttachmentsFailed >= 0 && c.AttachmentsSkipped >= 0
                && c.AttachmentsWarnings >= 0);
            Assert.True(c.AttachmentsWarnings <= c.AttachmentsDone);
            Assert.Equal(c.AttachmentsDone, view.Waves.Sum(w => w.AttachmentsDone));
            Assert.Equal(c.AttachmentsFailed, view.Waves.Sum(w => w.AttachmentsFailed));
            Assert.Equal(c.AttachmentsSkipped, view.Waves.Sum(w => w.AttachmentsSkipped));
            Assert.All(view.Waves, w => Assert.True(w.AttachmentsDone >= 0 && w.AttachmentsFailed >= 0
                && w.AttachmentsSkipped >= 0 && w.AttachmentWarnings >= 0));
        }
    }

    [Fact]
    public void AddCost_ChangesOnlyCostAndMediaCost()
    {
        var snapshot = CreateWithAttachments();
        snapshot.RecordAttachment("G.0001", 1, AttachmentRunStatus.Written, 0m);
        var before = snapshot.Peek();
        snapshot.AddCost(0.02m, 0.01m);
        var view = snapshot.Drain();

        Assert.Equal(0.03m, view.Counts.CostUsd);
        Assert.Equal(0.01m, view.Counts.MediaCostUsd);
        Assert.Equal(before.Counts.AttachmentsDone, view.Counts.AttachmentsDone);
        Assert.Equal(before.Counts.AttachmentsFailed, view.Counts.AttachmentsFailed);
        Assert.Equal(before.Counts.DoneThreads, view.Counts.DoneThreads);
        Assert.Equal(before.Waves.Sum(w => w.AttachmentsDone), view.Waves.Sum(w => w.AttachmentsDone));
    }

    [Fact]
    public async Task RecordAttachment_FromParallelTasks_CountsExactly()
    {
        var snapshot = CreateWithAttachments(4000);
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                snapshot.RecordAttachment($"P{t}.{i:D4}", 99, AttachmentRunStatus.Written, 0m);
        }));
        await Task.WhenAll(tasks);

        var view = snapshot.Drain();
        Assert.Equal(4000, view.Counts.AttachmentsDone);
        Assert.Equal(4000, view.Waves.Single(w => w.Index == 99).AttachmentsDone);
    }

    [Fact]
    public void Drain_ReturnsCopies_OfAttachmentCounts()
    {
        var snapshot = CreateWithAttachments();
        snapshot.RecordAttachment("F.0001", 1, AttachmentRunStatus.Written, 0m);
        var view = snapshot.Drain();
        view.Counts.AttachmentsDone = 50;
        view.Waves[0].AttachmentsDone = 50;

        var next = snapshot.Drain();
        Assert.Equal(1, next.Counts.AttachmentsDone);
        Assert.Equal(1, next.Waves[0].AttachmentsDone);
    }
}
