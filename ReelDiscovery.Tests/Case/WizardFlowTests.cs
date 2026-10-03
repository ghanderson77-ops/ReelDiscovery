using ReelDiscovery.Forms;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers WizardForm's flow table and Finish path without instantiating any form or control.
public class WizardFlowTests
{
    [Fact]
    public void CaseDriven_StepTypes()
    {
        Assert.Equal(
            new[]
            {
                typeof(StepApiConfiguration), typeof(StepCaseScenario), typeof(StepCoreDocument), typeof(StepCaseGuide),
                typeof(StepCorpusSettings), typeof(StepReviewPlan), typeof(StepGenerateCorpus), typeof(StepCaseSummary)
            },
            WizardForm.StepTypesFor(WizardFlow.CaseDriven));
    }

    [Theory]
    [InlineData(CaseStage.Described, typeof(StepCaseScenario))]
    [InlineData(CaseStage.CoreDocument, typeof(StepCoreDocument))]
    [InlineData(CaseStage.CaseGuide, typeof(StepCaseGuide))]
    [InlineData(CaseStage.CorpusSettings, typeof(StepCorpusSettings))]
    [InlineData(CaseStage.ReviewPlan, typeof(StepReviewPlan))]
    [InlineData(CaseStage.Generation, typeof(StepGenerateCorpus))]
    [InlineData(CaseStage.Summary, typeof(StepCaseSummary))]
    public void StepTypeForStage_MapsEveryStage(CaseStage stage, Type expected)
    {
        Assert.Equal(expected, WizardForm.StepTypeForStage(stage));
        Assert.Contains(expected, WizardForm.StepTypesFor(WizardFlow.CaseDriven));
    }

    [Fact]
    public void StepTypeForStage_UnknownValue_FallsBackToScenario()
    {
        // A tampered case.json stage never opens an arbitrary step (T-03-60)
        Assert.Equal(typeof(StepCaseScenario), WizardForm.StepTypeForStage((CaseStage)99));
    }

    [Fact]
    public void CaseDriven_CorpusStepsSitBetweenGuideAndSummary()
    {
        // D-12: Corpus Settings is step 5 of 8, Review Plan step 6 and Generate Corpus step 7 (UI-SPEC S4)
        var types = WizardForm.StepTypesFor(WizardFlow.CaseDriven);
        Assert.Equal(8, types.Count);
        Assert.Equal(4, types.ToList().IndexOf(typeof(StepCorpusSettings)));
        Assert.Equal(5, types.ToList().IndexOf(typeof(StepReviewPlan)));
        Assert.Equal(6, types.ToList().IndexOf(typeof(StepGenerateCorpus)));
        Assert.Equal(7, types.ToList().IndexOf(typeof(StepCaseSummary)));
    }

    [Fact]
    public void Legacy_StepTypes()
    {
        Assert.Equal(
            new[]
            {
                typeof(StepApiConfiguration), typeof(StepTopicInput), typeof(StepStorylines), typeof(StepCharacters),
                typeof(StepGenerationConfig), typeof(StepGeneration), typeof(StepComplete)
            },
            WizardForm.StepTypesFor(WizardFlow.Legacy));
    }

    [Fact]
    public void AllStepTypes_AreWizardSteps()
    {
        var types = WizardForm.StepTypesFor(WizardFlow.CaseDriven)
            .Concat(WizardForm.StepTypesFor(WizardFlow.Legacy))
            .Distinct();

        foreach (var type in types)
        {
            Assert.True(typeof(IWizardStep).IsAssignableFrom(type), $"{type.Name} must implement IWizardStep");
            Assert.True(typeof(UserControl).IsAssignableFrom(type), $"{type.Name} must derive from UserControl");
            Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        }
    }

    [Fact]
    public void FinishPath_ValidatesBeforeClose()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Forms", "WizardForm.cs"));
        var start = source.IndexOf("private async void BtnNext_Click", StringComparison.Ordinal);
        var end = source.IndexOf("private void BtnBack_Click", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "BtnNext_Click body not found");

