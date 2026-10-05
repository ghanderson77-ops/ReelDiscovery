using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Turns the relevant budget into fully structured, scheduled relevant threads (PLAN-02, PLAN-03, D-13): facts, issues,
/// participants, kinds, reply links and timestamps are all decided here; the LLM later writes the text fields only.
/// Pure: every draw uses the injected <see cref="SkeletonContext.Random"/>.
/// </summary>
public static class ThreadSkeletonBuilder
{
    // Share of relevant threads that carry two concurrent facts from different issues (D-15, RESEARCH A5: 10-15%)
    public const double MultiIssueTargetShare = 0.12;
    public const double MultiIssueMinShare = 0.10;
    public const double MultiIssueMaxShare = 0.15;

    // Normal pairs are this close; the top-up pass may use the nearest pairs up to the fallback window
    public const int MultiIssueWindowBusinessDays = 20;
    public const int MultiIssueFallbackWindowBusinessDays = 30;

    // Anchor offset after the fact date: 60% within 0..2 business days, otherwise 3..15
    private const int NearAnchorPercent = 60;
    private const int NearAnchorMaxDays = 2;
    private const int FarAnchorMaxDays = 15;

    // Later messages carry their own fact evidence 60% of the time, otherwise only quote it
    private const int OwnEvidencePercent = 60;

    // A later sender is picked from the parent CC instead of the parent To 25% of the time (when there is a CC)
    private const int SenderFromCcPercent = 25;

    // Default share of relevant threads that are privileged (RESEARCH Pattern 4 step 8, assumed)
    public const int DefaultPrivilegedPercent = 8;

    // Repair passes per thread before truncating (each sender swap reschedules the thread)
    private const int MaxRepairPasses = 3;

    // A replacement thread for truncated messages uses people active from the anchor through this many days later
    private const int StablePoolDays = 30;

    // Facts further apart than this are never compared for multi-issue pairing (keeps the business-day walk bounded;
    // 30 business days is at most 6 weeks plus a few holidays)
    private const int MaxPairCalendarDays = 46;

    private class ThreadSpec
    {
        public List<KeyFact> Facts { get; set; } = new();
        public int Size { get; set; }
        public DateOnly Anchor { get; set; }
        public bool IsPrivileged { get; set; }
        public string? CounselId { get; set; }

        // Restricted people (privileged side or a replacement thread's stable pool); null = everyone who knew the facts
        public List<string>? Pool { get; set; }

        // Replacement thread for messages removed by repair; never rebalanced again
        public bool IsReplacement { get; set; }
    }

    private class BuiltThread
    {
        public PlannedThread Thread { get; set; } = null!;
        public ThreadSpec Spec { get; set; } = null!;
        public List<string> Pool { get; set; } = new();
    }

    /// <summary>
    /// Builds every relevant thread for <paramref name="budget"/>. Message totals equal <c>budget.TotalMessages</c>;
    /// anything the code cannot do (single knowers, nobody active) is recorded in <c>ctx.Notes</c>.
    /// </summary>
    public static List<PlannedThread> BuildRelevant(SkeletonContext ctx, RelevantBudget budget)
    {
        var specs = CreateSpecs(ctx, budget);
        MergeMultiIssue(ctx, specs);
        AssignPrivileged(ctx, specs);

        var built = new List<BuiltThread>();
        var notedSingle = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            var item = BuildThread(ctx, spec, ThreadId(built.Count + 1), notedSingle);
            if (item != null)
                built.Add(item);
        }

        foreach (var item in built)
            ThreadScheduler.Schedule(item.Thread, item.Spec.Anchor, ctx, LatestFactDate(item.Spec));

        Repair(ctx, built, notedSingle);

