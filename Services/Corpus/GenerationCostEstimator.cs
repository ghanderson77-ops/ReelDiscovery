using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;

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

    // Live total: text with the expected prompt-cache effect, plus media
    public decimal Total { get; set; }

    // Story-model text in batch mode: relevant threads and their attachments at live prices (amended D-07), decoys at batch prices with no cache reads
    public decimal BatchStoryCost { get; set; }

    // Volume-model text in batch mode: non-relevant mail at batch prices; side-world, voice-cast and repairs live
    public decimal BatchVolumeCost { get; set; }

    // Batch total: BatchStoryCost + BatchVolumeCost + MediaCost (media is always priced live)
    public decimal BatchTotal { get; set; }

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

    /// <summary>Attachment doc ids that run live even in batch mode (repairs, revision 1 W9); priced live in the batch total.</summary>
    public HashSet<string> LiveDocIds { get; set; } = new(StringComparer.Ordinal);

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

    // [ASSUMED] byte-stable system prompt (~330 tokens) + JSON schema (~270) per evidence call (research A3); re-measure from live usage in 07-22
    public const int StablePrefixTokens = 600;

    // [ASSUMED] discovery heuristics (research A3); recalibrate on the first live run
    // One deposition chunk call: guide slice + witness facts + exhibits + prior Q/A tail
    public const int DepositionChunkInput = 7_000;

    // ~4,500 output tokens per 8-page chunk
    public const int DepositionOutputPerPage = 560;

    // One written-discovery call: guide slice + facts + slots (up to 25 per call)
    public const int WrittenSetInput = 6_000;
    public const int OutputPerRequest = 150;

    /// <summary>
    /// Discovery documents (Phase 8, D-28): Story tier, live calls; same Price path as the corpus estimate.
    /// A null model or no work gives zero.
    /// </summary>
    public static decimal PriceDiscoveryDeposition(int chunkCount, int testimonyPages, AIModelConfig? storyModel)
    {
        if (storyModel == null || chunkCount <= 0 || testimonyPages <= 0)
            return 0m;
        return Price(storyModel, chunkCount * DepositionChunkInput, testimonyPages * DepositionOutputPerPage, false) * QaOverhead;
    }

    /// <summary>
    /// Discovery documents (Phase 8, D-28): Story tier, live calls; same Price path as the corpus estimate.
    /// A null model or no work gives zero.
    /// </summary>
    public static decimal PriceDiscoveryWrittenSet(int callCount, int requestCount, AIModelConfig? storyModel)
    {
        if (storyModel == null || callCount <= 0 || requestCount <= 0)
            return 0m;
        return Price(storyModel, callCount * WrittenSetInput, requestCount * OutputPerRequest, false) * QaOverhead;
    }

    // Phase 5 Review Plan estimate; D-29 overload below splits Story / Volume / media
    /// <summary>
    /// count x live price(typical in, typical out) x QaOverhead per group; a null model gives a zero estimate.
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
            RelevantCost = relevant * Price(storyModel, RelevantInputPerMessage, RelevantOutputPerMessage, false) * QaOverhead,
            OtherCost = other * Price(storyModel, OtherInputPerMessage, OtherOutputPerMessage, false) * QaOverhead,
            AttachmentCost = (documents * Price(storyModel, DocumentInput, DocumentOutput, false)
                              + small * Price(storyModel, SmallAttachmentInput, SmallAttachmentOutput, false)) * QaOverhead
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
    /// parent's tier. Unavailable media prices images, voicemail and the voice-cast call at 0. D-17: every text line is
    /// priced live and in batch mode; relevant work is live in both totals (amended D-07).
    /// </summary>
    public static CostEstimate PriceWork(PricedWork work, AIModelConfig? storyModel, AIModelConfig? volumeModel,
        MediaPrices prices, bool mediaAvailable)
    {
        var story = storyModel ?? volumeModel;
        var volume = volumeModel ?? storyModel;
        if (story == null || volume == null || work == null)
            return new CostEstimate();

        prices ??= MediaPrices.Defaults();

        // Amended D-07: relevant threads run live even in batch mode, so only non-relevant work is batch-eligible
        var storyMessages = 0;
        var storyMessagesBatch = 0;
        var volumeMessages = 0;
        var volumeMessagesBatch = 0;
        foreach (var (thread, _) in (work.Messages ?? new()).Where(m => m.Thread != null && m.Message != null))
        {
            var batchable = thread.Relevance != ThreadRelevance.Relevant;
            if (IsStoryTier(thread))
            {
                storyMessages++;
                if (batchable)
                    storyMessagesBatch++;
            }
            else
            {
                volumeMessages++;
                if (batchable)
                    volumeMessagesBatch++;
            }
        }

        var liveDocIds = work.LiveDocIds ?? new HashSet<string>(StringComparer.Ordinal);
        var storyDocuments = 0;
        var storyDocumentsBatch = 0;
        var storySmall = 0;
        var storySmallBatch = 0;
        var volumeSmall = 0;
        var volumeSmallBatch = 0;
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

            // Repairs run live (revision 1 W9)
            var batchable = thread.Relevance != ThreadRelevance.Relevant && !liveDocIds.Contains(attachment.DocId ?? string.Empty);
            var office = attachment.Kind is AttachmentType.Word or AttachmentType.Excel or AttachmentType.PowerPoint;
            if (!IsStoryTier(thread))
            {
                volumeSmall++;
                if (batchable)
                    volumeSmallBatch++;
            }
            else if (office && IsRichAttachment(thread, parent, work.Guide))
            {
                storyDocuments++;
                if (batchable)
                    storyDocumentsBatch++;
            }
            else
            {
                storySmall++;
                if (batchable)
                    storySmallBatch++;
            }
        }

        // D-12: filler has V1/V5 checks only, so no length retries to price
        var estimate = new CostEstimate
        {
            ModelName = story.DisplayName ?? string.Empty,
            VolumeModelName = volume.DisplayName ?? string.Empty
        };

        // D-17 batch side: count x (live or batch) price x QaOverhead, split by whether the call can be batched. WR-A12: the
        // overhead share of a batched call models retries, which D-10 always runs live, so only the base call gets the
        // batch rate (the batch total still assumes no cache savings, UI-SPEC S1)
        decimal Split(AIModelConfig model, int count, int batchCount, int input, int output) =>
            (count - batchCount) * Price(model, input, output, false) * QaOverhead
            + batchCount * (Price(model, input, output, true) + (QaOverhead - 1m) * Price(model, input, output, false));

        estimate.RelevantCost = storyMessages * Price(story, RelevantInputPerMessage, RelevantOutputPerMessage, false) * QaOverhead;
        estimate.OtherCost = volumeMessages * Price(volume, OtherInputPerMessage, OtherOutputPerMessage, false) * QaOverhead;

        var storyAttachments = storyDocuments * Price(story, DocumentInput, DocumentOutput, false) * QaOverhead
                               + storySmall * Price(story, SmallAttachmentInput, SmallAttachmentOutput, false) * QaOverhead;
        var volumeAttachments = volumeSmall * Price(volume, SmallAttachmentInput, SmallAttachmentOutput, false) * QaOverhead;
        estimate.AttachmentCost = storyAttachments + volumeAttachments;

        if (work.IncludeSideWorld)
            estimate.SideWorldCost = Price(volume, SideWorldInputTokens, SideWorldOutputTokens, false) * QaOverhead;

        var voiceCast = mediaAvailable && work.IncludeVoiceCast;
        if (mediaAvailable)
        {
            // Media is priced per unit and always live; QA overhead models text retries (review 06-15)
            estimate.MediaCost = images * prices.ImageCost() + voicemails * prices.SpeechCost(VoicemailCharacters);
            if (voiceCast)
                estimate.VoiceCastCost = Price(volume, VoiceCastInputTokens, VoiceCastOutputTokens, false) * QaOverhead;
        }

        estimate.StoryCost = estimate.RelevantCost + storyAttachments;
        estimate.VolumeCost = estimate.OtherCost + volumeAttachments + estimate.SideWorldCost + estimate.VoiceCastCost;
        estimate.Total = estimate.StoryCost + estimate.VolumeCost + estimate.MediaCost;

        estimate.BatchStoryCost = Split(story, storyMessages, storyMessagesBatch, RelevantInputPerMessage, RelevantOutputPerMessage)
                                  + Split(story, storyDocuments, storyDocumentsBatch, DocumentInput, DocumentOutput)
                                  + Split(story, storySmall, storySmallBatch, SmallAttachmentInput, SmallAttachmentOutput);

        // Side-world and voice-cast requests never allow batching, so they are priced live
        estimate.BatchVolumeCost = Split(volume, volumeMessages, volumeMessagesBatch, OtherInputPerMessage, OtherOutputPerMessage)
                                   + Split(volume, volumeSmall, volumeSmallBatch, SmallAttachmentInput, SmallAttachmentOutput)
                                   + estimate.SideWorldCost + estimate.VoiceCastCost;
        estimate.BatchTotal = estimate.BatchStoryCost + estimate.BatchVolumeCost + estimate.MediaCost;
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

    /// <summary>
    /// One call's text price through AIModelConfig.CalculateCost. Live calls read StablePrefixTokens from the prompt cache
    /// only when the prefix meets the model's minimum (Pitfall 7; a minimum of 0 means the model is not cached); batch
    /// calls assume no cache reads and take the batch discount.
    /// </summary>
    private static decimal Price(AIModelConfig model, int inputTokens, int outputTokens, bool batch)
    {
        // Cache writes are amortized: one write per 5-minute window across thousands of calls is ignored (Pitfall 7: never claim savings the provider cannot give)
        var caches = !batch && model.MinCacheablePrefixTokens > 0 && StablePrefixTokens >= model.MinCacheablePrefixTokens
                     && inputTokens > StablePrefixTokens;
        var usage = caches
            ? new TokenUsage { InputTokens = inputTokens - StablePrefixTokens, CacheReadTokens = StablePrefixTokens, OutputTokens = outputTokens }
            : new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens, IsBatch = batch };
        return model.CalculateCost(usage).TotalUsd;
    }

    // Story tier: relevant threads and decoys (D-29); everything else is Volume
    private static bool IsStoryTier(PlannedThread thread) =>
        thread.Relevance == ThreadRelevance.Relevant || thread.Category == NonRelevantCategory.Decoy;
}
