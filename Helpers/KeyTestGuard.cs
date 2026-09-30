namespace ReelDiscovery.Helpers;

/// <summary>
/// Decides whether an asynchronous API-key test result still applies to what the user has on screen.
/// A test that completes after the key was cleared or edited must not be saved (CR-02).
/// </summary>
public static class KeyTestGuard
{
    public static bool IsResultCurrent(string testedKey, string currentKeyText) =>
        !string.IsNullOrEmpty(testedKey) &&
        string.Equals(testedKey, currentKeyText.Trim(), StringComparison.Ordinal);
}
