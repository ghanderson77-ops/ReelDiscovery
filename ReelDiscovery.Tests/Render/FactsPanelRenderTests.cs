using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Key Facts tab at 144 DPI: a fact to review with its notice, each link picker in view,
/// the Needs review filter and an empty timeline.
/// </summary>
[Trait("Category", "Render")]
public class FactsPanelRenderTests
{
    private const string ReviewReason = "Maria Delgado was changed after it was written.";

    [Fact]
    public void Facts_NeedsReview_Top_Large()
    {
        RenderHarness.RunSta(() =>
        {
            string? selected = null;
            var noticeShown = false;
            var notesShown = false;
            var dates = new List<string>();
            var result = RenderFacts("facts-needs-review-top", RenderHarness.Large, ReviewDraft(), (panel, host) =>
            {
                panel.SelectEntity("F-004");
                selected = SelectedId(panel);
                noticeShown = AllControls(panel).OfType<Label>().Any(l => l.Visible && l.Text == "Review this fact: " + ReviewReason);
                dates.AddRange(FindGrid(panel).Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Date"].Value!));
                notesShown = AllControls(panel).OfType<Label>().Any(l => l.Text == "Notes from generation");
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Equal("F-004", selected);
            Assert.True(noticeShown);
            Assert.True(notesShown);
            Assert.Equal(dates.OrderBy(d => d, StringComparer.Ordinal), dates);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Facts_NeedsReview_Top_Small()
    {
        RenderHarness.RunSta(() =>
        {
            var result = RenderFacts("facts-needs-review-top", RenderHarness.Small, ReviewDraft(), (panel, host) =>
            {
                panel.SelectEntity("F-004");
            }, new ClippingOptions { AllowGridHorizontalScroll = true });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Empty(result.Clipping);
        });
    }

    [Theory]
    [InlineData("issues", "Issues")]
    [InlineData("actors", "Who did it")]
    [InlineData("knowers", "Who knew")]
    public void Facts_Pickers(string name, string caption)
    {
        RenderHarness.RunSta(() =>
        {
            var items = new List<string>();
            var checkedIds = new List<string>();
            var listHeight = 0;
            var itemHeight = 0;
            var result = RenderFacts($"facts-pickers-{name}", RenderHarness.Large, ReviewDraft(), (panel, host) =>
            {
                panel.SelectEntity("F-004");
                var pickers = AllControls(panel).OfType<LinkPicker>().ToList();
                Assert.Equal(3, pickers.Count);
                var picker = pickers[caption == "Issues" ? 0 : caption == "Who did it" ? 1 : 2];
                var list = AllControls(picker).OfType<CheckedListBox>().Single();
                items.AddRange(list.Items.Cast<string>());
                checkedIds.AddRange(picker.CheckedIds);
                listHeight = list.Height;
                itemHeight = list.ItemHeight;
                var scroller = AllControls(panel).OfType<Panel>().First(p => p.AutoScroll);
                scroller.ScrollControlIntoView(picker);
            });

            // At most 6 rows tall
            Assert.True(listHeight <= 6 * itemHeight + 2, $"{listHeight} > 6 rows of {itemHeight}");
            if (name == "knowers")
            {
                // F-004: CH-002 did it and CH-004 also knew; the people who did it come first, locked, with the suffix
                Assert.StartsWith("CH-002  Tom Becker (did it) — ", items[0]);
                Assert.Equal(new[] { "CH-002", "CH-004" }, checkedIds.OrderBy(i => i));
            }
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Facts_FilterNeedsReview()
    {
        RenderHarness.RunSta(() =>
        {
            var ids = new List<string>();
            string? selected = null;
            var result = RenderFacts("facts-filter-needs-review", RenderHarness.Large, ReviewDraft(), (panel, host) =>
            {
                panel.ShowNeedsReview();
                ids.AddRange(FindGrid(panel).Rows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!));
                selected = SelectedId(panel);
            });

            Assert.Equal(new[] { "F-004" }, ids);
            Assert.Equal("F-004", selected);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Facts_Empty()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            draft.Guide!.Facts.Clear();
            foreach (var value in draft.Guide.Values)
                value.FactIds.Clear();
            var emptyText = false;
            var result = RenderFacts("facts-empty", RenderHarness.Large, draft, (panel, host) =>
            {
                emptyText = AllControls(panel).OfType<Label>().Any(l => l.Visible && l.Text == "No key facts. Click Add Fact or Regenerate Facts...");
            });

            Assert.True(emptyText);
            Assert.Empty(result.Clipping);
        });
    }

    /// <summary>
    /// The sample guide with F-004 flagged for review after a cast edit, and two mapping notes on it.
    /// </summary>
    private static CaseDraft ReviewDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var fact = draft.Guide!.FindFact("F-004")!;
        fact.NeedsReview = true;
        fact.ReviewReason = ReviewReason;
        fact.MappingNotes.Add("\"Tom B.\" was matched to CH-002 Tom Becker.");
        fact.MappingNotes.Add("\"the site super\" did not match a character and was dropped.");
        return draft;
    }

    private static RenderResult RenderFacts(string name, Size size, CaseDraft draft, Action<FactsTabPanel, RenderGuideHost>? arrange = null,
        ClippingOptions? options = null)
    {
        return RenderHarness.RenderControl(() => new FactsTabPanel(), name, size, control =>
        {
            var panel = (FactsTabPanel)control;
            var host = new RenderGuideHost(draft);
            panel.Attach(host);
            panel.RefreshFromDraft();
            arrange?.Invoke(panel, host);
        }, options);
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
