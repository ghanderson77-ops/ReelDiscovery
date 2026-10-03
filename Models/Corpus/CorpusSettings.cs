using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Corpus;

/// <summary>
/// Result of checking a user-entered corpus date window against the dated key facts (D-10).
/// </summary>
public class WindowClamp
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public bool StartInvalid { get; set; }
    public bool EndInvalid { get; set; }
    public bool StartClamped { get; set; }
    public bool EndClamped { get; set; }
    public bool EndBeforeStart { get; set; }
}

/// <summary>
/// User-controlled corpus settings saved in case.json (D-06..D-11).
/// </summary>
public class CorpusSettings
{
    public const int MinEmails = 50;
    public const int MaxEmails = 5_000;
    public const int MinRelevantPercent = 5;
    public const int MaxRelevantPercent = 60;
    public const int DefaultRelevantPercent = 20;
    public const int MaxPrefixLength = 6;
    public const int MaxHotPerHotFact = 5;
    public const int MaxPrivilegedPercent = 30;
    public const int MaxAttachmentPercent = 60;

    public static readonly int[] SizePresets = { 250, 1_000, 2_500, 5_000 };

    // Daily business / ancillary / newsletter-spam-personal / decoy
    public static readonly int[] DefaultMix = { 50, 20, 20, 10 };

    public int TotalEmails { get; set; } = 1_000;
    public int RelevantPercent { get; set; } = DefaultRelevantPercent;

    // Non-relevant mix; shares of the non-relevant emails, summing to 100
    public int DailyBusinessPercent { get; set; } = 50;
    public int AncillaryPercent { get; set; } = 20;
    public int NewsletterPercent { get; set; } = 20;
    public int DecoyPercent { get; set; } = 10;

    // yyyy-MM-dd; parse only via CaseDraft.TryParseDate
    public string WindowStart { get; set; } = string.Empty;
    public string WindowEnd { get; set; } = string.Empty;

    // The user's saved choice; the effective set comes from EnabledAttachmentTypes (D-11)
    public bool IncludeOffice { get; set; } = true;
    public bool IncludeCalendar { get; set; } = true;
    public bool IncludeImages { get; set; } = true;
    public bool IncludeVoicemail { get; set; } = true;

    // Advanced overrides; null = automatic (D-09)
    public int? HotPerHotFact { get; set; }
    public int? PrivilegedPercent { get; set; }
    public int? AttachmentPercent { get; set; }

    /// <summary>
    /// DocID prefix (A-Z, at most 6 letters); empty = derived from the case.
    /// </summary>
    public string DocIdPrefix { get; set; } = string.Empty;

    [JsonIgnore]
    public int RelevantEmails => RelevantCount(TotalEmails, RelevantPercent);

    [JsonIgnore]
    public int NonRelevantEmails => TotalEmails - RelevantEmails;

    [JsonIgnore]
    public int MixTotal => DailyBusinessPercent + AncillaryPercent + NewsletterPercent + DecoyPercent;

    /// <summary>
    /// The one relevant/non-relevant split used by the settings screen and the planner (Pitfall 4).
    /// </summary>
    public static int RelevantCount(int total, int percent) =>
        (int)Math.Round(total * percent / 100.0, MidpointRounding.AwayFromZero);

    public static int DefaultTotalFor(ComplexityLevel level) => level switch
    {
        ComplexityLevel.Simple => 250,
        ComplexityLevel.Complex => 2_500,
        _ => 1_000
    };

    /// <summary>
    /// First and last dated key fact, or null when no fact has a valid yyyy-MM-dd date.
    /// </summary>
    public static (DateTime First, DateTime Last)? FactRange(CaseGuide? guide)
    {
        if (guide == null)
            return null;

        var dates = new List<DateTime>();
        foreach (var fact in guide.Facts.Where(f => f != null))
        {
            if (CaseDraft.TryParseDate(fact.Date, out var date))
                dates.Add(date);
        }

        return dates.Count == 0 ? null : (dates.Min(), dates.Max());
    }

    /// <summary>
    /// First key fact minus one month to last key fact plus two months; ("", "") without dated facts (D-10).
    /// </summary>
    public static (string Start, string End) DefaultWindow(CaseGuide? guide)
    {
        var range = FactRange(guide);
        if (range == null)
            return (string.Empty, string.Empty);

        return (Format(range.Value.First.AddMonths(-1)), Format(range.Value.Last.AddMonths(2)));
    }

