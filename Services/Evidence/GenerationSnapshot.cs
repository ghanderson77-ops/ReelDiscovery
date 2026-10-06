using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Thread-safe progress state of a generation run. Workers write only here; the step's 250 ms timer drains it on the
/// UI thread (RUN-07).
/// </summary>
public class GenerationSnapshot
{
    public const int MaxQueuedEvents = 500;

    private readonly object _lock = new();
    private readonly Queue<GenerationEvent> _events = new();
    private GenerationCounts _counts = new();
    private List<WaveProgress> _waves = new();
    private Dictionary<string, AttachmentSeed> _attachments = new(StringComparer.Ordinal);
    private GenerationStage _stage;
    private int _stageWave;
    private int _stageWaveCount;
    private DateOnly _stageWaveStart;
    private DateOnly _stageWaveEnd;
    private int _stageCount;

    // D-09: stage label -> stage row, and every provider batch not yet collected (batch id -> its row and progress)
    private readonly Dictionary<string, int> _batchRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BatchTrack> _batchTracks = new(StringComparer.Ordinal);
    private bool _collectingAfterRestart;
    private int _pendingProviderBatches;

    /// <summary>
    /// Starts a run view: replaces counts and stage rows and clears queued events and the stage.
    /// <paramref name="attachments"/> seeds the replayed status of each attachment DocId so later
    /// <see cref="RecordAttachment"/> calls replace it instead of adding to it; counts and rows must already include the
    /// seeded statuses.
    /// </summary>
    public void Reset(GenerationCounts counts, IEnumerable<WaveProgress> waves,
        IReadOnlyDictionary<string, AttachmentSeed>? attachments = null)
    {
        var countsCopy = (counts ?? new GenerationCounts()).Clone();
        var wavesCopy = (waves ?? Enumerable.Empty<WaveProgress>()).Select(w => w.Clone()).ToList();
        var seedsCopy = new Dictionary<string, AttachmentSeed>(StringComparer.Ordinal);
        if (attachments != null)
        {
            foreach (var (docId, seed) in attachments)
            {
                if (!string.IsNullOrEmpty(docId) && seed != null)
                    seedsCopy[docId] = new AttachmentSeed { RowIndex = seed.RowIndex, Status = seed.Status };
            }
        }

        lock (_lock)
        {
            _counts = countsCopy;
            _waves = wavesCopy;
            _attachments = seedsCopy;
            _events.Clear();
            _stage = GenerationStage.Idle;
            _stageWave = 0;
            _stageWaveCount = 0;
            _stageWaveStart = default;
            _stageWaveEnd = default;
            _stageCount = 0;
            _batchRows.Clear();
            _batchTracks.Clear();
            _collectingAfterRestart = false;
            _pendingProviderBatches = 0;
        }
    }

    public void SetStage(GenerationStage stage, int wave = 0, int waveCount = 0, DateOnly start = default,
        DateOnly end = default, int count = 0)
    {
        lock (_lock)
        {
            _stage = stage;
            _stageWave = wave;
            _stageWaveCount = waveCount;
            _stageWaveStart = start;
            _stageWaveEnd = end;
            _stageCount = count;
        }
    }

    public void SetWaveRunning(int waveIndex, bool running)
    {
        lock (_lock)
        {
            foreach (var wave in _waves.Where(w => w.Index == waveIndex))
                wave.Running = running;
        }
    }

