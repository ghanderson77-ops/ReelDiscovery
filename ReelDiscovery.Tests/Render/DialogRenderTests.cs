using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
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

    // Render state 8 (05-UI-SPEC S2): D-RP with the generated-threads sentence
    [Theory]
    [InlineData(true, 12, "replan-dialog-generated")]
    [InlineData(false, 1, "replan-dialog-generated-unapproved")]
    public void ReplanDialog_WithGeneratedThreads_RendersWithoutClipping(bool approved, int generated, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var bodyShown = false;
            var narrowButtons = new List<string>();
            var result = RenderHarness.RenderForm(() => new ReplanDialog(approved, generated), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                bodyShown = Descendants(form).OfType<Label>().Any(l => l.Text == CorpusUiText.ReplanBody(approved, generated));
                // No ellipsized button text: every button is at least as wide as its caption needs
                narrowButtons = Descendants(form).OfType<Button>().Where(b => b.Width < b.PreferredSize.Width).Select(b => b.Text).ToList();
            });

            AssertRendered(result, height, workingHeight);
            Assert.True(bodyShown);
            Assert.Empty(narrowButtons);
        });
    }

    // 10 D-08: D-RP with the dropped-relabels sentence (render PNGs on the dev machine are 120 DPI; check 150% by hand)
    [Theory]
    [InlineData(true, 12, 3, "replan-dialog-relabels")]
    [InlineData(false, 0, 1, "replan-dialog-relabels-unapproved")]
    public void ReplanDialog_WithRelabels_RendersWithoutClipping(bool approved, int generated, int relabels, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var bodyShown = false;
            var narrowButtons = new List<string>();
            var result = RenderHarness.RenderForm(() => new ReplanDialog(approved, generated, relabels), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                bodyShown = Descendants(form).OfType<Label>().Any(l => l.Text == CorpusUiText.ReplanBody(approved, generated, relabels));
                narrowButtons = Descendants(form).OfType<Button>().Where(b => b.Width < b.PreferredSize.Width).Select(b => b.Text).ToList();
            });

            AssertRendered(result, height, workingHeight);
            Assert.True(bodyShown);
            Assert.Empty(narrowButtons);
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

    // Render state 8 (08-UI-SPEC): a deposition with every optional line, and a written set without the warning
    [Theory]
    [InlineData(true, "dialog-regenerate-deposition")]
    [InlineData(false, "dialog-regenerate-written")]
    public void RegenerateDiscoveryDialog_RendersWithoutClipping(bool deposition, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var focused = false;
            var warning = false;
            var buttons = new List<(string Text, int Top)>();
            var result = RenderHarness.RenderForm(() => deposition
                ? new RegenerateDiscoveryDialog("Deposition of Michael Scott",
                    "Brightline Windows v. Harmon Construction - Deposition of Michael Scott (Excerpt).docx", true,
                    DiscoveryUiText.SettingsLineDeposition(25, "2025-06-10"), fileOpen: true, 0.42m)
                : new RegenerateDiscoveryDialog("Requests for Production (25)",
                    "Brightline Windows v. Harmon Construction - Requests for Production.docx", false,
                    DiscoveryUiText.SettingsLineWritten(25, "Brightline Windows LLC", "Harmon Construction Group, Inc.", memo: false),
                    fileOpen: false, 0.18m), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                var dialog = (RegenerateDiscoveryDialog)form;
                focused = form.ActiveControl == dialog.GuidanceBox;
                warning = dialog.WarningShown;
                buttons = Descendants(form).OfType<Button>().OrderBy(b => b.Left).Select(b => (b.Text, b.Top)).ToList();
                Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == DiscoveryUiText.RegenerateHeadline(
                    deposition ? "Deposition of Michael Scott" : "Requests for Production (25)"));
                Assert.All(Descendants(form).OfType<Label>(), l => Assert.False(l.UseMnemonic));
            });

            AssertRendered(result, height, workingHeight);
            Assert.True(focused);
            Assert.Equal(deposition, warning);
            Assert.Equal(new[] { "Cancel", "Regenerate Document" }, buttons.Select(b => b.Text).ToArray());
            // Buttons read left to right and sit on one row
            Assert.Single(buttons.Select(b => b.Top).Distinct());
        });
    }

    [Fact]
    public void RegenerateDiscoveryDialog_TruncatesGuidance()
    {
        RenderHarness.RunSta(() =>
        {
            using var dialog = new RegenerateDiscoveryDialog("Interrogatories (18)", "a.docx", false, "", false, 0m);
            Assert.Equal(RegenerateDiscoveryDialog.MaxGuidanceLength, dialog.GuidanceBox.MaxLength);
            // Text set in code bypasses MaxLength; the property still cuts it
            dialog.GuidanceBox.Text = "  " + new string('x', 1_500) + "  ";
            Assert.Equal(1_000, dialog.Guidance.Length);
            dialog.GuidanceBox.Text = "  focus on the payment dispute  ";
            Assert.Equal("focus on the payment dispute", dialog.Guidance);
        });
    }

    [Fact]
    public void RegenerateTabDialog_RejectsIssues()
    {
        RenderHarness.RunSta(() =>
            Assert.Throws<ArgumentException>(() => new RegenerateTabDialog(GuidePart.Issues, 0)));
    }

    // Render check 9 (06-UI-SPEC S4, DPI rule 25): media prices row at the dialog's 820 x 360 minimum
    [Fact]
    public void ModelDialog_MediaPricesRow_MinimumSize()
    {
        RenderHarness.RunSta(() =>
        {
            var clientArea = Rectangle.Empty;
            var imageBounds = Rectangle.Empty;
            var ttsBounds = Rectangle.Empty;
            var labelBounds = new List<Rectangle>();
            var gridLeft = 0;
            var rowLeft = 0;
            var displayedRows = 0;
            var size = Size.Empty;
            var minimum = Size.Empty;
            var prices = new MediaPrices { ImagePerUnit = 0.006m, TtsPerMillionChars = 15.00m };
            var result = RenderHarness.RenderForm(
                () => new ModelConfigurationDialog(AIModelConfig.GetDefaultModels(), prices),
                "dialog-model-config-media-prices-min",
                form =>
                {
                    var dialog = (ModelConfigurationDialog)form;
                    form.Size = form.MinimumSize;
                    form.PerformLayout();
                    Application.DoEvents();

                    size = form.Size;
                    minimum = form.MinimumSize;
                    clientArea = form.RectangleToScreen(form.ClientRectangle);
                    imageBounds = dialog.ImagePriceInput.RectangleToScreen(dialog.ImagePriceInput.ClientRectangle);
                    ttsBounds = dialog.TtsPriceInput.RectangleToScreen(dialog.TtsPriceInput.ClientRectangle);
                    var mediaRow = dialog.ImagePriceInput.Parent!;
                    labelBounds = mediaRow.Controls.OfType<Label>()
                        .Select(l => l.RectangleToScreen(l.ClientRectangle)).ToList();
                    // Outer edges: the grid's 3D border sits inside its bounds
                    rowLeft = mediaRow.Parent!.RectangleToScreen(mediaRow.Bounds).Left;
                    gridLeft = dialog.ModelsGrid.Parent!.RectangleToScreen(dialog.ModelsGrid.Bounds).Left;
                    displayedRows = dialog.ModelsGrid.DisplayedRowCount(includePartialRow: false);
                    Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == "Image ($ per image):");
                    Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == "Voicemail audio ($ per 1M characters):");
                },
                // Eight fixed-width columns scroll sideways at the 820 px minimum by design; everything else must fit
                new ClippingOptions { AllowGridHorizontalScroll = true });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            Assert.Equal(minimum, size);
            Assert.True(clientArea.Contains(imageBounds), $"Image price {imageBounds} outside client {clientArea}");
            Assert.True(clientArea.Contains(ttsBounds), $"Voicemail price {ttsBounds} outside client {clientArea}");
            Assert.Equal(2, labelBounds.Count);
            Assert.All(labelBounds, b => Assert.True(clientArea.Contains(b), $"Label {b} outside client {clientArea}"));
            Assert.True(displayedRows >= 3, $"Only {displayedRows} grid rows visible");
            Assert.True(Math.Abs(rowLeft - gridLeft) <= 1, $"Media row left {rowLeft} vs grid left {gridLeft}");
        });
    }

    // 07-UI-SPEC S5 (D-18, D-04): 11 columns at the 1180 x 480 default size, no horizontal scrollbar
    [Fact]
    public void ModelDialog_CacheColumns_DefaultSize()
    {
        RenderHarness.RunSta(() =>
        {
            var hiddenColumns = new List<string>();
            var hScrollVisible = true;
            var clientArea = Rectangle.Empty;
            var ttsBounds = Rectangle.Empty;
            var headerHeight = 0;
            var rowHeight = 0;
            var expected = Size.Empty;
            var size = Size.Empty;
            var area = Rectangle.Empty;
            var result = RenderHarness.RenderForm(
                () => new ModelConfigurationDialog(AIModelConfig.GetDefaultModels(), MediaPrices.Defaults()),
                "dialog-model-config-cache-default",
                form =>
                {
                    var dialog = (ModelConfigurationDialog)form;
                    var grid = dialog.ModelsGrid;
                    expected = new Size(form.LogicalToDeviceUnits(ModelConfigurationDialog.LogicalSize.Width),
                        form.LogicalToDeviceUnits(ModelConfigurationDialog.LogicalSize.Height));
                    size = form.Size;
                    area = Screen.FromControl(form).WorkingArea;
                    hiddenColumns = grid.Columns.Cast<DataGridViewColumn>()
                        .Where(c => grid.GetColumnDisplayRectangle(c.Index, cutOverflow: false).Width < c.Width
                            || grid.GetColumnDisplayRectangle(c.Index, cutOverflow: true).Width < c.Width)
                        .Select(c => c.Name).ToList();
                    hScrollVisible = grid.Controls.OfType<HScrollBar>().Any(s => s.Visible);
                    clientArea = form.RectangleToScreen(form.ClientRectangle);
                    ttsBounds = dialog.TtsPriceInput.RectangleToScreen(dialog.TtsPriceInput.ClientRectangle);
                    headerHeight = grid.ColumnHeadersHeight;
                    rowHeight = grid.RowTemplate.Height;
                    Assert.Contains(Descendants(form).OfType<Label>(),
                        l => l.Text.EndsWith("the batch discount applies when batch pricing is on.", StringComparison.Ordinal));
                });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            if (area.Width >= expected.Width && area.Height >= expected.Height)
                Assert.Equal(expected, size);
            Assert.Empty(hiddenColumns);
            Assert.False(hScrollVisible, "Horizontal scrollbar shown at the default size");
            Assert.True(clientArea.Contains(ttsBounds), $"Voicemail price {ttsBounds} outside client {clientArea}");
            // Wrapped two-line headers are taller than a single row
            Assert.True(headerHeight > rowHeight, $"Header height {headerHeight} vs row {rowHeight}");
        });
    }

    // 07-UI-SPEC S5: at the 820 x 360 minimum Provider and Model ID stay frozen and at least 3 rows show
    [Fact]
    public void ModelDialog_CacheColumns_MinimumSize()
    {
        RenderHarness.RunSta(() =>
        {
            var frozen = new List<string>();
            var displayedRows = 0;
            var firstScrolled = -1;
            var providerShown = false;
            var modelIdShown = false;
            var result = RenderHarness.RenderForm(
                () => new ModelConfigurationDialog(AIModelConfig.GetDefaultModels(), MediaPrices.Defaults()),
                "dialog-model-config-cache-min",
                form =>
                {
                    var dialog = (ModelConfigurationDialog)form;
                    var grid = dialog.ModelsGrid;
                    form.Size = form.MinimumSize;
                    form.PerformLayout();
                    Application.DoEvents();

                    // Scroll to the last column: frozen columns must stay in view
                    grid.FirstDisplayedScrollingColumnIndex = grid.Columns["MaxOutputTokens"]!.Index;
                    Application.DoEvents();
                    firstScrolled = grid.FirstDisplayedScrollingColumnIndex;
                    frozen = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Frozen).Select(c => c.Name).ToList();
                    providerShown = grid.Columns["Provider"]!.Displayed;
                    modelIdShown = grid.Columns["ModelId"]!.Displayed;
                    displayedRows = grid.DisplayedRowCount(includePartialRow: false);
                },
                new ClippingOptions { AllowGridHorizontalScroll = true });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            Assert.Equal(new[] { "Provider", "ModelId" }, frozen);
            Assert.True(firstScrolled > 1, $"First scrolled column {firstScrolled}");
            Assert.True(providerShown && modelIdShown, "Frozen columns scrolled out of view");
            Assert.True(displayedRows >= 3, $"Only {displayedRows} grid rows visible");
        });
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
