namespace ReelDiscovery.Models;

public class Storyline
{
    /// <summary>
    /// Upper bound for a user-edited "Est. Emails" value; keeps the email distribution math far from int overflow.
    /// </summary>
    public const int MaxSuggestedEmailCount = 10_000;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Guid> InvolvedCharacterIds { get; set; } = new();
    public string TimelineHint { get; set; } = string.Empty;
    public int SuggestedEmailCount { get; set; }

    public override string ToString() => Title;
}
