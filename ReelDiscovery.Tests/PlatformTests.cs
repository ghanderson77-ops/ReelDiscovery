using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using ReelDiscovery.Models;

namespace ReelDiscovery.Tests;

public class PlatformTests
{
    [Fact]
    public void AppAssembly_TargetsNet10()
    {
        var attr = typeof(WizardState).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.NotNull(attr);
        Assert.Contains("Version=v10.0", attr!.FrameworkName);
    }

    [Fact]
    public void GlobalJson_PinsSdk10()
    {
        var path = FindGlobalJson();
        Assert.NotNull(path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        var sdk = doc.RootElement.GetProperty("sdk");

        Assert.StartsWith("10.0.", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    [Fact]
    public void OnlyServicesAiReferencesProviderSdks()
    {
        var globalJson = FindGlobalJson();
        Assert.NotNull(globalJson);
        var root = Path.GetDirectoryName(globalJson!)!;
        var aiFolder = Path.Combine(root, "Services", "Ai") + Path.DirectorySeparatorChar;

        var sourceFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(Path.GetRelativePath(root, f)))
            .ToList();

        // Guard against a vacuous pass (wrong root)
        Assert.Contains(sourceFiles, f => f.StartsWith(aiFolder, StringComparison.OrdinalIgnoreCase));

        var offenders = sourceFiles
            .Where(f => !f.StartsWith(aiFolder, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadLines(f).Any(l =>
            {
                var line = l.TrimStart();
                return line.StartsWith("using Anthropic", StringComparison.Ordinal) ||
                       line.StartsWith("using OpenAI", StringComparison.Ordinal);
            }))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Provider SDKs referenced outside Services/Ai: " + string.Join(", ", offenders));
    }

    private static bool IsExcluded(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p is "bin" or "obj" or "ReelDiscovery.Tests" or ".git" or ".planning");
    }

    private static string? FindGlobalJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "global.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
