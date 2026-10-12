using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Tests.Render;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static, UI-free helpers; no Form or UserControl is instantiated.
public class CaseScenarioUiLogicTests
{
    [Theory]
    [InlineData(EntityKind.Person, "Person")]
    [InlineData(EntityKind.Company, "Company")]
    [InlineData(EntityKind.Brand, "Brand")]
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
    public void CounterColor_AtOrBelowNinetyPercent_IsTextMuted(int length)
    {
        Assert.Equal(Theme.TextMuted, StepCaseScenario.CounterColor(length));
    }

    [Theory]
    [InlineData(7201)]
    [InlineData(8000)]
    public void CounterColor_AboveNinetyPercent_IsWarning(int length)
    {
        Assert.Equal(Theme.Warning, StepCaseScenario.CounterColor(length));
    }

    [Fact]
    public void CounterColor_OverCap_IsDanger()
    {
        Assert.Equal(Theme.Danger, StepCaseScenario.CounterColor(8001));
    }

    [Fact]
    public void MarkOpenedFromDisk_ClearsPlanOnEntryOnce()
    {
        // Opening a case never starts a billed planning run by itself (T-04-62, 09 W2)
        Assert.False(new WizardState().PlanOnEntryOnce);
        var state = new WizardState { PlanOnEntryOnce = true };
        StepCaseScenario.MarkOpenedFromDisk(state);
        Assert.False(state.PlanOnEntryOnce);
    }

    [Fact]
    public void MarkOpenedFromDisk_GuideEditedAfterApproval_WithdrawsApproval()
    {
        // 09 W1 backstop: a case whose guide changed after approval reopens unapproved
        using var fixture = new GenerateCorpusRenderTests.StepFixture(approved: true);
        fixture.Draft.Guide!.Facts[0].Title += " (revised)";
        var revision = fixture.Draft.Revision;
        fixture.State.PlanOnEntryOnce = true;

        StepCaseScenario.MarkOpenedFromDisk(fixture.State);

        Assert.False(fixture.Draft.Corpus!.Approved);
        Assert.True(fixture.Draft.Revision > revision);
        Assert.False(fixture.State.PlanOnEntryOnce);
    }

    [Fact]
    public void MarkOpenedFromDisk_UneditedGuide_KeepsApproval()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture(approved: true);
        var revision = fixture.Draft.Revision;

        StepCaseScenario.MarkOpenedFromDisk(fixture.State);

        Assert.True(fixture.Draft.Corpus!.Approved);
        Assert.Equal(revision, fixture.Draft.Revision);
    }

    [Fact]
    public void MarkOpenedFromDisk_CaseWithoutCorpus_DoesNotThrow()
    {
        var state = new WizardState { Case = new CaseDraft { Corpus = null }, PlanOnEntryOnce = true };
        StepCaseScenario.MarkOpenedFromDisk(state);
        Assert.False(state.PlanOnEntryOnce);
        Assert.Null(state.Case.Corpus);
    }

    [Fact]
    public void ReviewPlanEntry_ClearsPlanOnEntryOnceBeforeDeciding()
    {
        // Read-and-clear before the decision so a cancelled or failed run never re-plans on Back/Forward (09 Pitfall 5)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReelDiscovery.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "UserControls", "StepReviewPlan.Run.cs"));
        var enter = source.IndexOf("OnEnterStepAsync", StringComparison.Ordinal);
        var clear = source.IndexOf("_state.PlanOnEntryOnce = false;", StringComparison.Ordinal);
        var decide = source.IndexOf("ReviewPlanEntryDecision(", StringComparison.Ordinal);
        Assert.True(enter >= 0 && clear > enter && decide > clear,
            "Review Plan entry must read and clear PlanOnEntryOnce before ReviewPlanEntryDecision");
    }

    [Fact]
    public void OpenCase_CallsMarkOpenedFromDiskAfterLoad()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReelDiscovery.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "UserControls", "StepCaseScenario.cs"));
        var load = source.IndexOf("_state.Case = result.Draft!;", StringComparison.Ordinal);
        var mark = source.IndexOf("MarkOpenedFromDisk(_state);", StringComparison.Ordinal);
        Assert.True(load >= 0 && mark > load && mark - load < 120, "OpenCase must mark the case as opened from disk right after loading it");
    }

    [Fact]
    public void NeedsOpenConfirmation_NewDraft_IsFalse()
    {
        Assert.False(StepCaseScenario.NeedsOpenConfirmation(new CaseDraft()));
    }

    [Fact]
    public void NeedsOpenConfirmation_TypedScenarioWithoutFolder_IsTrue()
    {
        Assert.True(StepCaseScenario.NeedsOpenConfirmation(new CaseDraft { Scenario = "A dispute." }));
    }

    [Fact]
    public void NeedsOpenConfirmation_SavedFolder_IsFalse()
    {
        var draft = new CaseDraft { Scenario = "A dispute.", FolderPath = @"C:\Cases\Brightline v. Harmon" };

        Assert.False(StepCaseScenario.NeedsOpenConfirmation(draft));
    }

    [Fact]
    public void NeedsOpenConfirmation_WithCoreDocument_IsFalse()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.Scenario = "A dispute.";
        draft.FolderPath = string.Empty;

        Assert.False(StepCaseScenario.NeedsOpenConfirmation(draft));
    }

    [Fact]
    public void NeedsOpenConfirmation_Null_IsFalse()
    {
        Assert.False(StepCaseScenario.NeedsOpenConfirmation(null));
    }

    [Fact]
    public void RecentEmptyText_MatchesUiSpec()
    {
        Assert.Equal("No saved cases yet. A case is saved automatically once its core document is written.", StepCaseScenario.RecentEmptyText);
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