        var body = source[start..end];
        var validate = body.IndexOf("ValidateStepAsync", StringComparison.Ordinal);
        var close = body.IndexOf("this.Close()", StringComparison.Ordinal);

        Assert.True(validate >= 0, "BtnNext_Click must call ValidateStepAsync");
        Assert.True(close >= 0, "BtnNext_Click must close on Finish");
        Assert.True(validate < close, "ValidateStepAsync must run before this.Close()");

        // The discard prompt lives in OnFormClosing so Finish, Cancel and the window X all ask (WR-08)
        var closing = source.IndexOf("protected override void OnFormClosing", StringComparison.Ordinal);
        Assert.True(closing >= 0, "OnFormClosing not found");
        var closingBody = source[closing..];
        Assert.Contains("\"Close Case\"", closingBody);
        Assert.Contains("C11CloseUnsavedScenario", closingBody);
        Assert.Equal("Close Case", CaseGuideUiText.C11CloseUnsavedScenario().Title);
        Assert.Contains("e.Cancel = true", closingBody);
        Assert.Contains("CaseStore.SaveNow", source);
    }

    [Fact]
    public void FormClosing_SavesInsteadOfDiscarding()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Forms", "WizardForm.cs"));
        Assert.DoesNotContain("This version does not save the case yet", source);
        var closing = source.IndexOf("protected override void OnFormClosing", StringComparison.Ordinal);
        Assert.Contains("CaseStore.SaveNow", source);
        Assert.True(closing >= 0);
    }

    [Fact]
    public void DecideClose_LegacyFlow_ClosesNow()
    {
        var draft = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument() };
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(WizardFlow.Legacy, draft, runActive: false, saveSucceeded: false));
    }

    [Fact]
    public void DecideClose_EmptyDraft_ClosesNow()
    {
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(WizardFlow.CaseDriven, new CaseDraft(), false, true));
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(WizardFlow.CaseDriven, new CaseDraft { Scenario = "   " }, false, true));
    }

    [Fact]
    public void DecideClose_TypedScenarioWithoutFolder_AsksC11()
    {
        var draft = new CaseDraft { Scenario = "A dispute." };
        Assert.Equal(WizardForm.CloseDecision.AskUnsavedScenario, WizardForm.DecideClose(WizardFlow.CaseDriven, draft, false, true));
    }

    [Fact]
    public void DecideClose_SavedCase_ClosesWithoutPrompt()
    {
        var draft = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument(), FolderPath = @"C:\Cases\A" };
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(WizardFlow.CaseDriven, draft, false, saveSucceeded: true));
    }

    [Fact]
    public void DecideClose_FailedSave_AsksC10()
    {
        var draft = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument(), FolderPath = @"C:\Cases\A" };
        Assert.Equal(WizardForm.CloseDecision.AskFailedSave, WizardForm.DecideClose(WizardFlow.CaseDriven, draft, false, saveSucceeded: false));

        // A core document whose folder could not be created is a failed save too
        var unsaved = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument() };
        Assert.Equal(WizardForm.CloseDecision.AskFailedSave, WizardForm.DecideClose(WizardFlow.CaseDriven, unsaved, false, saveSucceeded: false));
    }

    [Fact]
    public void DecideClose_RunActive_TakesPrecedence()
    {
        var saved = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument(), FolderPath = @"C:\Cases\A" };
        Assert.Equal(WizardForm.CloseDecision.AskDuringRun, WizardForm.DecideClose(WizardFlow.CaseDriven, saved, runActive: true, saveSucceeded: true));
        Assert.Equal(WizardForm.CloseDecision.AskDuringRun, WizardForm.DecideClose(WizardFlow.CaseDriven, saved, runActive: true, saveSucceeded: false));
        Assert.Equal(WizardForm.CloseDecision.AskDuringRun, WizardForm.DecideClose(WizardFlow.CaseDriven, new CaseDraft { Scenario = "A dispute." }, runActive: true, saveSucceeded: true));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReelDiscovery.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate ReelDiscovery.csproj");
    }
}
