using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Whole-corpus run state projected from the plan and the replayed journal (D-05, RUN-06): every generatable thread
/// (relevant and non-relevant) and every planned attachment. Pure: the same plan and journal always give the same state,
/// after a finished, cancelled or crashed run. Attachment status comes only from committed, identity-validated records.
/// </summary>
public partial class GenerationRunState
{
    public Dictionary<string, ThreadRunStatus> StatusByThread { get; set; } = new(StringComparer.Ordinal);

    // Last ThreadDone / ThreadFailed record per thread
    public Dictionary<string, JournalRecord> Records { get; set; } = new(StringComparer.Ordinal);

    // Ledger deltas of done relevant threads only (input to LedgerMerger), committed attachment deltas after the thread's own
    public Dictionary<string, List<LedgerDelta>> DeltasByThread { get; set; } = new(StringComparer.Ordinal);

    // Every planned attachment DocId of a generation thread; Pending until a committed record says otherwise
    public Dictionary<string, AttachmentRunStatus> StatusByAttachment { get; set; } = new(StringComparer.Ordinal);

    // Last committed attachment record per DocId (nested in ThreadDone / AttachmentsRepaired of a done thread)
    public Dictionary<string, JournalRecord> AttachmentRecords { get; set; } = new(StringComparer.Ordinal);

    // Filler titles per message DocId, from ThreadDone Messages[].AttachmentTitles (NOISE-05 Pitfall 1)
    public Dictionary<string, List<string>> FillerTitlesByDocId { get; set; } = new(StringComparer.Ordinal);

    // D-20: persisted schedule per meeting key, reused by every later invite for that meeting (review concern 6)
    public Dictionary<string, InviteSlot> InviteSlotsByMeeting { get; set; } = new(StringComparer.Ordinal);

    // All generation threads (relevant + non-relevant)
    public int TotalThreads { get; set; }
    public int Written { get; set; }
    public int Warnings { get; set; }
    public int Failed { get; set; }

    public int RelevantTotal { get; set; }
    public int RelevantWritten { get; set; }
    public int RelevantWarnings { get; set; }
    public int RelevantFailed { get; set; }
    public int NonRelevantTotal { get; set; }
    public int NonRelevantWritten { get; set; }
    public int NonRelevantWarnings { get; set; }
    public int NonRelevantFailed { get; set; }

    public int AttachmentsTotal { get; set; }
    public int AttachmentsWritten { get; set; }
    public int AttachmentsWarnings { get; set; }
    public int AttachmentsFailed { get; set; }
    public int AttachmentsSkipped { get; set; }

    public int EmailsWritten { get; set; }

    // Planned messages of Failed threads
    public int EmailsFailed { get; set; }

    public int FilesWritten { get; set; }
    public int Mailboxes { get; set; }
    public decimal CostUsd { get; set; }

    // Media (image + voicemail) share of CostUsd
    public decimal MediaCostUsd { get; set; }

    public bool HasSideWorld { get; set; }
    public bool HasCastVoices { get; set; }
    public bool LatestAttemptEnded { get; set; }
    public bool HasRun { get; set; }
    public bool PlanChanged { get; set; }
    public string UnreadableReason { get; set; } = string.Empty;

    public int Pending => TotalThreads - Written - Warnings - Failed;

    public bool IsDone(string threadId) =>
        StatusByThread.TryGetValue(threadId ?? string.Empty, out var status)
        && status is ThreadRunStatus.Written or ThreadRunStatus.Warnings;

