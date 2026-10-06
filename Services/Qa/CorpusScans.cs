using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Qa;

/// <summary>
/// Post-hoc consistency scans of the written corpus (QA-03, D-15). Relevant threads are re-checked with the tuned
/// render rules (V4 timeline, V3 canonical value variants, V6 ledger contradictions) against the ledger as it stood
/// before their wave; their attachments get the attachment check; non-relevant mail is scanned for printed case values.
/// Journaled violations of Warnings threads are merged in. Free code checks only (no model call), report only, and
/// never throws for bad input: a rule or thread that cannot be checked is skipped.
/// </summary>
public static partial class CorpusScans
{
    private const string TimelineCode = "V4";
    private const string ValueCode = "V3";
    private const string StoryDetailCode = "V6";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // "msg 3: mentions ..." as journaled by the thread and attachment units
    [GeneratedRegex(@"^msg (\d+): (.*)$", RegexOptions.Singleline, 1000)]
    private static partial Regex JournaledSeq();

    /// <summary>
    /// Scans every written (ThreadDone) thread of the replay; queued (invalidated) threads are skipped. Findings are
    /// ordered by date sent, then DocId, check and text. <paramref name="overlay"/> supplies the subjects the answer key
    /// shows; when null the run's full overlay is rebuilt from the replay. JournalUtc is left for the caller to set.
    /// </summary>
    public static ScanReport Run(CaseDraft draft, CorpusPlan plan, JournalReplay replay, AnswerKeyOverlay? overlay = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(replay);

        var guide = draft.Guide ?? new CaseGuide();
        var report = new ScanReport { PlanId = plan.PlanId, CreatedUtc = DateTime.UtcNow };

        var state = GenerationRunState.From(plan, replay);
        overlay ??= state.BuildFullOverlay(plan, guide);
        var scaffold = GenerationRunner.BuildScaffold(draft, plan);

        var rows = new Dictionary<string, AnswerKeyRow>(StringComparer.Ordinal);
        foreach (var row in AnswerKeyBuilder.Build(plan, guide, overlay))
        {
            if (row != null && row.DocType == AnswerKeyBuilder.DocTypeEmail && !string.IsNullOrEmpty(row.DocId))
                rows.TryAdd(row.DocId, row);
        }

        var threads = (plan.Threads ?? new List<PlannedThread>())
            .Where(t => t != null && !string.IsNullOrEmpty(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var attachmentsByParent = (replay.LastByAttachment ?? new Dictionary<string, JournalRecord>())
            .Where(kv => kv.Value != null && kv.Value.Type == JournalRecordType.AttachmentDone)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .GroupBy(kv => kv.Value.ParentDocId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Value).ToList(), StringComparer.Ordinal);

        var findings = new List<ScanFinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var invalidated = replay.InvalidatedThreadIds ?? new HashSet<string>(StringComparer.Ordinal);

        foreach (var (threadId, record) in (replay.LastByThread ?? new Dictionary<string, JournalRecord>())
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (record == null || record.Type != JournalRecordType.ThreadDone || invalidated.Contains(threadId)
                || !threads.TryGetValue(threadId, out var thread))
                continue;

            var messages = (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).OrderBy(m => m.Seq).ToList();
            var written = new HashSet<string>((record.Messages ?? new List<JournalMessage>())
                .Where(m => m != null && !string.IsNullOrEmpty(m.DocId)).Select(m => m.DocId), StringComparer.Ordinal);
            report.ScannedEmails += messages.Count(m => written.Contains(m.DocId ?? string.Empty));

            var scope = new ThreadScope(thread, messages, record, rows, findings, seen);
            var attachments = messages
                .SelectMany(m => attachmentsByParent.TryGetValue(m.DocId ?? string.Empty, out var list) ? list : new List<JournalRecord>())
                .ToList();

            if (thread.Relevance == ThreadRelevance.Relevant)
                ScanRelevant(scope, draft, plan, guide, scaffold, state, attachments);
            else
                ScanNonRelevant(scope, guide, attachments);
        }

        report.Findings = findings
            .OrderBy(f => f.DateSent)
            .ThenBy(f => f.DocId, StringComparer.Ordinal)
            .ThenBy(f => f.Check)
            .ThenBy(f => f.Text, StringComparer.Ordinal)
            .ToList();
        return report;
    }

    private static void ScanRelevant(ThreadScope scope, CaseDraft draft, CorpusPlan plan, CaseGuide guide,
        RunScaffold scaffold, GenerationRunState state, List<JournalRecord> attachments)
    {
        var thread = scope.Thread;
        ThreadRenderContext ctx;
        RenderedThread? rendered;
        try
        {
            // As-of-wave ledger: only threads of earlier waves contribute, as when this thread was written (D-11)
            var wave = scaffold.WaveOf.TryGetValue(thread.Id, out var w) ? w : 0;
            var earlier = state.DeltasByThread
                .Where(kv => kv.Key != thread.Id && scaffold.WaveOf.TryGetValue(kv.Key, out var other) && other < wave)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var ledger = LedgerMerger.Build(plan, guide, scaffold.WaveOf, earlier);
            var assignment = scaffold.Assignments.TryGetValue(thread.Id, out var a) ? a : new ThreadAssignment { ThreadId = thread.Id };
            ctx = ThreadRenderContext.Create(draft, plan, thread, assignment, scaffold.People, ledger.Entries, wave);
            rendered = AttachmentRepair.Accepted(thread, scope.Record, scope.Messages);
        }
        catch (Exception)
        {
            // A thread whose context cannot be rebuilt is skipped; QA is report-only and an unchecked thread is not a finding
            return;
        }

        if (rendered != null)
        {
            foreach (var violation in SafeCheck(() => RenderValidator.Check(rendered, ctx)))
            {
                var check = CheckOf(violation.Code);
                if (check != null)
                    scope.Add(violation.Seq, check.Value, violation.Text);
            }
        }

        // Journaled violations of a kept-with-warnings thread (D-14) appear once next to the re-check
        if (scope.Record.Status == ThreadWriteStatus.Warnings)
            MergeJournaled(scope, scope.Record.Violations, null, attachment: false);

        foreach (var attachment in attachments)
        {
            var parent = scope.Messages.FirstOrDefault(m => string.Equals(m.DocId, attachment.ParentDocId, StringComparison.Ordinal));
            if (parent == null)
                continue;

            var title = TitleOf(attachment);
            var text = attachment.FlattenedText ?? string.Empty;
            var input = new AttachmentCheckInput
            {
                ParentSeq = parent.Seq,
                Text = text,

                // Excel line items are not labelled cells once flattened; V3 and the near-amount scan skip them (Pitfall 5)
                VariantText = attachment.AttachmentKind == AttachmentType.Excel ? string.Empty : text,
                AcceptedThread = rendered,
                NewDetails = (attachment.LedgerDelta ?? new List<LedgerDelta>()).ToList()
            };

            foreach (var violation in SafeCheck(() => RenderValidator.CheckAttachment(input, ctx)))
            {
                if (CheckOf(violation.Code) != null)
                    scope.Add(parent.Seq, ScanCheck.Attachment, AttachmentText(title, violation.Text));
            }

            MergeJournaled(scope, attachment.Violations, title, attachment: true, parent.Seq);
        }
    }

    private static void ScanNonRelevant(ThreadScope scope, CaseGuide guide, List<JournalRecord> attachments)
    {
        var values = (guide.Values ?? new List<CanonicalValue>()).Where(v => v != null).ToList();
        if (values.Count == 0)
            return;

        var bodies = (scope.Record.Messages ?? new List<JournalMessage>())
            .Where(m => m != null && !string.IsNullOrEmpty(m.DocId))
            .GroupBy(m => m.DocId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var message in scope.Messages)
        {
            if (!bodies.TryGetValue(message.DocId ?? string.Empty, out var saved))
                continue;
            foreach (var value in PrintedValues(saved.BodyPlain, values))
                scope.Add(message.Seq, ScanCheck.Value, PrintsText(value));
        }

        foreach (var attachment in attachments)
        {
            var parent = scope.Messages.FirstOrDefault(m => string.Equals(m.DocId, attachment.ParentDocId, StringComparison.Ordinal));
            if (parent == null)
                continue;
            foreach (var value in PrintedValues(attachment.FlattenedText, values))
                scope.Add(parent.Seq, ScanCheck.Attachment, AttachmentText(TitleOf(attachment), PrintsText(value)));
        }
    }

    // Registered case values a text prints; never throws (the scanner's own rule)
    private static IEnumerable<string> PrintedValues(string? text, List<CanonicalValue> values)
    {
        List<ScannedValue> scanned;
        try
        {
            scanned = CanonicalValueScanner.Scan(text);
        }
        catch (Exception)
        {
            // A scan that fails (regex timeout on hostile text) is skipped like any broken rule (Pitfall 5)
            return Array.Empty<string>();
        }

        return scanned.Where(s => SafeRegistered(s, values)).Select(s => s.Text.Trim()).ToList();
    }

    private static bool SafeRegistered(ScannedValue scanned, List<CanonicalValue> values)
    {
        try
        {
            return CanonicalValueScanner.IsRegistered(scanned, values);
        }
        catch (Exception)
        {
            // An unreadable value never becomes a finding
            return false;
        }
    }

    private static string PrintsText(string value) => $"prints the case value {value}";

    private static string AttachmentText(string title, string text) => title.Length > 0 ? $"{title}: {text}" : text;

    private static string TitleOf(JournalRecord attachment)
    {
        var title = (attachment.Title ?? string.Empty).Trim();
        return title.Length > 0 ? title : (attachment.FileName ?? string.Empty).Trim();
    }

    // Journaled "msg N: text" lines of the checks the scans own; other rules (people, length, legal wording) are not scan findings
    private static void MergeJournaled(ThreadScope scope, List<string>? violations, string? title, bool attachment, int parentSeq = 0)
    {
        foreach (var line in violations ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var match = JournaledSeq().Match(line.Trim());
            var seq = match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, Inv, out var s) ? s : 0;
            var text = match.Success ? match.Groups[2].Value.Trim() : line.Trim();
            var check = Classify(text);
            if (check == null)
                continue;

            if (attachment)
                scope.Add(parentSeq, ScanCheck.Attachment, AttachmentText(title ?? string.Empty, text));
            else if (seq > 0)
                scope.Add(seq, check.Value, text);
        }
    }