    /// <summary>
    /// Counts one finished thread. Written and Warnings count as done; Failed counts separately; Pending adds only the
    /// spend (a fatal stop left the thread unjournaled). <paramref name="previous"/>
    /// is the thread's status before this run: a retried thread that had failed leaves the Failed count first, so Retry
    /// Failed Threads never counts it twice. <paramref name="relevant"/> picks RelevantDone or NonRelevantDone.
    /// </summary>
    public void RecordThread(int waveIndex, ThreadRunStatus status, int emails, int files, decimal cost,
        ThreadRunStatus previous = ThreadRunStatus.Pending, bool relevant = true)
    {
        lock (_lock)
        {
            var wave = _waves.FirstOrDefault(w => w.Index == waveIndex);
            _counts.CostUsd += cost;

            // Reset seeded Failed from the replay; this run's outcome replaces that earlier failure
            // (failures are not split by relevance, so only the shared counters move)
            if (previous == ThreadRunStatus.Failed)
            {
                _counts.FailedThreads = Math.Max(0, _counts.FailedThreads - 1);
                if (wave != null)
                    wave.Failed = Math.Max(0, wave.Failed - 1);
            }

            switch (status)
            {
                case ThreadRunStatus.Written:
                case ThreadRunStatus.Warnings:
                    _counts.DoneThreads++;
                    if (relevant)
                        _counts.RelevantDone++;
                    else
                        _counts.NonRelevantDone++;
                    _counts.EmailsWritten += emails;
                    _counts.FilesWritten += files;
                    if (wave != null)
                        wave.Done++;
                    if (status == ThreadRunStatus.Warnings)
                    {
                        _counts.WarningThreads++;
                        if (wave != null)
                            wave.Warnings++;
                    }
                    break;
                case ThreadRunStatus.Failed:
                    _counts.FailedThreads++;
                    if (wave != null)
                        wave.Failed++;
                    break;
            }
        }
    }

    /// <summary>
    /// Records the committed outcome of one attachment. Call only after the outcome is durable (EML copies written and
    /// its journal line flushed); the status replaces the DocId's previous status (review MEDIUM), moving it between
    /// counters and from its previous stage row to <paramref name="rowIndex"/>. Written and Warnings count as done.
    /// <paramref name="cost"/> and <paramref name="mediaCost"/> are always added; CostUsd includes the media spend.
    /// </summary>
    public void RecordAttachment(string docId, int rowIndex, AttachmentRunStatus status, decimal cost, decimal mediaCost = 0m)
    {
        docId ??= string.Empty;
        lock (_lock)
        {
            _counts.CostUsd += cost + mediaCost;
            _counts.MediaCostUsd += mediaCost;

            if (_attachments.TryGetValue(docId, out var previous))
                Apply(previous.Status, _waves.FirstOrDefault(w => w.Index == previous.RowIndex), -1);

            Apply(status, _waves.FirstOrDefault(w => w.Index == rowIndex), 1);
            _attachments[docId] = new AttachmentSeed { RowIndex = rowIndex, Status = status };
        }
    }

    /// <summary>
    /// Spend of calls whose outcome was not committed: NotRun, cancelled or persistence failures (review concern 1).
    /// Changes only CostUsd (which includes media) and MediaCostUsd.
    /// </summary>
    public void AddCost(decimal cost, decimal mediaCost = 0m)
    {
        lock (_lock)
        {
            _counts.CostUsd += cost + mediaCost;
            _counts.MediaCostUsd += mediaCost;
        }
    }

    // Caller holds _lock. delta is +1 to count a status or -1 to remove it; removals clamp at 0.
    private void Apply(AttachmentRunStatus status, WaveProgress? row, int delta)
    {
        static int Step(int value, int delta) => Math.Max(0, value + delta);

        switch (status)
        {
            case AttachmentRunStatus.Written:
            case AttachmentRunStatus.Warnings:
                _counts.AttachmentsDone = Step(_counts.AttachmentsDone, delta);
                if (row != null)
                    row.AttachmentsDone = Step(row.AttachmentsDone, delta);
                if (status == AttachmentRunStatus.Warnings)
                {
                    _counts.AttachmentsWarnings = Step(_counts.AttachmentsWarnings, delta);
                    if (row != null)
                        row.AttachmentWarnings = Step(row.AttachmentWarnings, delta);
                }
                break;
            case AttachmentRunStatus.Failed:
                _counts.AttachmentsFailed = Step(_counts.AttachmentsFailed, delta);
                if (row != null)
                    row.AttachmentsFailed = Step(row.AttachmentsFailed, delta);
                break;
            case AttachmentRunStatus.Skipped:
                _counts.AttachmentsSkipped = Step(_counts.AttachmentsSkipped, delta);
                if (row != null)
                    row.AttachmentsSkipped = Step(row.AttachmentsSkipped, delta);
                break;
        }
    }

