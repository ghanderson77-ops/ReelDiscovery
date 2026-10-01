using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ReelDiscovery.Helpers;

/// <summary>
/// Rounded, owner-painted button with hover, pressed, focus and disabled states.
/// Height follows the font so it stays proportional at any DPI.
/// </summary>
public class ThemedButton : Button
{
    private ButtonStyle _variant = ButtonStyle.Default;
    private bool _hover;
    private bool _pressed;
    private bool _selected;
    private bool _autoFit;
    private int _minWidth;
    private int _minHeight;

    public ThemedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font(Theme.FontFamily, 9.5F);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonStyle Variant
    {
        get => _variant;
        set
        {
            _variant = value;
            Font = new Font(Theme.FontFamily, 9.5F, value == ButtonStyle.Primary || value == ButtonStyle.Danger ? FontStyle.Bold : FontStyle.Regular);
            Invalidate();
        }
    }

    /// <summary>
    /// Toggle variant only: draws the selected segment.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Sizes the button to its text: at least minWidth wide, and tall enough for the font plus padding.
    /// </summary>
    public void FitToText(int minWidth, int minHeight)
    {
        _autoFit = true;
        _minWidth = minWidth;
        _minHeight = minHeight;
        var text = TextRenderer.MeasureText(Text, Font);
        Width = Math.Max(minWidth, text.Width + LogicalToDeviceUnits(32));
        // Compact by default so buttons fit fixed-height rows on older screens; new screens pass a larger minimum
        Height = Math.Max(minHeight, text.Height + LogicalToDeviceUnits(10));
    }

    // Re-fit when the text changes (e.g. Next becomes Finish) so labels are never cut off
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (_autoFit)
            FitToText(_minWidth, _minHeight);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        base.OnEnabledChanged(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (fill, border, text) = Colors();
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var radius = LogicalToDeviceUnits(Theme.Radius);

        using (var path = Theme.RoundedRect(bounds, radius))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        // Keyboard focus ring (not shown for mouse clicks)
        if (Focused && ShowFocusCues && Enabled)
        {
            var inset = Rectangle.Inflate(bounds, -LogicalToDeviceUnits(3), -LogicalToDeviceUnits(3));
            using var focusPath = Theme.RoundedRect(inset, Math.Max(1, radius - 2));
            using var focusPen = new Pen(_variant is ButtonStyle.Primary or ButtonStyle.Danger ? Color.White : Theme.Accent) { DashStyle = DashStyle.Dot };
            g.DrawPath(focusPen, focusPath);
        }

        TextRenderer.DrawText(g, Text, Font, bounds, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private (Color Fill, Color Border, Color Text) Colors()
    {
        if (!Enabled)
            return (Theme.SurfaceMuted, Theme.Border, Theme.TextDisabled);

        return _variant switch
        {
            ButtonStyle.Primary => (_pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent,
                _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent, Color.White),
            ButtonStyle.Danger => (_pressed ? Theme.DangerPressed : _hover ? Theme.DangerHover : Theme.Danger,
                _pressed ? Theme.DangerPressed : _hover ? Theme.DangerHover : Theme.Danger, Color.White),
            ButtonStyle.Success => (Theme.Success, Theme.Success, Color.White),
            ButtonStyle.Toggle => _selected
                ? (Theme.AccentSoft, Theme.Accent, Theme.Accent)
                : (_hover ? Theme.SurfaceMuted : Theme.Surface, Theme.BorderStrong, Theme.TextMuted),
            _ => (_pressed ? Theme.Border : _hover ? Theme.SurfaceMuted : Theme.Surface, Theme.BorderStrong, Theme.Text)
        };
    }
}
