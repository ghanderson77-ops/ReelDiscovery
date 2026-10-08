using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Append-only record kinds in provider-batches.jsonl. Values are only ever added, never renumbered.
/// </summary>
public enum BatchLedgerRecordType
{
    Submitted,
    Collected,
    Cancelled,
    OrphansBilled,
    Consumed
}

public class BatchLedgerItem
{
    public string CustomId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;

    /// <summary>Model the request was sent to; prices results nobody consumed (orphans).</summary>
    public string ModelId { get; set; } = string.Empty;
}

public class BatchLedgerRecord
{
    public BatchLedgerRecordType Type { get; set; }
    public DateTime Utc { get; set; }
    public LlmProviderKind Provider { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public DateTime SubmittedUtc { get; set; }
    public string StageLabel { get; set; } = string.Empty;
    public string InputFileId { get; set; } = string.Empty;
    public string OutputFileId { get; set; } = string.Empty;

    /// <summary>Submitted: every item in the batch. Consumed: the one delivered item.</summary>
    public List<BatchLedgerItem> Items { get; set; } = new();

    public int Done { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Replayed view of the ledger. Batch ids and custom ids compare ordinally.
/// </summary>
public class BatchLedgerState
{
    /// <summary>
    /// False when the ledger exists but could not be read (a lock that outlasted the retries, no access). Callers must not
    /// treat that as "no batches" (CR-A05): batch mode falls back to live calls and Start Over keeps the file.
    /// </summary>
    public bool Readable { get; set; } = true;

    public Dictionary<string, BatchLedgerRecord> SubmittedById { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> CollectedIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> CancelledIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> OrphansBilledIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ConsumedCustomIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Consumed items keyed by (batch id, custom id): request ids are deterministic, so after "Regenerate thread" the same
    /// custom id can be in a newer batch whose result has not been used yet (CR-A02).
    /// </summary>
    public HashSet<(string BatchId, string CustomId)> ConsumedItems { get; set; } = new();
    public Dictionary<string, string> BatchIdByCustomId { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Batches of this plan that were submitted but neither collected nor cancelled, oldest first.
    /// </summary>
    public IEnumerable<BatchLedgerRecord> Pending(Guid planId) =>
        SubmittedById.Values
            .Where(r => r.PlanId == planId && !CollectedIds.Contains(r.BatchId) && !CancelledIds.Contains(r.BatchId))
            .OrderBy(r => r.SubmittedUtc)
            .ThenBy(r => r.BatchId, StringComparer.Ordinal);

    /// <summary>True when this batch's result for this custom id was already delivered (ordinal comparison).</summary>
    public bool IsConsumed(string batchId, string customId) => ConsumedItems.Contains((batchId ?? string.Empty, customId ?? string.Empty));
}

/// <summary>
/// Durable record of provider batches for one case folder (RUN-04). Kept apart from the evidence journal so the answer-key
/// projection never sees batch bookkeeping (D-05). A Submitted line is flushed before the submit path returns, so a crash
/// never loses a paid batch (D-09).
/// </summary>
public partial class BatchLedger
{
    public const string FileName = "provider-batches.jsonl";
    public const string ResultsFolderName = "batch-results";

    private const int MaxMoveAttempts = 5;

    // CR-A05: attempts to read the ledger through a transient lock (waits 50 ms x attempt in between)
    private const int MaxReadAttempts = 5;

    public const string UnreadableMessage =
        "The provider batch record could not be read, so it was kept. Try again once nothing else has the case folder open.";

    private static readonly JsonSerializerOptions LineOptions = new(CaseDraft.JsonOptions) { WriteIndented = false };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _caseFolder;

    public BatchLedger(string caseFolder)
    {
        _caseFolder = caseFolder ?? throw new ArgumentNullException(nameof(caseFolder));
        FilePath = PathFor(caseFolder);
    }

    public string FilePath { get; }

    // Case folder + constant name only, never a path from data (T-04-26)
    public static string PathFor(string caseFolder) => Path.Combine(caseFolder, FileName);

    /// <summary>
    /// Appends one record as a single line and flushes it to disk. IO failures propagate to the caller.
    /// </summary>
    public async Task AppendAsync(BatchLedgerRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Serialized before taking the lock so the whole line is ready for one write
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, LineOptions) + "\n");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_caseFolder);

            // One write per line + flush: a kill leaves at most a torn last line, which replay skips
            await using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            // A kill can leave a torn last line with no newline; start a fresh line so this record is not fused onto it
            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                if (stream.ReadByte() != '\n')
                    stream.WriteByte((byte)'\n');
            }

            stream.Seek(0, SeekOrigin.End);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Rebuilds the ledger state from disk. Never throws: a missing file gives an empty state; a file that cannot be read
    /// gives an empty state with <see cref="BatchLedgerState.Readable"/> false (CR-A05); torn, garbage or unknown-type
    /// lines are skipped (T-07-20).
    /// </summary>
    public BatchLedgerState Replay()
    {
        var state = new BatchLedgerState();

        string text;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!File.Exists(FilePath))
                    return state;

                // CR-A05: shares ReadWrite (and Delete) so an append in progress (ours or another instance's) does not make
                // the ledger look empty
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text = reader.ReadToEnd();
                break;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Deleted between the check and the open: no ledger
                return state;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MaxReadAttempts)
            {
                // Transient lock (OneDrive, antivirus, search indexer)
                Thread.Sleep(50 * attempt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                           or ArgumentException)
            {
                // Unreadable is not empty: callers fall back to live calls and never delete what they could not read
                state.Readable = false;
                return state;
            }
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            BatchLedgerRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<BatchLedgerRecord>(line, LineOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException
                                           or ArgumentException)
            {
                // Torn last line or hand-edited garbage: skip it
                continue;
            }

            if (record == null || !Enum.IsDefined(record.Type))
                continue;

            Apply(state, record);
        }

        return state;
    }

