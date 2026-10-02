namespace ReelDiscovery.Models.Corpus;

/// <summary>
/// One answer-key row (one document); values are display strings in AnswerKeyColumns.Headers order (D-32, KEY-02).
/// </summary>
public class AnswerKeyRow
{
    public string DocId { get; set; } = string.Empty;
    public string DocType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Custodian { get; set; } = string.Empty;
    public string AllCustodians { get; set; } = string.Empty;
    public string DateSent { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public string ParentDocId { get; set; } = string.Empty;
    public string Relevance { get; set; } = string.Empty;
    public string IssueCodes { get; set; } = string.Empty;
    public string FactIds { get; set; } = string.Empty;
    public string Hot { get; set; } = string.Empty;
    public string Privileged { get; set; } = string.Empty;
    public string NearMiss { get; set; } = string.Empty;
    public string NonRelevantCategory { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public string[] ToValues() => new[]
    {
        DocId, DocType, FilePath, FileName, Custodian, AllCustodians, DateSent, From, To, Cc, Subject, ThreadId,
        FamilyId, ParentDocId, Relevance, IssueCodes, FactIds, Hot, Privileged, NearMiss, NonRelevantCategory,
        Rationale, Status
    };
}

/// <summary>
/// Fixed answer-key column headers: D-32 order plus Doc Type, File Name and Parent DocID.
/// </summary>
public static class AnswerKeyColumns
{
    public static readonly string[] Headers =
    {
        "DocID",
        "Doc Type",
        "File Path",
        "File Name",
        "Custodian",
        "All Custodians",
        "Date Sent",
        "From",
        "To",
        "CC",
        "Subject",
        "Thread ID",
        "Family ID",
        "Parent DocID",
        "Relevance",
        "Issue Codes",
        "Fact IDs",
        "Hot",
        "Privileged",
        "Near-Miss Decoy",
        "Non-Relevant Category",
        "Rationale",
        "Status"
    };
}
