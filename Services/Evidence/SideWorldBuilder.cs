using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

public class SideWorldOutcome
{
    // Null only when a fatal provider stop (bad key, spend limit) ended the run
    public SideWorld? World { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    // Gateway cost of the call (PROV-07); the runner journals it as is
    public CostBreakdown Cost { get; set; } = new();
}

/// <summary>
/// The per-organization side world (D-21): recurring non-case projects and topics that daily-business and ancillary
/// filler draws from, built by one Volume-tier call with validated code templates as the fallback. Side projects must
/// not collide with case facts or terms (NOISE-01, NOISE-02).
/// </summary>
internal static class SideWorldBuilder
{
    public const string OperationName = "Generate Corpus - side projects";
    public const int MaxOutputTokens = 8_000;
    public const int MaxPromptTerms = 60;
    public const int MinProjects = 2;
    public const int TemplateCount = 3;

    public const string SystemPrompt =
        "You invent ordinary, recurring business work for the organizations of an e-discovery training dataset: other clients, accounts, internal initiatives and routine programs that have nothing to do with any dispute. Keep each organization exactly as named, including companies from films, TV series, books or games. Names are short and plausible; topics are routine (scheduling, budgets, vendors, staffing, renewals).";

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(SideWorldDto) };

    // {0} = organization short name; generic business nouns only, validated against case terms before use
    private static readonly (string Name, string[] Topics)[] Templates =
    {
        ("{0} Q3 Supplier Review", new[] { "supplier scorecards", "review meeting scheduling", "renewal terms" }),
        ("Facilities Refresh", new[] { "furniture order", "contractor walkthrough", "budget sign-off" }),
        ("Key Account Renewal", new[] { "renewal pricing", "account review call", "contract paperwork" }),
        ("Office Move Planning", new[] { "moving vendor quotes", "seating plan", "IT cutover weekend" }),
        ("{0} Annual Budget Cycle", new[] { "department budget requests", "headcount planning", "forecast review" }),
        ("Staff Training Program", new[] { "training calendar", "course enrollment", "trainer invoices" }),
        ("IT Systems Upgrade", new[] { "laptop replacement", "software licenses", "help desk schedule" }),
        ("{0} Benefits Enrollment", new[] { "open enrollment dates", "plan comparison", "HR reminders" })
    };

    /// <summary>
    /// The organizations that get a persisted side world: the first SideWorld.MaxOrganizations by ordinal id.
    /// </summary>
    internal static List<CaseOrganization> CoveredOrganizations(CaseGuide guide) =>
        (guide.Organizations ?? new())
            .Where(o => o != null && !string.IsNullOrWhiteSpace(o.Id))
            .OrderBy(o => o.Id.Trim(), StringComparer.Ordinal)
            .Take(SideWorld.MaxOrganizations)
            .ToList();

