using System.Text;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Documents;

public class CalendarServiceTests
{
    private readonly CalendarService _calendar = new();

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static CalendarInviteSpec Spec(Action<CalendarInviteSpec>? change = null)
    {
        var spec = new CalendarInviteSpec
        {
            Uid = "evt-0042@dundermifflin.com",
            DtStamp = new DateTimeOffset(2024, 3, 1, 8, 15, 0, TimeSpan.FromHours(-5)),
            Start = new DateTimeOffset(2024, 3, 5, 10, 0, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2024, 3, 5, 11, 0, 0, TimeSpan.FromHours(-5)),
            Summary = "Paper supply review",
            Description = "Walk through the Q1 invoices.\nBring the ledger.",
            Location = "Conference Room B",
            OrganizerName = "Michael Scott",
            OrganizerEmail = "michael.scott@dundermifflin.com",
            Attendees = new List<(string Name, string Email)> { ("Dwight Schrute", "dwight.schrute@dundermifflin.com") }
        };
        change?.Invoke(spec);
        return spec;
    }

    private static string Text(byte[] bytes) => StrictUtf8.GetString(bytes);

    private static string Unfold(string ics) => ics.Replace("\r\n ", string.Empty);

    private static List<string> UnfoldedLines(byte[] bytes) =>
        Unfold(Text(bytes)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

    [Fact]
    public void SameSpec_SameBytes()
    {
        var first = _calendar.CreateInvite(Spec());
        var second = _calendar.CreateInvite(Spec());

        Assert.Equal(first, second);
    }

    [Fact]
    public void UidAndDtStampComeFromSpec()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec()));

        Assert.Contains("UID:evt-0042@dundermifflin.com", lines);
        Assert.Contains("DTSTAMP:20240301T131500Z", lines);
    }

    [Fact]
    public void StartAndEndAreUtc()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec()));

        Assert.Contains("DTSTART:20240305T150000Z", lines);
        Assert.Contains("DTEND:20240305T160000Z", lines);
    }

    [Fact]
    public void NoBomAndCrlfEverywhere()
    {
        var bytes = _calendar.CreateInvite(Spec());
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        var text = Text(bytes);
        Assert.EndsWith("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));
        Assert.DoesNotContain("\r", text.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void LongSummary_FoldsAt75Octets()
    {
        var summary = string.Concat(Enumerable.Repeat("Quarterly paper supply review ", 7))[..200];
        var bytes = _calendar.CreateInvite(Spec(s => s.Summary = summary));
        var text = Text(bytes);

        var rawLines = text.Split("\r\n");
        Assert.Equal(string.Empty, rawLines[^1]); // ends with CRLF
        foreach (var line in rawLines[..^1])
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"Line too long ({Encoding.UTF8.GetByteCount(line)}): {line}");

        Assert.Contains(rawLines, l => l.StartsWith(' ') && !l.StartsWith("  "));
        Assert.Contains("SUMMARY:" + summary, UnfoldedLines(bytes));
    }

    [Fact]
    public void MultiByteSummary_NeverSplitsASequence()
    {
        var summary = string.Concat(Enumerable.Repeat("café 漢字 \U0001F600 ", 15));
        var bytes = _calendar.CreateInvite(Spec(s => s.Summary = summary));

        // Every physical line must decode on its own (no split UTF-8 sequence or surrogate pair)
        var start = 0;
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n')
            {
                var lineLength = i - start;
                Assert.True(lineLength <= 75, $"Line of {lineLength} octets");
                StrictUtf8.GetString(bytes, start, lineLength); // throws on a split sequence
                start = i + 2;
            }
        }

        Assert.Contains("SUMMARY:" + summary.Trim(), UnfoldedLines(bytes));
    }

    [Fact]
    public void OrganizerCn_IsQuotedWithInnerQuotesReplaced()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec(s => s.OrganizerName = "Scott, Michael \"Mike\"")));

        Assert.Contains("ORGANIZER;CN=\"Scott, Michael 'Mike'\":mailto:michael.scott@dundermifflin.com", lines);
    }

    [Fact]
    public void ControlCharacters_CannotInjectLines()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec(s =>
        {
            s.Location = "Room 1, Floor 2; Annex \\ East\r\nATTENDEE:mailto:evil@x";
            s.Attendees = new List<(string Name, string Email)>();
        })));

        Assert.DoesNotContain(lines, l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        var location = Assert.Single(lines, l => l.StartsWith("LOCATION:", StringComparison.Ordinal));
        Assert.Contains("Room 1\\, Floor 2\\; Annex \\\\ East", location);
    }

    [Fact]
    public void BadMailto_DropsAttendee()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec(s =>
            s.Attendees = new List<(string Name, string Email)> { ("Nobody", "bad address<>"), ("Pam Beesly", "pam<>.beesly@dundermifflin.com") })));

        var attendees = lines.Where(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal)).ToList();
        var attendee = Assert.Single(attendees);
        Assert.EndsWith(":mailto:pam.beesly@dundermifflin.com", attendee);
        Assert.Contains("ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE", attendee);
        Assert.Contains("CN=\"Pam Beesly\"", attendee);
    }

    [Fact]
    public void MethodRequest_AndExactlyOneEvent()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec()));

        Assert.Contains("METHOD:REQUEST", lines);
        Assert.Single(lines, l => l == "BEGIN:VEVENT");
        Assert.Single(lines, l => l == "END:VEVENT");
        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("ReelDiscovery", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Description_KeepsEscapedLineBreaks()
    {
        var lines = UnfoldedLines(_calendar.CreateInvite(Spec()));

        Assert.Contains("DESCRIPTION:Walk through the Q1 invoices.\\nBring the ledger.", lines);
    }

    [Fact]
    public void ForOrganization_CarriesNameAndDomainColors()
    {
        var byDomain = OrganizationThemes.ForDomain("dundermifflin.com");
        var byOrg = OrganizationThemes.ForOrganization("dundermifflin.com", "  Dunder Mifflin ");

        Assert.Equal("Dunder Mifflin", byOrg.OrganizationName);
        Assert.Equal(byDomain.PrimaryColor, byOrg.PrimaryColor);
        Assert.Equal(byDomain.SecondaryColor, byOrg.SecondaryColor);
        Assert.Equal(byDomain.AccentColor, byOrg.AccentColor);
        Assert.Equal(byDomain.HeadingFont, byOrg.HeadingFont);
        Assert.Equal(byDomain.BodyFont, byOrg.BodyFont);
        Assert.Equal(string.Empty, byDomain.OrganizationName);
        Assert.Equal(string.Empty, OrganizationThemes.ForOrganization("dundermifflin.com", null).OrganizationName);
    }
}
