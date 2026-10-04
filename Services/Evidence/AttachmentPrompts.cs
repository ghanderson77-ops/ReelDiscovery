using System.Globalization;
using System.Text;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Prompts of the per-attachment text call (D-14): the parent email, the plan spec, the richness level and only the case
/// context known to the parent message's participants as of its date (Pitfall 4). Also the code-built image prompt
/// (D-27, Pitfall 12). Prompts carry no format instructions: the strict schema defines the response shape.
/// </summary>
public static class AttachmentPrompts
{
    // Byte-stable for prompt caching (Phase 7): never interpolate run data (D-01, D-03)
    public const string SystemPrompt = "You write the content of one document attached to an email in an e-discovery training dataset built from a finished case guide. Keep each person, company and organization exactly as named in the guide, including characters and companies from films, TV series, books or games. Write the document the way the sender's organization would: specific and businesslike. Never use legal framing or legal terms such as alleged, plaintiff, defendant, lawsuit or liability unless the email is an exchange with counsel. Never comment on the document as evidence. Use the exact names, dates and amounts you are given and mention only the people and details listed. Supporting line items, schedules and totals may be new, but every amount the guide gives must appear exactly as given, and you must list each new figure, date, total or commitment you introduce in newDetails. Write plain text only: no markup, links or formatting symbols.";

    // Byte-stable for prompt caching (Phase 7): never interpolate run data (D-01, D-03)
    public const string FillerSystemPrompt = "You write the content of one document attached to an email in an e-discovery training dataset built from a finished case guide. Keep each person, company and organization exactly as named in the guide, including characters and companies from films, TV series, books or games. This document is ordinary business or personal material unrelated to any dispute. Keep it mundane and specific. Write plain text only: no markup, links or formatting symbols.";

    public const string LateRepairText = "Later emails already exist: do not introduce new figures, dates, totals or commitments; use only the details listed above.";

    public const string NoTextRule = "no text, letters, numbers, signage, logos or labels anywhere";

    public const string EarlierDraftNotAvailable = "The earlier version: earlier draft not available; write this version so it reads as a revision of an earlier draft.";

    // Chain context cap (RESEARCH Pattern 10)
    public const int MaxPriorVersionWords = 1_500;

    private const string FeedbackHeader = "Problems in the previous attempt (fix all of them and rewrite the whole document):";
    private const string CounselText = "The email is a confidential exchange with counsel.";
    private const string InviteTask = "Write only the meeting title, a short agenda and a location; the date, time and attendees are fixed above.";

    /// <summary>
    /// D-15 richness, the one rule shared with GenerationCostEstimator (review 06-15).
    /// </summary>
    public static bool IsRich(AttachmentRenderContext ctx) =>
        ctx.Relevant && GenerationCostEstimator.IsRichAttachment(ctx.Thread.Thread, ctx.Parent, ctx.Thread.Guide);

