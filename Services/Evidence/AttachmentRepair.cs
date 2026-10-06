using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// How one thread's attachment repair ended. Committed path: Records are the attachment records nested in the
/// AttachmentsRepaired line, Deltas the committed relevant details (empty for a late repair), NotRun* the spend of calls
/// with no record. Unjournaled path (the line could not be saved, or the repair broke before its commit): nothing is
/// reported and InputTokens / OutputTokens / CostUsd (text) + MediaCostUsd (media) hold every call; NotRun* stay 0.
/// </summary>
public class AttachmentRepairOutcome
{
    // Parts committed into the rewritten copies
    public int Added { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int FilesRewritten { get; set; }

    public List<JournalRecord> Records { get; set; } = new();
    public List<LedgerDelta> Deltas { get; set; } = new();

    // Every call of the repair, informational on the committed path (the nested records carry it)
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public decimal MediaCostUsd { get; set; }

    // PROV-07: gateway detail of CostUsd (text of every call)
    public CostBreakdown Cost { get; set; } = new();

    public bool Unjournaled { get; set; }
    public string Reason { get; set; } = string.Empty;

    // Committed path only: NotRun attachments have no record, so these tokens and this spend (text + media) go to RunEnded
    public int NotRunInputTokens { get; set; }
    public int NotRunOutputTokens { get; set; }
    public decimal NotRunCostUsd { get; set; }

    // PROV-07: text detail and media share of NotRunCostUsd
    public CostBreakdown NotRunCost { get; set; } = new();
    public decimal NotRunMediaCostUsd { get; set; }
}

/// <summary>
/// Adds the missing, failed or (with media available now) skipped attachments of an already written thread (D-26, RUN-02):
/// rebuilds the accepted thread from the journal, harvests the committed parts per parent message from any custodian copy,
/// generates only what is missing, rewrites every copy from the journaled text and commits one AttachmentsRepaired line.
/// Never asks for or changes email text; status, events, chain text and invite slots follow the commit (review concern 1).
/// </summary>
public class AttachmentRepair
{
    public const string KeptExistingPrefix = "kept the existing email";

    private readonly GenerationJournal _journal;
    private readonly MailboxWriter _mailbox;
    private readonly AttachmentUnitOfWork _attachments;
    private readonly GenerationSnapshot _snapshot;
    private readonly string _caseFolder;
    private readonly IReadOnlyDictionary<string, string> _custodianFolders;

    public AttachmentRepair(GenerationJournal journal, MailboxWriter mailbox, AttachmentUnitOfWork attachments,
        GenerationSnapshot snapshot, string caseFolder, IReadOnlyDictionary<string, string> custodianFolders)
    {
        _journal = journal;
        _mailbox = mailbox;
        _attachments = attachments;
        _snapshot = snapshot;
        _caseFolder = caseFolder;
        _custodianFolders = custodianFolders;
    }

    // Test seam: runs after every rewrite and just before the AttachmentsRepaired append
    internal Func<JournalRecord, Task>? BeforeAppendHook { get; set; }