    public void SetLedgerEntries(int count)
    {
        lock (_lock)
            _counts.LedgerEntries = count;
    }

    public void SetRemainingCost(decimal amount)
    {
        lock (_lock)
            _counts.RemainingCostUsd = amount;
    }

    public void SetFinished(bool running, bool finished)
    {
        lock (_lock)
        {
            _counts.Running = running;
            _counts.Finished = finished;
        }
    }

    public void AddEvent(GenerationEvent e)
    {
        if (e == null)
            return;

        lock (_lock)
            Enqueue(e);
    }

    // Caller holds _lock
    private void Enqueue(GenerationEvent e)
    {
        _events.Enqueue(e);

        // The Activity grid keeps 500 rows; the journal keeps everything (UI-SPEC S1)
        while (_events.Count > MaxQueuedEvents)
            _events.Dequeue();
    }

    /// <summary>
    /// Routes provider batches of a stage label ("wave 3", "non-relevant mail") to a stage row (D-09).
    /// </summary>
    public void MapBatchStage(string label, int rowIndex)
    {
        if (string.IsNullOrEmpty(label))
            return;

        lock (_lock)
            _batchRows[label] = rowIndex;
    }

    /// <summary>
    /// Applies one provider batch status change (D-09). Raised on the decorator's poll threads, so everything happens under
    /// the snapshot lock (T-07-33). The stage row aggregates its waiting batches (count, earliest submission, summed
    /// done/total, soonest next check); Submitted and the four terminal kinds add an Activity row, Progress does not.
    /// </summary>
    public void RecordBatchStatus(BatchStatusEvent e)
    {
        if (e == null)
            return;

        lock (_lock)
        {
            var row = _batchRows.TryGetValue(e.StageLabel ?? string.Empty, out var mapped) ? mapped : (int?)null;
            GenerationEventKind? kind = null;
            var touchedRow = row;
            switch (e.Kind)
            {
                case BatchStatusKind.Submitted:
                    _batchTracks[e.BatchId] = new BatchTrack
                    {
                        Row = row,
                        SubmittedUtc = e.SubmittedUtc,
                        Done = e.Done,
                        Total = e.Total,
                        NextCheckUtc = e.NextCheckUtc == default ? null : e.NextCheckUtc
                    };
                    _pendingProviderBatches++;
                    kind = GenerationEventKind.BatchSubmitted;
                    break;
                case BatchStatusKind.Progress:
                    // A batch resumed after a restart never raised Submitted here; its first progress starts the track
                    if (!_batchTracks.TryGetValue(e.BatchId, out var track))
                    {
                        track = new BatchTrack { Row = row, SubmittedUtc = e.SubmittedUtc };
                        _batchTracks[e.BatchId] = track;
                    }

                    track.Done = e.Done;
                    track.Total = e.Total;
                    if (e.NextCheckUtc != default)
                        track.NextCheckUtc = e.NextCheckUtc;
                    touchedRow = track.Row;
                    break;
                default:
                    if (_batchTracks.Remove(e.BatchId, out var ended))
                        touchedRow = ended.Row;
                    _pendingProviderBatches = Math.Max(0, _pendingProviderBatches - 1);

                    // The strip's "Collecting batches..." line ends with the first collected batch
                    _collectingAfterRestart = false;
                    kind = e.Kind switch
                    {
                        BatchStatusKind.Finished => GenerationEventKind.BatchFinished,
                        BatchStatusKind.FinishedWithFailures => GenerationEventKind.BatchFinishedWithFailures,
                        BatchStatusKind.FailedAtProvider => GenerationEventKind.BatchFailedAtProvider,
                        _ => GenerationEventKind.BatchCollectedAfterRestart
                    };
                    break;
            }

            if (touchedRow != null)
                RefreshBatchRow(touchedRow.Value);

            if (kind != null)
            {
                Enqueue(new GenerationEvent
                {
                    Kind = kind.Value,
                    LocalTime = DateTime.Now,
                    Done = e.Done,
                    Total = e.Total,
                    BatchRequests = e.Total,
                    BatchSucceeded = e.Succeeded,
                    BatchFailed = e.Failed,
                    BatchProvider = e.Provider.ToString(),
                    BatchStage = e.StageLabel ?? string.Empty,
                    BatchReason = e.Reason ?? string.Empty,
                    BatchDuration = e.Duration,
                    BatchSubmittedUtc = e.SubmittedUtc == default ? null : e.SubmittedUtc
                });
            }
        }
    }