    /// <summary>
    /// Every case term a side project must avoid: fact titles, issue titles, every canonical value (all kinds), cast full
    /// names and case project names.
    /// </summary>
    internal static HashSet<string> CaseTerms(CaseGuide guide)
    {
        // Validation uses every term; only the prompt list is capped (review 06-12)
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in OrderedTerms(guide))
            terms.Add(term);
        return terms;
    }

    // Most specific first: project names, other canonical values, people, then fact and issue titles
    private static IEnumerable<string> OrderedTerms(CaseGuide guide)
    {
        var values = (guide.Values ?? new()).Where(v => v != null).ToList();
        var ordered = values.Where(v => v.Kind == ValueKind.ProjectName).Select(v => v.Value)
            .Concat(values.Where(v => v.Kind != ValueKind.ProjectName).Select(v => v.Value))
            .Concat((guide.Cast ?? new()).Where(c => c != null).Select(c => c.FullName))
            .Concat((guide.Facts ?? new()).Where(f => f != null).Select(f => f.Title))
            .Concat((guide.Issues ?? new()).Where(i => i != null).Select(i => i.Title));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ordered)
        {
            var term = raw?.Trim() ?? string.Empty;
            if (term.Length > 0 && seen.Add(term))
                yield return term;
        }
    }

    internal static bool Collides(string? text, IReadOnlySet<string> terms)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        foreach (var term in terms)
        {
            if (RenderValidator.ContainsWord(value, term) || RenderValidator.ContainsWord(term, value))
                return true;
        }

        return false;
    }

    internal static LlmRequest BuildRequest(CaseDraft draft, CaseGuide guide)
    {
        var sb = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(sb, draft, includeScenario: false);
        sb.AppendLine();
        sb.AppendLine("Organizations (keep each id exactly as given):");
        foreach (var org in CoveredOrganizations(guide))
        {
            var details = string.Join(", ", new[] { org.Kind.ToString(), org.Location?.Trim() ?? string.Empty }.Where(s => s.Length > 0));
            sb.AppendLine($"{org.Id.Trim()} {org.Name?.Trim()} ({org.Domain?.Trim()}) - {details}");
        }

        sb.AppendLine();
        sb.AppendLine("Give each organization 3 or 4 projects with 2-4 topics each.");

        var promptTerms = OrderedTerms(guide).Take(MaxPromptTerms).ToList();
        if (promptTerms.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Do not use these names or terms:");
            foreach (var term in promptTerms)
                sb.AppendLine($"- {term}");
        }

        return new LlmRequest
        {
            Tier = LlmTier.Volume,
            OperationName = OperationName,
            SystemPrompt = SystemPrompt,
            UserPrompt = sb.ToString(),
            MaxOutputTokens = MaxOutputTokens
        };
    }

    public static async Task<SideWorldOutcome> BuildAsync(ILlmGateway llm, CaseDraft draft, CaseGuide guide, PlanCallRun run)
    {
        var outcome = await PlanCallRunner.RunAsync<SideWorldDto>(llm, BuildRequest(draft, guide), run);
        var result = new SideWorldOutcome
        {
            InputTokens = outcome.InputTokens,
            OutputTokens = outcome.OutputTokens,
            Cost = outcome.Cost.Clone()
        };

        // Fatal stop: leave the side world unset so the run ends without a fallback record
        if (run.IsFatal && outcome.Value == null)
            return result;

        result.World = outcome.Value == null ? Fallback(guide) : FromDto(outcome.Value, guide);
        return result;
    }

    private static SideWorld FromDto(SideWorldDto dto, CaseGuide guide)
    {
        var terms = CaseTerms(guide);
        var byId = new Dictionary<string, SideWorldOrgDto>(StringComparer.Ordinal);
        foreach (var item in dto.Organizations ?? new())
        {
            var id = item?.OrganizationId?.Trim() ?? string.Empty;
            if (item != null && id.Length > 0)
                byId.TryAdd(id, item);
        }

        var world = new SideWorld();
        var modelProjects = 0;
        foreach (var org in CoveredOrganizations(guide))
        {
            var projects = new List<SideProject>();
            if (byId.TryGetValue(org.Id.Trim(), out var item))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var project in item.Projects ?? new())
                {
                    var name = project?.Name?.Trim() ?? string.Empty;
                    if (project == null || name.Length == 0 || Collides(name, terms) || !names.Add(name))
                        continue;

                    var topics = (project.Topics ?? new())
                        .Select(t => t?.Trim() ?? string.Empty)
                        .Where(t => t.Length > 0 && !Collides(t, terms))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(SideWorld.MaxTopics)
                        .ToList();
                    if (topics.Count == 0)
                        continue;

                    projects.Add(new SideProject { Name = name, Topics = topics });
                    if (projects.Count >= SideWorld.MaxProjects)
                        break;
                }

                modelProjects += projects.Count;
            }

            if (projects.Count == 0)
            {
                // Organization missing from the response or every project collided
                projects = TemplatesFor(org, terms);
            }
            else if (projects.Count < MinProjects)
            {
                foreach (var template in TemplatesFor(org, terms))
                {
                    if (projects.Count >= MinProjects)
                        break;
                    if (!projects.Any(p => string.Equals(p.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
                        projects.Add(template);
                }
            }

            world.Organizations.Add(new SideWorldOrganization { OrganizationId = org.Id.Trim(), Projects = projects });
        }

        // No usable project from the model at all: the whole world is code templates
        if (modelProjects == 0)
            return Fallback(guide);

        world.Normalize();
        return world;
    }

    /// <summary>
    /// Every covered organization from validated code templates (FromFallback = true).
    /// </summary>
    internal static SideWorld Fallback(CaseGuide guide)
    {
        var terms = CaseTerms(guide);
        var world = new SideWorld { FromFallback = true };
        foreach (var org in CoveredOrganizations(guide))
            world.Organizations.Add(new SideWorldOrganization { OrganizationId = org.Id.Trim(), Projects = TemplatesFor(org, terms) });
        world.Normalize();
        return world;
    }

    /// <summary>
    /// The first TemplateCount fixed templates, in StableRank order per organization, whose name and topics avoid every
    /// case term. Deterministic per organization id.
    /// </summary>
    internal static List<SideProject> TemplatesFor(CaseOrganization org, IReadOnlySet<string> terms)
    {
        var shortName = ShortName(org);
        var id = org.Id?.Trim() ?? string.Empty;
        var result = new List<SideProject>();
        foreach (var index in Enumerable.Range(0, Templates.Length).OrderBy(i => RenderAssignments.StableRank(id + ":side" + i)).ThenBy(i => i))
        {
            var (pattern, topics) = Templates[index];
            var name = string.Format(pattern, shortName).Trim();
            if (Collides(name, terms) || topics.Any(t => Collides(t, terms)))
                continue;

            result.Add(new SideProject { Name = name, Topics = topics.ToList() });
            if (result.Count >= TemplateCount)
                break;
        }

        return result;
    }

    private static string ShortName(CaseOrganization org)
    {
        if (!string.IsNullOrWhiteSpace(org.ShortName))
            return org.ShortName.Trim();
        var name = org.Name?.Trim() ?? string.Empty;
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space].TrimEnd(',') : name;
    }

    /// <summary>
    /// The side projects filler uses for an organization: the persisted ones, else validated templates (organizations
    /// past the cap or missing from the world).
    /// </summary>
    public static List<SideProject> ProjectsFor(SideWorld? world, CaseOrganization org, CaseGuide guide)
    {
        var id = org.Id?.Trim() ?? string.Empty;
        var persisted = world?.Organizations?.FirstOrDefault(o => o != null && o.OrganizationId == id);
        if (persisted != null && persisted.Projects != null && persisted.Projects.Count > 0)
            return persisted.Projects;

        return TemplatesFor(org, CaseTerms(guide));
    }

    /// <summary>
    /// The journaled side world when it lists every covered guide organization, else null (forces a rebuild). An
    /// organization with no projects still counts: every template may collide with case terms, and
    /// <see cref="ProjectsFor"/> falls back for it, so such a world is never rebuilt on each Continue (IN-03).
    /// </summary>
    public static SideWorld? FromRecord(JournalRecord? record, CaseGuide guide)
    {
        var world = record?.SideWorld;
        if (world == null)
            return null;

        world.Normalize();
        var present = new HashSet<string>(world.Organizations.Select(o => o.OrganizationId), StringComparer.Ordinal);
        return CoveredOrganizations(guide).All(o => present.Contains(o.Id.Trim())) ? world : null;
    }

    // ----- strict-schema DTOs -----

    internal class SideWorldDto
    {
        [JsonPropertyName("organizations")]
        [Description("One entry per organization in the request")]
        public List<SideWorldOrgDto> Organizations { get; set; } = new();
    }

    internal class SideWorldOrgDto
    {
        [JsonPropertyName("organizationId")]
        [Description("Organization id exactly as given, for example ORG-01")]
        public string OrganizationId { get; set; } = string.Empty;

        [JsonPropertyName("projects")]
        [Description("3 or 4 recurring projects, accounts or initiatives unrelated to any dispute")]
        public List<SideProjectDto> Projects { get; set; } = new();
    }

    internal class SideProjectDto
    {
        [JsonPropertyName("name")]
        [Description("Short plausible project or account name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("topics")]
        [Description("2-4 routine topics, for example scheduling, budgets, vendors, staffing or renewals")]
        public List<string> Topics { get; set; } = new();
    }
}
