using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Generation;

/// <summary>
/// Shared fake-provider / WizardState setup for end-to-end EmailGenerator tests.
/// </summary>
internal static class EmailGeneratorTestSetup
{
    public static AIModelConfig StoryModel() => new()
    {
        ModelId = "gpt-story",
        DisplayName = "Story",
        Provider = LlmProviderKind.OpenAI,
        MaxOutputTokens = 64_000
    };

    public static AIModelConfig VolumeModel() => new()
    {
        ModelId = "gpt-volume",
        DisplayName = "Volume",
        Provider = LlmProviderKind.OpenAI,
        MaxOutputTokens = 64_000
    };

    public static LlmGateway Gateway(FakeLlmProvider fake) => new(fake, StoryModel(), VolumeModel());

    public static string NewOutputFolder() =>
        Path.Combine(Path.GetTempPath(), "ReelDiscoveryTests", Guid.NewGuid().ToString("N"));

    public static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    public static int CountEml(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.eml", SearchOption.AllDirectories).Length : 0;

    /// <summary>
    /// State with the given number of storylines, 3 emails per storyline, one parallel slot
    /// and all attachments disabled.
    /// </summary>
    public static WizardState State(string outputFolder, int storylineCount)
    {
        var state = new WizardState
        {
            Topic = "The Office",
            CompanyDomain = "example.com",
            Storylines = Enumerable.Range(1, storylineCount)
                .Select(i => new Storyline { Title = $"Storyline {i}", Description = $"Plot {i}." })
                .ToList(),
            Characters = new List<Character>
            {
                new() { FirstName = "Jim", LastName = "Halpert", Email = "jim@example.com", SignatureBlock = "Jim" },
                new() { FirstName = "Pam", LastName = "Beesly", Email = "pam@example.com", SignatureBlock = "Pam" }
            }
        };
        state.Config.TotalEmailCount = 3 * storylineCount;
        state.Config.AttachmentPercentage = 0;
        state.Config.IncludeImages = false;
        state.Config.IncludeVoicemails = false;
        state.Config.IncludeCalendarInvites = false;
        state.Config.ParallelThreads = 1;
        state.Config.LetAISuggestDates = false;
        state.Config.StartDate = new DateTime(2026, 3, 1);
        state.Config.EndDate = new DateTime(2026, 3, 31);
        state.Config.OutputFolder = outputFolder;
        return state;
    }

    /// <summary>
    /// Three-email thread JSON; media flags are configurable for media-gating tests.
    /// </summary>
    public static string ThreadJson(string subject, bool withMedia = false)
    {
        var media = withMedia
            ? "\"hasImage\": true, \"imageDescription\": \"A photo of the warehouse\", \"isImageInline\": true, \"hasVoicemail\": true, \"voicemailContext\": \"Urgent call about the numbers\""
            : "\"hasImage\": false, \"imageDescription\": \"\", \"isImageInline\": false, \"hasVoicemail\": false, \"voicemailContext\": \"\"";

        string Email(string from, string to, string time, string body, bool isReply, int replyTo) =>
            $"{{ \"fromEmail\": \"{from}\", \"toEmails\": [\"{to}\"], \"ccEmails\": [], \"sentDateTime\": \"2026-03-02T{time}\", \"bodyPlain\": \"{body}\", \"isReply\": {(isReply ? "true" : "false")}, \"isForward\": false, \"replyToIndex\": {replyTo}, \"hasDocument\": false, \"documentType\": \"\", \"documentDescription\": \"\", {media} }}";

        return $"{{ \"subject\": \"{subject}\", \"emails\": [ " +
               Email("jim@example.com", "pam@example.com", "09:00:00", "Pam, the numbers are in.\\n\\nJim", false, -1) + ", " +
               Email("pam@example.com", "jim@example.com", "10:00:00", "Great, thanks.\\n\\nPam", true, 0) + ", " +
               Email("jim@example.com", "pam@example.com", "11:00:00", "Lunch?\\n\\nJim", true, 1) +
               " ] }";
    }

    public sealed class SyncProgress<T> : IProgress<T>
    {
        public void Report(T value)
        {
            // Synchronous no-op: avoids posting to a SynchronizationContext in tests
        }
    }
}
