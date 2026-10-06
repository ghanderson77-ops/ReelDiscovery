using System.Collections.Concurrent;
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
/// Unjournaled = the files were written but the ThreadDone line could not be saved (the thread stays pending).
/// </summary>
public class ThreadOutcome
{
    public ThreadOutcomeKind Kind { get; set; }
    public JournalRecord? Record { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Emails { get; set; }
    public int Files { get; set; }

    // NotRun and Unjournaled: spend with no journal line of its own (the runner adds it to RunEnded). Failed: the spend
    // the ThreadFailed line already carries, thread and attachments together
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }

    // Committed attachment records nested in the ThreadDone line, in plan order
    public List<JournalRecord> AttachmentRecords { get; set; } = new();

    // Committed Written/Warnings relevant attachment deltas (FromAttachment, AttachmentDocId); empty for filler (D-13)
    public List<LedgerDelta> AttachmentDeltas { get; set; } = new();

    public bool Unjournaled { get; set; }

    // Committed path only: NotRun attachments have no record, so these tokens and this spend (text + media) go to RunEnded
    public int NotRunInputTokens { get; set; }
    public int NotRunOutputTokens { get; set; }
    public decimal NotRunCostUsd { get; set; }
}

/// <summary>
/// Run-step inputs of the attachment step shared by every thread unit of the step.
/// </summary>
public class ThreadAttachmentContext
{
    public ChainScheduler Chains { get; set; } = null!;

    // Shared per run step; InviteSlots seeded from GenerationRunState.InviteSlotsByMeeting (D-20)
    public ConcurrentDictionary<string, InviteSlot> InviteSlots { get; } = new(StringComparer.Ordinal);

    // Stage row the attachment counters move to (GenerationSnapshot.RecordAttachment)
    public int RowIndex { get; set; }

    // True when later waves are already written (review concern 5)
    public bool LateRepair { get; set; }
    public List<LedgerEntry> LaterEstablished { get; set; } = new();
}

/// <summary>
/// One thread end to end on one path for relevant and non-relevant mail (NOISE-05): render with the thread's
/// <see cref="RenderProfile"/>, check, retry with feedback, keep the best attempt, run each planned attachment, compose,
/// write every custodian copy and journal the thread with its attachments in one line (D-12..D-15, D-14, D-26, RUN-01,
/// RUN-02). Status, events, chain text and invite slots are applied only after that line is flushed (review concern 1).
/// Never touches controls or the CaseDraft; progress goes only to the <see cref="GenerationSnapshot"/> (RUN-07).
/// </summary>
public class ThreadUnitOfWork
{
    public const int MaxAttempts = 3;

    public const string CancelledReason = "the run was cancelled";

    private const string TimeoutPrefix = "The request timed out";

    private readonly ILlmGateway _llm;
    private readonly GenerationJournal _journal;
    private readonly MailboxWriter _mailbox;
    private readonly GenerationSnapshot _snapshot;
    private readonly AIModelConfig? _storyModel;
    private readonly AttachmentUnitOfWork? _attachments;

    public ThreadUnitOfWork(ILlmGateway llm, GenerationJournal journal, MailboxWriter mailbox, GenerationSnapshot snapshot,
        AIModelConfig? storyModel)
        : this(llm, journal, mailbox, snapshot, storyModel, null)
    {
    }

    public ThreadUnitOfWork(ILlmGateway llm, GenerationJournal journal, MailboxWriter mailbox, GenerationSnapshot snapshot,
        AIModelConfig? storyModel, AttachmentUnitOfWork? attachments)
    {
        _llm = llm;
        _journal = journal;
        _mailbox = mailbox;
        _snapshot = snapshot;
        _storyModel = storyModel;
        _attachments = attachments;
    }

    // Test seam: runs after every EML copy is written and just before the ThreadDone append
    internal Func<JournalRecord, Task>? BeforeAppendHook { get; set; }

    /// <summary>
    /// Runs one relevant thread (Phase 5 path, no attachment step).
    /// </summary>
    internal Task<ThreadOutcome> RunAsync(ThreadRenderContext ctx, PlanCallRun run) =>
        RunAsync(ctx, RenderProfile.Relevant(_llm), run);

