using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services;

public class StorylineGenerator
{
    private readonly ILlmGateway _llm;
    private const int MaxStorylinesPerBatch = 15;

    /// <summary>
    /// Top-level response types sent as strict JSON schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(StorylineApiResponse) };

    public StorylineGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    public async Task<StorylineGenerationResult> GenerateStorylinesAsync(
        string topic,
        string additionalInstructions,
        int storylineCount = 10,
        bool wantsDocuments = true,
        bool wantsImages = false,
        bool wantsVoicemails = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var mediaHints = BuildMediaHints(wantsDocuments, wantsImages, wantsVoicemails);

        // For large storyline counts, generate in batches to avoid API output limits
        if (storylineCount > MaxStorylinesPerBatch)
        {
            return await GenerateStorylinesInBatchesAsync(topic, additionalInstructions, storylineCount, mediaHints, progress, ct);
        }

        progress?.Report("Generating storylines...");
        return await GenerateSingleBatchAsync(topic, additionalInstructions, storylineCount, null, true, mediaHints, ct);
    }

    private static string BuildMediaHints(bool wantsDocuments, bool wantsImages, bool wantsVoicemails)
    {
        var hints = new List<string>();

        if (wantsDocuments)
        {
            hints.Add("DOCUMENTS: Create storylines where characters would naturally share reports, proposals, spreadsheets, or presentations (e.g., 'Attached is the quarterly report', 'See the attached budget breakdown')");
        }

        if (wantsImages)
        {
            hints.Add("IMAGES: Include storylines with visual moments where characters would share photos or images (e.g., 'Here's a photo from the ceremony', 'Look at this - I captured the moment', 'Attached: evidence from the scene')");
        }

        if (wantsVoicemails)
        {
            hints.Add("VOICEMAILS: Create scenarios where characters might leave urgent voice messages (e.g., important calls, time-sensitive situations, emotional moments that warrant a personal voice message)");
        }

        if (hints.Count == 0)
        {
            return "";
        }

        return "\n\nMEDIA OPPORTUNITIES - Design storylines that naturally include:\n" + string.Join("\n", hints.Select(h => $"• {h}"));
    }

    private async Task<StorylineGenerationResult> GenerateStorylinesInBatchesAsync(
        string topic,
        string additionalInstructions,
        int totalCount,
        string mediaHints,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var result = new StorylineGenerationResult();
        var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batchCount = (int)Math.Ceiling((double)totalCount / MaxStorylinesPerBatch);
        var remaining = totalCount;

        for (int batch = 0; batch < batchCount; batch++)
        {
            ct.ThrowIfCancellationRequested();

            var batchSize = Math.Min(MaxStorylinesPerBatch, remaining);
            var isFirstBatch = batch == 0;

            // Report progress
            progress?.Report($"Generating storylines (batch {batch + 1} of {batchCount})...");

            var batchResult = await GenerateSingleBatchAsync(
                topic,
                additionalInstructions,
                batchSize,
                existingTitles,
                isFirstBatch,
                mediaHints,
                ct);

            // First batch sets date range info
            if (isFirstBatch)
            {
                result.SuggestedStartDate = batchResult.SuggestedStartDate;
                result.SuggestedEndDate = batchResult.SuggestedEndDate;
                result.DateRangeReasoning = batchResult.DateRangeReasoning;
            }

            // Add storylines and track titles to avoid duplicates
            foreach (var storyline in batchResult.Storylines)
            {
                result.Storylines.Add(storyline);
                existingTitles.Add(storyline.Title);
            }

            // Report completion of batch
            progress?.Report($"Generated {result.Storylines.Count} of {totalCount} storylines...");

            remaining -= batchSize;
        }

        return result;
    }