    // The rule texts RenderValidator writes for V4, V3 and V6 (journaled lines carry no code)
    private static ScanCheck? Classify(string text)
    {
        if (text.EndsWith(", dated after this email", StringComparison.Ordinal)
            || text.EndsWith(" does not know yet", StringComparison.Ordinal))
            return ScanCheck.Timeline;
        if (text.Contains("; the case value is ", StringComparison.Ordinal)
            || text.EndsWith(", which is not a case document number", StringComparison.Ordinal))
            return ScanCheck.Value;
        if (text.Contains("; earlier emails say ", StringComparison.Ordinal)
            || text.StartsWith("adds a new detail after later emails were written", StringComparison.Ordinal))
            return ScanCheck.StoryDetail;
        return null;
    }

    private static ScanCheck? CheckOf(string? code) => code switch
    {
        TimelineCode => ScanCheck.Timeline,
        ValueCode => ScanCheck.Value,
        StoryDetailCode => ScanCheck.StoryDetail,
        _ => null
    };

    private static List<RenderViolation> SafeCheck(Func<List<RenderViolation>> check)
    {
        try
        {
            return check() ?? new List<RenderViolation>();
        }
        catch (Exception)
        {
            // The validator never throws by design; this guard keeps that promise for QA, which must never fail a run (D-15)
            return new List<RenderViolation>();
        }
    }