    /// <summary>
    /// Runs one thread with its profile and, when <paramref name="attachments"/> is given, its planned attachments.
    /// Throws only when the user cancels: during a thread render call (nothing journaled), or after the thread and its
    /// finished parts were committed. Every other problem becomes a ThreadFailed record for this thread, NotRun (no
    /// record) when a fatal provider error stopped the run before acceptance, or Unjournaled when the line could not be saved.
    /// </summary>
    internal async Task<ThreadOutcome> RunAsync(ThreadRenderContext ctx, RenderProfile profile, PlanCallRun run,
        ThreadAttachmentContext? attachments = null)
    {
        profile ??= RenderProfile.Relevant(_llm);
        var inputTokens = 0;
        var outputTokens = 0;
        var attempts = 0;
        var spend = new AttachmentSpend();
        var committedTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var relevant = ctx.Thread.Relevance == ThreadRelevance.Relevant;

        // WR-01: set once copies may be on disk, cleared again when the ThreadDone line is flushed
        ComposedThread? uncommitted = null;

        try
        {
            try
            {
                RenderedThread? best = null;
                var bestViolations = new List<RenderViolation>();
                List<RenderViolation>? feedback = null;

                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    var outcome = await profile.RenderAsync(ctx, attempt, feedback, run).ConfigureAwait(false);
                    inputTokens += outcome.InputTokens;
                    outputTokens += outcome.OutputTokens;
                    attempts = attempt;

                    // Left pending: Continue writes it later (D-06)
                    if (run.IsFatal)
                    {
                        var spent = Cost(inputTokens, outputTokens, profile);
                        _snapshot.RecordThread(ctx.WaveIndex, ThreadRunStatus.Pending, 0, 0, spent, relevant: relevant);
                        return new ThreadOutcome
                        {
                            Kind = ThreadOutcomeKind.NotRun,
                            Reason = run.FatalError,
                            InputTokens = inputTokens,
                            OutputTokens = outputTokens,
                            CostUsd = spent
                        };
                    }

                    if (outcome.Rendered == null)
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
                        return await FailAsync(ctx, profile, kind, reason, attempts, inputTokens, outputTokens, spend).ConfigureAwait(false);
                    }

                    var rendered = outcome.Rendered;
                    var violations = profile.Check(rendered, ctx);

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
                            Violations = violations.Select(Describe).ToList(),
                            Relevant = relevant
                        });

