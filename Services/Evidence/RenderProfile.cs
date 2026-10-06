using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// One render call's result as the unit of work sees it: the repaired thread (null when the call returned nothing),
/// the failure text or refusal, and the billed tokens.
/// </summary>
public class RenderAttempt
{
    public RenderedThread? Rendered { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool Refused { get; set; }
    public string RefusalMessage { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public int CacheReadTokens { get; set; }

    // Gateway cost of this call (PROV-07)
    public CostBreakdown Cost { get; set; } = new();
}

/// <summary>
/// One unit of work, one strategy per thread kind (06-RESEARCH Pattern 1, NOISE-05): which tier and request render the
/// thread, which checks it must pass and whether it may write the ledger. Relevant = Story + every V-check + ledger;
/// decoy = Story + V1/V5 structure + the must-not-mention scan; other non-relevant mail = Volume + V1/V5 only.
/// </summary>
public class RenderProfile
{
    public const string RelevantName = "Relevant";
    public const string DecoyName = "Decoy";
    public const string FillerName = "Filler";

    private Func<ThreadRenderContext, int, IReadOnlyList<RenderViolation>?, PlanCallRun, Task<RenderAttempt>> _render = null!;
    private Func<RenderedThread, ThreadRenderContext, List<RenderViolation>> _check = null!;

    private RenderProfile()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public LlmTier Tier { get; private set; }

    // D-13: only relevant threads write ledger deltas
    public bool UsesLedger { get; private set; }

    // The tier's model (D-29); null when the gateway has none. Cost itself comes from the gateway result (PROV-07)
    public AIModelConfig? Model { get; private set; }

    // Filler and decoys only: the sender organization's other work (NOISE-01)
    public SideWorld? SideWorld { get; private set; }

    internal Task<RenderAttempt> RenderAsync(ThreadRenderContext ctx, int attempt, IReadOnlyList<RenderViolation>? feedback,
        PlanCallRun run) => _render(ctx, attempt, feedback, run);

    internal List<RenderViolation> Check(RenderedThread rendered, ThreadRenderContext ctx) => _check(rendered, ctx);

    /// <summary>
    /// The Phase 5 relevant path: <see cref="ThreadRenderer.BuildRequest"/>, <see cref="RenderRepair.Apply"/> and the full
    /// <see cref="RenderValidator.Check"/> on the Story tier.
    /// </summary>
    public static RenderProfile Relevant(ILlmGateway llm) => new()
    {
        Name = RelevantName,
        Tier = LlmTier.Story,
        UsesLedger = true,
        Model = ModelFor(llm, LlmTier.Story),
        _render = async (ctx, attempt, feedback, run) =>
        {
            var outcome = await PlanCallRunner.RunAsync<ThreadRenderer.ThreadRenderDto>(
                llm, ThreadRenderer.BuildRequest(ctx, attempt, feedback), run).ConfigureAwait(false);
            return From(outcome, outcome.Value == null ? null : RenderRepair.Apply(outcome.Value, ctx));
        },
        _check = RenderValidator.Check
    };

    /// <summary>
    /// The profile for a planned thread: relevant threads use <see cref="Relevant"/>; decoys render the filler request on
    /// the Story tier and add the must-not-mention scan (D-23); every other non-relevant thread renders on the Volume tier
    /// with the structural checks only.
    /// </summary>
    public static RenderProfile For(PlannedThread thread, ILlmGateway llm, SideWorld? sideWorld)
    {
        if (thread == null || thread.Relevance == ThreadRelevance.Relevant)
            return Relevant(llm);

        var decoy = thread.Category == NonRelevantCategory.Decoy;
        var tier = decoy ? LlmTier.Story : LlmTier.Volume;
        return new RenderProfile
        {
            Name = decoy ? DecoyName : FillerName,
            Tier = tier,
            UsesLedger = false,
            Model = ModelFor(llm, tier),
            SideWorld = sideWorld,
            _render = async (ctx, attempt, feedback, run) =>
            {
                var outcome = await PlanCallRunner.RunAsync<FillerRenderer.FillerThreadDto>(
                    llm, FillerRenderer.BuildRequest(ctx, sideWorld, tier, attempt, feedback), run).ConfigureAwait(false);
                return From(outcome, outcome.Value == null ? null : FillerRenderer.ToRendered(outcome.Value, ctx));
            },
            // D-12 strict: V1/V5 only, no V8 retry for filler
            _check = decoy
                ? (rendered, ctx) => RenderValidator.CheckFiller(rendered, ctx).Concat(DecoyScanner.Scan(rendered, ctx.Thread)).ToList()
                : RenderValidator.CheckFiller
        };
    }

    private static RenderAttempt From<T>(PlanCallOutcome<T> outcome, RenderedThread? rendered) where T : class => new()
    {
        Rendered = rendered,
        Error = outcome.Error ?? string.Empty,
        Refused = outcome.Refused,
        RefusalMessage = outcome.RefusalMessage ?? string.Empty,
        InputTokens = outcome.InputTokens,
        OutputTokens = outcome.OutputTokens,
        CacheWriteTokens = outcome.CacheWriteTokens,
        CacheReadTokens = outcome.CacheReadTokens,
        Cost = outcome.Cost.Clone()
    };

    private static AIModelConfig? ModelFor(ILlmGateway llm, LlmTier tier)
    {
        try
        {
            return llm?.ResolveModel(tier);
        }
        catch (Exception)
        {
            // A gateway without a model for this tier prices the calls at the story model (ThreadUnitOfWork fallback)
            return null;
        }
    }
}
