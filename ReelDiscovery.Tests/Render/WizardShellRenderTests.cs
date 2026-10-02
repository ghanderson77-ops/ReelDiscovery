using ReelDiscovery.Forms;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the whole wizard window on the 5-step case flow at the active scaling (W5): header text,
/// navigation button text, default window size and clipping for the last three steps.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class WizardShellRenderTests
{
    [Fact]
    public void Shell_CaseGuideStep()
    {
        RenderStep(3, "wizard-shell-case-guide", "Step 4 of 5: Case Guide", "Next >");
    }

    [Fact]
    public void Shell_CaseSummaryStep()
    {
        RenderStep(4, "wizard-shell-case-summary", "Step 5 of 5: Case Summary", "Finish");
    }

    [Fact]
    public void Shell_CoreDocumentStep()
    {
        RenderStep(2, "wizard-shell-core-document", "Step 3 of 5: Review Core Document", "Next >");
    }

    private static void RenderStep(int index, string name, string expectedTitle, string expectedNext)
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new ShellFixture();
            var title = string.Empty;
            var next = string.Empty;
            var nextHeight = 0;
            var expectedHeight = 0;
            var client = Size.Empty;
            var expectedClient = Size.Empty;
            var area = Rectangle.Empty;
            var checksFolded = false;

            var result = RenderHarness.RenderForm(() => WizardForm.CreateForRender(fixture.State), name, arrange: form =>
            {
                ((WizardForm)form).ShowStepForRender(index);

                // ShowStep is async (entering a step saves the case): pump until the header shows the new step
                var titleLabel = Find<Label>(form, "lblStepTitle");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (titleLabel.Text != expectedTitle && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }

                title = titleLabel.Text;
                var nextButton = Find<Button>(form, "btnNext");
                next = nextButton.Text;
                nextHeight = nextButton.Height;
                expectedHeight = form.LogicalToDeviceUnits(34);
                client = form.ClientSize;
                expectedClient = new Size(form.LogicalToDeviceUnits(WizardForm.PreferredClientSize.Width),
                    form.LogicalToDeviceUnits(WizardForm.PreferredClientSize.Height));
                checksFolded = Controls(form).OfType<Label>().Any(l => l.Visible && l.Text == "Make the window taller to see the list.");
                area = Screen.FromControl(form).WorkingArea;
            });

            Assert.True(File.Exists(result.PngPath), result.PngPath);
            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Equal(expectedTitle, title);
            Assert.Equal(expectedNext, next);
            Assert.Equal(expectedHeight, nextHeight);

            // The window opens at the preferred logical client size unless the screen's working area is smaller;
            // at that size the Case Guide shows its Checks list instead of folding it
            if (area.Width > expectedClient.Width + 40 && area.Height > expectedClient.Height + 80)
            {
                Assert.Equal(expectedClient, client);
                Assert.False(checksFolded, "The Checks list folded at the default window size");
            }

            Assert.Empty(result.Clipping);
        });
    }

    private static IEnumerable<Control> Controls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Controls(child))
                yield return nested;
        }
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.Controls.Find(name, searchAllChildren: true).OfType<T>().Single();

    /// <summary>
    /// A saved guided case under a temp root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class ShellFixture : IDisposable
    {
        private readonly string _root;

        public ShellFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-shell-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            var draft = CaseTestData.SampleGuidedDraft();
            draft.AdvanceStage(CaseStage.Summary);
            store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            State = new WizardState { Case = draft, CaseStore = store };
        }

        public WizardState State { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly after the render; the temp folder is harmless
            }
        }
    }
}
