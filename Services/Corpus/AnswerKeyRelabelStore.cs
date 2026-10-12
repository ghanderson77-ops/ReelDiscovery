using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Reads and writes answer-key-relabels.json in the case folder: the user's answer-key relabels and the per-file
/// snapshots of what the app last wrote (D-01, D-20). Path is the case folder plus a constant name only. Reads never
/// throw: a missing, damaged or oversized sidecar reads as an empty set. Writes are atomic.
/// </summary>
public class AnswerKeyRelabelStore
{
    public const string FileName = "answer-key-relabels.json";
    public const long MaxFileBytes = 64L * 1024 * 1024;

    private const int MaxMoveAttempts = 5;

    private readonly string _caseFolder;

    public AnswerKeyRelabelStore(string caseFolder)
    {
        _caseFolder = caseFolder ?? string.Empty;
    }

    // Case folder + constant name only, never a path from data (T-04-68, T-07-40)
    public string FilePath => Path.Combine(_caseFolder, FileName);

    public AnswerKeyRelabelSet Load()
    {
        var path = FilePath;
        AnswerKeyRelabelSet? set;
        try
        {
            if (!File.Exists(path))
                return new AnswerKeyRelabelSet();
            if (new FileInfo(path).Length > MaxFileBytes)
                return new AnswerKeyRelabelSet();
            set = JsonSerializer.Deserialize<AnswerKeyRelabelSet>(File.ReadAllBytes(path), CaseDraft.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException)
        {
            // A damaged sidecar reads as no relabels; the next write harvests the key files again (D-01)
            return new AnswerKeyRelabelSet();
        }

        return Normalize(set);
    }

    public void Save(AnswerKeyRelabelSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        var path = FilePath;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(set, CaseDraft.JsonOptions);
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

    // Hand-edited or partial JSON can carry nulls; consumers rely on non-null collections and non-empty DocIds
    private static AnswerKeyRelabelSet Normalize(AnswerKeyRelabelSet? set)
    {
        set ??= new AnswerKeyRelabelSet();
        set.Relabels = (set.Relabels ?? new List<AnswerKeyRelabel>())
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.DocId))
            .ToList();

        var files = new Dictionary<string, AnswerKeyFileSnapshot>(StringComparer.Ordinal);
        foreach (var (name, snapshot) in set.Files ?? new Dictionary<string, AnswerKeyFileSnapshot>())
        {
            if (snapshot == null)
                continue;

            var applied = new Dictionary<string, AnswerKeyRelabel>(StringComparer.Ordinal);
            foreach (var (docId, relabel) in snapshot.Applied ?? new Dictionary<string, AnswerKeyRelabel>())
            {
                if (relabel != null && !string.IsNullOrWhiteSpace(docId))
                    applied[docId] = relabel;
            }

            snapshot.Applied = applied;
            snapshot.Sha256 ??= string.Empty;
            files[name] = snapshot;
        }

        set.Files = files;
        return set;
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
}
