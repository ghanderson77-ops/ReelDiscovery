using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Threads and message counts planned for one dated key fact.
/// </summary>
public class FactAllocation
{
    public string FactId { get; set; } = string.Empty;
    public int Threads { get; set; }
    public List<int> MessagesPerThread { get; set; } = new();
}

/// <summary>
/// Relevant-message budget across the dated key facts (PLAN-03, D-15).
/// </summary>
public class RelevantBudget
{
    public List<FactAllocation> Facts { get; set; } = new();
    public int TotalThreads { get; set; }
    public int TotalMessages { get; set; }

    /// <summary>
    /// Messages planned above the requested relevant count because every fact needs at least one (D-07, Pitfall 9).
    /// </summary>
    public int Shortfall { get; set; }
}

/// <summary>
/// Significance-weighted relevant budget and non-relevant category split. Pure: all sampling uses the injected Random.
/// </summary>
public static class BudgetAllocator
{
    public const double MeanRelevantDepth = 3.0;

    public static double SignificanceWeight(FactSignificance significance) => significance switch
    {
        FactSignificance.Hot => 3,
        FactSignificance.Key => 2,
        _ => 1
    };

    /// <summary>
    /// Splits <paramref name="relevantEmails"/> (callers compute it with CorpusSettings.RelevantCount, the one shared split)
    /// into threads per fact: thread counts by largest remainder over Hot 3 / Key 2 / Supporting 1 (x guidance multiplier),
    /// at least one thread per fact; depths from the Business table (+1 for Hot/Key, cap 12), then adjusted to hit the
    /// total exactly. With fewer messages than facts every fact gets one single-message thread and Shortfall is set.
    /// </summary>
    public static RelevantBudget AllocateRelevant(IReadOnlyList<KeyFact> datedFacts, int relevantEmails,
        IReadOnlyDictionary<string, double>? factMultipliers, Random random)
    {
        var budget = new RelevantBudget();
        var facts = datedFacts.Where(f => f != null).ToList();
        var n = facts.Count;
        if (n == 0)
            return budget;

        var target = Math.Max(0, relevantEmails);
        var perFact = facts.Select(_ => new List<int>()).ToList();

        if (target <= n)
        {
            foreach (var list in perFact)
                list.Add(1);
            budget.Shortfall = n - target;
            return Finish(budget, facts, perFact);
        }

        var weights = facts.Select(f => SignificanceWeight(f.Significance) * Multiplier(factMultipliers, f.Id)).ToList();
        var threadsTotal = Math.Max(n, (int)Math.Round(target / MeanRelevantDepth, MidpointRounding.AwayFromZero));
        var threadCounts = Apportion.Largest(threadsTotal, weights, 1);

        for (var i = 0; i < n; i++)
        {
            var bonus = facts[i].Significance is FactSignificance.Hot or FactSignificance.Key ? 1 : 0;
            for (var t = 0; t < threadCounts[i]; t++)
                perFact[i].Add(Math.Min(ThreadShapes.MaxThreadDepth, ThreadShapes.SampleDepth(ThreadShapeClass.Business, random) + bonus));
        }

        Adjust(facts, perFact, weights, target);
        return Finish(budget, facts, perFact);
    }

    /// <summary>
    /// Message counts per non-relevant category from the user's mix; totals are exact (D-08).
    /// </summary>
    public static Dictionary<NonRelevantCategory, int> AllocateNonRelevant(int nonRelevant, CorpusSettings settings)
    {
        var counts = Apportion.Largest(Math.Max(0, nonRelevant), new List<double>
        {
            settings.DailyBusinessPercent,
            settings.AncillaryPercent,
            settings.NewsletterPercent,
            settings.DecoyPercent
        });

        return new Dictionary<NonRelevantCategory, int>
        {
            [NonRelevantCategory.DailyBusiness] = counts[0],
            [NonRelevantCategory.Ancillary] = counts[1],
            [NonRelevantCategory.NewsletterSpamPersonal] = counts[2],
            [NonRelevantCategory.Decoy] = counts[3]
        };
    }

