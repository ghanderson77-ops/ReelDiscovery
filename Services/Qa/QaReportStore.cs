using System.Text;
using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Qa;

namespace ReelDiscovery.Services.Qa;

/// <summary>
/// Reads and writes the QA reports in the case folder: the scan and leakage reports (atomic whole-file writes) and the
/// blind-review log (append-only header + verdict lines). Paths are the case folder plus a constant name only.
/// Reads never throw: a missing or damaged report reads as null (Not run).
/// </summary>
public class QaReportStore
{
    public const string ScansFileName = "qa-scans.json";
    public const string LeakageFileName = "qa-leakage.json";
    public const string BlindReviewFileName = "qa-blind-review.jsonl";

    private const string HeaderKind = "Header";
    private const string VerdictKind = "Verdict";
    private const int MaxMoveAttempts = 5;

    private static readonly JsonSerializerOptions LineOptions = new(CaseDraft.JsonOptions) { WriteIndented = false };

    private readonly string _caseFolder;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QaReportStore(string caseFolder)
    {
        _caseFolder = caseFolder ?? string.Empty;
    }

    // Case folder + constant name only, never a path from data (T-07-40)
    public string ScansPath => Path.Combine(_caseFolder, ScansFileName);
    public string LeakagePath => Path.Combine(_caseFolder, LeakageFileName);
    public string BlindReviewPath => Path.Combine(_caseFolder, BlindReviewFileName);

    public void SaveScans(ScanReport report) => WriteJson(ScansPath, report);

    public ScanReport? LoadScans() => ReadJson<ScanReport>(ScansPath);

    public void SaveLeakage(LeakageReport report) => WriteJson(LeakagePath, report);

    public LeakageReport? LoadLeakage() => ReadJson<LeakageReport>(LeakagePath);

    /// <summary>Starts a new blind-review pass; verdicts appended before it no longer count.</summary>
    public Task AppendBlindReviewHeaderAsync(BlindReviewHeader header, CancellationToken ct = default) =>
        AppendLineAsync(new BlindReviewLine { Kind = HeaderKind, Header = header }, ct);

    public Task AppendBlindReviewVerdictAsync(BlindReviewVerdict verdict, CancellationToken ct = default) =>
        AppendLineAsync(new BlindReviewLine { Kind = VerdictKind, Verdict = verdict }, ct);

    /// <summary>
    /// The latest header and the verdicts after it (last line per DocId wins, first-seen order). Torn or unreadable
    /// lines are skipped; a missing or unreadable file gives (null, empty).
    /// </summary>
    public (BlindReviewHeader? Header, List<BlindReviewVerdict> Verdicts) LoadBlindReview()
    {
        BlindReviewHeader? header = null;
        var verdicts = new List<BlindReviewVerdict>();
        var indexByDoc = new Dictionary<string, int>(StringComparer.Ordinal);

        string[] lines;
        try
        {
            if (!File.Exists(BlindReviewPath))
                return (null, verdicts);
            lines = File.ReadAllLines(BlindReviewPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            // A report is a convenience view; a missing or damaged one shows Not run
            return (null, verdicts);
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            BlindReviewLine? record;
            try
            {
                record = JsonSerializer.Deserialize<BlindReviewLine>(line, LineOptions);
            }
            catch (JsonException)
            {
                // A kill can leave a torn last line; skip it like journal replay does
                continue;
            }

            if (record == null)
                continue;

            if (record.Kind == HeaderKind && record.Header != null)
            {
                header = record.Header;
                verdicts.Clear();
                indexByDoc.Clear();
            }
            else if (record.Kind == VerdictKind && record.Verdict != null && header != null)
            {
                var docId = record.Verdict.DocId ?? string.Empty;
                if (indexByDoc.TryGetValue(docId, out var index))
                {
                    verdicts[index] = record.Verdict;
                }
                else
                {
                    indexByDoc[docId] = verdicts.Count;
                    verdicts.Add(record.Verdict);
                }
            }
        }

        return (header, verdicts);
    }

    private async Task AppendLineAsync(BlindReviewLine record, CancellationToken ct)
    {
        // Serialized before taking the lock so the whole line is ready for one write
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, LineOptions) + "\n");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(BlindReviewPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.Read);

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

    private static void WriteJson<T>(string path, T report)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, CaseDraft.JsonOptions);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                       FileOptions.WriteThrough))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
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
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception)
        {
            TryDeleteTemp(tmp);
            throw;
        }
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), CaseDraft.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException)
        {
            // A report is a convenience view; a missing or damaged one shows Not run
            return null;
        }
    }

    private static void TryDeleteTemp(string tmp)
    {
        try
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless; the original write failure is what the caller reports
        }
    }

    private sealed class BlindReviewLine
    {
        public string Kind { get; set; } = string.Empty;
        public BlindReviewHeader? Header { get; set; }
        public BlindReviewVerdict? Verdict { get; set; }
    }
}
