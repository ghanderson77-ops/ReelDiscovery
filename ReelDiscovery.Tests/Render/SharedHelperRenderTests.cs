using ReelDiscovery.Helpers;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the shared grid, status dot, text field and button helpers together so their
/// left edges and heights can be checked at 144 DPI.
/// </summary>
[Trait("Category", "Render")]
public class SharedHelperRenderTests
{
    [Fact]
    public void SharedHelpers_RenderWithoutClipping()
    {
        RenderHarness.RunSta(() =>
        {
            var result = RenderHarness.RenderControl(() => new UserControl { BackColor = Theme.Background },
                "shared-helpers", RenderHarness.Large, Build);

            Assert.True(new FileInfo(result.PngPath).Length > 10_000);
            Assert.Equal(RenderHarness.RequiredDpi, result.DeviceDpi);
            Assert.Empty(result.Clipping);
        });
    }

    private static void Build(Control owner)
    {
        int L(int value) => owner.LogicalToDeviceUnits(value);

        // Grid card: Padding = 1 so header/cell text sits on Content x = 16
        var gridCard = new CardPanel { Name = "gridCard", Dock = DockStyle.Top, Padding = new Padding(1), Height = L(170) };
        var grid = new DataGridView { Name = "grid", Dock = DockStyle.Fill };
        GridStyle.Apply(grid, owner);
        grid.ReadOnly = true;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Id", Width = L(90) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = L(240) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "Role", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        GridStyle.AddDotColumn(grid, owner);
        var dotColors = new[] { Theme.Success, Theme.Warning, Theme.Danger };
        grid.Rows.Add(string.Empty, "CH-001", "Dana Brightline", "Owner, Brightline Glazing");
        grid.Rows.Add(string.Empty, "CH-002", "Victor Harmon", "President, Harmon Builders");
        grid.Rows.Add(string.Empty, "CH-003", "Priya Natarajan", "Project Manager, Harmon Builders");
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != grid.Columns[GridStyle.DotColumnName]!.Index || e.Graphics == null)
                return;
            e.PaintBackground(e.CellBounds, true);
            Theme.DrawStatusDot(e.Graphics, e.CellBounds, dotColors[e.RowIndex % dotColors.Length], owner);
            e.Handled = true;
        };
        grid.ClearSelection();
        gridCard.Controls.Add(grid);

        var spacer = new Panel { Dock = DockStyle.Top, Height = L(Theme.Gap), BackColor = Theme.Background };

        // Form card: Padding = 16, single-line field next to a 34-high button, then a 3-line field
        var formCard = new CardPanel { Name = "formCard", Dock = DockStyle.Top, Padding = new Padding(L(Theme.CardPadding)) };
        var layout = new TableLayoutPanel
        {
            Name = "formLayout",
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Surface,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, L(34) + L(Theme.SmallGap))); // 0: single-line field + button
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: multiline field

        var singleHost = TextFieldHost.Create(owner, new TextBox { Name = "txtSingle", Text = "Brightline Glazing, LLC" });
        singleHost.Name = "singleHost";
        singleHost.Dock = DockStyle.Fill;
        singleHost.Margin = new Padding(0, 0, L(Theme.SmallGap), L(Theme.SmallGap));
        var button = ButtonHelper.CreateButton("Save", 0, L(34), ButtonStyle.Default);
        button.Name = "btnSave";
        button.Margin = new Padding(0, 0, 0, L(Theme.SmallGap));

        var multiHost = TextFieldHost.Create(owner, new TextBox
        {
            Name = "txtMulti",
            Text = "Harmon approved change orders 7 through 12 in writing on March 3 and later refused to pay them."
        }, lines: 3);
        multiHost.Name = "multiHost";
        multiHost.Dock = DockStyle.Top;
        multiHost.Margin = Padding.Empty;

        layout.Controls.Add(singleHost, 0, 0);
        layout.Controls.Add(button, 1, 0);
        layout.Controls.Add(multiHost, 0, 1);
        layout.SetColumnSpan(multiHost, 2);
        formCard.Height = L(Theme.CardPadding) * 2 + L(34) + L(Theme.SmallGap) + multiHost.Height + L(2);
        formCard.Controls.Add(layout);

        // Dock Top stacks in reverse add order
        owner.Controls.Add(formCard);
        owner.Controls.Add(spacer);
        owner.Controls.Add(gridCard);
    }
}
