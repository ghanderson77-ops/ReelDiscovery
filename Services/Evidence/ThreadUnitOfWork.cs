using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

public enum ThreadOutcomeKind { Written, Warnings, Failed, NotRun }

/// <summary>
/// How one thread's unit of work ended. NotRun = a fatal provider error stopped the run before the thread was journaled.
/// </summary>
public class ThreadOutcome
{
    public ThreadOutcomeKind Kind { get; set; }
    public JournalRecord? Record { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Emails { get; set; }
    public int Files { get; set; }
}

/// <summary>
/// One relevant thread end to end: render, repair, check, retry with feedback, keep the best attempt, compose, write
/// every custodian copy and journal the result (D-12..D-15, RUN-01, RUN-02). Never touches controls or the CaseDraft;
/// progress goes only to the <see cref="GenerationSnapshot"/> (RUN-07).
/// </summary>
public class ThreadUnitOfWork
{
    public const int MaxAttempts = 3;

    private const string TimeoutPrefix = "The request timed out";

    private readonly ILlmGateway _llm;
    private readonly GenerationJournal _journal;
    private readonly MailboxWriter _mailbox;
    private readonly GenerationSnapshot _snapshot;
    private readonly AIModelConfig? _storyModel;

    public ThreadUnitOfWork(ILlmGateway llm, GenerationJournal journal, MailboxWriter mailbox, GenerationSnapshot snapshot,
        AIModelConfig? storyModel)
    {
        _llm = llm;
        _journal = journal;
        _mailbox = mailbox;
        _snapshot = snapshot;
        _storyModel = storyModel;
    }

