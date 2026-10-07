using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// 07-UI-SPEC render checks 7 and 8 (DPI rules 29 and 30): QaFindingsDialog in its three modes at the default and
/// minimum size, and BlindReviewDialog with and without the fallback sentence and with the resume clause.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class QaDialogRenderTests
{
    private static readonly (string Subject, string Text)[] Samples =
    {
        ("RE: Curtain wall water test results, level 4", "Mentions the March 21 revised window schedule eleven days before it was sent"),
        ("Change order 7 pricing", "States the change order amount as $184,500; the case guide has $148,500"),
        ("FW: Site meeting notes", "Refers to the notice of claim, which is dated after this email"),
        ("April payment application", "Attachment pay-app-04.xlsx: retainage is 5%, the case guide says 10%"),
        ("Inspection report request", "Names the wrong project superintendent for the April inspection"),
        ("RE: RE: Window submittal comments", "Says the mockup passed; the key facts record a failed mockup test"),
        ("Schedule recovery plan", "Mentions mediation, which was scheduled two months later"),
        ("Glazing subcontract termination", "Gives the termination date as June 1; the case guide has July 1")
    };

    private static List<QaFindingRow> Rows(QaFindingsMode mode)
    {
        var rows = new List<QaFindingRow>();
        for (var i = 0; i < Samples.Length; i++)
        {
            var blind = mode == QaFindingsMode.BlindReview;
            rows.Add(new QaFindingRow
            {
                ThreadId = $"T-{i:000}",
                DocId = $"RDX-{i * 37 + 104:000000}",
                DateSent = new DateTime(2024, 3, 3).AddDays(i * 9),
                Subject = Samples[i].Subject,
                Col1 = blind ? QaUiText.LabelText(i % 2 == 0) : QaUiText.CheckText((ScanCheck)(i % 4)),
                Col2 = blind ? QaUiText.LabelText(i % 2 != 0) : string.Empty,
                Text = blind
                    ? "The email discusses routine scheduling and does not mention the window leaks or the withheld payment."
                    : Samples[i].Text,
                Relevant = i % 2 == 0,
                RegenerateSubject = Samples[i].Subject,
                EmlCount = 4,
                AttachmentCount = 1
            });
        }
        return rows;
    }

    private static LeakageReport Leakage() => new()
    {
        Verdict = LeakageVerdict.Warning,
        BalancedAccuracy = 0.677,
        Auc = 0.74,
        RelevantEmails = 412,
        NonRelevantEmails = 1_688,
        Signals =
        {
            new() { Feature = "sender r.harmon@harmonbuilders.com", Kind = LeakageSignalKind.Lift, RelevantShare = 0.45, BaseRate = 0.2, Support = 188, Lift = 0.25 },
            new() { Feature = "privilege banner", Kind = LeakageSignalKind.OnlyOnRelevant, RelevantShare = 1, BaseRate = 0.2, Support = 9, Lift = 0.8 },
            new() { Feature = "sent on Sunday", Kind = LeakageSignalKind.Lift, RelevantShare = 0.38, BaseRate = 0.2, Support = 61, Lift = 0.18 },
            new() { Feature = "2 or more CC recipients", Kind = LeakageSignalKind.Lift, RelevantShare = 0.31, BaseRate = 0.2, Support = 402, Lift = 0.11 },
            new() { Feature = "sent after 6 PM", Kind = LeakageSignalKind.Lift, RelevantShare = 0.09, BaseRate = 0.2, Support = 143, Lift = -0.11 },
            new() { Feature = "voicemail attachment", Kind = LeakageSignalKind.NeverOnRelevant, RelevantShare = 0, BaseRate = 0.2, Support = 22, Lift = -0.2 }
        }
    };

    public static IEnumerable<object[]> FindingsCases()
    {
        foreach (var mode in new[] { QaFindingsMode.Scans, QaFindingsMode.BlindReview, QaFindingsMode.Leakage })
        {
            yield return new object[] { mode, false };
            yield return new object[] { mode, true };
        }
    }

    [Theory]
    [MemberData(nameof(FindingsCases))]
    public void QaFindingsDialog_RendersWithoutClipping(QaFindingsMode mode, bool minimum)
    {
        RenderHarness.RunSta(() =>
        {
            var name = $"dialog-qa-findings-{mode.ToString().ToLowerInvariant()}-{(minimum ? "min" : "default")}";
            var expected = Size.Empty;
            var size = Size.Empty;
            var area = Rectangle.Empty;
            var clientArea = Rectangle.Empty;
            var buttonBounds = new List<Rectangle>();
            var buttonTexts = new List<string>();
            var displayedRows = 0;
            var footerShown = false;
            var hScroll = false;
            var hiddenColumns = new List<string>();
            var queuedCell = string.Empty;
            var queuedRows = new HashSet<string>();
            // Blind review: one row already queued before the dialog opened, a second queued in the dialog (footer)
            if (mode == QaFindingsMode.BlindReview)
                queuedRows.Add("T-003");

            var result = RenderHarness.RenderForm(
                () => new QaFindingsDialog(mode, mode == QaFindingsMode.Leakage ? new List<QaFindingRow>() : Rows(mode),
                    mode == QaFindingsMode.Leakage ? Leakage() : null, "claude-haiku-4-5", queuedRows,
                    _ => Task.FromResult(new ThreadInvalidationResult { Appended = true, FilesDeleted = 4 }), _ => { }),
                name,
                form =>
                {
                    var dialog = (QaFindingsDialog)form;
                    expected = minimum
                        ? new Size(form.LogicalToDeviceUnits(QaFindingsDialog.LogicalMinimumSize.Width), form.LogicalToDeviceUnits(QaFindingsDialog.LogicalMinimumSize.Height))
                        : new Size(form.LogicalToDeviceUnits(QaFindingsDialog.LogicalSize.Width), form.LogicalToDeviceUnits(QaFindingsDialog.LogicalSize.Height));
                    area = Screen.FromControl(form).WorkingArea;
                    if (minimum)
                        form.Size = form.MinimumSize;

                    if (mode == QaFindingsMode.BlindReview)
                    {
                        dialog.Confirm = (_, _) => true;
                        var grid = dialog.FindingsGrid;
                        grid.CurrentCell = grid.Rows[0].Cells[grid.Columns.Count - 1];
                        grid.Rows[0].Selected = true;
                        dialog.RegenerateSelectedAsync().GetAwaiter().GetResult();
                        queuedCell = (string)grid.Rows[0].Cells["Col2"].Value;
                        // Keep a not-queued row selected so Regenerate Thread... shows enabled
                        grid.ClearSelection();
                        grid.CurrentCell = grid.Rows[1].Cells[grid.Columns.Count - 1];
                        grid.Rows[1].Selected = true;
                    }

                    form.PerformLayout();
                    Application.DoEvents();
                    size = form.Size;
                    clientArea = form.RectangleToScreen(form.ClientRectangle);
                    var buttons = new[] { dialog.OpenButton, dialog.RegenerateButton, dialog.CloseButton }.Where(b => b.Visible).ToList();
                    buttonBounds = buttons.Select(b => b.RectangleToScreen(b.ClientRectangle)).ToList();
                    buttonTexts = buttons.Select(b => b.Text).ToList();
                    displayedRows = dialog.FindingsGrid.DisplayedRowCount(includePartialRow: false);
                    footerShown = dialog.FooterLabel.Visible;
                    var g = dialog.FindingsGrid;
                    hScroll = g.Controls.OfType<HScrollBar>().Any(s => s.Visible);
                    hiddenColumns = g.Columns.Cast<DataGridViewColumn>()
                        .Where(c => g.GetColumnDisplayRectangle(c.Index, cutOverflow: true).Width < c.Width)
                        .Select(c => c.Name).ToList();
                },
                // Rule 29: a horizontal scrollbar is allowed only at the 640 minimum (below the sum of column minimums)
                new ClippingOptions { AllowGridHorizontalScroll = minimum });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            if (area.Width >= expected.Width && area.Height >= expected.Height)
                Assert.Equal(expected, size);

            // All buttons fully visible on one row
            Assert.Equal(mode == QaFindingsMode.Leakage ? new[] { "Close" } : new[] { "Open Email", "Regenerate Thread...", "Close" }, buttonTexts);
            Assert.All(buttonBounds, b => Assert.True(clientArea.Contains(b), $"Button {b} outside client {clientArea}"));
            Assert.Single(buttonBounds.Select(b => b.Top).Distinct());

            Assert.True(displayedRows >= 4 || mode == QaFindingsMode.Leakage && displayedRows >= Math.Min(4, Leakage().Signals.Count),
                $"Only {displayedRows} grid rows visible");
            if (!minimum)
            {
                Assert.False(hScroll, "Horizontal scrollbar at the default size");
                Assert.Empty(hiddenColumns);
            }

            if (mode == QaFindingsMode.BlindReview)
            {
                Assert.True(footerShown, "Footer status not shown after queuing");
                Assert.EndsWith(QaUiText.QueuedSuffix, queuedCell);
            }
        });
    }

    [Theory]
    [InlineData(false, 0, 6_000, 1_200, 2_400, "dialog-blind-review")]
    [InlineData(true, 0, 6_000, 1_200, 2_400, "dialog-blind-review-fallback")]
    [InlineData(true, 4_210, 250_000, 61_728, 123_456, "dialog-blind-review-resume")]
    public void BlindReviewDialog_RendersWithoutClipping(bool fallback, int reviewed, int total, int relevant, int size,
        string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var clientArea = Rectangle.Empty;
            var inputBounds = Rectangle.Empty;
            var inputText = string.Empty;
            var textFits = false;
            var estimate = string.Empty;
            var primary = string.Empty;
            var result = RenderHarness.RenderForm(
                () => new BlindReviewDialog("claude-haiku-4-5", "Anthropic", fallback, total, relevant, size, reviewed,
                    n => n * 0.0021m),
                name,
                form =>
                {
                    var dialog = (BlindReviewDialog)form;
                    height = form.ClientSize.Height;
                    workingHeight = Screen.FromControl(form).WorkingArea.Height;
                    clientArea = form.RectangleToScreen(form.ClientRectangle);
                    var input = dialog.SampleInput;
                    inputBounds = input.RectangleToScreen(input.ClientRectangle);
                    inputText = input.Text;
                    // Rule 30: six digits with separators fit in the edit part (the width minus the spin buttons)
                    var textWidth = TextRenderer.MeasureText(input.Text, input.Font).Width;
                    textFits = textWidth <= input.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                    estimate = dialog.EstimateLabel.Text;
                    primary = Descendants(form).OfType<ThemedButton>().Single(b => b.Variant == ButtonStyle.Primary).Text;
                });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            Assert.True(height > 0 && height < workingHeight, $"Client height {height} vs working area {workingHeight}");
            Assert.True(clientArea.Contains(inputBounds), $"Sample input {inputBounds} outside client {clientArea}");
            Assert.Equal(size.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), inputText);
            Assert.True(textFits, $"'{inputText}' does not fit the sample input");
            Assert.Equal("Start Review", primary);
            if (reviewed > 0)
                Assert.Contains("already reviewed are kept", estimate);
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
}
