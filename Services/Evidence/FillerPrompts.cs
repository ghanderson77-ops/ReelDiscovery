using System.Globalization;
using System.Text;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// System prompt and per-thread user prompt for non-relevant threads (NOISE-01..05). Same skeleton as
/// <see cref="EvidencePrompts"/> (name rules, organizations, people with voice and sign-off style, thread, one line per
/// planned message with the same length token, task, feedback last) so only content separates filler from evidence
/// (D-11, D-07). The case-context sections (facts, values, ledger) are replaced by a category block: side-world work for
/// daily business and ancillary mail (D-21), angle, shared names and the must-not-mention list for decoys (D-23), and a
/// style line by sender kind for bulk mail (D-22, D-24). Filler never reads or writes the ledger (D-13).
/// </summary>
public static class FillerPrompts
{
    // Byte-stable for prompt caching (Phase 7): mirrors EvidencePrompts.SystemPrompt clause for clause; never interpolate run data
    public const string SystemPrompt = "You write the email messages of an e-discovery training dataset built from a finished case guide. Keep each person, company and organization exactly as named in the guide, including characters and companies from films, TV series, books or games. These threads are ordinary correspondence that has nothing to do with any dispute. Write natural business email the way coworkers write: specific, in each sender's own voice, and only as long as asked. Except where a thread is marked as an exchange with counsel, never use legal framing or legal terms such as alleged, plaintiff, defendant, lawsuit or liability; in counsel exchanges, counsel gives legal advice in plain professional words. Never comment on the evidence as evidence, and keep any wrongdoing in ordinary business language without operational detail. Write only the new text each sender types: no signature block, no name under the sign-off, no privilege banner, and no quoted earlier messages. Use the exact names, dates and amounts you are given; mention only the people, facts and details listed for each message; never invent people, amounts, document numbers or IDs. For each planned attachment, give a short, realistic document title in attachmentTitles. Formatting is optional and modest: at most two **bold** phrases per message, at most one short list of '- ' lines per message, and at most one small pipe table of figures in the whole thread; no headings, links or other markup.";

    // D-24: fixed in every personal prompt
    public const string PersonalSafetyLine = "everyday personal plans only: meals, kids, sports, errands, appointments; no health, money, account, HR or identity details";

    // D-22: the formatter renders no links either (T-06-48)
    public const string SpamLine = "marketing or mild phishing tone; no working links, no real brands";

    public const string NewsletterLine = "a short industry newsletter issue: 2-4 short sections, each starting with a **Heading** line";

    public const string VendorLine = "a routine vendor message about ordinary orders, deliveries, quotes or invoices";

    private const string TaskText = "Write every message above in order. For each message return its seq, the body, the signOff (a closing word or phrase such as 'Thanks,', or empty), and attachmentTitles: one short, realistic title per attachment listed for that message, in the order listed (empty when it lists none).";

