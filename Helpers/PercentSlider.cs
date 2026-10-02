using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ReelDiscovery.Helpers;

/// <summary>
/// Themed horizontal slider (replaces the stock track bar): 4 px rounded track, 16 px round thumb,
/// AccentSoft focus halo. Raises ValueChanged live while dragging and ValueCommitted on mouse-up and
/// key-up, so the owner can autosave once per gesture instead of on every drag tick.
/// </summary>
public class PercentSlider : Control
{
    // Logical sizes (UI-SPEC: slider height 28, track 4, thumb 16, halo 24, thumb travel inset 8)
    private const int TrackHeight = 4;
    private const int ThumbSize = 16;
    private const int HaloSize = 24;
    private const int ThumbInset = 8;

    private int _minimum = 5;
    private int _maximum = 60;
    private int _value = 5;
    private bool _hover;
    private bool _dragging;

    public PercentSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
    }

    public event EventHandler? ValueChanged;

    public event EventHandler? ValueCommitted;

    protected override Size DefaultSize => new(200, 28);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
                _maximum = _minimum;
            SetValue(_value);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(_minimum, value);
            SetValue(_value);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    private void SetValue(int value)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);
        if (clamped == _value)
            return;
        _value = clamped;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Maps a device x coordinate to a value: the thumb center travels from 8 to Width - 8 (logical).
    /// </summary>
    internal int ValueFromX(int x)
    {
        var inset = LogicalToDeviceUnits(ThumbInset);
        var travel = Math.Max(1, Width - 2 * inset);
        var fraction = Math.Clamp((x - inset) / (double)travel, 0, 1);
        return _minimum + (int)Math.Round(fraction * (_maximum - _minimum));
    }

    private int ThumbCenterX()
    {
        var inset = LogicalToDeviceUnits(ThumbInset);
        var travel = Math.Max(0, Width - 2 * inset);
        var range = Math.Max(1, _maximum - _minimum);
        return inset + (int)Math.Round(travel * (_value - _minimum) / (double)range);
    }

    /// <summary>
    /// Keyboard stepping: Left/Right ±1, PageUp/PageDown ±5, Home/End to the ends. Returns true when handled.
    /// </summary>
    internal bool HandleKey(Keys key)
    {
        switch (key)
        {
            case Keys.Right:
            case Keys.Up:
                Value = _value + 1;
                return true;
            case Keys.Left:
            case Keys.Down:
                Value = _value - 1;
                return true;
            case Keys.PageUp:
                Value = _value + 5;
                return true;
            case Keys.PageDown:
                Value = _value - 5;
                return true;
            case Keys.Home:
                Value = _minimum;
                return true;
            case Keys.End:
                Value = _maximum;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Raises ValueCommitted (mouse-up and key-up end a gesture).
    /// </summary>
    internal void Commit() => ValueCommitted?.Invoke(this, EventArgs.Empty);

    private static bool IsSliderKey(Keys key) =>
        key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End;

    protected override bool IsInputKey(Keys keyData) =>
        IsSliderKey(keyData & Keys.KeyCode) || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && HandleKey(e.KeyCode))
            e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (Enabled && IsSliderKey(e.KeyCode))
            Commit();
        base.OnKeyUp(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left)
            return;
        Focus();
        _dragging = true;
        Capture = true;
        Value = ValueFromX(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Value = ValueFromX(e.X);
            return;
        }

        var hover = IsOverThumb(e.Location);
        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging)
            return;
        _dragging = false;
        Capture = false;
        Invalidate();
        Commit();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover)
        {
            _hover = false;
            Invalidate();
        }
    }

    private bool IsOverThumb(Point p)
    {
        var radius = LogicalToDeviceUnits(ThumbSize) / 2;
        var dx = p.X - ThumbCenterX();
        var dy = p.Y - Height / 2;
        return dx * dx + dy * dy <= radius * radius;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var inset = LogicalToDeviceUnits(ThumbInset);
        var trackHeight = LogicalToDeviceUnits(TrackHeight);
        var thumb = LogicalToDeviceUnits(ThumbSize);
        var halo = LogicalToDeviceUnits(HaloSize);
        var border = LogicalToDeviceUnits(2);
        var centerY = Height / 2f;
        var thumbX = ThumbCenterX();

        // Track: unfilled part across the whole travel, filled part from the left end to the thumb
        var trackTop = (int)Math.Round(centerY - trackHeight / 2f);
        var track = new Rectangle(inset, trackTop, Math.Max(1, Width - 2 * inset), trackHeight);
        using (var path = Theme.RoundedRect(track, trackHeight / 2))
        using (var brush = new SolidBrush(Theme.Border))
        {
            g.FillPath(brush, path);
        }

        var filled = new Rectangle(inset, trackTop, Math.Max(0, thumbX - inset), trackHeight);
        if (filled.Width > 0)
        {
            using var path = Theme.RoundedRect(filled, trackHeight / 2);
            using var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.TextDisabled);
            g.FillPath(brush, path);
        }

        // Focus halo behind the thumb (no dotted focus rectangle)
        if (Focused && Enabled)
        {
            using var brush = new SolidBrush(Theme.AccentSoft);
            g.FillEllipse(brush, thumbX - halo / 2f, centerY - halo / 2f, halo, halo);
        }

        // Thumb: Surface fill with a 2 px border drawn inside the 16 px circle
        var borderColor = !Enabled ? Theme.BorderStrong
            : _dragging ? Theme.AccentPressed
            : _hover ? Theme.AccentHover
            : Theme.Accent;
        var thumbRect = new RectangleF(thumbX - thumb / 2f + border / 2f, centerY - thumb / 2f + border / 2f,
            thumb - border, thumb - border);
        using (var brush = new SolidBrush(Theme.Surface))
        {
            g.FillEllipse(brush, thumbRect);
        }
        using (var pen = new Pen(borderColor, border))
        {
            g.DrawEllipse(pen, thumbRect);
        }
    }
}
