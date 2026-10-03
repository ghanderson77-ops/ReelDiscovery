using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ReelDiscovery.Models;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Fixed per-domain email themes for generated evidence (D-16, UI-SPEC S5).
/// </summary>
public static class OrganizationThemes
{
    public const int PaletteCount = 8;

    private static readonly string[] PersonalDomains = { "gmail.com", "yahoo.com", "outlook.com", "hotmail.com", "icloud.com", "aol.com", "proton.me" };

    private static readonly Func<OrganizationTheme>[] Palettes =
    {
        () => new OrganizationTheme { ThemeName = "Harbor Navy", PrimaryColor = "1F3A5F", SecondaryColor = "4A6FA5", AccentColor = "C9A227", HeadingFont = "Segoe UI Semibold", BodyFont = "Segoe UI" },
        () => new OrganizationTheme { ThemeName = "Slate Gray", PrimaryColor = "3E4A59", SecondaryColor = "7A8796", AccentColor = "D9822B", HeadingFont = "Calibri Light", BodyFont = "Calibri" },
        () => new OrganizationTheme { ThemeName = "Forest Green", PrimaryColor = "2E5E4E", SecondaryColor = "6B9080", AccentColor = "B5651D", HeadingFont = "Georgia", BodyFont = "Georgia" },
        () => new OrganizationTheme { ThemeName = "Burgundy", PrimaryColor = "6D2E46", SecondaryColor = "A26769", AccentColor = "D5B9B2", HeadingFont = "Garamond", BodyFont = "Arial" },
        () => new OrganizationTheme { ThemeName = "Steel Blue", PrimaryColor = "2F5D8A", SecondaryColor = "7FA7C9", AccentColor = "E07A5F", HeadingFont = "Tahoma", BodyFont = "Tahoma" },
        () => new OrganizationTheme { ThemeName = "Charcoal Teal", PrimaryColor = "264653", SecondaryColor = "2A9D8F", AccentColor = "E9C46A", HeadingFont = "Trebuchet MS", BodyFont = "Verdana" },
        () => new OrganizationTheme { ThemeName = "Earth Brown", PrimaryColor = "5C4033", SecondaryColor = "967259", AccentColor = "3F7CAC", HeadingFont = "Book Antiqua", BodyFont = "Calibri" },
        () => new OrganizationTheme { ThemeName = "Plum", PrimaryColor = "4B3869", SecondaryColor = "8E7DBE", AccentColor = "F2A65A", HeadingFont = "Century Gothic", BodyFont = "Arial" }
    };

    /// <summary>
    /// Theme for a sender domain; personal and blank domains get <see cref="OrganizationTheme.Default"/>.
    /// Always returns a new instance.
    /// </summary>
    public static OrganizationTheme ForDomain(string? domain)
    {
        var key = domain?.Trim().ToLowerInvariant() ?? string.Empty;
        if (key.Length == 0 || PersonalDomains.Contains(key, StringComparer.Ordinal))
            return OrganizationTheme.Default;

        // Deterministic, no LLM call, stable across runs and machines (UI-SPEC S5, U12)
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var index = (int)(BinaryPrimitives.ReadUInt64BigEndian(hash) % PaletteCount);

        var theme = Palettes[index]();
        var fallback = OrganizationTheme.Default;

        // Fixed palettes still pass the generator's sanitizers so no unchecked CSS reaches an EML (T-05-13)
        theme.PrimaryColor = ThemeGenerator.SanitizeHexColor(theme.PrimaryColor) ?? fallback.PrimaryColor;
        theme.SecondaryColor = ThemeGenerator.SanitizeHexColor(theme.SecondaryColor) ?? fallback.SecondaryColor;
        theme.AccentColor = ThemeGenerator.SanitizeHexColor(theme.AccentColor) ?? fallback.AccentColor;
        theme.HeadingFont = ThemeGenerator.SanitizeFont(theme.HeadingFont) ?? fallback.HeadingFont;
        theme.BodyFont = ThemeGenerator.SanitizeFont(theme.BodyFont) ?? fallback.BodyFont;
        theme.Domain = key;
        return theme;
    }
}
