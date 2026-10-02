using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Story-tier pass that seeds a real subject for every non-relevant thread (D-14): everyday slots (daily business, ancillary,
/// newsletters, spam, personal mail) see no case facts, issues or values; near-miss decoys see the project and organization
/// names plus an explicit must-not-mention list and are post-checked against it. The model returns text only (D-13).
/// </summary>
public partial class SubjectSeeder
{
    public const int SlotsPerBatch = 100;
    public const int SeedMaxOutputTokens = 8_000;
    public const int MaxParallelCalls = 3;

    private const string SeedTaskText = "Write a realistic subject line for each slot: the everyday work of these people, unrelated to any dispute. For external slots, also give the sending publication, company or person. Keep angle empty.";

    private const string DecoyTaskText = "Each slot is a near-miss: it mentions the same projects, companies and people as the matter, but it is about a routine, unrelated topic. Write a subject and one sentence on that unrelated topic (angle).";

    // Kinds of fact-linked canonical values a decoy must never mention (project names stay allowed)
    private static readonly HashSet<ValueKind> ForbiddenValueKinds = new()
    {
        ValueKind.Amount, ValueKind.InvoiceNumber, ValueKind.ChangeOrderNumber, ValueKind.ContractNumber, ValueKind.PurchaseOrder
    };

    private readonly ILlmGateway _llm;
    private readonly int _maxParallel;

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(SubjectSeedBatchDto) };

    public SubjectSeeder(ILlmGateway llm, int maxParallel = MaxParallelCalls)
    {
        _llm = llm;
        _maxParallel = Math.Max(1, maxParallel);
    }

    public const int MaxSubjectLength = 120;
    public const int MaxSenderNameLength = 60;
    public const int MaxDomainSlugLength = 30;

    /// <summary>
    /// Everyday topics for placeholder subjects (daily business, ancillary) and, prefixed with a project name, decoys.
    /// </summary>
    public static readonly string[] RoutineTopics =
    {
        "Team lunch Thursday", "Updated PTO calendar", "Parking passes for May", "Quarterly safety training",
        "Printer on 3 is down", "Expense report reminder", "Conference room booking", "New badge readers",
        "Holiday schedule", "Office move logistics", "IT password reset reminder", "Benefits enrollment deadline"
    };

    /// <summary>
    /// Seeds a subject for every non-relevant thread in place (and a decoy angle plus MustNotMention for decoys). Outside
    /// senders named by the model are renamed with safe, non-colliding domains (first seed per sender wins). Batches run in
    /// parallel (bounded); results are applied on the calling thread after all calls finish. Slots still missing (or decoys
    /// still mentioning a forbidden term) after one re-ask per batch get code templates and UsesFallbackContent (D-19).
    /// A fatal provider error returns with FatalError set; a user cancel throws OperationCanceledException (D-18).
    /// </summary>
    public async Task<FillResult> SeedAsync(CaseDraft draft, IReadOnlyList<PlannedThread> nonRelevant, List<ExternalParty> externals,
        IProgress<PlanProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        externals ??= new List<ExternalParty>();

        var batches = BuildBatches(nonRelevant);
        var n = batches.Count;
        var result = new FillResult { TotalChunks = n };
        if (n == 0)
            return result;

        var guide = draft.Guide ?? new CaseGuide();
        var mustNotMention = MustNotMention(guide);
        var state = new ApplyState(guide, externals, mustNotMention);

        // Build every prompt before any call so the draft is only read here
        var prompts = batches.Select(b => PromptFor(draft, b, externals, mustNotMention)).ToList();

        var refused = new bool[n];
        var errors = new string[n];
        Array.Fill(errors, string.Empty);

        using var run = new PlanCallRun(ct, _maxParallel, "Click Retry Planning.");

        // First pass: one call per batch; tasks only return DTOs and never touch the threads
        var firstTasks = new List<Task<PlanCallOutcome<SubjectSeedBatchDto>>>(n);
        for (var i = 0; i < n; i++)
        {
            var index = i;
            firstTasks.Add(PlanCallRunner.RunAsync<SubjectSeedBatchDto>(_llm, BatchRequest(prompts[index], index, n, retry: false), run,
                onStart: () => progress?.Report(new PlanProgress
                {
                    Stage = PlanStage.PlanningOther,
                    Message = $"Planning other emails ({index + 1} of {n})",
                    StageDone = index,
                    StageTotal = n
                })));
        }

        // A user cancel propagates from WhenAll before anything is applied to the threads
        var first = await Task.WhenAll(firstTasks);

        if (run.IsFatal)
        {
            result.FatalError = run.FatalError;
            return result;
        }

        for (var i = 0; i < n; i++)
            Record(first[i], batches[i], i, state, refused, errors, result);

        // Re-ask once (D-19): per batch, only the slots still missing or still forbidden; refused batches are not re-sent
        ct.ThrowIfCancellationRequested();
        var retries = new List<(int Index, List<(string SlotId, PlannedThread Thread)> Missing)>();
        for (var i = 0; i < n; i++)
        {
            if (refused[i])
                continue;
            var missing = batches[i].Where(s => !state.Seeded.Contains(s.SlotId)).ToList();
            if (missing.Count > 0)
                retries.Add((i, missing));
        }

        if (retries.Count > 0)
        {
            var retryTasks = retries
                .Select(r => PlanCallRunner.RunAsync<SubjectSeedBatchDto>(_llm,
                    BatchRequest(PromptFor(draft, r.Missing, externals, mustNotMention), r.Index, n, retry: true), run))
                .ToList();
            var second = await Task.WhenAll(retryTasks);

            if (run.IsFatal)
            {
                result.FatalError = run.FatalError;
                return result;
            }

            for (var k = 0; k < retries.Count; k++)
                Record(second[k], retries[k].Missing, retries[k].Index, state, refused, errors, result);
        }

        // Outcome counts and notes
        for (var i = 0; i < n; i++)
        {
            if (refused[i])
                result.RefusedChunks++;
            else if (errors[i].Length > 0)
            {
                result.FailedChunks++;
                result.Notes.Add($"Other emails part {i + 1} of {n} was not planned: {errors[i]}");
            }
        }

        // Templates for whatever the model still did not plan; every decoy carries the forbidden list for Phase 6
        var externalById = ExternalsById(externals);
        var templated = 0;
        foreach (var (slotId, thread) in batches.SelectMany(b => b))
        {
            if (thread.Category == NonRelevantCategory.Decoy)
                thread.MustNotMention = mustNotMention.ToList();

            if (state.Seeded.Contains(slotId))
                continue;

            thread.Subject = TemplateSubject(thread, guide, externalById, Random.Shared);
            if (thread.Category == NonRelevantCategory.Decoy)
                thread.DecoyAngle = "A routine update unrelated to any dispute.";
            thread.UsesFallbackContent = true;
            templated++;
        }

        if (templated == 1)
            result.Notes.Add("1 other email uses a placeholder subject because the model did not plan it.");
        else if (templated > 1)
            result.Notes.Add($"{templated} other emails use placeholder subjects because the model did not plan them.");

        return result;
    }

    /// <summary>
    /// Placeholder subject by category: daily business and ancillary "{routine topic}", decoys "{Project} {routine topic}",
    /// newsletters "{sender} weekly update", spam "Limited-time offer from {sender}", vendors "{sender} account update",
    /// personal mail "Weekend plans".
    /// </summary>
    public static string TemplateSubject(PlannedThread thread, CaseGuide guide, IReadOnlyDictionary<string, ExternalParty> externals, Random random)
    {
        var topic = RoutineTopics[random.Next(RoutineTopics.Length)];
        var fromId = thread.Messages.OrderBy(m => m.Seq).FirstOrDefault()?.FromId ?? string.Empty;
        if (externals.TryGetValue(fromId, out var external))
        {
            var sender = string.IsNullOrWhiteSpace(external.Name) ? "Our team" : external.Name.Trim();
            return external.Kind switch
            {
                ExternalKind.Newsletter => $"{sender} weekly update",
                ExternalKind.Spam => $"Limited-time offer from {sender}",
                ExternalKind.Vendor => $"{sender} account update",
                _ => "Weekend plans"
            };
        }

        switch (thread.Category)
        {
            case NonRelevantCategory.NewsletterSpamPersonal:
                return "Weekend plans";
            case NonRelevantCategory.Decoy:
                var project = ProjectNames(guide ?? new CaseGuide()).FirstOrDefault();
                return project != null ? $"{project} {char.ToLowerInvariant(topic[0])}{topic[1..]}" : topic;
            default:
                return topic;
        }
    }

    private static string PromptFor(CaseDraft draft, IReadOnlyList<(string SlotId, PlannedThread Thread)> slots, IReadOnlyList<ExternalParty> externals,
        IReadOnlyList<string> mustNotMention) =>
        slots.Count > 0 && slots[0].Thread.Category == NonRelevantCategory.Decoy
            ? BuildDecoyPrompt(draft, slots, mustNotMention)
            : BuildSeedPrompt(draft, slots, externals);

    private static LlmRequest BatchRequest(string prompt, int index, int total, bool retry) => new()
    {
        Tier = LlmTier.Story,
        OperationName = $"Corpus Plan - Other email (batch {index + 1} of {total})" + (retry ? " retry" : string.Empty),
        SystemPrompt = CorpusPlanPrompts.SystemPrompt,
        UserPrompt = prompt,
        MaxOutputTokens = SeedMaxOutputTokens
    };

    // Per-run apply state; only touched on the calling thread after Task.WhenAll
    private sealed class ApplyState
    {
        public ApplyState(CaseGuide guide, List<ExternalParty> externals, List<string> mustNotMention)
        {
            Externals = externals;
            ExternalById = ExternalsById(externals);
            MustNotMention = mustNotMention;

            foreach (var org in guide.Organizations ?? new List<CaseOrganization>())
            {
                if (org == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(org.Domain))
                    CaseDomains.Add(org.Domain.Trim());
                if (!string.IsNullOrWhiteSpace(org.Name))
                    CaseNames.Add(org.Name.Trim());
                if (!string.IsNullOrWhiteSpace(org.ShortName))
                    CaseNames.Add(org.ShortName.Trim());
            }

            foreach (var member in guide.Cast ?? new List<CastMember>())
            {
                if (member == null)
                    continue;
                var domain = EmailIdentity.DomainOf(member.Email);
                if (domain.Length > 0)
                    CaseDomains.Add(domain);
                if (!string.IsNullOrWhiteSpace(member.FullName))
                    CaseNames.Add(member.FullName);
            }
        }

        public List<ExternalParty> Externals { get; }
        public Dictionary<string, ExternalParty> ExternalById { get; }
        public List<string> MustNotMention { get; }
        public HashSet<string> Seeded { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Renamed { get; } = new(StringComparer.Ordinal);
        public HashSet<string> CaseDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CaseNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Applies one outcome to the requested slots and records refusal/error state for the batch
    private static void Record(PlanCallOutcome<SubjectSeedBatchDto> outcome, IReadOnlyList<(string SlotId, PlannedThread Thread)> requested,
        int index, ApplyState state, bool[] refused, string[] errors, FillResult result)
    {
        if (outcome.Value != null)
        {
            errors[index] = string.Empty;
            ApplyBatch(outcome.Value, requested, state);
            return;
        }

        if (outcome.Refused)
        {
            refused[index] = true;
            if (result.RefusalMessage.Length == 0)
                result.RefusalMessage = outcome.RefusalMessage;
            return;
        }

        errors[index] = outcome.Error.Length > 0 ? outcome.Error : "The request did not return subjects.";
    }

    /// <summary>
    /// Maps seeds strictly by canonical slot id onto the requested slots (unknown and repeated ids dropped), strips id tokens
    /// and caps the text (T-04-43). A decoy seed with a blank angle or any must-not-mention term is not applied (T-04-41).
    /// </summary>
    private static void ApplyBatch(SubjectSeedBatchDto batch, IReadOnlyList<(string SlotId, PlannedThread Thread)> requested, ApplyState state)
    {
        var bySlot = requested.ToDictionary(s => s.SlotId, s => s.Thread, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dto in batch.Seeds ?? new List<SubjectSeedDto>())
        {
            if (dto == null)
                continue;
            var slotId = (dto.SlotId ?? string.Empty).Trim().ToUpperInvariant();
            if (!bySlot.TryGetValue(slotId, out var thread) || !seen.Add(slotId) || state.Seeded.Contains(slotId))
                continue;

            var subject = Cap(StripReplyPrefix(Clean(dto.Subject)), MaxSubjectLength);
            if (subject.Length == 0)
                continue;

            if (thread.Category == NonRelevantCategory.Decoy)
            {
                var angle = Clean(dto.Angle);
                if (angle.Length == 0 || MentionsForbidden(subject, state.MustNotMention) || MentionsForbidden(angle, state.MustNotMention))
                    continue;
                thread.DecoyAngle = angle;
            }

            thread.Subject = subject;
            state.Seeded.Add(slotId);

            var fromId = thread.Messages.OrderBy(m => m.Seq).FirstOrDefault()?.FromId ?? string.Empty;
            if (state.ExternalById.TryGetValue(fromId, out var external) && !state.Renamed.Contains(external.Id))
            {
                if (RenameExternal(external, dto.SenderName, state))
                    state.Renamed.Add(external.Id);
            }
        }
    }

    internal static bool MentionsForbidden(string text, IReadOnlyList<string> mustNotMention) =>
        mustNotMention.Any(term => term.Length > 0 && text.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Renames an outside sender from the model's sender name. Business senders get "{mailbox}@{slug}.com" (slug folded to
    /// [a-z0-9], at most 30 characters; "mail" appended on a collision with a case, reserved or other external domain);
    /// personal contacts keep their consumer domain with "{first}.{last}". Names matching a case organization or person,
    /// or with nothing usable, keep the existing identity (T-04-42).
    /// </summary>
    private static bool RenameExternal(ExternalParty external, string? senderName, ApplyState state)
    {
        var name = Cap(Clean(senderName), MaxSenderNameLength);
        if (name.Length == 0 || state.CaseNames.Contains(name))
            return false;

        var otherAddresses = state.Externals
            .Where(e => !ReferenceEquals(e, external) && !string.IsNullOrWhiteSpace(e.Email))
            .Select(e => e.Email.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (external.Kind == ExternalKind.Personal)
        {
            var tokens = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || EmailIdentity.Fold(tokens[0]).Length == 0 || EmailIdentity.Fold(tokens[^1]).Length == 0)
                return false;
            var domain = string.IsNullOrWhiteSpace(external.Domain) ? NonRelevantSkeletonBuilder.PersonalDomains[0] : external.Domain.Trim();
            if (state.CaseDomains.Contains(domain))
                return false;
            external.Email = EmailIdentity.AddressFor(tokens[0], tokens[^1], domain, otherAddresses);
            external.Domain = domain;
            external.Name = name;
            return true;
        }

        var slug = EmailIdentity.Fold(name);
        if (slug.Length > MaxDomainSlugLength)
            slug = slug[..MaxDomainSlugLength];
        if (slug.Length == 0)
            return false;

        var taken = new HashSet<string>(state.CaseDomains, StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(NonRelevantSkeletonBuilder.PersonalDomains);
        taken.UnionWith(state.Externals.Where(e => !ReferenceEquals(e, external) && !string.IsNullOrWhiteSpace(e.Domain)).Select(e => e.Domain.Trim()));
        bool Unavailable(string d) => taken.Contains(d) || EmailIdentity.IsReservedDomain(d);

        var result = slug + ".com";
        if (Unavailable(result))
            result = slug + "mail.com";
        for (var k = 2; Unavailable(result); k++)
            result = slug + "mail" + k.ToString(CultureInfo.InvariantCulture) + ".com";

        var at = (external.Email ?? string.Empty).IndexOf('@');
        var mailbox = at > 0 ? EmailIdentity.Fold(external.Email![..at]) : string.Empty;
        if (mailbox.Length == 0)
            mailbox = external.Kind == ExternalKind.Newsletter ? "news" : "offers";

        external.Name = name;
        external.Domain = result;
        external.Email = mailbox + "@" + result;
        return true;
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var stripped = IdTokenPattern().Replace(text, string.Empty);
        return WhitespacePattern().Replace(stripped, " ").Trim();
    }

    private static string StripReplyPrefix(string subject)
    {
        var cleaned = ReplyPrefixPattern().Replace(subject, string.Empty).Trim();
        return cleaned.Length > 0 ? cleaned : subject;
    }

    private static string Cap(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var cut = text[..max];
        // Never split a surrogate pair at the cap
        if (char.IsHighSurrogate(cut[^1]))
            cut = cut[..^1];
        return cut.TrimEnd();
    }

    [GeneratedRegex(@"\b(?:CH|F|ISS|T|N|V|ORG|EXT)-\d{2,4}\b")]
    private static partial Regex IdTokenPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^(?:(?:RE|FW|FWD)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefixPattern();

    /// <summary>
    /// Terms a decoy subject or angle must never contain: every fact title and every Amount / InvoiceNumber /
    /// ChangeOrderNumber / ContractNumber / PurchaseOrder value linked to a fact. Project and organization names are
    /// deliberately allowed (decoys mention them). Distinct, case-insensitive, in guide order.
    /// </summary>
    public static List<string> MustNotMention(CaseGuide guide)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? term)
        {
            var trimmed = (term ?? string.Empty).Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
                result.Add(trimmed);
        }

        foreach (var fact in guide?.Facts ?? new List<KeyFact>())
            Add(fact?.Title);

        foreach (var value in guide?.Values ?? new List<CanonicalValue>())
        {
            if (value != null && ForbiddenValueKinds.Contains(value.Kind) && value.FactIds.Any(id => !string.IsNullOrWhiteSpace(id)))
                Add(value.Value);
        }

        return result;
    }

    /// <summary>
    /// Slot ids S-0001.. in thread order, then batches of at most <see cref="SlotsPerBatch"/>: everyday slots first, then
    /// decoys, which never share a batch with everyday slots (different prompts).
    /// </summary>
    internal static List<List<(string SlotId, PlannedThread Thread)>> BuildBatches(IReadOnlyList<PlannedThread> nonRelevant)
    {
        var slots = nonRelevant.Select((t, i) => (SlotId: $"S-{i + 1:0000}", Thread: t)).ToList();
        var batches = new List<List<(string SlotId, PlannedThread Thread)>>();
        batches.AddRange(slots.Where(s => s.Thread.Category != NonRelevantCategory.Decoy).Chunk(SlotsPerBatch).Select(c => c.ToList()));
        batches.AddRange(slots.Where(s => s.Thread.Category == NonRelevantCategory.Decoy).Chunk(SlotsPerBatch).Select(c => c.ToList()));
        return batches;
    }

    /// <summary>
    /// Everyday seeding request: name rules (without the case scenario), one line per slot with only that slot's sender and
    /// recipients, and the task. No facts, issues or canonical values ever reach this prompt (D-14).
    /// </summary>
    internal static string BuildSeedPrompt(CaseDraft draft, IReadOnlyList<(string SlotId, PlannedThread Thread)> slots, IReadOnlyList<ExternalParty> externals)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var externalById = ExternalsById(externals);

        var sb = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(sb, draft, includeScenario: false);
        sb.AppendLine();
        sb.AppendLine("Slots:");
        foreach (var (slotId, thread) in slots)
            sb.AppendLine(SlotLine(guide, slotId, thread, externalById));
        sb.AppendLine();
        sb.AppendLine(SeedTaskText);
        return sb.ToString();
    }

    /// <summary>
    /// Near-miss decoy request: name rules, the project names (ProjectName values) and organization names, the slot lines,
    /// the task and the must-not-mention list.
    /// </summary>
    internal static string BuildDecoyPrompt(CaseDraft draft, IReadOnlyList<(string SlotId, PlannedThread Thread)> slots, IReadOnlyList<string> mustNotMention)
    {
        var guide = draft.Guide ?? new CaseGuide();

        var sb = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(sb, draft);

        var projects = ProjectNames(guide);
        var organizations = guide.Organizations
            .Select(o => o?.Name?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        sb.AppendLine();
        if (projects.Count > 0)
            sb.AppendLine($"Projects: {string.Join("; ", projects)}");
        if (organizations.Count > 0)
            sb.AppendLine($"Companies and organizations: {string.Join("; ", organizations)}");

        sb.AppendLine();
        sb.AppendLine("Slots:");
        var noExternals = new Dictionary<string, ExternalParty>(StringComparer.Ordinal);
        foreach (var (slotId, thread) in slots)
            sb.AppendLine(SlotLine(guide, slotId, thread, noExternals));

        sb.AppendLine();
        sb.AppendLine(DecoyTaskText);
        if (mustNotMention.Count > 0)
        {
            sb.AppendLine("Do not mention any of these:");
            foreach (var term in mustNotMention)
                sb.AppendLine($"- {term}");
        }

        return sb.ToString();
    }

    // "S-0001 | Daily business | from CH-002 Tom Becker, Project Manager, Brightline Glazing, LLC | to CH-009 Owen Reilly | Mar 2024 | 3 messages | attachments: Word"
    private static string SlotLine(CaseGuide guide, string slotId, PlannedThread thread, IReadOnlyDictionary<string, ExternalParty> externalById)
    {
        var root = thread.Messages.OrderBy(m => m.Seq).FirstOrDefault();
        var fromId = root?.FromId ?? string.Empty;
        var external = externalById.TryGetValue(fromId, out var party) ? party : null;

        var category = external != null ? KindText(external.Kind) : CategoryText(thread.Category);
        var sender = external != null ? $"external: {KindText(external.Kind).ToLowerInvariant()}" : $"from {DescribeSender(guide, fromId)}";
        var to = root != null && root.ToIds.Count > 0 ? string.Join("; ", root.ToIds.Select(id => PersonName(guide, id))) : "-";
        var month = root != null ? LocalTime(root).ToString("MMM yyyy", CultureInfo.InvariantCulture) : "-";
        var count = thread.Messages.Count == 1 ? "1 message" : $"{thread.Messages.Count} messages";
        var kinds = thread.Messages.SelectMany(m => m.Attachments).Select(a => a.Kind.ToString()).Distinct(StringComparer.Ordinal).ToList();
        var attachments = kinds.Count > 0 ? string.Join(", ", kinds) : "none";
        return $"{slotId} | {category} | {sender} | to {to} | {month} | {count} | attachments: {attachments}";
    }

    private static string CategoryText(NonRelevantCategory category) => category switch
    {
        NonRelevantCategory.DailyBusiness => "Daily business",
        NonRelevantCategory.Ancillary => "Ancillary",
        NonRelevantCategory.Decoy => "Near-miss",
        NonRelevantCategory.NewsletterSpamPersonal => "Personal",
        _ => "Daily business"
    };

    private static string KindText(ExternalKind kind) => kind switch
    {
        ExternalKind.Newsletter => "Newsletter",
        ExternalKind.Vendor => "Vendor",
        ExternalKind.Spam => "Spam",
        _ => "Personal"
    };

    // "CH-002 Tom Becker, Project Manager, Brightline Glazing, LLC" (department after the title when set)
    private static string DescribeSender(CaseGuide guide, string id)
    {
        var member = guide.Cast.FirstOrDefault(c => c.Id == id);
        if (member == null || string.IsNullOrWhiteSpace(member.FullName))
            return id;
        var parts = new List<string> { $"{id} {member.FullName}" };
        if (!string.IsNullOrWhiteSpace(member.Title))
            parts.Add(member.Title.Trim());
        if (!string.IsNullOrWhiteSpace(member.Department))
            parts.Add(member.Department.Trim());
        var org = guide.FindOrganization(member.OrganizationId)?.Name;
        if (!string.IsNullOrWhiteSpace(org))
            parts.Add(org.Trim());
        return string.Join(", ", parts);
    }

    private static string PersonName(CaseGuide guide, string id)
    {
        var member = guide.Cast.FirstOrDefault(c => c.Id == id);
        return member != null && !string.IsNullOrWhiteSpace(member.FullName) ? $"{id} {member.FullName}" : id;
    }

    private static List<string> ProjectNames(CaseGuide guide) => guide.Values
        .Where(v => v != null && v.Kind == ValueKind.ProjectName)
        .Select(v => v.Value?.Trim() ?? string.Empty)
        .Where(v => v.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static Dictionary<string, ExternalParty> ExternalsById(IEnumerable<ExternalParty> externals) => externals
        .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id))
        .GroupBy(e => e.Id, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    // Sender-local clock time: the stored offset is sender-local, converted again when the zone id resolves
    private static DateTimeOffset LocalTime(PlannedMessage message)
    {
        var tz = BusinessCalendar.ResolveId(message.TimeZoneId);
        return tz != null ? TimeZoneInfo.ConvertTime(message.SentAt, tz) : message.SentAt;
    }

    // ----- LLM DTOs (strict-schema safe: text only, no dates, people, counts or labels) -----

    internal class SubjectSeedBatchDto
    {
        [JsonPropertyName("seeds")]
        [Description("One entry per slot listed in the request, using its slotId")]
        public List<SubjectSeedDto> Seeds { get; set; } = new();
    }

    internal class SubjectSeedDto
    {
        [JsonPropertyName("slotId")]
        [Description("Slot ID from the request")]
        public string SlotId { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        [Description("Realistic subject line")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("angle")]
        [Description("For decoy slots: one sentence on the unrelated topic; otherwise empty string")]
        public string Angle { get; set; } = string.Empty;

        [JsonPropertyName("senderName")]
        [Description("For external slots: the sending publication, company or person; otherwise empty string")]
        public string SenderName { get; set; } = string.Empty;
    }
}