    public static GenerationRunState From(CorpusPlan plan, JournalReplay replay)
    {
        var state = new GenerationRunState
        {
            HasRun = replay.Exists && (replay.HasRunStarted || replay.LastByThread.Count > 0),
            PlanChanged = replay.PlanChanged,
            UnreadableReason = replay.Reason ?? string.Empty,
            HasSideWorld = replay.LastSideWorld != null,
            HasCastVoices = replay.LastCastVoices != null,
            LatestAttemptEnded = replay.LatestAttemptEnded
        };

        var files = new HashSet<string>(StringComparer.Ordinal);
        var mailboxes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastRecordCost = 0m;

        var threads = GenerationThreads(plan);
        foreach (var thread in threads)
        {
            var relevant = thread.Relevance == ThreadRelevance.Relevant;
            state.TotalThreads++;
            if (relevant)
                state.RelevantTotal++;
            else
                state.NonRelevantTotal++;

            var status = ThreadRunStatus.Pending;
            if (replay.LastByThread.TryGetValue(thread.Id, out var record) && record != null)
            {
                state.Records[thread.Id] = record;
                lastRecordCost += Math.Max(0m, record.CostUsd);

                if (record.Type == JournalRecordType.ThreadDone)
                {
                    status = record.Status == ThreadWriteStatus.Written ? ThreadRunStatus.Written : ThreadRunStatus.Warnings;
                    state.EmailsWritten += record.Messages.Count;

                    foreach (var file in record.Files.Where(f => !string.IsNullOrWhiteSpace(f)))
                    {
                        files.Add(file);
                        // "Mailboxes/{folder}/{box}/{name}.eml": the custodian folder is the second segment
                        var segments = file.Split('/', '\\');
                        if (segments.Length > 1 && segments[1].Length > 0)
                            mailboxes.Add(segments[1]);
                    }

                    foreach (var message in record.Messages.Where(m => m != null && m.DocId.Length > 0 && m.AttachmentTitles.Count > 0))
                        state.FillerTitlesByDocId.TryAdd(message.DocId, message.AttachmentTitles.ToList());
                }
                else if (record.Type == JournalRecordType.ThreadFailed)
                {
                    status = ThreadRunStatus.Failed;
                    state.EmailsFailed += thread.Messages.Count(m => m != null);
                }
            }

            state.StatusByThread[thread.Id] = status;
            var done = status is ThreadRunStatus.Written or ThreadRunStatus.Warnings;
            switch (status)
            {
                case ThreadRunStatus.Written:
                    state.Written++;
                    if (relevant)
                        state.RelevantWritten++;
                    else
                        state.NonRelevantWritten++;
                    break;
                case ThreadRunStatus.Warnings:
                    state.Warnings++;
                    if (relevant)
                        state.RelevantWarnings++;
                    else
                        state.NonRelevantWarnings++;
                    break;
                case ThreadRunStatus.Failed:
                    state.Failed++;
                    if (relevant)
                        state.RelevantFailed++;
                    else
                        state.NonRelevantFailed++;
                    break;
            }

            var attachmentDeltas = new List<LedgerDelta>();
            foreach (var (_, attachment) in AttachmentsOf(thread))
            {
                if (state.StatusByAttachment.ContainsKey(attachment.DocId))
                    continue;

                state.AttachmentsTotal++;
                var attachmentStatus = AttachmentRunStatus.Pending;

                // Review concern 1: only outcomes committed with (or on top of) a written thread count
                if (done && replay.LastByAttachment.TryGetValue(attachment.DocId, out var outcome) && outcome != null)
                {
                    attachmentStatus = StatusOf(outcome);
                    if (attachmentStatus != AttachmentRunStatus.Pending)
                        state.AttachmentRecords[attachment.DocId] = outcome;

                    // D-18: committed attachment details follow the thread's own in plan order; late repairs stay out (review concerns 5, 7)
                    if (attachmentStatus is AttachmentRunStatus.Written or AttachmentRunStatus.Warnings && !outcome.LateRepair)
                        attachmentDeltas.AddRange(outcome.LedgerDelta.Where(d => d != null).Select(d => CopyDelta(d, attachment.DocId)));
                }

                state.StatusByAttachment[attachment.DocId] = attachmentStatus;
                switch (attachmentStatus)
                {
                    case AttachmentRunStatus.Written:
                        state.AttachmentsWritten++;
                        break;
                    case AttachmentRunStatus.Warnings:
                        state.AttachmentsWarnings++;
                        break;
                    case AttachmentRunStatus.Failed:
                        state.AttachmentsFailed++;
                        break;
                    case AttachmentRunStatus.Skipped:
                        state.AttachmentsSkipped++;
                        break;
                }
            }

            // D-13: filler details never reach the narrative ledger
            if (done && relevant && record != null)
                state.DeltasByThread[thread.Id] = record.LedgerDelta.Concat(attachmentDeltas).ToList();
        }

        // D-20: the earliest planned committed invite of a meeting owns its schedule
        foreach (var (_, attachment) in threads.SelectMany(AttachmentsOf)
                     .OrderBy(p => p.Parent.SentAt.UtcDateTime)
                     .ThenBy(p => p.Attachment.DocId, StringComparer.Ordinal))
        {
            if (!state.AttachmentRecords.TryGetValue(attachment.DocId, out var invite)
                || string.IsNullOrWhiteSpace(invite.InviteMeetingKey)
                || invite.InviteStart == null
                || invite.InviteEnd == null
                || state.StatusByAttachment[attachment.DocId] is not (AttachmentRunStatus.Written or AttachmentRunStatus.Warnings))
                continue;

            state.InviteSlotsByMeeting.TryAdd(invite.InviteMeetingKey, new InviteSlot
            {
                Start = invite.InviteStart.Value,
                End = invite.InviteEnd.Value,
                MeetingKey = invite.InviteMeetingKey,
                MatchedId = attachment.DocId
            });
        }

        state.FilesWritten = files.Count;
        state.Mailboxes = mailboxes.Count;

        // D-29: every attempt, including superseded ones (the replay's spend); last records only matter for hand-built replays
        state.CostUsd = Math.Max(lastRecordCost, replay.SpentCostUsd);
        state.MediaCostUsd = Math.Max(0m, replay.MediaSpentCostUsd);
        return state;
    }

