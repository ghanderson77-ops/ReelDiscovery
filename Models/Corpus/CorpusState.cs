using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Corpus;

public enum CorpusFileKind
{
    AnswerKeyCsv,
    AnswerKeyXlsx,
    ReviewProtocol
}

/// <summary>
/// A plan projection written to the case folder, stamped with the plan it came from.
/// </summary>
public class WrittenCorpusFile
{
    public CorpusFileKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public DateTime WrittenUtc { get; set; }
}

/// <summary>
/// Corpus part of case.json: settings, a reference to the plan in corpus-plan.json, and approval state (D-04).
/// </summary>
public class CorpusState
{
    public const string PlanFileName = "corpus-plan.json";

    public CorpusSettings Settings { get; set; } = new();

    /// <summary>
    /// Id of the plan stored in the sibling plan file; Guid.Empty = no plan yet.
    /// </summary>
    public Guid PlanId { get; set; }

    public string PlanGuideHash { get; set; } = string.Empty;
    public string PlanSettingsHash { get; set; } = string.Empty;
    public DateTime? PlanCreatedUtc { get; set; }
    public bool Approved { get; set; }
    public DateTime? ApprovedUtc { get; set; }

    // Hashes the user answered Not Now for on the stale banner
    public string DismissedStaleGuideHash { get; set; } = string.Empty;
    public string DismissedSettingsHash { get; set; } = string.Empty;

    public List<WrittenCorpusFile> WrittenFiles { get; set; } = new();

    // Loaded from corpus-plan.json by CorpusPlanStore; never serialized into case.json
    [JsonIgnore]
    public CorpusPlan? Plan { get; set; }

    [JsonIgnore]
    public bool HasPlan => PlanId != Guid.Empty;

    /// <summary>
    /// Repairs state loaded from untrusted case.json; never throws.
    /// </summary>
    public void Normalize()
    {
        Settings ??= new();
        Settings.Normalize();
        PlanGuideHash ??= string.Empty;
        PlanSettingsHash ??= string.Empty;
        DismissedStaleGuideHash ??= string.Empty;
        DismissedSettingsHash ??= string.Empty;
        WrittenFiles = CaseGuide.CleanList(WrittenFiles);
        foreach (var file in WrittenFiles)
            file.FileName ??= string.Empty;
    }
}
