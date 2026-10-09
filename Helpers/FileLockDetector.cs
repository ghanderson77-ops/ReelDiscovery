namespace ReelDiscovery.Helpers;

/// <summary>
/// Tells an open-in-another-program failure (Word/Excel holding a sharing lock) apart from other write failures.
/// </summary>
internal static class FileLockDetector
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// True when another program (Word) holds the file open with a sharing lock; false for a missing file.
    /// </summary>
    public static bool IsOpenElsewhere(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or no permission is not an open file
            return false;
        }
    }

    // Word/Excel hold an open file with a sharing lock: the replace fails with a sharing/lock violation or access denied
    public static bool IsFileOpenElsewhere(Exception ex, string path)
    {
        if (ex is IOException io && (io.HResult == SharingViolation || io.HResult == LockViolation))
            return true;
        if (ex is not UnauthorizedAccessException || string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or no permission: a real access problem, not an open file
            return false;
        }
    }
}
