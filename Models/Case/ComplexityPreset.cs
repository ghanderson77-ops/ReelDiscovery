using System.Text.Json.Serialization;

namespace ReelDiscovery.Models.Case;

public enum ComplexityLevel
{
    Simple,
    Standard,
    Complex
}

/// <summary>
/// Size targets for one complexity level; the single table read by the UI, the prompts and the validator.
/// </summary>
public class ComplexityPreset
{
    /// <summary>
    /// Memo length target derived from MemoPages (W4); used by the outline prompt and the validator.
    /// </summary>
    public const int MemoParagraphsPerPage = 6;

    public ComplexityLevel Level { get; set; }
    public int PartiesMin { get; set; }
    public int PartiesMax { get; set; }
    public int CountsMin { get; set; }
    public int CountsMax { get; set; }
    public int CharactersMin { get; set; }
    public int CharactersMax { get; set; }
    public int TimelineMonthsMin { get; set; }
    public int TimelineMonthsMax { get; set; }
    public int FactSubsectionsMin { get; set; }
    public int FactSubsectionsMax { get; set; }
    public int ParagraphsMin { get; set; }
    public int ParagraphsMax { get; set; }
    public int PagesMin { get; set; }
    public int PagesMax { get; set; }
    public int MemoPagesMin { get; set; }
    public int MemoPagesMax { get; set; }
    public int FindingsMin { get; set; }
    public int FindingsMax { get; set; }

    [JsonIgnore]
    public int MemoParagraphsMin => MemoPagesMin * MemoParagraphsPerPage;

    [JsonIgnore]
    public int MemoParagraphsMax => MemoPagesMax * MemoParagraphsPerPage;

    [JsonIgnore]
    public string Summary =>
        $"{Level}: {FormatRange(PartiesMin, PartiesMax)} parties · {FormatRange(CountsMin, CountsMax)} counts or issues · " +
        $"{FormatRange(CharactersMin, CharactersMax)} key characters · {FormatRange(TimelineMonthsMin, TimelineMonthsMax)} month timeline · " +
        $"about {FormatRange(PagesMin, PagesMax)} pages";

    public static string FormatRange(int min, int max) => min == max ? $"{min}" : $"{min}-{max}";

    // Research table values (assumption A1); every range strictly increases from Simple to Complex
    public static ComplexityPreset Get(ComplexityLevel level) => level switch
    {
        ComplexityLevel.Simple => new ComplexityPreset
        {
            Level = ComplexityLevel.Simple,
            PartiesMin = 2, PartiesMax = 2,
            CountsMin = 1, CountsMax = 2,
            CharactersMin = 6, CharactersMax = 10,
            TimelineMonthsMin = 3, TimelineMonthsMax = 6,
            FactSubsectionsMin = 2, FactSubsectionsMax = 2,
            ParagraphsMin = 40, ParagraphsMax = 70,
            PagesMin = 8, PagesMax = 12,
            MemoPagesMin = 6, MemoPagesMax = 10,
            FindingsMin = 1, FindingsMax = 2
        },
        ComplexityLevel.Standard => new ComplexityPreset
        {
            Level = ComplexityLevel.Standard,
            PartiesMin = 3, PartiesMax = 4,
            CountsMin = 3, CountsMax = 4,
            CharactersMin = 12, CharactersMax = 20,
            TimelineMonthsMin = 9, TimelineMonthsMax = 18,
            FactSubsectionsMin = 3, FactSubsectionsMax = 4,
            ParagraphsMin = 90, ParagraphsMax = 140,
            PagesMin = 20, PagesMax = 30,
            MemoPagesMin = 15, MemoPagesMax = 25,
            FindingsMin = 3, FindingsMax = 4
        },
        ComplexityLevel.Complex => new ComplexityPreset
        {
            Level = ComplexityLevel.Complex,
            PartiesMin = 5, PartiesMax = 7,
            CountsMin = 5, CountsMax = 7,
            CharactersMin = 25, CharactersMax = 40,
            TimelineMonthsMin = 24, TimelineMonthsMax = 48,
            FactSubsectionsMin = 6, FactSubsectionsMax = 8,
            ParagraphsMin = 180, ParagraphsMax = 260,
            PagesMin = 40, PagesMax = 60,
            MemoPagesMin = 30, MemoPagesMax = 50,
            FindingsMin = 5, FindingsMax = 7
        },
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown complexity level")
    };
}
