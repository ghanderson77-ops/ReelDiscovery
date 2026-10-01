using System.Text.Json;

namespace ReelDiscovery.Models.Case;

/// <summary>
/// Structured case guide (issues, organizations, cast, key facts, canonical values) that grounds corpus generation.
/// </summary>
public class CaseGuide
{
    public List<CaseIssue> Issues { get; set; } = new();
    public List<CaseOrganization> Organizations { get; set; } = new();
    public List<CastMember> Cast { get; set; } = new();
    public List<KeyFact> Facts { get; set; } = new();
    public List<CanonicalValue> Values { get; set; } = new();

    /// <summary>
    /// Last number issued per id prefix (ISS, ORG, CH, F, V); ids are never reused after deletion.
    /// </summary>
    public Dictionary<string, int> IdCounters { get; set; } = new();

    public GuidePartState IssuesState { get; set; } = new();
    public GuidePartState IssueDetailsState { get; set; } = new();
    public GuidePartState CastState { get; set; } = new();
    public GuidePartState FactsState { get; set; } = new();
    public GuidePartState ValuesState { get; set; } = new();

    /// <summary>
    /// GuideStaleness.StaleKey the user answered Not Now for; the banner stays hidden until the key changes.
    /// </summary>
    public string DismissedStaleKey { get; set; } = string.Empty;

    public GuidePartState State(GuidePart part) => part switch
    {
        GuidePart.Issues => IssuesState,
        GuidePart.Cast => CastState,
        GuidePart.Facts => FactsState,
        GuidePart.Values => ValuesState,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown guide part")
    };

    public CaseIssue? FindIssue(string? id) => Find(Issues, id, x => x.Id);
    public CaseOrganization? FindOrganization(string? id) => Find(Organizations, id, x => x.Id);
    public CastMember? FindCast(string? id) => Find(Cast, id, x => x.Id);
    public KeyFact? FindFact(string? id) => Find(Facts, id, x => x.Id);
    public CanonicalValue? FindValue(string? id) => Find(Values, id, x => x.Id);

    /// <summary>
    /// Every entity id across issues, organizations, cast, facts and values.
    /// </summary>
    public IEnumerable<string> AllIds()
    {
        foreach (var issue in Issues.Where(x => x != null))
            yield return issue.Id;
        foreach (var organization in Organizations.Where(x => x != null))
            yield return organization.Id;
        foreach (var member in Cast.Where(x => x != null))
            yield return member.Id;
        foreach (var fact in Facts.Where(x => x != null))
            yield return fact.Id;
        foreach (var value in Values.Where(x => x != null))
            yield return value.Id;
    }

    /// <summary>
    /// SHA-256 of the serialized guide; Phase 4 stamps its plan basis with it.
    /// </summary>
    public string ComputeHash() => CaseDraft.ComputeHash(JsonSerializer.Serialize(this, CaseDraft.JsonOptions));

    /// <summary>
    /// Repairs a guide loaded from untrusted case.json: null lists, strings and sub-objects become empty values; never throws.
    /// </summary>
    public void Normalize()
    {
        Issues = CleanList(Issues);
        Organizations = CleanList(Organizations);
        Cast = CleanList(Cast);
        Facts = CleanList(Facts);
        Values = CleanList(Values);
        IdCounters ??= new();
        DismissedStaleKey ??= string.Empty;

        IssuesState = NormalizeState(IssuesState);
        IssueDetailsState = NormalizeState(IssueDetailsState);
        CastState = NormalizeState(CastState);
        FactsState = NormalizeState(FactsState);
        ValuesState = NormalizeState(ValuesState);

        foreach (var issue in Issues)
        {
            issue.Id ??= string.Empty;
            issue.CountId ??= string.Empty;
            issue.Title ??= string.Empty;
            issue.LegalBasis ??= string.Empty;
            issue.Summary ??= string.Empty;
            issue.AgainstPartyIds = CleanStrings(issue.AgainstPartyIds);
            issue.Description ??= string.Empty;
            issue.Elements = CleanStrings(issue.Elements);
            issue.RelevanceCriteria ??= string.Empty;
        }

        foreach (var organization in Organizations)
        {
            organization.Id ??= string.Empty;
            organization.Name ??= string.Empty;
            organization.ShortName ??= string.Empty;
            organization.PartyId ??= string.Empty;
            organization.RepresentsOrgIds = CleanStrings(organization.RepresentsOrgIds);
            organization.Domain ??= string.Empty;
            organization.Location ??= string.Empty;
            organization.TimeZoneId ??= string.Empty;
        }

        foreach (var member in Cast)
        {
            member.Id ??= string.Empty;
            member.FirstName ??= string.Empty;
            member.LastName ??= string.Empty;
            member.Title ??= string.Empty;
            member.Department ??= string.Empty;
            member.OrganizationId ??= string.Empty;
            member.Email ??= string.Empty;
            member.Phone ??= string.Empty;
            member.ActiveFrom ??= string.Empty;
            member.ActiveTo ??= string.Empty;
            member.Involvement ??= string.Empty;
            member.VoiceNote ??= string.Empty;
            member.Signature ??= string.Empty;
        }

        foreach (var fact in Facts)
        {
            fact.Id ??= string.Empty;
            fact.Date ??= string.Empty;
            fact.Title ??= string.Empty;
            fact.Description ??= string.Empty;
            fact.IssueIds = CleanStrings(fact.IssueIds);
            fact.ActorIds = CleanStrings(fact.ActorIds);
            fact.KnownByIds = CleanStrings(fact.KnownByIds);
            fact.SourceSectionId ??= string.Empty;
            fact.MappingNotes = CleanStrings(fact.MappingNotes);
            fact.ReviewReason ??= string.Empty;
        }

        foreach (var value in Values)
        {
            value.Id ??= string.Empty;
            value.Label ??= string.Empty;
            value.Value ??= string.Empty;
            value.Description ??= string.Empty;
            value.FactIds = CleanStrings(value.FactIds);
            value.MappingNotes = CleanStrings(value.MappingNotes);
            value.ReviewReason ??= string.Empty;
        }
    }

    // Ordinal match; null/empty id never matches
    private static T? Find<T>(List<T> items, string? id, Func<T, string> getId) where T : class =>
        string.IsNullOrEmpty(id) ? null : items.FirstOrDefault(x => x != null && getId(x) == id);

    internal static List<T> CleanList<T>(List<T>? list) where T : class
    {
        list ??= new();
        list.RemoveAll(x => x == null);
        return list;
    }

    internal static List<string> CleanStrings(List<string>? list) => CleanList(list);

    private static GuidePartState NormalizeState(GuidePartState? state)
    {
        state ??= new();
        state.FailureMessage ??= string.Empty;
        state.BasisHash ??= string.Empty;
        state.BasisComponents ??= new();
        foreach (var key in state.BasisComponents.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
            state.BasisComponents[key] = string.Empty;
        state.CompletedUnits = CleanStrings(state.CompletedUnits);
        return state;
    }
}
