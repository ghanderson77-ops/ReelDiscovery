using System.ComponentModel;
using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Custom-painted stacked month histogram (UI-SPEC MonthHistogram): one bucket per calendar month,
/// Relevant (Accent) at the bottom and Not relevant (TextDisabled) on top, a nice (1/2/2.5/5) y-axis max with three
/// gridlines, non-overlapping "MMM yy" labels and a hover tooltip per month.
/// </summary>
public class MonthHistogram : Control
{
    // Logical drawing offsets (UI-SPEC: top 8, right 15, x-label band = font height + 8, label pad 8)
    private const int ContentX = 15;
    private const int TopInset = 8;
    private const int RightInset = 15;
    private const int LabelPad = 8;
    private static readonly int[] LabelSteps = { 1, 2, 3, 6, 12 };

    private readonly ToolTip _toolTip = new();
    private IReadOnlyList<MonthBucket> _buckets = Array.Empty<MonthBucket>();
    private int _hoverIndex = -1;

    public MonthHistogram()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Font = Theme.SmallFont();
        BackColor = Theme.Surface;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<MonthBucket> Buckets
    {
        get => _buckets;
        set
        {
            _buckets = value ?? Array.Empty<MonthBucket>();
            _hoverIndex = -1;
            _toolTip.SetToolTip(this, null);
            Invalidate();
        }
    }

    /// <summary>
    /// Smallest 1, 2, 2.5 or 5 x 10^k that is at least <paramref name="max"/> (never below 1); 2.5 is used only
    /// where it is a whole number (k >= 1), so 210 rounds to 250 rather than 500.
    /// </summary>
    public static int NiceAxisMax(int max)
    {
        if (max <= 1)
            return 1;
        long magnitude = 1;
        while (true)
        {
            // Tenths of the magnitude: 1, 2, 2.5 and 5
            foreach (var tenths in new[] { 10, 20, 25, 50 })
            {
                var scaled = tenths * magnitude;
                if (scaled % 10 != 0)
                    continue;
                var candidate = scaled / 10;
                if (candidate >= max)
                    return (int)Math.Min(candidate, int.MaxValue);
            }
            magnitude *= 10;
        }
    }

    /// <summary>
    /// Label every k-th bucket: the smallest k in {1, 2, 3, 6, 12} with labelWidth + 8 &lt;= k x bucketWidth, else 12.
    /// </summary>
    public static int HistogramLabelStep(int bucketWidth, int labelWidth)
    {
        foreach (var k in LabelSteps)
        {
            if (labelWidth + LabelPad <= k * bucketWidth)
                return k;
        }
        return 12;
    }

    internal static string TooltipFor(MonthBucket bucket) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{bucket.Month.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}: {bucket.Relevant:N0} relevant, {bucket.NonRelevant:N0} not relevant ({bucket.Total:N0} emails)");

