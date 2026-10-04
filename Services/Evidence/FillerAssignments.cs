using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Code-chosen length bands and privilege markings for non-relevant mail. Separate from
/// <see cref="RenderAssignments.Compute"/> on purpose: relevant markings already decided written subjects
/// (anti-pattern); same shares so distributions match (NOISE-05, D-07, Pitfalls 2-3).
/// </summary>
public static class FillerAssignments
{
    // Own salts so filler picks never line up with the relevant ones
    private const string LengthSalt = ":filler-length";
    private const string PrivilegeSalt = ":filler-privilege";
    private const string SubjectSalt = ":filler-subject";

    /// <summary>
    /// Assignments for every non-relevant thread with messages, keyed by thread id (ordinal). Relevant threads get no
    /// entry. Business mail (daily business, ancillary, decoys) gets the relevant length mix; newsletters are Long and
    /// spam/personal mail Short. Subtlety is always None.
    /// </summary>
    public static Dictionary<string, ThreadAssignment> Compute(CorpusPlan plan)
    {
        var threads = (plan?.Threads ?? new List<PlannedThread>())
            .Where(t => t != null
                && t.Relevance != ThreadRelevance.Relevant
                && !string.IsNullOrEmpty(t.Id)
                && t.Messages?.Any(m => m != null) == true)
            // corpus-plan.json is read from disk: a repeated id is kept once, first copy (as the runner does)
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        var kinds = new Dictionary<string, ExternalKind>(StringComparer.Ordinal);
        foreach (var external in plan?.Externals ?? new List<ExternalParty>())
        {
            if (external != null && !string.IsNullOrEmpty(external.Id))
                kinds.TryAdd(external.Id, external.Kind);
        }

        var result = new Dictionary<string, ThreadAssignment>(StringComparer.Ordinal);
        var byMessage = new Dictionary<PlannedMessage, MessageAssignment>(ReferenceEqualityComparer.Instance);
        var business = new List<PlannedMessage>();
        foreach (var thread in threads)
        {
            var assignment = new ThreadAssignment { ThreadId = thread.Id, Marking = PrivilegeMarking.None };
            var bulk = thread.Category == NonRelevantCategory.NewsletterSpamPersonal;
            foreach (var message in thread.Messages.Where(m => m != null).OrderBy(m => m.Seq))
            {
                var item = new MessageAssignment
                {
                    MessageId = message.Id,
                    Seq = message.Seq,
                    Subtlety = Subtlety.None,
                    Length = LengthBand.Medium
                };
                if (bulk)
                {
                    // Bulk mail is shaped by its sender, outside the parity population
                    var newsletter = kinds.TryGetValue(message.FromId ?? string.Empty, out var kind) && kind == ExternalKind.Newsletter;
                    item.Length = newsletter ? LengthBand.Long : LengthBand.Short;
                }
                else
                {
                    business.Add(message);
                }

                assignment.Messages.Add(item);
                byMessage[message] = item;
            }

            result[thread.Id] = assignment;
        }

        AssignLength(business, byMessage);
        AssignPrivilege(threads, result);
        return result;
    }

    private static List<PlannedMessage> Ranked(IEnumerable<PlannedMessage> messages, string salt) =>
        messages
            .OrderBy(m => RenderAssignments.StableRank(m.Id + salt))
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

    // Same rule as RenderAssignments: Long only for New/Forward (shortfall becomes Medium), Short to replies first
    private static void AssignLength(List<PlannedMessage> messages, Dictionary<PlannedMessage, MessageAssignment> byMessage)
    {
        var shares = Apportion.Largest(messages.Count, RenderAssignments.LengthBands.Select(b => b.Share).ToList());
        var shortCount = shares[0];
        var longCount = shares[2];

        var longPicks = Ranked(messages.Where(m => !IsReply(m)), LengthSalt).Take(longCount).ToList();
        var isLong = new HashSet<PlannedMessage>(longPicks, ReferenceEqualityComparer.Instance);

        var rest = messages.Where(m => !isLong.Contains(m)).ToList();
        var shortPicks = Ranked(rest.Where(IsReply), LengthSalt)
            .Concat(Ranked(rest.Where(m => !IsReply(m)), LengthSalt))
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

    // Same labeled/subject mix as relevant privileged threads so a banner is not a relevance signal (Pitfall 2)
    private static void AssignPrivilege(List<PlannedThread> threads, Dictionary<string, ThreadAssignment> result)
    {
        var privileged = threads
            .Where(t => t.IsPrivileged)
            .OrderBy(t => RenderAssignments.StableRank(t.Id + PrivilegeSalt))
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        var labeledCount = Apportion.Largest(privileged.Count,
            new[] { RenderAssignments.LabeledShare, 1 - RenderAssignments.LabeledShare })[0];
        var labeled = privileged.Take(labeledCount).ToList();
        foreach (var thread in privileged.Skip(labeledCount))
            result[thread.Id].Marking = PrivilegeMarking.Unlabeled;

        var subjectCount = Apportion.Largest(labeled.Count,
            new[] { 1 - RenderAssignments.SubjectTagShare, RenderAssignments.SubjectTagShare })[1];
        var bySubjectRank = labeled
            .OrderBy(t => RenderAssignments.StableRank(t.Id + SubjectSalt))
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < bySubjectRank.Count; i++)
            result[bySubjectRank[i].Id].Marking = i < subjectCount ? PrivilegeMarking.BannerAndSubject : PrivilegeMarking.Banner;
    }
}
