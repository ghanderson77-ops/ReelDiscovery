using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Every planned non-relevant thread plus the code-generated outside senders they use.
/// </summary>
public class NonRelevantSkeleton
{
    public List<PlannedThread> Threads { get; set; } = new();
    public List<ExternalParty> Externals { get; set; } = new();
}

/// <summary>
/// Reserves and structures every non-relevant slot (D-08, D-14) with the same shape tables, attachment rules and scheduler
/// as relevant threads, so metadata never reveals relevance (Pitfall 3). No case facts are placed here; decoys only borrow
/// the issue ids of a nearby fact. Pure: every draw uses <see cref="SkeletonContext.Random"/>.
/// </summary>
public static class NonRelevantSkeletonBuilder
{
    // Share of daily-business threads that copy in-house counsel and are privileged but not relevant
    public const double PrivilegedDailyShare = 0.02;

    // Share of daily-business threads that add one person from another case organization
    public const double ExternalDailyShare = 0.20;

    // Decoys start within this many days of the fact whose issues they borrow
    public const int DecoyDaysAroundFact = 30;

    // With at least this many daily threads, one is privileged even when the 2% share rounds to zero
    private const int MinDailyThreadsForPrivileged = 50;

    // Ancillary root goes to a key person of the sender's organization this often, otherwise a background colleague
    private const int AncillaryKeyRecipientPercent = 60;

    // People on a thread should stay active this many days after the anchor (threads rarely run longer)
    private const int StableDays = 30;

    // A later sender is picked from the parent CC instead of the parent To 25% of the time (same as relevant threads)
    private const int SenderFromCcPercent = 25;

    // Reschedule attempts when a thread runs past an active range or the window
    private const int MaxScheduleAttempts = 6;

    // Tries to find a decoy fact with someone active near it
    private const int MaxDecoyPicks = 10;

    private class Spec
    {
        public NonRelevantCategory Category { get; set; }
        public int Size { get; set; }
        public DateOnly Anchor { get; set; }

        // Earliest anchor when a thread is moved back to fit (window start, or the decoy's fact range)
        public DateOnly Low { get; set; }

        public string Sender { get; set; } = string.Empty;

        // Everyone else who may take part (never contains the sender)
        public List<string> Pool { get; set; } = new();

        // Recipients that must be on the root To, in order
        public List<string> Forced { get; set; } = new();

        public bool IsCrossOrganization { get; set; }
        public bool IsPrivileged { get; set; }
        public List<string> IssueIds { get; set; } = new();
    }

    private class Run
    {
        public SkeletonContext Ctx { get; set; } = null!;
        public List<DateOnly> BusinessDays { get; set; } = new();
        public Dictionary<string, List<DateOnly>> EligibleDays { get; } = new(StringComparer.Ordinal);
        public int Counter { get; set; }
    }

    /// <summary>
    /// Builds every non-relevant thread. Message totals per category equal <paramref name="messages"/>; senders of daily
    /// business are weighted by how much relevant mail they send in <paramref name="relevantThreads"/>.
    /// </summary>
    public static NonRelevantSkeleton Build(SkeletonContext ctx, IReadOnlyDictionary<NonRelevantCategory, int> messages, IReadOnlyList<PlannedThread> relevantThreads)
    {
        var run = new Run { Ctx = ctx, BusinessDays = BusinessDaysIn(ctx.WindowStart, ctx.WindowEnd) };
        var result = new NonRelevantSkeleton();
        if (ctx.CastById.Count == 0 || run.BusinessDays.Count == 0)
        {
            if (messages.Values.Any(v => v > 0))
                ctx.Notes.Add("The cast or the date window is empty, so no non-relevant emails were planned.");
            return result;
        }

        var daily = BuildDaily(run, Count(messages, NonRelevantCategory.DailyBusiness), relevantThreads);
        var ancillary = BuildAncillary(run, Count(messages, NonRelevantCategory.Ancillary));
        var decoy = BuildDecoys(run, Count(messages, NonRelevantCategory.Decoy));

        // Same exact-share attachment rule as relevant threads, applied per category
        AssignAttachments(daily, ctx);
        AssignAttachments(ancillary, ctx);
        AssignAttachments(decoy, ctx);

        result.Threads.AddRange(daily);
        result.Threads.AddRange(ancillary);
        result.Threads.AddRange(decoy);
        return result;
    }

    // ----- Daily business -----

