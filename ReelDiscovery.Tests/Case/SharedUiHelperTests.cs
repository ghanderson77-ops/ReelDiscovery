using ReelDiscovery.Helpers;

namespace ReelDiscovery.Tests.Case;

public class SharedUiHelperTests
{
    [Fact]
    public void GridStyle_AppliesReferenceLook()
    {
        using var owner = new UserControl();
        using var grid = new DataGridView();

        GridStyle.Apply(grid, owner);

        Assert.False(grid.RowHeadersVisible);
        Assert.Equal(DataGridViewSelectionMode.FullRowSelect, grid.SelectionMode);
        Assert.Equal(DataGridViewCellBorderStyle.SingleHorizontal, grid.CellBorderStyle);
        Assert.Equal(Theme.Border, grid.GridColor);
        Assert.Equal(BorderStyle.None, grid.BorderStyle);
        Assert.False(grid.EnableHeadersVisualStyles);
        Assert.Equal(Theme.SurfaceMuted, grid.ColumnHeadersDefaultCellStyle.BackColor);
        Assert.Equal(Theme.TextMuted, grid.ColumnHeadersDefaultCellStyle.ForeColor);
        Assert.Equal(Theme.AccentSoft, grid.DefaultCellStyle.SelectionBackColor);
        Assert.Equal(Theme.Text, grid.DefaultCellStyle.SelectionForeColor);
        Assert.False(grid.AllowUserToAddRows);
        Assert.False(grid.AllowUserToResizeRows);
        Assert.Equal(DataGridViewColumnHeadersHeightSizeMode.AutoSize, grid.ColumnHeadersHeightSizeMode);
        Assert.False(grid.AutoGenerateColumns);
        Assert.False(grid.ReadOnly);
    }

    [Fact]
    public void GridStyle_CellPaddingIs15Logical()
    {
        using var owner = new UserControl();
        using var grid = new DataGridView();

        GridStyle.Apply(grid, owner);

        Assert.Equal(owner.LogicalToDeviceUnits(15), grid.DefaultCellStyle.Padding.Left);
        // Header cells add an unscaled 2 px text margin, so their padding compensates to land on the same x
        Assert.Equal(owner.LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, grid.ColumnHeadersDefaultCellStyle.Padding.Left);
    }

    [Fact]
    public void AddDotColumn_IsNarrowReadOnlyUnsortable()
    {
        using var owner = new UserControl();
        using var grid = new DataGridView();
        GridStyle.Apply(grid, owner);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title" });

        var column = GridStyle.AddDotColumn(grid, owner);

        Assert.Equal("Dot", column.Name);
        Assert.Equal(GridStyle.DotColumnName, column.Name);
        Assert.True(column.ReadOnly);
        Assert.Equal(DataGridViewColumnSortMode.NotSortable, column.SortMode);
        Assert.Equal(owner.LogicalToDeviceUnits(28), column.Width);
        Assert.Equal(0, column.Index);
        Assert.Equal(string.Empty, column.HeaderText);
    }

    [Fact]
    public void TextFieldHost_SingleLine()
    {
        using var owner = new UserControl();
        var textBox = new TextBox();

        using var host = TextFieldHost.Create(owner, textBox);

        Assert.Equal(owner.LogicalToDeviceUnits(34), host.Height);
        Assert.Equal(BorderStyle.None, textBox.BorderStyle);
        Assert.False(textBox.Multiline);
        Assert.Equal(Theme.BorderStrong, host.BorderColor);
        Assert.Same(host, textBox.Parent);

        TextFieldHost.SetInvalid(host, true);
        Assert.Equal(Theme.Danger, host.BorderColor);
        TextFieldHost.SetInvalid(host, false);
        Assert.Equal(Theme.BorderStrong, host.BorderColor);
    }

    [Fact]
    public void TextFieldHost_Multiline()
    {
        using var owner = new UserControl();
        var textBox = new TextBox();

        using var host = TextFieldHost.Create(owner, textBox, lines: 4);

        Assert.True(textBox.Multiline);
        Assert.Equal(4 * textBox.Font.Height + owner.LogicalToDeviceUnits(16), host.Height);
    }
}
