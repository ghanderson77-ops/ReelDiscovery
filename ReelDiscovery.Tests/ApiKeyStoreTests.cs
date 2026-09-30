using System.Text;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests;

public class ApiKeyStoreTests : IDisposable
{
    private readonly string _folder;

    public ApiKeyStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ReelDiscoveryKeyTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private void WriteLegacy(string base64Content)
    {
        File.WriteAllText(Path.Combine(_folder, "reeldiscovery.settings"), base64Content);
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void RoundTrip_BothProviders()
    {
        var store = new ApiKeyStore(_folder);
        store.SetKey(LlmProviderKind.OpenAI, "sk-o");
        store.SetKey(LlmProviderKind.Anthropic, "sk-a");

        Assert.Equal("sk-o", store.GetKey(LlmProviderKind.OpenAI));
        Assert.Equal("sk-a", store.GetKey(LlmProviderKind.Anthropic));

        var reopened = new ApiKeyStore(_folder);
        Assert.Equal("sk-o", reopened.GetKey(LlmProviderKind.OpenAI));
        Assert.Equal("sk-a", reopened.GetKey(LlmProviderKind.Anthropic));
    }

    [Fact]
    public void KeysFile_ContainsNoPlaintext()
    {
        const string canary = "sk-plaintext-canary-789";
        var store = new ApiKeyStore(_folder);
        store.SetKey(LlmProviderKind.OpenAI, canary);

        Assert.True(File.Exists(store.KeysFilePath));
        var bytes = File.ReadAllBytes(store.KeysFilePath);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain(canary, text);
        Assert.DoesNotContain(B64(canary), text);
        Assert.DoesNotContain(canary, Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void ClearKey_RemovesOnlyThatProvider()
    {
        var store = new ApiKeyStore(_folder);
        store.SetKey(LlmProviderKind.OpenAI, "sk-o");
        store.SetKey(LlmProviderKind.Anthropic, "sk-a");

        store.ClearKey(LlmProviderKind.Anthropic);

        Assert.Null(store.GetKey(LlmProviderKind.Anthropic));
        Assert.Equal("sk-o", store.GetKey(LlmProviderKind.OpenAI));
    }

    [Fact]
    public void Migrate_ImportsAndDeletesLegacyFile()
    {
        WriteLegacy(B64("sk-legacy"));
        var store = new ApiKeyStore(_folder);

        Assert.True(store.MigrateLegacyKey());
        Assert.Equal("sk-legacy", store.GetKey(LlmProviderKind.OpenAI));
        Assert.False(File.Exists(store.LegacySettingsPath));
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        WriteLegacy(B64("sk-legacy"));
        var store = new ApiKeyStore(_folder);

        Assert.True(store.MigrateLegacyKey());
        Assert.False(store.MigrateLegacyKey());
        Assert.Equal("sk-legacy", store.GetKey(LlmProviderKind.OpenAI));
    }

    [Fact]
    public void Migrate_DoesNotOverwriteExistingKey()
    {
        var store = new ApiKeyStore(_folder);
        store.SetKey(LlmProviderKind.OpenAI, "sk-new");
        WriteLegacy(B64("sk-old"));

        Assert.False(store.MigrateLegacyKey());
        Assert.Equal("sk-new", store.GetKey(LlmProviderKind.OpenAI));
        Assert.False(File.Exists(store.LegacySettingsPath));
    }

    [Fact]
    public void Migrate_InvalidBase64_LeavesFileAndReturnsFalse()
    {
        WriteLegacy("this is not base64 !!!");
        var store = new ApiKeyStore(_folder);

        Assert.False(store.MigrateLegacyKey());
        Assert.True(File.Exists(store.LegacySettingsPath));
        Assert.Null(store.GetKey(LlmProviderKind.OpenAI));
    }

    [Fact]
    public void CorruptEntry_TreatedAsAbsent()
    {
        var store = new ApiKeyStore(_folder);

        File.WriteAllText(store.KeysFilePath, "{\"openai\":\"AAAA\"}");
        Assert.Null(store.GetKey(LlmProviderKind.OpenAI));

        File.WriteAllText(store.KeysFilePath, "not json");
        Assert.Null(store.GetKey(LlmProviderKind.OpenAI));
        Assert.Null(store.GetKey(LlmProviderKind.Anthropic));
    }

    [Fact]
    public void DoesNotTouchTelemetrySettings()
    {
        var settingsPath = Path.Combine(_folder, "settings.json");
        const string telemetryJson = "{\"TelemetryEnabled\":true,\"HasMadeChoice\":true}";
        File.WriteAllText(settingsPath, telemetryJson);
        var before = File.GetLastWriteTimeUtc(settingsPath);

        WriteLegacy(B64("sk-legacy"));
        var store = new ApiKeyStore(_folder);
        store.MigrateLegacyKey();
        store.SetKey(LlmProviderKind.Anthropic, "sk-a");
        store.ClearKey(LlmProviderKind.Anthropic);

        Assert.Equal(telemetryJson, File.ReadAllText(settingsPath));
        Assert.Equal(before, File.GetLastWriteTimeUtc(settingsPath));
    }
}
