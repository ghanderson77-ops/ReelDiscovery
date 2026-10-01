namespace ReelDiscovery.Tests.Render;

internal sealed class ClippingOptions
{
    public bool AllowGridHorizontalScroll = false;
    public HashSet<string> IgnoreNames = new();
}

/// <summary>
/// Walks a live control tree and reports text that does not fit, buttons smaller than their
/// preferred size, controls that overflow their parent and unwanted horizontal scrollbars.
/// </summary>
internal static class ClippingDetector
{
    public static List<string> Find(Control root, ClippingOptions? options = null)
    {
        options ??= new ClippingOptions();
        var findings = new List<string>();
        try
        {
            foreach (Control child in root.Controls)
                Walk(child, Describe(root), options, findings, parentScrolls: IsAutoScroll(root));
        }
        catch (Exception ex)
        {
            findings.Add($"{Describe(root)}: check failed: {ex.Message}");
        }
        return findings;
    }

    private static void Walk(Control control, string parentPath, ClippingOptions options, List<string> findings, bool parentScrolls)
    {
        if (!control.Visible)
            return;

        var path = parentPath + "/" + Describe(control);
        if (!string.IsNullOrEmpty(control.Name) && options.IgnoreNames.Contains(control.Name))
            return;

        try
        {
            Check(control, path, options, findings, parentScrolls);
        }
        catch (Exception ex)
        {
            findings.Add($"{path}: check failed: {ex.Message}");
        }

        // Grid and list internals (scrollbars, editing controls) are not layout children
        if (control is DataGridView || control is ListBox)
            return;

        var scrolls = IsAutoScroll(control);
        foreach (Control child in control.Controls)
            Walk(child, path, options, findings, scrolls);
    }

    private static void Check(Control control, string path, ClippingOptions options, List<string> findings, bool parentScrolls)
    {
        var w = control.Width;
        var h = control.Height;

        if (control is Label label && !string.IsNullOrEmpty(label.Text))
        {
            if (!label.AutoSize && !label.AutoEllipsis)
            {
                var availableWidth = Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal);
                var availableHeight = label.ClientSize.Height - label.Padding.Vertical;
                var measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(availableWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                if (measured.Height > availableHeight + 1)
                    findings.Add($"{path}: label text taller than label (bounds {w}x{h}, needs {measured.Width + label.Padding.Horizontal}x{measured.Height + label.Padding.Vertical})");

                var singleWord = !label.Text.Contains(' ') && !label.Text.Contains('\n');
                if (singleWord)
                {
                    var oneLine = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    if (oneLine.Width > availableWidth + 1)
                        findings.Add($"{path}: label text wider than label (bounds {w}x{h}, needs {oneLine.Width + label.Padding.Horizontal}x{oneLine.Height})");
                }
            }
            else if (label.AutoSize && label.MaximumSize.Width > 0)
            {
                var preferred = label.PreferredSize;
                if (preferred.Height > h + 1)
                    findings.Add($"{path}: wrapped label cut off (bounds {w}x{h}, needs {preferred.Width}x{preferred.Height})");
            }
        }

        if (control is ButtonBase button)
        {
            var preferred = button.PreferredSize;
            if (preferred.Width > w + 1 || preferred.Height > h + 1)
                findings.Add($"{path}: button smaller than its text (bounds {w}x{h}, needs {preferred.Width}x{preferred.Height})");
        }

        var parent = control.Parent;
        if (parent != null && !parentScrolls && parent is not DataGridView && parent is not ListBox)
        {
            var client = parent.ClientRectangle;
            var bounds = control.Bounds;
            if (bounds.Right > client.Right + 1 || bounds.Bottom > client.Bottom + 1)
                findings.Add($"{path}: extends past its parent (bounds {w}x{h}, needs {bounds.Right}x{bounds.Bottom} inside {client.Width}x{client.Height})");
        }

        if (control is ScrollableControl scrollable && scrollable.AutoScroll && scrollable.HorizontalScroll.Visible)
            findings.Add($"{path}: horizontal scrollbar visible (bounds {w}x{h}, needs {scrollable.DisplayRectangle.Width}x{scrollable.DisplayRectangle.Height})");

        if (control is DataGridView grid && !options.AllowGridHorizontalScroll)
        {
            foreach (Control inner in grid.Controls)
            {
                if (inner is HScrollBar bar && bar.Visible)
                {
                    var columns = grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
                    findings.Add($"{path}: grid horizontal scrollbar (bounds {w}x{h}, needs {columns}x{h})");
                }
            }
        }
    }

    private static bool IsAutoScroll(Control control) =>
        control is ScrollableControl scrollable && scrollable.AutoScroll;

    private static string Describe(Control control)
    {
        if (!string.IsNullOrEmpty(control.Name))
            return control.Name;
        var text = control.Text ?? string.Empty;
        text = text.Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 30)
            text = text[..30] + "...";
        return $"{control.GetType().Name}(\"{text}\")";
    }
}
