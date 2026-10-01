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
    [InlineData("Walter White", "Walter White")]
    [InlineData("Walter White", "Walter White Jr.")]
    [InlineData("Walter White", "walter white")]
    [InlineData("Walter White", "Hank White")]   // keeps the surname alias
    [InlineData("Walter White", "Walt Brennan")] // keeps the nickname alias
    public void FindLeakingSubstitute_RejectsSubstituteContainingRealName(string original, string substitute)
    {
        var entity = new EntitySubstitution
        {
            Original = original,
            Aliases = new List<string> { "White", "Walt" },
            Kind = EntityKind.Person,
            Substitute = substitute,
            Apply = true
        };

        Assert.Same(entity, EntityReviewDialog.FindLeakingSubstitute(new[] { entity }));
    }

    [Fact]
    public void FindLeakingSubstitute_ValidOrKeptOrBlank_ReturnsNull()
    {
        var valid = new EntitySubstitution { Original = "Walter White", Aliases = new List<string> { "White" }, Substitute = "Hank Brennan", Apply = true };
        var kept = new EntitySubstitution { Original = "Jesse Pinkman", Substitute = "Jesse Pinkman", Apply = false };
        var blank = new EntitySubstitution { Original = "Gus Fring", Substitute = "", Apply = true };

        Assert.Null(EntityReviewDialog.FindLeakingSubstitute(new[] { valid, kept, blank }));
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
    public void StoreInputs_KeepsUnsubmittedScenarioAndSettings()
    {
        var draft = new CaseDraft
        {
            EffectiveScenario = "Checked text",
            EntityCheckHash = "abc",
            Substitutions = new List<EntitySubstitution> { new() { Original = "Walter White", Substitute = "Hank Brennan", Apply = true } }
        };

        StepCaseScenario.StoreInputs(draft, "  Typed but not submitted.  ", ComplexityLevel.Complex,
            (int)DocumentTypePreference.Petition, (int)CourtPreference.State, "Texas");

        Assert.Equal("Typed but not submitted.", draft.Scenario);
        Assert.Equal(ComplexityLevel.Complex, draft.Complexity);
        Assert.Equal(DocumentTypePreference.Petition, draft.DocumentPreference);
        Assert.Equal(CourtPreference.State, draft.CourtPreference);
        Assert.Equal("Texas", draft.StatePreference);

        // The entity check results are left alone so a changed scenario is re-checked on Next
        Assert.Equal("Checked text", draft.EffectiveScenario);
        Assert.Equal("abc", draft.EntityCheckHash);
        Assert.Single(draft.Substitutions);
    }

    [Theory]
    [InlineData((int)CourtPreference.State, "Auto")]
    [InlineData((int)CourtPreference.Federal, "Texas")]
    [InlineData((int)CourtPreference.State, null)]
    public void StoreInputs_StateOnlyKeptForChosenStateCourt(int courtIndex, string? state)
    {
        var draft = new CaseDraft { StatePreference = "Ohio" };

        StepCaseScenario.StoreInputs(draft, "Scenario", ComplexityLevel.Standard, -1, courtIndex, state);

        Assert.Equal(string.Empty, draft.StatePreference);
        Assert.Equal(DocumentTypePreference.Auto, draft.DocumentPreference);
    }

    [Fact]
    public void CounterText_FormatsWithThousandsSeparator()
    {
        Assert.Equal("1,234 / 8,000 characters", StepCaseScenario.CounterText(1234));
    }
}