    /// <summary>
    /// D-09: shows "Collecting batches submitted before the app closed..." until the first batch is collected.
    /// </summary>
    public void SetCollectingAfterRestart(bool value)
    {
        lock (_lock)
            _collectingAfterRestart = value;
    }

    public void SetPendingProviderBatches(int count)
    {
        lock (_lock)
            _pendingProviderBatches = Math.Max(0, count);
    }

    /// <summary>
    /// Copy of the lowest stage row waiting on a provider batch (the status strip's countdown); null when none is.
    /// </summary>
    public WaveProgress? CurrentBatchWait()
    {
        lock (_lock)
            return _waves.Where(w => w.BatchWaiting).OrderBy(w => w.Index).FirstOrDefault()?.Clone();
    }

    // Caller holds _lock. Recomputes the row's batch fields from its waiting batches.
    private void RefreshBatchRow(int rowIndex)
    {
        var row = _waves.FirstOrDefault(w => w.Index == rowIndex);
        if (row == null)
            return;

        var waiting = _batchTracks.Values.Where(t => t.Row == rowIndex).ToList();
        row.BatchWaiting = waiting.Count > 0;
        row.BatchCount = waiting.Count;
        row.BatchFirstSubmittedUtc = waiting.Count > 0 ? waiting.Min(t => t.SubmittedUtc) : null;
        row.BatchDone = waiting.Sum(t => t.Done);
        row.BatchTotal = waiting.Sum(t => t.Total);
        row.BatchNextCheckUtc = waiting.Select(t => t.NextCheckUtc).Where(t => t != null).Min();
    }

    /// <summary>
    /// Copy of counts, waves and stage plus every queued event, which is removed from the queue.
    /// </summary>
    public GenerationView Drain()
    {
        lock (_lock)
        {
            var view = BuildView(_events.ToList());
            _events.Clear();
            return view;
        }
    }

    /// <summary>
    /// Same as Drain without dequeuing events.
    /// </summary>
    public GenerationView Peek()
    {
        lock (_lock)
            return BuildView(_events.ToList());
    }

    // Caller holds _lock
    private GenerationView BuildView(List<GenerationEvent> events) => new()
    {
        Counts = _counts.Clone(),
        Waves = _waves.Select(w => w.Clone()).ToList(),
        Events = events,
        Stage = _stage,
        StageWave = _stageWave,
        StageWaveCount = _stageWaveCount,
        StageWaveStart = _stageWaveStart,
        StageWaveEnd = _stageWaveEnd,
        StageCount = _stageCount,
        CollectingAfterRestart = _collectingAfterRestart,
        PendingProviderBatches = _pendingProviderBatches
    };

    // One provider batch not yet collected; guarded by _lock
    private sealed class BatchTrack
    {
        public int? Row { get; set; }
        public DateTime SubmittedUtc { get; set; }
        public int Done { get; set; }
        public int Total { get; set; }
        public DateTime? NextCheckUtc { get; set; }
    }
}
