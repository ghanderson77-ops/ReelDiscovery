using System.Globalization;
using System.Text;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// System prompt and per-thread user prompt for the evidence render call (D-07). The user prompt holds point-in-time
/// context only: gated facts, knowable values, the selected ledger entries and the thread's planned messages (CORP-01).
/// Prompts carry no format instructions: the strict schema defines the response shape.
/// </summary>
public static class EvidencePrompts
{
    // Byte-stable for prompt caching (Phase 7): never interpolate run data into this text (D-01, D-03)
    public const string SystemPrompt = "You write the email messages of an e-discovery training dataset built from a finished case guide. Keep each person, company and organization exactly as named in the guide, including characters and companies from films, TV series, books or games. Take a firm position on what happened, as the case guide states it. Write natural business email the way coworkers write: specific, in each sender's own voice, and only as long as asked. Except where a thread is marked as an exchange with counsel, never use legal framing or legal terms such as alleged, plaintiff, defendant, lawsuit or liability; in counsel exchanges, counsel gives legal advice in plain professional words. Never comment on the evidence as evidence, and keep any wrongdoing in ordinary business language without operational detail. Write only the new text each sender types: no signature block, no name under the sign-off, no privilege banner, and no quoted earlier messages. Use the exact names, dates and amounts you are given; mention only the people, facts and details listed for each message; never invent people, amounts, document numbers or IDs. Formatting is optional and modest: at most two **bold** phrases per message, at most one short list of '- ' lines per message, and at most one small pipe table of figures in the whole thread; no headings, links or other markup.";

    public const string HotSuffix = " This is a key document: state the key point plainly.";

    // Shared with FillerPrompts; text unchanged so EvidencePromptsTests stay green
    internal const string CounselText = "This is a confidential exchange with counsel: counsel gives legal advice in plain words; the client asks and answers plainly.";

    private const string TaskText = "Write every message above in order. For each message return its seq, the body, and the signOff (a closing word or phrase such as 'Thanks,', or empty), and list every person the message names in peopleNamed. Then list newDetails: specific new commitments, figures, meetings, decisions, people or documents this thread introduces that later emails may refer back to, each tied to the seq that introduced it.";

    // Shared with FillerPrompts; text unchanged so EvidencePromptsTests stay green
    internal const string FeedbackHeader = "Problems in the previous attempt (fix all of them and rewrite the whole thread):";

    /// <summary>
    /// The render request for one thread. Retry feedback goes last so everything before it stays byte-identical across
    /// attempts (D-13). Never uses CorpusPlanPrompts' case synopsis or full value list: they print the issues' relevance
    /// criteria and every canonical value regardless of who could know it (Pitfall 1/2).
    /// </summary>
    public static string BuildThreadPrompt(ThreadRenderContext ctx, IReadOnlyList<RenderViolation>? feedback = null)
    {
        var guide = ctx.Guide ?? new CaseGuide();
        var thread = ctx.Thread;
        var knowledge = ctx.Knowledge;
        var messages = (thread.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .OrderBy(m => m.Seq)
            .ToList();
        var participants = OrderedParticipants(messages);
        var onThread = new HashSet<string>(participants, StringComparer.Ordinal);

        var sb = new StringBuilder();

        // (a) Name rules without the scenario story (keeps the case narrative out of render prompts)
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
            sb.AppendLine(PersonLine(ctx, guide, id));

        // (d) Facts known to someone on the thread by its last message
        sb.AppendLine();
        sb.AppendLine($"What they know as of {knowledge.LastDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:");
        if (knowledge.Facts.Count == 0)
            sb.AppendLine("(none)");
        foreach (var fact in knowledge.Facts)
        {
            var date = string.IsNullOrWhiteSpace(fact.Date) ? "undated" : fact.Date.Trim();
            sb.AppendLine($"{fact.Id} {date} {fact.Title.Trim()}: {fact.Description.Trim()} (known to: {KnownTo(ctx, KnowledgeGate.KnowersOf(fact), onThread)})");
        }

        // (e) Values someone on the thread may know by its last message
        var values = knowledge.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.Value.Label) || !string.IsNullOrWhiteSpace(v.Value.Value))
            .ToList();
        sb.AppendLine();
        sb.AppendLine("Exact values they may use:");
        if (values.Count == 0)
            sb.AppendLine("(none)");
        foreach (var value in values)
        {
            var from = value.KnowableFrom != null
                ? $"known from {value.KnowableFrom.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to"
                : "known to";
            sb.AppendLine($"{value.Value.Label.Trim()}: {value.Value.Value.Trim()} ({from}: {KnownTo(ctx, value.KnownBy, onThread)})");
        }