                        // Same prompt plus the specific problems (D-13)
                        feedback = violations;
                    }
                }

                if (best == null)
                    return await FailAsync(ctx, profile, ThreadFailureKind.Provider, "The model returned no thread.", attempts,
                        inputTokens, outputTokens, spend).ConfigureAwait(false);

                // A missing or empty message cannot be composed into a believable thread
                var structural = bestViolations.FirstOrDefault(v => v.Code is "V1" or "V5");
                if (structural != null)
                {
                    return await FailAsync(ctx, profile, ThreadFailureKind.Provider,
                        $"The model did not return a usable thread: {structural.Text}", attempts, inputTokens, outputTokens, spend)
                        .ConfigureAwait(false);
                }

                // D-14: attachments run after the thread is accepted and before anything is written
                var outcomes = new List<AttachmentOutcome>();
                var slots = new Dictionary<string, InviteSlot>(StringComparer.Ordinal);
                var cancelled = false;
                if (_attachments != null && attachments != null)
                    cancelled = await RunAttachmentsAsync(ctx, profile, best, attachments, run, outcomes, slots, spend).ConfigureAwait(false);

                // D-26: a failed or skipped attachment never holds back its email
                var parts = new Dictionary<string, ComposedAttachment>(StringComparer.Ordinal);
                foreach (var done in outcomes.Where(o => IsCommitted(o) && o.Part != null))
                    parts.TryAdd(done.DocId, done.Part!);

                var composed = EmailComposer.Compose(ctx.Plan.PlanId, ctx.Thread, ctx.Assignment, best, ctx.People, parts);

                // Once a thread passed checks, its few milliseconds of writes complete even after Cancel, so no thread is half-saved (RUN-01)
                var files = new List<string>();
                var filesByEmail = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                uncommitted = composed;
                foreach (var email in composed.Emails)
                {
                    try
                    {
                        var written = await _mailbox.WriteAsync(email, CancellationToken.None).ConfigureAwait(false);
                        files.AddRange(written);
                        filesByEmail[email.Planned.DocId] = written;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        var writeReason = $"Could not write {email.Planned.DocId}.eml: {ex.Message}" + RemoveCopies(uncommitted);
                        uncommitted = null;
                        return await FailAsync(ctx, profile, ThreadFailureKind.Write, writeReason,
                            attempts, inputTokens, outputTokens, spend).ConfigureAwait(false);
                    }
                }

                var cost = Cost(inputTokens, outputTokens, profile);
                var clean = bestViolations.Count == 0;
                var utc = DateTime.UtcNow;
                var attachmentRecords = new List<JournalRecord>();
                var recordOutcomes = new List<(AttachmentOutcome Outcome, JournalRecord Record)>();
                foreach (var attachment in outcomes)
                {
                    var nested = attachment.ToRecord(ctx.Plan.PlanId);
                    if (nested == null)
                        continue;

                    nested.Utc = utc;
                    nested.Files = attachment.Part != null && filesByEmail.TryGetValue(attachment.ParentDocId, out var copies)
                        ? copies.ToList()
                        : new List<string>();
                    attachmentRecords.Add(nested);
                    recordOutcomes.Add((attachment, nested));
                }

                var record = new JournalRecord
                {
                    Type = JournalRecordType.ThreadDone,
                    Utc = utc,
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
                        SignOff = e.SignOff,
                        From = DisplayAddress(e.From),
                        AttachmentTitles = (best.Messages.FirstOrDefault(m => m != null && m.Seq == e.Planned.Seq)?.AttachmentTitles
                                            ?? new List<string>()).ToList()
                    }).ToList(),
                    // D-13: non-relevant threads never write ledger deltas
                    LedgerDelta = profile.UsesLedger ? best.NewDetails : new List<LedgerDelta>(),
                    Attachments = attachmentRecords,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    CostUsd = cost
                };

                try
                {
                    if (BeforeAppendHook != null)
                        await BeforeAppendHook(record).ConfigureAwait(false);
                    await _journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Not journaled: the thread stays pending, so its copies are removed (WR-01); the runner adds this spend to RunEnded
                    var journalReason = $"Could not save the run journal: {ex.Message}" + RemoveCopies(uncommitted);
                    uncommitted = null;
                    _snapshot.AddCost(cost + spend.TextCost, spend.MediaCost);

                    // WR-02: the cause reaches the activity log even though no record or tile changes
                    _snapshot.AddEvent(new GenerationEvent
                    {
                        Kind = GenerationEventKind.NotSaved,
                        LocalTime = DateTime.Now,
                        ThreadId = ctx.Thread.Id,
                        Subject = RootSubject(ctx),
                        WaveIndex = ctx.WaveIndex,
                        Attempt = attempts,
                        Reason = journalReason,
                        Relevant = relevant
                    });
                    return new ThreadOutcome
                    {
                        Kind = ThreadOutcomeKind.Failed,
                        Reason = journalReason,
                        InputTokens = inputTokens + spend.InputTokens,
                        OutputTokens = outputTokens + spend.OutputTokens,
                        CostUsd = cost + spend.TextCost + spend.MediaCost,
                        Unjournaled = true
                    };
                }

                // ----- committed: everything below reports what the journal now holds -----
                uncommitted = null;

                // Tiles update per thread, not per wave
                var status = clean ? ThreadRunStatus.Written : ThreadRunStatus.Warnings;
                _snapshot.RecordThread(ctx.WaveIndex, status, composed.Emails.Count, files.Count, cost, ctx.PreviousStatus, relevant);

                var attachmentDeltas = new List<LedgerDelta>();
                foreach (var (attachment, _) in recordOutcomes)
                {
                    _snapshot.RecordAttachment(attachment.DocId, attachments?.RowIndex ?? ctx.WaveIndex, StatusOf(attachment.Kind),
                        attachment.CostUsd, attachment.MediaCostUsd);
                    AddAttachmentEvent(ctx, attachment);

                    if (!IsCommitted(attachment))
                        continue;

                    committedTexts[attachment.DocId] = attachment.FlattenedText ?? string.Empty;
                    if (attachment.Relevant && profile.UsesLedger)
                        attachmentDeltas.AddRange(attachment.NewDetails);

                    // D-20: the first committed slot per canonical meeting key wins; fallback slots are never shared
                    if (attachments != null && attachment.AttachmentKind == AttachmentType.CalendarInvite && attachment.Relevant
                        && slots.TryGetValue(attachment.DocId, out var slot) && !string.IsNullOrEmpty(slot.MeetingKey))
                        attachments.InviteSlots.TryAdd(slot.MeetingKey, slot);
                }

                // NotRun attachments have no record; their spend still counts
                if (spend.NotRunCost > 0m || spend.NotRunMediaCost > 0m)
                    _snapshot.AddCost(spend.NotRunCost, spend.NotRunMediaCost);

                _snapshot.AddEvent(new GenerationEvent
                {
                    Kind = clean ? GenerationEventKind.ThreadWritten : GenerationEventKind.KeptWithWarnings,
                    LocalTime = DateTime.Now,
                    ThreadId = ctx.Thread.Id,
                    Subject = RootSubject(ctx),
                    WaveIndex = ctx.WaveIndex,
                    Emails = composed.Emails.Count,
                    Details = record.LedgerDelta.Count,
                    Attempt = attempts,
                    Violations = record.Violations.ToList(),
                    FilePath = LastFilePath(ctx),
                    Relevant = relevant,
                    Attachments = parts.Count
                });

                if (cancelled)
                    throw new OperationCanceledException(run.UserToken);

                return new ThreadOutcome
                {
                    Kind = clean ? ThreadOutcomeKind.Written : ThreadOutcomeKind.Warnings,
                    Record = record,
                    Emails = composed.Emails.Count,
                    Files = files.Count,
                    AttachmentRecords = attachmentRecords,
                    AttachmentDeltas = attachmentDeltas,
                    // Checker 06-19: NotRun outcomes have no record, so the runner journals these tokens and this spend in RunEnded
                    NotRunInputTokens = spend.NotRunInput,
                    NotRunOutputTokens = spend.NotRunOutput,
                    NotRunCostUsd = spend.NotRunCost + spend.NotRunMediaCost
                };
            }
            catch (OperationCanceledException)
            {
                RemoveCopies(uncommitted);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var reason = $"Could not write thread {ctx.Thread.Id}: {ex.Message}" + RemoveCopies(uncommitted);
                return await FailAsync(ctx, profile, ThreadFailureKind.Write, reason,
                    attempts, inputTokens, outputTokens, spend).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A MimeKit ParseException (malformed address) or a composer bug must fail only this thread, never escape Task.WhenAll and abort the run (RUN-02)
                var reason = $"Could not build thread {ctx.Thread.Id}: {ex.Message}" + RemoveCopies(uncommitted);
                return await FailAsync(ctx, profile, ThreadFailureKind.Provider, reason,
                    attempts, inputTokens, outputTokens, spend).ConfigureAwait(false);
            }
        }
        finally
        {
            // Successors must never wait forever, and never see uncommitted text
            if (attachments?.Chains != null)
                attachments.Chains.Complete(ctx.Thread.Id, committedTexts);
        }
    }

    /// <summary>
    /// The render context of one planned attachment of an accepted thread (D-14, D-16, D-20): final title and file name,
    /// the accepted parent body and thread, the thread deltas visible at the parent, the chain position and prior text,
    /// and for invites the code-fixed slot. Shared with attachment repair (06-20).
    /// </summary>
    internal static AttachmentRenderContext CreateAttachmentContext(ThreadRenderContext ctx, RenderProfile profile, PlannedMessage parent,
        PlannedAttachment spec, RenderedThread accepted, IReadOnlyList<LedgerDelta> threadDeltas, ThreadAttachmentContext attachments,
        string? priorText, IReadOnlyList<string>? fillerTitles)
    {
        var relevant = ctx.Thread.Relevance == ThreadRelevance.Relevant;
        var links = attachments?.Chains?.Links ?? new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
        var index = (parent.Attachments ?? new List<PlannedAttachment>()).IndexOf(spec);
        var fillerTitle = fillerTitles != null && index >= 0 && index < fillerTitles.Count ? fillerTitles[index] : null;
        var names = AttachmentNaming.NamesFor(parent, fillerTitles, links);
        threadDeltas ??= Array.Empty<LedgerDelta>();

        // Review concern 4: knowledge follows ancestry, not sequence
        var visibleSeqs = new HashSet<int>(KnowledgeGate.Ancestors(ctx.Thread, parent.Seq)) { parent.Seq };

        var actx = new AttachmentRenderContext
        {
            Thread = ctx,
            Parent = parent,
            Spec = spec,
            Title = AttachmentNaming.TitleFor(spec, parent, fillerTitle),
            FileName = names.TryGetValue(spec.DocId ?? string.Empty, out var name) ? name : spec.FileName ?? string.Empty,
            ParentBody = accepted?.Messages.FirstOrDefault(m => m != null && m.Seq == parent.Seq)?.Body ?? string.Empty,
            AcceptedThread = accepted,
            VisibleThreadDeltas = profile.UsesLedger
                ? threadDeltas.Where(d => d != null && visibleSeqs.Contains(d.Seq)).ToList()
                : new List<LedgerDelta>(),
            Chain = links.TryGetValue(spec.DocId ?? string.Empty, out var link) ? link : null,
            PriorVersionText = priorText ?? string.Empty,
            Relevant = relevant,
            IsDecoy = ctx.Thread.Category == NonRelevantCategory.Decoy,
            Tier = profile.Tier,
            SideWorld = profile.SideWorld,
            LateRepair = attachments?.LateRepair ?? false,
            LaterEstablished = attachments?.LaterEstablished ?? new List<LedgerEntry>()
        };
        actx.Rich = AttachmentPrompts.IsRich(actx);

        if (spec.Kind == AttachmentType.CalendarInvite)
        {
            actx.Slot = relevant
                ? InviteScheduler.Resolve(new InviteRequest
                {
                    Thread = ctx.Thread,
                    Parent = parent,
                    Invite = spec,
                    OwnDeltas = threadDeltas,
                    LedgerView = ctx.LedgerView,
                    Guide = ctx.Guide,
                    KnownSlots = attachments?.InviteSlots,
                    WaveIndex = ctx.WaveIndex
                })
                : InviteScheduler.ForNonRelevant(parent, spec);
        }

        return actx;
    }

    // Every planned attachment in seq then plan order; returns true when the user cancelled during the step
    private async Task<bool> RunAttachmentsAsync(ThreadRenderContext ctx, RenderProfile profile, RenderedThread best,
        ThreadAttachmentContext attachments, PlanCallRun run, List<AttachmentOutcome> outcomes, Dictionary<string, InviteSlot> slots,
        AttachmentSpend spend)
    {
        if (_attachments == null)
            return false;

        var messages = Ordered(ctx);
        var ownDocIds = new HashSet<string>(messages
            .SelectMany(m => m.Attachments ?? new List<PlannedAttachment>())
            .Where(a => a != null && !string.IsNullOrEmpty(a.DocId))
            .Select(a => a.DocId), StringComparer.Ordinal);
        var unitTexts = new Dictionary<string, string>(StringComparer.Ordinal);

        // Every delta of the thread known so far: the render's, then each committed-in-unit attachment's (relevant only)
        var threadDeltas = new List<LedgerDelta>();
        if (profile.UsesLedger)
            threadDeltas.AddRange(best.NewDetails);
        var links = attachments.Chains?.Links ?? new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var fillerTitles = best.Messages.FirstOrDefault(m => m != null && m.Seq == message.Seq)?.AttachmentTitles;
            foreach (var spec in (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null && !string.IsNullOrEmpty(a.DocId)))
            {
                // No new attachment call after a fatal stop or Cancel; the finished parts are still committed
                if (run.IsFatal)
                    return false;
                if (run.UserToken.IsCancellationRequested)
                    return true;

                string? prior = null;
                if (links.TryGetValue(spec.DocId, out var link) && link != null && !string.IsNullOrEmpty(link.PreviousDocId))
                {
                    if (ownDocIds.Contains(link.PreviousDocId))
                    {
                        // Same ThreadDone line commits both versions together
                        prior = unitTexts.TryGetValue(link.PreviousDocId, out var text) ? text : null;
                    }
                    else if (attachments.Chains != null)
                    {
                        try
                        {
                            // Waits only on lower-ranked in-step threads, and never inside a PlanCallRun slot
                            prior = await attachments.Chains.PriorTextAsync(spec.DocId, run.UserToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested)
                        {
                            return true;
                        }
                    }
                }

                var actx = CreateAttachmentContext(ctx, profile, message, spec, best, threadDeltas, attachments, prior, fillerTitles);
                AttachmentOutcome outcome;
                var cancelled = false;
                try
                {
                    outcome = await _attachments.RunAsync(actx, run).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested)
                {
                    // Review concern 1: Cancel after acceptance keeps the thread and its finished parts
                    outcome = AttachmentOutcome.For(actx);
                    outcome.Kind = AttachmentOutcomeKind.NotRun;
                    outcome.Reason = CancelledReason;
                    cancelled = true;
                }

                outcomes.Add(outcome);
                spend.Add(outcome);
                if (actx.Slot != null)
                    slots[spec.DocId] = actx.Slot;

                if (IsCommitted(outcome))
                {
                    unitTexts[outcome.DocId] = outcome.FlattenedText ?? string.Empty;
                    if (profile.UsesLedger && outcome.Relevant)
                        threadDeltas.AddRange(outcome.NewDetails);
                }

                if (cancelled)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Journals ThreadFailed with the tokens and cost spent so far (thread and every attachment attempt), counts it and
    /// adds an Activity row. Reports no attachment state. Never throws.
    /// </summary>
    // WR-01: removes every copy of an uncommitted thread (only pending threads run, so no committed EML shares these
    // paths); returns a reason suffix naming copies that could not be removed
    private string RemoveCopies(ComposedThread? composed)
    {
        if (composed == null)
            return string.Empty;

        var left = composed.Emails.Where(e => e?.Planned != null).SelectMany(e => _mailbox.DeleteCopies(e.Planned)).ToList();
        return left.Count == 0
            ? string.Empty
            : $" {left.Count} partial {(left.Count == 1 ? "copy" : "copies")} could not be removed: {string.Join(", ", left)}.";
    }

    private async Task<ThreadOutcome> FailAsync(ThreadRenderContext ctx, RenderProfile profile, ThreadFailureKind kind, string reason,
        int attempts, int inputTokens, int outputTokens, AttachmentSpend spend)
    {
        // JournalReplay sums cost over every ThreadDone and ThreadFailed record, so failures carry their spend too
        var cost = Cost(inputTokens, outputTokens, profile) + spend.TextCost;
        var media = spend.MediaCost;
        var record = new JournalRecord
        {
            Type = JournalRecordType.ThreadFailed,
            Utc = DateTime.UtcNow,
            PlanId = ctx.Plan.PlanId,
            ThreadId = ctx.Thread.Id,
            Attempts = attempts,
            FailureKind = kind,
            Reason = reason,
            InputTokens = inputTokens + spend.InputTokens,
            OutputTokens = outputTokens + spend.OutputTokens,
            CostUsd = cost,
            MediaCostUsd = media
        };

        try
        {
            await _journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Without a journal line the thread stays pending and Continue retries it; the run itself goes on (RUN-02)
        }

        _snapshot.RecordThread(ctx.WaveIndex, ThreadRunStatus.Failed, 0, 0, cost, ctx.PreviousStatus,
            ctx.Thread.Relevance == ThreadRelevance.Relevant);
        if (media > 0m)
            _snapshot.AddCost(0m, media);
        _snapshot.AddEvent(new GenerationEvent
        {
            Kind = GenerationEventKind.ThreadFailed,
            LocalTime = DateTime.Now,
            ThreadId = ctx.Thread.Id,
            Subject = RootSubject(ctx),
            WaveIndex = ctx.WaveIndex,
            Attempt = attempts,
            Reason = reason,
            FilePath = LastFilePath(ctx),
            Relevant = ctx.Thread.Relevance == ThreadRelevance.Relevant
        });

        return new ThreadOutcome
        {
            Kind = ThreadOutcomeKind.Failed,
            Record = record,
            Reason = reason,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            CostUsd = cost + media
        };
    }

    // KeptWithWarnings / Failed / Skipped rows; called only after the ThreadDone line is flushed
    private void AddAttachmentEvent(ThreadRenderContext ctx, AttachmentOutcome attachment)
    {
        var kind = attachment.Kind switch
        {
            AttachmentOutcomeKind.Warnings => GenerationEventKind.AttachmentKeptWithWarnings,
            AttachmentOutcomeKind.Failed => GenerationEventKind.AttachmentFailed,
            AttachmentOutcomeKind.Skipped => GenerationEventKind.AttachmentSkipped,
            _ => (GenerationEventKind?)null
        };
        if (kind == null)
            return;

        _snapshot.AddEvent(new GenerationEvent
        {
            Kind = kind.Value,
            LocalTime = DateTime.Now,
            ThreadId = ctx.Thread.Id,
            Subject = RootSubject(ctx),
            WaveIndex = ctx.WaveIndex,
            Attempt = attachment.Attempts,
            Violations = attachment.Violations.ToList(),
            Reason = attachment.Reason,
            FileName = attachment.FileName,
            Relevant = attachment.Relevant
        });
    }

    private static bool IsCommitted(AttachmentOutcome outcome) =>
        outcome.Kind is AttachmentOutcomeKind.Written or AttachmentOutcomeKind.Warnings;

    private static AttachmentRunStatus StatusOf(AttachmentOutcomeKind kind) => kind switch
    {
        AttachmentOutcomeKind.Written => AttachmentRunStatus.Written,
        AttachmentOutcomeKind.Warnings => AttachmentRunStatus.Warnings,
        AttachmentOutcomeKind.Failed => AttachmentRunStatus.Failed,
        AttachmentOutcomeKind.Skipped => AttachmentRunStatus.Skipped,
        _ => AttachmentRunStatus.Pending
    };

    // A missing or empty message (V1, V5) cannot be composed into a thread
    private static bool IsStructural(List<RenderViolation> violations) => violations.Any(v => v.Code is "V1" or "V5");

    // D-29: priced on the profile tier's model, so Volume filler is not billed at Story rates
    private decimal Cost(int inputTokens, int outputTokens, RenderProfile profile) =>
        (profile.Model ?? _storyModel)?.CalculateTotalCost(inputTokens, outputTokens) ?? 0m;

    private static string DisplayAddress(EmailAddressPart? address)
    {
        var name = address?.Name?.Trim() ?? string.Empty;
        var email = address?.Email?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return email;
        return email.Length == 0 ? name : $"{name} <{email}>";
    }

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

    // Spend of every attachment outcome of one unit; NotRun outcomes are also kept apart (they have no record)
    private sealed class AttachmentSpend
    {
        public int InputTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public decimal TextCost { get; private set; }
        public decimal MediaCost { get; private set; }
        public int NotRunInput { get; private set; }
        public int NotRunOutput { get; private set; }
        public decimal NotRunCost { get; private set; }
        public decimal NotRunMediaCost { get; private set; }

        public void Add(AttachmentOutcome outcome)
        {
            InputTokens += outcome.InputTokens;
            OutputTokens += outcome.OutputTokens;
            TextCost += outcome.CostUsd;
            MediaCost += outcome.MediaCostUsd;
            if (outcome.Kind != AttachmentOutcomeKind.NotRun)
                return;

            NotRunInput += outcome.InputTokens;
            NotRunOutput += outcome.OutputTokens;
            NotRunCost += outcome.CostUsd;
            NotRunMediaCost += outcome.MediaCostUsd;
        }
    }
}
