using ReelDiscovery.Models;

namespace ReelDiscovery.Tests.Concurrency;

public class ConcurrencyTests
{
    [Fact]
    public void AddError_FromParallelTasks_KeepsAllErrors()
    {
        var result = new GenerationResult();

        Parallel.For(0, 10_000, i => result.AddError($"e{i}"));

        Assert.Equal(10_000, result.Errors.Count);
    }

    [Fact]
    public void ProgressClone_IsIndependentSnapshot()
    {
        var progress = new GenerationProgress
        {
            TotalEmails = 10,
            CompletedEmails = 5,
            CurrentOperation = "Generating"
        };

        var snapshot = progress.Clone();
        progress.CompletedEmails = 9;
        progress.CurrentOperation = "Saving";

        Assert.Equal(5, snapshot.CompletedEmails);
        Assert.Equal(10, snapshot.TotalEmails);
        Assert.Equal("Generating", snapshot.CurrentOperation);
    }

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
