namespace ReelDiscovery.Models.Qa;

/// <summary>
/// The QA scan that raised a finding (QA-03).
/// </summary>
public enum ScanCheck
{
    Timeline,
    Value,
    StoryDetail,
    Attachment
}

/// <summary>
/// One flagged email from the timeline / value / story-detail / attachment scans.
/// </summary>
public class ScanFinding
{
    public string ThreadId { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime DateSent { get; set; }
    public ScanCheck Check { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool Relevant { get; set; }
}

/// <summary>
/// Result of the QA scans, stored as qa-scans.json.
/// </summary>
public class ScanReport
{
    public Guid PlanId { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>Write time of the journal the scans read; null when there was none.</summary>
    public DateTime? JournalUtc { get; set; }

    public int ScannedEmails { get; set; }
    public List<ScanFinding> Findings { get; set; } = new();
}
