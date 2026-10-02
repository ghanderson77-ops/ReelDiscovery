using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

public enum PlanArea
{
    Plan,
    Thread,
    Facts,
    Issues,
    Custodian
}

/// <summary>
/// One Review Plan check. Every plan finding is a Warning with a fix hint; none blocks Approve (D-24).
/// </summary>
public class PlanFinding
{
    public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
    public PlanArea Area { get; set; }

    /// <summary>
    /// Thread (T-0001), fact (F-003), issue (ISS-01) or cast (CH-002) id; empty for plan-level findings.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
    public string FixHint { get; set; } = string.Empty;

    public string Where => Area switch
    {
        PlanArea.Facts => $"Key Facts · {EntityId}",
        PlanArea.Issues => $"Issues · {EntityId}",
        PlanArea.Custodian => $"Custodian · {EntityId}",
        PlanArea.Thread => $"Thread {EntityId}",
        _ => "Plan"
    };
}

/// <summary>
/// Deterministic checks of a corpus plan against its case guide and settings (PLAN-06). Static, warnings only, and never
/// throws: each rule runs in isolation so one broken rule cannot hide the others (T-04-10).
/// </summary>
public static partial class CorpusPlanValidator
{
    // Rule 18: a metric ratio above this (or below its inverse) warns, when both groups have enough messages
    private const double LeakageRatio = 1.5;
    private const int LeakageMinMessages = 5;

    // Rule 15: allowed drift of a category's share of non-relevant email, in percentage points
    private const double MixTolerancePoints = 3.0;

    private const string Replan = "Click Re-plan.";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly (NonRelevantCategory Category, string Label)[] MixCategories =
    {
        (NonRelevantCategory.DailyBusiness, "Custodians' daily business"),
        (NonRelevantCategory.Ancillary, "Ancillary staff traffic"),
        (NonRelevantCategory.NewsletterSpamPersonal, "Newsletters, spam and personal"),
        (NonRelevantCategory.Decoy, "Near-miss decoys")
    };

    public static List<PlanFinding> Validate(CaseDraft? draft, CorpusPlan? plan, bool mediaAvailable)
    {
        var findings = new List<PlanFinding>();
        if (draft?.Guide == null || plan == null)
            return findings;

        Context ctx;
        try
        {
            ctx = new Context(draft, plan, mediaAvailable);
        }
        catch (Exception ex)
        {
            // A crafted plan or guide must never crash the Review Plan step
            findings.Add(CouldNotRun(ex));
            return findings;
        }

        Run(findings, ctx, CheckEmailCount);
        Run(findings, ctx, CheckRelevantRatio);
        Run(findings, ctx, CheckActiveRanges);
        Run(findings, ctx, CheckFactEvidence);
        Run(findings, ctx, CheckWindow);
        Run(findings, ctx, CheckBusinessTime);
        Run(findings, ctx, CheckOrder);
        Run(findings, ctx, CheckIds);
        Run(findings, ctx, CheckLabels);
        Run(findings, ctx, CheckFutureFacts);
        Run(findings, ctx, CheckKnowledge);
        Run(findings, ctx, CheckPrivilege);
        Run(findings, ctx, CheckAttachments);
        Run(findings, ctx, CheckDocIds);
        Run(findings, ctx, CheckMix);
        Run(findings, ctx, CheckDepth);
        Run(findings, ctx, CheckFallbacks);
        Run(findings, ctx, CheckLeakage);
        Run(findings, ctx, CheckStale);

        return findings;
    }

    private static void Run(List<PlanFinding> findings, Context ctx, Action<Context, List<PlanFinding>> rule)
    {
        try
        {
            rule(ctx, findings);
        }
        catch (Exception ex)
        {
            // One broken rule must not hide the others or throw into the UI (T-04-10)
            findings.Add(CouldNotRun(ex));
        }
    }

    private static PlanFinding CouldNotRun(Exception ex) =>
        Warning(PlanArea.Plan, string.Empty, $"A check could not run: {ex.Message.TrimEnd('.')}.", Replan);