    /// <summary>
    /// Attachments of written threads that are missing (no committed record, e.g. after a Phase 5 run), failed, or
    /// skipped when media is available now, grouped by thread. Relevant groups come in wave order (then first send time,
    /// then id) and non-relevant groups last, so repairs run chronologically (D-26, review concern 5).
    /// </summary>
    public List<AttachmentRepairGroup> RepairGroups(CorpusPlan plan, bool mediaAvailable)
    {
        var waveOf = WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan));
        var groups = new List<(AttachmentRepairGroup Group, DateTime First)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var thread in GenerationThreads(plan).Where(t => IsDone(t.Id)))
        {
            var relevant = thread.Relevance == ThreadRelevance.Relevant;
            var group = new AttachmentRepairGroup
            {
                ThreadId = thread.Id,
                Relevant = relevant,
                WaveIndex = relevant && waveOf.TryGetValue(thread.Id, out var wave) ? wave : 0
            };

            foreach (var (parent, attachment) in AttachmentsOf(thread))
            {
                if (!seen.Add(attachment.DocId))
                    continue;

                var status = StatusByAttachment.TryGetValue(attachment.DocId, out var s) ? s : AttachmentRunStatus.Pending;
                var repair = status is AttachmentRunStatus.Pending or AttachmentRunStatus.Failed
                             || (mediaAvailable && status == AttachmentRunStatus.Skipped);
                if (!repair)
                    continue;

                group.AttachmentDocIds.Add(attachment.DocId);
                if (!group.ParentDocIds.Contains(parent.DocId, StringComparer.Ordinal))
                    group.ParentDocIds.Add(parent.DocId);
            }

            if (group.AttachmentDocIds.Count > 0)
                groups.Add((group, FirstSent(thread)));
        }

        return groups
            .OrderBy(g => g.Group.Relevant ? 0 : 1)
            .ThenBy(g => g.Group.WaveIndex)
            .ThenBy(g => g.First)
            .ThenBy(g => g.Group.ThreadId, StringComparer.Ordinal)
            .Select(g => g.Group)
            .ToList();
    }

    // D-26: only the missing parts are generated; the email text is reused from the journal
    public List<string> AttachmentsToRepair(CorpusPlan plan, bool mediaAvailable) =>
        RepairGroups(plan, mediaAvailable).SelectMany(g => g.AttachmentDocIds).ToList();

    /// <summary>
    /// Replayed status and stage row of every planned attachment for GenerationSnapshot.Reset (06-02): repair items on
    /// <paramref name="repairRow"/>, other relevant attachments on their wave row, filler on <paramref name="nonRelevantRow"/>.
    /// </summary>
    public Dictionary<string, AttachmentSeed> AttachmentSeeds(CorpusPlan plan, IReadOnlyDictionary<string, int> waveOf, int nonRelevantRow, int repairRow, bool mediaAvailable)
    {
        // Same row rules and the same mediaAvailable as the runner's RepairGroups and GenerationHistory.Waves, so live and reopened views agree (review 06-23, checker 06-23)
        var repairs = new HashSet<string>(AttachmentsToRepair(plan, mediaAvailable), StringComparer.Ordinal);
        var seeds = new Dictionary<string, AttachmentSeed>(StringComparer.Ordinal);
        foreach (var thread in GenerationThreads(plan))
        {
            var row = nonRelevantRow;
            if (thread.Relevance == ThreadRelevance.Relevant)
                row = waveOf != null && waveOf.TryGetValue(thread.Id, out var wave) ? wave : 0;

            foreach (var (_, attachment) in AttachmentsOf(thread))
            {
                if (seeds.ContainsKey(attachment.DocId))
                    continue;

                seeds[attachment.DocId] = new AttachmentSeed
                {
                    RowIndex = repairs.Contains(attachment.DocId) ? repairRow : row,
                    Status = StatusByAttachment.TryGetValue(attachment.DocId, out var status) ? status : AttachmentRunStatus.Pending
                };
            }
        }

        return seeds;
    }

    /// <summary>
    /// Relevant render assignments plus filler assignments for every non-relevant thread; relevant entries win on a
    /// repeated id.
    /// </summary>
    public static Dictionary<string, ThreadAssignment> AllAssignments(CorpusPlan plan)
    {
        var all = new Dictionary<string, ThreadAssignment>(RenderAssignments.Compute(plan), StringComparer.Ordinal);
        foreach (var (id, assignment) in FillerAssignments.Compute(plan))
            all.TryAdd(id, assignment);
        return all;
    }

    /// <summary>
    /// Answer-key overlay for every generation thread (D-05). Email rows: status, the subject from the same function the
    /// EML writer uses (Pitfall 2) or the journaled voicemail subject, and the journaled voicemail sender. Attachment rows:
    /// status from committed records, the final EML part name (Pitfall 1) and the skip reason. Pass the run's chains so
    /// pending chained attachments show their versioned names.
    /// </summary>
    public AnswerKeyOverlay BuildOverlay(CorpusPlan plan, IReadOnlyDictionary<string, ThreadAssignment> assignments,
        IReadOnlyDictionary<string, AttachmentChainLink>? chains = null)
    {
        var overlay = new AnswerKeyOverlay();
        var links = chains ?? new Dictionary<string, AttachmentChainLink>(StringComparer.Ordinal);
        foreach (var thread in GenerationThreads(plan))
        {
            var status = StatusByThread.TryGetValue(thread.Id, out var s) ? s : ThreadRunStatus.Pending;
            var done = status is ThreadRunStatus.Written or ThreadRunStatus.Warnings;
            var relevant = thread.Relevance == ThreadRelevance.Relevant;
            var label = status switch
            {
                ThreadRunStatus.Written => AnswerKeyBuilder.StatusGenerated,
                ThreadRunStatus.Warnings => AnswerKeyBuilder.StatusGeneratedWithWarnings,
                ThreadRunStatus.Failed => AnswerKeyBuilder.StatusFailed,
                _ => AnswerKeyBuilder.StatusPlanned
            };
            var marking = assignments != null && assignments.TryGetValue(thread.Id, out var assignment) && assignment != null
                ? assignment.Marking
                : PrivilegeMarking.None;
            var journaled = done && Records.TryGetValue(thread.Id, out var record) && record != null
                ? record.Messages.Where(m => m != null && m.DocId.Length > 0)
                    .GroupBy(m => m.DocId, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
                : new Dictionary<string, JournalMessage>(StringComparer.Ordinal);

            foreach (var message in thread.Messages.Where(m => m != null && !string.IsNullOrEmpty(m.DocId)))
            {
                var entry = new AnswerKeyOverlayEntry
                {
                    Status = label,
                    Subject = EmailComposer.SubjectFor(thread, message, marking)
                };

                // UI-SPEC Decision 1: a written voicemail's key row shows what its EML shows, read from the journal
                if (VoicemailComposer.IsVoicemail(thread, message) && journaled.TryGetValue(message.DocId, out var written))
                {
                    if (!string.IsNullOrWhiteSpace(written.Subject))
                        entry.Subject = written.Subject.Trim();

                    // Phase 5 records have no From: that EML keeps its Phase 5 caller From (06-20)
                    if (!string.IsNullOrWhiteSpace(written.From))
                    {
                        entry.From = written.From.Trim();
                        var caller = VoicemailCaller().Match(entry.Subject);
                        if (caller.Success)
                            entry.Note = $"Voicemail from {caller.Groups[1].Value.Trim()}.";
                    }
                }

                overlay.ByDocId[message.DocId] = entry;
                AddAttachments(overlay, message, relevant || done, links);
            }
        }

        return overlay;
    }

    private void AddAttachments(AnswerKeyOverlay overlay, PlannedMessage message, bool finalNames,
        IReadOnlyDictionary<string, AttachmentChainLink> chains)
    {
        if (message.Attachments == null || message.Attachments.Count == 0)
            return;

        // A pending filler attachment keeps its planned name until its titles are journaled with the thread
        var names = finalNames
            ? AttachmentNaming.NamesFor(message, FillerTitlesByDocId.GetValueOrDefault(message.DocId), chains)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attachment in message.Attachments.Where(a => a != null && !string.IsNullOrEmpty(a.DocId)))
        {
            if (overlay.ByDocId.ContainsKey(attachment.DocId))
                continue;

            var status = StatusByAttachment.TryGetValue(attachment.DocId, out var s) ? s : AttachmentRunStatus.Pending;
            AttachmentRecords.TryGetValue(attachment.DocId, out var record);
            var entry = new AnswerKeyOverlayEntry
            {
                Status = status switch
                {
                    AttachmentRunStatus.Written => AnswerKeyBuilder.StatusGenerated,
                    AttachmentRunStatus.Warnings => AnswerKeyBuilder.StatusGeneratedWithWarnings,
                    AttachmentRunStatus.Failed => AnswerKeyBuilder.StatusFailed,
                    AttachmentRunStatus.Skipped => AnswerKeyBuilder.StatusSkipped,
                    _ => AnswerKeyBuilder.StatusPlanned
                },
                // Journaled names passed IsSafeFileName in Normalize (T-06-41)
                FileName = !string.IsNullOrWhiteSpace(record?.FileName)
                    ? record.FileName
                    : names.GetValueOrDefault(attachment.DocId) ?? string.Empty
            };

            if (status == AttachmentRunStatus.Skipped)
            {
                var reason = (record?.Reason ?? string.Empty).Trim().TrimEnd('.');
                entry.Note = reason.Length > 0 ? $"Skipped: {reason}" : "Skipped";
            }

            overlay.ByDocId[attachment.DocId] = entry;
        }
    }

    // "Voicemail from {Caller} ({m:ss})", the subject VoicemailComposer writes
    [GeneratedRegex(@"^Voicemail from (.+) \(\d+:\d{2}\)$")]
    private static partial Regex VoicemailCaller();

    public GenerationCounts ToCounts(CorpusPlan plan, int waveCount, int ledgerEntries, decimal remainingCost, bool running,
        bool finished)
    {
        var threads = GenerationThreads(plan);
        var nonRelevant = threads.Where(t => t.Relevance != ThreadRelevance.Relevant).ToList();
        var attachments = threads.SelectMany(AttachmentsOf).Select(p => p.Attachment).ToList();
        var counts = new GenerationCounts
        {
            TotalThreads = TotalThreads,
            DoneThreads = Written + Warnings,
            WarningThreads = Warnings,
            FailedThreads = Failed,
            RelevantTotal = RelevantTotal,
            RelevantDone = RelevantWritten + RelevantWarnings,
            NonRelevantTotal = NonRelevantTotal,
            NonRelevantDone = NonRelevantWritten + NonRelevantWarnings,
            NonRelevantDailyBusiness = nonRelevant.Count(t => t.Category == NonRelevantCategory.DailyBusiness),
            NonRelevantAncillary = nonRelevant.Count(t => t.Category == NonRelevantCategory.Ancillary),
            NonRelevantBulk = nonRelevant.Count(t => t.Category == NonRelevantCategory.NewsletterSpamPersonal),
            NonRelevantDecoy = nonRelevant.Count(t => t.Category == NonRelevantCategory.Decoy),
            WaveCount = waveCount,
            EmailsPlanned = threads.Sum(t => t.Messages.Count(m => m != null)),
            EmailsWritten = EmailsWritten,
            FilesWritten = FilesWritten,
            Mailboxes = Mailboxes,
            LedgerEntries = ledgerEntries,
            CostUsd = CostUsd,
            MediaCostUsd = MediaCostUsd,
            RemainingCostUsd = remainingCost,
            AttachmentsTotal = AttachmentsTotal,
            AttachmentsDone = AttachmentsWritten + AttachmentsWarnings,
            AttachmentsWarnings = AttachmentsWarnings,
            AttachmentsFailed = AttachmentsFailed,
            AttachmentsSkipped = AttachmentsSkipped,
            AttachmentsWord = attachments.Count(a => a.Kind == AttachmentType.Word),
            AttachmentsExcel = attachments.Count(a => a.Kind == AttachmentType.Excel),
            AttachmentsPowerPoint = attachments.Count(a => a.Kind == AttachmentType.PowerPoint),
            AttachmentsInvite = attachments.Count(a => a.Kind == AttachmentType.CalendarInvite),
            AttachmentsImage = attachments.Count(a => a.Kind == AttachmentType.Image),
            AttachmentsVoicemail = attachments.Count(a => a.Kind == AttachmentType.Voicemail),
            Running = running,
            Finished = finished
        };
        counts.MediaPlanned = counts.AttachmentsImage + counts.AttachmentsVoicemail;
        return counts;
    }

    /// <summary>
    /// Phase 5 heuristic: rough cost of the relevant messages still to write (Pending and Failed threads) on the Story
    /// model. A null model gives 0.
    /// </summary>
    public static decimal EstimateRemaining(CorpusPlan plan, GenerationRunState state, AIModelConfig? model)
    {
        if (model == null)
            return 0m;

        var remaining = RelevantThreads(plan).Where(t => !state.IsDone(t.Id)).Sum(t => t.Messages.Count(m => m != null));
        return remaining * model.CalculateTotalCost(GenerationCostEstimator.RelevantInputPerMessage,
            GenerationCostEstimator.RelevantOutputPerMessage) * GenerationCostEstimator.QaOverhead;
    }

    /// <summary>
    /// Cost of the work still to do: messages and attachments of threads not yet written, attachments to repair, the
    /// side-world call while filler is pending and none is journaled, and the voice-cast call while a voicemail is pending,
    /// media is available and no voice set is journaled.
    /// </summary>
    public static decimal EstimateRemaining(CorpusPlan plan, GenerationRunState state, AIModelConfig? story,
        AIModelConfig? volume, MediaPrices prices, bool mediaAvailable, CaseGuide? guide = null)
    {
        var work = new PricedWork { Guide = guide };
        var repairs = new HashSet<string>(state.AttachmentsToRepair(plan, mediaAvailable), StringComparer.Ordinal);
        foreach (var thread in GenerationThreads(plan))
        {
            var done = state.IsDone(thread.Id);
            if (!done && thread.Relevance != ThreadRelevance.Relevant)
                work.IncludeSideWorld = !state.HasSideWorld;

            foreach (var message in thread.Messages.Where(m => m != null))
            {
                if (!done)
                    work.Messages.Add((thread, message));

                foreach (var attachment in (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null))
                {
                    if (done && !repairs.Contains(attachment.DocId ?? string.Empty))
                        continue;

                    work.Attachments.Add((thread, message, attachment));
                    if (attachment.Kind == AttachmentType.Voicemail && mediaAvailable && !state.HasCastVoices)
                        work.IncludeVoiceCast = true;
                }
            }
        }

        // One pricing path with the Ready card (review 06-15)
        return GenerationCostEstimator.PriceWork(work, story, volume, prices ?? MediaPrices.Defaults(), mediaAvailable).Total;
    }

    private static AttachmentRunStatus StatusOf(JournalRecord record) => record.Type switch
    {
        JournalRecordType.AttachmentDone => record.Status == ThreadWriteStatus.Written
            ? AttachmentRunStatus.Written
            : AttachmentRunStatus.Warnings,
        JournalRecordType.AttachmentFailed => AttachmentRunStatus.Failed,
        JournalRecordType.AttachmentSkipped => AttachmentRunStatus.Skipped,
        _ => AttachmentRunStatus.Pending
    };

    private static LedgerDelta CopyDelta(LedgerDelta delta, string attachmentDocId) => new()
    {
        Seq = delta.Seq,
        Kind = delta.Kind,
        About = delta.About ?? string.Empty,
        Statement = delta.Statement ?? string.Empty,
        Value = delta.Value ?? string.Empty,
        FactIds = (delta.FactIds ?? new List<string>()).ToList(),
        FromAttachment = true,
        AttachmentDocId = attachmentDocId
    };

    private static DateTime FirstSent(PlannedThread thread) =>
        thread.Messages.Where(m => m != null).Select(m => m.SentAt.UtcDateTime).DefaultIfEmpty(DateTime.MaxValue).Min();

    // Planned attachments of a thread in plan order (messages as listed, then attachments as listed)
    private static IEnumerable<(PlannedMessage Parent, PlannedAttachment Attachment)> AttachmentsOf(PlannedThread thread) =>
        thread.Messages
            .Where(m => m != null)
            .SelectMany(m => (m.Attachments ?? new List<PlannedAttachment>())
                .Where(a => a != null && !string.IsNullOrEmpty(a.DocId))
                .Select(a => (m, a)));

    private static IEnumerable<PlannedThread> RelevantThreads(CorpusPlan plan) =>
        (plan?.Threads ?? new List<PlannedThread>())
        .Where(WavePlanner.IsGeneratable);

    // Relevant generatable threads plus non-relevant threads with an id and messages; first copy per id
    private static List<PlannedThread> GenerationThreads(CorpusPlan plan)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return (plan?.Threads ?? new List<PlannedThread>())
            .Where(t => WavePlanner.IsGeneratable(t)
                        || (t != null
                            && t.Relevance == ThreadRelevance.NotRelevant
                            && !string.IsNullOrEmpty(t.Id)
                            && t.Messages?.Any(m => m != null) == true))
            .Where(t => seen.Add(t.Id))
            .ToList();
    }
}

