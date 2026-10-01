using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the static text helpers of ValuesTabPanel; no control is instantiated.
public class ValuesPanelLogicTests
{
    [Theory]
    [InlineData(ValueKind.Amount, "Amount")]
    [InlineData(ValueKind.InvoiceNumber, "Invoice number")]
    [InlineData(ValueKind.ChangeOrderNumber, "Change order number")]
    [InlineData(ValueKind.ContractNumber, "Contract number")]
    [InlineData(ValueKind.PurchaseOrder, "Purchase order")]
    [InlineData(ValueKind.DocumentTitle, "Document title")]
    [InlineData(ValueKind.ProjectName, "Project name")]
    [InlineData(ValueKind.AccountNumber, "Account number")]
    [InlineData(ValueKind.Address, "Address")]
    [InlineData(ValueKind.Other, "Other")]
    public void KindText_RoundTrips(ValueKind kind, string text)
    {
        Assert.Equal(text, ValuesTabPanel.KindText(kind));
        Assert.Equal(kind, ValuesTabPanel.KindFromText(text));
    }

    [Fact]
    public void KindText_CoversEveryKind()
    {
        foreach (var kind in Enum.GetValues<ValueKind>())
            Assert.Equal(kind, ValuesTabPanel.KindFromText(ValuesTabPanel.KindText(kind)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Invoice")]
    [InlineData("3")]
    public void KindFromText_Unknown_IsOther(string text)
    {
        Assert.Equal(ValueKind.Other, ValuesTabPanel.KindFromText(text));
    }

    [Fact]
    public void MetaLine_HasIdAndKind()
    {
        var v = new CanonicalValue { Id = "V-004", Kind = ValueKind.InvoiceNumber };

        Assert.Equal("V-004 · Invoice number", ValuesTabPanel.MetaLine(v));
    }

    [Fact]
    public void MetaLine_Edited_AddsEditedByYou()
    {
        var v = new CanonicalValue { Id = "V-002", Kind = ValueKind.ChangeOrderNumber, IsUserEdited = true };

        Assert.Equal("V-002 · Change order number · Edited by you", ValuesTabPanel.MetaLine(v));
    }

    [Fact]
    public void NoticeText_PrefixesTheReviewReason()
    {
        var v = new CanonicalValue { NeedsReview = true, ReviewReason = "F-003 was changed after it was written." };

        Assert.Equal("Review this value: F-003 was changed after it was written.", ValuesTabPanel.NoticeText(v));
    }

    [Theory]
    [InlineData(22, "22 values")]
    [InlineData(1, "1 value")]
    [InlineData(0, "0 values")]
    public void CountText_UsesPlural(int n, string expected)
    {
        Assert.Equal(expected, ValuesTabPanel.CountText(n));
    }
}