    private static PlanFinding Warning(PlanArea area, string entityId, string message, string fixHint) => new()
    {
        Severity = IssueSeverity.Warning,
        Area = area,
        EntityId = entityId ?? string.Empty,
        Message = message,
        FixHint = fixHint
    };

    // ----- Rule 1: email count -----
    private static void CheckEmailCount(Context ctx, List<PlanFinding> findings)
    {
        if (ctx.Settings == null)
            return;

        var count = ctx.Messages.Count;
        if (count != ctx.Settings.TotalEmails)
        {
            findings.Add(Warning(PlanArea.Plan, string.Empty,
                $"The plan has {N(count)} emails; the settings ask for {N(ctx.Settings.TotalEmails)}.",
                "Click Re-plan to rebuild the plan from the current settings."));
        }
    }

    // ----- Rule 2: relevant ratio -----
    private static void CheckRelevantRatio(Context ctx, List<PlanFinding> findings)
    {
        if (ctx.Settings == null)
            return;

        var relevant = ctx.Messages.Count(m => m.Thread.Relevance == ThreadRelevance.Relevant);
        var target = ctx.Settings.RelevantEmails;
        var tolerance = Math.Max(1.0, ctx.Settings.TotalEmails * 0.01);
        if (Math.Abs(relevant - target) <= tolerance)
            return;

        var percent = ctx.Messages.Count == 0 ? 0 : relevant * 100.0 / ctx.Messages.Count;
        findings.Add(Warning(PlanArea.Plan, string.Empty,
            $"The plan has {N(relevant)} relevant emails ({percent.ToString("0.#", Inv)}%); the settings ask for {N(target)} ({ctx.Settings.RelevantPercent.ToString(Inv)}%).",
            "If every key fact needs its own email, raise % relevant on Corpus Settings, then click Re-plan."));
    }

