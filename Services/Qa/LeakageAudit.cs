using System.Globalization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Qa;

/// <summary>
/// Metadata-only label-leakage audit (QA-02, D-14). Reads answer-key email rows (sender, recipients, dates, subject
/// length, attachments, thread depth, privilege marks), never bodies, and never writes anything; the caller stores the
/// report. The verdict comes from thread-grouped cross-validated balanced accuracy of an L2 logistic regression.
/// </summary>
public static class LeakageAudit
{
    // [ASSUMED] research A4 / Pitfall 8
    public const double PassMaxBalancedAccuracy = 0.60;

    // [ASSUMED] research A4 / Pitfall 8
    public const double WarningMaxBalancedAccuracy = 0.70;

    // [ASSUMED] research A4 / Pitfall 8
    public const int MinEmailsPerClass = 40;

    // [ASSUMED] research A4 / Pitfall 8
    public const int Folds = 5;

    // [ASSUMED] research A4 / Pitfall 8
    public const int MinSignalSupport = 5;

    private const int MinFolds = 2;
    private const int TopAddresses = 20;
    private const int MaxSignals = 10;
    private const int MaxCcThreshold = 3;
    private const int MaxDepthBucket = 4;
    private const string PrivilegedYes = "Y";

    // Floating-point slack so a balanced accuracy of exactly 60% / 70% lands on the documented side
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Audits the plan's answer-key email rows (with the run overlay when given). With <paramref name="generatedOnly"/>
    /// only Generated rows are audited, or every row when nothing is generated yet.
    /// </summary>
    public static LeakageReport Run(CorpusPlan plan, CaseGuide guide, AnswerKeyOverlay? overlay = null,
        bool generatedOnly = true)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(guide);

