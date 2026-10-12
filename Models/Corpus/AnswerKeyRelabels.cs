namespace ReelDiscovery.Models.Corpus;

/// <summary>A user relabel harvested from the answer-key files, keyed by (PlanId, DocId) (D-04, D-05).</summary>
public class AnswerKeyRelabel
{
    public Guid PlanId { get; set; }
    public string DocId { get; set; } = string.Empty;

    /// <summary>Canonical "Relevant" or "Not Relevant".</summary>
    public string Relevance { get; set; } = string.Empty;

    /// <summary>The user's Rationale cell, verbatim; empty keeps the projected rationale (D-23).</summary>
    public string Rationale { get; set; } = string.Empty;

    public DateTime CapturedUtc { get; set; }

    /// <summary>Constant key file name it was read from ("answer-key.csv" or "answer-key.xlsx").</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>What one key file held when the app last wrote it (D-20): the per-file baseline for relabel votes.</summary>
public class AnswerKeyFileSnapshot
{
    public Guid PlanId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the exact bytes written; a change fingerprint, not a security control.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public DateTime WrittenUtc { get; set; }
    public Dictionary<string, AnswerKeyRelabel> Applied { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Contents of answer-key-relabels.json (D-01). Never part of case.json or the journal.</summary>
public class AnswerKeyRelabelSet
{
    public int Version { get; set; } = 1;
    public List<AnswerKeyRelabel> Relabels { get; set; } = new();

    /// <summary>Keyed by constant key file name.</summary>
    public Dictionary<string, AnswerKeyFileSnapshot> Files { get; set; } = new(StringComparer.Ordinal);
}
