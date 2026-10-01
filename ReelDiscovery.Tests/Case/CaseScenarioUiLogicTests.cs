using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static, UI-free helpers; no Form or UserControl is instantiated.
public class CaseScenarioUiLogicTests
{
    [Theory]
    [InlineData(EntityKind.Person, "Person")]
    [InlineData(EntityKind.Company, "Company")]
    [InlineData(EntityKind.Brand, "Brand")]
    [InlineData(EntityKind.FictionalWorkCharacter, "Fictional character")]
    [InlineData(EntityKind.FictionalWorkOrganization, "Fictional organization")]
    public void KindLabel_MapsEveryKind(EntityKind kind, string expected)
    {
        Assert.Equal(expected, EntityReviewDialog.KindLabel(kind));
    }

    [Fact]
    public void NameWithAliases_AppendsAliasesInParentheses()
    {
        var entity = new EntitySubstitution
        {
            Original = "Walter White",
            Aliases = new List<string> { "White", "Walt" }
        };

        Assert.Equal("Walter White (White, Walt)", EntityReviewDialog.NameWithAliases(entity));
    }

    [Fact]
    public void NameWithAliases_NoAliases_ReturnsName()
    {
        var entity = new EntitySubstitution { Original = "Walter White" };

        Assert.Equal("Walter White", EntityReviewDialog.NameWithAliases(entity));
    }

    [Fact]
    public void FindBlankSubstitute_ReturnsFirstAppliedBlank()
    {
        var kept = new EntitySubstitution { Original = "Kept", Apply = false, Substitute = "" };
        var filled = new EntitySubstitution { Original = "Filled", Apply = true, Substitute = "Hank Brennan" };
        var blank = new EntitySubstitution { Original = "Blank", Apply = true, Substitute = "   " };
        var blank2 = new EntitySubstitution { Original = "Blank2", Apply = true, Substitute = "" };

        Assert.Same(blank, EntityReviewDialog.FindBlankSubstitute(new[] { kept, filled, blank, blank2 }));
    }

    [Fact]
    public void FindBlankSubstitute_NoneBlank_ReturnsNull()
    {
        var kept = new EntitySubstitution { Original = "Kept", Apply = false, Substitute = "" };
        var filled = new EntitySubstitution { Original = "Filled", Apply = true, Substitute = "Hank Brennan" };

        Assert.Null(EntityReviewDialog.FindBlankSubstitute(new[] { kept, filled }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7200)]
    public void CounterColor_AtOrBelowNinetyPercent_IsDimGray(int length)
    {
        Assert.Equal(Color.DimGray, StepCaseScenario.CounterColor(length));
    }

    [Theory]
    [InlineData(7201)]
    [InlineData(8000)]
    public void CounterColor_AboveNinetyPercent_IsDarkOrange(int length)
    {
        Assert.Equal(Color.DarkOrange, StepCaseScenario.CounterColor(length));
    }

    [Fact]
    public void CounterColor_OverCap_IsRed()
    {
        Assert.Equal(Color.Red, StepCaseScenario.CounterColor(8001));
    }

    [Fact]
    public void CounterText_FormatsWithThousandsSeparator()
    {
        Assert.Equal("1,234 / 8,000 characters", StepCaseScenario.CounterText(1234));
    }
}