    public static CorpusSettings CreateDefault(CaseDraft draft)
    {
        var (start, end) = DefaultWindow(draft.Guide);
        return new CorpusSettings
        {
            TotalEmails = DefaultTotalFor(draft.Complexity),
            WindowStart = start,
            WindowEnd = end
        };
    }

    /// <summary>
    /// Widens a window so every dated key fact stays inside; invalid dates are flagged and left as typed.
    /// </summary>
    public static WindowClamp ClampWindow(string start, string end, CaseGuide? guide)
    {
        var clamp = new WindowClamp { Start = start ?? string.Empty, End = end ?? string.Empty };
        var hasStart = CaseDraft.TryParseDate(start, out var startDate);
        var hasEnd = CaseDraft.TryParseDate(end, out var endDate);
        clamp.StartInvalid = !hasStart;
        clamp.EndInvalid = !hasEnd;

        if (hasStart && hasEnd && endDate < startDate)
            clamp.EndBeforeStart = true;

        var range = FactRange(guide);
        if (range != null)
        {
            if (hasStart && startDate > range.Value.First)
            {
                clamp.Start = Format(range.Value.First);
                clamp.StartClamped = true;
            }

            if (hasEnd && endDate < range.Value.Last)
            {
                clamp.End = Format(range.Value.Last);
                clamp.EndClamped = true;
            }
        }

        return clamp;
    }

    /// <summary>
    /// Attachment types the planner may use; images and voicemail only when OpenAI media is available (D-11).
    /// </summary>
    public IReadOnlyList<AttachmentType> EnabledAttachmentTypes(bool mediaAvailable)
    {
        var types = new List<AttachmentType>();
        if (IncludeOffice)
        {
            types.Add(AttachmentType.Word);
            types.Add(AttachmentType.Excel);
            types.Add(AttachmentType.PowerPoint);
        }

        if (IncludeCalendar)
            types.Add(AttachmentType.CalendarInvite);
        if (mediaAvailable && IncludeImages)
            types.Add(AttachmentType.Image);
        if (mediaAvailable && IncludeVoicemail)
            types.Add(AttachmentType.Voicemail);

        return types;
    }

    /// <summary>
    /// SHA-256 of the serialized settings; the plan stamps its basis with it (staleness only, not a security control).
    /// </summary>
    public string ComputeHash() => CaseDraft.ComputeHash(JsonSerializer.Serialize(this, CaseDraft.JsonOptions));

    /// <summary>
    /// Repairs settings loaded from untrusted case.json: clamps every number to its range and sanitizes strings (T-04-01).
    /// </summary>
    public void Normalize()
    {
        TotalEmails = Math.Clamp(TotalEmails, MinEmails, MaxEmails);
        RelevantPercent = Math.Clamp(RelevantPercent, MinRelevantPercent, MaxRelevantPercent);

        // Clamp each share before summing: Enumerable.Sum is checked and overflows on crafted values (CR-01)
        var mix = new[] { DailyBusinessPercent, AncillaryPercent, NewsletterPercent, DecoyPercent }
            .Select(x => Math.Clamp(x, 0, 100)).ToArray();
        if (mix.Sum() == 0)
            mix = DefaultMix.ToArray();
        else if (mix.Sum() != 100)
            mix = Apportion.Largest(100, mix.Select(x => (double)x).ToList(), 0).ToArray();
        DailyBusinessPercent = mix[0];
        AncillaryPercent = mix[1];
        NewsletterPercent = mix[2];
        DecoyPercent = mix[3];

        WindowStart ??= string.Empty;
        WindowEnd ??= string.Empty;

        HotPerHotFact = ClampOverride(HotPerHotFact, 1, MaxHotPerHotFact);
        PrivilegedPercent = ClampOverride(PrivilegedPercent, 0, MaxPrivilegedPercent);
        AttachmentPercent = ClampOverride(AttachmentPercent, 0, MaxAttachmentPercent);

        DocIdPrefix = SanitizePrefix(DocIdPrefix);
    }

    /// <summary>
    /// Keeps A-Z only (after upper-casing), at most six letters, so no path or formula characters reach DocIDs.
    /// </summary>
    public static string SanitizePrefix(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var c in prefix.ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z')
                builder.Append(c);
            if (builder.Length == MaxPrefixLength)
                break;
        }

        return builder.ToString();
    }

    // Negative = automatic; otherwise clamped into range
    private static int? ClampOverride(int? value, int min, int max) =>
        value is null or < 0 ? null : Math.Clamp(value.Value, min, max);

    private static string Format(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
