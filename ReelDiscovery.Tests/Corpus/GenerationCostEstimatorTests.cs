using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
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