    /// <summary>
    /// Runs one thread. Throws only when the user cancels during a model call; every other problem becomes a ThreadFailed
    /// record for this thread, or NotRun (no record) when a fatal provider error stopped the run.
    /// </summary>
    internal async Task<ThreadOutcome> RunAsync(ThreadRenderContext ctx, PlanCallRun run)
    {
        var inputTokens = 0;
        var outputTokens = 0;
        var attempts = 0;

        try
        {
            RenderedThread? best = null;
            var bestViolations = new List<RenderViolation>();
            List<RenderViolation>? feedback = null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var outcome = await PlanCallRunner.RunAsync<ThreadRenderer.ThreadRenderDto>(
                    _llm, ThreadRenderer.BuildRequest(ctx, attempt, feedback), run).ConfigureAwait(false);
                inputTokens += outcome.InputTokens;
                outputTokens += outcome.OutputTokens;
                attempts = attempt;

                // Left pending: Continue writes it later (D-06)
                if (run.IsFatal)
                    return new ThreadOutcome { Kind = ThreadOutcomeKind.NotRun, Reason = run.FatalError };

                if (outcome.Value == null)
                {
                    // An earlier attempt is still the best available text
                    if (best != null)
                        break;

                    // No blind re-ask after a refusal or transport failure; the gateway already retried transient errors (D-06, D-15)
                    var kind = outcome.Refused
                        ? ThreadFailureKind.Refused
                        : outcome.Error.StartsWith(TimeoutPrefix, StringComparison.Ordinal) ? ThreadFailureKind.Timeout : ThreadFailureKind.Provider;
                    var reason = outcome.Refused
                        ? $"The model declined: {outcome.RefusalMessage}"
                        : outcome.Error.Length > 0 ? outcome.Error : "The model returned no thread.";
                    return await FailAsync(ctx, kind, reason, attempts, inputTokens, outputTokens).ConfigureAwait(false);
                }

                var rendered = RenderRepair.Apply(outcome.Value, ctx);
                var violations = RenderValidator.Check(rendered, ctx);

                // A composable attempt always beats a structurally broken one; then fewest violations wins and a tie goes to
                // the later attempt (D-14)
                var structurallyBroken = IsStructural(violations);
                var bestStructurallyBroken = IsStructural(bestViolations);
                var better = best == null
                    || (bestStructurallyBroken && !structurallyBroken)
                    || (bestStructurallyBroken == structurallyBroken && violations.Count <= bestViolations.Count);
                if (better)
                {
                    best = rendered;
                    bestViolations = violations;
                }

                if (violations.Count == 0)
                    break;

                if (attempt < MaxAttempts)
                {
                    _snapshot.AddEvent(new GenerationEvent
                    {
                        Kind = GenerationEventKind.Retry,
                        LocalTime = DateTime.Now,
                        ThreadId = ctx.Thread.Id,
                        Subject = RootSubject(ctx),
                        WaveIndex = ctx.WaveIndex,
                        Attempt = attempt,
                        Violations = violations.Select(Describe).ToList()
                    });

                    // Same prompt plus the specific problems (D-13)
                    feedback = violations;
                }
            }

            if (best == null)
                return await FailAsync(ctx, ThreadFailureKind.Provider, "The model returned no thread.", attempts, inputTokens, outputTokens)
                    .ConfigureAwait(false);

            // A missing or empty message cannot be composed into a believable thread
            var structural = bestViolations.FirstOrDefault(v => v.Code is "V1" or "V5");
            if (structural != null)
            {
                return await FailAsync(ctx, ThreadFailureKind.Provider,
                    $"The model did not return a usable thread: {structural.Text}", attempts, inputTokens, outputTokens).ConfigureAwait(false);
            }

            var composed = EmailComposer.Compose(ctx.Plan.PlanId, ctx.Thread, ctx.Assignment, best, ctx.People);

            // Once a thread passed checks, its few milliseconds of writes complete even after Cancel, so no thread is half-saved (RUN-01)
            var files = new List<string>();
            foreach (var email in composed.Emails)
            {
                try
                {
                    files.AddRange(await _mailbox.WriteAsync(email, CancellationToken.None).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return await FailAsync(ctx, ThreadFailureKind.Write, $"Could not write {email.Planned.DocId}.eml: {ex.Message}",
                        attempts, inputTokens, outputTokens).ConfigureAwait(false);
                }
            }

            var cost = Cost(inputTokens, outputTokens);
            var clean = bestViolations.Count == 0;
            var record = new JournalRecord
            {
                Type = JournalRecordType.ThreadDone,
                Utc = DateTime.UtcNow,
                PlanId = ctx.Plan.PlanId,
                ThreadId = ctx.Thread.Id,
                Status = clean ? ThreadWriteStatus.Written : ThreadWriteStatus.Warnings,
                Attempts = attempts,
                Violations = bestViolations.Select(Describe).ToList(),
                Files = files,
                Messages = composed.Emails.Select(e => new JournalMessage
                {
                    DocId = e.Planned.DocId,
                    MessageId = e.MessageId,
                    Subject = e.Subject,
                    BodyPlain = e.BodyPlain,
                    SignOff = e.SignOff
                }).ToList(),
                LedgerDelta = best.NewDetails,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsd = cost
            };
            await _journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);

            // Tiles update per thread, not per wave
            var status = clean ? ThreadRunStatus.Written : ThreadRunStatus.Warnings;
            _snapshot.RecordThread(ctx.WaveIndex, status, composed.Emails.Count, files.Count, cost, ctx.PreviousStatus);
            _snapshot.AddEvent(new GenerationEvent
            {
                Kind = clean ? GenerationEventKind.ThreadWritten : GenerationEventKind.KeptWithWarnings,
                LocalTime = DateTime.Now,
                ThreadId = ctx.Thread.Id,
                Subject = RootSubject(ctx),
                WaveIndex = ctx.WaveIndex,
                Emails = composed.Emails.Count,
                Details = best.NewDetails.Count,
                Attempt = attempts,
                Violations = record.Violations.ToList(),
                FilePath = LastFilePath(ctx)
            });

            return new ThreadOutcome
            {
                Kind = clean ? ThreadOutcomeKind.Written : ThreadOutcomeKind.Warnings,
                Record = record,
                Emails = composed.Emails.Count,
                Files = files.Count
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return await FailAsync(ctx, ThreadFailureKind.Write, $"Could not write thread {ctx.Thread.Id}: {ex.Message}",
                attempts, inputTokens, outputTokens).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A MimeKit ParseException (malformed address) or a composer bug must fail only this thread, never escape Task.WhenAll and abort the run (RUN-02)
            return await FailAsync(ctx, ThreadFailureKind.Provider, $"Could not build thread {ctx.Thread.Id}: {ex.Message}",
                attempts, inputTokens, outputTokens).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Journals ThreadFailed with the tokens and cost spent so far, counts it and adds an Activity row. Never throws.
    /// </summary>
    private async Task<ThreadOutcome> FailAsync(ThreadRenderContext ctx, ThreadFailureKind kind, string reason, int attempts,
        int inputTokens, int outputTokens)
    {
        // GenerationRunState sums cost over ThreadDone and ThreadFailed records, so failures carry their spend too
        var cost = Cost(inputTokens, outputTokens);
        var record = new JournalRecord
        {
            Type = JournalRecordType.ThreadFailed,
            Utc = DateTime.UtcNow,
            PlanId = ctx.Plan.PlanId,
            ThreadId = ctx.Thread.Id,
            Attempts = attempts,
            FailureKind = kind,
            Reason = reason,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = cost
        };

        try
        {
            await _journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Without a journal line the thread stays pending and Continue retries it; the run itself goes on (RUN-02)
        }

        _snapshot.RecordThread(ctx.WaveIndex, ThreadRunStatus.Failed, 0, 0, cost, ctx.PreviousStatus);
        _snapshot.AddEvent(new GenerationEvent
        {
            Kind = GenerationEventKind.ThreadFailed,
            LocalTime = DateTime.Now,
            ThreadId = ctx.Thread.Id,
            Subject = RootSubject(ctx),
            WaveIndex = ctx.WaveIndex,
            Attempt = attempts,
            Reason = reason,
            FilePath = LastFilePath(ctx)
        });

        return new ThreadOutcome { Kind = ThreadOutcomeKind.Failed, Record = record, Reason = reason };
    }

    // A missing or empty message (V1, V5) cannot be composed into a thread
    private static bool IsStructural(List<RenderViolation> violations) => violations.Any(v => v.Code is "V1" or "V5");

    private decimal Cost(int inputTokens, int outputTokens) => _storyModel?.CalculateTotalCost(inputTokens, outputTokens) ?? 0m;

    private static string Describe(RenderViolation violation) =>
        violation.Seq > 0 ? $"msg {violation.Seq}: {violation.Text}" : violation.Text;

    private static List<PlannedMessage> Ordered(ThreadRenderContext ctx) =>
        (ctx.Thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).OrderBy(m => m.Seq).ToList();

    private static string RootSubject(ThreadRenderContext ctx)
    {
        var root = Ordered(ctx).FirstOrDefault();
        return root == null
            ? ctx.Thread.Subject ?? string.Empty
            : EmailComposer.SubjectFor(ctx.Thread, root, ctx.Assignment?.Marking ?? PrivilegeMarking.None);
    }

    private static string LastFilePath(ThreadRenderContext ctx) => Ordered(ctx).LastOrDefault()?.FilePath ?? string.Empty;
}
