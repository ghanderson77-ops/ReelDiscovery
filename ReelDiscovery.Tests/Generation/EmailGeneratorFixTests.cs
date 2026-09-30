using ReelDiscovery.Models;
using ReelDiscovery.Services;

namespace ReelDiscovery.Tests.Generation;

public class EmailGeneratorFixTests
{
    private static List<EmailMessage> Messages(int count) =>
        Enumerable.Range(0, count).Select(i => new EmailMessage { Subject = $"m{i}" }).ToList();

    [Fact]
    public void ResolveReferencedEmail_IsBatchRelative()
    {
        var messages = Messages(20);

        var referenced = EmailGenerator.ResolveReferencedEmail(messages, 15, 2);

        Assert.Same(messages[17], referenced);
    }

    [Fact]
    public void ResolveReferencedEmail_OutOfBatchRange_FallsBackToLast()
    {
        var messages = Messages(17);

        Assert.Same(messages[16], EmailGenerator.ResolveReferencedEmail(messages, 15, 5));
        Assert.Same(messages[16], EmailGenerator.ResolveReferencedEmail(messages, 15, -1));
        Assert.Null(EmailGenerator.ResolveReferencedEmail(new List<EmailMessage>(), 0, 0));
    }

    [Fact]
    public void BuildMeetingStart_ClampsHourAndMinute()
    {
        var date = new DateTime(2026, 3, 2);

        Assert.Equal(new DateTime(2026, 3, 2, 23, 59, 0), EmailGenerator.BuildMeetingStart(date, 25, 75));
        Assert.Equal(new DateTime(2026, 3, 2, 0, 0, 0), EmailGenerator.BuildMeetingStart(date, -3, -1));
        Assert.Equal(new DateTime(2026, 3, 2, 10, 30, 0), EmailGenerator.BuildMeetingStart(date, 10, 30));
    }
}
