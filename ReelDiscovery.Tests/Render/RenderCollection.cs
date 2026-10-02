namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Every render test class joins this collection, so renders run one at a time and never in parallel with
/// other tests. Concurrent STA render threads disturbed each other's layout passes and produced intermittent
/// one-line clipping findings in otherwise clean screens.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class RenderCollection
{
    public const string Name = "Render";
}
