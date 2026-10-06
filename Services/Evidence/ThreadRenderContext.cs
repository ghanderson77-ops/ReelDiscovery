using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Everything one thread's render call may see: the approved plan structure, the code-chosen assignment, resolved
/// people, the point-in-time knowledge of its participants and the slice of the ledger visible to it (CORP-01, D-09).
/// </summary>
public class ThreadRenderContext
{
    public CaseDraft Draft { get; set; } = new();
    public CaseGuide Guide { get; set; } = new();
    public CorpusPlan Plan { get; set; } = new();
    public PlannedThread Thread { get; set; } = new();
    public ThreadAssignment Assignment { get; set; } = new();
    public PeopleDirectory People { get; set; } = null!;
    public ThreadKnowledge Knowledge { get; set; } = new();
    public List<LedgerEntry> LedgerView { get; set; } = new();

    // 1-based wave of the thread; the ledger view holds earlier waves only (D-11)
    public int WaveIndex { get; set; }

    // Status the journal held before this run (Failed when Retry Failed Threads re-runs it); keeps live counts exact
    public ThreadRunStatus PreviousStatus { get; set; }

    /// <summary>OQ3: details this thread established before it was regenerated; non-empty only for a regenerated relevant thread.</summary>
    public IReadOnlyList<LedgerDelta> PriorDeltas { get; set; } = Array.Empty<LedgerDelta>();

    public static ThreadRenderContext Create(CaseDraft draft, CorpusPlan plan, PlannedThread thread, ThreadAssignment assignment,
        PeopleDirectory people, IReadOnlyList<LedgerEntry> ledger, int waveIndex)
    {
        var guide = draft?.Guide ?? new CaseGuide();
        var knowledge = KnowledgeGate.Build(guide, thread);
        return new ThreadRenderContext
        {
            Draft = draft ?? new CaseDraft(),
            Guide = guide,
            Plan = plan ?? new CorpusPlan(),
            Thread = thread ?? new PlannedThread(),
            Assignment = assignment ?? new ThreadAssignment { ThreadId = thread?.Id ?? string.Empty },
            People = people,
            Knowledge = knowledge,
            LedgerView = LedgerSelector.Select(ledger ?? Array.Empty<LedgerEntry>(), thread!, waveIndex, knowledge.Participants),
            WaveIndex = waveIndex
        };
    }
}
