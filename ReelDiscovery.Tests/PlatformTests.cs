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
