using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

public class CoreDocumentGeneratorTests
{
    private const string Scenario = "Window subcontractor Brightline Glazing sues general contractor Harmon Builders over unpaid change orders on a Chicago tower.";

    private const string CivilOutlineJson = """
        {
          "matterKind": "Civil",
          "courtSystem": "Federal",
          "state": "Illinois",
          "courtName": "United States District Court for the Northern District of Illinois",
          "district": "Northern District of Illinois",
          "venueCity": "Chicago",
          "usesPetitionTerminology": false,
          "memoPerspective": "InternalCorporate",
          "title": "Complaint for Breach of Contract",
          "filingDate": "2024-09-16",
          "timelineStart": "2024-01-08",
          "timelineEnd": "2024-06-28",
          "demandsJuryTrial": true,
          "parties": [
            { "name": " Brightline Glazing, LLC ", "shortName": "Brightline", "role": "Plaintiff", "entityType": "Illinois limited liability company", "domicile": "Chicago, Illinois", "description": "Window subcontractor." },
            { "name": "Harmon Builders, Inc.", "shortName": "Harmon", "role": "Defendant", "entityType": "Delaware corporation", "domicile": "Chicago, Illinois", "description": "General contractor." }
          ],
          "counts": [
            { "title": "Breach of Contract", "againstParties": ["Harmon", "Nobody"], "legalBasis": "Illinois common law", "summary": "Harmon failed to pay approved change orders." }
          ],
          "factSubsections": [
            { "heading": "The Subcontract", "startDate": "2024-01-08", "endDate": "2024-02-29", "targetParagraphs": 4, "focus": "Formation of the subcontract." },
            { "heading": "Unpaid Change Orders", "startDate": "2024-03-01", "endDate": "2024-06-28", "targetParagraphs": 5, "focus": "Approved change orders that went unpaid." }
          ],
          "counsel": { "attorneyName": "Morgan Ellis", "firmName": "Ellis & Park LLP", "barNumber": "6301234", "address": "200 W. Adams St., Chicago, IL 60606", "email": "mellis@ellispark.example" },
          "memo": { "to": "", "from": "", "cc": "", "date": "", "re": "", "organization": "" }
        }
        """;

    private const string CriminalOutlineJson = """
        {
          "matterKind": "Criminal",
          "courtSystem": "Federal",
          "state": "New Mexico",
          "courtName": "United States District Court for the District of New Mexico",
          "district": "District of New Mexico",
          "venueCity": "Albuquerque",
          "usesPetitionTerminology": false,
          "memoPerspective": "Government",
          "title": "Investigation Memorandum: Desert Bloom Laundromats",
          "filingDate": "2023-10-02",
          "timelineStart": "2023-02-01",
          "timelineEnd": "2023-08-31",
          "demandsJuryTrial": false,
          "parties": [
            { "name": "Victor Lane", "shortName": "Lane", "role": "Subject", "entityType": "Individual", "domicile": "Albuquerque, New Mexico", "description": "Owner of Desert Bloom." },
            { "name": "Desert Bloom Laundromats LLC", "shortName": "Desert Bloom", "role": "Subject", "entityType": "New Mexico limited liability company", "domicile": "Albuquerque, New Mexico", "description": "Cash-intensive laundromat chain." }
          ],
          "counts": [
            { "title": "Money laundering", "againstParties": ["Lane", "Desert Bloom Laundromats LLC"], "legalBasis": "Federal money laundering statutes", "summary": "Drug proceeds commingled with receipts." }
          ],
          "factSubsections": [
            { "heading": "Early 2023 cash deposits", "startDate": "2023-02-01", "endDate": "2023-04-30", "targetParagraphs": 3, "focus": "Deposit growth." },
            { "heading": "Summer 2023 structuring", "startDate": "2023-05-01", "endDate": "2023-08-31", "targetParagraphs": 3, "focus": "Structured deposits." }
          ],
          "counsel": { "attorneyName": "", "firmName": "", "barNumber": "", "address": "", "email": "" },
          "memo": { "to": "AUSA Rachel Kim", "from": "Special Agent Tom Reyes", "cc": "", "date": "2023-10-02", "re": "Desert Bloom Laundromats", "organization": "Southwest Financial Crimes Task Force" }
        }
        """;

    private const string SectionJson = """
        { "paragraphs": ["Brightline and Harmon signed the subcontract on January 8, 2024.", "Harmon approved change order 4 for $84,000."] }
        """;

