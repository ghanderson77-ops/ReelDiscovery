using ReelDiscovery.Models;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Baseline 144-DPI renders of the Phase 2 case screens. Clipping findings are recorded in the
/// .clip.txt files, not asserted: they are fixed in later Phase 3 plans.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class BaselineRenderTests
{
    [Fact]
    public void StepCaseScenario_Baseline()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                var result = RenderHarness.RenderControl(() =>
                {
                    var step = new StepCaseScenario();
                    // The step reads the recent-cases list on enter; point it at an empty temp root, never the user's list
                    var root = Path.Combine(Path.GetTempPath(), "rd-baseline-" + Guid.NewGuid().ToString("N"));
                    var store = new Services.Case.CaseStore(Path.Combine(root, "Cases"), Path.Combine(root, "AppData"));
                    step.BindState(new WizardState { Case = CaseTestData.SampleComplaintDraft(), CaseStore = store });
                    return step;
                }, "step-case-scenario", size, control =>
                {
                    ((StepCaseScenario)control).OnEnterStepAsync().GetAwaiter().GetResult();
                });

                AssertRendered(result);
            }
        });
    }

    [Fact]
    public void StepCoreDocument_Baseline()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                var result = RenderHarness.RenderControl(() =>
                {
                    var step = new StepCoreDocument();
                    // The fixture has a CoreDocument with a current SourceHash: entering does not generate or prompt
                    step.BindState(new WizardState { Case = CaseTestData.SampleComplaintDraft() });
                    return step;
                }, "step-core-document", size, control =>
                {
                    ((StepCoreDocument)control).OnEnterStepAsync().GetAwaiter().GetResult();
                });

                AssertRendered(result);
            }
        });
    }

    private static void AssertRendered(RenderResult result)
    {
        Assert.True(File.Exists(result.PngPath), result.PngPath);
        Assert.True(new FileInfo(result.PngPath).Length > 10_000, $"{result.PngPath} looks blank");
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
    }
}
