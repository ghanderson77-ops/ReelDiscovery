namespace ReelDiscovery.Models.Evidence;

/// <summary>
/// One rule a rendered thread broke.
/// </summary>
public class RenderViolation
{
    // Code "V1".."V8"; Seq 0 = whole thread; Text is plain English shown in the Activity grid and fed back on retry (D-13)
    public string Code { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string Text { get; set; } = string.Empty;
}
