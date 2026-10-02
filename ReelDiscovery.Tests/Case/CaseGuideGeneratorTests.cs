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

    // CR-02: issue details interrupted by a cancel, fatal error or closed run (Pending / left Generating)
    private static CaseDraft InterruptedDetailsDraft(GuidePartStatus detailsStatus)
    {
        var draft = CaseTestData.SampleGuidedDraft();
        foreach (var issue in draft.Guide!.Issues)
        {
            issue.Description = string.Empty;
            issue.Elements.Clear();
            issue.RelevanceCriteria = string.Empty;
        }

        draft.Guide.IssueDetailsState.Status = detailsStatus;
        return draft;
    }

    [Theory]
    [InlineData(GuidePartStatus.Pending)]
    [InlineData(GuidePartStatus.Generating)]
    public async Task GenerateAsync_ResumesInterruptedIssueDetails(GuidePartStatus detailsStatus)
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson));
        var draft = InterruptedDetailsDraft(detailsStatus);
        var saved = new List<GuidePart>();

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft, null, part =>
        {
            saved.Add(part);
            return Task.CompletedTask;
        });

        var request = Assert.Single(fake.Requests);
        Assert.Contains("ISS-01", request.UserPrompt);
        Assert.Equal(GuidePartStatus.Ready, draft.Guide!.IssueDetailsState.Status);
        Assert.All(draft.Guide.Issues, i => Assert.True(i.HasDetails));
        Assert.Contains(GuidePart.Issues, saved);
        Assert.False(result.IsFatal);
        Assert.False(CaseGuideGenerator.IssueDetailsMissing(draft.Guide));
    }

    [Fact]
    public async Task GenerateAsync_FailedIssueDetailsWaitForRetryButton()
    {
        var (gateway, fake) = Gateway();
        var draft = InterruptedDetailsDraft(GuidePartStatus.Failed);

        await new CaseGuideGenerator(gateway).GenerateAsync(draft);

        Assert.Empty(fake.Requests);
        Assert.True(CaseGuideGenerator.IssueDetailsMissing(draft.Guide));
    }

    [Fact]
    public async Task GenerateAsync_InterruptedIssueDetailsFatalStops()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));
        var draft = InterruptedDetailsDraft(GuidePartStatus.Pending);
        draft.Guide!.CastState.Status = GuidePartStatus.Pending;

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Empty(result.PartsWritten);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide.CastState.Status);
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

    // Gap roster for a draft with an individual party who works for no organization (Walter White, organizationRef empty, so he
    // gets personal organization ORG-05 after the firms ORG-03/ORG-04): Harmon has no in-house counsel and Walter White no firm
    private const string GapRosterJson = """
        {
          "organizations": [
            { "name": "Ellis & Park LLP", "shortName": "Ellis & Park", "kind": "LawFirm", "representsRefs": ["ORG-01"], "location": "Chicago, Illinois" },
            { "name": "Thistle & Bracken LLP", "shortName": "Thistle & Bracken", "kind": "LawFirm", "representsRefs": ["ORG-02"], "location": "Chicago, Illinois" }
          ],
          "people": [
            { "existingId": "", "firstName": "Maria", "lastName": "Delgado", "title": "President", "organizationRef": "ORG-01", "role": "Party", "involvement": "Ran the job." },
            { "existingId": "", "firstName": "Raymond", "lastName": "Harmon", "title": "Chief Executive Officer", "organizationRef": "ORG-02", "role": "Party", "involvement": "Refused to pay." },
            { "existingId": "", "firstName": "Walter", "lastName": "White", "title": "Owner", "organizationRef": "", "role": "Party", "involvement": "Guaranteed the subcontract." },
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
            { "name": "Okafor & Lind LLP", "shortName": "Okafor & Lind", "kind": "LawFirm", "representsRefs": ["ORG-05"], "location": "Albuquerque, New Mexico" }
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
        Assert.Equal(new[] { "ORG-01", "ORG-02", "ORG-03" }, added);
        Assert.Equal("Brightline Glazing, LLC", orgs[0].Name);
        Assert.Equal(OrganizationKind.Party, orgs[0].Kind);
        Assert.Equal("party-1", orgs[0].PartyId);
        Assert.Equal("brightline.com", orgs[0].Domain);
        Assert.Equal("America/Chicago", orgs[0].TimeZoneId);
        Assert.Equal("Chicago, Illinois", orgs[0].Location);
        Assert.Equal("Harmon Builders, Inc.", orgs[1].Name);
        // The individual party Walter White is a person, placed at an employer by the roster, never an organization
        Assert.DoesNotContain(orgs, o => o.Kind == OrganizationKind.Individual || o.Name == "Walter White" || o.PartyId == "party-3");
        Assert.Equal(OrganizationKind.Agency, orgs[2].Kind);
        Assert.EndsWith(".gov", orgs[2].Domain);
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

    [Fact]
    public void SyncOrganizations_ReorderedPartiesKeepTheirOrganizations()
    {
        // WR-03: party ids are positional; a regenerated document listing Harmon first must not rename ORG-01 into Harmon
        var draft = CaseTestData.SampleComplaintDraft();
        CaseGuideGenerator.SyncOrganizations(draft);
        var guide = draft.Guide!;
        var parties = draft.CoreDocument!.Parties;
        (parties[0].Id, parties[1].Id) = ("party-2", "party-1");
        parties.Reverse();

        var added = CaseGuideGenerator.SyncOrganizations(draft, out var modified);

        Assert.Empty(added);
        Assert.True(modified);
        var brightline = guide.FindOrganization("ORG-01")!;
        Assert.Equal("Brightline Glazing, LLC", brightline.Name);
        Assert.Equal("party-2", brightline.PartyId);
        Assert.Equal("brightline.com", brightline.Domain);
        var harmon = guide.FindOrganization("ORG-02")!;
        Assert.Equal("Harmon Builders, Inc.", harmon.Name);
        Assert.Equal("party-1", harmon.PartyId);
        Assert.Equal("harmon.com", harmon.Domain);
    }

    [Fact]
    public void SyncOrganizations_RemovedPartyIsDemoted()
    {
        // WR-10: the organization stays (cast and domain intact) but is no longer a party that needs counsel
        var draft = CaseTestData.SampleComplaintDraft();
        CaseGuideGenerator.SyncOrganizations(draft);
        draft.CoreDocument!.Parties.RemoveAt(1);

        var added = CaseGuideGenerator.SyncOrganizations(draft, out var modified);

        Assert.Empty(added);
        Assert.True(modified);
        var harmon = draft.Guide!.FindOrganization("ORG-02")!;
        Assert.Equal(OrganizationKind.ThirdParty, harmon.Kind);
        Assert.Equal(string.Empty, harmon.PartyId);
        Assert.Equal("harmon.com", harmon.Domain);
        Assert.DoesNotContain(CaseGuideGenerator.CounselGaps(draft), g => g.Contains("Harmon", StringComparison.Ordinal));
        Assert.DoesNotContain("ORG-02 Harmon", CaseGuidePrompts.BuildRosterPrompt(draft)[CaseGuidePrompts.BuildRosterPrompt(draft).IndexOf("Requirements:", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task Issues_RetitledCountFlagsLinkedFacts()
    {
        // WR-03: ISS-01 keeps its id for a retitled count-1, so the facts linked to it ask for review
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson));
        var draft = CaseTestData.SampleGuidedDraft();
        draft.CoreDocument!.Counts[0].Title = "Unjust Enrichment";

        await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft);

        var guide = draft.Guide!;
        Assert.Equal("ISS-01", Assert.Single(guide.Issues).Id);
        Assert.All(guide.Facts.Where(f => f.IssueIds.Contains("ISS-01")), f =>
        {
            Assert.True(f.NeedsReview);
            Assert.Equal("ISS-01 changed from \"Breach of Contract\" to \"Unjust Enrichment\" in the core document.", f.ReviewReason);
        });
    }

    [Fact]
    public async Task Issues_SameTitleDoesNotFlagFacts()
    {
        var (gateway, _) = Gateway();
        var draft = CaseTestData.SampleGuidedDraft();

        await new CaseGuideGenerator(gateway).GenerateIssuesAsync(draft);

        Assert.DoesNotContain(draft.Guide!.Facts, f => f.NeedsReview);
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

        // Individual parties are listed apart from the organization parties, as people placed at their employer
        Assert.Matches(@"Individual parties:\r?\n- Walter White \(White\), Defendant, individual, Albuquerque, New Mexico: Personal guarantor", prompt);
        Assert.Contains("These parties are individual people. Each must be a key character, placed at the organization they work for (an existing organization ID, or add their employer under organizations). Never create an organization named after a person.", prompt);
        var parties = prompt[prompt.IndexOf("Parties:", StringComparison.Ordinal)..prompt.IndexOf("Individual parties:", StringComparison.Ordinal)];
        Assert.DoesNotContain("Walter White", parties);
        Assert.DoesNotContain("(individual): an outside law firm", prompt);
        Assert.DoesNotContain("Walter White: one in-house counsel", prompt);
        Assert.Contains("Keep every name the document uses exactly as written", prompt);
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
        Assert.Contains("ORG-05 Walter White (individual) needs an outside law firm with a partner and an associate", topUp.UserPrompt);
        Assert.DoesNotContain("ORG-01 Brightline Glazing, LLC needs", topUp.UserPrompt);
        Assert.Contains("Keep every name the document uses exactly as written", topUp.UserPrompt);
        Assert.Equal(CaseGuideGenerator.RosterMaxOutputTokens, topUp.MaxOutputTokens);

        var guide = draft.Guide!;
        var novak = Assert.Single(guide.Cast, c => c.FullName == "Paul Novak");
        Assert.Equal(CastRole.InHouseCounsel, novak.Role);
        Assert.Equal("ORG-02", novak.OrganizationId);
        // A private individual with no employer keeps the personal organization (old behavior)
        var white = Assert.Single(guide.Cast, c => c.FullName == "Walter White");
        Assert.Equal("party-3", white.PartyId);
        Assert.Equal("ORG-05", white.OrganizationId);
        Assert.Equal("Owner", white.Title);
        Assert.Equal(OrganizationKind.Individual, guide.FindOrganization("ORG-05")!.Kind);
        Assert.Equal("walter.white@whitefamily.com", white.Email);
        var okafor = guide.Organizations.Single(o => o.Name == "Okafor & Lind LLP");
        Assert.Equal(new[] { "ORG-05" }, okafor.RepresentsOrgIds);
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

    [Fact]
    public async Task Cast_ResumeCancel_SavesMergedUnits()
    {
        // WR-08: a resumed cast run (no organizations added) cancelled after one background unit finished must bump the
        // revision so the autosave hook runs for the merged staff
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(BackgroundJson), FakeLlmProvider.Ok(BackgroundJson), FakeLlmProvider.Ok(BackgroundJson));
        using var cts = new CancellationTokenSource();
        fake.OnSend = i =>
        {
            if (i == 1)
                cts.Cancel();
        };
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        guide.CastState.Status = GuidePartStatus.Pending;
        var staffBefore = guide.Cast.Count(c => c.Tier == CastTier.Background);
        var revision = draft.Revision;
        var saved = new List<GuidePart>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CaseGuideGenerator(gateway).GenerateAsync(draft, null, part =>
        {
            saved.Add(part);
            return Task.CompletedTask;
        }, cts.Token));

        Assert.True(guide.Cast.Count(c => c.Tier == CastTier.Background) > staffBefore);
        Assert.Contains("ORG-01", guide.CastState.CompletedUnits);
        Assert.True(draft.Revision > revision);
        Assert.Equal(new[] { GuidePart.Cast }, saved);
    }

    // ---------- Facts fixtures ----------

    // SampleGuidedDraft with issues and cast Ready and no facts or values yet
    private static CaseDraft FactsReadyDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        guide.Facts.Clear();
        guide.Values.Clear();
        guide.IdCounters["F"] = 0;
        guide.IdCounters["V"] = 0;
        guide.FactsState = new GuidePartState();
        guide.ValuesState = new GuidePartState();
        return draft;
    }

    private static string FactJson(string date, string title, string issues = "\"ISS-01\"", string actors = "\"CH-001\"", string knownBy = "", string significance = "Key", string existingId = "") =>
        $$"""{ "existingId": "{{existingId}}", "date": "{{date}}", "title": "{{title}}", "description": "{{title}} on the Lakeview Tower project.", "issueIds": [{{issues}}], "actorIds": [{{actors}}], "knownByIds": [{{knownBy}}], "significance": "{{significance}}" }""";

    private static string FactsJson(params string[] facts) => "{ \"facts\": [" + string.Join(", ", facts) + "] }";

    private static bool IsFactsRequest(LlmProviderRequest r) => r.UserPrompt.Contains("State each fact as what happened");

    private static FactSubsectionPlan Plan(int n, string start, string end, int paragraphs) => new()
    {
        Id = $"facts-{n}",
        Heading = $"Period {n}",
        StartDate = start,
        EndDate = end,
        TargetParagraphs = paragraphs,
        Focus = $"Events of period {n}."
    };

    private static CaseDraft WithFactPlan(CaseDraft draft, params FactSubsectionPlan[] plans)
    {
        var doc = draft.CoreDocument!;
        doc.FactPlan = plans.ToList();
        doc.Sections.RemoveAll(s => s.Kind == SectionKind.FactualAllegations);
        var at = doc.Sections.FindIndex(s => s.Kind == SectionKind.Count);
        doc.Sections.InsertRange(at < 0 ? doc.Sections.Count : at, plans.Select(p => new CoreDocumentSection
        {
            Id = p.Id,
            Kind = SectionKind.FactualAllegations,
            Heading = p.Heading,
            Paragraphs = new List<string> { $"Paragraph about {p.Heading}." },
            Status = SectionStatus.Ready
        }));
        return draft;
    }

    // ---------- Facts: planning ----------

    [Fact]
    public void PlanFactCalls_Standard()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Complexity = ComplexityLevel.Standard;
        WithFactPlan(draft,
            Plan(1, "2024-01-01", "2024-03-31", 10),
            Plan(2, "2024-04-01", "2024-06-30", 10),
            Plan(3, "2024-07-01", "2024-09-30", 10),
            Plan(4, "2024-10-01", "2024-12-31", 10));

        var calls = CaseGuideGenerator.PlanFactCalls(draft);

        Assert.Equal(4, calls.Count);
        Assert.All(calls, c => Assert.Equal(10, c.Target));
        Assert.Equal(new[] { "facts-1", "facts-2", "facts-3", "facts-4" }, calls.Select(c => c.Plan.Id));
        Assert.Equal("2024-04-01", calls[1].From);
        Assert.Equal("2024-06-30", calls[1].To);
    }

    [Fact]
    public void PlanFactCalls_SplitsLargeSection()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Complexity = ComplexityLevel.Complex;
        WithFactPlan(draft,
            Plan(1, "2024-01-01", "2024-12-31", 20),
            Plan(2, "2025-01-01", "2025-06-30", 10));

        var calls = CaseGuideGenerator.PlanFactCalls(draft);

        // 90 facts weighted 2:1 -> 60 + 30; the 60 is split into two calls over the two halves of the section
        Assert.Equal(3, calls.Count);
        Assert.Equal(new[] { 30, 30, 30 }, calls.Select(c => c.Target));
        Assert.Equal(new[] { "facts-1", "facts-1", "facts-2" }, calls.Select(c => c.Plan.Id));
        Assert.Equal(("2024-01-01", "2024-07-01"), (calls[0].From, calls[0].To));
        Assert.Equal(("2024-07-02", "2024-12-31"), (calls[1].From, calls[1].To));
        Assert.Equal(("2025-01-01", "2025-06-30"), (calls[2].From, calls[2].To));
        Assert.All(calls, c => Assert.True(c.Target <= CaseGuideGenerator.MaxFactsPerCall));
    }

    [Fact]
    public void PlanFactCalls_MinimumTwoAndBadDates()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Complexity = ComplexityLevel.Complex;
        WithFactPlan(draft,
            Plan(1, "early 2024", "late 2024", 50),
            Plan(2, "2025-01-01", "2025-06-30", 0));

        var calls = CaseGuideGenerator.PlanFactCalls(draft);

        // Unparseable dates: one call capped at 30; a zero-weight section still gets 2
        Assert.Equal(2, calls.Count);
        Assert.Equal(30, calls[0].Target);
        Assert.Equal("early 2024", calls[0].From);
        Assert.Equal(2, calls[1].Target);
        Assert.All(calls, c => Assert.True(c.Target >= 2));
    }

    // ---------- Facts: prompt ----------

    [Fact]
    public void FactsPrompt()
    {
        var draft = FactsReadyDraft();
        var plan = draft.CoreDocument!.FactPlan[0];

        var prompt = CaseGuidePrompts.BuildFactsPrompt(draft, plan, plan.StartDate, plan.EndDate, 8, Array.Empty<KeyFact>());

        Assert.Contains("The Subcontract", prompt);
        Assert.Contains("2024-01-08 to 2024-02-29", prompt);
        Assert.Contains("Harmon engaged Brightline to furnish and install the window systems", prompt);
        Assert.Contains("ISS-01 | Breach of Contract | ", prompt);
        Assert.Contains("CH-001 | Maria Delgado | President | Brightline Glazing, LLC | active 2023-10-01 to 2024-12-31", prompt);
        Assert.Contains("Harmon failed to pay approved change orders.", prompt);
        Assert.Contains("Write about 8 facts", prompt);
        Assert.Contains("State each fact as what happened", prompt);
        Assert.Contains("Keep every name the document uses exactly as written", prompt);
        Assert.Contains("Never use these real names: Acme Windows", prompt);
        Assert.DoesNotContain("Existing facts", prompt);
    }

    [Fact]
    public void FactsPrompt_ExistingAndHandEdited()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var plan = draft.CoreDocument!.FactPlan[1];
        var existing = draft.Guide!.Facts.Where(f => f.SourceSectionId == plan.Id).ToList();
        existing[0].IsUserEdited = true;

        var prompt = CaseGuidePrompts.BuildFactsPrompt(draft, plan, plan.StartDate, plan.EndDate, 8, existing, "more about the invoice");

        Assert.Contains("Existing facts (revise by existingId; return new facts with an empty existingId):", prompt);
        Assert.Contains("F-004 | 2024-04-22 | Brightline completes the changed work", prompt);
        Assert.Matches(@"Keep exactly; do not return these:\s*\r?\nF-003 \| 2024-03-18 \| Harmon approves Change Order 7", prompt);
        Assert.Contains("Guidance from the user: more about the invoice", prompt);
    }

    [Fact]
    public async Task Facts_Request()
    {
        var (inner, fake) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-01-15", "Subcontract signed"))),
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-03-18", "Change order approved"))));
        var gateway = new RecordingGateway(inner);
        var draft = FactsReadyDraft();
        var progress = new CaseTestData.SyncProgress<GuideProgress>();

        await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft, progress);

        Assert.Equal(new[] { "Case Guide - Facts (The Subcontract)", "Case Guide - Facts (Unpaid Change Orders)" }, gateway.Requests.Select(r => r.OperationName));
        Assert.All(gateway.Requests, r => Assert.Equal(LlmTier.Story, r.Tier));
        Assert.All(fake.Requests, r =>
        {
            Assert.Equal(16_000, r.MaxTokens);
            Assert.Equal(CaseGuidePrompts.SystemPrompt, r.SystemPrompt);
            CaseTestData.AssertNoJsonInstructions(r);
        });
        Assert.Contains("Write about 8 facts", fake.Requests[0].UserPrompt);
        Assert.Contains(progress.Reports, p => p.Message == "Writing the key facts: The Subcontract (1 of 2)...");
        Assert.Contains(progress.Reports, p => p.Message == "Writing the key facts: Unpaid Change Orders (2 of 2)...");
    }

    // ---------- Facts: mapping ----------

    [Fact]
    public async Task Facts_MapAndCheck()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(FactsJson(
                FactJson("2024-01-15", "Subcontract signed", "\"ISS-1\", \"ISS-99\"", "\"CH-001\", \"Michael Scott\", \"CH-077\"", "\"CH-003\""),
                FactJson("2024-02-01", "Harmon confirms the procedure", actors: "\"Lisa Chen\"", significance: "Hot"))),
            FakeLlmProvider.Ok(FactsJson()));
        var draft = FactsReadyDraft();

        await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        var facts = draft.Guide!.Facts;
        Assert.Equal(2, facts.Count);
        var first = facts[0];
        Assert.Equal("F-001", first.Id);
        Assert.Equal(new[] { "ISS-01" }, first.IssueIds);
        Assert.Equal(new[] { "CH-001" }, first.ActorIds);
        Assert.Superset(new HashSet<string> { "CH-001", "CH-003" }, first.KnownByIds.ToHashSet());
        Assert.Contains("Dropped unknown issue ISS-99.", first.MappingNotes);
        Assert.Contains("Dropped unknown character CH-077.", first.MappingNotes);
        Assert.Contains("Dropped unknown character Michael Scott.", first.MappingNotes);
        Assert.Equal("facts-1", first.SourceSectionId);
        Assert.Equal(FactSignificance.Key, first.Significance);
        Assert.Equal("Subcontract signed on the Lakeview Tower project.", first.Description);

        // Exact full-name match resolves to the cast id
        var second = facts[1];
        Assert.Equal(new[] { "CH-004" }, second.ActorIds);
        Assert.Contains("CH-004", second.KnownByIds);
        Assert.Equal(FactSignificance.Hot, second.Significance);
        Assert.Empty(second.MappingNotes);
    }

    [Fact]
    public async Task Facts_DateOrderIds()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-02-20", "Second"), FactJson("2024-01-10", "First"))),
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-05-01", "Fourth"), FactJson("2024-03-15", "Third"))));
        var draft = FactsReadyDraft();

        await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        var facts = draft.Guide!.Facts;
        Assert.Equal(new[] { "First", "Second", "Third", "Fourth" }, facts.Select(f => f.Title));
        Assert.Equal(new[] { "F-001", "F-002", "F-003", "F-004" }, facts.Select(f => f.Id));
        Assert.Equal(new[] { "facts-1", "facts-1", "facts-2", "facts-2" }, facts.Select(f => f.SourceSectionId));
    }

    [Fact]
    public async Task Facts_BadDateKept()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("March 3", "Undated meeting"), FactJson("2024-01-10", "First"))),
            FakeLlmProvider.Ok(FactsJson()));
        var draft = FactsReadyDraft();

        await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        var facts = draft.Guide!.Facts;
        var bad = Assert.Single(facts, f => f.Title == "Undated meeting");
        Assert.Equal("March 3", bad.Date);
        Assert.Contains("The date \"March 3\" is not YYYY-MM-DD.", bad.MappingNotes);
        // Undated facts sort last
        Assert.Equal("First", facts[0].Title);
    }

    // ---------- Facts: outcomes ----------

    [Fact]
    public async Task Facts_PartialFailure()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-01-15", "One"))),
            FakeLlmProvider.Refused("Not this period."),
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-09-15", "Three"))));
        var draft = WithFactPlan(FactsReadyDraft(),
            Plan(1, "2024-01-01", "2024-03-31", 3),
            Plan(2, "2024-04-01", "2024-06-30", 3),
            Plan(3, "2024-07-01", "2024-09-30", 3));
        var generator = new CaseGuideGenerator(gateway);

        var result = await generator.GenerateFactsAsync(draft);

        var guide = draft.Guide!;
        Assert.False(result.Succeeded);
        Assert.True(result.WasRefused);
        Assert.Equal(GuidePartStatus.Failed, guide.FactsState.Status);
        Assert.Equal("The model declined: Not this period.", guide.FactsState.FailureMessage);
        Assert.Equal(guide.FactsState.FailureMessage, result.FailureMessage);
        Assert.Equal(new[] { "One", "Three" }, guide.Facts.Select(f => f.Title));
        Assert.Equal(new[] { "facts-1", "facts-3" }, guide.FactsState.CompletedUnits.OrderBy(u => u));
        var before = fake.Requests.Count;

        fake.Responses.Enqueue(FakeLlmProvider.Ok(FactsJson(FactJson("2024-05-15", "Two"))));
        var retry = await generator.GenerateFactsAsync(draft);

        var request = Assert.Single(fake.Requests.Skip(before));
        Assert.Contains("Period 2", request.UserPrompt);
        Assert.True(retry.Succeeded);
        Assert.Equal(new[] { "One", "Two", "Three" }, guide.Facts.Select(f => f.Title));
        Assert.Equal(GuidePartStatus.Ready, guide.FactsState.Status);
    }

    [Fact]
    public async Task Facts_SeveralFailed()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-01-15", "One"))),
            FakeLlmProvider.Refused("Not this period."),
            FakeLlmProvider.Refused("Not this one either."));
        var draft = WithFactPlan(FactsReadyDraft(),
            Plan(1, "2024-01-01", "2024-03-31", 3),
            Plan(2, "2024-04-01", "2024-06-30", 3),
            Plan(3, "2024-07-01", "2024-09-30", 3));

        await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        Assert.Equal("2 periods were not written: The model declined: Not this period.", draft.Guide!.FactsState.FailureMessage);
    }

    [Fact]
    public async Task Facts_Ready()
    {
        var (gateway, _) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-01-15", "One"))),
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-03-15", "Two"))));
        var draft = FactsReadyDraft();
        var revision = draft.Revision;

        var result = await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        var guide = draft.Guide!;
        Assert.True(result.Succeeded);
        Assert.Equal(GuidePart.Facts, result.Part);
        Assert.Equal(GuidePartStatus.Ready, guide.FactsState.Status);
        Assert.Equal("", guide.FactsState.FailureMessage);
        Assert.Empty(guide.FactsState.CompletedUnits);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Facts), guide.FactsState.BasisHash);
        Assert.True(draft.Revision > revision);
    }

    [Fact]
    public async Task Facts_NoCast()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(FactsJson()));
        var draft = FactsReadyDraft();
        draft.Guide!.Cast.Clear();

        var result = await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        Assert.False(result.Succeeded);
        Assert.Equal("Write the cast before the key facts.", result.FailureMessage);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Facts_Fatal()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));
        var draft = FactsReadyDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide!.FactsState.Status);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Facts_UserCancel()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-01-15", "One"))),
            FakeLlmProvider.Ok(FactsJson(FactJson("2024-03-15", "Two"))));
        using var cts = new CancellationTokenSource();
        fake.OnSend = i =>
        {
            if (i == 1)
                cts.Cancel();
        };
        var draft = FactsReadyDraft();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CaseGuideGenerator(gateway).GenerateFactsAsync(draft, null, cts.Token));

        // The finished section is kept for Retry
        var guide = draft.Guide!;
        Assert.Equal(GuidePartStatus.Pending, guide.FactsState.Status);
        Assert.Equal(new[] { "One" }, guide.Facts.Select(f => f.Title));
        Assert.Equal(new[] { "facts-1" }, guide.FactsState.CompletedUnits);
    }

    [Fact]
    public async Task Facts_ParallelBound()
    {
        var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        var slow = new SlowProvider(fake);
        var gateway = new LlmGateway(slow, CaseTestData.StoryModel(), CaseTestData.VolumeModel());
        fake.Responses.Enqueue(FakeLlmProvider.Ok(FactsJson(FactJson("2024-01-15", "Same"))));
        var draft = WithFactPlan(FactsReadyDraft(), Enumerable.Range(1, 6)
            .Select(n => Plan(n, $"2024-{n:D2}-01", $"2024-{n:D2}-28", 3)).ToArray());

        await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        Assert.Equal(6, fake.Requests.Count(IsFactsRequest));
        Assert.InRange(slow.MaxConcurrent, 2, CaseGuideGenerator.MaxParallelCalls);
        Assert.Equal(6, draft.Guide!.Facts.Count);
        Assert.Equal(6, draft.Guide.Facts.Select(f => f.Id).Distinct().Count());
    }

    // ---------- Canonical values ----------

    private const string ValuesJson = """
        {
          "values": [
            { "existingId": "", "kind": "Amount", "label": "Change Order 7 amount", "value": " $184,250.00 ", "description": "Amount Harmon approved for Change Order 7.", "factIds": ["F-1", "F-99"] },
            { "existingId": "", "kind": "ChangeOrderNumber", "label": "Change order number", "value": "CO-007", "description": "Number of the approved change order.", "factIds": ["F-003"] },
            { "existingId": "", "kind": "Amount", "label": "change order 7 amount", "value": "$184,250.00", "description": "Same amount again.", "factIds": ["F-005"] }
          ]
        }
        """;

    // SampleGuidedDraft with issues, cast and facts Ready and no values; F-004 mentions CO-007
    private static CaseDraft ValuesReadyDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        guide.Values.Clear();
        guide.IdCounters["V"] = 0;
        guide.ValuesState = new GuidePartState();
        guide.FindFact("F-004")!.Description += " The work was priced under CO-007.";
        return draft;
    }

    private static bool IsValuesRequest(LlmProviderRequest r) => r.UserPrompt.Contains("Register every amount");

    [Fact]
    public async Task ValuesPrompt()
    {
        var (inner, fake) = Gateway(FakeLlmProvider.Ok(ValuesJson));
        var gateway = new RecordingGateway(inner);
        var draft = ValuesReadyDraft();
        var progress = new CaseTestData.SyncProgress<GuideProgress>();

        await new CaseGuideGenerator(gateway).GenerateValuesAsync(draft, progress);

        var request = Assert.Single(fake.Requests);
        Assert.Contains("F-001 | 2024-01-15 | Subcontract signed for Lakeview Tower | Brightline and Harmon signed the glazing subcontract", request.UserPrompt);
        Assert.Contains("These amounts and identifiers appear in the document; register each one exactly as written: $184,250.00, CO-007, INV-2024-118", request.UserPrompt);
        Assert.Contains("Register every amount, invoice number, change-order number, contract number, purchase order, account number, document title, project name and address the facts rely on.", request.UserPrompt);
        Assert.Contains("Kinds: Amount, InvoiceNumber, ChangeOrderNumber", request.UserPrompt);
        Assert.Contains("Keep every name the document uses exactly as written", request.UserPrompt);
        Assert.Equal(12_000, request.MaxTokens);
        Assert.Equal(CaseGuidePrompts.SystemPrompt, request.SystemPrompt);
        CaseTestData.AssertNoJsonInstructions(request);
        Assert.Equal("Case Guide - Canonical values", gateway.Requests[0].OperationName);
        Assert.Equal(LlmTier.Story, gateway.Requests[0].Tier);
        Assert.Contains(progress.Reports, p => p.Message == "Writing the canonical values...");
    }

    [Fact]
    public void ValuesPrompt_ExistingAndHandEdited()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var values = draft.Guide!.Values;
        values[1].IsUserEdited = true;

        var prompt = CaseGuidePrompts.BuildValuesPrompt(draft, new List<ScannedValue>(), values, "add the retainage");

        Assert.Contains("Existing values (revise by existingId; return new values with an empty existingId):", prompt);
        Assert.Contains("V-001 | Change Order 7 amount | $184,250.00", prompt);
        Assert.Matches(@"Keep exactly; do not return these:\s*\r?\nV-002 \| Change order number \| CO-007", prompt);
        Assert.DoesNotContain("These amounts and identifiers appear", prompt);
        Assert.Contains("Guidance from the user: add the retainage", prompt);
    }

    [Fact]
    public async Task Values_Map()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(ValuesJson));
        var draft = ValuesReadyDraft();

        await new CaseGuideGenerator(gateway).GenerateValuesAsync(draft);

        var values = draft.Guide!.Values;
        Assert.Equal(2, values.Count);
        var amount = values[0];
        Assert.Equal("V-001", amount.Id);
        Assert.Equal(ValueKind.Amount, amount.Kind);
        Assert.Equal("Change Order 7 amount", amount.Label);
        Assert.Equal("$184,250.00", amount.Value);
        Assert.Equal("Amount Harmon approved for Change Order 7.", amount.Description);
        // Duplicate label + value merged with the fact ids unioned
        Assert.Equal(new[] { "F-001", "F-005" }, amount.FactIds);
        Assert.Contains("Dropped unknown fact F-99.", amount.MappingNotes);

        var number = values[1];
        Assert.Equal("V-002", number.Id);
        Assert.Equal(ValueKind.ChangeOrderNumber, number.Kind);
        Assert.Equal("CO-007", number.Value);
        Assert.Equal(new[] { "F-003" }, number.FactIds);
    }

    [Fact]
    public async Task Values_Ready()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(ValuesJson));
        var draft = ValuesReadyDraft();
        var revision = draft.Revision;

        var result = await new CaseGuideGenerator(gateway).GenerateValuesAsync(draft);

        var guide = draft.Guide!;
        Assert.True(result.Succeeded);
        Assert.Equal(GuidePart.Values, result.Part);
        Assert.Equal(GuidePartStatus.Ready, guide.ValuesState.Status);
        Assert.Equal(GuideStaleness.BasisFor(draft, GuidePart.Values), guide.ValuesState.BasisHash);
        Assert.True(draft.Revision > revision);
    }

    [Fact]
    public async Task Values_NoFacts()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(ValuesJson));
        var draft = FactsReadyDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateValuesAsync(draft);

        Assert.False(result.Succeeded);
        Assert.Equal("Write the key facts before the canonical values.", result.FailureMessage);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Values_Refused()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Refused("No values."));
        var draft = ValuesReadyDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateValuesAsync(draft);

        Assert.False(result.Succeeded);
        Assert.True(result.WasRefused);
        Assert.Equal(GuidePartStatus.Failed, draft.Guide!.ValuesState.Status);
        Assert.Equal("The model declined: No values.", result.FailureMessage);
        Assert.Empty(draft.Guide.Values);
    }

    // ---------- GenerateAsync ----------

    private const string FactsSection1Json = """{ "facts": [ { "existingId": "", "date": "2024-01-15", "title": "Subcontract signed", "description": "Brightline and Harmon signed the subcontract.", "issueIds": ["ISS-01"], "actorIds": ["CH-001", "CH-003"], "knownByIds": [], "significance": "Key" } ] }""";

    private const string FactsSection2Json = """{ "facts": [ { "existingId": "", "date": "2024-05-10", "title": "Invoice submitted", "description": "Brightline invoiced $184,250.00.", "issueIds": ["ISS-01"], "actorIds": ["CH-001"], "knownByIds": ["CH-004"], "significance": "Hot" }, { "existingId": "", "date": "2024-03-18", "title": "Change order approved", "description": "Harmon approved CO-007.", "issueIds": ["ISS-01"], "actorIds": ["CH-004"], "knownByIds": [], "significance": "Key" } ] }""";

    private static LlmProviderResponse[] CastResponses(CaseDraft draft)
    {
        // Roster adds two law firms to the two party organizations: 4 non-individual organizations get background staff
        var orgs = draft.CoreDocument!.Parties.Count + 2;
        return new[] { FakeLlmProvider.Ok(CivilRosterJson), FakeLlmProvider.Ok(DetailsJson(1, 10)) }
            .Concat(Enumerable.Range(0, orgs).Select(_ => FakeLlmProvider.Ok(BackgroundJson)))
            .ToArray();
    }

    [Fact]
    public async Task GenerateAsync_FullRun()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var responses = new[] { FakeLlmProvider.Ok(IssueDetailsJson) }
            .Concat(CastResponses(draft))
            .Concat(new[] { FakeLlmProvider.Ok(FactsSection1Json), FakeLlmProvider.Ok(FactsSection2Json), FakeLlmProvider.Ok(ValuesJson) })
            .ToArray();
        var (gateway, fake) = Gateway(responses);
        var progress = new CaseTestData.SyncProgress<GuideProgress>();
        var completed = new List<GuidePart>();
        var revision = draft.Revision;

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft, progress, part =>
        {
            completed.Add(part);
            return Task.CompletedTask;
        });

        var all = new[] { GuidePart.Issues, GuidePart.Cast, GuidePart.Facts, GuidePart.Values };
        Assert.Equal(all, result.PartsWritten);
        Assert.Equal(all, completed);
        Assert.Empty(result.PartsFailed);
        Assert.False(result.IsFatal);
        Assert.Null(result.RefusedPart);
        Assert.True(draft.Revision > revision);
        Assert.Equal(responses.Length, fake.Requests.Count);

        var guide = draft.Guide!;
        Assert.All(all, p => Assert.Equal(GuidePartStatus.Ready, guide.State(p).Status));
        Assert.Equal(new[] { "2024-01-15", "2024-03-18", "2024-05-10" }, guide.Facts.Select(f => f.Date));
        Assert.Equal(new[] { "F-001", "F-002", "F-003" }, guide.Facts.Select(f => f.Id));
        Assert.NotEmpty(guide.Values);

        var messages = progress.Reports.Select(p => p.Message).ToList();
        var order = new[]
        {
            "Extracting the issues from the counts...",
            "Writing the cast: organizations and key characters...",
            "Writing the key facts: The Subcontract (1 of 2)...",
            "Writing the canonical values..."
        }.Select(m => messages.IndexOf(m)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public async Task GenerateAsync_SkipsReady()
    {
        var (gateway, fake) = Gateway(
            FakeLlmProvider.Ok(FactsSection1Json),
            FakeLlmProvider.Ok(FactsSection2Json),
            FakeLlmProvider.Ok(ValuesJson));
        var draft = FactsReadyDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft);

        Assert.True(IsFactsRequest(fake.Requests[0]));
        Assert.Equal(3, fake.Requests.Count);
        Assert.Equal(new[] { GuidePart.Facts, GuidePart.Values }, result.PartsWritten);
    }

    [Fact]
    public async Task GenerateAsync_StopsAtFailedPart()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson), FakeLlmProvider.Refused("I won't write that."));
        var draft = CaseTestData.SampleComplaintDraft();
        var completed = new List<GuidePart>();

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft, null, part =>
        {
            completed.Add(part);
            return Task.CompletedTask;
        });

        Assert.Equal(new[] { GuidePart.Issues }, result.PartsWritten);
        Assert.Equal(new[] { GuidePart.Cast }, result.PartsFailed);
        Assert.Equal(GuidePart.Cast, result.RefusedPart);
        Assert.Equal("The model declined: I won't write that.", result.RefusalMessage);
        Assert.Equal("The model declined: I won't write that.", result.FailureMessage);
        Assert.Equal(new[] { GuidePart.Issues, GuidePart.Cast }, completed);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide!.FactsState.Status);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide.ValuesState.Status);
        Assert.DoesNotContain(fake.Requests, IsFactsRequest);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task GenerateAsync_Fatal()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));
        var draft = FactsReadyDraft();

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.DoesNotContain(fake.Requests, IsValuesRequest);
        Assert.Empty(result.PartsWritten);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide!.FactsState.Status);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide.ValuesState.Status);
    }

    [Fact]
    public async Task GenerateAsync_UserCancel()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(IssueDetailsJson), FakeLlmProvider.Ok(CivilRosterJson));
        using var cts = new CancellationTokenSource();
        fake.OnSend = i =>
        {
            if (i == 1)
                cts.Cancel();
        };
        var draft = CaseTestData.SampleComplaintDraft();
        var completed = new List<GuidePart>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CaseGuideGenerator(gateway).GenerateAsync(draft, null, part =>
        {
            completed.Add(part);
            return Task.CompletedTask;
        }, cts.Token));

        Assert.Equal(GuidePart.Issues, completed[0]);
        Assert.Equal(GuidePartStatus.Ready, draft.Guide!.IssuesState.Status);
        Assert.Equal(GuidePartStatus.Pending, draft.Guide.CastState.Status);
        Assert.DoesNotContain(fake.Requests, IsFactsRequest);
    }

    [Fact]
    public async Task GenerateAsync_IssueDetailsFailureDoesNotStop()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var responses = new[] { FakeLlmProvider.Truncated(), FakeLlmProvider.Truncated() }
            .Concat(CastResponses(draft))
            .Concat(new[] { FakeLlmProvider.Ok(FactsSection1Json), FakeLlmProvider.Ok(FactsSection2Json), FakeLlmProvider.Ok(ValuesJson) })
            .ToArray();
        var (gateway, _) = Gateway(responses);

        var result = await new CaseGuideGenerator(gateway).GenerateAsync(draft);

        var guide = draft.Guide!;
        Assert.Equal(GuidePartStatus.Failed, guide.IssueDetailsState.Status);
        Assert.Equal(new[] { GuidePart.Issues, GuidePart.Cast, GuidePart.Facts, GuidePart.Values }, result.PartsWritten);
        Assert.Equal(GuidePartStatus.Ready, guide.ValuesState.Status);
        Assert.Null(result.RefusedPart);
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
