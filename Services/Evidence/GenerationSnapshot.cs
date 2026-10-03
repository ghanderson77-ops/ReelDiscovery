using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Thread-safe progress state of a generation run. Workers write only here; the step's 250 ms timer drains it on the
/// UI thread (RUN-07).
/// </summary>
public class GenerationSnapshot
{
    public const int MaxQueuedEvents = 500;

    private readonly object _lock = new();
    private readonly Queue<GenerationEvent> _events = new();
    private GenerationCounts _counts = new();
    private List<WaveProgress> _waves = new();
    private GenerationStage _stage;
    private int _stageWave;
    private int _stageWaveCount;
    private DateOnly _stageWaveStart;
    private DateOnly _stageWaveEnd;
    private int _stageCount;

    /// <summary>
    /// Starts a run view: replaces counts and wave rows and clears queued events and the stage.
    /// </summary>
    public void Reset(GenerationCounts counts, IEnumerable<WaveProgress> waves)
    {
        var countsCopy = (counts ?? new GenerationCounts()).Clone();
        var wavesCopy = (waves ?? Enumerable.Empty<WaveProgress>()).Select(w => w.Clone()).ToList();
        lock (_lock)
        {
            _counts = countsCopy;
            _waves = wavesCopy;
            _events.Clear();
            _stage = GenerationStage.Idle;
            _stageWave = 0;
            _stageWaveCount = 0;
            _stageWaveStart = default;
            _stageWaveEnd = default;
            _stageCount = 0;
        }
    }

    public void SetStage(GenerationStage stage, int wave = 0, int waveCount = 0, DateOnly start = default,
        DateOnly end = default, int count = 0)
    {
        lock (_lock)
        {
            _stage = stage;
            _stageWave = wave;
            _stageWaveCount = waveCount;
            _stageWaveStart = start;
            _stageWaveEnd = end;
            _stageCount = count;
        }
    }

    public void SetWaveRunning(int waveIndex, bool running)
    {
        lock (_lock)
        {
            foreach (var wave in _waves.Where(w => w.Index == waveIndex))
                wave.Running = running;
        }
    }

    /// <summary>
    /// Counts one finished thread. Written and Warnings count as done; Failed counts separately.
    /// </summary>
    public void RecordThread(int waveIndex, ThreadRunStatus status, int emails, int files, decimal cost)
    {
        lock (_lock)
        {
            var wave = _waves.FirstOrDefault(w => w.Index == waveIndex);
            _counts.CostUsd += cost;
            switch (status)
            {
                case ThreadRunStatus.Written:
                case ThreadRunStatus.Warnings:
                    _counts.DoneThreads++;
                    _counts.EmailsWritten += emails;
                    _counts.FilesWritten += files;
                    if (wave != null)
                        wave.Done++;
                    if (status == ThreadRunStatus.Warnings)
                    {
                        _counts.WarningThreads++;
                        if (wave != null)
                            wave.Warnings++;
                    }
                    break;
                case ThreadRunStatus.Failed:
                    _counts.FailedThreads++;
                    if (wave != null)
                        wave.Failed++;
                    break;
            }
        }
    }

    public void SetLedgerEntries(int count)
    {
        lock (_lock)
            _counts.LedgerEntries = count;
    }

    public void SetRemainingCost(decimal amount)
    {
        lock (_lock)
            _counts.RemainingCostUsd = amount;
    }

    public void SetFinished(bool running, bool finished)
    {
        lock (_lock)
        {
            _counts.Running = running;
            _counts.Finished = finished;
        }
    }

    public void AddEvent(GenerationEvent e)
    {
        if (e == null)
            return;

        lock (_lock)
        {
            _events.Enqueue(e);

            // The Activity grid keeps 500 rows; the journal keeps everything (UI-SPEC S1)
            while (_events.Count > MaxQueuedEvents)
                _events.Dequeue();
        }
    }

    /// <summary>
    /// Copy of counts, waves and stage plus every queued event, which is removed from the queue.
    /// </summary>
    public GenerationView Drain()
    {
        lock (_lock)
        {
            var view = BuildView(_events.ToList());
            _events.Clear();
            return view;
        }
    }

    /// <summary>
    /// Same as Drain without dequeuing events.
    /// </summary>
    public GenerationView Peek()
    {
        lock (_lock)
            return BuildView(_events.ToList());
    }

    // Caller holds _lock
    private GenerationView BuildView(List<GenerationEvent> events) => new()
    {
        Counts = _counts.Clone(),
        Waves = _waves.Select(w => w.Clone()).ToList(),
        Events = events,
        Stage = _stage,
        StageWave = _stageWave,
        StageWaveCount = _stageWaveCount,
        StageWaveStart = _stageWaveStart,
        StageWaveEnd = _stageWaveEnd,
        StageCount = _stageCount
    };
}