    private static CaseDraft NewDraft(ComplexityLevel level = ComplexityLevel.Simple) => new()
    {
        Scenario = Scenario,
        EffectiveScenario = Scenario,
        Complexity = level
    };

    private static string WithOutlineField(string json, string field, string replacement) =>
        Regex.Replace(json, $"\"{field}\": [^,\\n]+", $"\"{field}\": {replacement}");

    // ---------- Outline ----------

    [Fact]
    public void Outline_PromptCarriesPresetNumbers()
    {
        var prompt = CoreDocumentGenerator.BuildOutlinePrompt(NewDraft(ComplexityLevel.Standard));

        Assert.Contains("Parties: 3-4", prompt);
        Assert.Contains("Counts or issues: 3-4", prompt);
        Assert.Contains("Key characters: 12-20", prompt);
        Assert.Contains("Timeline span (months): 9-18", prompt);
        Assert.Contains("Factual subsections: 3-4", prompt);
        Assert.Contains("Numbered paragraphs (whole document): 90-140", prompt);
        Assert.Contains("Memo length if this is an investigation memo: 15-25 pages, about 90-150 paragraphs in total", prompt);
    }

    [Fact]
    public void Outline_PromptPrintsEqualBounds()
    {
        var prompt = CoreDocumentGenerator.BuildOutlinePrompt(NewDraft(ComplexityLevel.Simple));

        Assert.Contains("Parties: 2-2", prompt);
    }

    [Fact]
    public void Outline_PromptCarriesPreferences()
    {
        var draft = NewDraft();
        draft.DocumentPreference = DocumentTypePreference.Petition;
        draft.CourtPreference = CourtPreference.State;
        draft.StatePreference = "Texas";

        var prompt = CoreDocumentGenerator.BuildOutlinePrompt(draft);

        Assert.Contains("Document type: Petition", prompt);
        Assert.Contains("Court system: State", prompt);
        Assert.Contains("State: Texas", prompt);

        var auto = CoreDocumentGenerator.BuildOutlinePrompt(NewDraft());
        Assert.Contains("Document type: choose", auto);
        Assert.Contains("Court system: choose", auto);
        Assert.Contains("State: choose", auto);
    }

    [Fact]
    public void Outline_PromptCarriesSubstitutions()
    {
        var draft = NewDraft();
        draft.Substitutions.Add(new EntitySubstitution { Original = "Walter White", Substitute = "Hank Brennan", Apply = true });
        draft.Substitutions.Add(new EntitySubstitution { Original = "Jesse Pinkman", Substitute = "Cody Marsh", Apply = false });

        var prompt = CoreDocumentGenerator.BuildOutlinePrompt(draft);

        Assert.Contains("Hank Brennan", prompt);
        Assert.Contains("Never use these real names: Walter White", prompt);
        Assert.DoesNotContain("Jesse Pinkman", prompt);
        Assert.DoesNotContain("Cody Marsh", prompt);
    }

