using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

public enum CaseLoadError
{
    None,
    Missing,
    TooLarge,
    NewerVersion,
    Corrupt
}

public class CaseLoadResult
{
    public CaseDraft? Draft { get; set; }
    public CaseLoadError Error { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Succeeded => Draft != null;
}

public class RecentCase
{
    /// <summary>Full path of the case.json file.</summary>
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CaseStage Stage { get; set; }
    public DateTime LastOpenedUtc { get; set; }
}

/// <summary>
/// Per-case folder persistence (D-09/D-10/D-11): atomic, OneDrive-safe autosave of case.json,
/// tolerant loading, the recent-cases list and folder moves. Holds no secrets.
/// </summary>
public class CaseStore
{
    public const string CaseFileName = "case.json";
    public const long MaxCaseFileBytes = 50L * 1024 * 1024;
    public const int MaxRecentCases = 8;

    private const string RecentFileName = "recent.json";
    private const int MaxMoveAttempts = 5;

    private readonly string _appDataFolder;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _pendingLock = new();
    private PendingWrite? _pending;
    private long _nextSequence;
    private long _writtenSequence;
    private WriteOutcome _lastOutcome = new(true, string.Empty);

    public CaseStore(string? casesRoot = null, string? appDataFolder = null)
    {
        // No I/O here: WizardState creates a store on construction
        CasesRoot = casesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ReelDiscovery", "Cases");
        _appDataFolder = appDataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReelDiscovery");
    }

    public string CasesRoot { get; }

    public DateTime? LastSavedUtc { get; private set; }

    /// <summary>Reason the most recent save failed; empty after a successful save.</summary>
    public string LastSaveError { get; private set; } = string.Empty;

    /// <summary>Raised after every save attempt, successful or not.</summary>
    public event EventHandler? SaveCompleted;

    private string RecentFilePath => Path.Combine(_appDataFolder, RecentFileName);

    /// <summary>A case gets a folder (and is saved) only once its core document exists (D-09).</summary>
    public static bool CanSave(CaseDraft? draft) => draft?.CoreDocument != null;

    /// <summary>
    /// Returns the case folder, creating a unique one under CasesRoot on first use and setting draft.FolderPath.
    /// </summary>
    public string EnsureFolder(CaseDraft draft)
    {
        if (!string.IsNullOrEmpty(draft.FolderPath) && Directory.Exists(draft.FolderPath))
            return draft.FolderPath;

        var folder = CaseFolderNaming.UniqueFolderPath(CasesRoot, CaseFolderNaming.DefaultFolderName(draft));
        Directory.CreateDirectory(folder);
        draft.FolderPath = folder;
        return folder;
    }

