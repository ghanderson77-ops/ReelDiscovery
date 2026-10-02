using ReelDiscovery.Helpers;

namespace ReelDiscovery.UserControls;

/// <summary>
/// One Review Plan stat tile: caption, value and sub-line on a card. Every label ellipsizes, so the tile
/// carries a tooltip with the full text (and the content's own explanation) to keep nothing hidden.
/// </summary>
public class StatTile : UserControl
{
    private readonly ToolTip _toolTip = new();
    private CardPanel _card = null!;
    private TableLayoutPanel _layout = null!;
    private Label _lblCaption = null!;
    private Label _lblValue = null!;
    private Label _lblSubLine = null!;

    public StatTile()
    {
        InitializeUI();
    }

    /// <summary>
    /// The tooltip text currently set on the tile (exposed for tests).
    /// </summary>
    internal string TooltipText { get; private set; } = string.Empty;

    internal string CaptionText => _lblCaption.Text;

    internal string ValueText => _lblValue.Text;

    internal string SubLineText => _lblSubLine.Text;

    private void InitializeUI()
    {
        this.BackColor = Theme.Background;
        this.Margin = Padding.Empty;
        this.Padding = Padding.Empty;

        _card = new CardPanel
        {
            Dock = DockStyle.Fill,
            // 16 horizontal, 12 vertical
            Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12), LogicalToDeviceUnits(16), LogicalToDeviceUnits(12))
        };

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Value
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Sub-line

        _lblCaption = TileLabel(Theme.SmallFont(), Theme.TextMuted, 0);
        _lblValue = TileLabel(Theme.TitleFont(), Theme.Text, LogicalToDeviceUnits(4));
        _lblSubLine = TileLabel(Theme.SmallFont(), Theme.TextMuted, LogicalToDeviceUnits(2));
        _layout.Controls.Add(_lblCaption, 0, 0);
        _layout.Controls.Add(_lblValue, 0, 1);
        _layout.Controls.Add(_lblSubLine, 0, 2);

        _card.Controls.Add(_layout);
        this.Controls.Add(_card);
        this.Height = TileHeight();
    }

    private static Label TileLabel(Font font, Color color, int top)
    {
        var label = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, top, 0, 0),
            Font = font,
            ForeColor = color,
            BackColor = Theme.Surface
        };
        label.Height = label.PreferredHeight;
        return label;
    }

    /// <summary>
    /// Card padding plus the three label rows and their top margins.
    /// </summary>
    private int TileHeight() =>
        _card.Padding.Vertical
        + _lblCaption.Height + _lblCaption.Margin.Vertical
        + _lblValue.Height + _lblValue.Margin.Vertical
        + _lblSubLine.Height + _lblSubLine.Margin.Vertical;

    public override Size GetPreferredSize(Size proposedSize) => new(Width, TileHeight());

    public void SetContent(TileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _lblCaption.Text = content.Caption;
        _lblValue.Text = content.Value;
        _lblSubLine.Text = content.SubLine;

        var summary = string.IsNullOrEmpty(content.SubLine)
            ? $"{content.Caption}: {content.Value}"
            : $"{content.Caption}: {content.Value} ({content.SubLine})";
        TooltipText = string.IsNullOrWhiteSpace(content.Tooltip)
            ? summary
            : summary + Environment.NewLine + content.Tooltip;

        // Tooltips do not propagate to children: set it on every surface the mouse can rest on
        foreach (var control in new Control[] { this, _card, _layout, _lblCaption, _lblValue, _lblSubLine })
            _toolTip.SetToolTip(control, TooltipText);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
