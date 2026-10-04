using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence.Golden;

// Fixture inputs for ComposeHtmlGolden; the expected outputs were captured from the Phase 5 formatter
internal static partial class ComposeHtmlGoldenFixtures
{
    private static OrganizationTheme Theme => OrganizationThemes.ForDomain("brightline.com");

    // The 45 KB disclaimer PNG is masked so the frozen strings stay small; the template around it is compared exactly
    [GeneratedRegex(@"data:image/png;base64,[A-Za-z0-9+/=]+")]
    private static partial Regex BannerDataRegex();

    public static string MaskBanner(string html) => BannerDataRegex().Replace(html, "data:image/png;base64,{BANNER}");

    public static IEnumerable<(string Name, string Fragment, string Html)> All()
    {
        yield return Build("Plain", "Hi team,\n\nPlease see **net 30** terms.\n- one\n- two\n\nR&D <ops> note.",
            "Thanks,", "Maria Delgado\nPresident", null, false, true);

        var replyParent = HtmlEmailFormatter.ComposeFragment("Original **point**", "Best,", "Jordan Pike", null, Theme, false);
        yield return Build("QuotedReply", "Agreed.", "Thanks,", "Maria Delgado", new QuotedPart
        {
            IsForward = false,
            HeaderLines = new List<string> { "On Tue, Mar 19, 2024 at 9:14 AM, Jordan Pike <jordan@brightline.com> wrote:" },
            ParentFragment = replyParent
        }, false, true);

        var forwardParent = HtmlEmailFormatter.ComposeFragment("Numbers attached.", "", "", null, Theme, false);
        yield return Build("Forward", "FYI", "", "", new QuotedPart
        {
            IsForward = true,
            HeaderLines = new List<string> { "From: Jordan Pike <jordan@brightline.com>", "Subject: CO 7" },
            ParentFragment = forwardParent
        }, false, true);

        yield return Build("Privileged", "**a** and **b** need review.", "Regards,", "Counsel", null, true, true);

        yield return Build("Table", "Summary below.\n\n| Item | Amount |\n|---|---|\n| CO 7 | $184,250 |\n| CO 8 | $12,000 |",
            "Thanks,", "Maria Delgado", null, false, true);
    }

    private static (string, string, string) Build(string name, string body, string signOff, string signature,
        QuotedPart? quoted, bool privileged, bool allowTable) =>
        (name,
            HtmlEmailFormatter.ComposeFragment(body, signOff, signature, quoted, Theme, privileged, allowTable),
            MaskBanner(HtmlEmailFormatter.ComposeHtml(body, signOff, signature, quoted, Theme, privileged, allowTable)));
}
