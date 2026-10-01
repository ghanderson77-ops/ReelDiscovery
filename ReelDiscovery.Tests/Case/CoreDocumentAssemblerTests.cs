using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CoreDocumentAssemblerTests
{
    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(14, "XIV")]
    [InlineData(40, "XL")]
    [InlineData(1994, "MCMXCIV")]
    public void ToRoman(int number, string expected)
    {
        Assert.Equal(expected, CoreDocumentAssembler.ToRoman(number));
    }
}
