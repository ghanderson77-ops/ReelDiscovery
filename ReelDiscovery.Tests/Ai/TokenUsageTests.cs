using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests.Ai;

public class TokenUsageTests
{
    private static AIModelConfig Model(string id) =>
        AIModelConfig.GetDefaultModels().Single(m => m.ModelId == id);

    private static TokenUsage Million(bool isBatch = false) => new()
    {
        InputTokens = 1_000_000,
        CacheWriteTokens = 1_000_000,
        CacheReadTokens = 1_000_000,
        OutputTokens = 1_000_000,
        IsBatch = isBatch
    };

    [Fact]
    public void CalculateCost_Opus_PricesEveryCategory()
    {
        var cost = Model("claude-opus-5-5").CalculateCost(Million());

        Assert.Equal(4.00m, cost.InputUsd);
        Assert.Equal(5.00m, cost.CacheWriteUsd);
        Assert.Equal(0.20m, cost.CacheReadUsd);
        Assert.Equal(20.00m, cost.OutputUsd);
        Assert.Equal(0m, cost.BatchSavingsUsd);
        Assert.Equal(29.20m, cost.TotalUsd);
    }

    [Fact]
    public void CalculateCost_Batch_AppliesDiscount()
    {
        var cost = Model("claude-opus-5-5").CalculateCost(Million(isBatch: true));

        Assert.Equal(14.60m, cost.BatchSavingsUsd);
        Assert.Equal(14.60m, cost.TotalUsd);
        // Components stay pre-discount
        Assert.Equal(4.00m, cost.InputUsd);
    }

    [Fact]
    public void CalculateCost_Luna_InputOnly()
    {
        var cost = Model("gpt-6-luna").CalculateCost(new TokenUsage { InputTokens = 2_000_000 });

        Assert.Equal(0.20m, cost.InputUsd);
        Assert.Equal(0.20m, cost.TotalUsd);
    }

    [Fact]
    public void CalculateCost_DiscountAbove100_IsClamped()
    {
        var model = Model("claude-opus-5-5");
        model.BatchDiscountPercent = 150m;

        var cost = model.CalculateCost(Million(isBatch: true));

        Assert.Equal(0m, cost.TotalUsd);
    }

    [Fact]
    public void CalculateCost_NegativeDiscount_IsClamped()
    {
        var model = Model("claude-opus-5-5");
        model.BatchDiscountPercent = -20m;

        var cost = model.CalculateCost(Million(isBatch: true));

        Assert.Equal(0m, cost.BatchSavingsUsd);
        Assert.Equal(29.20m, cost.TotalUsd);
    }

    [Fact]
    public void CalculateCost_NegativeTokens_AreClampedAndUsageUnchanged()
    {
        var usage = new TokenUsage { InputTokens = -500, CacheWriteTokens = -1, CacheReadTokens = -2, OutputTokens = -3 };

        var cost = Model("claude-opus-5-5").CalculateCost(usage);

        Assert.Equal(0m, cost.TotalUsd);
        // CalculateCost prices a normalized clone; the caller's object is untouched
        Assert.Equal(-500, usage.InputTokens);
    }

    [Fact]
    public void CalculateCost_NullUsage_IsZero()
    {
        var cost = Model("claude-opus-5-5").CalculateCost(null!);

        Assert.Equal(0m, cost.TotalUsd);
    }

    [Fact]
    public void Normalize_ClampsNegativesToZero()
    {
        var usage = new TokenUsage { InputTokens = -1, CacheWriteTokens = -2, CacheReadTokens = 7, OutputTokens = -4 };

        usage.Normalize();

        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.CacheWriteTokens);
        Assert.Equal(7, usage.CacheReadTokens);
        Assert.Equal(0, usage.OutputTokens);
    }

    [Fact]
    public void TokenUsage_Add_SumsTokensAndKeepsIsBatch()
    {
        var total = new TokenUsage { InputTokens = 1, CacheWriteTokens = 2, CacheReadTokens = 3, OutputTokens = 4 };

        total.Add(new TokenUsage { InputTokens = 10, CacheWriteTokens = 20, CacheReadTokens = 30, OutputTokens = 40, IsBatch = true });
        total.Add(null);

        Assert.Equal(11, total.InputTokens);
        Assert.Equal(22, total.CacheWriteTokens);
        Assert.Equal(33, total.CacheReadTokens);
        Assert.Equal(44, total.OutputTokens);
        Assert.Equal(66, total.TotalInputTokens);
        Assert.False(total.IsBatch);
    }

    [Fact]
    public void TokenUsage_Clone_CopiesEveryField()
    {
        var usage = new TokenUsage { InputTokens = 1, CacheWriteTokens = 2, CacheReadTokens = 3, OutputTokens = 4, IsBatch = true };

        var clone = usage.Clone();
        usage.InputTokens = 99;

        Assert.Equal(1, clone.InputTokens);
        Assert.Equal(2, clone.CacheWriteTokens);
        Assert.Equal(3, clone.CacheReadTokens);
        Assert.Equal(4, clone.OutputTokens);
        Assert.True(clone.IsBatch);
    }

    [Fact]
    public void CostBreakdown_Add_SumsEveryField()
    {
        var total = new CostBreakdown { InputUsd = 1m, CacheWriteUsd = 2m, CacheReadUsd = 3m, OutputUsd = 4m, BatchSavingsUsd = 5m };

        total.Add(new CostBreakdown { InputUsd = 10m, CacheWriteUsd = 20m, CacheReadUsd = 30m, OutputUsd = 40m, BatchSavingsUsd = 50m });
        total.Add(null);

        Assert.Equal(11m, total.InputUsd);
        Assert.Equal(22m, total.CacheWriteUsd);
        Assert.Equal(33m, total.CacheReadUsd);
        Assert.Equal(44m, total.OutputUsd);
        Assert.Equal(55m, total.BatchSavingsUsd);
        Assert.Equal(55m, total.TotalUsd);
    }

    [Fact]
    public void CostBreakdown_Clone_IsIndependent()
    {
        var cost = new CostBreakdown { InputUsd = 1m, CacheWriteUsd = 2m, CacheReadUsd = 3m, OutputUsd = 4m, BatchSavingsUsd = 5m };

        var clone = cost.Clone();
        cost.InputUsd = 100m;

        Assert.Equal(1m, clone.InputUsd);
        Assert.Equal(5m, clone.BatchSavingsUsd);
        Assert.Equal(5m, clone.TotalUsd);
    }

    [Fact]
    public void CalculateTotalCost_Unchanged()
    {
        var opus = Model("claude-opus-5-5");

        Assert.Equal(1_000 * 4.00m / 1_000_000m + 500 * 20.00m / 1_000_000m, opus.CalculateTotalCost(1_000, 500));
        Assert.Equal(24.00m, opus.CalculateTotalCost(1_000_000, 1_000_000));
    }
}
