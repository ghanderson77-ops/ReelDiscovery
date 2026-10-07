namespace ReelDiscovery.Helpers;

public static class ThreadingHelper
{
    public static string GenerateMessageId(string domain)
    {
        var uniquePart = $"{Guid.NewGuid():N}.{DateTime.UtcNow.Ticks}";
        return $"<{uniquePart}@{domain}>";
    }

    public static string AddReplyPrefix(string subject)
    {
        if (subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }
        return $"RE: {subject}";
    }

    public static string AddForwardPrefix(string subject)
    {
        if (subject.StartsWith("FW:", StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }
        return $"FW: {subject}";
    }

    public static string GetCleanSubject(string subject)
    {
        // Remove RE:, FW:, Fwd: prefixes for comparison
        var cleaned = subject;
        while (true)
        {
            var trimmed = cleaned.TrimStart();
            if (trimmed.StartsWith("RE:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[3..];
            else if (trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[3..];
            else if (trimmed.StartsWith("FW:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[3..];
            else if (trimmed.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[4..];
            else
                break;
        }
        return cleaned.Trim();
    }

    /// <summary>
    /// Quotes text with > prefix for each line
    /// </summary>
    public static string QuoteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "> ";
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return string.Join("\n", lines.Select(line => $"> {line}"));
    }
}
