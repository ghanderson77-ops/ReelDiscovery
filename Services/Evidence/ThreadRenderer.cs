using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// The per-thread render call: one Story-tier strict-schema request returns the thread's messages and its ledger delta
/// together (D-07, D-19). Prose only: dates, people, order, labels and attachments stay code-owned (D-02).
/// </summary>
public static class ThreadRenderer
{
    // Worst case 12 messages is about 6-7K output tokens; the gateway doubles once on truncation (RESEARCH Pattern 4)
    public const int MaxOutputTokens = 12_000;

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(ThreadRenderDto) };

    /// <summary>
    /// The render request for one attempt; attempts after the first carry the previous attempt's violations at the end of
    /// the user prompt (D-13) and say so in the operation name.
    /// </summary>
    internal static LlmRequest BuildRequest(ThreadRenderContext ctx, int attempt, IReadOnlyList<RenderViolation>? feedback) => new()
    {
        Tier = LlmTier.Story,
        OperationName = $"Generate Corpus - {ctx.Thread.Id}" + (attempt > 1 ? $" attempt {attempt}" : string.Empty),
        SystemPrompt = EvidencePrompts.SystemPrompt,
        UserPrompt = EvidencePrompts.BuildThreadPrompt(ctx, feedback),
        MaxOutputTokens = MaxOutputTokens
    };

    // ----- strict-schema DTOs: no nullable members, no recursion, no dates/people/labels decided by the model (D-01, D-02) -----

    internal class ThreadRenderDto
    {
        [JsonPropertyName("messages")]
        [Description("One entry per message in the request, by seq, in order")]
        public List<RenderedMessageDto> Messages { get; set; } = new();

        [JsonPropertyName("newDetails")]
        [Description("New specific details this thread introduces that later emails may refer back to; do not repeat earlier details")]
        public List<LedgerDeltaDto> NewDetails { get; set; } = new();
    }

    internal class RenderedMessageDto
    {
        [JsonPropertyName("seq")]
        [Description("Message number from the request")]
        public int Seq { get; set; }

        [JsonPropertyName("body")]
        [Description("Only the new text this sender writes: no signature, no name under the sign-off, no quoted earlier messages")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("signOff")]
        [Description("Closing word or phrase before the signature, for example 'Thanks,', or empty")]
        public string SignOff { get; set; } = string.Empty;

        [JsonPropertyName("peopleNamed")]
        [Description("Full or first names of every person this message mentions")]
        public List<string> PeopleNamed { get; set; } = new();
    }

    internal class LedgerDeltaDto
    {
        [JsonPropertyName("seq")]
        [Description("Message number that introduced the detail")]
        public int Seq { get; set; }

        [JsonPropertyName("kind")]
        [Description("Commitment, Figure, Meeting, Decision, Person, Document or Other")]
        public LedgerKind Kind { get; set; }

        [JsonPropertyName("about")]
        [Description("Short name of what the detail is about, for example 'retainage on pay application 7'")]
        public string About { get; set; } = string.Empty;

        [JsonPropertyName("statement")]
        [Description("One sentence stating the detail")]
        public string Statement { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        [Description("Exact amount, date (yyyy-MM-dd), number or name, or empty")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("factIds")]
        [Description("Fact ids from the request this detail relates to, or empty")]
        public List<string> FactIds { get; set; } = new();
    }
}
