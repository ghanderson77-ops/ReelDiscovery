using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Models.Evidence;

public enum JournalRecordType { RunStarted, ThreadDone, ThreadFailed, RunEnded }

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
}

/// <summary>
/// One line of generation-journal.jsonl (RUN-01). Unused fields keep their defaults for each record type.
/// </summary>
public class JournalRecord
{
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

    // ThreadFailed
    public ThreadFailureKind FailureKind { get; set; }
    public string Reason { get; set; } = string.Empty;

    // RunEnded
    public GenerationOutcome Outcome { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Repairs a record replayed from the journal file; never throws.
    /// </summary>
    public void Normalize()
    {
        ThreadId ??= string.Empty;
        Reason ??= string.Empty;
        Message ??= string.Empty;
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
        }

        LedgerDelta = CaseGuide.CleanList(LedgerDelta);
        foreach (var delta in LedgerDelta)
        {
            delta.About ??= string.Empty;
            delta.Statement ??= string.Empty;
            delta.Value ??= string.Empty;
            delta.FactIds = CaseGuide.CleanStrings(delta.FactIds);
            if (!Enum.IsDefined(delta.Kind))
                delta.Kind = LedgerKind.Other;
        }

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
    }
}
