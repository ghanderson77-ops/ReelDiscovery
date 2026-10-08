using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Outcome of reading discovery-documents.json; a null Set means "no set" (never an exception).
/// </summary>
public class DiscoveryLoadResult
{
    public DiscoveryDocumentSet? Set { get; set; }

    /// <summary>
    /// User-facing reason the set was not loaded; empty when nothing went wrong or the file is simply missing.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    public bool Missing { get; set; }

    public bool Succeeded => Set != null;
}

/// <summary>
/// Persists generated discovery content in discovery-documents.json beside case.json and resolves the .docx paths
/// under the case's Discovery folder (D-29).
/// </summary>
public static class DiscoveryStore
{
    public const string FolderName = "Discovery";
    public const long MaxFileBytes = 20L * 1024 * 1024;

    private const string DocxExtension = ".docx";

    // Leaves room for the case folder and "\Discovery\" under the 260-character Windows path limit (research Pitfall 10)
    private const int MaxFileNameLength = 120;

    // Tests only: 0 = use MaxFileBytes. AsyncLocal so a cap set by one test never reaches a parallel test
    private static readonly AsyncLocal<long> _sizeCapOverride = new();

    internal static long SizeCapOverride { get => _sizeCapOverride.Value; set => _sizeCapOverride.Value = value; }

    private static readonly JsonSerializerOptions JsonOptions = new(CaseDraft.JsonOptions) { WriteIndented = false };

    /// <summary>
    /// Sidecar path built only from the case folder and the constant file name, never from file content (T-08-02).
    /// </summary>
    public static string SetPath(string caseFolder) => Path.Combine(caseFolder, DiscoveryDocumentSet.FileName);

    public static string FolderPath(string caseFolder) => Path.Combine(caseFolder, FolderName);

    /// <summary>
    /// Reads and repairs the set; refuses oversized, corrupt, newer-version and other-case files with a user-facing
    /// reason (T-08-03, T-08-04). A missing file sets Missing with an empty Reason. Never throws.
    /// </summary>
    public static DiscoveryLoadResult Load(string caseFolder, Guid caseId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caseFolder))
                return new DiscoveryLoadResult { Missing = true };

            var path = SetPath(caseFolder);
            if (!File.Exists(path))
                return new DiscoveryLoadResult { Missing = true };

            // Size cap before reading anything
            var cap = SizeCapOverride > 0 ? SizeCapOverride : MaxFileBytes;
            if (new FileInfo(path).Length > cap)
                return Fail($"The discovery documents file is larger than {MaxFileBytes / (1024 * 1024)} MB.");

            var json = File.ReadAllText(path);
            if (PeekFormatVersion(json) > DiscoveryDocumentSet.CurrentFormatVersion)
                return Fail("The discovery documents file was saved by a newer version of ReelDiscovery.");

            var set = JsonSerializer.Deserialize<DiscoveryDocumentSet>(json, JsonOptions);
            if (set == null)
                return Fail("The discovery documents file could not be read: the file is empty.");

            set.Normalize();
            if (set.CaseId != caseId)
                return Fail("The discovery documents file does not match this case.");

            return new DiscoveryLoadResult { Set = set };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                       or OverflowException)
        {
            var reason = ex.Message.Trim().TrimEnd('.');
            return Fail($"The discovery documents file could not be read: {reason}.");
        }
    }

    /// <summary>
    /// The saved set, or a new empty set for the case when none can be loaded.
    /// </summary>
    public static DiscoveryDocumentSet LoadOrNew(string caseFolder, Guid caseId) =>
        Load(caseFolder, caseId).Set ?? new DiscoveryDocumentSet { CaseId = caseId };

    /// <summary>
    /// Writes discovery-documents.json atomically (temp file + move).
    /// </summary>
    public static async Task SaveAsync(string caseFolder, DiscoveryDocumentSet set, CancellationToken ct = default)
    {
        await CaseStore.WriteAtomicAsync(SetPath(caseFolder), JsonSerializer.SerializeToUtf8Bytes(set, JsonOptions), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// True for a bare .docx file name: no folder part, no "..", no invalid characters, not rooted.
    /// Guards a name read back from the sidecar before it becomes a path (T-08-02).
    /// </summary>
    public static bool IsDiscoveryFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length <= DocxExtension.Length)
            return false;
        if (!name.EndsWith(DocxExtension, StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;
        return Path.GetFileName(name) == name && !Path.IsPathRooted(name);
    }

    /// <summary>
    /// Full path of a discovery .docx inside the case's Discovery folder, or null when the name is unsafe.
    /// </summary>
    public static string? DocxPath(string caseFolder, string? fileName)
    {
        if (!IsDiscoveryFileName(fileName))
            return null;

        // Never trust names read back from the sidecar: the resolved path must stay inside Discovery\ (ASVS V12)
        var root = Path.GetFullPath(FolderPath(caseFolder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, fileName!));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// "{case} - {document}.docx" (D-29); the case part is truncated first so the name stays short.
    /// </summary>
    public static string FileNameFor(string caseFolderName, string documentPart)
    {
        var casePart = CaseFolderNaming.Sanitize(caseFolderName);
        var docPart = CaseFolderNaming.Sanitize(documentPart);

        // 3 = " - "
        var caseLimit = Math.Max(10, MaxFileNameLength - docPart.Length - 3);
        if (casePart.Length > caseLimit)
            casePart = casePart[..caseLimit].TrimEnd('.', ' ');

        var name = $"{casePart} - {docPart}{DocxExtension}";
        return IsDiscoveryFileName(name) ? name : $"{docPart}{DocxExtension}";
    }

    /// <summary>
    /// True when the case's Discovery folder holds at least one .docx.
    /// </summary>
    public static bool HasDocuments(string caseFolder)
    {
        try
        {
            var folder = FolderPath(caseFolder);
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*" + DocxExtension).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable folder counts as empty: this only decides whether to offer "open folder"
            return false;
        }
    }

    private static DiscoveryLoadResult Fail(string reason) => new() { Reason = reason };

    private static int PeekFormatVersion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return DiscoveryDocumentSet.CurrentFormatVersion;

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(DiscoveryDocumentSet.FormatVersion), StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var version))
            {
                return version;
            }
        }

        // Missing FormatVersion means the first version
        return DiscoveryDocumentSet.CurrentFormatVersion;
    }
}
