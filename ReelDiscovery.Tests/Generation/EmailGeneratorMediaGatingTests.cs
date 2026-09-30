using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Generation.EmailGeneratorTestSetup;

namespace ReelDiscovery.Tests.Generation;

public class EmailGeneratorMediaGatingTests
{
    [Fact]
    public async Task NullMedia_DoesNotPlanOrGenerateMedia()
    {
        var fake = new FakeLlmProvider();
        fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson("Warehouse photos", withMedia: true)));

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 1);
            state.Config.IncludeImages = true;
            state.Config.ImagePercentage = 50;
            state.Config.IncludeVoicemails = true;
            state.Config.VoicemailPercentage = 50;

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.Empty(result.Errors);
            Assert.Equal(0, result.ImagesGenerated);
            Assert.Equal(0, result.VoicemailsGenerated);
            Assert.All(state.GeneratedThreads.SelectMany(t => t.Messages).SelectMany(m => m.Attachments), a =>
            {
                Assert.NotEqual(AttachmentType.Image, a.Type);
                Assert.NotEqual(AttachmentType.Voicemail, a.Type);
            });

            // Only the thread batch was requested: no voicemail-script call either
            var request = Assert.Single(fake.Requests);
            Assert.DoesNotContain("IMAGE ATTACHMENTS - MANDATORY", request.UserPrompt);
            Assert.DoesNotContain("VOICEMAIL ATTACHMENTS:", request.UserPrompt);
            Assert.Contains("Do NOT include any attachments in this batch", request.UserPrompt);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }
}