        // Attachment kinds come from the plan; answer-key rows only carry the file name
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attachment in plan.Threads.SelectMany(t => t.Messages).SelectMany(m => m.Attachments))
            if (!string.IsNullOrEmpty(attachment.DocId))
                kinds.TryAdd(attachment.DocId, KindLabel(attachment.Kind));

        var rows = AnswerKeyBuilder.Build(plan, guide, overlay);
        return RunRows(rows, plan.PlanId, generatedOnly, kinds);
    }

    /// <summary>
    /// Pass at or below 60%, Warning above 60% up to 70%, Fail above 70%; Not enough data below 40 emails in either class.
    /// </summary>
    public static LeakageVerdict VerdictFor(double balancedAccuracy, int relevant, int nonRelevant)
    {
        if (relevant < MinEmailsPerClass || nonRelevant < MinEmailsPerClass)
            return LeakageVerdict.NotEnoughData;
        if (balancedAccuracy <= PassMaxBalancedAccuracy + Epsilon)
            return LeakageVerdict.Pass;
        if (balancedAccuracy <= WarningMaxBalancedAccuracy + Epsilon)
            return LeakageVerdict.Warning;
        return LeakageVerdict.Fail;
    }

    /// <summary>
    /// Fold per row = StableRank(threadId) % k, so a thread's emails share one fold (no Random). Starts at k = 5 and
    /// lowers k while any fold lacks a relevant or a non-relevant email, stopping at 2.
    /// </summary>
    internal static (int[] Folds, int Count) AssignFolds(IReadOnlyList<string> threadIds, int[] labels)
    {
        var ranks = threadIds.Select(id => RenderAssignments.StableRank(id ?? string.Empty)).ToArray();
        for (var k = Folds; ; k--)
        {
            var folds = ranks.Select(r => (int)(r % (ulong)k)).ToArray();
            if (k <= MinFolds || EveryFoldHasBothClasses(folds, labels, k))
                return (folds, k);
        }
    }

    internal static LeakageReport RunRows(IReadOnlyList<AnswerKeyRow> rows, Guid planId, bool generatedOnly = true,
        IReadOnlyDictionary<string, string>? attachmentKinds = null)
    {
        var emailRows = rows.Where(r => string.IsNullOrEmpty(r.ParentDocId)).ToList();
        var depths = ThreadDepths(emailRows);
        var childKinds = ChildKinds(rows, attachmentKinds);

        var audited = emailRows;
        if (generatedOnly)
        {
            var generated = emailRows.Where(r => IsGenerated(r.Status)).ToList();
            // Before anything is written the plan itself is audited
            if (generated.Count > 0)
                audited = generated;
        }

        var items = audited.Select(r => Describe(r, depths, childKinds)).ToList();
        var labels = items.Select(i => i.Label).ToArray();
        var relevant = labels.Count(l => l == 1);
        var nonRelevant = labels.Length - relevant;

        var report = new LeakageReport
        {
            PlanId = planId,
            CreatedUtc = DateTime.UtcNow,
            RelevantEmails = relevant,
            NonRelevantEmails = nonRelevant
        };

        if (items.Count == 0)
        {
            report.Verdict = LeakageVerdict.NotEnoughData;
            return report;
        }

        var features = BuildFeatures(items);
        var modelFeatures = features.Where(f => f.InModel).ToList();
        var x = items.Select(i => modelFeatures.Select(f => f.Value(i)).ToArray()).ToArray();

        if (relevant > 0 && nonRelevant > 0)
        {
            var (folds, foldCount) = AssignFolds(items.Select(i => i.Row.ThreadId).ToList(), labels);
            var probabilities = OutOfFold(x, labels, folds, foldCount);
            // Threshold at the relevant share: with imbalanced classes a 0.5 cut would hide real leakage (T-07-41)
            var threshold = relevant / (double)labels.Length;
            report.BalancedAccuracy = LogisticRegression.BalancedAccuracy(labels, probabilities, threshold);
            report.Auc = LogisticRegression.Auc(labels, probabilities);
            report.Folds = foldCount;
        }
        else
        {
            report.BalancedAccuracy = 0.5;
            report.Auc = 0.5;
        }

        report.Verdict = VerdictFor(report.BalancedAccuracy, relevant, nonRelevant);
        report.Signals = TopSignals(items, features, relevant);
        return report;
    }

    // ---------- cross-validation ----------

    private static double[] OutOfFold(double[][] x, int[] y, int[] folds, int foldCount)
    {
        var p = new double[y.Length];
        for (var f = 0; f < foldCount; f++)
        {
            var train = Enumerable.Range(0, y.Length).Where(i => folds[i] != f).ToArray();
            var test = Enumerable.Range(0, y.Length).Where(i => folds[i] == f).ToArray();
            if (test.Length == 0)
                continue;

            var trainY = train.Select(i => y[i]).ToArray();
            var positives = trainY.Count(v => v == 1);
            if (positives == 0 || positives == trainY.Length)
            {
                // One-class training fold: the best a model can say is the training share
                var share = trainY.Length == 0 ? 0.5 : positives / (double)trainY.Length;
                foreach (var i in test)
                    p[i] = share;
                continue;
            }

            var model = LogisticRegression.Fit(train.Select(i => x[i]).ToArray(), trainY);
            foreach (var i in test)
                p[i] = model.PredictProbability(x[i]);
        }

        return p;
    }

    private static bool EveryFoldHasBothClasses(int[] folds, int[] labels, int k)
    {
        var hasPositive = new bool[k];
        var hasNegative = new bool[k];
        for (var i = 0; i < folds.Length; i++)
        {
            if (labels[i] == 1)
                hasPositive[folds[i]] = true;
            else
                hasNegative[folds[i]] = true;
        }

        for (var f = 0; f < k; f++)
            if (!hasPositive[f] || !hasNegative[f])
                return false;
        return true;
    }

    // ---------- signals ----------

    private static List<LeakageSignal> TopSignals(List<AuditItem> items, List<Feature> features, int relevant)
    {
        var n = items.Count;
        var baseRate = relevant / (double)n;
        var signals = new List<LeakageSignal>();

        foreach (var feature in features.Where(f => f.SignalText != null))
        {
            int support = 0, relevantSupport = 0;
            foreach (var item in items)
            {
                if (feature.Value(item) <= 0)
                    continue;
                support++;
                relevantSupport += item.Label;
            }

            // A value on every email (or almost none) says nothing about relevance
            if (support < MinSignalSupport || support == n)
                continue;

            var share = relevantSupport / (double)support;
            var lift = share - baseRate;
            if (Math.Abs(lift) < Epsilon)
                continue;

            signals.Add(new LeakageSignal
            {
                Feature = feature.SignalText!,
                Kind = relevantSupport == support ? LeakageSignalKind.OnlyOnRelevant
                    : relevantSupport == 0 ? LeakageSignalKind.NeverOnRelevant
                    : LeakageSignalKind.Lift,
                RelevantShare = share,
                BaseRate = baseRate,
                Support = support,
                Lift = lift
            });
        }

        return signals
            .OrderByDescending(s => Math.Abs(s.Lift) * s.Support)
            .ThenBy(s => s.Feature, StringComparer.Ordinal)
            .Take(MaxSignals)
            .ToList();
    }

    // ---------- features ----------

    private sealed class AuditItem
    {
        public AnswerKeyRow Row { get; init; } = null!;
        public int Label { get; init; }
        public DateTimeOffset? Sent { get; init; }
        public string Sender { get; init; } = string.Empty;
        public List<string> Recipients { get; init; } = new();
        public int RecipientCount { get; init; }
        public int CcCount { get; init; }
        public List<string> AttachmentKinds { get; init; } = new();
        public int Depth { get; init; }
        public bool Banner { get; init; }
        public bool SubjectTag { get; init; }
        public int SubjectLength { get; init; }
        public int MonthIndex { get; init; }
    }

    // One model column and/or one signal; SignalText null = model only, InModel false = signal only
    private sealed class Feature
    {
        public Feature(string? signalText, bool inModel, Func<AuditItem, double> value)
        {
            SignalText = signalText;
            InModel = inModel;
            Value = value;
        }

        public string? SignalText { get; }
        public bool InModel { get; }
        public Func<AuditItem, double> Value { get; }
    }

    private static List<Feature> BuildFeatures(List<AuditItem> items)
    {
        var features = new List<Feature>();

        // Sender: top 20 by count (ties ordinal), rest "other"
        var senders = TopValues(items.Select(i => i.Sender));
        foreach (var sender in senders)
            features.Add(new Feature($"sender {sender}", true, i => Flag(i.Sender == sender)));
        features.Add(new Feature(null, true, i => Flag(!senders.Contains(i.Sender))));

        // Recipients (To and Cc): top 20 addresses one-hot, rest "other"
        var recipients = TopValues(items.SelectMany(i => i.Recipients.Distinct(StringComparer.Ordinal)));
        foreach (var recipient in recipients)
            features.Add(new Feature($"recipient {recipient}", true, i => Flag(i.Recipients.Contains(recipient))));
        features.Add(new Feature(null, true, i => Flag(i.Recipients.Any(r => !recipients.Contains(r)))));

        features.Add(new Feature(null, true, i => i.RecipientCount));
        features.Add(new Feature(null, true, i => i.CcCount));
        for (var cc = 1; cc <= MaxCcThreshold; cc++)
        {
            var threshold = cc;
            features.Add(new Feature($"{threshold} or more CC recipients", false, i => Flag(i.CcCount >= threshold)));
        }

        // Weekday and hour bucket in the sender's local clock
        foreach (var day in new[]
                 {
                     DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
                     DayOfWeek.Saturday, DayOfWeek.Sunday
                 })
            features.Add(new Feature($"sent on {day}", true, i => Flag(i.Sent?.DayOfWeek == day)));
        foreach (var bucket in new[] { HourBucket.Early, HourBucket.Business, HourBucket.Late })
            features.Add(new Feature($"sent {HourText(bucket)}", true, i => Flag(i.Sent != null && HourOf(i.Sent.Value) == bucket)));

        var firstMonth = items.Where(i => i.Sent != null).Select(i => i.MonthIndex).DefaultIfEmpty(0).Min();
        features.Add(new Feature(null, true, i => i.Sent == null ? 0 : i.MonthIndex - firstMonth));

        foreach (var bucket in new[] { SubjectBucket.Short, SubjectBucket.Medium, SubjectBucket.Long })
            features.Add(new Feature($"subject length {SubjectText(bucket)}", true, i => Flag(SubjectOf(i.SubjectLength) == bucket)));

        features.Add(new Feature(null, true, i => i.AttachmentKinds.Count));
        foreach (var kind in items.SelectMany(i => i.AttachmentKinds).Distinct(StringComparer.Ordinal)
                     .OrderBy(k => k, StringComparer.Ordinal))
            features.Add(new Feature($"{kind} attachment", true, i => Flag(i.AttachmentKinds.Contains(kind))));

        // Thread depth: one-hot buckets 1, 2, 3, 4+ for the model; "depth n+" thresholds as signals
        for (var depth = 1; depth <= MaxDepthBucket; depth++)
        {
            var d = depth;
            features.Add(new Feature(null, true,
                i => Flag(d == MaxDepthBucket ? i.Depth >= d : i.Depth == d)));
            if (d >= 2)
                features.Add(new Feature($"thread depth {d}+", false, i => Flag(i.Depth >= d)));
        }

        features.Add(new Feature("privilege banner", true, i => Flag(i.Banner)));
        features.Add(new Feature("privileged subject tag", true, i => Flag(i.SubjectTag)));
        return features;
    }

    private static AuditItem Describe(AnswerKeyRow row, Dictionary<string, int> depths,
        Dictionary<string, List<string>> childKinds)
    {
        var sent = ParseSent(row.DateSent);
        var to = Addresses(row.To);
        var cc = Addresses(row.Cc);
        var subject = row.Subject ?? string.Empty;
        var tagged = subject.StartsWith(EmailComposer.PrivilegedSubjectTag, StringComparison.OrdinalIgnoreCase);
        var bare = tagged ? subject[EmailComposer.PrivilegedSubjectTag.Length..] : subject;

        return new AuditItem
        {
            Row = row,
            Label = string.Equals(row.Relevance, AnswerKeyBuilder.RelevantLabel, StringComparison.Ordinal) ? 1 : 0,
            Sent = sent,
            Sender = Address(row.From),
            Recipients = to.Concat(cc).ToList(),
            RecipientCount = to.Count,
            CcCount = cc.Count,
            AttachmentKinds = childKinds.TryGetValue(row.DocId, out var kinds) ? kinds : new List<string>(),
            Depth = depths.TryGetValue(row.DocId, out var depth) ? depth : 1,
            Banner = string.Equals(row.Privileged, PrivilegedYes, StringComparison.OrdinalIgnoreCase),
            SubjectTag = tagged,
            SubjectLength = bare.Trim().Length,
            MonthIndex = sent == null ? 0 : sent.Value.Year * 12 + sent.Value.Month - 1
        };
    }

    // Position of each email within its thread by sent time (1-based), over every email row
    private static Dictionary<string, int> ThreadDepths(List<AnswerKeyRow> emailRows)
    {
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var thread in emailRows.GroupBy(r => r.ThreadId ?? string.Empty, StringComparer.Ordinal))
        {
            var ordered = thread
                .OrderBy(r => ParseSent(r.DateSent)?.UtcDateTime ?? DateTime.MaxValue)
                .ThenBy(r => r.DocId, StringComparer.Ordinal)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
                depths[ordered[i].DocId] = i + 1;
        }

        return depths;
    }

    // Attachment kinds per parent DocID (plan kind when known, else from the file extension)
    private static Dictionary<string, List<string>> ChildKinds(IReadOnlyList<AnswerKeyRow> rows,
        IReadOnlyDictionary<string, string>? attachmentKinds)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in rows.Where(r => !string.IsNullOrEmpty(r.ParentDocId)))
        {
            var kind = attachmentKinds != null && attachmentKinds.TryGetValue(row.DocId, out var known)
                ? known
                : KindFromFileName(row.FileName);
            if (!result.TryGetValue(row.ParentDocId, out var list))
                result[row.ParentDocId] = list = new List<string>();
            list.Add(kind);
        }

        return result;
    }

    private static List<string> TopValues(IEnumerable<string> values) =>
        values.Where(v => v.Length > 0)
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(TopAddresses)
            .Select(g => g.Key)
            .ToList();

    private static List<string> Addresses(string? list) =>
        string.IsNullOrWhiteSpace(list)
            ? new List<string>()
            : list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Address)
                .Where(a => a.Length > 0)
                .ToList();

    // "Name <email>" -> email; otherwise the trimmed text (lowercase)
    private static string Address(string? display)
    {
        var text = (display ?? string.Empty).Trim();
        var open = text.LastIndexOf('<');
        var close = text.LastIndexOf('>');
        if (open >= 0 && close > open + 1)
            text = text[(open + 1)..close].Trim();
        return text.ToLowerInvariant();
    }

    private static DateTimeOffset? ParseSent(string? text) =>
        DateTimeOffset.TryParseExact(text, AnswerKeyBuilder.DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var sent)
            ? sent
            : null;

    private static bool IsGenerated(string? status) =>
        string.Equals(status, AnswerKeyBuilder.StatusGenerated, StringComparison.Ordinal)
        || string.Equals(status, AnswerKeyBuilder.StatusGeneratedWithWarnings, StringComparison.Ordinal);

    private static double Flag(bool value) => value ? 1.0 : 0.0;

    private enum HourBucket { Early, Business, Late }

    private static HourBucket HourOf(DateTimeOffset sent) =>
        sent.Hour < 8 ? HourBucket.Early : sent.Hour < 18 ? HourBucket.Business : HourBucket.Late;

    private static string HourText(HourBucket bucket) => bucket switch
    {
        HourBucket.Early => "before 8 AM",
        HourBucket.Business => "8 AM to 6 PM",
        _ => "after 6 PM"
    };

    private enum SubjectBucket { Short, Medium, Long }

    private static SubjectBucket SubjectOf(int length) =>
        length < 30 ? SubjectBucket.Short : length <= 60 ? SubjectBucket.Medium : SubjectBucket.Long;

    private static string SubjectText(SubjectBucket bucket) => bucket switch
    {
        SubjectBucket.Short => "under 30 characters",
        SubjectBucket.Medium => "30 to 60 characters",
        _ => "over 60 characters"
    };

    private static string KindLabel(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => "Word",
        AttachmentType.Excel => "Excel",
        AttachmentType.PowerPoint => "PowerPoint",
        AttachmentType.Image => "image",
        AttachmentType.CalendarInvite => "calendar invite",
        AttachmentType.Voicemail => "voicemail",
        _ => kind.ToString()
    };

    private static string KindFromFileName(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".docx" or ".doc" => KindLabel(AttachmentType.Word),
            ".xlsx" or ".xls" => KindLabel(AttachmentType.Excel),
            ".pptx" or ".ppt" => KindLabel(AttachmentType.PowerPoint),
            ".png" or ".jpg" or ".jpeg" or ".gif" => KindLabel(AttachmentType.Image),
            ".ics" => KindLabel(AttachmentType.CalendarInvite),
            ".mp3" or ".wav" or ".m4a" => KindLabel(AttachmentType.Voicemail),
            "" => "other",
            var ext => ext.TrimStart('.')
        };
}