    /// <summary>
    /// Repairs one written thread. Throws only when the user cancels, after the finished parts were committed. Every other
    /// problem becomes Failed records or an Unjournaled outcome for this thread (RUN-02).
    /// </summary>
    internal async Task<AttachmentRepairOutcome> RepairThreadAsync(ThreadRenderContext ctx, RenderProfile profile, JournalRecord threadDone,
        IReadOnlyDictionary<string, JournalRecord> attachmentRecords, AttachmentRepairGroup group, ThreadAttachmentContext actx,
        PlanCallRun run)
    {
        var result = new AttachmentRepairOutcome();
        var outcomes = new List<AttachmentOutcome>();
        var committedTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var thread = ctx.Thread;
        var relevant = thread.Relevance == ThreadRelevance.Relevant;
        var cancelled = false;

        try
        {
            try
            {
                var messages = (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).OrderBy(m => m.Seq).ToList();

                // 1. The accepted thread exactly as journaled; a message without saved text cannot be recomposed
                var accepted = Accepted(thread, threadDone, messages);
                if (accepted == null)
                {
                    result.Reason = $"The saved text of thread {thread.Id} is incomplete.";
                    result.Unjournaled = true;
                    AddNotSavedEvent(ctx, result.Reason);
                    return result;
                }

                var wanted = new HashSet<string>(group?.AttachmentDocIds ?? new List<string>(), StringComparer.Ordinal);
                var records = attachmentRecords ?? new Dictionary<string, JournalRecord>(StringComparer.Ordinal);

                var links = actx?.Chains?.Links ?? new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
                var titlesByDocId = (threadDone.Messages ?? new List<JournalMessage>())
                    .Where(m => m != null && !string.IsNullOrEmpty(m.DocId))
                    .GroupBy(m => m.DocId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().AttachmentTitles ?? new List<string>(), StringComparer.Ordinal);

                // 2. Committed parts per parent message, read back from any copy that has them (review concern 3)
                var reused = new Dictionary<string, ComposedAttachment>(StringComparer.Ordinal);
                var regenerate = new HashSet<string>(StringComparer.Ordinal);
                var committedByMessage = new Dictionary<int, List<string>>();
                foreach (var message in messages)
                {
                    var committed = Specs(message)
                        .Where(a => !wanted.Contains(a.DocId) && records.TryGetValue(a.DocId, out var r) && IsCommitted(r))
                        .Select(a => a.DocId)
                        .ToList();
                    committedByMessage[message.Seq] = committed;
                    if (committed.Count == 0)
                        continue;

                    var harvested = AttachmentHarvester.HarvestMessage(_caseFolder, MailboxWriter.CopyPathsFor(message, _custodianFolders), committed);
                    var domain = ctx.People.Find(message.FromId)?.Domain;

                    // IN-02: Normalize blanks an unsafe journaled name; the planned name keeps the reused part in the EML
                    var planned = AttachmentNaming.NamesFor(message,
                        titlesByDocId.TryGetValue(message.DocId ?? string.Empty, out var savedTitles) && savedTitles.Count > 0 ? savedTitles : null,
                        links);
                    foreach (var spec in Specs(message).Where(a => committed.Contains(a.DocId)))
                    {
                        var rebuilt = harvested.TryGetValue(spec.DocId, out var part) && part.Bytes.Length > 0
                            ? Rebuilt(spec, records[spec.DocId], part, domain, planned.GetValueOrDefault(spec.DocId))
                            : null;
                        if (rebuilt != null && MailboxWriter.CanCarry(rebuilt))
                            reused[spec.DocId] = rebuilt;
                        else
                            regenerate.Add(spec.DocId);
                    }
                }

                // 3. Only what is missing, in seq then plan order; a fatal stop or Cancel starts nothing new
                var threadDeltas = new List<LedgerDelta>();
                if (profile.UsesLedger)
                {
                    threadDeltas.AddRange(threadDone.LedgerDelta ?? new List<LedgerDelta>());
                    foreach (var docId in messages.SelectMany(Specs).Select(a => a.DocId).Where(d => reused.ContainsKey(d)))
                        threadDeltas.AddRange(records[docId].LedgerDelta ?? new List<LedgerDelta>());
                }

                var ownDocIds = new HashSet<string>(messages.SelectMany(Specs).Select(a => a.DocId), StringComparer.Ordinal);
                var repairTexts = new Dictionary<string, string>(StringComparer.Ordinal);
                var slots = new Dictionary<string, InviteSlot>(StringComparer.Ordinal);

                foreach (var message in messages)
                {
                    if (cancelled || run.IsFatal)
                        break;

                    var fillerTitles = titlesByDocId.TryGetValue(message.DocId ?? string.Empty, out var titles) && titles.Count > 0 ? titles : null;
                    foreach (var spec in Specs(message).Where(a => wanted.Contains(a.DocId) || regenerate.Contains(a.DocId)))
                    {
                        if (run.IsFatal)
                            break;
                        if (run.UserToken.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }

                        string? prior = null;
                        if (links.TryGetValue(spec.DocId, out var link) && link != null && !string.IsNullOrEmpty(link.PreviousDocId))
                        {
                            if (repairTexts.TryGetValue(link.PreviousDocId, out var fresh))
                            {
                                // A same-thread predecessor created earlier in this repair
                                prior = fresh;
                            }
                            else if (ownDocIds.Contains(link.PreviousDocId) && !regenerate.Contains(link.PreviousDocId)
                                     && !wanted.Contains(link.PreviousDocId))
                            {
                                // Reused same-thread predecessor: its committed text (the scheduler never waits on our own thread)
                                prior = records.TryGetValue(link.PreviousDocId, out var r) && IsCommitted(r) && !string.IsNullOrEmpty(r.FlattenedText)
                                    ? r.FlattenedText
                                    : null;
                            }
                            else if (!ownDocIds.Contains(link.PreviousDocId) && actx?.Chains != null)
                            {
                                try
                                {
                                    // Lower-ranked predecessors were awaited before the repair slot, so this never blocks a slot holder
                                    prior = await actx.Chains.PriorTextAsync(spec.DocId, run.UserToken).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested)
                                {
                                    cancelled = true;
                                    break;
                                }
                            }
                        }

                        var context = ThreadUnitOfWork.CreateAttachmentContext(ctx, profile, message, spec, accepted, threadDeltas, actx!,
                            prior, fillerTitles);
                        AttachmentOutcome outcome;
                        try
                        {
                            outcome = await _attachments.RunAsync(context, run).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested)
                        {
                            // Review concern 1: Cancel keeps the parts already finished in this repair
                            outcome = AttachmentOutcome.For(context);
                            outcome.Kind = AttachmentOutcomeKind.NotRun;
                            outcome.Reason = ThreadUnitOfWork.CancelledReason;
                            cancelled = true;
                        }

                        outcomes.Add(outcome);
                        if (context.Slot != null)
                            slots[spec.DocId] = context.Slot;

                        if (IsCommitted(outcome))
                        {
                            repairTexts[outcome.DocId] = outcome.FlattenedText ?? string.Empty;
                            if (profile.UsesLedger && outcome.Relevant)
                                threadDeltas.AddRange(outcome.NewDetails);
                        }

                        if (cancelled)
                            break;
                    }
                }

                // IN-02: a created part the writer would drop is Failed, and the no-loss rule below sees it as not created
                foreach (var outcome in outcomes.Where(o => IsCommitted(o) && o.Part != null && !MailboxWriter.CanCarry(o.Part)))
                    ThreadUnitOfWork.FailUncarriable(outcome);

                // 4. No-loss rule per message
                var excluded = new Dictionary<int, string>();
                foreach (var message in messages)
                {
                    var lost = committedByMessage[message.Seq]
                        .Where(d => !reused.ContainsKey(d) && !outcomes.Any(o => o.DocId == d && IsCommitted(o)))
                        .ToList();
                    if (lost.Count == 0)
                        continue;

                    // Review 06-20: a failed regeneration must never strip a part the copies may still hold
                    var file = records[lost[0]].FileName;
                    excluded[message.Seq] = $"{KeptExistingPrefix}: {file} could not be reused or created again";
                }

                foreach (var outcome in outcomes.Where(o => o.Kind != AttachmentOutcomeKind.NotRun && excluded.ContainsKey(o.ParentSeq)))
                    MarkFailed(outcome, excluded[outcome.ParentSeq]);

                // 5. Recompose from the journaled text; rewrite every copy only when a part was added
                var parts = new Dictionary<string, ComposedAttachment>(StringComparer.Ordinal);
                foreach (var message in messages)
                {
                    foreach (var spec in Specs(message))
                    {
                        if (reused.TryGetValue(spec.DocId, out var kept))
                        {
                            parts[spec.DocId] = kept;
                            continue;
                        }

                        if (excluded.ContainsKey(message.Seq))
                        {
                            // Not written: the placeholder only keeps later quotes ("[image: ...]") as the copies on disk show them
                            if (committedByMessage[message.Seq].Contains(spec.DocId))
                                parts[spec.DocId] = Placeholder(spec, records[spec.DocId], ctx.People.Find(message.FromId)?.Domain);
                            continue;
                        }

                        var created = outcomes.FirstOrDefault(o => o.DocId == spec.DocId && IsCommitted(o) && o.Part != null);
                        if (created != null)
                            parts[spec.DocId] = created.Part!;
                    }
                }

                var files = new List<string>();
                var filesByEmail = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                if (outcomes.Any(IsCommitted))
                {
                    var legacy = (threadDone.Messages ?? new List<JournalMessage>()).All(m => string.IsNullOrEmpty(m?.From));
                    var composed = EmailComposer.Compose(ctx.Plan.PlanId, thread, ctx.Assignment, accepted, ctx.People, parts, voicemailNotice: !legacy);
                    foreach (var email in composed.Emails.Where(e => !excluded.ContainsKey(e.Planned.Seq)))
                    {
                        try
                        {
                            var written = await _mailbox.WriteAsync(email, CancellationToken.None).ConfigureAwait(false);
                            files.AddRange(written);
                            filesByEmail[email.Planned.DocId] = written;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // The next repair rewrites every copy again, so partial rewrites converge (review 06-20)
                            var reason = $"Could not rewrite {email.Planned.DocId}.eml: {ex.Message}";
                            foreach (var outcome in outcomes.Where(IsCommitted).ToList())
                                MarkFailed(outcome, reason);
                            break;
                        }
                    }
                }

                // 6. One durable line for the whole repair
                var utc = DateTime.UtcNow;
                var recordOutcomes = new List<(AttachmentOutcome Outcome, JournalRecord Record)>();
                foreach (var outcome in outcomes)
                {
                    var nested = outcome.ToRecord(ctx.Plan.PlanId);
                    if (nested == null)
                        continue;

                    nested.Utc = utc;
                    nested.Files = IsCommitted(outcome) && filesByEmail.TryGetValue(outcome.ParentDocId, out var copies)
                        ? copies.ToList()
                        : new List<string>();
                    recordOutcomes.Add((outcome, nested));
                }

                AddSpend(result, outcomes);
                if (recordOutcomes.Count == 0)
                {
                    // Nothing to journal: only NotRun calls (fatal or cancelled), whose spend goes to RunEnded
                    AddNotRun(result, outcomes);
                    if (result.NotRunCostUsd > 0m)
                        _snapshot.AddCost(NotRunText(outcomes), NotRunMedia(outcomes));
                    if (cancelled)
                        throw new OperationCanceledException(run.UserToken);
                    return result;
                }

                var line = new JournalRecord
                {
                    Type = JournalRecordType.AttachmentsRepaired,
                    Utc = utc,
                    PlanId = ctx.Plan.PlanId,
                    ThreadId = thread.Id,
                    LateRepair = actx?.LateRepair ?? false,
                    Attachments = recordOutcomes.Select(r => r.Record).ToList(),
                    Files = files,
                    // The nested records carry the spend, so replay counts it once
                    CostUsd = 0m,
                    MediaCostUsd = 0m
                };

                try
                {
                    if (BeforeAppendHook != null)
                        await BeforeAppendHook(line).ConfigureAwait(false);
                    await _journal.AppendAsync(line, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The copies may hold parts the journal does not list; the items stay pending and the next repair rewrites them
                    _snapshot.AddCost(result.CostUsd, result.MediaCostUsd);
                    result.Unjournaled = true;
                    result.Reason = $"Could not save the run journal: {ex.Message}";
                    AddNotSavedEvent(ctx, result.Reason);
                    if (cancelled)
                        throw new OperationCanceledException(run.UserToken);
                    return result;
                }

                // ----- committed: everything below reports what the journal now holds -----
                var added = 0;
                foreach (var (outcome, record) in recordOutcomes)
                {
                    _snapshot.RecordAttachment(outcome.DocId, actx?.RowIndex ?? ctx.WaveIndex, StatusOf(outcome.Kind), outcome.CostUsd,
                        outcome.MediaCostUsd);
                    AddAttachmentEvent(ctx, outcome);
                    result.Records.Add(record);

                    switch (outcome.Kind)
                    {
                        case AttachmentOutcomeKind.Failed:
                            result.Failed++;
                            continue;
                        case AttachmentOutcomeKind.Skipped:
                            result.Skipped++;
                            continue;
                    }

                    added++;
                    committedTexts[outcome.DocId] = outcome.FlattenedText ?? string.Empty;
                    if (regenerate.Contains(outcome.DocId))
                    {
                        _snapshot.AddEvent(new GenerationEvent
                        {
                            Kind = GenerationEventKind.AttachmentRegenerated,
                            LocalTime = DateTime.Now,
                            ThreadId = thread.Id,
                            Subject = RootSubject(ctx),
                            WaveIndex = ctx.WaveIndex,
                            FileName = outcome.FileName,
                            Relevant = outcome.Relevant
                        });
                    }

                    // Review concern 5: a late repair never extends the story later emails already told
                    if (outcome.Relevant && profile.UsesLedger && !(actx?.LateRepair ?? false))
                        result.Deltas.AddRange(outcome.NewDetails);

                    // D-20: fallback slots are never shared; the earliest planned committed slot per meeting key wins at the step end
                    if (actx != null && outcome.AttachmentKind == AttachmentType.CalendarInvite && outcome.Relevant
                        && slots.TryGetValue(outcome.DocId, out var slot) && !string.IsNullOrEmpty(slot.MeetingKey))
                        actx.CommittedInvites[outcome.DocId] = ThreadUnitOfWork.CommittedInviteOf(thread, outcome.ParentDocId, outcome.DocId, slot);
                }

                result.Added = added;
                result.FilesRewritten = files.Count;
                AddNotRun(result, outcomes);
                if (result.NotRunCostUsd > 0m)
                    _snapshot.AddCost(NotRunText(outcomes), NotRunMedia(outcomes));

                if (added > 0)
                {
                    _snapshot.AddEvent(new GenerationEvent
                    {
                        Kind = GenerationEventKind.AttachmentsAdded,
                        LocalTime = DateTime.Now,
                        ThreadId = thread.Id,
                        Subject = RootSubject(ctx),
                        WaveIndex = ctx.WaveIndex,
                        Attachments = added,
                        Files = files.Count,
                        FilePath = messages.LastOrDefault()?.FilePath ?? string.Empty,
                        Relevant = relevant
                    });
                }

                if (cancelled)
                    throw new OperationCanceledException(run.UserToken);

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Broken(ctx, result, outcomes, $"Could not repair thread {thread.Id}: {ex.Message}");
            }
            catch (Exception ex)
            {
                // RUN-02: one thread's repair never aborts the run
                return Broken(ctx, result, outcomes, $"Could not repair thread {thread.Id}: {ex.Message}");
            }
        }
        finally
        {
            // Successors must never wait forever, and never see uncommitted text
            actx?.Chains?.Complete(thread.Id, committedTexts);
        }
    }

    // The ThreadDone text as a RenderedThread (Body = BodyPlain, by DocId); null when a planned message has no saved text.
    // Shared with the QA scans (CorpusScans), which re-check the accepted text
    internal static RenderedThread? Accepted(PlannedThread thread, JournalRecord threadDone, List<PlannedMessage> messages)
    {
        var byDocId = (threadDone?.Messages ?? new List<JournalMessage>())
            .Where(m => m != null && !string.IsNullOrEmpty(m.DocId))
            .GroupBy(m => m.DocId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rendered = new RenderedThread
        {
            ThreadId = thread.Id,
            NewDetails = (threadDone?.LedgerDelta ?? new List<LedgerDelta>()).ToList()
        };
        foreach (var message in messages)
        {
            if (!byDocId.TryGetValue(message.DocId ?? string.Empty, out var saved))
                return null;

            rendered.Messages.Add(new RenderedMessage
            {
                Seq = message.Seq,
                Body = saved.BodyPlain ?? string.Empty,
                SignOff = saved.SignOff ?? string.Empty,
                AttachmentTitles = (saved.AttachmentTitles ?? new List<string>()).ToList()
            });
        }

        return rendered;
    }

    // A committed part rebuilt from its record and the harvested bytes; an inline image keeps its own Content-Id
    private static ComposedAttachment Rebuilt(PlannedAttachment spec, JournalRecord record, HarvestedPart part, string? senderDomain,
        string? plannedName)
    {
        var kind = spec.Kind;
        var inline = kind == AttachmentType.Image && spec.IsInline;
        return new ComposedAttachment
        {
            DocId = spec.DocId,
            Kind = kind,
            FileName = CorpusPlan.IsSafeFileName(record.FileName) ? record.FileName : plannedName ?? string.Empty,
            Title = record.Title ?? string.Empty,
            MimeType = MimeTypeFor(kind),
            Bytes = part.Bytes,
            IsInline = inline,
            ContentId = !inline ? string.Empty
                : part.ContentId.Length > 0 ? part.ContentId : AttachmentNaming.ContentIdFor(spec.DocId, senderDomain)
        };
    }

    // Identity only (no bytes) for an email that is not rewritten
    private static ComposedAttachment Placeholder(PlannedAttachment spec, JournalRecord record, string? senderDomain)
    {
        var inline = spec.Kind == AttachmentType.Image && spec.IsInline;
        return new ComposedAttachment
        {
            DocId = spec.DocId,
            Kind = spec.Kind,
            FileName = record.FileName ?? string.Empty,
            Title = record.Title ?? string.Empty,
            MimeType = MimeTypeFor(spec.Kind),
            Bytes = Array.Empty<byte>(),
            IsInline = inline,
            ContentId = inline ? AttachmentNaming.ContentIdFor(spec.DocId, senderDomain) : string.Empty
        };
    }

    private static void MarkFailed(AttachmentOutcome outcome, string reason)
    {
        outcome.Kind = AttachmentOutcomeKind.Failed;
        outcome.FailureKind = ThreadFailureKind.Write;
        outcome.Reason = reason;
        outcome.Part = null;
        outcome.NewDetails = new List<LedgerDelta>();
    }

    // Spend of every call so far, reported on an Unjournaled outcome
    private AttachmentRepairOutcome Broken(ThreadRenderContext ctx, AttachmentRepairOutcome result, List<AttachmentOutcome> outcomes,
        string reason)
    {
        result.InputTokens = 0;
        result.OutputTokens = 0;
        result.CostUsd = 0m;
        result.MediaCostUsd = 0m;
        result.Cost = new CostBreakdown();
        AddSpend(result, outcomes);
        result.NotRunInputTokens = 0;
        result.NotRunOutputTokens = 0;
        result.NotRunCostUsd = 0m;
        result.NotRunCost = new CostBreakdown();
        result.NotRunMediaCostUsd = 0m;
        result.Records.Clear();
        result.Deltas.Clear();
        result.Unjournaled = true;
        result.Reason = reason;
        if (result.CostUsd > 0m || result.MediaCostUsd > 0m)
            _snapshot.AddCost(result.CostUsd, result.MediaCostUsd);
        AddNotSavedEvent(ctx, reason);
        return result;
    }

    // WR-02: an Unjournaled repair changes no record or tile, so its cause is shown as its own activity row
    private void AddNotSavedEvent(ThreadRenderContext ctx, string reason)
    {
        _snapshot.AddEvent(new GenerationEvent
        {
            Kind = GenerationEventKind.NotSaved,
            LocalTime = DateTime.Now,
            ThreadId = ctx.Thread.Id,
            Subject = RootSubject(ctx),
            WaveIndex = ctx.WaveIndex,
            Reason = reason,
            Relevant = ctx.Thread.Relevance == ThreadRelevance.Relevant
        });
    }

    private static void AddSpend(AttachmentRepairOutcome result, List<AttachmentOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
        {
            result.InputTokens += outcome.InputTokens;
            result.OutputTokens += outcome.OutputTokens;
            result.CostUsd += outcome.CostUsd;
            result.Cost.Add(outcome.Cost);
            result.MediaCostUsd += outcome.MediaCostUsd;
        }
    }

    // Checker 06-19: NotRun outcomes have no record, so the runner journals these tokens and this spend in RunEnded
    private static void AddNotRun(AttachmentRepairOutcome result, List<AttachmentOutcome> outcomes)
    {
        var notRun = outcomes.Where(o => o.Kind == AttachmentOutcomeKind.NotRun).ToList();
        result.NotRunInputTokens = notRun.Sum(o => o.InputTokens);
        result.NotRunOutputTokens = notRun.Sum(o => o.OutputTokens);
        result.NotRunCostUsd = notRun.Sum(o => o.CostUsd + o.MediaCostUsd);
        result.NotRunCost = new CostBreakdown();
        foreach (var outcome in notRun)
            result.NotRunCost.Add(outcome.Cost);
        result.NotRunMediaCostUsd = notRun.Sum(o => o.MediaCostUsd);
    }

    private static decimal NotRunText(List<AttachmentOutcome> outcomes) =>
        outcomes.Where(o => o.Kind == AttachmentOutcomeKind.NotRun).Sum(o => o.CostUsd);

    private static decimal NotRunMedia(List<AttachmentOutcome> outcomes) =>
        outcomes.Where(o => o.Kind == AttachmentOutcomeKind.NotRun).Sum(o => o.MediaCostUsd);

    // KeptWithWarnings / Failed / Skipped rows; called only after the AttachmentsRepaired line is flushed
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

    private static IEnumerable<PlannedAttachment> Specs(PlannedMessage message) =>
        (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null && !string.IsNullOrEmpty(a.DocId));

    private static bool IsCommitted(JournalRecord record) => record != null && record.Type == JournalRecordType.AttachmentDone;

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

    private static string RootSubject(ThreadRenderContext ctx)
    {
        var root = (ctx.Thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).OrderBy(m => m.Seq).FirstOrDefault();
        return root == null
            ? ctx.Thread.Subject ?? string.Empty
            : EmailComposer.SubjectFor(ctx.Thread, root, ctx.Assignment?.Marking ?? PrivilegeMarking.None);
    }

    // Same types as AttachmentUnitOfWork and MailboxWriter
    private static string MimeTypeFor(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        AttachmentType.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        AttachmentType.PowerPoint => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        AttachmentType.Image => "image/png",
        AttachmentType.CalendarInvite => "text/calendar",
        AttachmentType.Voicemail => "audio/mpeg",
        _ => "application/octet-stream"
    };
}
