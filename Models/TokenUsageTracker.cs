namespace ReelDiscovery.Models;

public class TokenUsageTracker
{
    private readonly object _lock = new();
    private readonly List<TokenUsageEntry> _entries = new();

    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }
    public decimal TotalCost { get; private set; }

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
        var cost = model.CalculateTotalCost(inputTokens, outputTokens);

        lock (_lock)
        {
            TotalInputTokens += inputTokens;
            TotalOutputTokens += outputTokens;
            TotalCost += cost;

            _entries.Add(new TokenUsageEntry
            {
                Timestamp = DateTime.Now,
                Operation = operation,
                ModelId = model.ModelId,
                Provider = model.Provider.ToString(),
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
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
            TotalCost = 0;
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
