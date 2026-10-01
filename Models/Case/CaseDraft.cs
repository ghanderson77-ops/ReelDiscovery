using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    // Shared with Phase 3 case.json; enums as strings
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

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

    [JsonIgnore]
    public ComplexityPreset Preset => ComplexityPreset.Get(Complexity);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CaseDraft FromJson(string json) =>
        JsonSerializer.Deserialize<CaseDraft>(json, JsonOptions)
        ?? throw new InvalidOperationException("The case draft file is empty.");

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
