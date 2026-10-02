using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class BudgetAllocatorTests
{
    private static List<KeyFact> DatedFacts() =>
        SkeletonContext.Create(CorpusTestData.RichDraft(), new CorpusSettings(), false, new Random(1)).DatedFacts;

    private static void AssertConsistent(RelevantBudget budget)
    {
        Assert.Equal(budget.Facts.Sum(f => f.Threads), budget.TotalThreads);
        Assert.Equal(budget.Facts.Sum(f => f.MessagesPerThread.Sum()), budget.TotalMessages);
        foreach (var fact in budget.Facts)
        {
            Assert.Equal(fact.Threads, fact.MessagesPerThread.Count);
            Assert.True(fact.Threads >= 1, fact.FactId);
            Assert.All(fact.MessagesPerThread, n => Assert.InRange(n, 1, ThreadShapes.MaxThreadDepth));
        }
    }

    [Fact]
    public void AllocateRelevant_ExactTotal_SignificanceOrdered()
    {
        var facts = DatedFacts();
        Assert.Equal(8, facts.Count);
        var budget = BudgetAllocator.AllocateRelevant(facts, 200, null, new Random(42));

        AssertConsistent(budget);
        Assert.Equal(200, budget.TotalMessages);
        Assert.Equal(0, budget.Shortfall);
        Assert.Equal(facts.Select(f => f.Id), budget.Facts.Select(f => f.FactId));

        double Mean(FactSignificance s)
        {
            var ids = facts.Where(f => f.Significance == s).Select(f => f.Id).ToHashSet();
            return budget.Facts.Where(f => ids.Contains(f.FactId)).Average(f => f.Threads);
        }

        Assert.True(Mean(FactSignificance.Hot) > Mean(FactSignificance.Key));
        Assert.True(Mean(FactSignificance.Key) > Mean(FactSignificance.Supporting));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(50)]
    [InlineData(333)]
    [InlineData(1_000)]
    [InlineData(3_000)]
    public void AllocateRelevant_HitsTargetForManySizes(int relevant)
    {
        for (var seed = 0; seed < 5; seed++)
        {
            var budget = BudgetAllocator.AllocateRelevant(DatedFacts(), relevant, null, new Random(seed));
            AssertConsistent(budget);
            Assert.Equal(relevant, budget.TotalMessages);
        }
    }

    [Fact]
    public void AllocateRelevant_TooFewMessages_OnePerFactWithShortfall()
    {
        var budget = BudgetAllocator.AllocateRelevant(DatedFacts(), 5, null, new Random(42));

        AssertConsistent(budget);
        Assert.All(budget.Facts, f => Assert.Equal(new[] { 1 }, f.MessagesPerThread));
        Assert.Equal(8, budget.TotalMessages);
        Assert.Equal(8, budget.TotalThreads);
        Assert.Equal(3, budget.Shortfall);
    }

    [Fact]
    public void AllocateRelevant_ExactlyOnePerFact_NoShortfall()
    {
        var budget = BudgetAllocator.AllocateRelevant(DatedFacts(), 8, null, new Random(42));

        Assert.Equal(8, budget.TotalMessages);
        Assert.Equal(0, budget.Shortfall);
    }

    [Fact]
    public void AllocateRelevant_MultiplierIncreasesThreads()
    {
        var facts = DatedFacts();
        var plain = BudgetAllocator.AllocateRelevant(facts, 200, new Dictionary<string, double> { ["F-003"] = 1.0 }, new Random(42));
        var boosted = BudgetAllocator.AllocateRelevant(facts, 200, new Dictionary<string, double> { ["F-003"] = 2.0 }, new Random(42));

        Assert.True(boosted.Facts.Single(f => f.FactId == "F-003").Threads > plain.Facts.Single(f => f.FactId == "F-003").Threads);
        Assert.Equal(200, boosted.TotalMessages);
    }

    [Fact]
    public void AllocateRelevant_LargeShare_SplitsIntoMoreThreads()
    {
        var facts = DatedFacts().Take(1).ToList();
        var budget = BudgetAllocator.AllocateRelevant(facts, 100, null, new Random(42));

        AssertConsistent(budget);
        Assert.Equal(100, budget.TotalMessages);
        Assert.True(budget.Facts[0].Threads >= 9);
        Assert.All(budget.Facts[0].MessagesPerThread, n => Assert.True(n <= ThreadShapes.MaxThreadDepth));
    }

    [Fact]
    public void AllocateRelevant_NoFacts_Empty()
    {
        var budget = BudgetAllocator.AllocateRelevant(new List<KeyFact>(), 100, null, new Random(42));

        Assert.Empty(budget.Facts);
        Assert.Equal(0, budget.TotalMessages);
    }

    [Fact]
    public void AllocateNonRelevant_SplitsByMix()
    {
        var settings = new CorpusSettings { DailyBusinessPercent = 50, AncillaryPercent = 20, NewsletterPercent = 20, DecoyPercent = 10 };
        var split = BudgetAllocator.AllocateNonRelevant(800, settings);

        Assert.Equal(400, split[NonRelevantCategory.DailyBusiness]);
        Assert.Equal(160, split[NonRelevantCategory.Ancillary]);
        Assert.Equal(160, split[NonRelevantCategory.NewsletterSpamPersonal]);
        Assert.Equal(80, split[NonRelevantCategory.Decoy]);
        Assert.Equal(800, split.Values.Sum());
    }

    [Fact]
    public void AllocateNonRelevant_UnevenMix_SumsExactly()
    {
        var settings = new CorpusSettings { DailyBusinessPercent = 33, AncillaryPercent = 33, NewsletterPercent = 33, DecoyPercent = 1 };

        Assert.Equal(800, BudgetAllocator.AllocateNonRelevant(800, settings).Values.Sum());
        Assert.Equal(37, BudgetAllocator.AllocateNonRelevant(37, settings).Values.Sum());
    }

    [Fact]
    public void SplitIntoThreads_SumsAndCaps()
    {
        var random = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            var threads = BudgetAllocator.SplitIntoThreads(37, ThreadShapeClass.Business, random);
            Assert.Equal(37, threads.Sum());
            Assert.All(threads, n => Assert.InRange(n, 1, ThreadShapes.MaxThreadDepth));
        }

        var bulk = BudgetAllocator.SplitIntoThreads(25, ThreadShapeClass.Bulk, random);
        Assert.Equal(25, bulk.Count);
        Assert.All(bulk, n => Assert.Equal(1, n));
        Assert.Empty(BudgetAllocator.SplitIntoThreads(0, ThreadShapeClass.Business, random));
    }

    [Fact]
    public void SignificanceWeight_HotKeySupporting()
    {
        Assert.Equal(3, BudgetAllocator.SignificanceWeight(FactSignificance.Hot));
        Assert.Equal(2, BudgetAllocator.SignificanceWeight(FactSignificance.Key));
        Assert.Equal(1, BudgetAllocator.SignificanceWeight(FactSignificance.Supporting));
    }
}
