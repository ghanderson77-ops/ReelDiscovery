using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services;

public class ThemeGenerator
{
    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict JSON schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(ThemeApiResponse) };

    public ThemeGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// Generates organization themes for all unique domains found in the character list.
    /// </summary>
    public async Task<Dictionary<string, OrganizationTheme>> GenerateThemesForDomainsAsync(
        string topic,
        List<Character> characters,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var themes = new Dictionary<string, OrganizationTheme>(StringComparer.OrdinalIgnoreCase);

        // Get unique domains and their organization names
        var domainOrgs = characters
            .GroupBy(c => c.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First().Organization,
                StringComparer.OrdinalIgnoreCase);

        if (domainOrgs.Count == 0)
            return themes;

        progress?.Report($"Generating organization themes for {domainOrgs.Count} organizations...");

        var systemPrompt = @"You are a brand designer creating professional color schemes and typography for corporate documents.
Given organizations from a specific topic/universe, create appropriate themes that match each organization's character and industry.

Guidelines:
- Colors should be professional and readable (good contrast)
- The primary color is used for title backgrounds and header bars
- The accent color is used for emphasis lines and highlights
- Consider the organization's industry, personality, and any brand associations
- For fictional organizations from TV shows/movies, try to match their canonical branding if known

Font Guidelines - choose fonts that match the organization's personality:
- Law firms, banks, traditional companies: Serif fonts like 'Georgia', 'Times New Roman', 'Garamond'
- Tech companies, startups, modern firms: Clean sans-serif like 'Segoe UI', 'Arial', 'Calibri'
- Creative agencies, media companies: Modern fonts like 'Century Gothic', 'Trebuchet MS'
- Government, formal institutions: Traditional like 'Times New Roman', 'Book Antiqua'
- Healthcare, scientific: Clean professional like 'Calibri', 'Arial'
- The heading font should be bolder/more distinctive than the body font";

        var orgList = string.Join("\n", domainOrgs.Select(kv => $"- {kv.Value} ({kv.Key})"));

        var userPrompt = $@"Topic/Universe: {topic}

Generate unique color themes for the following organizations:
{orgList}

Each theme should reflect the organization's character within the context of ""{topic}"".

For example:
- A law firm might use navy blue and gold with Georgia/Times New Roman fonts
- A tech startup might use vibrant blue and orange with Segoe UI/Calibri fonts
- A government agency might use formal blue and red with Times New Roman
- Dunder Mifflin (The Office) might use their classic blue and gray with standard corporate fonts

Available fonts (use ONLY these - they are commonly installed):
Serif: Georgia, Times New Roman, Garamond, Book Antiqua, Palatino Linotype
Sans-serif: Segoe UI, Arial, Calibri, Trebuchet MS, Century Gothic, Verdana, Tahoma";

        var llmResult = await _llm.GenerateAsync<ThemeApiResponse>(new LlmRequest
        {
            Tier = LlmTier.Volume,
            OperationName = "Theme Generation",
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxOutputTokens = 8_000
        }, ct);

        // Bad key / spend limit must stop the run; other failures fall back to default themes
        if (llmResult.IsFatal)
            throw new LlmCallException(llmResult);

        var response = llmResult.Outcome == LlmOutcome.Succeeded ? llmResult.Value : null;

        if (response?.Themes != null)
        {
            foreach (var t in response.Themes)
            {
                if (string.IsNullOrEmpty(t.Domain))
                    continue;

                themes[t.Domain] = new OrganizationTheme
                {
                    Domain = t.Domain,
                    OrganizationName = t.OrganizationName ?? domainOrgs.GetValueOrDefault(t.Domain, "Organization"),
                    ThemeName = t.ThemeName ?? "Corporate",
                    PrimaryColor = ThemeSanitizer.SanitizeHexColor(t.PrimaryColor) ?? "2B579A",
                    SecondaryColor = ThemeSanitizer.SanitizeHexColor(t.SecondaryColor) ?? "5B9BD5",
                    AccentColor = ThemeSanitizer.SanitizeHexColor(t.AccentColor) ?? "ED7D31",
                    HeadingFont = ThemeSanitizer.SanitizeFont(t.HeadingFont) ?? "Segoe UI Semibold",
                    BodyFont = ThemeSanitizer.SanitizeFont(t.BodyFont) ?? "Segoe UI"
                };
            }
        }

        // Ensure all domains have a theme (use defaults for any missing)
        foreach (var domain in domainOrgs.Keys)
        {
            if (!themes.ContainsKey(domain))
            {
                themes[domain] = new OrganizationTheme
                {
                    Domain = domain,
                    OrganizationName = domainOrgs[domain],
                    ThemeName = "Default Corporate"
                };
            }
        }

        progress?.Report($"Generated {themes.Count} presentation themes.");
        return themes;
    }

    internal class ThemeApiResponse
    {
        [JsonPropertyName("themes")]
        [Description("One theme per organization")]
        public List<ThemeDto> Themes { get; set; } = new();
    }

    internal class ThemeDto
    {
        [JsonPropertyName("domain")]
        [Description("The email domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("organizationName")]
        public string OrganizationName { get; set; } = string.Empty;

        [JsonPropertyName("themeName")]
        [Description("Descriptive name like 'Corporate Navy' or 'Tech Vibrant'")]
        public string ThemeName { get; set; } = string.Empty;

        [JsonPropertyName("primaryColor")]
        [Description("RRGGBB (hex without #, main brand color)")]
        public string PrimaryColor { get; set; } = string.Empty;

        [JsonPropertyName("secondaryColor")]
        [Description("RRGGBB (complementary color)")]
        public string SecondaryColor { get; set; } = string.Empty;

        [JsonPropertyName("accentColor")]
        [Description("RRGGBB (highlight/emphasis color)")]
        public string AccentColor { get; set; } = string.Empty;

        [JsonPropertyName("headingFont")]
        [Description("Font for titles/headings")]
        public string HeadingFont { get; set; } = string.Empty;

        [JsonPropertyName("bodyFont")]
        [Description("Font for body text")]
        public string BodyFont { get; set; } = string.Empty;
    }
}
