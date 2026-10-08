using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Discovery;

public class DiscoveryDefinition
{
    public string Term { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class DiscoveryRequest
{
    public int Number { get; set; }

    // Code-owned lead-in, e.g. "Admit that on March 3, 2024,"
    public string Prefix { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public List<string> IssueIds { get; set; } = new();
    public string FactId { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;
    public bool IsGenuineness { get; set; }
}

/// <summary>
/// Requests for production, interrogatories or requests for admission; caption, boilerplate and signature text are
/// stored so Save Again can rebuild the .docx without a model call (UI-SPEC S1).
/// </summary>
public class WrittenDiscoveryContent
{
    public DiscoveryKind Kind { get; set; }
    public DiscoveryVariant Variant { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CaseNumber { get; set; } = string.Empty;
    public string CaptionText { get; set; } = string.Empty;
    public string PreliminaryStatement { get; set; } = string.Empty;
    public List<DiscoveryDefinition> Definitions { get; set; } = new();
    public List<string> Instructions { get; set; } = new();

    // e.g. "REQUEST FOR PRODUCTION NO."
    public string RequestLabel { get; set; } = string.Empty;

    public List<DiscoveryRequest> Requests { get; set; } = new();
    public string SignatureText { get; set; } = string.Empty;
    public string CertificateText { get; set; } = string.Empty;
    public string ServiceDate { get; set; } = string.Empty;
    public string PropoundingPartyId { get; set; } = string.Empty;
    public string RespondingPartyId { get; set; } = string.Empty;

    /// <summary>
    /// Repairs content loaded from the untrusted sidecar; never throws.
    /// </summary>
    public void Normalize()
    {
        Title ??= string.Empty;
        CaseNumber ??= string.Empty;
        CaptionText ??= string.Empty;
        PreliminaryStatement ??= string.Empty;
        RequestLabel ??= string.Empty;
        SignatureText ??= string.Empty;
        CertificateText ??= string.Empty;
        ServiceDate ??= string.Empty;
        PropoundingPartyId ??= string.Empty;
        RespondingPartyId ??= string.Empty;
        Instructions = CaseGuide.CleanStrings(Instructions);

        Definitions = CaseGuide.CleanList(Definitions);
        foreach (var definition in Definitions)
        {
            definition.Term ??= string.Empty;
            definition.Text ??= string.Empty;
        }

        Requests = CaseGuide.CleanList(Requests);
        foreach (var request in Requests)
        {
            request.Number = Math.Max(0, request.Number);
            request.Prefix ??= string.Empty;
            request.Text ??= string.Empty;
            request.IssueIds = CaseGuide.CleanStrings(request.IssueIds);
            request.FactId ??= string.Empty;
            request.DocId ??= string.Empty;
        }
    }
}
