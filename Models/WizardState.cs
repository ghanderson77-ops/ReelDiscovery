using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Models;

public class WizardState
{
    public WizardState()
    {
        SetTextProvider(LlmProviderKind.OpenAI);
    }

    // Step 1 - API Configuration
    public string OpenAIApiKey { get; set; } = string.Empty;
    public string AnthropicApiKey { get; set; } = string.Empty;
    public bool OpenAIKeyTested { get; set; }
    public bool AnthropicKeyTested { get; set; }

    /// <summary>
    /// Text provider for this run; defaults to OpenAI on every launch (D-05) and is never persisted.
    /// </summary>
    public LlmProviderKind TextProvider { get; private set; } = LlmProviderKind.OpenAI;

    public string StoryModelId { get; set; } = string.Empty;
    public string VolumeModelId { get; set; } = string.Empty;

    // Tiers never mix providers (D-11): configs are filtered to the selected text provider
    public AIModelConfig? StoryModelConfig =>
        AvailableModelConfigs.FirstOrDefault(m => m.ModelId == StoryModelId && m.Provider == TextProvider);

    public AIModelConfig? VolumeModelConfig =>
        AvailableModelConfigs.FirstOrDefault(m => m.ModelId == VolumeModelId && m.Provider == TextProvider);

    /// <summary>
    /// Selects the text provider and resets Story/Volume to that provider's catalog tier defaults.
    /// </summary>
    public void SetTextProvider(LlmProviderKind provider)
    {
        TextProvider = provider;
        StoryModelId = AIModelConfig.GetTierDefault(AvailableModelConfigs, provider, LlmTier.Story)?.ModelId ?? string.Empty;
        VolumeModelId = AIModelConfig.GetTierDefault(AvailableModelConfigs, provider, LlmTier.Volume)?.ModelId ?? string.Empty;
    }

    // Images and voicemail always use OpenAI, regardless of the text provider
    public bool IsMediaAvailable => !string.IsNullOrWhiteSpace(OpenAIApiKey) && OpenAIKeyTested;

    /// <summary>Media-gating line on the API Configuration step (shown under the OpenAI key row); later steps use their own text that points back to this step.</summary>
    public string MediaUnavailableNotice =>
        "Images and voicemail need a tested OpenAI key. Enter and test one above to enable them.";

    /// <summary>
    /// Next is allowed once the selected text provider's key has passed its test (D-06).
    /// </summary>
    public static bool CanProceedFromKeys(LlmProviderKind provider, bool openAITested, bool anthropicTested) =>
        provider == LlmProviderKind.OpenAI ? openAITested : anthropicTested;

    public bool CanProceedFromKeys() => CanProceedFromKeys(TextProvider, OpenAIKeyTested, AnthropicKeyTested);

    public string ProviderSummary =>
        $"{TextProvider}: {StoryModelConfig?.DisplayName ?? StoryModelId} / {VolumeModelConfig?.DisplayName ?? VolumeModelId}";

    /// <summary>
    /// Model used to test a key: the text provider tests its Story model; the other provider
    /// (e.g. OpenAI used only for media) tests its Volume default so a key without access to the
    /// newest model does not needlessly disable media.
    /// </summary>
    public string GetKeyTestModelId(LlmProviderKind provider) =>
        provider == TextProvider
            ? StoryModelId
            : AIModelConfig.GetTierDefault(AvailableModelConfigs, provider, LlmTier.Volume)?.ModelId ?? string.Empty;

    public string GetApiKey(LlmProviderKind provider) =>
        provider == LlmProviderKind.OpenAI ? OpenAIApiKey : AnthropicApiKey;

    public ILlmGateway CreateGateway()
    {
        var key = GetApiKey(TextProvider);
        var story = StoryModelConfig
            ?? throw new InvalidOperationException($"No Story model selected for {TextProvider}");
        var volume = VolumeModelConfig
            ?? throw new InvalidOperationException($"No Volume model selected for {TextProvider}");
        return new LlmGateway(LlmProviderFactory.Create(TextProvider, key), story, volume, UsageTracker);
    }

    /// <summary>
    /// Generation-run gateway (D-07). Live mode equals <see cref="CreateGateway"/> with Batch null; batch mode wraps the live
    /// provider in a <see cref="BatchingLlmProvider"/> over the case folder's batch ledger and returns it so the runner can
    /// subscribe to its status, resume pending batches and bill orphans. <paramref name="transport"/> and
    /// <paramref name="timings"/> are test seams; null uses the text provider's batch transport and the default timings.
    /// </summary>
    public (ILlmGateway Gateway, BatchingLlmProvider? Batch) CreateRunGateway(bool useBatch, string caseFolder, Guid planId,
        IBatchTransport? transport = null, BatchTimings? timings = null)
    {
        var key = GetApiKey(TextProvider);
        var story = StoryModelConfig
            ?? throw new InvalidOperationException($"No Story model selected for {TextProvider}");
        var volume = VolumeModelConfig
            ?? throw new InvalidOperationException($"No Volume model selected for {TextProvider}");

        var live = LlmProviderFactory.Create(TextProvider, key);
        if (!useBatch)
            return (new LlmGateway(live, story, volume, UsageTracker), null);

        // CR-A04: orphans are priced with the catalog model of the provider that ran their batch (tiers never mix
        // providers, D-11), so a batch left by another provider is never billed at $0
        var provider = TextProvider;
        Func<LlmProviderKind, string, AIModelConfig?> price = (kind, id) =>
            AvailableModelConfigs.FirstOrDefault(m => m.ModelId == id && m.Provider == kind);
        var batch = new BatchingLlmProvider(live, transport ?? LlmProviderFactory.CreateBatchTransport(provider, key),
            new BatchLedger(caseFolder), planId, price, timings);
        return (new LlmGateway(batch, story, volume, UsageTracker), batch);
    }

