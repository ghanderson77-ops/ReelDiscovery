namespace ReelDiscovery.Models;

/// <summary>
/// Defines a color and typography theme for an organization, used for emails and documents.
/// </summary>
public class OrganizationTheme
{
    /// <summary>
    /// The domain this theme is associated with (e.g., "starkindustries.com")
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Organization name this theme represents
    /// </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// Primary color - used for title slide background and header bars (hex without #)
    /// </summary>
    public string PrimaryColor { get; set; } = "2B579A";

    /// <summary>
    /// Secondary color - used for accents and highlights (hex without #)
    /// </summary>
    public string SecondaryColor { get; set; } = "5B9BD5";

    /// <summary>
    /// Accent color - used for accent lines and emphasis (hex without #)
    /// </summary>
    public string AccentColor { get; set; } = "ED7D31";

    /// <summary>
    /// Text color for dark backgrounds (hex without #)
    /// </summary>
    public string TextLight { get; set; } = "FFFFFF";

    /// <summary>
    /// Text color for light backgrounds (hex without #)
    /// </summary>
    public string TextDark { get; set; } = "2D2D2D";

    /// <summary>
    /// Background color for light areas (hex without #)
    /// </summary>
    public string BackgroundLight { get; set; } = "F5F5F5";

    /// <summary>
    /// Theme name for display
    /// </summary>
    public string ThemeName { get; set; } = "Corporate";

    /// <summary>
    /// Heading/title font family (e.g., "Georgia", "Arial Black", "Segoe UI Semibold")
    /// </summary>
    public string HeadingFont { get; set; } = "Segoe UI Semibold";

    /// <summary>
    /// Body text font family (e.g., "Times New Roman", "Arial", "Segoe UI")
    /// </summary>
    public string BodyFont { get; set; } = "Segoe UI";

    /// <summary>
    /// Creates a default blue professional theme
    /// </summary>
    public static OrganizationTheme Default => new()
    {
        ThemeName = "Professional Blue",
        PrimaryColor = "2B579A",
        SecondaryColor = "5B9BD5",
        AccentColor = "ED7D31",
        HeadingFont = "Segoe UI Semibold",
        BodyFont = "Segoe UI"
    };
}
