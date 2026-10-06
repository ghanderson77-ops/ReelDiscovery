using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

public class CastVoiceOutcome
{
    // Null only when a fatal provider stop (bad key, spend limit) ended the run
    public CastVoiceSet? Voices { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    // Gateway cost of the call (PROV-07); the runner journals it as is
    public CostBreakdown Cost { get; set; } = new();
}

/// <summary>
/// The per-cast voicemail voice set (D-28; user decision 2026-10-04: per-cast voice call persisted in the journal;
/// StableRank fallback only if the call fails). One cheap Volume-tier call picks a tts-1 voice for each cast member.
/// </summary>
internal static class CastVoiceBuilder
{
    public const string OperationName = "Generate Corpus - voicemail voices";
    public const int MaxOutputTokens = 8_000;
    public const int MaxInvolvementWords = 25;

    public const string SystemPrompt =
        "You choose text-to-speech voices for the people in an e-discovery training dataset. For each person pick the one voice that best fits their name, role and description: alloy (neutral, even), echo (male, warm and measured), fable (expressive storyteller), onyx (male, deep), nova (female, bright), shimmer (female, soft). Keep each person id exactly as given and return one entry per person.";

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(CastVoiceDto) };

    // Guide cast order (ordinal id), capped at CastVoiceSet.MaxVoices people
    internal static List<CastMember> OrderedCast(CaseGuide guide) =>
        (guide.Cast ?? new())
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
            .OrderBy(c => c.Id.Trim(), StringComparer.Ordinal)
            .Take(CastVoiceSet.MaxVoices)
            .ToList();

    internal static LlmRequest BuildRequest(CaseDraft draft, CaseGuide guide)
    {
        var sb = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(sb, draft, includeScenario: false);
        sb.AppendLine();
        sb.AppendLine("Voices:");
        sb.AppendLine("- alloy: neutral, even");
        sb.AppendLine("- echo: male, warm and measured");
        sb.AppendLine("- fable: expressive storyteller");
        sb.AppendLine("- onyx: male, deep");
        sb.AppendLine("- nova: female, bright");
        sb.AppendLine("- shimmer: female, soft");
        sb.AppendLine();
        sb.AppendLine("People (id | name | title | organization | involvement):");
        foreach (var member in OrderedCast(guide))
        {
            var organization = guide.FindOrganization(member.OrganizationId)?.Name?.Trim() ?? string.Empty;
            sb.AppendLine($"{member.Id.Trim()} | {member.FullName} | {member.Title?.Trim()} | {organization} | {FirstWords(member.Involvement, MaxInvolvementWords)}");
        }

        return new LlmRequest
        {
            Tier = LlmTier.Volume,
            OperationName = OperationName,
            SystemPrompt = SystemPrompt,
            UserPrompt = sb.ToString(),
            MaxOutputTokens = MaxOutputTokens
        };
    }

    private static string FirstWords(string? text, int count) =>
        string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(count));

    public static async Task<CastVoiceOutcome> BuildAsync(ILlmGateway llm, CaseDraft draft, CaseGuide guide, PlanCallRun run)
    {
        var outcome = await PlanCallRunner.RunAsync<CastVoiceDto>(llm, BuildRequest(draft, guide), run);
        var result = new CastVoiceOutcome
        {
            InputTokens = outcome.InputTokens,
            OutputTokens = outcome.OutputTokens,
            Cost = outcome.Cost.Clone()
        };

        // Fatal stop: leave the voice set unset so no fallback record is journaled
        if (run.IsFatal && outcome.Value == null)
            return result;

        result.Voices = outcome.Value == null ? Fallback(guide) : FromDto(outcome.Value, guide);
        return result;
    }

    private static CastVoiceSet FromDto(CastVoiceDto dto, CaseGuide guide)
    {
        var cast = OrderedCast(guide);
        var known = new HashSet<string>(cast.Select(c => c.Id.Trim()), StringComparer.Ordinal);
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in dto.Voices ?? new())
        {
            var personId = item?.PersonId?.Trim() ?? string.Empty;
            var voice = item?.Voice?.Trim().ToLowerInvariant() ?? string.Empty;
            if (known.Contains(personId) && VoiceAssignments.Voices.Contains(voice))
                chosen.TryAdd(personId, voice);
        }

        var set = new CastVoiceSet();
        foreach (var member in cast)
        {
            var id = member.Id.Trim();
            // D-28: StableRank only when the call gave no usable voice for this person
            var voice = chosen.TryGetValue(id, out var picked) ? picked : VoiceAssignments.Fallback(id);
            set.Voices.Add(new CastVoice { PersonId = id, Voice = voice });
        }

        set.Normalize();
        return set;
    }

    /// <summary>
    /// Every cast member on the deterministic StableRank voice (FromFallback = true).
    /// </summary>
    internal static CastVoiceSet Fallback(CaseGuide guide)
    {
        var set = new CastVoiceSet { FromFallback = true };
        foreach (var member in OrderedCast(guide))
            set.Voices.Add(new CastVoice { PersonId = member.Id.Trim(), Voice = VoiceAssignments.Fallback(member.Id) });
        set.Normalize();
        return set;
    }

    /// <summary>
    /// The journaled voice set when it covers every cast id in the guide, else null (forces a new call).
    /// </summary>
    public static CastVoiceSet? FromRecord(JournalRecord? record, CaseGuide guide)
    {
        var set = record?.CastVoices;
        if (set == null)
            return null;

        set.Normalize();
        var present = new HashSet<string>(set.Voices.Select(v => v.PersonId), StringComparer.Ordinal);
        return OrderedCast(guide).All(c => present.Contains(c.Id.Trim())) ? set : null;
    }

    // ----- strict-schema DTOs -----

    internal class CastVoiceDto
    {
        [JsonPropertyName("voices")]
        [Description("One entry per person in the request")]
        public List<CastVoiceItemDto> Voices { get; set; } = new();
    }

    internal class CastVoiceItemDto
    {
        [JsonPropertyName("personId")]
        [Description("Person id exactly as given, for example CH-001")]
        public string PersonId { get; set; } = string.Empty;

        [JsonPropertyName("voice")]
        [Description("one of alloy, echo, fable, onyx, nova, shimmer")]
        public string Voice { get; set; } = string.Empty;
    }
}
