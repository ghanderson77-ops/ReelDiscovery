namespace ReelDiscovery.Models.Case;

public enum EntityKind
{
    Person,
    Company,
    Brand
}

public class EntitySubstitution
{
    public string Original { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public EntityKind Kind { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Substitute { get; set; } = string.Empty;

    /// <summary>
    /// True (default) = replace with Substitute; false = the user chose to keep the real name (CASE-03 override).
    /// </summary>
    public bool Apply { get; set; } = true;
}
