using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Everything one attachment's text call may see (D-14): the parent message, its accepted body, the plan spec, the
/// chain position and only the case context known to the parent's participants as of its date.
/// </summary>
public class AttachmentRenderContext
{
    // The parent thread's render context (knowledge, ledger view, people)
    public ThreadRenderContext Thread { get; set; } = null!;

    public PlannedMessage Parent { get; set; } = new();
    public PlannedAttachment Spec { get; set; } = new();

    // Final title and file name (AttachmentNaming / AttachmentChains)
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    // Accepted body of the parent message
    public string ParentBody { get; set; } = string.Empty;

    public RenderedThread? AcceptedThread { get; set; }

    // The accepted thread's deltas at the parent seq and its ancestors only, never a sibling branch (filled by 06-18)
    public List<LedgerDelta> VisibleThreadDeltas { get; set; } = new();

    public AttachmentChainLink? Chain { get; set; }

    // Committed flattened text of the chain's previous version; empty when not available yet (Pitfall 15)
    public string PriorVersionText { get; set; } = string.Empty;

    public bool Relevant { get; set; }

    // D-15 richness (AttachmentPrompts.IsRich)
    public bool Rich { get; set; }

    public bool IsDecoy { get; set; }
    public LlmTier Tier { get; set; }

    // Filler only: the sender organization's other work (NOISE-01)
    public SideWorld? SideWorld { get; set; }

    // Invites only: the code-fixed schedule (D-20)
    public InviteSlot? Slot { get; set; }

    // True when later waves are already written (review concern 5)
    public bool LateRepair { get; set; }

    // Checks only, never shown in the prompt: it is future knowledge for the parent
    public List<LedgerEntry> LaterEstablished { get; set; } = new();
}
