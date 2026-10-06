using ReelDiscovery.Models;

namespace ReelDiscovery.Helpers;

/// <summary>
/// Cleans LLM-written theme colors and fonts before they reach an HTML style block or an Office document (D-20, D-26).
/// </summary>
public static class ThemeSanitizer
{
    // Allowed fonts that are commonly installed on Windows
    public static readonly HashSet<string> AllowedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Serif
        "Georgia", "Times New Roman", "Garamond", "Book Antiqua", "Palatino Linotype",
        // Sans-serif
        "Segoe UI", "Segoe UI Semibold", "Segoe UI Light", "Arial", "Arial Black",
        "Calibri", "Calibri Light", "Trebuchet MS", "Century Gothic", "Verdana", "Tahoma"
    };

    /// <summary>
    /// Validates and sanitizes a hex color string (removes # if present, validates length).
    /// </summary>
    public static string? SanitizeHexColor(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return null;

        color = color.Trim().TrimStart('#');

        // Must be exactly 6 hex characters
        if (color.Length != 6)
            return null;

        // Validate hex characters
        if (!color.All(c => "0123456789ABCDEFabcdef".Contains(c)))
            return null;

        return color.ToUpperInvariant();
    }

    /// <summary>
    /// Validates font name against allowed list, returns null if invalid.
    /// </summary>
    public static string? SanitizeFont(string? font)
    {
        if (string.IsNullOrWhiteSpace(font))
            return null;

        font = font.Trim();

        // Check if it's in our allowed list
        if (AllowedFonts.Contains(font))
            return font;

        // Try to find a close match (case-insensitive)
        var match = AllowedFonts.FirstOrDefault(f => f.Equals(font, StringComparison.OrdinalIgnoreCase));
        return match;
    }

    // WrapInTemplate writes theme colors and fonts into the <style> block verbatim, so only sanitized values reach it (T-06-19)
    public static OrganizationTheme SanitizedTheme(OrganizationTheme theme)
    {
        var d = OrganizationTheme.Default;
        return new OrganizationTheme
        {
            Domain = theme.Domain,
            OrganizationName = theme.OrganizationName,
            ThemeName = theme.ThemeName,
            PrimaryColor = SanitizeHexColor(theme.PrimaryColor) ?? d.PrimaryColor,
            SecondaryColor = SanitizeHexColor(theme.SecondaryColor) ?? d.SecondaryColor,
            AccentColor = SanitizeHexColor(theme.AccentColor) ?? d.AccentColor,
            TextLight = SanitizeHexColor(theme.TextLight) ?? d.TextLight,
            TextDark = SanitizeHexColor(theme.TextDark) ?? d.TextDark,
            BackgroundLight = SanitizeHexColor(theme.BackgroundLight) ?? d.BackgroundLight,
            HeadingFont = SanitizeFont(theme.HeadingFont) ?? d.HeadingFont,
            BodyFont = SanitizeFont(theme.BodyFont) ?? d.BodyFont
        };
    }

    /// <summary>D-26: cleaning for the business HTML template. Valid values are kept byte-for-byte so golden output is unchanged; invalid ones fall back to the default theme.</summary>
    public static OrganizationTheme TemplateTheme(OrganizationTheme? theme)
    {
        var d = OrganizationTheme.Default;
        if (theme == null)
            return d;

        return new OrganizationTheme
        {
            Domain = theme.Domain,
            OrganizationName = theme.OrganizationName,
            ThemeName = theme.ThemeName,
            PrimaryColor = TemplateColor(theme.PrimaryColor, d.PrimaryColor),
            SecondaryColor = TemplateColor(theme.SecondaryColor, d.SecondaryColor),
            AccentColor = TemplateColor(theme.AccentColor, d.AccentColor),
            TextLight = TemplateColor(theme.TextLight, d.TextLight),
            TextDark = TemplateColor(theme.TextDark, d.TextDark),
            BackgroundLight = TemplateColor(theme.BackgroundLight, d.BackgroundLight),
            HeadingFont = TemplateFont(theme.HeadingFont, d.HeadingFont),
            BodyFont = TemplateFont(theme.BodyFont, d.BodyFont)
        };
    }

    // Exactly 6 hex chars (no '#', no whitespace) is kept in its original case; anything else is cleaned or defaulted
    private static string TemplateColor(string? value, string fallback) =>
        value != null && value.Length == 6 && value.All(char.IsAsciiHexDigit)
            ? value
            : SanitizeHexColor(value) ?? fallback;

    private static string TemplateFont(string? value, string fallback) =>
        value != null && AllowedFonts.Contains(value)
            ? value
            : SanitizeFont(value) ?? fallback;
}
