using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Models.Discovery;

public enum TurnKind
{
    Q,
    A,
    Colloquy,
    Objection,
    Parenthetical
}

public enum WitnessSide
{
    Plaintiff,
    Defense,
    NonParty
}

public enum SpeakerRole
{
    Examining,
    Defending,
    Appearance,
    Witness,
    Reporter,
    Videographer
}

public class DepositionSpeaker
{
    public string SpeakerId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public SpeakerRole Role { get; set; }

    // "MR", "MS", "DR" or empty
    public string Honorific { get; set; } = string.Empty;

    public string FirmName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PartyLabel { get; set; } = string.Empty;
}

public class DepositionTurn
{
    public TurnKind Kind { get; set; }
    public string SpeakerId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    // Exhibit marked or shown in this turn; 0 = none
    public int ExhibitNumber { get; set; }

    public int TopicIndex { get; set; }
}

public class DepositionExhibit
{
    public int Number { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;

    // yyyy-MM-dd
    public string Date { get; set; } = string.Empty;

    public string IssueId { get; set; } = string.Empty;
    public int TopicIndex { get; set; }
}

/// <summary>
/// Everything needed to rebuild a deposition excerpt .docx without another model call.
/// </summary>
public class DepositionContent
{
    public string WitnessId { get; set; } = string.Empty;
    public string WitnessName { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public List<string> CaptionLines { get; set; } = new();
    public string CaseNumber { get; set; } = string.Empty;

    // yyyy-MM-dd
    public string Date { get; set; } = string.Empty;

    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool Remote { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string ReporterCsr { get; set; } = string.Empty;
    public string VideographerName { get; set; } = string.Empty;
    public bool IsMemo { get; set; }
    public int TargetTestimonyPages { get; set; }
    public List<string> TopicTitles { get; set; } = new();
    public List<DepositionSpeaker> Speakers { get; set; } = new();
    public List<DepositionTurn> Turns { get; set; } = new();
    public List<DepositionExhibit> Exhibits { get; set; } = new();

    /// <summary>
    /// Repairs content loaded from the untrusted sidecar; never throws.
    /// </summary>
    public void Normalize()
    {
        WitnessId ??= string.Empty;
        WitnessName ??= string.Empty;
        DocumentTitle ??= string.Empty;
        CaseNumber ??= string.Empty;
        Date ??= string.Empty;
        StartTime ??= string.Empty;
        EndTime ??= string.Empty;
        Location ??= string.Empty;
        ReporterName ??= string.Empty;
        ReporterCsr ??= string.Empty;
        VideographerName ??= string.Empty;
        TargetTestimonyPages = Math.Max(0, TargetTestimonyPages);
        CaptionLines = CaseGuide.CleanStrings(CaptionLines);
        TopicTitles = CaseGuide.CleanStrings(TopicTitles);

        Speakers = CaseGuide.CleanList(Speakers);
        foreach (var speaker in Speakers)
        {
            speaker.SpeakerId ??= string.Empty;
            speaker.FullName ??= string.Empty;
            speaker.Honorific ??= string.Empty;
            speaker.FirmName ??= string.Empty;
            speaker.Email ??= string.Empty;
            speaker.PartyLabel ??= string.Empty;
        }

        Turns = CaseGuide.CleanList(Turns);
        foreach (var turn in Turns)
        {
            turn.SpeakerId ??= string.Empty;
            turn.Text ??= string.Empty;
            turn.ExhibitNumber = Math.Max(0, turn.ExhibitNumber);
            turn.TopicIndex = Math.Max(0, turn.TopicIndex);
        }

        Exhibits = CaseGuide.CleanList(Exhibits);
        foreach (var exhibit in Exhibits)
        {
            exhibit.Number = Math.Max(0, exhibit.Number);
            exhibit.Description ??= string.Empty;
            exhibit.DocId ??= string.Empty;
            exhibit.Date ??= string.Empty;
            exhibit.IssueId ??= string.Empty;
            exhibit.TopicIndex = Math.Max(0, exhibit.TopicIndex);
        }
    }
}
