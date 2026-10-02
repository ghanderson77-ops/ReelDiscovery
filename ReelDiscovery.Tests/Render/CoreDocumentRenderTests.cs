using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders Review Core Document after the Phase 3 changes (UI-SPEC S4): Content x alignment, the guide notice,
/// and no clipping at the active scaling. The 03-02 baseline findings must be gone.
/// </summary>
[Trait("Category", "Render")]
public class CoreDocumentRenderTests
{
    private const string GuideNotice = "The case guide was built from this document. Edits will mark it out of date.";

    [Fact]
    public void CoreDocument_NoGuide()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                var noticeVisible = true;
                var result = Render(CaseTestData.SampleComplaintDraft(), "core-document-no-guide", size,
                    control => noticeVisible = NoticeVisible(control));

                AssertRendered(result);
                Assert.False(noticeVisible, "The guide notice shows only when a guide exists");
            }
        });
    }

    [Fact]
    public void CoreDocument_WithGuide()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                var noticeVisible = false;
                var result = Render(CaseTestData.SampleGuidedDraft(), "core-document-with-guide", size,
                    control => noticeVisible = NoticeVisible(control));

                AssertRendered(result);
                Assert.True(noticeVisible, "The guide notice shows under the subtitle when a guide exists");
            }
        });
    }

    [Fact]
    public void CoreDocument_NextButtonText()
    {
        RenderHarness.RunSta(() => Assert.Equal("Next >", new StepCoreDocument().NextButtonText));
    }

    private static RenderResult Render(CaseDraft draft, string name, Size size, Action<Control> inspect)
    {
        var root = Path.Combine(Path.GetTempPath(), "rd-coredoc-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Saves (if any) go to a temp root, never the user's Documents folder
            var state = new WizardState
            {
                Case = draft,
                CaseStore = new CaseStore(Path.Combine(root, "Cases"), Path.Combine(root, "AppData"))
            };
            return RenderHarness.RenderControl(() =>
            {
                var step = new StepCoreDocument();
                // The fixtures have a current SourceHash: entering does not generate or prompt
                step.BindState(state);
                return step;
            }, name, size, control =>
            {
                ((StepCoreDocument)control).OnEnterStepAsync().GetAwaiter().GetResult();
                inspect(control);
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly after the render; the temp folder is harmless
            }
        }
    }

    private static bool NoticeVisible(Control root) =>
        AllControls(root).OfType<Label>().Any(l => l.Text == GuideNotice && l.Visible);

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static void AssertRendered(RenderResult result)
    {
        Assert.True(File.Exists(result.PngPath), result.PngPath);
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
        Assert.Empty(result.Clipping);
    }
}