    private async Task<StorylineGenerationResult> GenerateSingleBatchAsync(
        string topic,
        string additionalInstructions,
        int storylineCount,
        HashSet<string>? existingTitles,
        bool includeDateRange,
        string mediaHints,
        CancellationToken ct)
    {
        var systemPrompt = @"You are a creative writer generating email storylines set WITHIN the fictional universe of the source material.

You will be given a topic (movie, TV show, book, or other subject) and must create storylines that:
1. Stay TRUE to the original fictional universe - if it's Game of Thrones, the emails are between characters IN Westeros discussing dragons, politics, battles, and intrigue
2. Reference ACTUAL events, plot points, characters, and conflicts from the source material
3. Capture the authentic tone, drama, and stakes of the original work
4. Feel like communications that would actually happen within that fictional world

For example:
- Game of Thrones: Ravens/messages between houses about alliances, betrayals, battles, and the threat beyond the Wall
- Star Wars: Imperial memos, Rebel communications, Jedi Council discussions
- The Office: Actual Dunder Mifflin emails about paper sales, office drama, and Michael's management disasters
- Breaking Bad: Texts and emails about the meth operation, DEA concerns, and cartel dealings

The emails should feel AUTHENTIC to the source material's world, not like a corporate translation of it.

EMOTIONAL INTENSITY IS CRITICAL:
Every storyline MUST have real emotional stakes and interpersonal conflict:
- What's the CONFLICT? Who is fighting, disagreeing, or clashing?
- What EMOTIONS drive the exchanges? (anger, fear, betrayal, desperation, jealousy, grief, contempt)
- Are there VILLAINS, antagonists, or people being blamed?
- Will tempers flare? Will characters say things they shouldn't?
- Are there SIDES being taken? People turning against each other?

The description field MUST specify:
1. The core conflict/tension
2. Which characters are in opposition
3. The emotional tone (heated, tense, bitter, desperate, accusatory, etc.)

DO NOT create bland, cordial storylines:
- BAD: 'Planning the company picnic' (boring, no conflict)
- GOOD: 'Company picnic disaster - food poisoning blamed on Angela, accusations of deliberate sabotage, HR gets involved'
- BAD: 'Quarterly budget review' (routine, no stakes)
- GOOD: 'Quarterly budget exposes missing funds - accusations fly between departments, someone's getting fired'";

        var existingTitlesNote = existingTitles != null && existingTitles.Count > 0
            ? $"\n\nIMPORTANT: The following storylines have already been created. DO NOT duplicate these - create DIFFERENT storylines based on OTHER events from the source material:\n{string.Join("\n", existingTitles.Select(t => $"- {t}"))}\n"
            : "";

        var dateRangeInstruction = includeDateRange
            ? @"

Also suggest a realistic date range (startDate, endDate as yyyy-MM-dd) with reasoning for when these emails would have been sent, based on:
- If this is a movie/TV show/book, use dates that fit the setting or release period
- If this is a general topic, suggest dates within the past 1-2 years"
            : @"

Leave the suggested date range empty.";

        var userPrompt = $@"Topic: {topic}

Additional Instructions: {(string.IsNullOrWhiteSpace(additionalInstructions) ? "None" : additionalInstructions)}
{existingTitlesNote}{mediaHints}

Generate exactly {storylineCount} storylines set WITHIN the fictional universe of ""{topic}"", based on ACTUAL EVENTS and PLOT POINTS from the source material.

IMPORTANT: Each storyline must:
1. Be DIRECTLY BASED ON a specific event, plot point, or story arc from the source material
2. Stay IN-UNIVERSE - don't translate to a corporate setting, keep the original fictional context
3. Capture the authentic drama, stakes, and tone of the original work
4. Use titles that reference the actual events (e.g., for Game of Thrones: ""The Red Wedding Aftermath"", ""Wildfire Explosion at the Sept"", ""Dragons Missing from Dragonpit"")

The storylines should feel like they're happening INSIDE the fictional world:
- Game of Thrones: Political intrigue, battle planning, betrayals, supernatural threats
- Star Wars: Rebellion operations, Imperial orders, Jedi matters, trade negotiations
- Breaking Bad: Drug operation logistics, DEA investigations, cartel dealings
- The Office: Actual paper company dysfunction, Michael's disasters, office romance drama

Each storyline should:
1. Naturally generate email/message threads that characters in that world would send
2. Have a clear beginning, middle, and potential resolution
3. Involve 2-6 characters FROM the source material
4. Span a realistic timeframe for that story's events{dateRangeInstruction}

CRITICAL - MAKE STORYLINES EMOTIONALLY CHARGED:
- Every storyline needs CONFLICT and TENSION between characters
- The description MUST name who is in conflict with whom
- Include the EMOTIONAL STAKES (fear, anger, betrayal, desperation, jealousy)
- Characters who are enemies/rivals should CLASH in email threads
- Don't make everyone professional and cordial - let real emotions show";

        var llmResult = await _llm.GenerateAsync<StorylineApiResponse>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Storyline Generation",
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxOutputTokens = 32_000
        }, ct);
        var response = llmResult.GetValueOrThrow();

        var result = new StorylineGenerationResult();

        foreach (var s in response.Storylines)
        {
            result.Storylines.Add(new Storyline
            {
                Title = s.Title,
                Description = s.Description,
                TimelineHint = s.TimelineHint,
                SuggestedEmailCount = Math.Clamp(s.SuggestedEmailCount, 5, 20)
            });
        }

        if (!string.IsNullOrWhiteSpace(response.SuggestedDateRange.StartDate))
        {
            if (DateTime.TryParse(response.SuggestedDateRange.StartDate, out var start))
                result.SuggestedStartDate = start;
            if (DateTime.TryParse(response.SuggestedDateRange.EndDate, out var end))
                result.SuggestedEndDate = end;
            result.DateRangeReasoning = response.SuggestedDateRange.Reasoning;
        }

        return result;
    }

    // API Response DTOs
    internal class StorylineApiResponse
    {
        [JsonPropertyName("storylines")]
        [Description("The generated storylines")]
        public List<StorylineDto> Storylines { get; set; } = new();

        [JsonPropertyName("suggestedDateRange")]
        [Description("Suggested date range for the emails. Leave startDate and endDate as empty strings if a date range was not requested.")]
        public DateRangeDto SuggestedDateRange { get; set; } = new();
    }

    internal class StorylineDto
    {
        [JsonPropertyName("title")]
        [Description("Storyline title referencing the actual event from the source material")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("2-3 sentences: the conflict, who's against whom, and the emotional stakes")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("timelineHint")]
        [Description("Timeline hint, e.g. 'Spans 2 weeks', 'Single day event'")]
        public string TimelineHint { get; set; } = string.Empty;

        [JsonPropertyName("suggestedEmailCount")]
        [Description("Suggested number of emails, 5-20")]
        public int SuggestedEmailCount { get; set; }

        [JsonPropertyName("keyCharacterRoles")]
        [Description("Roles of the key characters involved")]
        public List<string> KeyCharacterRoles { get; set; } = new();
    }

    internal class DateRangeDto
    {
        [JsonPropertyName("reasoning")]
        [Description("Explanation of why these dates were chosen")]
        public string Reasoning { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        [Description("Start date as YYYY-MM-DD, or empty string")]
        public string StartDate { get; set; } = string.Empty;

        [JsonPropertyName("endDate")]
        [Description("End date as YYYY-MM-DD, or empty string")]
        public string EndDate { get; set; } = string.Empty;
    }
}

public class StorylineGenerationResult
{
    public List<Storyline> Storylines { get; set; } = new();
    public DateTime? SuggestedStartDate { get; set; }
    public DateTime? SuggestedEndDate { get; set; }
    public string? DateRangeReasoning { get; set; }
}
