using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// Regeneration semantics (D-19, D-20, GUIDE-08/09): whole-tab regeneration keeps matched ids and hand edits,
/// single-entry regeneration, and Update Guide for stale parts only.
/// </summary>
public class CaseGuideRegenerationTests
{
    // ---------- Canned responses ----------

    internal const string BackgroundJson = """
        {
          "people": [
            { "firstName": "Gina", "lastName": "Morales", "title": "Receptionist", "department": "Administration", "role": "Answers phones and books meetings.", "activeFrom": "2024-01-08", "activeTo": "2024-09-16" },
            { "firstName": "Hank", "lastName": "Oyelaran", "title": "Accounts Payable Clerk", "department": "Accounting", "role": "Processes vendor invoices.", "activeFrom": "2024-01-08", "activeTo": "2024-09-16" },
            { "firstName": "Iris", "lastName": "Paulsen", "title": "IT Technician", "department": "Information Technology", "role": "Handles laptops and accounts.", "activeFrom": "2024-02-01", "activeTo": "2024-09-16" }
          ]
        }
        """;

    // Brightline/Harmon key people of SampleGuidedDraft (CH-001..CH-007), returned by existingId
    private static readonly (string Id, string First, string Last, string Title, string Org, string Role)[] BaseRoster =
    {
        ("CH-001", "Maria", "Delgado", "President", "ORG-01", "Party"),
        ("CH-002", "Tom", "Becker", "Project Manager", "ORG-01", "Staff"),
        ("CH-003", "Raymond", "Harmon", "Chief Executive Officer", "ORG-02", "Party"),
        ("CH-004", "Lisa", "Chen", "Project Executive", "ORG-02", "Staff"),
        ("CH-005", "Karen", "Walsh", "General Counsel", "ORG-01", "InHouseCounsel"),
        ("CH-006", "Morgan", "Ellis", "Partner", "ORG-03", "OutsideCounsel"),
        ("CH-007", "Evan", "Price", "Associate", "ORG-03", "OutsideCounsel")
    };

    // Harmon's counsel in SampleCounselDraft (hand-edited there)
    private static readonly (string Id, string First, string Last, string Title, string Org, string Role)[] HarmonCounsel =
    {
        ("CH-010", "Paul", "Novak", "General Counsel", "ORG-02", "InHouseCounsel"),
        ("CH-011", "Nora", "Quist", "Partner", "ORG-04", "OutsideCounsel"),
        ("CH-012", "Owen", "Hale", "Associate", "ORG-04", "OutsideCounsel")
    };

    internal static string Person(string existingId, string first, string last, string title, string org, string role, string involvement = "Took part in the change order dispute.") =>
        $$"""{ "existingId": "{{existingId}}", "firstName": "{{first}}", "lastName": "{{last}}", "title": "{{title}}", "organizationRef": "{{org}}", "role": "{{role}}", "involvement": "{{involvement}}" }""";

    internal static string Firm(string name, string shortName, string represents) =>
        $$"""{ "name": "{{name}}", "shortName": "{{shortName}}", "kind": "LawFirm", "representsRefs": ["{{represents}}"], "location": "Chicago, Illinois" }""";

    internal static string Roster(IEnumerable<string> people, params string[] organizations) =>
        "{ \"organizations\": [" + string.Join(", ", organizations) + "], \"people\": [" + string.Join(", ", people) + "] }";

    internal static List<string> BasePeople(params string[] except) =>
        BaseRoster.Where(p => !except.Contains(p.Id)).Select(p => Person(p.Id, p.First, p.Last, p.Title, p.Org, p.Role)).ToList();

    internal static List<string> HarmonCounselPeople() =>
        HarmonCounsel.Select(p => Person(p.Id, p.First, p.Last, p.Title, p.Org, p.Role)).ToList();

    // Details for every id up to CH-040; ids outside a batch are ignored with a note
    internal static string DetailsJson()
    {
        var people = Enumerable.Range(1, 40).Select(n =>
            $$"""{ "castId": "CH-{{n:D3}}", "summary": "Character CH-{{n:D3}} took part in the change order dispute.", "activeFrom": "2024-01-15", "activeTo": "2024-08-30", "voiceNote": "Short, direct emails.", "signatureStyle": "Short" }""");
        return "{ \"people\": [" + string.Join(", ", people) + "] }";
    }

