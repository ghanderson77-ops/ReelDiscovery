namespace ReelDiscovery.Models.Case;

public enum CoreDocumentType
{
    Complaint,
    Petition,
    InvestigationMemo
}

public enum MatterKind
{
    Civil,
    Criminal,
    Regulatory
}

public enum MemoPerspective
{
    InternalCorporate,
    Government
}

public enum PartyRole
{
    Plaintiff,
    Defendant,
    Subject,
    Complainant,
    ThirdParty
}

public class CaseParty
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public PartyRole Role { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Domicile { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A cause of action for pleadings, or an issue/finding for an Investigation Memo.
/// </summary>
public class CaseCount
{
    public string Id { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string> AgainstPartyIds { get; set; } = new();
    public string LegalBasis { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class FactSubsectionPlan
{
    public string Id { get; set; } = string.Empty;
    public string Heading { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int TargetParagraphs { get; set; }
    public string Focus { get; set; } = string.Empty;
}

public class CounselInfo
{
    public string AttorneyName { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public string BarNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class MemoHeader
{
    public string To { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Re { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
}

public class CoreDocument
{
    public CoreDocumentType DocumentType { get; set; }
    public MatterKind MatterKind { get; set; }
    public MemoPerspective MemoPerspective { get; set; }
    public Jurisdiction Jurisdiction { get; set; } = new();
    public bool UsesPetitionTerminology { get; set; }
    public bool DemandsJuryTrial { get; set; }
    public string Title { get; set; } = string.Empty;

    // Dates are yyyy-MM-dd strings (empty = unknown); parse only via CaseDraft.TryParseDate
    public string FilingDate { get; set; } = string.Empty;
    public string TimelineStart { get; set; } = string.Empty;
    public string TimelineEnd { get; set; } = string.Empty;

    public List<CaseParty> Parties { get; set; } = new();
    public List<CaseCount> Counts { get; set; } = new();
    public List<FactSubsectionPlan> FactPlan { get; set; } = new();
    public CounselInfo Counsel { get; set; } = new();
    public MemoHeader Memo { get; set; } = new();
    public List<CoreDocumentSection> Sections { get; set; } = new();

    /// <summary>
    /// CaseDraft.ComputeSourceHash() at generation time; a mismatch means the scenario or settings changed (Pitfall 6).
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;

    public CoreDocumentSection? FindSection(string id) => Sections.FirstOrDefault(s => s.Id == id);
}
