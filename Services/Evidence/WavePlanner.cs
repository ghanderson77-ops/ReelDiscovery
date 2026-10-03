using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Groups relevant threads into chronological waves (D-11, CORP-10). Pure function of the plan.
/// </summary>
public static class WavePlanner
{
    public const int WindowDays = 7;
    public const int ExtendedWindowDays = 14;
    public const int MinThreadsPerWave = 3;
    public const int MaxThreadsPerWave = 12;

    /// <summary>
    /// A relevant thread the run can write: it has an id and at least one message. Waves, run totals, assignments and the
    /// step all count with this one predicate, so a thread the run never schedules never counts as pending.
    /// </summary>
    public static bool IsGeneratable(PlannedThread? thread) =>
        thread != null
        && thread.Relevance == ThreadRelevance.Relevant
        && !string.IsNullOrEmpty(thread.Id)
        && thread.Messages?.Any(m => m != null) == true;

    /// <summary>
    /// Waves of relevant threads in first-message order: a wave spans <see cref="WindowDays"/> from its first thread,
    /// stretches to <see cref="ExtendedWindowDays"/> while it holds fewer than <see cref="MinThreadsPerWave"/> threads,
    /// and is split at its largest time gap while it holds more than <see cref="MaxThreadsPerWave"/>.
    /// </summary>
    public static List<GenerationWave> Plan(CorpusPlan plan)
    {
        // Always computed from the whole plan, never the remaining threads, so Continue sees identical waves and ledger visibility (Pitfall 3)
        var ordered = (plan?.Threads ?? new List<PlannedThread>())
            .Where(IsGeneratable)
            // corpus-plan.json is read from disk: a repeated id is scheduled once, as the runner keeps the first copy
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(t => (Thread: t, First: MessageTime.FirstUtc(t)))
            .OrderBy(x => x.First)
            .ThenBy(x => x.Thread.Id, StringComparer.Ordinal)
            .ToList();

        var groups = new List<List<(PlannedThread Thread, DateTime First)>>();
        List<(PlannedThread Thread, DateTime First)>? current = null;
        var startUtc = DateTime.MinValue;
        foreach (var item in ordered)
        {
            if (current != null)
            {
                var span = item.First - startUtc;
                if (span < TimeSpan.FromDays(WindowDays) ||
                    (current.Count < MinThreadsPerWave && span < TimeSpan.FromDays(ExtendedWindowDays)))
                {
                    current.Add(item);
                    continue;
                }

                groups.Add(current);
            }

            current = new List<(PlannedThread Thread, DateTime First)> { item };
            startUtc = item.First;
        }

        if (current != null)
            groups.Add(current);

        var bounded = new List<List<(PlannedThread Thread, DateTime First)>>();
        foreach (var group in groups)
            Split(group, bounded);

        var waves = new List<GenerationWave>();
        for (var i = 0; i < bounded.Count; i++)
        {
            var members = bounded[i].Select(x => x.Thread).ToList();
            var dates = members.Select(MessageTime.FirstLocalDate).ToList();
            waves.Add(new GenerationWave
            {
                Index = i + 1,
                Start = dates.Min(),
                End = dates.Max(),
                ThreadIds = members.Select(t => t.Id).ToList(),
                MessageCount = members.Sum(t => t.Messages.Count)
            });
        }

        return waves;
    }

    /// <summary>
    /// Thread id -> 1-based wave index.
    /// </summary>
    public static Dictionary<string, int> WaveIndexByThread(IEnumerable<GenerationWave> waves)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var wave in waves)
            foreach (var id in wave.ThreadIds)
                map[id] = wave.Index;
        return map;
    }

    // Splits at the largest gap (earliest on ties) until every part fits; each split yields two smaller non-empty parts, so it terminates (T-05-07)
    private static void Split(List<(PlannedThread Thread, DateTime First)> group, List<List<(PlannedThread Thread, DateTime First)>> output)
    {
        if (group.Count <= MaxThreadsPerWave)
        {
            output.Add(group);
            return;
        }

        var cut = 1;
        var largest = TimeSpan.MinValue;
        for (var i = 1; i < group.Count; i++)
        {
            var gap = group[i].First - group[i - 1].First;
            if (gap > largest)
            {
                largest = gap;
                cut = i;
            }
        }

        Split(group.GetRange(0, cut), output);
        Split(group.GetRange(cut, group.Count - cut), output);
    }
}
