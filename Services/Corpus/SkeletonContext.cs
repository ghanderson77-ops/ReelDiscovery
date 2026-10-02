using System.Collections.Concurrent;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Null-cleaned lookups shared by the relevant and non-relevant skeleton builders, the thread scheduler and the plan
/// validator: cast, organizations, sender zones, active ranges, corpus window, enabled media and the injected Random.
/// </summary>
public class SkeletonContext
{
    private readonly Dictionary<string, CaseOrganization> _organizationById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TimeZoneInfo> _zoneCache = new(StringComparer.Ordinal);

    private SkeletonContext(CaseDraft draft, CaseGuide guide, CorpusSettings settings, Random random)
    {
        Draft = draft;
        Guide = guide;
        Settings = settings;
        Random = random;
    }

    public CaseDraft Draft { get; }
    public CaseGuide Guide { get; }
    public CorpusSettings Settings { get; }
    public Random Random { get; }
    public IReadOnlyList<AttachmentType> EnabledTypes { get; private set; } = Array.Empty<AttachmentType>();
    public int AttachmentPercent { get; private set; }
    public DateOnly WindowStart { get; private set; }
    public DateOnly WindowEnd { get; private set; }

    // First entry wins for duplicated ids
    public Dictionary<string, CastMember> CastById { get; } = new(StringComparer.Ordinal);

    // Facts with a valid yyyy-MM-dd date, date-sorted with ordinal id tie-break
    public List<KeyFact> DatedFacts { get; private set; } = new();

    // Planner notes surfaced on the plan (gaps, fallbacks)
    public List<string> Notes { get; } = new();

    /// <summary>
    /// Builds the context; never throws on a partial guide. Production passes Random.Shared (D-20).
    /// </summary>
    public static SkeletonContext Create(CaseDraft draft, CorpusSettings settings, bool mediaAvailable, Random random)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var ctx = new SkeletonContext(draft, guide, settings, random);

        foreach (var org in (guide.Organizations ?? new()).Where(o => o != null && !string.IsNullOrEmpty(o.Id)))
            ctx._organizationById.TryAdd(org.Id, org);
        foreach (var member in (guide.Cast ?? new()).Where(c => c != null && !string.IsNullOrEmpty(c.Id)))
            ctx.CastById.TryAdd(member.Id, member);

        ctx.DatedFacts = (guide.Facts ?? new())
            .Where(f => f != null && CaseDraft.TryParseDate(f.Date, out _))
            .OrderBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        ctx.EnabledTypes = settings.EnabledAttachmentTypes(mediaAvailable);
        ctx.AttachmentPercent = settings.AttachmentPercent ?? ThreadShapes.DefaultAttachmentPercent;
        ctx.ResolveWindow();
        return ctx;
    }

    /// <summary>
    /// Fact date as DateOnly, or null when missing or invalid.
    /// </summary>
    public static DateOnly? FactDate(KeyFact fact) =>
        CaseDraft.TryParseDate(fact?.Date, out var date) ? DateOnly.FromDateTime(date) : null;

    /// <summary>
    /// Active on <paramref name="day"/>: empty or invalid ActiveFrom/ActiveTo = unknown = unbounded; ids outside the cast
    /// (external parties) are always active.
    /// </summary>
    public bool IsActive(string castId, DateOnly day)
    {
        if (string.IsNullOrEmpty(castId) || !CastById.TryGetValue(castId, out var member))
            return true;

        if (CaseDraft.TryParseDate(member.ActiveFrom, out var from) && day < DateOnly.FromDateTime(from))
            return false;
        if (CaseDraft.TryParseDate(member.ActiveTo, out var to) && day > DateOnly.FromDateTime(to))
            return false;
        return true;
    }

    public CaseOrganization? OrganizationFor(string personId) =>
        !string.IsNullOrEmpty(personId) && CastById.TryGetValue(personId, out var member)
        && !string.IsNullOrEmpty(member.OrganizationId) && _organizationById.TryGetValue(member.OrganizationId, out var org)
            ? org
            : null;

    /// <summary>
    /// Sender zone: cast -> their organization's zone (BusinessCalendar.Resolve); external (EXT-) and unknown ids ->
    /// UsTimeZones.Default. The single external-sender zone contract for the scheduler and the plan validator.
    /// </summary>
    public TimeZoneInfo ZoneFor(string personId) =>
        _zoneCache.GetOrAdd(personId ?? string.Empty, id =>
        {
            if (CastById.ContainsKey(id))
                return BusinessCalendar.Resolve(OrganizationFor(id));
            return BusinessCalendar.ResolveId(UsTimeZones.Default) ?? TimeZoneInfo.Utc;
        });

    /// <summary>
    /// True when the two people do not share an organization (externals and unknown ids count as external).
    /// </summary>
    public bool IsExternalTo(string a, string b)
    {
        if (a == b)
            return false;

        var orgA = CastById.TryGetValue(a ?? string.Empty, out var ma) ? ma.OrganizationId ?? string.Empty : string.Empty;
        var orgB = CastById.TryGetValue(b ?? string.Empty, out var mb) ? mb.OrganizationId ?? string.Empty : string.Empty;
        return orgA.Length == 0 || orgB.Length == 0 || !string.Equals(orgA, orgB, StringComparison.Ordinal);
    }

    private void ResolveWindow()
    {
        var hasStart = CaseDraft.TryParseDate(Settings.WindowStart, out var start);
        var hasEnd = CaseDraft.TryParseDate(Settings.WindowEnd, out var end);

        if (!hasStart || !hasEnd)
        {
            var (defaultStart, defaultEnd) = CorpusSettings.DefaultWindow(Guide);
            if (!hasStart)
                hasStart = CaseDraft.TryParseDate(defaultStart, out start);
            if (!hasEnd)
                hasEnd = CaseDraft.TryParseDate(defaultEnd, out end);
        }

        if (!hasStart && !hasEnd)
        {
            // No window and no dated facts: plan over the last three months
            end = DateTime.Today;
            start = end.AddMonths(-3);
            Notes.Add("No corpus window or dated key facts; using the last three months.");
        }
        else if (!hasStart)
        {
            start = end.AddMonths(-3);
        }
        else if (!hasEnd)
        {
            end = start.AddMonths(3);
        }

        if (end < start)
            (start, end) = (end, start);

        WindowStart = DateOnly.FromDateTime(start);
        WindowEnd = DateOnly.FromDateTime(end);
    }
}
