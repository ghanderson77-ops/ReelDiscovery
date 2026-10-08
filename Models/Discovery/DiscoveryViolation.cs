using System.Text.Json.Serialization;

namespace ReelDiscovery.Models.Discovery;

public enum DiscoveryRule
{
    D1ShortPart,
    D1ExhibitNotShown,
    D2UnknownPerson,
    D3AmountVariant,
    D3UnknownIdentifier,
    D4MissingRequest,
    D4ExtraRequest,
    D5MissingFactDetail,
    D6AfterDepositionDate,
    D7Subparts,
    D8RealEntityName
}

/// <summary>
/// One consistency problem the discovery validator found in a generated document.
/// </summary>
public class DiscoveryViolation
{
    public DiscoveryRule Rule { get; set; }

    // Request number or topic index the problem belongs to; 0 = whole document
    public int Number { get; set; }

    public int Expected { get; set; }
    public int Actual { get; set; }

    // Offending text and the canonical value it should have matched
    public string Value { get; set; } = string.Empty;
    public string Canonical { get; set; } = string.Empty;

    /// <summary>
    /// Structural problems lose to any attempt with only non-structural problems (research Pattern 8).
    /// </summary>
    [JsonIgnore]
    public bool IsStructural => Rule is DiscoveryRule.D1ShortPart or DiscoveryRule.D1ExhibitNotShown
        or DiscoveryRule.D4MissingRequest or DiscoveryRule.D4ExtraRequest;
}
