using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class GenerationCostEstimatorTests
{
    private static readonly AIModelConfig Story = new()
    {
        DisplayName = "Story Model", InputTokenPricePerMillion = 3m, OutputTokenPricePerMillion = 15m
    };

    private static readonly AIModelConfig Volume = new()
    {
        DisplayName = "Volume Model", InputTokenPricePerMillion = 0.25m, OutputTokenPricePerMillion = 1.25m
    };

    private static readonly MediaPrices Prices = MediaPrices.Defaults();

    private static PlannedThread NewThread(ThreadRelevance relevance, NonRelevantCategory category, int messages,
        string id)
    {
        var thread = new PlannedThread { Id = id, Relevance = relevance, Category = category };
        for (var i = 0; i < messages; i++)
            thread.Messages.Add(new PlannedMessage { Id = $"{id}-M{i + 1:00}", Seq = i + 1 });
        return thread;
    }

    // 10 relevant (first two hot with a Word each), 4 decoys, 20 daily business (3 Excel, 2 images, 1 voicemail)
    private static CorpusPlan MixedPlan()
    {
        var relevant = NewThread(ThreadRelevance.Relevant, NonRelevantCategory.None, 10, "T-0001");
        relevant.Messages[0].IsHot = true;
        relevant.Messages[1].IsHot = true;
        relevant.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Word });
        relevant.Messages[1].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Word });

        var decoy = NewThread(ThreadRelevance.NotRelevant, NonRelevantCategory.Decoy, 4, "T-0002");
        var daily = NewThread(ThreadRelevance.NotRelevant, NonRelevantCategory.DailyBusiness, 20, "T-0003");
        for (var i = 0; i < 3; i++)
            daily.Messages[i].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Excel });
        daily.Messages[3].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Image });
        daily.Messages[4].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Image, IsInline = true });
        daily.Messages[5].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Voicemail });

        return new CorpusPlan { Threads = new List<PlannedThread> { relevant, decoy, daily } };
    }

    private static decimal S(int input, int output) => Story.CalculateTotalCost(input, output);

    private static decimal V(int input, int output) => Volume.CalculateTotalCost(input, output);

    [Fact]
    public void Estimate_SplitsStoryVolumeAndMedia()
    {
        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), Story, Volume, Prices);

        var relevant = 14 * S(4_000, 450) * 1.15m;
        var other = 20 * V(1_500, 300) * 1.15m;
        var storyAttachments = 2 * S(3_000, 1_500) * 1.15m;
        var volumeAttachments = 3 * V(800, 200) * 1.15m;
        var media = 2 * 0.006m + Prices.SpeechCost(400);
        var voiceCast = V(2_500, 400) * 1.15m;
        var sideWorld = V(3_000, 1_200) * 1.15m;

        Assert.Equal(relevant, estimate.RelevantCost);
        Assert.Equal(other, estimate.OtherCost);
        Assert.Equal(storyAttachments + volumeAttachments, estimate.AttachmentCost);
        Assert.Equal(media, estimate.MediaCost);
        Assert.Equal(voiceCast, estimate.VoiceCastCost);
        Assert.Equal(sideWorld, estimate.SideWorldCost);
        Assert.Equal(relevant + storyAttachments, estimate.StoryCost);
        Assert.Equal(other + volumeAttachments + voiceCast + sideWorld, estimate.VolumeCost);
        Assert.Equal(estimate.StoryCost + estimate.VolumeCost + estimate.MediaCost, estimate.Total);
        Assert.Equal("Story Model", estimate.ModelName);
        Assert.Equal("Volume Model", estimate.VolumeModelName);
    }

    [Fact]
    public void Estimate_NullVolume_PricesVolumeOnStory()
    {
        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), Story, null, Prices);

        Assert.Equal(20 * S(1_500, 300) * 1.15m, estimate.OtherCost);
        Assert.Equal(S(2_500, 400) * 1.15m, estimate.VoiceCastCost);
        Assert.Equal(S(3_000, 1_200) * 1.15m, estimate.SideWorldCost);
        Assert.Equal("Story Model", estimate.VolumeModelName);
    }

    [Fact]
    public void Estimate_BothModelsNull_Zero()
    {
        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), null, null, Prices);

        Assert.Equal(0m, estimate.Total);
        Assert.Equal(0m, estimate.MediaCost);
        Assert.Equal(string.Empty, estimate.ModelName);
    }

    [Fact]
    public void Estimate_OldOverload_Unchanged()
    {
        var plan = MixedPlan();
        var estimate = GenerationCostEstimator.Estimate(plan, Story);

        // Phase 5: every non-relevant message priced as Other on Story; images/voicemail as small attachments
        Assert.Equal(10 * S(4_000, 450) * 1.15m, estimate.RelevantCost);
        Assert.Equal(24 * S(1_500, 300) * 1.15m, estimate.OtherCost);
        Assert.Equal((5 * S(3_000, 1_500) + 3 * S(800, 200)) * 1.15m, estimate.AttachmentCost);
        Assert.Equal(estimate.RelevantCost + estimate.OtherCost + estimate.AttachmentCost, estimate.Total);
        Assert.Equal(0m, estimate.MediaCost);
        Assert.Equal(0m, estimate.VolumeCost);
    }

    [Fact]
    public void Estimate_InvitesAreSmallOnParentTier_MediaNeverText()
    {
        var relevant = NewThread(ThreadRelevance.Relevant, NonRelevantCategory.None, 1, "T-0001");
        relevant.Messages[0].IsHot = true;
        relevant.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.CalendarInvite });
        relevant.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Image });
        var daily = NewThread(ThreadRelevance.NotRelevant, NonRelevantCategory.DailyBusiness, 1, "T-0002");
        daily.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.CalendarInvite });
        daily.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Voicemail });
        var plan = new CorpusPlan { Threads = new List<PlannedThread> { relevant, daily } };

        var estimate = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices);

        Assert.Equal(S(800, 200) * 1.15m + V(800, 200) * 1.15m, estimate.AttachmentCost);
        Assert.Equal(0.006m + Prices.SpeechCost(400), estimate.MediaCost);
    }

    [Fact]
    public void Estimate_NoVoicemail_NoVoiceCast()
    {
        var plan = MixedPlan();
        foreach (var message in plan.Threads.SelectMany(t => t.Messages))
            message.Attachments.RemoveAll(a => a.Kind == AttachmentType.Voicemail);

        var estimate = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices);

        Assert.Equal(0m, estimate.VoiceCastCost);
        Assert.Equal(2 * 0.006m, estimate.MediaCost);
    }

    [Fact]
    public void Estimate_RelevantOnly_NoSideWorld()
    {
        var plan = MixedPlan();
        plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);

        var estimate = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices);

        Assert.Equal(0m, estimate.SideWorldCost);
        Assert.Equal(0m, estimate.VolumeCost);
    }

    [Fact]
    public void Estimate_MediaUnavailable_NoMediaNoVoiceCast()
    {
        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), Story, Volume, Prices, mediaAvailable: false);

        Assert.Equal(0m, estimate.MediaCost);
        Assert.Equal(0m, estimate.VoiceCastCost);
        Assert.Equal(estimate.StoryCost + estimate.VolumeCost, estimate.Total);
    }

    [Fact]
    public void Estimate_MediaNotMultipliedByQaOverhead()
    {
        var prices = new MediaPrices { ImagePerUnit = 1m, TtsPerMillionChars = 0m };

        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), Story, Volume, prices);

        Assert.Equal(2m, estimate.MediaCost);
    }

    [Fact]
    public void Estimate_FillerLengthDoesNotChangeOtherCost()
    {
        var plan = MixedPlan();
        var before = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices).OtherCost;

        // Length bands live in render assignments, not the plan: longer filler content never changes the per-message price
        // (D-12: filler has no length retries)
        var longer = MixedPlan();
        foreach (var thread in longer.Threads.Where(t => t.Category == NonRelevantCategory.DailyBusiness))
        {
            foreach (var message in thread.Messages)
            {
                message.MustConvey.Add(new string('x', 2_000));
                message.Intent = new string('y', 4_000);
            }
        }

        Assert.Equal(before, GenerationCostEstimator.Estimate(longer, Story, Volume, Prices).OtherCost);
    }

    [Fact]
    public void Estimate_RichFromGuideFact_UsesDocumentTokens()
    {
        var guide = new CaseGuide();
        guide.Facts.Add(new KeyFact { Id = "F-001", Significance = FactSignificance.Key });
        guide.Facts.Add(new KeyFact { Id = "F-002", Significance = FactSignificance.Supporting });

        var keyed = NewThread(ThreadRelevance.Relevant, NonRelevantCategory.None, 1, "T-0001");
        keyed.Messages[0].FactIds.Add("F-001");
        keyed.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Word });
        var supporting = NewThread(ThreadRelevance.Relevant, NonRelevantCategory.None, 1, "T-0002");
        supporting.FactIds.Add("F-002");
        supporting.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.PowerPoint });
        var decoy = NewThread(ThreadRelevance.NotRelevant, NonRelevantCategory.Decoy, 1, "T-0003");
        decoy.Messages[0].IsHot = true;
        decoy.Messages[0].Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Excel });
        var plan = new CorpusPlan { Threads = new List<PlannedThread> { keyed, supporting, decoy } };

        var estimate = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices, guide: guide);

        Assert.Equal((S(3_000, 1_500) + 2 * S(800, 200)) * 1.15m, estimate.AttachmentCost);

        // Without the guide only IsHot makes an attachment rich
        var noGuide = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices);
        Assert.Equal(3 * S(800, 200) * 1.15m, noGuide.AttachmentCost);
    }

    [Fact]
    public void IsRichAttachment_FollowsD15()
    {
        var guide = new CaseGuide();
        guide.Facts.Add(new KeyFact { Id = "F-001", Significance = FactSignificance.Hot });
        var thread = NewThread(ThreadRelevance.Relevant, NonRelevantCategory.None, 1, "T-0001");
        var parent = thread.Messages[0];

        Assert.False(GenerationCostEstimator.IsRichAttachment(thread, parent, guide));
        thread.FactIds.Add("F-001");
        Assert.True(GenerationCostEstimator.IsRichAttachment(thread, parent, guide));
        Assert.False(GenerationCostEstimator.IsRichAttachment(thread, parent, null));
        parent.IsHot = true;
        Assert.True(GenerationCostEstimator.IsRichAttachment(thread, parent, null));
        thread.Relevance = ThreadRelevance.NotRelevant;
        Assert.False(GenerationCostEstimator.IsRichAttachment(thread, parent, guide));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PriceWork_ForPlan_EqualsEstimate(bool mediaAvailable)
    {
        var plan = MixedPlan();
        var guide = new CaseGuide();
        var viaWork = GenerationCostEstimator.PriceWork(PricedWork.ForPlan(plan, guide), Story, Volume, Prices, mediaAvailable);
        var viaEstimate = GenerationCostEstimator.Estimate(plan, Story, Volume, Prices, mediaAvailable, guide);

        Assert.Equal(viaEstimate.RelevantCost, viaWork.RelevantCost);
        Assert.Equal(viaEstimate.OtherCost, viaWork.OtherCost);
        Assert.Equal(viaEstimate.AttachmentCost, viaWork.AttachmentCost);
        Assert.Equal(viaEstimate.StoryCost, viaWork.StoryCost);
        Assert.Equal(viaEstimate.VolumeCost, viaWork.VolumeCost);
        Assert.Equal(viaEstimate.MediaCost, viaWork.MediaCost);
        Assert.Equal(viaEstimate.VoiceCastCost, viaWork.VoiceCastCost);
        Assert.Equal(viaEstimate.SideWorldCost, viaWork.SideWorldCost);
        Assert.Equal(viaEstimate.Total, viaWork.Total);
        Assert.Equal(viaEstimate.ModelName, viaWork.ModelName);
        Assert.Equal(viaEstimate.VolumeModelName, viaWork.VolumeModelName);
    }

    private static AIModelConfig Catalog(string modelId) =>
        AIModelConfig.GetDefaultModels().First(m => m.ModelId == modelId);

    private static decimal Cost(AIModelConfig model, TokenUsage usage) => model.CalculateCost(usage).TotalUsd;

    // 10 relevant messages only, no attachments: isolates the per-message text price
    private static CorpusPlan RelevantOnlyPlan() =>
        new() { Threads = new List<PlannedThread> { NewThread(ThreadRelevance.Relevant, NonRelevantCategory.None, 10, "T-0001") } };

    [Fact]
    public void PriceWork_OpusStory_PricesStablePrefixAsCacheReads()
    {
        var opus = Catalog("claude-opus-5-5");
        Assert.True(GenerationCostEstimator.StablePrefixTokens >= opus.MinCacheablePrefixTokens);

        var estimate = GenerationCostEstimator.Estimate(RelevantOnlyPlan(), opus, opus, Prices);

        var cached = Cost(opus, new TokenUsage
        {
            InputTokens = 4_000 - GenerationCostEstimator.StablePrefixTokens,
            CacheReadTokens = GenerationCostEstimator.StablePrefixTokens,
            OutputTokens = 450
        });
        Assert.Equal(10 * cached * 1.15m, estimate.RelevantCost);
        Assert.True(estimate.RelevantCost < 10 * opus.CalculateTotalCost(4_000, 450) * 1.15m);
    }

    [Theory]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("gpt-6-sol")]
    [InlineData("gpt-6.1-sol")]
    [InlineData("gpt-6-luna")]
    public void PriceWork_PrefixBelowModelMinimum_NoCacheTerm(string modelId)
    {
        var model = Catalog(modelId);
        Assert.True(GenerationCostEstimator.StablePrefixTokens < model.MinCacheablePrefixTokens);

        var estimate = GenerationCostEstimator.Estimate(RelevantOnlyPlan(), model, model, Prices);

        Assert.Equal(10 * model.CalculateTotalCost(4_000, 450) * 1.15m, estimate.RelevantCost);
    }

    [Fact]
    public void PriceWork_LegacyModelWithoutMinPrefix_NoCacheTerm()
    {
        var legacy = new AIModelConfig
        {
            DisplayName = "Legacy", InputTokenPricePerMillion = 3m, OutputTokenPricePerMillion = 15m,
            CacheReadTokenPricePerMillion = 0.3m, MinCacheablePrefixTokens = 0
        };

        var estimate = GenerationCostEstimator.Estimate(RelevantOnlyPlan(), legacy, legacy, Prices);

        Assert.Equal(10 * legacy.CalculateTotalCost(4_000, 450) * 1.15m, estimate.RelevantCost);
    }

    // Live price with the cache term, as GenerationCostEstimator prices a live call
    private static decimal L(AIModelConfig m, int input, int output)
    {
        var prefix = GenerationCostEstimator.StablePrefixTokens;
        var caches = m.MinCacheablePrefixTokens > 0 && prefix >= m.MinCacheablePrefixTokens && input > prefix;
        return caches
            ? Cost(m, new TokenUsage { InputTokens = input - prefix, CacheReadTokens = prefix, OutputTokens = output })
            : Cost(m, new TokenUsage { InputTokens = input, OutputTokens = output });
    }

    private static decimal B(AIModelConfig m, int input, int output) =>
        Cost(m, new TokenUsage { InputTokens = input, OutputTokens = output, IsBatch = true });

    // WR-A12: a batched call is the base call at the batch rate plus the retry overhead at the live rate (D-10)
    private static decimal BQ(AIModelConfig m, int input, int output) =>
        B(m, input, output) + (1.15m - 1m) * L(m, input, output);

    [Fact]
    public void PriceWork_BatchTotal_RelevantLive_NonRelevantBatch()
    {
        var opus = Catalog("claude-opus-5-5");
        var haiku = Catalog("claude-haiku-4-5-20251001");

        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), opus, haiku, Prices);

        // Amended D-07: relevant threads and their attachments run live; decoys and other non-relevant mail batch
        var batchStory = 10 * L(opus, 4_000, 450) * 1.15m + 2 * L(opus, 3_000, 1_500) * 1.15m
                         + 4 * BQ(opus, 4_000, 450);

        // Side world and voice cast never allow batching, so they are priced live
        var batchVolume = 20 * BQ(haiku, 1_500, 300) + 3 * BQ(haiku, 800, 200)
                          + L(haiku, 3_000, 1_200) * 1.15m + L(haiku, 2_500, 400) * 1.15m;

        Assert.Equal(batchStory, estimate.BatchStoryCost);
        Assert.Equal(batchVolume, estimate.BatchVolumeCost);
        Assert.Equal(estimate.BatchStoryCost + estimate.BatchVolumeCost + estimate.MediaCost, estimate.BatchTotal);
        Assert.True(estimate.BatchTotal < estimate.Total);
    }

    [Fact]
    public void PriceWork_RelevantOnlyPlan_BatchEqualsLive()
    {
        var opus = Catalog("claude-opus-5-5");
        var haiku = Catalog("claude-haiku-4-5-20251001");

        var estimate = GenerationCostEstimator.Estimate(RelevantOnlyPlan(), opus, haiku, Prices);

        Assert.Equal(estimate.Total, estimate.BatchTotal);
        Assert.Equal(estimate.StoryCost, estimate.BatchStoryCost);
    }

    [Fact]
    public void PriceWork_LiveDocIds_PricedLiveInBatchTotal()
    {
        var opus = Catalog("claude-opus-5-5");
        var haiku = Catalog("claude-haiku-4-5-20251001");

        var plan = MixedPlan();
        var daily = plan.Threads[2];
        for (var i = 0; i < 3; i++)
            daily.Messages[i].Attachments[0].DocId = $"D-{i + 1:0000}";

        var baseline = GenerationCostEstimator.PriceWork(PricedWork.ForPlan(plan, null), opus, haiku, Prices, true);

        var work = PricedWork.ForPlan(plan, null);
        work.LiveDocIds = new HashSet<string>(StringComparer.Ordinal) { "D-0001", "D-0002", "D-0003" };
        var repaired = GenerationCostEstimator.PriceWork(work, opus, haiku, Prices, true);

        Assert.Equal(baseline.BatchVolumeCost + 3 * (L(haiku, 800, 200) * 1.15m - BQ(haiku, 800, 200)),
            repaired.BatchVolumeCost);
        Assert.Equal(baseline.Total, repaired.Total);
    }

    [Fact]
    public void PriceWork_MediaSameInBothTotals_ZeroWhenUnavailable()
    {
        var opus = Catalog("claude-opus-5-5");
        var haiku = Catalog("claude-haiku-4-5-20251001");

        var withMedia = GenerationCostEstimator.Estimate(MixedPlan(), opus, haiku, Prices);
        Assert.Equal(withMedia.StoryCost + withMedia.VolumeCost + withMedia.MediaCost, withMedia.Total);
        Assert.Equal(withMedia.BatchStoryCost + withMedia.BatchVolumeCost + withMedia.MediaCost, withMedia.BatchTotal);
        Assert.True(withMedia.MediaCost > 0m);

        var noMedia = GenerationCostEstimator.Estimate(MixedPlan(), opus, haiku, Prices, mediaAvailable: false);
        Assert.Equal(0m, noMedia.MediaCost);
        Assert.Equal(noMedia.BatchStoryCost + noMedia.BatchVolumeCost, noMedia.BatchTotal);
    }

    [Fact]
    public void PriceWork_NoDiscountModels_BatchEqualsLive()
    {
        var estimate = GenerationCostEstimator.Estimate(MixedPlan(), Story, Volume, Prices);

        Assert.Equal(estimate.Total, estimate.BatchTotal);
        Assert.Equal(estimate.StoryCost, estimate.BatchStoryCost);
        Assert.Equal(estimate.VolumeCost, estimate.BatchVolumeCost);
    }

    [Fact]
    public void ForPlan_Flags()
    {
        var work = PricedWork.ForPlan(MixedPlan(), null);

        Assert.Equal(34, work.Messages.Count);
        Assert.Equal(8, work.Attachments.Count);
        Assert.True(work.IncludeSideWorld);
        Assert.True(work.IncludeVoiceCast);
    }
}