        var threads = built.Where(b => b.Thread.Messages.Count > 0).Select(b => b.Thread).ToList();
        AssignHot(threads, ctx);
        AssignAttachments(threads, ctx);
        return threads;
    }

    // ----- Specs: one per budget thread, in fact date order -----

    private static List<ThreadSpec> CreateSpecs(SkeletonContext ctx, RelevantBudget budget)
    {
        var factById = new Dictionary<string, KeyFact>(StringComparer.Ordinal);
        foreach (var fact in ctx.DatedFacts)
            factById.TryAdd(fact.Id ?? string.Empty, fact);

        var specs = new List<ThreadSpec>();
        var allocations = (budget.Facts ?? new())
            .Where(a => a != null && factById.ContainsKey(a.FactId ?? string.Empty))
            .OrderBy(a => factById[a.FactId].Date, StringComparer.Ordinal)
            .ThenBy(a => a.FactId, StringComparer.Ordinal);

        foreach (var allocation in allocations)
        {
            var fact = factById[allocation.FactId];
            foreach (var size in allocation.MessagesPerThread ?? new())
            {
                if (size <= 0)
                    continue;
                specs.Add(new ThreadSpec
                {
                    Facts = new List<KeyFact> { fact },
                    Size = Math.Min(size, ThreadShapes.MaxThreadDepth),
                    Anchor = SampleAnchor(ctx, fact)
                });
            }
        }

        return specs;
    }

    private static DateOnly SampleAnchor(SkeletonContext ctx, KeyFact fact)
    {
        var factDate = SkeletonContext.FactDate(fact) ?? ctx.WindowStart;
        var offset = ThreadShapes.Chance(NearAnchorPercent, ctx.Random)
            ? ctx.Random.Next(0, NearAnchorMaxDays + 1)
            : ctx.Random.Next(NearAnchorMaxDays + 1, FarAnchorMaxDays + 1);
        var anchor = AddBusinessDays(factDate, offset);

        // Never before the fact (knowledge rule wins over the window); otherwise inside the window
        var low = factDate > ctx.WindowStart ? factDate : ctx.WindowStart;
        var high = ctx.WindowEnd > low ? ctx.WindowEnd : low;
        if (anchor < low)
            anchor = low;
        if (anchor > high)
            anchor = high;
        return BusinessCalendar.NextBusinessDay(anchor);
    }

    // ----- Multi-issue threads (D-15) -----

    private class IssuePair
    {
        public KeyFact A { get; set; } = null!;
        public KeyFact B { get; set; } = null!;
        public int Gap { get; set; }
    }

    /// <summary>
    /// Merges single-fact specs of two concurrent facts from different issues into one thread (D-15). Normal pairs are
    /// within <see cref="MultiIssueWindowBusinessDays"/>; when they fall short of <see cref="MultiIssueMinShare"/>, the
    /// nearest pairs up to <see cref="MultiIssueFallbackWindowBusinessDays"/> top it up. Every pair needs at least one
    /// person who knew both facts and is active on the merged anchor, so no knowledge is invented.
    /// </summary>
    private static void MergeMultiIssue(SkeletonContext ctx, List<ThreadSpec> specs)
    {
        if (specs.Count == 0)
            return;

        var issues = ctx.DatedFacts.SelectMany(f => Clean(f.IssueIds)).Distinct(StringComparer.Ordinal).Count();
        if (issues < 2)
        {
            ctx.Notes.Add("The case guide has only one issue with dated facts, so no thread combines two issues.");
            return;
        }

        var normal = new List<IssuePair>();
        var fallback = new List<IssuePair>();
        var facts = ctx.DatedFacts;
        for (var i = 0; i < facts.Count; i++)
        {
            for (var j = i + 1; j < facts.Count; j++)
            {
                var gap = MultiIssueGap(ctx, facts[i], facts[j]);
                if (gap == null)
                    continue;

                var pair = new IssuePair { A = facts[i], B = facts[j], Gap = gap.Value };
                (gap.Value <= MultiIssueWindowBusinessDays ? normal : fallback).Add(pair);
            }
        }

        if (normal.Count == 0 && fallback.Count == 0)
        {
            ctx.Notes.Add($"No two facts from different issues happen within {MultiIssueFallbackWindowBusinessDays} business days " +
                          "with someone who knew both, so no thread combines two issues. Add people under Who knew to connect them.");
            return;
        }

        Shuffle(normal, ctx.Random);
        var merged = MergePairs(ctx, specs, normal, 0, MultiIssueTargetShare);

        // Top up with the nearest cross-issue pairs (smallest gap first) only when the normal pairs fall short
        if (merged < Target(specs.Count, MultiIssueMinShare))
        {
            var nearest = normal.Concat(fallback)
                .OrderBy(p => p.Gap)
                .ThenBy(p => p.A.Id, StringComparer.Ordinal)
                .ThenBy(p => p.B.Id, StringComparer.Ordinal)
                .ToList();
            MergePairs(ctx, specs, nearest, merged, MultiIssueMinShare);
        }
    }

    // Round-robin over the pairs until merged specs reach share x the current spec count; returns the merged total
    private static int MergePairs(SkeletonContext ctx, List<ThreadSpec> specs, List<IssuePair> pairs, int merged, double share)
    {
        var progress = true;
        // Each merge turns a single-fact spec into a merged one and never adds a merged spec, so this is bounded
        while (progress && merged < Target(specs.Count, share))
        {
            progress = false;
            foreach (var pair in pairs)
            {
                if (merged >= Target(specs.Count, share))
                    break;
                if (!TryMerge(ctx, specs, pair))
                    continue;

                merged++;
                progress = true;
            }
        }

        return merged;
    }

    // Merged specs wanted for this share of the current spec count, never above the maximum share
    private static int Target(int specCount, double share)
    {
        var wanted = (int)Math.Ceiling(specCount * share - 1e-9);
        var cap = (int)Math.Floor(specCount * MultiIssueMaxShare + 1e-9);
        return Math.Min(wanted, cap);
    }

    private static bool TryMerge(SkeletonContext ctx, List<ThreadSpec> specs, IssuePair pair)
    {
        var specsA = SingleFactSpecs(specs, pair.A);
        var specsB = SingleFactSpecs(specs, pair.B);

        // Keep at least one single-fact thread for each fact
        if (specsA.Count < 2 || specsB.Count < 2)
            return false;

        var keep = specsA[ctx.Random.Next(specsA.Count)];
        var drop = specsB[ctx.Random.Next(specsB.Count)];

        // Someone who knew both facts must be active on the merged anchor, or the thread would invent knowledge
        var anchor = drop.Anchor > keep.Anchor ? drop.Anchor : keep.Anchor;
        if (!KnowerPool(ctx, new List<KeyFact> { pair.A, pair.B }).Any(id => ctx.IsActive(id, anchor)))
            return false;

        Merge(ctx, specs, keep, drop);
        return true;
    }

    /// <summary>
    /// Business days between two facts that may share a thread: different issue sets, both dated, at most
    /// <see cref="MultiIssueFallbackWindowBusinessDays"/> apart, and at least one shared knower active on the later
    /// fact's date. Null when the pair does not qualify.
    /// </summary>
    private static int? MultiIssueGap(SkeletonContext ctx, KeyFact a, KeyFact b)
    {
        var issuesA = Clean(a.IssueIds);
        var issuesB = Clean(b.IssueIds);
        if (issuesA.Count == 0 || issuesB.Count == 0 || issuesA.ToHashSet(StringComparer.Ordinal).SetEquals(issuesB))
            return null;

        var dateA = SkeletonContext.FactDate(a);
        var dateB = SkeletonContext.FactDate(b);
        if (dateA == null || dateB == null)
            return null;

        var gap = BusinessDaysBetween(dateA.Value, dateB.Value);
        if (gap > MultiIssueFallbackWindowBusinessDays)
            return null;

        var later = BusinessCalendar.NextBusinessDay(dateA.Value > dateB.Value ? dateA.Value : dateB.Value);
        return KnowerPool(ctx, new List<KeyFact> { a, b }).Any(id => ctx.IsActive(id, later)) ? gap : null;
    }

    private static List<ThreadSpec> SingleFactSpecs(List<ThreadSpec> specs, KeyFact fact) =>
        specs.Where(s => s.Facts.Count == 1 && ReferenceEquals(s.Facts[0], fact)).ToList();

    private static void Merge(SkeletonContext ctx, List<ThreadSpec> specs, ThreadSpec keep, ThreadSpec drop)
    {
        var droppedFact = drop.Facts[0];
        keep.Facts = keep.Facts.Concat(drop.Facts)
            .OrderBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();
        if (drop.Anchor > keep.Anchor)
            keep.Anchor = drop.Anchor;

        var total = keep.Size + drop.Size;
        keep.Size = Math.Min(total, ThreadShapes.MaxThreadDepth);
        var remainder = total - keep.Size;
        specs.Remove(drop);

        // Totals stay exact: overflow goes to another thread of the dropped fact, then of the kept fact, then a new thread
        remainder = Absorb(specs, droppedFact, remainder);
        remainder = Absorb(specs, keep.Facts.First(f => !ReferenceEquals(f, droppedFact)), remainder);
        if (remainder > 0)
        {
            specs.Add(new ThreadSpec
            {
                Facts = new List<KeyFact> { droppedFact },
                Size = remainder,
                Anchor = SampleAnchor(ctx, droppedFact)
            });
        }
    }

    private static int Absorb(List<ThreadSpec> specs, KeyFact fact, int remainder)
    {
        foreach (var spec in specs.Where(s => s.Facts.Count == 1 && ReferenceEquals(s.Facts[0], fact)))
        {
            if (remainder <= 0)
                break;
            var room = ThreadShapes.MaxThreadDepth - spec.Size;
            var add = Math.Min(room, remainder);
            spec.Size += add;
            remainder -= add;
        }

        return remainder;
    }

    // ----- Structure -----

    private static BuiltThread? BuildThread(SkeletonContext ctx, ThreadSpec spec, string threadId, HashSet<string> notedSingle)
    {
        var pool = spec.Pool ?? KnowerPool(ctx, spec.Facts);
        var label = string.Join(" and ", spec.Facts.Select(f => f.Id));

        var active = pool.Where(id => ctx.IsActive(id, spec.Anchor)).ToList();
        if (active.Count == 0)
        {
            if (pool.Count == 0)
            {
                // Dangling or missing knowers (T-04-27): use an active key character so the fact is still evidenced
                var stand = ctx.CastById.Values.Where(c => ctx.IsActive(c.Id, spec.Anchor))
                    .OrderBy(c => c.Tier).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
                if (stand == null)
                {
                    ctx.Notes.Add($"{label} has nobody in the cast to write its emails, so {spec.Size} relevant emails were not planned.");
                    return null;
                }

                ctx.Notes.Add($"{label} has nobody in the cast who knew about it, so {stand.FullName} writes notes to self. Add people under Who knew.");
                pool = new List<string> { stand.Id };
                active = new List<string>(pool);
            }
            else
            {
                ctx.Notes.Add($"Nobody who knew about {label} was active on {spec.Anchor:yyyy-MM-dd}; the thread uses the people who did it.");
                var actors = ActorsIn(spec.Facts, pool);
                active = actors.Count > 0 ? actors : new List<string>(pool);
            }
        }

        // One active person would mean emails to themselves: bring in the colleague who knew the most of these facts
        if (active.Count == 1 && spec.Pool == null)
        {
            var colleague = ColleagueFor(ctx, active[0], spec.Facts, spec.Anchor);
            if (colleague != null)
            {
                foreach (var fact in spec.Facts.Where(f => !KnowerPool(ctx, new List<KeyFact> { f }).Contains(colleague)))
                    AddKnowers(ctx, fact, new List<string> { colleague });
                pool = pool.Contains(colleague) ? pool : pool.Append(colleague).ToList();
                active.Add(colleague);
            }
            else if (spec.Facts.Count == 1 && notedSingle.Add(spec.Facts[0].Id))
            {
                ctx.Notes.Add($"{spec.Facts[0].Id} has only one person who knew about it and nobody else was active, so its emails " +
                              "are notes to self. Add people under Who knew to get real threads.");
            }
        }

        var thread = new PlannedThread
        {
            Id = threadId,
            Relevance = ThreadRelevance.Relevant,
            Category = NonRelevantCategory.None,
            IsPrivileged = spec.IsPrivileged,
            FactIds = spec.Facts.Select(f => f.Id).ToList(),
            IssueIds = spec.Facts.SelectMany(f => Clean(f.IssueIds)).Distinct(StringComparer.Ordinal).ToList()
        };

        var counsel = spec.CounselId != null && active.Contains(spec.CounselId) ? spec.CounselId : null;
        BuildMessages(ctx, thread, spec, active, counsel);
        return new BuiltThread { Thread = thread, Spec = spec, Pool = pool };
    }

    /// <summary>
    /// Fills <paramref name="thread"/> with <c>spec.Size</c> messages among <paramref name="people"/>. When
    /// <paramref name="mustInclude"/> is set (privileged counsel), that person is on the root message.
    /// </summary>
    private static void BuildMessages(SkeletonContext ctx, PlannedThread thread, ThreadSpec spec, List<string> people, string? mustInclude)
    {
        var random = ctx.Random;
        var factIds = thread.FactIds;
        thread.Messages.Clear();

        var actors = ActorsIn(spec.Facts, people);
        var keyActors = actors.Where(id => ctx.CastById.TryGetValue(id, out var m) && m.Tier == CastTier.Key).ToList();
        var senderPool = keyActors.Count > 0 ? keyActors
            : actors.Count > 0 ? actors
            : mustInclude != null ? new List<string> { mustInclude }
            : people;
        var sender = senderPool[random.Next(senderPool.Count)];

        var others = people.Where(id => id != sender).ToList();
        Shuffle(others, random);
        if (mustInclude != null && mustInclude != sender && others.Remove(mustInclude))
            others.Insert(0, mustInclude);

        var toCount = Math.Min(ThreadShapes.SampleRootToCount(random), others.Count);
        var ccCount = Math.Min(ThreadShapes.SampleRootCcCount(random), others.Count - toCount);
        var root = new PlannedMessage
        {
            Seq = 1,
            ReplyToSeq = 0,
            Kind = MessageKind.New,
            FromId = sender,
            ToIds = toCount > 0 ? others.Take(toCount).ToList() : new List<string> { sender },
            CcIds = others.Skip(toCount).Take(ccCount).ToList(),
            Basis = RelevanceBasis.Own,
            FactIds = new List<string>(factIds)
        };
        thread.Messages.Add(root);

        for (var seq = 2; seq <= spec.Size; seq++)
            thread.Messages.Add(BuildReply(ctx, thread, seq, people));

        AssignMessageIds(thread);
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
            var onThread = thread.Messages.SelectMany(Participants).ToHashSet(StringComparer.Ordinal);
            var candidates = people.Where(id => id != sender && !onThread.Contains(id)).ToList();
            forwardTarget = candidates.Count > 0 ? candidates[random.Next(candidates.Count)] : null;
        }

        var resolved = ThreadShapes.ResolveKind(kind, parent, sender, forwardTarget);
        var (to, cc) = ThreadShapes.RecipientsFor(resolved, parent, sender, forwardTarget, random);
        var own = ThreadShapes.Chance(OwnEvidencePercent, random);

        return new PlannedMessage
        {
            Seq = seq,
            ReplyToSeq = replyTo,
            Kind = resolved,
            FromId = sender,
            ToIds = to,
            CcIds = cc,
            Basis = own ? RelevanceBasis.Own : RelevanceBasis.Quoted,
            FactIds = own ? new List<string>(thread.FactIds) : new List<string>()
        };
    }

    private static void AssignMessageIds(PlannedThread thread)
    {
        for (var i = 0; i < thread.Messages.Count; i++)
        {
            thread.Messages[i].Seq = i + 1;
            thread.Messages[i].Id = $"{thread.Id}-M{i + 1:00}";
        }
    }

    // ----- Privileged threads (D-09) -----

    private static void AssignPrivileged(SkeletonContext ctx, List<ThreadSpec> specs)
    {
        var percent = ctx.Settings.PrivilegedPercent ?? DefaultPrivilegedPercent;
        if (percent <= 0 || specs.Count == 0)
            return;

        var candidates = new List<(ThreadSpec Spec, string Counsel, List<string> Pool)>();
        foreach (var spec in specs)
        {
            var pool = KnowerPool(ctx, spec.Facts);
            foreach (var counsel in pool.Where(id => IsCounsel(ctx, id) && ctx.IsActive(id, spec.Anchor)))
            {
                var side = SideOf(ctx, counsel);
                var restricted = pool.Where(id => side.Contains(ctx.CastById[id].OrganizationId ?? string.Empty)).ToList();

                // Counsel plus at least one active client-side person, so the thread is a real privileged exchange
                if (restricted.Count(id => ctx.IsActive(id, spec.Anchor)) >= 2)
                {
                    candidates.Add((spec, counsel, restricted));
                    break;
                }
            }
        }

        if (candidates.Count == 0)
            return;

        var target = (int)Math.Round(specs.Count * percent / 100.0, MidpointRounding.AwayFromZero);
        target = Math.Min(Math.Max(target, 1), candidates.Count);
        Shuffle(candidates, ctx.Random);
        foreach (var (spec, counsel, pool) in candidates.Take(target))
        {
            spec.IsPrivileged = true;
            spec.CounselId = counsel;
            spec.Pool = pool;
        }
    }

    private static bool IsCounsel(SkeletonContext ctx, string id) =>
        ctx.CastById.TryGetValue(id, out var member) && member.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel;

    /// <summary>
    /// Organizations on a counsel's client side (same rule as validator rule 12): in-house = own organization; outside
    /// counsel = the firm and the organizations in its RepresentsOrgIds; plus any law firm representing one of those clients.
    /// </summary>
    private static HashSet<string> SideOf(SkeletonContext ctx, string counselId)
    {
        var member = ctx.CastById[counselId];
        var organizations = (ctx.Guide.Organizations ?? new()).Where(o => o != null && !string.IsNullOrEmpty(o.Id)).ToList();
        var orgId = member.OrganizationId ?? string.Empty;

        var clients = new HashSet<string>(StringComparer.Ordinal);
        if (orgId.Length > 0)
            clients.Add(orgId);
        if (member.Role == CastRole.OutsideCounsel)
        {
            var firm = organizations.FirstOrDefault(o => o.Id == orgId);
            if (firm != null)
                clients.UnionWith(Clean(firm.RepresentsOrgIds));
        }

        var side = new HashSet<string>(clients, StringComparer.Ordinal);
        foreach (var org in organizations.Where(o => Clean(o.RepresentsOrgIds).Any(clients.Contains)))
            side.Add(org.Id);
        return side;
    }

    // ----- Repair (D-19) -----

    private static void Repair(SkeletonContext ctx, List<BuiltThread> built, HashSet<string> notedSingle)
    {
        // Replacement threads are appended while iterating; they are never rebalanced again, so this terminates
        for (var i = 0; i < built.Count; i++)
        {
            var item = built[i];
            var removed = RepairThread(ctx, item);
            if (removed == 0)
                continue;

            var label = string.Join(" and ", item.Spec.Facts.Select(f => f.Id));
            if (item.Spec.IsReplacement)
            {
                ctx.Notes.Add($"{removed} relevant emails for {label} could not be planned because nobody who knew about it was active long enough.");
                continue;
            }

            var replacement = ReplacementThread(ctx, item, removed, ThreadId(built.Count + 1), notedSingle);
            if (replacement == null)
            {
                ctx.Notes.Add($"{removed} relevant emails for {label} could not be planned because nobody who knew about it was active.");
                continue;
            }

            ThreadScheduler.Schedule(replacement.Thread, replacement.Spec.Anchor, ctx, LatestFactDate(replacement.Spec));
            built.Add(replacement);
        }
    }

    /// <summary>
    /// Swaps participants who are not active on the sender-local date for active eligible people; truncates the thread
    /// at the first message nobody can fix. Returns the number of messages removed.
    /// </summary>
    private static int RepairThread(SkeletonContext ctx, BuiltThread item)
    {
        var messages = item.Thread.Messages;
        for (var pass = 0; pass < MaxRepairPasses; pass++)
        {
            var senderSwapped = false;
            for (var index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                var day = BusinessCalendar.LocalDate(message.SentAt, ctx.ZoneFor(message.FromId));
                foreach (var person in Participants(message).Distinct(StringComparer.Ordinal).ToList())
                {
                    if (ctx.IsActive(person, day))
                        continue;

                    var replacement = PickReplacement(ctx, item, index, person, day);
                    if (replacement == null)
                        return Truncate(ctx, item, index, person, day);

                    senderSwapped |= Swap(messages, index, person, replacement);
                }
            }

            if (!senderSwapped)
                return 0;

            // A new sender means a new zone and latency: reschedule and check again
            ThreadScheduler.Schedule(item.Thread, item.Spec.Anchor, ctx, LatestFactDate(item.Spec));
        }

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var day = BusinessCalendar.LocalDate(message.SentAt, ctx.ZoneFor(message.FromId));
            var inactive = Participants(message).FirstOrDefault(p => !ctx.IsActive(p, day));
            if (inactive != null)
                return Truncate(ctx, item, index, inactive, day);
        }

        return 0;
    }

    private static string? PickReplacement(SkeletonContext ctx, BuiltThread item, int index, string person, DateOnly day)
    {
        var used = item.Thread.Messages.Skip(index).SelectMany(Participants).ToHashSet(StringComparer.Ordinal);
        var mustBeCounsel = item.Thread.IsPrivileged && IsCounsel(ctx, person);
        var candidates = item.Pool
            .Where(id => !used.Contains(id) && ctx.IsActive(id, day) && (!mustBeCounsel || IsCounsel(ctx, id)))
            .ToList();
        return candidates.Count > 0 ? candidates[ctx.Random.Next(candidates.Count)] : null;
    }

    // Replaces the person from message index onward so the reply structure stays coherent; true when a sender changed
    private static bool Swap(List<PlannedMessage> messages, int index, string person, string replacement)
    {
        var senderChanged = false;
        for (var i = index; i < messages.Count; i++)
        {
            var message = messages[i];
            if (message.FromId == person)
            {
                message.FromId = replacement;
                senderChanged = true;
            }

            message.ToIds = message.ToIds.Select(id => id == person ? replacement : id).Distinct(StringComparer.Ordinal).ToList();
            var to = message.ToIds;
            message.CcIds = message.CcIds.Select(id => id == person ? replacement : id)
                .Where(id => !to.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
        }

        return senderChanged;
    }

    private static int Truncate(SkeletonContext ctx, BuiltThread item, int index, string person, DateOnly day)
    {
        var messages = item.Thread.Messages;
        var removed = messages.Count - index;
        messages.RemoveRange(index, removed);
        var name = ctx.CastById.TryGetValue(person, out var member) && member.FullName.Length > 0 ? member.FullName : person;
        ctx.Notes.Add($"Thread {item.Thread.Id} was shortened because {name} was not active on {day:yyyy-MM-dd}.");
        return removed;
    }

    /// <summary>
    /// A new thread of the same facts carrying the removed messages, among people active from the anchor through
    /// <see cref="StablePoolDays"/> days later, so the totals stay exact.
    /// </summary>
    private static BuiltThread? ReplacementThread(SkeletonContext ctx, BuiltThread item, int size, string threadId, HashSet<string> notedSingle)
    {
        var anchor = item.Spec.Anchor;
        var stable = item.Pool.Where(id => ctx.IsActive(id, anchor) && ctx.IsActive(id, anchor.AddDays(StablePoolDays))).ToList();
        if (stable.Count == 0)
            stable = item.Pool.Where(id => ctx.IsActive(id, anchor)).ToList();
        if (stable.Count == 0)
            return null;

        var counsel = item.Spec.CounselId;
        var privileged = item.Spec.IsPrivileged && counsel != null && stable.Contains(counsel) && stable.Count >= 2;
        var spec = new ThreadSpec
        {
            Facts = item.Spec.Facts,
            Size = size,
            Anchor = anchor,
            IsPrivileged = privileged,
            CounselId = privileged ? counsel : null,
            Pool = stable,
            IsReplacement = true
        };
        return BuildThread(ctx, spec, threadId, notedSingle);
    }

    // ----- Hot documents (D-09) -----

    private static void AssignHot(List<PlannedThread> threads, SkeletonContext ctx)
    {
        foreach (var fact in ctx.DatedFacts.Where(f => f.Significance == FactSignificance.Hot))
        {
            var threadsOfFact = threads.Count(t => t.FactIds.Contains(fact.Id));
            if (threadsOfFact == 0)
                continue;

            var count = ctx.Settings.HotPerHotFact ?? (threadsOfFact >= 3 ? 2 : 1);
            var picks = threads
                .SelectMany(t => t.Messages)
                .Where(m => m.Basis == RelevanceBasis.Own && m.FactIds.Contains(fact.Id) && !m.IsHot)
                .OrderByDescending(m => Participants(m).Distinct(StringComparer.Ordinal).Count())
                .ThenBy(m => m.SentAt)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .Take(Math.Max(0, count))
                .ToList();
            foreach (var message in picks)
                message.IsHot = true;
        }
    }

    // ----- Attachments (D-11, PLAN-05) -----

    private static void AssignAttachments(List<PlannedThread> threads, SkeletonContext ctx)
    {
        if (ctx.EnabledTypes.Count == 0 || ctx.AttachmentPercent <= 0)
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

                var n = message.Attachments.Count(a => a.Kind == kind.Value) + 1;
                message.Attachments.Add(new PlannedAttachment
                {
                    Kind = kind.Value,
                    FileName = DefaultFileName(kind.Value, n),
                    Title = string.Empty
                });
            }
        }
    }

    // Enabled types this message can carry (ctx.EnabledTypes): calendar invites only on roots, voicemail only on single-message threads
    private static List<AttachmentType> AllowedTypes(SkeletonContext ctx, PlannedThread thread, PlannedMessage message) =>
        ctx.EnabledTypes
            .Where(t => (t != AttachmentType.CalendarInvite || message.Seq == 1)
                        && (t != AttachmentType.Voicemail || thread.Messages.Count == 1))
            .ToList();

    /// <summary>
    /// Code default file name ("Document 1.docx", "Meeting.ics", "Voicemail.mp3"); the LLM title comes later (04-13).
    /// </summary>
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

    // ----- Helpers -----

    /// <summary>
    /// People who knew every one of <paramref name="facts"/> (ActorIds ∪ KnownByIds, cast only), in first-fact order.
    /// </summary>
    private static List<string> KnowerPool(SkeletonContext ctx, List<KeyFact> facts)
    {
        List<string>? pool = null;
        foreach (var fact in facts)
        {
            var knowers = Clean(fact.ActorIds).Concat(Clean(fact.KnownByIds))
                .Where(ctx.CastById.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            pool = pool == null ? knowers : pool.Where(knowers.Contains).ToList();
        }

        return pool ?? new List<string>();
    }

    /// <summary>
    /// Gives every dated fact at least two knowers so its emails go between people, not to the sender alone. A fact with
    /// one knower gains the colleague who shares the most other facts with them (then same organization, Key tier, id);
    /// a fact with none gains the first active Key character plus that person's colleague. Counsel is only added for a
    /// counsel knower. Added people go into KnownByIds (with the actors, so KnowledgeGate still counts them) and each
    /// addition is noted. Deterministic: no random draws.
    /// </summary>
    public static void FillLoneKnowers(SkeletonContext ctx, List<string> notes)
    {
        var facts = ctx.DatedFacts;
        var cast = ctx.CastById.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

        foreach (var fact in facts)
        {
            var date = SkeletonContext.FactDate(fact);
            if (date == null)
                continue;

            var knowers = KnowerPool(ctx, new List<KeyFact> { fact });
            if (knowers.Count >= 2)
                continue;

            var active = cast.Where(c => ctx.IsActive(c.Id, date.Value)).ToList();
            var added = new List<string>();
            var anchor = knowers.FirstOrDefault();
            if (anchor == null)
            {
                anchor = active.OrderBy(c => c.Tier).ThenBy(c => c.Id, StringComparer.Ordinal).Select(c => c.Id).FirstOrDefault();
                if (anchor == null)
                    continue;
                added.Add(anchor);
            }

            var anchorIsCounsel = IsCounsel(ctx, anchor);
            var anchorOrg = ctx.CastById[anchor].OrganizationId ?? string.Empty;
            var colleague = active
                .Where(c => c.Id != anchor && (anchorIsCounsel || !IsCounsel(ctx, c.Id)))
                .OrderByDescending(c => facts.Count(f => !ReferenceEquals(f, fact) && KnowerPool(ctx, new List<KeyFact> { f }) is var p && p.Contains(anchor) && p.Contains(c.Id)))
                .ThenByDescending(c => (c.OrganizationId ?? string.Empty) == anchorOrg)
                .ThenBy(c => c.Tier)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .Select(c => c.Id)
                .FirstOrDefault();
            if (colleague != null)
                added.Add(colleague);
            if (added.Count == 0)
                continue;

            AddKnowers(ctx, fact, added);
            var names = string.Join(" and ", added.Select(id => ctx.CastById[id].FullName));
            notes.Add($"{fact.Id} listed fewer than two people under Who knew, so {names} {(added.Count == 1 ? "was" : "were")} added so its emails go between people.");
        }
    }

    /// <summary>
    /// Adds <paramref name="ids"/> under Who knew: KnownByIds becomes actors, then existing knowers, then the additions
    /// (distinct). <see cref="CorpusApproval"/> applies a saved plan's additions the same way, so the guide hash matches.
    /// </summary>
    public static void AddKnowers(KeyFact fact, IEnumerable<string> ids) =>
        fact.KnownByIds = Clean(fact.ActorIds).Concat(Clean(fact.KnownByIds)).Concat(Clean(ids.ToList()))
            .Distinct(StringComparer.Ordinal).ToList();

    private static void AddKnowers(SkeletonContext ctx, KeyFact fact, List<string> ids)
    {
        AddKnowers(fact, ids);
        if (!ctx.AddedKnowers.TryGetValue(fact.Id, out var list))
            ctx.AddedKnowers[fact.Id] = list = new List<string>();
        list.AddRange(ids.Where(id => !list.Contains(id)));
    }

    /// <summary>
    /// For a thread with one active knower: the active person who knew the most of its facts (then shares the most
    /// facts with the sender, same organization, Key tier, id), never counsel unless the sender is counsel. Null if none.
    /// </summary>
    private static string? ColleagueFor(SkeletonContext ctx, string sender, IReadOnlyCollection<KeyFact> threadFacts, DateOnly day)
    {
        var senderIsCounsel = IsCounsel(ctx, sender);
        var senderOrg = ctx.CastById.TryGetValue(sender, out var member) ? member.OrganizationId ?? string.Empty : string.Empty;
        return ctx.CastById.Values
            .Where(c => c.Id != sender && ctx.IsActive(c.Id, day) && (senderIsCounsel || !IsCounsel(ctx, c.Id)))
            .OrderByDescending(c => threadFacts.Count(f => KnowerPool(ctx, new List<KeyFact> { f }).Contains(c.Id)))
            .ThenByDescending(c => ctx.DatedFacts.Count(f => KnowerPool(ctx, new List<KeyFact> { f }) is var p && p.Contains(sender) && p.Contains(c.Id)))
            .ThenByDescending(c => (c.OrganizationId ?? string.Empty) == senderOrg)
            .ThenBy(c => c.Tier)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => c.Id)
            .FirstOrDefault();
    }

    private static List<string> ActorsIn(List<KeyFact> facts, List<string> people)
    {
        var actors = facts.SelectMany(f => Clean(f.ActorIds)).ToHashSet(StringComparer.Ordinal);
        return people.Where(actors.Contains).ToList();
    }

    private static DateOnly LatestFactDate(ThreadSpec spec) =>
        spec.Facts.Select(f => SkeletonContext.FactDate(f) ?? DateOnly.MinValue).Max();

    private static IEnumerable<string> Participants(PlannedMessage m) => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds);

    private static string ThreadId(int n) => $"T-{n:0000}";

    private static List<string> Clean(List<string>? ids) =>
        (ids ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static DateOnly AddBusinessDays(DateOnly start, int days)
    {
        var day = BusinessCalendar.NextBusinessDay(start);
        for (var i = 0; i < days; i++)
            day = BusinessCalendar.NextBusinessDay(day.AddDays(1));
        return day;
    }

    private static int BusinessDaysBetween(DateOnly a, DateOnly b)
    {
        if (b < a)
            (a, b) = (b, a);
        if (b.DayNumber - a.DayNumber > MaxPairCalendarDays)
            return int.MaxValue;

        var count = 0;
        for (var d = a.AddDays(1); d <= b; d = d.AddDays(1))
        {
            if (BusinessCalendar.IsBusinessDay(d))
                count++;
        }

        return count;
    }

    private static void Shuffle<T>(List<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