    private static string AxisLabel(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string MonthLabel(MonthBucket bucket) => bucket.Month.ToString("MMM yy", CultureInfo.InvariantCulture);

    private int AxisMax() => NiceAxisMax(_buckets.Count == 0 ? 0 : _buckets.Max(b => b.Total));

    private Rectangle PlotArea(int axisMax)
    {
        var flags = TextFormatFlags.NoPadding;
        var gutter = new[] { AxisLabel(0), AxisLabel(axisMax) }
            .Max(t => TextRenderer.MeasureText(t, Font, Size.Empty, flags).Width) + LogicalToDeviceUnits(LabelPad);
        var left = LogicalToDeviceUnits(ContentX) + gutter;
        var top = LogicalToDeviceUnits(TopInset);
        var right = Width - LogicalToDeviceUnits(RightInset);
        var bottom = Height - (Font.Height + LogicalToDeviceUnits(LabelPad));
        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    // Bucket edges come from rounding i * width / count, so columns never drift by a pixel
    private static int BucketEdge(Rectangle plot, int index, int count) =>
        plot.Left + (int)Math.Round(index * plot.Width / (double)count);

    private int BucketAt(int x)
    {
        if (_buckets.Count == 0)
            return -1;
        var plot = PlotArea(AxisMax());
        if (plot.Width <= 0 || x < plot.Left || x >= plot.Right)
            return -1;
        var index = (int)((x - plot.Left) * _buckets.Count / (double)plot.Width);
        return Math.Clamp(index, 0, _buckets.Count - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = BucketAt(e.X);
        if (index == _hoverIndex)
            return;
        _hoverIndex = index;
        _toolTip.SetToolTip(this, index >= 0 ? TooltipFor(_buckets[index]) : null);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex < 0)
            return;
        _hoverIndex = -1;
        _toolTip.SetToolTip(this, null);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var axisMax = AxisMax();
        var plot = PlotArea(axisMax);
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        var count = _buckets.Count;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        // Hovered month: SurfaceMuted column behind the gridlines and bars
        if (_hoverIndex >= 0 && _hoverIndex < count)
        {
            var x0 = BucketEdge(plot, _hoverIndex, count);
            var x1 = BucketEdge(plot, _hoverIndex + 1, count);
            using var brush = new SolidBrush(Theme.SurfaceMuted);
            g.FillRectangle(brush, x0, plot.Top, x1 - x0, plot.Height);
        }

        // Y axis: gridlines at 0, half and max; labels right-aligned so the widest starts at Content x
        var labelRight = plot.Left - LogicalToDeviceUnits(LabelPad);
        using (var pen = new Pen(Theme.Border))
        {
            // (fraction of the plot height, label); odd maxima (1, 5) have no whole-number midpoint,
            // so their middle gridline is drawn without a label
            var lines = new (double Fraction, string? Label)[]
            {
                (0, AxisLabel(0)),
                (0.5, axisMax % 2 == 0 ? AxisLabel(axisMax / 2) : null),
                (1, AxisLabel(axisMax))
            };
            foreach (var (fraction, label) in lines)
            {
                var y = plot.Bottom - (int)Math.Round(plot.Height * fraction);
                g.DrawLine(pen, plot.Left, y, plot.Right - 1, y);
                if (label == null)
                    continue;
                var size = TextRenderer.MeasureText(g, label, Font, Size.Empty, flags);
                TextRenderer.DrawText(g, label, Font, new Point(labelRight - size.Width, y - size.Height / 2), Theme.TextMuted, flags);
            }
        }

        if (count == 0)
            return;

        // Bars: one constant width, centered in each bucket; Relevant at the bottom, Not relevant on top
        var bucketWidth = plot.Width / (double)count;
        var barWidth = Math.Max(2, (int)Math.Round(bucketWidth * 0.7));
        using (var relevantBrush = new SolidBrush(Theme.Accent))
        using (var otherBrush = new SolidBrush(Theme.TextDisabled))
        {
            for (var i = 0; i < count; i++)
            {
                var bucket = _buckets[i];
                if (bucket.Total <= 0)
                    continue;
                var barX = plot.Left + (int)Math.Round((i + 0.5) * bucketWidth - barWidth / 2.0);
                var totalHeight = (int)Math.Round(plot.Height * Math.Min(1.0, bucket.Total / (double)axisMax));
                var relevantHeight = (int)Math.Round(plot.Height * Math.Min(1.0, Math.Max(0, bucket.Relevant) / (double)axisMax));
                relevantHeight = Math.Min(relevantHeight, totalHeight);
                if (relevantHeight > 0)
                    g.FillRectangle(relevantBrush, barX, plot.Bottom - relevantHeight, barWidth, relevantHeight);
                var otherHeight = totalHeight - relevantHeight;
                if (otherHeight > 0)
                    g.FillRectangle(otherBrush, barX, plot.Bottom - totalHeight, barWidth, otherHeight);
            }
        }

        // X labels: every k-th month starting with the first, clamped inside the plot, never overlapping
        var labelWidth = _buckets.Max(b => TextRenderer.MeasureText(g, MonthLabel(b), Font, Size.Empty, flags).Width);
        // The helper's 8 px pad is logical; fold the device-scaled pad into the measured width
        var step = HistogramLabelStep((int)Math.Floor(bucketWidth), labelWidth + LogicalToDeviceUnits(LabelPad) - LabelPad);
        var labelTop = plot.Bottom + LogicalToDeviceUnits(LabelPad) / 2;
        var previousRight = int.MinValue;
        for (var i = 0; i < count; i += step)
        {
            var text = MonthLabel(_buckets[i]);
            var size = TextRenderer.MeasureText(g, text, Font, Size.Empty, flags);
            var center = plot.Left + (i + 0.5) * bucketWidth;
            var x = (int)Math.Round(center - size.Width / 2.0);
            x = Math.Max(plot.Left, Math.Min(x, plot.Right - size.Width));
            if (x < previousRight + LogicalToDeviceUnits(4))
                continue;
            TextRenderer.DrawText(g, text, Font, new Point(x, labelTop), Theme.TextMuted, flags);
            previousRight = x + size.Width;
        }
    }
}
