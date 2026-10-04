using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Services;

namespace ReelDiscovery.Helpers;

public enum BulkMailKind
{
    Newsletter,
    Spam,
    Personal
}

/// <summary>
/// Inputs for one bulk (non-business) message. Every string is untrusted model text and is HTML-encoded on render.
/// </summary>
public class BulkMailContent
{
    public BulkMailKind Kind { get; set; }
    public string Body { get; set; } = string.Empty;
    public string SignOff { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// Newsletter masthead name; falls back to <see cref="SenderName"/> when blank.
    /// </summary>
    public string Publication { get; set; } = string.Empty;

    public DateTimeOffset IssueDate { get; set; }
    public string PostalAddress { get; set; } = string.Empty;
    public OrganizationTheme Theme { get; set; } = OrganizationTheme.Default;
    public List<InlineImageRef> InlineImages { get; set; } = new();

    /// <summary>
    /// true when the fragment will be quoted by a reply: images render as [image: title] (Pitfall 10);
    /// EmailComposer sets it for QuoteFragment (06-09).
    /// </summary>
    public bool ImagePlaceholders { get; set; }
}

/// <summary>
/// Newsletter, spam and personal HTML variants (D-22). Every variant keeps the disclaimer banner template
/// (NOISE-05: dropping it would be a signal), encodes every model string first and renders only allowlisted markup:
/// no links, no scripts and no model-supplied style (colors via SanitizeHexColor, fonts via SanitizeFont).
/// </summary>
public static partial class BulkMailFormatter
{
    public const int MaxNewsletterSections = 4;

    private const string MutedColor = "#767676";
    private const string SpamFinePrint = "You are receiving this offer because your address is on our mailing list. Reply STOP to opt out.";

    // Fixed call-to-action labels; the model never supplies button text or targets (T-06-21)
    private static readonly string[] SpamButtonLabels = { "Claim Your Offer", "Shop Now", "Get Started", "See Details", "Learn More" };

    // A whole line in **bold** is a newsletter section heading
    [GeneratedRegex(@"^\s*\*\*(.+?)\*\*\s*$")]
    private static partial Regex HeadingLineRegex();

    public static string Compose(BulkMailContent content)
    {
        var theme = content.Kind == BulkMailKind.Personal ? OrganizationTheme.Default : content.Theme ?? OrganizationTheme.Default;
        return HtmlEmailFormatter.WrapInTemplate(ComposeFragment(content), SanitizedTheme(theme));
    }

    public static string ComposeFragment(BulkMailContent content)
    {
        return content.Kind switch
        {
            BulkMailKind.Newsletter => ComposeNewsletter(content),
            BulkMailKind.Spam => ComposeSpam(content),
            _ => ComposePersonal(content)
        };
    }

    private static string ComposeNewsletter(BulkMailContent content)
    {
        var t = content.Theme ?? OrganizationTheme.Default;
        var primary = PrimaryColor(t);
        var fontStack = FontStack(t);
        var publication = string.IsNullOrWhiteSpace(content.Publication) ? (content.SenderName ?? string.Empty).Trim() : content.Publication.Trim();

        var html = new StringBuilder();
        html.Append($"<div style=\"font-family: {fontStack};\">\n");
        html.Append("<table role=\"presentation\" style=\"width: 100%; max-width: 600px; border-collapse: collapse;\">\n");

        // Masthead
        html.Append($"<tr><td style=\"padding: 12px 0; border-bottom: 2px solid {primary};\">\n");
        if (publication.Length > 0)
            html.Append($"<div style=\"font-size: 18px; font-weight: bold; color: {primary};\">{HtmlEmailFormatter.Encode(publication)}</div>\n");
        if (content.IssueDate != default)
            html.Append($"<div style=\"font-size: 12px; color: {MutedColor};\">{HtmlEmailFormatter.Encode(content.IssueDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture))}</div>\n");
        html.Append("</td></tr>\n");

        // Sections
        html.Append("<tr><td style=\"padding: 8px 0;\">\n");
        foreach (var (heading, lines) in SplitSections(content.Body ?? string.Empty))
        {
            html.Append("<div class=\"section\" style=\"margin: 0 0 12px 0;\">\n");
            if (!string.IsNullOrWhiteSpace(heading))
                html.Append($"<h3 style=\"margin: 16px 0 6px 0; font-size: 15px; font-weight: bold; color: {primary};\">{HtmlEmailFormatter.Encode(heading.Trim())}</h3>\n");
            AppendParagraphs(html, lines, new HtmlEmailFormatter.ComposeState());
            html.Append("</div>\n");
        }
        HtmlEmailFormatter.AppendInlineImages(html, content.InlineImages, content.ImagePlaceholders);
        html.Append("</td></tr>\n");

        // Plain-text footer: no live links at all
        var footer = $"You are receiving this because you subscribed to {(publication.Length > 0 ? publication : "our newsletter")}. Unsubscribe | Manage preferences";
        if (!string.IsNullOrWhiteSpace(content.PostalAddress))
            footer += " | " + content.PostalAddress.Trim();
        html.Append($"<tr><td style=\"padding-top: 16px; font-size: 11px; color: {MutedColor};\">{HtmlEmailFormatter.Encode(footer)}</td></tr>\n");

        html.Append("</table>\n</div>\n");
        return html.ToString();
    }

