namespace ReelDiscovery.Tests.Concurrency;

public class ConcurrencyTests
{
    [Fact]
    public void NoSharedRandomInstances()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var folder in new[] { "Services", "Helpers" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("new Random(") || text.Contains("Random _random"))
                {
                    offenders.Add(file);
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReelDiscovery.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate ReelDiscovery.csproj");
    }
}
