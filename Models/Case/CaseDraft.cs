using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Models.Case;

public enum DocumentTypePreference
{
    Auto,
    Complaint,
    Petition,
    InvestigationMemo
}

public enum CourtPreference
{
    Auto,
    Federal,
    State
}

/// <summary>
/// Root of the case-driven flow: scenario, preferences, real-name substitutions and the core document.
/// </summary>
public class CaseDraft
{
    public const int MaxScenarioLength = 8_000;
    public const int CurrentFormatVersion = 3;

    // Shared with Phase 3 case.json; enums as strings
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid CaseId { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Furthest wizard stage reached; only ever moves forward (AdvanceStage).
    /// </summary>
    public CaseStage Stage { get; set; } = CaseStage.Described;

    public string Scenario { get; set; } = string.Empty;

    /// <summary>
    /// Scenario with accepted fictional substitutes applied; the only scenario text sent to document generation.
    /// </summary>
    public string EffectiveScenario { get; set; } = string.Empty;

    public ComplexityLevel Complexity { get; set; } = ComplexityLevel.Standard;
    public DocumentTypePreference DocumentPreference { get; set; }
    public CourtPreference CourtPreference { get; set; }
    public string StatePreference { get; set; } = string.Empty;
    public List<EntitySubstitution> Substitutions { get; set; } = new();
    public string EntityCheckHash { get; set; } = string.Empty;
    public CoreDocument? CoreDocument { get; set; }

    /// <summary>
    /// Incremented on every edit or regeneration; Phase 3 keys stale-marking (GUIDE-09) off it.
    /// </summary>
    public int Revision { get; set; }

    public CaseGuide? Guide { get; set; }

    /// <summary>
    /// Corpus settings, plan reference and approval state (D-04); the plan itself lives in corpus-plan.json.
    /// </summary>
    public CorpusState? Corpus { get; set; }

    /// <summary>
    /// Case folder on disk; set by the case store when loading or saving, never written into case.json.
    /// </summary>
    [JsonIgnore]
    public string FolderPath { get; set; } = string.Empty;

    [JsonIgnore]
    public ComplexityPreset Preset => ComplexityPreset.Get(Complexity);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CaseDraft FromJson(string json) =>
        JsonSerializer.Deserialize<CaseDraft>(json, JsonOptions)
        ?? throw new InvalidOperationException("The case draft file is empty.");

    public void AdvanceStage(CaseStage stage)
    {
        if (stage > Stage)
            Stage = stage;
    }

    /// <summary>
    /// Repairs a draft loaded from untrusted case.json: null lists, strings and sub-objects become empty values (IN-03); never throws.
    /// </summary>
    public void Normalize()
    {
        Scenario ??= string.Empty;
        EffectiveScenario ??= string.Empty;
        StatePreference ??= string.Empty;
        EntityCheckHash ??= string.Empty;
        Substitutions = CaseGuide.CleanList(Substitutions);
        foreach (var substitution in Substitutions)
        {
            substitution.Original ??= string.Empty;
            substitution.Aliases = CaseGuide.CleanStrings(substitution.Aliases);
            substitution.Reason ??= string.Empty;
            substitution.Substitute ??= string.Empty;
        }

        if (CoreDocument != null)
        {
            var doc = CoreDocument;
            doc.Jurisdiction ??= new();
            doc.Counsel ??= new();
            doc.Memo ??= new();
            doc.Title ??= string.Empty;
            doc.FilingDate ??= string.Empty;
            doc.TimelineStart ??= string.Empty;
            doc.TimelineEnd ??= string.Empty;
            doc.SourceHash ??= string.Empty;
            doc.Parties = CaseGuide.CleanList(doc.Parties);
            doc.Counts = CaseGuide.CleanList(doc.Counts);
            foreach (var count in doc.Counts)
                count.AgainstPartyIds = CaseGuide.CleanStrings(count.AgainstPartyIds);
            doc.FactPlan = CaseGuide.CleanList(doc.FactPlan);
            doc.Sections = CaseGuide.CleanList(doc.Sections);
            foreach (var section in doc.Sections)
                section.Paragraphs = CaseGuide.CleanStrings(section.Paragraphs);
        }

        Guide?.Normalize();
        Corpus?.Normalize();

        // Older files are upgraded on load so a saved file carries the current version and older builds refuse it
        // instead of silently dropping Corpus or its generation state (T-04-03, T-05-03); newer files never get here
        // (CaseStore checks first)
        if (FormatVersion < CurrentFormatVersion)
            FormatVersion = CurrentFormatVersion;
        if (CaseId == Guid.Empty)
            CaseId = Guid.NewGuid();
    }

    /// <summary>
    /// Returns the UI-SPEC validation message, or null when the scenario is acceptable.
    /// </summary>
    public static string? ValidateScenario(string? scenario)
    {
        var trimmed = scenario?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return "Please describe the dispute or investigation before continuing.";

        // Hard cap before any LLM call (T-02-01)
        if (trimmed.Length > MaxScenarioLength)
            return $"Your scenario is {trimmed.Length.ToString("N0", CultureInfo.InvariantCulture)} characters. Shorten it to 8,000 characters or fewer and click Next again.";

        return null;
    }

    public static string ComputeHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public string ComputeSourceHash() =>
        ComputeHash(string.Join("|", EffectiveScenario, Complexity, DocumentPreference, CourtPreference, StatePreference));

    // Culture-independent parsing only (T-02-04)
    public static bool TryParseDate(string? value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
