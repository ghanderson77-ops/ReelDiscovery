using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Cast tab at 144 DPI: key and background details, the Background filter, a field error and a run in progress.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CastPanelRenderTests
{
    [Fact]
    public void Cast_KeySelected_Large()
    {
        RenderHarness.RunSta(() =>
        {
            string? selected = null;
            var result = RenderCast("cast-key-selected", RenderHarness.Large, null, (panel, host) =>
            {
                panel.SelectEntity("CH-006");
                selected = SelectedId(panel);
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Equal("CH-006", selected);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Cast_KeySelected_Small()
    {
        RenderHarness.RunSta(() =>
        {
            string? signature = null;
            var result = RenderCast("cast-key-selected", RenderHarness.Small, null, (panel, host) =>
            {
                panel.SelectEntity("CH-006");
                // Scrolled to the end so this render shows the lower fields (signature, linked facts); the Large one shows the top
                var labels = AllControls(panel).OfType<Label>().ToList();
                var captionIndex = labels.FindIndex(l => l.Text == "Signature");
                signature = labels[captionIndex + 1].Text;
                var scroller = AllControls(panel).OfType<Panel>().First(p => p.AutoScroll);
                scroller.ScrollControlIntoView(labels.Last(l => l.Text.StartsWith("Appears in") || l.Text.StartsWith("Not in")));
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Contains(Environment.NewLine, signature);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Cast_BackgroundSelected()
    {
        RenderHarness.RunSta(() =>
        {
            var captions = new List<string>();
            var result = RenderCast("cast-background-selected", RenderHarness.Large, null, (panel, host) =>
            {
                panel.SelectEntity("CH-008");
                captions.AddRange(AllControls(panel).OfType<Label>().Select(l => l.Text));
            });

            Assert.Contains("Role at the organization", captions);
            Assert.DoesNotContain("Writing voice", captions);
            Assert.DoesNotContain("Signature", captions);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Cast_FilterBackground()
    {
        RenderHarness.RunSta(() =>
        {
            var ids = new List<string>();
            var result = RenderCast("cast-filter-background", RenderHarness.Large, null, (panel, host) =>
            {
                AllControls(panel).OfType<ThemedButton>().Single(b => b.Text == "Background").PerformClick();
                ids.AddRange(FindGrid(panel).Rows.Cast<DataGridViewRow>().Select(r => (string)r.Tag!));
            });

            Assert.Equal(new[] { "CH-008", "CH-009" }, ids);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Cast_FieldError()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            var stored = draft.Guide!.FindCast("CH-002")!.ActiveFrom;
            var errorShown = false;
            var result = RenderCast("cast-field-error", RenderHarness.Large, draft, (panel, host) =>
            {
                panel.SelectEntity("CH-002");
                var activeFrom = AllControls(panel).OfType<TextBox>().First(t => t.PlaceholderText == "YYYY-MM-DD");
                activeFrom.Text = "03/03/2024";
                panel.CommitPendingEdits();
                errorShown = AllControls(panel).OfType<Label>().Any(l => l.Visible && l.Text == CaseGuideEditing.DateFormatError);
            });

            Assert.True(errorShown);
            Assert.Equal(stored, draft.Guide!.FindCast("CH-002")!.ActiveFrom);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Cast_Busy()
    {
        RenderHarness.RunSta(() =>
        {
            var gridReadOnly = false;
            var actionsEnabled = true;
            var result = RenderCast("cast-busy", RenderHarness.Large, null, (panel, host) =>
            {
                host.IsBusy = true;
                host.WritingEntityId = "CH-002";
                panel.SelectEntity("CH-002");
                panel.SetBusy(true);
                gridReadOnly = FindGrid(panel).ReadOnly;
                actionsEnabled = panel.TabActions.Any(a => a.Enabled);
            });

            Assert.True(gridReadOnly);
            Assert.False(actionsEnabled);
            Assert.Empty(result.Clipping);
        });
    }

    private static RenderResult RenderCast(string name, Size size, CaseDraft? draft, Action<CastTabPanel, RenderGuideHost>? arrange = null)
    {
        return RenderHarness.RenderControl(() => new CastTabPanel(), name, size, control =>
        {
            var panel = (CastTabPanel)control;
            var host = new RenderGuideHost(draft ?? CaseTestData.SampleGuidedDraft());
            panel.Attach(host);
            panel.RefreshFromDraft();
            arrange?.Invoke(panel, host);
        });
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