    /// <summary>
    /// The user prompt for one attachment attempt. Retry feedback goes last so everything before it stays
    /// byte-identical across attempts (D-13).
    /// </summary>
    public static string Build(AttachmentRenderContext ctx, IReadOnlyList<RenderViolation>? feedback = null)
    {
        var threadCtx = ctx.Thread;
        var guide = threadCtx.Guide ?? new CaseGuide();
        var thread = threadCtx.Thread;
        var parent = ctx.Parent;
        var participants = EvidencePrompts.OrderedParticipants(new[] { parent });
        var onMessage = new HashSet<string>(participants, StringComparer.Ordinal);
        var parentDate = MessageTime.LocalDate(parent);

        var sb = new StringBuilder();

        // (a) Name rules without the scenario story
        CaseGuidePrompts.AppendNameRules(sb, threadCtx.Draft, includeScenario: false);

        // (b) Organizations of the parent participants
        var organizations = participants
            .Select(id => threadCtx.People.Find(id))
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

        // (c) People on the parent message
        sb.AppendLine();
        sb.AppendLine("People on the email:");
        foreach (var id in participants)
            sb.AppendLine(EvidencePrompts.PersonLine(threadCtx, guide, id));

        // (d) Point-in-time case context (relevant) or ordinary context (filler, decoy)
        if (ctx.Relevant)
            AppendKnowledge(sb, ctx, guide, onMessage, parentDate);
        else
            AppendFillerContext(sb, ctx, guide);

        // (e) The parent email
        sb.AppendLine();
        sb.AppendLine("The email this document is attached to:");
        sb.AppendLine($"Attachment id: {ctx.Spec.DocId}");
        sb.AppendLine($"Thread id: {thread.Id}");
        sb.AppendLine($"Subject: \"{thread.Subject.Trim()}\"");
        var from = $"From {threadCtx.People.NameOf(parent.FromId)} to {EvidencePrompts.Names(threadCtx, parent.ToIds)}";
        if (parent.CcIds.Count > 0)
            from += $"; cc {EvidencePrompts.Names(threadCtx, parent.CcIds)}";
        sb.AppendLine(from);
        sb.AppendLine($"Sent: {MessageTime.Local(parent).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        if (thread.IsPrivileged)
            sb.AppendLine(CounselText);
        sb.AppendLine("Body:");
        sb.AppendLine(string.IsNullOrWhiteSpace(ctx.ParentBody) ? "(empty)" : ctx.ParentBody.Trim());

        // (f) The document spec, size (D-15), chain (D-16) and invite slot (D-20)
        sb.AppendLine();
        sb.AppendLine("The document:");
        sb.AppendLine($"Kind: {KindName(ctx.Spec.Kind)}");
        sb.AppendLine($"File name: {ctx.FileName}");
        sb.AppendLine($"Title: {ctx.Title}");
        if (!string.IsNullOrWhiteSpace(ctx.Spec.Description))
            sb.AppendLine($"Description: {ctx.Spec.Description.Trim()}");
        var size = SizeGuidance(ctx.Spec.Kind, ctx.Rich);
        if (size.Length > 0)
            sb.AppendLine($"Length: {size}");
        AppendChain(sb, ctx);
        if (ctx.Spec.Kind == AttachmentType.CalendarInvite)
            AppendInvite(sb, ctx);

        // (g) Task
        sb.AppendLine();
        sb.AppendLine(TaskFor(ctx.Spec.Kind, ctx.Relevant));

        // (h) Retry feedback last: the prefix above stays cacheable (D-13)
        if (feedback != null && feedback.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(FeedbackHeader);
            foreach (var violation in feedback.Where(v => v != null))
                sb.AppendLine($"- {violation.Text}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The image prompt, built in code from the spec and the sender's setting (D-27, Pitfall 12): never a person or
    /// organization name, and never any text in the picture.
    /// </summary>
    public static string ImagePrompt(AttachmentRenderContext ctx)
    {
        var threadCtx = ctx.Thread;
        var guide = threadCtx.Guide ?? new CaseGuide();
        var tokens = NameTokens(threadCtx, guide);
        var sender = threadCtx.People.Find(ctx.Parent.FromId);

        if (sender?.ExternalKind == ExternalKind.Spam)
        {
            var product = Scrub(string.IsNullOrWhiteSpace(threadCtx.Thread.Subject) ? ctx.Title : threadCtx.Thread.Subject, tokens);
            product = product.Length > 0 ? product.ToLowerInvariant() : "a seasonal offer";
            return $"an abstract marketing graphic for {product}; bright colors; {NoTextRule}";
        }

        var source = string.IsNullOrWhiteSpace(ctx.Spec.Description) ? ctx.Title : ctx.Spec.Description;
        var scene = Scrub(source, tokens);
        if (scene.Length == 0)
            scene = "a work site";

        var organization = sender != null && !sender.IsExternal ? guide.FindOrganization(sender.OrganizationId) : null;
        var setting = Scrub(organization?.Location ?? string.Empty, tokens);
        var where = setting.Length > 0 ? $", in {setting}" : string.Empty;
        return $"{scene}{where}; documentary photo; no people's faces in focus; {NoTextRule}";
    }

    private static void AppendKnowledge(StringBuilder sb, AttachmentRenderContext ctx, CaseGuide guide, HashSet<string> onMessage,
        DateOnly parentDate)
    {
        var threadCtx = ctx.Thread;
        var knowledge = threadCtx.Knowledge;
        var seq = ctx.Parent.Seq;

        // Facts the sender may mention at the parent message (Pitfall 4)
        var mayMention = knowledge.MayMentionBySeq.TryGetValue(seq, out var ids) ? ids : new List<string>();
        var facts = mayMention
            .Select(id => guide.FindFact(id))
            .Where(f => f != null)
            .Select(f => f!)
            .ToList();
        sb.AppendLine();
        sb.AppendLine($"What they know as of {parentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:");
        if (facts.Count == 0)
            sb.AppendLine("(none)");
        foreach (var fact in facts)
        {
            var date = string.IsNullOrWhiteSpace(fact.Date) ? "undated" : fact.Date.Trim();
            sb.AppendLine($"{fact.Id} {date} {fact.Title.Trim()}: {fact.Description.Trim()} (known to: {EvidencePrompts.KnownTo(threadCtx, KnowledgeGate.KnowersOf(fact), onMessage)})");
        }

        // Values the sender knows at the parent message
        var known = knowledge.KnownValueIdsBySeq.TryGetValue(seq, out var valueIds) ? valueIds : new HashSet<string>(StringComparer.Ordinal);
        var values = knowledge.Values
            .Where(v => known.Contains(v.Value.Id))
            .Where(v => !string.IsNullOrWhiteSpace(v.Value.Label) || !string.IsNullOrWhiteSpace(v.Value.Value))
            .ToList();
        sb.AppendLine();
        sb.AppendLine("Exact values they may use:");
        if (values.Count == 0)
            sb.AppendLine("(none)");
        foreach (var value in values)
            sb.AppendLine($"{value.Value.Label.Trim()}: {value.Value.Value.Trim()}");

        // Earlier ledger entries dated on or before the parent and seen by someone on it
        var ledger = (threadCtx.LedgerView ?? new List<LedgerEntry>())
            .Where(e => e != null && e.ParsedDate != null && e.ParsedDate.Value <= parentDate)
            .Where(e => (e.KnownByIds ?? new List<string>()).Count == 0 || e.KnownByIds!.Any(onMessage.Contains))
            .ToList();
        sb.AppendLine();
        sb.AppendLine("Earlier details from previous emails:");
        if (ledger.Count == 0)
            sb.AppendLine("(none)");
        foreach (var entry in ledger)
        {
            var value = string.IsNullOrWhiteSpace(entry.Value) ? string.Empty : $" [{entry.Value.Trim()}]";
            sb.AppendLine($"{entry.Date} {entry.Kind}: {entry.About.Trim()} - {entry.Statement.Trim()}{value}");
        }

        // Review concern 4: ancestry, not sequence
        var ancestors = KnowledgeGate.Ancestors(threadCtx.Thread, seq);
        var visible = new HashSet<int>(ancestors) { seq };

        var deltas = (ctx.VisibleThreadDeltas ?? new List<LedgerDelta>())
            .Where(d => d != null && visible.Contains(d.Seq))
            .ToList();
        if (deltas.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Details already in this thread:");
            foreach (var delta in deltas)
            {
                var value = string.IsNullOrWhiteSpace(delta.Value) ? string.Empty : $" [{delta.Value.Trim()}]";
                sb.AppendLine($"msg {delta.Seq} {delta.Kind}: {delta.About.Trim()} - {delta.Statement.Trim()}{value}");
            }
        }

        var bodies = ancestors
            .Select(s => ctx.AcceptedThread?.Messages?.FirstOrDefault(m => m != null && m.Seq == s))
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Body))
            .Select(m => m!)
            .ToList();
        if (bodies.Count > 0)
        {
            var planned = (threadCtx.Thread.Messages ?? new List<PlannedMessage>()).Where(m => m != null).ToList();
            sb.AppendLine();
            sb.AppendLine("Earlier messages this email replies to:");
            foreach (var message in bodies)
            {
                var sender = planned.FirstOrDefault(p => p.Seq == message.Seq)?.FromId ?? string.Empty;
                sb.AppendLine($"msg {message.Seq} from {threadCtx.People.NameOf(sender)}: {message.Body.Trim()}");
            }
        }

        if (ctx.LateRepair)
        {
            sb.AppendLine();
            sb.AppendLine(LateRepairText);
        }
    }

    private static void AppendFillerContext(StringBuilder sb, AttachmentRenderContext ctx, CaseGuide guide)
    {
        var thread = ctx.Thread.Thread;
        if (ctx.IsDecoy || thread.Category == NonRelevantCategory.Decoy)
        {
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(thread.DecoyAngle))
                sb.AppendLine($"Angle: {thread.DecoyAngle.Trim()}");
            var terms = (thread.MustNotMention ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (terms.Count > 0)
                sb.AppendLine($"Do not mention: {string.Join("; ", terms)}");
            return;
        }

        if (thread.Category is not (NonRelevantCategory.DailyBusiness or NonRelevantCategory.Ancillary))
            return;

        var sender = ctx.Thread.People.Find(ctx.Parent.FromId);
        var organizationId = sender?.OrganizationId ?? string.Empty;
        var projects = (ctx.SideWorld?.Organizations ?? new List<SideWorldOrganization>())
            .Where(o => o != null && organizationId.Length > 0 && string.Equals(o.OrganizationId, organizationId, StringComparison.Ordinal))
            .SelectMany(o => o.Projects ?? new List<SideProject>())
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
            .ToList();
        if (projects.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Ongoing work:");
        foreach (var project in projects)
        {
            var topics = (project.Topics ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            sb.AppendLine(topics.Count > 0 ? $"{project.Name.Trim()}: {string.Join(", ", topics)}" : project.Name.Trim());
        }
    }

    private static void AppendChain(StringBuilder sb, AttachmentRenderContext ctx)
    {
        var chain = ctx.Chain;
        if (chain == null || chain.Count < 2 || chain.Version < 1)
            return;

        sb.AppendLine($"This is version {chain.Version} of {chain.Count} of this document from the same organization.");
        if (chain.Version == 1)
            return;

        // D-16: the prior version is that organization's own earlier document, sent before this one
        var prior = CutWords(ctx.PriorVersionText, MaxPriorVersionWords);
        if (prior.Length == 0)
        {
            sb.AppendLine(EarlierDraftNotAvailable);
            return;
        }

        sb.AppendLine("The earlier version (revise it; keep what still holds and change what the email says changed):");
        sb.AppendLine(prior);
    }

    private static void AppendInvite(StringBuilder sb, AttachmentRenderContext ctx)
    {
        var slot = ctx.Slot;
        if (slot != null)
        {
            var start = slot.Start.ToString("ddd, MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
            var end = slot.End.ToString("h:mm tt", CultureInfo.InvariantCulture);
            sb.AppendLine($"Meeting time (fixed): {start} - {end}");
        }

        var attendees = EvidencePrompts.Names(ctx.Thread, ctx.Parent.ToIds.Concat(ctx.Parent.CcIds));
        sb.AppendLine($"Organizer (fixed): {ctx.Thread.People.NameOf(ctx.Parent.FromId)}");
        if (attendees.Length > 0)
            sb.AppendLine($"Attendees (fixed): {attendees}");
    }

    internal static string SizeGuidance(AttachmentType kind, bool rich) => kind switch
    {
        AttachmentType.Word => rich
            ? "1-3 pages in 3-6 headed sections of 2-4 paragraphs; add a table only when figures call for one"
            : "about half a page in 1-2 sections",
        AttachmentType.Excel => rich ? "10-40 rows of 3-6 columns" : "4-10 rows of 2-4 columns",
        AttachmentType.PowerPoint => rich ? "4-8 slides of 3-5 bullets" : "2-3 slides of 2-4 bullets",
        _ => string.Empty
    };

    private static string TaskFor(AttachmentType kind, bool relevant)
    {
        var lists = relevant
            ? " List every person it names in peopleNamed, and every new figure, date, total or commitment it introduces in newDetails."
            : string.Empty;
        return kind switch
        {
            AttachmentType.Word => "Write the document: its title, headed sections of paragraphs and at most one table (empty headers for none)." + lists,
            AttachmentType.Excel => "Write the workbook: its title, a short sheet name, column headers, rows with one cell per header and an optional short note." + lists,
            AttachmentType.PowerPoint => "Write the deck: its title and content slides, each with a title and short bullets." + lists,
            AttachmentType.CalendarInvite => InviteTask,
            _ => "Write the document."
        };
    }

    private static string KindName(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => "Word document",
        AttachmentType.Excel => "Excel workbook",
        AttachmentType.PowerPoint => "PowerPoint deck",
        AttachmentType.CalendarInvite => "Calendar invite",
        AttachmentType.Image => "Image",
        _ => "Voicemail"
    };

    private static string CutWords(string? text, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text.Trim() : string.Join(" ", words.Take(maxWords));
    }

    // Person name tokens plus organization names and short names, longest first
    private static List<string> NameTokens(ThreadRenderContext ctx, CaseGuide guide) =>
        ctx.People.KnownNameTokens
            .Concat(ctx.People.All.Select(p => p.OrganizationName))
            .Concat((guide.Organizations ?? new List<CaseOrganization>()).Where(o => o != null).SelectMany(o => new[] { o.Name, o.ShortName }))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .ToList();

    // Removes every whole-word, case-insensitive occurrence of each token, then tidies spaces and stray punctuation
    private static string Scrub(string? text, IReadOnlyList<string> tokens)
    {
        var result = (text ?? string.Empty).Trim();
        foreach (var token in tokens)
            result = RemoveWord(result, token);

        var words = result.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(char.IsLetterOrDigit))
            .ToList();
        // Dangling connectors left where a name was ("photo for", "at")
        while (words.Count > 0 && IsConnector(words[^1]))
            words.RemoveAt(words.Count - 1);
        return string.Join(" ", words).TrimEnd('.', ',', ';', ':').Trim();
    }

    private static bool IsConnector(string word) =>
        word.TrimEnd('.', ',', ';', ':').ToLowerInvariant() is "for" or "at" or "from" or "by" or "of" or "with" or "and" or "to" or "the" or "'s";

    private static string RemoveWord(string text, string term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return text;

        var sb = new StringBuilder();
        var start = 0;
        while (start < text.Length)
        {
            var index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            var end = index + term.Length;
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after)
            {
                sb.Append(text, start, index - start).Append(' ');
                // Drop a possessive left behind ("Harmon's")
                if (end + 1 < text.Length && text[end] == '\'' && (text[end + 1] == 's' || text[end + 1] == 'S'))
                    end += 2;
                start = end;
            }
            else
            {
                sb.Append(text, start, index + 1 - start);
                start = index + 1;
            }
        }

        if (start < text.Length)
            sb.Append(text, start, text.Length - start);
        return sb.ToString();
    }
}
