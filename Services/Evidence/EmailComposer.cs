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

    /// <summary>
    /// Composes every planned message of a thread from the repaired model text. Rendered messages are matched by Seq;
    /// a planned message without one throws <see cref="ArgumentException"/> (callers validate first).
    /// </summary>
    public static ComposedThread Compose(Guid planId, PlannedThread thread, ThreadAssignment assignment, RenderedThread rendered, PeopleDirectory people)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(people);

        var marking = assignment?.Marking ?? PrivilegeMarking.None;
        var banner = marking is PrivilegeMarking.Banner or PrivilegeMarking.BannerAndSubject;
        var result = new ComposedThread { ThreadId = thread.Id };
        var bySeq = new Dictionary<int, ComposedEmail>();
        var tableUsed = false;

        foreach (var planned in (thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).OrderBy(m => m.Seq))
        {
            var text = rendered.Messages.FirstOrDefault(m => m != null && m.Seq == planned.Seq)
                ?? throw new ArgumentException($"Thread {thread.Id} has no rendered text for message {planned.Seq}.", nameof(rendered));

            // D-02: addresses, dates and threading come only from the plan
            var sender = people.Find(planned.FromId);
            var domain = sender?.Domain ?? string.Empty;
            var parent = planned.Kind != MessageKind.New && bySeq.TryGetValue(planned.ReplyToSeq, out var p) ? p : null;
            var body = Normalize(text.Body).Trim();
            var signOff = Normalize(text.SignOff).Trim();
            var signature = SignatureFor(sender, assignment?.For(planned.Seq)?.Length ?? LengthBand.Medium, planned.Kind);

            var email = new ComposedEmail
            {
                Planned = planned,
                ThreadId = thread.Id,
                MessageId = MessageIdFor(planId, planned.DocId, domain),
                InReplyTo = parent?.MessageId ?? string.Empty,
                References = KnowledgeGate.Ancestors(thread, planned.Seq)
                    .Where(bySeq.ContainsKey)
                    .Select(seq => bySeq[seq].MessageId)
                    .ToList(),
                Subject = SubjectFor(thread, planned, marking),
                From = Address(people, planned.FromId),
                To = (planned.ToIds ?? new List<string>()).Select(id => Address(people, id)).ToList(),
                Cc = (planned.CcIds ?? new List<string>()).Select(id => Address(people, id)).ToList(),
                Date = planned.SentAt,
                BodyPlain = body,
                SignOff = signOff,
                Signature = signature
            };

            email.PlainText = PlainTextOf(email, parent, banner);

            // Quoted history is composer-built HTML only; model text is encoded inside ComposeFragment (T-05-19)
            QuotedPart? quoted = null;
            if (parent != null)
            {
                var forward = planned.Kind == MessageKind.Forward;
                quoted = new QuotedPart
                {
                    IsForward = forward,
                    HeaderLines = forward
                        ? ForwardHeaderLines(parent)
                        : new List<string> { QuoteHeader(MessageTime.Local(parent.Planned), parent.From.Name, parent.From.Email) },
                    ParentFragment = parent.HtmlFragment
                };
            }

            var theme = OrganizationThemes.ForDomain(domain);
            var allowTable = !tableUsed;
            email.HtmlFragment = HtmlEmailFormatter.ComposeFragment(body, signOff, signature, quoted, theme, banner, allowTable);
            email.Html = HtmlEmailFormatter.ComposeHtml(body, signOff, signature, quoted, theme, banner, allowTable);

            // One table per thread: only a table that actually rendered (not a rejected oversize one) uses the allowance
            if (allowTable && PipeTableRegex().IsMatch(body)
                && CountOf(email.HtmlFragment, "<table") > CountOf(parent?.HtmlFragment ?? string.Empty, "<table"))
                tableUsed = true;

            bySeq[planned.Seq] = email;
            result.Emails.Add(email);
        }

        return result;
    }

    private static string PlainTextOf(ComposedEmail email, ComposedEmail? parent, bool banner)
    {
        var text = new StringBuilder();
        if (banner)
            text.Append(HtmlEmailFormatter.PrivilegedBannerText).Append("\n\n");

        text.Append(email.BodyPlain).Append("\n\n");
        if (email.SignOff.Length > 0)
            text.Append(email.SignOff).Append('\n');
        text.Append(email.Signature);

        // The model never re-quotes; code builds the whole quoted history
        if (parent != null && email.Planned.Kind is MessageKind.Reply or MessageKind.ReplyAll)
        {
            text.Append("\n\n")
                .Append(QuoteHeader(MessageTime.Local(parent.Planned), parent.From.Name, parent.From.Email))
                .Append('\n')
                .Append(ThreadingHelper.QuoteText(parent.PlainText));
        }
        else if (parent != null && email.Planned.Kind == MessageKind.Forward)
        {
            text.Append("\n\n---------- Forwarded message ----------\n")
                .Append(string.Join("\n", ForwardHeaderLines(parent)))
                .Append("\n\n")
                .Append(parent.PlainText);
        }

        return text.ToString();
    }

    private static EmailAddressPart Address(PeopleDirectory people, string id) =>
        new() { Name = people.NameOf(id), Email = people.EmailOf(id) };

    private static string Normalize(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // A "| a | b |" line followed by a "|---" separator line
    [GeneratedRegex(@"^[ \t]*\|[^\n]*\n[ \t]*\|[ \t]*:?-{3,}", RegexOptions.Multiline)]
    private static partial Regex PipeTableRegex();

    private static string Display(EmailAddressPart part) =>
        part.Email.Length > 0 ? $"{part.Name} <{part.Email}>" : part.Name;

    // Only [a-z0-9.-] survive; blank results use the fallback (T-05-18)
    private static string SanitizeDomain(string? domain)
    {
        var clean = UnsafeDomainCharRegex().Replace((domain ?? string.Empty).Trim().ToLowerInvariant(), string.Empty).Trim('.', '-');
        return clean.Length > 0 ? clean : FallbackDomain;
    }
}