    private static List<PlannedThread> BuildDaily(Run run, int messages, IReadOnlyList<PlannedThread> relevantThreads)
    {
        var ctx = run.Ctx;
        var sizes = BudgetAllocator.SplitIntoThreads(messages, ThreadShapeClass.Business, ctx.Random);
        if (sizes.Count == 0)
            return new List<PlannedThread>();

        // Weight = 1 + relevant messages sent, so key custodians carry plenty of mundane mail (Pitfall 3)
        var sent = (relevantThreads ?? Array.Empty<PlannedThread>())
            .Where(t => t != null)
            .SelectMany(t => t.Messages.Where(m => m != null))
            .GroupBy(m => m.FromId ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var senders = ctx.CastById.Keys.Where(id => Eligible(run, id).Count > 0).ToList();
        var weights = senders.Select(id => 1 + (sent.TryGetValue(id, out var n) ? n : 0)).ToList();

        var specs = new List<Spec>();
        foreach (var size in sizes)
        {
            var sender = senders[WeightedIndex(weights, ctx.Random)];
            var anchor = PickDay(run, sender);
            var spec = new Spec
            {
                Category = NonRelevantCategory.DailyBusiness,
                Size = size,
                Anchor = anchor,
                Low = ctx.WindowStart,
                Sender = sender,
                Pool = Colleagues(ctx, sender, anchor)
            };

            if (spec.Pool.Count == 0 || ThreadShapes.Chance((int)Math.Round(ExternalDailyShare * 100), ctx.Random))
            {
                var outsider = OtherOrganizationPerson(ctx, sender, anchor);
                if (outsider != null)
                {
                    spec.Pool.Insert(0, outsider);
                    spec.Forced.Add(outsider);
                    spec.IsCrossOrganization = true;
                }
            }

            specs.Add(spec);
        }

        AssignDailyPrivilege(ctx, specs);
        return specs.Select(s => BuildThread(run, s)).ToList();
    }

    /// <summary>
    /// About 2% of daily threads copy the sender organization's in-house counsel and are privileged but not relevant.
    /// Every participant stays inside that organization (validator rule 12); cross-organization threads are never picked.
    /// </summary>
    private static void AssignDailyPrivilege(SkeletonContext ctx, List<Spec> specs)
    {
        var candidates = new List<(Spec Spec, string Counsel)>();
        foreach (var spec in specs.Where(s => !s.IsCrossOrganization))
        {
            var org = OrgOf(ctx, spec.Sender);
            if (org.Length == 0)
                continue;

            var people = spec.Pool.Append(spec.Sender).ToList();
            var counsel = people.FirstOrDefault(id => ctx.CastById[id].Role == CastRole.InHouseCounsel && OrgOf(ctx, id) == org);
            if (counsel != null && people.Count >= 2)
                candidates.Add((spec, counsel));
        }

        if (candidates.Count == 0)
            return;

        var target = (int)Math.Round(specs.Count * PrivilegedDailyShare, MidpointRounding.AwayFromZero);
        if (target == 0 && specs.Count >= MinDailyThreadsForPrivileged)
            target = 1;
        Shuffle(candidates, ctx.Random);
        foreach (var (spec, counsel) in candidates.Take(Math.Min(target, candidates.Count)))
        {
            spec.IsPrivileged = true;
            if (counsel != spec.Sender)
                spec.Forced.Insert(0, counsel);
        }
    }

    // ----- Ancillary (background staff) -----

    private static List<PlannedThread> BuildAncillary(Run run, int messages)
    {
        var ctx = run.Ctx;
        var sizes = BudgetAllocator.SplitIntoThreads(messages, ThreadShapeClass.Ancillary, ctx.Random);
        if (sizes.Count == 0)
            return new List<PlannedThread>();

        var senders = ctx.CastById.Values.Where(c => c.Tier == CastTier.Background && Eligible(run, c.Id).Count > 0).Select(c => c.Id).ToList();
        if (senders.Count == 0)
        {
            ctx.Notes.Add("The cast has no background staff, so ancillary emails are written by key characters.");
            senders = ctx.CastById.Keys.Where(id => Eligible(run, id).Count > 0).ToList();
        }

        var threads = new List<PlannedThread>();
        foreach (var size in sizes)
        {
            var sender = senders[ctx.Random.Next(senders.Count)];
            var anchor = PickDay(run, sender);
            var colleagues = Colleagues(ctx, sender, anchor);
            var key = colleagues.Where(id => ctx.CastById[id].Tier == CastTier.Key).ToList();
            var background = colleagues.Where(id => ctx.CastById[id].Tier != CastTier.Key).ToList();

            var first = ThreadShapes.Chance(AncillaryKeyRecipientPercent, ctx.Random) && key.Count > 0 ? key[ctx.Random.Next(key.Count)]
                : background.Count > 0 ? background[ctx.Random.Next(background.Count)]
                : key.Count > 0 ? key[ctx.Random.Next(key.Count)]
                : OtherOrganizationPerson(ctx, sender, anchor);

            var spec = new Spec
            {
                Category = NonRelevantCategory.Ancillary,
                Size = size,
                Anchor = anchor,
                Low = ctx.WindowStart,
                Sender = sender,
                Pool = colleagues
            };
            if (first != null)
            {
                if (!spec.Pool.Contains(first))
                    spec.Pool.Add(first);
                spec.Forced.Add(first);
            }

            threads.Add(BuildThread(run, spec));
        }

        return threads;
    }

    // ----- Near-miss decoys -----

    private static List<PlannedThread> BuildDecoys(Run run, int messages)
    {
        var ctx = run.Ctx;
        var sizes = BudgetAllocator.SplitIntoThreads(messages, ThreadShapeClass.Business, ctx.Random);
        if (sizes.Count == 0)
            return new List<PlannedThread>();

        // Facts two or more cast members knew make real conversations; fall back to any fact with a cast knower
        var facts = ctx.DatedFacts.Where(f => Knowers(ctx, f).Count >= 2).ToList();
        if (facts.Count == 0)
            facts = ctx.DatedFacts.Where(f => Knowers(ctx, f).Count >= 1).ToList();

        var threads = new List<PlannedThread>();
        if (facts.Count == 0)
        {
            // No fact to borrow from: keep the totals exact with business-like threads that borrow no issues
            ctx.Notes.Add("No dated key fact has a cast member who knew about it, so decoy emails borrow no issues.");
            var senders = ctx.CastById.Keys.Where(id => Eligible(run, id).Count > 0).ToList();
            foreach (var size in sizes)
            {
                var sender = senders[ctx.Random.Next(senders.Count)];
                var anchor = PickDay(run, sender);
                threads.Add(BuildThread(run, new Spec
                {
                    Category = NonRelevantCategory.Decoy,
                    Size = size,
                    Anchor = anchor,
                    Low = ctx.WindowStart,
                    Sender = sender,
                    Pool = Colleagues(ctx, sender, anchor)
                }));
            }

            return threads;
        }

        foreach (var size in sizes)
        {
            Spec? spec = null;
            for (var attempt = 0; attempt < MaxDecoyPicks && spec == null; attempt++)
            {
                var fact = facts[ctx.Random.Next(facts.Count)];
                var (low, high) = DecoyRange(ctx, fact);
                var anchor = DecoyAnchor(ctx, fact, low, high);
                var knowers = Knowers(ctx, fact);
                var people = Stable(ctx, knowers, anchor);
                if (people.Count == 0 && attempt < MaxDecoyPicks - 1)
                    continue;
                if (people.Count == 0)
                    people = knowers.Where(id => ctx.IsActive(id, anchor)).ToList();
                if (people.Count == 0)
                    people = knowers;

                var sender = people[ctx.Random.Next(people.Count)];
                spec = new Spec
                {
                    Category = NonRelevantCategory.Decoy,
                    Size = size,
                    Anchor = anchor,
                    Low = low,
                    Sender = sender,
                    Pool = people.Where(id => id != sender).ToList(),
                    IssueIds = Clean(fact.IssueIds)
                };
            }

            threads.Add(BuildThread(run, spec!));
        }

        return threads;
    }

    private static (DateOnly Low, DateOnly High) DecoyRange(SkeletonContext ctx, KeyFact fact)
    {
        var date = SkeletonContext.FactDate(fact) ?? ctx.WindowStart;
        var low = date.AddDays(-DecoyDaysAroundFact);
        var high = date.AddDays(DecoyDaysAroundFact);
        if (low < ctx.WindowStart)
            low = ctx.WindowStart;
        if (high > ctx.WindowEnd)
            high = ctx.WindowEnd;
        if (low > high)
        {
            // Fact outside the window: use the nearest window edge
            var edge = date < ctx.WindowStart ? ctx.WindowStart : ctx.WindowEnd;
            low = high = edge;
        }

        return (low, high);
    }

    // Fact date + uniform(-30..+30) days, clamped to the range and moved to a business day inside it when one exists
    private static DateOnly DecoyAnchor(SkeletonContext ctx, KeyFact fact, DateOnly low, DateOnly high)
    {
        var date = SkeletonContext.FactDate(fact) ?? ctx.WindowStart;
        var day = date.AddDays(ctx.Random.Next(-DecoyDaysAroundFact, DecoyDaysAroundFact + 1));
        if (day < low)
            day = low;
        if (day > high)
            day = high;

        var next = BusinessCalendar.NextBusinessDay(day);
        if (next <= high)
            return next;
        var previous = PreviousBusinessDay(day);
        return previous >= low ? previous : next;
    }

    // ----- Thread structure (same shape rules as ThreadSkeletonBuilder) -----

    private static PlannedThread BuildThread(Run run, Spec spec)
    {
        var ctx = run.Ctx;
        var random = ctx.Random;
        run.Counter++;
        var thread = new PlannedThread
        {
            Id = $"N-{run.Counter:0000}",
            Relevance = ThreadRelevance.NotRelevant,
            Category = spec.Category,
            IsPrivileged = spec.IsPrivileged,
            IssueIds = new List<string>(spec.IssueIds),
            OwnerCustodianId = spec.Sender
        };

        var sender = spec.Sender;
        var others = spec.Pool.Where(id => id != sender).Distinct(StringComparer.Ordinal).ToList();
        Shuffle(others, random);
        for (var i = spec.Forced.Count - 1; i >= 0; i--)
        {
            var forced = spec.Forced[i];
            if (forced == sender)
                continue;
            others.Remove(forced);
            others.Insert(0, forced);
        }

        var forcedCount = spec.Forced.Count(id => id != sender);
        var toCount = Math.Max(forcedCount, Math.Min(ThreadShapes.SampleRootToCount(random), others.Count));
        var ccCount = Math.Min(ThreadShapes.SampleRootCcCount(random), others.Count - toCount);
        thread.Messages.Add(new PlannedMessage
        {
            Seq = 1,
            ReplyToSeq = 0,
            Kind = MessageKind.New,
            FromId = sender,
            ToIds = toCount > 0 ? others.Take(toCount).ToList() : new List<string> { sender },
            CcIds = others.Skip(toCount).Take(ccCount).ToList(),
            Basis = RelevanceBasis.None
        });

        var people = others.Append(sender).ToList();
        for (var seq = 2; seq <= spec.Size; seq++)
            thread.Messages.Add(BuildReply(ctx, thread, seq, people));

        for (var i = 0; i < thread.Messages.Count; i++)
            thread.Messages[i].Id = $"{thread.Id}-M{i + 1:00}";

        ScheduleToFit(run, thread, spec.Anchor, spec.Low);
        return thread;
    }

    private static PlannedMessage BuildReply(SkeletonContext ctx, PlannedThread thread, int seq, List<string> people)
    {
        var random = ctx.Random;
        var replyTo = ThreadShapes.SampleReplyToSeq(seq, random);
        var parent = thread.Messages[replyTo - 1];

        var sender = parent.CcIds.Count > 0 && ThreadShapes.Chance(SenderFromCcPercent, random)
            ? parent.CcIds[random.Next(parent.CcIds.Count)]
            : parent.ToIds[random.Next(parent.ToIds.Count)];

        var kind = ThreadShapes.SampleKind(parent.ToIds.Count + parent.CcIds.Count, random);
        string? forwardTarget = null;
        if (kind == MessageKind.Forward)
        {
            // Forwards stay inside the thread's pool (so privileged threads stay inside counsel's organization)
            var onThread = thread.Messages.SelectMany(Participants).ToHashSet(StringComparer.Ordinal);
            var candidates = people.Where(id => id != sender && !onThread.Contains(id)).ToList();
            forwardTarget = candidates.Count > 0 ? candidates[random.Next(candidates.Count)] : null;
        }

        var resolved = ThreadShapes.ResolveKind(kind, parent, sender, forwardTarget);
        var (to, cc) = ThreadShapes.RecipientsFor(resolved, parent, sender, forwardTarget, random);
        return new PlannedMessage
        {
            Seq = seq,
            ReplyToSeq = replyTo,
            Kind = resolved,
            FromId = sender,
            ToIds = to,
            CcIds = cc,
            Basis = RelevanceBasis.None
        };
    }

    /// <summary>
    /// Schedules the thread; when a participant is not active on a message's sender-local date or the thread runs past
    /// the window, it is rescheduled (moved earlier when it ran past the window end, never before <paramref name="low"/>).
    /// </summary>
    private static void ScheduleToFit(Run run, PlannedThread thread, DateOnly anchor, DateOnly low)
    {
        var ctx = run.Ctx;
        for (var attempt = 0; attempt < MaxScheduleAttempts; attempt++)
        {
            ThreadScheduler.Schedule(thread, anchor, ctx);
            var last = thread.Messages.Max(m => BusinessCalendar.LocalDate(m.SentAt, ctx.ZoneFor(m.FromId)));
            var inactive = thread.Messages.Any(m =>
            {
                var day = BusinessCalendar.LocalDate(m.SentAt, ctx.ZoneFor(m.FromId));
                return Participants(m).Any(id => !ctx.IsActive(id, day));
            });
            if (!inactive && last <= ctx.WindowEnd)
                return;

            if (last > ctx.WindowEnd)
            {
                var moved = anchor.AddDays(-(last.DayNumber - ctx.WindowEnd.DayNumber) - 1);
                anchor = moved < low ? low : moved;
            }
        }

        ctx.Notes.Add($"Thread {thread.Id} could not be scheduled entirely inside its people's active dates and the date window.");
    }

    // ----- Attachments (same rule as relevant threads) -----

    private static void AssignAttachments(List<PlannedThread> threads, SkeletonContext ctx)
    {
        if (ctx.EnabledTypes.Count == 0 || ctx.AttachmentPercent <= 0 || threads.Count == 0)
            return;

        var total = threads.Sum(t => t.Messages.Count);
        var eligible = threads
            .SelectMany(t => t.Messages.Select(m => (Message: m, Allowed: AllowedTypes(ctx, t, m))))
            .Where(x => x.Allowed.Count > 0)
            .ToList();

        // Exact share: round(messages x percent) attached messages, chosen at random among those that can carry one
        var target = Math.Min(eligible.Count, (int)Math.Round(total * ctx.AttachmentPercent / 100.0, MidpointRounding.AwayFromZero));
        Shuffle(eligible, ctx.Random);
        foreach (var (message, allowed) in eligible.Take(target))
        {
            var count = ThreadShapes.SampleAttachmentCount(ctx.Random);
            for (var k = 0; k < count; k++)
            {
                var kind = ThreadShapes.SampleAttachmentType(allowed, message.Seq == 1, ctx.Random);
                if (kind == null)
                    break;
                AddAttachment(message, kind.Value);
            }
        }
    }

    private static void AddAttachment(PlannedMessage message, AttachmentType kind)
    {
        var n = message.Attachments.Count(a => a.Kind == kind) + 1;
        message.Attachments.Add(new PlannedAttachment { Kind = kind, FileName = DefaultFileName(kind, n), Title = string.Empty });
    }

    // Calendar invites only on roots, voicemail only on single-message threads
    private static List<AttachmentType> AllowedTypes(SkeletonContext ctx, PlannedThread thread, PlannedMessage message) =>
        ctx.EnabledTypes
            .Where(t => (t != AttachmentType.CalendarInvite || message.Seq == 1)
                        && (t != AttachmentType.Voicemail || thread.Messages.Count == 1))
            .ToList();

    // Code default file name, same pattern as relevant threads; the LLM title comes later
    private static string DefaultFileName(AttachmentType kind, int n) => kind switch
    {
        AttachmentType.Word => $"Document {n}.docx",
        AttachmentType.Excel => $"Spreadsheet {n}.xlsx",
        AttachmentType.PowerPoint => $"Presentation {n}.pptx",
        AttachmentType.Image => $"Photo {n}.png",
        AttachmentType.CalendarInvite => n == 1 ? "Meeting.ics" : $"Meeting {n}.ics",
        AttachmentType.Voicemail => n == 1 ? "Voicemail.mp3" : $"Voicemail {n}.mp3",
        _ => $"Attachment {n}.bin"
    };

    // ----- People and dates -----

    /// <summary>
    /// Business days in the window on which the person is active, preferring days where they stay active for
    /// <see cref="StableDays"/> more days; cached per person.
    /// </summary>
    private static List<DateOnly> Eligible(Run run, string id)
    {
        if (run.EligibleDays.TryGetValue(id, out var cached))
            return cached;

        var ctx = run.Ctx;
        var days = run.BusinessDays.Where(d => ctx.IsActive(id, d) && ctx.IsActive(id, d.AddDays(StableDays))).ToList();
        if (days.Count == 0)
            days = run.BusinessDays.Where(d => ctx.IsActive(id, d)).ToList();
        run.EligibleDays[id] = days;
        return days;
    }

    private static DateOnly PickDay(Run run, string id)
    {
        var days = Eligible(run, id);
        return days.Count > 0 ? days[run.Ctx.Random.Next(days.Count)] : run.BusinessDays[run.Ctx.Random.Next(run.BusinessDays.Count)];
    }

    // Same-organization cast members (key and background) who are active on the anchor, preferring those who stay active
    private static List<string> Colleagues(SkeletonContext ctx, string sender, DateOnly anchor)
    {
        var org = OrgOf(ctx, sender);
        if (org.Length == 0)
            return new List<string>();
        var colleagues = ctx.CastById.Keys.Where(id => id != sender && OrgOf(ctx, id) == org).ToList();
        return Stable(ctx, colleagues, anchor);
    }

    // People active from the anchor through StableDays later; otherwise those active on the anchor
    private static List<string> Stable(SkeletonContext ctx, IEnumerable<string> ids, DateOnly anchor)
    {
        var list = ids.ToList();
        var stable = list.Where(id => ctx.IsActive(id, anchor) && ctx.IsActive(id, anchor.AddDays(StableDays))).ToList();
        return stable.Count > 0 ? stable : list.Where(id => ctx.IsActive(id, anchor)).ToList();
    }

    // One active person from another Party or ThirdParty organization (any other organization when there is none)
    private static string? OtherOrganizationPerson(SkeletonContext ctx, string sender, DateOnly anchor)
    {
        var org = OrgOf(ctx, sender);
        var others = ctx.CastById.Values.Where(c => c.Id != sender && (c.OrganizationId ?? string.Empty) != org).ToList();
        var business = others.Where(c => ctx.OrganizationFor(c.Id)?.Kind is OrganizationKind.Party or OrganizationKind.ThirdParty).Select(c => c.Id);
        var candidates = Stable(ctx, business, anchor);
        if (candidates.Count == 0)
            candidates = Stable(ctx, others.Select(c => c.Id), anchor);
        return candidates.Count > 0 ? candidates[ctx.Random.Next(candidates.Count)] : null;
    }

    // Cast members who knew the fact (ActorIds ∪ KnownByIds)
    private static List<string> Knowers(SkeletonContext ctx, KeyFact fact) =>
        Clean(fact.ActorIds).Concat(Clean(fact.KnownByIds)).Where(ctx.CastById.ContainsKey).Distinct(StringComparer.Ordinal).ToList();

    private static string OrgOf(SkeletonContext ctx, string id) =>
        ctx.CastById.TryGetValue(id ?? string.Empty, out var member) ? member.OrganizationId ?? string.Empty : string.Empty;

    private static int Count(IReadOnlyDictionary<NonRelevantCategory, int> messages, NonRelevantCategory category) =>
        messages != null && messages.TryGetValue(category, out var n) ? Math.Max(0, n) : 0;

    private static List<DateOnly> BusinessDaysIn(DateOnly start, DateOnly end)
    {
        var days = new List<DateOnly>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (BusinessCalendar.IsBusinessDay(d))
                days.Add(d);
        }

        return days;
    }

    private static DateOnly PreviousBusinessDay(DateOnly day)
    {
        // At most a long weekend plus a holiday; bounded by the calendar
        while (!BusinessCalendar.IsBusinessDay(day))
            day = day.AddDays(-1);
        return day;
    }

    private static int WeightedIndex(List<int> weights, Random random)
    {
        var roll = random.Next(weights.Sum());
        for (var i = 0; i < weights.Count; i++)
        {
            if (roll < weights[i])
                return i;
            roll -= weights[i];
        }

        return weights.Count - 1;
    }

    private static IEnumerable<string> Participants(PlannedMessage m) => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds);

    private static List<string> Clean(List<string>? ids) =>
        (ids ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