    // One thread's findings sink: Doc ID, subject and date come from the answer-key row of the message at a seq
    private sealed class ThreadScope
    {
        private readonly Dictionary<string, AnswerKeyRow> _rows;
        private readonly List<ScanFinding> _findings;
        private readonly HashSet<string> _seen;

        public ThreadScope(PlannedThread thread, List<PlannedMessage> messages, JournalRecord record,
            Dictionary<string, AnswerKeyRow> rows, List<ScanFinding> findings, HashSet<string> seen)
        {
            Thread = thread;
            Messages = messages;
            Record = record;
            _rows = rows;
            _findings = findings;
            _seen = seen;
        }

        public PlannedThread Thread { get; }
        public List<PlannedMessage> Messages { get; }
        public JournalRecord Record { get; }

        public void Add(int seq, ScanCheck check, string text)
        {
            var message = Messages.FirstOrDefault(m => m.Seq == seq);
            if (message == null || string.IsNullOrWhiteSpace(text))
                return;

            var docId = message.DocId ?? string.Empty;

            // Merged, not duplicated: a re-check and its journaled line are one finding
            if (!_seen.Add(docId + "|" + check.ToString() + "|" + text))
                return;

            var saved = (Record.Messages ?? new List<JournalMessage>())
                .FirstOrDefault(m => m != null && string.Equals(m.DocId, docId, StringComparison.Ordinal));
            var subject = _rows.TryGetValue(docId, out var row) && !string.IsNullOrWhiteSpace(row.Subject)
                ? row.Subject
                : !string.IsNullOrWhiteSpace(saved?.Subject) ? saved!.Subject : Thread.Subject ?? string.Empty;

            _findings.Add(new ScanFinding
            {
                ThreadId = Thread.Id,
                DocId = docId,
                Subject = subject,
                DateSent = MessageTime.Local(message).DateTime,
                Check = check,
                Text = text,
                Relevant = Thread.Relevance == ThreadRelevance.Relevant
            });
        }
    }
}
