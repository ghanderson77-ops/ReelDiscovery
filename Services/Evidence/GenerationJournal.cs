using System.Text;
using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

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

    // Last outcome per planned attachment DocId, only from records whose thread and parent own that attachment
    public Dictionary<string, JournalRecord> LastByAttachment { get; set; } = new(StringComparer.Ordinal);

    public JournalRecord? LastRunEnded { get; set; }

    public JournalRecord? LastSideWorld { get; set; }
    public JournalRecord? LastCastVoices { get; set; }

    // Read-order index of the newest RunStarted / RunEnded line; -1 = none
    public int LastRunStartedLine { get; set; } = -1;
    public int LastRunEndedLine { get; set; } = -1;

    /// <summary>
    /// True only when the newest RunStarted has a RunEnded after it; an older RunEnded never hides an interrupted Continue
    /// (review 06-13 MEDIUM).
    /// </summary>
    public bool LatestAttemptEnded => LastRunEnded != null && LastRunEndedLine > LastRunStartedLine;

    /// <summary>
    /// Spend across every ThreadDone, ThreadFailed, attachment, SideWorld, CastVoices and RunEnded record of the current
    /// plan (text and media), including attempts a later retry replaced and calls a fatal stop left unjournaled per thread.
    /// </summary>
    public decimal SpentCostUsd { get; set; }

    // Media share (images, speech) of SpentCostUsd, superseded attempts included (D-29)
    public decimal MediaSpentCostUsd { get; set; }

    // PROV-07/D-16: summed CostDetail of every itemized record across all sessions (text only; media is above)
    public CostBreakdown SpentDetail { get; set; } = new();

    // Text spend no CostDetail explains (pre-Phase-7 lines); SpentDetail.TotalUsd + MediaSpentCostUsd + this == SpentCostUsd
    public decimal UnsplitSpentUsd { get; set; }

    // Provider of the newest RunStarted that recorded one; empty for pre-Phase-7 journals (D-12 reviewer choice)
    public string WriterProvider { get; set; } = string.Empty;

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

    // Tests only: 0 = use MaxJournalBytes. AsyncLocal so a cap set by one test never reaches a parallel test
    private static readonly AsyncLocal<long> _sizeCapOverride = new();

    internal static long SizeCapOverride { get => _sizeCapOverride.Value; set => _sizeCapOverride.Value = value; }

    /// <summary>
    /// IN-06: appends stop at 90% of the replay cap so the journal never grows past what replay reads; the small RunEnded
    /// audit line may still use the rest.
    /// </summary>
    public static long StopBytes => Cap * 9 / 10;

    public static string FullMessage =>
        $"The saved run is close to its {MaxJournalBytes / (1024 * 1024)} MB limit, so the run stopped to keep everything saved so far readable.";

    private static long Cap => SizeCapOverride > 0 ? SizeCapOverride : MaxJournalBytes;

    private volatile bool _isFull;

    /// <summary>True once an append was refused because the journal reached <see cref="StopBytes"/> (IN-06).</summary>
    public bool IsFull => _isFull;

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
    /// Appends one record as a single line and flushes it to disk. IO failures propagate to the caller. A line that would
    /// take the journal past <see cref="StopBytes"/> (RunEnded: past the replay cap) is refused with a
    /// <see cref="JournalFullException"/>, so the journal stays readable and the run can stop cleanly (IN-06).
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

            // + 1 for a newline that may be added after a torn last line
            var limit = record.Type == JournalRecordType.RunEnded ? Cap : StopBytes;
            if (stream.Length + bytes.Length + 1 > limit)
            {
                _isFull = true;
                throw new JournalFullException(FullMessage);
            }

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
        var owners = AttachmentOwners(plan);

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

            var line = -1;
            foreach (var record in ReadRecords(path, () => replay.SkippedLines++))
            {
                line++;
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
                        replay.LastRunStartedLine = line;
                        if (record.Provider.Length > 0)
                            replay.WriterProvider = record.Provider;
                        break;
                    case JournalRecordType.RunEnded:
                        // Phase 7: CostUsd = unjournaled text, MediaCostUsd = unjournaled media; older lines (combined
                        // CostUsd, no media, no detail) replay to the same total and count as not itemized
                        replay.LastRunEnded = record;
                        replay.LastRunEndedLine = line;
                        AddSpend(replay, record);
                        break;
                    case JournalRecordType.ThreadDone:
                        // Later lines win: a retried thread's newest outcome replaces the earlier one, but its spend stays counted
                        if (threadIds.Contains(record.ThreadId))
                        {
                            replay.LastByThread[record.ThreadId] = record;
                            AddSpend(replay, record, withMedia: false);

                            // A fresh ThreadDone carries the full attachment state of that render
                            foreach (var owned in owners.Where(o => o.Value.ThreadId == record.ThreadId))
                                replay.LastByAttachment.Remove(owned.Key);
                            foreach (var attachment in record.Attachments)
                                ApplyAttachment(replay, attachment, record.ThreadId, owners);
                        }
                        break;
                    case JournalRecordType.ThreadFailed:
                        if (threadIds.Contains(record.ThreadId))
                        {
                            replay.LastByThread[record.ThreadId] = record;
                            AddSpend(replay, record);
                        }
                        break;
                    case JournalRecordType.AttachmentsRepaired:
                        // A repair is only valid on top of a written thread
                        var written = replay.LastByThread.TryGetValue(record.ThreadId, out var thread)
                                      && thread.Type == JournalRecordType.ThreadDone;
                        AddSpend(replay, record, withMedia: false);
                        foreach (var attachment in record.Attachments)
                        {
                            if (written)
                                ApplyAttachment(replay, attachment, record.ThreadId, owners);
                            else
                                AddSpend(replay, attachment);
                        }
                        break;
                    case JournalRecordType.AttachmentDone:
                    case JournalRecordType.AttachmentFailed:
                    case JournalRecordType.AttachmentSkipped:
                        ApplyAttachment(replay, record, record.ThreadId, owners);
                        break;
                    case JournalRecordType.SideWorld:
                        replay.LastSideWorld = record;
                        AddSpend(replay, record, withMedia: false);
                        break;
                    case JournalRecordType.CastVoices:
                        // D-28: the last voice set wins; replay never re-asks for voices
                        replay.LastCastVoices = record;
                        AddSpend(replay, record, withMedia: false);
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
            replay.LastByAttachment.Clear();
            replay.LastSideWorld = null;
            replay.LastCastVoices = null;
            replay.LastRunEnded = null;
            replay.LastRunStartedLine = -1;
            replay.LastRunEndedLine = -1;
            replay.SpentCostUsd = 0m;
            replay.MediaSpentCostUsd = 0m;
            replay.SpentDetail = new CostBreakdown();
            replay.UnsplitSpentUsd = 0m;
            replay.WriterProvider = string.Empty;
            replay.Reason = $"The saved run could not be read: {ex.Message.Trim().TrimEnd('.')}.";
            return replay;
        }
    }

    // Planned owner (thread, parent message DocId) of every attachment DocId; the first occurrence wins
    private static Dictionary<string, (string ThreadId, string ParentDocId)> AttachmentOwners(CorpusPlan plan)
    {
        var owners = new Dictionary<string, (string ThreadId, string ParentDocId)>(StringComparer.Ordinal);
        foreach (var thread in plan.Threads.Where(t => t != null && !string.IsNullOrEmpty(t.Id)))
        {
            foreach (var message in thread.Messages.Where(m => m != null && !string.IsNullOrEmpty(m.DocId)))
            {
                foreach (var attachment in message.Attachments.Where(a => a != null && !string.IsNullOrEmpty(a.DocId)))
                    owners.TryAdd(attachment.DocId, (thread.Id, message.DocId));
            }
        }

        return owners;
    }

    private static bool Owns(JournalRecord attachment, string threadId,
        Dictionary<string, (string ThreadId, string ParentDocId)> owners) =>
        owners.TryGetValue(attachment.AttachmentDocId, out var owner)
        && owner.ThreadId == threadId
        && owner.ParentDocId == attachment.ParentDocId;

    /// <summary>
    /// Counts one record's spend: text CostUsd (itemized through CostDetail when present) plus, when withMedia, its
    /// MediaCostUsd. Whatever CostDetail does not explain goes to UnsplitSpentUsd, a safety net so the listed items always
    /// add up to the total (revision 1, blocker 2).
    /// </summary>
    private static void AddSpend(JournalReplay replay, JournalRecord record, bool withMedia = true)
    {
        var text = Math.Max(0m, record.CostUsd);
        var media = withMedia ? Math.Max(0m, record.MediaCostUsd) : 0m;
        replay.SpentCostUsd += text + media;
        replay.MediaSpentCostUsd += media;

        if (record.CostDetail != null)
        {
            replay.SpentDetail.Add(record.CostDetail);
            replay.UnsplitSpentUsd += Math.Max(0m, text - record.CostDetail.TotalUsd);
        }
        else
        {
            replay.UnsplitSpentUsd += text;
        }
    }

    private static void ApplyAttachment(JournalReplay replay, JournalRecord attachment, string threadId,
        Dictionary<string, (string ThreadId, string ParentDocId)> owners)
    {
        // Spend always counts; state only from records that belong to this plan's parent and thread (review concern 2)
        AddSpend(replay, attachment);
        if (!Owns(attachment, threadId, owners))
            return;

        attachment.ThreadId = threadId;
        replay.LastByAttachment[attachment.AttachmentDocId] = attachment;
    }

    /// <summary>
    /// Every safe file listed by a ThreadDone or AttachmentsRepaired record, whatever plan it belongs to (for sweeping written mailboxes).
    /// Returns what could be read; never throws.
    /// </summary>
    public static List<string> ListAllFiles(string caseFolder)
    {
        TryListAllFiles(caseFolder, out var files);
        return files;
    }

    /// <summary>
    /// Same as <see cref="ListAllFiles"/>; returns false when the journal exists but could not be read in full (over the
    /// size cap or an IO error mid-read), so the caller knows files may be missing from the list. Never throws.
    /// </summary>
    public static bool TryListAllFiles(string caseFolder, out List<string> files)
    {
        files = new List<string>();
        if (!Exists(caseFolder))
            return true;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var path = PathFor(caseFolder);
            if (TooLarge(path))
                return false;

            foreach (var record in ReadRecords(path, () => { }))
            {
                // AttachmentsRepaired lists the copies a repair rewrote
                if (record.Type is not (JournalRecordType.ThreadDone or JournalRecordType.AttachmentsRepaired))
                    continue;

                foreach (var file in record.Files.Where(f => CorpusPlan.IsSafeEmlPath(f) && seen.Add(f)))
                    files.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            // Partial list: the caller sweeps what was readable; planned paths cover the rest
            return false;
        }

        return true;
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

    private static bool TooLarge(string path) => new FileInfo(path).Length > Cap;

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

/// <summary>
/// An append was refused because the journal reached <see cref="GenerationJournal.StopBytes"/> (IN-06). An IOException,
/// so every append caller treats it as a journal-save failure; the runner turns it into a fatal stop.
/// </summary>
public class JournalFullException : IOException
{
    public JournalFullException(string message) : base(message)
    {
    }
}
