namespace ReelDiscovery.Models.Evidence;

public enum GenerationStage { Idle, Writing, Merging, UpdatingKey, WritingStory, Stopping, SideProjects, NonRelevant, Attachments }

// Stages-grid row type: a wave, the Non-relevant mail row or the Attachments for written threads row (UI-SPEC S1 Row 4)
public enum StageRowKind { Wave, NonRelevant, Attachments }

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
    RunFinished,
    SideProjectsReady,
    NonRelevantStarted,
    NonRelevantFinished,
    AttachmentRetry,
    AttachmentKeptWithWarnings,
    AttachmentFailed,
    AttachmentSkipped,
    MediaStopped,
    AttachmentRepairStarted,
    AttachmentsAdded,
    AttachmentRegenerated
}

/// <summary>
/// Run totals shown on the generation step.
/// </summary>
public class GenerationCounts
{
    // TotalThreads/DoneThreads/WarningThreads/FailedThreads count every generatable thread (relevant + non-relevant)
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

    // Relevant / non-relevant split of the thread counts (D-25)
    public int RelevantTotal { get; set; }
    public int RelevantDone { get; set; }
    public int NonRelevantTotal { get; set; }
    public int NonRelevantDone { get; set; }
    public int NonRelevantDailyBusiness { get; set; }
    public int NonRelevantAncillary { get; set; }
    public int NonRelevantBulk { get; set; }
    public int NonRelevantDecoy { get; set; }

    // Attachment counts are per-DocId status transitions: Done (Written + Warnings) + Failed + Skipped <= Total
    public int AttachmentsTotal { get; set; }
    public int AttachmentsDone { get; set; }
    public int AttachmentsWarnings { get; set; }
    public int AttachmentsFailed { get; set; }
    public int AttachmentsSkipped { get; set; }

    // Planned attachments by kind
    public int AttachmentsWord { get; set; }
    public int AttachmentsExcel { get; set; }
    public int AttachmentsPowerPoint { get; set; }
    public int AttachmentsInvite { get; set; }
    public int AttachmentsImage { get; set; }
    public int AttachmentsVoicemail { get; set; }

    // Image + voicemail attachments that need a paid media call (D-29)
    public int MediaPlanned { get; set; }

    // Media share of CostUsd (CostUsd already includes it)
    public decimal MediaCostUsd { get; set; }

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

    // Wave rows keep the default; the Non-relevant and Attachments rows use their own Index
    public StageRowKind RowKind { get; set; }

    public int Attachments { get; set; }
    public int AttachmentsDone { get; set; }
    public int AttachmentWarnings { get; set; }
    public int AttachmentsFailed { get; set; }
    public int AttachmentsSkipped { get; set; }

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

    // Attachment events: file name of the part
    public string FileName { get; set; } = string.Empty;

    public int Attachments { get; set; }

    // SideProjectsReady: side projects and organizations created
    public int Projects { get; set; }
    public int Organizations { get; set; }

    public int Files { get; set; }

    // ThreadWritten / NonRelevantFinished / AttachmentsAdded: whether the thread or part belongs to relevant mail
    // (ThreadWritten shows the ledger clause only for relevant threads)
    public bool Relevant { get; set; }

    public int AttachmentsDone { get; set; }
    public int AttachmentsTotal { get; set; }

    // MediaStopped: media attachments left unrendered
    public int MediaRemaining { get; set; }
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

    // Writing stage: attachments being written with the wave's threads (0 drops the progress clause)
    public int StageAttachments { get; set; }
}
