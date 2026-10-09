using ReelDiscovery.Forms;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Render;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// Phase 7: one case-driven flow (D-21). Covers WizardForm's step list, close decisions and Finish path,
/// and the removal of the flow choice (D-20).
/// </summary>
public class WizardFlowTests
{
    private static readonly Type[] CaseSteps =
    {
        typeof(StepApiConfiguration), typeof(StepCaseScenario), typeof(StepCoreDocument), typeof(StepCaseGuide),
        typeof(StepCorpusSettings), typeof(StepReviewPlan), typeof(StepGenerateCorpus), typeof(StepDiscoveryDocuments),
        typeof(StepCaseSummary)
    };

    [Fact]
    public void StepTypes_AreTheCaseFlow()
    {
        Assert.Equal(CaseSteps, WizardForm.StepTypes);
    }

    [Fact]
    public void StepTypes_OpenOnApiThenCaseScenario()
    {
        // D-21: the app opens on API Configuration and Next leads to Case Scenario (recent cases)
        Assert.Equal(typeof(StepApiConfiguration), WizardForm.StepTypes[0]);
        Assert.Equal(typeof(StepCaseScenario), WizardForm.StepTypes[1]);
    }

    [Fact]
    public void WizardFlowType_IsGone()
    {
        // D-20: no flow enum, no flow property on the state and no per-flow step table
        var assembly = typeof(WizardForm).Assembly;
        Assert.Null(assembly.GetType("ReelDiscovery.Models.WizardFlow"));
        Assert.Null(typeof(WizardState).GetProperty("Flow"));
        Assert.Null(typeof(WizardForm).GetMethod("StepTypesFor",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public));
    }

