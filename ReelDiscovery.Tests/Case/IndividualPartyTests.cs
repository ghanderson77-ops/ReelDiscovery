using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Case.CaseGuideRegenerationTests;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// UAT gap fix (2026-10-02): individual parties are people placed at their employer, not organizations of their own
/// (with a personal organization only for a private individual with no employer).
/// </summary>
public class IndividualPartyTests
{
    // ---------- Fixtures ----------

    // The user's UAT scenario: Ryan Howard, Michael Scott and David Wallace are individual parties who work at Dunder Mifflin
    private static CaseDraft OfficeDraft()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var doc = draft.CoreDocument!;
        doc.Parties = new List<CaseParty>
        {
            new()
            {
                Id = "party-1",
                Name = "Dunder Mifflin Paper Company, Inc.",
                ShortName = "Dunder Mifflin",
                Role = PartyRole.Plaintiff,
                EntityType = "Pennsylvania corporation",
                Domicile = "Scranton, Pennsylvania",
                Description = "Regional paper distributor."
            },
            Individual("party-2", "Ryan Howard", "Howard", "Former vice president who ran the Dunder Mifflin Infinity launch."),
            Individual("party-3", "Michael Scott", "Scott", "Scranton regional manager."),
            Individual("party-4", "David Wallace", "Wallace", "Chief financial officer.")
        };
        return draft;
    }

    private static CaseParty Individual(string id, string name, string shortName, string description) => new()
    {
        Id = id,
        Name = name,
        ShortName = shortName,
        Role = PartyRole.Defendant,
        EntityType = "individual",
        Domicile = "Scranton, Pennsylvania",
        Description = description
    };

    private static readonly string[] OfficePeople =
    {
        Person("", "Ryan", "Howard", "Vice President, North East Region", "ORG-01", "Party"),
        Person("", "Michael", "Scott", "Regional Manager", "ORG-01", "Party"),
        Person("", "David", "Wallace", "Chief Financial Officer", "ORG-01", "Party"),
        Person("", "Oscar", "Martinez", "Accountant", "ORG-01", "Staff"),
        Person("", "Jan", "Levinson", "General Counsel", "ORG-01", "InHouseCounsel"),
        Person("", "Morgan", "Ellis", "Partner", "Ellis & Park LLP", "OutsideCounsel"),
        Person("", "Evan", "Price", "Associate", "Ellis & Park LLP", "OutsideCounsel")
    };

    private static string OfficeRoster(IEnumerable<string> people, params string[] extraOrganizations) =>
        Roster(people, new[] { Firm("Ellis & Park LLP", "Ellis & Park", "ORG-01") }.Concat(extraOrganizations).ToArray());

    private static (LlmGateway Gateway, FakeLlmProvider Fake) Gateway(params string[] responses)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        foreach (var response in responses)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(response));
        return (gateway, fake);
    }

    private static readonly string[] PersonNames = { "Ryan Howard", "Michael Scott", "David Wallace" };

    // ---------- Bug 1: individual parties ----------

    [Fact]
    public async Task IndividualParty_PlacedAtEmployer()
    {
        var draft = OfficeDraft();
        var (gateway, fake) = Gateway(OfficeRoster(OfficePeople), DetailsJson(), BackgroundJson, BackgroundJson);

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide!;

        // No organization is named after a person; everyone works at Dunder Mifflin
        Assert.DoesNotContain(guide.Organizations, o => o.Kind == OrganizationKind.Individual || PersonNames.Contains(o.Name));
        var dunder = guide.FindOrganization("ORG-01")!;
        Assert.Equal("dundermifflin.com", dunder.Domain);

        var ryan = Assert.Single(guide.Cast, c => c.FullName == "Ryan Howard");
        Assert.Equal("party-2", ryan.PartyId);
        Assert.Equal("ORG-01", ryan.OrganizationId);
        Assert.Equal("ryan.howard@dundermifflin.com", ryan.Email);
        Assert.Contains("Dunder Mifflin Paper Company, Inc.", ryan.Signature);
        Assert.Equal("party-3", Assert.Single(guide.Cast, c => c.FullName == "Michael Scott").PartyId);
        Assert.Equal("party-4", Assert.Single(guide.Cast, c => c.FullName == "David Wallace").PartyId);
        Assert.Equal(string.Empty, Assert.Single(guide.Cast, c => c.FullName == "Oscar Martinez").PartyId);

        // The roster prompt lists the people apart and forbids person-named organizations (keep-names rule intact, D-01)
        var roster = fake.Requests[0].UserPrompt;
        Assert.Contains("Individual parties:", roster);
        Assert.Contains("Never create an organization named after a person.", roster);
        Assert.Contains("Keep every name the document uses exactly as written", roster);
        Assert.DoesNotContain("(individual)", roster);

        // Employer-placed individuals need no counsel of their own: no top-up, no gap
        Assert.DoesNotContain(fake.Requests, IsTopUpRequest);
        Assert.Empty(CaseGuideGenerator.CounselGaps(draft));

        var findings = CaseGuideValidator.Validate(draft);
        Assert.DoesNotContain(findings, f => f.Message.Contains("is not in the cast") || f.Message.Contains("has no organization"));
        Assert.DoesNotContain(findings, f => PersonNames.Any(n => f.Message.StartsWith(n + " has", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task IndividualParty_OrganizationNamedAfterPersonIgnored()
    {
        var draft = OfficeDraft();
        var orgNamedAfterRyan = """{ "name": "Ryan Howard", "shortName": "Howard", "kind": "Party", "representsRefs": [], "location": "Scranton, Pennsylvania" }""";
        var (gateway, _) = Gateway(OfficeRoster(OfficePeople, orgNamedAfterRyan), DetailsJson(), BackgroundJson, BackgroundJson);

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        Assert.Contains("Ignored organization Ryan Howard: it is named after a person.", result.Notes);
        Assert.DoesNotContain(draft.Guide!.Organizations, o => o.Name == "Ryan Howard");
        Assert.Equal("ryan.howard@dundermifflin.com", Assert.Single(draft.Guide.Cast, c => c.FullName == "Ryan Howard").Email);
    }

    [Fact]
    public async Task IndividualParty_UnmatchedFallsBackToPersonalOrganization()
    {
        // The roster leaves David Wallace out: he is added deterministically, at a personal organization (the old behavior,
        // kept only for an individual with no employer in the cast), and the counsel top-up asks for his firm
        var draft = OfficeDraft();
        var people = OfficePeople.Where(p => !p.Contains("\"Wallace\"", StringComparison.Ordinal));
        var (gateway, fake) = Gateway(OfficeRoster(people), """{ "organizations": [], "people": [] }""", DetailsJson(), BackgroundJson, BackgroundJson);

        await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var guide = draft.Guide!;
        var david = Assert.Single(guide.Cast, c => c.FullName == "David Wallace");
        Assert.Equal("party-4", david.PartyId);
        Assert.Equal(CastTier.Key, david.Tier);
        Assert.Equal(CastRole.Party, david.Role);
        var personal = guide.FindOrganization(david.OrganizationId)!;
        Assert.Equal(OrganizationKind.Individual, personal.Kind);
        Assert.Equal("party-4", personal.PartyId);
        Assert.Equal("wallacefamily.com", personal.Domain);
        Assert.Equal("david.wallace@wallacefamily.com", david.Email);
        Assert.False(david.DetailsPending);

        var topUp = Assert.Single(fake.Requests, IsTopUpRequest);
        Assert.Contains($"{personal.Id} David Wallace (individual) needs an outside law firm with a partner and an associate", topUp.UserPrompt);
        Assert.DoesNotContain("Ryan Howard (individual)", topUp.UserPrompt);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("is not in the cast"));
    }

    // A guide saved before the fix: Ryan and Michael filed under organizations named after them, an earlier demoted
    // "David Wallace" person-organization left empty, and the outside firm representing one of the person-organizations
    private static CaseDraft SavedPersonOrgDraft()
    {
        var draft = OfficeDraft();
        var guide = new CaseGuide();
        guide.Organizations.Add(Org("ORG-01", "Dunder Mifflin Paper Company, Inc.", "Dunder Mifflin", OrganizationKind.Party, "party-1", "dundermifflin.com"));
        guide.Organizations.Add(Org("ORG-02", "Ryan Howard", "Howard", OrganizationKind.Individual, "party-2", "howardfamily.com"));
        guide.Organizations.Add(Org("ORG-03", "Michael Scott", "Scott", OrganizationKind.Individual, "party-3", "scottfamily.com"));
        var firm = Org("ORG-04", "Ellis & Park LLP", "Ellis & Park", OrganizationKind.LawFirm, "", "ellispark.com");
        firm.RepresentsOrgIds = new List<string> { "ORG-01", "ORG-02" };
        guide.Organizations.Add(firm);
        guide.Organizations.Add(Org("ORG-05", "David Wallace", "Wallace", OrganizationKind.ThirdParty, "", "wallacefamily.com"));

        guide.Cast.Add(Member("CH-001", "Ryan", "Howard", "ORG-02", "howardfamily.com", CastRole.Party));
        guide.Cast.Add(Member("CH-002", "Michael", "Scott", "ORG-03", "scottfamily.com", CastRole.Party));
        guide.Cast.Add(Member("CH-003", "David", "Wallace", "ORG-01", "dundermifflin.com", CastRole.Party));
        guide.Cast.Add(Member("CH-004", "Jan", "Levinson", "ORG-01", "dundermifflin.com", CastRole.InHouseCounsel, "General Counsel"));
        guide.Cast.Add(Member("CH-005", "Morgan", "Ellis", "ORG-04", "ellispark.com", CastRole.OutsideCounsel, "Partner"));
        guide.Cast.Add(Member("CH-006", "Evan", "Price", "ORG-04", "ellispark.com", CastRole.OutsideCounsel, "Associate"));
        guide.IdCounters = new Dictionary<string, int> { ["ORG"] = 5, ["CH"] = 6 };
        guide.IssuesState.Status = GuidePartStatus.Ready;
        guide.CastState.Status = GuidePartStatus.Ready;
        draft.Guide = guide;
        return draft;
    }

    private static CaseOrganization Org(string id, string name, string shortName, OrganizationKind kind, string partyId, string domain) => new()
    {
        Id = id,
        Name = name,
        ShortName = shortName,
        Kind = kind,
        PartyId = partyId,
        Domain = domain,
        Location = "Scranton, Pennsylvania"
    };

    private static CastMember Member(string id, string first, string last, string orgId, string domain, CastRole role, string title = "Manager") => new()
    {
        Id = id,
        FirstName = first,
        LastName = last,
        Title = title,
        OrganizationId = orgId,
        Tier = CastTier.Key,
        Role = role,
        Email = $"{first}.{last}@{domain}".ToLowerInvariant(),
        ActiveFrom = "2024-01-08",
        ActiveTo = "2024-09-16"
    };

    [Fact]
    public async Task RegenerateCast_MovesPeopleToEmployerAndRemovesPersonOrganizations()
    {
        var draft = SavedPersonOrgDraft();
        var people = new[]
        {
            Person("CH-001", "Ryan", "Howard", "Vice President", "ORG-01", "Party"),
            Person("CH-002", "Michael", "Scott", "Regional Manager", "ORG-01", "Party"),
            Person("CH-003", "David", "Wallace", "Chief Financial Officer", "ORG-01", "Party"),
            Person("CH-004", "Jan", "Levinson", "General Counsel", "ORG-01", "InHouseCounsel"),
            Person("CH-005", "Morgan", "Ellis", "Partner", "ORG-04", "OutsideCounsel"),
            Person("CH-006", "Evan", "Price", "Associate", "ORG-04", "OutsideCounsel")
        };
        // Background for the two organizations left: Dunder Mifflin and the firm
        var (gateway, fake) = CastGateway(Roster(people), background: 2);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide!;

        // People moved: same ids, employer email and signature
        var ryan = guide.FindCast("CH-001")!;
        Assert.Equal("ORG-01", ryan.OrganizationId);
        Assert.Equal("ryan.howard@dundermifflin.com", ryan.Email);
        Assert.Contains("Dunder Mifflin Paper Company, Inc.", ryan.Signature);
        Assert.Equal("party-2", ryan.PartyId);
        var michael = guide.FindCast("CH-002")!;
        Assert.Equal("michael.scott@dundermifflin.com", michael.Email);
        Assert.Equal("party-3", michael.PartyId);
        Assert.Equal("party-4", guide.FindCast("CH-003")!.PartyId);

        // Empty person-organizations removed (including the demoted one), and stripped from the firm's clients
        Assert.Equal(new[] { "ORG-01", "ORG-04" }, guide.Organizations.Select(o => o.Id));
        Assert.Equal(new[] { "ORG-01" }, guide.FindOrganization("ORG-04")!.RepresentsOrgIds);
        Assert.Equal(2, fake.Requests.Count(IsBackgroundRequest));
        Assert.DoesNotContain(fake.Requests, IsTopUpRequest);

        // The roster prompt no longer offers the person-organizations
        var roster = fake.Requests[0].UserPrompt;
        Assert.DoesNotContain("ORG-02 | Ryan Howard |", roster);
        Assert.DoesNotContain("ORG-03 | Michael Scott |", roster);

        var findings = CaseGuideValidator.Validate(draft);
        Assert.DoesNotContain(findings, f => f.Message.Contains("is no longer a party"));
        Assert.DoesNotContain(findings, f => f.Message.Contains("is not in the cast") || f.Message.Contains("has no organization"));
        Assert.DoesNotContain(findings, f => f.Message.Contains("does not use"));
    }

    [Fact]
    public void SyncOrganizations_LeavesPersonOrganizationsForCleanup()
    {
        // WR-10 demotion is only for organization parties: a person-organization never becomes a "removed party" third party
        var draft = SavedPersonOrgDraft();

        CaseGuideGenerator.SyncOrganizations(draft, out _);

        var ryanOrg = draft.Guide!.FindOrganization("ORG-02")!;
        Assert.Equal(OrganizationKind.Individual, ryanOrg.Kind);
        Assert.Equal("party-2", ryanOrg.PartyId);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("is no longer a party"));
    }

    [Fact]
    public void RemovePersonOrganizations_KeepsOccupiedAndRepresentedOnes()
    {
        var draft = SavedPersonOrgDraft();
        var guide = draft.Guide!;

        // Nobody linked yet: Ryan and Michael still sit at their person-organizations; ORG-05 is empty and unrepresented
        Assert.True(CaseGuideGenerator.RemovePersonOrganizations(draft));
        Assert.Null(guide.FindOrganization("ORG-05"));
        Assert.NotNull(guide.FindOrganization("ORG-02"));
        Assert.NotNull(guide.FindOrganization("ORG-03"));

        // Michael moved but not linked: his empty, unrepresented organization goes
        guide.FindCast("CH-002")!.OrganizationId = "ORG-01";
        Assert.True(CaseGuideGenerator.RemovePersonOrganizations(draft));
        Assert.Null(guide.FindOrganization("ORG-03"));

        // Ryan moved but not linked to his party: the firm's reference keeps ORG-02
        guide.FindCast("CH-001")!.OrganizationId = "ORG-01";
        Assert.False(CaseGuideGenerator.RemovePersonOrganizations(draft));
        Assert.NotNull(guide.FindOrganization("ORG-02"));

        // Linked: the person sits at an employer, so the firm's reference no longer keeps it
        Assert.True(CaseGuideGenerator.LinkIndividualParties(draft));
        Assert.True(CaseGuideGenerator.RemovePersonOrganizations(draft));
        Assert.Null(guide.FindOrganization("ORG-02"));
        Assert.Equal(new[] { "ORG-01" }, guide.FindOrganization("ORG-04")!.RepresentsOrgIds);
        Assert.NotNull(guide.FindOrganization("ORG-01"));
    }

    [Fact]
    public void CounselGaps_NoFirmForEmployerPlacedIndividuals()
    {
        var draft = SavedPersonOrgDraft();
        var guide = draft.Guide!;
        Assert.Contains(CaseGuideGenerator.CounselGaps(draft), g => g.Contains("Michael Scott (individual)", StringComparison.Ordinal));

        guide.FindCast("CH-002")!.OrganizationId = "ORG-01";
        guide.FindCast("CH-001")!.OrganizationId = "ORG-01";
        CaseGuideGenerator.LinkIndividualParties(draft);
        CaseGuideGenerator.RemovePersonOrganizations(draft);

        var gaps = CaseGuideGenerator.CounselGaps(draft);
        Assert.DoesNotContain(gaps, g => PersonNames.Any(n => g.Contains(n, StringComparison.Ordinal)));
        Assert.Empty(gaps);
    }
}