    [Fact]
    public async Task Outline_UsesStoryModel_NoJson()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(CivilOutlineJson));

        await new CoreDocumentGenerator(gateway).GenerateOutlineAsync(NewDraft());

        var request = Assert.Single(fake.Requests);
        Assert.Equal("claude-story", request.ModelId);
        Assert.Equal(16_000, request.MaxTokens);
        CaseTestData.AssertNoJsonInstructions(request);
        Assert.Contains(Scenario, request.UserPrompt);
        Assert.DoesNotContain(Scenario, request.SystemPrompt);
    }

    [Fact]
    public async Task Outline_MapsCivilFederal()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(CivilOutlineJson));
        var draft = NewDraft();

        var doc = await new CoreDocumentGenerator(gateway).GenerateOutlineAsync(draft);

        Assert.Equal(CoreDocumentType.Complaint, doc.DocumentType);
        Assert.Equal(MatterKind.Civil, doc.MatterKind);
        Assert.Equal(CourtSystem.Federal, doc.Jurisdiction.CourtSystem);
        Assert.Equal("Illinois", doc.Jurisdiction.State);
        Assert.Equal("Chicago", doc.Jurisdiction.VenueCity);
        Assert.True(doc.DemandsJuryTrial);
        Assert.Equal("2024-09-16", doc.FilingDate);

        Assert.Equal(new[] { "party-1", "party-2" }, doc.Parties.Select(p => p.Id));
        Assert.Equal("Brightline Glazing, LLC", doc.Parties[0].Name);
        Assert.Equal(PartyRole.Defendant, doc.Parties[1].Role);

        var count = Assert.Single(doc.Counts);
        Assert.Equal("count-1", count.Id);
        Assert.Equal(1, count.Number);
        Assert.Equal(new[] { "party-2" }, count.AgainstPartyIds);

        Assert.Equal(new[] { "facts-1", "facts-2" }, doc.FactPlan.Select(f => f.Id));
        Assert.Equal(4, doc.FactPlan[0].TargetParagraphs);

        var expected = CoreDocumentTemplates.GetSectionSpecs(doc).Select(s => s.Id).ToList();
        Assert.Equal(expected, doc.Sections.Select(s => s.Id));
        Assert.Equal(new[] { "nature-of-action", "parties", "jurisdiction", "facts-1", "facts-2", "count-1", "prayer" }, doc.Sections.Select(s => s.Id));
        Assert.All(doc.Sections, s => Assert.Equal(SectionStatus.Pending, s.Status));
        Assert.Equal("count-1", doc.FindSection("count-1")!.CountId);

        Assert.Equal("Morgan Ellis", doc.Counsel.AttorneyName);
        Assert.Matches(@"^\(555\) 555-01\d\d$", doc.Counsel.Phone);
        Assert.Equal(draft.ComputeSourceHash(), doc.SourceHash);
    }

    [Fact]
    public async Task Outline_MapsCriminal()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(CriminalOutlineJson));

        var doc = await new CoreDocumentGenerator(gateway).GenerateOutlineAsync(NewDraft());

        Assert.Equal(CoreDocumentType.InvestigationMemo, doc.DocumentType);
        Assert.Equal(MemoPerspective.Government, doc.MemoPerspective);
        Assert.Equal("Southwest Financial Crimes Task Force", doc.Memo.Organization);
        Assert.Equal(new[] { "party-1", "party-2" }, doc.Counts[0].AgainstPartyIds);
        Assert.Equal(
            new[] { "exec-summary", "background", "scope", "key-individuals", "facts-1", "facts-2", "finding-1", "legal-analysis", "recommendations" },
            doc.Sections.Select(s => s.Id));
    }

    [Fact]
    public async Task Outline_UserCourtPreferenceWins()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        var stateOutline = WithOutlineField(CivilOutlineJson, "courtSystem", "\"State\"");
        stateOutline = WithOutlineField(stateOutline, "courtName", "\"Circuit Court of Cook County\"");
        fake.Responses.Enqueue(FakeLlmProvider.Ok(stateOutline));

        var draft = NewDraft();
        draft.CourtPreference = CourtPreference.Federal;
        draft.StatePreference = "Ohio";

        var doc = await new CoreDocumentGenerator(gateway).GenerateOutlineAsync(draft);

        Assert.Equal(CourtSystem.Federal, doc.Jurisdiction.CourtSystem);
        Assert.Equal("Circuit Court of Cook County", doc.Jurisdiction.CourtName);
        Assert.Equal("Ohio", doc.Jurisdiction.State);
        Assert.Contains("(required: the court name must belong to this court system)", fake.Requests[0].UserPrompt);
        Assert.Contains("State: Ohio (required: use a court in this state)", fake.Requests[0].UserPrompt);
    }

    [Fact]
    public async Task Outline_ClampsFactTargets()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        var json = CivilOutlineJson.Replace("\"targetParagraphs\": 4", "\"targetParagraphs\": 90")
            .Replace("\"targetParagraphs\": 5", "\"targetParagraphs\": 0");
        fake.Responses.Enqueue(FakeLlmProvider.Ok(json));

        var doc = await new CoreDocumentGenerator(gateway).GenerateOutlineAsync(NewDraft());

        Assert.Equal(40, doc.FactPlan[0].TargetParagraphs);
        Assert.Equal(3, doc.FactPlan[1].TargetParagraphs);
    }

    [Fact]
    public async Task Outline_NoParties_Throws()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        var json = Regex.Replace(CivilOutlineJson, @"""parties"": \[.*?\],\s*""counts""", "\"parties\": [], \"counts\"", RegexOptions.Singleline);
        fake.Responses.Enqueue(FakeLlmProvider.Ok(json));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new CoreDocumentGenerator(gateway).GenerateOutlineAsync(NewDraft()));

        Assert.Equal("The outline listed no parties.", ex.Message);
    }

    [Fact]
    public async Task Outline_Refused_Throws()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("I can't help with that."));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() => new CoreDocumentGenerator(gateway).GenerateOutlineAsync(NewDraft()));

        Assert.Equal(LlmOutcome.Refused, ex.Result.Outcome);
        Assert.Single(fake.Requests);
    }
}
