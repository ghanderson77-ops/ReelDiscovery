using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Builds a complete corpus plan before any prose (PLAN-02): allocation, relevant and non-relevant skeletons, numbering,
/// the relevant-thread filler, the subject seeder, the review protocol prose and the validator, in that order. The draft is
/// only read; every run returns a brand-new plan or a classified failure, so a cancelled or failed run leaves nothing behind
/// (D-18). The Random is used for this run only and never stored on the plan (D-20).
/// </summary>
public class CorpusPlanner
{
    public const int MaxParallelCalls = 3;
    public const int GuidanceMaxOutputTokens = 2_000;
    public const double BoostMultiplier = 2.0;
    public const double ReduceMultiplier = 0.5;
    public const string GuidanceOperationName = "Corpus Plan - Guidance";

    public const string NoDatedFactsMessage = "The case guide has no dated key facts. Add key facts on the Case Guide step, then click Plan Corpus.";
    public const string ShortfallNote = "The plan has more relevant emails than the % relevant setting because every key fact needs at least one email.";
    public const string GuidanceNotAppliedNote = "The guidance could not be applied to email volume; it was still passed to the thread planner.";
    public const string ProtocolFallbackNote = "The review protocol uses standard wording because the model did not write it.";

    private readonly ILlmGateway _llm;
    // Used only on the calling thread (skeletons are built before any parallel call); never stored on the plan (D-20)
    private readonly Random _rng;

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(GuidanceWeightsDto) }
        .Concat(RelevantThreadFiller.SchemaTypes)
        .Concat(SubjectSeeder.SchemaTypes)
        .Concat(ReviewProtocolBuilder.SchemaTypes)
        .ToList();

    public CorpusPlanner(ILlmGateway llm, Random? random = null)
    {
        _llm = llm;
        _rng = random ?? Random.Shared;
    }

    /// <summary>
    /// Plans the corpus for <paramref name="draft"/> from a snapshot of its corpus settings (defaults when none). Fatal
    /// provider errors and a refusal majority return a failed result with Plan null; a user cancel throws
    /// OperationCanceledException. The caller assigns the returned plan to the draft only on success.
    /// </summary>
    public async Task<CorpusPlanResult> PlanAsync(CaseDraft draft, bool mediaAvailable, string guidance, IProgress<PlanProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        guidance = (guidance ?? string.Empty).Trim();
        var result = new CorpusPlanResult();

        // 1. Snapshot: settings are cloned so planning never writes into the draft (T-04-48)
        var settings = CloneSettings(draft.Corpus?.Settings ?? CorpusSettings.CreateDefault(draft));
        settings.Normalize();

        var guide = draft.Guide;
        if (guide == null || !guide.Facts.Any(f => f != null && CaseDraft.TryParseDate(f.Date, out _)))
        {
            result.FailureMessage = NoDatedFactsMessage;
            return result;
        }

        var tracker = new ProgressTracker(progress);
        tracker.Report(PlanStage.Allocating, "Allocating emails to key facts", 0, 0, 0);

        // 2. Optional guidance weights (D-23)
        var notes = new List<string>();
        IReadOnlyDictionary<string, double>? multipliers = null;
        if (guidance.Length > 0)
        {
            var weights = await GuidanceWeightsAsync(draft, guide, guidance, ct);
            if (weights.FatalError.Length > 0)
            {
                result.FatalError = weights.FatalError;
                result.FailureMessage = weights.FatalError;
                return result;
            }

            if (weights.Multipliers == null)
                notes.Add(GuidanceNotAppliedNote);
            else
                multipliers = weights.Multipliers;
        }

        // 3. Allocation and skeletons
        var relevantEmails = CorpusSettings.RelevantCount(settings.TotalEmails, settings.RelevantPercent);
        var ctx = SkeletonContext.Create(draft, settings, mediaAvailable, _rng);
        var budget = BudgetAllocator.AllocateRelevant(ctx.DatedFacts, relevantEmails, multipliers, _rng);
        if (budget.Shortfall > 0)
            notes.Add(ShortfallNote);
        var relevant = ThreadSkeletonBuilder.BuildRelevant(ctx, budget);
        var nonRelevantEmails = Math.Max(0, settings.TotalEmails - budget.TotalMessages);
        var categories = BudgetAllocator.AllocateNonRelevant(nonRelevantEmails, settings);
        var nonRelevant = NonRelevantSkeletonBuilder.Build(ctx, categories, relevant);

        // 4. The new plan, numbered so filler and seeder prompts see final thread ids
        var plan = new CorpusPlan
        {
            PlanId = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            GuideHash = guide.ComputeHash(),
            SettingsHash = settings.ComputeHash(),
            DocIdPrefix = DocIdPrefix.Effective(settings, draft),
            Settings = settings,
            Threads = relevant.Concat(nonRelevant.Threads).ToList(),
            Externals = nonRelevant.Externals,
            Guidance = guidance
        };
        PlanNumbering.Assign(plan, guide);

        // 5. Progress over chunks + batches + 2 (D-18)
        var chunks = RelevantThreadFiller.BuildChunks(relevant).Count;
        var everyday = nonRelevant.Threads.Count(t => t.Category != NonRelevantCategory.Decoy);
        var decoys = nonRelevant.Threads.Count - everyday;
        var batches = Batches(everyday) + Batches(decoys);
        tracker.Total = chunks + batches + 2;
        tracker.Report(PlanStage.Allocating, "Allocating emails to key facts", 0, 0, 0);

        // 6. Text plans: relevant chunks, then non-relevant seeds
        var fill = await new RelevantThreadFiller(_llm, MaxParallelCalls)
            .FillAsync(draft, relevant, guidance, tracker.Wrap(0), ct);
        if (fill.IsFatal)
            return Fatal(result, fill.FatalError);
        tracker.Report(PlanStage.PlanningRelevant, $"Planning relevant threads ({chunks} of {chunks})", chunks, chunks, chunks);

        var seed = await new SubjectSeeder(_llm, MaxParallelCalls)
            .SeedAsync(draft, nonRelevant.Threads, plan.Externals, tracker.Wrap(chunks), ct);
        if (seed.IsFatal)
            return Fatal(result, seed.FatalError);
        tracker.Report(PlanStage.PlanningOther, $"Planning other emails ({batches} of {batches})", chunks + batches, batches, batches);

        result.TotalChunks = fill.TotalChunks + seed.TotalChunks;
        result.RefusedChunks = fill.RefusedChunks + seed.RefusedChunks;
        result.RefusalMessage = fill.RefusalMessage.Length > 0 ? fill.RefusalMessage : seed.RefusalMessage;
        if (result.RefusedChunks * 2 > result.TotalChunks)
        {
            result.FailureMessage = $"The model declined {result.RefusedChunks} of {result.TotalChunks} planning requests, so no plan was built."
                + (result.RefusalMessage.Length > 0 ? $" It said: {result.RefusalMessage}" : string.Empty)
                + " Change the case guide or the guidance, then click Plan Corpus.";
            return result;
        }

        notes.AddRange(fill.Notes);
        notes.AddRange(seed.Notes);

        // 7. Renumber (ids stable; links remapped), then the protocol prose as the last LLM stage
        PlanNumbering.Assign(plan, guide);
        tracker.Report(PlanStage.WritingProtocol, "Writing the review protocol", chunks + batches, 0, 1);
        try
        {
            plan.Protocol = await new ReviewProtocolBuilder(_llm).GenerateProseAsync(draft, ct);
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            return Fatal(result, ex.Result.Message ?? ex.Message);
        }

        if (plan.Protocol.IsFallback)
            notes.Add(ProtocolFallbackNote);

        plan.Notes = notes.Concat(ctx.Notes).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        result.Notes = plan.Notes.ToList();

        // 8. Deterministic checks
        tracker.Report(PlanStage.Validating, "Checking the plan", chunks + batches + 1, 0, 1);
        result.Findings = CorpusPlanValidator.Validate(draft, plan, mediaAvailable);
        result.Plan = plan;
        tracker.Report(PlanStage.Validating, "Checking the plan", tracker.Total, 1, 1);
        return result;
    }

    private static int Batches(int slots) => (slots + SubjectSeeder.SlotsPerBatch - 1) / SubjectSeeder.SlotsPerBatch;

    private static CorpusPlanResult Fatal(CorpusPlanResult result, string message)
    {
        result.Plan = null;
        result.FatalError = string.IsNullOrWhiteSpace(message) ? "The run stopped." : message;
        result.FailureMessage = result.FatalError;
        return result;
    }

    private static CorpusSettings CloneSettings(CorpusSettings settings) =>
        JsonSerializer.Deserialize<CorpusSettings>(JsonSerializer.Serialize(settings, CaseDraft.JsonOptions), CaseDraft.JsonOptions)
        ?? new CorpusSettings();

    // ----- Guidance weights (D-23) -----

    private sealed class GuidanceOutcome
    {
        public IReadOnlyDictionary<string, double>? Multipliers { get; set; }
        public string FatalError { get; set; } = string.Empty;
    }

    /// <summary>
    /// One small Story call mapping the guidance to boost/reduce per fact. Only the three fixed words map to multipliers and
    /// unknown fact ids are ignored (T-04-50). Non-fatal failures return null multipliers.
    /// </summary>
    private async Task<GuidanceOutcome> GuidanceWeightsAsync(CaseDraft draft, CaseGuide guide, string guidance, CancellationToken ct)
    {
        GuidanceWeightsDto dto;
        try
        {
            var result = await _llm.GenerateAsync<GuidanceWeightsDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = GuidanceOperationName,
                SystemPrompt = CorpusPlanPrompts.SystemPrompt,
                UserPrompt = BuildGuidancePrompt(draft, guide, guidance),
                MaxOutputTokens = GuidanceMaxOutputTokens
            }, ct);
            dto = result.GetValueOrThrow();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            return new GuidanceOutcome { FatalError = ex.Result.Message ?? ex.Message };
        }
        catch (LlmCallException)
        {
            return new GuidanceOutcome();
        }

        return new GuidanceOutcome { Multipliers = MapWeights(guide, dto) };
    }

    internal static Dictionary<string, double> MapWeights(CaseGuide guide, GuidanceWeightsDto dto)
    {
        var known = guide.Facts
            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id))
            .Select(f => f.Id)
            .ToDictionary(id => RelevantThreadFiller.CanonicalId(id), id => id, StringComparer.Ordinal);

        var multipliers = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var weight in dto.FactWeights ?? new List<FactWeightDto>())
        {
            if (weight == null || !known.TryGetValue(RelevantThreadFiller.CanonicalId(weight.FactId), out var factId))
                continue;

            var value = (weight.Weight ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "boost" => BoostMultiplier,
                "reduce" => ReduceMultiplier,
                _ => 1.0
            };

            // First answer per fact wins
            multipliers.TryAdd(factId, value);
        }

        return multipliers;
    }

    internal static string BuildGuidancePrompt(CaseDraft draft, CaseGuide guide, string guidance)
    {
        var sb = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(sb, draft);
        sb.AppendLine();
        sb.AppendLine("Key facts:");
        foreach (var fact in guide.Facts.Where(f => f != null && CaseDraft.TryParseDate(f.Date, out _)))
            sb.AppendLine($"{fact.Id} | {fact.Date.Trim()} | {fact.Title.Trim()}");
        sb.AppendLine();
        sb.AppendLine($"Guidance from the user: {guidance}");
        sb.AppendLine();
        sb.AppendLine("For each fact the guidance asks for more or less email about, give boost or reduce; otherwise normal.");
        return sb.ToString();
    }

    // ----- Progress (D-18) -----

    /// <summary>
    /// Maps stage-local reports from the parallel filler and seeder onto one monotonic Done over chunks + batches + 2.
    /// Reports arrive from worker threads, so the high-water mark is kept under a lock.
    /// </summary>
    private sealed class ProgressTracker
    {
        private readonly IProgress<PlanProgress>? _progress;
        private readonly object _lock = new();
        private int _done;

        public ProgressTracker(IProgress<PlanProgress>? progress)
        {
            _progress = progress;
        }

        public int Total { get; set; }

        public void Report(PlanStage stage, string message, int done, int stageDone, int stageTotal)
        {
            if (_progress == null)
                return;

            PlanProgress report;
            lock (_lock)
            {
                _done = Math.Max(_done, Math.Min(done, Total));
                report = new PlanProgress
                {
                    Stage = stage,
                    Message = message,
                    Done = _done,
                    Total = Total,
                    StageDone = stageDone,
                    StageTotal = stageTotal
                };
            }

            _progress.Report(report);
        }

        public IProgress<PlanProgress>? Wrap(int baseDone)
        {
            if (_progress == null)
                return null;

            // Synchronous sink: reports are forwarded in call order without posting to a SynchronizationContext
            return new Forwarder(p => Report(p.Stage, p.Message, baseDone + p.StageDone + 1, p.StageDone, p.StageTotal));
        }

        private sealed class Forwarder : IProgress<PlanProgress>
        {
            private readonly Action<PlanProgress> _action;

            public Forwarder(Action<PlanProgress> action)
            {
                _action = action;
            }

            public void Report(PlanProgress value) => _action(value);
        }
    }

    // ----- LLM DTOs (strict-schema safe: strings only, no nullables) -----

    internal class GuidanceWeightsDto
    {
        [JsonPropertyName("factWeights")]
        [Description("One entry per key fact in the request")]
        public List<FactWeightDto> FactWeights { get; set; } = new();
    }

    internal class FactWeightDto
    {
        [JsonPropertyName("factId")]
        [Description("Fact ID from the request, e.g. F-003")]
        public string FactId { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        [Description("boost, reduce or normal")]
        public string Weight { get; set; } = string.Empty;
    }
}

/// <summary>
/// Outcome of one PlanAsync run. Plan is non-null only on success; failures carry a user-facing message.
/// </summary>
public class CorpusPlanResult
{
    public CorpusPlan? Plan { get; set; }
    public List<PlanFinding> Findings { get; set; } = new();
    public bool Succeeded => Plan != null;
    public string FailureMessage { get; set; } = string.Empty;

    /// <summary>
    /// Non-empty when a fatal provider failure (bad key or spend limit) stopped planning.
    /// </summary>
    public string FatalError { get; set; } = string.Empty;

    public bool IsFatal => FatalError.Length > 0;
    public int RefusedChunks { get; set; }
    public int TotalChunks { get; set; }
    public string RefusalMessage { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = new();
}
