namespace ReelDiscovery.Models.Evidence;

public enum GenerationStage { Idle, Writing, Merging, UpdatingKey, WritingStory, Stopping }

public enum GenerationEventKind
{
    RunStarted,
    RunContinued,
    WaveStarted,
    ThreadWritten,
    Retry,
    KeptWithWarnings,
    ThreadFailed,
    WaveFinished,
    RunCancelled,
    RunStopped,
    RunFinished
}

/// <summary>
/// Run totals shown on the generation step.
/// </summary>
public class GenerationCounts
{
    public int TotalThreads { get; set; }

    // Written + kept-with-warnings
    public int DoneThreads { get; set; }

    public int WarningThreads { get; set; }
    public int FailedThreads { get; set; }
    public int WaveCount { get; set; }
    public int EmailsPlanned { get; set; }
    public int EmailsWritten { get; set; }
    public int FilesWritten { get; set; }
    public int Mailboxes { get; set; }
    public int LedgerEntries { get; set; }
    public decimal CostUsd { get; set; }
    public decimal RemainingCostUsd { get; set; }
    public bool Running { get; set; }
    public bool Finished { get; set; }

    public GenerationCounts Clone() => (GenerationCounts)MemberwiseClone();
}

/// <summary>
/// Progress of one wave for the waves list.
/// </summary>
public class WaveProgress
{
    public int Index { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public int Threads { get; set; }
    public int Emails { get; set; }
    public int Done { get; set; }
    public int Warnings { get; set; }
    public int Failed { get; set; }
    public bool Running { get; set; }

    public WaveProgress Clone() => (WaveProgress)MemberwiseClone();
}

/// <summary>
/// One Activity-grid row. Fields not relevant to the Kind keep their defaults.
/// </summary>
public class GenerationEvent
{
    public GenerationEventKind Kind { get; set; }
    public DateTime LocalTime { get; set; }
    public string ThreadId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int WaveIndex { get; set; }
    public int WaveCount { get; set; }
    public DateOnly WaveStart { get; set; }
    public DateOnly WaveEnd { get; set; }
    public int Threads { get; set; }
    public int Emails { get; set; }
    public int Details { get; set; }
    public int Attempt { get; set; }
    public List<string> Violations { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public int Written { get; set; }
    public int Warnings { get; set; }
    public int Failed { get; set; }
    public int LedgerTotal { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }

    // Plan FilePath of the thread's last message, for the Activity double-click
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// UI read model of a generation run: counts, waves, recent events and the current stage.
/// </summary>
public class GenerationView
{
    public GenerationCounts Counts { get; set; } = new();
    public List<WaveProgress> Waves { get; set; } = new();
    public List<GenerationEvent> Events { get; set; } = new();
    public GenerationStage Stage { get; set; }
    public int StageWave { get; set; }
    public int StageWaveCount { get; set; }
    public DateOnly StageWaveStart { get; set; }
    public DateOnly StageWaveEnd { get; set; }
    public int StageCount { get; set; }
}
