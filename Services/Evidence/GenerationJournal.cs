using System.Text;
using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// What the generation journal says about a plan; Replay fills it and never throws.
/// </summary>
public class JournalReplay
{
    public bool Exists { get; set; }

    /// <summary>
    /// User-facing reason the journal could not be read; empty when it was read.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    // A record belongs to a different plan (the plan was replaced after the run started)
    public bool PlanChanged { get; set; }
    public Guid JournalPlanId { get; set; }

    public bool HasRunStarted { get; set; }

    // Last ThreadDone / ThreadFailed record per thread of the current plan
    public Dictionary<string, JournalRecord> LastByThread { get; set; } = new(StringComparer.Ordinal);

    public JournalRecord? LastRunEnded { get; set; }

    /// <summary>
    /// Spend across every ThreadDone, ThreadFailed and RunEnded record of the current plan, including attempts a later
    /// retry replaced and calls a fatal stop left unjournaled per thread.
    /// </summary>
    public decimal SpentCostUsd { get; set; }

    // Blank, torn or corrupt lines
    public int SkippedLines { get; set; }

    // Distinct threads finished under another plan (shown on the Plan changed card)
    public int ForeignThreadCount { get; set; }

    public DateTime LastWriteUtc { get; set; }

    public bool Readable => Exists && Reason.Length == 0;
}

/// <summary>
/// Append-only JSONL run journal beside case.json (RUN-01, RUN-06). Each finished thread is one flushed line, so a kill
/// keeps every thread written before it; replay is the source of truth for Continue.
/// </summary>
public class GenerationJournal
{
    public const long MaxJournalBytes = 200L * 1024 * 1024;

    // Tests only: 0 = use MaxJournalBytes
    internal static long SizeCapOverride;

    private static readonly JsonSerializerOptions LineOptions = new(CaseDraft.JsonOptions) { WriteIndented = false };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public GenerationJournal(string caseFolder)
    {
        FilePath = PathFor(caseFolder);
    }

    public string FilePath { get; }

    // Case folder + constant name only, never a path from data (T-04-26)
    public static string PathFor(string caseFolder) => Path.Combine(caseFolder, GenerationState.JournalFileName);

    public static bool Exists(string caseFolder) =>
        !string.IsNullOrWhiteSpace(caseFolder) && File.Exists(PathFor(caseFolder));

    /// <summary>
    /// Appends one record as a single line and flushes it to disk. IO failures propagate to the caller.
    /// </summary>
    public async Task AppendAsync(JournalRecord record, CancellationToken ct = default)
    {
        // Serialized before taking the lock so the whole line is ready for one write
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, LineOptions) + "\n");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // One write per line + flush: a kill leaves at most a torn last line, which replay skips (RUN-01)
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
    /// Reads the journal for a plan: records for other plans or unknown threads are dropped, unsafe paths removed and the
    /// last record per thread kept. Missing, oversized and unreadable files are reported, never thrown (T-05-29).
    /// </summary>
    public static JournalReplay Replay(string caseFolder, CorpusPlan plan)
    {
        var replay = new JournalReplay();
        if (string.IsNullOrWhiteSpace(caseFolder))
            return replay;

        var threadIds = new HashSet<string>(
            plan.Threads.Where(t => t != null && !string.IsNullOrEmpty(t.Id)).Select(t => t.Id),
            StringComparer.Ordinal);
        var foreignThreads = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var path = PathFor(caseFolder);
            if (!File.Exists(path))
                return replay;

            replay.Exists = true;
            replay.LastWriteUtc = File.GetLastWriteTimeUtc(path);
            if (TooLarge(path))
            {
                replay.Reason = $"The saved run is larger than {MaxJournalBytes / (1024 * 1024)} MB.";
                return replay;
            }

            foreach (var record in ReadRecords(path, () => replay.SkippedLines++))
            {
                if (record.PlanId != plan.PlanId)
                {
                    replay.PlanChanged = true;
                    replay.JournalPlanId = record.PlanId;
                    if (record.Type == JournalRecordType.ThreadDone && record.ThreadId.Length > 0)
                        foreignThreads.Add(record.ThreadId);
                    continue;
                }

                switch (record.Type)
                {
                    case JournalRecordType.RunStarted:
                        replay.HasRunStarted = true;
                        break;
                    case JournalRecordType.RunEnded:
                        replay.LastRunEnded = record;
                        replay.SpentCostUsd += Math.Max(0m, record.CostUsd);
                        break;
                    case JournalRecordType.ThreadDone:
                    case JournalRecordType.ThreadFailed:
                        // Later lines win: a retried thread's newest outcome replaces the earlier one, but its spend stays counted
                        if (threadIds.Contains(record.ThreadId))
                        {
                            replay.LastByThread[record.ThreadId] = record;
                            replay.SpentCostUsd += Math.Max(0m, record.CostUsd);
                        }
                        break;
                }
            }

            replay.ForeignThreadCount = foreignThreads.Count;
            return replay;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            replay.Exists = true;
            replay.LastByThread.Clear();
            replay.SpentCostUsd = 0m;
            replay.Reason = $"The saved run could not be read: {ex.Message.Trim().TrimEnd('.')}.";
            return replay;
        }
    }

    /// <summary>
    /// Every safe file listed by a ThreadDone record, whatever plan it belongs to (for sweeping written mailboxes).
    /// Returns what could be read; never throws.
    /// </summary>
    public static List<string> ListAllFiles(string caseFolder)
    {
        var files = new List<string>();
        if (!Exists(caseFolder))
            return files;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var path = PathFor(caseFolder);
            if (TooLarge(path))
                return files;

            foreach (var record in ReadRecords(path, () => { }))
            {
                if (record.Type != JournalRecordType.ThreadDone)
                    continue;

                foreach (var file in record.Files.Where(f => CorpusPlan.IsSafeEmlPath(f) && seen.Add(f)))
                    files.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            // Partial list: the caller sweeps what was readable; planned paths cover the rest
        }

        return files;
    }

    /// <summary>
    /// Deletes the journal; a no-op when it is missing. IO failures propagate so the caller can report them.
    /// </summary>
    public static void Delete(string caseFolder)
    {
        var path = PathFor(caseFolder);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static bool TooLarge(string path)
    {
        var cap = SizeCapOverride > 0 ? SizeCapOverride : MaxJournalBytes;
        return new FileInfo(path).Length > cap;
    }

    // Parsed, normalized records with a defined Type; skipped lines are reported through onSkipped
    private static IEnumerable<JournalRecord> ReadRecords(string path, Action onSkipped)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                onSkipped();
                continue;
            }

            JournalRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<JournalRecord>(line, LineOptions);
            }
            catch (JsonException)
            {
                // Torn or corrupt line (killed mid-write): skip it, keep everything else
                onSkipped();
                continue;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                // Same as a corrupt line: a value the serializer cannot map
                onSkipped();
                continue;
            }

            if (record == null)
            {
                onSkipped();
                continue;
            }

            record.Normalize();

            // Unknown record types (newer build or tampering) are dropped, not misread
            if (!Enum.IsDefined(record.Type))
                continue;

            yield return record;
        }
    }
}
