namespace ReelDiscovery.Helpers;

/// <summary>
/// The shared grid look (from the Core Document issues grid). Apply it to every Phase 3 grid.
/// </summary>
public static class GridStyle
{
    public const string DotColumnName = "Dot";

    public static void Apply(DataGridView grid, Control owner)
    {
        // Every Phase 3 grid uses this so cell and header text land on Content x = 16 inside Padding = 1 cards (UI-SPEC)
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = Theme.Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Theme.Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.Font = Theme.BodyFont();

        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.TextMuted;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.SmallFont();
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(owner.LogicalToDeviceUnits(15), owner.LogicalToDeviceUnits(6), 0, owner.LogicalToDeviceUnits(6));

        grid.DefaultCellStyle.BackColor = Theme.Surface;
        grid.DefaultCellStyle.ForeColor = Theme.Text;
        grid.DefaultCellStyle.Padding = new Padding(owner.LogicalToDeviceUnits(15), 0, 0, 0);
        grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
        grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        grid.RowTemplate.Height = owner.LogicalToDeviceUnits(32);
    }

    /// <summary>
    /// Inserts a narrow status-dot column at index 0. Paint the dot with Theme.DrawStatusDot in CellPainting.
    /// </summary>
    public static DataGridViewTextBoxColumn AddDotColumn(DataGridView grid, Control owner)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = DotColumnName,
            HeaderText = string.Empty,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            Width = owner.LogicalToDeviceUnits(28)
        };
        // The dot is painted at inset 15, so 15 + 8 fits inside 28
        column.DefaultCellStyle.Padding = Padding.Empty;
        grid.Columns.Insert(0, column);
        return column;
    }
}
