using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class ComposeHtmlTests
{
    // SHA-256 (LF-normalized) of ConvertToHtml("Hello _world_") captured before ComposeHtml was added
    private const string LegacyHelloWorldHash = "B8AF650DD275366341574CE8A5E1C911729604B71701BFE2990D757A09C57D0E";

    private static OrganizationTheme Theme => OrganizationThemes.ForDomain("brightline.com");

    private static string Fragment(string body, bool allowTable = true) =>
        HtmlEmailFormatter.ComposeFragment(body, string.Empty, string.Empty, null, Theme, false, allowTable);

    private static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    [Fact]
    public void ConvertToHtml_LegacyOutputUnchanged()
    {
        var html = HtmlEmailFormatter.ConvertToHtml("Hello _world_");

        Assert.Contains("<p>Hello <em>world</em></p>", html);
        Assert.Equal(LegacyHelloWorldHash, Hash(html));
    }

    [Fact]
    public void Bold_AtMostTwoRuns_ThirdIsPlainWithoutAsterisks()
    {
        var html = Fragment("Please see **net 30** terms and **this**, **that**");

        Assert.Contains("<strong>net 30</strong>", html);
        Assert.Contains("<strong>this</strong>", html);
        Assert.Contains(", that", html);
        Assert.DoesNotContain("<strong>that</strong>", html);
        Assert.DoesNotContain("**", html);
        Assert.Equal(2, Count(html, "<strong>"));
    }

    [Fact]
    public void RawHtml_IsEncoded()
    {
        var html = Fragment("Hi <script>alert(1)</script> and <b>x</b> & more");

        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&amp; more", html);
    }

    [Fact]
    public void NoItalicsOrAutolinks()
    {
        var html = Fragment("Attached file_name_v2.xlsx, see http://example.com and _note_");

        Assert.Contains("file_name_v2.xlsx", html);
        Assert.Contains("http://example.com", html);
        Assert.Contains("_note_", html);
        Assert.DoesNotContain("<em>", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("href", html);
    }

    [Fact]
    public void BulletList_CappedAtSixItems_OverflowAsParagraphs()
    {
        var items = new[] { "one", "two", "three", "four", "five", "six", "seven", "eight" };
        var html = Fragment("Points:\n" + string.Join("\n", items.Select(i => "- " + i)));

        Assert.Equal(1, Count(html, "<ul"));
        Assert.Equal(6, Count(html, "<li"));
        Assert.Contains("<li>six</li>", html);
        Assert.DoesNotContain("<li>seven</li>", html);
        Assert.Contains("<p>- seven</p>", html);
        Assert.Contains("<p>- eight</p>", html);
    }

    [Fact]
    public void SecondList_RendersAsPlainParagraphs()
    {
        var html = Fragment("- a\n- b\n\nMore text\n\n- c\n- d");

        Assert.Equal(1, Count(html, "<ul"));
        Assert.Equal(2, Count(html, "<li"));
        Assert.Contains("- c", html);
        Assert.Contains("- d", html);
    }

    [Fact]
    public void PipeTable_RendersOneEncodedTable()
    {
        var html = Fragment("Summary below.\n\n| Item | Amount |\n|---|---|\n| CO 7 | $184,250 |\n| <i>x</i> | 1 |");

        Assert.Equal(1, Count(html, "<table"));
        Assert.Contains(">Item</th>", html);
        Assert.Contains(">CO 7</td>", html);
        Assert.Contains(">$184,250</td>", html);
        Assert.Contains("&lt;i&gt;x&lt;/i&gt;", html);
        Assert.DoesNotContain("<i>", html);
        Assert.Contains("#" + Theme.PrimaryColor, html);
        Assert.DoesNotContain("|---|", html);
    }

    [Fact]
    public void PipeTable_FiveColumns_RendersAsParagraphs()
    {
        var html = Fragment("| A | B | C | D | E |\n|---|---|---|---|---|\n| 1 | 2 | 3 | 4 | 5 |");

        Assert.DoesNotContain("<table", html);
        Assert.Contains("| A | B | C | D | E |", html);
    }

    [Fact]
    public void PipeTable_TooManyRows_RendersAsParagraphs()
    {
        var rows = string.Join("\n", Enumerable.Range(1, 7).Select(i => $"| r{i} | {i} |"));
        var html = Fragment("| A | B |\n|---|---|\n" + rows);

        Assert.DoesNotContain("<table", html);
    }

    [Fact]
    public void SecondTable_RendersAsParagraphs()
    {
        var table = "| Item | Amount |\n|---|---|\n| CO 7 | $184,250 |";
        var html = Fragment(table + "\n\nAnd again:\n\n" + table.Replace("CO 7", "CO 8"));

        Assert.Equal(1, Count(html, "<table"));
        Assert.Contains("| CO 8 | $184,250 |", html);
    }

    [Fact]
    public void AllowTableFalse_RendersTableAsParagraphs()
    {
        var html = Fragment("| Item | Amount |\n|---|---|\n| CO 7 | $184,250 |", allowTable: false);

        Assert.DoesNotContain("<table", html);
        Assert.Contains("| CO 7 | $184,250 |", html);
    }

    [Fact]
    public void PrivilegedBanner_PrecedesBody()
    {
        var html = HtmlEmailFormatter.ComposeFragment("Body text here", "", "", null, Theme, privilegedBanner: true);

        var banner = "<strong>PRIVILEGED &amp; CONFIDENTIAL - ATTORNEY-CLIENT COMMUNICATION</strong>";
        Assert.Contains(banner, html);
        Assert.True(html.IndexOf(banner, StringComparison.Ordinal) < html.IndexOf("Body text here", StringComparison.Ordinal));
    }

    [Fact]
    public void PrivilegedBanner_DoesNotConsumeBoldRuns()
    {
        var html = HtmlEmailFormatter.ComposeFragment("**a** and **b**", "", "", null, Theme, privilegedBanner: true);

        Assert.Contains("<strong>a</strong>", html);
        Assert.Contains("<strong>b</strong>", html);
    }

    [Fact]
    public void SignOffAndSignature_FollowBody()
    {
        var html = HtmlEmailFormatter.ComposeFragment("See attached.", "Thanks,", "Maria Delgado\nPresident", null, Theme, false);

        Assert.Contains("Thanks,", html);
        Assert.Contains("Maria Delgado<br>President", html);
        Assert.True(html.IndexOf("See attached.", StringComparison.Ordinal) < html.IndexOf("Thanks,", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Thanks,", StringComparison.Ordinal) < html.IndexOf("Maria Delgado", StringComparison.Ordinal));
    }

    [Fact]
    public void Signature_IsEncoded()
    {
        var html = HtmlEmailFormatter.ComposeFragment("Hi", "Thanks,", "R&D Lead <ops>", null, Theme, false);

        Assert.Contains("R&amp;D Lead &lt;ops&gt;", html);
    }

    [Fact]
    public void QuotedReply_RendersHeaderThenBlockquoteWithParent()
    {
        var parent = HtmlEmailFormatter.ComposeFragment("Original **point**", "Best,", "Jordan Pike", null, Theme, false);
        var quoted = new QuotedPart
        {
            IsForward = false,
            HeaderLines = new List<string> { "On Tue, Mar 19, 2024 at 9:14 AM, Jordan Pike <jordan@brightline.com> wrote:" },
            ParentFragment = parent
        };

        var html = HtmlEmailFormatter.ComposeFragment("Agreed.", "Thanks,", "Maria Delgado", quoted, Theme, false);

        var header = "On Tue, Mar 19, 2024 at 9:14 AM, Jordan Pike &lt;jordan@brightline.com&gt; wrote:";
        Assert.Contains(header, html);
        var blockquote = "<blockquote style=\"margin:0 0 0 .8ex;border-left:1px solid #ccc;padding-left:1ex\">";
        var start = html.IndexOf(blockquote, StringComparison.Ordinal);
        Assert.True(start > html.IndexOf(header, StringComparison.Ordinal));
        Assert.Contains(blockquote + parent + "</blockquote>", html);
        Assert.True(html.IndexOf("Maria Delgado", StringComparison.Ordinal) < start);
    }

    [Fact]
    public void QuotedForward_RendersForwardBlock()
    {
        var parent = HtmlEmailFormatter.ComposeFragment("Numbers attached.", "", "", null, Theme, false);
        var quoted = new QuotedPart
        {
            IsForward = true,
            HeaderLines = new List<string> { "From: Jordan Pike <jordan@brightline.com>", "Subject: CO 7" },
            ParentFragment = parent
        };

        var html = HtmlEmailFormatter.ComposeFragment("FYI", "", "", quoted, Theme, false);

        Assert.Contains("---------- Forwarded message ----------", html);
        Assert.Contains("From: Jordan Pike &lt;jordan@brightline.com&gt;", html);
        Assert.Contains(parent, html);
        Assert.DoesNotContain("<blockquote", html);
    }

    [Fact]
    public void ComposeHtml_WrapsInTemplateWithBanner()
    {
        var html = HtmlEmailFormatter.ComposeHtml("Hello there", "Thanks,", "Maria Delgado", null, Theme, false);
        var fragment = HtmlEmailFormatter.ComposeFragment("Hello there", "Thanks,", "Maria Delgado", null, Theme, false);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains(fragment, html);
        Assert.DoesNotContain("{CONTENT}", html);
        Assert.DoesNotContain("{BANNER_BASE64}", html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("class=\"disclaimer-banner\"", html);
    }

    [Fact]
    public void ComposeFragment_UsesThemeFont()
    {
        var theme = OrganizationThemes.ForDomain("brightline.com");
        var html = HtmlEmailFormatter.ComposeFragment("Hi", "", "", null, theme, false);

        Assert.Contains(theme.BodyFont, html);
    }

    [Fact]
    public void ComposeFragment_EmptyBody_IsSafe()
    {
        var html = HtmlEmailFormatter.ComposeFragment(string.Empty, string.Empty, string.Empty, null, OrganizationTheme.Default, false);

        Assert.NotNull(html);
        Assert.DoesNotContain("<ul", html);
    }

    [Fact]
    public void LongBoldRun_IsBounded()
    {
        var html = Fragment("**" + new string('x', 200) + "**");

        Assert.DoesNotContain("<strong>", html);
        Assert.DoesNotContain("**", html);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"))));
}
