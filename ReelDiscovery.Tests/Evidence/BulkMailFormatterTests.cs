using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class BulkMailFormatterTests
{
    private const string PromoCid = "fedcba9876543210@papertrends.com";

    private static OrganizationTheme Theme => OrganizationThemes.ForDomain("brightline.com");

    private static string Primary => "#" + Theme.PrimaryColor;

    private static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    private static BulkMailContent Newsletter(string body) => new()
    {
        Kind = BulkMailKind.Newsletter,
        Body = body,
        SenderName = "Paper Trends Weekly",
        Publication = "Paper Trends <Weekly>",
        IssueDate = new DateTimeOffset(2024, 3, 5, 8, 0, 0, TimeSpan.Zero),
        PostalAddress = "100 Main St, Scranton, PA 18503",
        Theme = Theme
    };

    private static BulkMailContent Spam(string body) => new()
    {
        Kind = BulkMailKind.Spam,
        Body = body,
        SenderName = "Deals Daily",
        Theme = Theme
    };

    private static BulkMailContent Personal(string body, string signOff) => new()
    {
        Kind = BulkMailKind.Personal,
        Body = body,
        SignOff = signOff,
        SenderName = "Jen",
        Theme = Theme
    };

    private static List<InlineImageRef> Promo() =>
        new() { new InlineImageRef { ContentId = PromoCid, Title = "Spring <sale>" } };

    [Fact]
    public void Newsletter_HeadedSections_MastheadAndFooter()
    {
        var html = BulkMailFormatter.ComposeFragment(Newsletter("**Market Watch**\nPaper prices rose.\n**Events**\nTrade show in May."));

        Assert.Equal(2, Count(html, "<h3"));
        Assert.Contains($"color: {Primary};\">Market Watch</h3>", html);
        Assert.Contains($"color: {Primary};\">Events</h3>", html);
        Assert.True(html.IndexOf("Market Watch", StringComparison.Ordinal) < html.IndexOf("Paper prices rose.", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Events", StringComparison.Ordinal) < html.IndexOf("Trade show in May.", StringComparison.Ordinal));
        Assert.DoesNotContain("**", html);

        Assert.Contains($"font-size: 18px; font-weight: bold; color: {Primary};\">Paper Trends &lt;Weekly&gt;</div>", html);
        Assert.Contains("font-size: 12px; color: #767676;\">March 5, 2024</div>", html);
        Assert.Contains("max-width: 600px", html);
        Assert.Contains("<table", html);
        Assert.Contains("You are receiving this because you subscribed to Paper Trends &lt;Weekly&gt;. "
            + "Unsubscribe | Manage preferences | 100 Main St, Scranton, PA 18503", html);
        Assert.Contains("font-size: 11px; color: #767676;", html);
    }

    [Fact]
    public void Newsletter_NoHeadings_SplitsIntoAtMostFourSectionsWithoutHeadings()
    {
        var body = string.Join("\n\n", Enumerable.Range(1, 6).Select(i => $"Block {i} text."));
        var html = BulkMailFormatter.ComposeFragment(Newsletter(body));

        Assert.DoesNotContain("<h3", html);
        Assert.Equal(4, Count(html, "class=\"section\""));
        for (var i = 1; i <= 6; i++)
            Assert.Contains($"Block {i} text.", html);
    }

    [Fact]
    public void Newsletter_MoreThanFourHeadings_CapsSections()
    {
        var body = string.Join("\n", Enumerable.Range(1, 6).Select(i => $"**Head {i}**\nText {i}."));
        var html = BulkMailFormatter.ComposeFragment(Newsletter(body));

        Assert.Equal(4, Count(html, "<h3"));
        Assert.Equal(4, Count(html, "class=\"section\""));
        Assert.Contains("Text 6.", html);
    }

    [Fact]
    public void Newsletter_BlankPublication_UsesSenderName()
    {
        var content = Newsletter("Hello readers.");
        content.Publication = "  ";
        var html = BulkMailFormatter.ComposeFragment(content);

        Assert.Contains(">Paper Trends Weekly</div>", html);
        Assert.Contains("subscribed to Paper Trends Weekly.", html);
    }

    [Fact]
    public void Spam_OfferLineButtonAndFinePrint()
    {
        var content = Spam("Save 70% on **everything** today!\n\nOur biggest sale of the year.");
        content.InlineImages = Promo();
        var html = BulkMailFormatter.ComposeFragment(content);

        Assert.Contains("<strong>Save 70% on everything today!</strong>", html);
        Assert.Equal(1, Count(html, "<span"));
        Assert.Contains($"padding: 10px 20px; background-color: {Primary};", html);
        Assert.Contains("font-size: 11px; color: #767676;", html);
        Assert.Contains("You are receiving this offer because your address is on our mailing list. Reply STOP to opt out.", html);
        Assert.Contains("Our biggest sale of the year.", html);
        Assert.Contains($"<img src=\"cid:{PromoCid}\" alt=\"Spring &lt;sale&gt;\" style=\"max-width:100%\">", html);
    }

    [Fact]
    public void Personal_PlainParagraphsDefaultFontNoMastheadOrFooter()
    {
        var html = BulkMailFormatter.ComposeFragment(Personal("Hey! Dinner Saturday?\n\nBring the kids.", "Jen"));
        var defaultFont = HtmlEmailFormatter.GetFontStack(OrganizationTheme.Default.BodyFont);

        Assert.Contains($"font-family: {defaultFont};", html);
        Assert.Contains("<p>Hey! Dinner Saturday?</p>", html);
        Assert.Contains("<p>Bring the kids.</p>", html);
        Assert.Contains("<p>Jen</p>", html);
        Assert.DoesNotContain("<table", html);
        Assert.DoesNotContain("<h3", html);
        Assert.DoesNotContain("Unsubscribe", html);
        Assert.DoesNotContain("<span", html);
    }

    [Fact]
    public void Personal_EmptySignOff_RendersNoSignOffLine()
    {
        var html = BulkMailFormatter.ComposeFragment(Personal("See you soon.", "  "));

        Assert.Equal(1, Count(html, "<p>"));
    }

    [Theory]
    [InlineData(BulkMailKind.Newsletter)]
    [InlineData(BulkMailKind.Spam)]
    [InlineData(BulkMailKind.Personal)]
    public void Compose_KeepsDisclaimerBannerTemplate(BulkMailKind kind)
    {
        var content = Newsletter("**Section**\nText.");
        content.Kind = kind;
        var business = HtmlEmailFormatter.ComposeHtml("Hi", "", "", null, Theme, false);

        var html = BulkMailFormatter.Compose(content);

        Assert.Contains("class=\"disclaimer-banner\"", business);
        Assert.Contains("class=\"disclaimer-banner\"", html);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains(BulkMailFormatter.ComposeFragment(content), html);
        Assert.DoesNotContain("{CONTENT}", html);
    }

    [Theory]
    [InlineData(BulkMailKind.Newsletter)]
    [InlineData(BulkMailKind.Spam)]
    [InlineData(BulkMailKind.Personal)]
    public void ModelText_IsEncoded_NoScriptsOrLinks(BulkMailKind kind)
    {
        var hostile = "<script>alert(1)</script> <a href=\"http://x\">click</a>";
        var content = new BulkMailContent
        {
            Kind = kind,
            Body = hostile + "\n\n**" + hostile + "**\n" + hostile,
            SignOff = hostile,
            SenderName = hostile,
            Publication = hostile,
            PostalAddress = hostile,
            IssueDate = new DateTimeOffset(2024, 3, 5, 8, 0, 0, TimeSpan.Zero),
            Theme = new OrganizationTheme { PrimaryColor = "red;background:url(x)", BodyFont = "x;}</style><script>" }
        };

        foreach (var html in new[] { BulkMailFormatter.ComposeFragment(content), BulkMailFormatter.Compose(content) })
        {
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.DoesNotContain("<script", html);
            Assert.DoesNotContain("<a ", html);
            // Encoded model text keeps the letters "href=" but never a live attribute
            Assert.DoesNotContain("href=\"", html);
            Assert.DoesNotContain("url(x)", html);
        }
    }

    [Fact]
    public void BoldRuns_CappedPerSection()
    {
        var html = BulkMailFormatter.ComposeFragment(Newsletter("**News**\n**a** and **b** and **c** too."));

        Assert.Contains("<strong>a</strong>", html);
        Assert.Contains("<strong>b</strong>", html);
        Assert.DoesNotContain("<strong>c</strong>", html);
        Assert.Contains(" c too.", html);
        Assert.DoesNotContain("**", html);
    }

    [Theory]
    [InlineData(BulkMailKind.Newsletter)]
    [InlineData(BulkMailKind.Spam)]
    public void ImagePlaceholders_RenderTitleInsteadOfCid(BulkMailKind kind)
    {
        var content = Newsletter("**Deals**\nSpring is here.");
        content.Kind = kind;
        content.InlineImages = Promo();

        content.ImagePlaceholders = true;
        var quoted = BulkMailFormatter.ComposeFragment(content);
        Assert.Contains("[image: Spring &lt;sale&gt;]", quoted);
        Assert.DoesNotContain("cid:", quoted);

        content.ImagePlaceholders = false;
        var live = BulkMailFormatter.ComposeFragment(content);
        Assert.Contains($"<img src=\"cid:{PromoCid}\"", live);
        Assert.DoesNotContain("[image:", live);
    }

    [Fact]
    public void EmptyContent_IsSafe()
    {
        foreach (var kind in new[] { BulkMailKind.Newsletter, BulkMailKind.Spam, BulkMailKind.Personal })
        {
            var html = BulkMailFormatter.Compose(new BulkMailContent { Kind = kind });
            Assert.NotNull(html);
            Assert.DoesNotContain("<h3", html);
        }
    }
}
