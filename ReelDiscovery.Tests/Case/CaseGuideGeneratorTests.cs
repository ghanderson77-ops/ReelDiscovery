using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

public class CaseGuideGeneratorTests
{
    private const string IssueDetailsJson = """
        {
          "issues": [
            { "issueId": "ISS-1", "description": "Harmon refused to pay approved change orders.", "elements": ["A valid subcontract", "Brightline performed", "Harmon failed to pay", "Damages"], "relevanceCriteria": "Emails about change order approvals, invoices and payment refusals." }
          ]
        }
        """;

    private const string IssueDetailsUnknownJson = """
        {
          "issues": [
            { "issueId": "ISS-01", "description": "Harmon refused to pay approved change orders.", "elements": ["A valid subcontract"], "relevanceCriteria": "Payment emails." },
            { "issueId": "ISS-09", "description": "Invented issue.", "elements": ["Nothing"], "relevanceCriteria": "Nothing." }
          ]
        }
        """;

    private const string IssueDetailsSecondJson = """
        {
          "issues": [
            { "issueId": "ISS-02", "description": "Harmon kept the benefit of the changed work.", "elements": ["Benefit conferred", "Unjust retention"], "relevanceCriteria": "Emails about the upper-floor work and its value." }
          ]
        }
        """;

    private static (LlmGateway Gateway, FakeLlmProvider Fake) Gateway(params LlmProviderResponse[] responses)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        foreach (var response in responses)
            fake.Responses.Enqueue(response);
        return (gateway, fake);
    }

    private static CaseDraft TwoCountDraft()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Counts.Add(new CaseCount
        {
            Id = "count-2",
            Number = 2,
            Title = "Unjust Enrichment",
            AgainstPartyIds = new List<string> { "party-2" },
            LegalBasis = "Illinois common law",
            Summary = "Harmon kept the benefit of the changed work without paying for it."
        });
        return draft;
    }

    // ---------- Prompts ----------

    [Fact]
    public void SystemPrompt_KeepsNames()
    {
        // D-01: names from films, TV series, books or games are kept exactly
        Assert.Contains("including characters and companies from films, TV series, books or games", CaseGuidePrompts.SystemPrompt);
        Assert.Contains("never rename them", CaseGuidePrompts.SystemPrompt);
        Assert.False(Regex.IsMatch(CaseGuidePrompts.SystemPrompt, @"every (person|character|company)[^.]*fictional", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void SystemPrompt_FirmPosition()
    {
        // D-02
        Assert.Contains("Take a firm position", CaseGuidePrompts.SystemPrompt);
        Assert.Contains("Do not hedge", CaseGuidePrompts.SystemPrompt);
    }

    [Fact]
    public void IssueDetailsPrompt_NameRules()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var issues = IssueExtractor.Extract(draft.CoreDocument!, new CaseGuide());

        var prompt = CaseGuidePrompts.BuildIssueDetailsPrompt(draft, issues);

        Assert.Contains("Use these fictional names exactly: Brightline Glazing", prompt);
        Assert.Contains("Never use these real names: Acme Windows", prompt);
        Assert.Matches(@"The user has approved keeping these names exactly as written[^\n]*Real Person", prompt);
        Assert.Contains("Keep every name the document uses exactly as written", prompt);
        Assert.Contains("ISS-01", prompt);
        Assert.Contains("ISS-01 | Count I | Breach of Contract | legal basis: Illinois common law", prompt);
        Assert.Contains("Harmon breached the subcontract by failing to pay for approved change order work", prompt);
    }

    [Fact]
    public void CounselDto_NoLongerAsksForExampleDomain()
    {
        var type = typeof(CoreDocumentGenerator).GetNestedType("CounselDto", BindingFlags.NonPublic)!;
        var description = type.GetProperty("Email")!.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.DoesNotContain("at an .example domain", description);
        Assert.Contains("own .com domain", description);
    }

    // ---------- Issues ----------

    [Fact]
    public async Task Issues_ExtractThenEnrich()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson));
        var draft = CaseTestData.SampleComplaintDraft();
        var progress = new CaseTestData.SyncProgress<GuideProgress>();

        var result = await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft, progress);

        var guide = draft.Guide!;
        var issue = Assert.Single(guide.Issues);
        Assert.Equal("ISS-01", issue.Id);
        Assert.Equal("count-1", issue.CountId);
        Assert.Equal("Harmon refused to pay approved change orders.", issue.Description);
        Assert.Equal(4, issue.Elements.Count);
        Assert.Equal("Emails about change order approvals, invoices and payment refusals.", issue.RelevanceCriteria);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("claude-story", request.ModelId);
        Assert.Equal(CaseGuideGenerator.IssueDetailsMaxOutputTokens, request.MaxTokens);
        Assert.Equal(8_000, request.MaxTokens);
        Assert.Equal(CaseGuidePrompts.SystemPrompt, request.SystemPrompt);
        CaseTestData.AssertNoJsonInstructions(request);
        Assert.DoesNotContain(draft.EffectiveScenario, request.SystemPrompt);

        Assert.Equal(GuidePartStatus.Ready, guide.IssuesState.Status);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Issues), guide.IssuesState.BasisHash);
        Assert.Equal(GuidePartStatus.Ready, guide.IssueDetailsState.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.IsFatal);
        Assert.Equal(GuidePart.Issues, result.Part);
        Assert.Contains(progress.Reports, p => p.Message == "Extracting the issues from the counts...");
        Assert.Contains(progress.Reports, p => p.Message == "Writing the issue details...");
    }

    [Fact]
    public async Task Issues_OperationName()
    {
        var gateway = new RecordingGateway(Gateway(FakeLlmProvider.Ok(IssueDetailsJson)).Gateway);

        await new CaseGuideGenerator(gateway).GenerateIssuesAsync(CaseTestData.SampleComplaintDraft());

        var request = Assert.Single(gateway.Requests);
        Assert.Equal(LlmTier.Story, request.Tier);
        Assert.Equal("Case Guide - Issue details", request.OperationName);
    }

    [Fact]
    public async Task Issues_MemoProgressSaysFindings()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson));
        var progress = new CaseTestData.SyncProgress<GuideProgress>();

        await new CaseGuideGenerator(gateway).GenerateIssuesAsync(CaseTestData.SampleMemoDraft(), progress);

        Assert.Contains(progress.Reports, p => p.Message == "Extracting the issues from the findings...");
    }

    [Fact]
    public async Task Issues_DetailsUnknownIdIgnored()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(IssueDetailsUnknownJson));
        var draft = CaseTestData.SampleComplaintDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft);

        Assert.Single(draft.Guide!.Issues);
        Assert.Contains(result.Notes, n => n.Contains("ISS-09"));
        Assert.Equal(GuidePartStatus.Ready, draft.Guide.IssueDetailsState.Status);
    }

    [Fact]
    public async Task Issues_EnrichRefused()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Refused("I can't help with that."));
        var draft = CaseTestData.SampleComplaintDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft);

        var guide = draft.Guide!;
        Assert.Single(guide.Issues);
        Assert.Equal(GuidePartStatus.Ready, guide.IssuesState.Status);
        Assert.Equal(GuidePartStatus.Failed, guide.IssueDetailsState.Status);
        Assert.StartsWith("The model declined: ", guide.IssueDetailsState.FailureMessage);
        Assert.True(result.WasRefused);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Issues_EnrichTruncated()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Truncated(), FakeLlmProvider.Truncated());
        var draft = CaseTestData.SampleComplaintDraft();

        await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft);

        Assert.Equal(GuidePartStatus.Failed, draft.Guide!.IssueDetailsState.Status);
        Assert.Equal("The response was cut off. Click Retry Issue Details.", draft.Guide.IssueDetailsState.FailureMessage);
    }

    [Fact]
    public async Task Issues_Fatal()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));
        var draft = CaseTestData.SampleComplaintDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Equal(GuidePartStatus.Ready, draft.Guide!.IssuesState.Status);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide.IssueDetailsState.Status);
    }

    [Fact]
    public async Task Issues_UserCancel()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson));
        using var cts = new CancellationTokenSource();
        fake.OnSend = _ => cts.Cancel();
        var draft = CaseTestData.SampleComplaintDraft();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft, null, cts.Token));

        Assert.Equal(GuidePartStatus.Ready, draft.Guide!.IssuesState.Status);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide.IssueDetailsState.Status);
    }

    [Fact]
    public async Task RetryIssueDetails_SendsOnlyMissing()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson));
        var draft = TwoCountDraft();
        var generator = new CaseGuideGenerator(gateway);

        await generator.GenerateIssuesAsync(draft);

        // ISS-02 got no details: the part is Failed and can be retried
        Assert.Equal(GuidePartStatus.Failed, draft.Guide!.IssueDetailsState.Status);
        Assert.Contains("ISS-02", fake.Requests[0].UserPrompt);

        fake.Responses.Enqueue(FakeLlmProvider.Ok(IssueDetailsSecondJson));
        var result = await generator.RetryIssueDetailsAsync(draft);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Contains("ISS-02", fake.Requests[1].UserPrompt);
        Assert.DoesNotContain("ISS-01", fake.Requests[1].UserPrompt);
        Assert.Equal(GuidePartStatus.Ready, draft.Guide.IssueDetailsState.Status);
        Assert.Equal("", draft.Guide.IssueDetailsState.FailureMessage);
        Assert.True(result.Succeeded);
        Assert.All(draft.Guide.Issues, i => Assert.True(i.HasDetails));
    }

    // ---------- Cast fixtures ----------

    // Default roster for SampleComplaintDraft: covers every counsel gap (in-house counsel for ORG-01/ORG-02 and a firm with a
    // partner and an associate for each), so no top-up call happens. 10 people = one details batch.
    private const string CivilRosterJson = """
        {
          "organizations": [
            { "name": "Ellis & Park LLP", "shortName": "Ellis & Park", "kind": "LawFirm", "representsRefs": ["ORG-01"], "location": "Chicago, Illinois" },
            { "name": "Thistle & Bracken LLP", "shortName": "Thistle & Bracken", "kind": "LawFirm", "representsRefs": ["ORG-02"], "location": "Chicago, Illinois" }
          ],
          "people": [
            { "existingId": "", "firstName": "Maria", "lastName": "Delgado", "title": "President", "organizationRef": "ORG-01", "role": "Party", "involvement": "Ran the Lakeview Tower job for Brightline." },
            { "existingId": "", "firstName": "Tom", "lastName": "Becker", "title": "Project Manager", "organizationRef": "ORG-01", "role": "Party", "involvement": "Submitted the change orders." },
            { "existingId": "", "firstName": "Raymond", "lastName": "Harmon", "title": "Chief Executive Officer", "organizationRef": "ORG-02", "role": "Party", "involvement": "Refused to pay." },
            { "existingId": "", "firstName": "Lisa", "lastName": "Chen", "title": "Project Executive", "organizationRef": "ORG-02", "role": "Party", "involvement": "Approved the change orders." },
            { "existingId": "", "firstName": "Karen", "lastName": "Walsh", "title": "General Counsel", "organizationRef": "ORG-01", "role": "InHouseCounsel", "involvement": "Sent the demand letter." },
            { "existingId": "", "firstName": "Paul", "lastName": "Novak", "title": "General Counsel", "organizationRef": "ORG-02", "role": "InHouseCounsel", "involvement": "Answered the demand." },
            { "existingId": "", "firstName": "Morgan", "lastName": "Ellis", "title": "Partner", "organizationRef": "Ellis & Park LLP", "role": "OutsideCounsel", "involvement": "Signed the complaint." },
            { "existingId": "", "firstName": "Evan", "lastName": "Price", "title": "Associate", "organizationRef": "Ellis & Park LLP", "role": "OutsideCounsel", "involvement": "Drafted the complaint." },
            { "existingId": "", "firstName": "Nora", "lastName": "Quist", "title": "Partner", "organizationRef": "Thistle & Bracken LLP", "role": "OutsideCounsel", "involvement": "Defends Harmon." },
            { "existingId": "", "firstName": "Owen", "lastName": "Hale", "title": "Associate", "organizationRef": "Thistle & Bracken LLP", "role": "OutsideCounsel", "involvement": "Reviews Harmon's records." }
          ]
        }
        """;

    // Roster for Roster_Maps: ORG-1 normalization, a name match, an unknown organization and no signing attorney
    private const string MapsRosterJson = """
        {
          "organizations": [
            { "name": "Thistle & Bracken LLP", "shortName": "Thistle & Bracken", "kind": "LawFirm", "representsRefs": ["ORG-01"], "location": "Chicago, Illinois" }
          ],
          "people": [
            { "existingId": "", "firstName": "Maria", "lastName": "Delgado", "title": "President", "organizationRef": "ORG-1", "role": "Party", "involvement": "Ran the job." },
            { "existingId": "", "firstName": "Raymond", "lastName": "Harmon", "title": "Chief Executive Officer", "organizationRef": "Harmon Builders, Inc.", "role": "Party", "involvement": "Refused to pay." },
            { "existingId": "", "firstName": "Nora", "lastName": "Quist", "title": "Partner", "organizationRef": "Thistle & Bracken LLP", "role": "OutsideCounsel", "involvement": "Advises Brightline." },
            { "existingId": "", "firstName": "Ghost", "lastName": "Person", "title": "Consultant", "organizationRef": "Unknown Corp", "role": "Staff", "involvement": "Nobody." }
          ]
        }
        """;

    // Gap roster for a draft with an individual party (ORG-03 Walter White): Harmon has no in-house counsel and Walter White no firm
    private const string GapRosterJson = """
        {
          "organizations": [
            { "name": "Ellis & Park LLP", "shortName": "Ellis & Park", "kind": "LawFirm", "representsRefs": ["ORG-01"], "location": "Chicago, Illinois" },
            { "name": "Thistle & Bracken LLP", "shortName": "Thistle & Bracken", "kind": "LawFirm", "representsRefs": ["ORG-02"], "location": "Chicago, Illinois" }
          ],
          "people": [
            { "existingId": "", "firstName": "Maria", "lastName": "Delgado", "title": "President", "organizationRef": "ORG-01", "role": "Party", "involvement": "Ran the job." },
            { "existingId": "", "firstName": "Raymond", "lastName": "Harmon", "title": "Chief Executive Officer", "organizationRef": "ORG-02", "role": "Party", "involvement": "Refused to pay." },
            { "existingId": "", "firstName": "Walter", "lastName": "White", "title": "Owner", "organizationRef": "ORG-03", "role": "Party", "involvement": "Guaranteed the subcontract." },
            { "existingId": "", "firstName": "Karen", "lastName": "Walsh", "title": "General Counsel", "organizationRef": "ORG-01", "role": "InHouseCounsel", "involvement": "Sent the demand letter." },
            { "existingId": "", "firstName": "Morgan", "lastName": "Ellis", "title": "Partner", "organizationRef": "Ellis & Park LLP", "role": "OutsideCounsel", "involvement": "Signed the complaint." },
            { "existingId": "", "firstName": "Evan", "lastName": "Price", "title": "Associate", "organizationRef": "Ellis & Park LLP", "role": "OutsideCounsel", "involvement": "Drafted the complaint." },
            { "existingId": "", "firstName": "Nora", "lastName": "Quist", "title": "Partner", "organizationRef": "Thistle & Bracken LLP", "role": "OutsideCounsel", "involvement": "Defends Harmon." },
            { "existingId": "", "firstName": "Owen", "lastName": "Hale", "title": "Associate", "organizationRef": "Thistle & Bracken LLP", "role": "OutsideCounsel", "involvement": "Reviews records." }
          ]
        }
        """;

    private const string TopUpJson = """
        {
          "organizations": [
            { "name": "Okafor & Lind LLP", "shortName": "Okafor & Lind", "kind": "LawFirm", "representsRefs": ["ORG-03"], "location": "Albuquerque, New Mexico" }
          ],
          "people": [
            { "existingId": "", "firstName": "Paul", "lastName": "Novak", "title": "General Counsel", "organizationRef": "ORG-02", "role": "InHouseCounsel", "involvement": "Answered the demand." },
            { "existingId": "", "firstName": "Ada", "lastName": "Okafor", "title": "Partner", "organizationRef": "Okafor & Lind LLP", "role": "OutsideCounsel", "involvement": "Represents Walter White." },
            { "existingId": "", "firstName": "Ben", "lastName": "Lind", "title": "Associate", "organizationRef": "Okafor & Lind LLP", "role": "OutsideCounsel", "involvement": "Assists on the White matter." }
          ]
        }
        """;

    private const string EmptyRosterJson = """{ "organizations": [], "people": [] }""";

    private const string BackgroundJson = """
        {
          "people": [
            { "firstName": "Gina", "lastName": "Morales", "title": "Receptionist", "department": "Administration", "role": "Answers phones and books meetings.", "activeFrom": "2024-01-08", "activeTo": "2024-09-16" },
            { "firstName": "Hank", "lastName": "Oyelaran", "title": "Accounts Payable Clerk", "department": "Accounting", "role": "Processes vendor invoices.", "activeFrom": "2024-01-08", "activeTo": "2024-09-16" },
            { "firstName": "Iris", "lastName": "Paulsen", "title": "IT Technician", "department": "Information Technology", "role": "Handles laptops and accounts.", "activeFrom": "2024-02-01", "activeTo": "2024-09-16" }
          ]
        }
        """;

    // Details for CH-from..CH-to; ids outside the batch are ignored with a note
    private static string DetailsJson(int from, int to, string badDateId = "")
    {
        var people = Enumerable.Range(from, to - from + 1).Select(n =>
        {
            var id = $"CH-{n:D3}";
            var activeFrom = id == badDateId ? "2024-13-45" : "2024-01-15";
            return $$"""{ "castId": "{{id}}", "summary": "Character {{id}} took part in the change order dispute.", "activeFrom": "{{activeFrom}}", "activeTo": "2024-08-30", "voiceNote": "Short, direct emails.", "signatureStyle": "Short" }""";
        });
        return "{ \"people\": [" + string.Join(", ", people) + "] }";
    }

    private static CaseDraft IndividualPartyDraft()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Parties.Add(new CaseParty
        {
            Id = "party-3",
            Name = "Walter White",
            ShortName = "White",
            Role = PartyRole.Defendant,
            EntityType = "individual",
            Domicile = "Albuquerque, New Mexico",
            Description = "Personal guarantor of Harmon's obligations."
        });
        return draft;
    }

    private static int NonIndividualOrgs(CaseDraft draft) =>
        draft.Guide!.Organizations.Count(o => o.Kind != OrganizationKind.Individual);

    private static bool IsDetailsRequest(LlmProviderRequest r) => r.UserPrompt.Contains("For each CH ID");

    private static bool IsBackgroundRequest(LlmProviderRequest r) => r.UserPrompt.Contains("background staff for");

    // ---------- Organizations ----------

    [Fact]
    public void SyncOrganizations_Seed()
    {
        var draft = IndividualPartyDraft();
        draft.CoreDocument!.Parties.Add(new CaseParty
        {
            Id = "party-4",
            Name = "Department of Justice",
            ShortName = "DOJ",
            Role = PartyRole.ThirdParty,
            EntityType = "federal agency",
            Domicile = "Washington, District of Columbia"
        });

        var added = CaseGuideGenerator.SyncOrganizations(draft);

        var orgs = draft.Guide!.Organizations;
        Assert.Equal(new[] { "ORG-01", "ORG-02", "ORG-03", "ORG-04" }, added);
        Assert.Equal("Brightline Glazing, LLC", orgs[0].Name);
        Assert.Equal(OrganizationKind.Party, orgs[0].Kind);
        Assert.Equal("party-1", orgs[0].PartyId);
        Assert.Equal("brightline.com", orgs[0].Domain);
        Assert.Equal("America/Chicago", orgs[0].TimeZoneId);
        Assert.Equal("Chicago, Illinois", orgs[0].Location);
        Assert.Equal("Harmon Builders, Inc.", orgs[1].Name);
        Assert.Equal(OrganizationKind.Individual, orgs[2].Kind);
        Assert.Equal("whitefamily.com", orgs[2].Domain);
        Assert.Equal(OrganizationKind.Agency, orgs[3].Kind);
        Assert.EndsWith(".gov", orgs[3].Domain);
    }

    [Fact]
    public void SyncOrganizations_Resync()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        CaseGuideGenerator.SyncOrganizations(draft);
        var guide = draft.Guide!;

        Assert.Empty(CaseGuideGenerator.SyncOrganizations(draft));
        Assert.Equal(2, guide.Organizations.Count);

        draft.CoreDocument!.Parties[1].Name = "Harmon Construction Group, Inc.";
        draft.CoreDocument.Parties[1].Domicile = "Evanston, Illinois";
        Assert.Empty(CaseGuideGenerator.SyncOrganizations(draft));
        var harmon = guide.FindOrganization("ORG-02")!;
        Assert.Equal("Harmon Construction Group, Inc.", harmon.Name);
        Assert.Equal("Harmon", harmon.ShortName);
        Assert.Equal("Evanston, Illinois", harmon.Location);
        Assert.Equal("harmon.com", harmon.Domain);

        guide.Organizations.Add(new CaseOrganization
        {
            Id = IdAllocator.Next(guide, IdAllocator.Organization),
            Name = "Ellis & Park LLP",
            Kind = OrganizationKind.LawFirm,
            Domain = "ellispark.com"
        });
        draft.CoreDocument.Parties.Add(new CaseParty
        {
            Id = "party-3",
            Name = "Lakeview Tower Owner LLC",
            ShortName = "Lakeview Owner",
            Role = PartyRole.ThirdParty,
            EntityType = "Illinois limited liability company",
            Domicile = "Chicago, Illinois"
        });

        var added = CaseGuideGenerator.SyncOrganizations(draft);

        Assert.Equal(new[] { "ORG-04" }, added);
        var owner = guide.FindOrganization("ORG-04")!;
        Assert.Equal("party-3", owner.PartyId);
        Assert.False(string.IsNullOrEmpty(owner.Domain));
        Assert.Equal(guide.Organizations.Count, guide.Organizations.Select(o => o.Domain).Distinct().Count());
    }

    // ---------- Prompts (cast) ----------

    [Fact]
    public void NameRules_EveryCastPrompt()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        CaseGuideGenerator.SyncOrganizations(draft);
        var guide = draft.Guide!;
        var member = new CastMember { Id = "CH-001", FirstName = "Maria", LastName = "Delgado", Title = "President", OrganizationId = "ORG-01", Role = CastRole.Party };
        guide.Cast.Add(member);

        var prompts = new[]
        {
            CaseGuidePrompts.BuildRosterPrompt(draft),
            CaseGuidePrompts.BuildCastDetailsPrompt(draft, new[] { member }),
            CaseGuidePrompts.BuildBackgroundPrompt(draft, guide.Organizations[0], 3),
            CaseGuidePrompts.BuildCounselTopUpPrompt(draft, new[] { "ORG-02 Harmon Builders, Inc. needs in-house counsel" })
        };

        Assert.All(prompts, p =>
        {
            Assert.Contains("Keep every name the document uses exactly as written", p);
            Assert.Contains("The user has approved keeping these names exactly as written", p);
            Assert.Contains("Never use these real names: Acme Windows", p);
        });
    }

    [Fact]
    public async Task RosterPrompt()
    {
        var (inner, fake) = Gateway(FakeLlmProvider.Ok(CivilRosterJson), FakeLlmProvider.Ok(DetailsJson(1, 10)), FakeLlmProvider.Ok(BackgroundJson));
        var gateway = new RecordingGateway(inner);
        var draft = CaseTestData.SampleComplaintDraft();

        await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var roster = fake.Requests[0];
        Assert.Contains("Name 6-10 key people who are not counsel", roster.UserPrompt);
        Assert.Contains("ORG-01 Brightline Glazing, LLC: one in-house counsel, and an outside law firm with at least one partner and one associate", roster.UserPrompt);
        Assert.Contains("ORG-02 Harmon Builders, Inc.: one in-house counsel", roster.UserPrompt);
        Assert.Contains("Morgan Ellis of Ellis & Park LLP signed the document and must be in the cast as outside counsel.", roster.UserPrompt);
        Assert.Contains("ORG-01 | Brightline Glazing, LLC | Party | Chicago, Illinois", roster.UserPrompt);
        Assert.Contains("Harmon engaged Brightline to furnish and install the window systems", roster.UserPrompt);
        Assert.Contains("Keep every name the document uses exactly as written", roster.UserPrompt);
        Assert.Equal(12_000, roster.MaxTokens);
        Assert.Equal(CaseGuidePrompts.SystemPrompt, roster.SystemPrompt);
        CaseTestData.AssertNoJsonInstructions(roster);
        Assert.Equal("Case Guide - Cast roster", gateway.Requests[0].OperationName);
        Assert.All(gateway.Requests, r => Assert.Equal(LlmTier.Story, r.Tier));
        // Default roster covers every counsel gap: no top-up
        Assert.DoesNotContain(gateway.Requests, r => r.OperationName == "Case Guide - Counsel top-up");
    }

    [Fact]
    public void RosterPrompt_IndividualParty()
    {
        var draft = IndividualPartyDraft();
        CaseGuideGenerator.SyncOrganizations(draft);

        var prompt = CaseGuidePrompts.BuildRosterPrompt(draft, "add a site superintendent");

        Assert.Contains("ORG-03 Walter White (individual): an outside law firm with at least one partner and one associate.", prompt);
        Assert.DoesNotContain("ORG-03 Walter White: one in-house counsel", prompt);
        Assert.Contains("Guidance from the user: add a site superintendent", prompt);
    }

    // ---------- Roster ----------

    [Fact]
    public async Task Roster_Maps()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(MapsRosterJson),
            FakeLlmProvider.Ok(EmptyRosterJson),
            FakeLlmProvider.Refused("No details."),
            FakeLlmProvider.Ok(BackgroundJson));
        var draft = CaseTestData.SampleComplaintDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var guide = draft.Guide!;
        Assert.Contains("Dropped Ghost Person: unknown organization Unknown Corp.", result.Notes);
        Assert.DoesNotContain(guide.Cast, c => c.LastName == "Person");

        var firm = guide.FindOrganization("ORG-03")!;
        Assert.Equal("Thistle & Bracken LLP", firm.Name);
        Assert.Equal(OrganizationKind.LawFirm, firm.Kind);
        Assert.Equal("thistlebracken.com", firm.Domain);
        Assert.Equal(new[] { "ORG-01" }, firm.RepresentsOrgIds);

        var maria = guide.FindCast("CH-001")!;
        Assert.Equal("Maria Delgado", maria.FullName);
        Assert.Equal("ORG-01", maria.OrganizationId);
        Assert.Equal("maria.delgado@brightline.com", maria.Email);
        Assert.Equal(CastTier.Key, maria.Tier);
        Assert.True(maria.DetailsPending);
        Assert.Matches(@"^\(555\) 555-01\d\d$", maria.Phone);

        var raymond = guide.FindCast("CH-002")!;
        Assert.Equal("ORG-02", raymond.OrganizationId);
        Assert.Equal("raymond.harmon@harmon.com", raymond.Email);

        var nora = guide.FindCast("CH-003")!;
        Assert.Equal("ORG-03", nora.OrganizationId);
        Assert.Equal("nora.quist@thistlebracken.com", nora.Email);
        Assert.Equal(CastRole.OutsideCounsel, nora.Role);

        // Roster had gaps (no in-house counsel, no associate): exactly one top-up
        Assert.Equal(1, fake.Requests.Count(r => r.UserPrompt.Contains("Add only these missing counsel")));
        Assert.All(guide.Cast.Where(c => c.Tier == CastTier.Key), c => Assert.False(string.IsNullOrEmpty(c.Email)));
    }

    [Fact]
    public async Task Roster_SeedsSigningAttorney()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(MapsRosterJson),
            FakeLlmProvider.Ok(EmptyRosterJson),
            FakeLlmProvider.Refused("No details."),
            FakeLlmProvider.Ok(BackgroundJson));
        var draft = CaseTestData.SampleComplaintDraft();

        await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var guide = draft.Guide!;
        var ellis = Assert.Single(guide.Cast, c => c.FullName == "Morgan Ellis");
        Assert.Equal(CastRole.OutsideCounsel, ellis.Role);
        Assert.Equal("Partner", ellis.Title);
        Assert.Equal(CastTier.Key, ellis.Tier);
        var firm = guide.FindOrganization(ellis.OrganizationId)!;
        Assert.Equal("Ellis & Park LLP", firm.Name);
        Assert.Equal(OrganizationKind.LawFirm, firm.Kind);
        Assert.Contains("ORG-01", firm.RepresentsOrgIds);
        Assert.Equal(ellis.Email, draft.CoreDocument!.Counsel.Email);
        Assert.Equal("morgan.ellis@ellispark.com", ellis.Email);
    }

    [Fact]
    public async Task CounselTopUp()
    {
        var (inner, fake) = Gateway(
            FakeLlmProvider.Ok(GapRosterJson),
            FakeLlmProvider.Ok(TopUpJson),
            FakeLlmProvider.Ok(DetailsJson(1, 10)),
            FakeLlmProvider.Ok(DetailsJson(11, 20)),
            FakeLlmProvider.Ok(BackgroundJson));
        var gateway = new RecordingGateway(inner);
        var draft = IndividualPartyDraft();

        await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var topUps = gateway.Requests.Where(r => r.OperationName == "Case Guide - Counsel top-up").ToList();
        var topUp = Assert.Single(topUps);
        Assert.Contains("ORG-02 Harmon Builders, Inc. needs in-house counsel", topUp.UserPrompt);
        Assert.Contains("ORG-03 Walter White (individual) needs an outside law firm with a partner and an associate", topUp.UserPrompt);
        Assert.DoesNotContain("ORG-01 Brightline Glazing, LLC needs", topUp.UserPrompt);
        Assert.Contains("Keep every name the document uses exactly as written", topUp.UserPrompt);
        Assert.Equal(CaseGuideGenerator.RosterMaxOutputTokens, topUp.MaxOutputTokens);

        var guide = draft.Guide!;
        var novak = Assert.Single(guide.Cast, c => c.FullName == "Paul Novak");
        Assert.Equal(CastRole.InHouseCounsel, novak.Role);
        Assert.Equal("ORG-02", novak.OrganizationId);
        var okafor = guide.Organizations.Single(o => o.Name == "Okafor & Lind LLP");
        Assert.Equal(new[] { "ORG-03" }, okafor.RepresentsOrgIds);
        Assert.Empty(CaseGuideGenerator.CounselGaps(draft));
        Assert.Equal(2, fake.Requests.Count(IsDetailsRequest));
    }

    [Fact]
    public async Task CounselTopUp_NeverLoops()
    {
        var (inner, _) = Gateway(
            FakeLlmProvider.Ok(GapRosterJson),
            FakeLlmProvider.Ok(EmptyRosterJson),
            FakeLlmProvider.Ok(DetailsJson(1, 10)),
            FakeLlmProvider.Ok(BackgroundJson));
        var gateway = new RecordingGateway(inner);
        var draft = IndividualPartyDraft();

        await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        Assert.Single(gateway.Requests, r => r.OperationName == "Case Guide - Counsel top-up");
        // Remaining gaps surface as validator warnings
        Assert.NotEmpty(CaseGuideGenerator.CounselGaps(draft));
    }

    // ---------- Details ----------

    [Fact]
    public async Task Details_Batched()
    {
        var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        var slow = new SlowProvider(fake);
        var gateway = new LlmGateway(slow, CaseTestData.StoryModel(), CaseTestData.VolumeModel());
        // Same response for every batch: ids outside the batch are ignored
        fake.Responses.Enqueue(FakeLlmProvider.Ok(DetailsJson(1, 23, badDateId: "CH-005")));
        fake.Responses.Enqueue(FakeLlmProvider.Ok(DetailsJson(1, 23, badDateId: "CH-005")));
        fake.Responses.Enqueue(FakeLlmProvider.Ok(DetailsJson(1, 23, badDateId: "CH-005")));
        fake.Responses.Enqueue(FakeLlmProvider.Ok(BackgroundJson));

        var draft = CaseTestData.SampleComplaintDraft();
        CaseGuideGenerator.SyncOrganizations(draft);
        var guide = draft.Guide!;
        for (var i = 0; i < 23; i++)
        {
            guide.Cast.Add(new CastMember
            {
                Id = IdAllocator.Next(guide, IdAllocator.Character),
                FirstName = "Person",
                LastName = $"Number{i + 1}",
                Title = "Estimator",
                OrganizationId = i % 2 == 0 ? "ORG-01" : "ORG-02",
                Tier = CastTier.Key,
                Role = CastRole.Party,
                Involvement = "Worked on the project.",
                DetailsPending = true
            });
        }

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var details = fake.Requests.Where(IsDetailsRequest).ToList();
        Assert.Equal(3, details.Count);
        Assert.Equal(new[] { 3, 10, 10 }, details.Select(r => Regex.Matches(r.UserPrompt, @"^CH-\d{3} \|", RegexOptions.Multiline).Count).OrderBy(n => n));
        var batchIds = details.Select(r => Regex.Matches(r.UserPrompt, @"^(CH-\d{3}) \|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList()).ToList();
        Assert.Equal(23, batchIds.SelectMany(b => b).Distinct().Count());
        Assert.Contains(batchIds, b => b.SequenceEqual(Enumerable.Range(1, 10).Select(n => $"CH-{n:D3}")));
        Assert.InRange(slow.MaxConcurrent, 2, CaseGuideGenerator.MaxParallelCalls);
        Assert.DoesNotContain(fake.Requests, r => r.UserPrompt.Contains("Name 6-10 key people"));
        Assert.DoesNotContain(fake.Requests, r => r.UserPrompt.Contains("Add only these missing counsel"));
        Assert.Equal(NonIndividualOrgs(draft), fake.Requests.Count(IsBackgroundRequest));

        var key = guide.Cast.Where(c => c.Tier == CastTier.Key).ToList();
        Assert.All(key, c => Assert.False(c.DetailsPending));
        var first = guide.FindCast("CH-001")!;
        Assert.Equal("2024-01-15", first.ActiveFrom);
        Assert.Equal("2024-08-30", first.ActiveTo);
        Assert.Equal("Character CH-001 took part in the change order dispute.", first.Involvement);
        Assert.Equal("Short, direct emails.", first.VoiceNote);
        Assert.Equal(SignatureStyle.Short, first.SignatureStyle);
        Assert.Equal(SignatureBuilder.Build(first, guide.FindOrganization(first.OrganizationId)), first.Signature);
        Assert.Contains("Brightline Glazing, LLC", first.Signature);

        // Invalid dates default to the case timeline with a note
        var bad = guide.FindCast("CH-005")!;
        Assert.Equal("2024-01-08", bad.ActiveFrom);
        Assert.Equal("2024-09-16", bad.ActiveTo);
        Assert.Contains(result.Notes, n => n.Contains("CH-005"));
        Assert.Equal(GuidePartStatus.Ready, guide.CastState.Status);
    }

    [Fact]
    public async Task Background_PerOrg()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(CivilRosterJson),
            FakeLlmProvider.Refused("No details."),
            FakeLlmProvider.Ok(BackgroundJson));
        var draft = CaseTestData.SampleComplaintDraft();

        await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var guide = draft.Guide!;
        var orgs = guide.Organizations.Where(o => o.Kind != OrganizationKind.Individual).ToList();
        var background = fake.Requests.Where(IsBackgroundRequest).ToList();
        Assert.Equal(orgs.Count, background.Count);
        Assert.Equal(4, orgs.Count);
        Assert.All(background, r =>
        {
            Assert.Contains("Write 3 background staff", r.UserPrompt);
            Assert.Equal(8_000, r.MaxTokens);
            Assert.Contains("Keep every name the document uses exactly as written", r.UserPrompt);
        });

        foreach (var org in orgs)
        {
            var staff = guide.Cast.Where(c => c.OrganizationId == org.Id && c.Tier == CastTier.Background).ToList();
            Assert.Equal(3, staff.Count);
            Assert.All(staff, s =>
            {
                Assert.Equal(CastRole.Staff, s.Role);
                Assert.False(string.IsNullOrEmpty(s.Department));
                Assert.Equal("", s.Signature);
                Assert.EndsWith("@" + org.Domain, s.Email);
            });
            Assert.Contains(staff, s => s.Involvement == "Processes vendor invoices.");
            Assert.Contains(org.Id, guide.CastState.CompletedUnits);
        }

        // Details were refused: the part is Failed with the key characters still pending
        Assert.Equal(GuidePartStatus.Failed, guide.CastState.Status);
    }

    [Fact]
    public async Task Cast_Ready()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(CivilRosterJson),
            FakeLlmProvider.Ok(DetailsJson(1, 10)),
            FakeLlmProvider.Ok(BackgroundJson));
        var draft = CaseTestData.SampleComplaintDraft();
        var progress = new CaseTestData.SyncProgress<GuideProgress>();
        var revision = draft.Revision;

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft, progress);

        var guide = draft.Guide!;
        Assert.True(result.Succeeded);
        Assert.False(result.IsFatal);
        Assert.Equal(GuidePart.Cast, result.Part);
        Assert.Equal(GuidePartStatus.Ready, guide.CastState.Status);
        Assert.Equal("", guide.CastState.FailureMessage);
        Assert.Empty(guide.CastState.CompletedUnits);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Cast), guide.CastState.BasisHash);
        Assert.True(draft.Revision > revision);

        var key = guide.Cast.TakeWhile(c => c.Tier == CastTier.Key).ToList();
        Assert.Equal(10, key.Count);
        Assert.Equal(key.Select(c => c.Id).OrderBy(id => id, StringComparer.Ordinal), key.Select(c => c.Id));
        var background = guide.Cast.Skip(key.Count).ToList();
        Assert.All(background, c => Assert.Equal(CastTier.Background, c.Tier));
        var orgOrder = guide.Organizations.Select(o => o.Id).ToList();
        Assert.Equal(background.Select(c => orgOrder.IndexOf(c.OrganizationId)).OrderBy(i => i), background.Select(c => orgOrder.IndexOf(c.OrganizationId)));
        Assert.Equal(12, background.Count);
        Assert.Empty(CaseGuideGenerator.CounselGaps(draft));
        Assert.Equal("morgan.ellis@ellispark.com", draft.CoreDocument!.Counsel.Email);

        Assert.Contains(progress.Reports, p => p.Message == "Writing the cast: organizations and key characters...");
        Assert.Contains(progress.Reports, p => p.Message == "Writing the cast: details for characters 1-10 of 10...");
        Assert.Contains(progress.Reports, p => p.Message == "Writing the cast: background staff for Brightline Glazing, LLC (1 of 4)...");
    }

    [Fact]
    public async Task Cast_RosterRefused()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Refused("I won't write that."));
        var draft = CaseTestData.SampleComplaintDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var guide = draft.Guide!;
        Assert.Equal(GuidePartStatus.Failed, guide.CastState.Status);
        Assert.Equal("The model declined: I won't write that.", guide.CastState.FailureMessage);
        Assert.True(result.WasRefused);
        Assert.False(result.Succeeded);
        Assert.Empty(guide.Cast);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Cast_DetailsBatchFails_ThenRetryResumes()
    {
        // 20 key people: Morgan Ellis plus 19 more -> details batches of 10 and 10
        var roster = CivilRosterJson.Replace("""
            "people": [
            """, """
            "people": [
                { "existingId": "", "firstName": "Extra", "lastName": "One", "title": "Estimator", "organizationRef": "ORG-01", "role": "Staff", "involvement": "Priced the work." },
                { "existingId": "", "firstName": "Extra", "lastName": "Two", "title": "Estimator", "organizationRef": "ORG-01", "role": "Staff", "involvement": "Priced the work." },
                { "existingId": "", "firstName": "Extra", "lastName": "Three", "title": "Estimator", "organizationRef": "ORG-01", "role": "Staff", "involvement": "Priced the work." },
                { "existingId": "", "firstName": "Extra", "lastName": "Four", "title": "Estimator", "organizationRef": "ORG-01", "role": "Staff", "involvement": "Priced the work." },
                { "existingId": "", "firstName": "Extra", "lastName": "Five", "title": "Estimator", "organizationRef": "ORG-01", "role": "Staff", "involvement": "Priced the work." },
                { "existingId": "", "firstName": "Extra", "lastName": "Six", "title": "Scheduler", "organizationRef": "ORG-02", "role": "Staff", "involvement": "Kept the schedule." },
                { "existingId": "", "firstName": "Extra", "lastName": "Seven", "title": "Scheduler", "organizationRef": "ORG-02", "role": "Staff", "involvement": "Kept the schedule." },
                { "existingId": "", "firstName": "Extra", "lastName": "Eight", "title": "Scheduler", "organizationRef": "ORG-02", "role": "Staff", "involvement": "Kept the schedule." },
                { "existingId": "", "firstName": "Extra", "lastName": "Nine", "title": "Scheduler", "organizationRef": "ORG-02", "role": "Staff", "involvement": "Kept the schedule." },
                { "existingId": "", "firstName": "Extra", "lastName": "Ten", "title": "Scheduler", "organizationRef": "ORG-02", "role": "Staff", "involvement": "Kept the schedule." },
            """);
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(roster),
            FakeLlmProvider.Ok(DetailsJson(1, 20)),
            FakeLlmProvider.Truncated(),
            FakeLlmProvider.Truncated(),
            FakeLlmProvider.Ok(BackgroundJson));
        var draft = CaseTestData.SampleComplaintDraft();
        var generator = new CaseGuideGenerator(gateway);

        var result = await generator.GenerateCastAsync(draft);

        var guide = draft.Guide!;
        Assert.Equal(GuidePartStatus.Failed, guide.CastState.Status);
        Assert.Equal("Details for 10 characters were not written. Click Retry Cast.", guide.CastState.FailureMessage);
        Assert.False(result.Succeeded);
        Assert.Equal(10, guide.Cast.Count(c => c.Tier == CastTier.Key && !c.DetailsPending));
        Assert.Equal(10, guide.Cast.Count(c => c.Tier == CastTier.Key && c.DetailsPending));
        Assert.Equal(NonIndividualOrgs(draft), guide.CastState.CompletedUnits.Count);
        var before = fake.Requests.Count;

        fake.Responses.Enqueue(FakeLlmProvider.Ok(DetailsJson(1, 20)));
        var retry = await generator.GenerateCastAsync(draft);

        var sent = fake.Requests.Skip(before).ToList();
        var request = Assert.Single(sent);
        Assert.True(IsDetailsRequest(request));
        Assert.Contains("CH-011 |", request.UserPrompt);
        Assert.DoesNotContain("CH-001 |", request.UserPrompt);
        Assert.True(retry.Succeeded);
        Assert.Equal(GuidePartStatus.Ready, guide.CastState.Status);
        Assert.All(guide.Cast.Where(c => c.Tier == CastTier.Key), c => Assert.False(c.DetailsPending));
    }

    [Fact]
    public async Task Cast_Fatal()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(CivilRosterJson),
            FakeLlmProvider.Ok(DetailsJson(1, 10)),
            FakeLlmProvider.Failed(LlmFailureKind.SpendLimit, "Spend limit reached"));
        var draft = CaseTestData.SampleComplaintDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateCastAsync(draft);

        var guide = draft.Guide!;
        Assert.True(result.IsFatal);
        Assert.Contains("Spend limit reached", result.FatalError);
        Assert.Equal(GuidePartStatus.Pending, guide.CastState.Status);
        // Completed work is kept: key characters with details; later background calls were never sent
        Assert.Equal(10, guide.Cast.Count(c => c.Tier == CastTier.Key && !c.DetailsPending));
        Assert.Single(fake.Requests, IsBackgroundRequest);
        Assert.DoesNotContain(guide.Cast, c => c.Tier == CastTier.Background);
    }

    [Fact]
    public async Task Cast_UserCancel()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(CivilRosterJson),
            FakeLlmProvider.Ok(DetailsJson(1, 10)),
            FakeLlmProvider.Ok(BackgroundJson));
        using var cts = new CancellationTokenSource();
        fake.OnSend = i =>
        {
            if (i == 1)
                cts.Cancel();
        };
        var draft = CaseTestData.SampleComplaintDraft();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CaseGuideGenerator(gateway).GenerateCastAsync(draft, null, cts.Token));

        var guide = draft.Guide!;
        Assert.Equal(GuidePartStatus.Pending, guide.CastState.Status);
        Assert.Equal(10, guide.Cast.Count(c => c.Tier == CastTier.Key));
        Assert.All(guide.Cast, c => Assert.True(c.DetailsPending));
    }

    /// <summary>
    /// Delays every send so parallel calls overlap, and records the peak number in flight.
    /// </summary>
    private sealed class SlowProvider : ILlmProvider
    {
        private readonly FakeLlmProvider _inner;
        private int _current;
        private int _max;

        public SlowProvider(FakeLlmProvider inner)
        {
            _inner = inner;
        }

        public int MaxConcurrent => Volatile.Read(ref _max);

        public LlmProviderKind Kind => _inner.Kind;

        public int? MaxTokensCeiling => _inner.MaxTokensCeiling;

        public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _current);
            lock (_inner)
                _max = Math.Max(_max, now);
            try
            {
                await Task.Delay(50, ct);
                // FakeLlmProvider is not thread-safe
                lock (_inner)
                    return _inner.SendAsync(request, ct).GetAwaiter().GetResult();
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) => _inner.TestKeyAsync(modelId, ct);
    }

    /// <summary>
    /// Records the LlmRequest (tier, operation name) before passing it to the real gateway.
    /// </summary>
    private sealed class RecordingGateway : ILlmGateway
    {
        private readonly ILlmGateway _inner;

        public RecordingGateway(ILlmGateway inner)
        {
            _inner = inner;
        }

        public List<LlmRequest> Requests { get; } = new();

        public LlmProviderKind Provider => _inner.Provider;

        public ReelDiscovery.Models.AIModelConfig ResolveModel(LlmTier tier) => _inner.ResolveModel(tier);

        public Task<LlmResult<T>> GenerateAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class
        {
            lock (Requests)
                Requests.Add(new LlmRequest { Tier = request.Tier, OperationName = request.OperationName, SystemPrompt = request.SystemPrompt, UserPrompt = request.UserPrompt, MaxOutputTokens = request.MaxOutputTokens });
            return _inner.GenerateAsync<T>(request, ct);
        }
    }
}
