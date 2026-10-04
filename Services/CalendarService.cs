using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ReelDiscovery.Services;

/// <summary>
/// Inputs for a deterministic calendar invite; UID and DTSTAMP are supplied by the caller (D-20).
/// </summary>
public class CalendarInviteSpec
{
    public string Uid { get; set; } = string.Empty;
    public DateTimeOffset DtStamp { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public string OrganizerEmail { get; set; } = string.Empty;
    public List<(string Name, string Email)> Attendees { get; set; } = new();
}

public partial class CalendarService
{
    private const int MaxLineOctets = 75;

    /// <summary>
    /// Deterministic RFC 5545 invite: UID and DTSTAMP come from the caller (D-20, Pitfall 9).
    /// Untrusted text is stripped of control characters, TEXT-escaped, CN values are DQUOTE-quoted,
    /// mailto values are reduced to a safe charset and lines are folded at 75 octets (T-06-07).
    /// </summary>
    public byte[] CreateInvite(CalendarInviteSpec spec)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "PRODID:-//Microsoft Corporation//Outlook 16.0 MIMEDIR//EN",
            "VERSION:2.0",
            "METHOD:REQUEST",
            "BEGIN:VEVENT"
        };

        var organizerEmail = SafeMailto(spec.OrganizerEmail);
        if (organizerEmail != null)
            lines.Add($"ORGANIZER;CN={QuoteCn(spec.OrganizerName)}:mailto:{organizerEmail}");

        foreach (var (name, email) in spec.Attendees ?? new List<(string Name, string Email)>())
        {
            // An address with no '@' left after filtering is dropped rather than emitted broken
            var mailto = SafeMailto(email);
            if (mailto != null)
                lines.Add($"ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN={QuoteCn(name)}:mailto:{mailto}");
        }

        var summary = SingleLine(spec.Summary);
        lines.Add($"UID:{SafeUid(spec, summary)}");
        lines.Add($"DTSTAMP:{FormatUtc(spec.DtStamp)}");
        lines.Add($"DTSTART:{FormatUtc(spec.Start)}");
        lines.Add($"DTEND:{FormatUtc(spec.End)}");
        lines.Add($"SUMMARY:{EscapeIcsText(summary)}");
        lines.Add($"DESCRIPTION:{EscapeIcsText(StripControls(spec.Description).Trim())}");
        lines.Add($"LOCATION:{EscapeIcsText(SingleLine(spec.Location))}");
        lines.Add("STATUS:CONFIRMED");
        lines.Add("SEQUENCE:0");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var sb = new StringBuilder();
        foreach (var line in lines)
            sb.Append(Fold(line));

        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // Keeps line breaks (TEXT-escaped later as \n); every other control character is removed
    private static string StripControls(string? text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        return ControlExceptNewline().Replace(normalized, string.Empty);
    }

    private static string SingleLine(string? text) =>
        Whitespace().Replace(StripControls(text).Replace('\n', ' '), " ").Trim();

    // Quoted-string param values may not contain DQUOTE or controls (RFC 5545 3.1)
    private static string QuoteCn(string? name) =>
        $"\"{SingleLine(name).Replace('"', '\'')}\"";

    private static string? SafeMailto(string? email)
    {
        var filtered = MailtoUnsafe().Replace(email ?? string.Empty, string.Empty);
        return filtered.Contains('@') ? filtered : null;
    }

    private static string SafeUid(CalendarInviteSpec spec, string summary)
    {
        var uid = SingleLine(spec.Uid).Replace(" ", string.Empty);
        if (uid.Length > 0)
            return uid;

        // Deterministic fallback from the event itself, never a random value
        var seed = $"{summary}|{FormatUtc(spec.Start)}|{SafeMailto(spec.OrganizerEmail)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..32].ToLowerInvariant();
    }

    // Folds at 75 octets on whole code points; continuation lines start with one space
    private static string Fold(string line)
    {
        var sb = new StringBuilder();
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                sb.Append("\r\n ");
                octets = 1;
            }
            sb.Append(rune.ToString());
            octets += size;
        }
        sb.Append("\r\n");
        return sb.ToString();
    }

    [GeneratedRegex(@"[\p{Cc}-[\n]]")]
    private static partial Regex ControlExceptNewline();

    [GeneratedRegex(@"[^A-Za-z0-9._%+\-@]")]
    private static partial Regex MailtoUnsafe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Creates an ICS calendar invite file
    /// </summary>
    public byte[] CreateCalendarInvite(
        string title,
        string description,
        DateTime startTime,
        DateTime endTime,
        string location,
        string organizerName,
        string organizerEmail,
        List<(string name, string email)> attendees)
    {
        var uid = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//ReelDiscovery//Email Generator//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:REQUEST");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{FormatDateTime(now)}");
        sb.AppendLine($"DTSTART:{FormatDateTime(startTime)}");
        sb.AppendLine($"DTEND:{FormatDateTime(endTime)}");
        sb.AppendLine($"SUMMARY:{EscapeIcsText(title)}");
        sb.AppendLine($"DESCRIPTION:{EscapeIcsText(description)}");
        sb.AppendLine($"LOCATION:{EscapeIcsText(location)}");
        sb.AppendLine($"ORGANIZER;CN={EscapeIcsText(organizerName)}:mailto:{organizerEmail}");

        foreach (var (name, email) in attendees)
        {
            sb.AppendLine($"ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;CN={EscapeIcsText(name)}:mailto:{email}");
        }

        sb.AppendLine("STATUS:CONFIRMED");
        sb.AppendLine("SEQUENCE:0");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string FormatDateTime(DateTime dt)
    {
        return dt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
    }

    private static string EscapeIcsText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // ICS escaping rules
        return text
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
