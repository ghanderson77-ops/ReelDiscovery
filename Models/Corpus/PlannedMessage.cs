namespace ReelDiscovery.Models.Corpus;

public enum MessageKind
{
    New,
    Reply,
    ReplyAll,
    Forward
}

/// <summary>
/// Why a message is relevant: Own = carries fact evidence; Quoted = inherits via quoted or forwarded text.
/// </summary>
public enum RelevanceBasis
{
    None,
    Own,
    Quoted
}

/// <summary>
/// One planned email; date, people, structure, labels and attachments are fixed in code before any prose (PLAN-02).
/// </summary>
public class PlannedMessage
{
    // "T-0042-M03" (thread-relative, opaque)
    public string Id { get; set; } = string.Empty;

    // "DM-000317", assigned chronologically after scheduling
    public string DocId { get; set; } = string.Empty;

    // 1-based; Seq order == time order
    public int Seq { get; set; }

    // 0 = thread root
    public int ReplyToSeq { get; set; }

    public MessageKind Kind { get; set; }

    // CH-xxx or EXT-xxx
    public string FromId { get; set; } = string.Empty;
    public List<string> ToIds { get; set; } = new();
    public List<string> CcIds { get; set; } = new();

    // Sender-local offset; System.Text.Json writes ISO 8601 with offset
    public DateTimeOffset SentAt { get; set; }

    // IANA id used for SentAt
    public string TimeZoneId { get; set; } = string.Empty;

    // Own evidence links (empty for Quoted-only)
    public List<string> FactIds { get; set; } = new();
    public RelevanceBasis Basis { get; set; }
    public bool IsHot { get; set; }

    // LLM-written for relevant threads; empty for non-relevant (Phase 6 writes)
    public string Intent { get; set; } = string.Empty;
    public List<string> MustConvey { get; set; } = new();

    public List<PlannedAttachment> Attachments { get; set; } = new();

    // Planned sender-copy path, relative to the case folder
    public string FilePath { get; set; } = string.Empty;

    // Every mailbox holding a copy
    public List<string> CustodianIds { get; set; } = new();
}
