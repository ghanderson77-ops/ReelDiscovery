using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Depth table used for a thread: Business = relevant, decoy and daily business (one shared table so shape never
/// reveals relevance, Pitfall 3); Ancillary = background staff traffic; Bulk = newsletters, spam, personal (singletons).
/// </summary>
public enum ThreadShapeClass
{
    Business,
    Ancillary,
    Bulk
}

/// <summary>
/// Realistic thread-shape parameters and samplers (PLAN-05, D-17). Every sampler takes an injected Random
/// (Random.Shared in production, seeded in tests); no seed is stored (D-20).
/// </summary>
public static class ThreadShapes
{
    public const int MaxThreadDepth = 12;
    public const int DefaultAttachmentPercent = 20;

    // Klimt & Yang 2004 Enron thread sizes, capped at 12 (RESEARCH Pattern 6); mean ~2.9 messages
    public static readonly (int Min, int Max, int Percent)[] BusinessDepth =
    {
        (1, 1, 30), (2, 2, 28), (3, 3, 15), (4, 4, 10), (5, 6, 9), (7, 9, 5), (10, 12, 3)
    };

    // Background-staff traffic is shorter (RESEARCH Pattern 6)
    public static readonly (int Min, int Max, int Percent)[] AncillaryDepth =
    {
        (1, 1, 45), (2, 2, 30), (3, 3, 12), (4, 4, 7), (5, 6, 4), (7, 9, 2)
    };

    // Non-root message kinds (RESEARCH Pattern 6, assumption A1; FEATURES.md suggests 60/25/15)
    public static readonly (MessageKind Kind, int Percent)[] KindPercents =
    {
        (MessageKind.Reply, 55), (MessageKind.ReplyAll, 30), (MessageKind.Forward, 15)
    };

    // Root To count 1/2/3-4 and CC count 0/1/2-3; ~53% multi-recipient like Avocado (Yang et al. 2017)
    public static readonly (int Min, int Max, int Percent)[] RootToCounts = { (1, 1, 65), (2, 2, 25), (3, 4, 10) };
    public static readonly (int Min, int Max, int Percent)[] RootCcCounts = { (0, 0, 50), (1, 1, 30), (2, 3, 20) };

    // Reply to the previous message 80% of the time, otherwise branch from an earlier one (assumption A1)
    public const int ReplyToPreviousPercent = 80;

    // Reply copies the parent CC 25% of the time (RESEARCH Pattern 6)
    public const int ReplyKeepsCcPercent = 25;

    // Latency mixture in business minutes fitted to Avocado tertiles <25 / 25-245 / >245 (Yang et al., SIGIR 2017)
    public static readonly (int Min, int Max, int Percent, bool LogUniform)[] LatencyBuckets =
    {
        (2, 25, 33, false), (25, 240, 33, true), (240, 480, 26, false), (480, 960, 8, false)
    };

    // External replies are slower (66 vs 135 min median in Avocado)
    public const double ExternalLatencyFactor = 1.5;

    // Mild bimodal intra-day curve peaking ~10:00 and ~14:00 (assumption, RESEARCH Pattern 5); 16:xx stops at 16:44
    public static readonly (int Hour, int Weight)[] RootHourWeights =
    {
        (9, 10), (10, 18), (11, 14), (12, 8), (13, 12), (14, 16), (15, 13), (16, 9)
    };

    // Attachment type mix renormalized over enabled types (RESEARCH Pattern 6, assumption A3)
    public static readonly (AttachmentType Type, int Weight)[] AttachmentTypeWeights =
    {
        (AttachmentType.Word, 40), (AttachmentType.Excel, 25), (AttachmentType.PowerPoint, 10),
        (AttachmentType.CalendarInvite, 12), (AttachmentType.Image, 8), (AttachmentType.Voicemail, 5)
    };

    // Attachments per attached message 1/2/3
    public static readonly (int Min, int Max, int Percent)[] AttachmentCounts = { (1, 1, 80), (2, 2, 15), (3, 3, 5) };

    public static int SampleDepth(ThreadShapeClass cls, Random random) => cls switch
    {
        ThreadShapeClass.Bulk => 1,
        ThreadShapeClass.Ancillary => Math.Min(MaxThreadDepth, SampleRange(AncillaryDepth, random)),
        _ => Math.Min(MaxThreadDepth, SampleRange(BusinessDepth, random))
    };

    /// <summary>
    /// Kind of a non-root message. <paramref name="parentRecipients"/> = parent To + CC count; Reply-All needs more than one
    /// (otherwise it is returned as Reply).
    /// </summary>
    public static MessageKind SampleKind(int parentRecipients, Random random)
    {
        var roll = random.Next(100);
        var kind = MessageKind.Reply;
        foreach (var (k, percent) in KindPercents)
        {
            if (roll < percent)
            {
                kind = k;
                break;
            }

            roll -= percent;
        }

        return kind == MessageKind.ReplyAll && parentRecipients <= 1 ? MessageKind.Reply : kind;
    }

    public static int SampleRootToCount(Random random) => SampleRange(RootToCounts, random);

    public static int SampleRootCcCount(Random random) => SampleRange(RootCcCounts, random);

    /// <summary>
    /// Parent sequence for message <paramref name="seq"/>: 0 for the root, otherwise the previous message 80% of the time
    /// and an earlier one (branch) otherwise.
    /// </summary>
    public static int SampleReplyToSeq(int seq, Random random)
    {
        if (seq <= 1)
            return 0;
        if (seq == 2)
            return 1;

        return random.Next(100) < ReplyToPreviousPercent ? seq - 1 : random.Next(1, seq - 1);
    }

    /// <summary>
    /// Reply latency in business minutes; external replies are 1.5x slower. Consumes the same random draws either way.
    /// </summary>
    public static int SampleLatencyMinutes(bool external, Random random)
    {
        var roll = random.Next(100);
        var bucket = LatencyBuckets[^1];
        foreach (var b in LatencyBuckets)
        {
            if (roll < b.Percent)
            {
                bucket = b;
                break;
            }

            roll -= b.Percent;
        }

        var u = random.NextDouble();
        var minutes = bucket.LogUniform
            ? bucket.Min * Math.Pow((double)bucket.Max / bucket.Min, u)
            : bucket.Min + u * (bucket.Max - bucket.Min);

        // Upper bound exclusive except for the last bucket
        var value = (int)Math.Floor(minutes);
        value = Math.Clamp(value, bucket.Min, bucket == LatencyBuckets[^1] ? bucket.Max : bucket.Max - 1);

        return external ? (int)Math.Round(value * ExternalLatencyFactor, MidpointRounding.AwayFromZero) : value;
    }

    /// <summary>
    /// Local send time of a thread root: 09:00 &lt;= t &lt; 16:45, peaking mid-morning and mid-afternoon.
    /// </summary>
    public static TimeOnly SampleRootTime(Random random)
    {
        var total = RootHourWeights.Sum(h => h.Weight);
        var roll = random.Next(total);
        var hour = RootHourWeights[^1].Hour;
        foreach (var (h, weight) in RootHourWeights)
        {
            if (roll < weight)
            {
                hour = h;
                break;
            }

            roll -= weight;
        }

        var minute = random.Next(hour == 16 ? 45 : 60);
        return new TimeOnly(hour, minute);
    }

    /// <summary>
    /// One attachment type from the enabled set (weights renormalized); calendar invites only on roots; null when nothing fits.
    /// </summary>
    public static AttachmentType? SampleAttachmentType(IReadOnlyList<AttachmentType> enabled, bool isRoot, Random random)
    {
        var candidates = AttachmentTypeWeights
            .Where(w => enabled.Contains(w.Type) && (isRoot || w.Type != AttachmentType.CalendarInvite))
            .ToList();
        if (candidates.Count == 0)
            return null;

        var roll = random.Next(candidates.Sum(c => c.Weight));
        foreach (var (type, weight) in candidates)
        {
            if (roll < weight)
                return type;
            roll -= weight;
        }

        return candidates[^1].Type;
    }

    public static int SampleAttachmentCount(Random random) => SampleRange(AttachmentCounts, random);

    /// <summary>
    /// True with the given percent probability.
    /// </summary>
    public static bool Chance(int percent, Random random) => random.Next(100) < percent;

    /// <summary>
    /// The kind actually used: Reply-All with no one to copy besides the parent sender becomes Reply; Forward without a
    /// usable target becomes Reply; New on a non-root message is a Reply.
    /// </summary>
    public static MessageKind ResolveKind(MessageKind kind, PlannedMessage parent, string senderId, string? forwardTarget)
    {
        switch (kind)
        {
            case MessageKind.ReplyAll:
                var others = parent.ToIds.Concat(parent.CcIds)
                    .Where(id => !string.IsNullOrEmpty(id) && id != senderId && id != parent.FromId);
                return others.Any() ? MessageKind.ReplyAll : MessageKind.Reply;
            case MessageKind.Forward:
                return string.IsNullOrWhiteSpace(forwardTarget) || forwardTarget == senderId ? MessageKind.Reply : MessageKind.Forward;
            default:
                return MessageKind.Reply;
        }
    }

    /// <summary>
    /// Recipients of a non-root message (RESEARCH Pattern 6): self removed, distinct, order preserved.
    /// Reply: To = parent sender, CC = parent CC 25% of the time. Reply-All: To = parent sender + parent To, CC = parent CC.
    /// Forward: To = the forward target. A sender replying to their own message addresses the parent's recipients.
    /// </summary>
    public static (List<string> To, List<string> Cc) RecipientsFor(MessageKind kind, PlannedMessage parent, string senderId,
        string? forwardTarget, Random random)
    {
        var effective = ResolveKind(kind, parent, senderId, forwardTarget);
        List<string> to;
        List<string> cc;

        switch (effective)
        {
            case MessageKind.Forward:
                to = new List<string> { forwardTarget!.Trim() };
                cc = new List<string>();
                break;
            case MessageKind.ReplyAll:
                to = Clean(new[] { parent.FromId }.Concat(parent.ToIds), senderId, null);
                cc = Clean(parent.CcIds, senderId, to);
                break;
            default:
                to = parent.FromId == senderId
                    ? Clean(parent.ToIds, senderId, null)
                    : Clean(new[] { parent.FromId }, senderId, null);
                if (to.Count == 0)
                    to.Add(senderId); // note to self
                cc = Chance(ReplyKeepsCcPercent, random) ? Clean(parent.CcIds, senderId, to) : new List<string>();
                break;
        }

        return (to, cc);
    }

    private static List<string> Clean(IEnumerable<string> ids, string senderId, List<string>? exclude)
    {
        var result = new List<string>();
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || id == senderId || result.Contains(id) || (exclude?.Contains(id) ?? false))
                continue;
            result.Add(id);
        }

        return result;
    }

    private static int SampleRange((int Min, int Max, int Percent)[] table, Random random)
    {
        var total = table.Sum(t => t.Percent);
        var roll = random.Next(total);
        foreach (var (min, max, percent) in table)
        {
            if (roll < percent)
                return min == max ? min : random.Next(min, max + 1);
            roll -= percent;
        }

        return table[^1].Max;
    }
}
