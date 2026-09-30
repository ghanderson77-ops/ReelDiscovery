using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Generation;

public class LegacyGeneratorPortTests
{
    public static TheoryData<string, Type> AllSchemaTypes()
    {
        var data = new TheoryData<string, Type>();
        foreach (var type in StorylineGenerator.SchemaTypes)
            data.Add(nameof(StorylineGenerator), type);
        foreach (var type in CharacterGenerator.SchemaTypes)
            data.Add(nameof(CharacterGenerator), type);
        foreach (var type in ThemeGenerator.SchemaTypes)
            data.Add(nameof(ThemeGenerator), type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllSchemaTypes))]
    public void AllSchemaTypes_AreStrict(string generator, Type type)
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(type);

        var errors = StrictSchemaValidator.Validate(schema);

        Assert.True(errors.Count == 0, $"{generator}.{type.Name}: {string.Join("; ", errors)}");
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{generator}.{type.Name} exceeds 16 union types");
    }

    [Fact]
    public void SchemaTypes_CoverAllDtos()
    {
        Assert.Contains(StorylineGenerator.SchemaTypes, t => t.Name == "StorylineApiResponse");
        Assert.Contains(CharacterGenerator.SchemaTypes, t => t.Name == "CharacterApiResponse");
        Assert.Contains(ThemeGenerator.SchemaTypes, t => t.Name == "ThemeApiResponse");
    }

    private static AIModelConfig StoryModel() => new()
    {
        ModelId = "claude-story",
        DisplayName = "Story",
        Provider = LlmProviderKind.Anthropic,
        MaxOutputTokens = 64_000
    };

    private static AIModelConfig VolumeModel() => new()
    {
        ModelId = "claude-volume",
        DisplayName = "Volume",
        Provider = LlmProviderKind.Anthropic,
        MaxOutputTokens = 64_000
    };

    private static (LlmGateway Gateway, FakeLlmProvider Fake) CreateGateway()
    {
        // Anthropic fake proves the generators are provider-agnostic
        var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        return (new LlmGateway(fake, StoryModel(), VolumeModel()), fake);
    }

    private static void AssertNoJsonInstructions(LlmProviderRequest request)
    {
        var prompts = request.SystemPrompt + request.UserPrompt;
        Assert.DoesNotContain("Respond with JSON", prompts);
        Assert.DoesNotContain("valid JSON", prompts);
    }

    private const string StorylineJson = """
        {
          "storylines": [
            { "title": "The Red Wedding Aftermath", "description": "Frey vs Stark.", "timelineHint": "Spans 2 weeks", "suggestedEmailCount": 50, "keyCharacterRoles": ["Lord"] },
            { "title": "Wildfire at the Sept", "description": "Cersei vs the Faith.", "timelineHint": "Single day event", "suggestedEmailCount": 1, "keyCharacterRoles": [] }
          ],
          "suggestedDateRange": { "reasoning": "Season 3", "startDate": "2013-06-01", "endDate": "2013-07-15" }
        }
        """;

    private const string CharacterJson = """
        {
          "primaryCompanyDomain": "dundermifflin.com",
          "primaryCompanyName": "Dunder Mifflin",
          "characters": [
            { "firstName": "Jim", "lastName": "Halpert", "gender": "male", "role": "Sales", "department": "Sales", "organization": "Dunder Mifflin", "domain": "dundermifflin.com", "isExternal": false, "personalityNotes": "Pranks Dwight.", "signatureBlock": "Jim Halpert\nSales", "storylineIndices": [0, 7] },
            { "firstName": "Jan", "lastName": "O'Neil", "gender": "female", "role": "VP", "department": "Corporate", "organization": "Serenity by Jan", "domain": "serenitybyjan.com", "isExternal": true, "personalityNotes": "Volatile.", "signatureBlock": "", "storylineIndices": [] }
          ]
        }
        """;

    private const string ThemeJson = """
        {
          "themes": [
            { "domain": "dundermifflin.com", "organizationName": "Dunder Mifflin", "themeName": "Corporate Navy", "primaryColor": "#1f3a5f", "secondaryColor": "zzzzzz", "accentColor": "ED7D31", "headingFont": "Georgia", "bodyFont": "Comic Sans MS" }
          ]
        }
        """;

    private static List<Character> ThemeCharacters() => new()
    {
        new Character { FirstName = "Jim", LastName = "Halpert", Email = "jim.halpert@dundermifflin.com", Organization = "Dunder Mifflin" }
    };

    [Fact]
    public async Task Storyline_RoundTrip_UsesStoryTier()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(StorylineJson));

        var result = await new StorylineGenerator(gateway).GenerateStorylinesAsync("Game of Thrones", "", storylineCount: 2);

        Assert.Equal(2, result.Storylines.Count);
        Assert.Equal("The Red Wedding Aftermath", result.Storylines[0].Title);
        Assert.Equal(20, result.Storylines[0].SuggestedEmailCount); // clamped from 50
        Assert.Equal(5, result.Storylines[1].SuggestedEmailCount);  // clamped from 1
        Assert.Equal(new DateTime(2013, 6, 1), result.SuggestedStartDate);
        Assert.Equal(new DateTime(2013, 7, 15), result.SuggestedEndDate);
        Assert.Equal("Season 3", result.DateRangeReasoning);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("claude-story", request.ModelId);
        AssertNoJsonInstructions(request);
    }

    [Fact]
    public async Task Storyline_EmptyDateRange_LeavesDatesUnset()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(
            "{\"storylines\":[],\"suggestedDateRange\":{\"reasoning\":\"\",\"startDate\":\"\",\"endDate\":\"\"}}"));

        var result = await new StorylineGenerator(gateway).GenerateStorylinesAsync("Topic", "", storylineCount: 1);

        Assert.Null(result.SuggestedStartDate);
        Assert.Null(result.SuggestedEndDate);
        Assert.Null(result.DateRangeReasoning);
    }

    [Fact]
    public async Task Storyline_Refused_ThrowsLlmCallException()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("I can't help with that"));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() =>
            new StorylineGenerator(gateway).GenerateStorylinesAsync("Topic", "", storylineCount: 2));

        Assert.Equal(LlmOutcome.Refused, ex.Result.Outcome);
        Assert.Single(fake.Requests); // no generator-level retry
    }

    [Fact]
    public async Task Character_RoundTrip_UsesStoryTier()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(CharacterJson));
        var storylines = new List<Storyline> { new() { Title = "S0" }, new() { Title = "S1" } };

        var result = await new CharacterGenerator(gateway).GenerateCharactersAsync("The Office", storylines);

        Assert.Equal("dundermifflin.com", result.CompanyDomain);
        Assert.Equal("Dunder Mifflin", result.CompanyName);
        Assert.Equal(2, result.Characters.Count);
        Assert.Equal("jim.halpert@dundermifflin.com", result.Characters[0].Email);
        Assert.Equal("jan.oneil@serenitybyjan.com", result.Characters[1].Email);
        Assert.True(result.Characters[1].IsExternal);
        Assert.Equal("Jim Halpert\nSales", result.Characters[0].SignatureBlock);

        // Out-of-range storyline index 7 is ignored
        Assert.Contains(result.Characters[0].Id, storylines[0].InvolvedCharacterIds);
        Assert.Empty(storylines[1].InvolvedCharacterIds);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("claude-story", request.ModelId);
        AssertNoJsonInstructions(request);
    }

    [Fact]
    public async Task Theme_UsesVolumeTier()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThemeJson));

        var themes = await new ThemeGenerator(gateway).GenerateThemesForDomainsAsync("The Office", ThemeCharacters());

        var theme = themes["dundermifflin.com"];
        Assert.Equal("1F3A5F", theme.PrimaryColor);
        Assert.Equal("5B9BD5", theme.SecondaryColor); // invalid hex -> default
        Assert.Equal("ED7D31", theme.AccentColor);
        Assert.Equal("Georgia", theme.HeadingFont);
        Assert.Equal("Segoe UI", theme.BodyFont); // not in allowlist -> default

        var request = Assert.Single(fake.Requests);
        Assert.Equal("claude-volume", request.ModelId);
        AssertNoJsonInstructions(request);
    }

    [Fact]
    public async Task Theme_NonFatalFailure_FallsBack()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(new LlmProviderResponse { Outcome = LlmOutcome.Invalid, Message = "bad output" });

        var themes = await new ThemeGenerator(gateway).GenerateThemesForDomainsAsync("The Office", ThemeCharacters());

        var theme = themes["dundermifflin.com"];
        Assert.Equal("Default Corporate", theme.ThemeName);
        Assert.Equal("Dunder Mifflin", theme.OrganizationName);
    }

    [Fact]
    public async Task Theme_Fatal_Throws()
    {
        var (gateway, fake) = CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(LlmFailureKind.SpendLimit, "credit balance too low"));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() =>
            new ThemeGenerator(gateway).GenerateThemesForDomainsAsync("The Office", ThemeCharacters()));

        Assert.Equal(LlmFailureKind.SpendLimit, ex.Result.FailureKind);
    }
}