    // ----- Rule 3: participants active on the sender-local date -----
    private static void CheckActiveRanges(Context ctx, List<PlanFinding> findings)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ctx.Messages.OrderBy(m => m.Message.SentAt.UtcTicks))
        {
            var day = ctx.SenderLocalDate(entry.Message);
            if (day == null)
                continue;

            foreach (var id in Participants(entry.Message))
            {
                if (reported.Contains(id) || !ctx.CastById.TryGetValue(id, out var member) || ctx.IsActive(member, day.Value))
                    continue;

                reported.Add(id);
                var name = ctx.Name(id);
                findings.Add(Warning(PlanArea.Custodian, id,
                    $"{name} ({id}) is on an email dated {D(day.Value)} but is only active {RangeText(member.ActiveFrom)} to {RangeText(member.ActiveTo)}.",
                    $"Widen {name}'s active range in the Case Guide, then click Re-plan."));
            }
        }
    }

    // ----- Rule 4: every dated key fact has planned evidence (PLAN-06) -----
    private static void CheckFactEvidence(Context ctx, List<PlanFinding> findings)
    {
        var evidenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ctx.Messages.Where(m => m.Thread.Relevance == ThreadRelevance.Relevant))
        {
            foreach (var factId in L(entry.Message.FactIds))
                evidenced.Add(factId);
        }

        foreach (var fact in ctx.Facts)
        {
            if (!CaseDraft.TryParseDate(fact.Date, out _) || evidenced.Contains(S(fact.Id)))
                continue;

            findings.Add(Warning(PlanArea.Facts, S(fact.Id),
                $"No planned email is evidence of {S(fact.Id)} ({S(fact.Title)}).",
                "Raise % relevant or the corpus size, then click Re-plan."));
        }
    }

    // ----- Rule 5: inside the corpus window -----
    private static void CheckWindow(Context ctx, List<PlanFinding> findings)
    {
        if (ctx.WindowStart == null && ctx.WindowEnd == null)
            return;

        foreach (var (thread, messages) in ctx.Threads)
        {
            foreach (var message in messages)
            {
                var day = ctx.SenderLocalDate(message) ?? DateOnly.FromDateTime(message.SentAt.DateTime);
                if ((ctx.WindowStart != null && day < ctx.WindowStart) || (ctx.WindowEnd != null && day > ctx.WindowEnd))
                {
                    findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                        $"Thread {S(thread.Id)} has an email dated {D(day)}, outside the date window ({WindowText(ctx.WindowStart)} to {WindowText(ctx.WindowEnd)}).",
                        Replan));
                    break;
                }
            }
        }
    }

    // ----- Rule 6: business hours and observed holidays in the sender zone -----
    private static void CheckBusinessTime(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads)
        {
            foreach (var message in messages)
            {
                var zone = ctx.ZoneFor(S(message.FromId));
                var local = Local(message.SentAt, zone);
                if (local == null || BusinessCalendar.IsBusinessTime(message.SentAt, zone))
                    continue;

                findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                    $"Thread {S(thread.Id)} has an email sent outside business hours ({local.Value.ToString("yyyy-MM-dd HH:mm", Inv)} {zone.Id}).",
                    Replan));
                break;
            }
        }
    }

    // ----- Rule 7: monotonic thread dates -----
    private static void CheckOrder(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads)
        {
            var ordered = messages.OrderBy(m => m.Seq).ToList();
            var bySeq = new Dictionary<int, PlannedMessage>();
            var broken = false;

            for (var i = 0; i < ordered.Count && !broken; i++)
            {
                var message = ordered[i];
                if (i > 0 && (message.Seq == ordered[i - 1].Seq || message.SentAt <= ordered[i - 1].SentAt))
                    broken = true;
                if (message.ReplyToSeq >= message.Seq)
                    broken = true;
                if (message.ReplyToSeq > 0 && bySeq.TryGetValue(message.ReplyToSeq, out var parent) && parent.SentAt >= message.SentAt)
                    broken = true;

                bySeq.TryAdd(message.Seq, message);
            }

            if (broken)
                findings.Add(Warning(PlanArea.Thread, S(thread.Id), $"Thread {S(thread.Id)} has replies out of date order.", Replan));
        }
    }

    // ----- Rule 8: every id resolves -----
    private static void CheckIds(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads)
        {
            var unknown = new List<string>();
            void Check(string? id, bool known)
            {
                if (!string.IsNullOrEmpty(id) && !known && !unknown.Contains(id))
                    unknown.Add(id);
            }

            foreach (var message in messages)
            {
                foreach (var id in Participants(message))
                    Check(id, ctx.IsPerson(id));
                foreach (var id in L(message.FactIds))
                    Check(id, ctx.FactById.ContainsKey(id));
            }

            foreach (var id in L(thread.FactIds))
                Check(id, ctx.FactById.ContainsKey(id));
            foreach (var id in L(thread.IssueIds))
                Check(id, ctx.IssueIds.Contains(id));
            foreach (var id in L(thread.LinkedThreadIds))
                Check(id, ctx.ThreadIds.Contains(id));

            foreach (var id in unknown)
            {
                findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                    $"Thread {S(thread.Id)} refers to {id}, which is not in the case guide or the plan.",
                    "Click Re-plan to rebuild the plan from the current guide."));
            }
        }
    }

    // ----- Rule 9: label consistency -----
    private static void CheckLabels(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads)
        {
            var details = new List<string>();
            var relevant = thread.Relevance == ThreadRelevance.Relevant;

            if (relevant && L(thread.FactIds).Count == 0)
                details.Add("relevant with no key facts");
            if (!relevant && L(thread.FactIds).Count > 0)
                details.Add("not relevant but linked to key facts");
            if (relevant && thread.Category == NonRelevantCategory.Decoy)
                details.Add("a decoy marked relevant");
            else if (relevant && thread.Category != NonRelevantCategory.None)
                details.Add("relevant with a non-relevant category");

            if (!relevant && messages.Any(m => m.IsHot))
            {
                details.Add("hot but not relevant");
            }
            else
            {
                foreach (var message in messages.Where(m => m.IsHot))
                {
                    var hasHotFact = L(message.FactIds).Any(id =>
                        ctx.FactById.TryGetValue(id, out var fact) && fact.Significance == FactSignificance.Hot);
                    if (!hasHotFact)
                        details.Add($"{S(message.Id)} is hot but carries no hot fact");
                }
            }

            if (details.Count > 0)
            {
                findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                    $"Thread {S(thread.Id)} has inconsistent labels: {string.Join("; ", details)}.", Replan));
            }
        }
    }

    // ----- Rule 10: no fact mentioned before its date -----
    private static void CheckFutureFacts(Context ctx, List<PlanFinding> findings)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ctx.Messages)
        {
            var day = ctx.SenderLocalDate(entry.Message);
            if (day == null)
                continue;

            foreach (var factId in L(entry.Message.FactIds))
            {
                if (!ctx.FactById.TryGetValue(factId, out var fact) || !CaseDraft.TryParseDate(fact.Date, out var factDate))
                    continue;
                if (DateOnly.FromDateTime(factDate) <= day.Value || !reported.Add($"{S(entry.Thread.Id)}|{factId}"))
                    continue;

                findings.Add(Warning(PlanArea.Facts, factId,
                    $"Thread {S(entry.Thread.Id)} mentions {factId} before it happened ({fact.Date}).",
                    $"Check the date of {factId} in the Case Guide, then click Re-plan."));
            }
        }
    }

    // ----- Rule 11: participants knew the facts they discuss -----
    private static void CheckKnowledge(Context ctx, List<PlanFinding> findings)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ctx.Messages)
        {
            var message = entry.Message;
            var facts = L(message.FactIds).Where(id => ctx.FactById.ContainsKey(id)).ToList();
            if (facts.Count == 0)
                continue;

            var to = L(message.ToIds);
            if (L(message.CcIds).Count == 0 && to.Count == 1 && to[0] == S(message.FromId))
                continue;   // note to self

            var knowers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fact in facts.Select(id => ctx.FactById[id]))
            {
                knowers.UnionWith(L(fact.ActorIds));
                knowers.UnionWith(L(fact.KnownByIds));
            }

            foreach (var id in Participants(message))
            {
                if (knowers.Contains(id) || !reported.Add($"{S(entry.Thread.Id)}|{id}"))
                    continue;

                var name = ctx.Name(id);
                findings.Add(Warning(PlanArea.Thread, S(entry.Thread.Id),
                    $"{name} is on thread {S(entry.Thread.Id)} but did not know about {facts[0]}.",
                    $"Add {name} under Who knew for {facts[0]} in the Case Guide, or click Re-plan."));
            }
        }
    }

    // ----- Rule 12: privilege plausibility -----
    private static void CheckPrivilege(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads.Where(t => t.Thread.IsPrivileged))
        {
            var people = messages.OrderBy(m => m.Seq).SelectMany(Participants).Distinct(StringComparer.Ordinal).ToList();
            var counsel = people.FirstOrDefault(id => ctx.CastById.TryGetValue(id, out var m) && IsCounsel(m));
            if (counsel == null)
            {
                findings.Add(Warning(PlanArea.Thread, S(thread.Id), $"Privileged thread {S(thread.Id)} has no counsel.", Replan));
                continue;
            }

            // The privileged side is the first counsel's client; that client and its law firms may take part
            var clients = ctx.ClientsOf(ctx.CastById[counsel]);
            var allowed = new HashSet<string>(clients, StringComparer.Ordinal);
            foreach (var org in ctx.OrganizationById.Values)
            {
                if (L(org.RepresentsOrgIds).Any(clients.Contains))
                    allowed.Add(S(org.Id));
            }

            var outsider = people.FirstOrDefault(id =>
                !ctx.CastById.TryGetValue(id, out var member) || !allowed.Contains(S(member.OrganizationId)));
            if (outsider != null)
            {
                findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                    $"Privileged thread {S(thread.Id)} includes {ctx.Name(outsider)} from the other side.", Replan));
            }
        }
    }

    // ----- Rule 13: attachment specs (T-04-11) -----
    private static void CheckAttachments(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads)
        {
            foreach (var message in messages)
            {
                foreach (var attachment in Clean(message.Attachments))
                {
                    var name = attachment.FileName ?? string.Empty;
                    string? problem = null;
                    if (!ctx.EnabledTypes.Contains(attachment.Kind))
                        problem = attachment.Kind.ToString();
                    else if (attachment.Kind == Models.AttachmentType.CalendarInvite && message.ReplyToSeq != 0)
                        problem = attachment.Kind.ToString();
                    else if (string.IsNullOrWhiteSpace(name))
                        problem = "no file name";
                    else if (name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        problem = name;

                    if (problem != null)
                    {
                        findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                            $"Thread {S(thread.Id)} has an attachment the settings do not allow ({problem}).", Replan));
                    }
                }
            }
        }
    }

    // ----- Rule 14: DocIDs unique and contiguous -----
    private static void CheckDocIds(Context ctx, List<PlanFinding> findings)
    {
        var problem = DocIdProblem(ctx);
        if (problem != null)
            findings.Add(Warning(PlanArea.Plan, string.Empty, $"DocIDs are not unique and in order ({problem}).", Replan));
    }

    private static string? DocIdProblem(Context ctx)
    {
        var messageIds = new HashSet<string>(StringComparer.Ordinal);
        var docIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ctx.Messages)
        {
            var message = entry.Message;
            if (string.IsNullOrEmpty(message.Id) || !messageIds.Add(message.Id))
                return string.IsNullOrEmpty(message.Id) ? "an email has no id" : $"message id {message.Id} is used twice";
            if (string.IsNullOrEmpty(message.DocId))
                return "an email has no DocID";
            if (!docIds.Add(message.DocId))
                return $"{message.DocId} is used twice";

            foreach (var attachment in Clean(message.Attachments))
            {
                if (string.IsNullOrEmpty(attachment.DocId))
                    return "an attachment has no DocID";
                if (!docIds.Add(attachment.DocId))
                    return $"{attachment.DocId} is used twice";
            }
        }

        var prefix = ctx.Plan.DocIdPrefix ?? string.Empty;
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= ctx.Messages.Count; i++)
            expected.Add(DocId(prefix, i));

        foreach (var entry in ctx.Messages.OrderBy(m => m.Message.SentAt.UtcTicks))
        {
            if (!expected.Contains(entry.Message.DocId))
                return $"{entry.Message.DocId} is out of sequence";
        }

        foreach (var entry in ctx.Messages)
        {
            var attachments = Clean(entry.Message.Attachments);
            for (var k = 0; k < attachments.Count; k++)
            {
                var wanted = $"{entry.Message.DocId}.{(k + 1).ToString("0000", Inv)}";
                if (!string.Equals(attachments[k].DocId, wanted, StringComparison.OrdinalIgnoreCase))
                    return $"attachment {attachments[k].DocId} does not follow {entry.Message.DocId}";
            }
        }

        return null;
    }

    // ----- Rule 15: non-relevant mix -----
    private static void CheckMix(Context ctx, List<PlanFinding> findings)
    {
        if (ctx.Settings == null)
            return;

        var nonRelevant = ctx.Messages.Where(m => m.Thread.Relevance == ThreadRelevance.NotRelevant).ToList();
        var settingsMix = new[]
        {
            ctx.Settings.DailyBusinessPercent, ctx.Settings.AncillaryPercent, ctx.Settings.NewsletterPercent, ctx.Settings.DecoyPercent
        };
        if (nonRelevant.Count == 0 || settingsMix.Sum(x => Math.Max(0, x)) == 0)
            return;

        // Compare with the planner's own whole-email split so rounding never warns
        var expected = Apportion.Largest(nonRelevant.Count, settingsMix.Select(x => (double)Math.Max(0, x)).ToList());
        for (var i = 0; i < MixCategories.Length; i++)
        {
            var (category, label) = MixCategories[i];
            var actual = nonRelevant.Count(m => m.Thread.Category == category);
            var driftPoints = Math.Abs(actual - expected[i]) * 100.0 / nonRelevant.Count;
            if (driftPoints <= MixTolerancePoints)
                continue;

            var share = actual * 100.0 / nonRelevant.Count;
            findings.Add(Warning(PlanArea.Plan, string.Empty,
                $"{label} is {share.ToString("0.#", Inv)}% of other email; the settings ask for {settingsMix[i].ToString(Inv)}%.",
                Replan));
        }
    }

    // ----- Rule 16: depth cap -----
    private static void CheckDepth(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, messages) in ctx.Threads.Where(t => t.Messages.Count > ThreadShapes.MaxThreadDepth))
        {
            findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                $"Thread {S(thread.Id)} has {N(messages.Count)} emails (more than {ThreadShapes.MaxThreadDepth.ToString(Inv)}).", Replan));
        }
    }

    // ----- Rule 17: fallback content and planner notes -----
    private static void CheckFallbacks(Context ctx, List<PlanFinding> findings)
    {
        foreach (var (thread, _) in ctx.Threads.Where(t => t.Thread.UsesFallbackContent))
        {
            findings.Add(Warning(PlanArea.Thread, S(thread.Id),
                $"Thread {S(thread.Id)} uses a placeholder subject because the model did not plan it.",
                "Click Re-plan, or continue: the email text is written later from the facts."));
        }

        foreach (var note in (ctx.Plan.Notes ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)))
            findings.Add(Warning(PlanArea.Plan, string.Empty, note, "Click Re-plan if this matters for your demo."));
    }

    // ----- Rule 18: metadata leakage (T-04-12) -----
    private static void CheckLeakage(Context ctx, List<PlanFinding> findings)
    {
        var relevant = ctx.Threads.Where(t => t.Thread.Relevance == ThreadRelevance.Relevant).ToList();
        var business = ctx.Threads.Where(t => t.Thread.Relevance == ThreadRelevance.NotRelevant
            && t.Thread.Category is NonRelevantCategory.DailyBusiness or NonRelevantCategory.Decoy).ToList();

        var relevantMessages = relevant.SelectMany(t => t.Messages).ToList();
        var businessMessages = business.SelectMany(t => t.Messages).ToList();
        if (relevantMessages.Count < LeakageMinMessages || businessMessages.Count < LeakageMinMessages)
            return;

        var metrics = new (string Name, double A, double B, bool Percent)[]
        {
            ("thread depth", relevant.Average(t => (double)t.Messages.Count), business.Average(t => (double)t.Messages.Count), false),
            ("attachment rate", AttachmentRate(relevantMessages), AttachmentRate(businessMessages), true),
            ("recipients per email", relevantMessages.Average(Recipients), businessMessages.Average(Recipients), false),
            ("sender hour", MeanHour(ctx, relevantMessages), MeanHour(ctx, businessMessages), false)
        };

        foreach (var (name, a, b, percent) in metrics)
        {
            // A zero (or unknown) value on either side cannot be compared; skip rather than divide by zero
            if (a <= 0 || b <= 0 || double.IsNaN(a) || double.IsNaN(b))
                continue;

            var ratio = a / b;
            if (ratio <= LeakageRatio && ratio >= 1 / LeakageRatio)
                continue;

            var text = percent
                ? $"{(a * 100).ToString("0.#", Inv)}% vs {(b * 100).ToString("0.#", Inv)}%"
                : $"{a.ToString("0.##", Inv)} vs {b.ToString("0.##", Inv)}";
            findings.Add(Warning(PlanArea.Plan, string.Empty,
                $"Relevant email differs from everyday email in {name} ({text}), which can give the labels away.", Replan));
        }
    }

    private static double AttachmentRate(List<PlannedMessage> messages) =>
        messages.Count(m => Clean(m.Attachments).Count > 0) / (double)messages.Count;

    private static double Recipients(PlannedMessage message) => L(message.ToIds).Count + L(message.CcIds).Count;

    private static double MeanHour(Context ctx, List<PlannedMessage> messages)
    {
        var hours = messages
            .Select(m => Local(m.SentAt, ctx.ZoneFor(S(m.FromId))))
            .Where(local => local != null)
            .Select(local => local!.Value.Hour + local.Value.Minute / 60.0)
            .ToList();
        return hours.Count == 0 ? double.NaN : hours.Average();
    }

    // ----- Rule 19: stale against the case guide -----
    private static void CheckStale(Context ctx, List<PlanFinding> findings)
    {
        var current = ctx.Guide.ComputeHash();
        var planHash = ctx.Plan.GuideHash ?? string.Empty;
        var dismissed = ctx.Draft.Corpus?.DismissedStaleGuideHash ?? string.Empty;
        if (string.Equals(planHash, current, StringComparison.Ordinal) || string.Equals(dismissed, current, StringComparison.Ordinal))
            return;

        findings.Add(Warning(PlanArea.Plan, string.Empty, "The plan is out of date with the case guide.",
            "Click Re-plan, or Keep This Plan to use it anyway."));
    }

    // ----- Helpers -----

    private static IEnumerable<string> Participants(PlannedMessage message)
    {
        // ToIds/CcIds are read as stored: a null list makes the calling rule report "could not run"
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in new[] { message.FromId }.Concat(message.ToIds).Concat(message.CcIds))
        {
            if (!string.IsNullOrEmpty(id) && seen.Add(id))
                yield return id;
        }
    }

    private static bool IsCounsel(CastMember member) => member.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel;

    /// <summary>
    /// Local wall-clock time in <paramref name="zone"/>, or null for instants at the edge of the DateTime range (garbage plans).
    /// </summary>
    private static DateTime? Local(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var year = instant.UtcDateTime.Year;
        if (year < 2 || year > 9998)
            return null;
        return TimeZoneInfo.ConvertTime(instant, zone).DateTime;
    }

    private static string DocId(string prefix, int number) => $"{prefix}-{number.ToString("000000", Inv)}";

    private static string N(int value) => value.ToString("N0", Inv);

    private static string D(DateOnly day) => day.ToString("yyyy-MM-dd", Inv);

    private static string RangeText(string? date) => string.IsNullOrWhiteSpace(date) ? "(open)" : date;

    private static string WindowText(DateOnly? day) => day == null ? "(open)" : D(day.Value);

    private static string S(string? value) => value ?? string.Empty;

    private static List<string> L(List<string>? list) =>
        list == null ? new List<string>() : list.Where(x => !string.IsNullOrEmpty(x)).ToList();

    private static List<T> Clean<T>(List<T>? list) where T : class =>
        list == null ? new List<T>() : list.Where(x => x != null).ToList();

    /// <summary>
    /// Null-cleaned plan and guide lookups shared by every rule (dictionaries built first-wins, T-04-10).
    /// </summary>
    private class Context
    {
        private readonly Dictionary<string, TimeZoneInfo> _zones = new(StringComparer.Ordinal);
        private readonly TimeZoneInfo _externalZone;

        public Context(CaseDraft draft, CorpusPlan plan, bool mediaAvailable)
        {
            Draft = draft;
            Guide = draft.Guide!;
            Plan = plan;
            Settings = plan.Settings;

            foreach (var org in Clean(Guide.Organizations).Where(o => S(o.Id).Length > 0))
                OrganizationById.TryAdd(org.Id, org);
            foreach (var member in Clean(Guide.Cast).Where(c => S(c.Id).Length > 0))
                CastById.TryAdd(member.Id, member);

            Facts = Clean(Guide.Facts);
            foreach (var fact in Facts.Where(f => S(f.Id).Length > 0))
                FactById.TryAdd(fact.Id, fact);
            foreach (var issue in Clean(Guide.Issues).Where(i => S(i.Id).Length > 0))
                IssueIds.Add(issue.Id);
            foreach (var external in Clean(plan.Externals).Where(e => S(e.Id).Length > 0))
                ExternalById.TryAdd(external.Id, external);

            foreach (var thread in Clean(plan.Threads))
            {
                var messages = Clean(thread.Messages);
                Threads.Add((thread, messages));
                if (S(thread.Id).Length > 0)
                    ThreadIds.Add(thread.Id);
                foreach (var message in messages.OrderBy(m => m.Seq))
                    Messages.Add((thread, message));
            }

            EnabledTypes = Settings?.EnabledAttachmentTypes(mediaAvailable) ?? Array.Empty<Models.AttachmentType>();
            _externalZone = BusinessCalendar.ResolveId(UsTimeZones.Default) ?? TimeZoneInfo.Utc;

            if (Settings != null)
            {
                var hasStart = CaseDraft.TryParseDate(Settings.WindowStart, out var start);
                var hasEnd = CaseDraft.TryParseDate(Settings.WindowEnd, out var end);
                if (!hasStart || !hasEnd)
                {
                    var (defaultStart, defaultEnd) = CorpusSettings.DefaultWindow(Guide);
                    if (!hasStart)
                        hasStart = CaseDraft.TryParseDate(defaultStart, out start);
                    if (!hasEnd)
                        hasEnd = CaseDraft.TryParseDate(defaultEnd, out end);
                }

                WindowStart = hasStart ? DateOnly.FromDateTime(start) : null;
                WindowEnd = hasEnd ? DateOnly.FromDateTime(end) : null;
            }
        }

        public CaseDraft Draft { get; }
        public CaseGuide Guide { get; }
        public CorpusPlan Plan { get; }
        public CorpusSettings? Settings { get; }
        public List<KeyFact> Facts { get; }
        public Dictionary<string, CaseOrganization> OrganizationById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CastMember> CastById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, KeyFact> FactById { get; } = new(StringComparer.Ordinal);
        public HashSet<string> IssueIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ExternalParty> ExternalById { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ThreadIds { get; } = new(StringComparer.Ordinal);
        public List<(PlannedThread Thread, List<PlannedMessage> Messages)> Threads { get; } = new();
        public List<(PlannedThread Thread, PlannedMessage Message)> Messages { get; } = new();
        public IReadOnlyList<Models.AttachmentType> EnabledTypes { get; }
        public DateOnly? WindowStart { get; }
        public DateOnly? WindowEnd { get; }

        public bool IsPerson(string id) => CastById.ContainsKey(id) || ExternalById.ContainsKey(id);

        public string Name(string id)
        {
            if (CastById.TryGetValue(id, out var member) && member.FullName.Length > 0)
                return member.FullName;
            if (ExternalById.TryGetValue(id, out var external) && S(external.Name).Length > 0)
                return external.Name;
            return id;
        }

        /// <summary>
        /// Cast: organization zone via BusinessCalendar.Resolve; externals and unknown ids: UsTimeZones.Default (the same
        /// contract as SkeletonContext.ZoneFor).
        /// </summary>
        public TimeZoneInfo ZoneFor(string id)
        {
            if (_zones.TryGetValue(id, out var cached))
                return cached;

            var zone = CastById.TryGetValue(id, out var member)
                ? BusinessCalendar.Resolve(OrganizationById.GetValueOrDefault(S(member.OrganizationId)))
                : _externalZone;
            _zones[id] = zone;
            return zone;
        }

        public DateOnly? SenderLocalDate(PlannedMessage message)
        {
            var local = Local(message.SentAt, ZoneFor(S(message.FromId)));
            return local == null ? null : DateOnly.FromDateTime(local.Value);
        }

        public bool IsActive(CastMember member, DateOnly day)
        {
            if (CaseDraft.TryParseDate(member.ActiveFrom, out var from) && day < DateOnly.FromDateTime(from))
                return false;
            if (CaseDraft.TryParseDate(member.ActiveTo, out var to) && day > DateOnly.FromDateTime(to))
                return false;
            return true;
        }

        /// <summary>
        /// Organizations a counsel acts for: in-house = their own organization; outside counsel = the firm's clients.
        /// </summary>
        public HashSet<string> ClientsOf(CastMember counsel)
        {
            var clients = new HashSet<string>(StringComparer.Ordinal);
            var orgId = S(counsel.OrganizationId);
            if (counsel.Role == CastRole.OutsideCounsel && OrganizationById.TryGetValue(orgId, out var firm))
            {
                clients.UnionWith(L(firm.RepresentsOrgIds));
                clients.Add(orgId);
            }
            else if (orgId.Length > 0)
            {
                clients.Add(orgId);
            }

            return clients;
        }
    }
}
