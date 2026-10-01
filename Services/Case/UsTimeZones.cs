namespace ReelDiscovery.Services.Case;

/// <summary>
/// US state (full name or 2-letter code) to IANA time zone.
/// </summary>
public static class UsTimeZones
{
    // Phase 4 schedules emails in each organization's local business hours (PLAN-04)
    public const string Default = Eastern;

    private const string Eastern = "America/New_York";
    private const string Central = "America/Chicago";
    private const string Mountain = "America/Denver";
    private const string Arizona = "America/Phoenix";
    private const string Pacific = "America/Los_Angeles";
    private const string Alaska = "America/Anchorage";
    private const string Hawaii = "Pacific/Honolulu";

    // Split states use the zone of their most populous part
    private static readonly (string Name, string Code, string Zone)[] States =
    {
        ("Alabama", "AL", Central),
        ("Alaska", "AK", Alaska),
        ("Arizona", "AZ", Arizona),
        ("Arkansas", "AR", Central),
        ("California", "CA", Pacific),
        ("Colorado", "CO", Mountain),
        ("Connecticut", "CT", Eastern),
        ("Delaware", "DE", Eastern),
        ("District of Columbia", "DC", Eastern),
        ("Florida", "FL", Eastern),
        ("Georgia", "GA", Eastern),
        ("Hawaii", "HI", Hawaii),
        ("Idaho", "ID", Mountain),
        ("Illinois", "IL", Central),
        ("Indiana", "IN", Eastern),
        ("Iowa", "IA", Central),
        ("Kansas", "KS", Central),
        ("Kentucky", "KY", Eastern),
        ("Louisiana", "LA", Central),
        ("Maine", "ME", Eastern),
        ("Maryland", "MD", Eastern),
        ("Massachusetts", "MA", Eastern),
        ("Michigan", "MI", Eastern),
        ("Minnesota", "MN", Central),
        ("Mississippi", "MS", Central),
        ("Missouri", "MO", Central),
        ("Montana", "MT", Mountain),
        ("Nebraska", "NE", Central),
        ("Nevada", "NV", Pacific),
        ("New Hampshire", "NH", Eastern),
        ("New Jersey", "NJ", Eastern),
        ("New Mexico", "NM", Mountain),
        ("New York", "NY", Eastern),
        ("North Carolina", "NC", Eastern),
        ("North Dakota", "ND", Central),
        ("Ohio", "OH", Eastern),
        ("Oklahoma", "OK", Central),
        ("Oregon", "OR", Pacific),
        ("Pennsylvania", "PA", Eastern),
        ("Rhode Island", "RI", Eastern),
        ("South Carolina", "SC", Eastern),
        ("South Dakota", "SD", Central),
        ("Tennessee", "TN", Central),
        ("Texas", "TX", Central),
        ("Utah", "UT", Mountain),
        ("Vermont", "VT", Eastern),
        ("Virginia", "VA", Eastern),
        ("Washington", "WA", Pacific),
        ("West Virginia", "WV", Eastern),
        ("Wisconsin", "WI", Central),
        ("Wyoming", "WY", Mountain)
    };

    private static readonly Dictionary<string, string> Lookup = BuildLookup();

    internal static int StateCount => States.Length;

    /// <summary>
    /// Zone for a "City, State" location, matching the last comma-separated part that names a state
    /// (a trailing ZIP code is ignored). Unknown or blank -> America/New_York.
    /// </summary>
    public static string ForLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return Default;

        var parts = location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (Lookup.TryGetValue(Key(parts[i]), out var zone))
                return zone;
        }

        return Default;
    }

    // "WA 98101" -> "WA"; "D.C." -> "DC"
    private static string Key(string part)
    {
        var words = part.Replace(".", string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.All(c => char.IsAsciiDigit(c) || c == '-'));
        return string.Join(" ", words);
    }

    private static Dictionary<string, string> BuildLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, code, zone) in States)
        {
            lookup[name] = zone;
            lookup[code] = zone;
        }

        return lookup;
    }
}
