using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>A damaged key file copied aside before it was rewritten (D-10).</summary>
public class AnswerKeyBackup
{
    public string FileName { get; set; } = string.Empty;
    public string BackupFileName { get; set; } = string.Empty;
}

/// <summary>Outcome of one funnel write: files written, the first failure, backups and the relabels applied.</summary>
public class AnswerKeyWriteResult
{
    public List<WrittenCorpusFile> Files { get; set; } = new();
    public string FailedFileName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool FileOpenElsewhere { get; set; }
    public List<AnswerKeyBackup> Backups { get; set; } = new();
    public int Relabels { get; set; }
    public bool Succeeded => FailedFileName.Length == 0;
}

/// <summary>
/// The one answer-key writer (D-02): harvests user relabels from the key files on disk, keeps them in
/// answer-key-relabels.json, re-applies them to the plan projection and writes answer-key.csv and answer-key.xlsx.
/// AnswerKeyBuilder stays a pure plan projection (KEY-03, D-11).
/// </summary>
public static class AnswerKeyFiles
{
    private const int MaxBackupAttempts = 99;

    private static readonly string[] KeyFiles = { AnswerKeyCsvWriter.FileName, AnswerKeyXlsxWriter.FileName };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Harvests relabels, writes answer-key.csv then answer-key.xlsx with them applied, and saves the sidecar. Each file
    /// is written on its own unless <paramref name="stopAtFirstFailure"/>; a file that could not be read is never
    /// overwritten, and a file that could not be parsed is backed up first. Never throws for I/O failures.
    /// </summary>
    public static async Task<AnswerKeyWriteResult> WriteAsync(string caseFolder, CorpusPlan plan, CaseGuide guide,
        AnswerKeyOverlay? overlay, FileWriter? writer = null, bool stopAtFirstFailure = false, CancellationToken ct = default)
    {
        writer ??= CaseStore.WriteAtomicAsync;
        var result = new AnswerKeyWriteResult();

        // One harvest-and-write at a time per case: runner checkpoints, Regenerate Thread, reopen and approval may overlap (Pitfall 3). Never nested.
        var gate = Gates.GetOrAdd(GateKey(caseFolder), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = new AnswerKeyRelabelStore(caseFolder);
            var harvest = Harvest(caseFolder, store, plan, guide);
            var relabels = harvest.Relabels;
            var set = harvest.Set;

            var (csv, xlsx) = BuildBytes(plan, guide, overlay, relabels);
            result.Relabels = relabels.Count;

            var files = new List<(CorpusFileKind Kind, string FileName, byte[] Bytes)>
            {
                (CorpusFileKind.AnswerKeyCsv, AnswerKeyCsvWriter.FileName, csv),
                (CorpusFileKind.AnswerKeyXlsx, AnswerKeyXlsxWriter.FileName, xlsx)
            };

            foreach (var (kind, fileName, bytes) in files)
            {
                ct.ThrowIfCancellationRequested();
                // Paths = case folder + constant names only (T-04-68)
                var path = Path.Combine(caseFolder, fileName);
                var state = harvest.Files[fileName];

                if (state.Read.ReadError != null)
                {
                    // Pitfall 7: unreadable now means unsaved edits may be in it; never overwrite what we could not harvest
                    RecordFailure(result, fileName, path, state.Read.ReadError);
                    if (stopAtFirstFailure)
                        break;
                    continue;
                }

                if (state.Damaged)
                {
                    var backup = await BackupAsync(caseFolder, fileName, path, state.Read, writer, ct).ConfigureAwait(false);
                    if (backup.Error != null)
                    {
                        // Never overwrite a file we could neither parse nor back up
                        RecordFailure(result, fileName, path, backup.Error);
                        if (stopAtFirstFailure)
                            break;
                        continue;
                    }

                    result.Backups.Add(new AnswerKeyBackup { FileName = fileName, BackupFileName = backup.Name });
                }

                try
                {
                    await writer(path, bytes, ct).ConfigureAwait(false);
                    result.Files.Add(Stamp(kind, fileName, plan.PlanId));
                    set.Files[fileName] = new AnswerKeyFileSnapshot
                    {
                        PlanId = plan.PlanId,
                        Sha256 = Hash(bytes),
                        WrittenUtc = DateTime.UtcNow,
                        Applied = relabels.ToDictionary(p => p.Key, p => Copy(p.Value), StringComparer.Ordinal)
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Carry on with the next file: a locked CSV must not block the XLSX (Pitfall 8); the old snapshot stays the baseline (D-20)
                    RecordFailure(result, fileName, path, ex);
                    if (stopAtFirstFailure)
                        break;
                }
            }

            // Only this plan's relabels and snapshots are kept (D-04, D-08)
            set.Relabels = relabels.Values.OrderBy(r => r.DocId, StringComparer.Ordinal).ToList();
            foreach (var name in set.Files.Where(f => f.Value == null || f.Value.PlanId != plan.PlanId).Select(f => f.Key).ToList())
                set.Files.Remove(name);

            try
            {
                store.Save(set);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The key files already hold the relabels; the next write harvests them again against the old snapshots
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The relabels the next write would keep, without writing anything (no key files, no backup, no sidecar). Never
    /// throws for I/O failures: falls back to the sidecar's relabels for this plan. Call off the UI thread.
    /// </summary>
    public static Dictionary<string, AnswerKeyRelabel> PeekRelabels(string caseFolder, CorpusPlan plan, CaseGuide guide)
    {
        var store = new AnswerKeyRelabelStore(caseFolder);
        try
        {
            return Harvest(caseFolder, store, plan, guide).Relabels;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A dry harvest only feeds warnings: the stored relabels are the best answer we have
            var planLabels = PlanLabels(AnswerKeyBuilder.Build(plan, guide));
            return CurrentRelabels(store.Load(), plan.PlanId, planLabels);
        }
    }

    /// <summary>
    /// CSV and XLSX bytes of the plan projection with the overlay and the relabels applied.
    /// </summary>
    public static (byte[] Csv, byte[] Xlsx) BuildBytes(CorpusPlan plan, CaseGuide guide, AnswerKeyOverlay? overlay,
        IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels)
    {
        // CSV fields go through AnswerKeyCsvWriter.Field, which neutralizes formula prefixes, user Rationale included (T-05-32)
        var rows = Apply(AnswerKeyBuilder.Build(plan, guide, overlay), relabels);
        var csv = AnswerKeyCsvWriter.Build(rows);
        var xlsx = AnswerKeyXlsxWriter.Build(rows, guide);
        return (csv, xlsx);
    }

    /// <summary>
    /// Sets Relevance (and a custom Rationale when there is one) on the rows whose DocID was relabeled. No family
    /// propagation (D-05). Returns the same list.
    /// </summary>
    public static List<AnswerKeyRow> Apply(List<AnswerKeyRow> rows, IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels)
    {
        if (relabels == null || relabels.Count == 0)
            return rows;

        foreach (var row in rows)
        {
            if (row == null || !relabels.TryGetValue(row.DocId, out var relabel) || relabel == null)
                continue;
            row.Relevance = relabel.Relevance;
            if (relabel.Rationale.Length > 0)
                row.Rationale = relabel.Rationale;
        }

        return rows;
    }

    // ---- Harvest (D-03, D-20, Pattern 1) ----

    private sealed class FileState
    {
        public AnswerKeyFileRead Read { get; set; } = new();
        public bool Damaged { get; set; }
    }

    private sealed class HarvestResult
    {
        public AnswerKeyRelabelSet Set { get; set; } = new();
        public Dictionary<string, AnswerKeyRelabel> Relabels { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, FileState> Files { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class Vote
    {
        public string DocId { get; set; } = string.Empty;
        public string Relevance { get; set; } = string.Empty;
        public string Custom { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTime LastWriteUtc { get; set; }
    }

    private sealed class PlanLabelSet
    {
        public Dictionary<string, string> Label { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Thread { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Rationale { get; } = new(StringComparer.Ordinal);
    }

    private static HarvestResult Harvest(string caseFolder, AnswerKeyRelabelStore store, CorpusPlan plan, CaseGuide guide)
    {
        // Base projection without the overlay: its rationale is the one before any run note (Pitfall 6)
        var projection = PlanLabels(AnswerKeyBuilder.Build(plan, guide));
        var set = store.Load();
        var current = CurrentRelabels(set, plan.PlanId, projection);
        var harvest = new HarvestResult { Set = set };

        var votes = new List<Vote>();
        foreach (var fileName in KeyFiles)
        {
            // Paths = case folder + constant names only (T-04-68)
            var read = AnswerKeyReader.ReadShared(Path.Combine(caseFolder, fileName));
            var state = new FileState { Read = read };
            harvest.Files[fileName] = state;

            if (!read.Exists || read.ReadError != null)
                continue;
            if (read.TooLarge)
            {
                state.Damaged = true;
                continue;
            }

            set.Files.TryGetValue(fileName, out var snapshot);
            if (snapshot != null && string.Equals(snapshot.Sha256, Hash(read.Bytes), StringComparison.OrdinalIgnoreCase))
                continue; // Our own write, untouched (Pitfall 1)

            var parsed = fileName == AnswerKeyCsvWriter.FileName
                ? AnswerKeyReader.ParseCsv(read.Bytes)
                : AnswerKeyReader.ParseXlsx(read.Bytes);
            if (!parsed.Parsed)
            {
                state.Damaged = true;
                continue;
            }

            var owner = snapshot != null ? snapshot.PlanId : LegacyOwner(parsed.Rows, plan.PlanId, projection);
            if (owner != plan.PlanId)
                continue; // Another plan's key file (D-04, D-08, Pitfall 4)

            votes.AddRange(VotesOf(parsed.Rows, fileName, read.LastWriteUtc, snapshot, current, projection));
        }

        foreach (var vote in Resolve(votes))
        {
            if (vote.Relevance == projection.Label[vote.DocId])
            {
                // Set back to the plan label: no longer a relabel (D-03)
                current.Remove(vote.DocId);
                continue;
            }

            current[vote.DocId] = new AnswerKeyRelabel
            {
                PlanId = plan.PlanId,
                DocId = vote.DocId,
                Relevance = vote.Relevance,
                Rationale = vote.Custom,
                CapturedUtc = DateTime.UtcNow,
                Source = vote.Source
            };
        }

        harvest.Relabels = current;
        return harvest;
    }

    private static PlanLabelSet PlanLabels(List<AnswerKeyRow> rows)
    {
        var labels = new PlanLabelSet();
        foreach (var row in rows)
        {
            if (row == null || string.IsNullOrEmpty(row.DocId) || labels.Label.ContainsKey(row.DocId))
                continue;
            labels.Label[row.DocId] = row.Relevance;
            labels.Thread[row.DocId] = row.ThreadId;
            labels.Rationale[row.DocId] = row.Rationale;
        }

        return labels;
    }

    // Stored relabels of this plan whose DocID still exists (D-04, D-09); the last entry wins on duplicates
    private static Dictionary<string, AnswerKeyRelabel> CurrentRelabels(AnswerKeyRelabelSet set, Guid planId,
        PlanLabelSet projection)
    {
        var current = new Dictionary<string, AnswerKeyRelabel>(StringComparer.Ordinal);
        foreach (var relabel in set.Relabels)
        {
            if (relabel == null || relabel.PlanId != planId || !projection.Label.ContainsKey(relabel.DocId))
                continue;
            var relevance = AnswerKeyReader.NormalizeRelevance(relabel.Relevance);
            if (relevance == null || relevance == projection.Label[relabel.DocId])
                continue;
            relabel.Relevance = relevance;
            relabel.Rationale ??= string.Empty;
            current[relabel.DocId] = relabel;
        }

        return current;
    }

    // A file without a snapshot (written before Phase 10, or the sidecar was deleted) belongs to this plan only when it
    // holds exactly the plan's DocIDs, each in its planned thread (A3)
    private static Guid LegacyOwner(List<AnswerKeyReadRow> rows, Guid planId, PlanLabelSet projection)
    {
        var docIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!projection.Thread.TryGetValue(row.DocId, out var threadId)
                || !string.Equals(row.ThreadId.Trim(), threadId, StringComparison.Ordinal))
                return Guid.Empty;
            docIds.Add(row.DocId);
        }

        return docIds.Count == projection.Label.Count ? planId : Guid.Empty;
    }

    // A row votes only when it differs from what this file held after our last write of it (D-20)
    private static List<Vote> VotesOf(List<AnswerKeyReadRow> rows, string fileName, DateTime lastWriteUtc,
        AnswerKeyFileSnapshot? snapshot, Dictionary<string, AnswerKeyRelabel> current, PlanLabelSet projection)
    {
        var votes = new List<Vote>();
        foreach (var group in rows.GroupBy(r => r.DocId, StringComparer.Ordinal))
        {
            if (!projection.Label.TryGetValue(group.Key, out var planLabel))
                continue; // Not in the plan, or added by hand (D-09)

            // Duplicate rows that disagree say nothing about this DocID (D-09 spirit)
            if (group.Select(r => AnswerKeyReader.NormalizeRelevance(r.Relevance)).Distinct().Count() > 1)
                continue;

            var row = group.First();
            var value = AnswerKeyReader.NormalizeRelevance(row.Relevance);
            if (value == null)
                continue; // Only "Relevant" and "Not Relevant" count (D-05)

            AnswerKeyRelabel? applied = null;
            snapshot?.Applied.TryGetValue(group.Key, out applied);
            var baseValue = applied?.Relevance ?? planLabel;
            var custom = CustomRationale(row.Rationale, projection.Rationale[group.Key]);

            if (value != baseValue)
            {
                votes.Add(new Vote { DocId = group.Key, Relevance = value, Custom = custom, Source = fileName, LastWriteUtc = lastWriteUtc });
            }
            else if (current.TryGetValue(group.Key, out var relabel) && custom.Length > 0
                     && custom != Normalize(applied?.Rationale ?? relabel.Rationale))
            {
                // A Rationale-only edit on an already relabeled row (D-23); on other rows it is ignored (D-05)
                votes.Add(new Vote { DocId = group.Key, Relevance = relabel.Relevance, Custom = custom, Source = fileName, LastWriteUtc = lastWriteUtc });
            }
        }

        return votes;
    }

    // One vote per DocID
    private static IEnumerable<Vote> Resolve(List<Vote> votes)
    {
        foreach (var group in votes.GroupBy(v => v.DocId, StringComparer.Ordinal))
        {
            var winner = group.First();
            foreach (var vote in group.Skip(1))
            {
                if (vote.Relevance == winner.Relevance && vote.Custom == winner.Custom)
                    continue;
                // D-03/D-20: only conflicting edits use the file time; a single file's edit always counts
                if (vote.LastWriteUtc > winner.LastWriteUtc)
                    winner = vote;
            }

            yield return winner;
        }
    }

    // The user's Rationale when it is not the projected one (D-05, D-23). XLSX stores CRLF as LF, so line endings are
    // normalized before comparing (10-01); a run note appended to the projection keeps its prefix (Pitfall 6)
    private static string CustomRationale(string? cell, string projected)
    {
        var text = Normalize(AnswerKeyReader.StripCsvGuard(cell)).Trim();
        var baseText = Normalize(projected).Trim();
        if (text.Length == 0 || (baseText.Length > 0 && text.StartsWith(baseText, StringComparison.Ordinal)))
            return string.Empty;
        return text.Length > AnswerKeyXlsxWriter.MaxCellLength ? text[..AnswerKeyXlsxWriter.MaxCellLength] : text;
    }

    private static string Normalize(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    // ---- Writing ----

    private static async Task<(string Name, Exception? Error)> BackupAsync(string caseFolder, string fileName, string path,
        AnswerKeyFileRead read, FileWriter writer, CancellationToken ct)
    {
        // Constant stem + invariant timestamp + counter, never a name from file content (T-10-11)
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var attempt = 1; attempt <= MaxBackupAttempts; attempt++)
        {
            var suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
            var name = $"{stem} (unreadable {stamp}{suffix}){extension}";
            var backupPath = Path.Combine(caseFolder, name);
            if (File.Exists(backupPath))
                continue;

            try
            {
                if (read.TooLarge)
                    File.Copy(path, backupPath, overwrite: false);
                else
                    await writer(backupPath, read.Bytes, ct).ConfigureAwait(false);
                return (name, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (name, ex);
            }
        }

        return (string.Empty, new IOException($"No free backup name for {fileName}."));
    }

    private static string GateKey(string caseFolder) =>
        Path.GetFullPath(Path.TrimEndingDirectorySeparator(caseFolder.Trim()));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AnswerKeyRelabel Copy(AnswerKeyRelabel relabel) => new()
    {
        PlanId = relabel.PlanId,
        DocId = relabel.DocId,
        Relevance = relabel.Relevance,
        Rationale = relabel.Rationale,
        CapturedUtc = relabel.CapturedUtc,
        Source = relabel.Source
    };

    private static WrittenCorpusFile Stamp(CorpusFileKind kind, string fileName, Guid planId) => new()
    {
        Kind = kind,
        FileName = fileName,
        PlanId = planId,
        WrittenUtc = DateTime.UtcNow
    };

    // The first failure is the one reported; later failures only keep their file out of result.Files
    private static void RecordFailure(AnswerKeyWriteResult result, string fileName, string path, Exception ex)
    {
        if (result.FailedFileName.Length > 0)
            return;

        result.FailedFileName = fileName;
        result.Error = ex.Message;
        result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
    }
}
