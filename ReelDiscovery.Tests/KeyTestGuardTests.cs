using ReelDiscovery.Helpers;

namespace ReelDiscovery.Tests;

/// <summary>
/// CR-02 regression: a key test result only applies while the textbox still holds the tested key.
/// </summary>
public class KeyTestGuardTests
{
    [Fact]
    public void SameKey_IsCurrent()
    {
        Assert.True(KeyTestGuard.IsResultCurrent("sk-abc", "sk-abc"));
    }

    [Fact]
    public void SameKeyWithWhitespace_IsCurrent()
    {
        Assert.True(KeyTestGuard.IsResultCurrent("sk-abc", "  sk-abc  "));
    }

    [Fact]
    public void Cleared_IsNotCurrent()
    {
        Assert.False(KeyTestGuard.IsResultCurrent("sk-abc", ""));
    }

    [Fact]
    public void Edited_IsNotCurrent()
    {
        Assert.False(KeyTestGuard.IsResultCurrent("sk-abc", "sk-abd"));
    }

    [Fact]
    public void CaseDiffers_IsNotCurrent()
    {
        Assert.False(KeyTestGuard.IsResultCurrent("sk-abc", "SK-ABC"));
    }

    [Fact]
    public void EmptyTestedKey_IsNotCurrent()
    {
        Assert.False(KeyTestGuard.IsResultCurrent("", ""));
    }
}