    /// <summary>
    /// The filler request for one thread. Retry feedback goes last so everything before it stays byte-identical across
    /// attempts (D-13). Never prints case facts, canonical values or ledger entries (T-06-46).
    /// </summary>
    public static string BuildThreadPrompt(ThreadRenderContext ctx, SideWorld? sideWorld, IReadOnlyList<RenderViolation>? feedback = null)
    {
        var guide = ctx.Guide ?? new CaseGuide();
        var thread = ctx.Thread;
        var messages = (thread.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .OrderBy(m => m.Seq)
            .ToList();
        var participants = EvidencePrompts.OrderedParticipants(messages);

        var sb = new StringBuilder();

        // (a) Name rules without the scenario story
        CaseGuidePrompts.AppendNameRules(sb, ctx.Draft, includeScenario: false);

        // (b) Organizations of the participants
        var organizations = participants
            .Select(id => ctx.People.Find(id))
            .Where(p => p != null && !p.IsExternal && p.OrganizationId.Length > 0)
            .Select(p => guide.FindOrganization(p!.OrganizationId))
            .Where(o => o != null)
            .Select(o => o!)
            .DistinctBy(o => o.Id)
            .ToList();
        if (organizations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Organizations:");
            foreach (var organization in organizations)
                sb.AppendLine(string.IsNullOrWhiteSpace(organization.Domain)
                    ? organization.Name.Trim()
                    : $"{organization.Name.Trim()} ({organization.Domain.Trim()})");
        }

        // (c) People with their voice and sign-off style
        sb.AppendLine();
        sb.AppendLine("People in this thread:");
        foreach (var id in participants)
            sb.AppendLine(EvidencePrompts.PersonLine(ctx, guide, id));

        // (d) Category block replaces the case-context sections (facts, values, ledger)
        sb.AppendLine();
        switch (thread.Category)
        {
            case NonRelevantCategory.Decoy:
                AppendDecoy(sb, thread, organizations, guide);
                break;
            case NonRelevantCategory.NewsletterSpamPersonal:
                sb.AppendLine($"Style: {BulkLine(ctx, participants)}");
                break;
            default:
                AppendOngoingWork(sb, organizations, sideWorld, guide);
                break;
        }

        // (g) The thread
        sb.AppendLine();
        sb.AppendLine($"Thread id: {thread.Id}");
        sb.AppendLine($"Subject: \"{thread.Subject.Trim()}\"");
        if (!string.IsNullOrWhiteSpace(thread.Purpose))
            sb.AppendLine($"Purpose: {thread.Purpose.Trim()}");
        // Pitfall 2: privileged non-relevant threads read like relevant counsel threads
        if (thread.IsPrivileged)
            sb.AppendLine(EvidencePrompts.CounselText);

        // (h) One line per planned message
        foreach (var message in messages)
            sb.AppendLine(MessageLine(ctx, message, messages.Count));

        // (i) Task
        sb.AppendLine();
        sb.AppendLine(TaskText);

        // (j) Retry feedback last: the prefix above stays cacheable (D-13)
        if (feedback != null && feedback.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(EvidencePrompts.FeedbackHeader);
            foreach (var violation in feedback.Where(v => v != null))
                sb.AppendLine(violation.Seq > 0 ? $"- msg {violation.Seq}: {violation.Text}" : $"- thread: {violation.Text}");
        }

        return sb.ToString();
    }

    // D-21: the side projects of every participant organization, persisted or validated templates past the cap
    private static void AppendOngoingWork(StringBuilder sb, List<CaseOrganization> organizations, SideWorld? sideWorld, CaseGuide guide)
    {
        sb.AppendLine("Ongoing work:");
        var lines = 0;
        foreach (var organization in organizations)
        {
            foreach (var project in SideWorldBuilder.ProjectsFor(sideWorld, organization, guide).Where(p => p != null && p.Name.Length > 0))
            {
                var topics = (project.Topics ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
                sb.AppendLine(topics.Count > 0
                    ? $"{organization.Name.Trim()}: {project.Name.Trim()} ({string.Join(", ", topics)})"
                    : $"{organization.Name.Trim()}: {project.Name.Trim()}");
                lines++;
            }
        }

        if (lines == 0)
            sb.AppendLine("(routine day-to-day work)");
    }

    // D-23: why it looks relevant, the names it may share with the case, and the terms the code scan rejects
    private static void AppendDecoy(StringBuilder sb, PlannedThread thread, List<CaseOrganization> organizations, CaseGuide guide)
    {
        var avoid = (thread.MustNotMention ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var shared = organizations.Select(o => o.Name)
            .Concat((guide.Values ?? new List<CanonicalValue>()).Where(v => v != null && v.Kind == ValueKind.ProjectName).Select(v => v.Value))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Where(n => !avoid.Any(t => RenderValidator.ContainsWord(n, t) || RenderValidator.ContainsWord(t, n)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(thread.DecoyAngle))
            sb.AppendLine($"Angle: {thread.DecoyAngle.Trim()}");
        sb.AppendLine($"Shared names you may use: {(shared.Count > 0 ? string.Join(", ", shared) : "(none)")}");
        if (avoid.Count > 0)
            sb.AppendLine($"Do not mention: {string.Join("; ", avoid)}");
    }

    // D-22/D-24: the style of bulk mail follows the first external sender's kind
    private static string BulkLine(ThreadRenderContext ctx, List<string> participants)
    {
        var kind = participants
            .Select(id => ctx.People.Find(id)?.ExternalKind)
            .FirstOrDefault(k => k != null);
        return kind switch
        {
            ExternalKind.Newsletter => NewsletterLine,
            ExternalKind.Spam => SpamLine,
            ExternalKind.Vendor => VendorLine,
            _ => PersonalSafetyLine
        };
    }

    // "msg 2 | Reply reply to msg 1 | from Tom Becker to Lisa Chen; cc Raymond Harmon | 2024-03-19 11:42 | length: 8-60 words | attachments: Word document (title needed)"
    private static string MessageLine(ThreadRenderContext ctx, PlannedMessage message, int messageCount)
    {
        var assignment = ctx.Assignment?.For(message.Seq);
        var band = RenderAssignments.Spec(assignment?.Length ?? LengthBand.Medium);
        var attachments = (message.Attachments ?? new List<PlannedAttachment>()).Where(a => a != null).ToList();

        var sb = new StringBuilder();
        sb.Append($"msg {message.Seq} | {message.Kind}");
        if (message.ReplyToSeq > 0)
            sb.Append($" reply to msg {message.ReplyToSeq}");

        sb.Append($" | from {ctx.People.NameOf(message.FromId)} to {EvidencePrompts.Names(ctx, message.ToIds)}");
        if (message.CcIds.Count > 0)
            sb.Append($"; cc {EvidencePrompts.Names(ctx, message.CcIds)}");

        sb.Append($" | {MessageTime.Local(message).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");

        // D-28: a single-message voicemail thread's body is the spoken transcript
        var isVoicemail = messageCount == 1 && attachments.Any(a => a.Kind == AttachmentType.Voicemail);
        sb.Append(isVoicemail
            ? $" | {EvidencePrompts.VoicemailInstruction(ctx.People.NameOf(message.FromId))}"
            : $" | length: {band.MinWords}-{band.MaxWords} words");

        if (!string.IsNullOrWhiteSpace(message.Intent))
            sb.Append($" | intent: {message.Intent.Trim()}");

        // Pitfall 1: one title per planned attachment, in plan order, so filler names look like relevant ones
        if (attachments.Count > 0)
            sb.Append($" | attachments: {string.Join(", ", attachments.Select(AttachmentLabel))}");

        return sb.ToString();
    }

    private static string AttachmentLabel(PlannedAttachment attachment)
    {
        var noun = attachment.Kind switch
        {
            AttachmentType.Word => "Word document",
            AttachmentType.Excel => "Excel workbook",
            AttachmentType.PowerPoint => "PowerPoint deck",
            AttachmentType.Image => "image",
            AttachmentType.CalendarInvite => "calendar invite",
            AttachmentType.Voicemail => "voicemail recording",
            _ => "file"
        };
        return string.IsNullOrWhiteSpace(attachment.Title)
            ? $"{noun} (title needed)"
            : $"{noun} \"{attachment.Title.Trim()}\"";
    }
}
