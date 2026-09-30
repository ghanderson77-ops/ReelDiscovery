namespace ReelDiscovery.Models;

public class GenerationResult
{
    public int TotalEmailsGenerated { get; set; }
    public int TotalThreadsGenerated { get; set; }
    public int TotalAttachmentsGenerated { get; set; }
    public int WordDocumentsGenerated { get; set; }
    public int ExcelDocumentsGenerated { get; set; }
    public int PowerPointDocumentsGenerated { get; set; }
    public int ImagesGenerated { get; set; }
    public int CalendarInvitesGenerated { get; set; }
    public int VoicemailsGenerated { get; set; }
    public string OutputFolder { get; set; } = string.Empty;
    public TimeSpan ElapsedTime { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool WasCancelled { get; set; }

    private readonly object _errorsLock = new();

    /// <summary>
    /// Thread-safe way to record an error from parallel generation tasks.
    /// </summary>
    public void AddError(string message)
    {
        lock (_errorsLock)
        {
            Errors.Add(message);
        }
    }
}

public class GenerationProgress
{
    public int TotalEmails { get; set; }
    public int CompletedEmails { get; set; }
    public int TotalAttachments { get; set; }
    public int CompletedAttachments { get; set; }
    public int TotalImages { get; set; }
    public int CompletedImages { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
    public string? CurrentStoryline { get; set; }

    public double OverallPercentage =>
        TotalEmails == 0 ? 0 : (CompletedEmails * 100.0) / TotalEmails;

    /// <summary>
    /// Immutable-by-convention snapshot for progress reporting. All members are value types
    /// or strings, so a shallow copy is a full snapshot.
    /// </summary>
    public GenerationProgress Clone() => (GenerationProgress)MemberwiseClone();
}