    /// <summary>
    /// Thread lengths summing to <paramref name="messages"/>: depths sampled from the class table until the next would
    /// overshoot, then the remainder as the last thread. Bulk = all singletons.
    /// </summary>
    public static List<int> SplitIntoThreads(int messages, ThreadShapeClass cls, Random random)
    {
        var result = new List<int>();
        if (messages <= 0)
            return result;

        if (cls == ThreadShapeClass.Bulk)
            return Enumerable.Repeat(1, messages).ToList();

        var sum = 0;
        // Each iteration adds at least one message, so this is bounded by messages
        while (sum < messages)
        {
            var depth = ThreadShapes.SampleDepth(cls, random);
            if (sum + depth > messages)
            {
                result.Add(messages - sum);
                break;
            }

            result.Add(depth);
            sum += depth;
        }

        return result;
    }

    private static double Multiplier(IReadOnlyDictionary<string, double>? multipliers, string factId)
    {
        if (multipliers == null || !multipliers.TryGetValue(factId ?? string.Empty, out var m) || double.IsNaN(m) || double.IsInfinity(m))
            return 1;
        return Math.Max(0, m);
    }

    // Adds to the shortest thread below the cap / trims from the longest above 1, first in fact-then-thread order
    private static void Adjust(List<KeyFact> facts, List<List<int>> perFact, List<double> weights, int target)
    {
        var sum = perFact.Sum(l => l.Sum());

        // Hard bound so crafted inputs can never spin (T-04-08)
        var bound = 4 * (target + sum) + 100;
        for (var iteration = 0; sum != target && iteration < bound; iteration++)
        {
            if (sum < target)
            {
                var (fi, ti) = Pick(perFact, d => d < ThreadShapes.MaxThreadDepth, shortest: true);
                if (fi >= 0)
                {
                    perFact[fi][ti]++;
                }
                else
                {
                    // Every thread is full: the fact with the most weight per thread gets another thread
                    var best = Enumerable.Range(0, facts.Count).OrderByDescending(i => weights[i] / perFact[i].Count).ThenBy(i => i).First();
                    perFact[best].Add(1);
                }

                sum++;
            }
            else
            {
                var (fi, ti) = Pick(perFact, d => d > 1, shortest: false);
                if (fi >= 0)
                {
                    perFact[fi][ti]--;
                    sum--;
                    continue;
                }

                // Every thread has one message: drop extra threads, Supporting facts first
                var drop = Enumerable.Range(0, facts.Count)
                    .Where(i => perFact[i].Count > 1)
                    .OrderByDescending(i => facts[i].Significance)
                    .ThenByDescending(i => perFact[i].Count)
                    .ThenBy(i => i)
                    .Select(i => (int?)i)
                    .FirstOrDefault();
                if (drop == null)
                    break;

                perFact[drop.Value].RemoveAt(perFact[drop.Value].Count - 1);
                sum--;
            }
        }
    }

    private static (int Fact, int Thread) Pick(List<List<int>> perFact, Func<int, bool> eligible, bool shortest)
    {
        var best = (Fact: -1, Thread: -1);
        var bestDepth = 0;
        for (var f = 0; f < perFact.Count; f++)
        {
            for (var t = 0; t < perFact[f].Count; t++)
            {
                var depth = perFact[f][t];
                if (!eligible(depth))
                    continue;
                if (best.Fact < 0 || (shortest ? depth < bestDepth : depth > bestDepth))
                {
                    best = (f, t);
                    bestDepth = depth;
                }
            }
        }

        return best;
    }

    private static RelevantBudget Finish(RelevantBudget budget, List<KeyFact> facts, List<List<int>> perFact)
    {
        for (var i = 0; i < facts.Count; i++)
        {
            budget.Facts.Add(new FactAllocation
            {
                FactId = facts[i].Id,
                Threads = perFact[i].Count,
                MessagesPerThread = perFact[i]
            });
        }

        budget.TotalThreads = budget.Facts.Sum(f => f.Threads);
        budget.TotalMessages = budget.Facts.Sum(f => f.MessagesPerThread.Sum());
        return budget;
    }
}
