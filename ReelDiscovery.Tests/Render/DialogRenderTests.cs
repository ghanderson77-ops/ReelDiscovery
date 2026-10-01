using ReelDiscovery.Forms;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Case Guide dialogs (D1 RegenerateTabDialog, D2 DeleteReferencesDialog) at 144 DPI and checks
/// they are content-sized, unclipped and fit on the screen.
/// </summary>
[Trait("Category", "Render")]
public class DialogRenderTests
{
    private static readonly string[] FactLines =
    {
        "F-012  2024-03-03  Change order 7 approved",
        "F-014  2024-03-09  Site meeting on the curtain wall leaks",
        "F-017  2024-03-21  Revised window schedule sent to Harmon",
        "F-021  2024-04-02  Water test fails on level 4",
        "F-024  2024-04-15  Harmon withholds the April payment",
        "F-029  2024-05-06  Brightline sends notice of claim",
        "F-031  2024-05-20  Developer asks for the inspection reports",
        "F-036  2024-06-11  Mediation scheduled",
        "F-040  2024-07-01  Harmon terminates the subcontract"
    };

    [Theory]
    [InlineData(GuidePart.Cast, 3, "dialog-regenerate-cast")]
    [InlineData(GuidePart.Facts, 0, "dialog-regenerate-facts")]
    [InlineData(GuidePart.Values, 1, "dialog-regenerate-values")]
    public void RegenerateTabDialog_RendersWithoutClipping(GuidePart part, int editedCount, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var result = RenderHarness.RenderForm(() => new RegenerateTabDialog(part, editedCount), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
            });

            AssertRendered(result, height, workingHeight);
        });
    }

    [Theory]
    [InlineData(9, "dialog-delete-references-9")]
    [InlineData(1, "dialog-delete-references-1")]
    public void DeleteReferencesDialog_RendersWithoutClipping(int lineCount, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var lines = FactLines.Take(lineCount).ToList();
            var result = RenderHarness.RenderForm(() => new DeleteReferencesDialog("Maria Delgado", "Maria", "CH-007", lines), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
            });

            AssertRendered(result, height, workingHeight);
        });
    }

    [Fact]
    public void RegenerateTabDialog_RejectsIssues()
    {
        RenderHarness.RunSta(() =>
            Assert.Throws<ArgumentException>(() => new RegenerateTabDialog(GuidePart.Issues, 0)));
    }

    private static void AssertRendered(RenderResult result, int clientHeight, int workingHeight)
    {
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
        Assert.True(File.Exists(result.PngPath));
        Assert.Empty(result.Clipping);
        Assert.True(clientHeight > 0 && clientHeight < workingHeight,
            $"Dialog client height {clientHeight} must fit the working area {workingHeight}");
    }
}
