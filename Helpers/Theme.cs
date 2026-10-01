using System.Drawing.Drawing2D;

namespace ReelDiscovery.Helpers;

/// <summary>
/// Shared palette, type and spacing for the wizard. Spacing values are logical pixels:
/// convert with Control.LogicalToDeviceUnits so they scale with the display DPI.
/// </summary>
public static class Theme
{
    // Palette
    public static readonly Color Background = Color.FromArgb(244, 245, 247);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceMuted = Color.FromArgb(248, 249, 250);
    public static readonly Color Border = Color.FromArgb(222, 226, 230);
    public static readonly Color BorderStrong = Color.FromArgb(206, 212, 218);
    public static readonly Color Text = Color.FromArgb(31, 35, 40);
    public static readonly Color TextMuted = Color.FromArgb(101, 109, 118);
    public static readonly Color TextDisabled = Color.FromArgb(160, 167, 176);
    public static readonly Color Accent = Color.FromArgb(0, 120, 215);
    public static readonly Color AccentHover = Color.FromArgb(0, 103, 192);
    public static readonly Color AccentPressed = Color.FromArgb(0, 84, 166);
    public static readonly Color AccentSoft = Color.FromArgb(232, 242, 252);
    public static readonly Color Success = Color.FromArgb(26, 127, 55);
    public static readonly Color Warning = Color.FromArgb(176, 106, 0);
    public static readonly Color Danger = Color.FromArgb(207, 34, 46);
    public static readonly Color DangerHover = Color.FromArgb(180, 28, 39);
    public static readonly Color DangerPressed = Color.FromArgb(150, 22, 32);

    // Spacing (logical pixels)
    public const int PageMargin = 24;
    public const int Gap = 16;
    public const int SmallGap = 8;
    public const int CardPadding = 16;
    public const int Radius = 6;

    // Type
    public const string FontFamily = "Segoe UI";
    public static Font TitleFont() => new(FontFamily, 15F, FontStyle.Bold);
    public static Font HeadingFont() => new(FontFamily, 12F, FontStyle.Bold);
    public static Font LabelFont() => new(FontFamily, 9.5F, FontStyle.Bold);
    public static Font BodyFont() => new(FontFamily, 9.5F);
    public static Font SmallFont() => new(FontFamily, 9F);
    public static Font EditorFont() => new(FontFamily, 10.5F);

    /// <summary>
    /// Rounded rectangle path for custom painting (radius in device pixels).
    /// </summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
