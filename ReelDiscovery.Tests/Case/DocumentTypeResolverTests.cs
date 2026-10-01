using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class DocumentTypeResolverTests
{
    [Theory]
    [InlineData(MatterKind.Civil, DocumentTypePreference.Auto, CourtSystem.Federal, false, CoreDocumentType.Complaint)]
    [InlineData(MatterKind.Civil, DocumentTypePreference.Auto, CourtSystem.Federal, true, CoreDocumentType.Complaint)]
    [InlineData(MatterKind.Civil, DocumentTypePreference.Auto, CourtSystem.State, true, CoreDocumentType.Petition)]
    [InlineData(MatterKind.Civil, DocumentTypePreference.Auto, CourtSystem.State, false, CoreDocumentType.Complaint)]
    [InlineData(MatterKind.Criminal, DocumentTypePreference.Auto, CourtSystem.Federal, false, CoreDocumentType.InvestigationMemo)]
    [InlineData(MatterKind.Regulatory, DocumentTypePreference.Auto, CourtSystem.State, false, CoreDocumentType.InvestigationMemo)]
    [InlineData(MatterKind.Criminal, DocumentTypePreference.Complaint, CourtSystem.Federal, false, CoreDocumentType.Complaint)]
    [InlineData(MatterKind.Civil, DocumentTypePreference.InvestigationMemo, CourtSystem.State, true, CoreDocumentType.InvestigationMemo)]
    [InlineData(MatterKind.Civil, DocumentTypePreference.Petition, CourtSystem.Federal, false, CoreDocumentType.Petition)]
    public void Resolve_Matrix(MatterKind kind, DocumentTypePreference preference, CourtSystem court, bool petitionTerms, CoreDocumentType expected)
    {
        Assert.Equal(expected, DocumentTypeResolver.Resolve(kind, preference, court, petitionTerms));
    }
}
