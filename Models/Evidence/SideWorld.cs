namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// Other projects and topics each organization works on, so daily-business mail is about real-feeling work that is not
/// the dispute (NOISE-01). Generated once per run and kept in the journal; Normalize never throws.
/// </summary>
public class SideWorld
{
    // Complex preset allows 7 parties plus firms and vendors; organizations past the cap use code templates at prompt time (06-12)
    public const int MaxOrganizations = 24;
    public const int MaxProjects = 4;
    public const int MaxTopics = 4;
    public const int MaxNameChars = 80;

    public List<SideWorldOrganization> Organizations { get; set; } = new();

    // True when the model gave no usable side world and code templates filled it
    public bool FromFallback { get; set; }

    public void Normalize()
    {
        Organizations ??= new();
        Organizations.RemoveAll(o => o == null);
        if (Organizations.Count > MaxOrganizations)
            Organizations.RemoveRange(MaxOrganizations, Organizations.Count - MaxOrganizations);

        foreach (var organization in Organizations)
        {
            organization.OrganizationId = organization.OrganizationId?.Trim() ?? string.Empty;
            organization.Projects ??= new();
            organization.Projects.RemoveAll(p => p == null);
            foreach (var project in organization.Projects)
            {
                project.Name = Clip(project.Name);
                project.Topics = (project.Topics ?? new())
                    .Select(Clip)
                    .Where(t => t.Length > 0)
                    .Take(MaxTopics)
                    .ToList();
            }

            organization.Projects = organization.Projects.Where(p => p.Name.Length > 0).Take(MaxProjects).ToList();
        }
    }

    internal static string Clip(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length <= MaxNameChars)
            return text;
        // Never end on a high surrogate: a lone surrogate makes the OpenXml writer throw (IN-01)
        var cut = char.IsHighSurrogate(text[MaxNameChars - 1]) ? MaxNameChars - 1 : MaxNameChars;
        return text[..cut].TrimEnd();
    }
}

public class SideWorldOrganization
{
    // ORG-xxx from the case guide
    public string OrganizationId { get; set; } = string.Empty;
    public List<SideProject> Projects { get; set; } = new();
}

public class SideProject
{
    public string Name { get; set; } = string.Empty;
    public List<string> Topics { get; set; } = new();
}

public class CastVoice
{
    // CH-xxx
    public string PersonId { get; set; } = string.Empty;

    // One of CastVoiceSet.AllowedVoices
    public string Voice { get; set; } = string.Empty;
}

/// <summary>
/// The tts-1 voice of every cast member, chosen in one call at run start (D-28 clarification 2026-10-04) and journaled
/// before any voicemail is spoken, so a resumed run never re-asks and a person keeps one voice. Normalize never throws.
/// </summary>
public class CastVoiceSet
{
    public const int MaxVoices = 200;

    public static readonly IReadOnlySet<string> AllowedVoices =
        new HashSet<string>(new[] { "alloy", "echo", "fable", "onyx", "nova", "shimmer" }, StringComparer.Ordinal);

    public List<CastVoice> Voices { get; set; } = new();

    // True when the model gave no usable voices and code assigned them
    public bool FromFallback { get; set; }

    /// <summary>
    /// The voice for a cast member, or null when the person has none.
    /// </summary>
    public string? VoiceFor(string? personId)
    {
        if (string.IsNullOrEmpty(personId))
            return null;
        return Voices.FirstOrDefault(v => v != null && v.PersonId == personId)?.Voice;
    }

    public void Normalize()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CastVoice>();
        foreach (var entry in Voices ?? new())
        {
            if (entry == null || kept.Count >= MaxVoices)
                continue;

            var personId = entry.PersonId?.Trim() ?? string.Empty;
            var voice = entry.Voice?.Trim().ToLowerInvariant() ?? string.Empty;
            if (personId.Length == 0 || !AllowedVoices.Contains(voice) || !seen.Add(personId))
                continue;

            entry.PersonId = personId;
            entry.Voice = voice;
            kept.Add(entry);
        }

        Voices = kept;
    }
}