    /// <summary>
    /// Blind-review gateway (D-12): the provider other than the corpus writer when its key is tested, else the writer's
    /// provider with SameProviderFallback true, else a null gateway. Both tiers use that provider's Volume default (OQ5).
    /// </summary>
    /// <param name="writerProvider">JournalReplay.WriterProvider; empty or unknown means the current text provider.</param>
    public (ILlmGateway? Gateway, LlmProviderKind Provider, bool SameProviderFallback, AIModelConfig? Model) CreateReviewerGateway(string writerProvider)
    {
        var (provider, fallback, model, hasKey) = ChooseReviewer(writerProvider);
        if (!hasKey || model == null)
            return (null, provider, hasKey && fallback, null);

        var gateway = new LlmGateway(LlmProviderFactory.Create(provider, GetApiKey(provider)), model, model, UsageTracker);
        return (gateway, provider, fallback, model);
    }

    /// <summary>
    /// The reviewer CreateReviewerGateway would use, without creating a client (Case Summary's not-run estimate). With no
    /// tested key, HasKey is false and Model is the writer provider's Volume default, so an estimate can still be shown.
    /// </summary>
    public (LlmProviderKind Provider, bool SameProviderFallback, AIModelConfig? Model, bool HasKey) ChooseReviewer(string writerProvider)
    {
        var writer = Enum.TryParse<LlmProviderKind>(writerProvider?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : TextProvider;
        var other = writer == LlmProviderKind.OpenAI ? LlmProviderKind.Anthropic : LlmProviderKind.OpenAI;

        if (IsKeyReady(other))
            return (other, false, AIModelConfig.GetTierDefault(AvailableModelConfigs, other, LlmTier.Volume), true);
        if (IsKeyReady(writer))
            return (writer, true, AIModelConfig.GetTierDefault(AvailableModelConfigs, writer, LlmTier.Volume), true);
        return (writer, false, AIModelConfig.GetTierDefault(AvailableModelConfigs, writer, LlmTier.Volume), false);
    }

    private bool IsKeyReady(LlmProviderKind provider) =>
        (provider == LlmProviderKind.OpenAI ? OpenAIKeyTested : AnthropicKeyTested)
        && !string.IsNullOrWhiteSpace(GetApiKey(provider));

    public OpenAIMediaClient? CreateMediaClient() =>
        IsMediaAvailable ? new OpenAIMediaClient(OpenAIApiKey, MediaSettings.ImageModel, MediaSettings.ImageQuality) : null;

    /// <summary>Image model and quality used by every media client (moved out of the deleted legacy generation settings, D-20).</summary>
    public MediaSettings MediaSettings { get; set; } = new();

    /// <summary>Editable per-unit media prices (D-29); cloned into each media generator.</summary>
    public MediaPrices MediaPrices { get; set; } = MediaPrices.Defaults();

    /// <summary>Media seam for the evidence pipeline; null without a tested OpenAI key (PROV-06).</summary>
    public IMediaGenerator? CreateMediaGenerator() =>
        IsMediaAvailable
            ? new OpenAIMediaGenerator(new OpenAIMediaClient(OpenAIApiKey, MediaSettings.ImageModel, MediaSettings.ImageQuality), MediaPrices.Clone(), UsageTracker)
            : null;

    // Model Configurations
    public List<AIModelConfig> AvailableModelConfigs { get; set; } = AIModelConfig.GetDefaultModels();

    // Token Usage Tracking
    public TokenUsageTracker UsageTracker { get; } = new();

    // Case-driven flow (Phase 2+)
    public CaseDraft Case { get; set; } = new();

    /// <summary>
    /// Shared case.json store (autosave, load, recent cases); injectable so tests and the render harness use temp folders.
    /// </summary>
    public CaseStore CaseStore { get; set; } = new();

    /// <summary>
    /// One-shot status text the next case step shows on entry (e.g. "Opened {title}."), then clears.
    /// </summary>
    public string PendingNotice { get; set; } = string.Empty;

    /// <summary>Set when a case is opened from disk (04-19) so Review Plan shows No corpus plan yet instead of auto-planning; cleared by Next on Corpus Settings or by Plan Corpus on Review Plan.</summary>
    public bool SuppressAutoPlanOnce { get; set; }
}
