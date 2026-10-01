using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

public class RealEntityDetectorTests
{
    private const string Scenario = "Jeffrey Skilling pressures Arthur Andersen to sign off on the Q3 accounts.";

    private const string TwoEntitiesJson = """
        {
          "entities": [
            { "name": "Jeffrey Skilling", "aliases": ["Skilling", "Jeff"], "kind": "Person", "reason": "Former Enron chief executive.", "suggestedSubstitute": "Gregory Hollis" },
            { "name": "Arthur Andersen", "aliases": ["Andersen"], "kind": "Company", "reason": "Real accounting firm.", "suggestedSubstitute": "Whitcombe Lyle LLP" }
          ]
        }
        """;

    [Fact]
    public async Task Detect_MapsEntities_ApplyTrue()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(TwoEntitiesJson));

        var result = await new RealEntityDetector(gateway).DetectAsync(Scenario);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.True(e.Apply));

        Assert.Equal("Jeffrey Skilling", result[0].Original);
        Assert.Equal(EntityKind.Person, result[0].Kind);
        Assert.Equal(new[] { "Skilling", "Jeff" }, result[0].Aliases);
        Assert.Equal("Gregory Hollis", result[0].Substitute);
        Assert.Equal("Former Enron chief executive.", result[0].Reason);

        Assert.Equal("Arthur Andersen", result[1].Original);
        Assert.Equal(EntityKind.Company, result[1].Kind);
        Assert.Equal(new[] { "Andersen" }, result[1].Aliases);
        Assert.Equal("Whitcombe Lyle LLP", result[1].Substitute);
    }

    [Fact]
    public async Task Detect_DropsBlankNames_AndCaseInsensitiveDuplicates()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("""
            {
              "entities": [
                { "name": "  ", "aliases": [], "kind": "Person", "reason": "r", "suggestedSubstitute": "X" },
                { "name": " Enron ", "aliases": [], "kind": "Company", "reason": "first", "suggestedSubstitute": "Northwind Energy Inc." },
                { "name": "ENRON", "aliases": [], "kind": "Company", "reason": "second", "suggestedSubstitute": "Other Co" }
              ]
            }
            """));

        var result = await new RealEntityDetector(gateway).DetectAsync(Scenario);

        var entity = Assert.Single(result);
        Assert.Equal("Enron", entity.Original);
        Assert.Equal("first", entity.Reason);
        Assert.Equal("Northwind Energy Inc.", entity.Substitute);
    }

    [Fact]
    public async Task Detect_CleansAliases()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("""
            {
              "entities": [
                { "name": "Walter White", "aliases": [" Walt ", "walt", "", "   ", "walter white", "White"], "kind": "Person", "reason": "r", "suggestedSubstitute": "Hank Brennan" }
              ]
            }
            """));

        var result = await new RealEntityDetector(gateway).DetectAsync(Scenario);

        var entity = Assert.Single(result);
        Assert.Equal(new[] { "Walt", "White" }, entity.Aliases);
    }

    [Fact]
    public async Task Detect_BlankSubstitute_StaysEmpty()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("""
            {
              "entities": [
                { "name": "Enron", "aliases": [], "kind": "Company", "reason": "r", "suggestedSubstitute": "   " }
              ]
            }
            """));

        var result = await new RealEntityDetector(gateway).DetectAsync(Scenario);

        var entity = Assert.Single(result);
        Assert.Equal("", entity.Substitute);
        Assert.True(entity.Apply);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n ")]
    public async Task Detect_BlankScenario_ReturnsEmpty_NoRequest(string scenario)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();

        var result = await new RealEntityDetector(gateway).DetectAsync(scenario);

        Assert.Empty(result);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Detect_Request_StoryTier_ScenarioOnlyInUserPrompt()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(TwoEntitiesJson));

        await new RealEntityDetector(gateway).DetectAsync(Scenario);

        var request = Assert.Single(fake.Requests);
        Assert.Equal("claude-story", request.ModelId);
        Assert.Equal(RealEntityDetector.MaxOutputTokens, request.MaxTokens);
        Assert.Equal(4000, request.MaxTokens);
        Assert.Contains(Scenario, request.UserPrompt);
        Assert.DoesNotContain(Scenario, request.SystemPrompt);
        Assert.DoesNotContain("Jeffrey Skilling", request.SystemPrompt);
        CaseTestData.AssertNoJsonInstructions(request);
        Assert.DoesNotContain("json", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detect_Refused_ThrowsLlmCallException_NoRetry()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Refused("I can't help with that"));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() =>
            new RealEntityDetector(gateway).DetectAsync(Scenario));

        Assert.Equal(LlmOutcome.Refused, ex.Result.Outcome);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Detect_SpendLimit_ThrowsFatal()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Failed(LlmFailureKind.SpendLimit, "Spend cap reached"));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() =>
            new RealEntityDetector(gateway).DetectAsync(Scenario));

        Assert.True(ex.Result.IsFatal);
    }

    [Fact]
    public async Task Detect_EmptyEntities_ReturnsEmptyList()
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("""{"entities":[]}"""));

        var result = await new RealEntityDetector(gateway).DetectAsync(Scenario);

        Assert.Empty(result);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Detect_CancelledToken_ThrowsOperationCanceled()
    {
        // The scenario step's "Cancel check" link and step disposal cancel this token (WR-07)
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(TwoEntitiesJson));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RealEntityDetector(gateway).DetectAsync(Scenario, cts.Token));
    }

    [Fact]
    public async Task Detect_SystemPrompt_ExcludesNamesThatExistOnlyInFiction()
    {
        // A scenario based on a TV show or film keeps its character and company names
        var (gateway, fake) = CaseTestData.CreateGateway();
        fake.Responses.Enqueue(FakeLlmProvider.Ok("""{"entities":[]}"""));

        await new RealEntityDetector(gateway).DetectAsync("Dunder Mifflin regional manager Michael Scott hides losses.");

        var request = Assert.Single(fake.Requests);
        Assert.Contains("already fictional, so never list them", request.SystemPrompt);
        Assert.DoesNotContain("every named character or organization taken from", request.SystemPrompt);
    }

    [Fact]
    public void EntityKind_HasOnlyRealWorldKinds()
    {
        Assert.Equal(new[] { "Person", "Company", "Brand" }, Enum.GetNames<EntityKind>());
    }

    [Fact]
    public void SchemaTypes_AreStrict()
    {
        foreach (var type in RealEntityDetector.SchemaTypes)
        {
            var errors = StrictSchemaValidator.Validate(JsonSchemaRegistry.GetSchemaJson(type));
            Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
        }
    }
}
