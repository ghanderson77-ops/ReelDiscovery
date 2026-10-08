using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Probes the case folder for a usable generated corpus (D-21, D-22): the plan case.json references, a readable and
/// unchanged journal, and at least one written relevant email. Read-only; never throws.
/// </summary>
public static class CorpusDetector
{
    /// <summary>
    /// Exhibit candidates (written relevant emails) and the merged narrative ledger, or a "none" snapshot with a reason.
    /// </summary>
    public static DiscoveryCorpus Detect(CaseDraft draft)
    {
        if (draft?.Guide == null || string.IsNullOrEmpty(draft.FolderPath))
            return DiscoveryCorpus.None("no case folder or guide");

        // Detection is an enrichment; a broken corpus must never block discovery documents (D-05)
        try
        {
            var load = CorpusPlanStore.EnsureLoaded(draft);
            var plan = load.Plan;
            if (plan == null)
                return DiscoveryCorpus.None(load.Reason.Length > 0 ? load.Reason : "No corpus plan.");

            var replay = GenerationJournal.Replay(draft.FolderPath, plan);
            if (!replay.Exists)
                return DiscoveryCorpus.None("No emails have been generated yet.");
            if (!replay.Readable)
                return DiscoveryCorpus.None(replay.Reason);
            if (replay.PlanChanged)
                return DiscoveryCorpus.None("The saved run belongs to a different corpus plan.");

            var written = new HashSet<string>(BlindReviewer.GeneratedEmailDocIds(replay), StringComparer.Ordinal);
            var candidates = new List<ExhibitCandidate>();
            foreach (var thread in plan.Threads)
            {
                if (thread == null || thread.Relevance != ThreadRelevance.Relevant)
                    continue;

                foreach (var message in thread.Messages.OrderBy(m => m.Seq))
                {
                    if (message == null || string.IsNullOrEmpty(message.DocId) || !written.Contains(message.DocId))
                        continue;

                    var participants = new List<string> { message.FromId };
                    participants.AddRange(message.ToIds);
                    participants.AddRange(message.CcIds);
                    candidates.Add(new ExhibitCandidate
                    {
                        DocId = message.DocId,
                        ThreadId = thread.Id,
                        Subject = thread.Subject,
                        Date = message.SentAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ParticipantIds = participants.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal).ToList(),
                        IsHot = message.IsHot,
                        FactIds = (message.FactIds.Count > 0 ? message.FactIds : thread.FactIds).ToList()
                    });
                }
            }

            if (candidates.Count == 0)
                return DiscoveryCorpus.None("No relevant emails have been written yet.");

            // Same chain Case Summary uses to re-project the ledger (D-22)
            var runState = GenerationRunState.From(plan, replay);
            var merged = LedgerMerger.Build(plan, draft.Guide, WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan)), runState.DeltasByThread);

            return new DiscoveryCorpus
            {
                Usable = true,
                PlanId = plan.PlanId,
                Candidates = candidates,
                Ledger = merged.Entries
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DiscoveryCorpus.None($"The corpus could not be read: {ex.Message}");
        }
    }
}
