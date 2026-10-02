using System.Globalization;
using System.Text;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Fixes the identity of every planned document (D-28..D-30): chronological thread ids, message ids, DocIDs, custodians
/// and opaque planned paths. Ids are formatted locally, never via the guide id counters (Pitfall 2), so the guide is never touched.
/// </summary>
public static class PlanNumbering
{
    public const string MailboxesFolder = "Mailboxes";
    public const string SentFolder = "Sent Items";
    public const string InboxFolder = "Inbox";

    // Used only when a message has no cast participant at all
    public const string UnassignedFolder = "Unassigned";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Assigns thread ids, message ids, DocIDs, attachment DocIDs, custodians and planned paths; idempotent.
    /// </summary>
    public static void Assign(CorpusPlan plan, CaseGuide guide)
    {
        var prefix = CorpusSettings.SanitizePrefix(plan.DocIdPrefix);
        if (prefix.Length == 0)
            prefix = DocIdPrefix.Fallback;

        // Thread ids follow each thread's first message time (UTC); ties keep the current id order
        var ordered = plan.Threads
            .Select((thread, index) => (Thread: thread, Index: index))
            .OrderBy(x => FirstSent(x.Thread))
            .ThenBy(x => x.Thread.Id ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .Select(x => x.Thread)
            .ToList();

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var newId = string.Format(CultureInfo.InvariantCulture, "T-{0:0000}", i + 1);
            idMap.TryAdd(ordered[i].Id ?? string.Empty, newId);
            ordered[i].Id = newId;
        }

        plan.Threads = ordered;

        foreach (var thread in plan.Threads)
        {
            thread.LinkedThreadIds = (thread.LinkedThreadIds ?? new List<string>())
                .Select(old => old != null && idMap.TryGetValue(old, out var mapped) ? mapped : null)
                .Where(id => id != null && id != thread.Id)
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            thread.Messages = thread.Messages.OrderBy(m => m.Seq).ToList();
            foreach (var message in thread.Messages)
                message.Id = string.Format(CultureInfo.InvariantCulture, "{0}-M{1:00}", thread.Id, message.Seq);
        }

        // DocIDs strictly by time, never relevant-first (Pitfall 3)
        var chronological = plan.Threads
            .SelectMany(t => t.Messages.Select(m => (Thread: t, Message: m)))
            .OrderBy(x => x.Message.SentAt.UtcDateTime)
            .ThenBy(x => x.Thread.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Message.Seq)
            .ToList();

        var castById = new Dictionary<string, CastMember>(StringComparer.Ordinal);
        foreach (var member in guide.Cast)
        {
            if (member != null && !string.IsNullOrEmpty(member.Id))
                castById.TryAdd(member.Id, member);
        }

        var folders = BuildFolders(guide);

        for (var i = 0; i < chronological.Count; i++)
        {
            var message = chronological[i].Message;
            message.DocId = string.Format(CultureInfo.InvariantCulture, "{0}-{1:000000}", prefix, i + 1);

            for (var k = 0; k < message.Attachments.Count; k++)
                message.Attachments[k].DocId = string.Format(CultureInfo.InvariantCulture, "{0}.{1:0000}", message.DocId, k + 1);

            message.CustodianIds = new[] { message.FromId }
                .Concat(message.ToIds ?? new List<string>())
                .Concat(message.CcIds ?? new List<string>())
                .Where(id => id != null && castById.ContainsKey(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var owner = OwnerOf(message, castById);
            var box = owner == null ? InboxFolder : owner == message.FromId ? SentFolder : InboxFolder;
            var folder = owner != null && folders.TryGetValue(owner, out var name) ? name : UnassignedFolder;
            message.FilePath = $"{MailboxesFolder}/{folder}/{box}/{message.DocId}.eml";
        }

        foreach (var thread in plan.Threads)
        {
            var root = thread.Messages.FirstOrDefault();
            thread.OwnerCustodianId = root == null ? string.Empty : OwnerOf(root, castById) ?? string.Empty;
        }
    }

    /// <summary>
    /// Mailbox folder for a cast member: the email local part filtered to [a-z0-9._-] (no "..", no leading/trailing dots),
    /// else the cast id; "-{id}" is appended when the name is reserved or already taken by another cast member.
    /// </summary>
    public static string CustodianFolder(CastMember member, IReadOnlyDictionary<string, string> taken)
    {
        var id = SafeId(member.Id);
        var email = member.Email ?? string.Empty;
        var at = email.IndexOf('@');
        var local = (at >= 0 ? email[..at] : email).ToLowerInvariant();

        var builder = new StringBuilder(local.Length);
        foreach (var c in local)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-')
                builder.Append(c);
        }

        var folder = builder.ToString();
        while (folder.Contains("..", StringComparison.Ordinal))
            folder = folder.Replace("..", ".", StringComparison.Ordinal);
        folder = folder.Trim('.');

        if (folder.Length == 0)
            return id;

        var dot = folder.IndexOf('.');
        var baseName = dot >= 0 ? folder[..dot] : folder;
        if (ReservedNames.Contains(baseName))
            return $"{folder}-{id}";

        if (taken.TryGetValue(folder, out var owner) && !string.Equals(owner, member.Id, StringComparison.Ordinal))
            return $"{folder}-{id}";

        return folder;
    }

    // Sender when the sender is cast, else the first cast recipient (To, then CC); null when no cast participant
    private static string? OwnerOf(PlannedMessage message, Dictionary<string, CastMember> castById)
    {
        if (message.FromId != null && castById.ContainsKey(message.FromId))
            return message.FromId;

        return (message.ToIds ?? new List<string>())
            .Concat(message.CcIds ?? new List<string>())
            .FirstOrDefault(id => id != null && castById.ContainsKey(id));
    }

    // Cast order decides who keeps a shared local part (first wins)
    private static Dictionary<string, string> BuildFolders(CaseGuide guide)
    {
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in guide.Cast)
        {
            if (member == null || string.IsNullOrEmpty(member.Id) || byId.ContainsKey(member.Id))
                continue;

            var folder = CustodianFolder(member, taken);
            if (taken.ContainsKey(folder))
                folder = $"{folder}-{SafeId(member.Id)}";

            taken[folder] = member.Id;
            byId[member.Id] = folder;
        }

        return byId;
    }

    // Cast ids are code-issued (CH-001); filter anyway so a tampered case.json cannot inject path characters
    private static string SafeId(string? id)
    {
        var builder = new StringBuilder();
        foreach (var c in id ?? string.Empty)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
                builder.Append(c);
        }

        return builder.Length == 0 ? "custodian" : builder.ToString();
    }

    private static DateTime FirstSent(PlannedThread thread) =>
        thread.Messages.Count == 0 ? DateTime.MaxValue : thread.Messages.Min(m => m.SentAt.UtcDateTime);
}
