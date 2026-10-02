using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
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
