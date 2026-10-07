using System.Text.RegularExpressions;

namespace ReelDiscovery.Helpers;

public static partial class FileNameHelper
{
    public static string SanitizeForFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed";

        // Remove invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Where(c => !invalid.Contains(c)).ToArray());

        // Replace spaces with underscores
        sanitized = sanitized.Replace(" ", "_");

        // Remove multiple consecutive underscores
        sanitized = MultipleUnderscoresRegex().Replace(sanitized, "_");

        // Trim underscores from start/end
        sanitized = sanitized.Trim('_');

        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized;
    }

    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();
}
