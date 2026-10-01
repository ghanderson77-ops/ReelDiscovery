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
