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

    public string MediaUnavailableNotice =>
        "Images and voicemail need a tested OpenAI key. Add one on the API Keys step to enable them.";

    /// <summary>Forces image/voicemail options off when no tested OpenAI key is configured (PROV-06).</summary>
    public void ApplyMediaAvailability()
    {
        if (IsMediaAvailable) return;
        WantsImages = false;
        WantsVoicemails = false;
        Config.IncludeImages = false;
        Config.IncludeVoicemails = false;
    }

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

        // Orphans are priced with this provider's catalog models (tiers never mix providers, D-11)
        var provider = TextProvider;
        var batch = new BatchingLlmProvider(live, transport ?? LlmProviderFactory.CreateBatchTransport(provider, key),
            new BatchLedger(caseFolder), planId,
            id => AvailableModelConfigs.FirstOrDefault(m => m.ModelId == id && m.Provider == provider), timings);
        return (new LlmGateway(batch, story, volume, UsageTracker), batch);
    }

    /// <summary>
    /// Blind-review gateway (D-12): the provider other than the corpus writer when its key is tested, else the writer's
    /// provider with SameProviderFallback true, else a null gateway. Both tiers use that provider's Volume default (OQ5).
    /// </summary>
    /// <param name="writerProvider">JournalReplay.WriterProvider; empty or unknown means the current text provider.</param>
    public (ILlmGateway? Gateway, LlmProviderKind Provider, bool SameProviderFallback, AIModelConfig? Model) CreateReviewerGateway(string writerProvider)
    {
        var writer = Enum.TryParse<LlmProviderKind>(writerProvider?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : TextProvider;
        var other = writer == LlmProviderKind.OpenAI ? LlmProviderKind.Anthropic : LlmProviderKind.OpenAI;

        LlmProviderKind provider;
        bool fallback;
        if (IsKeyReady(other))
        {
            provider = other;
            fallback = false;
        }
        else if (IsKeyReady(writer))
        {
            provider = writer;
            fallback = true;
        }
        else
        {
            return (null, writer, false, null);
        }

        var model = AIModelConfig.GetTierDefault(AvailableModelConfigs, provider, LlmTier.Volume);
        if (model == null)
            return (null, provider, fallback, null);

        var gateway = new LlmGateway(LlmProviderFactory.Create(provider, GetApiKey(provider)), model, model, UsageTracker);
        return (gateway, provider, fallback, model);
    }

    private bool IsKeyReady(LlmProviderKind provider) =>
        (provider == LlmProviderKind.OpenAI ? OpenAIKeyTested : AnthropicKeyTested)
        && !string.IsNullOrWhiteSpace(GetApiKey(provider));

    public OpenAIMediaClient? CreateMediaClient() =>
        IsMediaAvailable ? new OpenAIMediaClient(OpenAIApiKey, Config.ImageModel, Config.ImageQuality) : null;

    /// <summary>Editable per-unit media prices (D-29); cloned into each media generator.</summary>
    public MediaPrices MediaPrices { get; set; } = MediaPrices.Defaults();

    /// <summary>Media seam for the evidence pipeline; null without a tested OpenAI key (PROV-06).</summary>
    public IMediaGenerator? CreateMediaGenerator() =>
        IsMediaAvailable
            ? new OpenAIMediaGenerator(new OpenAIMediaClient(OpenAIApiKey, Config.ImageModel, Config.ImageQuality), MediaPrices.Clone(), UsageTracker)
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

    // Step 2 - Topic Input
    public string Topic { get; set; } = string.Empty;
    public string AdditionalInstructions { get; set; } = string.Empty;
    public int StorylineCount { get; set; } = 10;

    // Media type hints (inform storyline generation)
    public bool WantsDocuments { get; set; } = true;
    public bool WantsImages { get; set; } = false;
    public bool WantsVoicemails { get; set; } = false;

    // Step 3 - Storylines
    public List<Storyline> Storylines { get; set; } = new();
    public DateTime? AISuggestedStartDate { get; set; }
    public DateTime? AISuggestedEndDate { get; set; }
    public string? AISuggestedDateReasoning { get; set; }

    // Step 4 - Characters
    public List<Character> Characters { get; set; } = new();
    public string CompanyDomain { get; set; } = string.Empty;

    // Organization themes by domain (used for emails and documents)
    public Dictionary<string, OrganizationTheme> DomainThemes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Step 5 - Generation Config
    public GenerationConfig Config { get; set; } = new();

    // Step 6-7 - Results
    public List<EmailThread> GeneratedThreads { get; set; } = new();
    public GenerationResult? Result { get; set; }
}
