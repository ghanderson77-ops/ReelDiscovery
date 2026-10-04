using System.Security.Cryptography;
using System.Text;
using MimeKit;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Writes one composed email as an EML copy in every participating custodian's mailbox (OUT-01, D-16): the sender's copy
/// in Sent Items, each cast recipient's copy in Inbox. Writes are atomic and never leave the case folder (T-05-27).
/// </summary>
public class MailboxWriter
{
    public const string DocIdHeader = "X-ReelDiscovery-DocID";

    private readonly string _caseFolder;
    private readonly IReadOnlyDictionary<string, string> _custodianFolders;

    public MailboxWriter(string caseFolder, IReadOnlyDictionary<string, string> custodianFolders)
    {
        _caseFolder = caseFolder;
        _custodianFolders = custodianFolders;
    }

    /// <summary>
    /// Relative copy paths for a planned message: sender copy first, then recipients in CustodianIds order, then the
    /// planned FilePath when not already listed. Only paths that pass CorpusPlan.IsSafeEmlPath are returned.
    /// </summary>
    public static List<string> CopyPathsFor(PlannedMessage message, IReadOnlyDictionary<string, string> folders)
    {
        var custodians = (message.CustodianIds ?? new List<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // The sender's copy leads, whatever order the custodian list is in
        var ordered = custodians.Where(id => id == message.FromId)
            .Concat(custodians.Where(id => id != message.FromId));

        var paths = new List<string>();
        foreach (var id in ordered)
        {
            if (!folders.TryGetValue(id, out var folder))
                continue;

            var box = id == message.FromId ? PlanNumbering.SentFolder : PlanNumbering.InboxFolder;
            paths.Add($"{PlanNumbering.MailboxesFolder}/{folder}/{box}/{message.DocId}.eml");
        }

        if (!string.IsNullOrEmpty(message.FilePath))
            paths.Add(message.FilePath);

        return paths
            .Where(CorpusPlan.IsSafeEmlPath)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds the EML: plan headers (Message-ID, threading, Date with the sender's offset, DocID header) and a
    /// multipart/alternative body with text/plain first and text/html second. Inline images join the HTML in a
    /// multipart/related; file attachments follow the body in plan order inside a multipart/mixed (ATT-03). Every
    /// attachment part carries the DocID header of its answer-key row.
    /// </summary>
    public static MimeMessage BuildMime(ComposedEmail email)
    {
        var message = new MimeMessage
        {
            MessageId = email.MessageId.Trim('<', '>'),

            // Plan SentAt keeps the sender's offset; never new DateTimeOffset(DateTime) (EmlFileService defect)
            Date = email.Date,
            Subject = email.Subject
        };

        message.From.Add(new MailboxAddress(email.From.Name, email.From.Email));
        foreach (var to in email.To)
            message.To.Add(new MailboxAddress(to.Name, to.Email));
        foreach (var cc in email.Cc)
            message.Cc.Add(new MailboxAddress(cc.Name, cc.Email));

        if (!string.IsNullOrWhiteSpace(email.InReplyTo))
            message.InReplyTo = email.InReplyTo.Trim('<', '>');
        foreach (var reference in email.References.Where(r => !string.IsNullOrWhiteSpace(r)))
            message.References.Add(reference.Trim('<', '>'));

        // OUT-02: ties every custodian copy back to the answer key row
        message.Headers.Add(DocIdHeader, email.Planned.DocId);

        // D-16: multipart/alternative, plain text first so clients prefer the HTML part
        var builder = new BodyBuilder { TextBody = email.PlainText, HtmlBody = email.Html };
        AddAttachments(builder, email.Attachments ?? new List<ComposedAttachment>());
        var body = builder.ToMessageBody();

        // Boundaries derived from the message identity so rewriting the same email gives byte-identical files (D-26)
        SetBoundaries(body, email, "0");
        message.Body = body;

        // Long or non-ASCII lines get a transfer encoding, keeping every line within RFC 5322 limits
        message.Prepare(EncodingConstraint.SevenBit);
        return message;
    }

    /// <summary>
    /// Full path of a safe relative EML path under the case folder, or null when the path is unsafe or resolves
    /// anywhere outside the case folder (T-05-27).
    /// </summary>
    internal static string? ResolveUnderCase(string caseFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(caseFolder) || !CorpusPlan.IsSafeEmlPath(relativePath))
            return null;

        var root = Path.GetFullPath(caseFolder).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(Path.Combine(caseFolder, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Writes every custodian copy atomically and returns the relative paths written. IO failures propagate so the
    /// unit of work can record a Write failure.
    /// </summary>
    public async Task<List<string>> WriteAsync(ComposedEmail email, CancellationToken ct = default)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            BuildMime(email).WriteTo(ms, ct);
            bytes = ms.ToArray();
        }

        var written = new List<string>();
        foreach (var relative in CopyPathsFor(email.Planned, _custodianFolders))
        {
            ct.ThrowIfCancellationRequested();
            var full = ResolveUnderCase(_caseFolder, relative);
            if (full == null)
                continue;

            // The atomic writer creates its temp file beside the target and does not create folders
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await CaseStore.WriteAtomicAsync(full, bytes, ct).ConfigureAwait(false);
            written.Add(relative);
        }

        return written;
    }

    private static void AddAttachments(BodyBuilder builder, IEnumerable<ComposedAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (attachment == null || attachment.Bytes == null || attachment.Bytes.Length == 0)
                continue;

            // T-06-30: names may come from model titles; keep only a safe bare file name
            var name = Path.GetFileName(attachment.FileName ?? string.Empty);
            if (!CorpusPlan.IsSafeFileName(name))
                continue;

            if (attachment.Kind == AttachmentType.Image && attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                // Inline image: referenced from the HTML by cid:, so it sits beside the HTML in multipart/related
                var part = builder.LinkedResources.Add(name, attachment.Bytes, new ContentType("image", "png"));
                part.ContentId = attachment.ContentId;
                part.Headers.Add(DocIdHeader, attachment.DocId);
                continue;
            }

            var file = builder.Attachments.Add(name, attachment.Bytes, ContentTypeFor(attachment.Kind));
            file.Headers.Add(DocIdHeader, attachment.DocId);
        }
    }

    private static ContentType ContentTypeFor(AttachmentType kind)
    {
        switch (kind)
        {
            case AttachmentType.Word:
                return new ContentType("application", "vnd.openxmlformats-officedocument.wordprocessingml.document");
            case AttachmentType.Excel:
                return new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            case AttachmentType.PowerPoint:
                return new ContentType("application", "vnd.openxmlformats-officedocument.presentationml.presentation");
            case AttachmentType.Image:
                return new ContentType("image", "png");
            case AttachmentType.CalendarInvite:
                // UI-SPEC S5: an invite, not a published calendar
                var calendar = new ContentType("text", "calendar") { Charset = "utf-8" };
                calendar.Parameters.Add("method", "REQUEST");
                return calendar;
            case AttachmentType.Voicemail:
                return new ContentType("audio", "mpeg");
            default:
                return new ContentType("application", "octet-stream");
        }
    }

    private static void SetBoundaries(MimeEntity entity, ComposedEmail email, string path)
    {
        if (entity is not Multipart multipart)
            return;

        // Root formula unchanged: Phase 5 EMLs without attachments stay byte-identical
        multipart.Boundary = path == "0" ? BoundaryFor(email) : NestedBoundaryFor(email, path);
        for (var i = 0; i < multipart.Count; i++)
            SetBoundaries(multipart[i], email, $"{path}.{i}");
    }

    private static string NestedBoundaryFor(ComposedEmail email, string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{email.Planned.DocId}|{email.MessageId}|{path}"));
        return "=-rd-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private static string BoundaryFor(ComposedEmail email)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{email.Planned.DocId}|{email.MessageId}"));
        return "=-rd-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }
}
