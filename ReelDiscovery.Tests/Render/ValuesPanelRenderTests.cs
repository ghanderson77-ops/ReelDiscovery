using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Canonical Values tab at the active scaling: the first value selected (two sizes), a label
/// conflict with mapping notes, a value to review with its notice, and an empty registry.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class ValuesPanelRenderTests
{
    private const string ReviewReason = "F-003 was changed after it was written.";
    private const string Hint = "Every document and email must use this exact text.";

    [Fact]
    public void Values_FirstSelected_Large()
    {
        RenderHarness.RunSta(() =>
        {
            string? selected = null;
            var hintShown = false;
            var kinds = new List<string>();
            var count = string.Empty;
            var result = RenderValues("values-first-selected", RenderHarness.Large, CaseTestData.SampleGuidedDraft(), (panel, host) =>
            {
                panel.SelectEntity("V-001");
                selected = SelectedId(panel);
                hintShown = AllControls(panel).OfType<Label>().Any(l => l.Visible && l.Text == Hint
                    && l.ForeColor == Theme.TextMuted);
                kinds.AddRange(FindGrid(panel).Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Kind"].Value!));
                count = AllControls(panel).OfType<Label>().First(l => l.Text.EndsWith(" values", StringComparison.Ordinal) && char.IsDigit(l.Text[0])).Text;
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Equal("V-001", selected);
            Assert.True(hintShown);
            Assert.Contains("Change order number", kinds);
            Assert.Equal("4 values", count);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Values_FirstSelected_Small()
    {
        RenderHarness.RunSta(() =>
        {
            var result = RenderValues("values-first-selected", RenderHarness.Small, CaseTestData.SampleGuidedDraft(), (panel, host) =>
            {
                panel.SelectEntity("V-001");
            }, new ClippingOptions { AllowGridHorizontalScroll = true });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Values_WithFindings()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            var v1 = draft.Guide!.FindValue("V-001")!;
            var v2 = draft.Guide.FindValue("V-002")!;
            // Same label, different value: a validator Error (T-03-50)
            v2.Label = v1.Label;
            v2.MappingNotes.Add("\"the CO-7 email\" did not match a fact and was dropped.");
            var hasError = false;
            var notesShown = false;
            Color dot = Color.Empty;
            var result = RenderValues("values-with-findings", RenderHarness.Large, draft, (panel, host) =>
            {
                hasError = host.Findings.Any(f => f.EntityId == "V-002" && f.Severity == IssueSeverity.Error);
                panel.SelectEntity("V-002");
                notesShown = AllControls(panel).OfType<Label>().Any(l => l.Text == "Notes from generation");
                dot = DotColorOf(panel, "V-002");
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(hasError);
            Assert.True(notesShown);
            Assert.Equal(Theme.Danger, dot);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Values_NeedsReview()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            var value = draft.Guide!.FindValue("V-001")!;
            value.NeedsReview = true;
            value.ReviewReason = ReviewReason;
            var noticeShown = false;
            Color dot = Color.Empty;
            var result = RenderValues("values-needs-review", RenderHarness.Large, draft, (panel, host) =>
            {
                panel.SelectEntity("V-001");
                noticeShown = AllControls(panel).OfType<Label>().Any(l => l.Visible && l.Text == "Review this value: " + ReviewReason);
                dot = DotColorOf(panel, "V-001");
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(noticeShown);
            Assert.Equal(Theme.Warning, dot);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Values_Empty()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            draft.Guide!.Values.Clear();
            var emptyText = false;
            var result = RenderValues("values-empty", RenderHarness.Large, draft, (panel, host) =>
            {
                emptyText = AllControls(panel).OfType<Label>().Any(l => l.Visible && l.Text == "No canonical values. Click Add Value or Regenerate Values...");
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(emptyText);
            Assert.Empty(result.Clipping);
        });
    }

    private static RenderResult RenderValues(string name, Size size, CaseDraft draft, Action<ValuesTabPanel, RenderGuideHost>? arrange = null,
        ClippingOptions? options = null)
    {
        return RenderHarness.RenderControl(() => new ValuesTabPanel(), name, size, control =>
        {
            var panel = (ValuesTabPanel)control;
            var host = new RenderGuideHost(draft);
            panel.Attach(host);
            panel.RefreshFromDraft();
            arrange?.Invoke(panel, host);
        }, options);
    }

    /// <summary>
    /// The protected DotColor of a row, read through reflection (what the dot painter draws).
    /// </summary>
    private static Color DotColorOf(ValuesTabPanel panel, string id)
    {
        var method = typeof(ValuesTabPanel).GetMethod("DotColor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (Color)method.Invoke(panel, new object[] { id })!;
    }

    private static string? SelectedId(Control panel)
    {
        var grid = FindGrid(panel);
        return grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as string : null;
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

    private static DataGridView FindGrid(Control root) => AllControls(root).OfType<DataGridView>().First();
}
