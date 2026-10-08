using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class CaseNumberTests
{
    [Fact]
    public void For_Federal_UsesDivisionYearCvAndFiveDigits()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var number = CaseNumber.For(draft);

        Assert.Matches(@"^[1-4]:\d{2}-cv-\d{5}$", number);
        Assert.Contains(":25-cv-", number);
    }

    [Fact]
    public void For_SameCaseId_IsStable()
    {
        var a = DiscoveryTestData.LitigationDraft();
        var b = DiscoveryTestData.LitigationDraft();
        b.CaseId = a.CaseId;

        Assert.Equal(CaseNumber.For(a), CaseNumber.For(b));
        Assert.Equal(CaseNumber.For(a), CaseNumber.For(a));
    }

    [Fact]
    public void For_DifferentCaseIds_AreAlmostAlwaysDifferent()
    {
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i <= 20; i++)
        {
            var draft = DiscoveryTestData.LitigationDraft();
            draft.CaseId = new Guid($"6f1c2a3b-0000-4000-8000-{i:000000000000}");
            numbers.Add(CaseNumber.For(draft));
        }

        Assert.True(numbers.Count >= 18, $"Only {numbers.Count} distinct numbers");
    }

    [Fact]
    public void For_StateCourt_UsesYearCvAndSixDigits()
    {
        var draft = DiscoveryTestData.StateCourtDraft();

        Assert.Matches(@"^2025-CV-\d{6}$", CaseNumber.For(draft));
    }

    [Fact]
    public void For_Memos_UseYearAndFourDigits()
    {
        foreach (var draft in new[] { DiscoveryTestData.MemoCriminalDraft(), DiscoveryTestData.MemoRegulatoryDraft(), DiscoveryTestData.MemoCivilDraft() })
            Assert.Matches(@"^\d{4}-\d{4}$", CaseNumber.For(draft));
    }

    [Fact]
    public void For_NoFilingDate_FallsBackToTimelineEnd()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.CoreDocument!.FilingDate = string.Empty;

        Assert.Contains(":24-cv-", CaseNumber.For(draft));
    }

    [Fact]
    public void Label_FollowsDocumentTypeAndMatterKind()
    {
        Assert.Equal("Case No.", CaseNumber.Label(DiscoveryTestData.LitigationDraft().CoreDocument!));
        Assert.Equal("Cause No.", CaseNumber.Label(DiscoveryTestData.StateCourtDraft().CoreDocument!));
        Assert.Equal("Grand Jury Matter No.", CaseNumber.Label(DiscoveryTestData.MemoCriminalDraft().CoreDocument!));
        Assert.Equal("CID No.", CaseNumber.Label(DiscoveryTestData.MemoRegulatoryDraft().CoreDocument!));
        Assert.Equal("Matter No.", CaseNumber.Label(DiscoveryTestData.MemoCivilDraft().CoreDocument!));

        var criminal = DiscoveryTestData.MemoCriminalDraft();
        Assert.Matches(@"^Grand Jury Matter No\. \d{4}-\d{4}$", $"{CaseNumber.Label(criminal.CoreDocument!)} {CaseNumber.For(criminal)}");
    }

    [Fact]
    public void For_NullOrNoCoreDocument_NeverThrows()
    {
        Assert.Equal(string.Empty, CaseNumber.For(null!));
        Assert.Equal("Case No.", CaseNumber.Label(null!));

        var draft = DiscoveryTestData.LitigationDraft();
        draft.CoreDocument = null;
        Assert.Matches(@"^[1-4]:\d{2}-cv-\d{5}$", CaseNumber.For(draft));
        Assert.True(Regex.IsMatch(CaseNumber.For(draft), $@":{draft.CreatedUtc.Year % 100:00}-cv-"));
    }
}
