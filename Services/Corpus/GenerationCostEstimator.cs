using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Rough cost to write a planned corpus, with a breakdown (D-21). The Phase 5 overload prices everything on the Story
/// model; the D-29 overload splits Story, Volume and per-unit media.
/// </summary>
public class CostEstimate
{
    // Story-tier messages (relevant threads and decoys)
    public decimal RelevantCost { get; set; }

    // Volume-tier messages (daily business, ancillary, bulk)
    public decimal OtherCost { get; set; }

    // Office and invite attachments on both tiers
    public decimal AttachmentCost { get; set; }

    // Per-model totals including attachments; VolumeCost also holds the side-world and voice-cast calls
    public decimal StoryCost { get; set; }
    public decimal VolumeCost { get; set; }

    // Images and voicemail speech, per unit
    public decimal MediaCost { get; set; }

    // D-28: one Volume call picks a tts-1 voice per cast member
    public decimal VoiceCastCost { get; set; }

    // D-21: one Volume call invents the side projects before filler renders
    public decimal SideWorldCost { get; set; }

    public decimal Total { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string VolumeModelName { get; set; } = string.Empty;
}

/// <summary>
/// The work a price covers: messages and attachments with their threads, and whether the side-world and voice-cast
/// calls are still to run. The plan estimate and the remaining-cost line both price through this (review 06-15).
/// </summary>
public class PricedWork
{
    public List<(PlannedThread Thread, PlannedMessage Message)> Messages { get; set; } = new();
    public List<(PlannedThread Thread, PlannedMessage Parent, PlannedAttachment Attachment)> Attachments { get; set; } = new();
    public bool IncludeSideWorld { get; set; }
    public bool IncludeVoiceCast { get; set; }
    public CaseGuide? Guide { get; set; }

    /// <summary>
    /// Every planned message and attachment; the side world when any non-relevant thread has messages, the voice cast
    /// when any voicemail is planned.
    /// </summary>
    public static PricedWork ForPlan(CorpusPlan plan, CaseGuide? guide)
    {
        var work = new PricedWork { Guide = guide };
        foreach (var thread in (plan?.Threads ?? new List<PlannedThread>()).Where(t => t != null))
        {
            foreach (var message in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null))
            {
                work.Messages.Add((thread, message));
                if (thread.Relevance != ThreadRelevance.Relevant)
                    work.IncludeSideWorld = true;

                foreach (var attachment in (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null))
                {
                    work.Attachments.Add((thread, message, attachment));
                    if (attachment.Kind == AttachmentType.Voicemail)
                        work.IncludeVoiceCast = true;
                }
            }
        }

        return work;
    }
}

public static class GenerationCostEstimator
{
    // [ASSUMED] heuristic (RESEARCH A9); recalibrate from real usage in Phases 5-7
    // Relevant email: guide slice + ledger + plan, amortized per message
    public const int RelevantInputPerMessage = 4_000;
    public const int RelevantOutputPerMessage = 450;
    public const int OtherInputPerMessage = 1_500;
    public const int OtherOutputPerMessage = 300;

    // Word/Excel/PowerPoint attachment
    public const int DocumentInput = 3_000;
    public const int DocumentOutput = 1_500;

    // Calendar invite, voicemail script, image prompt
    public const int SmallAttachmentInput = 800;
    public const int SmallAttachmentOutput = 200;

    // Ledger extraction and validation retries
    public const decimal QaOverhead = 1.15m;

    // Speech characters of one voicemail script
    public const int VoicemailCharacters = 400;

    // D-28: one Volume call picks a tts-1 voice per cast member
    public const int VoiceCastInputTokens = 2_500;
    public const int VoiceCastOutputTokens = 400;

    // D-21: one Volume call invents the side projects per organization
    public const int SideWorldInputTokens = 3_000;
    public const int SideWorldOutputTokens = 1_200;

    // Phase 5 Review Plan estimate; D-29 overload below splits Story / Volume / media
    /// <summary>
    /// count x model.CalculateTotalCost(typical in, typical out) x QaOverhead per group; a null model gives a zero estimate.
    /// </summary>
    public static CostEstimate Estimate(CorpusPlan plan, AIModelConfig? storyModel)
    {
        if (storyModel == null)
            return new CostEstimate();

        var relevant = 0;
        var other = 0;
        var documents = 0;
        var small = 0;
        foreach (var thread in (plan?.Threads ?? new List<PlannedThread>()).Where(t => t != null))
        {
            foreach (var message in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null))
            {
                if (thread.Relevance == ThreadRelevance.Relevant)
                    relevant++;
                else
                    other++;

                foreach (var attachment in (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null))
                {
                    if (attachment.Kind is AttachmentType.Word or AttachmentType.Excel or AttachmentType.PowerPoint)
                        documents++;
                    else
                        small++;
                }
            }
        }

