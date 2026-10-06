using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Qa;

public class BlindReviewRunResult
{
    // Verdicts in the current pass for the requested documents (earlier sessions included)
    public int Reviewed { get; set; }
    public List<BlindReviewVerdict> Disagreements { get; set; } = new();

    // Refused or failed calls in this session; those documents have no verdict and are retried by a rerun
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();

    // Bad key or spend limit stopped the run; empty otherwise
    public string FatalMessage { get; set; } = string.Empty;

    // Gateway cost of every call made in this session, failed attempts included
    public decimal CostUsd { get; set; }
}

/// <summary>
/// Blind review (QA-01, D-11..D-13): a second model codes a sample of generated emails from the review protocol alone.
/// Each verdict is appended to qa-blind-review.jsonl as it arrives, so a cancel keeps finished verdicts and a rerun skips
/// reviewed DocIds. Report only: nothing in the corpus changes.
/// </summary>
public class BlindReviewer
{
    public const string OperationName = BlindReviewPrompts.OperationName;
    public const int MaxParallelCalls = 4;
    public const int EstimatedOutputTokens = 150;
    public const int MaxReasonChars = 400;
    private const int MaxIssueCodes = 20;
    private const int MaxIssueCodeChars = 20;
    private const string RetryHint = "Run the blind review again to continue.";

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(BlindReviewPrompts.BlindReviewDto) };

    private readonly ILlmGateway _reviewer;
    private readonly QaReportStore _store;

    public BlindReviewer(ILlmGateway reviewer, QaReportStore store)
    {
        _reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    // Tests only: 1 makes call order deterministic
    internal int MaxParallel { get; set; } = MaxParallelCalls;

    /// <summary>
    /// DocIds of every email the journal records as written (ThreadDone), in thread then message order.
    /// </summary>
    public static List<string> GeneratedEmailDocIds(JournalReplay replay)
    {
        var ids = new List<string>();
        foreach (var record in (replay?.LastByThread ?? new()).Values)
        {
            if (record == null || record.Type != JournalRecordType.ThreadDone)
                continue;
            foreach (var message in record.Messages ?? new())
            {
                if (message != null && !string.IsNullOrWhiteSpace(message.DocId))
                    ids.Add(message.DocId);
            }
        }
        return ids;
    }

    /// <summary>
    /// The sampled emails as a reviewer sees them: header fields from the answer key (overlay-rendered subject and sender),
    /// body and sign-off from the journaled message, and each written attachment's title and first 1,500 characters.
    /// DocIds missing from the key are skipped; order follows <paramref name="docIds"/>.
    /// </summary>
    public static List<BlindReviewDocument> BuildDocuments(CorpusPlan plan, CaseGuide guide, JournalReplay replay,
        AnswerKeyOverlay? overlay, IReadOnlyList<string> docIds)
    {
        var rows = AnswerKeyBuilder.Build(plan, guide, overlay)
            .Where(r => r.DocType == AnswerKeyBuilder.DocTypeEmail)
            .ToDictionary(r => r.DocId, StringComparer.Ordinal);

        var messages = new Dictionary<string, JournalMessage>(StringComparer.Ordinal);
        foreach (var record in (replay?.LastByThread ?? new()).Values)
        {
            if (record == null || record.Type != JournalRecordType.ThreadDone)
                continue;
            foreach (var message in record.Messages ?? new())
            {
                if (message != null && !string.IsNullOrEmpty(message.DocId))
                    messages.TryAdd(message.DocId, message);
            }
        }

        var attachmentsByParent = (replay?.LastByAttachment ?? new())
            .Where(kv => kv.Value != null && kv.Value.Type == JournalRecordType.AttachmentDone)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .GroupBy(kv => kv.Value.ParentDocId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Value).ToList(), StringComparer.Ordinal);

        var docs = new List<BlindReviewDocument>();
        foreach (var docId in docIds ?? Array.Empty<string>())
        {
            if (docId == null || !rows.TryGetValue(docId, out var row))
                continue;

            messages.TryGetValue(docId, out var message);
            var subject = overlay == null && !string.IsNullOrWhiteSpace(message?.Subject) ? message!.Subject : row.Subject;

            var body = (message?.BodyPlain ?? string.Empty).Trim();
            var signOff = (message?.SignOff ?? string.Empty).Trim();
            if (signOff.Length > 0)
                body = body.Length > 0 ? $"{body}\n\n{signOff}" : signOff;

            var doc = new BlindReviewDocument
            {
                DocId = row.DocId,
                ThreadId = row.ThreadId,
                From = row.From,
                To = row.To,
                Cc = row.Cc,
                DateSent = row.DateSent,
                Subject = subject,
                Body = body,
                KeyRelevant = row.Relevance == AnswerKeyBuilder.RelevantLabel
            };

            if (attachmentsByParent.TryGetValue(docId, out var attachments))
            {
                foreach (var attachment in attachments)
                {
                    var title = !string.IsNullOrWhiteSpace(attachment.Title) ? attachment.Title.Trim() : attachment.FileName?.Trim() ?? string.Empty;
                    doc.Attachments.Add(new BlindReviewAttachment
                    {
                        Title = title,
                        Text = BlindReviewPrompts.Truncate(attachment.FlattenedText, BlindReviewPrompts.AttachmentTextLimit)
                    });
                }
            }

            docs.Add(doc);
        }

        return docs;
    }

    /// <summary>
    /// D-11 estimate before running: N x [protocol tokens at cache-read price when the protocol reaches the model's cache
    /// minimum (else input price) + document tokens at input price + 150 output tokens] + one protocol cache write.
    /// Tokens are approximated as characters / 4.
    /// </summary>
    public static decimal Estimate(AIModelConfig model, string systemPrompt, IReadOnlyList<BlindReviewDocument> docs)
    {
        ArgumentNullException.ThrowIfNull(model);
        docs ??= Array.Empty<BlindReviewDocument>();
        var protocolTokens = Tokens(systemPrompt);
        var cacheable = model.MinCacheablePrefixTokens > 0 && protocolTokens >= model.MinCacheablePrefixTokens;

        var usage = new TokenUsage();
        foreach (var doc in docs)
        {
            var docTokens = Tokens(BlindReviewPrompts.BuildUserPrompt(doc));
            usage.Add(new TokenUsage
            {
                InputTokens = docTokens + (cacheable ? 0 : protocolTokens),
                CacheReadTokens = cacheable ? protocolTokens : 0,
                OutputTokens = EstimatedOutputTokens
            });
        }

        if (cacheable && docs.Count > 0)
            usage.CacheWriteTokens += protocolTokens;

        return model.CalculateCost(usage).TotalUsd;
    }

    private static int Tokens(string? text) => ((text?.Length ?? 0) + 3) / 4;

    /// <summary>Verdicts where the answer key and the reviewer disagree on relevance.</summary>
    public static List<BlindReviewVerdict> Disagreements(IEnumerable<BlindReviewVerdict> verdicts) =>
        (verdicts ?? Enumerable.Empty<BlindReviewVerdict>())
            .Where(v => v != null && v.KeyRelevant != v.ReviewerRelevant)
            .ToList();

    /// <summary>
    /// Reviews every document not already in the current pass. A new header starts a pass when none exists or the plan,
    /// reviewer provider or model changed; a resized sample re-states the header and keeps its matching verdicts.
    /// Throws OperationCanceledException on user cancel after in-flight verdicts are appended.
    /// </summary>
    public async Task<BlindReviewRunResult> RunAsync(BlindReviewHeader header, IReadOnlyList<BlindReviewDocument> docs,
        string systemPrompt, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(header);
        docs ??= Array.Empty<BlindReviewDocument>();
        var requested = new HashSet<string>(docs.Select(d => d.DocId), StringComparer.Ordinal);

        var (existingHeader, existingVerdicts) = _store.LoadBlindReview();
        var samePass = existingHeader != null
                       && existingHeader.PlanId == header.PlanId
                       && string.Equals(existingHeader.ReviewerProvider, header.ReviewerProvider, StringComparison.Ordinal)
                       && string.Equals(existingHeader.ReviewerModel, header.ReviewerModel, StringComparison.Ordinal);

        var kept = samePass ? existingVerdicts.Where(v => requested.Contains(v.DocId)).ToList() : new List<BlindReviewVerdict>();
        if (!samePass)
        {
            await _store.AppendBlindReviewHeaderAsync(header, ct);
        }
        else if (existingHeader!.SampleSize != header.SampleSize)
        {
            // Resized sample: restate the header and carry the verdicts that are still in the sample
            await _store.AppendBlindReviewHeaderAsync(header, ct);
            foreach (var verdict in kept)
                await _store.AppendBlindReviewVerdictAsync(verdict, ct);
        }

        var reviewed = new HashSet<string>(kept.Select(v => v.DocId), StringComparer.Ordinal);
        var todo = docs.Where(d => d != null && !reviewed.Contains(d.DocId)).ToList();

        var result = new BlindReviewRunResult();
        var gate = new object();
        var done = reviewed.Count;

        using (var run = new PlanCallRun(ct, MaxParallel, RetryHint))
        {
            var tasks = todo.Select(doc => ReviewOneAsync(doc, systemPrompt, run, result, gate, () =>
            {
                int value;
                lock (gate)
                    value = ++done;
                progress?.Report(value);
            })).ToList();

            // WhenAll waits for every task, so finished verdicts are on disk before a cancel propagates
            await Task.WhenAll(tasks);

            if (run.IsFatal)
                result.FatalMessage = run.FatalError;
        }

        var (_, verdicts) = _store.LoadBlindReview();
        var current = verdicts.Where(v => requested.Contains(v.DocId)).ToList();
        result.Reviewed = current.Count;
        result.Disagreements = Disagreements(current);
        return result;
    }

    private async Task ReviewOneAsync(BlindReviewDocument doc, string systemPrompt, PlanCallRun run,
        BlindReviewRunResult result, object gate, Action onVerdict)
    {
        var request = new LlmRequest
        {
            Tier = LlmTier.Volume,
            OperationName = OperationName,
            SystemPrompt = systemPrompt ?? string.Empty,
            UserPrompt = BlindReviewPrompts.BuildUserPrompt(doc),
            MaxOutputTokens = BlindReviewPrompts.MaxOutputTokens,
            AllowBatch = false
        };

        var outcome = await PlanCallRunner.RunAsync<BlindReviewPrompts.BlindReviewDto>(_reviewer, request, run);
        var cost = outcome.Cost.TotalUsd;
        lock (gate)
            result.CostUsd += cost;

        if (outcome.Value == null)
        {
            // Fatal stops and skipped calls carry no error text; the fatal message is reported once
            if (outcome.Error.Length > 0)
            {
                lock (gate)
                {
                    result.Errors++;
                    result.ErrorMessages.Add(outcome.Error);
                }
            }
            return;
        }

        var verdict = new BlindReviewVerdict
        {
            DocId = doc.DocId,
            ThreadId = doc.ThreadId,
            KeyRelevant = doc.KeyRelevant,
            ReviewerRelevant = outcome.Value.Relevant,
            IssueCodes = CleanCodes(outcome.Value.IssueCodes),
            Reason = BlindReviewPrompts.Truncate((outcome.Value.Reason ?? string.Empty).Trim(), MaxReasonChars),
            Utc = DateTime.UtcNow,
            CostUsd = cost
        };

        // Not cancellable: a verdict that was paid for is always kept (D-13)
        await _store.AppendBlindReviewVerdictAsync(verdict, CancellationToken.None);
        onVerdict();
    }

    private static List<string> CleanCodes(List<string>? codes) =>
        (codes ?? new())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => BlindReviewPrompts.Truncate(c.Trim(), MaxIssueCodeChars))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxIssueCodes)
            .ToList();
}
