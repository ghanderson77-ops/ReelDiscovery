using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Discovery;

/// <summary>
/// User selections for the ancillary discovery documents, saved in case.json (D-29).
/// </summary>
public class DiscoverySettings
{
    public const int MinPages = 10;
    public const int MaxPages = 60;
    public const int MinRfp = 5;
    public const int MaxRfp = 80;

    // FRCP 33(a)(1): at most 25 interrogatories including discrete subparts (D-18)
    public const int MaxInterrogatories = 25;
    public const int MinRogs = 1;
    public const int MinRfa = 1;
    public const int MaxRfa = 50;

    // At most six deposition witnesses (D-12)
    public const int MaxWitnesses = 6;

    // CH-xxx ids of the deponents, in order
    public List<string> WitnessIds { get; set; } = new();

    // Testimony pages per deposition excerpt (D-15)
    public int PagesPerExcerpt { get; set; } = 25;

    public bool RfpEnabled { get; set; } = true;
    public int RfpCount { get; set; } = 25;
    public bool RogEnabled { get; set; } = true;
    public int RogCount { get; set; } = 18;
    public bool RfaEnabled { get; set; } = true;
    public int RfaCount { get; set; } = 20;

    // PTY-xx ids from the core document parties
    public string PropoundingPartyId { get; set; } = string.Empty;
    public string RespondingPartyId { get; set; } = string.Empty;

    // Complexity defaults are re-applied only while this is false
    public bool UserTouched { get; set; }

    /// <summary>
    /// Default selections for a complexity level (D-15, D-17, D-18, D-19).
    /// </summary>
    public static DiscoverySettings ForPreset(ComplexityLevel level) => level switch
    {
        ComplexityLevel.Simple => new DiscoverySettings { PagesPerExcerpt = 15, RfpCount = 15, RogCount = 12, RfaCount = 10 },
        ComplexityLevel.Complex => new DiscoverySettings { PagesPerExcerpt = 40, RfpCount = 40, RogCount = 25, RfaCount = 30 },
        _ => new DiscoverySettings { PagesPerExcerpt = 25, RfpCount = 25, RogCount = 18, RfaCount = 20 }
    };

    /// <summary>
    /// Repairs settings loaded from untrusted case.json (T-08-01); never throws.
    /// </summary>
    public void Normalize()
    {
        PagesPerExcerpt = Math.Clamp(PagesPerExcerpt, MinPages, MaxPages);
        RfpCount = Math.Clamp(RfpCount, MinRfp, MaxRfp);
        RogCount = Math.Clamp(RogCount, MinRogs, MaxInterrogatories);
        RfaCount = Math.Clamp(RfaCount, MinRfa, MaxRfa);

        // Blank and duplicate ids are dropped; order is kept
        WitnessIds = CaseGuide.CleanStrings(WitnessIds)
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxWitnesses)
            .ToList();

        PropoundingPartyId = (PropoundingPartyId ?? string.Empty).Trim();
        RespondingPartyId = (RespondingPartyId ?? string.Empty).Trim();
    }
}
