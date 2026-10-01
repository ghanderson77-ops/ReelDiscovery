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

    // ---------- Sections ----------

    private static readonly string[] ComplaintOrder = { "nature-of-action", "parties", "jurisdiction", "facts-1", "facts-2", "count-1", "prayer" };

    private static string Paragraphs(params string[] paragraphs) =>
        "{ \"paragraphs\": [" + string.Join(", ", paragraphs.Select(p => "\"" + p + "\"")) + "] }";

    private static (LlmGateway Gateway, FakeLlmProvider Fake) CivilGateway(params LlmProviderResponse[] sectionResponses)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(CivilOutlineJson));
        foreach (var response in sectionResponses)
            fake.Responses.Enqueue(response);
        if (sectionResponses.Length == 0)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(SectionJson));
        return (gateway, fake);
    }

    [Fact]
    public async Task Generate_Complaint_AllSectionsReady()
    {
        var (gateway, fake) = CivilGateway();
        var draft = NewDraft();

        var result = await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        Assert.NotNull(draft.CoreDocument);
        Assert.Equal(ComplaintOrder, draft.CoreDocument!.Sections.Select(s => s.Id));
        Assert.All(draft.CoreDocument.Sections, s =>
        {
            Assert.Equal(SectionStatus.Ready, s.Status);
            Assert.Equal(2, s.Paragraphs.Count);
            Assert.Equal(string.Empty, s.FailureMessage);
        });
        Assert.Equal(7, result.SectionsWritten);
        Assert.Equal(0, result.SectionsFailed);
        Assert.False(result.IsFatal);
        Assert.Equal(2, draft.Revision);
        Assert.Equal(8, fake.Requests.Count);
        Assert.All(fake.Requests.Skip(1), r =>
        {
            Assert.Equal(12_000, r.MaxTokens);
            Assert.Equal("claude-story", r.ModelId);
            CaseTestData.AssertNoJsonInstructions(r);
        });
    }

    [Fact]
    public async Task Generate_ReportsProgress()
    {
        var (gateway, _) = CivilGateway();
        var progress = new CaseTestData.SyncProgress<string>();

        await new CoreDocumentGenerator(gateway).GenerateAsync(NewDraft(), progress);

        Assert.Equal("Writing the outline...", progress.Reports[0]);
        Assert.Contains("Outline ready: 7 sections", progress.Reports);
        Assert.Contains("Writing The Subcontract (1 of 2 fact sections)...", progress.Reports);
        Assert.Contains("Writing Count I of I...", progress.Reports);
        Assert.Contains("Writing PRAYER FOR RELIEF...", progress.Reports);
    }

    [Fact]
    public async Task GenerateSections_RetryBumpsRevision()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(Paragraphs("Rewritten unpaid change order facts.")));
        var draft = CaseTestData.SampleComplaintDraft();
        var facts2 = draft.CoreDocument!.FindSection("facts-2")!;
        facts2.Status = SectionStatus.Failed;
        facts2.FailureMessage = "The response was cut off. Click Retry Section.";

        var generator = new CoreDocumentGenerator(gateway);
        var result = await generator.GenerateSectionsAsync(draft);

        Assert.Single(fake.Requests);
        Assert.Contains("Section: Unpaid Change Orders", fake.Requests[0].UserPrompt);
        Assert.Equal(1, result.SectionsWritten);
        Assert.Equal(SectionStatus.Ready, facts2.Status);
        Assert.Equal(string.Empty, facts2.FailureMessage);
        Assert.Equal(new[] { "Rewritten unpaid change order facts." }, facts2.Paragraphs);
        Assert.Equal(2, draft.Revision);

        var again = await generator.GenerateSectionsAsync(draft);

        Assert.Single(fake.Requests);
        Assert.Equal(0, again.SectionsWritten);
        Assert.Equal(2, draft.Revision);
    }

    [Fact]
    public async Task GenerateSections_NoDocument_Throws()
    {
        var (gateway, _) = CaseTestData.CreateGateway();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CoreDocumentGenerator(gateway).GenerateSectionsAsync(NewDraft()));
    }

    [Fact]
    public async Task Generate_SectionPromptsNameCharacterRange()
    {
        var (gateway, fake) = CivilGateway();

        await new CoreDocumentGenerator(gateway).GenerateAsync(NewDraft());

        var parties = fake.Requests.Single(r => r.UserPrompt.Contains("Section: PARTIES"));
        Assert.Contains("about 6-10 named individuals", parties.UserPrompt);
        var facts = fake.Requests.Where(r => r.UserPrompt.Contains("Section: The Subcontract") || r.UserPrompt.Contains("Section: Unpaid Change Orders")).ToList();
        Assert.Equal(2, facts.Count);
        Assert.All(facts, r => Assert.Contains("about 6-10 named individuals", r.UserPrompt));

        var (memoGateway, memoFake) = CaseTestData.CreateGateway();
        memoFake.Responses.Enqueue(FakeLlmProvider.Ok(CriminalOutlineJson));
        memoFake.Responses.Enqueue(FakeLlmProvider.Ok(SectionJson));

        await new CoreDocumentGenerator(memoGateway).GenerateAsync(NewDraft());

        var keyIndividuals = memoFake.Requests.Single(r => r.UserPrompt.Contains("Section: KEY INDIVIDUALS AND ENTITIES"));
        Assert.Contains("about 6-10 named individuals", keyIndividuals.UserPrompt);
    }

    [Fact]
    public async Task Generate_StripsLeadingNumbers()
    {
        var (gateway, _) = CivilGateway(FakeLlmProvider.Ok(Paragraphs("1. The plaintiff is a company.", "2) It sued.", "   ")));
        var draft = NewDraft();

        await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        Assert.Equal(new[] { "The plaintiff is a company.", "It sued." }, draft.CoreDocument!.Sections[0].Paragraphs);
    }

    [Fact]
    public async Task Generate_CitationRetry()
    {
        var (gateway, fake) = CivilGateway(
            FakeLlmProvider.Ok(Paragraphs("Harmon breached the subcontract. See 531 U.S. 98 (2000).")),
            FakeLlmProvider.Ok(SectionJson));
        var draft = NewDraft();

        await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        Assert.Contains("531 U.S. 98", fake.Requests[2].UserPrompt);
        Assert.Contains("Rewrite the section without", fake.Requests[2].UserPrompt);
        Assert.StartsWith(fake.Requests[1].UserPrompt, fake.Requests[2].UserPrompt);
        Assert.DoesNotContain(draft.CoreDocument!.Sections[0].Paragraphs, p => p.Contains("531 U.S."));
        Assert.Equal(9, fake.Requests.Count);
    }

    [Fact]
    public async Task Generate_CitationRetry_StillDirty()
    {
        var dirty = Paragraphs("Harmon breached the subcontract. See 531 U.S. 98 (2000).");
        var (gateway, fake) = CivilGateway(
            FakeLlmProvider.Ok(dirty),
            FakeLlmProvider.Ok(dirty),
            FakeLlmProvider.Ok(SectionJson));
        var draft = NewDraft();

        await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        var nature = draft.CoreDocument!.Sections[0];
        Assert.Equal(SectionStatus.Ready, nature.Status);
        Assert.Contains(nature.Paragraphs, p => p.Contains("531 U.S. 98"));
        Assert.Contains("Section: PARTIES", fake.Requests[3].UserPrompt);
        Assert.Equal(9, fake.Requests.Count);
    }

    [Fact]
    public async Task Generate_SectionRefused_MarksFailed()
    {
        var (gateway, _) = CivilGateway(FakeLlmProvider.Refused("I won't write that."), FakeLlmProvider.Ok(SectionJson));
        var draft = NewDraft();

        var result = await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        var nature = draft.CoreDocument!.Sections[0];
        Assert.Equal(SectionStatus.Failed, nature.Status);
        Assert.StartsWith("The model declined", nature.FailureMessage);
        Assert.Empty(nature.Paragraphs);
        Assert.All(draft.CoreDocument.Sections.Skip(1), s => Assert.Equal(SectionStatus.Ready, s.Status));
        Assert.Equal(1, result.SectionsFailed);
        Assert.Equal(6, result.SectionsWritten);
        Assert.False(result.IsFatal);
    }

    [Fact]
    public async Task Generate_SectionTruncated_MarksFailed()
    {
        var (gateway, fake) = CivilGateway(
            FakeLlmProvider.Ok(SectionJson),
            FakeLlmProvider.Truncated(),
            FakeLlmProvider.Truncated(),
            FakeLlmProvider.Ok(SectionJson));
        var draft = NewDraft();

        var result = await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        var parties = draft.CoreDocument!.FindSection("parties")!;
        Assert.Equal(SectionStatus.Failed, parties.Status);
        Assert.Equal("The response was cut off. Click Retry Section.", parties.FailureMessage);
        Assert.Equal(1, result.SectionsFailed);
        Assert.Equal(6, result.SectionsWritten);
        Assert.Equal(9, fake.Requests.Count);
    }

    [Fact]
    public async Task Generate_FatalStopsAndKeeps()
    {
        var (gateway, fake) = CivilGateway(
            FakeLlmProvider.Ok(SectionJson),
            FakeLlmProvider.Failed(LlmFailureKind.SpendLimit, "Spend limit reached"));
        var draft = NewDraft();

        var result = await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        Assert.True(result.IsFatal);
        Assert.Contains("Spend limit reached", result.FatalError);
        var sections = draft.CoreDocument!.Sections;
        Assert.Equal(SectionStatus.Ready, sections[0].Status);
        Assert.All(sections.Skip(1), s => Assert.Equal(SectionStatus.Pending, s.Status));
        Assert.Equal(3, fake.Requests.Count);
        Assert.Equal(1, result.SectionsWritten);
        Assert.Equal(0, result.SectionsFailed);
        Assert.Equal(2, draft.Revision);
    }

    [Fact]
    public async Task Generate_MemoExecutiveSummaryLast()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(CriminalOutlineJson));
        fake.Responses.Enqueue(FakeLlmProvider.Ok(SectionJson));
        var draft = NewDraft();

        var result = await new CoreDocumentGenerator(gateway).GenerateAsync(draft);

        Assert.Equal(9, result.SectionsWritten);
        Assert.Contains("Section: EXECUTIVE SUMMARY", fake.Requests[^1].UserPrompt);
        Assert.Contains("Section: BACKGROUND", fake.Requests[1].UserPrompt);
        Assert.Contains("Memo: TO AUSA Rachel Kim", fake.Requests[1].UserPrompt);
        Assert.Equal("exec-summary", draft.CoreDocument!.Sections[0].Id);
    }

    [Fact]
    public async Task Generate_SectionPromptContainsPriorText()
    {
        var (gateway, fake) = CivilGateway(
            FakeLlmProvider.Ok(SectionJson),
            FakeLlmProvider.Ok(SectionJson),
            FakeLlmProvider.Ok(SectionJson),
            FakeLlmProvider.Ok(Paragraphs("On January 9, 2024 Dana Ortiz countersigned the subcontract.")),
            FakeLlmProvider.Ok(SectionJson));

        await new CoreDocumentGenerator(gateway).GenerateAsync(NewDraft());

        var facts2 = fake.Requests.Single(r => r.UserPrompt.Contains("Section: Unpaid Change Orders"));
        Assert.Contains("On January 9, 2024 Dana Ortiz countersigned the subcontract.", facts2.UserPrompt);
        Assert.Contains("== The Subcontract ==", facts2.UserPrompt);
        Assert.Contains("(nothing written yet)", fake.Requests[1].UserPrompt);
    }

    [Fact]
    public async Task Generate_CancelPropagates()
    {
        var (gateway, fake) = CivilGateway();
        using var cts = new CancellationTokenSource();
        fake.OnSend = i =>
        {
            if (i == 2)
                cts.Cancel();
        };
        var draft = NewDraft();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CoreDocumentGenerator(gateway).GenerateAsync(draft, null, cts.Token));

        var sections = draft.CoreDocument!.Sections;
        Assert.Equal(SectionStatus.Ready, sections[0].Status);
        Assert.All(sections.Skip(1), s => Assert.Equal(SectionStatus.Pending, s.Status));
        Assert.Equal(2, draft.Revision);
    }

    // ---------- Regenerate ----------

    [Fact]
    public async Task Regenerate_ReplacesOnlyTarget()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(Paragraphs("On March 3, 2024 the parties met at the site.", "Harmon promised payment at that meeting.")));
        var draft = CaseTestData.SampleComplaintDraft();
        var doc = draft.CoreDocument!;
        var facts1 = doc.FindSection("facts-1")!;
        facts1.Paragraphs = new List<string> { "EDITED FACT" };
        facts1.IsUserEdited = true;
        var facts2 = doc.FindSection("facts-2")!;
        facts2.IsUserEdited = true;
        var index = doc.Sections.IndexOf(facts2);
        var before = doc.Sections.Where(s => s.Id != "facts-2").ToDictionary(s => s.Id, s => string.Join("|", s.Paragraphs));

        await new CoreDocumentGenerator(gateway).RegenerateSectionAsync(draft, "facts-2", "add the March 3 site meeting");

        Assert.Same(facts2, doc.Sections[index]);
        Assert.Equal("facts-2", facts2.Id);
        Assert.Equal(new[] { "On March 3, 2024 the parties met at the site.", "Harmon promised payment at that meeting." }, facts2.Paragraphs);
        Assert.Equal(SectionStatus.Ready, facts2.Status);
        Assert.False(facts2.IsUserEdited);
        Assert.True(facts1.IsUserEdited);
        foreach (var section in doc.Sections.Where(s => s.Id != "facts-2"))
            Assert.Equal(before[section.Id], string.Join("|", section.Paragraphs));
        Assert.Equal(2, draft.Revision);

        var prompt = Assert.Single(fake.Requests).UserPrompt;
        Assert.Contains("EDITED FACT", prompt);
        Assert.Contains("Harmon refused to pay the invoices and offered no legitimate reason for withholding payment.", prompt);
        Assert.Contains("Current text of this section (rewrite it):", prompt);
        Assert.Contains("Guidance from the user: add the March 3 site meeting", prompt);
    }

    [Fact]
    public async Task Regenerate_Failure_KeepsOldText()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("No."));
        var draft = CaseTestData.SampleComplaintDraft();
        var facts2 = draft.CoreDocument!.FindSection("facts-2")!;
        var oldText = facts2.Paragraphs.ToList();

        await Assert.ThrowsAsync<LlmCallException>(() => new CoreDocumentGenerator(gateway).RegenerateSectionAsync(draft, "facts-2", ""));

        Assert.Equal(oldText, facts2.Paragraphs);
        Assert.Equal(SectionStatus.Ready, facts2.Status);
        Assert.Equal(1, draft.Revision);
    }

    [Fact]
    public async Task Regenerate_UnknownId_Throws()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CoreDocumentGenerator(gateway).RegenerateSectionAsync(CaseTestData.SampleComplaintDraft(), "facts-9", ""));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CoreDocumentGenerator(gateway).RegenerateSectionAsync(NewDraft(), "facts-1", ""));
        Assert.Empty(fake.Requests);
    }
}
