using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Models.Evidence;

// Appended only: an older build reads an unknown name as a JsonException and skips the line (06-RESEARCH Pattern 12)
public enum JournalRecordType
{
    // ThreadInvalidated: Phase 7 D-13/D-15: "Regenerate thread"; older builds skip this line
    RunStarted, ThreadDone, ThreadFailed, RunEnded, AttachmentDone, AttachmentFailed, AttachmentSkipped, SideWorld, CastVoices, AttachmentsRepaired, ThreadInvalidated
}

public enum ThreadWriteStatus { Written, Warnings }

public enum ThreadFailureKind { None, Refused, Timeout, Provider, Write }

/// <summary>
/// Text of one written email, kept in the journal so a resumed run can quote and rebuild without the model.
/// </summary>
public class JournalMessage
{
    public string DocId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyPlain { get; set; } = string.Empty;
    public string SignOff { get; set; } = string.Empty;

    // Display sender "Name <email>" (D-28)
    public string From { get; set; } = string.Empty;

    // Filler titles per planned attachment, in plan order (NOISE-05 Pitfall 1)
    public List<string> AttachmentTitles { get; set; } = new();
}

/// <summary>
/// One line of generation-journal.jsonl (RUN-01). Unused fields keep their defaults for each record type.
/// </summary>
public partial class JournalRecord
{
    public const int MaxFlattenedTextChars = 20_000;
    public const int MaxAttachmentTitles = 8;
    public const int MaxTitleChars = 120;
    public const int MaxNestedAttachments = 32;
    public const int MaxMeetingKeyChars = 120;

    public JournalRecordType Type { get; set; }
    public DateTime Utc { get; set; }
    public Guid PlanId { get; set; }

    // ThreadDone / ThreadFailed
    public string ThreadId { get; set; } = string.Empty;
    public ThreadWriteStatus Status { get; set; }
    public int Attempts { get; set; }
    public List<string> Violations { get; set; } = new();

    // relative "Mailboxes/{folder}/{box}/{DocId}.eml"
    public List<string> Files { get; set; } = new();

