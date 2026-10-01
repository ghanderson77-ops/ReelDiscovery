using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Picks the core document type (CASE-04). An explicit user preference always wins.
/// </summary>
public static class DocumentTypeResolver
{
    public static CoreDocumentType Resolve(MatterKind matterKind, DocumentTypePreference preference, CourtSystem courtSystem, bool usesPetitionTerminology)
    {
        switch (preference)
        {
            case DocumentTypePreference.Complaint:
                return CoreDocumentType.Complaint;
            case DocumentTypePreference.Petition:
                return CoreDocumentType.Petition;
            case DocumentTypePreference.InvestigationMemo:
                return CoreDocumentType.InvestigationMemo;
        }

        if (matterKind is MatterKind.Criminal or MatterKind.Regulatory)
            return CoreDocumentType.InvestigationMemo;

        // Petition only for state courts that use petition terminology (assumption A10)
        return courtSystem == CourtSystem.State && usesPetitionTerminology
            ? CoreDocumentType.Petition
            : CoreDocumentType.Complaint;
    }
}