    private static string ComposeSpam(BulkMailContent content)
    {
        var t = content.Theme ?? OrganizationTheme.Default;
        var primary = PrimaryColor(t);
        var fontStack = FontStack(t);
        var lines = SplitLines(content.Body ?? string.Empty);

        var offerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        var offer = offerIndex >= 0 ? lines[offerIndex].Trim() : string.Empty;
        var rest = offerIndex >= 0 ? lines.Skip(offerIndex + 1).ToArray() : lines;

        var html = new StringBuilder();
        html.Append($"<div style=\"font-family: {fontStack};\">\n");
        if (offer.Length > 0)
            html.Append($"<p style=\"font-size: 18px; margin: 0 0 12px 0;\"><strong>{HtmlEmailFormatter.Encode(offer).Replace("**", string.Empty)}</strong></p>\n");
        AppendParagraphs(html, rest, new HtmlEmailFormatter.ComposeState());
        HtmlEmailFormatter.AppendInlineImages(html, content.InlineImages, content.ImagePlaceholders);

        var label = SpamButtonLabels[StableIndex(offer, SpamButtonLabels.Length)];
        html.Append("<p style=\"margin: 16px 0;\"><span style=\"display: inline-block; padding: 10px 20px; ")
            .Append($"background-color: {primary}; color: #FFFFFF; font-weight: bold; border-radius: 4px;\">")
            .Append(HtmlEmailFormatter.Encode(label))
            .Append("</span></p>\n");
        html.Append($"<p style=\"margin-top: 20px; font-size: 11px; color: {MutedColor};\">{HtmlEmailFormatter.Encode(SpamFinePrint)}</p>\n");
        html.Append("</div>\n");
        return html.ToString();
    }

    private static string ComposePersonal(BulkMailContent content)
    {
        // Personal mail ignores the sender theme: consumer senders have no branding
        var fontStack = FontStack(OrganizationTheme.Default);

        var html = new StringBuilder();
        html.Append($"<div style=\"font-family: {fontStack};\">\n");
        AppendParagraphs(html, SplitLines(content.Body ?? string.Empty), new HtmlEmailFormatter.ComposeState());
        HtmlEmailFormatter.AppendInlineImages(html, content.InlineImages, content.ImagePlaceholders);
        if (!string.IsNullOrWhiteSpace(content.SignOff))
            html.Append("<p>").Append(string.Join("<br>", SplitLines(content.SignOff.Trim()).Select(l => HtmlEmailFormatter.Encode(l.Trim())))).Append("</p>\n");
        html.Append("</div>\n");
        return html.ToString();
    }

