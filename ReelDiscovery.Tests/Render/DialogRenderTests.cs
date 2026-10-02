using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Case Guide dialogs (D1 RegenerateTabDialog, D2 DeleteReferencesDialog) at 144 DPI and checks
/// they are content-sized, unclipped and fit on the screen.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
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
    [InlineData(12, 3, "dialog-regenerate-selected-edited")]
    [InlineData(35, 0, "dialog-regenerate-selected")]
    public void RegenerateSelectedDialog_RendersWithoutClipping(int selected, int edited, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var checkbox = string.Empty;
            var result = RenderHarness.RenderForm(() => new RegenerateSelectedDialog(selected, edited), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                checkbox = Descendants(form).OfType<CheckBox>().SingleOrDefault()?.Text ?? string.Empty;
                Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == $"Regenerate {selected} selected facts");
                Assert.Contains(Descendants(form).OfType<Button>(), b => b.Text == "Regenerate Selected");
            });

            AssertRendered(result, height, workingHeight);
            Assert.Equal(edited > 0 ? $"Also replace my {edited} edited facts" : string.Empty, checkbox);
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
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

    [Theory]
    [InlineData(true, "dialog-guide-errors-many")]
    [InlineData(false, "dialog-guide-errors-one")]
    public void GuideErrorsDialog_RendersWithoutClipping(bool many, string name)
    {
        RenderHarness.RunSta(() =>
        {
            // A real broken guide: the errors and hints come from the validator
            var draft = CaseTestData.SampleGuidedDraft();
            var guide = draft.Guide!;
            guide.FindFact("F-006")!.Date = "2024-10-02";
            if (many)
            {
                guide.FindCast("CH-002")!.ActiveTo = "2024-03-01";
                guide.FindFact("F-002")!.ActorIds.Add("CH-099");
                guide.FindFact("F-005")!.Title = "";
                guide.FindValue("V-001")!.FactIds.Add("F-099");
                guide.FindCast("CH-007")!.Email = "evan at ellis";
            }
            var errors = CaseGuideValidator.Validate(draft).Where(f => f.Severity == IssueSeverity.Error).ToList();
            var content = CaseGuideUiText.ErrorsGateText(errors);

            var height = 0;
            var workingHeight = 0;
            var buttons = new List<(string Text, bool Primary)>();
            var cancel = string.Empty;
            var lines = 0;
            var result = RenderHarness.RenderForm(() => new GuideErrorsDialog(content), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                buttons.AddRange(Descendants(form).OfType<ThemedButton>()
                    .OrderBy(b => b.PointToScreen(Point.Empty).X)
                    .Select(b => (b.Text, b.Variant == ButtonStyle.Primary)));
                cancel = (form.CancelButton as Button)?.Text ?? string.Empty;
                lines = Descendants(form).OfType<Label>().Count(l => content.Lines.Any(e => e.FixHint == l.Text));
            });

            AssertRendered(result, height, workingHeight);
            Assert.Equal("Show Checks", cancel);
            Assert.Equal(content.Lines.Count, lines);
            if (many)
            {
                Assert.True(errors.Count > 5);
                Assert.Equal(new[] { ("Show Checks", false), ("Continue Anyway", false), (content.FixAutomaticallyText, true) }, buttons);
            }
            else
            {
                Assert.Single(errors);
                Assert.Equal(new[] { ("Show Checks", false), ("Continue Anyway", false) }, buttons);
            }
        });
    }

    [Fact]
    public void FixAllDialog_RendersWithoutClipping()
    {
        RenderHarness.RunSta(() =>
        {
            var content = CaseGuideUiText.FixAllText(new List<(GuideQuickFix, int)>
            {
                (GuideQuickFix.RemoveBrokenLinks, 2),
                (GuideQuickFix.WidenActiveRange, 3),
                (GuideQuickFix.ResetEmail, 1),
                (GuideQuickFix.MarkReviewed, 4)
            });
            var height = 0;
            var workingHeight = 0;
            var cancel = string.Empty;
            var primary = string.Empty;
            var result = RenderHarness.RenderForm(() => new FixAllDialog(content), "dialog-fix-all", form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                cancel = (form.CancelButton as Button)?.Text ?? string.Empty;
                primary = Descendants(form).OfType<ThemedButton>().Single(b => b.Variant == ButtonStyle.Primary).Text;
                Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == "Fix 10 checks automatically?");
                Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == "Widen or repair 3 active ranges");
            });

            AssertRendered(result, height, workingHeight);
            Assert.Equal("Cancel", cancel);
            Assert.Equal("Fix All", primary);
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
