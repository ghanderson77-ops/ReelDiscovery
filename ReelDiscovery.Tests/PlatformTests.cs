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

    // D-22: no legacy key migration remains, so startup cannot call it
    [Fact]
    public void ApiKeyStore_HasNoLegacyMigration()
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var storeType = typeof(ReelDiscovery.Services.ApiKeyStore);

        Assert.Empty(storeType.GetMember("MigrateLegacyKey", flags));
        Assert.Empty(storeType.GetMember("LegacySettingsPath", flags));
        Assert.Empty(storeType.GetMember("LegacySettingsFileName", flags));
    }

    [Fact]
    public void Program_DoesNotMigrateLegacyKey()
    {
        var globalJson = FindGlobalJson();
        Assert.NotNull(globalJson);
        var program = File.ReadAllText(Path.Combine(Path.GetDirectoryName(globalJson!)!, "Program.cs"));

        Assert.DoesNotContain("MigrateLegacyKey", program);
        Assert.DoesNotContain("reeldiscovery.settings", program);
        Assert.Contains("Application.Run(new WizardForm())", program);
    }

    // D-20/PLAT-05: the legacy generation path is deleted, not just unreachable
    [Theory]
    [InlineData("ReelDiscovery.Services.EmailGenerator")]
    [InlineData("ReelDiscovery.Services.StorylineGenerator")]
    [InlineData("ReelDiscovery.Services.CharacterGenerator")]
    [InlineData("ReelDiscovery.Services.ThemeGenerator")]
    [InlineData("ReelDiscovery.Services.EmlFileService")]
    [InlineData("ReelDiscovery.UserControls.StepTopicInput")]
    [InlineData("ReelDiscovery.UserControls.StepStorylines")]
    [InlineData("ReelDiscovery.UserControls.StepCharacters")]
    [InlineData("ReelDiscovery.UserControls.StepGenerationConfig")]
    [InlineData("ReelDiscovery.UserControls.StepGeneration")]
    [InlineData("ReelDiscovery.UserControls.StepComplete")]
    [InlineData("ReelDiscovery.Models.Storyline")]
    [InlineData("ReelDiscovery.Models.Character")]
    [InlineData("ReelDiscovery.Models.EmailThread")]
    [InlineData("ReelDiscovery.Models.EmailMessage")]
    [InlineData("ReelDiscovery.Models.GenerationResult")]
    [InlineData("ReelDiscovery.Models.GenerationProgress")]
    [InlineData("ReelDiscovery.Models.GenerationConfig")]
    [InlineData("ReelDiscovery.Models.PresentationTheme")]
    [InlineData("ReelDiscovery.Models.WizardFlow")]
    [InlineData("ReelDiscovery.Helpers.DateHelper")]
    public void LegacyType_IsDeleted(string typeName)
    {
        Assert.Null(typeof(WizardState).Assembly.GetType(typeName));
    }

    [Fact]
    public void WizardState_HasNoLegacyMembers()
    {
        foreach (var name in new[]
        {
            "Topic", "AdditionalInstructions", "StorylineCount", "WantsDocuments", "WantsImages", "WantsVoicemails",
            "Storylines", "AISuggestedStartDate", "AISuggestedEndDate", "AISuggestedDateReasoning", "Characters",
            "CompanyDomain", "DomainThemes", "Config", "GeneratedThreads", "Result", "ApplyMediaAvailability"
        })
        {
            Assert.Empty(typeof(WizardState).GetMember(name));
        }
    }

    [Fact]
    public void MediaSettings_KeepsImageDefaults()
    {
        var settings = new WizardState().MediaSettings;

        Assert.Equal("gpt-image-2.5-flare", settings.ImageModel);
        Assert.Equal("low", settings.ImageQuality);
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
