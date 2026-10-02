using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Case Summary step at 144 DPI with a case folder under a temp root (UI-SPEC S2).
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CaseSummaryRenderTests
{
    [Fact]
    public void Summary_BeforeExport()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());
                var result = Render(fixture, "case-summary-before-export", size);

                AssertRendered(result);
            }
        });
    }

    [Fact]
    public void Summary_AfterExport()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());
            var folder = fixture.State.Case.FolderPath;
            foreach (var kind in new[] { CaseExportKind.CoreDocument, CaseExportKind.Cast, CaseExportKind.KeyFacts })
            {
                var path = Path.Combine(folder, CaseGuideDocxExporter.FileNameFor(kind, Path.GetFileName(folder)));
                File.WriteAllBytes(path, CaseGuideDocxExporter.Build(fixture.State.Case, kind));
            }

            var openButtons = 0;
            var exportLines = 0;
            var result = Render(fixture, "case-summary-after-export", RenderHarness.Large, control =>
            {
                openButtons = AllControls(control).OfType<Button>().Count(b => b.Text == "Open in Word" && b.Visible);
                exportLines = AllControls(control).OfType<Label>().Count(l => l.Text.StartsWith("Exported ", StringComparison.Ordinal) && l.Text.EndsWith(".docx", StringComparison.Ordinal));
            });

            AssertRendered(result);
            Assert.Equal(3, openButtons);
            Assert.Equal(3, exportLines);
        });
    }

    [Fact]
    public void Summary_WithErrors()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            // Orphan link: a fact points to an issue that does not exist
            draft.Guide!.Facts[0].IssueIds.Add("ISS-99");
            using var fixture = new SummaryFixture(draft);

            Color? checksColor = null;
            var result = Render(fixture, "case-summary-with-errors", RenderHarness.Large, control =>
            {
                var line = AllControls(control).OfType<Label>()
                    .FirstOrDefault(l => l.Text.Contains("error", StringComparison.Ordinal) && l.Text.Contains("warning", StringComparison.Ordinal));
                checksColor = line?.ForeColor;
            });

            AssertRendered(result);
            Assert.Equal(Helpers.Theme.Danger, checksColor);
        });
    }

    private static RenderResult Render(SummaryFixture fixture, string name, Size size, Action<Control>? inspect = null)
    {
        return RenderHarness.RenderControl(() =>
        {
            var step = new StepCaseSummary();
            step.LoadForRender(fixture.State);
            return step;
        }, name, size, control => inspect?.Invoke(control));
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

    private static void AssertRendered(RenderResult result)
    {
        Assert.True(File.Exists(result.PngPath), result.PngPath);
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
        Assert.Empty(result.Clipping);
    }

    /// <summary>
    /// A saved case under a temp Cases root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class SummaryFixture : IDisposable
    {
        private readonly string _root;

        public SummaryFixture(CaseDraft draft)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-summary-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
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
