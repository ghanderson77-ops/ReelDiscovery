using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Models;

public class TokenUsageTracker
{
    private readonly object _lock = new();
    private readonly List<TokenUsageEntry> _entries = new();

    // All input tokens billed (uncached + cache write + cache read)
    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }

    // Cache-write and cache-read input tokens (already included in TotalInputTokens)
    public int TotalCacheWriteTokens { get; private set; }
    public int TotalCacheReadTokens { get; private set; }

    // The one real session total: text TotalUsd (after batch savings) + media (D-16)
    public decimal TotalCost { get; private set; }

    // Pre-discount text dollars per category for the header tooltip (D-16)
    public decimal TotalInputCost { get; private set; }
    public decimal TotalCacheWriteCost { get; private set; }
    public decimal TotalCacheReadCost { get; private set; }
    public decimal TotalOutputCost { get; private set; }

    // Dollars the batch discount took off (already subtracted from TotalCost)
    public decimal TotalBatchSavings { get; private set; }

    // Per-unit priced image/speech dollars (included in TotalCost)
    public decimal TotalMediaCost { get; private set; }

    public IReadOnlyList<TokenUsageEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    public void RecordUsage(string operation, AIModelConfig model, int inputTokens, int outputTokens)
    {
        var usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens };
        RecordUsage(operation, model, usage, model.CalculateCost(usage));
    }

    /// <summary>
    /// Records one billed text call with its normalized usage and the cost priced by
    /// AIModelConfig.CalculateCost (PROV-07).
    /// </summary>
    public void RecordUsage(string operation, AIModelConfig model, TokenUsage usage, CostBreakdown cost)
    {
        ArgumentNullException.ThrowIfNull(model);
        usage ??= new TokenUsage();
        cost ??= new CostBreakdown();

        lock (_lock)
        {
            TotalInputTokens += usage.TotalInputTokens;
            TotalOutputTokens += usage.OutputTokens;
            TotalCacheWriteTokens += usage.CacheWriteTokens;
            TotalCacheReadTokens += usage.CacheReadTokens;
            TotalInputCost += cost.InputUsd;
            TotalCacheWriteCost += cost.CacheWriteUsd;
            TotalCacheReadCost += cost.CacheReadUsd;
            TotalOutputCost += cost.OutputUsd;
            TotalBatchSavings += cost.BatchSavingsUsd;
            TotalCost += cost.TotalUsd;

            _entries.Add(new TokenUsageEntry
            {
                Timestamp = DateTime.Now,
                Operation = operation,
                ModelId = model.ModelId,
                Provider = model.Provider.ToString(),
                InputTokens = usage.TotalInputTokens,
                OutputTokens = usage.OutputTokens,
                Cost = cost.TotalUsd
            });
        }
    }

    /// <summary>
    /// Records a per-unit priced media call (image or speech) on the session ticker; tokens stay 0 (D-29).
    /// </summary>
    public void RecordMediaUsage(string operation, decimal cost)
    {
        lock (_lock)
        {
            TotalCost += cost;
            TotalMediaCost += cost;

            _entries.Add(new TokenUsageEntry
            {
                Timestamp = DateTime.Now,
                Operation = operation,
                ModelId = "media",
                Provider = "OpenAI",
                InputTokens = 0,
                OutputTokens = 0,
                Cost = cost
            });
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            TotalInputTokens = 0;
            TotalOutputTokens = 0;
            TotalCacheWriteTokens = 0;
            TotalCacheReadTokens = 0;
            TotalCost = 0;
            TotalInputCost = 0;
            TotalCacheWriteCost = 0;
            TotalCacheReadCost = 0;
            TotalOutputCost = 0;
            TotalBatchSavings = 0;
            TotalMediaCost = 0;
            _entries.Clear();
        }
    }

    public string GetSummary()
    {
        lock (_lock)
        {
            return $"Total: {TotalInputTokens:N0} input + {TotalOutputTokens:N0} output tokens = ${TotalCost:F4}";
        }
    }

    public string GetDetailedSummary()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Token Usage Summary");
            sb.AppendLine($"==================");
            sb.AppendLine($"Input Tokens:  {TotalInputTokens:N0}");
            sb.AppendLine($"Output Tokens: {TotalOutputTokens:N0}");
            sb.AppendLine($"Total Tokens:  {TotalInputTokens + TotalOutputTokens:N0}");
            sb.AppendLine($"Total Cost:    ${TotalCost:F4}");
            sb.AppendLine();
            sb.AppendLine($"Breakdown by Operation:");
            sb.AppendLine($"-----------------------");

            var byOperation = _entries
                .GroupBy(e => e.Operation)
                .Select(g => new
                {
                    Operation = g.Key,
                    InputTokens = g.Sum(e => e.InputTokens),
                    OutputTokens = g.Sum(e => e.OutputTokens),
                    Cost = g.Sum(e => e.Cost),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Cost);

            foreach (var op in byOperation)
            {
                sb.AppendLine($"  {op.Operation} ({op.Count}x):");
                sb.AppendLine($"    Tokens: {op.InputTokens:N0} in / {op.OutputTokens:N0} out");
                sb.AppendLine($"    Cost:   ${op.Cost:F4}");
            }

            return sb.ToString();
        }
    }
}

public class TokenUsageEntry
{
    public DateTime Timestamp { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal Cost { get; set; }
}
