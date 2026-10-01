using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ReelDiscovery.Helpers;

/// <summary>
/// Thin rounded progress bar in the accent color. Marquee mode animates a moving segment
/// for work with no known length (writing the outline).
/// </summary>
public class SlimProgressBar : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private int _value;
    private int _maximum = 100;
    private bool _marquee;
    private float _marqueeOffset;

    public SlimProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _timer.Tick += (s, e) =>
        {
            _marqueeOffset = (_marqueeOffset + 0.015f) % 1.3f;
            Invalidate();
        };
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, _maximum);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            _value = Math.Min(_value, _maximum);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Marquee
    {
        get => _marquee;
        set
        {
            _marquee = value;
            _timer.Enabled = value && Visible;
            Invalidate();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        _timer.Enabled = _marquee && Visible;
        base.OnVisibleChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = Height / 2;
        using (var trackPath = Theme.RoundedRect(track, radius))
        using (var trackBrush = new SolidBrush(Theme.Border))
        {
            g.FillPath(trackBrush, trackPath);
        }

        Rectangle fill;
        if (_marquee)
        {
            var segment = (int)(Width * 0.3f);
            var x = (int)((_marqueeOffset - 0.3f) * Width);
            fill = Rectangle.Intersect(track, new Rectangle(x, 0, segment, Height - 1));
        }
        else
        {
            fill = new Rectangle(0, 0, (int)((Width - 1) * (_value / (float)_maximum)), Height - 1);
        }

        if (fill.Width <= 0)
            return;

        using var fillPath = Theme.RoundedRect(fill, radius);
        using var fillBrush = new SolidBrush(Theme.Accent);
        g.FillPath(fillBrush, fillPath);
    }
}
