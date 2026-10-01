using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Builds the case guide through the Story tier (D-03): issues extracted in code and enriched by one call (GUIDE-01),
/// then the cast (GUIDE-02..04). Code owns every id, email address, phone and signature.
/// </summary>
public class CaseGuideGenerator
{
    public const int IssueDetailsMaxOutputTokens = 8_000;
    public const int RosterMaxOutputTokens = 12_000;
    public const int CastDetailsMaxOutputTokens = 12_000;
    public const int BackgroundMaxOutputTokens = 8_000;
    public const int MaxParallelCalls = 3;
    public const int MaxDetailsPerCall = 10;

    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[]
    {
        typeof(IssueDetailsDto), typeof(CastRosterDto), typeof(CastDetailsDto), typeof(BackgroundStaffDto)
    };

    public CaseGuideGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    // ---------- Issues ----------

    /// <summary>
    /// Extracts the issues from the counts/findings (always succeeds), then writes the missing issue details.
    /// A failed enrichment marks only IssueDetailsState; a fatal failure is reported on the result. Cancellation propagates.
    /// </summary>
    public async Task<GuidePartResult> GenerateIssuesAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Write the core document before building the case guide.");
        var guide = draft.Guide ??= new CaseGuide();
        var result = new GuidePartResult { Part = GuidePart.Issues };

        progress?.Report(new GuideProgress
        {
            Part = GuidePart.Issues,
            Message = doc.DocumentType == CoreDocumentType.InvestigationMemo
                ? "Extracting the issues from the findings..."
                : "Extracting the issues from the counts..."
        });

        guide.Issues = IssueExtractor.Extract(doc, guide);
        guide.IssuesState.Status = GuidePartStatus.Ready;
        guide.IssuesState.FailureMessage = string.Empty;
        GuideStaleness.Stamp(draft, GuidePart.Issues);
        draft.Revision++;
        result.Succeeded = true;

