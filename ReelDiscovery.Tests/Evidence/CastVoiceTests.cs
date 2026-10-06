using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

public class CastVoiceTests
{
    private const string Hint = "Click Retry Generation.";

    private static CaseGuide Guide()
    {
        var guide = new CaseGuide();
        guide.Organizations.Add(new CaseOrganization { Id = "ORG-01", Name = "Brightline Glazing, LLC", Domain = "brightline.com" });
        guide.Organizations.Add(new CaseOrganization { Id = "ORG-02", Name = "Harmon Builders, Inc.", Domain = "harmon.com" });
        guide.Cast.Add(new CastMember
        {
            Id = "CH-001", FirstName = "Oscar", LastName = "Martinez", Title = "Controller", OrganizationId = "ORG-01",
            Involvement = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"w{i}"))
        });
        guide.Cast.Add(new CastMember { Id = "CH-002", FirstName = "Dana", LastName = "Ortiz", Title = "Project Manager", OrganizationId = "ORG-02", Involvement = "Approved the change orders." });
        guide.Cast.Add(new CastMember { Id = "CH-003", FirstName = "Walter", LastName = "Hale", Title = "CFO", OrganizationId = "ORG-02", Involvement = "Withheld payment." });
        guide.Cast.Add(new CastMember { Id = "CH-004", FirstName = "Nina", LastName = "Park", Title = "Estimator", OrganizationId = "ORG-01", Involvement = "Priced the work." });
        return guide;
    }

    private static CaseDraft Draft(CaseGuide guide, bool clearSubstitutions = false)
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Guide = guide;
        if (clearSubstitutions)
            draft.Substitutions.Clear();
        return draft;
    }

    private static string Json(params (string PersonId, string Voice)[] voices) =>
        JsonSerializer.Serialize(new { voices = voices.Select(v => new { personId = v.PersonId, voice = v.Voice }) });

    private static async Task<(CastVoiceOutcome Outcome, FakeLlmProvider Fake, PlanCallRun Run)> Build(CaseGuide guide, LlmProviderResponse response)
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Responses.Enqueue(response);
        var run = new PlanCallRun(CancellationToken.None, 2, Hint);
        var outcome = await CastVoiceBuilder.BuildAsync(gateway, Draft(guide), guide, run);
        return (outcome, fake, run);
    }

    private const string CleanJson = """{ "voices": [ { "personId": "CH-001", "voice": "onyx" }, { "personId": "CH-002", "voice": "nova" }, { "personId": "CH-003", "voice": "echo" }, { "personId": "CH-004", "voice": "shimmer" } ] }""";

    [Fact]
    public async Task CleanResponse_FourVoices_OneVolumeRequest()
    {
        var guide = Guide();
        var (outcome, fake, run) = await Build(guide, FakeLlmProvider.Ok(CleanJson));
        using var _ = run;

        var set = outcome.Voices!;
        Assert.False(set.FromFallback);
        Assert.Equal(new[] { "CH-001", "CH-002", "CH-003", "CH-004" }, set.Voices.Select(v => v.PersonId));
        Assert.Equal(new[] { "onyx", "nova", "echo", "shimmer" }, set.Voices.Select(v => v.Voice));
        Assert.Single(fake.Requests);
        Assert.Equal(CaseTestData.VolumeModel().ModelId, fake.Requests[0].ModelId);
        Assert.Equal("CastVoiceDto", fake.Requests[0].SchemaName);
        Assert.Equal(CastVoiceBuilder.SystemPrompt, fake.Requests[0].SystemPrompt);

        var request = CastVoiceBuilder.BuildRequest(Draft(guide), guide);
        Assert.Equal(LlmTier.Volume, request.Tier);
        Assert.Equal("Generate Corpus - voicemail voices", request.OperationName);
        // Single calls stay live (keep the default path fast)
        Assert.False(request.AllowBatch);
    }

    [Fact]
    public void UserPrompt_ListsPeopleAndVoices()
    {
        var guide = Guide();
        var request = CastVoiceBuilder.BuildRequest(Draft(guide, clearSubstitutions: true), guide);
        var prompt = request.UserPrompt;

        var involvement = string.Join(" ", Enumerable.Range(1, 25).Select(i => $"w{i}"));
        Assert.Contains($"CH-001 | Oscar Martinez | Controller | Brightline Glazing, LLC | {involvement}" + Environment.NewLine, prompt);
        Assert.DoesNotContain("w26", prompt);
        Assert.Contains("CH-003 | Walter Hale | CFO | Harmon Builders, Inc. | Withheld payment.", prompt);
        foreach (var voice in CastVoiceSet.AllowedVoices)
            Assert.Contains($"- {voice}: ", prompt);
        Assert.Contains("Keep every name the document uses exactly as written", prompt);
        Assert.DoesNotContain("fictional", prompt + request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownInvalidAndMissing_GetFallback()
    {
        var guide = Guide();
        var json = Json(("CH-001", "onyx"), ("CH-999", "nova"), ("CH-002", "banana"), ("CH-004", "fable"));

        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run;

        var set = outcome.Voices!;
        Assert.False(set.FromFallback);
        Assert.Equal(new[] { "CH-001", "CH-002", "CH-003", "CH-004" }, set.Voices.Select(v => v.PersonId));
        Assert.Equal("onyx", set.VoiceFor("CH-001"));
        Assert.Equal(VoiceAssignments.Fallback("CH-002"), set.VoiceFor("CH-002"));
        Assert.Equal(VoiceAssignments.Fallback("CH-003"), set.VoiceFor("CH-003"));
        Assert.Equal("fable", set.VoiceFor("CH-004"));
        Assert.Null(set.VoiceFor("CH-999"));
    }

    [Fact]
    public async Task MixedCaseVoice_StoredLowercase()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Ok(Json(("CH-001", "Onyx"))));
        using var _ = run;

        Assert.Equal("onyx", outcome.Voices!.VoiceFor("CH-001"));
    }

    [Fact]
    public async Task Refusal_AllFallback()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Refused("No."));
        using var _ = run;

        var set = outcome.Voices!;
        Assert.True(set.FromFallback);
        Assert.False(run.IsFatal);
        Assert.All(guide.Cast, c => Assert.Equal(VoiceAssignments.Fallback(c.Id), set.VoiceFor(c.Id)));
    }

    [Fact]
    public async Task ProviderFailure_AllFallback()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Failed(LlmFailureKind.BadRequest, "bad"));
        using var _ = run;

        Assert.True(outcome.Voices!.FromFallback);
        Assert.Equal(4, outcome.Voices.Voices.Count);
    }

    [Fact]
    public async Task AuthFailure_VoicesNull_RunFatal()
    {
        var guide = Guide();
        var (outcome, _, run) = await Build(guide, FakeLlmProvider.Failed(LlmFailureKind.Auth, "bad key"));
        using var _ = run;

        Assert.Null(outcome.Voices);
        Assert.True(run.IsFatal);
    }

    [Fact]
    public void FromRecord_RequiresEveryCastId()
    {
        var guide = Guide();
        var full = CastVoiceBuilder.Fallback(guide);
        var record = new JournalRecord { Type = JournalRecordType.CastVoices, CastVoices = full };
        Assert.Same(full, CastVoiceBuilder.FromRecord(record, guide));

        var edited = new CastVoiceSet
        {
            Voices = guide.Cast.Select(c => new CastVoice { PersonId = c.Id, Voice = " Nova " }).ToList()
        };
        var normalized = CastVoiceBuilder.FromRecord(new JournalRecord { Type = JournalRecordType.CastVoices, CastVoices = edited }, guide);
        Assert.NotNull(normalized);
        Assert.All(normalized!.Voices, v => Assert.Equal("nova", v.Voice));

        var missing = CastVoiceBuilder.Fallback(guide);
        missing.Voices.RemoveAt(2);
        Assert.Null(CastVoiceBuilder.FromRecord(new JournalRecord { Type = JournalRecordType.CastVoices, CastVoices = missing }, guide));
        Assert.Null(CastVoiceBuilder.FromRecord(null, guide));
        Assert.Null(CastVoiceBuilder.FromRecord(new JournalRecord { Type = JournalRecordType.CastVoices }, guide));
    }

    [Fact]
    public async Task SameInputs_IdenticalSet()
    {
        var guide = Guide();
        var json = Json(("CH-001", "onyx"), ("CH-003", "banana"));

        var (first, _, run1) = await Build(guide, FakeLlmProvider.Ok(json));
        using var _ = run1;
        var (second, _, run2) = await Build(guide, FakeLlmProvider.Ok(json));
        using var __ = run2;

        Assert.Equal(first.Voices!.Voices.Select(v => (v.PersonId, v.Voice)), second.Voices!.Voices.Select(v => (v.PersonId, v.Voice)));
    }

    [Fact]
    public void Dto_PassesStrictSchema()
    {
        foreach (var type in CastVoiceBuilder.SchemaTypes)
        {
            var schema = JsonSchemaRegistry.GetSchemaJson(type);
            var errors = StrictSchemaValidator.Validate(schema);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
        }
    }
}