    // Heading lines start sections (at most MaxNewsletterSections; later headings stay as text in the last one).
    // Without headings, blank-line blocks become sections and the overflow merges into the last section.
    private static List<(string? Heading, List<string> Lines)> SplitSections(string body)
    {
        var lines = SplitLines(body);
        var sections = new List<(string? Heading, List<string> Lines)>();

        if (lines.Any(l => HeadingLineRegex().IsMatch(l)))
        {
            var current = (Heading: (string?)null, Lines: new List<string>());
            var headings = 0;
            foreach (var line in lines)
            {
                var match = HeadingLineRegex().Match(line);
                if (match.Success && headings < MaxNewsletterSections)
                {
                    if (current.Heading != null || current.Lines.Any(l => !string.IsNullOrWhiteSpace(l)))
                        sections.Add(current);
                    current = (match.Groups[1].Value.Replace("**", string.Empty), new List<string>());
                    headings++;
                    continue;
                }
                current.Lines.Add(line);
            }
            if (current.Heading != null || current.Lines.Any(l => !string.IsNullOrWhiteSpace(l)))
                sections.Add(current);
            return sections;
        }

        var blocks = new List<List<string>>();
        var block = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (block.Count > 0)
                    blocks.Add(block);
                block = new List<string>();
                continue;
            }
            block.Add(line);
        }
        if (block.Count > 0)
            blocks.Add(block);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (i < MaxNewsletterSections)
            {
                sections.Add((null, blocks[i]));
                continue;
            }
            // Keep the paragraph break inside the merged last section
            sections[^1].Lines.Add(string.Empty);
            sections[^1].Lines.AddRange(blocks[i]);
        }
        return sections;
    }

    // Blank lines split paragraphs; lines within a paragraph join with <br>; bold capped by ComposeInline
    private static void AppendParagraphs(StringBuilder html, IEnumerable<string> lines, HtmlEmailFormatter.ComposeState state)
    {
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count == 0)
                return;
            html.Append("<p>").Append(string.Join("<br>", paragraph.Select(l => HtmlEmailFormatter.ComposeInline(l, state)))).Append("</p>\n");
            paragraph.Clear();
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }
            paragraph.Add(line.Trim());
        }
        Flush();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToArray();

    // WrapInTemplate writes theme colors and fonts into the <style> block verbatim, so only sanitized values reach it (T-06-19)
    private static OrganizationTheme SanitizedTheme(OrganizationTheme theme)
    {
        var d = OrganizationTheme.Default;
        return new OrganizationTheme
        {
            Domain = theme.Domain,
            OrganizationName = theme.OrganizationName,
            ThemeName = theme.ThemeName,
            PrimaryColor = ThemeGenerator.SanitizeHexColor(theme.PrimaryColor) ?? d.PrimaryColor,
            SecondaryColor = ThemeGenerator.SanitizeHexColor(theme.SecondaryColor) ?? d.SecondaryColor,
            AccentColor = ThemeGenerator.SanitizeHexColor(theme.AccentColor) ?? d.AccentColor,
            TextLight = ThemeGenerator.SanitizeHexColor(theme.TextLight) ?? d.TextLight,
            TextDark = ThemeGenerator.SanitizeHexColor(theme.TextDark) ?? d.TextDark,
            BackgroundLight = ThemeGenerator.SanitizeHexColor(theme.BackgroundLight) ?? d.BackgroundLight,
            HeadingFont = ThemeGenerator.SanitizeFont(theme.HeadingFont) ?? d.HeadingFont,
            BodyFont = ThemeGenerator.SanitizeFont(theme.BodyFont) ?? d.BodyFont
        };
    }

    private static string PrimaryColor(OrganizationTheme theme) =>
        "#" + (ThemeGenerator.SanitizeHexColor(theme.PrimaryColor) ?? "2B579A");

    private static string FontStack(OrganizationTheme theme) =>
        HtmlEmailFormatter.GetFontStack(ThemeGenerator.SanitizeFont(theme.BodyFont) ?? "Segoe UI");

    // string.GetHashCode is randomized per process; composition must be deterministic for Retry Failed rewrites
    private static int StableIndex(string text, int count)
    {
        var hash = 17;
        foreach (var c in text)
            hash = unchecked(hash * 31 + c);
        return (int)((uint)hash % (uint)count);
    }
}
