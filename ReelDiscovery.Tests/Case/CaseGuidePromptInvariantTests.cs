using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Case.CaseGuideRegenerationTests;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// W1 / D-01: every case guide request path sends the byte-identical shared system prompt (no run data in it) and carries
/// the effective scenario and the name rules, including the user's kept names, in the user prompt. Prompts never ask for JSON (strict schemas do).
/// </summary>
public class CaseGuidePromptInvariantTests
{
    public static TheoryData<string> RequestPaths() => new()
    {
        "roster",
        "cast details",
        "background",
        "counsel top-up",
        "facts",
        "values",
        "Regenerate Cast",
        "Regenerate Facts",
        "Regenerate Values",
        "entry Character",
        "entry Fact",
        "entry Value",
        "issue details"
    };

    [Theory]
    [MemberData(nameof(RequestPaths))]
    public async Task EveryRequest_SharesSystemPromptAndNameRules(string path)
    {
        var (draft, requests) = await RunAsync(path);

        Assert.NotEmpty(requests);
        // The fixture has a kept (not substituted) name, so the CASE-03 override line must be present
        Assert.Contains(draft.Substitutions, s => !s.Apply);
        Assert.All(requests, request =>
        {
            Assert.Equal(CaseGuidePrompts.SystemPrompt, request.SystemPrompt, StringComparer.Ordinal);
            Assert.DoesNotContain(draft.EffectiveScenario, request.SystemPrompt);
            Assert.Contains("Keep every name the document uses exactly as written", request.UserPrompt);
            // CR-01 / D-01: the effective scenario reaches every user prompt, so scenario-only names are kept
            Assert.Contains(draft.EffectiveScenario, request.UserPrompt);
            Assert.Contains("Oscar Martinez", request.UserPrompt);
            Assert.Contains("Keep every name the scenario uses exactly as written", request.UserPrompt);
            Assert.DoesNotContain("Oscar Martinez", request.SystemPrompt);
            Assert.Contains("The user has approved keeping these names exactly as written", request.UserPrompt);
            CaseTestData.AssertNoJsonInstructions(request);
        });
    }

    // First-generation cast for SampleComplaintDraft: the roster leaves Harmon without in-house counsel, so one top-up runs
    private static string FirstRoster() => Roster(new[]
    {
        Person("", "Maria", "Delgado", "President", "ORG-01", "Party"),
        Person("", "Tom", "Becker", "Project Manager", "ORG-01", "Staff"),
        Person("", "Raymond", "Harmon", "Chief Executive Officer", "ORG-02", "Party"),
        Person("", "Lisa", "Chen", "Project Executive", "ORG-02", "Staff"),
        Person("", "Karen", "Walsh", "General Counsel", "ORG-01", "InHouseCounsel"),
        Person("", "Morgan", "Ellis", "Partner", "Ellis & Park LLP", "OutsideCounsel"),
        Person("", "Evan", "Price", "Associate", "Ellis & Park LLP", "OutsideCounsel"),
        Person("", "Nora", "Quist", "Partner", "Thistle & Bracken LLP", "OutsideCounsel"),
        Person("", "Owen", "Hale", "Associate", "Thistle & Bracken LLP", "OutsideCounsel")
    }, Firm("Ellis & Park LLP", "Ellis & Park", "ORG-01"), Firm("Thistle & Bracken LLP", "Thistle & Bracken", "ORG-02"));

    private static async Task<(CaseDraft Draft, List<LlmProviderRequest> Requests)> RunAsync(string path)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        var generator = new CaseGuideGenerator(gateway);
        CaseDraft draft;

        switch (path)
        {
            case "roster":
            case "cast details":
            case "background":
            case "counsel top-up":
                draft = CaseTestData.SampleComplaintDraft();
                Enqueue(fake, FirstRoster(), Roster(new[] { Person("", "Paul", "Novak", "General Counsel", "ORG-02", "InHouseCounsel") }), DetailsJson());
                Enqueue(fake, Enumerable.Repeat(BackgroundJson, 4).ToArray());
                await generator.GenerateCastAsync(draft);
                Func<LlmProviderRequest, bool> filter = path switch
                {
                    "roster" => IsRosterRequest,
                    "cast details" => IsDetailsRequest,
                    "background" => IsBackgroundRequest,
                    _ => IsTopUpRequest
                };
                return (draft, fake.Requests.Where(filter).ToList());

            case "facts":
                draft = CaseTestData.SampleGuidedDraft();
                draft.Guide!.Facts.Clear();
                draft.Guide.Values.Clear();
                draft.Guide.FactsState = new GuidePartState();
                Enqueue(fake, FactsJson(FactJson("2024-01-15", "Subcontract signed")));
                await generator.GenerateFactsAsync(draft);
                break;

            case "values":
                draft = CaseTestData.SampleGuidedDraft();
                draft.Guide!.Values.Clear();
                draft.Guide.ValuesState = new GuidePartState();
                Enqueue(fake, ValuesJson(ValueJson("", "Amount", "Change Order 7 amount", "$184,250.00", "\"F-003\"")));
                await generator.GenerateValuesAsync(draft);
                break;

            case "Regenerate Cast":
                draft = CaseTestData.SampleCounselDraft();
                Enqueue(fake, Roster(BasePeople()), DetailsJson());
                Enqueue(fake, Enumerable.Repeat(BackgroundJson, 4).ToArray());
                await generator.RegenerateTabAsync(draft, GuidePart.Cast, "add a second associate", false);
                break;

            case "Regenerate Facts":
                draft = CaseTestData.SampleGuidedDraft();
                Enqueue(fake, FactsJson(FactJson("2024-01-15", "Subcontract signed", existingId: "F-001")));
                await generator.RegenerateTabAsync(draft, GuidePart.Facts, "", false);
                break;

            case "Regenerate Values":
                draft = CaseTestData.SampleGuidedDraft();
                Enqueue(fake, ValuesJson(ValueJson("V-001", "Amount", "Change Order 7 amount", "$184,250.00", "\"F-003\"")));
                await generator.RegenerateTabAsync(draft, GuidePart.Values, "", false);
                break;

            case "entry Character":
                draft = CaseTestData.SampleGuidedDraft();
                Enqueue(fake, CastEntryJson("Tom", "Becker", "Project Manager"));
                await generator.RegenerateEntryAsync(draft, GuidePart.Cast, "CH-002", "");
                break;

            case "entry Fact":
                draft = CaseTestData.SampleGuidedDraft();
                Enqueue(fake, FactEntryJson);
                await generator.RegenerateEntryAsync(draft, GuidePart.Facts, "F-003", "");
                break;

            case "entry Value":
                draft = CaseTestData.SampleGuidedDraft();
                Enqueue(fake, ValueEntryJson);
                await generator.RegenerateEntryAsync(draft, GuidePart.Values, "V-001", "");
                break;

            case "issue details":
                draft = CaseTestData.SampleComplaintDraft();
                Enqueue(fake, """{ "issues": [ { "issueId": "ISS-01", "description": "Harmon refused to pay.", "elements": ["A valid subcontract"], "relevanceCriteria": "Payment emails." } ] }""");
                await generator.GenerateIssuesAsync(draft);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, "Unknown request path");
        }

        return (draft, fake.Requests.ToList());
    }

    private static void Enqueue(FakeLlmProvider fake, params string[] responses)
    {
        foreach (var response in responses)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(response));
    }
}