    public List<JournalMessage> Messages { get; set; } = new();
    public List<LedgerDelta> LedgerDelta { get; set; } = new();
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }

    // PROV-07: cache parts of InputTokens (InputTokens counts all billed input)
    public int CacheWriteTokens { get; set; }
    public int CacheReadTokens { get; set; }

    // PROV-07: pre-discount category dollars of CostUsd; null on lines written before Phase 7
    public CostBreakdown? CostDetail { get; set; }

    // RunStarted: the provider that writes the corpus (D-12 reviewer choice)
    public string Provider { get; set; } = string.Empty;

    // ThreadFailed
    public ThreadFailureKind FailureKind { get; set; }
    public string Reason { get; set; } = string.Empty;

    // RunEnded
    public GenerationOutcome Outcome { get; set; }
    public string Message { get; set; } = string.Empty;

    // Attachment outcome (nested in ThreadDone / AttachmentsRepaired, or a standalone line written by older test data)
    public string AttachmentDocId { get; set; } = string.Empty;
    public string ParentDocId { get; set; } = string.Empty;
    public AttachmentType AttachmentKind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FlattenedText { get; set; } = string.Empty;
    public decimal MediaCostUsd { get; set; }

    // Repaired after later waves were written: deltas stay out of the ledger (review concern 5)
    public bool LateRepair { get; set; }

    // D-20: one persisted schedule per matched meeting (review concern 6)
    public string InviteMeetingKey { get; set; } = string.Empty;
    public DateTimeOffset? InviteStart { get; set; }
    public DateTimeOffset? InviteEnd { get; set; }

    // Commit group: attachment outcomes journaled in the same line as their thread (review concern 1)
    /// <summary>
    /// On ThreadDone: outcomes of every attachment that ran inside the thread unit. On AttachmentsRepaired: outcomes added by
    /// a repair, with Files = the rewritten copies. One line, one flush: a thread and its attachments are durable together.
    /// </summary>
    public List<JournalRecord> Attachments { get; set; } = new();

    // SideWorld
    public SideWorld? SideWorld { get; set; }

    // CastVoices (D-28; written at run start before any voicemail is spoken)
    public CastVoiceSet? CastVoices { get; set; }

    [GeneratedRegex(@"\p{Cc}")]
    private static partial Regex ControlChars();

    /// <summary>
    /// Repairs a record replayed from the journal file; never throws.
    /// </summary>
    public void Normalize()
    {
        ThreadId ??= string.Empty;
        Reason ??= string.Empty;
        Message ??= string.Empty;
        Provider ??= string.Empty;
        Violations = CaseGuide.CleanStrings(Violations);

        // Only "Mailboxes/{folder}/{box}/{name}.eml" survives, so no replayed path can leave the case folder (T-05-01)
        Files = CaseGuide.CleanStrings(Files);
        Files.RemoveAll(f => !CorpusPlan.IsSafeEmlPath(f));

        Messages = CaseGuide.CleanList(Messages);
        foreach (var message in Messages)
        {
            message.DocId ??= string.Empty;
            message.MessageId ??= string.Empty;
            message.Subject ??= string.Empty;
            message.BodyPlain ??= string.Empty;
            message.SignOff ??= string.Empty;
            message.From ??= string.Empty;
            message.AttachmentTitles = CaseGuide.CleanStrings(message.AttachmentTitles)
                .Take(MaxAttachmentTitles)
                .Select(t => Clip(t, MaxTitleChars))
                .ToList();
        }

        LedgerDelta = CaseGuide.CleanList(LedgerDelta);
        foreach (var delta in LedgerDelta)
        {
            delta.About ??= string.Empty;
            delta.Statement ??= string.Empty;
            delta.Value ??= string.Empty;
            delta.FactIds = CaseGuide.CleanStrings(delta.FactIds);
            delta.AttachmentDocId ??= string.Empty;
            if (!Enum.IsDefined(delta.Kind))
                delta.Kind = LedgerKind.Other;
            delta.FromAttachment |= delta.AttachmentDocId.Length > 0;
        }

        NormalizeAttachmentFields();

        // Nested outcomes are one level deep and only of attachment types; each is repaired like a top-level record
        Attachments = CaseGuide.CleanList(Attachments)
            .Where(a => a.Type is JournalRecordType.AttachmentDone or JournalRecordType.AttachmentFailed
                            or JournalRecordType.AttachmentSkipped
                        && !string.IsNullOrWhiteSpace(a.AttachmentDocId))
            .Take(MaxNestedAttachments)
            .ToList();
        foreach (var attachment in Attachments)
        {
            attachment.Attachments ??= new();
            attachment.Attachments.Clear();
            attachment.Normalize();
        }

        SideWorld?.Normalize();
        CastVoices?.Normalize();

        // An undefined Type is left as-is so the replayer can skip the record rather than misread it
        if (!Enum.IsDefined(Status))
            Status = ThreadWriteStatus.Warnings;
        if (!Enum.IsDefined(FailureKind))
            FailureKind = ThreadFailureKind.Provider;
        if (!Enum.IsDefined(Outcome))
            Outcome = GenerationOutcome.None;

        Attempts = Math.Max(0, Attempts);
        InputTokens = Math.Max(0, InputTokens);
        OutputTokens = Math.Max(0, OutputTokens);
        CostUsd = Math.Max(0m, CostUsd);
        MediaCostUsd = Math.Max(0m, MediaCostUsd);
        CacheWriteTokens = Math.Max(0, CacheWriteTokens);
        CacheReadTokens = Math.Max(0, CacheReadTokens);

        // A tampered line never carries negative dollars into the replayed breakdown (T-07-14)
        if (CostDetail != null)
        {
            CostDetail.InputUsd = Math.Max(0m, CostDetail.InputUsd);
            CostDetail.CacheWriteUsd = Math.Max(0m, CostDetail.CacheWriteUsd);
            CostDetail.CacheReadUsd = Math.Max(0m, CostDetail.CacheReadUsd);
            CostDetail.OutputUsd = Math.Max(0m, CostDetail.OutputUsd);
            CostDetail.BatchSavingsUsd = Math.Max(0m, CostDetail.BatchSavingsUsd);
        }
    }

    private void NormalizeAttachmentFields()
    {
        AttachmentDocId ??= string.Empty;
        ParentDocId ??= string.Empty;
        FlattenedText = Clip(FlattenedText, MaxFlattenedTextChars);
        InviteMeetingKey = Clip(InviteMeetingKey, MaxMeetingKeyChars);

        // A replayed name never leaves the mailbox folder (T-06-01)
        FileName = CorpusPlan.IsSafeFileName(FileName) ? FileName : string.Empty;
        Title = Clip(ControlChars().Replace(Title ?? string.Empty, string.Empty), MaxTitleChars);

        if (!Enum.IsDefined(AttachmentKind))
            AttachmentKind = AttachmentType.Word;

        if (InviteStart == null || InviteEnd == null || InviteEnd <= InviteStart)
        {
            InviteStart = null;
            InviteEnd = null;
        }
    }

    private static string Clip(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length > max ? value[..max] : value;
    }
}