    private static void Apply(BatchLedgerState state, BatchLedgerRecord record)
    {
        var batchId = record.BatchId ?? string.Empty;
        var items = record.Items ?? new List<BatchLedgerItem>();

        switch (record.Type)
        {
            case BatchLedgerRecordType.Submitted:
                if (batchId.Length == 0 || !state.SubmittedById.TryAdd(batchId, record))
                    return;

                record.Items = items;
                foreach (var item in items.Where(i => i != null && !string.IsNullOrEmpty(i.CustomId)))
                    state.BatchIdByCustomId[item.CustomId] = batchId;
                break;
            case BatchLedgerRecordType.Collected:
                if (batchId.Length > 0)
                    state.CollectedIds.Add(batchId);
                break;
            case BatchLedgerRecordType.Cancelled:
                if (batchId.Length > 0)
                    state.CancelledIds.Add(batchId);
                break;
            case BatchLedgerRecordType.OrphansBilled:
                if (batchId.Length > 0)
                    state.OrphansBilledIds.Add(batchId);
                break;
            case BatchLedgerRecordType.Consumed:
                foreach (var item in items.Where(i => i != null && !string.IsNullOrEmpty(i.CustomId)))
                {
                    state.ConsumedCustomIds.Add(item.CustomId);
                    state.ConsumedItems.Add((batchId, item.CustomId));
                }
                break;
        }
    }

    /// <summary>
    /// Number of this plan's batches that were submitted but neither collected nor cancelled. Never throws; an unreadable
    /// ledger counts as 0 (Start Over keeps it, CR-A05).
    /// </summary>
    public static int PendingBatchCount(string caseFolder, Guid planId)
    {
        if (string.IsNullOrWhiteSpace(caseFolder))
            return 0;

        return new BatchLedger(caseFolder).Replay().Pending(planId).Count();
    }

    /// <summary>
    /// Number of batches of any plan that were submitted but neither collected nor cancelled (Start Over, WR-A05). Never
    /// throws; an unreadable ledger counts as 0 (Start Over keeps it, CR-A05).
    /// </summary>
    public static int PendingBatchCountAllPlans(string caseFolder)
    {
        if (string.IsNullOrWhiteSpace(caseFolder))
            return 0;

        var state = new BatchLedger(caseFolder).Replay();
        return state.SubmittedById.Values.Count(r => !state.CollectedIds.Contains(r.BatchId) && !state.CancelledIds.Contains(r.BatchId));
    }

    /// <summary>
    /// Path of the cached, classified results of one batch. Always inside {caseFolder}/batch-results (T-07-19).
    /// </summary>
    public string ResultsPathFor(string batchId) =>
        Path.Combine(_caseFolder, ResultsFolderName, SafeFileId(batchId) + ".jsonl");

    /// <summary>
    /// Writes the classified results of one batch atomically (temp file + move). Only classified text and token counts are
    /// persisted, never request headers or keys (T-07-21). IO failures propagate as exceptions.
    /// </summary>
    public void SaveResults(string batchId, IReadOnlyList<BatchItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var path = ResultsPathFor(batchId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var builder = new StringBuilder();
        foreach (var item in items.Where(i => i != null))
            builder.Append(JsonSerializer.Serialize(item, LineOptions)).Append('\n');

        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                       FileOptions.WriteThrough))
            {
                fs.Write(Encoding.UTF8.GetBytes(builder.ToString()));
                fs.Flush(true);
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MaxMoveAttempts)
                {
                    // Transient lock (OneDrive, antivirus, search indexer)
                    Thread.Sleep(100 * attempt);
                }
            }
        }
        catch (Exception)
        {
            // Leave no temp files behind; the caller decides how to report the failure
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>
    /// Reads cached results of one batch. Never throws: empty when the file is missing or unreadable; bad lines are skipped.
    /// </summary>
    public IReadOnlyList<BatchItemResult> ReadResults(string batchId)
    {
        var results = new List<BatchItemResult>();

        string[] lines;
        try
        {
            var path = ResultsPathFor(batchId);
            if (!File.Exists(path))
                return results;

            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            // Unreadable cache: the caller downloads the results again
            return results;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            BatchItemResult? item;
            try
            {
                item = JsonSerializer.Deserialize<BatchItemResult>(line, LineOptions);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException
                                           or ArgumentException)
            {
                // Corrupt line: skip it; the item is treated as missing
                continue;
            }

            if (item == null || string.IsNullOrEmpty(item.CustomId) || !Enum.IsDefined(item.Outcome))
                continue;

            if (item.Response != null && (!Enum.IsDefined(item.Response.Outcome) || !Enum.IsDefined(item.Response.FailureKind)))
                continue;

            results.Add(item);
        }

        return results;
    }

    /// <summary>
    /// Provider ids are never used raw in file names: ids matching ^[A-Za-z0-9_-]{1,64}$ are kept, anything else becomes
    /// "h" + 40 hex chars of its SHA-256 (T-07-19).
    /// </summary>
    public static string SafeFileId(string providerId)
    {
        var id = providerId ?? string.Empty;
        if (SafeIdRegex().IsMatch(id))
            return id;

        return "h" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..40].ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file is harmless and never read
        }
    }

    // \z, not $: $ also matches before a trailing newline
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex SafeIdRegex();
}
