using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Tests.Case;

public class ComplexityPresetTests
{
    [Theory]
    [InlineData(ComplexityLevel.Simple)]
    [InlineData(ComplexityLevel.Standard)]
    [InlineData(ComplexityLevel.Complex)]
    public void Get_ReturnsLevel(ComplexityLevel level)
    {
        Assert.Equal(level, ComplexityPreset.Get(level).Level);
    }

    public static TheoryData<string> RangeNames() => new()
    {
        "Parties", "Counts", "Characters", "TimelineMonths", "FactSubsections",
        "Paragraphs", "Pages", "MemoPages", "Findings", "MemoParagraphs", "Facts"
    };

    [Theory]
    [MemberData(nameof(RangeNames))]
    public void Presets_StrictlyIncrease(string name)
    {
        var simple = ComplexityPreset.Get(ComplexityLevel.Simple);
        var standard = ComplexityPreset.Get(ComplexityLevel.Standard);
        var complex = ComplexityPreset.Get(ComplexityLevel.Complex);

        foreach (var preset in new[] { simple, standard, complex })
            Assert.True(Min(preset, name) <= Max(preset, name), $"{preset.Level}.{name} min > max");

        Assert.True(Min(standard, name) > Max(simple, name), $"Standard.{name}Min must exceed Simple.{name}Max");
        Assert.True(Min(complex, name) > Max(standard, name), $"Complex.{name}Min must exceed Standard.{name}Max");
    }

    [Theory]
    [InlineData(ComplexityLevel.Simple, "Simple: 2 parties · 1-2 counts or issues · 6-10 key characters · 3-6 month timeline · about 8-12 pages")]
    [InlineData(ComplexityLevel.Standard, "Standard: 3-4 parties · 3-4 counts or issues · 12-20 key characters · 9-18 month timeline · about 20-30 pages")]
    [InlineData(ComplexityLevel.Complex, "Complex: 5-7 parties · 5-7 counts or issues · 25-40 key characters · 24-48 month timeline · about 40-60 pages")]
    public void Summary_MatchesUiSpec(ComplexityLevel level, string expected)
    {
        Assert.Equal(expected, ComplexityPreset.Get(level).Summary);
    }

    [Fact]
    public void MemoParagraphs()
    {
        var simple = ComplexityPreset.Get(ComplexityLevel.Simple);
        Assert.Equal(36, simple.MemoParagraphsMin);
        Assert.Equal(60, simple.MemoParagraphsMax);

        var standard = ComplexityPreset.Get(ComplexityLevel.Standard);
        Assert.Equal(90, standard.MemoParagraphsMin);
        Assert.Equal(150, standard.MemoParagraphsMax);

        var complex = ComplexityPreset.Get(ComplexityLevel.Complex);
        Assert.Equal(180, complex.MemoParagraphsMin);
        Assert.Equal(300, complex.MemoParagraphsMax);
    }

    [Fact]
    public void BackgroundPerOrg_ByLevel()
    {
        Assert.Equal(3, ComplexityPreset.Get(ComplexityLevel.Simple).BackgroundPerOrg);
        Assert.Equal(6, ComplexityPreset.Get(ComplexityLevel.Standard).BackgroundPerOrg);
        Assert.Equal(10, ComplexityPreset.Get(ComplexityLevel.Complex).BackgroundPerOrg);
    }

    [Fact]
    public void FactsRange_ByLevel()
    {
        var simple = ComplexityPreset.Get(ComplexityLevel.Simple);
        Assert.Equal(12, simple.FactsMin);
        Assert.Equal(20, simple.FactsMax);

        var standard = ComplexityPreset.Get(ComplexityLevel.Standard);
        Assert.Equal(30, standard.FactsMin);
        Assert.Equal(50, standard.FactsMax);

        var complex = ComplexityPreset.Get(ComplexityLevel.Complex);
        Assert.Equal(70, complex.FactsMin);
        Assert.Equal(110, complex.FactsMax);
    }

    [Theory]
    [InlineData(2, 2, "2")]
    [InlineData(1, 2, "1-2")]
    public void FormatRange_CollapsesEqualBounds(int min, int max, string expected)
    {
        Assert.Equal(expected, ComplexityPreset.FormatRange(min, max));
    }

    private static int Min(ComplexityPreset preset, string name) => name switch
    {
        "Parties" => preset.PartiesMin,
        "Counts" => preset.CountsMin,
        "Characters" => preset.CharactersMin,
        "TimelineMonths" => preset.TimelineMonthsMin,
        "FactSubsections" => preset.FactSubsectionsMin,
        "Paragraphs" => preset.ParagraphsMin,
        "Pages" => preset.PagesMin,
        "MemoPages" => preset.MemoPagesMin,
        "Findings" => preset.FindingsMin,
        "MemoParagraphs" => preset.MemoParagraphsMin,
        "Facts" => preset.FactsMin,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static int Max(ComplexityPreset preset, string name) => name switch
    {
        "Parties" => preset.PartiesMax,
        "Counts" => preset.CountsMax,
        "Characters" => preset.CharactersMax,
        "TimelineMonths" => preset.TimelineMonthsMax,
        "FactSubsections" => preset.FactSubsectionsMax,
        "Paragraphs" => preset.ParagraphsMax,
        "Pages" => preset.PagesMax,
        "MemoPages" => preset.MemoPagesMax,
        "Findings" => preset.FindingsMax,
        "MemoParagraphs" => preset.MemoParagraphsMax,
        "Facts" => preset.FactsMax,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
