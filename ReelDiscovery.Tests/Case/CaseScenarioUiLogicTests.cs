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
}
