namespace ReelDiscovery.Tests;

/// <summary>
/// Source-scan guards for D-16: Slack telemetry was removed and must not come back.
/// </summary>
public class TelemetryRemovalTests
{
    private static readonly string[] ScannedExtensions = [".cs", ".csproj", ".json", ".resx", ".config"];
    private static readonly string[] ExcludedDirs = ["bin", "obj", ".git", ".planning"];

    [Fact]
    public void NoSlackWebhookInSource()
    {
        // Built from parts so this file does not match itself
        var needle = "hooks." + "slack.com";
        var root = FindRepoRoot();

        var offenders = EnumerateSourceFiles(root, ScannedExtensions, ExcludedDirs)
            .Where(f => File.ReadAllText(f).Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoTelemetryTypesRemain()
    {
        var root = FindRepoRoot();

        Assert.False(File.Exists(Path.Combine(root, "Services", "TelemetryService.cs")));
        Assert.False(File.Exists(Path.Combine(root, "Forms", "TelemetryOptInDialog.cs")));

        var offenders = EnumerateSourceFiles(root, [".cs"], ["bin", "obj", ".git", ".planning", "ReelDiscovery.Tests"])
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("TelemetryService", StringComparison.Ordinal)
                    || text.Contains("TelemetryOptInDialog", StringComparison.Ordinal);
            })
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root, string[] extensions, string[] excludedDirs)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!excludedDirs.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                    pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
            }
        }
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
