namespace ReelDiscovery.Models.Case;

public enum OrganizationKind
{
    Party,
    LawFirm,
    Agency,
    ThirdParty,
    Individual
}

public class CaseOrganization
{
    /// <summary>
    /// Stable id in the form ORG-01; allocated by code (IdAllocator), never by the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public OrganizationKind Kind { get; set; }

    /// <summary>
    /// Core document party id (party-N) for Kind Party; empty otherwise.
    /// </summary>
    public string PartyId { get; set; } = string.Empty;

    /// <summary>
    /// Organizations this one represents (law firm -> client organization ids).
    /// </summary>
    public List<string> RepresentsOrgIds { get; set; } = new();

    public string Domain { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// IANA time zone id, e.g. America/Chicago.
    /// </summary>
    public string TimeZoneId { get; set; } = string.Empty;
}
