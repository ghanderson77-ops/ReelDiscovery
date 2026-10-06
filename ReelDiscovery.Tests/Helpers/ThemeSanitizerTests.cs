using ReelDiscovery.Helpers;
using ReelDiscovery.Models;

// Not ReelDiscovery.Tests.Helpers: that namespace would shadow ReelDiscovery.Helpers in the render tests (Helpers.Theme)
namespace ReelDiscovery.Tests.HelperTests;

public class ThemeSanitizerTests
{
    [Theory]
    [InlineData("#1a2b3c", "1A2B3C")]
    [InlineData(" 2b579a ", "2B579A")]
    [InlineData("zzz", null)]
    [InlineData("zzzzzz", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SanitizeHexColor_KeepsLegacyBehavior(string? input, string? expected)
    {
        Assert.Equal(expected, ThemeSanitizer.SanitizeHexColor(input));
    }

    [Theory]
    [InlineData(" calibri ", "calibri")]
    [InlineData("Georgia", "Georgia")]
    [InlineData("Comic</style>", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void SanitizeFont_KeepsLegacyBehavior(string? input, string? expected)
    {
        Assert.Equal(expected, ThemeSanitizer.SanitizeFont(input));
    }

    [Fact]
    public void AllowedFonts_IsCaseInsensitive()
    {
        Assert.Contains("segoe ui", ThemeSanitizer.AllowedFonts);
        Assert.DoesNotContain("Comic Sans MS", ThemeSanitizer.AllowedFonts);
    }

    [Fact]
    public void TemplateTheme_NullTheme_ReturnsDefault()
    {
        var t = ThemeSanitizer.TemplateTheme(null);
        var d = OrganizationTheme.Default;

        Assert.Equal(d.PrimaryColor, t.PrimaryColor);
        Assert.Equal(d.BodyFont, t.BodyFont);
        Assert.Equal(d.ThemeName, t.ThemeName);
    }

    [Fact]
    public void TemplateTheme_ValidValues_KeptByteForByte()
    {
        var theme = new OrganizationTheme
        {
            Domain = "acme.com",
            OrganizationName = "Acme",
            ThemeName = "Acme Navy",
            PrimaryColor = "1a2b3c",
            SecondaryColor = "AbCdEf",
            AccentColor = "ED7D31",
            TextLight = "ffffff",
            TextDark = "2d2d2d",
            BackgroundLight = "F5F5F5",
            HeadingFont = "georgia",
            BodyFont = "Georgia"
        };

        var t = ThemeSanitizer.TemplateTheme(theme);

        Assert.Equal("acme.com", t.Domain);
        Assert.Equal("Acme", t.OrganizationName);
        Assert.Equal("Acme Navy", t.ThemeName);
        Assert.Equal("1a2b3c", t.PrimaryColor);
        Assert.Equal("AbCdEf", t.SecondaryColor);
        Assert.Equal("ED7D31", t.AccentColor);
        Assert.Equal("ffffff", t.TextLight);
        Assert.Equal("2d2d2d", t.TextDark);
        Assert.Equal("F5F5F5", t.BackgroundLight);
        Assert.Equal("georgia", t.HeadingFont);
        Assert.Equal("Georgia", t.BodyFont);
    }

    [Fact]
    public void TemplateTheme_HashOrPaddedColor_IsCleaned()
    {
        var theme = new OrganizationTheme { PrimaryColor = "#1A2B3C", SecondaryColor = " 5b9bd5 " };

        var t = ThemeSanitizer.TemplateTheme(theme);

        Assert.Equal("1A2B3C", t.PrimaryColor);
        Assert.Equal("5B9BD5", t.SecondaryColor);
    }

    [Fact]
    public void TemplateTheme_InvalidValues_FallBackToDefault()
    {
        var d = OrganizationTheme.Default;
        var theme = new OrganizationTheme
        {
            PrimaryColor = "red;}body{",
            SecondaryColor = "",
            AccentColor = "12345",
            TextLight = "gggggg",
            TextDark = "#2d2d2d;x",
            BackgroundLight = "</style>",
            HeadingFont = "Comic Sans MS",
            BodyFont = "x</style><script>"
        };

        var t = ThemeSanitizer.TemplateTheme(theme);

        Assert.Equal(d.PrimaryColor, t.PrimaryColor);
        Assert.Equal(d.SecondaryColor, t.SecondaryColor);
        Assert.Equal(d.AccentColor, t.AccentColor);
        Assert.Equal(d.TextLight, t.TextLight);
        Assert.Equal(d.TextDark, t.TextDark);
        Assert.Equal(d.BackgroundLight, t.BackgroundLight);
        Assert.Equal(d.HeadingFont, t.HeadingFont);
        Assert.Equal(d.BodyFont, t.BodyFont);
    }

    [Fact]
    public void TemplateTheme_PaddedFont_IsTrimmed()
    {
        var t = ThemeSanitizer.TemplateTheme(new OrganizationTheme { BodyFont = " Verdana " });

        Assert.Equal("Verdana", t.BodyFont);
    }

    [Fact]
    public void SanitizedTheme_UpperCasesColorsAndFallsBack()
    {
        var d = OrganizationTheme.Default;
        var theme = new OrganizationTheme
        {
            Domain = "acme.com",
            OrganizationName = "Acme",
            ThemeName = "Acme Navy",
            PrimaryColor = "1a2b3c",
            SecondaryColor = "#abcdef",
            AccentColor = "bad",
            TextLight = "ffffff",
            TextDark = "2d2d2d",
            BackgroundLight = "f5f5f5",
            HeadingFont = " Arial ",
            BodyFont = "x</style>"
        };

        var t = ThemeSanitizer.SanitizedTheme(theme);

        Assert.Equal("acme.com", t.Domain);
        Assert.Equal("Acme", t.OrganizationName);
        Assert.Equal("Acme Navy", t.ThemeName);
        Assert.Equal("1A2B3C", t.PrimaryColor);
        Assert.Equal("ABCDEF", t.SecondaryColor);
        Assert.Equal(d.AccentColor, t.AccentColor);
        Assert.Equal("FFFFFF", t.TextLight);
        Assert.Equal("2D2D2D", t.TextDark);
        Assert.Equal("F5F5F5", t.BackgroundLight);
        Assert.Equal("Arial", t.HeadingFont);
        Assert.Equal(d.BodyFont, t.BodyFont);
    }
}
