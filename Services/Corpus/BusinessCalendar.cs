using System.Collections.Concurrent;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Business time for planned emails (PLAN-04, D-16): weekdays 09:00-17:00 in the sender organization's IANA time zone,
/// skipping observed US federal holidays.
/// </summary>
public static class BusinessCalendar
{
    public static readonly TimeOnly DayStart = new(9, 0);
    public static readonly TimeOnly DayEnd = new(17, 0);
    public const int BusinessMinutesPerDay = 480;

    // Snapped instants land 0-45 minutes after 09:00 so not every message is sent at exactly 9:00
    private const int MaxJitterMinutes = 45;

    // Planner tasks may call in parallel
    private static readonly ConcurrentDictionary<int, HashSet<DateOnly>> _holidayCache = new();
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> _zoneCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Observed US federal holidays falling in <paramref name="year"/> (5 U.S.C. 6103, OPM observance:
    /// Saturday -> Friday, Sunday -> Monday; New Year's Day of year+1 may be observed on Dec 31).
    /// The returned set is shared; do not modify it.
    /// </summary>
    public static HashSet<DateOnly> ObservedFederalHolidays(int year) => _holidayCache.GetOrAdd(year, BuildHolidays);

    public static bool IsHoliday(DateOnly day) => ObservedFederalHolidays(day.Year).Contains(day);

    public static bool IsBusinessDay(DateOnly day) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(day);

    /// <summary>
    /// The first business day on or after <paramref name="day"/>.
    /// </summary>
    public static DateOnly NextBusinessDay(DateOnly day)
    {
        while (!IsBusinessDay(day))
            day = day.AddDays(1);
        return day;
    }

    public static DateTimeOffset At(TimeZoneInfo tz, DateOnly day, TimeOnly time)
    {
        // 09:00-17:00 never hits a DST gap (transitions happen at 02:00 local)
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    /// <summary>
    /// Local calendar date in <paramref name="tz"/>. Instants at the edge of the DateTime range (only in a tampered
    /// corpus-plan.json) use their own offset's date, because ConvertTime throws there (WR-05).
    /// </summary>
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo tz)
    {
        var year = instant.UtcDateTime.Year;
        if (year < 2 || year > 9998)
            return DateOnly.FromDateTime(instant.DateTime);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, tz).DateTime);
    }

    public static bool IsBusinessTime(DateTimeOffset instant, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(instant, tz).DateTime;
        var time = TimeOnly.FromDateTime(local);
        return IsBusinessDay(DateOnly.FromDateTime(local)) && time >= DayStart && time < DayEnd;
    }

    /// <summary>
    /// <paramref name="instant"/> itself when it is business time; otherwise 09:00 (+ jitter, clamped 0-45 minutes)
    /// on the same day when it is an early-morning business day, else on the next business day.
    /// </summary>
    public static DateTimeOffset NextBusinessInstant(DateTimeOffset instant, TimeZoneInfo tz, int jitterMinutes = 0)
    {
        if (IsBusinessTime(instant, tz))
            return instant;

        var jitter = Math.Clamp(jitterMinutes, 0, MaxJitterMinutes);
        var local = TimeZoneInfo.ConvertTime(instant, tz).DateTime;
        var day = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);

        var target = IsBusinessDay(day) && time < DayStart ? day : NextBusinessDay(day.AddDays(1));
        return At(tz, target, DayStart.AddMinutes(jitter));
    }

    /// <summary>
    /// Adds business minutes (480 per business day): snaps to the next business instant, then consumes minutes day by day.
    /// The result is always business time; landing exactly on 17:00 rolls to 09:00 of the next business day.
    /// </summary>
    public static DateTimeOffset AddBusinessMinutes(DateTimeOffset start, int minutes, TimeZoneInfo tz)
    {
        var current = NextBusinessInstant(start, tz);
        var remaining = TimeSpan.FromMinutes(Math.Max(0, minutes));

        while (true)
        {
            var local = TimeZoneInfo.ConvertTime(current, tz).DateTime;
            var day = DateOnly.FromDateTime(local);
            var leftToday = day.ToDateTime(DayEnd) - local;

            if (remaining < leftToday)
            {
                var result = local + remaining;
                return new DateTimeOffset(result, tz.GetUtcOffset(result));
            }

            remaining -= leftToday;
            current = At(tz, NextBusinessDay(day.AddDays(1)), DayStart);
            if (remaining == TimeSpan.Zero)
                return current;
        }
    }

    /// <summary>
    /// Organization time zone: TimeZoneId -> zone of Location -> America/New_York -> UTC. Never throws.
    /// </summary>
    public static TimeZoneInfo Resolve(CaseOrganization? org)
    {
        foreach (var id in new[] { org?.TimeZoneId, UsTimeZones.ForLocation(org?.Location), UsTimeZones.Default })
        {
            var zone = ResolveId(id);
            if (zone != null)
                return zone;
        }

        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// The zone for an IANA (or Windows) id, or null when it is blank or unknown. Never throws.
    /// </summary>
    public static TimeZoneInfo? ResolveId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var key = id.Trim();
        if (_zoneCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(key);
            _zoneCache.TryAdd(key, zone);
            return zone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            // Untrusted case.json value; fall through to the next candidate (failures are not cached)
            return null;
        }
    }

    private static HashSet<DateOnly> BuildHolidays(int year)
    {
        var set = new HashSet<DateOnly>();

        // year+1 New Year's Day can be observed on Dec 31 of this year
        foreach (var y in new[] { year, year + 1 })
        {
            void Fixed(int month, int day)
            {
                if (y > DateOnly.MaxValue.Year)
                    return;
                var observed = Observed(new DateOnly(y, month, day));
                if (observed.Year == year)
                    set.Add(observed);
            }

            Fixed(1, 1);
            if (y == year)
            {
                if (y >= 1986)
                    set.Add(NthWeekday(y, 1, DayOfWeek.Monday, 3));   // Martin Luther King, Jr. Day
                set.Add(NthWeekday(y, 2, DayOfWeek.Monday, 3));       // Washington's Birthday
                set.Add(LastWeekday(y, 5, DayOfWeek.Monday));         // Memorial Day
                set.Add(NthWeekday(y, 9, DayOfWeek.Monday, 1));       // Labor Day
                set.Add(NthWeekday(y, 10, DayOfWeek.Monday, 2));      // Columbus Day
                set.Add(NthWeekday(y, 11, DayOfWeek.Thursday, 4));    // Thanksgiving
            }

            if (y >= 2021)
                Fixed(6, 19);                                          // Juneteenth (from 2021 only)
            Fixed(7, 4);                                               // Independence Day
            Fixed(11, 11);                                             // Veterans Day
            Fixed(12, 25);                                             // Christmas Day
        }

        return set;
    }

    private static DateOnly Observed(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => day.AddDays(-1),
        DayOfWeek.Sunday => day.AddDays(1),
        _ => day
    };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek dow, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)dow - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (n - 1) * 7);
    }

    private static DateOnly LastWeekday(int year, int month, DayOfWeek dow)
    {
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)dow + 7) % 7;
        return last.AddDays(-offset);
    }
}
