using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Outcome of reading corpus-plan.json; a null Plan with a Reason means "no plan" (never an exception).
/// </summary>
public class PlanLoadResult
{
    public CorpusPlan? Plan { get; set; }

    /// <summary>
    /// User-facing reason the plan was not loaded; empty when nothing went wrong.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    // The file was readable but held a different plan (the backup may hold the right one)
    internal bool IdMismatch { get; set; }

    public bool Succeeded => Plan != null;
}

/// <summary>
/// Persists the immutable corpus plan in corpus-plan.json beside case.json (D-04). Callers must write the plan file
/// first and case.json (with the new PlanId) second, so a crash never leaves case.json pointing at a missing plan
/// (Pitfall 11, T-04-25). The plan being replaced is kept as corpus-plan.json.prev, and Load falls back to it when
/// case.json still names the previous plan (a crash or failed case.json save between the two writes, WR-01).
/// </summary>
public static class CorpusPlanStore
{
    public const long MaxPlanFileBytes = 100L * 1024 * 1024;

    // Tests only: 0 = use MaxPlanFileBytes. AsyncLocal so a cap set by one test never reaches a parallel test
    private static readonly AsyncLocal<long> _sizeCapOverride = new();

    internal static long SizeCapOverride { get => _sizeCapOverride.Value; set => _sizeCapOverride.Value = value; }

    private static readonly JsonSerializerOptions PlanJsonOptions = new(CaseDraft.JsonOptions) { WriteIndented = false };

    /// <summary>
    /// Plan path built only from the case folder and the constant file name, never from case.json content (T-04-26).
    /// </summary>
    public static string PlanPath(string caseFolder) => Path.Combine(caseFolder, CorpusState.PlanFileName);

    /// <summary>
    /// The previous plan kept by SaveAsync; same folder-plus-constant rule as PlanPath.
    /// </summary>
    public static string PreviousPlanPath(string caseFolder) => PlanPath(caseFolder) + ".prev";

    public static byte[] Serialize(CorpusPlan plan) => JsonSerializer.SerializeToUtf8Bytes(plan, PlanJsonOptions);

    /// <summary>
    /// Writes corpus-plan.json atomically (temp file + move) after copying the plan it replaces to corpus-plan.json.prev.
    /// Save the plan before case.json.
    /// </summary>
    public static async Task SaveAsync(string caseFolder, CorpusPlan plan, CancellationToken ct = default)
    {
        var path = PlanPath(caseFolder);
        if (File.Exists(path))
        {
            // The backup must be complete before the only copy is replaced, so it goes through the atomic writer too
            var previous = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            await CaseStore.WriteAtomicAsync(PreviousPlanPath(caseFolder), previous, ct).ConfigureAwait(false);
        }

        await CaseStore.WriteAtomicAsync(path, Serialize(plan), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads and repairs the plan; refuses missing, oversized, corrupt, newer-version and mismatched files with a
    /// user-facing reason (T-04-23, T-04-24). Never throws.
    /// </summary>
    public static PlanLoadResult Load(string caseFolder, Guid expectedPlanId)
    {
        if (string.IsNullOrWhiteSpace(caseFolder))
            return Fail($"The plan file {CorpusState.PlanFileName} is missing from the case folder.");

        var result = LoadFile(PlanPath(caseFolder), expectedPlanId);
        if (result.Succeeded || !result.IdMismatch)
            return result;

        // case.json still names the plan that a re-plan replaced: recover it from the backup (WR-01)
        var previous = LoadFile(PreviousPlanPath(caseFolder), expectedPlanId);
        if (!previous.Succeeded)
            return result;

        RestorePrevious(caseFolder);
        return previous;
    }

    // Puts the recovered plan back as corpus-plan.json, so the next re-plan backs up the plan case.json names
    private static void RestorePrevious(string caseFolder)
    {
        try
        {
            File.Copy(PreviousPlanPath(caseFolder), PlanPath(caseFolder), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the recovered plan is already loaded; the next load falls back to the backup again
        }
    }

    private static PlanLoadResult LoadFile(string path, Guid expectedPlanId)
    {
        try
        {
            if (!File.Exists(path))
                return Fail($"The plan file {CorpusState.PlanFileName} is missing from the case folder.");

            // Size cap before reading anything
            var cap = SizeCapOverride > 0 ? SizeCapOverride : MaxPlanFileBytes;
            if (new FileInfo(path).Length > cap)
                return Fail($"The plan file is larger than {MaxPlanFileBytes / (1024 * 1024)} MB.");

            var json = File.ReadAllText(path);
            if (PeekFormatVersion(json) > CorpusPlan.CurrentFormatVersion)
                return Fail("The plan was saved by a newer version of ReelDiscovery.");

            var plan = JsonSerializer.Deserialize<CorpusPlan>(json, PlanJsonOptions);
            if (plan == null)
                return Fail("The plan file could not be read: the file is empty.");

            plan.Normalize();
            if (plan.PlanId != expectedPlanId)
                return new PlanLoadResult { Reason = "The saved plan does not match this case.", IdMismatch = true };

            return new PlanLoadResult { Plan = plan };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or NotSupportedException or ArgumentException
                                       or OverflowException)
        {
            var reason = ex.Message.Trim().TrimEnd('.');
            return Fail($"The plan file could not be read: {reason}.");
        }
    }

    /// <summary>
    /// Loads the plan into draft.Corpus.Plan when case.json references one that is not loaded yet. No corpus or no
    /// PlanId is a no-op (empty Reason); a failure leaves Plan null and returns the reason.
    /// </summary>
    public static PlanLoadResult EnsureLoaded(CaseDraft draft)
    {
        var corpus = draft.Corpus;
        if (corpus == null || !corpus.HasPlan)
            return new PlanLoadResult();

        if (corpus.Plan != null)
            return new PlanLoadResult { Plan = corpus.Plan };

        var result = Load(draft.FolderPath, corpus.PlanId);
        if (result.Succeeded)
            corpus.Plan = result.Plan;
        return result;
    }

    private static PlanLoadResult Fail(string reason) => new() { Reason = reason };

    private static int PeekFormatVersion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return CorpusPlan.CurrentFormatVersion;

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(CorpusPlan.FormatVersion), StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var version))
            {
                return version;
            }
        }

        // Missing FormatVersion means the first version
        return CorpusPlan.CurrentFormatVersion;
    }
}
