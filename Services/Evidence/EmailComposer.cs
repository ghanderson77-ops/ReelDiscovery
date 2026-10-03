using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Evidence;

public class EmailAddressPart
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// One fully structured email: every header comes from the plan, only the body and sign-off from the model (D-02).
/// </summary>
public class ComposedEmail
{
    public PlannedMessage Planned { get; set; } = new();
    public string ThreadId { get; set; } = string.Empty;

    // Without angle brackets; MimeKit adds them
    public string MessageId { get; set; } = string.Empty;

    // Empty for thread roots
    public string InReplyTo { get; set; } = string.Empty;

    // Root..parent chain; empty for thread roots
    public List<string> References { get; set; } = new();

    public string Subject { get; set; } = string.Empty;
    public EmailAddressPart From { get; set; } = new();
    public List<EmailAddressPart> To { get; set; } = new();
    public List<EmailAddressPart> Cc { get; set; } = new();

    // Planned SentAt with the sender's offset
    public DateTimeOffset Date { get; set; }

    public string PlainText { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;

    /// <summary>
    /// The model's body only (no banner, signature or quoted history), for the journal.
    /// </summary>
    public string BodyPlain { get; set; } = string.Empty;

    public string SignOff { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;

    // Body HTML without the template, nested into replies and forwards
    [JsonIgnore]
    internal string HtmlFragment { get; set; } = string.Empty;
}

public class ComposedThread
{
    public string ThreadId { get; set; } = string.Empty;
    public List<ComposedEmail> Emails { get; set; } = new();
}

/// <summary>
/// Pure composer: plan + repaired model text -> threaded, signed, themed, privilege-marked emails (CORP-05..07).
/// </summary>
public static partial class EmailComposer
{
    public const string PrivilegedSubjectTag = "Privileged & Confidential - ";
    public const string FallbackDomain = "reeldiscovery.local";

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [GeneratedRegex(@"\p{Cc}")]
    private static partial Regex ControlCharRegex();

    [GeneratedRegex(@"[^a-z0-9.\-]")]
    private static partial Regex UnsafeDomainCharRegex();

    /// <summary>
    /// Subject of a planned message: the sanitized root subject (privilege-tagged for BannerAndSubject), with one
    /// RE:/FW: prefix for replies and forwards.
    /// </summary>
    public static string SubjectFor(PlannedThread thread, PlannedMessage message, PrivilegeMarking marking)
    {
        // Same function feeds the answer-key overlay so key and EML subjects match (Pitfall 2)
        // Control characters (CR/LF included) are removed so the subject can never inject headers (T-05-17)
        var root = ControlCharRegex().Replace(thread?.Subject ?? string.Empty, string.Empty).Trim();
        if (marking == PrivilegeMarking.BannerAndSubject && !root.StartsWith(PrivilegedSubjectTag, StringComparison.OrdinalIgnoreCase))
            root = PrivilegedSubjectTag + root;

        return message?.Kind switch
        {
            MessageKind.Reply or MessageKind.ReplyAll => ThreadingHelper.AddReplyPrefix(root),
            MessageKind.Forward => ThreadingHelper.AddForwardPrefix(root),
            _ => root
        };
    }

    /// <summary>
    /// Deterministic Message-ID (no angle brackets): 24 lower-hex chars of SHA-256(PlanId:DocId) at the sender domain.
    /// </summary>
    public static string MessageIdFor(Guid planId, string docId, string senderDomain)
    {
        // Deterministic: copies share it and a rewritten thread keeps it, so de-duplication and Continue stay consistent
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{planId:N}:{docId}"))).ToLowerInvariant()[..24];
        return hash + "@" + SanitizeDomain(senderDomain);
    }

    /// <summary>
    /// The sender's signature: the cast Signature verbatim (SignatureBuilder output when blank), the first name for
    /// short NameOnly replies, the full name for externals, empty for an unknown sender.
    /// </summary>
    public static string SignatureFor(PersonInfo? sender, LengthBand band, MessageKind kind)
    {
        // D-20: code appends the cast signature verbatim; the model never writes it
        if (sender == null)
            return string.Empty;

        var member = sender.Cast;
        if (member == null)
            return sender.FullName.Trim();

        // Short replies from a NameOnly signer just sign with the first name
        if (band == LengthBand.Short && kind != MessageKind.New && member.SignatureStyle == SignatureStyle.NameOnly)
        {
            var first = member.FirstName?.Trim() ?? string.Empty;
            if (first.Length > 0)
                return first;
        }

        return string.IsNullOrWhiteSpace(member.Signature)
            ? SignatureBuilder.Build(member, null)
            : member.Signature;
    }

    /// <summary>
    /// Reply quote header, always en-US: "On Tue, Mar 19, 2024 at 9:42 AM, Name &lt;addr&gt; wrote:".
    /// </summary>
    public static string QuoteHeader(DateTimeOffset local, string name, string email) =>
        string.Format(EnUs, "On {0:ddd, MMM d, yyyy} at {0:h:mm tt}, {1} <{2}> wrote:", local, name, email);

    /// <summary>
    /// Header lines of a forwarded message block (From, Date, Subject, To, optional Cc), dates en-US sender-local.
    /// </summary>
    public static List<string> ForwardHeaderLines(ComposedEmail parent)
    {
        var local = MessageTime.Local(parent.Planned);
        var lines = new List<string>
        {
            "From: " + Display(parent.From),
            string.Format(EnUs, "Date: {0:ddd, MMM d, yyyy} at {0:h:mm tt}", local),
            "Subject: " + parent.Subject,
            "To: " + string.Join("; ", parent.To.Select(Display))
        };
        if (parent.Cc.Count > 0)
            lines.Add("Cc: " + string.Join("; ", parent.Cc.Select(Display)));
        return lines;
    }

    private static string Display(EmailAddressPart part) =>
        part.Email.Length > 0 ? $"{part.Name} <{part.Email}>" : part.Name;

    // Only [a-z0-9.-] survive; blank results use the fallback (T-05-18)
    private static string SanitizeDomain(string? domain)
    {
        var clean = UnsafeDomainCharRegex().Replace((domain ?? string.Empty).Trim().ToLowerInvariant(), string.Empty).Trim('.', '-');
        return clean.Length > 0 ? clean : FallbackDomain;
    }
}