    [Fact]
    public void WizardForm_NoLegacyStepReferences()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Forms", "WizardForm.cs"));
        foreach (var name in new[] { "StepTopicInput", "StepStorylines", "StepCharacters", "StepGenerationConfig", "typeof(StepGeneration)", "StepComplete" })
            Assert.DoesNotContain(name, source);
    }

    [Fact]
    public void ApiStep_HasNoWorkflowRows()
    {
        // S7: the Workflow header and radios (rows 13-14) are removed; the disclaimer row is last
        RenderHarness.RunSta(() =>
        {
            using var step = new StepApiConfiguration();
            Assert.Empty(AllControls(step).OfType<RadioButton>());
            Assert.DoesNotContain(AllControls(step).OfType<Label>(), l => l.Text == "Workflow");

            var layout = step.Controls.OfType<TableLayoutPanel>().Single();
            Assert.Equal(13, layout.RowCount);
            Assert.Equal(13, layout.RowStyles.Count);
            var disclaimer = AllControls(step).OfType<Label>().Single(l => l.Text.StartsWith("NOTICE:", StringComparison.Ordinal));
            Assert.Equal(12, layout.GetRow(disclaimer));
        });
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
        Assert.Contains(expected, WizardForm.StepTypes);
    }

    [Fact]
    public void StepTypeForStage_UnknownValue_FallsBackToScenario()
    {
        // A tampered case.json stage never opens an arbitrary step (T-03-60)
        Assert.Equal(typeof(StepCaseScenario), WizardForm.StepTypeForStage((CaseStage)99));
    }

    [Fact]
    public void CorpusStepsSitBetweenGuideAndSummary()
    {
        // D-12: Corpus Settings is step 5 of 9, Review Plan step 6 and Generate Corpus step 7 (UI-SPEC S4)
        var types = WizardForm.StepTypes.ToList();
        Assert.Equal(9, types.Count);
        Assert.Equal(4, types.IndexOf(typeof(StepCorpusSettings)));
        Assert.Equal(5, types.IndexOf(typeof(StepReviewPlan)));
        Assert.Equal(6, types.IndexOf(typeof(StepGenerateCorpus)));
        Assert.Equal(8, types.IndexOf(typeof(StepCaseSummary)));
    }

    [Fact]
    public void DiscoveryStepSitsBetweenCorpusAndSummary()
    {
        // D-07: Discovery Documents is the optional step 8 of 9, between Generate Corpus and Case Summary
        var types = WizardForm.StepTypes.ToList();
        Assert.Equal(9, types.Count);
        Assert.Equal(6, types.IndexOf(typeof(StepGenerateCorpus)));
        Assert.Equal(7, types.IndexOf(typeof(StepDiscoveryDocuments)));
        Assert.Equal(8, types.IndexOf(typeof(StepCaseSummary)));
    }

    [Fact]
    public void DiscoveryStep_HasNoCaseStage()
    {
        // D-09: no new CaseStage; reopening a case lands on a corpus stage or Summary, never on the discovery step
        Assert.Equal(7, Enum.GetValues<CaseStage>().Length);
        foreach (var stage in Enum.GetValues<CaseStage>())
            Assert.NotEqual(typeof(StepDiscoveryDocuments), WizardForm.StepTypeForStage(stage));
    }

    [Fact]
    public void LeaveConfirmation_IsGeneralizedThroughIConfirmLeave()
    {
        // Research Pattern 1: steps that guard leaving implement IConfirmLeave instead of another `is StepX` check
        Assert.True(typeof(IConfirmLeave).IsAssignableFrom(typeof(StepCaseSummary)));
        Assert.True(typeof(IConfirmLeave).IsAssignableFrom(typeof(StepDiscoveryDocuments)));

        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Forms", "WizardForm.cs"));
        Assert.Contains("is IConfirmLeave", source);
        Assert.DoesNotContain("is StepCaseSummary summary && !summary.ConfirmLeave()", source);
    }

    [Fact]
    public void DiscoveryRun_IsStoppedOnClose()
    {
        // D-05 / T-08-48: a discovery run counts as active, close asks C-D2 and the run is cancelled and awaited
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Forms", "WizardForm.cs"));

        var active = source.IndexOf("private bool IsRunActive()", StringComparison.Ordinal);
        var stop = source.IndexOf("private void StopActiveRun()", StringComparison.Ordinal);
        Assert.True(active >= 0 && stop > active, "IsRunActive body not found");
        Assert.Contains("StepDiscoveryDocuments { IsGenerating: true }", source[active..stop]);

        var waiting = source.IndexOf("private void WaitWhileStopping", StringComparison.Ordinal);
        Assert.True(waiting > stop, "StopActiveRun body not found");
        Assert.Contains("discovery.CancelAndWaitForRun", source[stop..waiting]);

        var closing = source.IndexOf("protected override void OnFormClosing", StringComparison.Ordinal);
        Assert.True(closing >= 0, "OnFormClosing not found");
        Assert.Contains("StepDiscoveryDocuments => DiscoveryUiText.CD2CloseDuringRun()", source[closing..]);
    }

    [Fact]
    public void NavigateToStep_ByType_OpensDiscoveryDocuments()
    {
        // D-08: Case Summary reaches the Discovery Documents step by type, never by index
        RenderHarness.RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "rd-flow-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CaseStore(Path.Combine(root, "Cases"), Path.Combine(root, "AppData"));
                var draft = CaseTestData.SampleGuidedDraft();
                draft.AdvanceStage(CaseStage.Summary);
                store.EnsureFolder(draft);
                Assert.True(store.SaveNow(draft), store.LastSaveError);

                using var form = WizardForm.CreateForRender(new WizardState { Case = draft, CaseStore = store });
                form.NavigateToStep(typeof(StepDiscoveryDocuments));

                // ShowStep is async (entering a step saves the case): pump until the header shows the new step
                var title = form.Controls.Find("lblStepTitle", searchAllChildren: true).OfType<Label>().Single();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!title.Text.StartsWith("Step 8 of 9", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }

                Assert.Equal("Step 8 of 9: Discovery Documents", title.Text);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                    // A file handle may linger briefly after the form closes; the temp folder is harmless
                }
            }
        });
    }

    [Fact]
    public void AllStepTypes_AreWizardSteps()
    {
        foreach (var type in WizardForm.StepTypes)
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
        Assert.DoesNotContain("BuildSteps", body);

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
    public void Cancel_ClosesWithoutLegacyPrompt()
    {
        // OnFormClosing saves and asks only when something would be lost (D-10); the legacy prompt is gone
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Forms", "WizardForm.cs"));
        Assert.DoesNotContain("Are you sure you want to cancel?", source);
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
    public void DecideClose_NullDraft_ClosesNow()
    {
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(null!, runActive: false, saveSucceeded: false));
    }

    [Fact]
    public void DecideClose_EmptyDraft_ClosesNow()
    {
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(new CaseDraft(), false, true));
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(new CaseDraft { Scenario = "   " }, false, true));
    }

    [Fact]
    public void DecideClose_TypedScenarioWithoutFolder_AsksC11()
    {
        var draft = new CaseDraft { Scenario = "A dispute." };
        Assert.Equal(WizardForm.CloseDecision.AskUnsavedScenario, WizardForm.DecideClose(draft, false, true));
    }

    [Fact]
    public void DecideClose_SavedCase_ClosesWithoutPrompt()
    {
        var draft = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument(), FolderPath = @"C:\Cases\A" };
        Assert.Equal(WizardForm.CloseDecision.CloseNow, WizardForm.DecideClose(draft, false, saveSucceeded: true));
    }

    [Fact]
    public void DecideClose_FailedSave_AsksC10()
    {
        var draft = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument(), FolderPath = @"C:\Cases\A" };
        Assert.Equal(WizardForm.CloseDecision.AskFailedSave, WizardForm.DecideClose(draft, false, saveSucceeded: false));

        // A core document whose folder could not be created is a failed save too
        var unsaved = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument() };
        Assert.Equal(WizardForm.CloseDecision.AskFailedSave, WizardForm.DecideClose(unsaved, false, saveSucceeded: false));
    }

    [Fact]
    public void DecideClose_RunActive_TakesPrecedence()
    {
        var saved = new CaseDraft { Scenario = "A dispute.", CoreDocument = new CoreDocument(), FolderPath = @"C:\Cases\A" };
        Assert.Equal(WizardForm.CloseDecision.AskDuringRun, WizardForm.DecideClose(saved, runActive: true, saveSucceeded: true));
        Assert.Equal(WizardForm.CloseDecision.AskDuringRun, WizardForm.DecideClose(saved, runActive: true, saveSucceeded: false));
        Assert.Equal(WizardForm.CloseDecision.AskDuringRun, WizardForm.DecideClose(new CaseDraft { Scenario = "A dispute." }, runActive: true, saveSucceeded: true));
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
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
