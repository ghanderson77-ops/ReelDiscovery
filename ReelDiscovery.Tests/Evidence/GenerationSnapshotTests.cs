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
}
