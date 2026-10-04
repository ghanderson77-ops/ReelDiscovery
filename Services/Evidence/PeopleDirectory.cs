using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// One resolved participant (cast member or plan external) for email composition.
/// </summary>
public class PersonInfo
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Sender domain: the organization Domain (lower-case), else the email host, else empty.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    public string OrganizationId { get; set; } = string.Empty;
    public CastMember? Cast { get; set; }
    public bool IsExternal { get; set; }

    /// <summary>
    /// The plan kind of an external party (picks the bulk-mail formatter); null for cast members.
    /// </summary>
    public ExternalKind? ExternalKind { get; set; }

    /// <summary>
    /// Cast organization name from the guide; empty for externals and cast without a resolvable organization.
    /// </summary>
    public string OrganizationName { get; set; } = string.Empty;
}

/// <summary>
/// Resolves every person id in a plan (cast first, then externals; first wins on duplicate ids).
/// </summary>
public class PeopleDirectory
{
    private readonly Dictionary<string, PersonInfo> _people = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nameTokens = new(StringComparer.OrdinalIgnoreCase);

    private PeopleDirectory()
    {
    }

    public IReadOnlyCollection<string> KnownNameTokens => _nameTokens;

    public IReadOnlyCollection<PersonInfo> All => _people.Values;

    public static PeopleDirectory Create(CaseGuide guide, CorpusPlan plan)
    {
        var directory = new PeopleDirectory();

        foreach (var member in (guide?.Cast ?? new List<CastMember>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id)))
        {
            var email = member.Email?.Trim() ?? string.Empty;
            var organization = guide!.FindOrganization(member.OrganizationId);
            var orgDomain = organization?.Domain?.Trim().ToLowerInvariant() ?? string.Empty;
            directory.Add(new PersonInfo
            {
                Id = member.Id.Trim(),
                FullName = member.FullName,
                FirstName = member.FirstName?.Trim() ?? string.Empty,
                LastName = member.LastName?.Trim() ?? string.Empty,
                Email = email,
                Domain = orgDomain.Length > 0 ? orgDomain : HostOf(email),
                OrganizationId = member.OrganizationId ?? string.Empty,
                Cast = member,
                IsExternal = false,
                ExternalKind = null,
                OrganizationName = organization?.Name?.Trim() ?? string.Empty
            });
        }

        foreach (var external in (plan?.Externals ?? new List<ExternalParty>()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id)))
        {
            var name = external.Name?.Trim() ?? string.Empty;
            var email = external.Email?.Trim() ?? string.Empty;
            var domain = external.Domain?.Trim().ToLowerInvariant() ?? string.Empty;

            // Split on the first space; a single word is the first name only
            var space = name.IndexOf(' ');
            var first = space > 0 ? name[..space] : name;
            var last = space > 0 ? name[(space + 1)..].Trim() : string.Empty;

            directory.Add(new PersonInfo
            {
                Id = external.Id.Trim(),
                FullName = name,
                FirstName = first,
                LastName = last,
                Email = email,
                Domain = domain.Length > 0 ? domain : HostOf(email),
                OrganizationId = string.Empty,
                Cast = null,
                IsExternal = true,
                ExternalKind = external.Kind
            });
        }

        return directory;
    }

    public PersonInfo? Find(string? id) =>
        id != null && _people.TryGetValue(id.Trim(), out var person) ? person : null;

    // Full name; the id when the person is unknown or unnamed
    public string NameOf(string id)
    {
        var person = Find(id);
        return person != null && person.FullName.Length > 0 ? person.FullName : id;
    }

    public string EmailOf(string id) => Find(id)?.Email ?? string.Empty;

    private void Add(PersonInfo person)
    {
        if (!_people.TryAdd(person.Id, person))
            return;

        foreach (var token in new[] { person.FullName, person.FirstName, person.LastName })
        {
            if (!string.IsNullOrWhiteSpace(token))
                _nameTokens.Add(token.Trim());
        }
    }

    private static string HostOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..].Trim().ToLowerInvariant() : string.Empty;
    }
}
