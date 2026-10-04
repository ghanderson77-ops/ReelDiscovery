namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// How the last generation run ended. Fatal = provider stop such as Auth/SpendLimit; InternalError = unexpected app
/// exception during a run.
/// </summary>
public enum GenerationOutcome { None, Finished, Cancelled, Fatal, InternalError }

/// <summary>
/// Per-thread run status projected from the journal; used by GenerationRunState and GenerationSnapshot.
/// </summary>
public enum ThreadRunStatus { Pending, Written, Warnings, Failed }

/// <summary>
/// Relevant-email generation state saved in case.json under Corpus.Generation (D-10): ledger, last outcome, cached counts.
/// The append-only journal beside case.json is the source of truth for per-thread progress.
/// </summary>
public class GenerationState
{
    public const string JournalFileName = "generation-journal.jsonl";

    // Plan the run (and its journal) belongs to
    public Guid RunPlanId { get; set; }

    public NarrativeLedger Ledger { get; set; } = new();

    public GenerationOutcome LastOutcome { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public DateTime? LastRunEndedUtc { get; set; }

    // journal state last materialized into case.json; reopen re-projects when the journal is newer
    public DateTime? ProjectedUtc { get; set; }

    // Cached counts for the Summary/reopen view
    public int ThreadsTotal { get; set; }
    public int ThreadsWritten { get; set; }
    public int ThreadsWithWarnings { get; set; }
    public int ThreadsFailed { get; set; }
    public int EmailsWritten { get; set; }
    public int EmailsFailed { get; set; }
    public int FilesWritten { get; set; }
    public int Mailboxes { get; set; }
    public decimal CostUsd { get; set; }

    // Phase 6 cached counts; ThreadsTotal..ThreadsFailed above keep meaning relevant threads
    public int NonRelevantTotal { get; set; }
    public int NonRelevantWritten { get; set; }
    public int NonRelevantWithWarnings { get; set; }
    public int NonRelevantFailed { get; set; }
    public int AttachmentsTotal { get; set; }
    public int AttachmentsWritten { get; set; }
    public int AttachmentsWithWarnings { get; set; }
    public int AttachmentsFailed { get; set; }
    public int AttachmentsSkipped { get; set; }

    // Media (image + voicemail) share of CostUsd
    public decimal MediaCostUsd { get; set; }

    // Last answer-key / story-so-far write error; empty when fine
    public string KeyError { get; set; } = string.Empty;
    public string StoryError { get; set; } = string.Empty;

    /// <summary>
    /// Repairs state loaded from untrusted case.json; never throws.
    /// </summary>
    public void Normalize()
    {
        Ledger ??= new();
        Ledger.Normalize();
        LastMessage ??= string.Empty;
        KeyError ??= string.Empty;
        StoryError ??= string.Empty;
        if (!Enum.IsDefined(LastOutcome))
            LastOutcome = GenerationOutcome.None;

        ThreadsTotal = Math.Max(0, ThreadsTotal);
        ThreadsWritten = Math.Max(0, ThreadsWritten);
        ThreadsWithWarnings = Math.Max(0, ThreadsWithWarnings);
        ThreadsFailed = Math.Max(0, ThreadsFailed);
        EmailsWritten = Math.Max(0, EmailsWritten);
        EmailsFailed = Math.Max(0, EmailsFailed);
        FilesWritten = Math.Max(0, FilesWritten);
        Mailboxes = Math.Max(0, Mailboxes);
        CostUsd = Math.Max(0m, CostUsd);

        // Missing in Phase 5 case.json files: default 0 (06-RESEARCH Existing Run State)
        NonRelevantTotal = Math.Max(0, NonRelevantTotal);
        NonRelevantWritten = Math.Max(0, NonRelevantWritten);
        NonRelevantWithWarnings = Math.Max(0, NonRelevantWithWarnings);
        NonRelevantFailed = Math.Max(0, NonRelevantFailed);
        AttachmentsTotal = Math.Max(0, AttachmentsTotal);
        AttachmentsWritten = Math.Max(0, AttachmentsWritten);
        AttachmentsWithWarnings = Math.Max(0, AttachmentsWithWarnings);
        AttachmentsFailed = Math.Max(0, AttachmentsFailed);
        AttachmentsSkipped = Math.Max(0, AttachmentsSkipped);
        MediaCostUsd = Math.Max(0m, MediaCostUsd);
    }
}
