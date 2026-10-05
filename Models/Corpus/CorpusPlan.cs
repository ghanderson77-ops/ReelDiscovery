using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Corpus;

/// <summary>
/// Code-owned corpus plan (PLAN-02); stored in corpus-plan.json beside case.json and immutable once built.
/// </summary>
public class CorpusPlan
{
    public const int CurrentFormatVersion = 1;

    // Root of every planned .eml path; equals PlanNumbering.MailboxesFolder
    public const string MailboxesRoot = "Mailboxes";

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid PlanId { get; set; }
    public DateTime CreatedUtc { get; set; }

    // Basis stamps for staleness (D-05)
    public string GuideHash { get; set; } = string.Empty;
    public string SettingsHash { get; set; } = string.Empty;

    public string DocIdPrefix { get; set; } = string.Empty;

    // Snapshot of the settings the plan was built from
    public CorpusSettings Settings { get; set; } = new();

    public List<PlannedThread> Threads { get; set; } = new();
    public List<ExternalParty> Externals { get; set; } = new();
    public ReviewProtocolProse Protocol { get; set; } = new();

    // Repair/mapping notes surfaced in Checks
    public List<string> Notes { get; set; } = new();

    // Re-plan guidance text the plan was built with
    public string Guidance { get; set; } = string.Empty;

    /// <summary>
    /// People the planner added under Who knew (fact id to cast ids) so no fact's emails go to the sender alone. Applied to
    /// the guide when the plan is saved; GuideHash already includes them.
    /// </summary>
    public Dictionary<string, List<string>> AddedKnowers { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public int EmailCount => Threads.Sum(t => t.Messages.Count);

    [JsonIgnore]
    public int AttachmentCount => Threads.Sum(t => t.Messages.Sum(m => m.Attachments.Count));

    /// <summary>
    /// Every message in thread order, then by Seq within each thread.
    /// </summary>
    public IEnumerable<PlannedMessage> AllMessages()
    {
        foreach (var thread in Threads)
        {
            foreach (var message in thread.Messages.OrderBy(m => m.Seq))
                yield return message;
        }
    }

    /// <summary>
    /// Repairs a plan loaded from untrusted corpus-plan.json: null lists, strings and sub-objects become empty values at
    /// every level (T-04-02), and an unsafe email path or attachment file name becomes empty (WR-10); never throws.
    /// </summary>
    public void Normalize()
    {
        GuideHash ??= string.Empty;
        SettingsHash ??= string.Empty;
        DocIdPrefix ??= string.Empty;
        Guidance ??= string.Empty;
        Settings ??= new();
        Settings.Normalize();
        Notes = CaseGuide.CleanStrings(Notes);

        Threads = CaseGuide.CleanList(Threads);
        foreach (var thread in Threads)
            NormalizeThread(thread);

        Externals = CaseGuide.CleanList(Externals);
        foreach (var external in Externals)
        {
            external.Id ??= string.Empty;
            external.Name ??= string.Empty;
            external.Email ??= string.Empty;
            external.Domain ??= string.Empty;
        }

        Protocol ??= new();
        Protocol.Purpose ??= string.Empty;
        Protocol.Background ??= string.Empty;
        Protocol.HotGuidance ??= string.Empty;
        Protocol.PrivilegeGuidance ??= string.Empty;
        Protocol.Issues = CaseGuide.CleanList(Protocol.Issues);
        foreach (var issue in Protocol.Issues)
        {
            issue.IssueId ??= string.Empty;
            issue.RelevantExamples = CaseGuide.CleanStrings(issue.RelevantExamples);
            issue.NotRelevantExamples = CaseGuide.CleanStrings(issue.NotRelevantExamples);
        }
    }

    /// <summary>
    /// True for the relative path shape PlanNumbering writes: four '/'-separated segments starting with Mailboxes and ending
    /// in .eml, each a safe file name. Rooted paths, backslashes and ".." are refused.
    /// </summary>
    public static bool IsSafeEmlPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || Path.IsPathRooted(path))
            return false;

        var segments = path.Split('/');
        return segments.Length == 4
            && segments[0] == MailboxesRoot
            && segments.All(IsSafeFileName)
            && segments[3].EndsWith(".eml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A bare file or folder name: not blank, no "..", no invalid file-name characters (which include both separators).
    /// </summary>
    public static bool IsSafeFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains("..", StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.Trim() == name;

    private static void NormalizeThread(PlannedThread thread)
    {
        thread.Id ??= string.Empty;
        thread.IssueIds = CaseGuide.CleanStrings(thread.IssueIds);
        thread.FactIds = CaseGuide.CleanStrings(thread.FactIds);
        thread.Subject ??= string.Empty;
        thread.Purpose ??= string.Empty;
        thread.Rationale ??= string.Empty;
        thread.DecoyAngle ??= string.Empty;
        thread.MustNotMention = CaseGuide.CleanStrings(thread.MustNotMention);
        thread.LinkedThreadIds = CaseGuide.CleanStrings(thread.LinkedThreadIds);
        thread.OwnerCustodianId ??= string.Empty;
        thread.Messages = CaseGuide.CleanList(thread.Messages);

        foreach (var message in thread.Messages)
        {
            message.Id ??= string.Empty;
            message.DocId ??= string.Empty;
            message.FromId ??= string.Empty;
            message.ToIds = CaseGuide.CleanStrings(message.ToIds);
            message.CcIds = CaseGuide.CleanStrings(message.CcIds);
            message.TimeZoneId ??= string.Empty;
            message.FactIds = CaseGuide.CleanStrings(message.FactIds);
            message.Intent ??= string.Empty;
            message.MustConvey = CaseGuide.CleanStrings(message.MustConvey);
            // Only "Mailboxes/{folder}/{box}/{name}.eml" survives, so no loaded path can leave the output folder (WR-10)
            message.FilePath = IsSafeEmlPath(message.FilePath) ? message.FilePath : string.Empty;
            message.CustodianIds = CaseGuide.CleanStrings(message.CustodianIds);
            message.Attachments = CaseGuide.CleanList(message.Attachments);
            foreach (var attachment in message.Attachments)
            {
                attachment.DocId ??= string.Empty;
                attachment.FileName = IsSafeFileName(attachment.FileName) ? attachment.FileName : string.Empty;
                attachment.Title ??= string.Empty;
                attachment.Description ??= string.Empty;
            }
        }
    }
}
