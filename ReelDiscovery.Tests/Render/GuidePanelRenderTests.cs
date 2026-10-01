using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the shared Case Guide tab skeleton (Issues tab) and the LinkPicker at 144 DPI before the step shell exists.
/// </summary>
[Trait("Category", "Render")]
public class GuidePanelRenderTests
{
    [Fact]
    public void Issues_FirstSelected_Large()
    {
        RenderHarness.RunSta(() =>
        {
            string? selected = null;
            var result = RenderPanel(() => new IssuesTabPanel(), "issues-first-selected", RenderHarness.Large, null,
                panel => selected = FindGrid(panel).SelectedRows.Count > 0 ? FindGrid(panel).SelectedRows[0].Tag as string : null);

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Equal("ISS-01", selected);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Issues_FirstSelected_Small()
    {
        RenderHarness.RunSta(() =>
        {
            var result = RenderPanel(() => new IssuesTabPanel(), "issues-first-selected", RenderHarness.Small, null);

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Issues_Empty()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = ThreeIssueDraft();
            draft.Guide!.Issues.Clear();
            var gridVisible = true;
            var result = RenderPanel(() => new IssuesTabPanel(), "issues-empty", RenderHarness.Large, draft,
                panel => gridVisible = FindGrid(panel).Visible);

            Assert.False(gridVisible);
            Assert.Empty(result.Clipping);
        });
    }

    [Fact]
    public void Issues_RebuildKeepsSortAndSelection()
    {
        RenderHarness.RunSta(() =>
        {
            string? selected = null;
            SortOrder order = SortOrder.None;
            RenderPanel(() => new IssuesTabPanel(), "issues-resorted", RenderHarness.Large, null, panel =>
            {
                var grid = FindGrid(panel);
                grid.Sort(grid.Columns["Id"]!, System.ComponentModel.ListSortDirection.Descending);
                var target = grid.Rows.Cast<DataGridViewRow>().First(r => (string)r.Tag! == "ISS-02");
                grid.CurrentCell = target.Cells[0];
                panel.RefreshFromDraft();
                selected = grid.SelectedRows[0].Tag as string;
                order = grid.SortOrder;
            });

            Assert.Equal("ISS-02", selected);
            Assert.Equal(SortOrder.Descending, order);
        });
    }

    [Fact]
    public void LinkPicker_Render()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = ThreeIssueDraft();
            var guide = draft.Guide!;
            var pickers = new List<LinkPicker>();
            var result = RenderHarness.RenderControl(() => new UserControl { BackColor = Theme.Background }, "linkpicker", RenderHarness.Large, owner =>
            {
                int L(int value) => owner.LogicalToDeviceUnits(value);

                var card = new CardPanel
                {
                    Name = "pickerCard",
                    Dock = DockStyle.Left,
                    Width = L(360),
                    Padding = new Padding(L(Theme.CardPadding))
                };
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 7,
                    BackColor = Theme.Surface,
                    Margin = Padding.Empty
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (var i = 0; i < 6; i++)
                    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                // Absorbs the leftover height so the last picker keeps its own height
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var issues = new LinkPicker("Filter issues");
                issues.SetItems(guide.Issues.Select(i => new LinkItem { Id = i.Id, Text = i.Title }).ToList(), new[] { guide.Issues[^1].Id });

                var cast = new LinkPicker("Filter characters");
                cast.SetItems(guide.Cast.Select(c => new LinkItem
                {
                    Id = c.Id,
                    Text = $"{c.FullName} — {c.Title}, {guide.FindOrganization(c.OrganizationId)?.ShortName}"
                }).ToList(), new[] { "CH-004", "CH-002", "CH-003" }, new[] { "CH-004", "CH-002" });

                var facts = new LinkPicker("Filter facts");
                facts.SetItems(guide.Facts.Select(f => new LinkItem { Id = f.Id, Text = $"{f.Date}  {f.Title}" }).ToList(), new[] { "F-003" });

                var row = 0;
                foreach (var (caption, picker) in new[] { ("Issues", issues), ("Who knew", cast), ("Used in facts", facts) })
                {
                    layout.Controls.Add(new Label
                    {
                        Text = caption,
                        AutoSize = true,
                        Margin = new Padding(0, row == 0 ? 0 : L(12), 0, L(4)),
                        Font = Theme.SmallFont(),
                        ForeColor = Theme.TextMuted
                    }, 0, row++);
                    picker.Dock = DockStyle.Fill;
                    layout.Controls.Add(picker, 0, row++);
                    pickers.Add(picker);
                }
                card.Controls.Add(layout);
                owner.Controls.Add(card);
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Empty(result.Clipping);
            Assert.Equal(new[] { "CH-002", "CH-003", "CH-004" }, pickers[1].CheckedIds.OrderBy(x => x));
        });
    }

    private static RenderResult RenderPanel(Func<GuideTabPanel> create, string name, Size size, CaseDraft? draft,
        Action<GuideTabPanel>? arrange = null)
    {
        return RenderHarness.RenderControl(create, name, size, control =>
        {
            var panel = (GuideTabPanel)control;
            panel.Attach(new RenderGuideHost(draft ?? ThreeIssueDraft()));
            panel.RefreshFromDraft();
            arrange?.Invoke(panel);
        });
    }

    /// <summary>
    /// The guided sample has one count; add two more (with matching counts) so the grid, sorting and
    /// the LinkPicker show several issues.
    /// </summary>
    private static CaseDraft ThreeIssueDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var doc = draft.CoreDocument!;
        var guide = draft.Guide!;
        var extra = new[]
        {
            (2, "Unjust Enrichment", "Harmon kept the benefit of the upper-floor window work without paying for it.", new[] { "F-003", "F-004", "F-006" }),
            (3, "Violation of the Illinois Prompt Payment Act", "Harmon withheld payment past the statutory deadline after the owner paid it.", new[] { "F-005", "F-006" })
        };
        foreach (var (number, title, summary, factIds) in extra)
        {
            doc.Counts.Add(new CaseCount
            {
                Id = $"count-{number}",
                Number = number,
                Title = title,
                AgainstPartyIds = new List<string> { "party-2" },
                LegalBasis = "Illinois common law",
                Summary = summary
            });
            guide.Issues.Add(new CaseIssue
            {
                Id = $"ISS-{number:00}",
                CountId = $"count-{number}",
                Number = number,
                Title = title,
                LegalBasis = "Illinois common law",
                Summary = summary,
                AgainstPartyIds = new List<string> { "party-2" },
                Description = summary,
                Elements = new List<string> { "Harmon received a benefit.", "Retaining it without payment is unjust." },
                RelevanceCriteria = "Documents about the Change Order 7 work, its invoice and Harmon's refusal to pay."
            });
            foreach (var factId in factIds)
                guide.FindFact(factId)!.IssueIds.Add($"ISS-{number:00}");
        }
        guide.IdCounters["ISS"] = guide.Issues.Count;
        return draft;
    }

    private static DataGridView FindGrid(Control root) =>
        FindGridOrNull(root) ?? throw new InvalidOperationException("No grid found.");

    private static DataGridView? FindGridOrNull(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is DataGridView grid)
                return grid;
            var nested = FindGridOrNull(child);
            if (nested != null)
                return nested;
        }
        return null;
    }
}