/// <summary>
/// Attachments of one written thread still to generate (D-26), in the order the runner repairs them.
/// </summary>
public class AttachmentRepairGroup
{
    public string ThreadId { get; set; } = string.Empty;
    public bool Relevant { get; set; }

    // 1-based wave of a relevant thread; 0 for non-relevant
    public int WaveIndex { get; set; }

    public List<string> AttachmentDocIds { get; set; } = new();
    public List<string> ParentDocIds { get; set; } = new();
}

/// <summary>
/// Applies a run checkpoint to the CaseDraft: ledger and cached counts into Corpus.Generation (case.json, D-10).
/// </summary>
public static class GenerationStateApplier
{
    // UI thread only: CaseDraft is never mutated from workers (RESEARCH Pattern 8)
    public static void Apply(CaseDraft draft, CorpusPlan plan, GenerationRunState state, NarrativeLedger ledger,
        IReadOnlyList<WrittenCorpusFile> files, GenerationOutcome outcome, string message, DateTime utcNow, bool final)
    {
        draft.Corpus ??= new CorpusState();
        var generation = draft.Corpus.Generation ??= new GenerationState();

        generation.RunPlanId = plan.PlanId;
        generation.Ledger = ledger ?? new NarrativeLedger();
        // ThreadsTotal..ThreadsFailed keep meaning relevant threads (Phase 5 readers)
        generation.ThreadsTotal = state.RelevantTotal;
        generation.ThreadsWritten = state.RelevantWritten;
        generation.ThreadsWithWarnings = state.RelevantWarnings;
        generation.ThreadsFailed = state.RelevantFailed;
        generation.NonRelevantTotal = state.NonRelevantTotal;
        generation.NonRelevantWritten = state.NonRelevantWritten;
        generation.NonRelevantWithWarnings = state.NonRelevantWarnings;
        generation.NonRelevantFailed = state.NonRelevantFailed;
        generation.AttachmentsTotal = state.AttachmentsTotal;
        generation.AttachmentsWritten = state.AttachmentsWritten;
        generation.AttachmentsWithWarnings = state.AttachmentsWarnings;
        generation.AttachmentsFailed = state.AttachmentsFailed;
        generation.AttachmentsSkipped = state.AttachmentsSkipped;
        generation.MediaCostUsd = state.MediaCostUsd;
        generation.EmailsWritten = state.EmailsWritten;
        generation.EmailsFailed = state.EmailsFailed;
        generation.FilesWritten = state.FilesWritten;
        generation.Mailboxes = state.Mailboxes;
        generation.CostUsd = state.CostUsd;
        generation.ProjectedUtc = utcNow;

        // Re-stamp the files this checkpoint rewrote (same Kind replaced); others stay as recorded
        CorpusApproval.RecordPartialWrites(draft, files ?? new List<WrittenCorpusFile>());

        if (!final)
            return;

        generation.LastOutcome = outcome;
        generation.LastMessage = message ?? string.Empty;
        generation.LastRunEndedUtc = utcNow;
        draft.AdvanceStage(CaseStage.Generation);
    }

    /// <summary>
    /// Start Over: drops the generation state and the story-so-far file record. The caller deletes files and the journal.
    /// </summary>
    public static void Clear(CaseDraft draft)
    {
        var corpus = draft.Corpus;
        if (corpus == null)
            return;

        corpus.Generation = null;
        corpus.WrittenFiles = corpus.WrittenFiles.Where(f => f != null && f.Kind != CorpusFileKind.StorySoFar).ToList();
    }
}