    internal static string FactJson(string date, string title, string issues = "\"ISS-01\"", string actors = "\"CH-001\"", string knownBy = "", string significance = "Key", string existingId = "") =>
        $$"""{ "existingId": "{{existingId}}", "date": "{{date}}", "title": "{{title}}", "description": "{{title}} on the Lakeview Tower project.", "issueIds": [{{issues}}], "actorIds": [{{actors}}], "knownByIds": [{{knownBy}}], "significance": "{{significance}}" }""";

    internal static string FactsJson(params string[] facts) => "{ \"facts\": [" + string.Join(", ", facts) + "] }";

    internal static string ValueJson(string existingId, string kind, string label, string value, string factIds) =>
        $$"""{ "existingId": "{{existingId}}", "kind": "{{kind}}", "label": "{{label}}", "value": "{{value}}", "description": "{{label}} in the case.", "factIds": [{{factIds}}] }""";

    internal static string ValuesJson(params string[] values) => "{ \"values\": [" + string.Join(", ", values) + "] }";

    /// <summary>
    /// Regenerate Cast responses: roster -> (top-up) -> details batches -> one background response per non-Individual organization.
    /// </summary>
    internal static (LlmGateway Gateway, FakeLlmProvider Fake) CastGateway(string roster, int background, int detailBatches = 1, string? topUp = null)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(roster));
        if (topUp != null)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(topUp));
        for (var i = 0; i < detailBatches; i++)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(DetailsJson()));
        for (var i = 0; i < background; i++)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(BackgroundJson));
        return (gateway, fake);
    }

    private static (LlmGateway Gateway, FakeLlmProvider Fake) Gateway(params LlmProviderResponse[] responses)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        foreach (var response in responses)
            fake.Responses.Enqueue(response);
        return (gateway, fake);
    }

    internal static bool IsRosterRequest(LlmProviderRequest r) => r.UserPrompt.Contains("key people who are not counsel");
    internal static bool IsTopUpRequest(LlmProviderRequest r) => r.UserPrompt.Contains("Add only these missing counsel");
    internal static bool IsDetailsRequest(LlmProviderRequest r) => r.UserPrompt.Contains("For each CH ID");
    internal static bool IsBackgroundRequest(LlmProviderRequest r) => r.UserPrompt.Contains("background staff for");
    internal static bool IsFactsRequest(LlmProviderRequest r) => r.UserPrompt.Contains("State each fact as what happened");
    internal static bool IsValuesRequest(LlmProviderRequest r) => r.UserPrompt.Contains("Register every amount");

    private static int NonIndividualOrgs(CaseDraft draft) =>
        draft.Guide!.Organizations.Count(o => o.Kind != OrganizationKind.Individual);

    private static void StampAll(CaseDraft draft)
    {
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);
    }

    private static string GuideJson(CaseDraft draft) => JsonSerializer.Serialize(draft.Guide, CaseDraft.JsonOptions);

    // ---------- Regenerate Cast ----------

    [Fact]
    public async Task RegenCast_PromptListsCurrentRoster()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var (gateway, fake) = CastGateway(Roster(BasePeople()), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "add a second associate", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var roster = fake.Requests[0];
        Assert.True(IsRosterRequest(roster));
        Assert.Contains("Current roster (revise by existingId; leave existingId empty for new people):", roster.UserPrompt);
        Assert.Contains("CH-001 | Maria Delgado | President | ORG-01 | Party", roster.UserPrompt);
        Assert.Contains("CH-007 | Evan Price | Associate | ORG-03 | OutsideCounsel", roster.UserPrompt);
        Assert.Matches(@"Keep exactly; do not return these:\s*\r?\nCH-010 \| Paul Novak \| General Counsel \| ORG-02 \| InHouseCounsel", roster.UserPrompt);
        // Edited people are not offered for revision
        var revise = roster.UserPrompt[roster.UserPrompt.IndexOf("Current roster (", StringComparison.Ordinal)..roster.UserPrompt.IndexOf("Keep exactly;", StringComparison.Ordinal)];
        Assert.DoesNotContain("CH-010", revise);
        Assert.DoesNotContain("CH-008", roster.UserPrompt[..roster.UserPrompt.IndexOf("Document:", StringComparison.Ordinal)]);
        Assert.Contains("Guidance from the user: add a second associate", roster.UserPrompt);
        Assert.Equal(CaseGuidePrompts.SystemPrompt, roster.SystemPrompt);
        Assert.Equal(GuidePart.Cast, result.Part);
    }

    [Fact]
    public async Task RegenCast_KeepsMatchedIds()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var people = BasePeople("CH-002", "CH-004");
        people.Add(Person("CH-2", "Tom", "Becker", "Senior Project Manager", "ORG-01", "Staff"));
        people.Add(Person("", "Dana", "Reyes", "Site Superintendent", "ORG-01", "Staff"));
        var (gateway, fake) = CastGateway(Roster(people), background: 4);
        var revision = draft.Revision;

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide!;
        var tom = guide.FindCast("CH-002")!;
        Assert.Equal("Tom Becker", tom.FullName);
        Assert.Equal("Senior Project Manager", tom.Title);
        Assert.False(tom.IsUserEdited);

        var dana = guide.FindCast("CH-013")!;
        Assert.Equal("Dana Reyes", dana.FullName);
        Assert.Equal("ORG-01", dana.OrganizationId);
        Assert.Equal("dana.reyes@brightline.com", dana.Email);

        // Changed and new characters were sent for details (DetailsPending), then finished
        var details = Assert.Single(fake.Requests, IsDetailsRequest);
        Assert.Contains("CH-002 | Tom Becker | Senior Project Manager", details.UserPrompt);
        Assert.Contains("CH-013 | Dana Reyes | Site Superintendent", details.UserPrompt);
        Assert.All(guide.Cast, c => Assert.False(c.DetailsPending));

        // Not returned and not edited: removed, and the facts citing her need review
        Assert.Null(guide.FindCast("CH-004"));
        var citing = guide.Facts.Where(f => f.Id is "F-002" or "F-003" or "F-004" or "F-005").ToList();
        Assert.All(citing, f =>
        {
            Assert.True(f.NeedsReview);
            Assert.Equal("Lisa Chen was removed from the cast.", f.ReviewReason);
            Assert.DoesNotContain("CH-004", f.ActorIds);
            Assert.DoesNotContain("CH-004", f.KnownByIds);
        });
        Assert.False(guide.FindFact("F-001")!.NeedsReview);

        // Hand-edited counsel kept
        Assert.NotNull(guide.FindCast("CH-010"));
        Assert.NotNull(guide.FindCast("CH-012"));
        Assert.True(draft.Revision > revision);
    }

    [Fact]
    public async Task RegenCast_HandEditKept()
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.Guide!.FindCast("CH-001")!.IsUserEdited = true;
        var people = BasePeople("CH-001");
        people.Insert(0, Person("CH-001", "Maria", "Delgado", "Owner", "ORG-01", "Party"));
        var (gateway, fake) = CastGateway(Roster(people), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide;
        var roster = fake.Requests[0].UserPrompt;
        var revise = roster[roster.IndexOf("Current roster (", StringComparison.Ordinal)..roster.IndexOf("Keep exactly;", StringComparison.Ordinal)];
        Assert.DoesNotContain("CH-001", revise);
        Assert.Contains("CH-001 | Maria Delgado | President | ORG-01 | Party", roster[roster.IndexOf("Keep exactly;", StringComparison.Ordinal)..]);

        var maria = Assert.Single(guide.Cast, c => c.FullName == "Maria Delgado");
        Assert.Equal("CH-001", maria.Id);
        Assert.Equal("President", maria.Title);
        Assert.True(maria.IsUserEdited);
        Assert.Contains("Kept your edits to Maria Delgado.", result.Notes);
        Assert.Equal(10, guide.Cast.Count(c => c.Tier == CastTier.Key));
        Assert.DoesNotContain(fake.Requests.Where(IsDetailsRequest), r => r.UserPrompt.Contains("CH-001 |"));
    }

    [Fact]
    public async Task RegenCast_ReplaceEdited()
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.Guide!.FindCast("CH-001")!.IsUserEdited = true;
        var people = BasePeople("CH-001");
        people.Insert(0, Person("CH-001", "Maria", "Delgado", "Owner", "ORG-01", "Party"));
        people.AddRange(HarmonCounselPeople());
        var (gateway, fake) = CastGateway(Roster(people), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: true);

        Assert.True(result.Succeeded, result.FailureMessage);
        var roster = fake.Requests[0].UserPrompt;
        Assert.Contains("CH-001 | Maria Delgado | President | ORG-01 | Party", roster[roster.IndexOf("Current roster (", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("Keep exactly;", roster);

        var maria = draft.Guide.FindCast("CH-001")!;
        Assert.Equal("Owner", maria.Title);
        Assert.False(maria.IsUserEdited);
        Assert.DoesNotContain(result.Notes, n => n.StartsWith("Kept your edits", StringComparison.Ordinal));
        Assert.False(draft.Guide.FindCast("CH-011")!.IsUserEdited);
        Assert.DoesNotContain(fake.Requests, IsTopUpRequest);
    }

    [Fact]
    public async Task RegenCast_DuplicateExistingIdInResponse()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var people = BasePeople();
        people.Add(Person("CH-002", "Tara", "Voss", "Estimator", "ORG-01", "Staff"));
        var (gateway, _) = CastGateway(Roster(people), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide!;
        Assert.Equal("Tom Becker", guide.FindCast("CH-002")!.FullName);
        var tara = Assert.Single(guide.Cast, c => c.FullName == "Tara Voss");
        Assert.Equal("CH-013", tara.Id);
    }

    [Fact]
    public async Task RegenCast_PartyRenamed()
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.CoreDocument!.Parties[1].Name = "Harmon Construction Group, Inc.";
        var emails = draft.Guide!.Cast.Where(c => c.Tier == CastTier.Key).ToDictionary(c => c.Id, c => c.Email);
        var (gateway, _) = CastGateway(Roster(BasePeople()), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var harmon = draft.Guide.FindOrganization("ORG-02")!;
        Assert.Equal("Harmon Construction Group, Inc.", harmon.Name);
        Assert.Equal("Harmon", harmon.ShortName);
        Assert.Equal("harmon.com", harmon.Domain);
        Assert.Equal(4, draft.Guide.Organizations.Count);
        Assert.All(emails, e => Assert.Equal(e.Value, draft.Guide.FindCast(e.Key)!.Email));
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("name differs from party"));
    }

    [Fact]
    public async Task RegenCast_PartyAdded()
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.CoreDocument!.Parties.Add(new CaseParty
        {
            Id = "party-3",
            Name = "Lakeview Tower Owner LLC",
            ShortName = "Lakeview Owner",
            Role = PartyRole.ThirdParty,
            EntityType = "Illinois limited liability company",
            Domicile = "Chicago, Illinois",
            Description = "Developer and owner of the Lakeview Tower project."
        });
        var people = BasePeople();
        people.Add(Person("", "Grace", "Lin", "General Counsel", "ORG-05", "InHouseCounsel"));
        people.Add(Person("", "Victor", "Stone", "Partner", "Stone & Wren LLP", "OutsideCounsel"));
        people.Add(Person("", "Amy", "Wren", "Associate", "Stone & Wren LLP", "OutsideCounsel"));
        var (gateway, fake) = CastGateway(Roster(people, Firm("Stone & Wren LLP", "Stone & Wren", "ORG-05")), background: 6);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide!;
        var owner = guide.FindOrganization("ORG-05")!;
        Assert.Equal("party-3", owner.PartyId);
        Assert.Equal(OrganizationKind.Party, owner.Kind);
        Assert.False(string.IsNullOrEmpty(owner.Domain));
        Assert.Equal(guide.Organizations.Count, guide.Organizations.Select(o => o.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("ORG-05 Lakeview Tower Owner LLC: one in-house counsel, and an outside law firm", fake.Requests[0].UserPrompt);
        Assert.Equal(CaseGuidePrompts.SystemPrompt, fake.Requests[0].SystemPrompt);
        Assert.Equal(draft.Preset.BackgroundPerOrg, guide.Cast.Count(c => c.Tier == CastTier.Background && c.OrganizationId == "ORG-05"));
        Assert.Equal(NonIndividualOrgs(draft), fake.Requests.Count(IsBackgroundRequest));
        Assert.DoesNotContain(fake.Requests, IsTopUpRequest);
    }

    [Fact]
    public async Task RegenCast_AddsLawFirm()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var people = BasePeople();
        people.Add(Person("", "Ivy", "Calder", "Partner", "Calder & Finch LLP", "OutsideCounsel"));
        people.Add(Person("", "Leo", "Finch", "Associate", "Calder & Finch LLP", "OutsideCounsel"));
        var (gateway, fake) = CastGateway(Roster(people, Firm("Calder & Finch LLP", "Calder & Finch", "ORG-02")), background: 5);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var guide = draft.Guide!;
        var firm = guide.Organizations.Single(o => o.Name == "Calder & Finch LLP");
        Assert.Equal("ORG-05", firm.Id);
        Assert.Equal(OrganizationKind.LawFirm, firm.Kind);
        Assert.Equal(new[] { "ORG-02" }, firm.RepresentsOrgIds);
        Assert.False(string.IsNullOrEmpty(firm.Domain));
        Assert.Equal(5, NonIndividualOrgs(draft));
        Assert.Equal(5, fake.Requests.Count(IsBackgroundRequest));
        Assert.Contains(fake.Requests, r => r.UserPrompt.Contains("background staff for Calder & Finch LLP"));
        Assert.Equal(firm.Id, guide.Cast.Single(c => c.FullName == "Ivy Calder").OrganizationId);
    }

    [Fact]
    public async Task RegenCast_BackgroundRewritten()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var guide = draft.Guide!;
        guide.FindFact("F-005")!.KnownByIds.Add("CH-008");
        guide.FindCast("CH-009")!.IsUserEdited = true;
        var (gateway, fake) = CastGateway(Roster(BasePeople()), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Null(guide.FindCast("CH-008"));
        var fact = guide.FindFact("F-005")!;
        Assert.True(fact.NeedsReview);
        Assert.Equal("Priya Nair was removed from the cast.", fact.ReviewReason);
        Assert.DoesNotContain("CH-008", fact.KnownByIds);

        var owen = guide.FindCast("CH-009")!;
        Assert.Equal("Owen Reilly", owen.FullName);
        Assert.Contains("Kept your edits to Owen Reilly.", result.Notes);

        var background = fake.Requests.Where(IsBackgroundRequest).ToList();
        Assert.Equal(NonIndividualOrgs(draft), background.Count);
        Assert.Contains(background, r => r.UserPrompt.Contains("Write 2 background staff for Brightline Glazing, LLC"));
        Assert.Contains(background, r => r.UserPrompt.Contains("Write 3 background staff for Harmon Builders, Inc."));
        Assert.All(guide.Organizations.Where(o => o.Kind != OrganizationKind.Individual), o =>
            Assert.Equal(draft.Preset.BackgroundPerOrg, guide.Cast.Count(c => c.Tier == CastTier.Background && c.OrganizationId == o.Id)));
        Assert.All(guide.Cast.Where(c => c.Tier == CastTier.Background), c => Assert.Equal(string.Empty, c.Signature));
    }

    [Fact]
    public async Task RegenCast_BackgroundRewritten_ReplaceEdited()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var guide = draft.Guide!;
        guide.FindCast("CH-009")!.IsUserEdited = true;
        var people = BasePeople();
        people.AddRange(HarmonCounselPeople());
        var (gateway, fake) = CastGateway(Roster(people), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: true);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Null(guide.FindCast("CH-009"));
        Assert.Contains(fake.Requests, r => r.UserPrompt.Contains("Write 3 background staff for Brightline Glazing, LLC"));
        Assert.Equal(draft.Preset.BackgroundPerOrg, guide.Cast.Count(c => c.Tier == CastTier.Background && c.OrganizationId == "ORG-01"));
    }

    [Fact]
    public async Task RegenCast_CounselTopUp()
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.Guide!.Cast.RemoveAll(c => c.Id == "CH-010");
        var topUp = Roster(new[] { Person("", "Paul", "Novak", "General Counsel", "ORG-02", "InHouseCounsel") });
        var (gateway, fake) = CastGateway(Roster(BasePeople()), background: 4, topUp: topUp);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var request = Assert.Single(fake.Requests, IsTopUpRequest);
        Assert.Same(request, fake.Requests[1]);
        Assert.Contains("ORG-02 Harmon Builders, Inc. needs in-house counsel", request.UserPrompt);
        var novak = Assert.Single(draft.Guide.Cast, c => c.FullName == "Paul Novak");
        Assert.Equal(CastRole.InHouseCounsel, novak.Role);
        Assert.Empty(CaseGuideGenerator.CounselGaps(draft));
    }

    [Fact]
    public async Task RegenCast_RunsDetailsAfterRoster()
    {
        var draft = CaseTestData.SampleCounselDraft();
        StampAll(draft);
        var (gateway, fake) = CastGateway(Roster(BasePeople()), background: 4);

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.True(IsRosterRequest(fake.Requests[0]));
        var details = Assert.Single(fake.Requests, IsDetailsRequest);
        Assert.Same(details, fake.Requests[1]);
        foreach (var id in new[] { "CH-001", "CH-002", "CH-003", "CH-004", "CH-005", "CH-006", "CH-007" })
            Assert.Contains(id + " |", details.UserPrompt);
        Assert.DoesNotContain("CH-010 |", details.UserPrompt);

        var guide = draft.Guide!;
        Assert.All(guide.Cast, c => Assert.False(c.DetailsPending));
        Assert.Equal("2024-01-15", guide.FindCast("CH-002")!.ActiveFrom);
        Assert.Equal(GuidePartStatus.Ready, guide.CastState.Status);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Cast), guide.CastState.BasisHash);
        Assert.True(GuideStaleness.IsStale(draft, GuidePart.Facts));
        Assert.Equal(1 + 1 + NonIndividualOrgs(draft), fake.Requests.Count);
    }

    // ---------- Regenerate Facts / Values ----------

    [Fact]
    public async Task RegenFacts()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        StampAll(draft);
        guide.FindFact("F-005")!.IsUserEdited = true;
        guide.FindValue("V-003")!.FactIds.Add("F-004");
        var castState = JsonSerializer.Serialize(guide.CastState, CaseDraft.JsonOptions);
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(FactsJson(
                FactJson("2024-01-16", "Subcontract signed after review", actors: "\"CH-001\", \"CH-003\"", existingId: "F-1"),
                FactJson("2024-02-20", "Harmon issues the notice to proceed", actors: "\"CH-003\""))),
            FakeLlmProvider.Ok(FactsJson(
                FactJson("2024-03-18", "Harmon approves Change Order 7 in writing", actors: "\"CH-004\"", knownBy: "\"CH-002\"", significance: "Hot", existingId: "F-003"),
                FactJson("2024-06-20", "Harmon refuses to pay the invoice", actors: "\"CH-003\"", existingId: "F-006"))));

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Facts, "focus on the invoice", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(2, fake.Requests.Count);
        Assert.All(fake.Requests, r => Assert.True(IsFactsRequest(r)));
        var first = fake.Requests[0].UserPrompt;
        Assert.Contains("Existing facts (revise by existingId; return new facts with an empty existingId):", first);
        Assert.Contains("F-001 | 2024-01-15 | Subcontract signed for Lakeview Tower", first);
        Assert.Contains("F-002 | 2024-02-12 | Harmon confirms the change order procedure", first);
        Assert.DoesNotContain("F-003 |", first);
        Assert.Contains("Guidance from the user: focus on the invoice", first);
        var second = fake.Requests[1].UserPrompt;
        Assert.Contains("F-003 | 2024-03-18 | Harmon approves Change Order 7", second);
        Assert.Matches(@"Keep exactly; do not return these:\s*\r?\nF-005 \| 2024-05-10 \| Brightline invoices Change Order 7", second);

        var f1 = guide.FindFact("F-001")!;
        Assert.Equal("2024-01-16", f1.Date);
        Assert.Equal("Subcontract signed after review", f1.Title);
        Assert.False(f1.IsUserEdited);
        Assert.Equal("facts-1", f1.SourceSectionId);

        var added = guide.FindFact("F-007")!;
        Assert.Equal("2024-02-20", added.Date);
        Assert.Equal("facts-1", added.SourceSectionId);

        Assert.Null(guide.FindFact("F-002"));
        Assert.Null(guide.FindFact("F-004"));
        Assert.Equal("Brightline invoices Change Order 7", guide.FindFact("F-005")!.Title);
        Assert.True(guide.FindFact("F-005")!.IsUserEdited);
        Assert.Equal(new[] { "CH-004", "CH-002" }, guide.FindFact("F-003")!.KnownByIds);
        Assert.Equal(new[] { "F-001" }, guide.FindValue("V-003")!.FactIds);
        Assert.Equal(new[] { "F-001", "F-007", "F-003", "F-005", "F-006" }, guide.Facts.Select(f => f.Id));

        Assert.Equal(GuidePartStatus.Ready, guide.FactsState.Status);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Facts), guide.FactsState.BasisHash);
        Assert.True(GuideStaleness.IsStale(draft, GuidePart.Values));
        Assert.Equal(castState, JsonSerializer.Serialize(guide.CastState, CaseDraft.JsonOptions));
    }

    [Fact]
    public async Task RegenValues()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        StampAll(draft);
        guide.FindValue("V-002")!.IsUserEdited = true;
        var factsState = JsonSerializer.Serialize(guide.FactsState, CaseDraft.JsonOptions);
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(ValuesJson(
            ValueJson("V-1", "Amount", "Change Order 7 approved amount", "$184,250.00", "\"F-003\", \"F-005\""),
            ValueJson("V-002", "ChangeOrderNumber", "Change order", "CO-7", "\"F-003\""),
            ValueJson("V-004", "InvoiceNumber", "Unpaid invoice", "INV-2024-118", "\"F-005\", \"F-99\""),
            ValueJson("", "Amount", "Retainage withheld", "$18,425.00", "\"F-006\""))));

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Values, "add the retainage", replaceEdited: false);

        Assert.True(result.Succeeded, result.FailureMessage);
        var request = Assert.Single(fake.Requests);
        Assert.Contains("Existing values (revise by existingId; return new values with an empty existingId):", request.UserPrompt);
        Assert.Matches(@"Keep exactly; do not return these:\s*\r?\nV-002 \| Change order number \| CO-007", request.UserPrompt);
        Assert.Contains("Guidance from the user: add the retainage", request.UserPrompt);

        var v1 = guide.FindValue("V-001")!;
        Assert.Equal("Change Order 7 approved amount", v1.Label);
        Assert.Equal(new[] { "F-003", "F-005" }, v1.FactIds);
        Assert.False(v1.IsUserEdited);

        var v2 = guide.FindValue("V-002")!;
        Assert.Equal("CO-007", v2.Value);
        Assert.True(v2.IsUserEdited);
        Assert.Contains("Kept your edits to Change order number.", result.Notes);

        Assert.Null(guide.FindValue("V-003"));
        Assert.Contains("Dropped unknown fact F-99.", guide.FindValue("V-004")!.MappingNotes);
        var retainage = guide.FindValue("V-005")!;
        Assert.Equal("$18,425.00", retainage.Value);
        Assert.Equal(new[] { "F-006" }, retainage.FactIds);

        Assert.Equal(GuidePartStatus.Ready, guide.ValuesState.Status);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Values), guide.ValuesState.BasisHash);
        Assert.Equal(factsState, JsonSerializer.Serialize(guide.FactsState, CaseDraft.JsonOptions));
    }

    [Fact]
    public async Task RegenIssues_Throws()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok("{}"));
        var draft = CaseTestData.SampleGuidedDraft();

        await Assert.ThrowsAsync<ArgumentException>(() => new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Issues, "", false));
        Assert.Empty(fake.Requests);
    }

    public static TheoryData<GuidePart, bool> FailureCases() => new()
    {
        { GuidePart.Cast, false },
        { GuidePart.Cast, true },
        { GuidePart.Facts, false },
        { GuidePart.Facts, true },
        { GuidePart.Values, false },
        { GuidePart.Values, true }
    };

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task RegenTab_FailureLeavesEntriesUntouched(GuidePart part, bool fatal)
    {
        var draft = CaseTestData.SampleCounselDraft();
        StampAll(draft);
        var before = GuideJson(draft);
        var revision = draft.Revision;
        var (gateway, _) = Gateway(fatal
            ? FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key")
            : FakeLlmProvider.Refused("I won't rewrite that."));

        var result = await new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, part, "", replaceEdited: false);

        Assert.False(result.Succeeded);
        if (fatal)
        {
            Assert.True(result.IsFatal);
            Assert.Contains("Invalid API key", result.FatalError);
        }
        else
        {
            Assert.True(result.WasRefused);
            Assert.Equal("The model declined: I won't rewrite that.", result.FailureMessage);
        }

        Assert.Equal(before, GuideJson(draft));
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public async Task RegenTab_UserCancelLeavesEntriesUntouched()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var before = GuideJson(draft);
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(Roster(BasePeople())));
        using var cts = new CancellationTokenSource();
        fake.OnSend = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CaseGuideGenerator(gateway).RegenerateTabAsync(draft, GuidePart.Cast, "", false, null, cts.Token));

        Assert.Equal(before, GuideJson(draft));
    }
}
