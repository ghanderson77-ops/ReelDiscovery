using System.Text.Json.Serialization;

namespace ReelDiscovery.Models.Case;

public enum CastTier
{
    Key,
    Background
}

public enum CastRole
{
    Party,
    InHouseCounsel,
    OutsideCounsel,
    Staff
}

public enum SignatureStyle
{
    Full,
    Short,
    NameOnly,
    MobileFooter
}

public class CastMember
{
    /// <summary>
    /// Stable id in the form CH-001; allocated by code (IdAllocator), never by the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    [JsonIgnore]
    public string FullName => string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public CastTier Tier { get; set; }
    public CastRole Role { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// True when the user typed the address; organization domain changes then leave it alone.
    /// </summary>
    public bool EmailIsUserEdited { get; set; }

    public string Phone { get; set; } = string.Empty;

    // Dates are yyyy-MM-dd strings (empty = unknown); parse only via CaseDraft.TryParseDate
    public string ActiveFrom { get; set; } = string.Empty;
    public string ActiveTo { get; set; } = string.Empty;

    public string Involvement { get; set; } = string.Empty;
    public string VoiceNote { get; set; } = string.Empty;
    public SignatureStyle SignatureStyle { get; set; }

    /// <summary>
    /// Built by SignatureBuilder from the cast fields; never written by the model (D-16).
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// True while the character exists but its details have not been generated yet.
    /// </summary>
    public bool DetailsPending { get; set; }

    public bool IsUserEdited { get; set; }
}
