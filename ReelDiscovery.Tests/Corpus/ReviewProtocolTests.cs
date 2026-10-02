using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

public class ReviewProtocolTests
{
    private const string ProseJson = """
        {
          "purpose": "This protocol guides review of documents collected for the Lakeview Tower payment dispute. See BRG-000123 for an example.",
          "background": "Brightline Glazing installed windows on Lakeview Tower and was not paid for Change Order 7.",
          "hotGuidance": "Flag documents that directly show Harmon approving or refusing Change Order 7.",
          "privilegeGuidance": "Communications with Karen Walsh, Paul Novak or outside counsel are privileged unless a third party is copied.",
          "issues": [
            { "issueId": "ISS-01", "relevantExamples": ["An email approving Change Order 7.", "An invoice cover note."], "notRelevantExamples": ["A lunch invitation.", "A newsletter."] },
            { "issueId": " iss-02 ", "relevantExamples": ["A late-payment notice."], "notRelevantExamples": ["A holiday schedule."] },
            { "issueId": "ISS-99", "relevantExamples": ["Unknown."], "notRelevantExamples": [] }
          ]
        }
        """;

    private static (RecordingGateway Gateway, FakeLlmProvider Fake) Gateway(params LlmProviderResponse[] responses)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        foreach (var response in responses)
            fake.Responses.Enqueue(response);
        return (new RecordingGateway(gateway), fake);
    }

    private static (List<ValidationErrorInfo> Errors, string Text) OpenAndValidate(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => p.InnerText);
        return (errors, string.Join("\n", paragraphs));
    }

    [Fact]
    public void SchemaTypes_AreStrict()
    {
        Assert.NotEmpty(ReviewProtocolBuilder.SchemaTypes);
        foreach (var type in ReviewProtocolBuilder.SchemaTypes)
        {
            var schema = JsonSchemaRegistry.GetSchemaJson(type);
            var errors = StrictSchemaValidator.Validate(schema);
            Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
            Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16);
        }
    }

    [Fact]
    public async Task GenerateProse_MapsIssuesById_AndSendsStoryRequest()
    {
        var (gateway, fake) = Gateway(FakeLlmProvider.Ok(ProseJson));
        var builder = new ReviewProtocolBuilder(gateway);

        var prose = await builder.GenerateProseAsync(CorpusTestData.RichDraft());

        Assert.False(prose.IsFallback);
        Assert.Equal(new[] { "ISS-01", "ISS-02" }, prose.Issues.Select(i => i.IssueId));
        Assert.Equal(2, prose.Issues[0].RelevantExamples.Count);
        Assert.Equal("A late-payment notice.", prose.Issues[1].RelevantExamples.Single());
        Assert.StartsWith("Brightline Glazing installed", prose.Background);
        Assert.DoesNotMatch(new Regex(@"[A-Z]{2,6}-\d{6}"), prose.Purpose);

        var request = Assert.Single(gateway.Requests);
        Assert.Equal(LlmTier.Story, request.Tier);
        Assert.Equal("Corpus Plan - Review protocol", request.OperationName);
        Assert.Equal(8000, request.MaxOutputTokens);
        Assert.Equal(CorpusPlanPrompts.SystemPrompt, request.SystemPrompt);
        CaseTestData.AssertNoJsonInstructions(Assert.Single(fake.Requests));
    }

    [Fact]
    public async Task GenerateProse_FatalFailure_Propagates()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key"));
        var builder = new ReviewProtocolBuilder(gateway);

        var ex = await Assert.ThrowsAsync<LlmCallException>(() => builder.GenerateProseAsync(CorpusTestData.RichDraft()));
        Assert.True(ex.Result.IsFatal);
    }

    [Fact]
    public async Task GenerateProse_Refused_ReturnsFallback()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Refused("no"));
        var builder = new ReviewProtocolBuilder(gateway);

        var prose = await builder.GenerateProseAsync(CorpusTestData.RichDraft());

        Assert.True(prose.IsFallback);
        Assert.NotEmpty(prose.Purpose);
    }

    [Fact]
    public async Task GenerateProse_EmptyFields_ReturnsFallback()
    {
        const string empty = """{ "purpose": "", "background": "", "hotGuidance": "", "privilegeGuidance": "", "issues": [] }""";
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(empty));
        var builder = new ReviewProtocolBuilder(gateway);

        var prose = await builder.GenerateProseAsync(CorpusTestData.RichDraft());

        Assert.True(prose.IsFallback);
    }

    [Fact]
    public async Task GenerateProse_UserCancel_Throws()
    {
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(ProseJson));
        var builder = new ReviewProtocolBuilder(gateway);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.GenerateProseAsync(CorpusTestData.RichDraft(), cts.Token));
    }

    [Fact]
    public void FallbackProse_IsBuiltFromGuide()
    {
        var draft = CorpusTestData.RichDraft();

        var prose = ReviewProtocolBuilder.FallbackProse(draft);

        Assert.True(prose.IsFallback);
        Assert.False(string.IsNullOrWhiteSpace(prose.Purpose));
        Assert.False(string.IsNullOrWhiteSpace(prose.Background));
        Assert.False(string.IsNullOrWhiteSpace(prose.HotGuidance));
        Assert.False(string.IsNullOrWhiteSpace(prose.PrivilegeGuidance));
        Assert.Equal(draft.Guide!.Issues.Select(i => i.Id), prose.Issues.Select(i => i.IssueId));
        foreach (var (issue, issueProse) in draft.Guide.Issues.Zip(prose.Issues))
        {
            var example = Assert.Single(issueProse.RelevantExamples);
            Assert.Contains(issue.RelevanceCriteria.TrimEnd('.'), example);
            Assert.Empty(issueProse.NotRelevantExamples);
        }
    }

    [Fact]
    public async Task Build_ValidatesAndContainsReviewerManual()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var (gateway, _) = Gateway(FakeLlmProvider.Ok(ProseJson));
        var prose = await new ReviewProtocolBuilder(gateway).GenerateProseAsync(draft);

        var (errors, text) = OpenAndValidate(ReviewProtocolBuilder.Build(draft, prose));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains("Document Review Protocol", text);
        Assert.Contains(CaseFolderNaming.CaseTitle(draft), text);
        Assert.Contains("Privileged & Confidential", text);
        foreach (var issue in guide.Issues)
        {
            Assert.Contains(issue.Id, text);
            Assert.Contains(issue.Title, text);
            Assert.Contains(issue.RelevanceCriteria, text);
        }
        foreach (var counsel in guide.Cast.Where(c => c.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel))
            Assert.Contains(counsel.FullName, text);
        foreach (var firm in guide.Organizations.Where(o => o.Kind == OrganizationKind.LawFirm))
            Assert.Contains(firm.Name, text);
        foreach (var field in new[] { "Family", "Relevance", "Issue Codes", "Hot", "Privileged" })
            Assert.Contains(field, text);
        Assert.Contains("An email approving Change Order 7.", text);
        Assert.Contains("A holiday schedule.", text);

        Assert.DoesNotMatch(new Regex(@"[A-Z]{2,6}-\d{6}"), text);
        Assert.DoesNotContain("Near-Miss", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Non-Relevant Category", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decoy", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithFallbackProse_Validates()
    {
        var draft = CorpusTestData.RichDraft();

        var (errors, text) = OpenAndValidate(ReviewProtocolBuilder.Build(draft, ReviewProtocolBuilder.FallbackProse(draft)));

        Assert.Empty(errors.Select(e => e.Description + " @ " + e.Path?.XPath));
        Assert.Contains("5. Issue Codes", text);
    }

    [Fact]
    public void FileNameFor_AppendsReviewProtocol()
    {
        Assert.Equal("Brightline v. Harmon - Review Protocol.docx", ReviewProtocolBuilder.FileNameFor("Brightline v. Harmon"));
    }

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
