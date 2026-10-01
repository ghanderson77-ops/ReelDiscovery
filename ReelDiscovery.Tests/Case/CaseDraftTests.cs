using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseDraftTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateScenario_Empty(string? scenario)
    {
        Assert.Equal("Please describe the dispute or investigation before continuing.", CaseDraft.ValidateScenario(scenario));
    }

    [Fact]
    public void ValidateScenario_TooLong()
    {
        Assert.Equal(
            "Your scenario is 8,001 characters. Shorten it to 8,000 characters or fewer and click Next again.",
            CaseDraft.ValidateScenario(new string('a', 8001)));
    }

    [Fact]
    public void ValidateScenario_Ok()
    {
        Assert.Null(CaseDraft.ValidateScenario(new string('a', CaseDraft.MaxScenarioLength)));
        Assert.Null(CaseDraft.ValidateScenario("A construction defect dispute."));
    }

    [Fact]
    public void ComputeSourceHash_ChangesWithPreset()
    {
        var simple = new CaseDraft { EffectiveScenario = "Scenario text", Complexity = ComplexityLevel.Simple };
        var complex = new CaseDraft { EffectiveScenario = "Scenario text", Complexity = ComplexityLevel.Complex };
        var simpleAgain = new CaseDraft { EffectiveScenario = "Scenario text", Complexity = ComplexityLevel.Simple };

        Assert.NotEqual(simple.ComputeSourceHash(), complex.ComputeSourceHash());
        Assert.Equal(simple.ComputeSourceHash(), simpleAgain.ComputeSourceHash());
        Assert.Matches("^[0-9a-f]{64}$", simple.ComputeSourceHash());
    }

    [Fact]
    public void TryParseDate()
    {
        Assert.True(CaseDraft.TryParseDate("2024-03-05", out var date));
        Assert.Equal(new DateTime(2024, 3, 5), date);

        Assert.False(CaseDraft.TryParseDate("03/05/2024", out _));
        Assert.False(CaseDraft.TryParseDate("", out _));
        Assert.False(CaseDraft.TryParseDate(null, out _));
    }

    [Fact]
    public void UsStates()
    {
        Assert.Equal(51, Jurisdiction.UsStates.Length);
        Assert.Contains("Texas", Jurisdiction.UsStates);
        Assert.Equal("District of Columbia", Jurisdiction.UsStates[^1]);
        Assert.Equal(Jurisdiction.UsStates[..50].OrderBy(s => s, StringComparer.Ordinal), Jurisdiction.UsStates[..50]);
    }

    [Fact]
    public void WizardState_Defaults()
    {
        var state = new WizardState();

        Assert.Equal(WizardFlow.CaseDriven, state.Flow);
        Assert.NotNull(state.Case);
        Assert.Equal(ComplexityLevel.Standard, state.Case.Complexity);
    }
}