        await EnrichIssuesAsync(draft, result, progress, ct);
        return result;
    }

    /// <summary>
    /// Writes details only for issues that have none (Retry Issue Details).
    /// </summary>
    public async Task<GuidePartResult> RetryIssueDetailsAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.CoreDocument == null || draft.Guide == null)
            throw new InvalidOperationException("Extract the issues before writing their details.");

        var result = new GuidePartResult { Part = GuidePart.Issues };
        await EnrichIssuesAsync(draft, result, progress, ct);
        result.Succeeded = draft.Guide.IssueDetailsState.Status == GuidePartStatus.Ready;
        return result;
    }

    private async Task EnrichIssuesAsync(CaseDraft draft, GuidePartResult result, IProgress<GuideProgress>? progress, CancellationToken ct)
    {
        var guide = draft.Guide!;
        var state = guide.IssueDetailsState;
        var pending = guide.Issues.Where(i => !i.HasDetails).ToList();
        if (pending.Count == 0)
        {
            SetReady(state);
            return;
        }

        state.Status = GuidePartStatus.Generating;
        progress?.Report(new GuideProgress { Part = GuidePart.Issues, Message = "Writing the issue details...", Total = pending.Count });

        try
        {
            var dto = (await _llm.GenerateAsync<IssueDetailsDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = "Case Guide - Issue details",
                SystemPrompt = CaseGuidePrompts.SystemPrompt,
                UserPrompt = CaseGuidePrompts.BuildIssueDetailsPrompt(draft, pending),
                MaxOutputTokens = IssueDetailsMaxOutputTokens
            }, ct)).GetValueOrThrow();

            var written = 0;
            foreach (var detail in dto.Issues ?? new List<IssueDetailDto>())
            {
                if (detail == null)
                    continue;

                // Only issues listed in the request are accepted (T-03-30)
                var id = IdAllocator.Normalize(detail.IssueId, IdAllocator.Issue);
                var issue = id == null ? null : pending.FirstOrDefault(i => i.Id == id);
                if (issue == null)
                {
                    result.Notes.Add($"Ignored details for unknown issue {Clean(detail.IssueId)}.");
                    continue;
                }

                issue.Description = Clean(detail.Description);
                issue.Elements = (detail.Elements ?? new List<string>()).Select(Clean).Where(e => e.Length > 0).ToList();
                issue.RelevanceCriteria = Clean(detail.RelevanceCriteria);
                written++;
            }

            if (written > 0)
                draft.Revision++;

            var missing = guide.Issues.Count(i => !i.HasDetails);
            if (missing == 0)
                SetReady(state);
            else
                SetFailed(state, $"Details for {missing} issues were not written. Click Retry Issue Details.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: nothing written
            state.Status = GuidePartStatus.Pending;
            throw;
        }
        catch (OperationCanceledException)
        {
            // Not cancelled by the user: the request timed out
            SetFailed(state, "The request timed out. Click Retry Issue Details.");
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop; the issues themselves are kept
            state.Status = GuidePartStatus.Pending;
            result.FatalError = ex.Result.Message ?? ex.Message;
        }
        catch (LlmCallException ex)
        {
            if (ex.Result.Outcome == LlmOutcome.Refused)
                result.WasRefused = true;
            SetFailed(state, DescribeOutcome(ex.Result, "Issue Details"));
        }
        catch (Exception ex)
        {
            SetFailed(state, ex.Message);
        }

        if (state.Status == GuidePartStatus.Failed)
            result.FailureMessage = state.FailureMessage;
    }

    // ---------- Shared helpers ----------

    private static void SetReady(GuidePartState state)
    {
        state.Status = GuidePartStatus.Ready;
        state.FailureMessage = string.Empty;
        state.GeneratedUtc = DateTime.UtcNow;
    }

    private static void SetFailed(GuidePartState state, string message)
    {
        state.Status = GuidePartStatus.Failed;
        state.FailureMessage = message;
    }

    private static string DescribeOutcome(LlmResult result, string part) => result.Outcome switch
    {
        LlmOutcome.Refused => $"The model declined: {result.Message}",
        LlmOutcome.Truncated => $"The response was cut off. Click Retry {part}.",
        LlmOutcome.Invalid => $"The response could not be read. Click Retry {part}.",
        _ => $"{result.FailureKind}: {result.Message}"
    };

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    // API Response DTOs (strict schemas: no nullable properties, no recursion, dates as YYYY-MM-DD strings)
    internal class IssueDetailsDto
    {
        [JsonPropertyName("issues")]
        [Description("One entry per issue ID listed in the request")]
        public List<IssueDetailDto> Issues { get; set; } = new();
    }

    internal class IssueDetailDto
    {
        [JsonPropertyName("issueId")]
        [Description("Issue ID from the request, e.g. ISS-01")]
        public string IssueId { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("2-3 sentences stating the issue as fact, taking a firm position")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("elements")]
        [Description("3-6 short items that must be proven")]
        public List<string> Elements { get; set; } = new();

        [JsonPropertyName("relevanceCriteria")]
        [Description("2-3 sentences describing what responsive evidence in an email corpus looks like")]
        public string RelevanceCriteria { get; set; } = string.Empty;
    }

    internal class CastRosterDto
    {
        [JsonPropertyName("organizations")]
        [Description("Organizations to add that are not listed in the request, e.g. outside law firms; empty when none")]
        public List<NewOrganizationDto> Organizations { get; set; } = new();

        [JsonPropertyName("people")]
        [Description("Key people in the cast")]
        public List<RosterPersonDto> People { get; set; } = new();
    }

    internal class NewOrganizationDto
    {
        [JsonPropertyName("name")]
        [Description("Full name, e.g. Thistle & Bracken LLP")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shortName")]
        [Description("Short name used in text, e.g. Thistle & Bracken")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        [Description("LawFirm, Agency, ThirdParty or Party")]
        public OrganizationKind Kind { get; set; }

        [JsonPropertyName("representsRefs")]
        [Description("For a law firm: IDs of the organizations it represents, e.g. ORG-01; empty otherwise")]
        public List<string> RepresentsRefs { get; set; } = new();

        [JsonPropertyName("location")]
        [Description("City and state, e.g. Chicago, Illinois")]
        public string Location { get; set; } = string.Empty;
    }

    internal class RosterPersonDto
    {
        [JsonPropertyName("existingId")]
        [Description("ID of an existing character this entry revises, e.g. CH-003; empty string for a new person")]
        public string ExistingId { get; set; } = string.Empty;

        [JsonPropertyName("firstName")]
        [Description("First name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        [Description("Last name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [Description("Job title, e.g. Project Manager, General Counsel, Partner, Associate")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("organizationRef")]
        [Description("Organization ID from the request, e.g. ORG-02, or the exact name of an organization added in this response")]
        public string OrganizationRef { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Description("Party, InHouseCounsel, OutsideCounsel or Staff")]
        public CastRole Role { get; set; }

        [JsonPropertyName("involvement")]
        [Description("One line on this person's involvement in the matter")]
        public string Involvement { get; set; } = string.Empty;
    }

    internal class CastDetailsDto
    {
        [JsonPropertyName("people")]
        [Description("One entry per character ID listed in the request")]
        public List<CastDetailDto> People { get; set; } = new();
    }

    internal class CastDetailDto
    {
        [JsonPropertyName("castId")]
        [Description("Character ID from the request, e.g. CH-004")]
        public string CastId { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        [Description("2-4 sentences on the person's key involvement, stated as fact")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("activeFrom")]
        [Description("First date active in the story, YYYY-MM-DD")]
        public string ActiveFrom { get; set; } = string.Empty;

        [JsonPropertyName("activeTo")]
        [Description("Last date active in the story, YYYY-MM-DD")]
        public string ActiveTo { get; set; } = string.Empty;

        [JsonPropertyName("voiceNote")]
        [Description("1-2 lines on how this person writes email")]
        public string VoiceNote { get; set; } = string.Empty;

        [JsonPropertyName("signatureStyle")]
        [Description("Full, Short, NameOnly or MobileFooter")]
        public SignatureStyle SignatureStyle { get; set; }
    }

    internal class BackgroundStaffDto
    {
        [JsonPropertyName("people")]
        [Description("Background staff for the organization")]
        public List<BackgroundPersonDto> People { get; set; } = new();
    }

    internal class BackgroundPersonDto
    {
        [JsonPropertyName("firstName")]
        [Description("First name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        [Description("Last name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [Description("Job title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("department")]
        [Description("Department, e.g. Accounting")]
        public string Department { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Description("One line on what this person does at the organization")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("activeFrom")]
        [Description("YYYY-MM-DD")]
        public string ActiveFrom { get; set; } = string.Empty;

        [JsonPropertyName("activeTo")]
        [Description("YYYY-MM-DD")]
        public string ActiveTo { get; set; } = string.Empty;
    }
}

public class GuideProgress
{
    public GuidePart Part { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Done { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Outcome of one guide part run. Failures are also recorded on the part's GuidePartState.
/// </summary>
public class GuidePartResult
{
    public GuidePart Part { get; set; }
    public bool Succeeded { get; set; }
    public string FailureMessage { get; set; } = string.Empty;

    /// <summary>
    /// Non-empty when a fatal failure (bad key or spend limit) stopped further calls.
    /// </summary>
    public string FatalError { get; set; } = string.Empty;

    public bool IsFatal => FatalError.Length > 0;
    public bool WasRefused { get; set; }

    /// <summary>
    /// Mapping notes (ignored ids, dropped people, defaulted dates).
    /// </summary>
    public List<string> Notes { get; set; } = new();
}
