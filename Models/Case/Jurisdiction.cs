namespace ReelDiscovery.Models.Case;

public enum CourtSystem
{
    Federal,
    State
}

public class Jurisdiction
{
    /// <summary>
    /// The 50 US states (alphabetical) followed by District of Columbia; shared by the scenario
    /// step's State combo and the validator's court/state contradiction check.
    /// </summary>
    public static readonly string[] UsStates =
    {
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware",
        "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky",
        "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi",
        "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
        "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania",
        "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont",
        "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming",
        "District of Columbia"
    };

    public CourtSystem CourtSystem { get; set; }
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Full court name rendered in the caption, e.g. United States District Court for the Northern District of Illinois.
    /// </summary>
    public string CourtName { get; set; } = string.Empty;

    /// <summary>
    /// Federal district or state county, e.g. Northern District of Illinois or Harris County.
    /// </summary>
    public string District { get; set; } = string.Empty;

    public string VenueCity { get; set; } = string.Empty;
}
