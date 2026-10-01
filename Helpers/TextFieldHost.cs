using System.Runtime.InteropServices;

namespace ReelDiscovery.Helpers;

/// <summary>
/// Borderless TextBox inside a rounded CardPanel border. Single-line hosts match the 34 px button height.
/// </summary>
public static class TextFieldHost
{
    private const int SingleLineHeight = 34;
    private const int EmSetMargins = 0x00D3;
    private const int EcLeftAndRightMargin = 0x0003;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static CardPanel Create(Control owner, TextBox textBox, int lines = 1)
    {
        var multiline = lines > 1;
        textBox.BorderStyle = BorderStyle.None;
        textBox.Font = Theme.BodyFont();
        textBox.ForeColor = Theme.Text;
        textBox.BackColor = Theme.Surface;
        textBox.Multiline = multiline;
        textBox.ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None;
        textBox.AcceptsReturn = multiline;
        textBox.Dock = DockStyle.Fill;
        // Single-line and multiline edit controls have different default margins; zero both so the
        // text always starts exactly at the host's 10 px padding (one left edge for every field)
        textBox.HandleCreated += (_, _) => SendMessage(textBox.Handle, EmSetMargins, new IntPtr(EcLeftAndRightMargin), IntPtr.Zero);
        if (textBox.IsHandleCreated)
            SendMessage(textBox.Handle, EmSetMargins, new IntPtr(EcLeftAndRightMargin), IntPtr.Zero);

        var host = new CardPanel
        {
            Radius = Theme.Radius,
            BorderColor = Theme.BorderStrong,
            BackColor = Theme.Surface,
            Dock = DockStyle.Top,
            Margin = Padding.Empty
        };

        var horizontal = owner.LogicalToDeviceUnits(10);
        if (multiline)
        {
            host.Height = lines * textBox.Font.Height + owner.LogicalToDeviceUnits(16);
            host.Padding = new Padding(horizontal, owner.LogicalToDeviceUnits(8), horizontal, owner.LogicalToDeviceUnits(8));
        }
        else
        {
            var height = owner.LogicalToDeviceUnits(SingleLineHeight);
            // Center the text vertically the same way as the Core Document guidance box
            var vertical = Math.Max(owner.LogicalToDeviceUnits(4), (height - textBox.PreferredHeight) / 2);
            host.Height = height;
            host.Padding = new Padding(horizontal, vertical, horizontal, 0);
        }

        host.Controls.Add(textBox);
        return host;
    }

    public static void SetInvalid(CardPanel host, bool invalid)
    {
        host.BorderColor = invalid ? Theme.Danger : Theme.BorderStrong;
    }
}
