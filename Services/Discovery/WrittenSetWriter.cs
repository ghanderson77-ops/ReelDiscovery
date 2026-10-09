using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Result of writing one RFP/ROG/RFA set: the content when every call produced usable requests, otherwise the reason it
/// failed (D-05). Cost covers every attempt of every call, including failed and refused ones (PROV-07).
/// </summary>
public class WrittenSetWriteOutcome
{
    public WrittenDiscoveryContent? Content { get; set; }
    public List<DiscoveryViolation> Warnings { get; set; } = new();
    public string Error { get; set; } = string.Empty;
    public bool Refused { get; set; }
    public bool TimedOut { get; set; }

    // A fatal provider error (bad key, spend limit) stopped the run before this document finished; leave it pending
    public bool NotRun { get; set; }

    // The user cancelled before this document finished; Cost still holds every completed attempt (T-08-47)
    public bool Cancelled { get; set; }

    public CostBreakdown Cost { get; set; } = new();
}

/// <summary>
/// One written-discovery unit of work (D-04, D-17..D-20, D-26, D-28): slots are written in calls of at most
/// <see cref="WrittenDiscoveryPlanner.MaxSlotsPerCall"/> on the Story tier with the shared system prompt, each call checked
/// by <see cref="DiscoveryValidator"/>, retried with feedback up to <see cref="MaxAttempts"/> times and the best attempt
/// kept. Code owns numbering, keys, prefixes and all boilerplate. Never throws; a user cancel returns the spend so far with Cancelled set.
/// </summary>
public sealed partial class WrittenSetWriter
{
    // D-26: one attempt plus two retries with feedback
    public const int MaxAttempts = 3;

    private const string TimeoutPrefix = "The request timed out";
    private const string NoRequestsError = "The model returned no requests.";

    private readonly ILlmGateway _llm;

    public WrittenSetWriter(ILlmGateway llm)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    /// <summary>
    /// Writes every slot of <paramref name="plan"/> in batches and assembles the set content.
    /// <paramref name="onPart"/> receives (1-based part, part count) before each call.
    /// </summary>
    internal async Task<WrittenSetWriteOutcome> WriteAsync(CaseDraft draft, WrittenSetPlan plan, string caseNumber, string systemPrompt,
        string guidance, PlanCallRun run, Action<int, int>? onPart = null)
    {
        var cost = new CostBreakdown();
        var warnings = new List<DiscoveryViolation>();
        var slots = plan.Slots.Where(s => s != null).ToList();
        var batches = slots.Chunk(WrittenDiscoveryPlanner.MaxSlotsPerCall).Select(b => b.ToList()).ToList();
        var label = DiscoveryBoilerplate.RequestLabel(plan.Kind, plan.Variant);

        // Slot number -> cleaned model text from its batch's best attempt
        var texts = new Dictionary<int, string>();

        for (var i = 0; i < batches.Count; i++)
        {
            // A fatal error elsewhere stopped the run before this part: left pending, the next run writes it (D-05)
            if (run.IsFatal)
                return new WrittenSetWriteOutcome { NotRun = true, Error = run.FatalError, Cost = cost };

            var batch = batches[i];
            onPart?.Invoke(i + 1, batches.Count);

            var ctx = CheckContext(draft, plan, batch);
            Dictionary<int, string>? best = null;
            List<DiscoveryViolation>? bestViolations = null;
            List<DiscoveryViolation>? feedback = null;
            var error = string.Empty;
            var refused = false;
            var timedOut = false;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // No retries after a fatal stop: a part that already has an attempt keeps it
                if (attempt > 1 && run.IsFatal)
                {
                    if (best != null)
                        break;
                    return new WrittenSetWriteOutcome { NotRun = true, Error = run.FatalError, Cost = cost };
                }

                var request = new LlmRequest
                {
                    Tier = LlmTier.Story,
                    OperationName = DiscoveryPrompts.OperationWritten,
                    SystemPrompt = systemPrompt,
                    UserPrompt = DiscoveryPrompts.BuildWrittenPrompt(draft, plan, batch, guidance, feedback ?? new List<DiscoveryViolation>()),
                    MaxOutputTokens = DiscoveryPrompts.MaxOutputTokens,
                    AllowBatch = false
                };

                PlanCallOutcome<DiscoveryPrompts.RequestSetDto> outcome;
                try
                {
                    outcome = await PlanCallRunner.RunAsync<DiscoveryPrompts.RequestSetDto>(_llm, request, run);
                }
                catch (OperationCanceledException)
                {
                    // User cancel: hand back the spend of the finished attempts so the generator records it (the
                    // in-flight call's cost is unknown)
                    return new WrittenSetWriteOutcome { NotRun = true, Cancelled = true, Cost = cost };
                }
                cost.Add(outcome.Cost);

                // A fatal stop only discards a call that produced nothing: a returned value was paid for and is kept (WR-04)
                if (outcome.Value == null && run.IsFatal)
                {
                    if (best != null)
                        break;

                    // Left pending: the next run writes it (D-05)
                    return new WrittenSetWriteOutcome { NotRun = true, Error = run.FatalError, Cost = cost };
                }

                if (outcome.Value == null)
                {
                    // An earlier attempt is still the best available text
                    if (best != null)
                        break;

                    // A refusal or timeout may succeed on the next attempt; the gateway already retried transient errors
                    refused = outcome.Refused;
                    timedOut = outcome.Error.StartsWith(TimeoutPrefix, StringComparison.Ordinal);
                    error = outcome.Error.Length > 0 ? outcome.Error : NoRequestsError;
                    continue;
                }

                var returned = CleanRequests(outcome.Value, batch, label);
                var violations = DiscoveryValidator.CheckRequests(returned.Select(r => (r.Slot, Joined(r.Prefix, r.Text))).ToList(), ctx,
                    outcome.Value.PeopleNamed ?? new List<string>());

                // CX-02: an empty list, blank texts or only unknown slots is no content, never a "best attempt" that
                // could replace a saved document with boilerplate
                if (!returned.Any(r => ctx.ExpectedSlots.Contains(r.Slot)))
                {
                    if (best != null)
                        break;

                    refused = false;
                    timedOut = false;
                    error = NoRequestsError;
                    feedback = violations;
                    continue;
                }

                if (DiscoveryValidator.IsBetter(violations, bestViolations))
                {
                    // First returned text per code-issued slot; unknown slots are dropped (T-08-37)
                    best = new Dictionary<int, string>();
                    foreach (var r in returned.Where(r => ctx.ExpectedSlots.Contains(r.Slot)))
                        best.TryAdd(r.Slot, r.Text);
                    bestViolations = violations;
                }

                if (violations.Count == 0)
                    break;

                feedback = violations;
            }

            // A set missing a whole call's worth of requests is not kept
            if (best == null || best.Count == 0)
                return new WrittenSetWriteOutcome { Error = error, Refused = refused, TimedOut = timedOut, Cost = cost };

            foreach (var (slot, text) in best)
                texts[slot] = text;
            warnings.AddRange(bestViolations ?? new List<DiscoveryViolation>());
        }

