using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Rough cost to write a planned corpus on the Story model, with a breakdown (D-21). Image and speech media are billed
/// separately by OpenAI and are not included.
/// </summary>
public class CostEstimate
{
    public decimal RelevantCost { get; set; }
    public decimal OtherCost { get; set; }
    public decimal AttachmentCost { get; set; }
    public decimal Total { get; set; }
    public string ModelName { get; set; } = string.Empty;
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
}
