using ReelDiscovery.Forms;
using ReelDiscovery.Models;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers WizardForm's flow table and Finish path without instantiating any form or control.
public class WizardFlowTests
{
    [Fact]
    public void CaseDriven_StepTypes()
    {
        Assert.Equal(
            new[] { typeof(StepApiConfiguration), typeof(StepCaseScenario), typeof(StepCoreDocument) },
            WizardForm.StepTypesFor(WizardFlow.CaseDriven));
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
        Assert.Contains("\"Close Case\"", body);
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
