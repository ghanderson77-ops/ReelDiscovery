using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Deterministic D-16 predecessor text (review concern 7). Invariant (checker 06-18): a thread waits only on in-step
/// threads of strictly lower rank (first SentAt, then Id), so the wait graph is a DAG by rank and no cycle is possible,
/// whatever the chains look like; a higher-ranked in-step predecessor contributes its step-start text; same-thread
/// predecessors never wait. Committed text persists across steps of the run.
/// </summary>
public class ChainScheduler
{
    private readonly object _lock = new();
    private readonly IReadOnlyDictionary<string, string> _threadOfAttachment;
    private readonly IReadOnlyDictionary<string, int> _threadRank;
    private readonly Dictionary<string, List<string>> _attachmentsOfThread = new(StringComparer.Ordinal);

    // The single committed store for the whole run: seeded at run start, merged by Complete, never cleared
    private readonly Dictionary<string, string> _committed;
    private Dictionary<string, string> _stepStart;
    private Dictionary<string, TaskCompletionSource> _step = new(StringComparer.Ordinal);

    public ChainScheduler(IReadOnlyDictionary<string, AttachmentChainLink> links, IReadOnlyDictionary<string, string> committedText,
        IReadOnlyDictionary<string, string> threadOfAttachment, IReadOnlyDictionary<string, int> threadRank)
    {
        Links = links ?? new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
        _threadOfAttachment = threadOfAttachment ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _threadRank = threadRank ?? new Dictionary<string, int>(StringComparer.Ordinal);
        _committed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (docId, text) in committedText ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrEmpty(docId) && text != null)
                _committed[docId] = text;
        }
        _stepStart = new Dictionary<string, string>(_committed, StringComparer.Ordinal);

        foreach (var (docId, threadId) in _threadOfAttachment)
        {
            if (string.IsNullOrEmpty(docId) || string.IsNullOrEmpty(threadId))
                continue;
            if (!_attachmentsOfThread.TryGetValue(threadId, out var list))
                _attachmentsOfThread[threadId] = list = new List<string>();
            list.Add(docId);
        }
    }

    public IReadOnlyDictionary<string, AttachmentChainLink> Links { get; }

    /// <summary>
    /// Fixed total thread order: first message SentAt (UTC, <see cref="MessageTime.FirstUtc"/>), then Id ordinal, as
    /// index 0..n-1. Independent of the plan's list order.
    /// </summary>
    public static Dictionary<string, int> RankThreads(CorpusPlan plan)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = (plan?.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(MessageTime.FirstUtc)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
            ranks[ordered[i].Id] = i;
        return ranks;
    }

    /// <summary>
    /// Owning thread of every planned attachment DocId (first occurrence wins).
    /// </summary>
    public static Dictionary<string, string> ThreadOfAttachment(CorpusPlan plan)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var thread in (plan?.Threads ?? new List<PlannedThread>()).Where(t => t != null && !string.IsNullOrEmpty(t.Id)))
        {
            foreach (var message in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null))
            {
                foreach (var attachment in (message.Attachments ?? new List<PlannedAttachment>())
                             .Where(a => a != null && !string.IsNullOrEmpty(a.DocId)))
                    result.TryAdd(attachment.DocId, thread.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// Starts a run step: the given threads may be waited on until they Complete; committed text is snapshotted as the
    /// step-start text. Never clears committed text.
    /// </summary>
    public void BeginStep(IEnumerable<string> threadIds)
    {
        var step = new Dictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        foreach (var id in threadIds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(id))
                step.TryAdd(id, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        lock (_lock)
        {
            _step = step;
            _stepStart = new Dictionary<string, string>(_committed, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The committed flattened text of the chain predecessor of <paramref name="attachmentDocId"/>, or null when none is
    /// available. Waits only for an in-step predecessor thread that ranks strictly lower than the successor's thread.
    /// </summary>
    public Task<string?> PriorTextAsync(string attachmentDocId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(attachmentDocId) || !Links.TryGetValue(attachmentDocId, out var link)
            || link == null || string.IsNullOrEmpty(link.PreviousDocId))
            return Task.FromResult<string?>(null);

        var predecessor = link.PreviousDocId;
        var predecessorThread = _threadOfAttachment.TryGetValue(predecessor, out var t) ? t : string.Empty;
        var successorThread = _threadOfAttachment.TryGetValue(attachmentDocId, out var s) ? s : string.Empty;

        lock (_lock)
        {
            // The unit resolves same-thread predecessors itself; waiting here would deadlock on our own Complete
            if (predecessorThread.Length == 0 || successorThread.Length == 0 || predecessorThread == successorThread)
                return Task.FromResult(Lookup(_committed, predecessor));

            if (_step.TryGetValue(predecessorThread, out var source))
            {
                // Checker 06-18: waiting on a higher-ranked thread could close a cycle (interleaved or crossing chains);
                // step-start text keeps the answer independent of finish order
                if (Compare(predecessorThread, successorThread) > 0)
                    return Task.FromResult(Lookup(_stepStart, predecessor));

                return WaitThenLookupAsync(source, predecessor, ct);
            }

            // Not in this step: its text cannot change until the step ends
            return Task.FromResult(Lookup(_committed, predecessor));
        }
    }

    /// <summary>
    /// Waits for every lower-ranked in-step thread that owns the chain predecessor of any attachment of
    /// <paramref name="threadId"/>: exactly the set <see cref="PriorTextAsync"/> would wait on, so a caller holding a
    /// bounded slot afterwards never blocks on chain text.
    /// </summary>
    public Task WaitForPredecessorsAsync(string threadId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<Task> waits;
        lock (_lock)
        {
            waits = EdgesLocked(threadId)
                .Select(e => _step[e.To].Task)
                .ToList();
        }

        return waits.Count == 0 ? Task.CompletedTask : Task.WhenAll(waits).WaitAsync(ct);
    }

    /// <summary>
    /// Merges the thread's committed attachment texts (an empty set changes nothing) and releases its waiters. Harmless
    /// to call again; never removes text.
    /// </summary>
    public void Complete(string threadId, IReadOnlyDictionary<string, string> committedTexts)
    {
        TaskCompletionSource? source = null;
        lock (_lock)
        {
            // Checker 06-18: committed text outlives the step, so later waves of the same run see it
            foreach (var (docId, text) in committedTexts ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrEmpty(docId) && text != null)
                    _committed[docId] = text;
            }

            if (!string.IsNullOrEmpty(threadId))
                _step.TryGetValue(threadId, out source);
        }

        source?.TrySetResult();
    }

    // Test seam: every (thread, in-step predecessor thread) wait edge of a thread
    internal IEnumerable<(string From, string To)> WaitEdges(string threadId)
    {
        lock (_lock)
            return EdgesLocked(threadId);
    }

    // Strict total order: rank (threads missing from the plan after every planned one), then Id ordinal
    internal int Compare(string a, string b)
    {
        var rankA = _threadRank.TryGetValue(a ?? string.Empty, out var ra) ? ra : int.MaxValue;
        var rankB = _threadRank.TryGetValue(b ?? string.Empty, out var rb) ? rb : int.MaxValue;
        var byRank = rankA.CompareTo(rankB);
        return byRank != 0 ? byRank : string.CompareOrdinal(a, b);
    }

    // Caller holds _lock
    private List<(string From, string To)> EdgesLocked(string threadId)
    {
        var edges = new List<(string From, string To)>();
        if (string.IsNullOrEmpty(threadId) || !_attachmentsOfThread.TryGetValue(threadId, out var docIds))
            return edges;

        foreach (var docId in docIds)
        {
            if (!Links.TryGetValue(docId, out var link) || link == null || string.IsNullOrEmpty(link.PreviousDocId))
                continue;
            if (!_threadOfAttachment.TryGetValue(link.PreviousDocId, out var predecessorThread)
                || string.IsNullOrEmpty(predecessorThread) || predecessorThread == threadId)
                continue;
            if (!_step.ContainsKey(predecessorThread) || Compare(predecessorThread, threadId) >= 0)
                continue;
            if (!edges.Contains((threadId, predecessorThread)))
                edges.Add((threadId, predecessorThread));
        }

        return edges;
    }

    private async Task<string?> WaitThenLookupAsync(TaskCompletionSource source, string predecessor, CancellationToken ct)
    {
        await source.Task.WaitAsync(ct).ConfigureAwait(false);
        lock (_lock)
            return Lookup(_committed, predecessor);
    }

    private static string? Lookup(Dictionary<string, string> store, string docId) =>
        store.TryGetValue(docId, out var text) && !string.IsNullOrEmpty(text) ? text : null;
}
