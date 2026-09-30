using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Generation;

public class EmailGeneratorPortTests
{
    public static TheoryData<Type> AllSchemaTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in EmailGenerator.SchemaTypes)
            data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllSchemaTypes))]
    public void AllSchemaTypes_AreStrict(Type type)
    {
        var schema = JsonSchemaRegistry.GetSchemaJson(type);

        var errors = StrictSchemaValidator.Validate(schema);

        Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
        Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16, $"{type.Name} exceeds 16 union types");
    }

    [Fact]
    public void SchemaTypes_ListsEveryResponseDto()
    {
        var names = EmailGenerator.SchemaTypes.Select(t => t.Name).ToList();

        Assert.Equal(6, names.Count);
        Assert.Contains("ThreadApiResponse", names);
        Assert.Contains("VoicemailScriptResponse", names);
        Assert.Contains("WordDocResponse", names);
        Assert.Contains("ExcelDocResponse", names);
        Assert.Contains("PowerPointDocResponse", names);
        Assert.Contains("MeetingDetectionResponse", names);
    }

    private static AIModelConfig StoryModel() => new()
    {
        ModelId = "gpt-story",
        DisplayName = "Story",
        Provider = LlmProviderKind.OpenAI,
        MaxOutputTokens = 64_000
    };

    private static AIModelConfig VolumeModel() => new()
    {
        ModelId = "gpt-volume",
        DisplayName = "Volume",
        Provider = LlmProviderKind.OpenAI,
        MaxOutputTokens = 64_000
    };

    private sealed class SyncProgress<T> : IProgress<T>
    {
        public void Report(T value)
        {
            // Synchronous no-op: avoids posting to a SynchronizationContext in tests
        }
    }

    private const string ThreadJson = """
        {
          "subject": "Quarterly numbers",
          "emails": [
            { "fromEmail": "jim@example.com", "toEmails": ["pam@example.com"], "ccEmails": [], "sentDateTime": "2026-03-02T09:00:00", "bodyPlain": "Pam, the numbers are in.\n\nJim", "isReply": false, "isForward": false, "replyToIndex": -1, "hasDocument": false, "documentType": "", "documentDescription": "", "hasImage": false, "imageDescription": "", "isImageInline": false, "hasVoicemail": false, "voicemailContext": "" },
            { "fromEmail": "pam@example.com", "toEmails": ["jim@example.com"], "ccEmails": [], "sentDateTime": "2026-03-02T10:00:00", "bodyPlain": "Great, thanks.\n\nPam", "isReply": true, "isForward": false, "replyToIndex": 0, "hasDocument": false, "documentType": "", "documentDescription": "", "hasImage": false, "imageDescription": "", "isImageInline": false, "hasVoicemail": false, "voicemailContext": "" },
            { "fromEmail": "jim@example.com", "toEmails": ["pam@example.com"], "ccEmails": [], "sentDateTime": "2026-03-02T11:00:00", "bodyPlain": "Lunch?\n\nJim", "isReply": true, "isForward": false, "replyToIndex": 1, "hasDocument": false, "documentType": "", "documentDescription": "", "hasImage": false, "imageDescription": "", "isImageInline": false, "hasVoicemail": false, "voicemailContext": "" }
          ]
        }
        """;

    [Fact]
    public async Task EndToEnd_FakeProvider_WritesEml()
    {
        var fake = new FakeLlmProvider(LlmProviderKind.OpenAI);
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson));
        var gateway = new LlmGateway(fake, StoryModel(), VolumeModel());

        var outputFolder = Path.Combine(Path.GetTempPath(), "ReelDiscoveryTests", Guid.NewGuid().ToString("N"));
        try
        {
            var state = new WizardState
            {
                Topic = "The Office",
                CompanyDomain = "example.com",
                Storylines = new List<Storyline> { new() { Title = "Quarterly numbers", Description = "Sales numbers are due." } },
                Characters = new List<Character>
                {
                    new() { FirstName = "Jim", LastName = "Halpert", Email = "jim@example.com", SignatureBlock = "Jim" },
                    new() { FirstName = "Pam", LastName = "Beesly", Email = "pam@example.com", SignatureBlock = "Pam" }
                }
            };
            state.Config.TotalEmailCount = 3;
            state.Config.AttachmentPercentage = 0;
            state.Config.IncludeImages = false;
            state.Config.IncludeVoicemails = false;
            state.Config.IncludeCalendarInvites = false;
            state.Config.ParallelThreads = 1;
            state.Config.LetAISuggestDates = false;
            state.Config.StartDate = new DateTime(2026, 3, 1);
            state.Config.EndDate = new DateTime(2026, 3, 31);
            state.Config.OutputFolder = outputFolder;

            var result = await new EmailGenerator(gateway, null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.Empty(result.Errors);
            Assert.False(result.WasCancelled);
            Assert.Equal(3, result.TotalEmailsGenerated);
            Assert.Equal(3, Directory.GetFiles(outputFolder, "*.eml", SearchOption.AllDirectories).Length);

            Assert.NotEmpty(fake.Requests);
            Assert.All(fake.Requests, r =>
            {
                Assert.Equal("gpt-volume", r.ModelId);
                Assert.DoesNotContain("Respond with JSON", r.SystemPrompt + r.UserPrompt);
                Assert.DoesNotContain("valid JSON", r.SystemPrompt + r.UserPrompt);
            });
        }
        finally
        {
            if (Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Fact]
    public void Constructor_AcceptsNullMedia()
    {
        var gateway = new LlmGateway(new FakeLlmProvider(LlmProviderKind.OpenAI), StoryModel(), VolumeModel());

        var generator = new EmailGenerator(gateway, null);

        Assert.NotNull(generator);
    }
}