    public async Task<bool> SaveAsync(CaseDraft draft, CancellationToken ct = default)
    {
        if (!CanSave(draft))
            return false;

        try
        {
            var snapshot = TakeSnapshot(draft);
            lock (_pendingLock)
            {
                // Latest snapshot wins
                if (_pending == null || _pending.Sequence < snapshot.Sequence)
                    _pending = snapshot;
            }

            // The whole drain (lock, write, release) runs on the pool so a UI-thread SaveNow can never deadlock it
            var outcome = await Task.Run(() => DrainAsync(ct), ct);
            return Complete(draft, outcome);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Saving is core work: report the failure (UI shows 'Not saved: ...'), never swallow it; the draft stays in memory and saves again on the next change
            return Complete(draft, new WriteOutcome(false, ex.Message));
        }
        finally
        {
            SaveCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Synchronous flush used when the app closes; supersedes any queued background snapshot.
    /// </summary>
    public bool SaveNow(CaseDraft draft)
    {
        if (!CanSave(draft))
            return false;

        try
        {
            var snapshot = TakeSnapshot(draft);
            WriteOutcome outcome;

            // Same writer lock as SaveAsync: a background write must never race the close-time write
            _writeLock.Wait();
            try
            {
                lock (_pendingLock)
                {
                    // The fresh snapshot supersedes anything queued
                    if (_pending != null && _pending.Sequence <= snapshot.Sequence)
                        _pending = null;
                }

                try
                {
                    WriteAtomic(snapshot.Path, snapshot.Bytes);
                    outcome = new WriteOutcome(true, string.Empty);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Saving is core work: report the failure instead of swallowing it
                    outcome = new WriteOutcome(false, ex.Message);
                }

                _lastOutcome = outcome;
                _writtenSequence = Math.Max(_writtenSequence, snapshot.Sequence);
            }
            finally
            {
                _writeLock.Release();
            }

            return Complete(draft, outcome);
        }
        catch (Exception ex)
        {
            // Saving is core work: report the failure (folder creation etc.), never swallow it
            return Complete(draft, new WriteOutcome(false, ex.Message));
        }
        finally
        {
            SaveCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Loads a case.json; refuses oversized, newer-version and corrupt files with a user-facing message. Never throws.
    /// </summary>
    public CaseLoadResult Load(string caseJsonPath)
    {
        var fileName = SafeFileName(caseJsonPath);
        try
        {
            var fullPath = Path.GetFullPath(caseJsonPath);
            if (!File.Exists(fullPath))
            {
                RemoveRecent(fullPath);
                return Fail(CaseLoadError.Missing,
                    $"This case folder no longer exists: {caseJsonPath}. It was removed from Recent cases.");
            }

            // Size cap before reading anything (T-03-12)
            if (new FileInfo(fullPath).Length > MaxCaseFileBytes)
                return Fail(CaseLoadError.TooLarge,
                    $"Could not open {fileName}: the file is larger than 50 MB. Choose a different case.json with Open Case...");

            var json = File.ReadAllText(fullPath);
            if (PeekFormatVersion(json) > CaseDraft.CurrentFormatVersion)
                return Fail(CaseLoadError.NewerVersion,
                    "This case was saved by a newer version of ReelDiscovery. Update the app to open it.");

            var draft = CaseDraft.FromJson(json);
            draft.Normalize();
            draft.FolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
            TouchRecent(draft);
            return new CaseLoadResult { Draft = draft, Error = CaseLoadError.None };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var reason = ex.Message.Trim().TrimEnd('.');
            return Fail(CaseLoadError.Corrupt,
                $"Could not open {fileName}: {reason}. Choose a different case.json with Open Case...");
        }
    }

    public List<RecentCase> GetRecentCases() =>
        ReadRecent()
            .Where(r => FileExistsSafe(r.Path))
            .OrderByDescending(r => r.LastOpenedUtc)
            .Take(MaxRecentCases)
            .ToList();

    /// <summary>Upserts this case at the top of the recent list (max 8).</summary>
    public void TouchRecent(CaseDraft draft)
    {
        if (string.IsNullOrEmpty(draft.FolderPath))
            return;

        var path = Path.Combine(draft.FolderPath, CaseFileName);
        var entries = ReadRecent();
        entries.RemoveAll(r => SamePath(r.Path, path));
        entries.Insert(0, new RecentCase
        {
            Path = path,
            Title = CaseFolderNaming.CaseTitle(draft),
            Stage = draft.Stage,
            LastOpenedUtc = DateTime.UtcNow
        });
        WriteRecent(entries);
    }

    public void RemoveRecent(string caseJsonPath)
    {
        var entries = ReadRecent();
        if (entries.RemoveAll(r => SamePath(r.Path, caseJsonPath)) > 0)
            WriteRecent(entries);
    }

    /// <summary>
    /// Moves the case folder under targetParent (D-09); returns the new folder. Throws IOException /
    /// UnauthorizedAccessException with the OS reason so the UI can show it.
    /// </summary>
    public string MoveFolder(CaseDraft draft, string targetParent)
    {
        if (string.IsNullOrEmpty(draft.FolderPath) || !Directory.Exists(draft.FolderPath))
            throw new DirectoryNotFoundException("The case has not been saved to a folder yet.");

        // Hold the writer lock so no background save writes into the old folder mid-move
        _writeLock.Wait();
        try
        {
            var source = Path.GetFullPath(draft.FolderPath);
            var oldCaseFile = Path.Combine(source, CaseFileName);
            Directory.CreateDirectory(targetParent);
            var target = CaseFolderNaming.UniqueFolderPath(targetParent, Path.GetFileName(source));

            if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, target);
            }
            else
            {
                CopyDirectory(source, target);
                Directory.Delete(source, recursive: true);
            }

            draft.FolderPath = target;
            lock (_pendingLock)
            {
                // A queued snapshot still points at the old folder; the next save re-snapshots
                _pending = null;
            }

            RemoveRecent(oldCaseFile);
            TouchRecent(draft);
            return target;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // OneDrive-safe: temp file in the same folder, then same-volume rename with overwrite, retried on transient locks
    public static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        var tmp = TempPathFor(path);
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
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
                    await Task.Delay(100 * attempt, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            // Leave no temp files behind; the caller reports "Not saved"
            TryDelete(tmp);
            throw;
        }
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        var tmp = TempPathFor(path);
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
                    Thread.Sleep(100 * attempt);
                }
            }
        }
        catch (Exception)
        {
            // Leave no temp files behind; the caller reports "Not saved"
            TryDelete(tmp);
            throw;
        }
    }

