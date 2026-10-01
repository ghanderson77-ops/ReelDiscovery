using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Story-tier pre-flight that lists real-world people, companies and brands named in a scenario.
/// Names that exist only in fiction (film, TV, book or game characters and companies) are
/// already fictional and are never flagged. Each entry carries each with a suggested fictional substitute.
/// The substitution itself is deterministic (NameSubstitution).
/// </summary>
public class RealEntityDetector
{
    private readonly ILlmGateway _llm;

    public const int MaxOutputTokens = 4_000;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(EntityResponseDto) };

    // Byte-stable for prompt caching: never interpolate run data into this text
    private const string SystemPrompt = "You review scenario descriptions for a fictional legal training dataset. List every real-world person, company or brand named in the scenario. The user may base the scenario on a film, TV series, book or game: characters, companies and organizations that exist only in such a work are already fictional, so never list them (for example Dunder Mifflin, Michael Scott, Walter White or Los Pollos Hermanos). List a name only when it refers to a real person or a company or brand that exists in the real world. Do not list places, cities, countries, government agencies, courts, police or regulators, laws, statutes or generic roles. For each entry give: the name as written; aliases (other forms that may appear, such as a surname alone, a first name, a nickname or a short company name); the kind; a one-sentence reason; and a suggested fictional substitute of the same kind, culture and style that is not a real person, company or brand and is not from any well-known work. Keep corporate suffixes in company substitutes (for example Inc. or LLC). If nothing qualifies, return an empty list.";

    public RealEntityDetector(ILlmGateway llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// Detects names to substitute. Every entry defaults to Apply = true.
    /// Refusals and provider failures throw LlmCallException (never a silent empty list).
    /// </summary>
    public async Task<List<EntitySubstitution>> DetectAsync(string scenario, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scenario))
        {
            return new();
        }

        var result = await _llm.GenerateAsync<EntityResponseDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Real Name Check",
            SystemPrompt = SystemPrompt,
            UserPrompt = $"Scenario:\n{scenario}",
            MaxOutputTokens = MaxOutputTokens
        }, ct);
        var response = result.GetValueOrThrow();

        var entities = new List<EntitySubstitution>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in response.Entities)
        {
            var name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            entities.Add(new EntitySubstitution
            {
                Original = name,
                Aliases = CleanAliases(name, dto.Aliases),
                Kind = dto.Kind,
                Reason = (dto.Reason ?? string.Empty).Trim(),
                Substitute = (dto.SuggestedSubstitute ?? string.Empty).Trim(),
                Apply = true
            });
        }

        return entities;
    }

    private static List<string> CleanAliases(string name, List<string> aliases)
    {
        var cleaned = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };

        foreach (var alias in aliases ?? new List<string>())
        {
            var trimmed = (alias ?? string.Empty).Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                cleaned.Add(trimmed);
            }
        }

        return cleaned;
    }

    // API Response DTOs
    internal class EntityResponseDto
    {
        [JsonPropertyName("entities")]
        [Description("Real-world names found in the scenario; empty when none")]
        public List<EntityDto> Entities { get; set; } = new();
    }

    internal class EntityDto
    {
        [JsonPropertyName("name")]
        [Description("The name exactly as written in the scenario")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("aliases")]
        [Description("Other forms of the name that may appear: surname, first name, nickname, short company name")]
        public List<string> Aliases { get; set; } = new();

        [JsonPropertyName("kind")]
        [Description("Person, Company or Brand")]
        public EntityKind Kind { get; set; }

        [JsonPropertyName("reason")]
        [Description("One sentence: why this is a real-world name")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("suggestedSubstitute")]
        [Description("A plausible fictional replacement of the same kind; never a real or famous name")]
        public string SuggestedSubstitute { get; set; } = string.Empty;
    }
}
