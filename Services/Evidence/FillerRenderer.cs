using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// The per-thread non-relevant call (NOISE-01..05): the shared-skeleton prompt from <see cref="FillerPrompts"/>, a
/// ledger-free strict DTO (D-13) and mapping through the same <see cref="RenderRepair"/> as relevant mail, plus one
/// short title per planned attachment (Pitfall 1). Tier is chosen by the caller: Volume for filler, Story for decoys.
/// </summary>
public static partial class FillerRenderer
{
    public const int MaxTitleChars = 120;

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(FillerThreadDto) };

    [GeneratedRegex(@"[\r\n\t]+")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"\p{Cc}")]
    private static partial Regex ControlChars();

    /// <summary>
    /// The filler request for one attempt; attempts after the first carry the previous attempt's violations at the end of
    /// the user prompt (D-13) and say so in the operation name.
    /// </summary>
    internal static LlmRequest BuildRequest(ThreadRenderContext ctx, SideWorld? sideWorld, LlmTier tier, int attempt,
        IReadOnlyList<RenderViolation>? feedback) => new()
    {
        Tier = tier,
        OperationName = $"Generate Corpus - {ctx.Thread.Id}" + (attempt > 1 ? $" attempt {attempt}" : string.Empty),
        SystemPrompt = FillerPrompts.SystemPrompt,
        UserPrompt = FillerPrompts.BuildThreadPrompt(ctx, sideWorld, feedback),
        MaxOutputTokens = ThreadRenderer.MaxOutputTokens,
        // D-07/D-10: only a unit's first attempt may wait in a provider batch; feedback retries run live
        AllowBatch = attempt == 1
    };

    /// <summary>
    /// Maps untrusted filler output to a RenderedThread through <see cref="RenderRepair.Apply"/> (no ledger deltas). Each
    /// message gets one title per planned attachment: control characters stripped, at most <see cref="MaxTitleChars"/>,
    /// cut to the planned count; a missing or blank title takes the code title so later titles never shift (T-06-47).
    /// </summary>
    internal static RenderedThread ToRendered(FillerThreadDto? dto, ThreadRenderContext ctx)
    {
        if (dto == null)
            return RenderRepair.Apply(null, ctx);

        // First entry per seq wins, matching RenderRepair
        var bySeq = new Dictionary<int, FillerMessageDto>();
        var render = new ThreadRenderer.ThreadRenderDto();
        foreach (var message in dto.Messages ?? new List<FillerMessageDto>())
        {
            if (message == null)
            {
                render.Messages.Add(null!);
                continue;
            }

            bySeq.TryAdd(message.Seq, message);
            render.Messages.Add(new ThreadRenderer.RenderedMessageDto
            {
                Seq = message.Seq,
                Body = message.Body ?? string.Empty,
                SignOff = message.SignOff ?? string.Empty,
                PeopleNamed = new List<string>()
            });
        }

        var result = RenderRepair.Apply(render, ctx);
        result.NewDetails = new List<LedgerDelta>();

        var planned = (ctx.Thread?.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null)
            .GroupBy(m => m.Seq)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var message in result.Messages)
        {
            if (!planned.TryGetValue(message.Seq, out var parent))
                continue;

            var specs = (parent.Attachments ?? new List<PlannedAttachment>()).ToList();
            var raw = bySeq.TryGetValue(message.Seq, out var source) ? source.AttachmentTitles ?? new List<string>() : new List<string>();
            message.AttachmentTitles = specs
                .Select((spec, i) =>
                {
                    var title = i < raw.Count ? CleanTitle(raw[i]) : string.Empty;
                    return title.Length > 0 || spec == null ? title : CleanTitle(AttachmentNaming.TitleFor(spec, parent, null));
                })
                .ToList();
        }

        return result;
    }

    // Line breaks and tabs become one space, other control characters are removed, then trim and cut
    internal static string CleanTitle(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var text = ControlChars().Replace(LineBreaks().Replace(value, " "), string.Empty).Trim();
        if (text.Length <= MaxTitleChars)
            return text;
        // Never end on a high surrogate: a lone surrogate makes the OpenXml writer throw (IN-01)
        var cut = char.IsHighSurrogate(text[MaxTitleChars - 1]) ? MaxTitleChars - 1 : MaxTitleChars;
        return text[..cut].TrimEnd();
    }

    // ----- strict-schema DTOs (D-01): no nullable members, no recursion -----

    // D-13: no newDetails, filler can never write the ledger
    internal class FillerThreadDto
    {
        [JsonPropertyName("messages")]
        [Description("One entry per message in the request, by seq, in order")]
        public List<FillerMessageDto> Messages { get; set; } = new();
    }

    internal class FillerMessageDto
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

        [JsonPropertyName("attachmentTitles")]
        [Description("One short, realistic title per attachment listed for this message, in order; empty when none")]
        public List<string> AttachmentTitles { get; set; } = new();
    }
}