        return new WrittenSetWriteOutcome
        {
            Content = Assemble(draft, plan, caseNumber, slots, texts, label),
            Warnings = Dedupe(warnings),
            Cost = cost
        };
    }

    private static WrittenDiscoveryContent Assemble(CaseDraft draft, WrittenSetPlan plan, string caseNumber, List<RequestSlot> slots,
        Dictionary<int, string> texts, string label)
    {
        var doc = draft?.CoreDocument ?? new CoreDocument();
        var safeDraft = draft ?? new CaseDraft();
        var serviceDate = DiscoveryBoilerplate.ServiceDate(safeDraft);

        // Missing slots are omitted and the rest renumbered consecutively by code (D-04)
        var requests = new List<DiscoveryRequest>();
        foreach (var slot in slots)
        {
            if (!texts.TryGetValue(slot.Number, out var text))
                continue;

            requests.Add(new DiscoveryRequest
            {
                Number = requests.Count + 1,
                Prefix = slot.Prefix,
                Text = Joined(slot.Prefix, text),
                IssueIds = new List<string>(slot.IssueIds),
                FactId = slot.FactId,
                DocId = slot.DocId,
                IsGenuineness = slot.IsGenuineness
            });
        }

        return new WrittenDiscoveryContent
        {
            Kind = plan.Kind,
            Variant = plan.Variant,
            Title = plan.Title,
            CaseNumber = caseNumber ?? string.Empty,
            CaptionText = DiscoveryBoilerplate.CaptionText(doc, caseNumber ?? string.Empty, plan.Title),
            PreliminaryStatement = DiscoveryBoilerplate.PreliminaryStatement(doc, plan.Kind, plan.Propounding, plan.Responding),
            Definitions = DiscoveryBoilerplate.Definitions(safeDraft, plan.Kind, plan.Responding),
            Instructions = DiscoveryBoilerplate.Instructions(doc, plan.Kind),
            RequestLabel = label,
            Requests = requests,
            ServiceDate = serviceDate,
            SignatureText = DiscoveryBoilerplate.SignatureText(safeDraft, plan.Propounding, serviceDate),
            CertificateText = DiscoveryBoilerplate.CertificateText(safeDraft, plan.Responding, serviceDate),
            PropoundingPartyId = plan.Propounding?.Id ?? string.Empty,
            RespondingPartyId = plan.Responding?.Id ?? string.Empty
        };
    }

    private static DiscoveryCheckContext CheckContext(CaseDraft draft, WrittenSetPlan plan, List<RequestSlot> batch)
    {
        var ctx = new DiscoveryCheckContext { Draft = draft ?? new CaseDraft(), Kind = plan.Kind };
        foreach (var slot in batch)
        {
            ctx.ExpectedSlots.Add(slot.Number);
            if (slot.FactId.Length > 0)
                ctx.SlotFactIds[slot.Number] = slot.FactId;
            if (slot.IsGenuineness)
                ctx.GenuinenessSlots.Add(slot.Number);
            if (!string.IsNullOrWhiteSpace(slot.DocId))
                ctx.AllowedIdentifiers.Add(slot.DocId.Trim());
        }

        return ctx;
    }

    /// <summary>
    /// T-08-38: strips a leading request label ("REQUEST FOR PRODUCTION NO. 4:", "Request No. 4:") and any repeats of the
    /// slot's code-owned prefix, then invalid XML characters. Empty texts are dropped so the slot reads as missing (D4).
    /// </summary>
    private static List<(int Slot, string Prefix, string Text)> CleanRequests(DiscoveryPrompts.RequestSetDto dto, List<RequestSlot> batch,
        string label)
    {
        var prefixes = batch.GroupBy(s => s.Number).ToDictionary(g => g.Key, g => g.First().Prefix ?? string.Empty);
        var result = new List<(int Slot, string Prefix, string Text)>();

        foreach (var r in (dto.Requests ?? new List<DiscoveryPrompts.RequestDto>()).Where(r => r != null))
        {
            var prefix = prefixes.TryGetValue(r.Slot, out var p) ? p : string.Empty;
            var text = CaseGuideDocxExporter.StripInvalidXmlChars(r.Text ?? string.Empty).Trim();
            text = StripLabel(text, label);
            text = StripPrefix(text, prefix);
            if (text.Length > 0)
                result.Add((r.Slot, prefix, text));
        }

        return result;
    }

    private static string StripLabel(string text, string label)
    {
        var trimmedLabel = label.Trim();
        if (trimmedLabel.Length > 0 && text.StartsWith(trimmedLabel, StringComparison.OrdinalIgnoreCase))
        {
            var m = LeadingNumber().Match(text[trimmedLabel.Length..]);
            if (m.Success)
                return text[(trimmedLabel.Length + m.Length)..].Trim();
        }

        var generic = GenericLabel().Match(text);
        return generic.Success ? text[generic.Length..].Trim() : text;
    }

    // A model that repeats the prefix (once or more, with or without its trailing comma) has it removed (D-19)
    private static string StripPrefix(string text, string prefix)
    {
        var full = prefix.Trim();
        if (full.Length == 0)
            return text;

        var bare = full.TrimEnd(',').TrimEnd();
        var changed = true;
        while (changed && text.Length > 0)
        {
            changed = false;
            foreach (var candidate in new[] { full, bare })
            {
                if (candidate.Length > 0 && text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[candidate.Length..].TrimStart(',', ' ', '\t').Trim();
                    changed = true;
                    break;
                }
            }
        }

        return text;
    }

    private static string Joined(string prefix, string text) =>
        string.IsNullOrWhiteSpace(prefix) ? text : prefix.Trim() + " " + text;

    private static List<DiscoveryViolation> Dedupe(List<DiscoveryViolation> violations) =>
        violations
            .Where(v => v != null)
            .GroupBy(v => (v.Rule, v.Number, v.Value, v.Canonical))
            .Select(g => g.First())
            .ToList();

    // " 4:" / " 4." / " 4 -" after the code label
    [GeneratedRegex(@"^\s*\d+\s*[:.\-]?\s*")]
    private static partial Regex LeadingNumber();

    // Any "<words> No. 4:" lead-in, e.g. "Request No. 4:" or "Interrogatory No. 4."
    [GeneratedRegex(@"^\s*(?:[A-Za-z]+\s+){0,4}No\.?\s*\d+\s*[:.\-]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex GenericLabel();
}
