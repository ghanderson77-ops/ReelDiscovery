namespace ReelDiscovery.Models.Qa;

/// <summary>
/// Leakage audit verdict (D-14). NotRun means no report exists yet.
/// </summary>
public enum LeakageVerdict
{
    NotRun,
    Pass,
    Warning,
    Fail,
    NotEnoughData
}

/// <summary>
/// How a feature value relates to relevance: only on relevant mail, never on relevant mail, or a lift over the base rate.
/// </summary>
public enum LeakageSignalKind
{
    OnlyOnRelevant,
    NeverOnRelevant,
    Lift
}

/// <summary>
/// One metadata feature value that tells relevant mail apart (D-14, UI-SPEC R3).
/// </summary>
public class LeakageSignal
{
    /// <summary>Plain-words feature name, e.g. "privilege banner" or "sender jane@example.com".</summary>
    public string Feature { get; set; } = string.Empty;

    public LeakageSignalKind Kind { get; set; }

    /// <summary>P(relevant | feature value), 0..1.</summary>
    public double RelevantShare { get; set; }

    /// <summary>P(relevant) over all audited emails, 0..1.</summary>
    public double BaseRate { get; set; }

    /// <summary>Number of audited emails that carry the feature value.</summary>
    public int Support { get; set; }

    /// <summary>RelevantShare - BaseRate.</summary>
    public double Lift { get; set; }
}

/// <summary>
/// Result of the metadata-only leakage audit (QA-02), stored as qa-leakage.json.
/// </summary>
public class LeakageReport
{
    public Guid PlanId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public LeakageVerdict Verdict { get; set; }

    /// <summary>Cross-validated balanced accuracy, 0..1 (0.5 is a coin flip).</summary>
    public double BalancedAccuracy { get; set; }

    /// <summary>Cross-validated ROC AUC, 0..1.</summary>
    public double Auc { get; set; }

    /// <summary>Folds actually used (fewer than 5 when a fold lacked a relevant or non-relevant email).</summary>
    public int Folds { get; set; }

    public int RelevantEmails { get; set; }
    public int NonRelevantEmails { get; set; }
    public List<LeakageSignal> Signals { get; set; } = new();
}
