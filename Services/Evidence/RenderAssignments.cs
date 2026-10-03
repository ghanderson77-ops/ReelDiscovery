using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Code-chosen rendering knobs for every relevant message: subtlety (D-17), length band (D-21) and privilege marking
/// (D-18). Exact mixes via largest-remainder apportionment; picks rank by SHA-256 so retries, Continue and other
/// processes get identical results.
/// </summary>
public static class RenderAssignments
{
    // Shared with Phase 6 filler so relevant and non-relevant lengths match (NOISE-05, D-21)
    public static readonly IReadOnlyList<LengthBandSpec> LengthBands = new List<LengthBandSpec>
    {
        new() { Band = LengthBand.Short, MinWords = 8, MaxWords = 60, Share = 0.55 },
        new() { Band = LengthBand.Medium, MinWords = 60, MaxWords = 160, Share = 0.35 },
        new() { Band = LengthBand.Long, MinWords = 160, MaxWords = 320, Share = 0.10 }
    };

    // Indirect, Clear, Explicit (D-17)
    public static readonly double[] SubtletyShares = { 0.60, 0.30, 0.10 };

    // Share of privileged threads that carry a privilege label, and of those the share that also tag the subject (D-18, UI-SPEC S5)
    public const double LabeledShare = 0.85;
    public const double SubjectTagShare = 0.40;

    public static LengthBandSpec Spec(LengthBand band) => LengthBands.First(b => b.Band == band);

    /// <summary>
    /// Stable 64-bit rank of an id: the first 8 bytes (big-endian) of its UTF-8 SHA-256.
    /// </summary>
    public static ulong StableRank(string id)
    {
        // Never string.GetHashCode(): randomized per process, would change on every Continue
        return BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(id ?? string.Empty)));
    }

    /// <summary>
    /// Assignments for every relevant thread, keyed by thread id (ordinal). Non-relevant threads get no entry.
    /// </summary>
    public static Dictionary<string, ThreadAssignment> Compute(CorpusPlan plan)
    {
        var threads = (plan?.Threads ?? new List<PlannedThread>())
            .Where(WavePlanner.IsGeneratable)
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<string, ThreadAssignment>(StringComparer.Ordinal);
        var byMessage = new Dictionary<PlannedMessage, MessageAssignment>(ReferenceEqualityComparer.Instance);
        foreach (var thread in threads)
        {
            var assignment = new ThreadAssignment { ThreadId = thread.Id, Marking = PrivilegeMarking.None };
            foreach (var message in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).OrderBy(m => m.Seq))
            {
                var item = new MessageAssignment { MessageId = message.Id, Seq = message.Seq, Subtlety = Subtlety.None, Length = LengthBand.Medium };
                assignment.Messages.Add(item);
                byMessage[message] = item;
            }

            result[thread.Id] = assignment;
        }

        var messages = threads.SelectMany(t => (t.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .Select(m => (Thread: t, Message: m))).ToList();

        AssignSubtlety(messages, byMessage);
        AssignLength(messages.Select(x => x.Message).ToList(), byMessage);
        AssignPrivilege(threads, result);
        return result;
    }

    private static List<PlannedMessage> Ranked(IEnumerable<PlannedMessage> messages, string salt) =>
        messages
            .OrderBy(m => StableRank(m.Id + salt))
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

    private static void AssignSubtlety(List<(PlannedThread Thread, PlannedMessage Message)> messages,
        Dictionary<PlannedMessage, MessageAssignment> byMessage)
    {
        // Privileged threads: own-basis advice is Counsel; everything else (quoted/none) carries no subtlety
        foreach (var (thread, message) in messages)
        {
            byMessage[message].Subtlety = thread.IsPrivileged && message.Basis == RelevanceBasis.Own
                ? Subtlety.Counsel
                : Subtlety.None;
        }

        // 60/30/10 over own-basis messages of non-privileged threads; hot messages fill Explicit first
        var pool = messages
            .Where(x => !x.Thread.IsPrivileged && x.Message.Basis == RelevanceBasis.Own)
            .Select(x => x.Message)
            .ToList();
        var shares = Apportion.Largest(pool.Count, SubtletyShares);
        var hot = pool.Count(m => m.IsHot);
        var explicitLeft = Math.Max(0, shares[2] - hot);
        var clear = shares[1];
        var indirect = pool.Count - hot - explicitLeft - clear;
        if (indirect < 0)
        {
            // Too many hot messages: shrink Indirect first, then Clear
            clear = Math.Max(0, clear + indirect);
            indirect = 0;
        }

        var ranked = Ranked(pool.Where(m => !m.IsHot), ":subtlety");
        for (var i = 0; i < ranked.Count; i++)
        {
            byMessage[ranked[i]].Subtlety = i < indirect ? Subtlety.Indirect
                : i < indirect + clear ? Subtlety.Clear
                : Subtlety.Explicit;
        }

        // D-17 (locked): hot documents are always explicit, including privileged counsel threads
        foreach (var (_, message) in messages)
        {
            if (message.IsHot)
                byMessage[message].Subtlety = Subtlety.Explicit;
        }
    }

    private static void AssignLength(List<PlannedMessage> messages, Dictionary<PlannedMessage, MessageAssignment> byMessage)
    {
        var shares = Apportion.Largest(messages.Count, LengthBands.Select(b => b.Share).ToList());
        var shortCount = shares[0];
        var longCount = shares[2];

        // Long only for New/Forward; a shortfall becomes Medium
        var longPicks = Ranked(messages.Where(m => !IsReply(m)), ":length").Take(longCount).ToList();
        var isLong = new HashSet<PlannedMessage>(longPicks, ReferenceEqualityComparer.Instance);

        // Short goes to replies first, then other messages
        var rest = messages.Where(m => !isLong.Contains(m)).ToList();
        var shortPicks = Ranked(rest.Where(IsReply), ":length")
            .Concat(Ranked(rest.Where(m => !IsReply(m)), ":length"))
            .Take(shortCount)
            .ToList();
        var isShort = new HashSet<PlannedMessage>(shortPicks, ReferenceEqualityComparer.Instance);

        foreach (var message in messages)
        {
            byMessage[message].Length = isLong.Contains(message) ? LengthBand.Long
                : isShort.Contains(message) ? LengthBand.Short
                : LengthBand.Medium;
        }
    }

    private static bool IsReply(PlannedMessage message) =>
        message.Kind == MessageKind.Reply || message.Kind == MessageKind.ReplyAll;

    private static void AssignPrivilege(List<PlannedThread> threads, Dictionary<string, ThreadAssignment> result)
    {
        var privileged = threads
            .Where(t => t.IsPrivileged)
            .OrderBy(t => StableRank(t.Id + ":privilege"))
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        var labeledCount = Apportion.Largest(privileged.Count, new[] { LabeledShare, 1 - LabeledShare })[0];
        var labeled = privileged.Take(labeledCount).ToList();
        foreach (var thread in privileged.Skip(labeledCount))
            result[thread.Id].Marking = PrivilegeMarking.Unlabeled;

        var subjectCount = Apportion.Largest(labeled.Count, new[] { 1 - SubjectTagShare, SubjectTagShare })[1];
        var bySubjectRank = labeled
            .OrderBy(t => StableRank(t.Id + ":subject"))
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < bySubjectRank.Count; i++)
            result[bySubjectRank[i].Id].Marking = i < subjectCount ? PrivilegeMarking.BannerAndSubject : PrivilegeMarking.Banner;
    }
}
