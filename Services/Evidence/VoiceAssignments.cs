using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// The tts-1 voice a voicemail caller speaks in (D-28): the voice the per-cast call chose and journaled in the CastVoices
/// record, else a stable StableRank pick.
/// </summary>
public static class VoiceAssignments
{
    // Ordinal order so the fallback index never depends on set iteration order
    private static readonly string[] Ordered = CastVoiceSet.AllowedVoices.OrderBy(v => v, StringComparer.Ordinal).ToArray();

    public static IReadOnlySet<string> Voices => CastVoiceSet.AllowedVoices;

    /// <summary>
    /// Deterministic voice for a person: StableRank of "{personId}:voice" over the six allowed voices.
    /// </summary>
    public static string Fallback(string? personId)
    {
        var rank = RenderAssignments.StableRank((personId ?? string.Empty).Trim() + ":voice");
        return Ordered[(int)(rank % (ulong)Ordered.Length)];
    }

    public static string For(string? personId, CastVoiceSet? cast)
    {
        var voice = cast?.VoiceFor(personId?.Trim())?.Trim().ToLowerInvariant();
        if (voice != null && Voices.Contains(voice))
            return voice;

        // D-28 clarification 2026-10-04: StableRank only when the per-cast call failed or skipped this person
        return Fallback(personId);
    }
}