        var estimate = new CostEstimate
        {
            ModelName = storyModel.DisplayName ?? string.Empty,
            RelevantCost = relevant * storyModel.CalculateTotalCost(RelevantInputPerMessage, RelevantOutputPerMessage) * QaOverhead,
            OtherCost = other * storyModel.CalculateTotalCost(OtherInputPerMessage, OtherOutputPerMessage) * QaOverhead,
            AttachmentCost = (documents * storyModel.CalculateTotalCost(DocumentInput, DocumentOutput)
                              + small * storyModel.CalculateTotalCost(SmallAttachmentInput, SmallAttachmentOutput)) * QaOverhead
        };
        estimate.Total = estimate.RelevantCost + estimate.OtherCost + estimate.AttachmentCost;
        return estimate;
    }

    /// <summary>
    /// D-29 estimate: relevant threads and decoys on the Story model, other non-relevant mail on the Volume model, images
    /// and voicemail per unit from media prices. A null Volume model prices Volume work on Story (and the reverse); both
    /// null gives a zero estimate.
    /// </summary>
    public static CostEstimate Estimate(CorpusPlan plan, AIModelConfig? storyModel, AIModelConfig? volumeModel, MediaPrices prices, bool mediaAvailable = true, CaseGuide? guide = null) =>
        PriceWork(PricedWork.ForPlan(plan, guide), storyModel, volumeModel, prices, mediaAvailable);

    /// <summary>
    /// The single pricing path for the Ready card and the remaining-cost line (review 06-15). Rich Office attachments
    /// (IsRichAttachment) use document tokens; other Office attachments and invites use small-attachment tokens on their
    /// parent's tier. Unavailable media prices images, voicemail and the voice-cast call at 0.
    /// </summary>
    public static CostEstimate PriceWork(PricedWork work, AIModelConfig? storyModel, AIModelConfig? volumeModel,
        MediaPrices prices, bool mediaAvailable)
    {
        var story = storyModel ?? volumeModel;
        var volume = volumeModel ?? storyModel;
        if (story == null || volume == null || work == null)
            return new CostEstimate();

        prices ??= MediaPrices.Defaults();

        var storyMessages = 0;
        var volumeMessages = 0;
        foreach (var (thread, _) in (work.Messages ?? new()).Where(m => m.Thread != null && m.Message != null))
        {
            if (IsStoryTier(thread))
                storyMessages++;
            else
                volumeMessages++;
        }

        var storyDocuments = 0;
        var storySmall = 0;
        var volumeSmall = 0;
        var images = 0;
        var voicemails = 0;
        foreach (var (thread, parent, attachment) in (work.Attachments ?? new())
                     .Where(a => a.Thread != null && a.Parent != null && a.Attachment != null))
        {
            if (attachment.Kind == AttachmentType.Image)
            {
                images++;
                continue;
            }

            if (attachment.Kind == AttachmentType.Voicemail)
            {
                voicemails++;
                continue;
            }

            var office = attachment.Kind is AttachmentType.Word or AttachmentType.Excel or AttachmentType.PowerPoint;
            if (!IsStoryTier(thread))
                volumeSmall++;
            else if (office && IsRichAttachment(thread, parent, work.Guide))
                storyDocuments++;
            else
                storySmall++;
        }

        // D-12: filler has V1/V5 checks only, so no length retries to price
        var estimate = new CostEstimate
        {
            ModelName = story.DisplayName ?? string.Empty,
            VolumeModelName = volume.DisplayName ?? string.Empty,
            RelevantCost = storyMessages * story.CalculateTotalCost(RelevantInputPerMessage, RelevantOutputPerMessage) * QaOverhead,
            OtherCost = volumeMessages * volume.CalculateTotalCost(OtherInputPerMessage, OtherOutputPerMessage) * QaOverhead
        };

        var storyAttachments = storyDocuments * story.CalculateTotalCost(DocumentInput, DocumentOutput) * QaOverhead
                               + storySmall * story.CalculateTotalCost(SmallAttachmentInput, SmallAttachmentOutput) * QaOverhead;
        var volumeAttachments = volumeSmall * volume.CalculateTotalCost(SmallAttachmentInput, SmallAttachmentOutput) * QaOverhead;
        estimate.AttachmentCost = storyAttachments + volumeAttachments;

        if (work.IncludeSideWorld)
            estimate.SideWorldCost = volume.CalculateTotalCost(SideWorldInputTokens, SideWorldOutputTokens) * QaOverhead;

        if (mediaAvailable)
        {
            // Media is priced per unit; QA overhead models text retries (review 06-15)
            estimate.MediaCost = images * prices.ImageCost() + voicemails * prices.SpeechCost(VoicemailCharacters);
            if (work.IncludeVoiceCast)
                estimate.VoiceCastCost = volume.CalculateTotalCost(VoiceCastInputTokens, VoiceCastOutputTokens) * QaOverhead;
        }

        estimate.StoryCost = estimate.RelevantCost + storyAttachments;
        estimate.VolumeCost = estimate.OtherCost + volumeAttachments + estimate.SideWorldCost + estimate.VoiceCastCost;
        estimate.Total = estimate.StoryCost + estimate.VolumeCost + estimate.MediaCost;
        return estimate;
    }

    /// <summary>
    /// D-15 richness; AttachmentPrompts.IsRich delegates here so prompts and estimates agree. True for a relevant thread
    /// whose parent message is hot, or whose parent or thread cites a Hot or Key fact in the guide.
    /// </summary>
    public static bool IsRichAttachment(PlannedThread thread, PlannedMessage parent, CaseGuide? guide)
    {
        if (thread == null || parent == null || thread.Relevance != ThreadRelevance.Relevant)
            return false;

        if (parent.IsHot)
            return true;

        if (guide == null)
            return false;

        return (parent.FactIds ?? new List<string>())
            .Concat(thread.FactIds ?? new List<string>())
            .Select(id => guide.FindFact(id))
            .Any(f => f != null && f.Significance is FactSignificance.Hot or FactSignificance.Key);
    }

    // Story tier: relevant threads and decoys (D-29); everything else is Volume
    private static bool IsStoryTier(PlannedThread thread) =>
        thread.Relevance == ThreadRelevance.Relevant || thread.Category == NonRelevantCategory.Decoy;
}
