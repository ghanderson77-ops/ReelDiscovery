using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services;

/// <summary>
/// Persists provider API keys under %LOCALAPPDATA%\ReelDiscovery\keys.dat, each value
/// encrypted with Windows DPAPI (CurrentUser scope + app entropy). Key values are never
/// written in plaintext and never included in exception messages.
/// </summary>
public class ApiKeyStore
{
    private const string KeysFileName = "keys.dat";
    private static readonly byte[] Entropy = "ReelDiscovery.ApiKeys.v1"u8.ToArray();

    private readonly string _folder;

    public ApiKeyStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReelDiscovery");
    }

    internal string KeysFilePath => Path.Combine(_folder, KeysFileName);

    public string? GetKey(LlmProviderKind provider)
    {
        var dto = LoadDto();
        var blob = provider == LlmProviderKind.OpenAI ? dto.OpenAI : dto.Anthropic;
        if (string.IsNullOrEmpty(blob))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (CryptographicException)
        {
            // Corrupt or foreign-machine DPAPI blob: treat as absent so the user can re-enter the key
            return null;
        }
        catch (FormatException)
        {
            // Corrupt entry (not Base64): treat as absent so the user can re-enter the key
            return null;
        }
    }

    public void SetKey(LlmProviderKind provider, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ClearKey(provider);
            return;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
        var blob = Convert.ToBase64String(protectedBytes);

        var dto = LoadDto();
        if (provider == LlmProviderKind.OpenAI)
        {
            dto.OpenAI = blob;
        }
        else
        {
            dto.Anthropic = blob;
        }

        SaveDto(dto);
    }

    public void ClearKey(LlmProviderKind provider)
    {
        var dto = LoadDto();
        if (provider == LlmProviderKind.OpenAI)
        {
            dto.OpenAI = null;
        }
        else
        {
            dto.Anthropic = null;
        }

        SaveDto(dto);
    }

    private KeyFileDto LoadDto()
    {
        var path = KeysFilePath;
        if (!File.Exists(path))
        {
            return new KeyFileDto();
        }

        try
        {
            return JsonSerializer.Deserialize<KeyFileDto>(File.ReadAllText(path)) ?? new KeyFileDto();
        }
        catch (JsonException)
        {
            // Corrupt key file: treat all keys as absent so the user can re-enter them
            return new KeyFileDto();
        }
    }

    private void SaveDto(KeyFileDto dto)
    {
        Directory.CreateDirectory(_folder);
        var tmp = KeysFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto));
        // Atomic replace so a crash mid-write never leaves a truncated keys.dat
        File.Move(tmp, KeysFilePath, overwrite: true);
    }

    private class KeyFileDto
    {
        [JsonPropertyName("openai")]
        public string? OpenAI { get; set; }

        [JsonPropertyName("anthropic")]
        public string? Anthropic { get; set; }
    }
}
