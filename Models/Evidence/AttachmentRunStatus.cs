namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// Per-attachment run state (D-05); Skipped = media unavailable or stopped, resumable.
/// </summary>
public enum AttachmentRunStatus { Pending, Written, Warnings, Failed, Skipped }

/// <summary>
/// Replayed status of one attachment DocId and the stage row it was counted on, used to seed GenerationSnapshot.
/// </summary>
public class AttachmentSeed
{
    public int RowIndex { get; set; }
    public AttachmentRunStatus Status { get; set; }
}
