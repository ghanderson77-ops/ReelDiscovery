using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Rebuilds the Generate Corpus dashboard (activity rows and wave rows) from a journal replay, so a reopened case shows
/// what was generated without a run (UI-SPEC entry rules 6-7). Pure: no I/O.
/// </summary>
public static class GenerationHistory
{
    /// <summary>
    /// One event per replayed record in journal order: the last record of each thread, then the last RunEnded. Older
    /// records of retried threads are not in the replay; the journal itself keeps them.
    /// </summary>
    public static List<GenerationEvent> Events(CorpusPlan plan, JournalReplay replay,
        IReadOnlyDictionary<string, ThreadAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(replay);
        assignments ??= new Dictionary<string, ThreadAssignment>();

        var threads = (plan.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var records = replay.LastByThread.Values
            .Where(r => r != null)
            .OrderBy(r => r.Utc)
            .ThenBy(r => r.ThreadId, StringComparer.Ordinal)
            .ToList();

        var events = new List<GenerationEvent>();
        var done = 0;
        foreach (var record in records)
        {
            threads.TryGetValue(record.ThreadId, out var thread);
            var ordered = thread == null
                ? new List<PlannedMessage>()
                : thread.Messages.Where(m => m != null).OrderBy(m => m.Seq).ToList();
            var marking = assignments.TryGetValue(record.ThreadId, out var assignment) && assignment != null
                ? assignment.Marking
                : PrivilegeMarking.None;
            var subject = thread == null ? string.Empty
                : ordered.Count == 0 ? thread.Subject ?? string.Empty
                : EmailComposer.SubjectFor(thread, ordered[0], marking);

            var e = new GenerationEvent
            {
                LocalTime = Local(record.Utc),
                ThreadId = record.ThreadId,
                Subject = subject,
                Attempt = record.Attempts,
                FilePath = ordered.Count > 0 ? ordered[^1].FilePath ?? string.Empty : string.Empty,
                Relevant = thread?.Relevance == ThreadRelevance.Relevant
            };

            if (record.Type == JournalRecordType.ThreadDone)
            {
                done++;
                e.Kind = record.Status == ThreadWriteStatus.Written
                    ? GenerationEventKind.ThreadWritten
                    : GenerationEventKind.KeptWithWarnings;
                e.Emails = record.Messages.Count;
                e.Details = record.LedgerDelta.Count;
                e.Violations = record.Violations.ToList();
            }
            else if (record.Type == JournalRecordType.ThreadFailed)
            {
                e.Kind = GenerationEventKind.ThreadFailed;
                e.Reason = record.Reason ?? string.Empty;
            }
            else
            {
                continue;
            }

            events.Add(e);
        }

        var ended = replay.LastRunEnded;
        if (ended != null)
        {
            var total = threads.Values.Count(WavePlanner.IsGeneratable);
            GenerationEventKind? kind = ended.Outcome switch
            {
                GenerationOutcome.Finished => GenerationEventKind.RunFinished,
                GenerationOutcome.Cancelled => GenerationEventKind.RunCancelled,
                GenerationOutcome.Fatal or GenerationOutcome.InternalError => GenerationEventKind.RunStopped,
                _ => null
            };
            if (kind != null)
            {
                events.Add(new GenerationEvent
                {
                    Kind = kind.Value,
                    LocalTime = Local(ended.Utc),
                    Reason = ended.Message ?? string.Empty,
                    Done = done,
                    Total = total
                });
            }
        }

        return events;
    }

    /// <summary>
    /// Stages rows for the whole plan, none running: a row per relevant wave, then the Non-relevant mail row (when the plan
    /// has non-relevant threads) and the Attachments for written threads row (when written threads have attachments to
    /// repair). Thread Done (written + kept-with-warnings), Warnings and Failed come from the state; attachment counters
    /// from <see cref="GenerationRunState.AttachmentSeeds"/>, so repairs count only on the Attachments row and wave and
    /// Non-relevant rows count the attachments of threads pending at run start. <paramref name="mediaAvailable"/> must be
    /// the value the run used (or the next run will use): skipped media is a repair item only when it is true.
    /// </summary>
    public static List<WaveProgress> Waves(CorpusPlan plan, GenerationRunState state, bool mediaAvailable = false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(state);

        // Same row indexes and the same mediaAvailable as GenerationRunner so live and reopened views match (checker 06-23)
        var waves = WavePlanner.Plan(plan);
        var nonRelevantRow = waves.Count + 1;
        var repairRow = waves.Count + 2;
        var rows = waves.Select(w => new WaveProgress
        {
            Index = w.Index,
            Start = w.Start,
            End = w.End,
            Threads = w.ThreadIds.Count,
            Emails = w.MessageCount,
            Done = w.ThreadIds.Count(state.IsDone),
            Warnings = w.ThreadIds.Count(id => Status(state, id) == ThreadRunStatus.Warnings),
            Failed = w.ThreadIds.Count(id => Status(state, id) == ThreadRunStatus.Failed),
            Running = false
        }).ToList();

        var threads = (plan.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Non-relevant generation threads (the runner's rule): an id and at least one message
        var filler = threads.Values
            .Where(t => t.Relevance != ThreadRelevance.Relevant && t.Messages != null && t.Messages.Any(m => m != null))
            .ToList();
        if (filler.Count > 0)
        {
            rows.Add(new WaveProgress
            {
                Index = nonRelevantRow,
                RowKind = StageRowKind.NonRelevant,
                Start = filler.Min(MessageTime.FirstLocalDate),
                End = filler.SelectMany(t => t.Messages.Where(m => m != null)).Select(MessageTime.LocalDate).Max(),
                Threads = filler.Count,
                Emails = filler.Sum(t => t.Messages.Count(m => m != null)),
                Done = filler.Count(t => state.IsDone(t.Id)),
                Warnings = filler.Count(t => Status(state, t.Id) == ThreadRunStatus.Warnings),
                Failed = filler.Count(t => Status(state, t.Id) == ThreadRunStatus.Failed),
                Running = false
            });
        }

        // Only when written threads have attachments to repair (the runner's rule: groups of journaled threads)
        var groups = state.RepairGroups(plan, mediaAvailable)
            .Where(g => threads.ContainsKey(g.ThreadId) && state.Records.ContainsKey(g.ThreadId))
            .ToList();
        if (groups.Count > 0)
        {
            rows.Add(new WaveProgress
            {
                Index = repairRow,
                RowKind = StageRowKind.Attachments,
                Threads = groups.Count,
                Running = false
            });
        }

        var seeds = state.AttachmentSeeds(plan, WavePlanner.WaveIndexByThread(waves), nonRelevantRow, repairRow, mediaAvailable);
        foreach (var seed in seeds.Values)
        {
            var row = rows.FirstOrDefault(r => r.Index == seed.RowIndex);
            if (row == null)
                continue;

            row.Attachments++;
            switch (seed.Status)
            {
                case AttachmentRunStatus.Written:
                    row.AttachmentsDone++;
                    break;
                case AttachmentRunStatus.Warnings:
                    row.AttachmentsDone++;
                    row.AttachmentWarnings++;
                    break;
                case AttachmentRunStatus.Failed:
                    row.AttachmentsFailed++;
                    break;
                case AttachmentRunStatus.Skipped:
                    row.AttachmentsSkipped++;
                    break;
            }
        }

        return rows;
    }

    private static ThreadRunStatus Status(GenerationRunState state, string id) =>
        state.StatusByThread.TryGetValue(id, out var status) ? status : ThreadRunStatus.Pending;

    // Journal times are UTC; an unspecified kind (hand-edited file) is read as UTC too
    private static DateTime Local(DateTime utc) =>
        utc.Kind == DateTimeKind.Local ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
