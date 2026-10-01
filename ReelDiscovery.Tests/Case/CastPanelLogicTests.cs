using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static text and filter helpers of CastTabPanel; no control is instantiated.
public class CastPanelLogicTests
{
    [Theory]
    [InlineData(CastRole.Party, "Party")]
    [InlineData(CastRole.InHouseCounsel, "In-house counsel")]
    [InlineData(CastRole.OutsideCounsel, "Outside counsel")]
    [InlineData(CastRole.Staff, "Staff")]
    public void RoleText_CoversEveryRole(CastRole role, string expected)
    {
        Assert.Equal(expected, CastTabPanel.RoleText(role));
    }

    [Fact]
    public void MetaLine_Unedited_HasIdTierAndRole()
    {
        var m = new CastMember { Id = "CH-007", Tier = CastTier.Key, Role = CastRole.OutsideCounsel };

        Assert.Equal("CH-007 · Key · Outside counsel", CastTabPanel.MetaLine(m));
    }

    [Fact]
    public void MetaLine_Edited_AddsEditedByYou()
    {
        var m = new CastMember { Id = "CH-009", Tier = CastTier.Background, Role = CastRole.Staff, IsUserEdited = true };

        Assert.Equal("CH-009 · Background · Staff · Edited by you", CastTabPanel.MetaLine(m));
    }

    [Theory]
    [InlineData(0, "Not in any fact yet.")]
    [InlineData(1, "Appears in 1 fact.")]
    [InlineData(3, "Appears in 3 facts.")]
    public void LinkedFactsText_Pluralizes(int n, string expected)
    {
        Assert.Equal(expected, CastTabPanel.LinkedFactsText(n));
    }

    [Theory]
    [InlineData("Maria Elena Ortiz", "Maria Elena", "Ortiz")]
    [InlineData("Cher", "Cher", "")]
    [InlineData("  Tom   Becker  ", "Tom", "Becker")]
    [InlineData("", "", "")]
    public void SplitName_SplitsAtTheLastSpace(string full, string first, string last)
    {
        var (f, l) = CastTabPanel.SplitName(full);

        Assert.Equal(first, f);
        Assert.Equal(last, l);
    }

    [Theory]
    [InlineData(CastTier.Key, CastFilter.All, true)]
    [InlineData(CastTier.Background, CastFilter.All, true)]
    [InlineData(CastTier.Key, CastFilter.Key, true)]
    [InlineData(CastTier.Background, CastFilter.Key, false)]
    [InlineData(CastTier.Key, CastFilter.Background, false)]
    [InlineData(CastTier.Background, CastFilter.Background, true)]
    public void PassesFilter_MatchesTier(CastTier tier, CastFilter filter, bool expected)
    {
        Assert.Equal(expected, CastTabPanel.PassesFilter(new CastMember { Tier = tier }, filter));
    }
}
