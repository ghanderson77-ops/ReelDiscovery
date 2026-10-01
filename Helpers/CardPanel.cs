using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ReelDiscovery.Helpers;

/// <summary>
/// White rounded surface with a 1px border. Children sit inside Padding, so the corners stay clean.
/// Also used with a small radius as the border around a borderless TextBox.
/// </summary>
public class CardPanel : Panel
{
    private Color _borderColor = Theme.Border;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
    }

    /// <summary>
    /// Corner radius in logical pixels.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 8;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(bounds, LogicalToDeviceUnits(Radius));
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(_borderColor);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }
}
