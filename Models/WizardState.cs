using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

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

    public OpenAIMediaClient? CreateMediaClient() =>
        IsMediaAvailable ? new OpenAIMediaClient(OpenAIApiKey, Config.ImageModel, Config.ImageQuality) : null;

    // Model Configurations
    public List<AIModelConfig> AvailableModelConfigs { get; set; } = AIModelConfig.GetDefaultModels();

    // Token Usage Tracking
    public TokenUsageTracker UsageTracker { get; } = new();

    // Workflow
    public WizardFlow Flow { get; set; } = WizardFlow.CaseDriven;

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
