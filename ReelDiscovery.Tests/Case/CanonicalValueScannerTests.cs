using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CanonicalValueScannerTests
{
    private static List<string> Texts(List<ScannedValue> values, ScannedKind kind) =>
        values.Where(v => v.Kind == kind).Select(v => v.Text).ToList();

    [Fact]
    public void Amounts()
    {
        var found = CanonicalValueScanner.Scan("paid $184,250.00 then $12,000 and $7.5 million");

        Assert.Equal(new[] { "$184,250.00", "$12,000", "$7.5 million" }, Texts(found, ScannedKind.Amount));
        Assert.Empty(Texts(found, ScannedKind.Identifier));
    }

    [Fact]
    public void Amounts_NoFalsePositive()
    {
        Assert.Empty(CanonicalValueScanner.Scan("10,000 units were delivered"));
    }

    [Fact]
    public void Identifiers()
    {
        var found = CanonicalValueScanner.Scan("Change Order CO-007 and invoice INV-2024-118, PO-4471, Contract No. 2023-17, ticket #4471");

        Assert.Equal(new[] { "CO-007", "INV-2024-118", "PO-4471", "No. 2023-17", "#4471" }, Texts(found, ScannedKind.Identifier));
        Assert.Empty(Texts(found, ScannedKind.Amount));
    }

    [Fact]
    public void Identifiers_NoFalsePositive()
    {
        Assert.Empty(CanonicalValueScanner.Scan("Count No. 2 was dismissed and item #1 remains."));
    }

    [Fact]
    public void Distinct_InOrderOfAppearance()
    {
        var found = CanonicalValueScanner.Scan("CO-007 for $5,000, then $5,000 again under CO-007");

        Assert.Equal(new[] { "CO-007", "$5,000" }, found.Select(v => v.Text));
    }

    [Fact]
    public void Scan_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(CanonicalValueScanner.Scan(null));
        Assert.Empty(CanonicalValueScanner.Scan(string.Empty));
    }

    [Theory]
    [InlineData("$184,250", "$184,250.00", true)]
    [InlineData("$184,250", "$184,205", false)]
    [InlineData("$7.5 million", "$7,500,000", true)]
    [InlineData("$12 thousand", "$12,000.00", true)]
    [InlineData("$ 5,000", "$5,000", true)]
    public void AmountsEqual(string a, string b, bool expected)
    {
        Assert.Equal(expected, CanonicalValueScanner.AmountsEqual(a, b));
    }

    [Fact]
    public void Amounts_AbbreviatedSuffix()
    {
        var found = CanonicalValueScanner.Scan("the total came to $1.25M");

        Assert.Equal(new[] { "$1.25M" }, Texts(found, ScannedKind.Amount));
    }

    [Theory]
    [InlineData("we owe $500K now", "$500K")]
    [InlineData("budget of $2.5MM total", "$2.5MM")]
    [InlineData("a $3B fund", "$3B")]
    [InlineData("about $1.25 M overall", "$1.25 M")]
    [InlineData("$12 Main Street", "$12")]
    [InlineData("$5 Mbps plan", "$5")]
    [InlineData("$40 kits", "$40")]
    public void Amounts_SuffixOnlyAtWordBoundary(string text, string expected)
    {
        Assert.Equal(new[] { expected }, Texts(CanonicalValueScanner.Scan(text), ScannedKind.Amount));
    }

    [Theory]
    [InlineData("$1.25M", "$1,250,000", true)]
    [InlineData("$500K", "$500,000", true)]
    [InlineData("$2.5MM", "$2,500,000", true)]
    [InlineData("$3B", "$3,000,000,000", true)]
    [InlineData("$1.25 M", "$1,250,000", true)]
    [InlineData("$1.25m", "$1,250,000.00", true)]
    [InlineData("$1.25M", "$1,250,001", false)]
    public void AmountsEqual_AbbreviatedSuffix(string a, string b, bool expected)
    {
        Assert.Equal(expected, CanonicalValueScanner.AmountsEqual(a, b));
    }

    [Fact]
    public void IsRegistered_Amount_UsesAmountKindValues()
    {
        var values = new List<CanonicalValue>
        {
            new() { Id = "V-001", Kind = ValueKind.Amount, Value = "$184,250.00" },
            new() { Id = "V-002", Kind = ValueKind.Other, Value = "$12,000" }
        };

        Assert.True(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Amount, Text = "$184,250" }, values));
        Assert.False(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Amount, Text = "$12,000" }, values));
        Assert.False(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Amount, Text = "$99" }, values));
    }

    [Fact]
    public void IsRegistered_Identifier_ContainsEitherWay()
    {
        var values = new List<CanonicalValue>
        {
            new() { Id = "V-001", Kind = ValueKind.ChangeOrderNumber, Value = "CO-007" },
            new() { Id = "V-002", Kind = ValueKind.ContractNumber, Value = "Subcontract No. 2023-17" },
            new() { Id = "V-003", Kind = ValueKind.Other, Value = "" }
        };

        Assert.True(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Identifier, Text = "co-007" }, values));
        Assert.True(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Identifier, Text = "No. 2023-17" }, values));
        Assert.False(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Identifier, Text = "PO-4471" }, values));
    }

    [Fact]
    public void IsRegistered_NullValues_False()
    {
        Assert.False(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Amount, Text = "$1" }, null!));
        Assert.False(CanonicalValueScanner.IsRegistered(new ScannedValue { Kind = ScannedKind.Amount, Text = "$1" }, new CanonicalValue[] { null! }));
    }

    [Fact]
    public void Timeout_IsCaught()
    {
        var huge = string.Concat(Enumerable.Repeat("$1,000,000,000 PO-1 No. 12-34-56 ", 70_000));
        Assert.True(huge.Length >= 2_000_000);

        var found = CanonicalValueScanner.Scan(huge);

        Assert.NotNull(found);
    }
}