    private PendingWrite TakeSnapshot(CaseDraft draft)
    {
        var folder = EnsureFolder(draft);
        draft.UpdatedUtc = DateTime.UtcNow;
        // Serialize on the caller (UI) thread: generators mutate the draft on the UI context, so this is a consistent snapshot
        var bytes = Encoding.UTF8.GetBytes(draft.ToJson());
        return new PendingWrite(Path.Combine(folder, CaseFileName), bytes, Interlocked.Increment(ref _nextSequence));
    }

    private async Task<WriteOutcome> DrainAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            PendingWrite? write;
            lock (_pendingLock)
            {
                write = _pending;
                _pending = null;
            }

            // Already written by a newer snapshot (coalesced) or superseded by SaveNow
            if (write == null || write.Sequence <= _writtenSequence)
                return _lastOutcome;

            WriteOutcome outcome;
            try
            {
                await WriteAtomicAsync(write.Path, write.Bytes, ct).ConfigureAwait(false);
                outcome = new WriteOutcome(true, string.Empty);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Saving is core work: carry the reason back to the caller for LastSaveError
                outcome = new WriteOutcome(false, ex.Message);
            }

            _lastOutcome = outcome;
            _writtenSequence = write.Sequence;
            return outcome;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private bool Complete(CaseDraft draft, WriteOutcome outcome)
    {
        if (!outcome.Succeeded)
        {
            LastSaveError = string.IsNullOrWhiteSpace(outcome.Error) ? "The case could not be saved." : outcome.Error;
            return false;
        }

        LastSavedUtc = DateTime.UtcNow;
        LastSaveError = string.Empty;
        TouchRecent(draft);
        return true;
    }

    private static int PeekFormatVersion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return 1;

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, nameof(CaseDraft.FormatVersion), StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var version))
            {
                return version;
            }
        }

        // Missing FormatVersion means version 1
        return 1;
    }

    private List<RecentCase> ReadRecent()
    {
        try
        {
            if (!File.Exists(RecentFilePath))
                return new List<RecentCase>();

            var dto = JsonSerializer.Deserialize<RecentFileDto>(File.ReadAllText(RecentFilePath));
            return (dto?.Cases ?? new List<RecentCaseDto>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Path))
                .Select(c => new RecentCase
                {
                    Path = c.Path!,
                    Title = c.Title ?? string.Empty,
                    Stage = Enum.TryParse<CaseStage>(c.Stage, out var stage) ? stage : CaseStage.CoreDocument,
                    LastOpenedUtc = c.LastOpenedUtc
                })
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt recent list: start over; it only lists shortcuts
            return new List<RecentCase>();
        }
    }

    private void WriteRecent(List<RecentCase> entries)
    {
        try
        {
            var dto = new RecentFileDto
            {
                Cases = entries
                    .Where(r => FileExistsSafe(r.Path))
                    .OrderByDescending(r => r.LastOpenedUtc)
                    .Take(MaxRecentCases)
                    .Select(r => new RecentCaseDto
                    {
                        Path = r.Path,
                        Title = r.Title,
                        Stage = r.Stage.ToString(),
                        LastOpenedUtc = r.LastOpenedUtc
                    })
                    .ToList()
            };

            Directory.CreateDirectory(_appDataFolder);
            WriteAtomic(RecentFilePath, JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-critical: the recent list is only a shortcut; the case itself was saved
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static string TempPathFor(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
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
            // Best effort: a leftover temp file is harmless and the original error is what matters
        }
    }

    private static bool FileExistsSafe(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Malformed entry from a hand-edited recent.json: compare literally
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string SafeFileName(string path)
    {
        var name = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? CaseFileName : name;
    }

    private static CaseLoadResult Fail(CaseLoadError error, string message) =>
        new() { Error = error, Message = message };

    private sealed class PendingWrite
    {
        public PendingWrite(string path, byte[] bytes, long sequence)
        {
            Path = path;
            Bytes = bytes;
            Sequence = sequence;
        }

        public string Path { get; }
        public byte[] Bytes { get; }
        public long Sequence { get; }
    }

    private sealed class WriteOutcome
    {
        public WriteOutcome(bool succeeded, string error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        public bool Succeeded { get; }
        public string Error { get; }
    }

    private class RecentFileDto
    {
        [JsonPropertyName("cases")]
        public List<RecentCaseDto>? Cases { get; set; }
    }

    private class RecentCaseDto
    {
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("stage")]
        public string? Stage { get; set; }

        [JsonPropertyName("lastOpenedUtc")]
        public DateTime LastOpenedUtc { get; set; }
    }
}
