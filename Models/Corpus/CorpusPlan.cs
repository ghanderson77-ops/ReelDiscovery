namespace ReelDiscovery.Models.Corpus;

/// <summary>
/// Code-owned corpus plan (PLAN-02); stored in corpus-plan.json beside case.json.
/// </summary>
public class CorpusPlan
{
    public List<string> Notes { get; set; } = new();
}