        // (f) Ledger entries selected for this thread (earlier waves, dated before it, seen by someone on it)
        sb.AppendLine();
        sb.AppendLine("Earlier details from previous emails (refer back to them when natural; do not present them as new):");
        if (ctx.LedgerView.Count == 0)
            sb.AppendLine("(none)");
        foreach (var entry in ctx.LedgerView)
        {
            var value = string.IsNullOrWhiteSpace(entry.Value) ? string.Empty : $" [{entry.Value.Trim()}]";
            var known = string.Join(", ", (entry.KnownByIds ?? new List<string>()).Where(onThread.Contains).Select(ctx.People.NameOf));
            sb.AppendLine($"{entry.Date} {entry.Kind}: {entry.About.Trim()} - {entry.Statement.Trim()}{value} (known to: {known})");
        }

        // (g) The thread
        sb.AppendLine();
        sb.AppendLine($"Thread id: {thread.Id}");
        sb.AppendLine($"Subject: \"{thread.Subject.Trim()}\"");
        sb.AppendLine($"Purpose: {thread.Purpose.Trim()}");
        if (thread.IsPrivileged)
            sb.AppendLine(CounselText);

        // (h) One line per planned message
        var facts = guide.Facts
            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id))
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var message in messages)
            sb.AppendLine(MessageLine(ctx, message, facts));

        // (i) Task
        sb.AppendLine();
        sb.AppendLine(TaskText);

        // (j) Retry feedback last: the prefix above stays cacheable (D-13)
        if (feedback != null && feedback.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(FeedbackHeader);
            foreach (var violation in feedback.Where(v => v != null))
                sb.AppendLine(violation.Seq > 0 ? $"- msg {violation.Seq}: {violation.Text}" : $"- thread: {violation.Text}");
        }

        return sb.ToString();
    }

    internal static string SubtletyGuidance(Subtlety subtlety) => subtlety switch
    {
        Subtlety.Indirect => "indirect: logistics, oblique references, euphemism or 'call me'; do not state the problem outright",
        Subtlety.Clear => "clearly about the matter in ordinary business words",
        Subtlety.Explicit => "explicit: states the key point plainly",
        Subtlety.Counsel => "legal advice to or from counsel in plain words",
        _ => "a short reaction to the quoted message; add no new facts"
    };

    // "msg 2 | Reply reply to msg 1 | from Tom Becker to Lisa Chen; cc Raymond Harmon | 2024-03-19 11:42 | subtlety: ... | length: 8-60 words | may mention: ..."
    private static string MessageLine(ThreadRenderContext ctx, PlannedMessage message, IReadOnlyDictionary<string, KeyFact> facts)
    {
        var assignment = ctx.Assignment?.For(message.Seq);
        var subtlety = assignment?.Subtlety ?? (message.Basis == RelevanceBasis.Own ? Subtlety.Clear : Subtlety.None);
        var band = RenderAssignments.Spec(assignment?.Length ?? LengthBand.Medium);

        var sb = new StringBuilder();
        sb.Append($"msg {message.Seq} | {message.Kind}");
        if (message.ReplyToSeq > 0)
            sb.Append($" reply to msg {message.ReplyToSeq}");

        sb.Append($" | from {ctx.People.NameOf(message.FromId)} to {Names(ctx, message.ToIds)}");
        if (message.CcIds.Count > 0)
            sb.Append($"; cc {Names(ctx, message.CcIds)}");

        sb.Append($" | {MessageTime.Local(message).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        sb.Append($" | subtlety: {SubtletyGuidance(subtlety)}");
        // D-28: a single-message voicemail thread's body is the spoken transcript
        var isVoicemail = (ctx.Thread.Messages ?? new List<PlannedMessage>()).Count(m => m != null) == 1
            && (message.Attachments ?? new List<PlannedAttachment>()).Any(a => a != null && a.Kind == AttachmentType.Voicemail);
        sb.Append(isVoicemail
            ? $" | {VoicemailInstruction(ctx.People.NameOf(message.FromId))}"
            : $" | length: {band.MinWords}-{band.MaxWords} words");

        var mayMention = ctx.Knowledge.MayMentionBySeq.TryGetValue(message.Seq, out var ids) ? ids : new List<string>();
        var mention = mayMention
            .Select(id => facts.TryGetValue(id, out var fact) ? $"{id} {fact.Title.Trim()}" : id)
            .ToList();
        sb.Append($" | may mention: {(mention.Count > 0 ? string.Join("; ", mention) : "nothing beyond the quoted messages")}");

        if (!string.IsNullOrWhiteSpace(message.Intent))
            sb.Append($" | intent: {message.Intent.Trim()}");

        var points = (message.MustConvey ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (points.Count > 0)
            sb.Append($" | must convey: {string.Join("; ", points)}");

        var titles = (message.Attachments ?? new List<PlannedAttachment>())
            .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Title))
            .Select(a => $"\"{a.Title.Trim()}\"")
            .ToList();
        if (titles.Count > 0)
            sb.Append($" | attachments: {string.Join(", ", titles)}");

        // D-17 (locked): hot documents are always explicit, whatever subtlety was assigned
        if (message.IsHot)
            sb.Append(HotSuffix);

        return sb.ToString();
    }

    internal static string VoicemailInstruction(string caller) =>
        $"voicemail: write what {caller} says on the phone, 20-90 words, spoken style, no greeting line or sign-off";

    // Shared with AttachmentPrompts and FillerPrompts (06-PATTERNS)
    // "CH-004 Lisa Chen, Project Executive, Harmon Builders, Inc. | voice: ... | signs off: Full"; externals "EXT-002 Dana Whitfield, summitglazingsupply.com"
    internal static string PersonLine(ThreadRenderContext ctx, CaseGuide guide, string id)
    {
        var person = ctx.People.Find(id);
        if (person == null)
            return id;
        if (person.IsExternal || person.Cast == null)
            return string.IsNullOrWhiteSpace(person.Domain) ? $"{id} {person.FullName}" : $"{id} {person.FullName}, {person.Domain}";

        var member = person.Cast;
        var parts = new List<string> { person.FullName };
        if (!string.IsNullOrWhiteSpace(member.Title))
            parts.Add(member.Title.Trim());
        var organization = guide.FindOrganization(member.OrganizationId)?.Name;
        if (!string.IsNullOrWhiteSpace(organization))
            parts.Add(organization.Trim());

        var line = $"{id} {string.Join(", ", parts)}";
        if (!string.IsNullOrWhiteSpace(member.VoiceNote))
            line += $" | voice: {member.VoiceNote.Trim()}";
        return line + $" | signs off: {member.SignatureStyle}";
    }

    // Knowers on the thread by name; "everyone" when public; every knower when none of them is on the thread
    internal static string KnownTo(ThreadRenderContext ctx, IReadOnlyCollection<string> knowers, HashSet<string> onThread)
    {
        if (knowers.Count == 0)
            return "everyone";
        var present = knowers.Where(onThread.Contains).ToList();
        var listed = present.Count > 0 ? present : knowers.ToList();
        return string.Join(", ", listed.Select(ctx.People.NameOf));
    }

    internal static string Names(ThreadRenderContext ctx, IEnumerable<string> ids) =>
        string.Join(", ", (ids ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(ctx.People.NameOf));

    // Participants in order of first appearance (sender, to, cc by seq)
    internal static List<string> OrderedParticipants(IEnumerable<PlannedMessage> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var message in messages)
        {
            foreach (var id in new[] { message.FromId }.Concat(message.ToIds ?? new List<string>()).Concat(message.CcIds ?? new List<string>()))
            {
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id.Trim()))
                    ordered.Add(id.Trim());
            }
        }

        return ordered;
    }
}
