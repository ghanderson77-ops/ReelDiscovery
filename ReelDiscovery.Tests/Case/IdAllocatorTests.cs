using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class IdAllocatorTests
{
    [Fact]
    public void Next_Sequence()
    {
        var guide = new CaseGuide();

        Assert.Equal("CH-001", IdAllocator.Next(guide, IdAllocator.Character));
        Assert.Equal("CH-002", IdAllocator.Next(guide, IdAllocator.Character));
        Assert.Equal("ISS-01", IdAllocator.Next(guide, IdAllocator.Issue));
        Assert.Equal("ORG-01", IdAllocator.Next(guide, IdAllocator.Organization));
        Assert.Equal("F-001", IdAllocator.Next(guide, IdAllocator.Fact));
        Assert.Equal("V-001", IdAllocator.Next(guide, IdAllocator.Value));
        Assert.Equal(2, guide.IdCounters["CH"]);
    }

    [Fact]
    public void Next_NeverReuses()
    {
        var guide = new CaseGuide();
        for (var i = 0; i < 3; i++)
            guide.Cast.Add(new CastMember { Id = IdAllocator.Next(guide, IdAllocator.Character) });

        guide.Cast.RemoveAll(c => c.Id == "CH-003");

        Assert.Equal("CH-004", IdAllocator.Next(guide, IdAllocator.Character));
    }

    [Fact]
    public void Next_RespectsHigherExistingIds()
    {
        var guide = new CaseGuide();
        guide.Facts.Add(new KeyFact { Id = "F-040" });
        guide.IdCounters["F"] = 3;

        Assert.Equal("F-041", IdAllocator.Next(guide, IdAllocator.Fact));
        Assert.Equal(41, guide.IdCounters["F"]);
    }

    [Fact]
    public void Next_IgnoresOtherPrefixes()
    {
        var guide = new CaseGuide();
        guide.Values.Add(new CanonicalValue { Id = "V-050" });

        Assert.Equal("F-001", IdAllocator.Next(guide, IdAllocator.Fact));
    }

    [Fact]
    public void Next_WidthGrows()
    {
        var guide = new CaseGuide();
        guide.IdCounters["CH"] = 999;

        Assert.Equal("CH-1000", IdAllocator.Next(guide, IdAllocator.Character));
    }

    [Theory]
    [InlineData("ISS", 2)]
    [InlineData("ORG", 2)]
    [InlineData("CH", 3)]
    [InlineData("F", 3)]
    [InlineData("V", 3)]
    public void WidthFor_Prefix(string prefix, int width)
    {
        Assert.Equal(width, IdAllocator.WidthFor(prefix));
    }

    [Theory]
    [InlineData(" ch-7 ", "CH", "CH-007")]
    [InlineData("CH-0007", "CH", "CH-007")]
    [InlineData("F-12", "F", "F-012")]
    [InlineData("ISS-1", "ISS", "ISS-01")]
    [InlineData("Michael", "CH", null)]
    [InlineData("F-012", "CH", null)]
    [InlineData(null, "CH", null)]
    [InlineData("CH-", "CH", null)]
    [InlineData("CH-1a", "CH", null)]
    [InlineData("CH-1234567", "CH", null)]
    [InlineData("CH--1", "CH", null)]
    public void Normalize_Cases(string? raw, string prefix, string? expected)
    {
        Assert.Equal(expected, IdAllocator.Normalize(raw, prefix));
    }
}
